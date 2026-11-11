using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using DualSensePrecision.App.Services;
using DualSensePrecision.Core;

namespace DualSensePrecision.App.ViewModels;

public sealed record InfoRow(string Label, string Value);

/// <summary>Controller identity + live raw inputs + raw HID report monitor. Everything shown is read from the device.</summary>
public sealed partial class ControllerViewModel : ObservableObject
{
    private readonly byte[] _raw = new byte[64];
    private long _lastVersion = -1;

    [ObservableProperty] public partial bool IsConnected { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SticksLabel), nameof(RawTitle), nameof(RawHint))]
    public partial bool IsXbox { get; set; }

    public string SticksLabel => IsXbox ? "STICKS (−1 to +1, full 16-bit resolution)" : "STICKS (0–255, 128 = centre)";
    public string RawTitle => IsXbox ? "Raw XInput state" : "Raw HID input report";
    public string RawHint => IsXbox
        ? "Exact values Windows returned: packet number (bytes 00–03), buttons (04–05), LT, RT, then left X/Y and right X/Y as signed 16-bit."
        : "Exact bytes from the latest report (report ID 0x01 at offset 00).";
    [ObservableProperty] public partial string Headline { get; set; } = "No controller";
    [ObservableProperty] public partial string Battery { get; set; } = "—";
    [ObservableProperty] public partial IReadOnlyList<InfoRow> Rows { get; set; } = Array.Empty<InfoRow>();

    [ObservableProperty] public partial string Sticks { get; set; } = "";
    [ObservableProperty] public partial string Triggers { get; set; } = "";
    [ObservableProperty] public partial string Buttons { get; set; } = "";
    [ObservableProperty] public partial string Motion { get; set; } = "";
    [ObservableProperty] public partial string Touch { get; set; } = "";
    [ObservableProperty] public partial string RawHex { get; set; } = "";

    public void RefreshIdentity()
    {
        var c = AppServices.Controller;
        IsConnected = c.Status == ConnectionStatus.Connected && c.Info != null;
        if (!IsConnected)
        {
            Headline = c.Status == ConnectionStatus.Error ? (c.LastError ?? "Connection problem") : "No controller connected";
            Rows = Array.Empty<InfoRow>();
            return;
        }

        var i = c.Info!;
        Headline = i.Name;
        IsXbox = i.Family == ControllerFamily.Xbox;
        if (IsXbox)
        {
            Rows = new InfoRow[]
            {
                new("Type", "Xbox controller (read through XInput)"),
                new("Connection", i.ConnectionLabel),
                new("Vendor / product ID", i.VendorId == 0 ? "Not reported by Windows" : $"0x{i.VendorId:X4} / 0x{i.ProductId:X4}"),
                new("XInput slot", i.DevicePath.Replace("XInput slot ", "")),
                new("Firmware", "Not available through XInput"),
                new("Motion sensors, touchpad, lights", "Not on this controller"),
            };
            return;
        }
        Rows = new InfoRow[]
        {
            new("Model", i.Model == DualSenseModel.DualSenseEdge ? "DualSense Edge" : "DualSense"),
            new("Connection", i.IsMock ? "Simulated (development mode)" : "USB (wired)"),
            new("Vendor / product ID", $"0x{i.VendorId:X4} / 0x{i.ProductId:X4}"),
            new("Identifier", i.SerialOrMac ?? "Not reported"),
            new("Firmware", i.FirmwareVersion ?? "Not reported by this controller"),
            new("Hardware version", i.HardwareVersion ?? "Not reported"),
            new("Firmware build", i.FirmwareBuildDate ?? "Not reported"),
            new("IMU calibration", c.Calibration.IsValid ? "Loaded from controller" : "Unavailable — raw values shown"),
            new("Report sizes", $"in {i.InputReportLength} B · out {i.OutputReportLength} B"),
            new("Device path", i.DevicePath),
        };
    }

    public void RefreshLive()
    {
        var c = AppServices.Controller;
        if (c.Status != ConnectionStatus.Connected) return;
        long v = c.Pipeline.Sample(out var s, out _);
        if (v == _lastVersion) return;
        _lastVersion = v;

        var (bv, bd) = ControllerText.Battery(s);
        Battery = s.BatteryKind == BatteryKind.Percent ? (s.Charging switch
        {
            ChargingState.Charging => $"{s.BatteryPercent}% · charging",
            ChargingState.Full => "100% · full",
            ChargingState.Error => "Battery error reported",
            _ => $"{s.BatteryPercent}%",
        }) : $"{bv} · {bd}";

        bool xbox = c.Family == ControllerFamily.Xbox;
        if (xbox)
        {
            Sticks = $"L  X {s.LX,7:+0.0000;-0.0000; 0.0000}  Y {s.LY,7:+0.0000;-0.0000; 0.0000}     R  X {s.RX,7:+0.0000;-0.0000; 0.0000}  Y {s.RY,7:+0.0000;-0.0000; 0.0000}";
            Triggers = $"LT {s.L2,3} ({s.L2 / 2.55,5:0.0}%)     RT {s.R2,3} ({s.R2 / 2.55,5:0.0}%)";
            Buttons = s.Buttons == DsButtons.None ? "none"
                : string.Join(" · ", Enum.GetValues<DsButtons>().Where(b => b != DsButtons.None && (s.Buttons & b) == b).Select(b => ButtonNames.Name(b, ControllerFamily.Xbox)));
            Motion = "Not on this controller";
            Touch = "Not on this controller";
        }
        else
        {
            Sticks = $"L  X {s.LeftX,3}  Y {s.LeftY,3}     R  X {s.RightX,3}  Y {s.RightY,3}";
            Triggers = $"L2 {s.L2,3} ({s.L2 / 2.55,5:0.0}%)     R2 {s.R2,3} ({s.R2 / 2.55,5:0.0}%)";
            Buttons = s.Buttons == DsButtons.None ? "none" : s.Buttons.ToString().Replace(", ", " · ");
        }

        var cal = c.Calibration;
        if (!xbox) Motion = $"Gyro °/s  P {cal.GyroDegPerSec(0, s.GyroPitch),7:0.0}  Y {cal.GyroDegPerSec(1, s.GyroYaw),7:0.0}  R {cal.GyroDegPerSec(2, s.GyroRoll),7:0.0}\n" +
                 $"Accel g   X {cal.AccelG(0, s.AccelX),7:0.00}  Y {cal.AccelG(1, s.AccelY),7:0.00}  Z {cal.AccelG(2, s.AccelZ),7:0.00}";
        if (!xbox) Touch = $"{Fmt(s.Touch0)}     {Fmt(s.Touch1)}";

        int n = c.CopyLatestRaw(_raw);
        var sb = new StringBuilder(n * 3 + 16);
        for (int k = 0; k < n; k++)
        {
            if (k % 16 == 0) sb.Append(k == 0 ? "" : "\n").Append($"{k:X2}  ");
            sb.Append(_raw[k].ToString("X2")).Append(' ');
        }
        RawHex = sb.ToString();
    }

    private static string Fmt(TouchPoint p) => p.Active ? $"#{p.Id} ({p.X,4}, {p.Y,4})" : "—";
}
