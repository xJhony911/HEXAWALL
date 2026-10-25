using System.ComponentModel;
using System.Runtime.InteropServices;

namespace HexaWall;

internal sealed class HotKeyWindow : NativeWindow, IDisposable
{
    public event Action? Pressed;
    public event Action? ShellRestarted;
    public event Action? DisplayChanged;
    private readonly uint taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
    private int id = 1;
    private bool registered;
    public uint Modifiers { get; private set; }
    public uint VirtualKey { get; private set; }
    public string? StartupError { get; private set; }

    public HotKeyWindow(Settings settings, bool allowUnregistered = false)
    {
        CreateHandle(new CreateParams { Caption = "HexaWall.Messages", ExStyle = 0x80 });
        Modifiers = settings.HotKeyModifiers; VirtualKey = settings.HotKeyVirtualKey;
        if (!TryChange(settings.HotKeyModifiers, settings.HotKeyVirtualKey, out string error))
        {
            if (!allowUnregistered) { DestroyHandle(); throw new Win32Exception(error); }
            StartupError = error;
        }
    }

    public static string Format(uint modifiers, uint virtualKey)
    {
        var parts = new List<string>();
        if ((modifiers & 2) != 0) parts.Add("Ctrl");
        if ((modifiers & 1) != 0) parts.Add("Alt");
        if ((modifiers & 4) != 0) parts.Add("Shift");
        if ((modifiers & 8) != 0) parts.Add("Win");
        parts.Add(((Keys)virtualKey).ToString());
        return string.Join(" + ", parts);
    }

    public bool TryChange(uint modifiers, uint virtualKey, out string error)
    {
        error = "";
        if (modifiers == 0 || (modifiers & ~15u) != 0 || virtualKey is 0 or > 254 or 0x7B)
        {
            error = "Usa al menos Ctrl, Alt, Shift o Win y una tecla. F12 está reservada por Windows."; return false;
        }
        if (registered && Modifiers == modifiers && VirtualKey == virtualKey) return true;
        int replacement = id == 1 ? 2 : 1;
        if (!Native.RegisterHotKey(Handle, replacement, modifiers | 0x4000, virtualKey))
        {
            int code = Marshal.GetLastPInvokeError();
            error = $"{Format(modifiers, virtualKey)} no está disponible (Win32 {code}). Se conserva el atajo anterior.";
            return false;
        }
        if (registered) Native.UnregisterHotKey(Handle, id);
        id = replacement; registered = true; Modifiers = modifiers; VirtualKey = virtualKey;
        return true;
    }

    public void Suspend()
    {
        if (registered) Native.UnregisterHotKey(Handle, id);
        registered = false;
    }
    public bool Resume(out string error) => TryChange(Modifiers, VirtualKey, out error);

    protected override void WndProc(ref Message m)
    {
        if (registered && m.Msg == Native.WM_HOTKEY && m.WParam == id) Pressed?.Invoke();
        else if ((uint)m.Msg == taskbarCreated) ShellRestarted?.Invoke();
        else if (m.Msg == Native.WM_DISPLAYCHANGE) DisplayChanged?.Invoke();
        base.WndProc(ref m);
    }
    public void Dispose() { Suspend(); DestroyHandle(); }
}
