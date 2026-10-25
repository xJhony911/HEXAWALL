using System.Windows.Interop;
using System.Windows.Threading;
using W = System.Windows;
using I = System.Windows.Input;
using M = System.Windows.Media;

namespace HexaWall;

internal partial class SettingsWindow : W.Window
{
    private readonly Settings settings;
    private readonly ConfigurationController controller;
    private readonly HotKeyWindow hotkey;
    private readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private bool loading = true, capturing;

    public SettingsWindow(Settings settings, ConfigurationController controller, HotKeyWindow hotkey)
    {
        this.settings = settings; this.controller = controller; this.hotkey = hotkey;
        InitializeComponent(); Icon = IconFactory.WpfIcon();
        LoadControls();
        debounce.Tick += (_, _) => { debounce.Stop(); SaveControls(); };
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var area = Screen.FromHandle(hwnd).WorkingArea;
            double dpi = Math.Max(96, Native.GetDpiForWindow(hwnd)) / 96d;
            MaxHeight = Math.Max(400, area.Height / dpi - 32); MaxWidth = Math.Max(400, area.Width / dpi - 32);
            Height = Math.Min(Height, MaxHeight); Width = Math.Min(Width, MaxWidth);
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == I.Key.Escape && !capturing) { Close(); e.Handled = true; }
        };
        Closed += (_, _) =>
        {
            if (debounce.IsEnabled) { debounce.Stop(); SaveControls(); }
            FinishCapture();
        };
        System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(this);
    }

    private void LoadControls()
    {
        loading = true;
        FolderValue.Text = settings.Folder; FolderValue.ToolTip = settings.Folder; LayoutChoice.SelectedIndex = (int)settings.Layout;
        TransitionChoice.SelectedIndex = (int)settings.Transition;
        DurationSlider.Value = settings.TransitionMilliseconds; DurationValue.Text = $"{settings.TransitionMilliseconds} ms";
        AllMonitorsToggle.IsChecked = settings.AllMonitors; GamingToggle.IsChecked = settings.PauseInFullscreen;
        StartupToggle.IsChecked = settings.StartWithWindows;
        HotkeyValue.Text = HotKeyWindow.Format(settings.HotKeyModifiers, settings.HotKeyVirtualKey);
        loading = false;
    }
    private void SaveControls()
    {
        if (loading) return;
        debounce.Stop();
        var candidate = settings.Copy();
        candidate.Folder = FolderValue.Text;
        candidate.Layout = (LayoutMode)LayoutChoice.SelectedIndex; candidate.Transition = (TransitionMode)TransitionChoice.SelectedIndex;
        candidate.TransitionMilliseconds = (int)Math.Round(DurationSlider.Value);
        candidate.AllMonitors = AllMonitorsToggle.IsChecked == true;
        candidate.PauseInFullscreen = GamingToggle.IsChecked == true;
        candidate.StartWithWindows = StartupToggle.IsChecked == true;
        Apply(candidate);
    }
    private bool Apply(Settings candidate)
    {
        if (controller.Apply(candidate, out string error))
        {
            StatusValue.Text = "Guardado"; StatusValue.Foreground = new M.SolidColorBrush(M.Color.FromRgb(44, 232, 198)); return true;
        }
        LoadControls(); StatusValue.Text = error; StatusValue.Foreground = new M.SolidColorBrush(M.Color.FromRgb(255, 153, 153)); return false;
    }
    private void BrowseClicked(object sender, W.RoutedEventArgs e)
    {
        using var dialog = new FolderBrowserDialog { Description = "Carpeta de wallpapers", UseDescriptionForTitle = true, SelectedPath = settings.Folder };
        if (dialog.ShowDialog(new NativeOwner(new WindowInteropHelper(this).Handle)) != System.Windows.Forms.DialogResult.OK) return;
        FolderValue.Text = dialog.SelectedPath; SaveControls();
    }
    private sealed class NativeOwner(nint handle) : System.Windows.Forms.IWin32Window { public nint Handle => handle; }
    private void ChoiceChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) { if (!loading) SaveControls(); }
    private void ToggleChanged(object sender, W.RoutedEventArgs e) { if (!loading) SaveControls(); }
    private void DurationChanged(object sender, W.RoutedPropertyChangedEventArgs<double> e)
    {
        if (DurationValue != null) DurationValue.Text = $"{Math.Round(e.NewValue)} ms";
        if (loading) return;
        debounce.Stop(); debounce.Start();
    }
    private void CloseClicked(object sender, W.RoutedEventArgs e) => Close();
    private void DragHeader(object sender, I.MouseButtonEventArgs e)
    {
        if (e.ChangedButton == I.MouseButton.Left && e.OriginalSource is not System.Windows.Controls.Button) DragMove();
    }
    private void CaptureStarted(object sender, I.KeyboardFocusChangedEventArgs e)
    {
        if (loading || capturing) return;
        debounce.Stop(); SaveControls();
        capturing = true; hotkey.Suspend();
        HotkeyValue.Text = "Presiona tu combinación…";
    }
    private void CaptureEnded(object sender, I.KeyboardFocusChangedEventArgs e) => FinishCapture();
    private void FinishCapture()
    {
        if (!capturing) return;
        capturing = false;
        if (!hotkey.Resume(out string error))
        {
            StatusValue.Text = error; StatusValue.Foreground = M.Brushes.LightCoral;
        }
        HotkeyValue.Text = HotKeyWindow.Format(settings.HotKeyModifiers, settings.HotKeyVirtualKey);
    }
    private void CaptureKey(object sender, I.KeyEventArgs e)
    {
        if (!capturing) return;
        e.Handled = true;
        var key = e.Key == I.Key.System ? e.SystemKey : e.Key;
        if (key == I.Key.Escape) { FinishCapture(); I.Keyboard.ClearFocus(); return; }
        if (key is I.Key.LeftAlt or I.Key.RightAlt or I.Key.LeftCtrl or I.Key.RightCtrl or I.Key.LeftShift or I.Key.RightShift or I.Key.LWin or I.Key.RWin) return;
        var mods = I.Keyboard.Modifiers;
        uint modifiers = 0;
        if ((mods & I.ModifierKeys.Alt) != 0) modifiers |= 1;
        if ((mods & I.ModifierKeys.Control) != 0) modifiers |= 2;
        if ((mods & I.ModifierKeys.Shift) != 0) modifiers |= 4;
        if ((mods & I.ModifierKeys.Windows) != 0) modifiers |= 8;
        var candidate = settings.Copy(); candidate.HotKeyModifiers = modifiers; candidate.HotKeyVirtualKey = (uint)I.KeyInterop.VirtualKeyFromKey(key);
        bool saved = Apply(candidate);
        FinishCapture();
        if (saved) { HotkeyValue.Text = HotKeyWindow.Format(settings.HotKeyModifiers, settings.HotKeyVirtualKey); I.Keyboard.ClearFocus(); }
    }
}
