using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
namespace HexaWall;
internal sealed record LiteItem(string Path, string Key);
internal sealed record LiteImages(int Version, Dictionary<int, Bitmap> Bitmaps) : IDisposable
{
    public void Dispose() { foreach (var bitmap in Bitmaps.Values) bitmap.Dispose(); Bitmaps.Clear(); }
}
internal sealed class LiteCache : IDisposable
{
    private sealed record Job(int Version, LiteItem[] Items, int[] Indices, int Selected);
    private readonly Channel<Job> jobs = Channel.CreateBounded<Job>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly CancellationTokenSource stop = new();
    private readonly Task worker;
    private int version;
    private readonly string directory = Path.Combine(Settings.DataDirectory, "thumbnails-lite-v1");
    public event Action<LiteImages>? Ready;
    public LiteCache() { Directory.CreateDirectory(directory); worker = Task.Run(Work); }
    public static LiteItem[] Scan(string folder)
    {
        if (!Directory.Exists(folder)) return [];
        string[] extensions = [".jpg", ".jpeg", ".png", ".bmp", ".webp"];
        return Directory.EnumerateFiles(folder).Where(p => extensions.Contains(Path.GetExtension(p).ToLowerInvariant()))
            .Take(20000).Order(StringComparer.OrdinalIgnoreCase).Select(p =>
            {
                var info = new FileInfo(p);
                string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}")));
                return new LiteItem(p, key);
            }).ToArray();
    }
    public int Request(LiteItem[] items, int[] indices, int selected)
    {
        int ticket = Interlocked.Increment(ref version); jobs.Writer.TryWrite(new(ticket, items, indices, selected)); return ticket;
    }
    private async Task Work()
    {
        try
        {
            await foreach (var job in jobs.Reader.ReadAllAsync(stop.Token))
            {
                var images = new LiteImages(job.Version, []);
                try
                {
                    foreach (int index in job.Indices.OrderBy(i => i == job.Selected ? 0 : 1))
                    {
                        if (stop.IsCancellationRequested || job.Version != Volatile.Read(ref version)) break;
                        var entry = job.Items[index]; string disk = Path.Combine(directory, entry.Key + ".jpg");
                        try
                        {
                            if (!File.Exists(disk))
                            {
                                using var original = WicImage.Load(entry.Path, 960, 960);
                                string temporary = disk + ".tmp";
                                original.Save(temporary, ImageFormat.Jpeg); File.Move(temporary, disk, true);
                            }
                            images.Bitmaps[index] = WicImage.Load(disk, index == job.Selected ? 960 : 320, index == job.Selected ? 540 : 180);
                        }
                        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or ArgumentException) { Log.Error(e); }
                    }
                    if (!stop.IsCancellationRequested && job.Version == Volatile.Read(ref version) && Ready is { } ready)
                    { ready(images); images = null!; }
                }
                finally { images?.Dispose(); }
                Prune();
            }
        }
        catch (OperationCanceledException) { }
    }
    private void Prune()
    {
        try
        {
            var files = new DirectoryInfo(directory).GetFiles("*.jpg").OrderBy(f => f.LastWriteTimeUtc).ToArray();
            long bytes = files.Sum(f => f.Length);
            foreach (var file in files) { if (bytes <= 128L * 1024 * 1024) break; bytes -= file.Length; file.Delete(); }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    public void Dispose() { Interlocked.Increment(ref version); stop.Cancel(); jobs.Writer.TryComplete(); worker.GetAwaiter().GetResult(); stop.Dispose(); }
}
