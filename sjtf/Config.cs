using System.Text.Json.Serialization;
using Tomlyn;
using Tomlyn.Serialization;

namespace Sjtf;

/// <summary>
/// HTTP 请求头配置 / HTTP request header configuration.
/// </summary>
public sealed class SjtfHttpHeaders
{
    /// <summary>
    /// HTTP 请求 User-Agent / HTTP request User-Agent.
    /// </summary>
    [TomlPropertyName("user_agent")]
    public string UserAgent { get; set; } = "";
}

/// <summary>
/// HTTP 相关配置 / HTTP related configuration.
/// </summary>
public sealed class SjtfHttp
{
    /// <summary>
    /// HTTP 请求头配置 / HTTP request header configuration.
    /// </summary>
    [TomlPropertyName("request.header")]
    public SjtfHttpHeaders RequestHeader { get; set; } = new();
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

    /// <summary>
    /// 下载失败时的最大重试次数 / Maximum retry count on download failure.
    /// </summary>
    [JsonPropertyName("download_retry_max")]
    public int DownloadRetryMax { get; set; } = 3;

    /// <summary>
    /// 是否创建符号链接 / Whether to create symbolic links.
    /// </summary>
    [JsonPropertyName("create_symlink")]
    public bool CreateSymlink { get; set; } = true;
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
    public string Proxy { get; set; } = "";
}

[TomlSerializable(typeof(SjtfConfig))]
[TomlSerializable(typeof(SjtfGeneral))]
[TomlSerializable(typeof(SjtfGithub))]
[TomlSerializable(typeof(SjtfHttp))]
[TomlSerializable(typeof(SjtfHttpHeaders))]
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
    private static string ConfigPath() => Path.Combine(Tools.SjtfRoot(), "config.toml");

    /// <summary>
    /// 加载并反序列化 config.toml / Load and deserialize config.toml.
    /// </summary>
    private static SjtfConfig? LoadDoc()
    {
        var path = ConfigPath();
        if (!File.Exists(path)) return null;
        return TomlSerializer.Deserialize(File.ReadAllText(path), SjtfConfigContext.Default.SjtfConfig);
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
    /// 从配置中加载下载最大重试次数 / Load download max retry count from config.
    /// </summary>
    public static int LoadDownloadRetryMax() => LoadDoc()?.General.DownloadRetryMax ?? 3;

    /// <summary>
    /// 从配置中加载 HTTP 请求 User-Agent，若未配置则使用默认值 / Load HTTP User-Agent from config, fallback to default if not set.
    /// </summary>
    public static string LoadUserAgent()
    {
        var doc = LoadDoc();
        return doc?.Http?.RequestHeader?.UserAgent ?? DefaultUserAgent;
    }

    /// <summary>
    /// 确保 symlink 目录存在 / Ensure the symlink directory exists.
    /// </summary>
    public static void EnsureSymlinkDir()
    {
        var installRoot = LoadInstallDir();
        var symlinkDir = Path.Combine(installRoot, "symlink");
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

        var content = @"[general]
install_dir = ""D:\\sjtf_pkgs""
download_retry_max = 3
create_symlink = true

[github]
token_classic = ""put your classic token here""
proxy = ""https://gh-proxy.com""

[http.request.header]
user_agent = ""Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36 Edg/151.0.0.0""
";
        File.WriteAllText(path, content);
    }

    /// <summary>
    /// 从配置中加载是否创建符号链接 / Load create-symlink setting from config.
    /// </summary>
    public static bool LoadCreateSymlink() => LoadDoc()?.General.CreateSymlink ?? true;
}
