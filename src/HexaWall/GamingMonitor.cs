using System.ComponentModel;
using System.Runtime.InteropServices;

namespace HexaWall;

internal sealed class GamingMonitor : IDisposable
{
    private delegate void WinEventProc(nint hook, uint evt, nint window, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventProc callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWinEvent(nint hook);
    private readonly WinEventProc callback;
    private readonly List<nint> hooks = [];
    private readonly System.Windows.Forms.Timer debounce = new() { Interval = 120 };
    public bool Busy { get; private set; }
    public event Action<bool>? Changed;

    public GamingMonitor()
    {
        // Out-of-context accessibility events. No keyboard/mouse hook, injected DLL or periodic polling.
        callback = (_, evt, window, objectId, childId, _, _) =>
        {
            if (evt == 0x800B && (objectId != 0 || childId != 0 || window != Native.GetForegroundWindow())) return;
            if (!debounce.Enabled) debounce.Start();
        };
        foreach (uint evt in new uint[] { 3, 0x000B, 0x800B })
        {
            nint hook = SetWinEventHook(evt, evt, 0, callback, 0, 0, 2);
            if (hook == 0)
            {
                var error = new Win32Exception(Marshal.GetLastPInvokeError());
                Dispose(); throw error;
            }
            hooks.Add(hook);
        }
        debounce.Tick += (_, _) => { debounce.Stop(); Refresh(); };
        Busy = Native.IsBusy();
    }

    public bool Refresh()
    {
        bool busy = Native.IsBusy();
        if (busy != Busy) { Busy = busy; Changed?.Invoke(busy); }
        return busy;
    }
    public void Dispose()
    {
        debounce.Dispose(); foreach (nint hook in hooks) UnhookWinEvent(hook); hooks.Clear();
        GC.KeepAlive(callback);
    }
}
