using System.Runtime.InteropServices;
using DualSensePrecision.Core;
using DualSensePrecision.HID.Protocol;
using DualSensePrecision.Telemetry;

namespace DualSensePrecision.HID.Xbox;

/// <summary>
/// An Xbox controller (Xbox Series, Xbox One, Xbox 360; wired, Bluetooth or Xbox Wireless Adapter), read through XInput.
///
/// Honest limits, shown in the UI:
///  - XInput has no report stream to time. This polls the controller's slot every ~1 ms and reports each time
///    Windows says the state changed, so the measured number is "changes seen", not the hardware report rate.
///  - Battery is a four-step level (Empty/Low/Medium/Full), not a percentage.
///  - Trigger rumble (impulse triggers), the Share button and anything DualSense-specific aren't available via XInput.
///
/// Two threads: a slot scanner (every 500 ms: what's connected, which slot is the app's own virtual pad, battery)
/// and a poller for the chosen slot. Scanning empty slots can be slow in XInput, so it never runs on the poller.
/// </summary>
public sealed class XboxControllerSource : IControllerSource
{
    private const int PollIntervalUs = 1000;
    private const int ScanIntervalMs = 500;
    private const long HeartbeatUs = 4000; // re-send the unchanged state at ≥250/s so downstream stages never hold a stale value

    private readonly XboxSlotFilter _filter = new();
    private readonly object _gate = new(); // adopt/drop happen on both threads
    private readonly AutoResetEvent _wakePoller = new(false);
    private readonly AutoResetEvent _wakeScanner = new(false);
    private readonly byte[] _raw = new byte[XboxProtocol.RawLength];
    private Thread? _poller, _scanner;
    private volatile bool _stop;
    private volatile int _slot = -1;
    private ConnectionStatus _status = ConnectionStatus.Disconnected;
    private volatile byte _batteryType = XboxProtocol.BatteryTypeUnknown, _batteryLevel;

    public ReportTelemetry Telemetry { get; } = new() { DetectStalls = false };
    public ConnectionStatus Status => _status;
    public ControllerInfo? Info { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>Tells the filter whether the app's virtual Xbox pad is starting / running, so it's never read as input.</summary>
    public Func<VirtualPadPhase> VirtualPhase { get; set; } = () => VirtualPadPhase.Off;

    /// <summary>The XInput slot (0–3) the app identified as its own virtual pad, or -1.</summary>
    public int VirtualSlot => _filter.VirtualSlot;

    /// <summary>The XInput slot being read, or -1.</summary>
    public int ActiveSlot => _slot;

    public event ReportHandler? ReportReceived;
    public event Action<ConnectionStatus>? StatusChanged;

    public void Start()
    {
        if (_poller != null) return;
        _stop = false;
        if (!XInputNative.Available)
        {
            LastError = "XInput isn't available on this PC, so Xbox controllers can't be read.";
            return;
        }
        Scan(); // settle which slots are real before anything can be adopted
        _scanner = new Thread(ScanLoop) { IsBackground = true, Name = "Xbox slot scanner" };
        _poller = new Thread(PollLoop) { IsBackground = true, Name = "Xbox XInput poller", Priority = ThreadPriority.AboveNormal };
        _scanner.Start();
        _poller.Start();
    }

    public void Stop()
    {
        if (_poller == null) { SetStatus(ConnectionStatus.Disconnected); return; }
        _stop = true;
        _wakePoller.Set();
        _wakeScanner.Set();
        _poller.Join(1000);
        _scanner?.Join(1000);
        int slot = _slot;
        if (slot >= 0) XInputNative.SetVibration(slot, 0, 0);
        _poller = _scanner = null;
        _slot = -1;
        Info = null;
        SetStatus(ConnectionStatus.Disconnected);
    }

    /// <summary>Xbox controllers take no DualSense output reports (lights, adaptive triggers). Use <see cref="SetVibration"/>.</summary>
    public bool SendOutputReport(ReadOnlySpan<byte> report) => false;

    /// <summary>Rumble: left = big low-frequency motor, right = small high-frequency motor (0–65535).</summary>
    public bool SetVibration(ushort left, ushort right)
    {
        int slot = _slot;
        return slot >= 0 && _status == ConnectionStatus.Connected && XInputNative.SetVibration(slot, left, right);
    }

    // ---------------- scanner ----------------

    private void ScanLoop()
    {
        while (!_stop)
        {
            _wakeScanner.WaitOne(ScanIntervalMs);
            if (_stop) break;
            try { Scan(); }
            catch (Exception ex) { LastError = $"Xbox controller scan failed: {ex.Message}"; }
        }
    }

    private void Scan()
    {
        var connected = new List<XInputSlotInfo>(4);
        for (int i = 0; i < XInputNative.MaxSlots; i++)
        {
            if (XInputNative.GetState(i, out _) != XInputNative.ErrorSuccess) continue;
            var (vid, pid) = XInputNative.GetIds(i);
            connected.Add(new XInputSlotInfo(i, vid, pid));
        }
        _filter.Update(VirtualPhase(), connected);

        int current = _slot;
        var currentInfo = connected.FirstOrDefault(c => c.Slot == current);
        bool currentOk = current >= 0 && connected.Any(c => c.Slot == current) && !_filter.IsExcluded(currentInfo);

        if (!currentOk)
        {
            if (current >= 0) Drop();
            var pick = connected.FirstOrDefault(c => !_filter.IsExcluded(c));
            if (connected.Any(c => !_filter.IsExcluded(c))) Adopt(pick);
        }
        else if (XInputNative.GetBattery(current) is { } b)
        {
            UpdateBattery(b);
        }
    }

    private void Adopt(XInputSlotInfo c)
    {
        lock (_gate) AdoptLocked(c);
    }

    private void AdoptLocked(XInputSlotInfo c)
    {
        if (XInputNative.GetBattery(c.Slot) is { } b) UpdateBattery(b, describe: false);
        Info = Describe(c);
        Telemetry.Reset();
        LastError = null;
        _slot = c.Slot;
        SetStatus(ConnectionStatus.Connected);
        _wakePoller.Set();
    }

    private void Drop()
    {
        lock (_gate)
        {
            _slot = -1;
            Info = null;
            SetStatus(ConnectionStatus.Disconnected);
        }
    }

    private void UpdateBattery(XInputNative.BatteryInformation b, bool describe = true)
    {
        bool changedKind = (b.BatteryType == XboxProtocol.BatteryTypeWired) != (_batteryType == XboxProtocol.BatteryTypeWired);
        _batteryType = b.BatteryType;
        _batteryLevel = b.BatteryLevel;
        if (describe && changedKind && Info is { } i)
            Info = i with { ConnectionLabel = ConnectionLabel() }; // plugged a cable into a wireless pad, or unplugged it
    }

    private string ConnectionLabel() => _batteryType == XboxProtocol.BatteryTypeWired ? "USB · wired" : "Wireless";

    private ControllerInfo Describe(XInputSlotInfo c) => new(
        DualSenseModel.Unknown,
        XboxProtocol.ModelName(c.VendorId, c.ProductId),
        c.VendorId, c.ProductId,
        $"XInput slot {c.Slot + 1}",
        SerialOrMac: null, FirmwareVersion: null, HardwareVersion: null, FirmwareBuildDate: null,
        InputReportLength: XboxProtocol.RawLength, OutputReportLength: 0,
        IsMock: false)
    {
        Family = ControllerFamily.Xbox,
        ConnectionLabel = ConnectionLabel(),
    };

    // ---------------- poller ----------------

    private void PollLoop()
    {
        using var timer = new PreciseTimer();
        uint lastPacket = 0;
        bool havePacket = false;
        long lastEmit = 0;
        int polledSlot = -1;

        while (!_stop)
        {
            int slot = _slot;
            if (slot < 0)
            {
                havePacket = false;
                _wakePoller.WaitOne(250);
                continue;
            }
            if (slot != polledSlot) { polledSlot = slot; havePacket = false; }

            int rc = XInputNative.GetState(slot, out var st);
            long arrival = HighResClock.Now();
            if (rc != XInputNative.ErrorSuccess)
            {
                lock (_gate) if (_slot == slot) Drop();
                _wakeScanner.Set(); // look for another controller straight away
                continue;
            }

            bool changed = !havePacket || st.PacketNumber != lastPacket;
            bool heartbeat = HighResClock.ToMicroseconds(arrival - lastEmit) >= HeartbeatUs;
            if (changed || heartbeat)
            {
                havePacket = true;
                lastPacket = st.PacketNumber;
                lastEmit = arrival;
                Emit(st, arrival, changed);
            }
            timer.Wait(PollIntervalUs);
        }
    }

    private void Emit(in XInputNative.State st, long arrival, bool changed)
    {
        var g = st.Gamepad;
        var s = XboxProtocol.ToState(g.Buttons, g.LeftTrigger, g.RightTrigger, g.ThumbLX, g.ThumbLY, g.ThumbRX, g.ThumbRY, arrival);
        var (kind, pct) = XboxProtocol.Battery(_batteryType, _batteryLevel);
        s.BatteryKind = kind;
        s.BatteryPercent = pct;
        s.Charging = ChargingState.Unknown;
        s.Sequence = (byte)st.PacketNumber;
        XboxProtocol.WriteRaw(_raw, st.PacketNumber, g.Buttons, g.LeftTrigger, g.RightTrigger, g.ThumbLX, g.ThumbLY, g.ThumbRX, g.ThumbRY);

        try { ReportReceived?.Invoke(in s, _raw); }
        catch (Exception ex) { LastError = $"Input handler error: {ex.Message}"; }

        // Only real changes are timed; heartbeats are re-sends of the same state.
        if (changed) Telemetry.Record(arrival, HighResClock.Now() - arrival, 0);
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
        _wakePoller.Dispose();
        _wakeScanner.Dispose();
    }

    /// <summary>
    /// Sub-millisecond-capable wait: a high-resolution waitable timer (Windows 10 1803+), falling back to Sleep(1).
    /// Avoids changing the system-wide timer resolution.
    /// </summary>
    private sealed class PreciseTimer : IDisposable
    {
        private const uint CreateWaitableTimerHighResolution = 0x00000002;
        private const uint TimerAllAccess = 0x1F0003;
        private readonly IntPtr _handle;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWaitableTimerExW(IntPtr attributes, string? name, uint flags, uint access);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetWaitableTimer(IntPtr timer, ref long dueTime, int period, IntPtr completion, IntPtr arg, bool resume);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        public PreciseTimer()
        {
            try { _handle = CreateWaitableTimerExW(IntPtr.Zero, null, CreateWaitableTimerHighResolution, TimerAllAccess); }
            catch { _handle = IntPtr.Zero; }
        }

        public void Wait(int microseconds)
        {
            if (_handle != IntPtr.Zero)
            {
                long due = -(long)microseconds * 10; // relative, 100 ns units
                if (SetWaitableTimer(_handle, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
                {
                    WaitForSingleObject(_handle, 50);
                    return;
                }
            }
            Thread.Sleep(1);
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero) CloseHandle(_handle);
        }
    }
}
