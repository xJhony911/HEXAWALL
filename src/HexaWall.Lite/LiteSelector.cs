using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
namespace HexaWall;
internal sealed class LiteSelector : Form
{
    private readonly Settings settings;
    private readonly Form dim = new() { FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, TopMost = true,
        BackColor = Color.FromArgb(10, 12, 16), Opacity = .55, StartPosition = FormStartPosition.Manual };
    private LayeredSurface? surface;
    private readonly LiteCache cache = new();
    private LiteItem[] items = [];
    private LiteImages? images;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<LiteImages, byte> pendingImages = new();
    private readonly Dictionary<int, RectangleF> hit = [];
    private readonly System.Windows.Forms.Timer animation = new() { Interval = 16 };
    private readonly System.Windows.Forms.Timer rescan = new() { Interval = 300 };
    private readonly Stopwatch clock = new();
    private FileSystemWatcher? watcher;
    private readonly Font small = new("Segoe UI", 11), label = new("Segoe UI", 14);
    private int selected, columns = 1, first, version;
    private double flow, previous;
    private bool closing, loading, scanning;
    public string? SelectedPath { get; private set; }
    public bool ConfigureRequested { get; private set; }
    internal int VisibleImages => images?.Bitmaps.Count ?? 0;
    internal void CapturePreview(string path) => surface?.Bitmap.Save(path, ImageFormat.Png);
    public LiteSelector(Settings settings)
    {
        this.settings = settings;
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
        StartPosition = FormStartPosition.Manual; KeyPreview = true; Text = "HEXAWALL"; Icon = IconFactory.Create();
        var monitor = Screen.FromPoint(Cursor.Position).Bounds; dim.Bounds = monitor;
        // Bound the alpha surface even on 4K/8K monitors; the native dim window still covers the entire screen.
        Size = new Size(Math.Min(1440, monitor.Width), Math.Min(860, monitor.Height));
        Location = new Point(monitor.Left + (monitor.Width - Width) / 2, monitor.Top + (monitor.Height - Height) / 2);
        cache.Ready += Receive;
        animation.Tick += (_, _) =>
        {
            double seconds = clock.Elapsed.TotalSeconds, dt = Math.Min(.1, seconds - previous); previous = seconds;
            flow = selected + (flow - selected) * Math.Exp(-22 * dt); Draw();
            if (Math.Abs(flow - selected) < .005) { animation.Stop(); flow = selected; Draw(); }
        };
        rescan.Tick += async (_, _) => { rescan.Stop(); await Scan(); };
        MouseWheel += (_, e) => Select(selected - Math.Sign(e.Delta));
        MouseMove += (_, e) =>
        {
            int index = Hit(e.Location);
            if (settings.Layout != LayoutMode.CoverFlow && index >= 0) Select(index);
        };
        MouseDown += (_, e) =>
        {
            if (new Rectangle(Width - 74, 18, 48, 48).Contains(e.Location)) { ConfigureRequested = true; Close(); return; }
            int index = Hit(e.Location); if (index >= 0) { Select(index); Choose(); }
        };
        KeyDown += (_, e) =>
        {
            int delta = e.KeyCode switch { Keys.Left or Keys.A => -1, Keys.Right or Keys.D => 1,
                Keys.Up or Keys.W => -columns, Keys.Down or Keys.S => columns, _ => 0 };
            if (delta != 0) { Select(selected + delta); e.Handled = true; }
            else if (e.KeyCode == Keys.Escape) Close();
            else if (e.KeyCode == Keys.Enter) Choose();
            else if (e.KeyCode == Keys.F1) { ConfigureRequested = true; Close(); }
            else if (e.KeyCode == Keys.Tab) { settings.Layout = (LayoutMode)(((int)settings.Layout + 1) % 3); settings.Save(); first = 0; flow = selected; LoadVisible(); }
        };
    }
    protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x80000 | 0x80; return p; } }
    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e); dim.Show(); Owner = dim; BringToFront(); Activate(); Native.SetForegroundWindow(Handle);
        surface = new LayeredSurface(Width, Height); Draw(); await Scan();
        if (!closing && Directory.Exists(settings.Folder))
        {
            watcher = new FileSystemWatcher(settings.Folder) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
            watcher.Created += Watch; watcher.Deleted += Watch; watcher.Changed += Watch; watcher.Renamed += Watch;
            watcher.EnableRaisingEvents = true;
        }
    }
    private void Watch(object sender, FileSystemEventArgs e)
    {
        try { if (!closing) BeginInvoke((Action)(() => { if (!closing) { rescan.Stop(); rescan.Start(); } })); }
        catch (InvalidOperationException) { }
    }
    private async Task Scan()
    {
        if (scanning) { rescan.Start(); return; }
        scanning = true;
        try
        {
            var found = await Task.Run(() => LiteCache.Scan(settings.Folder));
            if (closing) return;
            items = found; selected = Math.Clamp(selected, 0, Math.Max(0, items.Length - 1)); flow = selected; LoadVisible();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Error(e); }
        finally { scanning = false; }
    }
    private int[] VisibleIndices()
    {
        if (settings.Layout == LayoutMode.CoverFlow)
        { columns = 1; int from = Math.Max(0, selected - 3); return Enumerable.Range(from, Math.Min(items.Length, selected + 4) - from).ToArray(); }
        columns = Math.Clamp((Width - 80) / 240, 1, 6);
        int rows = Math.Clamp((Height - 200) / 210, 1, 3), count = columns * rows;
        int row = selected / columns; first = Math.Clamp(first, Math.Max(0, row - rows + 1), row);
        return Enumerable.Range(first * columns, Math.Max(0, Math.Min(count, items.Length - first * columns))).ToArray();
    }
    private void LoadVisible()
    {
        if (closing) return; loading = true;
        version = cache.Request(items, VisibleIndices(), selected); Draw();
    }
    private void Receive(LiteImages ready)
    {
        if (closing) { ready.Dispose(); return; }
        pendingImages.TryAdd(ready, 0);
        try
        {
            BeginInvoke((Action)(() =>
            {
                if (!pendingImages.TryRemove(ready, out _)) return;
                if (closing || ready.Version != version) { ready.Dispose(); return; }
                images?.Dispose(); images = ready; loading = false; Draw();
            }));
        }
        catch (InvalidOperationException) { if (pendingImages.TryRemove(ready, out _)) ready.Dispose(); }
    }
    internal void Select(int value)
    {
        value = Math.Clamp(value, 0, Math.Max(0, items.Length - 1)); if (selected == value) return;
        selected = value; LoadVisible();
        if (settings.Layout == LayoutMode.CoverFlow) { clock.Restart(); previous = 0; animation.Start(); }
    }
    private void Choose() { if (items.Length > 0) { SelectedPath = items[selected].Path; Close(); } }
    private int Hit(Point point) => hit.OrderByDescending(p => p.Key == selected).FirstOrDefault(p => p.Value.Contains(point), new KeyValuePair<int, RectangleF>(-1, RectangleF.Empty)).Key;
    private static GraphicsPath Shape(RectangleF rectangle, bool hex)
    {
        var path = new GraphicsPath();
        if (hex)
        {
            var points = Enumerable.Range(0, 6).Select(i => { double a = (60 * i - 30) * Math.PI / 180; return new PointF(rectangle.X + rectangle.Width / 2 + rectangle.Width / 2 * (float)Math.Cos(a), rectangle.Y + rectangle.Height / 2 + rectangle.Height / 2 * (float)Math.Sin(a)); }).ToArray();
            path.AddPolygon(points);
        }
        else
        {
            float r = 18; path.AddArc(rectangle.X, rectangle.Y, r, r, 180, 90); path.AddArc(rectangle.Right - r, rectangle.Y, r, r, 270, 90);
            path.AddArc(rectangle.Right - r, rectangle.Bottom - r, r, r, 0, 90); path.AddArc(rectangle.X, rectangle.Bottom - r, r, r, 90, 90); path.CloseFigure();
        }
        return path;
    }
    private void Tile(Graphics g, int index, RectangleF rect, bool hex)
    {
        hit[index] = rect; using var shape = Shape(rect, hex);
        using var backdrop = new SolidBrush(Color.FromArgb(170, 24, 26, 32)); g.FillPath(backdrop, shape);
        if (images?.Bitmaps.TryGetValue(index, out var image) == true)
        {
            var saved = g.Save(); g.SetClip(shape);
            float factor = Math.Max(rect.Width / image.Width, rect.Height / image.Height);
            float sw = rect.Width / factor, sh = rect.Height / factor;
            g.DrawImage(image, rect, new RectangleF((image.Width - sw) / 2, (image.Height - sh) / 2, sw, sh), GraphicsUnit.Pixel);
            if (index != selected) { using var shade = new SolidBrush(Color.FromArgb(55, 0, 0, 0)); g.FillRectangle(shade, rect); }
            g.Restore(saved);
        }
        using var edge = new Pen(Color.FromArgb(index == selected ? 255 : 70, 44, 232, 198), index == selected ? 3 : 1); g.DrawPath(edge, shape);
    }
    private void Draw()
    {
        if (surface == null || closing) return;
        var g = surface.Graphics; g.CompositingMode = CompositingMode.SourceCopy; g.Clear(Color.Transparent);
        g.CompositingMode = CompositingMode.SourceOver; g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        hit.Clear(); int[] visible = VisibleIndices();
        if (settings.Layout == LayoutMode.CoverFlow)
        {
            foreach (int index in visible.OrderByDescending(i => Math.Abs(i - flow)))
            {
                double offset = index - flow, distance = Math.Abs(offset);
                float scale = (float)Math.Max(.52, 1 - distance * .16), w = Math.Min(640, Width * .48f) * scale, h = w * 9 / 16;
                float x = Width / 2f + (float)offset * Math.Min(220, Width * .16f) - w / 2;
                Tile(g, index, new RectangleF(x, (Height - 100) / 2f - h / 2, w, h), false);
            }
        }
        else
        {
            bool hex = settings.Layout == LayoutMode.Honeycomb; float cell = (Width - 80f) / columns;
            for (int i = 0; i < visible.Length; i++)
            {
                int row = i / columns, col = i % columns; float w = Math.Min(220, cell - 24), h = hex ? 200 : w * 9 / 16;
                if (visible[i] == selected) { w *= 1.04f; h *= 1.04f; }
                Tile(g, visible[i], new RectangleF(40 + col * cell + (cell - w) / 2 + (hex && row % 2 == 1 ? 10 : 0), 100 + row * 210, w, h), hex);
            }
        }
        using var white = new SolidBrush(Color.FromArgb(220, 240, 243, 246));
        using var accent = new SolidBrush(IconFactory.Accent);
        using var centered = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        string title = items.Length == 0 ? "F1 · Elige tu carpeta de wallpapers" : Path.GetFileNameWithoutExtension(items[selected].Path);
        g.DrawString(title, label, white, new RectangleF(60, Height - 100, Width - 120, 28), centered);
        g.DrawString($"{(items.Length == 0 ? 0 : selected + 1)} / {items.Length} · {settings.Layout} · Flechas / WASD · Enter · Tab · F1 · Esc" + (loading ? " · Cargando…" : ""), small, white, new RectangleF(20, Height - 58, Width - 40, 24), centered);
        using var gear = IconFactory.Glyph(MenuGlyph.Settings, 32); g.DrawImage(gear, Width - 66, 24, 32, 32);
        surface.Present(this);
    }
    protected override void OnPaint(PaintEventArgs e) { }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (!closing)
        {
            closing = true; watcher?.Dispose(); animation.Stop(); rescan.Stop();
            Owner = null; dim.Close();
        }
        base.OnFormClosed(e);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            closing = true; cache.Ready -= Receive; cache.Dispose(); images?.Dispose(); images = null;
            foreach (var pending in pendingImages.Keys) if (pendingImages.TryRemove(pending, out _)) pending.Dispose();
            surface?.Dispose(); surface = null; dim.Dispose(); animation.Dispose(); rescan.Dispose(); watcher?.Dispose(); small.Dispose(); label.Dispose(); Icon?.Dispose();
        }
        base.Dispose(disposing);
    }
}

