using System.Text.Json;
using System.Text.Json.Nodes;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Serialization;

namespace Sjtf;

/// <summary>
/// JSON 和 TOML 到 Lua 代码的转换器 / Converters from JSON and TOML to Lua code.
/// 提供将 JSON 对象和 TOML 表递归转换为 Lua 字面量的工具方法。
/// Provides utility methods to recursively convert JSON objects and TOML tables to Lua literals.
/// </summary>
internal static class LuaConverters
{
    /// <summary>
    /// 将 config.toml 加载为 Lua 表表示 / Load config.toml as a Lua table representation.
    /// </summary>
    /// <returns>Lua 表代码字符串 / Lua table code string.</returns>
    public static string LoadConfigLua()
    {
        var path = Path.Combine(Tools.SjtfRoot(), "config.toml");
        if (!File.Exists(path))
            return "{}";

        var toml = File.ReadAllText(path);
        var table = TomlSerializer.Deserialize(toml, TomlModelContext.Default.TomlTable);
        if (table == null) return "{}";
        return TomlTableToLua(table);
    }

    /// <summary>
    /// 将 JSON 节点递归转换为 Lua 代码 / Recursively convert a JSON node to Lua code.
    /// </summary>
    /// <param name="node">JSON 节点 / JSON node.</param>
    /// <returns>Lua 代码字符串 / Lua code string.</returns>
    public static string JsonObjectToLua(JsonNode? node)
    {
        if (node == null) return "nil";
        switch (node.GetValueKind())
        {
            case JsonValueKind.Object:
                var parts = new List<string>();
                if (node is JsonObject obj)
                    foreach (var prop in obj)
                        parts.Add($"[\"{EscapeLua(prop.Key)}\"]={JsonObjectToLua(prop.Value)}");
                return "{" + string.Join(",", parts) + "}";
            case JsonValueKind.Array:
                var items = new List<string>();
                if (node is JsonArray arr)
                    foreach (var item in arr)
                        items.Add(JsonObjectToLua(item));
                return "{" + string.Join(",", items) + "}";
            case JsonValueKind.String:
                return "\"" + EscapeLua(node.GetValue<string>()) + "\"";
            case JsonValueKind.Number:
                if (node is JsonValue jv)
                {
                    if (jv.TryGetValue<int>(out var i)) return i.ToString();
                    if (jv.TryGetValue<long>(out var l)) return l.ToString();
                    if (jv.TryGetValue<double>(out var d)) return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                return node.ToString();
            case JsonValueKind.True: return "true";
            case JsonValueKind.False: return "false";
            case JsonValueKind.Null: return "nil";
            default: return "\"" + EscapeLua(node.ToString()) + "\"";
        }
    }

    /// <summary>
    /// 将 TomlTable 递归转换为 Lua 表代码 / Recursively convert TomlTable to Lua table code.
    /// </summary>
    /// <param name="table">TOML 表 / TOML table.</param>
    /// <returns>Lua 表代码字符串 / Lua table code string.</returns>
    public static string TomlTableToLua(TomlTable table)
    {
        var parts = new List<string>();
        foreach (var kvp in table)
            parts.Add($"[\"{EscapeLua(kvp.Key)}\"]={TomlValueToLua(kvp.Value)}");
        return "{" + string.Join(",", parts) + "}";
    }

    /// <summary>
    /// 将 TOML 值转换为 Lua 字面量 / Convert a TOML value to a Lua literal.
    /// </summary>
    /// <param name="value">TOML 值 / TOML value.</param>
    /// <returns>Lua 字面量字符串 / Lua literal string.</returns>
    public static string TomlValueToLua(object? value)
    {
        return value switch
        {
            null => "nil",
            string s => "\"" + EscapeLua(s) + "\"",
            long l => l.ToString(),
            double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            TomlTable t => TomlTableToLua(t),
            TomlArray arr => "{" + string.Join(",", arr.Select(TomlValueToLua)) + "}",
            _ => "\"" + EscapeLua(value.ToString()!) + "\""
        };
    }

    /// <summary>
    /// 转义字符串以便安全嵌入 Lua 代码 / Escape a string for safe embedding in Lua code.
    /// </summary>
    /// <param name="s">输入字符串 / Input string.</param>
    /// <returns>转义后的字符串 / Escaped string.</returns>
    public static string EscapeLua(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    }
}
