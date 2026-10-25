using System.Diagnostics;
using SkiaSharp;

namespace HexaWall;

internal sealed class TransitionEngine : IDisposable
{
    private readonly Settings settings;
    private readonly DesktopWallpaper wallpaper;
    private readonly GamingMonitor gaming;
    private readonly List<TransitionForm> forms = [];
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 15 };
    private readonly Stopwatch clock = new();
    private CancellationTokenSource? cancellation;
    private bool busy, disposed, applied, started;
    private string? pendingPath, pendingMonitor;
    private nint host;
    public event Action<Exception>? Failed;

    public TransitionEngine(Settings settings, DesktopWallpaper wallpaper, GamingMonitor gaming)
    {
        this.settings = settings; this.wallpaper = wallpaper; this.gaming = gaming;
        timer.Tick += Tick;
        gaming.Changed += OnGaming;
    }
    private void OnGaming(bool value) { if (value && settings.PauseInFullscreen) Cancel(); }

    public async Task ApplyAsync(string path, Point cursor)
    {
        if (busy || disposed) return;
        busy = true; applied = false; started = false;
        var source = new CancellationTokenSource(); cancellation = source;
        var token = source.Token;
        var buffers = new List<(MonitorWallpaper Monitor, SKBitmap? Old, SKBitmap Next)>();
        try
        {
            string input = path;
            path = await Task.Run(() => WallpaperFile.Prepare(input, token), token);
            token.ThrowIfCancellationRequested();
            var monitors = wallpaper.Read();
            var target = monitors.FirstOrDefault(m => m.Bounds.Contains(cursor)) ?? monitors.FirstOrDefault()
                ?? throw new InvalidOperationException("No hay monitores conectados.");
            pendingPath = path; pendingMonitor = settings.AllMonitors ? null : target.Id;
            if (settings.PauseInFullscreen && gaming.Refresh()) { wallpaper.Apply(path, pendingMonitor); applied = true; return; }
            host = WorkerW.FindHost();
            if (host == 0) { wallpaper.Apply(path, pendingMonitor); applied = true; return; }
            var targets = settings.AllMonitors ? monitors : [target];
            var desktop = monitors.Select(m => m.Bounds).Aggregate(Rectangle.Union);
            await Task.Run(() =>
            {
                foreach (var monitor in targets)
                {
                    token.ThrowIfCancellationRequested();
                    SKBitmap? old = null;
                    try
                    {
                        if (File.Exists(monitor.Path)) old = ImageLoader.Load(monitor.Path, Math.Min(4096, Math.Max(desktop.Width, desktop.Height)));
                        var next = ImageLoader.Load(path, Math.Min(4096, Math.Max(monitor.Bounds.Width, monitor.Bounds.Height)));
                        buffers.Add((monitor, old, next));
                    }
                    catch { old?.Dispose(); throw; }
                }
            }, token);
            token.ThrowIfCancellationRequested();
            if (settings.PauseInFullscreen && gaming.Refresh()) { wallpaper.Apply(path, pendingMonitor); applied = true; return; }
            while (buffers.Count > 0)
            {
                var buffer = buffers[0];
                var form = new TransitionForm(buffer.Monitor, desktop, buffer.Old, buffer.Next, settings.Transition, cursor);
                forms.Add(form); buffers.RemoveAt(0); form.FirstPaint += TryStart;
                form.RenderFailed += e =>
                {
                    try { if (!applied) { wallpaper.Apply(pendingPath!, pendingMonitor); applied = true; } }
                    catch (Exception error) { Log.Error(error); }
                    Log.Error(e); Failed?.Invoke(e); Cleanup();
                };
                WorkerW.Attach(form, host, buffer.Monitor.Bounds);
            }
            foreach (var form in forms) { form.Show(); form.Refresh(); }
            // A finite watchdog also handles a driver that never delivers the first paint.
            clock.Restart(); timer.Start();
            TryStart();
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Cleanup();
            if (!applied && !disposed)
            {
                try { wallpaper.Apply(path, pendingMonitor); applied = true; }
                catch (Exception fallback) { Log.Error(fallback); }
            }
            Log.Error(e); Failed?.Invoke(e);
        }
        finally
        {
            foreach (var buffer in buffers) { buffer.Old?.Dispose(); buffer.Next.Dispose(); }
            if (forms.Count == 0) busy = false;
            // Keep cancellation alive until the animation completes; it also invalidates async preparation.
            if (!busy) { if (ReferenceEquals(cancellation, source)) cancellation = null; source.Dispose(); }
        }
    }

    private void TryStart()
    {
        if (disposed || !busy || started || forms.Count == 0 || forms.Any(f => !f.HasPainted)) return;
        try
        {
            // The old image already covers the shell. Commit underneath, then reveal the new image.
            wallpaper.Apply(pendingPath!, pendingMonitor); applied = true;
            started = true; clock.Restart(); timer.Stop(); Application.Idle += RenderOnIdle;
        }
        catch (Exception e) { Log.Error(e); Failed?.Invoke(e); Cleanup(); }
    }

    private void RenderOnIdle(object? sender, EventArgs e)
    {
        // SwapBuffers is paced by the display driver. Return to the message loop as soon as input arrives.
        // This subscription exists only for the short transition, including the final settling frame.
        while (started && forms.Count > 0 && !Native.PeekMessage(out _, 0, 0, 0, 0))
            Tick(sender, e);
    }

    private void Tick(object? sender, EventArgs e)
    {
        if (!Native.IsWindow(host) || (settings.PauseInFullscreen && Native.IsBusy())) { Cancel(); return; }
        if (!started)
        {
            if (clock.ElapsedMilliseconds > 1500)
            {
                try { if (!applied) wallpaper.Apply(pendingPath!, pendingMonitor); }
                catch (Exception error) { Log.Error(error); Failed?.Invoke(error); }
                Cleanup();
            }
            return;
        }
        float t = Math.Clamp((float)clock.Elapsed.TotalMilliseconds / settings.TransitionMilliseconds, 0, 1);
        foreach (var form in forms) form.Frame(t);
        // Retain the final frame briefly while Explorer installs its static wallpaper.
        if (clock.ElapsedMilliseconds > settings.TransitionMilliseconds + 180) Cleanup();
    }

    public void Cancel()
    {
        cancellation?.Cancel();
        // Already committed transitions keep the selected static wallpaper. Preparation is simply cancelled.
        if (forms.Count > 0) Cleanup();
    }

    private void Cleanup()
    {
        timer.Stop();
        Application.Idle -= RenderOnIdle; started = false;
        foreach (var form in forms) form.Dispose(); forms.Clear();
        cancellation?.Dispose(); cancellation = null;
        busy = false;
    }
    public void Dispose()
    {
        disposed = true; gaming.Changed -= OnGaming; cancellation?.Cancel(); Cleanup(); timer.Dispose();
    }
}
