namespace Sjtf;

/// <summary>
/// 多线程分块下载进度聚合器（线程安全）/ Multi-thread chunk download progress aggregator (thread-safe).
/// </summary>
internal sealed class ChunkProgress : IDisposable
{
    private readonly long _totalSize;
    private long _downloaded;
    private int _completedChunks;
    private readonly int _totalChunks;
    private readonly string _label;
    private Task? _progressTask;
    private volatile bool _disposed;
    private readonly object _renderLock = new();

    public ChunkProgress(string label, long totalSize, int totalChunks)
    {
        _label = label;
        _totalSize = totalSize;
        _totalChunks = totalChunks;
    }

    public void Report(long bytes)
    {
        Interlocked.Add(ref _downloaded, bytes);
    }

    public void CompleteChunk()
    {
        Interlocked.Increment(ref _completedChunks);
    }

    public void StartProgressLoop()
    {
        _progressTask = Task.Run(() => ProgressLoopAsync());
    }

    public void Complete()
    {
        _disposed = true;
        try { _progressTask?.Wait(3000); } catch { }

        lock (_renderLock)
        {
            var downloaded = Interlocked.Read(ref _downloaded);
            var percent = _totalSize > 0 ? 100 : 0;
            var bar = new string('█', 20);
            var text = $"{_label} [{bar}] {percent,3}% {Formatters.FormatSize(downloaded)}/{Formatters.FormatSize(_totalSize)} [{_totalChunks}/{_totalChunks} chunks]";
            if (text.Length < ConsoleProgress._lastProgressLength) text += new string(' ', ConsoleProgress._lastProgressLength - text.Length);
            Console.WriteLine($"\r{text}");
            ConsoleProgress._lastProgressLength = 0;
        }
    }

    private async Task ProgressLoopAsync()
    {
        var samples = new Queue<(DateTime Time, long Bytes)>();
        const double windowSec = 2.0;
        const int barWidth = 20;
        var lastUpdate = DateTime.UtcNow;
        var lastLength = 0;

        while (!_disposed)
        {
            await Task.Delay(100).ConfigureAwait(false);

            var downloaded = Interlocked.Read(ref _downloaded);
            var completed = Volatile.Read(ref _completedChunks);
            var now = DateTime.UtcNow;

            lock (_renderLock)
            {
                if ((now - lastUpdate).TotalMilliseconds < 100 && completed < _totalChunks && !_disposed)
                    continue;
                lastUpdate = now;

                samples.Enqueue((now, downloaded));
                while (samples.Count > 0 && samples.Peek().Time < now - TimeSpan.FromSeconds(windowSec))
                    samples.Dequeue();

                double speed = 0;
                if (samples.Count >= 2)
                {
                    var first = samples.Peek();
                    var last = samples.Last();
                    var dt = (last.Time - first.Time).TotalSeconds;
                    if (dt > 0) speed = (last.Bytes - first.Bytes) / dt;
                }

                var percent = _totalSize > 0 ? (int)(100.0 * downloaded / _totalSize) : 0;
                var filled = _totalSize > 0 ? (int)(barWidth * downloaded / _totalSize) : 0;
                if (filled > barWidth) filled = barWidth;
                if (filled < 0) filled = 0;
                var bar = new string('█', filled) + new string(' ', barWidth - filled);
                var speedStr = $"{Formatters.FormatSize((long)speed)}/s";
                var text = $"{_label} [{bar}] {percent,3}% {Formatters.FormatSize(downloaded)}/{Formatters.FormatSize(_totalSize)} ({speedStr}) [{completed}/{_totalChunks} chunks]";

                if (text.Length < lastLength) text += new string(' ', lastLength - text.Length);
                lastLength = text.Length;
                ConsoleProgress._lastProgressLength = lastLength;
                Console.Write($"\r{text}");
            }

            if (completed >= _totalChunks) break;
        }

        if (_disposed) return;

        lock (_renderLock)
        {
            var downloaded = Interlocked.Read(ref _downloaded);
            var finalSpeed = 0.0;
            if (samples.Count >= 2)
            {
                var first = samples.Peek();
                var last = samples.Last();
                var dt = (last.Time - first.Time).TotalSeconds;
                if (dt > 0) finalSpeed = (last.Bytes - first.Bytes) / dt;
            }
            var percent = _totalSize > 0 ? 100 : 0;
            var bar = new string('█', barWidth);
            var text = $"{_label} [{bar}] {percent,3}% {Formatters.FormatSize(downloaded)}/{Formatters.FormatSize(_totalSize)} ({Formatters.FormatSize((long)finalSpeed)}/s) [{_totalChunks}/{_totalChunks} chunks]";
            if (text.Length < ConsoleProgress._lastProgressLength) text += new string(' ', ConsoleProgress._lastProgressLength - text.Length);
            Console.WriteLine($"\r{text}");
            ConsoleProgress._lastProgressLength = 0;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _progressTask?.Wait(3000); } catch { }
    }
}