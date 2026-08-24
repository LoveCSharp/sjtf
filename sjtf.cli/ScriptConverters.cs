using System.Globalization;
using System.Text.Json.Nodes;
using Tomlyn;
using Tomlyn.Model;

namespace Sjtf.Cli;

/// <summary>
/// 脚本引擎使用的转换器 / Converters used by the script engine.
/// 把 config.toml 加载为 JSON 字符串。Lua 时代的 Lua 字面量转换已被删除。
/// Loads config.toml into a JSON string. The Lua-literal converters are gone.
/// </summary>
internal static class ScriptConverters
{
    /// <summary>
    /// 将 config.toml 加载为 JSON 字符串。
    /// Load config.toml and serialize it as a JSON string.
    /// 如果 config.toml 不存在或反序列化失败，返回 "{}"。
    /// Returns "{}" if config.toml is missing or cannot be deserialized.
    /// </summary>
    /// <returns>JSON 字符串 / JSON string.</returns>
    public static string LoadConfigJson()
    {
        var path = Path.Combine(Paths.DataDir(), "config.toml");
        if (!File.Exists(path)) return "{}";

        string raw;
        try
        {
            raw = File.ReadAllText(path);
        }
        catch
        {
            return "{}";
        }

        TomlTable? table;
        try
        {
            table = TomlSerializer.Deserialize(raw, TomlModelContext.Default.TomlTable);
        }
        catch
        {
            return "{}";
        }

        if (table == null) return "{}";
        return TomlTableToJsonString(table);
    }

    /// <summary>
    /// 将一个 TomlTable 递归转换为 JSON 字符串。
    /// Recursively convert a TomlTable to a JSON string.
    /// </summary>
    public static string TomlTableToJsonString(TomlTable table)
    {
        var node = TomlTableToJsonNode(table);
        if (node == null) return "{}";
        return node.ToJsonString();
    }

    /// <summary>
    /// 将 TomlTable 递归转换为 JsonNode。
    /// Recursively convert a TomlTable to a JsonNode.
    /// </summary>
    public static JsonNode? TomlTableToJsonNode(TomlTable table)
    {
        var obj = new JsonObject();
        foreach (var kvp in table)
        {
            var key = kvp.Key;
            var value = TomlValueToJsonNode(kvp.Value);
            obj[key] = value;
        }
        return obj;
    }

    /// <summary>
    /// 将一个 TOML 值递归转换为 JsonNode。
    /// Recursively convert a TOML value to a JsonNode.
    /// </summary>
    public static JsonNode? TomlValueToJsonNode(object? value)
    {
        return value switch
        {
            null => null,
            string s => JsonValue.Create(s),
            bool b => JsonValue.Create(b),
            long l => JsonValue.Create(l),
            double d => JsonValue.Create(d),
            TomlTable t => TomlTableToJsonNode(t),
            TomlArray arr => TomlArrayToJsonNode(arr),
            DateTime dt => JsonValue.Create(dt.ToString("o", CultureInfo.InvariantCulture)),
            DateTimeOffset dto => JsonValue.Create(dto.ToString("o", CultureInfo.InvariantCulture)),
            _ => JsonValue.Create(value.ToString() ?? "")
        };
    }

    /// <summary>
    /// 将 TomlArray 递归转换为 JsonArray。
    /// Recursively convert a TomlArray to a JsonArray.
    /// </summary>
    public static JsonArray TomlArrayToJsonNode(TomlArray arr)
    {
        var ja = new JsonArray();
        foreach (var item in arr)
            ja.Add(TomlValueToJsonNode(item));
        return ja;
    }
}
