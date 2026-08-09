using System.CommandLine;
using Sjtf;
using Spectre.Console;
using System.Text.Json;
using System.Text.Json.Nodes;

Config.EnsureDefault();
Config.EnsureSymlinkDir();

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

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

var pkgListCommand = new Command("list", "List packages defined in pkgs.json.");
pkgListCommand.SetAction(_ => ListPackages());
packagesCommand.Subcommands.Add(pkgListCommand);

var pkgUpdateCommand = new Command("update", "Update pkgs.json from remote source.");
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

        await Packages.UpdateRemoteAsync(remoteUrl, cts.Token);
        return 0;
    }
    catch (OperationCanceledException)
    {
        Console.Error.WriteLine("pkgs: update cancelled");
        return 1;
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
            await InstallOneAsync(name, skipIfUptodate: true, ct: cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine($"{name}: cancelled");
            anyError = true;
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
            await UninstallOneAsync(name, ct: cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine($"{name}: cancelled");
            anyError = true;
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
            await UpgradeOneAsync(name, ct: cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine($"{name}: cancelled");
            anyError = true;
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
    var path = Path.Combine(Tools.SjtfRoot(), "favorites.json");
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
                await UpgradeOneAsync(name, ct: cts.Token);
            else
                await InstallOneAsync(name, skipIfUptodate: true, ct: cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine($"{name}: cancelled");
            anyError = true;
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
            await UninstallOneAsync(name, ct: cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine($"{name}: cancelled");
            anyError = true;
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

try
{
    return await rootCommand.Parse(args).InvokeAsync(new InvocationConfiguration(), cts.Token);
}
catch (OperationCanceledException)
{
    return 130;
}

    /// <summary>
    /// 列出 pkgs.json 中定义的所有包 / List all packages defined in pkgs.json.
    /// </summary>
    /// <returns>退出代码 / Exit code.</returns>
    static int ListPackages()
    {
        try
        {
            var pkgs = Packages.Load();

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
                rows.Add((prop.Key, description));
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
        var installedPath = Path.Combine(Tools.SjtfRoot(), "installed.json");

        if (!File.Exists(installedPath))
        {
            Installed.Load();
        }

        var descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var pkgs = Packages.Load();
            foreach (var prop in pkgs.AsObject())
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
                var version = prop.Value.ValueKind == JsonValueKind.String
                    ? prop.Value.GetString() ?? ""
                    : prop.Value.GetRawText();
                descriptions.TryGetValue(prop.Name, out var description);
                entries.Add((prop.Name, version, description ?? ""));
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
    /// <param name="ct">取消令牌 / Cancellation token.</param>
    static async Task InstallOneAsync(string name, bool skipIfUptodate, CancellationToken ct = default)
    {
        var pkgs = Packages.Load();
        if (!pkgs.TryGetPropertyValue(name, out var pkgNode) || pkgNode is not JsonObject pkg)
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
            plan = await source.ResolveAsync(pkg, name, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
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
        if (skipIfUptodate && installed.TryGetValue(name, out var currentVer) && currentVer == plan.Version)
        {
            Console.WriteLine($"{name}: {plan.Version} is already installed, skipping");
            return;
        }

        var dlPath = await InstallHelpers.DownloadAndVerifyAsync(name, plan, ct);

        InstallHelpers.PlaceAsset(name, pkg, dlPath, installRoot, installFull);
        InstallHelpers.CreateSymlinks(name, pkg, installRoot, installFull);
        InstallHelpers.RunAfterInstallScript(name, pkg, installRoot, installFull);

        installed[name] = plan.Version;
        Installed.Save(installed);
        Console.WriteLine($"{name}: installed {plan.Version}");
    }

    /// <summary>
    /// 异步卸载单个包 / Asynchronously uninstall a single package.
    /// </summary>
    /// <param name="name">包名称 / Package name.</param>
    /// <param name="ct">取消令牌 / Cancellation token.</param>
    static async Task UninstallOneAsync(string name, CancellationToken ct = default)
    {
    var installed = Installed.Load();
    if (!installed.ContainsKey(name))
    {
        Console.WriteLine($"{name}: not installed, skipping");
        return;
    }

    var pkgs = Packages.Load();
    if (!pkgs.TryGetPropertyValue(name, out var pkgNode) || pkgNode is not JsonObject pkg)
    {
        throw new InvalidOperationException($"package \"{name}\" not found in pkgs.json");
    }

    var installDirRel = InstallHelpers.ReadRequiredString(pkg, "install_dir", name);
    var installRoot = Config.LoadInstallDir();
    var installFull = Path.Combine(installRoot, installDirRel);
    var os = Arch.CurrentOs();

    // Get package type and fetch_asset / 获取包类型和 fetch_asset
    var pkgType = "portable-compressed-archive";
    JsonObject? fetch = null;
    if (pkg.TryGetPropertyValue("fetch_asset", out var fetchNode) && fetchNode is JsonObject fetchObj)
    {
        fetch = fetchObj;
        if (fetch.TryGetPropertyValue("type", out var typeNode) && typeNode is JsonValue typeVal && typeVal.GetValueKind() == JsonValueKind.String)
            pkgType = typeVal.GetValue<string>();
    }

    // Delete shims first / 先删除 shim 符号链接
    if (pkg.TryGetPropertyValue("shim", out var shimNode) && shimNode is JsonObject shimObj)
    {
        if (shimObj.TryGetPropertyValue(os, out var osNode) && osNode is JsonObject osObj)
        {
            if (osObj.TryGetPropertyValue("symlink", out var symlinkNode) && symlinkNode is JsonArray symlinkArr)
            {
                var symRoot = Path.Combine(installRoot, "shims");
                foreach (var item in symlinkArr)
                {
                    if (item is not JsonValue val || val.GetValueKind() != JsonValueKind.String) continue;
                    var targetRel = val.GetValue<string>() ?? "";
                    var linkName = Path.GetFileName(targetRel);
                    if (string.IsNullOrEmpty(linkName)) continue;
                    var linkPath = Path.Combine(symRoot, linkName);
                    if (File.Exists(linkPath))
                    {
                        Console.WriteLine($"{name}: removing shim {linkPath}");
                        File.Delete(linkPath);
                    }
                }
            }

            if (osObj.TryGetPropertyValue("cmd", out var cmdNode) && cmdNode is JsonObject cmdObj)
            {
                var symRoot = Path.Combine(installRoot, "shims");
                foreach (var kv in cmdObj)
                {
                    var cmdName = kv.Key;
                    if (string.IsNullOrEmpty(cmdName)) continue;
                    var cmdPath = Path.Combine(symRoot, cmdName);
                    if (File.Exists(cmdPath))
                    {
                        Console.WriteLine($"{name}: removing shim {cmdPath}");
                        File.Delete(cmdPath);
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

    if (pkgType == "installer")
    {
        // For installer type, run uninstall program / 对于安装程序类型，运行卸载程序
        var uninstallProgram = "";
        if (fetch != null && fetch.TryGetPropertyValue("uninstall_program", out var upNode) && upNode is JsonValue upVal && upVal.GetValueKind() == JsonValueKind.String)
            uninstallProgram = upVal.GetValue<string>();

        var uninstallParams = "";
        if (fetch != null && fetch.TryGetPropertyValue("uninstall_params", out var paramsNode) && paramsNode is JsonValue paramsVal && paramsVal.GetValueKind() == JsonValueKind.String)
            uninstallParams = paramsVal.GetValue<string>();

        if (!string.IsNullOrEmpty(uninstallParams))
        {
            uninstallParams = uninstallParams.Replace("{PKG_INSTALL_DIR}", installFull, StringComparison.OrdinalIgnoreCase)
                                            .Replace("{INSTALL_DIR}", installRoot, StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrEmpty(uninstallProgram))
        {
            var uninstallExe = Path.Combine(installFull, uninstallProgram);
            if (File.Exists(uninstallExe))
            {
                Console.WriteLine($"{name}: running uninstaller {uninstallExe} {uninstallParams}");
                var psi = new System.Diagnostics.ProcessStartInfo(uninstallExe, uninstallParams)
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
                Console.Error.WriteLine($"{name}: uninstaller not found at {uninstallExe}");
            }
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

    InstallHelpers.RunAfterUninstallScript(name, pkg, installRoot, installFull);

    // Remove from installed.json / 从 installed.json 中移除
    installed.Remove(name);
    Installed.Save(installed);
    Console.WriteLine($"{name}: uninstalled");
}

    /// <summary>
    /// 异步升级单个包 / Asynchronously upgrade a single package.
    /// </summary>
    /// <param name="name">包名称 / Package name.</param>
    /// <param name="ct">取消令牌 / Cancellation token.</param>
    static async Task UpgradeOneAsync(string name, CancellationToken ct = default)
    {
        var installed = Installed.Load();
        if (!installed.ContainsKey(name))
        {
            Console.WriteLine($"{name}: not installed, cannot upgrade");
            return;
        }

        var pkgs = Packages.Load();
        if (!pkgs.TryGetPropertyValue(name, out var pkgNode) || pkgNode is not JsonObject pkg)
        {
            throw new InvalidOperationException($"package \"{name}\" not found in pkgs.json");
        }

        var fetchSourceName = InstallHelpers.ReadRequiredString(pkg, "fetch_source", name);
        var source = FetchSources.Get(fetchSourceName);

        Console.WriteLine($"{name}: fetching latest version info...");
        DownloadPlan plan;
        try
        {
            plan = await source.ResolveAsync(pkg, name, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var root = Tools.GetInnermostException(ex);
            Console.WriteLine($"skip {name}: {root.Message}");
            return;
        }

        if (installed[name] == plan.Version)
        {
            Console.WriteLine($"{name}: {plan.Version} is already the latest version");
            return;
        }

        Console.WriteLine($"{name}: upgrading from {installed[name]} to {plan.Version}");

        var installDirRel = InstallHelpers.ReadRequiredString(pkg, "pkg_install_relative_dir", name);
        var installRoot = Config.LoadInstallDir();
        var installFull = Path.Combine(installRoot, installDirRel);
        Directory.CreateDirectory(installFull);

        var dlPath = await InstallHelpers.DownloadAndVerifyAsync(name, plan, ct);

        InstallHelpers.PlaceAsset(name, pkg, dlPath, installRoot, installFull);
        InstallHelpers.CreateSymlinks(name, pkg, installRoot, installFull);
        InstallHelpers.RunAfterInstallScript(name, pkg, installRoot, installFull);

        installed[name] = plan.Version;
        Installed.Save(installed);
        Console.WriteLine($"{name}: upgraded to {plan.Version}");
    }
