using System.Diagnostics;

namespace DualSensePrecision.Telemetry;

/// <summary>Stopwatch is QueryPerformanceCounter on Windows. All timing in the app goes through here.</summary>
public static class HighResClock
{
    public static readonly double TicksPerMicrosecond = Stopwatch.Frequency / 1_000_000.0;
    public static long Now() => Stopwatch.GetTimestamp();
    public static double ToMicroseconds(long ticks) => ticks / TicksPerMicrosecond;
}

/// <summary>Summary of measured timings. All values come from real samples; nothing is estimated.</summary>
public readonly record struct TimingStats(
    int SampleCount,
    double AverageIntervalUs,
    double P50IntervalUs,
    double P95IntervalUs,
    double P99IntervalUs,
    double MaxIntervalUs,
    double MeasuredRateHz,
    double AverageProcessingUs,
    double P99ProcessingUs,
    long TotalReports,
    long DroppedReports,
    long Stalls)
{
    public static readonly TimingStats Empty = default;
    public bool HasData => SampleCount > 1;
}

/// <summary>
/// Lock-free single-writer recorder for report arrival intervals and processing times.
/// The reader thread calls <see cref="Record"/>; any thread may call <see cref="Snapshot"/>.
/// Torn reads of a ring slot are possible during Snapshot but only affect one sample of a 4096-sample window,
/// which is acceptable for display statistics and keeps the hot path wait-free.
/// </summary>
public sealed class ReportTelemetry
{
    public const int WindowSize = 4096;
    private readonly float[] _intervalsUs = new float[WindowSize];
    private readonly float[] _processingUs = new float[WindowSize];
    private long _count;           // number of intervals recorded (monotonic)
    private long _lastArrival;
    private long _totalReports;
    private long _dropped;
    private long _stalls;

    /// <summary>An interval longer than this many times the running average counts as a stall.</summary>
    public double StallFactor { get; set; } = 4.0;
    private double _ewmaUs;   // running average interval, updated per report on the reader thread

    public void Reset()
    {
        Interlocked.Exchange(ref _count, 0);
        Interlocked.Exchange(ref _lastArrival, 0);
        Interlocked.Exchange(ref _totalReports, 0);
        Interlocked.Exchange(ref _dropped, 0);
        Interlocked.Exchange(ref _stalls, 0);
        _ewmaUs = 0;
    }

    /// <param name="arrival">Stopwatch timestamp when the report arrived.</param>
    /// <param name="processingTicks">Stopwatch ticks spent parsing + processing the report.</param>
    /// <param name="droppedSinceLast">Gap in the controller's sequence counter.</param>
    public void Record(long arrival, long processingTicks, int droppedSinceLast)
    {
        Interlocked.Increment(ref _totalReports);
        if (droppedSinceLast > 0) Interlocked.Add(ref _dropped, droppedSinceLast);

        long last = _lastArrival;
        _lastArrival = arrival;
        if (last == 0) return;

        float intervalUs = (float)HighResClock.ToMicroseconds(arrival - last);
        long i = _count;
        int slot = (int)(i & (WindowSize - 1));
        _intervalsUs[slot] = intervalUs;
        _processingUs[slot] = (float)HighResClock.ToMicroseconds(processingTicks);
        Volatile.Write(ref _count, i + 1);

        // Warm up for 64 samples, then flag intervals far above the running average.
        // Stalls are excluded from the average so one long gap doesn't hide the next.
        if (i < 64)
            _ewmaUs = i == 0 ? intervalUs : _ewmaUs + (intervalUs - _ewmaUs) / (i + 1);
        else if (intervalUs > _ewmaUs * StallFactor)
            Interlocked.Increment(ref _stalls);
        else
            _ewmaUs += (intervalUs - _ewmaUs) / 64;
    }

    /// <summary>Copies the window and computes statistics. Allocates two small arrays; call at UI rate, not per report.</summary>
    public TimingStats Snapshot()
    {
        long count = Volatile.Read(ref _count);
        int n = (int)Math.Min(count, WindowSize);
        if (n < 2) return TimingStats.Empty with { TotalReports = Interlocked.Read(ref _totalReports) };

        var iv = new float[n];
        var pr = new float[n];
        Array.Copy(_intervalsUs, iv, n);
        Array.Copy(_processingUs, pr, n);

        double sum = 0, psum = 0;
        for (int k = 0; k < n; k++) { sum += iv[k]; psum += pr[k]; }
        Array.Sort(iv);
        Array.Sort(pr);
        double avg = sum / n;
        double p50 = Percentile(iv, 0.50);

        return new TimingStats(
            n, avg, p50, Percentile(iv, 0.95), Percentile(iv, 0.99), iv[^1],
            avg > 0 ? 1_000_000.0 / avg : 0,
            psum / n, Percentile(pr, 0.99),
            Interlocked.Read(ref _totalReports), Interlocked.Read(ref _dropped), Interlocked.Read(ref _stalls));
    }

    /// <summary>Most recent intervals in arrival order, for the live graph.</summary>
    public int CopyRecentIntervals(Span<float> destination)
    {
        long count = Volatile.Read(ref _count);
        int n = (int)Math.Min(Math.Min(count, WindowSize), destination.Length);
        for (int k = 0; k < n; k++)
        {
            long idx = count - n + k;
            destination[k] = _intervalsUs[(int)(idx & (WindowSize - 1))];
        }
        return n;
    }

    /// <summary>Nearest-rank percentile on a sorted array.</summary>
    public static double Percentile(float[] sorted, double p)
    {
        if (sorted.Length == 0) return 0;
        int rank = (int)Math.Ceiling(p * sorted.Length) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Length - 1)];
    }
}
