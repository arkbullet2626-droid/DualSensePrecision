namespace DualSensePrecision.InputEngine;

public enum DeadzoneShape { Radial, Axial }
public enum OutputShape { Circular, Square }

/// <summary>Per-stick settings. Immutable; the UI swaps whole instances so the reader thread never sees a half-edit.</summary>
public sealed record StickSettings
{
    public double InnerDeadzone { get; init; } = 0.05;   // fraction of full deflection
    public double OuterDeadzone { get; init; } = 0.98;   // deflection at which output reaches 100 %
    public double AntiDeadzone { get; init; } = 0.0;     // output jumps to this the moment the stick leaves the deadzone
    public double SensitivityX { get; init; } = 1.0;
    public double SensitivityY { get; init; } = 1.0;
    public DeadzoneShape DeadzoneShape { get; init; } = DeadzoneShape.Radial;
    public OutputShape OutputShape { get; init; } = OutputShape.Circular;
    public bool InvertY { get; init; }
    public StickCalibration Calibration { get; init; } = StickCalibration.Identity;
    public ResponseCurve Curve { get; init; } = ResponseCurve.Linear;
}

/// <summary>Centre offset and per-direction range, measured by the calibration wizard. Values in normalised units.</summary>
public sealed record StickCalibration(double CenterX, double CenterY, double MaxPosX, double MaxNegX, double MaxPosY, double MaxNegY)
{
    public static StickCalibration Identity { get; } = new(0, 0, 1, 1, 1, 1);

    public (double x, double y) Apply(double x, double y)
    {
        x -= CenterX; y -= CenterY;
        x = x >= 0 ? x / Math.Max(MaxPosX - CenterX, 1e-3) : x / Math.Max(MaxNegX + CenterX, 1e-3);
        y = y >= 0 ? y / Math.Max(MaxPosY - CenterY, 1e-3) : y / Math.Max(MaxNegY + CenterY, 1e-3);
        return (Math.Clamp(x, -1, 1), Math.Clamp(y, -1, 1));
    }
}

public static class StickProcessor
{
    /// <summary>
    /// raw → calibration → deadzone (inner/outer) → response curve → anti-deadzone → sensitivity → output shape.
    /// Input and output are normalised [-1, 1] with Y up positive.
    /// </summary>
    public static (float x, float y) Process(float rawX, float rawY, StickSettings s)
    {
        var (x, y) = s.Calibration.Apply(rawX, rawY);
        if (s.InvertY) y = -y;

        double inner = Math.Clamp(s.InnerDeadzone, 0, 0.95);
        double outer = Math.Clamp(s.OuterDeadzone, inner + 0.01, 1.0);

        double ox, oy;
        if (s.DeadzoneShape == DeadzoneShape.Radial)
        {
            double mag = Math.Sqrt(x * x + y * y);
            if (mag <= inner) return (0, 0);
            double scaled = Shape(mag, inner, outer, s);
            ox = x / mag * scaled;
            oy = y / mag * scaled;
        }
        else
        {
            ox = AxisShape(x, inner, outer, s);
            oy = AxisShape(y, inner, outer, s);
            if (ox == 0 && oy == 0) return (0, 0);
        }

        ox *= s.SensitivityX;
        oy *= s.SensitivityY;

        if (s.OutputShape == OutputShape.Square)
        {
            // Stretch the circle so full deflection on a diagonal reaches the corner of the square.
            double m = Math.Sqrt(ox * ox + oy * oy);
            double edge = Math.Max(Math.Abs(ox), Math.Abs(oy));
            if (edge > 1e-9)
            {
                double k = Math.Min(m, 1) / edge;
                ox *= k; oy *= k;
            }
            ox = Math.Clamp(ox, -1, 1);
            oy = Math.Clamp(oy, -1, 1);
        }
        else
        {
            double m = Math.Sqrt(ox * ox + oy * oy);
            if (m > 1) { ox /= m; oy /= m; }
        }
        return ((float)ox, (float)oy);
    }

    private static double AxisShape(double v, double inner, double outer, StickSettings s)
    {
        double a = Math.Abs(v);
        if (a <= inner) return 0;
        return Math.Sign(v) * Shape(a, inner, outer, s);
    }

    /// <summary>Magnitude past the deadzone → 0..1 through the curve, then anti-deadzone lift.</summary>
    private static double Shape(double mag, double inner, double outer, StickSettings s)
    {
        double t = Math.Clamp((mag - inner) / (outer - inner), 0, 1);
        double c = s.Curve.Evaluate((float)t);
        double anti = Math.Clamp(s.AntiDeadzone, 0, 0.9);
        return anti + (1 - anti) * c;
    }
}
