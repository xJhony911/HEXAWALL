using System.Drawing.Imaging;
using System.Runtime.InteropServices;
namespace HexaWall;
internal sealed class LayeredSurface : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct Header { public uint Size; public int Width, Height; public ushort Planes, Bits; public uint Compression, ImageSize; public int X, Y; public uint Used, Important; }
    [StructLayout(LayoutKind.Sequential)] private struct Blend { public byte Operation, Flags, Alpha, Format; }
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint dc, ref Header header, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint item);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint item);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(nint window, nint destination, ref Point point, ref Size size, nint source, ref Point origin, uint key, ref Blend blend, uint flags);
    private readonly nint dc, dib, previous;
    public Bitmap Bitmap { get; }
    public Graphics Graphics { get; }
    public LayeredSurface(int width, int height)
    {
        dc = CreateCompatibleDC(0);
        var header = new Header { Size = (uint)Marshal.SizeOf<Header>(), Width = width, Height = -height, Planes = 1, Bits = 32 };
        dib = CreateDIBSection(dc, ref header, 0, out nint bits, 0, 0);
        if (dc == 0 || dib == 0) { if (dc != 0) DeleteDC(dc); throw new System.ComponentModel.Win32Exception(); }
        previous = SelectObject(dc, dib);
        Bitmap = new Bitmap(width, height, width * 4, PixelFormat.Format32bppPArgb, bits);
        Graphics = Graphics.FromImage(Bitmap);
    }
    public void Present(Form window)
    {
        Graphics.Flush(); var point = window.Location; var size = window.Size; var origin = Point.Empty;
        var blend = new Blend { Alpha = 255, Format = 1 };
        if (!UpdateLayeredWindow(window.Handle, 0, ref point, ref size, dc, ref origin, 0, ref blend, 2))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
    }
    public void Dispose() { Graphics.Dispose(); Bitmap.Dispose(); SelectObject(dc, previous); DeleteObject(dib); DeleteDC(dc); }
}
