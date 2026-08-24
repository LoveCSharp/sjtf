using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sjtf.Cli;

/// <summary>
/// 包定义加载与解析 / Package definition loading and resolution.
/// 负责读取 pkgs.json 并解析架构相关的资源正则表达式。
/// Responsible for reading pkgs.json and resolving architecture-specific asset regex patterns.
/// </summary>
internal static class Packages
{
    /// <summary>
    /// 从远程 URL 下载 pkgs.json 并覆盖本地文件 / Download pkgs.json from remote URL and overwrite local file.
    /// </summary>
    /// <param name="remoteUrl">远程 pkgs.json URL / Remote pkgs.json URL.</param>
    public static async Task UpdateRemoteAsync(string remoteUrl)
    {
        if (string.IsNullOrEmpty(remoteUrl))
            throw new InvalidOperationException("remote_url is not set in config.toml [pkgs]");

        var pkgsPath = Path.Combine(Paths.DataDir(), "pkgs.json");
        Console.WriteLine($"pkgs: fetching {remoteUrl}");
        await Downloader.DownloadFileAsync(remoteUrl, pkgsPath, "pkgs",
            Config.LoadMaxConnectionPerServer(), Config.LoadSplit(), Config.LoadMinSplitSize());
        Console.WriteLine($"pkgs: updated {pkgsPath}");
    }
    /// <summary>
    /// 加载 pkgs.json（必要时从远程下载）并与 pkgs_custom.json 合并，返回根 JSON 对象 / Load pkgs.json
    /// (downloading from remote if missing) and merge with pkgs_custom.json, return the root JSON object.
    /// 合并规则 / Merge rules:
    ///   - pkgs_custom.json 不存在则静默跳过 / silently skip if pkgs_custom.json missing
    ///   - 同名 key 由 pkgs_custom.json 完全覆盖 pkgs.json / same-name keys fully overridden by pkgs_custom.json
    ///   - 仅在内存合并，不写回任何文件 / in-memory merge only, no files written
    /// 如果本地 pkgs.json 不存在且配置了 remote_url，则自动从远程下载。
    /// If pkgs.json does not exist locally and remote_url is configured, automatically download it from remote.
    /// </summary>
    /// <returns>合并后的包定义 JSON 对象 / Merged package definition JSON object.</returns>
    public static JsonObject Load()
    {
        var basePath = Path.Combine(Paths.DataDir(), "pkgs.json");
        if (!File.Exists(basePath))
        {
            var remoteUrl = Config.LoadPkgsRemoteUrl();
            if (!string.IsNullOrEmpty(remoteUrl))
            {
                UpdateRemoteAsync(remoteUrl).GetAwaiter().GetResult();
            }
            else
            {
                throw new InvalidOperationException($"pkgs.json not found at {basePath}");
            }
        }

        var baseDoc = LoadJsonObjectFile(basePath, "pkgs.json");

        var customPath = Path.Combine(Paths.DataDir(), "pkgs_custom.json");
        if (File.Exists(customPath))
        {
            var customDoc = LoadJsonObjectFile(customPath, "pkgs_custom.json");

            // custom 中的每个 key 完全覆盖 base 中同名 key。
            // DeepClone 避免 base 与 custom 共享 JsonNode 引用导致后续突变相互影响。
            foreach (var kvp in customDoc)
            {
                baseDoc[kvp.Key] = kvp.Value?.DeepClone();
            }
        }

        return baseDoc;
    }

    /// <summary>
    /// 读取指定路径的 JSON 文件并验证根为对象 / Load a JSON file from path and verify its root is an object.
    /// </summary>
    private static JsonObject LoadJsonObjectFile(string path, string displayName)
    {
        var raw = File.ReadAllText(path);
        var node = JsonNode.Parse(raw) ?? throw new InvalidOperationException($"{displayName} is empty");
        if (node is not JsonObject obj)
        {
            throw new InvalidOperationException($"{displayName} root must be an object");
        }
        return obj;
    }

    /// <summary>
    /// 解析指定包的 fetch_asset.arch 正则表达式 / Resolve the fetch_asset.arch regex for a given package.
    /// </summary>
    /// <param name="pkg">包定义 JSON 对象 / Package definition JSON object.</param>
    /// <returns>架构匹配正则表达式字符串 / Architecture matching regex string.</returns>
    public static string ResolveAssetRe(JsonObject pkg)
    {
        var os = Arch.CurrentOs();
        var arch = Arch.CurrentArch();

        if (!pkg.TryGetPropertyValue("fetch_asset", out var fetchNode) || fetchNode is not JsonObject fetch)
            throw new InvalidOperationException("fetch_asset missing");
        if (!fetch.TryGetPropertyValue("arch", out var archNode) || archNode is not JsonObject archObj)
            throw new InvalidOperationException("fetch_asset.arch missing");

        if (!archObj.TryGetPropertyValue(os, out var osNode) || osNode is not JsonObject osObj)
            throw new InvalidOperationException($"no entry for os={os}");
        if (!osObj.TryGetPropertyValue(arch, out var reNode) || reNode is not JsonObject reObj)
            throw new InvalidOperationException($"no entry for arch={arch}");
        if (!reObj.TryGetPropertyValue("file", out var fileNode) || fileNode is not JsonValue fileVal || fileVal.GetValueKind() != JsonValueKind.String)
            throw new InvalidOperationException($"no \"file\" entry for arch={arch}");
        return fileVal.GetValue<string>();
    }
}