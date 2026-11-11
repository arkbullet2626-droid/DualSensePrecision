using System.Text;
using DualSensePrecision.Core;

namespace DualSensePrecision.InputEngine;

public enum Verdict { NotTested, Good, Warning, Problem }

/// <summary>
/// Stick at rest: how far from centre it sits and how much it wanders. All values are in normalised units
/// (1.0 = full deflection). Fed with raw (unprocessed) positions so the user's own deadzone doesn't hide drift.
/// </summary>
public sealed class DriftAnalyzer
{
    private readonly object _lock = new();
    private double _sumX, _sumY, _sumSq, _maxDev;
    private int _n;

    public void Add(float x, float y)
    {
        lock (_lock)
        {
            _sumX += x; _sumY += y;
            double m = Math.Sqrt(x * x + y * y);
            _sumSq += m * m;
            if (m > _maxDev) _maxDev = m;
            _n++;
        }
    }

    public int Count { get { lock (_lock) return _n; } }

    public DriftResult Result()
    {
        lock (_lock)
        {
            if (_n == 0) return DriftResult.Empty;
            double mx = _sumX / _n, my = _sumY / _n;
            double rms = Math.Sqrt(_sumSq / _n);
            var v = _maxDev < 0.05 ? Verdict.Good : _maxDev < 0.10 ? Verdict.Warning : Verdict.Problem;
            // Suggest just enough deadzone to hide the worst wander, with a small margin. Never below 3 %.
            double suggest = Math.Clamp(Math.Ceiling((_maxDev + 0.02) * 100) / 100, 0.03, 0.30);
            return new DriftResult(_n, mx, my, Math.Sqrt(mx * mx + my * my), _maxDev, rms, suggest, v);
        }
    }

    public void Reset() { lock (_lock) { _sumX = _sumY = _sumSq = _maxDev = 0; _n = 0; } }
}

public readonly record struct DriftResult(int Samples, double MeanX, double MeanY, double CentreOffset, double MaxDeviation, double Rms, double SuggestedDeadzone, Verdict Verdict)
{
    public static readonly DriftResult Empty = new(0, 0, 0, 0, 0, 0, 0.05, Verdict.NotTested);
}

/// <summary>
/// Stick range: the furthest the stick reached in each of 36 directions (10° each).
/// A healthy stick reaches close to 1.0 all the way round.
/// </summary>
public sealed class RangeAnalyzer
{
    public const int Bins = 36;
    private readonly double[] _max = new double[Bins];
    private readonly object _lock = new();

    public void Add(float x, float y)
    {
        double m = Math.Sqrt(x * x + y * y);
        if (m < 0.3) return; // only the outer part of the motion tells us about range
        double angle = Math.Atan2(y, x);
        int bin = (int)Math.Floor((angle + Math.PI) / (2 * Math.PI) * Bins) % Bins;
        lock (_lock) if (m > _max[bin]) _max[bin] = m;
    }

    /// <summary>Copy of the per-direction maxima (index 0 = pointing left, counter-clockwise).</summary>
    public double[] Maxima() { lock (_lock) return (double[])_max.Clone(); }

    public static double BinAngle(int bin) => -Math.PI + (bin + 0.5) * 2 * Math.PI / Bins;

    public RangeResult Result()
    {
        var m = Maxima();
        var reached = m.Where(v => v >= 0.7).ToArray();
        double coverage = reached.Length / (double)Bins;
        if (reached.Length == 0) return RangeResult.Empty;
        double avg = reached.Average();
        double min = reached.Min();
        var v = coverage < 0.9 ? Verdict.NotTested
              : min >= 0.93 ? Verdict.Good
              : min >= 0.85 ? Verdict.Warning
              : Verdict.Problem;
        return new RangeResult(coverage, avg, min, v);
    }

    public void Reset() { lock (_lock) Array.Clear(_max); }
}

public readonly record struct RangeResult(double Coverage, double AverageEdge, double WeakestEdge, Verdict Verdict)
{
    public static readonly RangeResult Empty = new(0, 0, 0, Verdict.NotTested);
}

/// <summary>Trigger travel: resting value and the full pull range (0–255).</summary>
public sealed class TriggerAnalyzer
{
    private readonly object _lock = new();
    private int _min = 255, _max, _n;

    public void Add(byte v)
    {
        lock (_lock)
        {
            if (v < _min) _min = v;
            if (v > _max) _max = v;
            _n++;
        }
    }

    public TriggerResult Result()
    {
        lock (_lock)
        {
            if (_n == 0) return TriggerResult.Empty;
            var v = _max < 200 ? Verdict.NotTested
                  : _max >= 250 && _min <= 3 ? Verdict.Good
                  : Verdict.Warning;
            return new TriggerResult(_min, _max, v);
        }
    }

    public void Reset() { lock (_lock) { _min = 255; _max = 0; _n = 0; } }
}

public readonly record struct TriggerResult(int Min, int Max, Verdict Verdict)
{
    public static readonly TriggerResult Empty = new(0, 0, Verdict.NotTested);
}

/// <summary>Which buttons have been pressed at least once.</summary>
public sealed class ButtonCoverage
{
    public static readonly DsButtons[] Standard =
    {
        DsButtons.Cross, DsButtons.Circle, DsButtons.Square, DsButtons.Triangle,
        DsButtons.L1, DsButtons.R1, DsButtons.L2, DsButtons.R2, DsButtons.L3, DsButtons.R3,
        DsButtons.DpadUp, DsButtons.DpadDown, DsButtons.DpadLeft, DsButtons.DpadRight,
        DsButtons.Create, DsButtons.Options, DsButtons.Touchpad, DsButtons.PS, DsButtons.Mute,
    };

    public static readonly DsButtons[] EdgeExtras = { DsButtons.FnLeft, DsButtons.FnRight, DsButtons.BackLeft, DsButtons.BackRight };

    private int _seen;
    public DsButtons Seen => (DsButtons)Volatile.Read(ref _seen);

    public void Add(DsButtons pressed)
    {
        int cur, next;
        do { cur = _seen; next = cur | (int)pressed; } while (cur != next && Interlocked.CompareExchange(ref _seen, next, cur) != cur);
    }

    public IReadOnlyList<DsButtons> Missing(bool edge) =>
        Missing(Standard.Concat(edge ? EdgeExtras : Array.Empty<DsButtons>()));

    /// <summary>Which of <paramref name="expected"/> haven't been pressed yet (e.g. the Xbox button set).</summary>
    public IReadOnlyList<DsButtons> Missing(IEnumerable<DsButtons> expected) => expected.Where(b => (Seen & b) == 0).ToList();

    public void Reset() => Volatile.Write(ref _seen, 0);
}

/// <summary>Motion sensors at rest: gyro bias/noise (deg/s) and the size of gravity (g).</summary>
public sealed class MotionAnalyzer
{
    private readonly object _lock = new();
    private double _gx, _gy, _gz, _gSq, _accMag;
    private int _n;

    public void Add(double gyroX, double gyroY, double gyroZ, double accX, double accY, double accZ)
    {
        lock (_lock)
        {
            _gx += gyroX; _gy += gyroY; _gz += gyroZ;
            _gSq += gyroX * gyroX + gyroY * gyroY + gyroZ * gyroZ;
            _accMag += Math.Sqrt(accX * accX + accY * accY + accZ * accZ);
            _n++;
        }
    }

    public MotionResult Result()
    {
        lock (_lock)
        {
            if (_n == 0) return MotionResult.Empty;
            double bx = _gx / _n, by = _gy / _n, bz = _gz / _n;
            double bias = Math.Sqrt(bx * bx + by * by + bz * bz);
            double noise = Math.Sqrt(Math.Max(0, _gSq / _n - bias * bias));
            double g = _accMag / _n;
            bool gyroOk = bias < 2.0 && noise < 3.0;
            bool accOk = g is > 0.9 and < 1.1;
            var v = gyroOk && accOk ? Verdict.Good : (bias < 5 && g is > 0.8 and < 1.2) ? Verdict.Warning : Verdict.Problem;
            return new MotionResult(bias, noise, g, v);
        }
    }

    public void Reset() { lock (_lock) { _gx = _gy = _gz = _gSq = _accMag = 0; _n = 0; } }
}

public readonly record struct MotionResult(double GyroBias, double GyroNoise, double Gravity, Verdict Verdict)
{
    public static readonly MotionResult Empty = new(0, 0, 0, Verdict.NotTested);
}

/// <summary>Builds the plain-text diagnostic summary.</summary>
public static class DiagnosticReport
{
    public static string Mark(Verdict v) => v switch
    {
        Verdict.Good => "PASS",
        Verdict.Warning => "CHECK",
        Verdict.Problem => "FAIL",
        _ => "NOT TESTED",
    };

    public static string Build(
        string controller, string firmware, double reportRateHz,
        IReadOnlyList<DsButtons> missingButtons, Func<DsButtons, string> buttonName,
        DriftResult leftDrift, DriftResult rightDrift,
        RangeResult leftRange, RangeResult rightRange,
        TriggerResult l2, TriggerResult r2,
        bool touchSeen, MotionResult motion, DateTime when, bool xbox = false)
    {
        var sb = new StringBuilder();
        sb.AppendLine("DualSense Precision — controller diagnostic");
        sb.AppendLine($"{when:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"Controller: {controller}");
        sb.AppendLine($"Firmware:   {firmware}");
        sb.AppendLine(xbox
            ? $"Update rate (XInput, changes seen — not a hardware report rate): {(reportRateHz > 0 ? $"{reportRateHz:0} Hz" : "n/a")}"
            : $"Report rate (measured): {(reportRateHz > 0 ? $"{reportRateHz:0} Hz" : "n/a")}");
        sb.AppendLine();

        var buttonsVerdict = missingButtons.Count == 0 ? Verdict.Good : Verdict.Warning;
        sb.AppendLine($"[{Mark(buttonsVerdict)}] Buttons — " + (missingButtons.Count == 0
            ? "every button registered"
            : "not pressed during the test: " + string.Join(", ", missingButtons.Select(buttonName))));

        void AddDrift(string name, DriftResult d)
        {
            if (d.Verdict == Verdict.NotTested) { sb.AppendLine($"[{Mark(d.Verdict)}] {name} drift"); return; }
            sb.AppendLine($"[{Mark(d.Verdict)}] {name} drift — at rest it wandered up to {d.MaxDeviation * 100:0.0}% (centre offset {d.CentreOffset * 100:0.0}%). " +
                          $"Suggested inner deadzone: {d.SuggestedDeadzone * 100:0}%.");
        }
        AddDrift("Left stick", leftDrift);
        AddDrift("Right stick", rightDrift);

        void AddRange(string name, RangeResult r)
        {
            if (r.Verdict == Verdict.NotTested) { sb.AppendLine($"[{Mark(r.Verdict)}] {name} range — not enough full circles ({r.Coverage * 100:0}% of directions reached)"); return; }
            sb.AppendLine($"[{Mark(r.Verdict)}] {name} range — reaches {r.AverageEdge * 100:0}% on average, weakest direction {r.WeakestEdge * 100:0}%.");
        }
        AddRange("Left stick", leftRange);
        AddRange("Right stick", rightRange);

        void AddTrig(string name, TriggerResult t)
        {
            if (t.Verdict == Verdict.NotTested) { sb.AppendLine($"[{Mark(t.Verdict)}] {name} — not fully pulled"); return; }
            sb.AppendLine($"[{Mark(t.Verdict)}] {name} — rests at {t.Min}, full pull reaches {t.Max} of 255.");
        }
        AddTrig(xbox ? "LT" : "L2", l2);
        AddTrig(xbox ? "RT" : "R2", r2);

        if (xbox)
        {
            sb.AppendLine("Touchpad and motion sensors — not on Xbox controllers.");
            return sb.ToString();
        }
        sb.AppendLine($"[{Mark(touchSeen ? Verdict.Good : Verdict.NotTested)}] Touchpad — {(touchSeen ? "touch registered" : "not touched")}");
        if (motion.Verdict == Verdict.NotTested) sb.AppendLine($"[{Mark(motion.Verdict)}] Motion sensors");
        else sb.AppendLine($"[{Mark(motion.Verdict)}] Motion sensors — gyro at rest {motion.GyroBias:0.00}°/s (noise {motion.GyroNoise:0.00}), gravity {motion.Gravity:0.00} g.");
        return sb.ToString();
    }
}
