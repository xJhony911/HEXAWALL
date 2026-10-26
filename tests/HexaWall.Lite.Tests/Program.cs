using HexaWall;
using System.Drawing.Imaging;
using System.Diagnostics;
internal static class Program
{
    [STAThread] private static void Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "HexaWallLite.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); Environment.SetEnvironmentVariable("HEXAWALL_DATA_HOME", Path.Combine(root, "data"));
        try { Test(root).GetAwaiter().GetResult(); }
        finally { Directory.Delete(root, true); }
    }
    private static async Task Test(string root)
    {
        string folder = Path.Combine(root, "images"); Directory.CreateDirectory(folder);
        using (var original = new Bitmap(3840, 2160))
        {
            using var graphics = Graphics.FromImage(original); graphics.Clear(Color.MediumTurquoise);
            original.Save(Path.Combine(folder, "4k.png"), ImageFormat.Png);
        }
        using (var bitmap = WicImage.Load(Path.Combine(folder, "4k.png"), 320, 180))
        { Check(bitmap.Size == new Size(320, 180), "WIC scales 4K without a managed original bitmap"); Check(bitmap.GetPixel(10, 10).G > 150, "WIC pixel format and stride"); }
        using (var surface = new LayeredSurface(80, 60))
        { surface.Graphics.Clear(Color.Transparent); Check(surface.Bitmap.GetPixel(0, 0).A == 0, "native alpha surface preserves transparency"); }
        var items = LiteCache.Scan(folder); Check(items.Length == 1, "image indexing");
        using (var cache = new LiteCache())
        {
            var ready = new TaskCompletionSource<LiteImages>(TaskCreationOptions.RunContinuationsAsynchronously);
            cache.Ready += images => { if (!ready.TrySetResult(images)) images.Dispose(); };
            cache.Request(items, [0], 0);
            using var loaded = await ready.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Check(loaded.Bitmaps[0].Width <= 960 && loaded.Bitmaps[0].Height <= 540, "selected image has a bounded allocation");
            Check(Directory.GetFiles(Path.Combine(Settings.DataDirectory, "thumbnails-lite-v1"), "*.jpg").Length == 1, "disk thumbnail generated");
        }
        using var hotkey = new HotKeyWindow(new Settings { HotKeyModifiers = 7, HotKeyVirtualKey = 0x87 });
        using (var conflict = new HotKeyWindow(new Settings { HotKeyModifiers = 7, HotKeyVirtualKey = 0x86 }))
            Check(!hotkey.TryChange(7, 0x86, out _), "hotkey conflict preserves previous registration");
        var settings = new Settings { Folder = folder, HotKeyModifiers = 7, HotKeyVirtualKey = 0x87 };
        var startup = new FakeStartup(); var controller = new ConfigurationController(settings, hotkey, startup);
        var change = settings.Copy(); change.Layout = LayoutMode.Cards; change.StartWithWindows = true;
        Check(controller.Apply(change, out _), "graphical configuration persists");
        Check(Settings.Load().Layout == LayoutMode.Cards && startup.IsEnabled, "settings and startup update");
        hotkey.Suspend(); Check(hotkey.Resume(out _), "hotkey capture resumes");
        Console.WriteLine("PASS: all Lite tests");
    }
    private sealed class FakeStartup : IStartupRegistration
    {
        private string? value; public bool IsEnabled => value != null;
        public string? Read() => value; public void Set(bool enabled) => value = enabled ? "test" : null;
        public void Restore(string? previous) => value = previous;
    }
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS: " + name); }
}
