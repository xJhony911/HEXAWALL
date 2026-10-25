using SkiaSharp;

namespace HexaWall;

internal static class ImageLoader
{
    public static SKBitmap Load(string path, int maxEdge)
    {
        using var stream = File.OpenRead(path);
        using var managed = new SKManagedStream(stream);
        using var codec = SKCodec.Create(managed) ?? throw new IOException("Formato de imagen no compatible.");
        var original = codec.Info;
        if ((long)original.Width * original.Height > 32_000_000)
            throw new IOException("La imagen excede el límite de seguridad de 32 megapíxeles.");
        float scale = Math.Min(1f, (float)maxEdge / Math.Max(original.Width, original.Height));
        var dimensions = codec.GetScaledDimensions(scale);
        using var decoded = new SKBitmap(new SKImageInfo(dimensions.Width, dimensions.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        if (codec.GetPixels(decoded.Info, decoded.GetPixels()) != SKCodecResult.Success)
            throw new IOException("Imagen incompleta o corrupta.");
        int width = Math.Max(1, (int)(original.Width * scale));
        int height = Math.Max(1, (int)(original.Height * scale));
        bool swap = codec.EncodedOrigin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var result = new SKBitmap(swap ? height : width, swap ? width : height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(result);
        switch (codec.EncodedOrigin)
        {
            case SKEncodedOrigin.TopRight: canvas.Translate(width, 0); canvas.Scale(-1, 1); break;
            case SKEncodedOrigin.BottomRight: canvas.Translate(width, height); canvas.RotateDegrees(180); break;
            case SKEncodedOrigin.BottomLeft: canvas.Translate(0, height); canvas.Scale(1, -1); break;
            case SKEncodedOrigin.LeftTop: canvas.RotateDegrees(90); canvas.Scale(1, -1); break;
            case SKEncodedOrigin.RightTop: canvas.Translate(height, 0); canvas.RotateDegrees(90); break;
            case SKEncodedOrigin.RightBottom: canvas.Translate(height, width); canvas.RotateDegrees(90); canvas.Scale(-1, 1); break;
            case SKEncodedOrigin.LeftBottom: canvas.Translate(0, width); canvas.RotateDegrees(-90); break;
        }
        using var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };
        canvas.DrawBitmap(decoded, new SKRect(0, 0, width, height), paint);
        return result;
    }

    public static SKBitmap Preview(string path, int width, int height)
    {
        using var source = Load(path, Math.Max(width, height) * 2);
        var result = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };
        Fill(canvas, source, new SKRect(0, 0, width, height), paint);
        return result;
    }

    public static System.Windows.Media.Imaging.BitmapSource Freeze(SKBitmap bitmap)
    {
        var source = System.Windows.Media.Imaging.BitmapSource.Create(bitmap.Width, bitmap.Height, 96, 96,
            System.Windows.Media.PixelFormats.Pbgra32, null, bitmap.GetPixels(), bitmap.ByteCount, bitmap.RowBytes);
        source.Freeze();
        return source;
    }

    public static void Fill(SKCanvas canvas, SKBitmap bitmap, SKRect destination, SKPaint? paint = null)
    {
        float scale = Math.Max(destination.Width / bitmap.Width, destination.Height / bitmap.Height);
        float w = destination.Width / scale, h = destination.Height / scale;
        var source = SKRect.Create((bitmap.Width - w) / 2, (bitmap.Height - h) / 2, w, h);
        canvas.DrawBitmap(bitmap, source, destination, paint);
    }

    public static SKPath Hexagon(float x, float y, float radius)
    {
        var path = new SKPath();
        for (int i = 0; i < 6; i++)
        {
            float angle = MathF.PI / 180 * (60 * i - 30);
            var p = new SKPoint(x + radius * MathF.Cos(angle), y + radius * MathF.Sin(angle));
            if (i == 0) path.MoveTo(p); else path.LineTo(p);
        }
        path.Close();
        return path;
    }
}
