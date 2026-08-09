using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using NLua;

namespace Sjtf;

internal static class InstallHelpers
{
    /// <summary>
    /// 从 JSON 对象中读取必需的字符串值 / Read a required string value from a JSON object.
    /// </summary>
    /// <param name="obj">JSON 对象 / JSON object.</param>
    /// <param name="key">属性键 / Property key.</param>
    /// <param name="contextName">错误上下文名称 / Error context name.</param>
    /// <returns>字符串值 / String value.</returns>
    public static string ReadRequiredString(JsonObject obj, string key, string contextName = "")
    {
        if (!obj.TryGetPropertyValue(key, out var node) || node is not JsonValue val || val.GetValueKind() != JsonValueKind.String)
        {
            var prefix = string.IsNullOrEmpty(contextName) ? "" : $"{contextName}: ";
            throw new InvalidOperationException($"{prefix}{key} missing");
        }
        return val.GetValue<string>();
    }

    /// <summary>
    /// 下载包资产并验证摘要 / Download package asset and verify digest.
    /// </summary>
    /// <param name="name">包名称 / Package name.</param>
    /// <param name="plan">下载计划 / Download plan.</param>
    /// <param name="ct">取消令牌 / Cancellation token.</param>
    /// <returns>下载文件的本地路径 / Local path of the downloaded file.</returns>
    public static async Task<string> DownloadAndVerifyAsync(string name, DownloadPlan plan, CancellationToken ct = default)
    {
        var maxConn = Config.LoadMaxConnectionPerServer();
        var splitCount = Config.LoadSplit();
        var minSplitMB = Config.LoadMinSplitSize();

        var ext = ExtractExtensionFromUrl(plan.DownloadUrl);
        var dlName = $"{name}-{Arch.CurrentOs()}-{Arch.CurrentArch()}-{plan.Version}{ext}";
        var dlPath = Path.Combine(Tools.CacheDir(), dlName);

        try
        {
            if (!File.Exists(dlPath))
            {
                await Tools.DownloadFileAsync(plan.DownloadUrl, dlPath, $"{name}: downloading", maxConn, splitCount, minSplitMB);
            }

            Console.WriteLine($"{name}: verifying {plan.DigestAlgorithm} digest...");
            var actualDigest = await ComputeDigestAsync(dlPath, plan.DigestAlgorithm, ct);
            if (string.Equals(actualDigest, plan.ExpectedDigest, StringComparison.OrdinalIgnoreCase))
            {
                return dlPath;
            }

            throw new InvalidOperationException($"{name}: {plan.DigestAlgorithm} digest mismatch (expected {plan.ExpectedDigest}, got {actualDigest})");
        }
        catch (OperationCanceledException)
        {
            Tools.CleanupPartialDownload(dlPath);
            throw;
        }
        catch (Exception ex)
        {
            Tools.CleanupPartialDownload(dlPath);
            throw new InvalidOperationException($"{name}: download failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 从 URL 的最后路径段中提取文件扩展名（包括前导点）。
    /// 先剥离查询字符串和片段。当 URL 没有扩展名或路径组件时返回空字符串。
    ///
    /// Extracts the file extension (including the leading dot) from a URL's
    /// last path segment. Strips query string and fragment first. Returns
    /// empty string when the URL has no extension or no path component.
    /// </summary>
    public static string ExtractExtensionFromUrl(string url)
    {
        var qs = url.IndexOf('?');
        if (qs >= 0) url = url[..qs];
        var hash = url.IndexOf('#');
        if (hash >= 0) url = url[..hash];

        var lastSlash = url.LastIndexOf('/');
        if (lastSlash < 0 || lastSlash == url.Length - 1) return "";
        var filename = url[(lastSlash + 1)..];

        var dot = filename.LastIndexOf('.');
        if (dot <= 0) return "";
        return filename[dot..];
    }

    /// <summary>
    /// 以十六进制小写字符串形式计算文件的摘要值。
    /// 支持的算法：sha256、sha1、sha512、md5。
    ///
    /// Computes the requested digest of a file as a lowercase hex string.
    /// Supported algorithms: sha256, sha1, sha512, md5.
    /// </summary>
    public static async Task<string> ComputeDigestAsync(string path, string algorithm, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        byte[] hash = algorithm.ToLowerInvariant() switch
        {
            "sha256" => await SHA256.HashDataAsync(stream, ct),
            "sha1" => await SHA1.HashDataAsync(stream, ct),
            "sha512" => await SHA512.HashDataAsync(stream, ct),
            "md5" => await MD5.HashDataAsync(stream, ct),
            _ => throw new InvalidOperationException($"unsupported digest algorithm \"{algorithm}\" (supported: sha256, sha1, sha512, md5)")
        };
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// 将包资源放置到安装目录 / Place package asset into the installation directory.
    /// </summary>
    /// <param name="name">包名称 / Package name.</param>
    /// <param name="pkg">包定义 JSON 对象 / Package definition JSON object.</param>
    /// <param name="dlPath">已下载的文件路径 / Downloaded file path.</param>
    /// <param name="installFull">完整安装目录 / Full installation directory.</param>
    public static void PlaceAsset(string name, JsonObject pkg, string dlPath, string installRoot, string installFull)
    {
        if (!pkg.TryGetPropertyValue("fetch_asset", out var fetchNode) || fetchNode is not JsonObject fetch)
            throw new InvalidOperationException($"{name}: fetch_asset missing");
        if (!fetch.TryGetPropertyValue("type", out var typeNode) || typeNode is not JsonValue typeVal || typeVal.GetValueKind() != JsonValueKind.String)
            throw new InvalidOperationException($"{name}: fetch_asset.type missing");
        var pkgType = typeVal.GetValue<string>();

        switch (pkgType)
        {
            case "portable-compressed-archive":
                Tools.ExpandArchiveRealRoot(dlPath, installFull, $"{name}: extracting");
                break;
            case "portable-exe":
                var target = Path.Combine(installFull, $"{name}.exe");
                Console.WriteLine($"{name}: placing exe at {target}...");
                File.Copy(dlPath, target, overwrite: true);
                break;
            case "installer":
                var installParams = "";
                if (fetch.TryGetPropertyValue("install_params", out var paramsNode) && paramsNode is JsonValue paramsVal && paramsVal.GetValueKind() == JsonValueKind.String)
                    installParams = paramsVal.GetValue<string>();
                installParams = installParams.Replace("{PKG_INSTALL_DIR}", installFull, StringComparison.OrdinalIgnoreCase)
                                            .Replace("{INSTALL_DIR}", installRoot, StringComparison.OrdinalIgnoreCase);
                Console.WriteLine($"{name}: running installer {dlPath} {installParams}");
                var psi = new System.Diagnostics.ProcessStartInfo(dlPath, installParams)
                {
                    UseShellExecute = false
                };
                var proc = System.Diagnostics.Process.Start(psi);
                proc?.WaitForExit();
                if (proc?.ExitCode != 0)
                    throw new InvalidOperationException($"{name}: installer exited with code {proc?.ExitCode}");
                break;
            default:
                throw new InvalidOperationException($"{name}: unsupported type \"{pkgType}\"");
        }
    }

    /// <summary>
    /// 为包创建符号链接 / Create symbolic links for a package.
    /// </summary>
    /// <param name="name">包名称 / Package name.</param>
    /// <param name="pkg">包定义 JSON 对象 / Package definition JSON object.</param>
    /// <param name="installRoot">安装根目录 / Installation root directory.</param>
    /// <param name="installFull">完整安装目录 / Full installation directory.</param>
    public static void CreateSymlinks(string name, JsonObject pkg, string installRoot, string installFull)
    {
        var symRoot = Path.Combine(installRoot, "shims");
        Directory.CreateDirectory(symRoot);

        var os = Arch.CurrentOs();

        if (!pkg.TryGetPropertyValue("shim", out var shimNode) || shimNode is not JsonObject shimObj)
            return;

        if (!shimObj.TryGetPropertyValue(os, out var osNode) || osNode is not JsonObject osObj)
            return;

        if (osObj.TryGetPropertyValue("symlink", out var symlinkNode) && symlinkNode is JsonArray symlinkArr)
        {
            foreach (var item in symlinkArr)
            {
                if (item is not JsonValue val || val.GetValueKind() != JsonValueKind.String) continue;
                var targetRel = val.GetValue<string>() ?? "";
                if (string.IsNullOrEmpty(targetRel)) continue;
                var linkName = Path.GetFileName(targetRel);
                if (string.IsNullOrEmpty(linkName)) continue;
                var linkPath = Path.Combine(symRoot, linkName);
                var targetFull = Path.Combine(installFull, targetRel);
                Console.WriteLine($"{name}: shim {linkPath} -> {targetFull}");
                Tools.CreateSymlink(linkPath, targetFull);
            }
        }

        if (osObj.TryGetPropertyValue("cmd", out var cmdNode) && cmdNode is JsonObject cmdObj)
        {
            foreach (var kv in cmdObj)
            {
                var cmdName = kv.Key;
                if (string.IsNullOrEmpty(cmdName)) continue;
                var cmdContent = kv.Value?.GetValue<string>() ?? "";
                if (string.IsNullOrEmpty(cmdContent)) continue;
                var cmdPath = Path.Combine(symRoot, cmdName);
                var replaced = cmdContent.Replace("{PKG_INSTALL_DIR}", installFull, StringComparison.OrdinalIgnoreCase)
                                        .Replace("{INSTALL_DIR}", installRoot, StringComparison.OrdinalIgnoreCase);
                Console.WriteLine($"{name}: shim {cmdPath}");
                File.WriteAllText(cmdPath, replaced, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }
    }

    /// <summary>
    /// 运行安装后 Lua 脚本 / Run the after-install Lua script.
    /// </summary>
    /// <param name="name">包名称 / Package name.</param>
    /// <param name="pkg">包定义 JSON 对象 / Package definition JSON object.</param>
    /// <param name="installRoot">安装根目录 / Installation root directory.</param>
    /// <param name="installFull">完整安装目录 / Full installation directory.</param>
    public static void RunAfterInstallScript(string name, JsonObject pkg, string installRoot, string installFull)
    {
        if (!pkg.TryGetPropertyValue("script_after_install", out var scriptNode) || scriptNode is not JsonValue scriptVal || scriptVal.GetValueKind() != JsonValueKind.True)
            return;

        var os = Arch.CurrentOs();
        var arch = Arch.CurrentArch();
        var scriptPath = Path.Combine(Tools.SjtfRoot(), "scripts", "after_install", os, arch, $"{name}.lua");
        if (!File.Exists(scriptPath))
        {
            Console.Error.WriteLine($"{name}: after-install script not found at {scriptPath}");
            return;
        }

        Console.WriteLine($"{name}: running after-install script");

        Directory.CreateDirectory(Path.Combine(installRoot, "shims"));

        using var lua = new Lua();

        var bindings = new LuaBindings(lua);
        lua.RegisterFunction("http_get", bindings, typeof(LuaBindings).GetMethod(nameof(LuaBindings.HttpGet)));
        lua.RegisterFunction("json_decode", bindings, typeof(LuaBindings).GetMethod(nameof(LuaBindings.JsonDecode)));
        lua.RegisterFunction("regex_match", bindings, typeof(LuaBindings).GetMethod(nameof(LuaBindings.RegexMatch)));
        lua.RegisterFunction("create_directory", bindings, typeof(LuaBindings).GetMethod(nameof(LuaBindings.CreateDirectory)));

        lua.DoString("pkg = " + LuaConverters.JsonObjectToLua(pkg));
        lua.DoString("config = " + LuaConverters.LoadConfigLua());
        lua["os"] = os;
        lua["arch"] = arch;
        lua["install_dir"] = installFull;
        lua["install_root"] = installRoot;

        var scriptSource = File.ReadAllText(scriptPath);
        lua.DoString(scriptSource);
    }

    /// <summary>
    /// 运行卸载后 Lua 脚本 / Run the after-uninstall Lua script.
    /// </summary>
    /// <param name="name">包名称 / Package name.</param>
    /// <param name="pkg">包定义 JSON 对象 / Package definition JSON object.</param>
    /// <param name="installRoot">安装根目录 / Installation root directory.</param>
    /// <param name="installFull">完整安装目录 / Full installation directory.</param>
    public static void RunAfterUninstallScript(string name, JsonObject pkg, string installRoot, string installFull)
    {
        if (!pkg.TryGetPropertyValue("script_after_uninstall", out var scriptNode) || scriptNode is not JsonValue scriptVal || scriptVal.GetValueKind() != JsonValueKind.True)
            return;

        var os = Arch.CurrentOs();
        var arch = Arch.CurrentArch();
        var scriptPath = Path.Combine(Tools.SjtfRoot(), "scripts", "after_uninstall", os, arch, $"{name}.lua");
        if (!File.Exists(scriptPath))
        {
            Console.Error.WriteLine($"{name}: after-uninstall script not found at {scriptPath}");
            return;
        }

        Console.WriteLine($"{name}: running after-uninstall script");

        using var lua = new Lua();

        var bindings = new LuaBindings(lua);
        lua.RegisterFunction("http_get", bindings, typeof(LuaBindings).GetMethod(nameof(LuaBindings.HttpGet)));
        lua.RegisterFunction("json_decode", bindings, typeof(LuaBindings).GetMethod(nameof(LuaBindings.JsonDecode)));
        lua.RegisterFunction("regex_match", bindings, typeof(LuaBindings).GetMethod(nameof(LuaBindings.RegexMatch)));
        lua.RegisterFunction("remove_file", bindings, typeof(LuaBindings).GetMethod(nameof(LuaBindings.RemoveFile)));

        lua.DoString("pkg = " + LuaConverters.JsonObjectToLua(pkg));
        lua.DoString("config = " + LuaConverters.LoadConfigLua());
        lua["os"] = os;
        lua["arch"] = arch;
        lua["install_dir"] = installFull;
        lua["install_root"] = installRoot;

        var scriptSource = File.ReadAllText(scriptPath);
        lua.DoString(scriptSource);
    }
}
