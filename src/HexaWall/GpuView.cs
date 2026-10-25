using System.ComponentModel;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace HexaWall;

internal sealed class GpuPaintEventArgs(SKSurface surface, int width, int height) : EventArgs
{
    public SKSurface Surface { get; } = surface;
    public int Width { get; } = width;
    public int Height { get; } = height;
}

// Owns a WGL context and a Skia GPU surface. No continuous render thread or UI toolkit GL dependency.
internal sealed class GpuView : Control
{
    [StructLayout(LayoutKind.Sequential)]
    private struct PixelFormat
    {
        public ushort Size, Version;
        public uint Flags;
        public byte PixelType, ColorBits, RedBits, RedShift, GreenBits, GreenShift, BlueBits, BlueShift,
            AlphaBits, AlphaShift, AccumBits, AccumRedBits, AccumGreenBits, AccumBlueBits, AccumAlphaBits,
            DepthBits, StencilBits, AuxBuffers, LayerType, Reserved;
        public uint LayerMask, VisibleMask, DamageMask;
    }
    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern int ChoosePixelFormat(nint dc, ref PixelFormat format);
    [DllImport("gdi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetPixelFormat(nint dc, int index, ref PixelFormat format);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SwapBuffers(nint dc);
    [DllImport("opengl32.dll", SetLastError = true)] private static extern nint wglCreateContext(nint dc);
    [DllImport("opengl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool wglDeleteContext(nint context);
    [DllImport("opengl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool wglMakeCurrent(nint dc, nint context);
    [DllImport("opengl32.dll", CharSet = CharSet.Ansi)] private static extern nint wglGetProcAddress(string name);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SwapInterval(int interval);
    private nint dc, gl;
    private GRGlInterface? glInterface;
    private GRContext? context;
    private GRBackendRenderTarget? target;
    private SKSurface? surface;
    private Size surfaceSize;
    private bool failed;
    public event EventHandler<GpuPaintEventArgs>? PaintSurface;
    public event Action<Exception>? RenderFailed;

    public GpuView()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque, true);
        ResizeRedraw = true;
    }
    protected override CreateParams CreateParams
    {
        get { var p = base.CreateParams; p.ClassStyle |= 0x0020; return p; } // CS_OWNDC
    }
    protected override void OnPaintBackground(PaintEventArgs e) { }

    private void MakeCurrent()
    {
        if (gl == 0)
        {
            dc = GetDC(Handle);
            var format = new PixelFormat { Size = (ushort)Marshal.SizeOf<PixelFormat>(), Version = 1,
                Flags = 0x00000004 | 0x00000020 | 0x00000001, PixelType = 0, ColorBits = 32, AlphaBits = 8, StencilBits = 8 };
            int index = ChoosePixelFormat(dc, ref format);
            if (index == 0 || !SetPixelFormat(dc, index, ref format)) throw new Win32Exception(Marshal.GetLastPInvokeError());
            gl = wglCreateContext(dc);
            if (gl == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        if (!wglMakeCurrent(dc, gl)) throw new InvalidOperationException("No se pudo activar el contexto OpenGL.");
        if (context == null)
        {
            glInterface = GRGlInterface.Create() ?? throw new InvalidOperationException("El controlador no ofrece una interfaz OpenGL compatible.");
            context = GRContext.CreateGl(glInterface) ?? throw new InvalidOperationException("Skia no pudo inicializar la GPU.");
            context.SetResourceCacheLimit(8 * 1024 * 1024);
            nint proc = wglGetProcAddress("wglSwapIntervalEXT");
            if (proc != 0 && proc != 1 && proc != 2 && proc != 3 && proc != -1)
                Marshal.GetDelegateForFunctionPointer<SwapInterval>(proc)(1);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (failed || Width <= 0 || Height <= 0) return;
        try
        {
            MakeCurrent();
            if (surface == null || surfaceSize != ClientSize)
            {
                surface?.Dispose(); target?.Dispose();
                surfaceSize = ClientSize;
                target = new GRBackendRenderTarget(Width, Height, 0, 8, new GRGlFramebufferInfo(0, 0x8058));
                surface = SKSurface.Create(context!, target, GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888)
                    ?? throw new InvalidOperationException("No se pudo crear el lienzo GPU.");
            }
            surface.Canvas.ResetMatrix();
            PaintSurface?.Invoke(this, new GpuPaintEventArgs(surface, Width, Height));
            surface.Canvas.Flush(); context!.Flush();
            if (!SwapBuffers(dc)) throw new InvalidOperationException("Falló SwapBuffers.");
        }
        catch (Exception error)
        {
            failed = true; Log.Error(error);
            BeginInvoke((Action)(() => { if (!IsDisposed) RenderFailed?.Invoke(error); }));
        }
        finally { wglMakeCurrent(0, 0); }
    }

    public void ReleaseResources()
    {
        if (gl == 0) return;
        wglMakeCurrent(dc, gl);
        surface?.Dispose(); surface = null; target?.Dispose(); target = null;
        context?.PurgeResources();
        wglMakeCurrent(0, 0);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (gl != 0)
        {
            wglMakeCurrent(dc, gl);
            surface?.Dispose(); surface = null; target?.Dispose(); target = null;
            context?.Dispose(); context = null; glInterface?.Dispose(); glInterface = null;
            wglMakeCurrent(0, 0); wglDeleteContext(gl); gl = 0;
        }
        if (dc != 0) { ReleaseDC(Handle, dc); dc = 0; }
        failed = false;
        base.OnHandleDestroyed(e);
    }
}
