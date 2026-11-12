using Microsoft.Win32;

namespace DualSensePrecision.App.Services;

/// <summary>
/// "Start with Windows": a per-user entry under HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
/// No admin rights; the user can also see and switch it off in Task Manager → Startup apps.
/// The app starts minimised to the tray when launched this way.
/// </summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DualSense Precision";
    public const string MinimizedArg = "--minimized";

    private static string Command => $"\"{Environment.ProcessPath}\" {MinimizedArg}";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(RunKey);
                return k?.GetValue(ValueName) is string;
            }
            catch { return false; }
        }
    }

    public static void Set(bool enabled)
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) k.SetValue(ValueName, Command);
        else if (k.GetValue(ValueName) != null) k.DeleteValue(ValueName);
    }

    /// <summary>Keeps the entry pointing at this copy of the app (e.g. after an update moved it).</summary>
    public static void Refresh()
    {
        try
        {
            if (!IsEnabled) return;
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (k?.GetValue(ValueName) as string != Command) k?.SetValue(ValueName, Command);
        }
        catch { }
    }
}
