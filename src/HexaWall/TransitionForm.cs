using SkiaSharp;

namespace HexaWall;

internal sealed class TransitionForm : Form
{
    private readonly GpuView view = new() { Dock = DockStyle.Fill };
    private readonly SKBitmap? oldImage;
    private readonly SKBitmap newImage;
    private readonly MonitorWallpaper monitor;
    private readonly Rectangle desktop;
    private readonly Point origin;
    private readonly TransitionMode mode;
    private float progress;
    private bool first = true;
    public bool HasPainted { get; private set; }
    public event Action? FirstPaint;
    public event Action<Exception>? RenderFailed;

    public TransitionForm(MonitorWallpaper monitor, Rectangle desktop, SKBitmap? oldImage, SKBitmap newImage, TransitionMode mode, Point cursor)
    {
        this.monitor = monitor; this.desktop = desktop; this.oldImage = oldImage; this.newImage = newImage;
        this.mode = mode; origin = new Point(cursor.X - monitor.Bounds.X, cursor.Y - monitor.Bounds.Y);
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None; Bounds = monitor.Bounds;
        Controls.Add(view); view.PaintSurface += DrawFrame; view.RenderFailed += e => RenderFailed?.Invoke(e);
    }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            var p = base.CreateParams;
            p.ExStyle |= (int)(Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT);
            return p;
        }
    }

    public void Frame(float value) { progress = value; view.Refresh(); }

    private void DrawFrame(object? sender, GpuPaintEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var color = new SKColor((byte)(monitor.Color & 255), (byte)((monitor.Color >> 8) & 255), (byte)((monitor.Color >> 16) & 255));
        canvas.Clear(color);
        float width = monitor.Bounds.Width, height = monitor.Bounds.Height;
        canvas.Scale(e.Width / width, e.Height / height);
        var bounds = new SKRect(0, 0, width, height);
        using var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };
        if (oldImage != null) DrawOld(canvas, oldImage, bounds, paint);
        float t = Math.Clamp(progress, 0, 1);
        float eased = t * t * (3 - 2 * t);
        if (t > 0)
        {
            canvas.Save();
            if (mode == TransitionMode.Circle)
            {
                float radius = MathF.Sqrt(MathF.Pow(Math.Max(Math.Abs(origin.X), Math.Abs(width - origin.X)), 2)
                    + MathF.Pow(Math.Max(Math.Abs(origin.Y), Math.Abs(height - origin.Y)), 2));
                using var clip = new SKPath(); clip.AddCircle(origin.X, origin.Y, radius * eased + 1);
                canvas.ClipPath(clip, SKClipOperation.Intersect, true);
            }
            else if (mode == TransitionMode.Hexagons)
            {
                using var clip = new SKPath();
                const float radius = 84;
                float stepX = MathF.Sqrt(3) * radius, stepY = radius * 1.5f;
                for (int row = -1; row * stepY < height + radius; row++)
                    for (int col = -1; col * stepX < width + stepX; col++)
                    {
                        float x = col * stepX + (row % 2 == 0 ? 0 : stepX / 2), y = row * stepY;
                        float delay = .55f * Math.Clamp((x + y * .3f) / (width + height * .3f), 0, 1);
                        float local = Math.Clamp((eased - delay) / .45f, 0, 1);
                        if (local <= 0) continue;
                        using var hex = ImageLoader.Hexagon(x, y, (radius + 2) * local);
                        clip.AddPath(hex);
                    }
                canvas.ClipPath(clip, SKClipOperation.Intersect, true);
            }
            else paint.Color = SKColors.White.WithAlpha((byte)(255 * eased));
            ImageLoader.Fill(canvas, newImage, bounds, paint);
            canvas.Restore();
        }
        HasPainted = true;
        if (first)
        {
            first = false;
            // Start coordinator only after returning from the GL paint/swap.
            BeginInvoke((Action)(() => { if (!IsDisposed) FirstPaint?.Invoke(); }));
        }
    }

    private void DrawOld(SKCanvas canvas, SKBitmap image, SKRect bounds, SKPaint paint)
    {
        switch (monitor.Position)
        {
            case 0: // Center. The decoder's size is bounded, so this mode is approximate for huge originals.
                canvas.DrawBitmap(image, (bounds.Width - image.Width) / 2, (bounds.Height - image.Height) / 2, paint); break;
            case 1:
                using (var shader = SKShader.CreateBitmap(image, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat,
                    SKMatrix.CreateTranslation(-monitor.Bounds.X, -monitor.Bounds.Y)))
                using (var tile = new SKPaint { Shader = shader }) canvas.DrawRect(bounds, tile);
                break;
            case 2: canvas.DrawBitmap(image, bounds, paint); break;
            case 3:
                float scale = Math.Min(bounds.Width / image.Width, bounds.Height / image.Height);
                float w = image.Width * scale, h = image.Height * scale;
                canvas.DrawBitmap(image, SKRect.Create((bounds.Width - w) / 2, (bounds.Height - h) / 2, w, h), paint); break;
            case 5:
                canvas.Save(); canvas.Translate(desktop.X - monitor.Bounds.X, desktop.Y - monitor.Bounds.Y);
                ImageLoader.Fill(canvas, image, SKRect.Create(0, 0, desktop.Width, desktop.Height), paint); canvas.Restore(); break;
            default: ImageLoader.Fill(canvas, image, bounds, paint); break;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { view.Dispose(); oldImage?.Dispose(); newImage.Dispose(); }
        base.Dispose(disposing);
    }
}
