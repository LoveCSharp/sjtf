namespace Sjtf.Cli;

/// <summary>
/// 格式化助手（字节数等人类可读表示）/ Formatting helpers (human-readable byte counts, etc.).
/// </summary>
internal static class Formatters
{
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
}