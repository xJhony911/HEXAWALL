using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using SkiaSharp;
using System.Windows.Media.Imaging;

namespace HexaWall;

internal sealed record WallpaperEntry(string Path, string Key, string Thumbnail);

internal sealed class ThumbnailCache : IDisposable
{
    private sealed record Resident(BitmapSource Bitmap, long Bytes, LinkedListNode<string> Node);
    private readonly object gate = new();
    private readonly Dictionary<string, Resident> memory = new();
    private readonly LinkedList<string> lru = new();
    private long memoryBytes;
    private readonly ConcurrentDictionary<string, byte> pending = new();
    private readonly ConcurrentDictionary<string, byte> failed = new();
    private readonly Channel<WallpaperEntry> requests = Channel.CreateBounded<WallpaperEntry>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.Wait });
    private readonly Channel<bool> scans = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.Wait });
    private readonly CancellationTokenSource stop = new();
    private readonly SemaphoreSlim diskGate = new(1, 1);
    private readonly string directory;
    private readonly string folder;
    private readonly long memoryLimit, diskLimit;
    private readonly FileSystemWatcher? watcher;
    private readonly Task scanTask, loadTask;
    private WallpaperEntry[] entries = [];
    private bool paused;
    private int generation;
    public WallpaperEntry[] Entries => Volatile.Read(ref entries);
    public int ResidentCount { get { lock (gate) return memory.Count; } }
    public event Action? Changed;
    public event Action? Ready;

    public ThumbnailCache(Settings settings, string? cacheDirectory = null)
    {
        directory = cacheDirectory ?? System.IO.Path.Combine(Settings.DataDirectory, "thumbnails-v2-960x540");
        folder = settings.Folder;
        memoryLimit = settings.ThumbnailMemoryMiB * 1024L * 1024;
        diskLimit = settings.DiskCacheMiB * 1024L * 1024;
        Directory.CreateDirectory(directory);
        if (Directory.Exists(folder))
        {
            watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 16384
            };
            watcher.Created += OnChange; watcher.Changed += OnChange; watcher.Deleted += OnChange;
            watcher.Renamed += OnChange;
            watcher.Error += (_, _) => scans.Writer.TryWrite(true); // Overflow: reconcile entire directory.
            watcher.EnableRaisingEvents = true;
        }
        scanTask = Task.Run(ScanLoop);
        loadTask = Task.Run(LoadLoop);
        scans.Writer.TryWrite(true);
    }

    private void OnChange(object sender, FileSystemEventArgs e) => scans.Writer.TryWrite(true);
    public void Rescan() => scans.Writer.TryWrite(true);

    private async Task ScanLoop()
    {
        try
        {
            await foreach (var signal in scans.Reader.ReadAllAsync(stop.Token))
            {
                await Task.Delay(250, stop.Token);
                while (scans.Reader.TryRead(out _)) { }
                failed.Clear();
                try
                {
                    var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".bmp" };
                    var result = new List<WallpaperEntry>();
                    if (Directory.Exists(folder))
                    {
                        foreach (string path in Directory.EnumerateFiles(folder))
                        {
                            stop.Token.ThrowIfCancellationRequested();
                            if (!extensions.Contains(System.IO.Path.GetExtension(path))) continue;
                            try
                            {
                                var file = new FileInfo(path);
                                string identity = $"v2-960x540-q94|{file.FullName.ToUpperInvariant()}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";
                                string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
                                result.Add(new(file.FullName, key, System.IO.Path.Combine(directory, key + ".webp")));
                            }
                            catch (IOException) { }
                            catch (UnauthorizedAccessException) { }
                        }
                    }
                    Volatile.Write(ref entries, result.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToArray());
                    Changed?.Invoke();
                    // Prewarm a bounded first page, never decode originals in the UI thread.
                    foreach (var entry in Entries.Take(6))
                    {
                        lock (gate) { if (paused) break; }
                        await EnsureDisk(entry);
                    }
                    PruneDisk();
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Error(e); }
            }
        }
        catch (OperationCanceledException) { }
    }

    public void Pause(bool value)
    {
        lock (gate)
        {
            paused = value;
            if (value) { generation++; ClearLocked(); }
        }
    }

    public void ClearMemory() { lock (gate) { generation++; ClearLocked(); } }
    private void ClearLocked()
    {
        memory.Clear(); lru.Clear(); memoryBytes = 0;
    }

    // Frozen BitmapSources can be consumed across threads; only visible WPF visuals retain them.
    public BitmapSource? Request(WallpaperEntry entry)
    {
        lock (gate)
        {
            if (memory.TryGetValue(entry.Key, out var resident))
            {
                lru.Remove(resident.Node); lru.AddFirst(resident.Node);
                return resident.Bitmap;
            }
            if (!paused && !failed.ContainsKey(entry.Key) && pending.TryAdd(entry.Key, 0) && !requests.Writer.TryWrite(entry))
                pending.TryRemove(entry.Key, out _);
            return null;
        }
    }

    private async Task LoadLoop()
    {
        try
        {
            await foreach (var entry in requests.Reader.ReadAllAsync(stop.Token))
            {
                try
                {
                    int ticket;
                    lock (gate) { if (paused) continue; ticket = generation; }
                    if (!await EnsureDisk(entry)) { failed.TryAdd(entry.Key, 0); continue; }
                    using var input = File.OpenRead(entry.Thumbnail);
                    using SKBitmap? bitmap = SKBitmap.Decode(input);
                    if (bitmap == null)
                    {
                        failed.TryAdd(entry.Key, 0);
                        throw new IOException("No se pudo leer la miniatura WebP: " + entry.Thumbnail);
                    }
                    var frozen = ImageLoader.Freeze(bitmap);
                    lock (gate)
                    {
                        if (!paused && ticket == generation && !memory.ContainsKey(entry.Key))
                        {
                            memory[entry.Key] = new(frozen, bitmap.ByteCount, lru.AddFirst(entry.Key));
                            memoryBytes += bitmap.ByteCount;
                            while (memoryBytes > memoryLimit && lru.Last is { } last)
                            {
                                var evicted = memory[last.Value];
                                memoryBytes -= evicted.Bytes;
                                memory.Remove(last.Value); lru.RemoveLast();
                            }
                        }
                    }
                    Ready?.Invoke();
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { Log.Error(e); }
                finally { pending.TryRemove(entry.Key, out _); }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task<bool> EnsureDisk(WallpaperEntry entry)
    {
        await diskGate.WaitAsync(stop.Token);
        try
        {
            if (File.Exists(entry.Thumbnail)) return true;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                stop.Token.ThrowIfCancellationRequested();
                try
                {
                    using var bitmap = ImageLoader.Preview(entry.Path, 960, 540);
                    using var image = SKImage.FromBitmap(bitmap);
                    using var data = image.Encode(SKEncodedImageFormat.Webp, 94);
                    string temp = entry.Thumbnail + ".tmp";
                    using (var file = File.Create(temp)) data.SaveTo(file);
                    File.Move(temp, entry.Thumbnail, true);
                    PruneDisk();
                    return true;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    if (attempt == 2) { Log.Error(e); return false; }
                    await Task.Delay(150 * (attempt + 1), stop.Token);
                }
            }
            return false;
        }
        finally { diskGate.Release(); }
    }

    private void PruneDisk()
    {
        try
        {
            var files = new DirectoryInfo(directory).GetFiles("*.webp").OrderBy(x => x.LastWriteTimeUtc).ToArray();
            long size = files.Sum(x => x.Length);
            foreach (var file in files)
            {
                if (size <= diskLimit) break;
                long length = file.Length; file.Delete(); size -= length;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        watcher?.Dispose(); stop.Cancel();
        requests.Writer.TryComplete(); scans.Writer.TryComplete();
        // Workers never depend on the UI thread, so joining here cannot deadlock a posted repaint.
        try { Task.WhenAll(scanTask, loadTask).GetAwaiter().GetResult(); }
        finally { ClearMemory(); diskGate.Dispose(); stop.Dispose(); }
    }
}
