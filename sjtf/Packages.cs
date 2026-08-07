using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sjtf;

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
    /// <param name="ct">取消令牌 / Cancellation token.</param>
    public static async Task UpdateRemoteAsync(string remoteUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(remoteUrl))
            throw new InvalidOperationException("remote_url is not set in config.toml [pkgs]");

        var pkgsPath = Path.Combine(Tools.SjtfRoot(), "pkgs.json");
        Console.WriteLine($"pkgs: fetching {remoteUrl}");
        await Tools.DownloadFileAsync(remoteUrl, pkgsPath, "pkgs", ct);
        Console.WriteLine($"pkgs: updated {pkgsPath}");
    }
    /// <summary>
    /// 加载 pkgs.json 并返回根 JSON 对象 / Load pkgs.json and return the root JSON object.
    /// </summary>
    /// <returns>包定义 JSON 对象 / Package definition JSON object.</returns>
    public static JsonObject Load()
    {
        var path = Path.Combine(Tools.SjtfRoot(), "pkgs.json");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"pkgs.json not found at {path}");
        }
        var raw = File.ReadAllText(path);
        var node = JsonNode.Parse(raw) ?? throw new InvalidOperationException("pkgs.json is empty");
        if (node is not JsonObject obj)
        {
            throw new InvalidOperationException("pkgs.json root must be an object");
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
        if (!osObj.TryGetPropertyValue(arch, out var reNode) || reNode is not JsonValue reVal || reVal.GetValueKind() != JsonValueKind.String)
            throw new InvalidOperationException($"no entry for arch={arch}");
        return reVal.GetValue<string>();
    }
}