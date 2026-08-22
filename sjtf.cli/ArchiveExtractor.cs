using System.IO.Compression;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace Sjtf.Cli;

/// <summary>
/// 压缩包解压与目录复制助手 / Archive extraction and directory copy helpers.
/// </summary>
internal static class ArchiveExtractor
{
    /// <summary>
    /// 解压压缩包到目标目录 / Extract an archive to the destination directory.
    /// </summary>
    /// <param name="archivePath">压缩包路径 / Archive file path.</param>
    /// <param name="destDir">目标目录 / Destination directory.</param>
    /// <param name="label">进度条标签 / Progress bar label.</param>
    public static void ExtractArchive(string archivePath, string destDir, string? label = null)
    {
        Directory.CreateDirectory(destDir);

        var isTarGz = archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
                      archivePath.EndsWith(".tar.GZ", StringComparison.OrdinalIgnoreCase) ||
                      archivePath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase);

        if (isTarGz)
        {
            ExtractTarGz(archivePath, destDir, label);
            return;
        }

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
            ConsoleProgress._lastProgressLength = 0;
            ConsoleProgress.DrawProgress(label!, 0, totalSize, 0, barWidth);
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
                    var speed = ConsoleProgress.ComputeSlidingSpeed(samples, now, extractedBytes, windowSec);
                    ConsoleProgress.DrawProgress(label!, extractedBytes, totalSize, speed, barWidth);
                }
            }
        }

        if (showProgress)
        {
            var finalSpeed = ConsoleProgress.ComputeSlidingSpeed(samples, DateTime.UtcNow, extractedBytes, windowSec);
            ConsoleProgress.DrawProgress(label!, extractedBytes, totalSize, finalSpeed, barWidth);
            ConsoleProgress.EndProgressLine();
        }
    }

    internal static void ExtractTarGz(string archivePath, string destDir, string? label)
    {
        using var fileStream = File.OpenRead(archivePath);
        using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        using var reader = ReaderFactory.OpenReader(gzipStream);

        long totalSize = 0;
        var entries = new List<IEntry>();
        while (reader.MoveToNextEntry())
        {
            if (!reader.Entry.IsDirectory)
            {
                totalSize += reader.Entry.Size;
                entries.Add(reader.Entry);
            }
        }

        long extractedBytes = 0;
        var lastUpdate = DateTime.UtcNow;
        var samples = new Queue<(DateTime Time, long Bytes)>();
        const double windowSec = 2.0;
        const int barWidth = 20;
        var showProgress = !string.IsNullOrEmpty(label);

        if (showProgress)
        {
            ConsoleProgress._lastProgressLength = 0;
            ConsoleProgress.DrawProgress(label!, 0, totalSize, 0, barWidth);
        }

        using var fileStream2 = File.OpenRead(archivePath);
        using var gzipStream2 = new GZipStream(fileStream2, CompressionMode.Decompress);
        using var reader2 = ReaderFactory.OpenReader(gzipStream2);

        while (reader2.MoveToNextEntry())
        {
            if (reader2.Entry.IsDirectory || string.IsNullOrEmpty(reader2.Entry.Key)) continue;

            var entryPath = Path.Combine(destDir, reader2.Entry.Key);
            var entryDir = Path.GetDirectoryName(entryPath);
            if (!string.IsNullOrEmpty(entryDir))
                Directory.CreateDirectory(entryDir);

            using var entryStream = reader2.OpenEntryStream();
            using var fs = File.Create(entryPath);
            entryStream.CopyTo(fs);

            extractedBytes += reader2.Entry.Size;

            if (showProgress)
            {
                var now = DateTime.UtcNow;
                if ((now - lastUpdate).TotalMilliseconds >= 100 || extractedBytes >= totalSize)
                {
                    lastUpdate = now;
                    var speed = ConsoleProgress.ComputeSlidingSpeed(samples, now, extractedBytes, windowSec);
                    ConsoleProgress.DrawProgress(label!, extractedBytes, totalSize, speed, barWidth);
                }
            }
        }

        if (showProgress)
        {
            var finalSpeed = ConsoleProgress.ComputeSlidingSpeed(samples, DateTime.UtcNow, extractedBytes, windowSec);
            ConsoleProgress.DrawProgress(label!, extractedBytes, totalSize, finalSpeed, barWidth);
            ConsoleProgress.EndProgressLine();
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
        var temp = Path.Combine(Paths.CacheDir(), $"extract_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{archivePath.GetHashCode()}");
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
    internal static void CopyDir(string src, string dest)
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
}