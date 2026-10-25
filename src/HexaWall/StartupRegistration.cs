using Microsoft.Win32;

namespace HexaWall;

internal interface IStartupRegistration
{
    bool IsEnabled { get; }
    string? Read();
    void Set(bool enabled);
    void Restore(string? value);
}

internal sealed class StartupRegistration : IStartupRegistration
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public string? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Key);
        return key?.GetValue("HexaWall") as string;
    }
    public bool IsEnabled => !string.IsNullOrWhiteSpace(Read());
    public void Set(bool enabled)
    {
        string executable = Environment.ProcessPath ?? throw new IOException("No se pudo localizar el ejecutable.");
        Restore(enabled ? $"\"{executable}\"" : null);
    }
    public void Restore(string? value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Key, true);
        if (value == null) key.DeleteValue("HexaWall", false);
        else key.SetValue("HexaWall", value, RegistryValueKind.String);
    }
}

internal sealed class ConfigurationController(Settings settings, HotKeyWindow hotkey, IStartupRegistration startup)
{
    public event Action<Settings>? Changed;

    public bool Apply(Settings candidate, out string error)
    {
        error = ""; candidate = candidate.Copy();
        try
        {
            candidate.Folder = Path.GetFullPath(candidate.Folder.Trim());
            if (!string.Equals(candidate.Folder, settings.Folder, StringComparison.OrdinalIgnoreCase) && !Directory.Exists(candidate.Folder))
                throw new IOException("La carpeta no existe o no está disponible.");
            if (!Enum.IsDefined(candidate.Layout) || !Enum.IsDefined(candidate.Transition))
                throw new ArgumentException("La opción seleccionada no es válida.");
            candidate.TransitionMilliseconds = Math.Clamp(candidate.TransitionMilliseconds, 100, 2000);
            bool startupChanged = candidate.StartWithWindows != startup.IsEnabled;
            string? oldRun = startupChanged ? startup.Read() : null;
            var previous = settings.Copy();
            if (!hotkey.TryChange(candidate.HotKeyModifiers, candidate.HotKeyVirtualKey, out error)) return false;
            try
            {
                if (startupChanged) startup.Set(candidate.StartWithWindows);
                candidate.Save();
            }
            catch
            {
                hotkey.TryChange(previous.HotKeyModifiers, previous.HotKeyVirtualKey, out _);
                if (startupChanged) startup.Restore(oldRun);
                throw;
            }
            settings.CopyFrom(candidate); Changed?.Invoke(previous); return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            error = e.Message; Log.Error(e); return false;
        }
    }
}
