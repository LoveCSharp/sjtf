using System.Text.Json;
using System.Text.Json.Nodes;
using NLua;

namespace Sjtf;

/// <summary>
/// 基于 Lua 脚本的资源获取源实现 / Lua-script-based fetch source implementation.
/// 通过执行 Lua 脚本从指定源（如 GitHub）解析最新版本、下载 URL 和摘要。
/// Executes Lua scripts to resolve latest version, download URL, and digest from a given source (e.g. GitHub).
/// </summary>
internal sealed class LuaFetchSource : IFetchSource
{
    private readonly string _sourceName;

    public LuaFetchSource(string sourceName)
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
    /// <returns>下载计划 / Download plan.</returns>
    public async Task<DownloadPlan> ResolveAsync(JsonObject pkg, string packageName)
    {
        using var lua = new Lua();

        var bindings = new LuaBindings(lua) { PackageName = packageName };
        lua.RegisterFunction("http_get", bindings, typeof(LuaBindings).GetMethod(nameof(LuaBindings.HttpGet)));
        lua.RegisterFunction("json_decode", bindings, typeof(LuaBindings).GetMethod(nameof(LuaBindings.JsonDecode)));
        lua.RegisterFunction("regex_match", bindings, typeof(LuaBindings).GetMethod(nameof(LuaBindings.RegexMatch)));

        lua.DoString("pkg = " + LuaConverters.JsonObjectToLua(pkg));
        lua.DoString("config = " + LuaConverters.LoadConfigLua());
        lua["os"] = Arch.CurrentOs();
        lua["arch"] = Arch.CurrentArch();

        var scriptPath = Path.Combine(Paths.SjtfRoot(), "scripts", $"{_sourceName}_fetch_latest.lua");
        if (!File.Exists(scriptPath))
            throw new InvalidOperationException($"lua script not found at {scriptPath}");

        var scriptSource = await File.ReadAllTextAsync(scriptPath);
        lua.DoString(scriptSource);

        var result = lua.GetTable("result") as LuaTable;
        if (result == null)
            throw new InvalidOperationException("lua script did not set result");

        var version = result["version"] as string
            ?? throw new InvalidOperationException("lua result missing version");
        var url = result["url"] as string
            ?? throw new InvalidOperationException("lua result missing url");
        var digest = result["digest"] as string ?? "";
        var digestAlgorithm = result["digest_algorithm"] as string ?? "sha256";

        return new DownloadPlan(
            Version: version,
            DownloadUrl: url,
            DigestAlgorithm: digestAlgorithm,
            ExpectedDigest: digest);
    }
}
