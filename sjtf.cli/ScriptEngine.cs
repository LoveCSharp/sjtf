using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using Jint;
using Jint.Native;

namespace Sjtf.Cli;

/// <summary>
/// Jint 脚本引擎工厂与共享 helpers / Jint script engine factory and shared helpers.
/// 所有 C# → JS 数据传递走 JSON 字符串。脚本内部用 JSON.parse / JSON.stringify。
/// All C# → JS data passing uses JSON strings. Scripts internally use JSON.parse / JSON.stringify.
/// </summary>
internal static class ScriptEngine
{
    public static readonly HttpClient HttpClient = new();

    /// <summary>
    /// 创建配置好的 Jint engine，开启 TaskInterop 支持 async/await。
    /// Create a configured Jint engine with TaskInterop enabled for async/await.
    /// </summary>
    /// <param name="label">脚本标签（用于日志） / Script label (for logging).</param>
    /// <returns>配置好的 Engine 实例 / Configured Engine instance.</returns>
    /// <remarks>
    /// JS 边界只接受 string，避免 STJ 反射路径。
    /// The C# ↔ JS boundary only passes strings so STJ reflection paths are not needed.
    /// </remarks>
    public static Engine Create(string label)
    {
        var engine = new Engine(options =>
        {
            options.ExperimentalFeatures = Jint.ExperimentalFeature.TaskInterop;
        });

        engine.SetValue("log", (Delegate)new Action<string>(msg => Log(label, msg)));

        engine.SetValue("httpGet", (Delegate)new Func<string, string>(url => HttpGet(label, url)));

        engine.SetValue("httpGetWithHeaders", (Delegate)new Func<string, string, string>((url, headersJson) => HttpGetWithHeaders(label, url, headersJson)));

        engine.SetValue("createDirectory", (Delegate)new Func<string, string?>(path => CreateDirectory(path)));

        engine.SetValue("removeFile", (Delegate)new Func<string, string?>(path => RemoveFile(path)));

        engine.SetValue("writeFile", (Delegate)new Action<string, string>((path, content) => WriteFile(path, content)));

        engine.SetValue("fileExists", (Delegate)new Func<string, bool>(FileExists));

        engine.SetValue("directoryExists", (Delegate)new Func<string, bool>(DirectoryExists));

        engine.SetValue("removeDirectory", (Delegate)new Func<string, string?>(RemoveDirectory));

        engine.SetValue("directoryList", (Delegate)new Func<string, string>(DirectoryList));

        engine.SetValue("logError", (Delegate)new Action<string>(LogError));

        return engine;
    }

    /// <summary>
    /// 调用脚本中定义的 async 函数并 await 它的返回值。
    /// Invoke a user-defined async function defined via <c>engine.Execute</c>
    /// and await its promise, returning the resolved value (or throwing if
    /// the promise rejects).
    /// </summary>
    /// <param name="engine">Jint engine / Jint engine.</param>
    /// <param name="functionName">脚本中声明的函数名 / Name of the function declared in the script.</param>
    /// <param name="args">传递给函数的参数 / Arguments passed to the function.</param>
    /// <returns>函数返回的 JsValue（已 await）/ JsValue returned by the function (already awaited).</returns>
    public static async Task<JsValue> InvokeAsync(Engine engine, string functionName, params object[] args)
    {
        try
        {
            return await engine.InvokeAsync(functionName, args);
        }
        catch (Jint.Runtime.PromiseRejectedException ex)
        {
            throw new InvalidOperationException($"{functionName}() rejected: {ex.Message}", ex);
        }
    }

    private static void Log(string label, string msg) =>
        Console.WriteLine($"[{label}] {msg}");

    private static string HttpGet(string label, string url)
    {
        Console.WriteLine($"{label}: http get request: {url}");
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", Config.LoadUserAgent());
        using var resp = HttpClient.SendAsync(req).GetAwaiter().GetResult();
        resp.EnsureSuccessStatusCode();
        return resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    }

    private static string HttpGetWithHeaders(string label, string url, string headersJson)
    {
        Console.WriteLine($"{label}: http get request: {url}");
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", Config.LoadUserAgent());

        try
        {
            var parsed = JsonNode.Parse(headersJson) as JsonObject;
            if (parsed != null)
            {
                foreach (var kv in parsed)
                {
                    var key = kv.Key;
                    var value = kv.Value?.GetValue<string>();
                    if (string.IsNullOrEmpty(value)) continue;
                    req.Headers.Remove(key);
                    req.Headers.TryAddWithoutValidation(key, value);
                }
            }
        }
        catch
        {
            // ignore malformed headers JSON
        }

        using var resp = HttpClient.SendAsync(req).GetAwaiter().GetResult();
        resp.EnsureSuccessStatusCode();
        return resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    }

    private static string? CreateDirectory(string path)
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

    private static string? RemoveFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static string? RemoveDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static string DirectoryList(string path)
    {
        try
        {
            var items = new JsonArray();

            if (Directory.Exists(path))
            {
                foreach (var dirPath in Directory.EnumerateDirectories(path))
                {
                    var info = new DirectoryInfo(dirPath);
                    items.Add((JsonNode)new JsonObject
                    {
                        ["name"] = info.Name,
                        ["isDirectory"] = true,
                        ["size"] = 0L
                    });
                }

                foreach (var filePath in Directory.EnumerateFiles(path))
                {
                    var info = new FileInfo(filePath);
                    items.Add((JsonNode)new JsonObject
                    {
                        ["name"] = info.Name,
                        ["isDirectory"] = false,
                        ["size"] = info.Length
                    });
                }
            }

            var result = new JsonObject
            {
                ["path"] = path,
                ["items"] = items
            };

            return result.ToJsonString();
        }
        catch (Exception ex)
        {
            return new JsonObject
            {
                ["error"] = ex.Message
            }.ToJsonString();
        }
    }

    private static void WriteFile(string path, string content) =>
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    private static bool FileExists(string path) => File.Exists(path);

    private static bool DirectoryExists(string path) => Directory.Exists(path);

    private static void LogError(string msg) => Console.Error.WriteLine(msg);
}
