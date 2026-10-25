using System.IO;
using System.Diagnostics;
using HexaWall;
using SkiaSharp;
using System.Windows.Media.Imaging;
using W = System.Windows;
using M = System.Windows.Media;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string root = Path.Combine(Path.GetTempPath(), "HexaWall.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string? oldData = Environment.GetEnvironmentVariable("HEXAWALL_DATA_HOME");
        Environment.SetEnvironmentVariable("HEXAWALL_DATA_HOME", Path.Combine(root, "data"));
        try
        {
            if (args.Length == 2 && args[0] == "--write-icon")
            {
                string iconPath = Path.GetFullPath(args[1]); Directory.CreateDirectory(Path.GetDirectoryName(iconPath)!);
                using var icon = IconFactory.Create(); using var output = File.Create(iconPath); icon.Save(output);
                Console.WriteLine("Icon generated: " + iconPath); return 0;
            }
            if (args.Contains("--ui") || args.Contains("--gpu")) return UiSmoke(root, args.Last());
            CoreTests(root).GetAwaiter().GetResult();
            Console.WriteLine("PASS: all functional tests"); return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            string log = Path.Combine(root, "data", "error.log");
            if (File.Exists(log)) Console.Error.WriteLine(File.ReadAllText(log));
            return 1;
        }
        finally
        {
            Environment.SetEnvironmentVariable("HEXAWALL_DATA_HOME", oldData);
            Directory.Delete(root, true);
        }
    }

    private static async Task CoreTests(string root)
    {
        using (var hex = ImageLoader.Hexagon(100, 100, 50))
        {
            Assert(hex.Contains(100, 100) && !hex.Contains(50, 50), "hexagonal hit geometry");
        }
        double RunAt(int hz)
        {
            double value = 0;
            for (int i = 0; i < hz; i++) value = OverlayWindow.Approach(value, 1, 19, 1d / hz);
            return value;
        }
        Assert(Math.Abs(RunAt(60) - RunAt(165)) < 1e-10 && Math.Abs(RunAt(144) - RunAt(165)) < 1e-10,
            "deltaTime interpolation is independent of 60/144/165 Hz");
        using (var icon = IconFactory.Create())
        using (var stream = new MemoryStream())
        {
            icon.Save(stream); byte[] bytes = stream.ToArray();
            Assert(BitConverter.ToUInt16(bytes, 4) == 8, "native icon contains eight vector-rendered sizes");
        }
        var colors = new DarkColorTable();
        Assert(colors.ToolStripDropDownBackground.ToArgb() == Color.FromArgb(24, 26, 32).ToArgb(), "dark tray background");
        Assert(colors.MenuBorder.ToArgb() == Color.FromArgb(44, 232, 198).ToArgb(), "neon tray border");
        string folder = Path.Combine(root, "images"); Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "one.png"); WriteImage(path, 0);
        using (var preview = ImageLoader.Preview(path, 960, 540))
        {
            Assert(preview.Width == 960 && preview.Height == 540, "960x540 thumbnail dimensions");
            var frozen = ImageLoader.Freeze(preview); Assert(frozen.IsFrozen, "thumbnail safe across threads");
        }
        using (var cache = new ThumbnailCache(new Settings { Folder = folder, ThumbnailMemoryMiB = 8 }, Path.Combine(root, "cache")))
        {
            await Until(() => cache.Entries.Length == 1, "initial folder index");
            var entry = cache.Entries[0];
            await Until(() => cache.Request(entry) != null, "asynchronous high-resolution thumbnail");
            var image = cache.Request(entry)!;
            Assert(image.PixelWidth == 960 && image.PixelHeight == 540, "resident thumbnail resolution");
            using (var selected = new SelectedPreview())
            {
                selected.Request(entry, 1280);
                await Until(() => selected.Get(entry.Key) != null, "selected original loaded asynchronously");
                Assert(selected.Get(entry.Key)!.PixelWidth == 1280 && selected.Get(entry.Key)!.PixelHeight == 720,
                    "selected preview is at least 1280x720");
                selected.Request(entry, 1920);
                selected.Clear();
                await Task.Delay(300);
                Assert(selected.Get(entry.Key) == null, "cancelled HQ preview cannot publish stale result");
            }
            cache.Pause(true); Assert(cache.Request(entry) == null && cache.ResidentCount == 0, "gaming cache eviction");
            cache.Pause(false);
            WriteImage(path, 7); File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(2));
            await Until(() => cache.Entries.Length == 1 && cache.Entries[0].Key != entry.Key, "watcher invalidates changed image");
            File.Delete(path); await Until(() => cache.Entries.Length == 0, "watcher detects deletion");
            WriteImage(Path.Combine(folder, "two.png"), 2);
            await Until(() => cache.Entries.Length == 1, "watcher detects new image");
        }
        var settings = new Settings { Folder = folder, HotKeyModifiers = 7, HotKeyVirtualKey = 0x87 };
        using var hotkey = new HotKeyWindow(settings);
        using (var blocker = new HotKeyWindow(new Settings { HotKeyModifiers = 7, HotKeyVirtualKey = 0x86 }))
        {
            Assert(!hotkey.TryChange(7, 0x86, out _) && hotkey.VirtualKey == 0x87, "conflicting hotkey preserves existing registration");
        }
        Assert(hotkey.TryChange(7, 0x86, out _), "hotkey rebind succeeds immediately");
        settings.HotKeyVirtualKey = 0x86;
        hotkey.Suspend(); Assert(hotkey.Resume(out _), "hotkey capture suspension resumes cleanly");
        var startup = new FakeStartup();
        var controller = new ConfigurationController(settings, hotkey, startup);
        var change = settings.Copy(); change.Layout = LayoutMode.Cards; change.TransitionMilliseconds = 900;
        change.StartWithWindows = true; change.PauseInFullscreen = false;
        Assert(controller.Apply(change, out _), "live settings commit");
        var saved = Settings.Load();
        Assert(saved.Layout == LayoutMode.Cards && saved.TransitionMilliseconds == 900 && !saved.PauseInFullscreen && startup.IsEnabled,
            "GUI settings persisted and autostart service updated");
        string goodData = Settings.DataDirectory;
        string blockerFile = Path.Combine(root, "not-a-directory"); File.WriteAllText(blockerFile, "test");
        Environment.SetEnvironmentVariable("HEXAWALL_DATA_HOME", blockerFile);
        change = settings.Copy(); change.StartWithWindows = false; change.HotKeyVirtualKey = 0x87;
        Assert(!controller.Apply(change, out _) && startup.IsEnabled && hotkey.VirtualKey == 0x86,
            "failed persistence rolls back autostart and hotkey");
        Environment.SetEnvironmentVariable("HEXAWALL_DATA_HOME", goodData);
    }

    private sealed class FakeStartup : IStartupRegistration
    {
        private string? value;
        public bool IsEnabled => value != null;
        public string? Read() => value;
        public void Set(bool enabled) => value = enabled ? "test.exe" : null;
        public void Restore(string? previous) => value = previous;
    }

    private static int UiSmoke(string root, string captureDirectory)
    {
        captureDirectory = Path.GetFullPath(captureDirectory); Directory.CreateDirectory(captureDirectory);
        string folder = Path.Combine(root, "images"); Directory.CreateDirectory(folder);
        for (int i = 0; i < 40; i++) WriteImage(Path.Combine(folder, $"Landscape {i + 1:00}.png"), i);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); Application.EnableVisualStyles();
        var wpf = new W.Application { ShutdownMode = W.ShutdownMode.OnExplicitShutdown };
        var settings = new Settings { Folder = folder, HotKeyModifiers = 7, HotKeyVirtualKey = 0x87 };
        using var cache = new ThumbnailCache(settings, Path.Combine(root, "cache"));
        using var hotkey = new HotKeyWindow(settings);
        var startup = new FakeStartup(); var controller = new ConfigurationController(settings, hotkey, startup);
        using var overlay = new OverlayWindow(settings, cache, dismissOnDeactivate: false);
        controller.Changed += _ => overlay.RefreshSettings();
        int stage = 0, result = 0;
        SettingsWindow? gui = null;
        var timeout = Stopwatch.StartNew();
        var target = Screen.FromPoint(Cursor.Position).Bounds;
        using var timer = new System.Windows.Forms.Timer { Interval = 350 };
        timer.Tick += (_, _) =>
        {
            try
            {
                if (timeout.Elapsed.TotalSeconds > 40) throw new TimeoutException($"UI stage {stage}, resident={cache.ResidentCount}, visible={overlay.IsVisible}");
                if (stage < 3 && (overlay.LoadedVisibleCount != overlay.VisibleCount || overlay.VisibleCount == 0 || overlay.ActiveImage == null || overlay.AnimationRunning)) return;
                switch (stage)
                {
                    case 0:
                        Assert(Native.GetWindowRect(overlay.Handle, out var rect) && rect.Rectangle == target, "overlay covers full physical monitor including taskbar");
                        Assert((Native.GetWindowLongPtr(overlay.Handle, Native.GWL_EXSTYLE).ToInt64() & 0x80000) != 0, "native window uses per-pixel layered composition");
                        overlay.Activate(); Native.SetForegroundWindow(overlay.Handle);
                        if (Native.GetForegroundWindow() == overlay.Handle)
                            Assert(!Native.IsBusy(), "own fullscreen overlay is excluded from gaming detection");
                        else Console.WriteLine("INFO: foreground focus is owned by another application; fullscreen self-exclusion assertion skipped.");
                        Capture(overlay.Surface, Path.Combine(captureDirectory, "Honeycomb.png"), true);
                        settings.Layout = LayoutMode.CoverFlow; overlay.RefreshSettings(); overlay.SelectIndex(4); stage++; break;
                    case 1:
                        Capture(overlay.Surface, Path.Combine(captureDirectory, "CoverFlow.png"), true);
                        settings.Layout = LayoutMode.Cards; overlay.RefreshSettings(); stage++; break;
                    case 2:
                        Capture(overlay.Surface, Path.Combine(captureDirectory, "Cards.png"), true);
                        overlay.SetConfigurationOpen(true);
                        gui = new SettingsWindow(settings, controller, hotkey) { Owner = overlay, Topmost = true, WindowStartupLocation = W.WindowStartupLocation.CenterOwner };
                        gui.Show(); stage++; break;
                    case 3:
                        gui!.DurationSlider.Value = 900; gui.AllMonitorsToggle.IsChecked = false;
                        gui.GamingToggle.IsChecked = false; gui.StartupToggle.IsChecked = true;
                        stage++; break;
                    case 4:
                        var saved = Settings.Load();
                        Assert(saved.TransitionMilliseconds == 900 && !saved.AllMonitors && !saved.PauseInFullscreen && startup.IsEnabled,
                            "real GUI controls persist without touching the real Run registry key");
                        Capture((W.FrameworkElement)gui!.Content, Path.Combine(captureDirectory, "Settings.png"), false);
                        gui.Close(); gui = null; overlay.SetConfigurationOpen(false);
                        overlay.CloseOverlay(); Assert(!overlay.AnimationRunning, "render subscription stops when hidden");
                        overlay.Open(); stage++; break;
                    case 5:
                        Assert(overlay.IsVisible, "overlay can reopen after hide");
                        Console.WriteLine($"WPF rendering tier: {M.RenderCapability.Tier >> 16}");
                        timer.Stop(); overlay.CloseOverlay(); Application.ExitThread(); stage++; break;
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e); result = 1; timer.Stop(); gui?.Close(); overlay.CloseOverlay(); Application.ExitThread();
            }
        };
        overlay.RenderFailed += e => { Console.Error.WriteLine(e); result = 1; timer.Stop(); Application.ExitThread(); };
        overlay.Open(); timer.Start(); Application.Run();
        overlay.Dispose(); wpf.Shutdown();
        Assert(stage == 6 && result == 0, "fullscreen transparent UI, three layouts, live settings and reopen lifecycle");
        return result;
    }

    private static void Capture(W.FrameworkElement element, string path, bool checkAlpha)
    {
        element.UpdateLayout();
        var dpi = M.VisualTreeHelper.GetDpi(element);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, M.PixelFormats.Pbgra32);
        bitmap.Render(element);
        if (checkAlpha)
        {
            byte[] pixel = new byte[4]; bitmap.CopyPixels(new W.Int32Rect(0, 0, 1, 1), pixel, 4, 0);
            Assert(pixel[3] is >= 139 and <= 141, "scrim has real 55% per-pixel alpha");
        }
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file); Console.WriteLine("UI capture: " + path);
    }

    private static void WriteImage(string path, int index)
    {
        using var bitmap = new SKBitmap(1920, 1080); using var canvas = new SKCanvas(bitmap);
        using var sky = new SKPaint { Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(1920, 1080),
            new SKColor[] { SKColor.FromHsl(index * 29 % 360, 50, 48), SKColor.FromHsl((index * 29 + 65) % 360, 60, 12) }, null, SKShaderTileMode.Clamp) };
        canvas.DrawPaint(sky);
        using var sun = new SKPaint { Color = new SKColor(252, 227, 171), IsAntialias = true }; canvas.DrawCircle(1280, 310, 105, sun);
        using var mountains = new SKPath(); mountains.MoveTo(0, 850); mountains.LineTo(430, 350); mountains.LineTo(810, 770);
        mountains.LineTo(1120, 440); mountains.LineTo(1570, 820); mountains.LineTo(1920, 580); mountains.LineTo(1920, 1080); mountains.LineTo(0, 1080); mountains.Close();
        using var ridge = new SKPaint { Color = SKColor.FromHsl((index * 29 + 20) % 360, 34, 13), IsAntialias = true }; canvas.DrawPath(mountains, ridge);
        using var text = new SKPaint { Color = SKColors.White.WithAlpha(210), TextSize = 34, IsAntialias = true };
        canvas.DrawText($"LANDSCAPE  {index + 1:00}", 90, 990, text);
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100); using var file = File.Create(path); encoded.SaveTo(file);
    }
    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name);
    }
    private static async Task Until(Func<bool> condition, string name)
    {
        var watch = Stopwatch.StartNew();
        while (!condition()) { if (watch.Elapsed.TotalSeconds > 20) throw new TimeoutException(name); await Task.Delay(30); }
        Console.WriteLine("PASS: " + name);
    }
}
