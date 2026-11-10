using DualSensePrecision.InputEngine;

namespace DualSensePrecision.VirtualController;

public enum BackendAvailability
{
    /// <summary>Driver present and usable.</summary>
    Ready,
    /// <summary>Driver not installed. Installing requires explicit user consent (never automatic).</summary>
    DriverMissing,
    /// <summary>Not implemented yet in this build.</summary>
    NotImplemented,
}

/// <summary>
/// Physical DualSense → InputPipeline → backend → virtual pad visible to games.
/// See docs/ARCHITECTURE.md §6 for the driver evaluation (ViGEmBus archived; VIIPER/usbip-win2 maintained).
/// </summary>
public interface IVirtualControllerBackend : IDisposable
{
    string Name { get; }
    string Description { get; }
    BackendAvailability CheckAvailability();
    bool IsConnected { get; }
    void Connect();
    void Disconnect();

    /// <summary>Called on the reader thread for every processed report. Must not block.</summary>
    void Submit(in ProcessedState state);

    /// <summary>Rumble requested by the game for the virtual pad (0–255 each), to forward to the DualSense.</summary>
    event Action<byte, byte>? RumbleRequested;
}

/// <summary>Default: no virtual output. Processing still runs so the tester and graphs are real.</summary>
public sealed class NullBackend : IVirtualControllerBackend
{
    public string Name => "None";
    public string Description => "No virtual controller. Settings are previewed in the app only.";
    public BackendAvailability CheckAvailability() => BackendAvailability.Ready;
    public bool IsConnected => false;
    public void Connect() { }
    public void Disconnect() { }
    public void Submit(in ProcessedState state) { }
    public event Action<byte, byte>? RumbleRequested { add { } remove { } }
    public void Dispose() { }
}

/// <summary>
/// Optional output-rate cap. 0 = event-driven (forward every report immediately — lowest latency).
/// A cap never raises the rate above what the controller delivers; it can only skip intermediate reports.
/// </summary>
public sealed class OutputRateLimiter
{
    private long _lastTicks;
    private long _minIntervalTicks;
    private int _rateHz;

    public int RateHz
    {
        get => _rateHz;
        set
        {
            _rateHz = Math.Max(0, value);
            _minIntervalTicks = _rateHz == 0 ? 0 : System.Diagnostics.Stopwatch.Frequency / _rateHz;
        }
    }

    /// <summary>True if this report should be forwarded.</summary>
    public bool ShouldForward(long timestamp)
    {
        if (_minIntervalTicks == 0) return true;
        if (timestamp - _lastTicks < _minIntervalTicks) return false;
        _lastTicks = timestamp;
        return true;
    }

    public static readonly int[] Choices = { 0, 125, 250, 500, 1000, 2000, 4000, 8000 };
}
