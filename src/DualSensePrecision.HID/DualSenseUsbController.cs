using DualSensePrecision.Core;
using DualSensePrecision.HID.Protocol;
using DualSensePrecision.Telemetry;

namespace DualSensePrecision.HID;

/// <summary>
/// The real, wired DualSense. Owns one dedicated reader thread that:
///   waits for a controller → opens it → reads reports as fast as the controller sends them → reconnects on loss.
/// No timer polls input. Discovery is event-driven, with a slow rescan only while disconnected as a safety net.
/// </summary>
public sealed class DualSenseUsbController : IControllerSource
{
    private readonly AutoResetEvent _wake = new(false);
    private readonly ManualResetEventSlim _stopped = new(true);
    private DeviceArrivalWatcher? _watcher;
    private Thread? _thread;
    private volatile bool _stop;
    private volatile HidDevice? _device;
    private ConnectionStatus _status = ConnectionStatus.Disconnected;

    public ReportTelemetry Telemetry { get; } = new();
    public ImuCalibration Calibration { get; private set; } = ImuCalibration.Default();
    public FirmwareInfo? Firmware { get; private set; }

    public ConnectionStatus Status => _status;
    public ControllerInfo? Info { get; private set; }
    public string? LastError { get; private set; }

    public event ReportHandler? ReportReceived;
    public event Action<ConnectionStatus>? StatusChanged;

    /// <summary>How long to wait between rescans while no controller is present (safety net for missed notifications).</summary>
    public TimeSpan IdleRescanInterval { get; set; } = TimeSpan.FromSeconds(3);

    public void Start()
    {
        if (_thread != null) return;
        _stop = false;
        _stopped.Reset();
        _watcher = new DeviceArrivalWatcher();
        _watcher.Changed += () => _wake.Set();
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "DualSense USB reader",
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.Start();
    }

    public void Stop()
    {
        if (_thread == null) return;
        _stop = true;
        _device?.SignalStop();
        _wake.Set();
        _stopped.Wait(TimeSpan.FromSeconds(2));
        _thread = null;
        _watcher?.Dispose();
        _watcher = null;
    }

    public bool SendOutputReport(ReadOnlySpan<byte> report)
    {
        var d = _device;
        return d != null && _status == ConnectionStatus.Connected && d.Write(report);
    }

    private void Run()
    {
        try
        {
            while (!_stop)
            {
                var candidate = SafeFind();
                if (candidate == null)
                {
                    SetStatus(ConnectionStatus.Disconnected);
                    _wake.WaitOne(IdleRescanInterval);
                    continue;
                }

                SetStatus(ConnectionStatus.Connecting);
                HidDevice dev;
                try
                {
                    dev = HidDevice.Open(candidate.Path);
                }
                catch (HidException ex)
                {
                    Fail(ex.Message);
                    _wake.WaitOne(IdleRescanInterval);
                    continue;
                }

                try
                {
                    _device = dev;
                    Describe(dev, candidate);
                    Telemetry.Reset();
                    LastError = null;
                    SetStatus(ConnectionStatus.Connected);
                    ReadLoop(dev, DualSenseProtocol.ModelFromProductId(candidate.ProductId));
                }
                catch (HidException ex)
                {
                    Fail(ex.Message);
                    if (!_stop) _wake.WaitOne(500); // brief back-off so a flapping device can't spin the thread
                }
                finally
                {
                    _device = null;
                    dev.Dispose();
                    Info = null;
                    if (!_stop) SetStatus(ConnectionStatus.Disconnected);
                }
            }
        }
        finally
        {
            SetStatus(ConnectionStatus.Disconnected);
            _stopped.Set();
        }
    }

    private void ReadLoop(HidDevice dev, DualSenseModel model)
    {
        Span<byte> buf = stackalloc byte[Math.Max(dev.InputReportLength, DualSenseProtocol.UsbInputReportLength)];
        bool first = true;
        byte lastSeq = 0;

        while (!_stop)
        {
            int n = dev.ReadReport(buf);
            long arrival = HighResClock.Now();
            if (n == 0) return; // stop requested

            var raw = buf[..n];
            if (!DualSenseProtocol.TryParseUsbInput(raw, model, arrival, out var state))
                continue; // not report 0x01 (e.g. Bluetooth-style report) — ignored, visible in raw monitor later

            int dropped = first ? 0 : DualSenseProtocol.DroppedBetween(lastSeq, state.Sequence);
            first = false;
            lastSeq = state.Sequence;

            try { ReportReceived?.Invoke(in state, raw); }
            catch (Exception ex) { LastError = $"Input handler error: {ex.Message}"; }

            Telemetry.Record(arrival, HighResClock.Now() - arrival, dropped);
        }
    }

    private void Describe(HidDevice dev, HidDeviceCandidate c)
    {
        var model = DualSenseProtocol.ModelFromProductId(c.ProductId);

        var fwRaw = dev.GetFeature(DualSenseProtocol.FeatureFirmwareInfo, 64);
        Firmware = fwRaw != null ? FirmwareInfo.TryParse(fwRaw) : null;

        var calRaw = dev.GetFeature(DualSenseProtocol.FeatureCalibration, 41);
        Calibration = calRaw != null ? ImuCalibration.Parse(calRaw) : ImuCalibration.Default();

        var pairRaw = dev.GetFeature(DualSenseProtocol.FeaturePairingInfo, 20);
        string? mac = pairRaw != null ? PairingInfo.TryParseMac(pairRaw) : null;

        Info = new ControllerInfo(
            model,
            dev.ProductName ?? (model == DualSenseModel.DualSenseEdge ? "DualSense Edge Wireless Controller" : "DualSense Wireless Controller"),
            c.VendorId, c.ProductId, c.Path,
            mac ?? dev.SerialNumber,
            Firmware?.FirmwareDisplay,
            Firmware?.HardwareDisplay,
            Firmware is { } f ? $"{f.BuildDate} {f.BuildTime}" : null,
            dev.InputReportLength, dev.OutputReportLength,
            IsMock: false);
    }

    private HidDeviceCandidate? SafeFind()
    {
        try { return HidEnumerator.FindDualSenseControllers().FirstOrDefault(); }
        catch (Exception ex) { Fail($"Device scan failed: {ex.Message}"); return null; }
    }

    private void Fail(string message)
    {
        LastError = message;
        SetStatus(ConnectionStatus.Error);
    }

    private void SetStatus(ConnectionStatus s)
    {
        if (_status == s) return;
        _status = s;
        try { StatusChanged?.Invoke(s); } catch { /* UI handlers must not kill the reader */ }
    }

    public void Dispose()
    {
        Stop();
        _wake.Dispose();
        _stopped.Dispose();
    }
}
