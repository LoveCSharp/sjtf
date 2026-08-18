namespace Sjtf;

/// <summary>
/// 跨领域小工具助手（符号链接、异常处理、半成品清理）。
/// Cross-cutting utility helpers (symlinks, exception unwrapping, partial-download cleanup).
/// </summary>
internal static class Tools
{
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
        foreach (var d in Directory.GetDirectories(Paths.CacheDir(), pattern))
        {
            try { Directory.Delete(d, recursive: true); } catch { }
        }
    }
}