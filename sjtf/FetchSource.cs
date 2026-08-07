using System.Text.Json.Nodes;

namespace Sjtf;

/// <summary>
/// 解析给定包"从哪里下载以及要验证什么"。
/// 实现由包的 <c>fetch_source</c> 字段选择（例如 "github"）。
/// 结果提供给统一的安装流程（如已是最新则跳过，下载+验证+重试，解压...）。
///
/// Resolves "where to download a package from and what to verify it against"
/// for a given package. Implementations are selected by the package's
/// <c>fetch_source</c> field (e.g. "github"). The result feeds the unified
/// install pipeline (skip-if-up-to-date, download+verify+retry, extract, ...).
/// </summary>
public interface IFetchSource
{
    string Name { get; }

    /// <summary>
    /// 获取包定义中的 fetch_source 名称 / Get the fetch_source name from a package definition.
    /// </summary>
    /// <param name="pkg">包定义 JSON 对象 / Package definition JSON object.</param>
    /// <param name="ct">取消令牌 / Cancellation token.</param>
    /// <returns>下载计划 / Download plan.</returns>
    Task<DownloadPlan> ResolveAsync(JsonObject pkg, CancellationToken ct = default);
}

/// <summary>
/// 安装流程获取和验证包资源所需的一切。
///
/// Everything the install pipeline needs to obtain and verify a package asset.
/// </summary>
/// <param name="Version">上游版本字符串（例如 GitHub tag_name）。与 installed.json 比较以决定是否跳过。 / Upstream version string (e.g. GitHub tag_name).</param>
/// <param name="DownloadUrl">流程下载的完整URL。 / Fully-qualified URL the pipeline downloads from.</param>
/// <param name="DigestAlgorithm">用于验证下载文件的摘要算法。 / Digest algorithm for verification.</param>
/// <param name="ExpectedDigest">流程验证下载文件的十六进制摘要。 / Expected hex digest for verification.</param>
public sealed record DownloadPlan(
    string Version,
    string DownloadUrl,
    string DigestAlgorithm,
    string ExpectedDigest);

internal static class FetchSources
{
    private static readonly Dictionary<string, IFetchSource> _all = new()
    {
        ["github"] = new LuaFetchSource("github"),
        ["update_code_visualstudio_com"] = new LuaFetchSource("update_code_visualstudio_com"),
    };

    /// <summary>
    /// 按名称获取资源获取源 / Get a fetch source by name.
    /// </summary>
    /// <param name="name">源名称（如 "github"）/ Source name (e.g. "github").</param>
    /// <returns>资源获取源实现 / Fetch source implementation.</returns>
    public static IFetchSource Get(string name)
    {
        if (_all.TryGetValue(name, out var src)) return src;
        throw new InvalidOperationException(
            $"unsupported fetch_source \"{name}\" (supported: {string.Join(", ", _all.Keys)})");
    }
}
