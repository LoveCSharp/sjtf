namespace Sjtf.Cli;

/// <summary>
/// 带进度条/提示的流复制助手 / Stream copy helpers with progress bar / hint.
/// </summary>
internal static class StreamCopy
{
    /// <summary>
    /// 带已知总大小的进度条复制流 / Copy stream with progress bar (known total size).
    /// </summary>
    internal static async Task CopyWithProgressAsync(Stream src, Stream dst, string label, long total, bool skipInitialDraw = false)
    {
        var buffer = new byte[8192];
        long downloaded = 0;
        var lastUpdate = DateTime.UtcNow;
        var samples = new Queue<(DateTime Time, long Bytes)>();
        const double windowSec = 2.0;
        const int barWidth = 20;

        if (!skipInitialDraw)
        {
            ConsoleProgress._lastProgressLength = 0;
            ConsoleProgress.DrawProgress(label, 0, total, 0, barWidth);
        }

        int read;
        while ((read = await src.ReadAsync(buffer)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read));
            downloaded += read;
            var now = DateTime.UtcNow;
            if ((now - lastUpdate).TotalMilliseconds >= 100 || downloaded == total)
            {
                lastUpdate = now;
                var speed = ConsoleProgress.ComputeSlidingSpeed(samples, now, downloaded, windowSec);
                ConsoleProgress.DrawProgress(label, downloaded, total, speed, barWidth);
            }
        }
        var finalSpeed = ConsoleProgress.ComputeSlidingSpeed(samples, DateTime.UtcNow, downloaded, windowSec);
        ConsoleProgress.DrawProgress(label, downloaded, total, finalSpeed, barWidth);
        ConsoleProgress.EndProgressLine();
    }

    /// <summary>
    /// 带未知总大小的进度提示复制流 / Copy stream with progress hint (unknown total size).
    /// </summary>
    internal static async Task CopyWithProgressUnknownAsync(Stream src, Stream dst, string label, bool skipInitialDraw = false)
    {
        var buffer = new byte[8192];
        long downloaded = 0;
        var lastUpdate = DateTime.UtcNow;
        var samples = new Queue<(DateTime Time, long Bytes)>();
        const double windowSec = 2.0;

        if (!skipInitialDraw)
        {
            ConsoleProgress._lastProgressLength = 0;
            ConsoleProgress.WriteProgressLine($"{label} 0 B downloaded... (0 B/s)");
        }

        int read;
        while ((read = await src.ReadAsync(buffer)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read));
            downloaded += read;
            var now = DateTime.UtcNow;
            if ((now - lastUpdate).TotalMilliseconds >= 100)
            {
                lastUpdate = now;
                var speed = ConsoleProgress.ComputeSlidingSpeed(samples, now, downloaded, windowSec);
                ConsoleProgress.WriteProgressLine($"{label} {Formatters.FormatSize(downloaded)} downloaded... ({Formatters.FormatSize((long)speed)}/s)");
            }
        }
        ConsoleProgress.EndProgressLine();
    }
}