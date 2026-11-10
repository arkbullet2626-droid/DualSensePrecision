using DualSensePrecision.Core;

namespace DualSensePrecision.InputEngine;

/// <summary>Result of processing one report: what a virtual controller would be fed.</summary>
public struct ProcessedState
{
    public float LeftX, LeftY, RightX, RightY;   // [-1, 1], Y up
    public float L2, R2;                          // [0, 1]
    public DsButtons Buttons;                     // after mapping
    public DsButtons ExternalButtons;             // bound to keyboard / mouse / macro
    public long SourceTimestamp;
}

public sealed record PipelineSettings
{
    public StickSettings LeftStick { get; init; } = new();
    public StickSettings RightStick { get; init; } = new();
    public TriggerSettings LeftTrigger { get; init; } = new();
    public TriggerSettings RightTrigger { get; init; } = new();
    public ButtonMapper Mapper { get; init; } = new();
}

/// <summary>
/// Runs on the HID reader thread. Settings are swapped atomically from the UI thread; the reader always sees
/// a complete settings object. The latest raw + processed states are published for the UI to sample.
/// </summary>
public sealed class InputPipeline
{
    private PipelineSettings _settings = new();
    private readonly object _publishLock = new();
    private ControllerState _latestRaw;
    private ProcessedState _latestProcessed;
    private long _version;

    public PipelineSettings Settings
    {
        get => Volatile.Read(ref _settings);
        set => Volatile.Write(ref _settings, value);
    }

    public ProcessedState Process(in ControllerState s)
    {
        var cfg = Volatile.Read(ref _settings);
        var (lx, ly) = StickProcessor.Process(ControllerState.NormalizeAxis(s.LeftX), -ControllerState.NormalizeAxis(s.LeftY), cfg.LeftStick);
        var (rx, ry) = StickProcessor.Process(ControllerState.NormalizeAxis(s.RightX), -ControllerState.NormalizeAxis(s.RightY), cfg.RightStick);
        var l2 = TriggerProcessor.Process(s.L2, cfg.LeftTrigger);
        var r2 = TriggerProcessor.Process(s.R2, cfg.RightTrigger);

        // Trigger digital bits come from our activation point, not the firmware's fixed threshold.
        var physical = s.Buttons & ~(DsButtons.L2 | DsButtons.R2);
        if (l2.Pressed) physical |= DsButtons.L2;
        if (r2.Pressed) physical |= DsButtons.R2;

        var mapper = cfg.Mapper;
        var p = new ProcessedState
        {
            LeftX = lx, LeftY = ly, RightX = rx, RightY = ry,
            L2 = l2.Value, R2 = r2.Value,
            Buttons = mapper.Map(physical, out var ext),
            ExternalButtons = ext,
            SourceTimestamp = s.ArrivalTimestamp,
        };
        // Triggers are analog on an Xbox pad: honour remaps onto/away from L2/R2.
        if ((mapper.SuppressedTriggers & DsButtons.L2) != 0) p.L2 = 0;
        if ((mapper.SuppressedTriggers & DsButtons.R2) != 0) p.R2 = 0;
        if ((mapper.ForcedTriggers & DsButtons.L2) != 0) p.L2 = 1;
        if ((mapper.ForcedTriggers & DsButtons.R2) != 0) p.R2 = 1;

        // Publishing is a short struct copy; the UI copies out under the same lock at ~60 Hz.
        lock (_publishLock)
        {
            _latestRaw = s;
            _latestProcessed = p;
            _version++;
        }
        return p;
    }

    /// <summary>Returns the latest states and a version number that increments per processed report.</summary>
    public long Sample(out ControllerState raw, out ProcessedState processed)
    {
        lock (_publishLock)
        {
            raw = _latestRaw;
            processed = _latestProcessed;
            return _version;
        }
    }
}
