using CommunityToolkit.Mvvm.ComponentModel;
using DualSensePrecision.App.Services;
using DualSensePrecision.Core;
using DualSensePrecision.Telemetry;
using DualSensePrecision.VirtualController;

namespace DualSensePrecision.App.ViewModels;

public sealed partial class PerformanceViewModel : ObservableObject
{
    private readonly CpuSampler _cpu = new();
    private long _lastPackets;
    private DateTime _lastPacketSample = DateTime.UtcNow;

    [ObservableProperty] public partial string SourceLabel { get; set; } = "";
    [ObservableProperty] public partial string MeasuredRate { get; set; } = "—";
    [ObservableProperty] public partial string AvgInterval { get; set; } = "—";
    [ObservableProperty] public partial string P50 { get; set; } = "—";
    [ObservableProperty] public partial string P95 { get; set; } = "—";
    [ObservableProperty] public partial string P99 { get; set; } = "—";
    [ObservableProperty] public partial string MaxInterval { get; set; } = "—";
    [ObservableProperty] public partial string Dropped { get; set; } = "—";
    [ObservableProperty] public partial string Stalls { get; set; } = "—";
    [ObservableProperty] public partial string ProcessingAvg { get; set; } = "—";
    [ObservableProperty] public partial string ProcessingP99 { get; set; } = "—";
    [ObservableProperty] public partial string Cpu { get; set; } = "—";
    [ObservableProperty] public partial string TotalReports { get; set; } = "—";
    [ObservableProperty] public partial string OutputNote { get; set; } = "";
    [ObservableProperty] public partial string Verdict { get; set; } = "";
    [ObservableProperty] public partial string VirtualRate { get; set; } = "—";
    [ObservableProperty] public partial string RateLabel { get; set; } = "REPORT RATE";
    [ObservableProperty] public partial string RateHint { get; set; } = "Physical rate, measured";
    [ObservableProperty] public partial string GraphTitle { get; set; } = "Report interval — live";
    [ObservableProperty] public partial string DroppedHint { get; set; } = "Gaps in controller sequence counter";
    [ObservableProperty] public partial string StallsHint { get; set; } = "Interval > 4× median";

    public IReadOnlyList<string> OutputRateLabels { get; } =
        OutputRateLimiter.Choices.Select(r => r == 0 ? "Event-driven (match controller)" : $"{r} Hz cap").ToList();

    public int SelectedOutputRateIndex
    {
        get => Array.IndexOf(OutputRateLimiter.Choices, AppServices.Controller.OutputLimiter.RateHz) is var i and >= 0 ? i : 0;
        set
        {
            if (value < 0) return;
            int hz = OutputRateLimiter.Choices[value];
            AppServices.Controller.OutputLimiter.RateHz = hz;
            AppServices.SaveSettings(AppServices.Settings with { OutputRateHz = hz });
            OnPropertyChanged();
        }
    }

    public TimingStats Last { get; private set; }

    public void Refresh()
    {
        var c = AppServices.Controller;
        var t = c.Telemetry;
        bool connected = c.Status == ConnectionStatus.Connected;

        bool xbox = connected && c.Family == ControllerFamily.Xbox;
        SourceLabel = !connected ? "No controller connected — nothing to measure."
            : c.IsMock ? "SIMULATED — these numbers come from the mock controller and Windows scheduling, not hardware."
            : xbox ? "Xbox controller, read through Windows' XInput. XInput doesn't expose the controller's USB/Bluetooth report stream, " +
                     "so this measures how often the input changed (checked every ~1 ms). While you move a stick it approaches the controller's " +
                     "own rate; at rest it falls towards zero. It is not a hardware report rate."
            : "Measured live from your DualSense over USB. Rolling window of the last 4,096 reports.";
        RateLabel = xbox ? "UPDATE RATE (XINPUT)" : "REPORT RATE";
        RateHint = xbox ? "Input changes seen per second" : "Physical rate, measured";
        GraphTitle = xbox ? "Time between input changes — live" : "Report interval — live";
        DroppedHint = xbox ? "Not measurable through XInput" : "Gaps in controller sequence counter";
        StallsHint = xbox ? "Not meaningful: Xbox pads go quiet at rest" : "Interval > 4× median";

        Cpu = $"{_cpu.Sample():0.0}";

        // Virtual controller output frequency, measured from packets actually written to VIIPER.
        var v = AppServices.Viiper;
        long packets = v?.PacketsSent ?? 0;
        var nowUtc = DateTime.UtcNow;
        double secs = (nowUtc - _lastPacketSample).TotalSeconds;
        VirtualRate = v is { IsConnected: true } && secs > 0 && packets >= _lastPackets ? $"{(packets - _lastPackets) / secs:0}" : "—";
        _lastPackets = packets;
        _lastPacketSample = nowUtc;

        var s = connected && t != null ? t.Snapshot() : TimingStats.Empty;
        Last = s;
        if (!s.HasData)
        {
            MeasuredRate = AvgInterval = P50 = P95 = P99 = MaxInterval = Dropped = Stalls = ProcessingAvg = ProcessingP99 = "—";
            TotalReports = s.TotalReports.ToString("N0");
            OutputNote = "";
            Verdict = "";
            return;
        }

        MeasuredRate = $"{s.MeasuredRateHz:0}";
        AvgInterval = Ms(s.AverageIntervalUs);
        P50 = Ms(s.P50IntervalUs);
        P95 = Ms(s.P95IntervalUs);
        P99 = Ms(s.P99IntervalUs);
        MaxInterval = Ms(s.MaxIntervalUs);
        Dropped = xbox ? "n/a" : s.DroppedReports.ToString("N0");
        Stalls = xbox ? "n/a" : s.Stalls.ToString("N0");
        ProcessingAvg = $"{s.AverageProcessingUs:0.0}";
        ProcessingP99 = $"{s.P99ProcessingUs:0.0}";
        TotalReports = s.TotalReports.ToString("N0");

        int cap = c.OutputLimiter.RateHz;
        if (xbox)
        {
            OutputNote = cap == 0
                ? "Every change is forwarded the moment it's seen (and the current state is re-sent at least 250 times a second). This is the lowest-latency setting."
                : $"Output is limited to {cap} Hz.";
            Verdict = "Move both sticks continuously for a few seconds to see the controller's real update rhythm. Wired is usually steadier than Bluetooth.";
            return;
        }
        OutputNote = cap == 0
            ? $"Every report is forwarded the moment it arrives (~{s.MeasuredRateHz:0} Hz). This is the lowest-latency setting."
            : cap >= s.MeasuredRateHz * 0.95
                ? $"Your controller delivers ~{s.MeasuredRateHz:0} Hz, so a {cap} Hz cap has no effect. Output can never exceed what the controller sends."
                : $"Output is limited to {cap} Hz — below the controller's ~{s.MeasuredRateHz:0} Hz, so some reports are skipped.";

        double jitter = s.P99IntervalUs - s.P50IntervalUs;
        Verdict = s.DroppedReports > 0
            ? $"{s.DroppedReports:N0} reports were skipped by the controller or USB stack (sequence gaps). Try another USB port or a shorter cable."
            : jitter < s.P50IntervalUs * 0.25
                ? "Stable: report timing is consistent."
                : "Some timing jitter. Background load or USB hubs can cause this; try a direct motherboard port.";
    }

    private static string Ms(double us) => $"{us / 1000.0:0.000}";
}
