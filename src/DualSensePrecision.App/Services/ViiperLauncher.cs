using System.Diagnostics;
using Microsoft.Win32;

namespace DualSensePrecision.App.Services;

/// <summary>
/// VIIPER normally starts with Windows (its installer adds a Run entry). If it isn't running — the user quit it,
/// or installed it and hasn't signed out since — this starts the copy that's already installed, the same way
/// Windows would. It never downloads or installs anything, and tries at most once per app session.
/// </summary>
public static class ViiperLauncher
{
    private static int _tried;

    public static string DefaultExe =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VIIPER", "viiper.exe");

    /// <summary>Starts the installed VIIPER server if it isn't running. Returns a short note for the log.</summary>
    public static string? TryStartOnce()
    {
        if (Interlocked.Exchange(ref _tried, 1) == 1) return null;
        try
        {
            if (Process.GetProcessesByName("viiper").Length > 0)
                return "VIIPER is running but not answering yet.";

            var (exe, args) = FromRunEntry() ?? (DefaultExe, "server");
            if (args.Length == 0) args = "server";
            if (!File.Exists(exe)) return "VIIPER isn't installed.";

            Process.Start(new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(exe)!,
            });
            return $"Started the installed VIIPER ({Path.GetFileName(exe)} {args}).";
        }
        catch (Exception ex)
        {
            return "Couldn't start VIIPER: " + ex.Message;
        }
    }

    /// <summary>VIIPER's own autostart command (HKCU\...\Run "VIIPER"), split into file and arguments.</summary>
    private static (string Exe, string Args)? FromRunEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (key?.GetValue("VIIPER") is not string cmd || string.IsNullOrWhiteSpace(cmd)) return null;
        return SplitCommand(Environment.ExpandEnvironmentVariables(cmd.Trim()));
    }

    internal static (string Exe, string Args)? SplitCommand(string cmd)
    {
        if (cmd.StartsWith('"'))
        {
            int end = cmd.IndexOf('"', 1);
            if (end < 0) return null;
            return (cmd[1..end], cmd[(end + 1)..].Trim());
        }
        int exeEnd = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeEnd < 0) return null;
        exeEnd += 4;
        return (cmd[..exeEnd], cmd[exeEnd..].Trim());
    }
}
