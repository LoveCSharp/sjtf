using System.Text.Json.Serialization;
using Tomlyn;
using Tomlyn.Model;
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
    public string user_agent { get; set; } = "";
}

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
    /// 下载和校验失败时的总重试次数 / Total retry count for download and verification failures. Range: 2 ~ 10.
    /// </summary>
    [JsonPropertyName("retry")]
    public int Retry { get; set; } = 5;
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
}

[TomlSerializable(typeof(SjtfConfig))]
[TomlSerializable(typeof(SjtfGeneral))]
[TomlSerializable(typeof(SjtfGithub))]
[TomlSerializable(typeof(SjtfHttp))]
[TomlSerializable(typeof(SjtfHttpHeaders))]
[TomlSerializable(typeof(SjtfPkgs))]
[TomlSerializable(typeof(SjtfDownload))]
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
    /// 从配置中加载远程 pkgs.json 地址 / Load remote pkgs.json URL from config.
    /// </summary>
    public static string LoadPkgsRemoteUrl()
    {
        var url = LoadDoc()?.Pkgs?.RemoteUrl;
        return string.IsNullOrEmpty(url) ? "" : url;
    }

    /// <summary>
    /// 从配置中加载下载和校验的总重试次数 / Load total retry count for download and verification from config.
    /// </summary>
    public static int LoadDownloadRetryMax()
    {
        var v = LoadDoc()?.Download.Retry ?? 5;
        return Math.Clamp(v, 2, 10);
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
    /// 从配置中加载 HTTP 请求 User-Agent，若未配置则使用默认值 / Load HTTP User-Agent from config, fallback to default if not set.
    /// </summary>
    public static string LoadUserAgent()
    {
        var path = ConfigPath();
        if (!File.Exists(path)) return DefaultUserAgent;
        try
        {
            var toml = File.ReadAllText(path);
            var table = TomlSerializer.Deserialize(toml, TomlModelContext.Default.TomlTable);
            if (table == null) return DefaultUserAgent;

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
        return DefaultUserAgent;
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
create_symlink = true

[pkgs]
remote_url = ""https://cdn.jsdelivr.net/gh/LoveCSharp/sjtf@main/sjtf/pkgs.json""

[download]
max_connection_per_server = 10  # 1 ~ 16
split = 10                      # 1 ~ 16
min_split_size = 1              # Chunk download size setting, unit: MB     1 ~ 1024
retry = 5

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
