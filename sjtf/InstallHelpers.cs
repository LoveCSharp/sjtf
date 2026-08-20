using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

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
    /// <returns>下载文件的本地路径 / Local path of the downloaded file.</returns>
    public static async Task<string> DownloadAndVerifyAsync(string name, DownloadPlan plan)
    {
        var maxConn = Config.LoadMaxConnectionPerServer();
        var splitCount = Config.LoadSplit();
        var minSplitMB = Config.LoadMinSplitSize();

        // 先用 HTTP 探测 Content-Disposition 头拿到建议文件名，再用其扩展名；
        // 探测失败或无扩展名时 fallback 到 URL 扩展名。
        // Peek Content-Disposition header to get the suggested filename; fall back
        // to URL extension if the header is absent or has no extension.
        var suggestedName = HttpFileDownloader.PeekFilename(plan.DownloadUrl);
        var ext = "";
        if (!string.IsNullOrEmpty(suggestedName))
            ext = ExtractExtensionFromFilename(suggestedName);
        if (string.IsNullOrEmpty(ext))
            ext = ExtractExtensionFromUrl(plan.DownloadUrl);
        var dlName = $"{name}-{Arch.CurrentOs()}-{Arch.CurrentArch()}-{plan.Version}{ext}";
        var dlPath = Path.Combine(Paths.CacheDir(), dlName);

        try
        {
            if (!File.Exists(dlPath))
            {
                await Downloader.DownloadFileAsync(plan.DownloadUrl, dlPath, $"{name}: downloading", maxConn, splitCount, minSplitMB);
            }

            Console.WriteLine($"{name}: verifying {plan.DigestAlgorithm} digest...");
            var actualDigest = await ComputeDigestAsync(dlPath, plan.DigestAlgorithm);
            if (string.Equals(actualDigest, plan.ExpectedDigest, StringComparison.OrdinalIgnoreCase))
            {
                return dlPath;
            }

            try { File.Delete(dlPath); } catch { }
            Console.WriteLine($"{name}: digest mismatch, re-downloading...");
            await Downloader.DownloadFileAsync(plan.DownloadUrl, dlPath, $"{name}: downloading", maxConn, splitCount, minSplitMB);

            actualDigest = await ComputeDigestAsync(dlPath, plan.DigestAlgorithm);
            if (string.Equals(actualDigest, plan.ExpectedDigest, StringComparison.OrdinalIgnoreCase))
            {
                return dlPath;
            }

            throw new InvalidOperationException($"{name}: {plan.DigestAlgorithm} digest mismatch after re-download (expected {plan.ExpectedDigest}, got {actualDigest})");
        }
        catch (Exception ex)
        {
            Tools.CleanupPartialDownload(dlPath);
            throw new InvalidOperationException($"{name}: download failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 从文件名（含路径或仅 basename）中提取扩展名。
    /// 处理 `.tar.gz` / `.tar.bz2` / `.tar.xz` / `.tar.zst` / `.tar.lz` / `.tar.lzma`
    /// 等 tar 复合扩展名（返回 `.tar.<ext>` 而非仅 `<ext>`）。
    /// 同时兼容大小写（`.tar.GZ` / `.tar.Bz2` 等）。
    ///
    /// Extract extension from a filename (with or without path).
    /// Recognises compound tar extensions such as `.tar.gz`, `.tar.bz2`, `.tar.xz`,
    /// `.tar.zst`, `.tar.lz`, `.tar.lzma` and similar (returns `.tar.<ext>` rather
    /// than just `<ext>`). Case-insensitive matching.
    /// </summary>
    public static string ExtractExtensionFromFilename(string filename)
    {
        if (string.IsNullOrEmpty(filename)) return "";

        var qs = filename.IndexOf('?');
        if (qs >= 0) filename = filename[..qs];
        var hash = filename.IndexOf('#');
        if (hash >= 0) filename = filename[..hash];

        var dot = filename.LastIndexOf('.');
        if (dot <= 0 || dot >= filename.Length - 1) return "";

        var ext = filename[dot..];

        var knownTarCompression = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".gz", ".bz2", ".xz", ".zst", ".lz", ".lzma",
            ".br", ".z", ".lzo", ".lrz", ".sz", ".lz4"
        };

        if (knownTarCompression.Contains(ext) && dot >= 4)
        {
            var prefix = filename[(dot - 4)..dot];
            if (prefix.Equals(".tar", StringComparison.OrdinalIgnoreCase))
                return filename[(dot - 4)..];
        }

        return ext;
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
        if (string.IsNullOrEmpty(url)) return "";

        var qs = url.IndexOf('?');
        if (qs >= 0) url = url[..qs];
        var hash = url.IndexOf('#');
        if (hash >= 0) url = url[..hash];

        var lastSlash = url.LastIndexOf('/');
        if (lastSlash < 0 || lastSlash == url.Length - 1) return "";
        var filename = url[(lastSlash + 1)..];

        return ExtractExtensionFromFilename(filename);
    }

    /// <summary>
    /// 以十六进制小写字符串形式计算文件的摘要值。
    /// 支持的算法：sha256、sha1、sha512、md5。
    ///
    /// Computes the requested digest of a file as a lowercase hex string.
    /// Supported algorithms: sha256, sha1, sha512, md5.
    /// </summary>
    public static async Task<string> ComputeDigestAsync(string path, string algorithm)
    {
        await using var stream = File.OpenRead(path);
        byte[] hash = algorithm.ToLowerInvariant() switch
        {
            "sha256" => await SHA256.HashDataAsync(stream),
            "sha1" => await SHA1.HashDataAsync(stream),
            "sha512" => await SHA512.HashDataAsync(stream),
            "md5" => await MD5.HashDataAsync(stream),
            _ => throw new InvalidOperationException($"unsupported digest algorithm \"{algorithm}\" (supported: sha256, sha1, sha512, md5)")
        };
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// 将包资源放置到安装目录 / Place package asset into the installation directory.
    /// </summary>
    /// <param name="name">包名称 / Package name.</param>
    /// <param name="pkg">包定义 JSON 对象 / Package definition JSON object.</param>
    /// <param name="plan">下载计划（提供 type 和 install_params）/ Download plan (supplies type and install_params).</param>
    /// <param name="dlPath">已下载的文件路径 / Downloaded file path.</param>
    /// <param name="installFull">完整安装目录 / Full installation directory.</param>
    public static void PlaceAsset(string name, JsonObject pkg, DownloadPlan plan, string dlPath, string installRoot, string installFull)
    {
        var pkgType = plan.Type;

        switch (pkgType)
        {
            case "portable-compressed-archive":
                ArchiveExtractor.ExpandArchiveRealRoot(dlPath, installFull, $"{name}: extracting");
                break;
            case "portable-executable":
                var target = Path.Combine(installFull, $"{name}.exe");
                Console.WriteLine($"{name}: placing exe at {target}...");
                File.Copy(dlPath, target, overwrite: true);
                break;
            case "installer":
                var installProgram = plan.InstallProgram;
                if (installProgram == "{DOWNLOADED_CACHE_FILE_FULL_PATH}")
                    installProgram = dlPath;

                var installParams = plan.InstallParams;
                installParams = installParams.Replace("{PKG_INSTALL_DIR}", installFull, StringComparison.OrdinalIgnoreCase)
                                            .Replace("{INSTALL_DIR}", installRoot, StringComparison.OrdinalIgnoreCase);
                Console.WriteLine($"{name}: running installer {installProgram} {installParams}");
                var psi = new System.Diagnostics.ProcessStartInfo(installProgram, installParams)
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
    public static void CreateShims(string name, JsonObject pkg, string installRoot, string installFull)
    {
        var symRoot = Path.Combine(installRoot, "shims");
        Directory.CreateDirectory(symRoot);

        var os = Arch.CurrentOs();

        if (!pkg.TryGetPropertyValue("shim", out var shimNode) || shimNode is not JsonObject shimObj)
            return;

        if (!shimObj.TryGetPropertyValue(os, out var osNode) || osNode is not JsonObject osObj)
            return;

        if (osObj.TryGetPropertyValue("symlink", out var linkNode) && linkNode is JsonObject linkObj)
        {
            foreach (var kv in linkObj)
            {
                var linkName = kv.Key;
                if (string.IsNullOrEmpty(linkName)) continue;
                var targetRel = kv.Value?.GetValue<string>() ?? "";
                if (string.IsNullOrEmpty(targetRel)) continue;
                var linkPath = Path.Combine(symRoot, linkName);
                var targetFull = Path.Combine(installFull, targetRel);
                Console.WriteLine($"{name}: shim {linkPath} -> {targetFull}");
                Tools.CreateSymlink(linkPath, targetFull);
            }
        }

        if (osObj.TryGetPropertyValue("shell_script", out var shellNode) && shellNode is JsonObject shellObj)
        {
            foreach (var kv in shellObj)
            {
                var scriptName = kv.Key;
                if (string.IsNullOrEmpty(scriptName)) continue;
                var scriptContent = kv.Value?.GetValue<string>() ?? "";
                if (string.IsNullOrEmpty(scriptContent)) continue;
                var scriptPath = Path.Combine(symRoot, scriptName);
                var replaced = scriptContent.Replace("{PKG_INSTALL_DIR}", installFull, StringComparison.OrdinalIgnoreCase)
                                            .Replace("{INSTALL_DIR}", installRoot, StringComparison.OrdinalIgnoreCase);
                Console.WriteLine($"{name}: shim {scriptPath}");
                File.WriteAllText(scriptPath, replaced, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }
    }

    /// <summary>
    /// 运行安装前 JS 脚本 / Run the before-install JavaScript script.
    /// </summary>
    /// <param name="name">包名称 / Package name.</param>
    /// <param name="pkg">包定义 JSON 对象 / Package definition JSON object.</param>
    /// <param name="installRoot">安装根目录 / Installation root directory.</param>
    /// <param name="installFull">完整安装目录 / Full installation directory.</param>
    public static async Task RunBeforeInstallScript(string name, JsonObject pkg, string installRoot, string installFull)
    {
        var os = Arch.CurrentOs();
        var arch = Arch.CurrentArch();
        var scriptPath = Path.Combine(Paths.SjtfRoot(), "scripts", "hooks", $"{name}-{os}-{arch}-before_install.js");
        if (!File.Exists(scriptPath))
            return;

        Console.WriteLine($"{name}: running before-install script");

        Directory.CreateDirectory(Path.Combine(installRoot, "shims"));

        var scriptSource = await File.ReadAllTextAsync(scriptPath);

        var engine = ScriptEngine.Create($"before_install/{name}");
        engine.SetValue("pkgJSON", pkg.ToJsonString());
        engine.SetValue("configJSON", ScriptConverters.LoadConfigJson());
        engine.SetValue("os", os);
        engine.SetValue("arch", arch);
        engine.SetValue("installDir", installFull);
        engine.SetValue("installRoot", installRoot);

        engine.Execute(scriptSource);
        await ScriptEngine.InvokeAsync(engine, "beforeInstall");
    }

    /// <summary>
    /// 在升级前运行 JS 钩子 / Run the before-upgrade JS hook.
    /// </summary>
    /// <param name="name">包名称 / Package name.</param>
    /// <param name="pkg">包定义 JSON 对象 / Package definition JSON object.</param>
    /// <param name="installRoot">安装根目录 / Installation root directory.</param>
    /// <param name="installFull">完整安装目录 / Full installation directory.</param>
    public static async Task RunBeforeUpgradeScript(string name, JsonObject pkg, string installRoot, string installFull)
    {
        var os = Arch.CurrentOs();
        var arch = Arch.CurrentArch();
        var scriptPath = Path.Combine(Paths.SjtfRoot(), "scripts", "hooks", $"{name}-{os}-{arch}-before_upgrade.js");

        if (!File.Exists(scriptPath))
            return;

        Console.WriteLine($"{name}: running before-upgrade script");
        Directory.CreateDirectory(Path.Combine(installRoot, "shims"));

        var scriptSource = await File.ReadAllTextAsync(scriptPath);
        var engine = ScriptEngine.Create($"before_upgrade/{name}");
        engine.SetValue("pkgJSON", pkg.ToJsonString());
        engine.SetValue("configJSON", ScriptConverters.LoadConfigJson());
        engine.SetValue("os", os);
        engine.SetValue("arch", arch);
        engine.SetValue("installDir", installFull);
        engine.SetValue("installRoot", installRoot);

        engine.Execute(scriptSource);
        await ScriptEngine.InvokeAsync(engine, "beforeUpgrade");
    }

    /// <summary>
    /// 运行安装后 JS 脚本 / Run the after-install JavaScript script.
    /// </summary>
    /// <param name="name">包名称 / Package name.</param>
    /// <param name="pkg">包定义 JSON 对象 / Package definition JSON object.</param>
    /// <param name="installRoot">安装根目录 / Installation root directory.</param>
    /// <param name="installFull">完整安装目录 / Full installation directory.</param>
    public static async Task RunAfterInstallScript(string name, JsonObject pkg, string installRoot, string installFull)
    {
        var os = Arch.CurrentOs();
        var arch = Arch.CurrentArch();
        var scriptPath = Path.Combine(Paths.SjtfRoot(), "scripts", "hooks", $"{name}-{os}-{arch}-after_install.js");
        if (!File.Exists(scriptPath))
            return;

        Console.WriteLine($"{name}: running after-install script");

        Directory.CreateDirectory(Path.Combine(installRoot, "shims"));

        var scriptSource = await File.ReadAllTextAsync(scriptPath);

        var engine = ScriptEngine.Create($"after_install/{name}");
        engine.SetValue("pkgJSON", pkg.ToJsonString());
        engine.SetValue("configJSON", ScriptConverters.LoadConfigJson());
        engine.SetValue("os", os);
        engine.SetValue("arch", arch);
        engine.SetValue("installDir", installFull);
        engine.SetValue("installRoot", installRoot);

        engine.Execute(scriptSource);
        await ScriptEngine.InvokeAsync(engine, "afterInstall");
    }

    /// <summary>
    /// 在升级完成后运行 JS 钩子 / Run the after-upgrade JS hook.
    /// </summary>
    /// <param name="name">包名称 / Package name.</param>
    /// <param name="pkg">包定义 JSON 对象 / Package definition JSON object.</param>
    /// <param name="installRoot">安装根目录 / Installation root directory.</param>
    /// <param name="installFull">完整安装目录 / Full installation directory.</param>
    public static async Task RunAfterUpgradeScript(string name, JsonObject pkg, string installRoot, string installFull)
    {
        var os = Arch.CurrentOs();
        var arch = Arch.CurrentArch();
        var scriptPath = Path.Combine(Paths.SjtfRoot(), "scripts", "hooks", $"{name}-{os}-{arch}-after_upgrade.js");

        if (!File.Exists(scriptPath))
            return;

        Console.WriteLine($"{name}: running after-upgrade script");
        Directory.CreateDirectory(Path.Combine(installRoot, "shims"));

        var scriptSource = await File.ReadAllTextAsync(scriptPath);
        var engine = ScriptEngine.Create($"after_upgrade/{name}");
        engine.SetValue("pkgJSON", pkg.ToJsonString());
        engine.SetValue("configJSON", ScriptConverters.LoadConfigJson());
        engine.SetValue("os", os);
        engine.SetValue("arch", arch);
        engine.SetValue("installDir", installFull);
        engine.SetValue("installRoot", installRoot);

        engine.Execute(scriptSource);
        await ScriptEngine.InvokeAsync(engine, "afterUpgrade");
    }

    /// <summary>
    /// 运行卸载后 JS 脚本 / Run the after-uninstall JavaScript script.
    /// </summary>
    /// <param name="name">包名称 / Package name.</param>
    /// <param name="pkg">包定义 JSON 对象 / Package definition JSON object.</param>
    /// <param name="installRoot">安装根目录 / Installation root directory.</param>
    /// <param name="installFull">完整安装目录 / Full installation directory.</param>
    public static async Task RunAfterUninstallScript(string name, JsonObject pkg, string installRoot, string installFull)
    {
        var os = Arch.CurrentOs();
        var arch = Arch.CurrentArch();
        var scriptPath = Path.Combine(Paths.SjtfRoot(), "scripts", "hooks", $"{name}-{os}-{arch}-after_uninstall.js");
        if (!File.Exists(scriptPath))
            return;

        Console.WriteLine($"{name}: running after-uninstall script");

        var scriptSource = await File.ReadAllTextAsync(scriptPath);

        var engine = ScriptEngine.Create($"after_uninstall/{name}");
        engine.SetValue("pkgJSON", pkg.ToJsonString());
        engine.SetValue("configJSON", ScriptConverters.LoadConfigJson());
        engine.SetValue("os", os);
        engine.SetValue("arch", arch);
        engine.SetValue("installDir", installFull);
        engine.SetValue("installRoot", installRoot);

        engine.Execute(scriptSource);
        await ScriptEngine.InvokeAsync(engine, "afterUninstall");
    }

    /// <summary>
    /// 在卸载前运行 JS 钩子 / Run the before-uninstall JS hook.
    /// </summary>
    /// <param name="name">包名称 / Package name.</param>
    /// <param name="pkg">包定义 JSON 对象 / Package definition JSON object.</param>
    /// <param name="installRoot">安装根目录 / Installation root directory.</param>
    /// <param name="installFull">完整安装目录 / Full installation directory.</param>
    public static async Task RunBeforeUninstallScript(string name, JsonObject pkg, string installRoot, string installFull)
    {
        var os = Arch.CurrentOs();
        var arch = Arch.CurrentArch();
        var scriptPath = Path.Combine(Paths.SjtfRoot(), "scripts", "hooks", $"{name}-{os}-{arch}-before_uninstall.js");

        if (!File.Exists(scriptPath))
            return;

        Console.WriteLine($"{name}: running before-uninstall script");

        var scriptSource = await File.ReadAllTextAsync(scriptPath);
        var engine = ScriptEngine.Create($"before_uninstall/{name}");
        engine.SetValue("pkgJSON", pkg.ToJsonString());
        engine.SetValue("configJSON", ScriptConverters.LoadConfigJson());
        engine.SetValue("os", os);
        engine.SetValue("arch", arch);
        engine.SetValue("installDir", installFull);
        engine.SetValue("installRoot", installRoot);

        engine.Execute(scriptSource);
        await ScriptEngine.InvokeAsync(engine, "beforeUninstall");
    }
}
