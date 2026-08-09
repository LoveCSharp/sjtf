using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using NLua;

namespace Sjtf;

/// <summary>
/// 为 Lua 脚本提供的 C# 绑定函数集合 / Collection of C# binding functions exposed to Lua scripts.
/// 包括 HTTP 请求、JSON 解析、正则匹配、文件和目录操作。
/// Includes HTTP requests, JSON parsing, regex matching, file and directory operations.
/// </summary>
internal class LuaBindings
{
    private readonly Lua _lua;
    public string? PackageName { get; set; }
    private static readonly HttpClient _http = new HttpClient();

    public LuaBindings(Lua lua)
    {
        _lua = lua;
    }

    /// <summary>
    /// 执行 HTTP GET 请求并返回响应体 / Execute an HTTP GET request and return the response body.
    /// 自动添加 User-Agent 头；Lua 传入的 headers 会覆盖默认值。
    /// Automatically adds User-Agent header; Lua headers override defaults if duplicated.
    /// </summary>
    /// <param name="url">请求 URL / Request URL.</param>
    /// <param name="headers">请求头 Lua 表 / Request headers Lua table.</param>
    /// <returns>响应内容字符串 / Response content string.</returns>
    public string HttpGet(string url, LuaTable? headers = null)
    {
        var prefix = PackageName ?? "http_get";
        Console.WriteLine($"{prefix}: http get request: {url}");
        using var req = new HttpRequestMessage(HttpMethod.Get, url);

        req.Headers.TryAddWithoutValidation("User-Agent", Config.LoadUserAgent());

        if (headers != null)
        {
            foreach (var key in headers.Keys)
            {
                if (key is string keyStr)
                {
                    var value = headers[keyStr] as string;
                    if (!string.IsNullOrEmpty(value))
                    {
                        req.Headers.Remove(keyStr);
                        req.Headers.TryAddWithoutValidation(keyStr, value);
                    }
                }
            }
        }

        using var resp = _http.SendAsync(req).GetAwaiter().GetResult();
        resp.EnsureSuccessStatusCode();
        return resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// 将 JSON 字符串解码为 Lua 表 / Decode a JSON string into a Lua table.
    /// </summary>
    /// <param name="json">JSON 字符串 / JSON string.</param>
    /// <returns>Lua 表 / Lua table.</returns>
    public LuaTable JsonDecode(string json)
    {
        var luaCode = "return " + JsonToLua(json);
        var result = _lua.DoString(luaCode);
        return (LuaTable)result[0];
    }

    /// <summary>
    /// 检查输入字符串是否匹配正则表达式 / Check if the input string matches the regex pattern.
    /// </summary>
    /// <param name="pattern">正则表达式模式 / Regular expression pattern.</param>
    /// <param name="input">输入字符串 / Input string.</param>
    /// <returns>是否匹配 / Whether it matches.</returns>
    public bool RegexMatch(string pattern, string input)
    {
        try
        {
            return Regex.IsMatch(input, pattern);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 删除文件（Lua 安全包装） / Delete a file (Lua-safe wrapper).
    /// </summary>
    /// <param name="path">文件路径 / File path.</param>
    /// <returns>错误信息或 null / Error message or null.</returns>
    public string? RemoveFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// 创建目录（Lua 安全包装） / Create a directory (Lua-safe wrapper).
    /// </summary>
    /// <param name="path">目录路径 / Directory path.</param>
    /// <returns>错误信息或 null / Error message or null.</returns>
    public string? CreateDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// 将 JSON 字符串转换为 Lua 代码 / Convert a JSON string to Lua code.
    /// </summary>
    private static string JsonToLua(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return ElementToLua(doc.RootElement);
    }

    /// <summary>
    /// 将 JSON 元素递归转换为 Lua 代码 / Recursively convert a JSON element to Lua code.
    /// </summary>
    private static string ElementToLua(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var parts = new List<string>();
                foreach (var prop in element.EnumerateObject())
                    parts.Add($"[\"{EscapeLua(prop.Name)}\"]={ElementToLua(prop.Value)}");
                return "{" + string.Join(",", parts) + "}";
            case JsonValueKind.Array:
                var items = new List<string>();
                foreach (var item in element.EnumerateArray())
                    items.Add(ElementToLua(item));
                return "{" + string.Join(",", items) + "}";
            case JsonValueKind.String:
                return "\"" + EscapeLua(element.GetString()!) + "\"";
            case JsonValueKind.Number:
                if (element.TryGetInt64(out var l)) return l.ToString();
                if (element.TryGetDouble(out var d)) return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return element.GetRawText();
            case JsonValueKind.True:
                return "true";
            case JsonValueKind.False:
                return "false";
            case JsonValueKind.Null:
                return "nil";
            default:
                return "\"" + EscapeLua(element.GetRawText()) + "\"";
        }
    }

    /// <summary>
    /// 转义字符串以便安全嵌入 Lua 代码 / Escape a string for safe embedding in Lua code.
    /// </summary>
    private static string EscapeLua(string s)
    {
        return LuaConverters.EscapeLua(s);
    }
}
