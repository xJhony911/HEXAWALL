using System.Drawing.Imaging;
using System.Runtime.InteropServices;
namespace HexaWall;

// Minimal ABI bindings to the Windows SDK wincodec.h interfaces. All COM pointers
// stay on the decoding thread and are released before the bitmap is published.
internal static unsafe class WicImage
{
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint mode);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, nint outer, uint context, ref Guid iid, out nint value);
    private static nint Slot(nint value, int slot) => (*(nint**)value)[slot];
    private static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }
    private static void Release(nint value) { if (value != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(value, 2))(value); }

    public static Bitmap Load(string path, int maximumWidth, int maximumHeight)
    {
        int initialized = CoInitializeEx(0, 0);
        nint factory = 0, decoder = 0, frame = 0, scaler = 0, converter = 0;
        Bitmap? result = null;
        try
        {
            var clsid = new Guid("cacaf262-9370-4615-a13b-9f5539da4c0a");
            var iid = new Guid("ec5ec8a9-c395-4314-9c77-54d7a935ff70");
            Check(CoCreateInstance(ref clsid, 0, 1, ref iid, out factory));
            fixed (char* filename = path)
                Check(((delegate* unmanaged[Stdcall]<nint, char*, nint, uint, uint, nint*, int>)Slot(factory, 3))(factory, filename, 0, 0x80000000, 0, &decoder));
            Check(((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Slot(decoder, 13))(decoder, 0, &frame));
            uint width, height;
            Check(((delegate* unmanaged[Stdcall]<nint, uint*, uint*, int>)Slot(frame, 3))(frame, &width, &height));
            if (width == 0 || height == 0 || (ulong)width * height > 32_000_000)
                throw new IOException("La imagen excede el límite de 32 megapíxeles.");
            double ratio = Math.Min(1, Math.Min((double)maximumWidth / width, (double)maximumHeight / height));
            uint w = (uint)Math.Max(1, (int)(width * ratio)), h = (uint)Math.Max(1, (int)(height * ratio));
            Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(factory, 11))(factory, &scaler));
            Check(((delegate* unmanaged[Stdcall]<nint, nint, uint, uint, uint, int>)Slot(scaler, 8))(scaler, frame, w, h, 3));
            Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(factory, 10))(factory, &converter));
            Guid format = new("6fddc324-4e03-4bfe-b185-3d77768dc910");
            Check(((delegate* unmanaged[Stdcall]<nint, nint, Guid*, uint, nint, double, uint, int>)Slot(converter, 8))(converter, scaler, &format, 0, 0, 0, 0));
            result = new Bitmap((int)w, (int)h, PixelFormat.Format32bppPArgb);
            var bits = result.LockBits(new Rectangle(0, 0, (int)w, (int)h), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try { Check(((delegate* unmanaged[Stdcall]<nint, nint, uint, uint, nint, int>)Slot(converter, 7))(converter, 0, (uint)bits.Stride, checked((uint)bits.Stride * h), bits.Scan0)); }
            finally { result.UnlockBits(bits); }
            return result;
        }
        catch { result?.Dispose(); throw; }
        finally
        {
            Release(converter); Release(scaler); Release(frame); Release(decoder); Release(factory);
            if (initialized >= 0) CoUninitialize();
        }
    }
}
