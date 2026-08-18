using System.Text.Json.Serialization;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Serialization;

namespace Sjtf;

/// <summary>
/// HTTP 相关配置 / HTTP related configuration.
/// </summary>
public sealed class SjtfHttp
{
    /// <summary>
    /// HTTP 请求 User-Agent / HTTP request User-Agent.
    /// </summary>
    [TomlPropertyName("user_agent")]
    public string UserAgent { get; set; } = "";
}

/// <summary>
/// 通用配置 / General configuration.
/// </summary>
public sealed class SjtfGeneral
{
    /// <summary>
    /// 工具安装目录 / Tool installation directory.
    /// </summary>
    [JsonPropertyName("install_dir")]
    public string InstallDir { get; set; } = "";
}

/// <summary>
/// GitHub 相关配置 / GitHub related configuration.
/// </summary>
public sealed class SjtfGithub
{
    /// <summary>
    /// GitHub 经典个人访问令牌 (ghp_...) / GitHub classic personal access token (ghp_...).
    /// </summary>
    [JsonPropertyName("token_classic")]
    public string? TokenClassic { get; set; }

    /// <summary>
    /// GitHub API 请求代理地址 / Proxy address for GitHub API requests.
    /// </summary>
    [TomlPropertyName("proxy")]
    public string Proxy { get; set; } = "";
}

/// <summary>
/// 包源配置 / Package source configuration.
/// </summary>
public sealed class SjtfPkgs
{
    /// <summary>
    /// 远程 pkgs.json 地址 / Remote pkgs.json URL.
    /// </summary>
    [TomlPropertyName("remote_url")]
    public string RemoteUrl { get; set; } = "";
}

/// <summary>
/// 下载配置 / Download configuration.
/// </summary>
public sealed class SjtfDownload
{
    /// <summary>
    /// 每个服务器的最大连接数（线程数）/ Maximum connections per server (thread count). Range: 1 ~ 16.
    /// </summary>
    [JsonPropertyName("max_connection_per_server")]
    public int MaxConnectionPerServer { get; set; } = 10;

    /// <summary>
    /// 下载分块数 / Number of download chunks. Range: 1 ~ 16.
    /// </summary>
    [JsonPropertyName("split")]
    public int Split { get; set; } = 10;

    /// <summary>
    /// 最小分块大小（单位 MB）/ Minimum chunk size (unit: MB). Range: 1 ~ 1024.
    /// </summary>
    [JsonPropertyName("min_split_size")]
    public int MinSplitSize { get; set; } = 1;

    /// <summary>
    /// 是否启用 aria2 下载 / Whether to enable aria2 download.
    /// </summary>
    [JsonPropertyName("aria2_enable")]
    public bool Aria2Enable { get; set; } = true;
}

/// <summary>
/// aria2 配置 / aria2 configuration.
/// </summary>
public sealed class SjtfAria2
{
    /// <summary>
    /// aria2 默认下载 URL（Windows x64）。当 [aria2] 段未配置且当前 OS/Arch 匹配 windows_x86_64 时使用。
    /// Default aria2 download URL (Windows x64). Used when [aria2] is not configured and the current OS/Arch matches windows_x86_64.
    /// </summary>
    public const string DefaultUrl = "https://github.com/aria2/aria2/releases/download/release-1.37.0/aria2-1.37.0-win-64bit-build1.zip";

    /// <summary>
    /// aria2 二进制文件下载地址，键名为 "{os}_{arch}"（如 windows_x86_64）/ aria2 binary download URL, key is "{os}_{arch}" (e.g. windows_x86_64).
    /// </summary>
    [TomlPropertyName("windows_x86_64")]
    public string? WindowsX86_64 { get; set; }

    [TomlPropertyName("linux_x86_64")]
    public string? LinuxX86_64 { get; set; }

    [TomlPropertyName("osx_arm64")]
    public string? OsxArm64 { get; set; }

    [TomlPropertyName("osx_x86_64")]
    public string? OsxX86_64 { get; set; }

    public string? GetUrl(string os, string arch)
    {
        var key = $"{os}_{arch}";
        var configured = key switch
        {
            "windows_x86_64" => WindowsX86_64,
            "linux_x86_64" => LinuxX86_64,
            "osx_arm64" => OsxArm64,
            "osx_x86_64" => OsxX86_64,
            _ => null
        };
        if (!string.IsNullOrEmpty(configured)) return configured;
        if (os == "windows" && arch == "x86_64") return DefaultUrl;
        return null;
    }
}

/// <summary>
/// sjtf 配置根模型 / sjtf configuration root model.
/// </summary>
public sealed class SjtfConfig
{
    [TomlPropertyName("general")]
    public SjtfGeneral General { get; set; } = new();

    [TomlPropertyName("github")]
    public SjtfGithub Github { get; set; } = new();

    [TomlPropertyName("http")]
    public SjtfHttp Http { get; set; } = new();

    [TomlPropertyName("pkgs")]
    public SjtfPkgs Pkgs { get; set; } = new();

    [TomlPropertyName("download")]
    public SjtfDownload Download { get; set; } = new();

    [TomlPropertyName("aria2")]
    public SjtfAria2 Aria2 { get; set; } = new();
}

[TomlSerializable(typeof(SjtfConfig))]
[TomlSerializable(typeof(SjtfGeneral))]
[TomlSerializable(typeof(SjtfGithub))]
[TomlSerializable(typeof(SjtfHttp))]
[TomlSerializable(typeof(SjtfPkgs))]
[TomlSerializable(typeof(SjtfDownload))]
[TomlSerializable(typeof(SjtfAria2))]
internal partial class SjtfConfigContext : TomlSerializerContext
{
}

internal static class Config
{
    /// <summary>
    /// 默认 HTTP User-Agent / Default HTTP User-Agent.
    /// </summary>
    public const string DefaultUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36 Edg/151.0.0.0";

    /// <summary>
    /// 获取 config.toml 的完整路径 / Get the full path of config.toml.
    /// </summary>
    private static string ConfigPath() => Path.Combine(Paths.SjtfRoot(), "config.toml");

    private static SjtfConfig? _cachedDoc;
    private static long _cachedDocMtime;
    private static readonly object _cacheLock = new();

    private static string? _cachedUserAgent;
    private static long _cachedUserAgentMtime;

    /// <summary>
    /// 加载并反序列化 config.toml / Load and deserialize config.toml.
    /// </summary>
    private static SjtfConfig? LoadDoc()
    {
        var path = ConfigPath();
        if (!File.Exists(path)) return null;

        var mtime = File.GetLastWriteTimeUtc(path).Ticks;

        lock (_cacheLock)
        {
            if (_cachedDoc != null && _cachedDocMtime == mtime)
                return _cachedDoc;

            var doc = TomlSerializer.Deserialize(File.ReadAllText(path), SjtfConfigContext.Default.SjtfConfig);
            _cachedDoc = doc;
            _cachedDocMtime = mtime;
            return doc;
        }
    }

    /// <summary>
    /// 从配置中加载安装目录 / Load the installation directory from config.
    /// </summary>
    public static string LoadInstallDir()
    {
        var doc = LoadDoc() ?? throw new InvalidOperationException($"config.toml not found at {ConfigPath()}");
        if (string.IsNullOrEmpty(doc.General.InstallDir))
        {
            throw new InvalidOperationException("install_dir not set in config.toml");
        }
        return doc.General.InstallDir;
    }

    /// <summary>
    /// 从配置中加载 GitHub 经典令牌 / Load GitHub classic token from config.
    /// </summary>
    public static string LoadGithubToken()
    {
        var doc = LoadDoc();
        if (doc?.Github?.TokenClassic is not { } token) return "";
        if (!token.StartsWith("ghp_")) return "";
        return token;
    }

    /// <summary>
    /// 从配置中加载 GitHub 代理地址 / Load GitHub proxy address from config.
    /// </summary>
    public static string LoadGithubProxy()
    {
        var proxy = LoadDoc()?.Github?.Proxy;
        return string.IsNullOrEmpty(proxy) ? "" : proxy;
    }

    /// <summary>
    /// 从配置中加载远程 pkgs.json 地址 / Load remote pkgs.json URL from config.
    /// </summary>
    public static string LoadPkgsRemoteUrl()
    {
        var url = LoadDoc()?.Pkgs?.RemoteUrl;
        return string.IsNullOrEmpty(url) ? "" : url;
    }

    /// <summary>
    /// 从配置中加载每个服务器的最大连接数 / Load max connections per server from config.
    /// </summary>
    public static int LoadMaxConnectionPerServer()
    {
        var v = LoadDoc()?.Download.MaxConnectionPerServer ?? 10;
        return Math.Clamp(v, 1, 16);
    }

    /// <summary>
    /// 从配置中加载下载分块数 / Load download split count from config.
    /// </summary>
    public static int LoadSplit()
    {
        var v = LoadDoc()?.Download.Split ?? 10;
        return Math.Clamp(v, 1, 16);
    }

    /// <summary>
    /// 从配置中加载最小分块大小（MB）/ Load minimum split size in MB from config.
    /// </summary>
    public static int LoadMinSplitSize()
    {
        var v = LoadDoc()?.Download.MinSplitSize ?? 1;
        return Math.Clamp(v, 1, 1024);
    }

    /// <summary>
    /// 从配置中加载是否启用 aria2 / Load aria2 enable flag from config.
    /// </summary>
    public static bool LoadAria2Enable() => LoadDoc()?.Download.Aria2Enable ?? true;

    /// <summary>
    /// 从配置中加载指定 OS/Arch 的 aria2 下载地址（含内置 fallback）/ Load aria2 download URL for given OS/arch from config (with built-in fallback).
    /// fallback 集中在 <see cref="SjtfAria2.GetUrl"/>；此处仅做委托。
    /// Fallback is centralized in <see cref="SjtfAria2.GetUrl"/>; this method just delegates.
    /// </summary>
    public static string? LoadAria2Url(string os, string arch)
    {
        return LoadDoc()?.Aria2.GetUrl(os, arch);
    }

    /// <summary>
    /// 从配置中加载 HTTP 请求 User-Agent，若未配置则使用默认值 / Load HTTP User-Agent from config, fallback to default if not set.
    /// </summary>
    public static string LoadUserAgent()
    {
        var path = ConfigPath();
        if (!File.Exists(path)) return DefaultUserAgent;

        var mtime = File.GetLastWriteTimeUtc(path).Ticks;

        lock (_cacheLock)
        {
            if (_cachedUserAgentMtime == mtime)
                return _cachedUserAgent ?? DefaultUserAgent;

            var ua = TryReadUserAgentFromToml(path);
            _cachedUserAgent = ua;
            _cachedUserAgentMtime = mtime;
            return ua ?? DefaultUserAgent;
        }
    }

    /// <summary>
    /// 从 config.toml 中解析 [http.request.header].user_agent；未找到返回 null。
    /// Parse [http.request.header].user_agent from config.toml; return null if not found.
    /// </summary>
    private static string? TryReadUserAgentFromToml(string path)
    {
        try
        {
            var toml = File.ReadAllText(path);
            var table = TomlSerializer.Deserialize(toml, TomlModelContext.Default.TomlTable);
            if (table == null) return null;

            if (table.TryGetValue("http", out var httpVal) && httpVal is TomlTable httpTable)
            {
                if (httpTable.TryGetValue("request", out var reqVal) && reqVal is TomlTable reqTable)
                {
                    if (reqTable.TryGetValue("header", out var headerVal) && headerVal is TomlTable headerTable)
                    {
                        if (headerTable.TryGetValue("user_agent", out var uaVal) && uaVal is string ua)
                            return ua;
                    }
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// 确保 shims 目录存在 / Ensure the shims directory exists.
    /// </summary>
    public static void EnsureSymlinkDir()
    {
        var installRoot = LoadInstallDir();
        var symlinkDir = Path.Combine(installRoot, "shims");
        Directory.CreateDirectory(symlinkDir);
    }

    /// <summary>
    /// 确保默认配置文件存在 / Ensure the default configuration file exists.
    /// 如果 config.toml 不存在，则使用默认值创建该文件。
    /// If config.toml does not exist, create it with default values.
    /// </summary>
    public static void EnsureDefault()
    {
        var path = ConfigPath();
        if (File.Exists(path)) return;

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var content = $@"[general]
install_dir = ""D:\\sjtf_pkgs""

[pkgs]
remote_url = ""https://cdn.jsdelivr.net/gh/LoveCSharp/sjtf@main/sjtf/pkgs.json""

[download]
aria2_enable = true
max_connection_per_server = 10  # 1 ~ 16
split = 10                      # 1 ~ 16
min_split_size = 1              # Chunk download size setting, unit: MB     1 ~ 1024

[aria2]
windows_x86_64 = ""{SjtfAria2.DefaultUrl}""

[github]
token_classic = ""put your classic token here""
proxy = ""https://gh-proxy.com""

[http.request.header]
user_agent = ""{DefaultUserAgent}""
";
        File.WriteAllText(path, content);
    }
}
