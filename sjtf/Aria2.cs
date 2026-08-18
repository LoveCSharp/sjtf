using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace Sjtf;

/// <summary>
/// aria2 集成：查找/下载 aria2 二进制文件并执行下载任务。
/// aria2 integration: find/download aria2 binary and execute download tasks.
/// </summary>
internal static class Aria2
{
    private const string Aria2cExeName = "aria2c";

    /// <summary>
    /// 所有 aria2 相关日志的统一前缀 / Unified prefix for all aria2-related log lines.
    /// </summary>
    internal const string LogPrefix = "aria2:";

    /// <summary>
    /// 获取当前平台的 aria2c 可执行文件名 / Get the aria2c executable name for the current platform.
    /// </summary>
    public static string ExeName()
    {
        var isWin = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        return isWin ? Aria2cExeName + ".exe" : Aria2cExeName;
    }

    /// <summary>
    /// 获取 tools 目录路径 / Get the tools directory path.
    /// </summary>
    public static string ToolsDir() => Path.Combine(Paths.SjtfRoot(), "tools");

    /// <summary>
    /// 仅在 PATH / tools 目录中查找 aria2c，不发起任何下载。找不到返回 null。
    /// Search PATH / tools dir only, do not download. Returns null if not found.
    /// </summary>
    /// <returns>aria2c 完整路径，或 null / Full path to aria2c, or null.</returns>
    public static string? TryLocateAria2()
    {
        var exeName = ExeName();

        if (TryFindInPath(exeName) is { } pathInEnv)
        {
            Console.WriteLine($"{LogPrefix} found in PATH: {pathInEnv}");
            return pathInEnv;
        }

        var toolsDir = ToolsDir();
        Directory.CreateDirectory(toolsDir);
        var localPath = Path.Combine(toolsDir, exeName);

        if (File.Exists(localPath))
        {
            Console.WriteLine($"{LogPrefix} found in tools dir: {localPath}");
            return localPath;
        }

        return null;
    }

    /// <summary>
    /// 从 GitHub 下载 aria2 zip 并解压到 tools 目录 / Download aria2 zip from GitHub and extract to tools dir.
    /// </summary>
    /// <returns>安装后的完整路径 / Full path after installation.</returns>
    public static async Task<string> DownloadAria2Async()
    {
        var exeName = ExeName();
        var toolsDir = ToolsDir();
        Directory.CreateDirectory(toolsDir);
        var localPath = Path.Combine(toolsDir, exeName);

        var os = Arch.CurrentOs();
        var arch = Arch.CurrentArch();
        var url = Config.LoadAria2Url(os, arch);

        if (string.IsNullOrEmpty(url))
        {
            throw new InvalidOperationException($"{LogPrefix} no download URL configured for {os}_{arch}");
        }

        // GitHub proxy：仅当 URL 以 https://github.com 开头、且 [github].proxy 已配置时拼接前缀
        var proxy = Config.LoadGithubProxy();
        if (!string.IsNullOrEmpty(proxy) && url.StartsWith("https://github.com", StringComparison.OrdinalIgnoreCase))
        {
            url = proxy.TrimEnd('/') + "/" + url;
        }

        Console.WriteLine($"{LogPrefix} downloading: {url}");

        string? downloadedPath = null;
        try
        {
            downloadedPath = await HttpFileDownloader.DownloadChunkedToDirAsync(
                url, toolsDir, baseName: "aria2", label: $"{LogPrefix} downloading",
                Config.LoadMaxConnectionPerServer(), Config.LoadSplit(), Config.LoadMinSplitSize());
            Console.WriteLine($"{LogPrefix} extracting {downloadedPath}...");
            ExtractAria2Binary(downloadedPath, toolsDir, exeName);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"{LogPrefix} failed to download or extract: {ex.Message}", ex);
        }
        finally
        {
            if (downloadedPath != null)
            {
                try { File.Delete(downloadedPath); } catch { }
            }
        }

        if (File.Exists(localPath))
        {
            Console.WriteLine($"{LogPrefix} installed to {localPath}");
            return localPath;
        }

        throw new InvalidOperationException($"{LogPrefix} binary not found after extraction: {localPath}");
    }

    /// <summary>
    /// 查找可用的 aria2c 可执行文件路径。优先级：PATH > tools 目录 > 下载到 tools 目录。
    /// Find available aria2c executable path. Priority: PATH > tools dir > download to tools dir.
    /// </summary>
    /// <returns>aria2c 可执行文件完整路径，如果不可用则返回 null / Full path to aria2c, or null if not available.</returns>
    public static async Task<string?> FindOrDownloadAria2Async()
    {
        if (TryLocateAria2() is { } existing)
        {
            return existing;
        }

        var os = Arch.CurrentOs();
        var arch = Arch.CurrentArch();
        var url = Config.LoadAria2Url(os, arch);

        if (string.IsNullOrEmpty(url))
        {
            Console.WriteLine($"{LogPrefix} no download URL configured for {os}_{arch}, falling back to built-in downloader");
            return null;
        }

        try
        {
            return await DownloadAria2Async();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{LogPrefix} {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 使用 aria2c 下载文件 / Download file using aria2c.
    /// </summary>
    /// <param name="aria2cPath">aria2c 可执行文件路径 / Path to aria2c executable.</param>
    /// <param name="url">要下载的 URL / URL to download.</param>
    /// <param name="destFile">目标文件路径 / Destination file path.</param>
    /// <param name="label">进度条标签 / Progress bar label.</param>
    /// <param name="maxConnections">最大连接数 / Max connections.</param>
    /// <param name="splitCount">分块数 / Split count.</param>
    /// <param name="minSplitSizeMB">最小分块大小（MB）/ Minimum split size in MB.</param>
    public static async Task RunAsync(string aria2cPath, string url, string destFile, string? label,
        int maxConnections, int splitCount, int minSplitSizeMB)
    {
        var args = BuildArgs(url, destFile, maxConnections, splitCount, minSplitSizeMB);

        Console.WriteLine($"{label ?? "downloading"}: using aria2c ({maxConnections} connections, {splitCount} splits)");

        var psi = new ProcessStartInfo
        {
            FileName = aria2cPath,
            Arguments = string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)),
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = false,
        };

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"{LogPrefix} failed to start process: {aria2cPath}");

        var tcs = new TaskCompletionSource<bool>();
        proc.EnableRaisingEvents = true;
        proc.Exited += (s, e) => tcs.TrySetResult(true);

        _ = proc.StandardOutput.BaseStream.CopyToAsync(Console.OpenStandardOutput());
        _ = proc.StandardError.BaseStream.CopyToAsync(Console.OpenStandardError());

        await tcs.Task;

        if (proc.ExitCode != 0)
        {
            throw new InvalidOperationException($"aria2c exited with code {proc.ExitCode}");
        }

        if (!File.Exists(destFile))
        {
            throw new InvalidOperationException($"{LogPrefix} output file not found: {destFile}");
        }
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
            $"--max-connection-per-server={maxConnections}",
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
