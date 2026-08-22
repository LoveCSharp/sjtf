namespace Sjtf.Cli;

/// <summary>
/// 文件下载助手（aria2 调度入口）。底层 HTTP 下载逻辑下沉到 <see cref="HttpFileDownloader"/>。
/// File download helpers (aria2 dispatcher). Underlying HTTP download logic lives in <see cref="HttpFileDownloader"/>.
/// </summary>
internal static class Downloader
{
    /// <summary>
    /// 异步下载文件到指定路径（单线程，委托给 <see cref="HttpFileDownloader.DownloadAsync"/>）/ Download a file asynchronously to the specified path (single-stream, delegates to <see cref="HttpFileDownloader.DownloadAsync"/>).
    /// </summary>
    /// <param name="url">要下载的 URL / URL to download.</param>
    /// <param name="destFile">目标文件路径 / Destination file path.</param>
    /// <param name="label">进度条标签 / Progress bar label.</param>
    public static Task DownloadFileAsync(string url, string destFile, string? label = null)
        => HttpFileDownloader.DownloadAsync(url, destFile, label);

    /// <summary>
    /// aria2 调度入口：若启用 aria2 则用 aria2c，否则回退到内置多线程分块下载。
    /// aria2 dispatcher: use aria2c if enabled, otherwise fall back to built-in chunked download.
    /// </summary>
    /// <param name="url">要下载的 URL / URL to download.</param>
    /// <param name="destFile">目标文件路径 / Destination file path.</param>
    /// <param name="label">进度条标签 / Progress bar label.</param>
    /// <param name="maxConnections">最大连接数（线程数）/ Maximum connections (threads).</param>
    /// <param name="splitCount">目标分块数 / Target number of chunks.</param>
    /// <param name="minSplitSizeMB">最小分块大小（MB）/ Minimum chunk size (MB).</param>
    public static async Task DownloadFileAsync(string url, string destFile, string? label,
        int maxConnections, int splitCount, int minSplitSizeMB)
    {
        Console.WriteLine($"{label ?? "downloading"}: {url}");

        maxConnections = Math.Clamp(maxConnections, 1, 16);
        splitCount = Math.Clamp(splitCount, 1, 16);
        minSplitSizeMB = Math.Clamp(minSplitSizeMB, 1, 1024);

        if (Config.LoadAria2Enable())
        {
            try
            {
                var aria2cPath = await Aria2.FindOrDownloadAria2Async();
                if (!string.IsNullOrEmpty(aria2cPath) && File.Exists(aria2cPath))
                {
                    await Aria2.RunAsync(aria2cPath, url, destFile, label, maxConnections, splitCount, minSplitSizeMB);
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"{Aria2.LogPrefix} {ex.Message}");
                throw;
            }
        }

        await DownloadFileBuiltinAsync(url, destFile, label, maxConnections, splitCount, minSplitSizeMB);
    }

    /// <summary>
    /// 使用内置多线程分块下载文件（委托给 <see cref="HttpFileDownloader.DownloadChunkedAsync"/>）/ Download file using built-in multi-threaded chunk downloader (delegates to <see cref="HttpFileDownloader.DownloadChunkedAsync"/>).
    /// </summary>
    public static Task DownloadFileBuiltinAsync(string url, string destFile, string? label,
        int maxConnections, int splitCount, int minSplitSizeMB)
        => HttpFileDownloader.DownloadChunkedAsync(url, destFile, label, maxConnections, splitCount, minSplitSizeMB);
}
