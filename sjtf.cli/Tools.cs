using System.Diagnostics;
using System.Text;

namespace Sjtf.Cli;

/// <summary>
/// 跨领域小工具助手（符号链接、快捷方式、异常处理、半成品清理）。
/// Cross-cutting utility helpers (symlinks, shortcuts, exception unwrapping, partial-download cleanup).
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

    /// <summary>
    /// 创建 Windows .lnk 快捷方式 / Create a Windows .lnk shortcut.
    /// 仅在 Windows 下可用；其他平台抛 PlatformNotSupportedException。
    /// Only available on Windows; throws PlatformNotSupportedException otherwise.
    /// 通过 powershell.exe + WScript.Shell COM 在进程外生成 .lnk（避免命令行转义地狱，
    /// 也保持 AOT 兼容——不直接绑定 COM 类型）。
    /// Implementation invokes powershell.exe with WScript.Shell COM out of process (avoids
    /// command-line escaping pitfalls and keeps AOT-compatible — no direct COM interop).
    /// </summary>
    /// <param name="lnkPath">.lnk 文件完整路径 / Full path to the .lnk file.</param>
    /// <param name="targetPath">目标可执行文件完整路径 / Full path to the target executable.</param>
    /// <param name="workingDir">起始目录（已替换占位符）/ Working directory (placeholders already substituted).</param>
    /// <param name="arguments">启动参数 / Launch arguments.</param>
    /// <param name="iconLocation">图标位置字符串（"path" 或 "path,index"）/ Icon location string.</param>
    public static void CreateShortcut(string lnkPath, string targetPath, string workingDir, string arguments, string iconLocation)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("desktop shortcuts (.lnk) are Windows-only");

        // 转义单引号给 PowerShell（用 '' 转义）
        // Escape single quotes for PowerShell (use '' to escape).
        string Esc(string s) => (s ?? "").Replace("'", "''");

        var script = $@"$ErrorActionPreference = 'Stop'
$WshShell = New-Object -ComObject WScript.Shell
$Shortcut = $WshShell.CreateShortcut('{Esc(lnkPath)}')
$Shortcut.TargetPath = '{Esc(targetPath)}'
{(string.IsNullOrEmpty(workingDir) ? "" : $"$Shortcut.WorkingDirectory = '{Esc(workingDir)}'")}
{(string.IsNullOrEmpty(arguments) ? "" : $"$Shortcut.Arguments = '{Esc(arguments)}'")}
{(string.IsNullOrEmpty(iconLocation) ? "" : $"$Shortcut.IconLocation = '{Esc(iconLocation)}'")}
$Shortcut.Save()
";

        var scriptPath = Path.GetTempFileName() + ".ps1";
        try
        {
            File.WriteAllText(scriptPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var psi = new ProcessStartInfo("powershell.exe",
                $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\"")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi)!;
            proc.WaitForExit();
            if (proc.ExitCode != 0)
            {
                var err = proc.StandardError.ReadToEnd();
                throw new InvalidOperationException($"create shortcut failed (exit {proc.ExitCode}): {err}");
            }
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }

    /// <summary>
    /// 删除 .lnk 快捷方式（如存在）/ Delete a .lnk shortcut if it exists.
    /// 失败时仅写 warning，不抛出（uninstall 流程不应因快捷方式残留而中断）。
    /// On failure, only writes a warning (uninstall flow must not abort over a stray shortcut).
    /// </summary>
    /// <param name="lnkPath">.lnk 文件完整路径 / Full path to the .lnk file.</param>
    public static void RemoveShortcut(string lnkPath)
    {
        try
        {
            if (File.Exists(lnkPath))
                File.Delete(lnkPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"warning: failed to remove shortcut '{lnkPath}': {ex.Message}");
        }
    }
}