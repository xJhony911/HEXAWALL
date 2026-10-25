namespace HexaWall;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var instance = new Mutex(true, "Local\\HexaWall.5B142BDA-6A02-4D69-996E-67B770AA3F12", out bool first);
        if (!first) return;
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        var wpf = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        Application.ThreadException += (_, e) => Log.Error(e.Exception);
        try
        {
            using var context = new TrayContext(); Application.Run(context);
        }
        catch (Exception e)
        {
            Log.Error(e); MessageBox.Show(e.Message, "HEXAWALL", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { wpf.Shutdown(); }
    }
}

internal sealed class TrayContext : ApplicationContext
{
    private readonly Settings settings = Settings.Load();
    private readonly NotifyIcon tray;
    private readonly Icon icon = IconFactory.Create();
    private readonly Font menuFont = new("Segoe UI", 10);
    private readonly List<Image> menuImages = [];
    private readonly Control dispatcher = new();
    private readonly HotKeyWindow hotkey;
    private readonly GamingMonitor gaming;
    private readonly DesktopWallpaper desktop;
    private readonly TransitionEngine transitions;
    private readonly ConfigurationController configuration;
    private readonly List<Task> retiredCaches = [];
    private ThumbnailCache cache;
    private OverlayWindow? overlay;
    private SettingsWindow? settingsWindow;
    private bool disposed;

    public TrayContext()
    {
        _ = dispatcher.Handle;
        var startup = new StartupRegistration(); settings.StartWithWindows = startup.IsEnabled;
        hotkey = new HotKeyWindow(settings, allowUnregistered: true); gaming = new GamingMonitor(); desktop = new DesktopWallpaper();
        cache = new ThumbnailCache(settings); cache.Pause(settings.PauseInFullscreen && gaming.Busy);
        transitions = new TransitionEngine(settings, desktop, gaming);
        configuration = new ConfigurationController(settings, hotkey, startup);
        configuration.Changed += ApplyRuntimeSettings;
        var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), BackColor = ColorTranslator.FromHtml("#181A20"),
            ForeColor = Color.White, Font = menuFont, ImageScalingSize = new Size(24, 24), ShowImageMargin = true, Padding = new Padding(4) };
        AddMenu(menu, "Abrir selector", MenuGlyph.Selector, Toggle);
        AddMenu(menu, "Elegir carpeta…", MenuGlyph.Folder, ChooseFolder);
        AddMenu(menu, "Cambiar diseño", MenuGlyph.Layout, () =>
        {
            var change = settings.Copy(); change.Layout = (LayoutMode)(((int)settings.Layout + 1) % 3); ChangeOptions(change);
        });
        AddMenu(menu, "Volver a explorar carpeta", MenuGlyph.Refresh, () => cache.Rescan());
        AddMenu(menu, "Configuración…", MenuGlyph.Settings, OpenSettings);
        menu.Items.Add(new ToolStripSeparator()); AddMenu(menu, "Salir", MenuGlyph.Exit, ExitThread);
        tray = new NotifyIcon { Text = TrayText(), Icon = icon, ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => Toggle();
        hotkey.Pressed += Toggle;
        hotkey.DisplayChanged += () => { transitions.Cancel(); settingsWindow?.Close(); overlay?.CloseOverlay(); };
        hotkey.ShellRestarted += () => { transitions.Cancel(); tray.Visible = false; tray.Visible = true; };
        transitions.Failed += e => Notify($"No se pudo animar el fondo. {e.Message}");
        gaming.Changed += busy =>
        {
            bool pause = settings.PauseInFullscreen && busy; cache.Pause(pause);
            if (pause) { settingsWindow?.Close(); overlay?.CloseOverlay(); }
        };
        if (!Directory.Exists(settings.Folder)) Notify("Elige una carpeta de wallpapers desde Configuración.");
        if (hotkey.StartupError != null) Notify(hotkey.StartupError + " Abre Configuración desde la bandeja para cambiarlo.");
    }

    private void AddMenu(ContextMenuStrip menu, string text, MenuGlyph glyph, Action action)
    {
        var image = IconFactory.Glyph(glyph); menuImages.Add(image);
        var item = new ToolStripMenuItem(text, image) { Padding = new Padding(7, 6, 12, 6) };
        item.Click += (_, _) => action(); menu.Items.Add(item);
    }
    private string TrayText()
    {
        string text = "HEXAWALL · " + HotKeyWindow.Format(settings.HotKeyModifiers, settings.HotKeyVirtualKey);
        return text.Length <= 63 ? text : text[..63];
    }
    private void EnsureOverlay()
    {
        if (overlay != null) return;
        overlay = new OverlayWindow(settings, cache);
        overlay.RenderFailed += e => Notify(e.Message);
        overlay.ConfigurationRequested += OpenSettings; overlay.OptionsChanged += ChangeOptions;
        overlay.Chosen += async (entry, cursor) =>
        {
            try { await transitions.ApplyAsync(entry.Path, cursor); }
            catch (Exception e) { Log.Error(e); Notify(e.Message); }
        };
    }
    private void Toggle()
    {
        if (settingsWindow != null) { settingsWindow.Activate(); return; }
        if (settings.PauseInFullscreen && gaming.Refresh()) { cache.Pause(true); return; }
        EnsureOverlay(); overlay!.Open();
    }
    private void OpenSettings()
    {
        if (settingsWindow != null) { settingsWindow.Activate(); return; }
        var window = new SettingsWindow(settings, configuration, hotkey);
        settingsWindow = window;
        if (overlay?.IsVisible == true)
        {
            overlay.SetConfigurationOpen(true); window.Owner = overlay;
            window.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner; window.Topmost = true;
        }
        try { window.ShowDialog(); }
        finally { settingsWindow = null; overlay?.SetConfigurationOpen(false); }
    }
    private void ChooseFolder()
    {
        overlay?.CloseOverlay();
        using var dialog = new FolderBrowserDialog { Description = "Carpeta de wallpapers", SelectedPath = settings.Folder, UseDescriptionForTitle = true };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        var change = settings.Copy(); change.Folder = dialog.SelectedPath; ChangeOptions(change);
    }
    private void ChangeOptions(Settings candidate)
    {
        if (!configuration.Apply(candidate, out string error)) Notify(error);
    }
    private void ApplyRuntimeSettings(Settings previous)
    {
        transitions.Cancel();
        if (!string.Equals(previous.Folder, settings.Folder, StringComparison.OrdinalIgnoreCase))
        {
            var old = cache; cache = new ThumbnailCache(settings);
            overlay?.ReplaceCache(cache);
            retiredCaches.RemoveAll(t => t.IsCompletedSuccessfully);
            retiredCaches.Add(Task.Run(old.Dispose));
        }
        bool pause = settings.PauseInFullscreen && gaming.Refresh(); cache.Pause(pause);
        if (!pause) overlay?.RefreshSettings();
        tray.Text = TrayText();
    }
    private void Notify(string text)
    {
        if (!disposed && !(settings.PauseInFullscreen && gaming.Busy)) tray.ShowBalloonTip(4000, "HEXAWALL", text, ToolTipIcon.Info);
    }
    protected override void ExitThreadCore() { Dispose(); base.ExitThreadCore(); }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true; settingsWindow?.Close(); overlay?.Dispose();
            hotkey.Dispose(); transitions.Dispose(); gaming.Dispose(); cache.Dispose(); desktop.Dispose(); dispatcher.Dispose();
            try { Task.WhenAll(retiredCaches).GetAwaiter().GetResult(); } catch (Exception e) { Log.Error(e); }
            tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose();
            foreach (var image in menuImages) image.Dispose(); menuFont.Dispose(); icon.Dispose();
        }
        base.Dispose(disposing);
    }
}
