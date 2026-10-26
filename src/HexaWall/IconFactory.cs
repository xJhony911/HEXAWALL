using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace HexaWall;

internal enum MenuGlyph { Selector, Folder, Layout, Refresh, Settings, Exit }

internal static class IconFactory
{
    public static readonly Color Accent = ColorTranslator.FromHtml("#2CE8C6");

    // ICO is raster by definition. Each size is rendered directly from vector geometry, in memory.
    public static Icon Create()
    {
        int[] sizes = [16, 20, 24, 32, 48, 64, 128, 256];
        var images = new List<byte[]>();
        foreach (int size in sizes)
        {
            using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bitmap);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.ScaleTransform(size / 256f, size / 256f);
            using var dark = new SolidBrush(Color.FromArgb(24, 26, 32));
            using var hex = new GraphicsPath();
            var points = Enumerable.Range(0, 6).Select(i =>
            {
                double a = (60 * i - 30) * Math.PI / 180;
                return new PointF(128 + 108 * (float)Math.Cos(a), 128 + 108 * (float)Math.Sin(a));
            }).ToArray();
            hex.AddPolygon(points); g.FillPath(dark, hex);
            using var edge = new Pen(Accent, size <= 24 ? 18 : 11) { LineJoin = LineJoin.Round };
            g.DrawPath(edge, hex);
            using var ridge = new Pen(Accent, 10) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLines(ridge, new PointF[] { new(65, 158), new(109, 107), new(144, 145), new(172, 118), new(195, 158) });
            using var stream = new MemoryStream(); bitmap.Save(stream, ImageFormat.Png); images.Add(stream.ToArray());
        }
        using var ico = new MemoryStream();
        using (var writer = new BinaryWriter(ico, System.Text.Encoding.UTF8, true))
        {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
            int offset = 6 + sizes.Length * 16;
            for (int i = 0; i < sizes.Length; i++)
            {
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write(images[i].Length); writer.Write(offset); offset += images[i].Length;
            }
            foreach (var bytes in images) writer.Write(bytes);
        }
        ico.Position = 0;
        using var icon = new Icon(ico);
        return (Icon)icon.Clone();
    }

#if !LEAN
    public static System.Windows.Media.ImageSource WpfIcon()
    {
        using var icon = Create(); using var stream = new MemoryStream(); icon.Save(stream); stream.Position = 0;
        var source = System.Windows.Media.Imaging.BitmapFrame.Create(stream,
            System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
        source.Freeze(); return source;
    }

#endif
    public static Bitmap Glyph(MenuGlyph glyph, int size = 24)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias; g.ScaleTransform(size / 24f, size / 24f);
        using var pen = new Pen(Accent, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        switch (glyph)
        {
            case MenuGlyph.Selector:
                g.DrawPolygon(pen, new PointF[] { new(12, 2), new(21, 7), new(21, 17), new(12, 22), new(3, 17), new(3, 7) }); break;
            case MenuGlyph.Folder:
                g.DrawLines(pen, new PointF[] { new(3, 19), new(3, 5), new(9, 5), new(12, 8), new(21, 8), new(21, 19), new(3, 19) }); break;
            case MenuGlyph.Layout:
                foreach (int x in new[] { 3, 14 }) foreach (int y in new[] { 3, 14 }) g.DrawRectangle(pen, x, y, 7, 7); break;
            case MenuGlyph.Refresh:
                g.DrawArc(pen, 4, 4, 16, 16, 40, 285); g.DrawLines(pen, new PointF[] { new(17, 3), new(20, 7), new(15, 8) }); break;
            case MenuGlyph.Settings:
                g.DrawEllipse(pen, 5, 5, 14, 14); g.DrawEllipse(pen, 9, 9, 6, 6);
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4;
                    g.DrawLine(pen, 12 + 7 * (float)Math.Cos(a), 12 + 7 * (float)Math.Sin(a), 12 + 10 * (float)Math.Cos(a), 12 + 10 * (float)Math.Sin(a));
                }
                break;
            case MenuGlyph.Exit:
                g.DrawArc(pen, 4, 4, 16, 16, -45, 270); g.DrawLine(pen, 12, 2, 12, 12); break;
        }
        return bitmap;
    }
}
