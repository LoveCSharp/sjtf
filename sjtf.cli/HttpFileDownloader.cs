using System.Linq;

namespace Sjtf.Cli;

/// <summary>
/// 纯 HTTP 文件下载助手（单线程 + Range 分块并发）。
/// Pure HTTP file download helpers (single-stream + Range chunked concurrent).
/// 与 aria2 完全无关：只关心"把 URL 下载到本地文件"。
/// Completely independent of aria2: only concerned with "downloading a URL to a local file".
/// </summary>
internal static class HttpFileDownloader
{
    /// <summary>
    /// 异步下载文件到指定路径 / Download a file asynchronously to the specified path.
    /// </summary>
    /// <param name="url">要下载的 URL / URL to download.</param>
    /// <param name="destFile">目标文件路径 / Destination file path.</param>
    /// <param name="label">进度条标签 / Progress bar label.</param>
    /// <param name="preknownTotal">已知的总字节数（由调用方通过 HEAD 拿到）；传入后可避免再次 HEAD 并立即画 0% 行 / Pre-known total size from a prior HEAD; skips the internal HEAD and draws the 0% line immediately.</param>
    public static async Task DownloadAsync(string url, string destFile, string? label = null, long? preknownTotal = null)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd(Config.LoadUserAgent());

        var showProgress = !string.IsNullOrEmpty(label);
        long? total = preknownTotal;

        if (showProgress && !total.HasValue)
        {
            try
            {
                using var headResp = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, url));
                if (headResp.IsSuccessStatusCode)
                {
                    var len = headResp.Content.Headers.ContentLength;
                    if (len.HasValue && len.Value > 0)
                    {
                        total = len.Value;
                    }
                }
            }
            catch
            {
            }
        }

        if (showProgress && total.HasValue)
        {
            ConsoleProgress._lastProgressLength = 0;
            ConsoleProgress.DrawProgress(label!, 0, total.Value, 0, 20);
        }

        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();

        var effectiveTotal = total ?? resp.Content.Headers.ContentLength;

        await using var src = await resp.Content.ReadAsStreamAsync();
        await using var dst = File.Create(destFile);

        if (showProgress && effectiveTotal.HasValue)
        {
            await StreamCopy.CopyWithProgressAsync(src, dst, label!, effectiveTotal.Value, skipInitialDraw: total.HasValue);
        }
        else if (showProgress)
        {
            await StreamCopy.CopyWithProgressUnknownAsync(src, dst, label!, skipInitialDraw: total.HasValue);
        }
        else
        {
            await src.CopyToAsync(dst);
        }
    }

    /// <summary>
    /// 使用内置多线程分块下载文件（HEAD 探测 + Range 并发，不使用 aria2）/ Download file using built-in multi-threaded chunk downloader (HEAD probe + Range concurrent, no aria2).
    /// </summary>
    /// <param name="url">要下载的 URL / URL to download.</param>
    /// <param name="destFile">目标文件路径 / Destination file path.</param>
    /// <param name="label">进度条标签 / Progress bar label.</param>
    /// <param name="maxConnections">最大连接数（线程数）/ Maximum connections (threads).</param>
    /// <param name="splitCount">目标分块数 / Target number of chunks.</param>
    /// <param name="minSplitSizeMB">最小分块大小（MB）/ Minimum chunk size (MB).</param>
    public static async Task DownloadChunkedAsync(string url, string destFile, string? label,
        int maxConnections, int splitCount, int minSplitSizeMB)
    {
        maxConnections = Math.Clamp(maxConnections, 1, 16);
        splitCount = Math.Clamp(splitCount, 1, 16);
        minSplitSizeMB = Math.Clamp(minSplitSizeMB, 1, 1024);

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd(Config.LoadUserAgent());

        long? fileSize = null;

        HttpResponseMessage headResp;
        try
        {
            headResp = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, url));
        }
        catch (HttpRequestException)
        {
            await DownloadAsync(url, destFile, label, preknownTotal: fileSize);
            return;
        }

        if (!headResp.IsSuccessStatusCode)
        {
            headResp.Dispose();
            await DownloadAsync(url, destFile, label, preknownTotal: fileSize);
            return;
        }

        using (headResp)
        {
            var totalSize = headResp.Content.Headers.ContentLength;
            if (!totalSize.HasValue || totalSize.Value == 0)
            {
                await DownloadAsync(url, destFile, label, preknownTotal: fileSize);
                return;
            }

            fileSize = totalSize.Value;

            var acceptRanges = headResp.Headers.AcceptRanges.Any(h => h.Equals("bytes", StringComparison.OrdinalIgnoreCase));
            if (!acceptRanges)
            {
                await DownloadAsync(url, destFile, label, preknownTotal: fileSize);
                return;
            }

            var minChunkBytes = (long)minSplitSizeMB * 1024 * 1024;

            int actualConnections = Math.Min(splitCount, maxConnections);
            if (fileSize < minChunkBytes * 2 || actualConnections < 2)
            {
                await DownloadAsync(url, destFile, label, preknownTotal: fileSize);
                return;
            }

            if (fileSize < minChunkBytes * actualConnections)
            {
                actualConnections = Math.Max(1, (int)(fileSize / minChunkBytes));
            }
            actualConnections = Math.Clamp(actualConnections, 1, 16);
            if (actualConnections < 2)
            {
                await DownloadAsync(url, destFile, label, preknownTotal: fileSize);
                return;
            }

            var baseChunkSize = fileSize.Value / actualConnections;
            var remainder = fileSize.Value % actualConnections;

            var tempDir = Path.Combine(Paths.CacheDir(), $"{Path.GetFileName(destFile)}.parts_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
            Directory.CreateDirectory(tempDir);

            try
            {
                var chunks = new (long Offset, long Length, string Path)[actualConnections];
                for (int i = 0; i < actualConnections; i++)
                {
                    var offset = i * baseChunkSize + Math.Min(i, (int)remainder);
                    var len = baseChunkSize + (i < remainder ? 1 : 0);
                    if (i == actualConnections - 1)
                    {
                        len = fileSize.Value - offset;
                    }
                    var chunkPath = Path.Combine(tempDir, $"chunk_{i:D4}");
                    chunks[i] = (offset, len, chunkPath);
                }

                using var progress = new ChunkProgress(label ?? "downloading", fileSize.Value, actualConnections);
                progress.StartProgressLoop();

                var tasks = new Task[actualConnections];
                for (int i = 0; i < actualConnections; i++)
                {
                    var (offset, length, chunkPath) = chunks[i];
                    tasks[i] = DownloadChunkAsync(http, url, offset, length, chunkPath, progress, i);
                }

                await Task.WhenAll(tasks);
                progress.Complete();

                await using var finalStream = File.Create(destFile);
                for (int i = 0; i < actualConnections; i++)
                {
                    await using var chunkStream = File.OpenRead(chunks[i].Path);
                    await chunkStream.CopyToAsync(finalStream);
                    await finalStream.FlushAsync();
                }
            }
            finally
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    /// <summary>
    /// 下载文件到指定目录，文件名由响应头 / URL / 随机名决定。
    /// Download to a directory; the filename is derived from the Content-Disposition header,
    /// the URL path, or a random suffix-less name.
    /// </summary>
    /// <param name="url">要下载的 URL / URL to download.</param>
    /// <param name="destDir">目标目录（不存在则自动创建）/ Destination directory (auto-created).</param>
    /// <param name="baseName">当响应头与 URL 都无法提供扩展名时使用的基础名（不含扩展名）/ Fallback basename used when no extension is discoverable.</param>
    /// <param name="label">进度条标签 / Progress bar label.</param>
    /// <returns>最终落地文件的完整路径 / Final on-disk path of the downloaded file.</returns>
    public static async Task<string> DownloadToDirAsync(
        string url,
        string destDir,
        string baseName,
        string? label = null)
    {
        Directory.CreateDirectory(destDir);
        var destFile = ResolveDestFile(url, destDir, baseName);
        await DownloadAsync(url, destFile, label, preknownTotal: null);
        return destFile;
    }

    /// <summary>
    /// 多线程分块版本，参数语义同 <see cref="DownloadChunkedAsync"/>，但目标文件名由响应头 / URL 推导。
    /// Chunked variant; arguments match <see cref="DownloadChunkedAsync"/>, but the destination
    /// filename is derived from the Content-Disposition header / URL.
    /// </summary>
    /// <returns>最终落地文件的完整路径 / Final on-disk path of the downloaded file.</returns>
    public static async Task<string> DownloadChunkedToDirAsync(
        string url,
        string destDir,
        string baseName,
        string? label,
        int maxConnections,
        int splitCount,
        int minSplitSizeMB)
    {
        Directory.CreateDirectory(destDir);
        var destFile = ResolveDestFile(url, destDir, baseName);
        await DownloadChunkedAsync(url, destFile, label, maxConnections, splitCount, minSplitSizeMB);
        return destFile;
    }

    /// <summary>
    /// 通过 HTTP HEAD（实际用 GET + ResponseHeadersRead 避免部分服务器不支持 HEAD）探测 URL，
    /// 从 <c>Content-Disposition</c> 头提取建议的下载文件名。
    /// 仅返回头部中的文件名（不含扩展名或不含路径），失败返回 <c>null</c>。
    ///
    /// Peek the URL via GET with <c>HttpCompletionOption.ResponseHeadersRead</c>
    /// (HEAD is unreliable; some servers return 405) and extract the suggested
    /// filename from the <c>Content-Disposition</c> header. Returns null on
    /// failure or when the header is absent.
    /// </summary>
    /// <param name="url">要探测的 URL / URL to peek.</param>
    /// <returns>建议的文件名（含扩展名）或 null / Suggested filename or null.</returns>
    public static string? PeekFilename(string url)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd(Config.LoadUserAgent());
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = http.Send(req, HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode) return null;
            return ExtractFilenameFromContentDisposition(resp);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 推导落地路径：Content-Disposition 文件名 → URL 扩展名 → baseName。
    /// Resolve the on-disk path from Content-Disposition filename, then URL extension, then baseName.
    /// </summary>
    private static string ResolveDestFile(string url, string destDir, string baseName)
    {
        var filename = PeekFilename(url);

        if (string.IsNullOrEmpty(filename))
        {
            var ext = InstallHelpers.ExtractExtensionFromUrl(url);
            filename = string.IsNullOrEmpty(ext) ? baseName : baseName + ext;
        }

        filename = Path.GetFileName(filename);
        if (string.IsNullOrEmpty(filename))
        {
            filename = baseName;
        }

        return Path.Combine(destDir, filename);
    }

    /// <summary>
    /// 从 <c>Content-Disposition</c> 头提取文件名（优先 RFC 5987 <c>filename*</c>），仅保留最后一段。
    /// Extract the filename from a <c>Content-Disposition</c> header (preferring RFC 5987 <c>filename*</c>);
    /// only the last path segment is kept.
    /// </summary>
    private static string? ExtractFilenameFromContentDisposition(HttpResponseMessage resp)
    {
        var cd = resp.Content.Headers.ContentDisposition;
        if (cd == null) return null;
        var fn = !string.IsNullOrEmpty(cd.FileNameStar) ? cd.FileNameStar : cd.FileName;
        if (string.IsNullOrEmpty(fn)) return null;
        fn = Path.GetFileName(fn);
        return string.IsNullOrEmpty(fn) ? null : fn;
    }

    /// <summary>
    /// 下载单个分块（带 Range 请求头）/ Download a single chunk with Range header.
    /// </summary>
    private static async Task DownloadChunkAsync(HttpClient http, string url, long offset, long length,
        string chunkPath, ChunkProgress progress, int chunkIndex)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(offset, offset + length - 1);
        req.Headers.UserAgent.ParseAdd(Config.LoadUserAgent());

        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();

        var expectedLen = resp.Content.Headers.ContentLength ?? length;
        if (expectedLen != length)
            throw new InvalidOperationException($"chunk {chunkIndex}: expected {length} bytes, server returned {expectedLen}");

        await using var src = await resp.Content.ReadAsStreamAsync();
        await using var dst = File.Create(chunkPath);

        var buffer = new byte[65536];
        int read;
        while ((read = await src.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
            progress.Report(read);
        }
        progress.CompleteChunk();
    }
}
