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
}
