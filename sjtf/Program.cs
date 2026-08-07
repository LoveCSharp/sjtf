using System.CommandLine;
using Sjtf;
using Spectre.Console;
using System.Text.Json;
using System.Text.Json.Nodes;

Config.EnsureDefault();
Config.EnsureSymlinkDir();

var rootCommand = new RootCommand("sjtf - command-line skeleton tool.");

rootCommand.SetAction(_ =>
{
    Console.WriteLine("sjtf - run with --help to see available options.");
    return 0;
});

var packagesCommand = new Command("packages", "List packages defined in pkgs.json.")
{
    Aliases = { "pkgs" }
};
packagesCommand.SetAction(_ => ListPackages());
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

return await rootCommand.Parse(args).InvokeAsync();

    /// <summary>
    /// 列出 pkgs.json 中定义的所有包 / List all packages defined in pkgs.json.
    /// </summary>
    /// <returns>退出代码 / Exit code.</returns>
    static int ListPackages()
{
    var path = Path.Combine(Tools.SjtfRoot(), "pkgs.json");

    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"error: pkgs.json not found at {path}");
        return 1;
    }

    try
    {
        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);

        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            Console.Error.WriteLine("error: pkgs.json root must be a JSON object.");
            return 1;
        }

        var rows = new List<(string Name, string Description)>();
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var description = "";
            if (prop.Value.TryGetProperty("description", out var descNode) && descNode.ValueKind == JsonValueKind.String)
                description = descNode.GetString() ?? "";
            rows.Add((prop.Name, description));
        }
        rows.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        if (rows.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]packages:[/]");
            AnsiConsole.MarkupLine("  [grey](none)[/]");
            return 0;
        }

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
        var pkgsPath = Path.Combine(Tools.SjtfRoot(), "pkgs.json");

        if (!File.Exists(installedPath))
        {
            Installed.Load();
        }

        try
        {
        var descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(pkgsPath))
        {
            using var pkgsStream = File.OpenRead(pkgsPath);
            using var pkgsDoc = JsonDocument.Parse(pkgsStream);
            if (pkgsDoc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in pkgsDoc.RootElement.EnumerateObject())
                {
                    if (prop.Value.TryGetProperty("description", out var descNode) && descNode.ValueKind == JsonValueKind.String)
                        descriptions[prop.Name] = descNode.GetString() ?? "";
                }
            }
        }

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
    static async Task InstallOneAsync(string name, bool skipIfUptodate)
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
        plan = await source.ResolveAsync(pkg, name);
    }
    catch (Exception ex)
    {
        var root = Tools.GetInnermostException(ex);
        Console.WriteLine($"skip {name}: {root.Message}");
        return;
    }

    var installDirRel = InstallHelpers.ReadRequiredString(pkg, "install_dir", name);
    var installRoot = Config.LoadInstallDir();
    var installFull = Path.Combine(installRoot, installDirRel);
    Directory.CreateDirectory(installFull);
    if (skipIfUptodate && installed.TryGetValue(name, out var currentVer) && currentVer == plan.Version)
    {
        Console.WriteLine($"{name}: {plan.Version} is already installed, skipping");
        return;
    }

    var maxAttempts = Config.LoadDownloadRetryMax();
    var dlPath = await InstallHelpers.DownloadAndVerifyAsync(name, plan, maxAttempts);

    InstallHelpers.PlaceAsset(name, pkg, dlPath, installFull);
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
    static async Task UninstallOneAsync(string name)
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

    // Get package type and fetch_asset / 获取包类型和 fetch_asset
    var pkgType = "portable-compressed-archive";
    JsonObject? fetch = null;
    if (pkg.TryGetPropertyValue("fetch_asset", out var fetchNode) && fetchNode is JsonObject fetchObj)
    {
        fetch = fetchObj;
        if (fetch.TryGetPropertyValue("type", out var typeNode) && typeNode is JsonValue typeVal && typeVal.GetValueKind() == JsonValueKind.String)
            pkgType = typeVal.GetValue<string>();
    }

    // Delete symlinks first / 先删除符号链接
    if (pkg.TryGetPropertyValue("symlinks", out var symNode) && symNode is JsonObject symObj)
    {
        var symRoot = Path.Combine(installRoot, "symlink");
        foreach (var kv in symObj)
        {
            var linkPath = Path.Combine(symRoot, kv.Key);
            if (File.Exists(linkPath))
            {
                Console.WriteLine($"{name}: removing symlink {linkPath}");
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
    static async Task UpgradeOneAsync(string name)
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
        plan = await source.ResolveAsync(pkg, name);
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

    var installDirRel = InstallHelpers.ReadRequiredString(pkg, "install_dir", name);
    var installRoot = Config.LoadInstallDir();
    var installFull = Path.Combine(installRoot, installDirRel);
    Directory.CreateDirectory(installFull);

    var maxAttempts = Config.LoadDownloadRetryMax();
    var dlPath = await InstallHelpers.DownloadAndVerifyAsync(name, plan, maxAttempts);

    InstallHelpers.PlaceAsset(name, pkg, dlPath, installFull);
    InstallHelpers.CreateSymlinks(name, pkg, installRoot, installFull);
    InstallHelpers.RunAfterInstallScript(name, pkg, installRoot, installFull);

    installed[name] = plan.Version;
    Installed.Save(installed);
    Console.WriteLine($"{name}: upgraded to {plan.Version}");
}
