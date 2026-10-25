using System.Diagnostics;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using W = System.Windows;
using C = System.Windows.Controls;
using M = System.Windows.Media;
using I = System.Windows.Input;
using S = System.Windows.Shapes;

namespace HexaWall;

internal sealed class OverlayWindow : W.Window, IDisposable
{
    private sealed class Tile
    {
        public required int Index;
        public required string Key;
        public C.Grid? Visual;
        public C.Image? Image;
        public S.Path? Edge;
        public M.ScaleTransform? Scale;
        public M.ImageBrush? Texture;
        public M.SolidColorBrush? Frame;
        public AxisAngleRotation3D? Rotation;
        public ScaleTransform3D? Scale3D;
        public TranslateTransform3D? Translation;
        public GeometryModel3D? Model;
        public ModelVisual3D? ModelVisual;
        public BitmapSource? Thumbnail;
        public double X, Y, TargetX, TargetY, Activation, Opacity = 1;
    }

    private readonly Settings settings;
    private ThumbnailCache cache;
    private readonly SelectedPreview selectedPreview = new();
    private readonly C.Grid root = new();
    private readonly C.Canvas grid = new() { ClipToBounds = false };
    private readonly C.Viewport3D flowView = new() { Margin = new W.Thickness(0, 86, 0, 150), ClipToBounds = false };
    private readonly PerspectiveCamera camera = new(new Point3D(0, 0, 9), new Vector3D(0, 0, -1), new Vector3D(0, 1, 0), 45);
    private readonly C.TextBlock title = new() { FontSize = 18, FontWeight = W.FontWeights.Medium, TextAlignment = W.TextAlignment.Center, TextTrimming = W.TextTrimming.CharacterEllipsis };
    private readonly C.TextBlock detail = new() { FontSize = 12, Opacity = .64, TextAlignment = W.TextAlignment.Center, Margin = new W.Thickness(0, 9, 0, 0) };
    private readonly C.TextBlock help = new() { FontSize = 12, Opacity = .54, TextAlignment = W.TextAlignment.Center, Text = "Flechas / WASD   ·   Enter aplicar   ·   Tab diseño   ·   F1 ajustes   ·   Esc cerrar", Margin = new W.Thickness(0, 17, 0, 0) };
    private readonly C.TextBlock empty = new() { Text = "Tu próximo escritorio empieza aquí", FontSize = 27, HorizontalAlignment = W.HorizontalAlignment.Center, VerticalAlignment = W.VerticalAlignment.Center };
    private readonly Dictionary<int, Tile> tiles = new();
    private readonly Stopwatch frameClock = new();
    private readonly bool dismissOnDeactivate;
    private WallpaperEntry[] items = [];
    private LayoutMode lastLayout = (LayoutMode)(-1);
    private Rectangle monitor;
    private int selected, columns = 1, firstRow;
    private double flow, lastSeconds;
    private bool rendering, disposed, configurationOpen, sourceUpdateQueued;
    private W.Point lastMouse = new(double.NaN, double.NaN);

    public event Action<WallpaperEntry, Point>? Chosen;
    public event Action? ConfigurationRequested;
    public event Action<Settings>? OptionsChanged;
    public event Action<Exception>? RenderFailed;
    public nint Handle => new WindowInteropHelper(this).Handle;
    internal W.FrameworkElement Surface => root;
    internal bool AnimationRunning => rendering;
    internal int VisibleCount => tiles.Count;
    internal int LoadedVisibleCount => tiles.Values.Count(t => t.Thumbnail != null);
    internal BitmapSource? ActiveImage => items.ElementAtOrDefault(selected) is { } entry ? selectedPreview.Get(entry.Key) : null;

    public OverlayWindow(Settings settings, ThumbnailCache cache, bool dismissOnDeactivate = true)
    {
        this.settings = settings; this.cache = cache; this.dismissOnDeactivate = dismissOnDeactivate;
        Title = "HEXAWALL"; WindowStyle = W.WindowStyle.None; ResizeMode = W.ResizeMode.NoResize;
        AllowsTransparency = true; Background = M.Brushes.Transparent; ShowInTaskbar = false; Topmost = true;
        WindowStartupLocation = W.WindowStartupLocation.Manual;
        FontFamily = new M.FontFamily("Segoe UI"); Foreground = M.Brushes.White;
        Icon = IconFactory.WpfIcon();
        // Per-pixel alpha: only the scrim is 55% opaque; wallpapers and HUD keep their own opacity.
        root.Background = new M.SolidColorBrush(M.Color.FromArgb(140, 10, 12, 16));
        Content = root; root.Children.Add(grid); root.Children.Add(flowView);
        flowView.Camera = camera;
        M.RenderOptions.SetBitmapScalingMode(root, M.BitmapScalingMode.HighQuality);
        var hud = new C.StackPanel { VerticalAlignment = W.VerticalAlignment.Bottom, HorizontalAlignment = W.HorizontalAlignment.Center,
            Margin = new W.Thickness(48, 0, 48, 34), MaxWidth = 1000, IsHitTestVisible = false,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 8, ShadowDepth = 1, Opacity = .7 } };
        hud.Children.Add(title); hud.Children.Add(detail); hud.Children.Add(help); root.Children.Add(hud);
        empty.IsHitTestVisible = false; root.Children.Add(empty);
        var gear = new C.Button { Width = 48, Height = 48, Margin = new W.Thickness(0, 24, 30, 0),
            HorizontalAlignment = W.HorizontalAlignment.Right, VerticalAlignment = W.VerticalAlignment.Top,
            ToolTip = "Configuración · F1", Cursor = I.Cursors.Hand, Content = GearImage() };
        var border = new W.FrameworkElementFactory(typeof(C.Border));
        border.SetValue(C.Border.BackgroundProperty, new M.SolidColorBrush(M.Color.FromArgb(100, 24, 26, 32)));
        border.SetValue(C.Border.CornerRadiusProperty, new W.CornerRadius(24));
        var presenter = new W.FrameworkElementFactory(typeof(C.ContentPresenter));
        presenter.SetValue(W.FrameworkElement.HorizontalAlignmentProperty, W.HorizontalAlignment.Center);
        presenter.SetValue(W.FrameworkElement.VerticalAlignmentProperty, W.VerticalAlignment.Center);
        border.AppendChild(presenter); gear.Template = new C.ControlTemplate(typeof(C.Button)) { VisualTree = border };
        gear.Click += (_, _) => OpenConfiguration(); root.Children.Add(gear);
        SourceInitialized += (_, _) =>
        {
            var source = HwndSource.FromHwnd(Handle);
            source?.AddHook(WindowMessage);
            long ex = Native.GetWindowLongPtr(Handle, Native.GWL_EXSTYLE).ToInt64();
            Native.SetWindowLongPtr(Handle, Native.GWL_EXSTYLE, (nint)(ex | Native.WS_EX_TOOLWINDOW));
        };
        SizeChanged += (_, _) => { if (IsVisible) { BuildLayout(); RequestSelected(); } };
        Deactivated += (_, _) => { if (this.dismissOnDeactivate && !configurationOpen && IsVisible) CloseOverlay(); };
        PreviewKeyDown += KeyDownHandler;
        root.MouseMove += MouseMoved;
        root.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is C.Button) return;
            int index = HitIndex(e.GetPosition(root));
            if (index >= 0) { SelectIndex(index); Choose(); e.Handled = true; }
        };
        root.MouseWheel += (_, e) => { SelectIndex(selected - Math.Sign(e.Delta)); e.Handled = true; };
        cache.Changed += CacheChanged; cache.Ready += SourcesReady; selectedPreview.Ready += SourcesReady;
        System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(this);
    }

    private static C.Image GearImage()
    {
        var drawing = new M.DrawingGroup();
        using (var dc = drawing.Open())
        {
            var pen = new M.Pen(new M.SolidColorBrush(M.Color.FromRgb(44, 232, 198)), 1.6);
            dc.DrawEllipse(null, pen, new W.Point(12, 12), 7, 7);
            dc.DrawEllipse(null, pen, new W.Point(12, 12), 2.8, 2.8);
            for (int i = 0; i < 8; i++)
            {
                double angle = i * Math.PI / 4;
                dc.DrawLine(pen, new W.Point(12 + 7 * Math.Cos(angle), 12 + 7 * Math.Sin(angle)),
                    new W.Point(12 + 10 * Math.Cos(angle), 12 + 10 * Math.Sin(angle)));
            }
        }
        drawing.Freeze(); return new C.Image { Source = new M.DrawingImage(drawing), Width = 24, Height = 24, IsHitTestVisible = false };
    }

    private nint WindowMessage(nint hwnd, int message, nint wp, nint lp, ref bool handled)
    {
        if (message == 0x02E0 && IsVisible) // WM_DPICHANGED: let WPF adopt DPI, then restore full physical monitor bounds.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)(() => { PlaceOnMonitor(); BuildLayout(); RequestSelected(); }));
        return 0;
    }

    public void Open()
    {
        if (IsVisible) { CloseOverlay(); return; }
        monitor = Screen.FromPoint(System.Windows.Forms.Cursor.Position).Bounds;
        items = cache.Entries; selected = Math.Clamp(selected, 0, Math.Max(0, items.Length - 1)); flow = selected;
        lastMouse = new(double.NaN, double.NaN); cache.Pause(false);
        Width = monitor.Width; Height = monitor.Height; Left = monitor.Left; Top = monitor.Top;
        Show(); PlaceOnMonitor(); Activate(); Native.SetForegroundWindow(Handle); Focus();
        UpdateLayout(); BuildLayout(); RequestSelected(); UpdateHud(); StartAnimation();
    }

    private void PlaceOnMonitor()
    {
        if (Handle != 0 && !monitor.IsEmpty)
            Native.SetWindowPos(Handle, new nint(-1), monitor.X, monitor.Y, monitor.Width, monitor.Height, 0x0010);
    }

    public void CloseOverlay()
    {
        StopAnimation(); Hide(); ReleaseTiles(); selectedPreview.Clear(); cache.ClearMemory();
    }
    public void SetConfigurationOpen(bool value)
    {
        configurationOpen = value;
        if (value) StopAnimation();
        else if (IsVisible) { Activate(); StartAnimation(); }
    }
    private void OpenConfiguration()
    {
        SetConfigurationOpen(true); ConfigurationRequested?.Invoke();
    }
    public void ReplaceCache(ThumbnailCache replacement)
    {
        cache.Changed -= CacheChanged; cache.Ready -= SourcesReady;
        ReleaseTiles(); selectedPreview.Clear(); cache = replacement;
        cache.Changed += CacheChanged; cache.Ready += SourcesReady;
        items = cache.Entries; selected = 0; flow = 0; firstRow = 0;
        RefreshSettings();
    }
    public void RefreshSettings()
    {
        if (!IsVisible) return;
        BuildLayout(); RequestSelected(); UpdateHud(); StartAnimation();
    }
    private void CacheChanged() => Post(() =>
    {
        string? previousKey = items.ElementAtOrDefault(selected)?.Key;
        items = cache.Entries;
        selected = Math.Max(0, Array.FindIndex(items, x => x.Key == previousKey));
        flow = selected;
        if (IsVisible) { BuildLayout(); RequestSelected(); UpdateHud(); StartAnimation(); }
    });
    private void SourcesReady() => Post(() =>
    {
        if (!IsVisible || sourceUpdateQueued) return;
        sourceUpdateQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, (Action)(() =>
        {
            sourceUpdateQueued = false;
            if (!disposed && IsVisible) UpdateSources();
        }));
    });
    private void Post(Action action)
    {
        if (disposed || Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke((Action)(() => { if (!disposed) action(); }));
    }

    private void RequestSelected()
    {
        if (!IsVisible || items.Length == 0) return;
        double dpi = M.VisualTreeHelper.GetDpi(this).DpiScaleX;
        double displayed = settings.Layout == LayoutMode.CoverFlow ? Math.Min(860, ActualWidth * .52) : 480;
        selectedPreview.Request(items[selected], (int)Math.Ceiling(Math.Max(1280, displayed * dpi * 1.12)));
    }
    private void UpdateSources()
    {
        foreach (var tile in tiles.Values)
        {
            if (tile.Index >= items.Length || tile.Key != items[tile.Index].Key) continue;
            tile.Thumbnail ??= cache.Request(items[tile.Index]);
            M.ImageSource? source = (tile.Index == selected ? selectedPreview.Get(tile.Key) : null) ?? tile.Thumbnail;
            if (tile.Image != null) tile.Image.Source = source;
            if (tile.Texture != null && source != null) tile.Texture.ImageSource = source;
        }
    }

    public void SelectIndex(int index)
    {
        if (items.Length == 0) return;
        index = Math.Clamp(index, 0, items.Length - 1);
        if (index == selected) return;
        selected = index; BuildLayout(); RequestSelected(); UpdateHud(); UpdateSources(); StartAnimation();
    }
    private void Choose()
    {
        if (items.Length == 0) return;
        var entry = items[selected]; var point = System.Windows.Forms.Cursor.Position;
        CloseOverlay(); Chosen?.Invoke(entry, point);
    }
    private void KeyDownHandler(object sender, I.KeyEventArgs e)
    {
        var key = e.Key == I.Key.System ? e.SystemKey : e.Key;
        int delta = key switch
        {
            I.Key.Left or I.Key.A => -1, I.Key.Right or I.Key.D => 1,
            I.Key.Up or I.Key.W => settings.Layout == LayoutMode.CoverFlow ? -1 : -columns,
            I.Key.Down or I.Key.S => settings.Layout == LayoutMode.CoverFlow ? 1 : columns,
            _ => 0
        };
        if (delta != 0) { SelectIndex(selected + delta); e.Handled = true; return; }
        if (key == I.Key.Escape) { CloseOverlay(); e.Handled = true; }
        else if (key == I.Key.Enter) { Choose(); e.Handled = true; }
        else if (key == I.Key.F1) { OpenConfiguration(); e.Handled = true; }
        else if (key == I.Key.Tab) { CycleLayout(); e.Handled = true; }
        else if (key is I.Key.D1 or I.Key.D2 or I.Key.D3)
        {
            var change = settings.Copy(); change.Transition = (TransitionMode)((int)key - (int)I.Key.D1);
            CommitOptions(change); e.Handled = true;
        }
    }
    public void CycleLayout()
    {
        var change = settings.Copy(); change.Layout = (LayoutMode)(((int)settings.Layout + 1) % 3); CommitOptions(change);
    }
    private void CommitOptions(Settings change)
    {
        if (OptionsChanged != null) OptionsChanged(change);
        else { change.Save(); settings.CopyFrom(change); RefreshSettings(); }
    }
    private void UpdateHud()
    {
        empty.Visibility = items.Length == 0 ? W.Visibility.Visible : W.Visibility.Collapsed;
        title.Text = items.Length == 0 ? "F1 para elegir una carpeta" : Path.GetFileNameWithoutExtension(items[selected].Path);
        detail.Text = $"{(items.Length == 0 ? "0" : (selected + 1).ToString())} / {items.Length}   ·   {settings.Layout}   ·   {settings.Transition}";
        help.Text = items.Length == 0 ? "Configuración en el engranaje o en la bandeja" : "Flechas / WASD   ·   Enter aplicar   ·   Tab diseño   ·   F1 ajustes   ·   Esc cerrar";
    }

    private void BuildLayout()
    {
        if (!IsVisible || ActualWidth < 1 || ActualHeight < 1) return;
        if (lastLayout != settings.Layout) { ReleaseTiles(); firstRow = 0; flow = selected; lastLayout = settings.Layout; }
        bool cover = settings.Layout == LayoutMode.CoverFlow;
        grid.Visibility = cover ? W.Visibility.Collapsed : W.Visibility.Visible;
        flowView.Visibility = cover ? W.Visibility.Visible : W.Visibility.Collapsed;
        if (cover) BuildFlow(); else BuildGrid();
        UpdateSources(); StartAnimation();
    }
    private void KeepOnly(HashSet<int> keep)
    {
        foreach (int index in tiles.Keys.Where(i => !keep.Contains(i) || i >= items.Length || tiles[i].Key != items[i].Key).ToArray())
        {
            var tile = tiles[index];
            if (tile.Visual != null) grid.Children.Remove(tile.Visual);
            if (tile.ModelVisual != null) flowView.Children.Remove(tile.ModelVisual);
            tiles.Remove(index);
        }
    }
    private void BuildGrid()
    {
        bool hex = settings.Layout == LayoutMode.Honeycomb;
        double margin = Math.Clamp(ActualWidth * .055, 32, 110);
        double available = Math.Max(100, ActualWidth - 2 * margin);
        double desired = hex ? 230 : 320;
        columns = Math.Max(1, (int)(available / desired));
        double cellWidth = available / (columns + (hex ? .5 : 0));
        double width = Math.Min(hex ? 230 : 340, cellWidth - 30);
        double radius = width / Math.Sqrt(3);
        double height = hex ? radius * 2 : width * 9 / 16;
        double stepY = hex ? radius * 1.5 + 24 : height + 36;
        double availableHeight = Math.Max(height, ActualHeight - 100 - 160);
        int rows = Math.Max(1, (int)Math.Floor((availableHeight - height) / stepY) + 1);
        int selectedRow = selected / columns;
        firstRow = Math.Clamp(firstRow, Math.Max(0, selectedRow - rows + 1), selectedRow);
        var visible = Enumerable.Range(firstRow * columns, Math.Max(0, Math.Min(rows * columns, items.Length - firstRow * columns))).ToHashSet();
        KeepOnly(visible);
        int actualRows = Math.Max(1, (visible.Count + columns - 1) / columns);
        double top = 92 + Math.Max(0, (availableHeight - (height + (actualRows - 1) * stepY)) / 2);
        foreach (int index in visible)
        {
            int row = index / columns, col = index % columns;
            double x = margin + col * cellWidth + (cellWidth - width) / 2 + (hex && row % 2 != 0 ? cellWidth / 2 : 0);
            double y = top + (row - firstRow) * stepY;
            if (!tiles.TryGetValue(index, out var tile))
            {
                tile = NewGridTile(index, width, height, hex); tile.X = x; tile.Y = y;
                tiles[index] = tile; grid.Children.Add(tile.Visual!);
            }
            ResizeGridTile(tile, width, height, hex);
            tile.TargetX = x; tile.TargetY = y;
            C.Panel.SetZIndex(tile.Visual!, index == selected ? 10 : 0);
        }
    }
    private Tile NewGridTile(int index, double width, double height, bool hex)
    {
        var image = new C.Image { Stretch = M.Stretch.UniformToFill };
        M.RenderOptions.SetBitmapScalingMode(image, M.BitmapScalingMode.HighQuality);
        var rootTile = new C.Grid { Cursor = I.Cursors.Hand, Tag = index, RenderTransformOrigin = new W.Point(.5, .5) };
        var background = new S.Path { Fill = new M.SolidColorBrush(M.Color.FromArgb(120, 24, 26, 32)) };
        var edge = new S.Path { Stroke = new M.SolidColorBrush(M.Color.FromRgb(44, 232, 198)), StrokeThickness = 2,
            IsHitTestVisible = false, Effect = new System.Windows.Media.Effects.DropShadowEffect
                { Color = M.Color.FromRgb(44, 232, 198), BlurRadius = 14, ShadowDepth = 0, Opacity = .5 } };
        rootTile.Children.Add(background); rootTile.Children.Add(image); rootTile.Children.Add(edge);
        var scale = new M.ScaleTransform(1, 1); rootTile.RenderTransform = scale;
        return new Tile { Index = index, Key = items[index].Key, Visual = rootTile, Image = image, Edge = edge, Scale = scale };
    }
    private static void ResizeGridTile(Tile tile, double width, double height, bool hex)
    {
        var visual = tile.Visual!;
        if (visual.Width == width && visual.Height == height) return;
        visual.Width = width; visual.Height = height;
        M.Geometry shape;
        if (hex)
        {
            var geometry = new M.StreamGeometry();
            using var context = geometry.Open();
            for (int i = 0; i < 6; i++)
            {
                double a = (60 * i - 30) * Math.PI / 180;
                var point = new W.Point(width / 2 + height / 2 * Math.Cos(a), height / 2 + height / 2 * Math.Sin(a));
                if (i == 0) context.BeginFigure(point, true, true); else context.LineTo(point, true, false);
            }
            shape = geometry;
        }
        else shape = new M.RectangleGeometry(new W.Rect(0, 0, width, height), 18, 18);
        shape.Freeze(); ((S.Path)visual.Children[0]).Data = shape; tile.Image!.Clip = shape; tile.Edge!.Data = shape;
    }

    private void BuildFlow()
    {
        columns = 1;
        int start = Math.Max(0, selected - 5), end = Math.Min(items.Length, selected + 6);
        var visible = Enumerable.Range(start, end - start).ToHashSet(); KeepOnly(visible);
        if (flowView.Children.Count == 0) flowView.Children.Add(new ModelVisual3D { Content = new AmbientLight(M.Colors.White) });
        double centerWidth = Math.Min(860, ActualWidth * .52);
        camera.Position = new Point3D(0, 0, 3.5556 * ActualWidth / (2 * Math.Tan(Math.PI / 8) * centerWidth));
        foreach (int index in visible)
        {
            if (tiles.ContainsKey(index)) continue;
            var placeholder = new M.DrawingImage(new M.GeometryDrawing(new M.SolidColorBrush(M.Color.FromRgb(24, 26, 32)), null,
                new M.RectangleGeometry(new W.Rect(0, 0, 16, 9))));
            placeholder.Freeze();
            // UV coordinates already map the full 16:9 bitmap to a 16:9 plane. UniformToFill would crop to a UV square.
            var texture = new M.ImageBrush(placeholder) { Stretch = M.Stretch.Fill };
            M.RenderOptions.SetBitmapScalingMode(texture, M.BitmapScalingMode.HighQuality);
            var frame = new M.SolidColorBrush(M.Color.FromArgb(0, 44, 232, 198));
            var group = new Model3DGroup();
            group.Children.Add(new GeometryModel3D(RoundedPlane(3.5756, 2.02, -.015), new DiffuseMaterial(frame)));
            var model = new GeometryModel3D(RoundedPlane(3.5556, 2, 0), new DiffuseMaterial(texture)); group.Children.Add(model);
            var shade = new M.RadialGradientBrush();
            shade.GradientStops.Add(new M.GradientStop(M.Colors.Transparent, .4));
            shade.GradientStops.Add(new M.GradientStop(M.Color.FromArgb(110, 0, 0, 0), 1));
            shade.Freeze();
            group.Children.Add(new GeometryModel3D(RoundedPlane(3.5556, 2, .004), new DiffuseMaterial(shade)));
            var rotation = new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0);
            var scale = new ScaleTransform3D(); var translation = new TranslateTransform3D();
            var transform = new Transform3DGroup(); transform.Children.Add(scale);
            transform.Children.Add(new RotateTransform3D(rotation)); transform.Children.Add(translation);
            group.Transform = transform;
            var visual = new ModelVisual3D { Content = group }; flowView.Children.Add(visual);
            tiles[index] = new Tile { Index = index, Key = items[index].Key, Texture = texture, Frame = frame,
                Rotation = rotation, Scale3D = scale, Translation = translation, Model = model, ModelVisual = visual };
        }
    }
    private static MeshGeometry3D RoundedPlane(double width, double height, double z)
    {
        var mesh = new MeshGeometry3D(); mesh.Positions.Add(new Point3D(0, 0, z)); mesh.TextureCoordinates.Add(new W.Point(.5, .5));
        const double radius = .10;
        foreach (var corner in new[] { (width / 2 - radius, height / 2 - radius, 0d), (-width / 2 + radius, height / 2 - radius, 90d),
            (-width / 2 + radius, -height / 2 + radius, 180d), (width / 2 - radius, -height / 2 + radius, 270d) })
            for (int i = 0; i <= 8; i++)
            {
                double a = (corner.Item3 + i * 90d / 8) * Math.PI / 180;
                double x = corner.Item1 + radius * Math.Cos(a), y = corner.Item2 + radius * Math.Sin(a);
                mesh.Positions.Add(new Point3D(x, y, z)); mesh.TextureCoordinates.Add(new W.Point(x / width + .5, .5 - y / height));
            }
        for (int i = 1; i < mesh.Positions.Count; i++)
        {
            mesh.TriangleIndices.Add(0); mesh.TriangleIndices.Add(i); mesh.TriangleIndices.Add(i + 1 == mesh.Positions.Count ? 1 : i + 1);
        }
        mesh.Freeze(); return mesh;
    }

    internal static double Approach(double current, double target, double rate, double dt)
        => target + (current - target) * Math.Exp(-rate * Math.Clamp(dt, 0, .1));
    private void StartAnimation()
    {
        if (!IsVisible || rendering || disposed) return;
        rendering = true; frameClock.Restart(); lastSeconds = 0; M.CompositionTarget.Rendering += RenderFrame;
    }
    private void StopAnimation()
    {
        if (!rendering) return;
        rendering = false; M.CompositionTarget.Rendering -= RenderFrame;
    }
    private void RenderFrame(object? sender, EventArgs e)
    {
        if (!IsVisible || disposed) { StopAnimation(); return; }
        try
        {
            double now = frameClock.Elapsed.TotalSeconds, dt = Math.Max(.0001, now - lastSeconds); lastSeconds = now;
            flow = Approach(flow, selected, 19, dt);
            bool moving = Math.Abs(flow - selected) > .001;
            if (!moving) flow = selected;
            foreach (var tile in tiles.Values)
            {
                double active = tile.Index == selected ? 1 : 0;
                tile.Activation = Approach(tile.Activation, active, 22, dt);
                moving |= Math.Abs(tile.Activation - active) > .002;
                if (Math.Abs(tile.Activation - active) <= .002) tile.Activation = active;
                if (tile.Visual != null)
                {
                    tile.X = Approach(tile.X, tile.TargetX, 23, dt); tile.Y = Approach(tile.Y, tile.TargetY, 23, dt);
                    moving |= Math.Abs(tile.X - tile.TargetX) > .05 || Math.Abs(tile.Y - tile.TargetY) > .05;
                    C.Canvas.SetLeft(tile.Visual, tile.X); C.Canvas.SetTop(tile.Visual, tile.Y);
                    tile.Scale!.ScaleX = tile.Scale.ScaleY = 1 + .09 * tile.Activation;
                    tile.Edge!.Opacity = .10 + .9 * tile.Activation;
                    tile.Opacity = Approach(tile.Opacity, active == 1 ? 1 : .88, 18, dt); tile.Visual.Opacity = tile.Opacity;
                }
                else
                {
                    double offset = tile.Index - flow, distance = Math.Abs(offset);
                    tile.Translation!.OffsetX = offset * 2.15;
                    tile.Translation.OffsetZ = -Math.Min(distance, 5) * .75;
                    tile.Rotation!.Angle = -Math.Sign(offset) * Math.Min(distance, 1) * 48;
                    tile.Scale3D!.ScaleX = tile.Scale3D.ScaleY = 1 - Math.Min(.28, distance * .08);
                    double opacity = Math.Max(.20, 1 - distance * .19);
                    tile.Opacity = Approach(tile.Opacity, opacity, 22, dt); tile.Texture!.Opacity = tile.Opacity;
                    tile.Frame!.Color = M.Color.FromArgb((byte)(255 * tile.Activation), 44, 232, 198);
                    moving |= Math.Abs(tile.Opacity - opacity) > .002;
                }
            }
            if (!moving) StopAnimation();
        }
        catch (Exception error) { StopAnimation(); CloseOverlay(); Log.Error(error); RenderFailed?.Invoke(error); }
    }

    private int HitIndex(W.Point point)
    {
        if (settings.Layout == LayoutMode.CoverFlow)
        {
            var local = root.TranslatePoint(point, flowView);
            int found = -1;
            M.VisualTreeHelper.HitTest(flowView, null, result =>
            {
                if (result is RayMeshGeometry3DHitTestResult hit)
                {
                    var tile = tiles.Values.FirstOrDefault(t => t.ModelVisual == hit.VisualHit);
                    if (tile != null) { found = tile.Index; return M.HitTestResultBehavior.Stop; }
                }
                return M.HitTestResultBehavior.Continue;
            }, new M.PointHitTestParameters(local));
            return found;
        }
        W.DependencyObject? target = grid.InputHitTest(root.TranslatePoint(point, grid)) as W.DependencyObject;
        while (target != null && target != grid)
        {
            if (target is C.Grid { Tag: int index }) return index;
            target = M.VisualTreeHelper.GetParent(target);
        }
        return -1;
    }
    private void MouseMoved(object sender, I.MouseEventArgs e)
    {
        var point = e.GetPosition(root);
        if (!double.IsNaN(lastMouse.X) && (point - lastMouse).Length < 2) return;
        lastMouse = point;
        int index = HitIndex(point); if (index >= 0) SelectIndex(index);
    }
    private void ReleaseTiles()
    {
        grid.Children.Clear(); flowView.Children.Clear(); tiles.Clear();
    }
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!disposed) { e.Cancel = true; CloseOverlay(); }
        base.OnClosing(e);
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; StopAnimation();
        cache.Changed -= CacheChanged; cache.Ready -= SourcesReady; selectedPreview.Ready -= SourcesReady;
        ReleaseTiles(); selectedPreview.Dispose(); Close();
    }
}
