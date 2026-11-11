using System.Diagnostics;
using DualSensePrecision.Configuration;
using DualSensePrecision.Core;
using DualSensePrecision.HID;
using DualSensePrecision.HID.Protocol;
using DualSensePrecision.HID.Xbox;
using DualSensePrecision.InputEngine;
using DualSensePrecision.Profiles;
using DualSensePrecision.Telemetry;
using DualSensePrecision.VirtualController;

namespace DualSensePrecision.App.Services;

/// <summary>
/// App-wide owner of the controller connection and the processing pipeline.
/// Wires: controller reader thread → InputPipeline → (virtual output) and publishes snapshots for the UI.
/// </summary>
public sealed class ControllerService : IDisposable
{
    private IControllerSource? _source;
    private readonly object _rawLock = new();
    private readonly byte[] _latestRaw = new byte[DualSensePrecision.HID.Protocol.DualSenseProtocol.UsbInputReportLength];
    private int _latestRawLength;

    public InputPipeline Pipeline { get; } = new();
    public OutputRateLimiter OutputLimiter { get; } = new();
    public KeyMouseOutput KeyOutput { get; } = new();
    public MacroRecorder Recorder { get; } = new();

    /// <summary>Every raw report, on the reader thread (used by the Controller Tester's analyzers). Keep handlers tiny.</summary>
    public event Action<ControllerState>? RawObserved;

    /// <summary>Called on the reader thread with (physical buttons, external-mapped buttons, mapper) — the macro engine hooks in here.</summary>
    public Action<DsButtons, DsButtons, ButtonMapper>? ButtonHook { get; set; }

    private readonly object _submitLock = new();
    private ProcessedState _lastProcessed;
    private int _injected; // DsButtons held by running macros

    /// <summary>Buttons currently held by macros; merged into the virtual controller output.</summary>
    public DsButtons InjectedButtons => (DsButtons)Volatile.Read(ref _injected);

    /// <summary>Sets the macro-held buttons and pushes an output packet straight away (don't wait for the next report).</summary>
    public void SetInjectedButtons(DsButtons buttons)
    {
        Volatile.Write(ref _injected, (int)buttons);
        lock (_submitLock) Backend.Submit(Inject(_lastProcessed, buttons));
    }

    private static ProcessedState Inject(ProcessedState p, DsButtons inj)
    {
        if (inj == DsButtons.None) return p;
        p.Buttons |= inj;
        if ((inj & DsButtons.L2) != 0) p.L2 = 1;
        if ((inj & DsButtons.R2) != 0) p.R2 = 1;
        return p;
    }
    public IVirtualControllerBackend Backend { get; private set; } = new NullBackend();

    /// <summary>Swaps the virtual output target. The caller owns (and disposes) the old backend.</summary>
    public void SetBackend(IVirtualControllerBackend backend) => Backend = backend;

    public IControllerSource? Source => _source;

    /// <summary>The physical source delivering input right now (DualSense, Xbox or mock), or null.</summary>
    public IControllerSource? ActiveSource => _source is AutoControllerSource a ? a.Active : _source;

    /// <summary>The Xbox reader (exists whenever real hardware detection is on), for the virtual-pad filter and rumble.</summary>
    public XboxControllerSource? Xbox => (_source as AutoControllerSource)?.Xbox;

    public bool IsMock => _source is MockControllerSource;
    public ControllerFamily Family => Info?.Family ?? ControllerFamily.DualSense;
    public bool IsXbox => Status == ConnectionStatus.Connected && Family == ControllerFamily.Xbox;

    public ReportTelemetry? Telemetry => ActiveSource switch
    {
        DualSenseUsbController u => u.Telemetry,
        XboxControllerSource x => x.Telemetry,
        MockControllerSource m => m.Telemetry,
        _ => null,
    };
    public ImuCalibration Calibration => (ActiveSource as DualSenseUsbController)?.Calibration ?? ImuCalibration.Default();
    public FirmwareInfo? Firmware => (ActiveSource as DualSenseUsbController)?.Firmware;

    /// <summary>Tells the Xbox reader whether the app's virtual pad is starting/running (so it's never read back as input).</summary>
    public Func<VirtualPadPhase> VirtualPhase { get; set; } = () => VirtualPadPhase.Off;

    /// <summary>Xbox rumble (0–255 per motor, already scaled). Returns false if no Xbox controller is active.</summary>
    public bool SetXboxVibration(byte left, byte right, int strengthPercent)
    {
        if (ActiveSource is not XboxControllerSource x) return false;
        var (l, r) = XboxProtocol.Vibration(left, right, strengthPercent);
        return x.SetVibration(l, r);
    }

    /// <summary>Applies the "which controller wins" setting without restarting anything.</summary>
    public void SetPreferred(ControllerFamily family)
    {
        if (_source is AutoControllerSource a) a.Preferred = family;
    }

    public ConnectionStatus Status => _source?.Status ?? ConnectionStatus.Disconnected;
    public ControllerInfo? Info => _source?.Info;
    public string? LastError => _source?.LastError;

    /// <summary>Raised on a background thread. UI code must marshal to its DispatcherQueue.</summary>
    public event Action<ConnectionStatus>? StatusChanged;

    public void Start(AppSettings settings)
    {
        Stop();
        OutputLimiter.RateHz = settings.OutputRateHz;
        if (settings.UseMockController)
            _source = new MockControllerSource();
        else
        {
            var xbox = new XboxControllerSource { VirtualPhase = () => VirtualPhase() };
            _source = new AutoControllerSource(new DualSenseUsbController(), xbox, settings.PreferredController);
        }
        _source.ReportReceived += OnReport;
        _source.StatusChanged += s =>
        {
            // Unplugged (or switched) controller → centre the virtual pad so a game doesn't see a stuck stick or held button.
            if (s != ConnectionStatus.Connected)
            {
                Backend.Submit(default);
                KeyOutput.ReleaseAll(); // never leave a key held down after an unplug
            }
            StatusChanged?.Invoke(s);
        };
        _source.Start();
    }

    public void Stop()
    {
        if (_source == null) return;
        _source.ReportReceived -= OnReport;
        _source.Dispose();
        KeyOutput.ReleaseAll();
        _source = null;
        StatusChanged?.Invoke(ConnectionStatus.Disconnected);
    }

    private void OnReport(in ControllerState state, ReadOnlySpan<byte> raw)
    {
        var processed = Pipeline.Process(state);
        var mapper = Pipeline.Settings.Mapper;
        if (!IsMock)
        {
            KeyOutput.Update(processed.ExternalButtons, mapper); // simulated input never types real keys
            ButtonHook?.Invoke(state.Buttons, processed.ExternalButtons, mapper);
        }
        Recorder.OnReport(state.Buttons, state.ArrivalTimestamp);
        try { RawObserved?.Invoke(state); } catch { /* a diagnostic observer must never break input */ }

        lock (_submitLock)
        {
            _lastProcessed = processed;
            if (OutputLimiter.ShouldForward(state.ArrivalTimestamp))
                Backend.Submit(Inject(processed, InjectedButtons));
        }

        lock (_rawLock)
        {
            int n = Math.Min(raw.Length, _latestRaw.Length);
            raw[..n].CopyTo(_latestRaw);
            _latestRawLength = n;
        }
    }

    /// <summary>Copies the most recent raw input report (for the raw HID monitor).</summary>
    public int CopyLatestRaw(Span<byte> destination)
    {
        lock (_rawLock)
        {
            int n = Math.Min(_latestRawLength, destination.Length);
            _latestRaw.AsSpan(0, n).CopyTo(destination);
            return n;
        }
    }

    public bool Send(OutputReportBuilder builder) => _source?.SendOutputReport(builder.Build()) ?? false;

    public void Dispose() => Stop();
}

/// <summary>Real process CPU usage, sampled from TotalProcessorTime.</summary>
public sealed class CpuSampler
{
    private readonly Process _process = Process.GetCurrentProcess();
    private TimeSpan _lastCpu;
    private long _lastWall = Stopwatch.GetTimestamp();

    public CpuSampler() { _process.Refresh(); _lastCpu = _process.TotalProcessorTime; }

    /// <summary>Percent of total machine CPU used by this app since the last call.</summary>
    public double Sample()
    {
        _process.Refresh();
        var cpu = _process.TotalProcessorTime;
        long wall = Stopwatch.GetTimestamp();
        double wallMs = (wall - _lastWall) * 1000.0 / Stopwatch.Frequency;
        double cpuMs = (cpu - _lastCpu).TotalMilliseconds;
        _lastCpu = cpu; _lastWall = wall;
        return wallMs <= 0 ? 0 : Math.Clamp(cpuMs / wallMs / Environment.ProcessorCount * 100, 0, 100);
    }
}
