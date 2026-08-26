using System.CommandLine;
using Sjtf.Cli;
using Spectre.Console;
using System.Text.Json;
using System.Text.Json.Nodes;

var configCreated = Config.EnsureDefault();
Config.EnsureSymlinkDir();

if (configCreated)
{
    Console.Error.WriteLine("config.toml was generated for the first time. Please review and adjust settings (e.g. install_dir) before running sjtf again.");
    return 1;
}

var rootCommand = new RootCommand("sjtf - command-line skeleton tool.");

rootCommand.SetAction(_ =>
{
    Console.WriteLine("sjtf - run with --help to see available options.");
    return 0;
});

var packagesCommand = new Command("packages", "Manage package definitions.")
{
    Aliases = { "pkgs" }
};
packagesCommand.SetAction(_ =>
{
    Console.WriteLine("Usage: sjtf packages <list|update>");
    return 0;
});

var pkgListCommand = new Command("list", "List packages defined in pkgs.json.")
{
    Aliases = { "ls" }
};
pkgListCommand.SetAction(_ => ListPackages());
packagesCommand.Subcommands.Add(pkgListCommand);

var pkgUpdateCommand = new Command("update", "Update pkgs.json from remote source.")
{
    Aliases = { "up" }
};
pkgUpdateCommand.SetAction(async _ =>
{
    try
    {
        var remoteUrl = Config.LoadPkgsRemoteUrl();
        if (string.IsNullOrEmpty(remoteUrl))
        {
            Console.Error.WriteLine("error: remote_url is not set in config.toml [pkgs]");
            return 1;
        }

        await Packages.UpdateRemoteAsync(remoteUrl);
        return 0;
    }
    catch (Exception ex)
    {
        var root = Tools.GetInnermostException(ex);
        Console.Error.WriteLine($"error: pkgs update failed: {root.Message}");
        return 1;
    }
});
packagesCommand.Subcommands.Add(pkgUpdateCommand);

rootCommand.Subcommands.Add(packagesCommand);

var listCommand = new Command("list", "List installed packages from installed.json.")
{
    Aliases = { "ls" }
};
listCommand.SetAction(_ => ListInstalled());
rootCommand.Subcommands.Add(listCommand);

var installCommand = new Command("install", "Install one or more packages.")
{
    Aliases = { "i" }
};
var nameArg = new Argument<string[]>("name")
{
    Arity = ArgumentArity.OneOrMore,
    Description = "Package name(s) to install"
};
installCommand.Arguments.Add(nameArg);
installCommand.SetAction(async parseResult =>
{
    var names = parseResult.GetValue(nameArg) ?? Array.Empty<string>();
    var anyError = false;
    foreach (var name in names)
    {
        try
        {
            await InstallOneAsync(name, skipIfUptodate: true);
        }
        catch (Exception ex)
        {
            var root = Tools.GetInnermostException(ex);
            Console.Error.WriteLine($"install {name} failed: {root.Message}");
            anyError = true;
        }
    }
    return anyError ? 1 : 0;
});
rootCommand.Subcommands.Add(installCommand);

var uninstallCommand = new Command("uninstall", "Uninstall one or more packages.")
{
    Aliases = { "u", "rm", "remove" }
};
var uninstallNameArg = new Argument<string[]>("name")
{
    Arity = ArgumentArity.OneOrMore,
    Description = "Package name(s) to uninstall"
};
uninstallCommand.Arguments.Add(uninstallNameArg);
uninstallCommand.SetAction(async parseResult =>
{
    var names = parseResult.GetValue(uninstallNameArg) ?? Array.Empty<string>();
    var anyError = false;
    foreach (var name in names)
    {
        try
        {
            await UninstallOneAsync(name);
        }
        catch (Exception ex)
        {
            var root = Tools.GetInnermostException(ex);
            Console.Error.WriteLine($"uninstall {name} failed: {root.Message}");
            anyError = true;
        }
    }
    return anyError ? 1 : 0;
});
rootCommand.Subcommands.Add(uninstallCommand);

var upgradeCommand = new Command("upgrade", "Upgrade one or more installed packages.")
{
    Aliases = { "up" }
};
var upgradeNameArg = new Argument<string[]>("name")
{
    Arity = ArgumentArity.ZeroOrMore,
    Description = "Package name(s) to upgrade (empty with --all to upgrade all)"
};
upgradeCommand.Arguments.Add(upgradeNameArg);
var upgradeAllOption = new Option<bool>("--all")
{
    Description = "Upgrade all installed packages."
};
upgradeCommand.Options.Add(upgradeAllOption);
upgradeCommand.SetAction(async parseResult =>
{
    var names = parseResult.GetValue(upgradeNameArg) ?? Array.Empty<string>();
    var upgradeAll = parseResult.GetValue(upgradeAllOption);

    if (names.Length == 0 && !upgradeAll)
    {
        Console.WriteLine("Usage: sjtf upgrade <name>... [--all]");
        Console.WriteLine("  <name>   Package name(s) to upgrade");
        Console.WriteLine("  --all    Upgrade all installed packages");
        return 0;
    }

    if (upgradeAll)
    {
        var installed = Installed.Load();
        names = installed.Keys.ToArray();
        if (names.Length == 0)
        {
            Console.WriteLine("no packages installed");
            return 0;
        }
    }

    var anyError = false;
    foreach (var name in names)
    {
        try
        {
            await UpgradeOneAsync(name);
        }
        catch (Exception ex)
        {
            var root = Tools.GetInnermostException(ex);
            Console.Error.WriteLine($"upgrade {name} failed: {root.Message}");
            anyError = true;
        }
    }
    return anyError ? 1 : 0;
});
rootCommand.Subcommands.Add(upgradeCommand);

var favoritesCommand = new Command("favorites", "Sync installed packages with favorites.json.")
{
    Aliases = { "favors" }
};
favoritesCommand.SetAction(async _ =>
{
    var path = Path.Combine(Paths.DataDir(), "favorites.json");
    if (!File.Exists(path))
    {
        Console.Error.WriteLine("favorites.json not found. Create it with a JSON array of package names.");
        return 1;
    }

    string[] favNames;
    try
    {
        var raw = File.ReadAllText(path);
        var arr = JsonNode.Parse(raw) as JsonArray;
        if (arr == null || arr.Count == 0)
        {
            Console.Error.WriteLine("favorites.json is empty or not a valid JSON array.");
            return 1;
        }
        favNames = arr.Where(n => n != null).Select(n => n!.GetValue<string>()).ToArray();
    }
    catch (Exception ex)
    {
        var root = Tools.GetInnermostException(ex);
        Console.Error.WriteLine($"failed to parse favorites.json: {root.Message}");
        return 1;
    }

    if (favNames.Length == 0)
    {
        Console.Error.WriteLine("favorites.json contains no package names.");
        return 1;
    }

    var installed = Installed.Load();
    var anyError = false;

    // Install or upgrade favorites / 安装或升级收藏包
    foreach (var name in favNames)
    {
        try
        {
            if (installed.ContainsKey(name))
                await UpgradeOneAsync(name);
            else
                await InstallOneAsync(name, skipIfUptodate: true);
        }
        catch (Exception ex)
        {
            var root = Tools.GetInnermostException(ex);
            Console.Error.WriteLine($"favorites {name} failed: {root.Message}");
            anyError = true;
        }
    }

    // Uninstall packages not in favorites / 卸载不在收藏中的包
    var toRemove = installed.Keys.Where(k => !favNames.Contains(k)).ToArray();
    foreach (var name in toRemove)
    {
        try
        {
            await UninstallOneAsync(name);
        }
        catch (Exception ex)
        {
            var root = Tools.GetInnermostException(ex);
            Console.Error.WriteLine($"uninstall {name} failed: {root.Message}");
            anyError = true;
        }
    }

    return anyError ? 1 : 0;
});
rootCommand.Subcommands.Add(favoritesCommand);

return await rootCommand.Parse(args).InvokeAsync(new InvocationConfiguration());

    /// <summary>
    /// 列出 pkgs.json 中定义的所有包 / List all packages defined in pkgs.json.
    /// </summary>
    /// <returns>退出代码 / Exit code.</returns>
    static int ListPackages()
    {
        try
        {
            var loaded = Packages.Load();
            var pkgs = loaded.Root;
            var newKeys = loaded.NewKeys;
            var overriddenKeys = loaded.OverriddenKeys;

            if (pkgs.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]packages:[/]");
                AnsiConsole.MarkupLine("  [grey](none)[/]");
                return 0;
            }

            var rows = new List<(string Name, string Description)>();
            foreach (var prop in pkgs.AsObject())
            {
                var description = "";
                if (prop.Value is JsonObject descObj && descObj.TryGetPropertyValue("description", out var descNode) && descNode is JsonValue descVal && descVal.GetValueKind() == JsonValueKind.String)
                    description = descVal.GetValue<string>();
                var displayName = overriddenKeys.Contains(prop.Key) ? prop.Key + "*co"
                                : newKeys.Contains(prop.Key)       ? prop.Key + "*c"
                                : prop.Key;
                rows.Add((displayName, description));
            }
            rows.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            var table = new Table()
                .Border(TableBorder.MinimalHeavyHead)
                .AddColumn("[bold]Name[/]")
                .AddColumn("[bold]Description[/]");
            foreach (var (name, description) in rows)
                table.AddRow(name, description);
            AnsiConsole.Write(table);
            return 0;
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"error: invalid JSON in pkgs.json: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// 列出 installed.json 中已安装的包 / List installed packages from installed.json.
    /// </summary>
    /// <returns>退出代码 / Exit code.</returns>
    static int ListInstalled()
    {
        var installedPath = Path.Combine(Paths.DataDir(), "installed.json");

        if (!File.Exists(installedPath))
        {
            Installed.Load();
        }

        var descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> newKeys = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> overriddenKeys = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            var loaded = Packages.Load();
            newKeys = loaded.NewKeys;
            overriddenKeys = loaded.OverriddenKeys;
            foreach (var prop in loaded.Root.AsObject())
            {
                if (prop.Value is JsonObject descObj && descObj.TryGetPropertyValue("description", out var descNode) && descNode is JsonValue descVal && descVal.GetValueKind() == JsonValueKind.String)
                    descriptions[prop.Key] = descVal.GetValue<string>();
            }
        }
        catch (InvalidOperationException)
        {
        }

        try
        {
            using var stream = File.OpenRead(installedPath);
            using var doc = JsonDocument.Parse(stream);

            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                Console.Error.WriteLine("error: installed.json root must be a JSON object.");
                return 1;
            }

            var entries = new List<(string Name, string Version, string Description)>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var version = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString() ?? "",
                    JsonValueKind.Object => prop.Value.TryGetProperty("version", out var verNode) && verNode.ValueKind == JsonValueKind.String
                        ? verNode.GetString() ?? ""
                        : "",
                    _ => prop.Value.GetRawText()
                };
                descriptions.TryGetValue(prop.Name, out var description);
                var displayName = overriddenKeys.Contains(prop.Name) ? prop.Name + "*co"
                                : newKeys.Contains(prop.Name)         ? prop.Name + "*c"
                                : prop.Name;
                entries.Add((displayName, version, description ?? ""));
            }
            entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            if (entries.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]installed:[/]");
                AnsiConsole.MarkupLine("  [grey](none)[/]");
                return 0;
            }

            var table = new Table()
                .Border(TableBorder.MinimalHeavyHead)
                .AddColumn("[bold]Name[/]")
                .AddColumn("[bold]Version[/]")
                .AddColumn("[bold]Description[/]");
            foreach (var (name, version, description) in entries)
                table.AddRow(name, version, description);
            AnsiConsole.Write(table);
            return 0;
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"error: invalid JSON in installed.json: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// 异步安装单个包 / Asynchronously install a single package.
    /// </summary>
    /// <param name="name">包名称 / Package name.</param>
    /// <param name="skipIfUptodate">如果已是最新版本则跳过 / Skip if already up-to-date.</param>
    static async Task InstallOneAsync(string name, bool skipIfUptodate)
    {
        var pkgs = Packages.Load();
        if (!pkgs.Root.TryGetPropertyValue(name, out var pkgNode) || pkgNode is not JsonObject pkg)
        {
            throw new InvalidOperationException($"package \"{name}\" not found in pkgs.json");
        }

        var fetchSourceName = InstallHelpers.ReadRequiredString(pkg, "fetch_source", name);
        var source = FetchSources.Get(fetchSourceName);

        var installed = Installed.Load();

        Console.WriteLine($"{name}: fetching latest version info...");
        DownloadPlan plan;
        try
        {
            plan = await source.ResolveAsync(pkg, name);
        }
        catch (Exception ex)
        {
            var root = Tools.GetInnermostException(ex);
            Console.WriteLine($"skip {name}: {root.Message}");
            return;
        }

        var installDirRel = InstallHelpers.ReadRequiredString(pkg, "pkg_install_relative_dir", name);
        var installRoot = Config.LoadInstallDir();
        var installFull = Path.Combine(installRoot, installDirRel);
        Directory.CreateDirectory(installFull);
        if (skipIfUptodate && installed.TryGetValue(name, out var currentInfo) && currentInfo.Version == plan.Version)
        {
            // 即使跳过也用新 plan 更新 type / uninstall 字段（迁移场景）
            var needsUpdate = currentInfo.Type != plan.Type
                || (plan.Type == "installer"
                    && (currentInfo.UninstallProgram != plan.UninstallProgram
                        || currentInfo.UninstallParams != plan.UninstallParams));
            if (needsUpdate)
            {
                installed[name] = plan.Type == "installer"
                    ? new InstalledInfo(currentInfo.Version, plan.Type, plan.UninstallProgram, plan.UninstallParams)
                    : new InstalledInfo(currentInfo.Version, plan.Type);
                Installed.Save(installed);
            }
            Console.WriteLine($"{name}: {plan.Version} is already installed, skipping");
            return;
        }

        var dlPath = await InstallHelpers.DownloadAndVerifyAsync(name, plan);

        await InstallHelpers.RunBeforeInstallScript(name, pkg, installRoot, installFull);
        InstallHelpers.PlaceAsset(name, pkg, plan, dlPath, installRoot, installFull);
        InstallHelpers.ApplyFilePermissions(name, pkg, installFull);
        InstallHelpers.RemoveDesktopShortcuts(name, pkg);
        InstallHelpers.CreateShims(name, pkg, installRoot, installFull);
        await InstallHelpers.RunAfterInstallScript(name, pkg, installRoot, installFull);

        installed[name] = plan.Type == "installer"
            ? new InstalledInfo(plan.Version, plan.Type, plan.UninstallProgram, plan.UninstallParams)
            : new InstalledInfo(plan.Version, plan.Type);
        Installed.Save(installed);
        Console.WriteLine($"{name}: installed {plan.Version}");
    }

    /// <summary>
    /// 异步卸载单个包 / Asynchronously uninstall a single package.
    /// </summary>
    /// <param name="name">包名称 / Package name.</param>
    static async Task UninstallOneAsync(string name)
    {
    var installed = Installed.Load();
    if (!installed.ContainsKey(name))
    {
        Console.WriteLine($"{name}: not installed, skipping");
        return;
    }

    var pkgs = Packages.Load();
    if (!pkgs.Root.TryGetPropertyValue(name, out var pkgNode) || pkgNode is not JsonObject pkg)
    {
        throw new InvalidOperationException($"package \"{name}\" not found in pkgs.json");
    }

    var installDirRel = InstallHelpers.ReadRequiredString(pkg, "pkg_install_relative_dir", name);
    var installRoot = Config.LoadInstallDir();
    var installFull = Path.Combine(installRoot, installDirRel);
    var os = Arch.CurrentOs();

    // before_uninstall 钩子（删除前执行，给脚本机会关闭进程/备份数据）
    await InstallHelpers.RunBeforeUninstallScript(name, pkg, installRoot, installFull);

    // 包类型从 installed.json 读取（严格模式：缺失时直接报错）
    // Package type comes from installed.json (strict: error when missing).
    if (!installed.TryGetValue(name, out var installedInfo))
        throw new InvalidOperationException($"{name}: not in installed.json");

    if (string.IsNullOrEmpty(installedInfo.Type))
        throw new InvalidOperationException(
            $"{name}: installed.json has no recorded type (likely installed before type tracking was added). " +
            $"Run 'sjtf install {name}' to refresh the record, then retry.");

    var pkgType = installedInfo.Type;

    // Delete shims first / 先删除 shim 符号链接
    if (pkg.TryGetPropertyValue("shim", out var shimNode) && shimNode is JsonObject shimObj)
    {
        if (shimObj.TryGetPropertyValue(os, out var osNode) && osNode is JsonObject osObj)
        {
            if (osObj.TryGetPropertyValue("symlink", out var symlinkNode) && symlinkNode is JsonObject symlinkObj)
            {
                var symRoot = Path.Combine(installRoot, "shims");
                foreach (var kv in symlinkObj)
                {
                    var linkName = kv.Key;
                    if (string.IsNullOrEmpty(linkName)) continue;
                    var linkPath = Path.Combine(symRoot, linkName);
                    if (File.Exists(linkPath))
                    {
                        Console.WriteLine($"{name}: removing shim {linkPath}");
                        File.Delete(linkPath);
                    }
                }
            }

            if (osObj.TryGetPropertyValue("shell_script", out var shellNode) && shellNode is JsonObject shellObj)
            {
                var symRoot = Path.Combine(installRoot, "shims");
                foreach (var kv in shellObj)
                {
                    var scriptName = kv.Key;
                    if (string.IsNullOrEmpty(scriptName)) continue;
                    var scriptPath = Path.Combine(symRoot, scriptName);
                    if (File.Exists(scriptPath))
                    {
                        Console.WriteLine($"{name}: removing shim {scriptPath}");
                        File.Delete(scriptPath);
                    }
                }
            }
        }
    }

    else if (pkg.TryGetPropertyValue("symlinks", out var symNode) && symNode is JsonObject symObj)
    {
        var symRoot = Path.Combine(installRoot, "shims");
        foreach (var kv in symObj)
        {
            var linkPath = Path.Combine(symRoot, kv.Key);
            if (File.Exists(linkPath))
            {
                    Console.WriteLine($"{name}: removing shim {linkPath}");
                File.Delete(linkPath);
            }
        }
    }

    InstallHelpers.RemoveDesktopShortcuts(name, pkg);

    if (pkgType == "installer")
    {
        // uninstall_program 必须从 installed.json 读取（不再 fallback 到 pkgs.json）。
        // 值由 JS 在 fetch 时替换 {PKG_INSTALL_DIR} 占位符；pkgs.json 作者应显式指定占位符。
        // 程序不再自动将相对路径前缀 installFull —— 按原值直接调用。
        // uninstall_program is read from installed.json (no fallback to pkgs.json).
        // {PKG_INSTALL_DIR} is substituted by JS at fetch time; pkgs.json authors must
        // explicitly use the placeholder. No auto-prefixing of relative paths.
        var uninstallProgram = installedInfo.UninstallProgram;
        var uninstallParams = installedInfo.UninstallParams;

        if (string.IsNullOrEmpty(uninstallProgram))
            throw new InvalidOperationException(
                $"{name}: installed.json has no recorded uninstall_program " +
                $"(likely installed before uninstall fields were persisted). " +
                $"Run 'sjtf install {name}' to refresh the record, then retry.");

        uninstallParams = uninstallParams
            .Replace("{PKG_INSTALL_DIR}", installFull, StringComparison.OrdinalIgnoreCase)
            .Replace("{INSTALL_DIR}", installRoot, StringComparison.OrdinalIgnoreCase);

        if (File.Exists(uninstallProgram))
        {
            Console.WriteLine($"{name}: running uninstaller {uninstallProgram} {uninstallParams}");
            var psi = new System.Diagnostics.ProcessStartInfo(uninstallProgram, uninstallParams)
            {
                UseShellExecute = false
            };
            var proc = System.Diagnostics.Process.Start(psi);
            proc?.WaitForExit();
            if (proc?.ExitCode != 0)
                Console.Error.WriteLine($"{name}: uninstaller exited with code {proc?.ExitCode}");
        }
        else
        {
            Console.Error.WriteLine($"{name}: uninstaller not found at {uninstallProgram}");
        }
    }
    else
    {
        // For portable types, delete install directory / 对于便携类型，删除安装目录
        if (Directory.Exists(installFull))
        {
            Console.WriteLine($"{name}: removing {installFull}");
            Directory.Delete(installFull, recursive: true);
        }
    }

    await InstallHelpers.RunAfterUninstallScript(name, pkg, installRoot, installFull);

    // Remove from installed.json / 从 installed.json 中移除
    installed.Remove(name);
    Installed.Save(installed);
    Console.WriteLine($"{name}: uninstalled");
}

    /// <summary>
    /// 异步升级单个包 / Asynchronously upgrade a single package.
    /// </summary>
    /// <param name="name">包名称 / Package name.</param>
    static async Task UpgradeOneAsync(string name)
    {
        var installed = Installed.Load();
        if (!installed.ContainsKey(name))
        {
            Console.WriteLine($"{name}: not installed, cannot upgrade");
            return;
        }

        var pkgs = Packages.Load();
        if (!pkgs.Root.TryGetPropertyValue(name, out var pkgNode) || pkgNode is not JsonObject pkg)
        {
            throw new InvalidOperationException($"package \"{name}\" not found in pkgs.json");
        }

        var fetchSourceName = InstallHelpers.ReadRequiredString(pkg, "fetch_source", name);
        var source = FetchSources.Get(fetchSourceName);

        Console.WriteLine($"{name}: fetching latest version info...");
        DownloadPlan plan;
        try
        {
            plan = await source.ResolveAsync(pkg, name);
        }
        catch (Exception ex)
        {
            var root = Tools.GetInnermostException(ex);
            Console.WriteLine($"skip {name}: {root.Message}");
            return;
        }

        if (installed[name].Version == plan.Version)
        {
            Console.WriteLine($"{name}: {plan.Version} is already the latest version");
            return;
        }

        Console.WriteLine($"{name}: upgrading from {installed[name].Version} to {plan.Version}");

        var installDirRel = InstallHelpers.ReadRequiredString(pkg, "pkg_install_relative_dir", name);
        var installRoot = Config.LoadInstallDir();
        var installFull = Path.Combine(installRoot, installDirRel);
        Directory.CreateDirectory(installFull);

        var dlPath = await InstallHelpers.DownloadAndVerifyAsync(name, plan);

        await InstallHelpers.RunBeforeUpgradeScript(name, pkg, installRoot, installFull);
        InstallHelpers.PlaceAsset(name, pkg, plan, dlPath, installRoot, installFull);
        InstallHelpers.ApplyFilePermissions(name, pkg, installFull);
        InstallHelpers.RemoveDesktopShortcuts(name, pkg);
        InstallHelpers.CreateShims(name, pkg, installRoot, installFull);
        await InstallHelpers.RunAfterUpgradeScript(name, pkg, installRoot, installFull);

        installed[name] = plan.Type == "installer"
            ? new InstalledInfo(plan.Version, plan.Type, plan.UninstallProgram, plan.UninstallParams)
            : new InstalledInfo(plan.Version, plan.Type);
        Installed.Save(installed);
        Console.WriteLine($"{name}: upgraded to {plan.Version}");
    }
