namespace Sjtf;

/// <summary>
/// 路径解析助手（sjtf 根目录与缓存目录）/ Path resolution helpers (sjtf root and cache directories).
/// </summary>
internal static class Paths
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
}