using System.Security.Cryptography;
using System.Text;
using SkiaSharp;

namespace HexaWall;

internal static class WallpaperFile
{
    // Windows wallpaper support for WebP varies. Commit an immutable JPEG instead of relying on codecs installed on the machine.
    public static string Prepare(string path, CancellationToken token)
    {
        path = Path.GetFullPath(path);
        if (!File.Exists(path)) throw new FileNotFoundException("La imagen ya no existe.", path);
        if (!Path.GetExtension(path).Equals(".webp", StringComparison.OrdinalIgnoreCase)) return path;
        var file = new FileInfo(path);
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{path}|{file.Length}|{file.LastWriteTimeUtc.Ticks}")));
        string directory = Path.Combine(Settings.DataDirectory, "converted"); Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, key + ".jpg");
        if (File.Exists(target)) return target;
        token.ThrowIfCancellationRequested();
        using var bitmap = ImageLoader.Load(path, 8192);
        using var data = bitmap.Encode(SKEncodedImageFormat.Jpeg, 95);
        string temp = target + ".tmp";
        using (var output = File.Create(temp)) data.SaveTo(output);
        token.ThrowIfCancellationRequested();
        File.Move(temp, target, true);
        return target;
    }

    public static void Prune(IEnumerable<string> active)
    {
        string directory = Path.Combine(Settings.DataDirectory, "converted");
        if (!Directory.Exists(directory)) return;
        var keep = new HashSet<string>(active, StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var file in new DirectoryInfo(directory).GetFiles("*.jpg").OrderByDescending(x => x.LastWriteTimeUtc)
                .Where(x => !keep.Contains(x.FullName)).Skip(3)) file.Delete();
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
