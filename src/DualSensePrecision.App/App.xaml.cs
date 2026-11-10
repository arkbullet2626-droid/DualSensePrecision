using DualSensePrecision.App.Services;
using Microsoft.UI.Xaml;

namespace DualSensePrecision.App;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            // Surface, don't swallow: log to disk and show the message in the shell's info bar.
            CrashLog.Write(e.Exception);
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

        AppServices.Initialize();
        _window = new MainWindow();
        _window.Closed += (_, _) => AppServices.Shutdown();
        _window.Activate();
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
        var h = FindWindow(null, "DualSense Precision");
        if (h == IntPtr.Zero) return;
        ShowWindow(h, 9); // SW_RESTORE
        SetForegroundWindow(h);
    }
}
