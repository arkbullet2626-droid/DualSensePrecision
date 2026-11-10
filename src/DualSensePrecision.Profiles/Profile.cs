using DualSensePrecision.Core;
using DualSensePrecision.InputEngine;

namespace DualSensePrecision.Profiles;

/// <summary>
/// Portable, versioned profile document (JSON). Plain data only, so files are human-readable and shareable.
/// Converted to runtime <see cref="PipelineSettings"/> with <see cref="ToPipelineSettings"/>.
/// </summary>
public sealed record Profile
{
    public const int CurrentSchema = 1;
    public int SchemaVersion { get; init; } = CurrentSchema;
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "Default";
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
    public DateTime ModifiedUtc { get; init; } = DateTime.UtcNow;

    public StickProfile LeftStick { get; init; } = new();
    public StickProfile RightStick { get; init; } = new();
    public TriggerProfile LeftTrigger { get; init; } = new();
    public TriggerProfile RightTrigger { get; init; } = new();
    public MappingProfile Mappings { get; init; } = new();
    public List<MacroDefinition> Macros { get; init; } = new();
    public VirtualControllerProfile VirtualController { get; init; } = new();
    public AppearanceProfile Appearance { get; init; } = new();

    public PipelineSettings ToPipelineSettings() => new()
    {
        LeftStick = LeftStick.ToSettings(),
        RightStick = RightStick.ToSettings(),
        LeftTrigger = LeftTrigger.ToSettings(),
        RightTrigger = RightTrigger.ToSettings(),
        Mapper = Mappings.ToMapper(),
    };
}

public sealed record StickProfile
{
    public double InnerDeadzone { get; init; } = 0.05;
    public double OuterDeadzone { get; init; } = 0.98;
    public double AntiDeadzone { get; init; }
    public double SensitivityX { get; init; } = 1.0;
    public double SensitivityY { get; init; } = 1.0;
    public DeadzoneShape DeadzoneShape { get; init; } = DeadzoneShape.Radial;
    public OutputShape OutputShape { get; init; } = OutputShape.Circular;
    public bool InvertY { get; init; }
    public string CurvePreset { get; init; } = CurvePresets.Linear;
    public List<double[]> CurvePoints { get; init; } = new() { new[] { 0.0, 0.0 }, new[] { 1.0, 1.0 } };
    public double[] Calibration { get; init; } = { 0, 0, 1, 1, 1, 1 }; // centerX, centerY, +X, -X, +Y, -Y

    public StickSettings ToSettings() => new()
    {
        InnerDeadzone = InnerDeadzone,
        OuterDeadzone = OuterDeadzone,
        AntiDeadzone = AntiDeadzone,
        SensitivityX = SensitivityX,
        SensitivityY = SensitivityY,
        DeadzoneShape = DeadzoneShape,
        OutputShape = OutputShape,
        InvertY = InvertY,
        Curve = ProfileCurves.Build(CurvePreset, CurvePoints),
        Calibration = Calibration is { Length: 6 } c
            ? new StickCalibration(c[0], c[1], c[2], c[3], c[4], c[5])
            : StickCalibration.Identity,
    };
}

public sealed record TriggerProfile
{
    public TriggerMode Mode { get; init; } = TriggerMode.Analog;
    public double Deadzone { get; init; } = 0.02;
    public double MaxPoint { get; init; } = 1.0;
    public double ActivationPoint { get; init; } = 0.5;
    public double SecondStagePoint { get; init; } = 0.95;
    public double Sensitivity { get; init; } = 1.0;
    public string CurvePreset { get; init; } = CurvePresets.Linear;
    public List<double[]> CurvePoints { get; init; } = new() { new[] { 0.0, 0.0 }, new[] { 1.0, 1.0 } };
    public AdaptiveTriggerProfile Adaptive { get; init; } = new();

    public TriggerSettings ToSettings() => new()
    {
        Mode = Mode,
        Deadzone = Deadzone,
        MaxPoint = MaxPoint,
        ActivationPoint = ActivationPoint,
        SecondStagePoint = SecondStagePoint,
        Sensitivity = Sensitivity,
        Curve = ProfileCurves.Build(CurvePreset, CurvePoints),
    };
}

public enum AdaptiveEffectKind { Off, Feedback, Weapon, Vibration }

public sealed record AdaptiveTriggerProfile
{
    public AdaptiveEffectKind Effect { get; init; } = AdaptiveEffectKind.Off;
    public int Start { get; init; } = 3;
    public int End { get; init; } = 6;
    public int Strength { get; init; } = 5;
    public int Frequency { get; init; } = 20;
}

public sealed record MappingProfile
{
    public Dictionary<DsButtons, ButtonMapping> Base { get; init; } = new();
    public Dictionary<DsButtons, ButtonMapping> Shift { get; init; } = new();
    public DsButtons ShiftButton { get; init; } = DsButtons.None;

    public ButtonMapper ToMapper() => new(Base, Shift, ShiftButton);
}

public enum MacroStepKind { ButtonDown, ButtonUp, KeyDown, KeyUp, MouseDown, MouseUp, Delay }

/// <summary>Once: plays to the end on each press. WhileHeld: repeats while the button is held. Toggle: press to start repeating, press again to stop.</summary>
public enum MacroPlayMode { Once, WhileHeld, Toggle }

public sealed record MacroStep(MacroStepKind Kind, DsButtons Button = DsButtons.None, ushort VirtualKey = 0,
    MouseButtonTarget Mouse = MouseButtonTarget.Left, int DelayMs = 0);

public sealed record MacroDefinition
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "New macro";
    public bool Enabled { get; init; } = true;
    public MacroPlayMode PlayMode { get; init; } = MacroPlayMode.Once;
    public List<MacroStep> Steps { get; init; } = new();
}

public enum VirtualTarget { None, Xbox360, DualShock4 }

public sealed record VirtualControllerProfile
{
    public VirtualTarget Target { get; init; } = VirtualTarget.None;
    public bool HidePhysicalController { get; init; }
}

public sealed record AppearanceProfile
{
    public string LightbarColor { get; init; } = "#3D7BFF";
    public int LightbarBrightness { get; init; } = 100; // %
    public int PlayerLedMask { get; init; } = 0b00100;
    public int PlayerLedBrightness { get; init; }       // 0 high, 1 mid, 2 low
    public int MuteLed { get; init; }                   // 0 off, 1 on, 2 pulse
    public int RumbleIntensity { get; init; } = 100;    // %
    public int TriggerIntensity { get; init; } = 100;   // %
}

internal static class ProfileCurves
{
    public static ResponseCurve Build(string preset, List<double[]> points)
    {
        if (preset != CurvePresets.Custom) return CurvePresets.Create(preset);
        var pts = points.Where(p => p is { Length: 2 }).Select(p => new CurvePoint(p[0], p[1])).ToList();
        return pts.Count >= 2 ? new ResponseCurve(pts) : ResponseCurve.Linear;
    }
}
