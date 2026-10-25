using System.Runtime.InteropServices;

namespace HexaWall;

[ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDesktopWallpaper
{
    void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitor, [MarshalAs(UnmanagedType.LPWStr)] string path);
    void GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitor, [MarshalAs(UnmanagedType.LPWStr)] out string path);
    void GetMonitorDevicePathAt(uint index, [MarshalAs(UnmanagedType.LPWStr)] out string monitor);
    void GetMonitorDevicePathCount(out uint count);
    void GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitor, out Native.Rect rect);
    void SetBackgroundColor(uint color);
    void GetBackgroundColor(out uint color);
    void SetPosition(int position);
    void GetPosition(out int position);
}

internal sealed record MonitorWallpaper(string Id, Rectangle Bounds, string Path, int Position, uint Color);

internal sealed class DesktopWallpaper : IDisposable
{
    private readonly IDesktopWallpaper api = (IDesktopWallpaper)Activator.CreateInstance(
        Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD"), true)!)!;

    public MonitorWallpaper[] Read()
    {
        api.GetMonitorDevicePathCount(out uint count);
        api.GetPosition(out int position);
        api.GetBackgroundColor(out uint color);
        var monitors = new List<MonitorWallpaper>();
        for (uint i = 0; i < count; i++)
        {
            api.GetMonitorDevicePathAt(i, out string id);
            api.GetMonitorRECT(id, out var rect);
            api.GetWallpaper(id, out string path);
            if (rect.Right > rect.Left && rect.Bottom > rect.Top)
                monitors.Add(new(id, rect.Rectangle, path, position, color));
        }
        return monitors.ToArray();
    }

    public void Apply(string path, string? monitor)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("La imagen ya no existe.", path);
        api.SetPosition(4); // DWPOS_FILL. New selections deliberately use Fill.
        api.SetWallpaper(monitor, Path.GetFullPath(path));
        try { WallpaperFile.Prune(Read().Select(m => m.Path)); }
        catch (COMException e) { Log.Error(e); }
    }
    public void Dispose() => Marshal.FinalReleaseComObject(api);
}
