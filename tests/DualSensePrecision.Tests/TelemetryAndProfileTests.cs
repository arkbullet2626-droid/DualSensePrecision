using System.Diagnostics;
using DualSensePrecision.InputEngine;
using DualSensePrecision.Profiles;
using DualSensePrecision.Telemetry;
using Xunit;

namespace DualSensePrecision.Tests;

public class TelemetryTests
{
    private static long Ticks(double us) => (long)(us * Stopwatch.Frequency / 1_000_000.0);

    [Fact]
    public void EmptyHasNoData() => Assert.False(new ReportTelemetry().Snapshot().HasData);

    [Fact]
    public void SteadyOneMillisecondReportsMeasureAs1000Hz()
    {
        var t = new ReportTelemetry();
        long ts = 1_000_000;
        for (int i = 0; i < 2000; i++) { t.Record(ts, Ticks(5), 0); ts += Ticks(1000); }
        var s = t.Snapshot();
        Assert.InRange(s.MeasuredRateHz, 995, 1005);
        Assert.InRange(s.P50IntervalUs, 999, 1001);
        Assert.InRange(s.P99IntervalUs, 999, 1001);
        Assert.InRange(s.AverageProcessingUs, 4.9, 5.1);
        Assert.Equal(2000, s.TotalReports);
    }

    [Fact]
    public void PercentilesSeeOutliers()
    {
        var t = new ReportTelemetry();
        long ts = 1;
        for (int i = 0; i < 1000; i++)
        {
            double gap = i % 100 == 0 ? 8000 : 4000; // 1 % long gaps
            ts += Ticks(gap);
            t.Record(ts, 0, 0);
        }
        var s = t.Snapshot();
        Assert.InRange(s.P50IntervalUs, 3999, 4001);
        Assert.InRange(s.MaxIntervalUs, 7999, 8001);
        Assert.InRange(s.P95IntervalUs, 3999, 4001);
    }

    [Fact]
    public void CountsDroppedReports()
    {
        var t = new ReportTelemetry();
        t.Record(10, 0, 0);
        t.Record(20, 0, 3);
        t.Record(30, 0, 1);
        Assert.Equal(4, t.Snapshot().DroppedReports);
    }

    [Fact]
    public void WindowWrapsWithoutError()
    {
        var t = new ReportTelemetry();
        long ts = 1;
        for (int i = 0; i < ReportTelemetry.WindowSize * 3; i++) { ts += Ticks(250); t.Record(ts, 0, 0); }
        var s = t.Snapshot();
        Assert.Equal(ReportTelemetry.WindowSize, s.SampleCount);
        var recent = new float[100];
        Assert.Equal(100, t.CopyRecentIntervals(recent));
        Assert.All(recent, v => Assert.InRange(v, 249f, 251f));
    }

    [Fact]
    public void Steady250HzReportsAreNotStalls()
    {
        // Regression: v0.2.0 compared 4 ms intervals against a 1 ms default and flagged them as stalls.
        var t = new ReportTelemetry();
        long ts = 1;
        for (int i = 0; i < 2000; i++) { ts += Ticks(i % 2 == 0 ? 3990 : 4010); t.Record(ts, 0, 0); }
        Assert.Equal(0, t.Snapshot().Stalls);
    }

    [Fact]
    public void LongGapCountsAsStall()
    {
        var t = new ReportTelemetry();
        long ts = 1;
        for (int i = 0; i < 500; i++) { ts += Ticks(4000); t.Record(ts, 0, 0); }
        ts += Ticks(40_000); t.Record(ts, 0, 0);
        for (int i = 0; i < 100; i++) { ts += Ticks(4000); t.Record(ts, 0, 0); }
        Assert.Equal(1, t.Snapshot().Stalls);
    }

    [Fact]
    public void NearestRankPercentile()
    {
        var a = Enumerable.Range(1, 100).Select(i => (float)i).ToArray();
        Assert.Equal(50, ReportTelemetry.Percentile(a, 0.50));
        Assert.Equal(99, ReportTelemetry.Percentile(a, 0.99));
        Assert.Equal(100, ReportTelemetry.Percentile(a, 1.0));
    }
}

public sealed class ProfileStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dsp-tests-" + Guid.NewGuid().ToString("N"));
    private readonly ProfileStore _store;

    public ProfileStoreTests() => _store = new ProfileStore(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void DefaultsAreCreatedOnce()
    {
        _store.EnsureDefaults();
        int n = _store.LoadAll(out _).Count;
        _store.EnsureDefaults();
        Assert.Equal(n, _store.LoadAll(out _).Count);
        Assert.Contains(_store.LoadAll(out _), p => p.Name == "Competitive FPS");
    }

    [Fact]
    public void CreateRejectsDuplicateName()
    {
        _store.Create("Rust");
        Assert.Throws<ProfileConflictException>(() => _store.Create("rust"));
    }

    [Fact]
    public void RenameDuplicateDelete()
    {
        var p = _store.Create("Fortnite");
        var renamed = _store.Rename(p.Id, "Fortnite Build");
        Assert.Equal("Fortnite Build", _store.Get(p.Id)!.Name);

        var copy = _store.Duplicate(renamed.Id);
        Assert.NotEqual(renamed.Id, copy.Id);
        Assert.Equal("Fortnite Build copy", copy.Name);

        _store.Delete(copy.Id);
        Assert.Null(_store.Get(copy.Id));
    }

    [Fact]
    public void RoundTripPreservesSettings()
    {
        var p = _store.Create("Precise", new Profile
        {
            RightStick = new StickProfile
            {
                InnerDeadzone = 0.07,
                OutputShape = OutputShape.Square,
                CurvePreset = CurvePresets.Custom,
                CurvePoints = new() { new[] { 0.0, 0.0 }, new[] { 0.4, 0.2 }, new[] { 1.0, 1.0 } },
            },
            Mappings = new MappingProfile
            {
                Base = { [Core.DsButtons.Cross] = new ButtonMapping { Kind = MappingKind.Controller, ControllerTarget = Core.DsButtons.Circle } },
            },
        });

        var loaded = _store.Get(p.Id)!;
        Assert.Equal(0.07, loaded.RightStick.InnerDeadzone);
        Assert.Equal(OutputShape.Square, loaded.RightStick.OutputShape);
        Assert.Equal(MappingKind.Controller, loaded.Mappings.Base[Core.DsButtons.Cross].Kind);

        var settings = loaded.ToPipelineSettings();
        Assert.Equal(0.2, settings.RightStick.Curve.Evaluate(0.4f), 2);
    }

    [Fact]
    public void ImportNeverOverwritesSilently()
    {
        var original = _store.Create("Racing Wheel");
        string file = Path.Combine(_dir, "..", $"export-{Guid.NewGuid():N}.json");
        _store.Export(original.Id, file);
        try
        {
            Assert.Throws<ProfileConflictException>(() => _store.Import(file));

            var copy = _store.Import(file, asCopy: true);
            Assert.NotEqual(original.Id, copy.Id);
            Assert.Equal("Racing Wheel 2", copy.Name);

            var replaced = _store.Import(file, overwrite: true);
            Assert.Equal(original.Id, replaced.Id);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void EveryTemplateBuildsAWorkingProfile()
    {
        Assert.Equal(DefaultProfiles.Templates.Count, DefaultProfiles.Templates.Select(t => t.Name).Distinct().Count());
        foreach (var t in DefaultProfiles.Templates)
        {
            var p = _store.Create(_store.UniqueName(t.Name), t.Create());
            var settings = _store.Get(p.Id)!.ToPipelineSettings();
            Assert.NotNull(settings.RightStick.Curve);
            Assert.InRange(settings.LeftStick.InnerDeadzone, 0, 0.2);
        }
        Assert.Equal(DefaultProfiles.Templates.Count, _store.LoadAll(out _).Count);
    }

    [Fact]
    public void TemplatesReturnFreshCopies()
    {
        var t = DefaultProfiles.Templates.First(x => x.Name == "Rocket League");
        var a = t.Create();
        var b = t.Create();
        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal(OutputShape.Square, a.LeftStick.OutputShape);
    }

    [Fact]
    public void UniqueNameAddsANumber()
    {
        _store.Create("Fortnite");
        Assert.Equal("Fortnite 2", _store.UniqueName("Fortnite"));
        _store.Create("Fortnite 2");
        Assert.Equal("Fortnite 3", _store.UniqueName("Fortnite"));
    }

    [Fact]
    public void RejectsEmptyNames() => Assert.Throws<ArgumentException>(() => _store.Create("   "));

    [Fact]
    public void ReportsCorruptFilesInsteadOfHidingThem()
    {
        File.WriteAllText(Path.Combine(_dir, "broken.json"), "{ not json");
        _store.LoadAll(out var errors);
        Assert.Single(errors);
    }
}
