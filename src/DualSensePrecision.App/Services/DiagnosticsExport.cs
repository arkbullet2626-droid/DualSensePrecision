using System.IO.Compression;
using System.Text;
using DualSensePrecision.Configuration;
using DualSensePrecision.Core;
using DualSensePrecision.HID.HidHide;

namespace DualSensePrecision.App.Services;

/// <summary>
/// Builds a .zip the user can choose to send when asking for help: the app's logs, its settings file and a short
/// system summary. Profiles aren't included. Nothing is uploaded — the user decides where the file goes.
/// </summary>
public static class DiagnosticsExport
{
    public static void Write(string zipPath)
    {
        if (File.Exists(zipPath)) File.Delete(zipPath);
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        var summary = zip.CreateEntry("summary.txt");
        using (var w = new StreamWriter(summary.Open(), Encoding.UTF8)) w.Write(Summary());

        if (File.Exists(AppPaths.SettingsFile))
            zip.CreateEntryFromFile(AppPaths.SettingsFile, "settings.json");

        if (Directory.Exists(AppPaths.LogsDir))
            foreach (var f in Directory.EnumerateFiles(AppPaths.LogsDir))
            {
                try
                {
                    // Copy first: today's log is still open for appending.
                    var tmp = Path.GetTempFileName();
                    File.Copy(f, tmp, true);
                    zip.CreateEntryFromFile(tmp, "logs/" + Path.GetFileName(f));
                    File.Delete(tmp);
                }
                catch { }
            }
        AppLog.Info($"Diagnostics exported to {Path.GetFileName(zipPath)}");
    }

    public static string Summary()
    {
        var c = AppServices.Controller;
        var sb = new StringBuilder();
        sb.AppendLine($"Dual Precision {UpdateChecker.Current}");
        sb.AppendLine($"Created: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Windows: {Environment.OSVersion.VersionString} ({(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")})");
        sb.AppendLine($".NET: {Environment.Version}");
        sb.AppendLine();
        sb.AppendLine($"Controller: {c.Status}{(c.IsMock ? " (simulated)" : "")}");
        if (c.Info is { } i)
        {
            sb.AppendLine($"  Name: {i.Name} · Family: {i.Family} · Connection: {i.ConnectionLabel}");
            sb.AppendLine($"  VID/PID: 0x{i.VendorId:X4}/0x{i.ProductId:X4} · Firmware: {i.FirmwareVersion ?? "n/a"}");
        }
        if (c.Telemetry?.Snapshot() is { HasData: true } t) sb.AppendLine($"  Measured rate: {t.MeasuredRateHz:0} Hz");
        sb.AppendLine();
        sb.AppendLine($"Virtual controller: {AppServices.Viiper?.State.ToString() ?? "off"}");
        sb.AppendLine($"XInput slots in use: {string.Join(", ", XInputProbe.Connected().Select(p => p.Slot + 1))}");
        sb.AppendLine($"HidHide: {(HidHideClient.IsInstalled ? "installed" : HidHideClient.IsRegistered ? "installed, restart pending" : "not installed")}");
        sb.AppendLine($"Hide real controller: {(AppServices.Settings.HideRealController ? "on" : "off")} · hiding now: {AppServices.Hide.IsHiding}");
        sb.AppendLine($"Active profile: {AppServices.ActiveProfile?.Name ?? "none"}");
        return sb.ToString();
    }
}
