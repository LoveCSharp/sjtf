using System.Runtime.InteropServices;

namespace Sjtf;

/// <summary>
/// 操作系统和架构检测 / Operating system and architecture detection.
/// 提供当前运行环境的 OS 和 CPU 架构标识字符串。
/// Provides OS and CPU architecture identifier strings for the current runtime environment.
/// </summary>
internal static class Arch
{
    /// <summary>
    /// 获取当前操作系统标识 / Get the current operating system identifier.
    /// 返回值为 "windows"、"linux"、"macos" 或 "unknown"。
    /// Returns "windows", "linux", "macos", or "unknown".
    /// </summary>
    public static string CurrentOs() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" :
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" :
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos" :
        "unknown";

    /// <summary>
    /// 获取当前 CPU 架构标识 / Get the current CPU architecture identifier.
    /// 返回值为 "x86_64"、"x86"、"aarch64"、"arm" 或原始架构名称小写形式。
    /// Returns "x86_64", "x86", "aarch64", "arm", or the lower-case raw architecture name.
    /// </summary>
    public static string CurrentArch()
    {
        var arch = RuntimeInformation.ProcessArchitecture;
        return arch switch
        {
            Architecture.X64 => "x86_64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "aarch64",
            Architecture.Arm => "arm",
            _ => arch.ToString().ToLowerInvariant()
        };
    }
}
