using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Sjtf;

/// <summary>
/// aria2 集成：查找/下载 aria2 二进制文件并执行下载任务。
/// aria2 integration: find/download aria2 binary and execute download tasks.
/// </summary>
internal static class Aria2
{
    private const string Aria2cExeName = "aria2c";

    /// <summary>
    /// 获取当前平台的 aria2c 可执行文件名 / Get the aria2c executable name for the current platform.
    /// </summary>
    public static string ExeName()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "aria2c.exe" : "aria2c";
    }

    /// <summary>
    /// 获取 tools 目录路径 / Get the tools directory path.
    /// </summary>
    public static string ToolsDir() => Path.Combine(Tools.SjtfRoot(), "tools");

    /// <summary>
    /// 查找可用的 aria2c 可执行文件路径。优先级：PATH > tools 目录 > 下载到 tools 目录。
    /// Find available aria2c executable path. Priority: PATH > tools dir > download to tools dir.
    /// </summary>
    /// <param name="ct">取消令牌 / Cancellation token.</param>
    /// <returns>aria2c 可执行文件完整路径，如果不可用则返回 null / Full path to aria2c, or null if not available.</returns>
    public static async Task<string?> FindOrDownloadAria2Async(CancellationToken ct = default)
    {
        var exeName = ExeName();

        if (TryFindInPath(exeName) is { } pathInEnv)
        {
            Console.WriteLine($"aria2: found in PATH: {pathInEnv}");
            return pathInEnv;
        }

        var toolsDir = ToolsDir();
        Directory.CreateDirectory(toolsDir);
        var localPath = Path.Combine(toolsDir, exeName);

        if (File.Exists(localPath))
        {
            Console.WriteLine($"aria2: found in tools dir: {localPath}");
            return localPath;
        }

        var os = Arch.CurrentOs();
        var arch = Arch.CurrentArch();
        var url = Config.LoadAria2Url(os, arch);

        if (string.IsNullOrEmpty(url))
        {
            Console.WriteLine($"aria2: no download URL configured for {os}_{arch}, falling back to built-in downloader");
            return null;
        }

        Console.WriteLine($"aria2: downloading from {url}...");

        if (url.StartsWith("https://github.com", StringComparison.OrdinalIgnoreCase))
        {
            var proxy = Config.LoadGithubProxy();
            if (!string.IsNullOrEmpty(proxy))
            {
                url = url.Replace("https://github.com", proxy, StringComparison.OrdinalIgnoreCase);
                Console.WriteLine($"aria2: using GitHub proxy: {proxy}");
            }
        }

        var zipPath = localPath + ".zip";
        try
        {
            await Tools.DownloadFileAsync(url, zipPath, "aria2: downloading", ct);
            Console.WriteLine($"aria2: extracting {zipPath}...");
            ExtractAria2Binary(zipPath, toolsDir, exeName);
        }
        finally
        {
            try { File.Delete(zipPath); } catch { }
        }

        if (File.Exists(localPath))
        {
            Console.WriteLine($"aria2: installed to {localPath}");
            return localPath;
        }

        Console.WriteLine("aria2: extraction failed, falling back to built-in downloader");
        return null;
    }

    /// <summary>
    /// 构建 aria2c 命令行参数 / Build aria2c command line arguments.
    /// </summary>
    /// <param name="url">要下载的 URL / URL to download.</param>
    /// <param name="destFile">目标文件路径 / Destination file path.</param>
    /// <param name="maxConnections">最大连接数 / Max connections.</param>
    /// <param name="splitCount">分块数 / Split count.</param>
    /// <param name="minSplitSizeMB">最小分块大小（MB）/ Minimum split size in MB.</param>
    /// <returns>参数列表 / Arguments list.</returns>
    public static List<string> BuildArgs(string url, string destFile, int maxConnections, int splitCount, int minSplitSizeMB)
    {
        var destDir = Path.GetDirectoryName(destFile) ?? "";
        var fileName = Path.GetFileName(destFile);
        var args = new List<string>
        {
            "--continue=true",
            $"--max-connection={maxConnections}",
            $"--split={splitCount}",
            $"--min-split-size={minSplitSizeMB}M",
            "--file-allocation=none",
            "--content-disposition-default-utf8=true",
            "--allow-overwrite=true",
            "--auto-file-renaming=false",
            "--console-log-level=error",
            "--summary-interval=0",
            "-d", destDir,
            "-o", fileName,
            url
        };
        return args;
    }

    /// <summary>
    /// 在系统 PATH 中查找 aria2c / Find aria2c in system PATH.
    /// </summary>
    private static string? TryFindInPath(string exeName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrEmpty(dir)) continue;
            var candidate = Path.Combine(dir, exeName);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>
    /// 从 zip 文件中提取 aria2c 二进制文件 / Extract aria2c binary from zip file.
    /// </summary>
    private static void ExtractAria2Binary(string zipPath, string destDir, string exeName)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            if (!entry.Name.Equals(exeName, StringComparison.OrdinalIgnoreCase)) continue;

            var targetPath = Path.Combine(destDir, exeName);
            entry.ExtractToFile(targetPath, overwrite: true);

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    File.SetUnixFileMode(targetPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
                catch { }
            }

            return;
        }

        throw new InvalidOperationException($"aria2c binary ({exeName}) not found in zip: {zipPath}");
    }
}
