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
    /// Packages.Load() 的返回值：合并后的根 JsonObject + 自定义包来源分类。
    /// Returned by Packages.Load(): merged root JsonObject + classification of custom packages.
    /// </summary>
    /// <param name="Root">合并后的根 JsonObject（custom 同名覆盖 base）/ Merged root JsonObject (custom overrides base).</param>
    /// <param name="NewKeys">仅在 pkgs_custom.json 出现的包名集合（大小写不敏感）/ Names present ONLY in pkgs_custom.json (case-insensitive).</param>
    /// <param name="OverriddenKeys">pkgs.json 和 pkgs_custom.json 同时出现的包名集合（custom 覆盖 base）/ Names present in BOTH pkgs.json and pkgs_custom.json (custom overrides base).</param>
    public sealed record LoadedPackages(
        JsonObject Root,
        HashSet<string> NewKeys,
        HashSet<string> OverriddenKeys);

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
    /// <returns>合并后的包定义 JSON 对象 + 自定义包来源分类（NewKeys 仅新增 / OverriddenKeys 覆盖）/ Merged package definition JSON object plus custom-source classification (NewKeys = custom-only, OverriddenKeys = custom overrides base).</returns>
    public static LoadedPackages Load()
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

        var newKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var overriddenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var customPath = Path.Combine(Paths.DataDir(), "pkgs_custom.json");
        if (File.Exists(customPath))
        {
            var customDoc = LoadJsonObjectFile(customPath, "pkgs_custom.json");

            // custom 中的每个 key 完全覆盖 base 中同名 key。
            // DeepClone 避免 base 与 custom 共享 JsonNode 引用导致后续突变相互影响。
            // 分类：同名覆盖 → OverriddenKeys；仅 custom 新增 → NewKeys。
            foreach (var kvp in customDoc)
            {
                if (baseDoc.ContainsKey(kvp.Key))
                    overriddenKeys.Add(kvp.Key);
                else
                    newKeys.Add(kvp.Key);

                baseDoc[kvp.Key] = kvp.Value?.DeepClone();
            }
        }

        return new LoadedPackages(baseDoc, newKeys, overriddenKeys);
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