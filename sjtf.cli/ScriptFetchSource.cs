using System.Text.Json.Nodes;
using Jint;

namespace Sjtf.Cli;

/// <summary>
/// 基于 JS 脚本的资源获取源实现 / JavaScript-script-based fetch source implementation.
/// 通过执行 JS 脚本从指定源（如 GitHub）解析最新版本、下载 URL 和摘要。
/// Executes JS scripts to resolve latest version, download URL, and digest from a given source (e.g. GitHub).
/// </summary>
internal sealed class ScriptFetchSource : IFetchSource
{
    private readonly string _sourceName;

    public ScriptFetchSource(string sourceName)
    {
        _sourceName = sourceName;
    }

    /// <summary>
    /// 获取源名称 / Get the source name.
    /// </summary>
    public string Name => _sourceName;

    /// <summary>
    /// 异步解析包的下载计划和验证信息 / Asynchronously resolve download plan and verification info for a package.
    /// </summary>
    /// <param name="pkg">包定义 JSON 对象 / Package definition JSON object.</param>
    /// <param name="packageName">包名称 / Package name.</param>
    /// <returns>下载计划 / Download plan.</returns>
    public async Task<DownloadPlan> ResolveAsync(JsonObject pkg, string packageName)
    {
        var scriptPath = Path.Combine(Paths.SjtfCliRoot(), "scripts", "fetch", $"{_sourceName}_fetch_latest.js");
        if (!File.Exists(scriptPath))
            throw new InvalidOperationException($"script not found at {scriptPath}");

        var scriptSource = await File.ReadAllTextAsync(scriptPath);

        var engine = ScriptEngine.Create(_sourceName);

        engine.SetValue("pkgJSON", pkg.ToJsonString());
        engine.SetValue("configJSON", ScriptConverters.LoadConfigJson());
        engine.SetValue("os", Arch.CurrentOs());
        engine.SetValue("arch", Arch.CurrentArch());

        // 新增：用于 fetch 脚本中替换 {PKG_INSTALL_DIR}
        var installDirRel = pkg["pkg_install_relative_dir"]?.GetValue<string>()
            ?? throw new InvalidOperationException($"{packageName}: pkg_install_relative_dir missing");
        var installRoot = Config.LoadInstallDir();
        var installFull = Path.Combine(installRoot, installDirRel);
        engine.SetValue("installRoot", installRoot);
        engine.SetValue("installFull", installFull);

        engine.Execute(scriptSource);

        var result = await ScriptEngine.InvokeAsync(engine, "fetch");
        if (result == null || result.IsNull() || result.IsUndefined())
            throw new InvalidOperationException("script did not return a result");

        if (!result.IsString())
            throw new InvalidOperationException("script result is not a JSON string (must return JSON.stringify(...))");

        var resultJson = result.AsString();
        if (string.IsNullOrEmpty(resultJson))
            throw new InvalidOperationException("script returned an empty string");

        var parsed = JsonNode.Parse(resultJson) as JsonObject
            ?? throw new InvalidOperationException("script result is not a JSON object");

        return new DownloadPlan(
            Version: parsed["version"]?.GetValue<string>()
                ?? throw new InvalidOperationException("script result missing version"),
            DownloadUrl: parsed["url"]?.GetValue<string>()
                ?? throw new InvalidOperationException("script result missing url"),
            DigestAlgorithm: parsed["digest_algorithm"]?.GetValue<string>() ?? "sha256",
            ExpectedDigest: parsed["digest"]?.GetValue<string>() ?? "",
            Type: parsed["type"]?.GetValue<string>()
                ?? throw new InvalidOperationException("script result missing type"),
            InstallProgram: parsed["install_program"]?.GetValue<string>() ?? "",
            InstallParams: parsed["install_params"]?.GetValue<string>() ?? "",
            UninstallProgram: parsed["uninstall_program"]?.GetValue<string>() ?? "",
            UninstallParams: parsed["uninstall_params"]?.GetValue<string>() ?? "");
    }
}
