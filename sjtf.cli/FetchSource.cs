using System.Text.Json.Nodes;

namespace Sjtf.Cli;

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
    /// 解析包的下载计划和验证信息 / Resolve download plan and verification info for a package.
    /// </summary>
    /// <param name="pkg">包定义 JSON 对象 / Package definition JSON object.</param>
    /// <param name="packageName">包名称 / Package name.</param>
    /// <returns>下载计划 / Download plan.</returns>
    Task<DownloadPlan> ResolveAsync(JsonObject pkg, string packageName);
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
/// <param name="Type">包类型（portable-compressed-archive / portable-executable / installer），由 fetch 脚本从 fetch_asset.arch.{os}.{arch}.type 返回，决定安装方式并记入 installed.json。 / Package type returned by the fetch script; drives PlaceAsset and is recorded in installed.json.</param>
/// <param name="InstallProgram">installer 类型的安装程序。空字符串 = 按原值使用（可能为无效命令）；占位符 {DOWNLOADED_CACHE_FILE_FULL_PATH} → 替换为下载的 cache 文件路径；其他值（如自定义脚本路径）按原值使用。 / Installer executable. Empty string = used verbatim (may be an invalid command); placeholder {DOWNLOADED_CACHE_FILE_FULL_PATH} is replaced by PlaceAsset with the downloaded cache file path; other values (e.g. custom script) are used verbatim.</param>
/// <param name="InstallParams">installer 类型的安装参数（支持 {PKG_INSTALL_DIR} / {INSTALL_DIR} 占位符）。 / Installer arguments (supports {PKG_INSTALL_DIR} / {INSTALL_DIR} placeholders).</param>
/// <param name="UninstallProgram">installer 类型的卸载程序（绝对路径，已由 JS 用 installFull 替换 {PKG_INSTALL_DIR}）。 / Uninstaller executable; absolute path, with {PKG_INSTALL_DIR} already substituted by JS.</param>
/// <param name="UninstallParams">installer 类型的卸载参数（按原值使用）。 / Uninstaller arguments (used verbatim).</param>
public sealed record DownloadPlan(
    string Version,
    string DownloadUrl,
    string DigestAlgorithm,
    string ExpectedDigest,
    string Type,
    string InstallProgram = "",
    string InstallParams = "",
    string UninstallProgram = "",
    string UninstallParams = "");

internal static class FetchSources
{
    // 延迟初始化字典：首次访问时扫描<exe>/scripts/fetch/ 下所有
    // *_fetch_latest.js，按文件名剥离后缀作为 fetch_source 名称。
    // 用户只需将 xxx_fetch_latest.js 放入 scripts/fetch/，无需修改 C# 代码。
    //
    // Lazy-initialized registry: scans <exe>/scripts/fetch/*_fetch_latest.js
    // on first access, registering each file's name (sans suffix) as a
    // fetch_source. Users add custom sources by dropping a JS file in —
    // no C# changes required.
    private static Dictionary<string, IFetchSource>? _all;

    private static Dictionary<string, IFetchSource> All => _all ??= LoadFromDisk();

    private static Dictionary<string, IFetchSource> LoadFromDisk()
    {
        var result = new Dictionary<string, IFetchSource>(StringComparer.OrdinalIgnoreCase);
        var dir = Path.Combine(Paths.SjtfCliRoot(), "scripts", "fetch");
        if (!Directory.Exists(dir)) return result;

        const string suffix = "_fetch_latest.js";
        foreach (var file in Directory.EnumerateFiles(dir, "*" + suffix))
        {
            var name = Path.GetFileNameWithoutExtension(file); // 去掉 .js
            if (!name.EndsWith("_fetch_latest", StringComparison.OrdinalIgnoreCase))
                continue;
            var sourceName = name.Substring(0, name.Length - "_fetch_latest".Length);
            if (string.IsNullOrEmpty(sourceName))
                continue;
            result[sourceName] = new ScriptFetchSource(sourceName);
        }
        return result;
    }

    /// <summary>
    /// 按名称获取资源获取源 / Get a fetch source by name.
    /// </summary>
    /// <param name="name">源名称（如 "github"）/ Source name (e.g. "github").</param>
    /// <returns>资源获取源实现 / Fetch source implementation.</returns>
    public static IFetchSource Get(string name)
    {
        if (All.TryGetValue(name, out var src)) return src;
        throw new InvalidOperationException(
            $"unsupported fetch_source \"{name}\" " +
            $"(no <exe>/scripts/fetch/{name}_fetch_latest.js found; " +
            $"discovered: {string.Join(", ", All.Keys)})");
    }

    /// <summary>
    /// 列出已发现的源名称 / List discovered fetch source names.
    /// </summary>
    public static IReadOnlyCollection<string> KnownNames => All.Keys;
}
