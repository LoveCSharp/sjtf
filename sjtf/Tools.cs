using System.Security.Cryptography;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace Sjtf;

internal static partial class Tools
{
    /// <summary>
    /// 获取 sjtf 可执行文件所在的根目录 / Get the root directory where sjtf executable resides.
    /// </summary>
    public static string SjtfRoot()
    {
        var path = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot determine process path");

        var info = new FileInfo(path);
        var resolved = info.ResolveLinkTarget(returnFinalTarget: true);
        if (resolved != null)
            path = resolved.FullName;

        var dir = Path.GetDirectoryName(path) ?? "";
        return dir.TrimEnd(Path.DirectorySeparatorChar);
    }

    /// <summary>
    /// 获取缓存目录路径（如不存在则创建） / Get cache directory path (create if not exists).
    /// </summary>
    public static string CacheDir()
    {
        var d = Path.Combine(SjtfRoot(), "cache");
        Directory.CreateDirectory(d);
        return d;
    }

    /// <summary>
    /// 异步下载文件到指定路径 / Download a file asynchronously to the specified path.
    /// </summary>
    /// <param name="url">要下载的 URL / URL to download.</param>
    /// <param name="destFile">目标文件路径 / Destination file path.</param>
    /// <param name="label">进度条标签 / Progress bar label.</param>
    /// <param name="ct">取消令牌 / Cancellation token.</param>
    public static async Task DownloadFileAsync(string url, string destFile, string? label = null, CancellationToken ct = default)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd(Config.LoadUserAgent());
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        var total = resp.Content.Headers.ContentLength;
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(destFile);

        var showProgress = !string.IsNullOrEmpty(label);
        if (showProgress && total.HasValue)
        {
            await CopyWithProgressAsync(src, dst, label!, total.Value, ct);
        }
        else if (showProgress)
        {
            await CopyWithProgressUnknownAsync(src, dst, label!, ct);
        }
        else
        {
            await src.CopyToAsync(dst, ct);
        }
    }

    /// <summary>
    /// 带已知总大小的进度条复制流 / Copy stream with progress bar (known total size).
    /// </summary>
    private static async Task CopyWithProgressAsync(Stream src, Stream dst, string label, long total, CancellationToken ct)
    {
        var buffer = new byte[8192];
        long downloaded = 0;
        var lastUpdate = DateTime.UtcNow;
        var samples = new Queue<(DateTime Time, long Bytes)>();
        const double windowSec = 2.0;
        const int barWidth = 20;

        _lastProgressLength = 0;
        DrawProgress(label, 0, total, 0, barWidth);

        int read;
        while ((read = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read), ct);
            downloaded += read;
            var now = DateTime.UtcNow;
            if ((now - lastUpdate).TotalMilliseconds >= 100 || downloaded == total)
            {
                lastUpdate = now;
                var speed = ComputeSlidingSpeed(samples, now, downloaded, windowSec);
                DrawProgress(label, downloaded, total, speed, barWidth);
            }
        }
        var finalSpeed = ComputeSlidingSpeed(samples, DateTime.UtcNow, downloaded, windowSec);
        DrawProgress(label, downloaded, total, finalSpeed, barWidth);
        EndProgressLine();
    }

    /// <summary>
    /// 带未知总大小的进度提示复制流 / Copy stream with progress hint (unknown total size).
    /// </summary>
    private static async Task CopyWithProgressUnknownAsync(Stream src, Stream dst, string label, CancellationToken ct)
    {
        var buffer = new byte[8192];
        long downloaded = 0;
        var lastUpdate = DateTime.UtcNow;
        var samples = new Queue<(DateTime Time, long Bytes)>();
        const double windowSec = 2.0;

        _lastProgressLength = 0;
        WriteProgressLine($"{label} 0 B downloaded... (0 B/s)");

        int read;
        while ((read = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read), ct);
            downloaded += read;
            var now = DateTime.UtcNow;
            if ((now - lastUpdate).TotalMilliseconds >= 100)
            {
                lastUpdate = now;
                var speed = ComputeSlidingSpeed(samples, now, downloaded, windowSec);
                WriteProgressLine($"{label} {FormatSize(downloaded)} downloaded... ({FormatSize((long)speed)}/s)");
            }
        }
        EndProgressLine();
    }

    private static int _lastProgressLength;

    /// <summary>
    /// 写入一行进度文本（覆盖上一行）/ Write a progress line (overwrite previous line).
    /// </summary>
    private static void WriteProgressLine(string text)
    {
        if (text.Length < _lastProgressLength)
        {
            text += new string(' ', _lastProgressLength - text.Length);
        }
        _lastProgressLength = text.Length;
        Console.Write($"\r{text}");
    }

    /// <summary>
    /// 结束当前进度行（换行） / End the current progress line (newline).
    /// </summary>
    private static void EndProgressLine()
    {
        _lastProgressLength = 0;
        Console.WriteLine();
    }

    /// <summary>
    /// 使用滑动窗口计算下载速度 / Compute download speed using a sliding window.
    /// </summary>
    private static double ComputeSlidingSpeed(Queue<(DateTime Time, long Bytes)> samples, DateTime now, long downloaded, double windowSec)
    {
        samples.Enqueue((now, downloaded));
        var cutoff = now - TimeSpan.FromSeconds(windowSec);
        while (samples.Count > 0 && samples.Peek().Time < cutoff)
        {
            samples.Dequeue();
        }
        if (samples.Count < 2) return 0;
        var first = samples.Peek();
        var last = samples.Last();
        var dt = (last.Time - first.Time).TotalSeconds;
        return dt > 0 ? (last.Bytes - first.Bytes) / dt : 0;
    }

    /// <summary>
    /// 绘制进度条到控制台 / Draw a progress bar to the console.
    /// </summary>
    private static void DrawProgress(string label, long downloaded, long total, double speedBps, int barWidth)
    {
        var percent = total > 0 ? (int)(100.0 * downloaded / total) : 0;
        var filled = total > 0 ? (int)(barWidth * downloaded / total) : 0;
        if (filled > barWidth) filled = barWidth;
        if (filled < 0) filled = 0;
        var bar = new string('█', filled) + new string(' ', barWidth - filled);
        var speed = $"{FormatSize((long)speedBps)}/s";
        var text = $"{label} [{bar}] {percent,3}% {FormatSize(downloaded)}/{FormatSize(total)} ({speed})";
        WriteProgressLine(text);
    }

    /// <summary>
    /// 将字节数格式化为人类可读的字符串 / Format byte count to human-readable string.
    /// </summary>
    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:F1} MB";
        return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
    }

    /// <summary>
    /// 解压压缩包到目标目录 / Extract an archive to the destination directory.
    /// </summary>
    /// <param name="archivePath">压缩包路径 / Archive file path.</param>
    /// <param name="destDir">目标目录 / Destination directory.</param>
    /// <param name="label">进度条标签 / Progress bar label.</param>
    public static void ExtractArchive(string archivePath, string destDir, string? label = null)
    {
        Directory.CreateDirectory(destDir);
        using var archive = ArchiveFactory.OpenArchive(archivePath);

        var totalSize = archive.TotalUncompressedSize;
        long extractedBytes = 0;
        var lastUpdate = DateTime.UtcNow;
        var samples = new Queue<(DateTime Time, long Bytes)>();
        const double windowSec = 2.0;
        const int barWidth = 20;
        var showProgress = !string.IsNullOrEmpty(label);

        if (showProgress)
        {
            _lastProgressLength = 0;
            DrawProgress(label!, 0, totalSize, 0, barWidth);
        }

        foreach (var entry in archive.Entries)
        {
            if (entry.IsDirectory || string.IsNullOrEmpty(entry.Key)) continue;

            var entryPath = Path.Combine(destDir, entry.Key);
            var entryDir = Path.GetDirectoryName(entryPath);
            if (!string.IsNullOrEmpty(entryDir))
                Directory.CreateDirectory(entryDir);

            using var entryStream = entry.OpenEntryStream();
            using var fs = File.Create(entryPath);
            entryStream.CopyTo(fs);

            extractedBytes += entry.Size;

            if (showProgress)
            {
                var now = DateTime.UtcNow;
                if ((now - lastUpdate).TotalMilliseconds >= 100 || extractedBytes >= totalSize)
                {
                    lastUpdate = now;
                    var speed = ComputeSlidingSpeed(samples, now, extractedBytes, windowSec);
                    DrawProgress(label!, extractedBytes, totalSize, speed, barWidth);
                }
            }
        }

        if (showProgress)
        {
            var finalSpeed = ComputeSlidingSpeed(samples, DateTime.UtcNow, extractedBytes, windowSec);
            DrawProgress(label!, extractedBytes, totalSize, finalSpeed, barWidth);
            EndProgressLine();
        }
    }

    /// <summary>
    /// 解压压缩包并自动去除多余的单层根目录 / Extract archive and automatically strip one extra root directory.
    /// </summary>
    /// <param name="archivePath">压缩包路径 / Archive file path.</param>
    /// <param name="destDir">目标目录 / Destination directory.</param>
    /// <param name="label">进度条标签 / Progress bar label.</param>
    public static void ExpandArchiveRealRoot(string archivePath, string destDir, string? label = null)
    {
        var temp = Path.Combine(CacheDir(), $"extract_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{archivePath.GetHashCode()}");
        Directory.CreateDirectory(temp);
        try
        {
            ExtractArchive(archivePath, temp, label);

            var root = temp;
            for (int i = 0; i < 100; i++)
            {
                var entries = Directory.GetFileSystemEntries(root);
                if (entries.Length != 1) break;
                if (!Directory.Exists(entries[0])) break;
                root = entries[0];
            }

            CopyDir(root, destDir);
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// 递归复制目录 / Recursively copy a directory.
    /// </summary>
    private static void CopyDir(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var dir in Directory.EnumerateDirectories(src, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(dir.Replace(src, dest));
        }
        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var target = file.Replace(src, dest);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    /// <summary>
    /// 创建或覆盖符号链接 / Create or overwrite a symbolic link.
    /// </summary>
    /// <param name="linkPath">符号链接路径 / Symbolic link path.</param>
    /// <param name="targetPath">目标路径 / Target path.</param>
    public static void CreateSymlink(string linkPath, string targetPath)
    {
        if (File.Exists(linkPath) || Directory.Exists(linkPath))
        {
            File.Delete(linkPath);
        }
        File.CreateSymbolicLink(linkPath, targetPath);
    }

    /// <summary>
    /// 获取异常链中最内层的异常 / Get the innermost exception from an exception chain.
    /// </summary>
    /// <param name="ex">外层异常 / Outer exception.</param>
    /// <returns>最内层异常 / Innermost exception.</returns>
    public static Exception GetInnermostException(Exception ex)
    {
        var root = ex;
        while (root.InnerException != null)
            root = root.InnerException;
        return root;
    }

    /// <summary>
    /// 清理不完整的下载产物（最终文件 + 分块临时目录）/ Cleanup partial download artifacts (final file + chunk temp dirs).
    /// </summary>
    /// <param name="destFile">目标文件路径 / Destination file path.</param>
    internal static void CleanupPartialDownload(string destFile)
    {
        try { File.Delete(destFile); } catch { }
        var pattern = Path.GetFileName(destFile) + ".parts_*";
        foreach (var d in Directory.GetDirectories(CacheDir(), pattern))
        {
            try { Directory.Delete(d, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// 分块下载文件（支持 Range 请求 + 多线程）/ Multi-chunk file download with Range requests.
    /// </summary>
    /// <param name="url">要下载的 URL / URL to download.</param>
    /// <param name="destFile">目标文件路径 / Destination file path.</param>
    /// <param name="label">进度条标签 / Progress bar label.</param>
    /// <param name="maxConnections">最大连接数（线程数）/ Maximum connections (threads).</param>
    /// <param name="splitCount">目标分块数 / Target number of chunks.</param>
    /// <param name="minSplitSizeMB">最小分块大小（MB）/ Minimum chunk size (MB).</param>
    /// <param name="ct">取消令牌 / Cancellation token.</param>
    public static async Task DownloadFileAsync(string url, string destFile, string? label,
        int maxConnections, int splitCount, int minSplitSizeMB, CancellationToken ct = default)
    {
        maxConnections = Math.Clamp(maxConnections, 1, 16);
        splitCount = Math.Clamp(splitCount, 1, 16);
        minSplitSizeMB = Math.Clamp(minSplitSizeMB, 1, 1024);

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd(Config.LoadUserAgent());

        using var headResp = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, url), ct);
        headResp.EnsureSuccessStatusCode();

        var totalSize = headResp.Content.Headers.ContentLength;
        if (!totalSize.HasValue || totalSize.Value == 0)
        {
            await DownloadFileAsync(url, destFile, label, ct);
            return;
        }

        var acceptRanges = headResp.Headers.AcceptRanges.Any(h => h.Equals("bytes", StringComparison.OrdinalIgnoreCase));
        if (!acceptRanges)
        {
            await DownloadFileAsync(url, destFile, label, ct);
            return;
        }

        var fileSize = totalSize.Value;
        var minChunkBytes = (long)minSplitSizeMB * 1024 * 1024;

        int actualConnections = Math.Min(splitCount, maxConnections);
        if (fileSize < minChunkBytes * 2 || actualConnections < 2)
        {
            await DownloadFileAsync(url, destFile, label, ct);
            return;
        }

        if (fileSize < minChunkBytes * actualConnections)
        {
            actualConnections = Math.Max(1, (int)(fileSize / minChunkBytes));
        }
        actualConnections = Math.Clamp(actualConnections, 1, 16);
        if (actualConnections < 2)
        {
            await DownloadFileAsync(url, destFile, label, ct);
            return;
        }

        var baseChunkSize = fileSize / actualConnections;
        var remainder = fileSize % actualConnections;

        var tempDir = Path.Combine(CacheDir(), $"{Path.GetFileName(destFile)}.parts_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
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
                    len = fileSize - offset;
                }
                var chunkPath = Path.Combine(tempDir, $"chunk_{i:D4}");
                chunks[i] = (offset, len, chunkPath);
            }

            using var progress = new ChunkProgress(label ?? "downloading", fileSize, actualConnections);
            progress.StartProgressLoop();

            var tasks = new Task[actualConnections];
            for (int i = 0; i < actualConnections; i++)
            {
                var (offset, length, chunkPath) = chunks[i];
                tasks[i] = DownloadChunkAsync(http, url, offset, length, chunkPath, progress, i, ct);
            }

            await Task.WhenAll(tasks);

            if (ct.IsCancellationRequested)
                throw new OperationCanceledException(ct);

            await using var finalStream = File.Create(destFile);
            for (int i = 0; i < actualConnections; i++)
            {
                await using var chunkStream = File.OpenRead(chunks[i].Path);
                await chunkStream.CopyToAsync(finalStream, ct);
                await finalStream.FlushAsync(ct);
            }
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// 下载单个分块（带 Range 请求头）/ Download a single chunk with Range header.
    /// </summary>
    private static async Task DownloadChunkAsync(HttpClient http, string url, long offset, long length,
        string chunkPath, ChunkProgress progress, int chunkIndex, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(offset, offset + length - 1);
        req.Headers.UserAgent.ParseAdd(Config.LoadUserAgent());

        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        var expectedLen = resp.Content.Headers.ContentLength ?? length;
        if (expectedLen != length)
            throw new InvalidOperationException($"chunk {chunkIndex}: expected {length} bytes, server returned {expectedLen}");

        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(chunkPath);

        var buffer = new byte[65536];
        int read;
        while ((read = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            progress.Report(read);
        }
        progress.CompleteChunk();
    }
}

/// <summary>
/// 多线程分块下载进度聚合器（线程安全）/ Multi-thread chunk download progress aggregator (thread-safe).
/// </summary>
internal sealed class ChunkProgress : IDisposable
{
    private readonly long _totalSize;
    private long _downloaded;
    private int _completedChunks;
    private readonly int _totalChunks;
    private readonly string _label;
    private readonly CancellationTokenSource _cts;
    private Task? _progressTask;
    private volatile bool _disposed;
    private readonly object _renderLock = new();

    private static int _lastProgressLength;

    public ChunkProgress(string label, long totalSize, int totalChunks)
    {
        _label = label;
        _totalSize = totalSize;
        _totalChunks = totalChunks;
        _cts = new CancellationTokenSource();
    }

    public void Report(long bytes)
    {
        Interlocked.Add(ref _downloaded, bytes);
    }

    public void CompleteChunk()
    {
        Interlocked.Increment(ref _completedChunks);
    }

    public void StartProgressLoop()
    {
        _progressTask = Task.Run(() => ProgressLoopAsync(_cts.Token));
    }

    private async Task ProgressLoopAsync(CancellationToken ct)
    {
        var samples = new Queue<(DateTime Time, long Bytes)>();
        const double windowSec = 2.0;
        const int barWidth = 20;
        var lastUpdate = DateTime.UtcNow;
        var lastLength = 0;

        while (!ct.IsCancellationRequested && !_disposed)
        {
            await Task.Delay(100, ct).ConfigureAwait(false);

            var downloaded = Interlocked.Read(ref _downloaded);
            var completed = Volatile.Read(ref _completedChunks);
            var now = DateTime.UtcNow;

            lock (_renderLock)
            {
                if ((now - lastUpdate).TotalMilliseconds < 100 && completed < _totalChunks && !_disposed)
                    continue;
                lastUpdate = now;

                samples.Enqueue((now, downloaded));
                while (samples.Count > 0 && samples.Peek().Time < now - TimeSpan.FromSeconds(windowSec))
                    samples.Dequeue();

                double speed = 0;
                if (samples.Count >= 2)
                {
                    var first = samples.Peek();
                    var last = samples.Last();
                    var dt = (last.Time - first.Time).TotalSeconds;
                    if (dt > 0) speed = (last.Bytes - first.Bytes) / dt;
                }

                var percent = _totalSize > 0 ? (int)(100.0 * downloaded / _totalSize) : 0;
                var filled = _totalSize > 0 ? (int)(barWidth * downloaded / _totalSize) : 0;
                if (filled > barWidth) filled = barWidth;
                if (filled < 0) filled = 0;
                var bar = new string('█', filled) + new string(' ', barWidth - filled);
                var speedStr = $"{Tools.FormatSize((long)speed)}/s";
                var text = $"{_label} [{bar}] {percent,3}% {Tools.FormatSize(downloaded)}/{Tools.FormatSize(_totalSize)} ({speedStr}) [{completed}/{_totalChunks} chunks]";

                if (text.Length < lastLength) text += new string(' ', lastLength - text.Length);
                lastLength = text.Length;
                _lastProgressLength = lastLength;
                Console.Write($"\r{text}");
            }

            if (completed >= _totalChunks) break;
        }

        if (_disposed) return;

        lock (_renderLock)
        {
            var downloaded = Interlocked.Read(ref _downloaded);
            var finalSpeed = 0.0;
            if (samples.Count >= 2)
            {
                var first = samples.Peek();
                var last = samples.Last();
                var dt = (last.Time - first.Time).TotalSeconds;
                if (dt > 0) finalSpeed = (last.Bytes - first.Bytes) / dt;
            }
            var percent = _totalSize > 0 ? 100 : 0;
            var bar = new string('█', barWidth);
            var text = $"{_label} [{bar}] {percent,3}% {Tools.FormatSize(downloaded)}/{Tools.FormatSize(_totalSize)} ({Tools.FormatSize((long)finalSpeed)}/s) [{_totalChunks}/{_totalChunks} chunks]";
            if (text.Length < _lastProgressLength) text += new string(' ', _lastProgressLength - text.Length);
            Console.WriteLine($"\r{text}");
            _lastProgressLength = 0;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _cts.Cancel(); } catch { }
        try { _progressTask?.Wait(3000); } catch { }
        _cts.Dispose();
    }
}
