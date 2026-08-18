namespace Sjtf;

/// <summary>
/// 控制台进度输出助手（进度条与滑动速度计算）/ Console progress rendering helpers (progress bar and sliding speed).
/// </summary>
internal static class ConsoleProgress
{
    internal static int _lastProgressLength;

    /// <summary>
    /// 写入一行进度文本（覆盖上一行）/ Write a progress line (overwrite previous line).
    /// </summary>
    internal static void WriteProgressLine(string text)
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
    internal static void EndProgressLine()
    {
        _lastProgressLength = 0;
        Console.WriteLine();
    }

    /// <summary>
    /// 使用滑动窗口计算下载速度 / Compute download speed using a sliding window.
    /// </summary>
    internal static double ComputeSlidingSpeed(Queue<(DateTime Time, long Bytes)> samples, DateTime now, long downloaded, double windowSec)
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
    internal static void DrawProgress(string label, long downloaded, long total, double speedBps, int barWidth)
    {
        var percent = total > 0 ? (int)(100.0 * downloaded / total) : 0;
        var filled = total > 0 ? (int)(barWidth * downloaded / total) : 0;
        if (filled > barWidth) filled = barWidth;
        if (filled < 0) filled = 0;
        var bar = new string('█', filled) + new string(' ', barWidth - filled);
        var speed = $"{Formatters.FormatSize((long)speedBps)}/s";
        var text = $"{label} [{bar}] {percent,3}% {Formatters.FormatSize(downloaded)}/{Formatters.FormatSize(total)} ({speed})";
        WriteProgressLine(text);
    }
}