using DualSensePrecision.App.Services;
using Microsoft.UI.Xaml;

namespace DualSensePrecision.App;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        // Built-in controls (dropdowns, dialogs, flyouts) start in light or dark style to match the saved colours.
        // This must be set before App.xaml loads and before any window exists; the main window also switches live.
        bool light = false;
        try { light = (new DualSensePrecision.Configuration.SettingsStore().Load().Palette ?? new()).IsLight; }
        catch { /* default dark */ }
        RequestedTheme = light ? ApplicationTheme.Light : ApplicationTheme.Dark;
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            // Surface, don't swallow: log to disk and show the message in the shell's info bar.
            CrashLog.Write(e.Exception);
            AppLog.Error("Unhandled exception", e.Exception);
            e.Handled = _window != null;
            _window?.ShowError($"Unexpected error: {e.Message}");
        };
    }

    private static Mutex? _singleInstance;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _singleInstance = new Mutex(true, @"Local\DualSensePrecision.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            SingleInstance.BringExistingToFront();
            Exit();
            return;
        }

        AppLog.Prune();
        AppLog.Info($"Dual Precision {UpdateChecker.Current} starting on {Environment.OSVersion.VersionString} ({RequestedTheme.ToString().ToLowerInvariant()} pop-ups)");
        AppServices.Initialize();
        ThemeService.Apply(AppServices.Settings.Palette ?? new());
        StartupRegistration.Refresh();
        _window = new MainWindow();
        _window.Closed += (_, _) => AppServices.Shutdown();

        // Started by "Start with Windows": stay in the tray until the user opens it.
        bool minimized = Environment.GetCommandLineArgs().Contains(StartupRegistration.MinimizedArg, StringComparer.OrdinalIgnoreCase);
        if (minimized) _window.StartInTray();
        else _window.Activate();
    }

    /// <summary>Closes everything cleanly and starts a fresh copy of the app (used by "Reset app").</summary>
    public static void Restart()
    {
        try { AppServices.Shutdown(); } catch (Exception ex) { CrashLog.Write(ex); }
        try { _singleInstance?.ReleaseMutex(); } catch { }
        try { _singleInstance?.Dispose(); } catch { }
        _singleInstance = null;
        try { if (Environment.ProcessPath is { } exe) System.Diagnostics.Process.Start(exe); }
        catch (Exception ex) { CrashLog.Write(ex); }
        Current.Exit();
    }
}

internal static class CrashLog
{
    public static void Write(Exception ex)
    {
        try
        {
            var dir = DualSensePrecision.Configuration.AppPaths.LogsDir;
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "errors.log"), $"[{DateTime.Now:O}] {ex}\n\n");
        }
        catch { /* logging must never crash the app */ }
    }
}

internal static partial class SingleInstance
{
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string windowName);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int cmd);

    public static void BringExistingToFront()
    {
        var h = FindWindow(null, "Dual Precision");
        if (h == IntPtr.Zero) return;
        ShowWindow(h, 9); // SW_RESTORE
        SetForegroundWindow(h);
    }
}
