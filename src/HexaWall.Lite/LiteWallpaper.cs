using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text;
namespace HexaWall;
internal static class LiteWallpaper
{
    public static string Prepare(string path)
    {
        if (!Path.GetExtension(path).Equals(".webp", StringComparison.OrdinalIgnoreCase)) return path;
        var file = new FileInfo(path);
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}")));
        string directory = Path.Combine(Settings.DataDirectory, "wallpapers-lite"); Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, key + ".jpg");
        if (!File.Exists(target))
        {
            // Windows wallpaper COM does not accept WebP. Bound conversion memory as well as thumbnails.
            using var bitmap = WicImage.Load(path, 1920, 1080);
            bitmap.Save(target + ".tmp", ImageFormat.Jpeg); File.Move(target + ".tmp", target, true);
        }
        return target;
    }
}
