using System.Text.Json;
using System.Text.Json.Serialization;

namespace HexaWall;

internal enum LayoutMode { Honeycomb, CoverFlow, Cards }
internal enum TransitionMode { Hexagons, Circle, Fade }

internal sealed class Settings
{
    public string Folder { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
    public LayoutMode Layout { get; set; } = LayoutMode.Honeycomb;
    public TransitionMode Transition { get; set; } = TransitionMode.Circle;
    public bool AllMonitors { get; set; } = true;
    public bool PauseInFullscreen { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public uint HotKeyModifiers { get; set; } = 1;
    public uint HotKeyVirtualKey { get; set; } = 0x57;
    public int TransitionMilliseconds { get; set; } = 650;
    public int ThumbnailMemoryMiB { get; set; } = 64;
    public int DiskCacheMiB { get; set; } = 256;
    public static string DataDirectory => Environment.GetEnvironmentVariable("HEXAWALL_DATA_HOME") is { Length: > 0 } custom
        ? Path.GetFullPath(custom)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HexaWall");
    public static string FileName => Path.Combine(DataDirectory, "settings.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static Settings Load()
    {
        Directory.CreateDirectory(DataDirectory);
        try
        {
            var value = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FileName), Json) ?? new();
            value.TransitionMilliseconds = Math.Clamp(value.TransitionMilliseconds, 100, 2000);
            value.ThumbnailMemoryMiB = Math.Clamp(value.ThumbnailMemoryMiB, 32, 256);
            value.DiskCacheMiB = Math.Clamp(value.DiskCacheMiB, 32, 2048);
            return value;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(DataDirectory);
        File.WriteAllText(FileName + ".tmp", JsonSerializer.Serialize(this, Json));
        File.Move(FileName + ".tmp", FileName, true);
    }

    public Settings Copy() => (Settings)MemberwiseClone();
    public void CopyFrom(Settings other)
    {
        Folder = other.Folder; Layout = other.Layout; Transition = other.Transition;
        AllMonitors = other.AllMonitors; PauseInFullscreen = other.PauseInFullscreen;
        StartWithWindows = other.StartWithWindows;
        HotKeyModifiers = other.HotKeyModifiers; HotKeyVirtualKey = other.HotKeyVirtualKey;
        TransitionMilliseconds = other.TransitionMilliseconds;
        ThumbnailMemoryMiB = other.ThumbnailMemoryMiB; DiskCacheMiB = other.DiskCacheMiB;
    }
}

internal static class Log
{
    private static readonly object Gate = new();
    public static void Error(Exception e)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Settings.DataDirectory);
                string file = Path.Combine(Settings.DataDirectory, "error.log");
                if (File.Exists(file) && new FileInfo(file).Length > 1_000_000) File.Delete(file);
                File.AppendAllText(file, $"{DateTimeOffset.Now:O} {e}\n");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
