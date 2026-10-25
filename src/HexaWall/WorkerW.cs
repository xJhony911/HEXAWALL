using System.ComponentModel;
using System.Runtime.InteropServices;

namespace HexaWall;

internal static class WorkerW
{
    // 0x052C and the shell window hierarchy are undocumented. Never hide or destroy shell windows.
    public static nint FindHost()
    {
        nint progman = Native.FindWindow("Progman", null);
        if (progman == 0) return 0;
        if (Native.SendMessageTimeout(progman, 0x052C, 0xD, 1, 2, 1000, out _) == 0) return 0;
        Native.SendMessageTimeout(progman, 0x052C, 0, 0, 2, 1000, out _);
        nint host = 0;
        Native.EnumWindows((window, _) =>
        {
            nint icons = Native.FindWindowEx(window, 0, "SHELLDLL_DefView", null);
            if (icons == 0) return true;
            // Classic shell: empty WorkerW immediately behind the top-level icon host.
            nint candidate = Native.FindWindowEx(0, window, "WorkerW", null);
            if (Valid(candidate)) { host = candidate; return false; }
            // Newer shell builds can place the empty WorkerW inside Progman.
            candidate = Native.FindWindowEx(window, icons, "WorkerW", null);
            if (Valid(candidate)) { host = candidate; return false; }
            return true;
        }, 0);
        return host;
    }

    private static bool Valid(nint host) => host != 0 && Native.IsWindow(host)
        && Native.IsWindowVisible(host)
        && Native.FindWindowEx(host, 0, "SHELLDLL_DefView", null) == 0
        && Native.GetWindowRect(host, out var bounds) && bounds.Right > bounds.Left && bounds.Bottom > bounds.Top;

    public static void Attach(Form form, nint host, Rectangle screen)
    {
        if (!Valid(host)) throw new InvalidOperationException("WorkerW no disponible.");
        if (!Native.GetWindowRect(host, out var hostRect) || !hostRect.Rectangle.Contains(screen))
            throw new InvalidOperationException("WorkerW no cubre el monitor; se usará el fondo estático.");
        nint handle = form.Handle;
        long style = Native.GetWindowLongPtr(handle, Native.GWL_STYLE).ToInt64();
        Native.SetWindowLongPtr(handle, Native.GWL_STYLE, (nint)((style & ~Native.WS_POPUP) | Native.WS_CHILD));
        Marshal.SetLastPInvokeError(0);
        nint old = Native.SetParent(handle, host);
        if (old == 0 && Marshal.GetLastPInvokeError() != 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
        if (Native.GetParent(handle) != host) throw new InvalidOperationException("No fue posible adjuntar WorkerW.");
        var point = new Native.Point { X = screen.X, Y = screen.Y };
        Native.MapWindowPoints(0, host, ref point, 1);
        if (!Native.SetWindowPos(handle, 0, point.X, point.Y, screen.Width, screen.Height, 0x0010 | 0x0020 | 0x0004))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
    }
}
