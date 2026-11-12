using DualSensePrecision.Configuration;

namespace DualSensePrecision.App.Services;

/// <summary>
/// A small local log for troubleshooting: one file per day in %LOCALAPPDATA%\DualSensePrecision\logs, kept 7 days.
/// It records what the app did (start, controller connected, virtual pad state, HidHide changes, errors) —
/// never stick/button input. Nothing is sent anywhere; "Export diagnostics" lets the user choose to share it.
/// </summary>
public static class AppLog
{
    private static readonly object Lock = new();
    private const int KeepDays = 7;

    public static string CurrentFile => Path.Combine(AppPaths.LogsDir, $"app-{DateTime.Now:yyyy-MM-dd}.log");

    public static void Info(string message) => Write("INFO ", message);
    public static void Warn(string message) => Write("WARN ", message);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", ex == null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(AppPaths.LogsDir);
                File.AppendAllText(CurrentFile, $"{DateTime.Now:HH:mm:ss.fff} {level} {message}{Environment.NewLine}");
            }
        }
        catch { /* logging must never break the app */ }
    }

    /// <summary>Deletes log files older than a week.</summary>
    public static void Prune()
    {
        try
        {
            if (!Directory.Exists(AppPaths.LogsDir)) return;
            foreach (var f in Directory.EnumerateFiles(AppPaths.LogsDir, "*.log"))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-KeepDays)) File.Delete(f);
        }
        catch { }
    }
}
