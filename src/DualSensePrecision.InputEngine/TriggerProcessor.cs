namespace DualSensePrecision.InputEngine;

public enum TriggerMode
{
    /// <summary>Analog output shaped by deadzone, max and curve.</summary>
    Analog,
    /// <summary>Output jumps to 100 % at the activation point (hair trigger).</summary>
    Digital,
    /// <summary>Two-stage: analog up to the first threshold, a separate "full press" action past the second.</summary>
    DualZone,
}

public sealed record TriggerSettings
{
    public TriggerMode Mode { get; init; } = TriggerMode.Analog;
    public double Deadzone { get; init; } = 0.02;          // pull below this = 0
    public double MaxPoint { get; init; } = 1.0;           // pull at which output reaches 100 %
    public double ActivationPoint { get; init; } = 0.5;    // digital "pressed" threshold
    public double SecondStagePoint { get; init; } = 0.95;  // DualZone full-press threshold
    public double Sensitivity { get; init; } = 1.0;
    public ResponseCurve Curve { get; init; } = ResponseCurve.Linear;
}

public readonly record struct TriggerOutput(float Value, bool Pressed, bool FullPress);

public static class TriggerProcessor
{
    public static TriggerOutput Process(byte raw, TriggerSettings s)
    {
        double v = raw / 255.0;
        double dz = Math.Clamp(s.Deadzone, 0, 0.9);
        double max = Math.Clamp(s.MaxPoint, dz + 0.01, 1.0);

        bool pressed = v >= Math.Clamp(s.ActivationPoint, 0.005, 1.0);
        bool full = s.Mode == TriggerMode.DualZone && v >= Math.Clamp(s.SecondStagePoint, 0.01, 1.0);

        if (s.Mode == TriggerMode.Digital)
            return new TriggerOutput(pressed ? 1f : 0f, pressed, false);

        if (v <= dz) return new TriggerOutput(0, pressed, full);
        double t = Math.Clamp((v - dz) / (max - dz), 0, 1);
        double o = Math.Clamp(s.Curve.Evaluate((float)t) * s.Sensitivity, 0, 1);
        return new TriggerOutput((float)o, pressed, full);
    }
}
