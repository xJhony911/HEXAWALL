using System.Diagnostics;
using System.Text.Json;
namespace HexaWall;
internal static class Program
{
    [STAThread] private static void Main(string[] args)
    {
        string isolation = Environment.GetEnvironmentVariable("HEXAWALL_DATA_HOME") is { Length: > 0 } data
            ? "." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(data))))[..16] : "";
        using var mutex = new Mutex(true, "Local\\HexaWall.5B142BDA-6A02-4D69-996E-67B770AA3F12" + isolation, out bool first);
        if (!first) return;
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += (_, e) => { Log.Error(e.Exception); MessageBox.Show(e.Exception.Message, "HEXAWALL"); };
        using var tray = new LiteTray(args.Contains("--memory-test")); Application.Run(tray);
    }
}
internal sealed class LiteTray : ApplicationContext
{
    private readonly Settings settings = Settings.Load();
    private readonly HotKeyWindow hotkey;
    private readonly ConfigurationController configuration;
    private readonly Control dispatcher = new();
    private readonly Icon icon = IconFactory.Create();
    private readonly NotifyIcon tray;
    private readonly Font menuFont = new("Segoe UI", 10);
    private readonly List<Image> glyphs = [];
    private Form? active;
    private bool disposed;
    private readonly System.Windows.Forms.Timer? testTimer;
    private readonly List<object> measurements = [];
    private System.Threading.Timer? sampler;
    private double peakPrivate, peakCommit, peakWorking;
    private readonly object sampleGate = new();
    private int phase;
    public LiteTray(bool test)
    {
        _ = dispatcher.Handle;
        hotkey = new HotKeyWindow(settings, allowUnregistered: true);
        var startup = new StartupRegistration(); settings.StartWithWindows = startup.IsEnabled;
        configuration = new ConfigurationController(settings, hotkey, startup);
        var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), BackColor = Color.FromArgb(24, 26, 32), ForeColor = Color.White, Font = menuFont };
        Add(menu, "Abrir selector", MenuGlyph.Selector, Open); Add(menu, "Configuración…", MenuGlyph.Settings, Configure);
        menu.Items.Add(new ToolStripSeparator()); Add(menu, "Salir", MenuGlyph.Exit, ExitThread);
        tray = new NotifyIcon { Text = Tooltip(), Icon = icon, ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => Open(); hotkey.Pressed += Open;
        hotkey.ShellRestarted += () => { tray.Visible = false; tray.Visible = true; };
        hotkey.DisplayChanged += () => active?.Close(); configuration.Changed += _ => tray.Text = Tooltip();
        if (hotkey.StartupError != null) tray.ShowBalloonTip(4000, "HEXAWALL", hotkey.StartupError, ToolTipIcon.Info);
        if (test)
        {
            sampler = new System.Threading.Timer(_ =>
            {
                var memory = MemorySnapshot.Read();
                lock (sampleGate)
                {
                    peakPrivate = Math.Max(peakPrivate, memory.PrivateWorkingSetMiB ?? 0);
                    peakCommit = Math.Max(peakCommit, memory.PrivateCommitMiB); peakWorking = Math.Max(peakWorking, memory.WorkingSetMiB);
                }
            }, null, 0, 100);
            testTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            testTimer.Tick += (_, _) => Probe(); testTimer.Start();
        }
    }
    private string Tooltip() => "HEXAWALL · " + HotKeyWindow.Format(settings.HotKeyModifiers, settings.HotKeyVirtualKey);
    private void Add(ContextMenuStrip menu, string text, MenuGlyph glyph, Action action)
    {
        var image = IconFactory.Glyph(glyph); glyphs.Add(image); var item = new ToolStripMenuItem(text, image);
        item.Click += (_, _) => action(); menu.Items.Add(item);
    }
    private void Open()
    {
        if (active != null) { active.Close(); return; }
        if (settings.PauseInFullscreen && Native.IsBusy()) return;
        string? chosen; bool configure;
        using (var selector = new LiteSelector(settings))
        {
            active = selector; selector.ShowDialog(); active = null;
            chosen = selector.SelectedPath; configure = selector.ConfigureRequested;
        }
        if (configure) { Configure(); return; }
        if (chosen != null)
        {
            try
            {
                using var desktop = new DesktopWallpaper();
                string? monitor = settings.AllMonitors ? null : desktop.Read().FirstOrDefault(m => m.Bounds.Contains(Cursor.Position))?.Id;
                desktop.Apply(LiteWallpaper.Prepare(chosen), monitor);
            }
            catch (Exception e) { Log.Error(e); tray.ShowBalloonTip(4000, "HEXAWALL", e.Message, ToolTipIcon.Error); }
        }
    }
    private void Configure()
    {
        if (active != null) { active.Activate(); return; }
        using var form = new LiteSettings(settings, configuration, hotkey); active = form;
        form.ShowDialog(); active = null;
    }
    private void Probe()
    {
        measurements.Add(new { Stage = phase, Memory = MemorySnapshot.Read(), Visible = (active as LiteSelector)?.VisibleImages });
        string report = Environment.GetEnvironmentVariable("HEXAWALL_MEMORY_REPORT") ?? Path.Combine(Settings.DataDirectory, "memory.json");
        lock (sampleGate) File.WriteAllText(report, JsonSerializer.Serialize(new { PeakPrivateWorkingSetMiB = peakPrivate, PeakPrivateCommitMiB = peakCommit, PeakWorkingSetMiB = peakWorking, Measurements = measurements }, new JsonSerializerOptions { WriteIndented = true }));
        if (Environment.GetEnvironmentVariable("HEXAWALL_QA_DIR") is { Length: > 0 } qa && active is LiteSelector capture && phase is 2 or 4 or 6)
        { Directory.CreateDirectory(qa); capture.CapturePreview(Path.Combine(qa, settings.Layout + ".png")); }
        int step = phase++;
        if (step == 0 || step == 8) { dispatcher.BeginInvoke((Action)Open); }
        if (step == 2 && active is LiteSelector selector) selector.Select(15);
        if (step == 3) { active?.Close(); settings.Layout = LayoutMode.CoverFlow; dispatcher.BeginInvoke((Action)Open); }
        if (step == 5) { active?.Close(); settings.Layout = LayoutMode.Cards; dispatcher.BeginInvoke((Action)Open); }
        if (step == 7 || step == 10) active?.Close();
        if (step == 11) dispatcher.BeginInvoke((Action)Configure);
        if (step == 12 && active is LiteSettings gui && Environment.GetEnvironmentVariable("HEXAWALL_QA_DIR") is { Length: > 0 } settingsQa)
        {
            using var bitmap = new Bitmap(gui.Width, gui.Height); gui.DrawToBitmap(bitmap, new Rectangle(Point.Empty, gui.Size));
            bitmap.Save(Path.Combine(settingsQa, "Settings.png"), System.Drawing.Imaging.ImageFormat.Png);
        }
        if (step == 13) active?.Close();
        if (step == 15)
        {
            testTimer!.Stop();
            ExitThread();
        }
    }
    protected override void ExitThreadCore() { Dispose(); base.ExitThreadCore(); }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true; active?.Close(); testTimer?.Dispose(); sampler?.Dispose(); hotkey.Dispose(); dispatcher.Dispose();
            tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose(); icon.Dispose(); menuFont.Dispose();
            foreach (var image in glyphs) image.Dispose();
        }
        base.Dispose(disposing);
    }
}


