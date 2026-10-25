using System.Threading.Channels;
using System.Windows.Media.Imaging;

namespace HexaWall;

internal sealed class SelectedPreview : IDisposable
{
    private sealed record Job(WallpaperEntry Entry, int Width, int Height, int Version);
    private sealed record Result(string Key, BitmapSource Source);
    private readonly Channel<Job> queue = Channel.CreateBounded<Job>(new BoundedChannelOptions(1)
        { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly CancellationTokenSource stop = new();
    private readonly object gate = new();
    private readonly Task worker;
    private Result? current;
    private string requested = "";
    private int version;
    public event Action? Ready;

    public SelectedPreview() => worker = Task.Run(Work);
    public BitmapSource? Get(string key) => Volatile.Read(ref current) is { } value && value.Key == key ? value.Source : null;

    public void Request(WallpaperEntry entry, int width)
    {
        width = Math.Clamp(width, 1280, 3840);
        int height = (int)Math.Ceiling(width * 9d / 16);
        string identity = $"{entry.Key}|{width}|{height}";
        lock (gate)
        {
            if (requested == identity) return;
            requested = identity;
            Volatile.Write(ref current, null);
            queue.Writer.TryWrite(new(entry, width, height, Interlocked.Increment(ref version)));
        }
    }

    private async Task Work()
    {
        try
        {
            await foreach (var job in queue.Reader.ReadAllAsync(stop.Token))
            {
                if (job.Version != Volatile.Read(ref version)) continue;
                try
                {
                    using var bitmap = ImageLoader.Preview(job.Entry.Path, job.Width, job.Height);
                    if (stop.IsCancellationRequested || job.Version != Volatile.Read(ref version)) continue;
                    var source = ImageLoader.Freeze(bitmap);
                    lock (gate)
                    {
                        if (job.Version != version || stop.IsCancellationRequested) continue;
                        Volatile.Write(ref current, new(job.Entry.Key, source));
                    }
                    Ready?.Invoke();
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { Log.Error(e); }
            }
        }
        catch (OperationCanceledException) { }
    }

    public void Clear()
    {
        lock (gate)
        {
            Interlocked.Increment(ref version); requested = "";
            Volatile.Write(ref current, null);
        }
    }
    public void Dispose()
    {
        Clear(); stop.Cancel(); queue.Writer.TryComplete();
        worker.GetAwaiter().GetResult(); stop.Dispose();
    }
}
