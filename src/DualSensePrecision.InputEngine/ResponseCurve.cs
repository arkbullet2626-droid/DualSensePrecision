namespace DualSensePrecision.InputEngine;

public readonly record struct CurvePoint(double X, double Y);

/// <summary>
/// User-editable response curve through control points, interpolated with a monotone cubic (Fritsch–Carlson)
/// so dragging a point can never make the output go backwards. Evaluated through a 1025-entry lookup table:
/// one multiply, one index, one lerp on the hot path.
/// </summary>
public sealed class ResponseCurve
{
    private const int LutSize = 1024;
    private readonly float[] _lut = new float[LutSize + 1];
    public IReadOnlyList<CurvePoint> Points { get; }
    public string PresetName { get; }

    public ResponseCurve(IEnumerable<CurvePoint> points, string presetName = CurvePresets.Custom)
    {
        Points = Normalize(points);
        PresetName = presetName;
        BuildLut();
    }

    public static ResponseCurve Linear { get; } = new(new[] { new CurvePoint(0, 0), new CurvePoint(1, 1) }, CurvePresets.Linear);

    /// <summary>Maps 0..1 → 0..1.</summary>
    public float Evaluate(float x)
    {
        if (x <= 0) return _lut[0];
        if (x >= 1) return _lut[LutSize];
        float f = x * LutSize;
        int i = (int)f;
        float t = f - i;
        return _lut[i] + (_lut[i + 1] - _lut[i]) * t;
    }

    /// <summary>Exact spline evaluation (used for building the LUT and drawing the editor graph).</summary>
    public double EvaluateExact(double x)
    {
        var p = Points;
        if (x <= p[0].X) return p[0].Y;
        if (x >= p[^1].X) return p[^1].Y;
        int k = 0;
        while (k < p.Count - 2 && x > p[k + 1].X) k++;
        double h = p[k + 1].X - p[k].X;
        double t = (x - p[k].X) / h;
        double t2 = t * t, t3 = t2 * t;
        double h00 = 2 * t3 - 3 * t2 + 1, h10 = t3 - 2 * t2 + t, h01 = -2 * t3 + 3 * t2, h11 = t3 - t2;
        return h00 * p[k].Y + h10 * h * _tangents[k] + h01 * p[k + 1].Y + h11 * h * _tangents[k + 1];
    }

    private double[] _tangents = Array.Empty<double>();

    private void BuildLut()
    {
        _tangents = MonotoneTangents(Points);
        for (int i = 0; i <= LutSize; i++)
            _lut[i] = (float)Math.Clamp(EvaluateExact(i / (double)LutSize), 0, 1);
    }

    /// <summary>Clamps points into the unit square, sorts by X, removes duplicates, and pins X=0 and X=1 endpoints.</summary>
    private static List<CurvePoint> Normalize(IEnumerable<CurvePoint> input)
    {
        var pts = input
            .Select(p => new CurvePoint(Math.Clamp(p.X, 0, 1), Math.Clamp(p.Y, 0, 1)))
            .OrderBy(p => p.X)
            .ToList();

        var dedup = new List<CurvePoint>();
        foreach (var p in pts)
            if (dedup.Count == 0 || p.X - dedup[^1].X > 1e-6) dedup.Add(p);

        if (dedup.Count == 0 || dedup[0].X > 0) dedup.Insert(0, new CurvePoint(0, 0));
        if (dedup[^1].X < 1) dedup.Add(new CurvePoint(1, 1));

        // Enforce non-decreasing Y so the curve is monotone.
        for (int i = 1; i < dedup.Count; i++)
            if (dedup[i].Y < dedup[i - 1].Y) dedup[i] = dedup[i] with { Y = dedup[i - 1].Y };
        return dedup;
    }

    private static double[] MonotoneTangents(IReadOnlyList<CurvePoint> p)
    {
        int n = p.Count;
        var d = new double[n - 1];
        for (int i = 0; i < n - 1; i++) d[i] = (p[i + 1].Y - p[i].Y) / (p[i + 1].X - p[i].X);
        var m = new double[n];
        m[0] = d[0];
        m[n - 1] = d[n - 2];
        for (int i = 1; i < n - 1; i++)
            m[i] = d[i - 1] * d[i] <= 0 ? 0 : (d[i - 1] + d[i]) / 2;
        for (int i = 0; i < n - 1; i++)
        {
            if (d[i] == 0) { m[i] = 0; m[i + 1] = 0; continue; }
            double a = m[i] / d[i], b = m[i + 1] / d[i];
            double s = a * a + b * b;
            if (s > 9)
            {
                double tau = 3 / Math.Sqrt(s);
                m[i] = tau * a * d[i];
                m[i + 1] = tau * b * d[i];
            }
        }
        return m;
    }
}

public static class CurvePresets
{
    public const string Linear = "Linear", Precision = "Precision", Smooth = "Smooth", Aggressive = "Aggressive",
        Exponential = "Exponential", CompetitiveFps = "Competitive FPS", Custom = "Custom";

    public static readonly string[] All = { Linear, Precision, Smooth, Aggressive, Exponential, CompetitiveFps, Custom };

    public static ResponseCurve Create(string name) => name switch
    {
        Precision => Make(name, (0, 0), (0.5, 0.3), (1, 1)),
        Smooth => Make(name, (0, 0), (0.25, 0.15), (0.5, 0.4), (0.75, 0.7), (1, 1)),
        Aggressive => Make(name, (0, 0), (0.25, 0.4), (0.5, 0.68), (0.75, 0.88), (1, 1)),
        Exponential => new ResponseCurve(Enumerable.Range(0, 6).Select(i => i / 5.0).Select(x => new CurvePoint(x, Math.Pow(x, 2.2))), name),
        CompetitiveFps => Make(name, (0, 0), (0.3, 0.22), (0.6, 0.55), (0.85, 0.85), (1, 1)),
        _ => ResponseCurve.Linear,
    };

    private static ResponseCurve Make(string name, params (double x, double y)[] pts) =>
        new(pts.Select(p => new CurvePoint(p.x, p.y)), name);
}
