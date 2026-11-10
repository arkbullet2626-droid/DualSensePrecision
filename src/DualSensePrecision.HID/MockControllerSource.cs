using DualSensePrecision.Core;
using DualSensePrecision.HID.Protocol;
using DualSensePrecision.Telemetry;

namespace DualSensePrecision.HID;

/// <summary>
/// DEVELOPMENT ONLY. Synthesises well-formed USB input reports so the UI can be built without hardware.
/// Everything it produces is flagged IsMock=true, and the UI labels all of its numbers as simulated.
/// Its timing reflects Windows thread scheduling, not any controller.
/// </summary>
public sealed class MockControllerSource : IControllerSource
{
    private Thread? _thread;
    private volatile bool _stop;
    private ConnectionStatus _status;

    public ReportTelemetry Telemetry { get; } = new();
    public ConnectionStatus Status => _status;
    public ControllerInfo? Info { get; private set; }
    public string? LastError => null;
    public int SimulatedRateHz { get; set; } = 250;

    public event ReportHandler? ReportReceived;
    public event Action<ConnectionStatus>? StatusChanged;

    public void Start()
    {
        if (_thread != null) return;
        _stop = false;
        Info = new ControllerInfo(DualSenseModel.DualSense, "Mock DualSense (simulated)", DualSenseProtocol.SonyVendorId,
            DualSenseProtocol.DualSenseProductId, "mock://dualsense", null, null, null, null, 64, 48, IsMock: true);
        _thread = new Thread(Run) { IsBackground = true, Name = "Mock controller" };
        _thread.Start();
        _status = ConnectionStatus.Connected;
        StatusChanged?.Invoke(_status);
    }

    public void Stop()
    {
        _stop = true;
        _thread?.Join(1000);
        _thread = null;
        _status = ConnectionStatus.Disconnected;
        StatusChanged?.Invoke(_status);
    }

    public bool SendOutputReport(ReadOnlySpan<byte> report) => _status == ConnectionStatus.Connected;

    private void Run()
    {
        var buf = new byte[DualSenseProtocol.UsbInputReportLength];
        byte seq = 0;
        long start = HighResClock.Now();
        long next = start;
        while (!_stop)
        {
            long periodTicks = (long)(System.Diagnostics.Stopwatch.Frequency / (double)Math.Max(1, SimulatedRateHz));
            next += periodTicks;
            long remaining;
            while ((remaining = next - HighResClock.Now()) > 0)
                Thread.Sleep(HighResClock.ToMicroseconds(remaining) > 2000 ? 1 : 0); // sleep coarse, spin only the last ~2 ms

            double t = HighResClock.ToMicroseconds(HighResClock.Now() - start) / 1_000_000.0;
            Synthesize(buf, t, seq++);
            long arrival = HighResClock.Now();
            if (DualSenseProtocol.TryParseUsbInput(buf, DualSenseModel.DualSense, arrival, out var s))
                ReportReceived?.Invoke(in s, buf);
            Telemetry.Record(arrival, HighResClock.Now() - arrival, 0);
        }
    }

    /// <summary>Writes a plausible report: left stick circles, right stick figure-eight, triggers ramp, buttons cycle.</summary>
    public static void Synthesize(byte[] r, double t, byte seq)
    {
        Array.Clear(r);
        r[0] = DualSenseProtocol.UsbInputReportId;
        r[1] = (byte)(128 + 100 * Math.Cos(t));
        r[2] = (byte)(128 + 100 * Math.Sin(t));
        r[3] = (byte)(128 + 110 * Math.Sin(t * 0.7));
        r[4] = (byte)(128 + 60 * Math.Sin(t * 1.4));
        r[5] = (byte)(127.5 + 127.5 * Math.Sin(t * 0.9));
        r[6] = (byte)(127.5 + 127.5 * Math.Cos(t * 0.6));
        r[7] = seq;
        int phase = (int)(t * 2) % 8;
        r[8] = (byte)(0x08 | (0x10 << (phase % 4)));
        r[9] = (byte)(phase >= 4 ? 0x01 << (phase - 4) : 0);
        r[33] = 0x80; r[37] = 0x80; // no touches
        r[53] = 0x07;               // ~75%, discharging
    }

    public void Dispose() => Stop();
}
