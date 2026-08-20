using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sjtf;

/// <summary>
/// 单个已安装包的记录 / Record of a single installed package.
/// </summary>
/// <param name="Version">已安装的版本号 / Installed version string.</param>
/// <param name="Type">安装时使用的包类型（用于卸载）/ Package type used at install time (needed for uninstall).</param>
/// <param name="UninstallProgram">installer 类型的卸载程序绝对路径（持久化，避免卸载时依赖 pkgs.json）。 / Absolute uninstaller path for installer packages (persisted so uninstall does not depend on pkgs.json).</param>
/// <param name="UninstallParams">installer 类型的卸载参数（持久化）。 / Uninstaller arguments for installer packages (persisted).</param>
public sealed record InstalledInfo(
    string Version,
    string Type,
    string UninstallProgram = "",
    string UninstallParams = "");

/// <summary>
/// 已安装包记录管理 / Installed package record management.
/// 负责加载和保存 installed.json 文件。
/// Responsible for loading and saving the installed.json file.
/// </summary>
internal static class Installed
{
    /// <summary>
    /// 从 installed.json 加载已安装包字典 / Load installed package dictionary from installed.json.
    /// 兼容旧格式 <c>{name: "version"}</c>，读取时迁移为 <c>{name: {version, type}}</c>（type 为空）。
    /// Legacy <c>{name: "version"}</c> entries are migrated to <c>{name: {version, type}}</c> with an empty type.
    /// </summary>
    /// <returns>包名到已安装信息的字典 / Dictionary of package names to installed info.</returns>
    public static Dictionary<string, InstalledInfo> Load()
    {
        var path = Path.Combine(Paths.SjtfRoot(), "installed.json");
        if (!File.Exists(path))
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, "{}");
            return new Dictionary<string, InstalledInfo>();
        }
        var raw = File.ReadAllText(path);
        var doc = JsonDocument.Parse(raw);
        var result = new Dictionary<string, InstalledInfo>();
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Object)
            {
                var version = "";
                var type = "";
                var uninstallProgram = "";
                var uninstallParams = "";
                if (prop.Value.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String)
                    version = v.GetString() ?? "";
                if (prop.Value.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String)
                    type = t.GetString() ?? "";
                if (prop.Value.TryGetProperty("uninstall_program", out var up) && up.ValueKind == JsonValueKind.String)
                    uninstallProgram = up.GetString() ?? "";
                if (prop.Value.TryGetProperty("uninstall_params", out var ups) && ups.ValueKind == JsonValueKind.String)
                    uninstallParams = ups.GetString() ?? "";
                result[prop.Name] = new InstalledInfo(version, type, uninstallProgram, uninstallParams);
            }
            else if (prop.Value.ValueKind == JsonValueKind.String)
            {
                result[prop.Name] = new InstalledInfo(prop.Value.GetString() ?? "", "");
            }
            else if (prop.Value.ValueKind == JsonValueKind.Null)
            {
                result[prop.Name] = new InstalledInfo("", "");
            }
        }
        return result;
    }

    /// <summary>
    /// 将已安装包字典保存到 installed.json / Save installed package dictionary to installed.json.
    /// </summary>
    /// <param name="installed">包名到已安装信息的字典 / Dictionary of package names to installed info.</param>
    public static void Save(Dictionary<string, InstalledInfo> installed)
    {
        var path = Path.Combine(Paths.SjtfRoot(), "installed.json");
        var obj = new JsonObject();
        foreach (var kv in installed)
        {
            var entry = new JsonObject
            {
                ["version"] = kv.Value.Version,
                ["type"] = kv.Value.Type
            };
            if (!string.IsNullOrEmpty(kv.Value.UninstallProgram))
                entry["uninstall_program"] = kv.Value.UninstallProgram;
            if (!string.IsNullOrEmpty(kv.Value.UninstallParams))
                entry["uninstall_params"] = kv.Value.UninstallParams;
            obj[kv.Key] = entry;
        }
        File.WriteAllText(path, obj.ToJsonString());
    }
}
