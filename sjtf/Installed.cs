using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sjtf;

/// <summary>
/// 已安装包记录管理 / Installed package record management.
/// 负责加载和保存 installed.json 文件。
/// Responsible for loading and saving the installed.json file.
/// </summary>
internal static class Installed
{
    /// <summary>
    /// 从 installed.json 加载已安装包字典 / Load installed package dictionary from installed.json.
    /// </summary>
    /// <returns>包名到版本号的字典 / Dictionary of package names to version strings.</returns>
    public static Dictionary<string, string> Load()
    {
        var path = Path.Combine(Tools.SjtfRoot(), "installed.json");
        if (!File.Exists(path))
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, "{}");
            return new Dictionary<string, string>();
        }
        var raw = File.ReadAllText(path);
        var doc = JsonDocument.Parse(raw);
        var result = new Dictionary<string, string>();
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            result[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                ? prop.Value.GetString() ?? ""
                : prop.Value.GetRawText();
        }
        return result;
    }

    /// <summary>
    /// 将已安装包字典保存到 installed.json / Save installed package dictionary to installed.json.
    /// </summary>
    /// <param name="installed">包名到版本号的字典 / Dictionary of package names to version strings.</param>
    public static void Save(Dictionary<string, string> installed)
    {
        var path = Path.Combine(Tools.SjtfRoot(), "installed.json");
        var obj = new JsonObject();
        foreach (var kv in installed)
        {
            obj[kv.Key] = kv.Value;
        }
        File.WriteAllText(path, obj.ToJsonString());
    }
}
