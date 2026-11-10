using DualSensePrecision.Core;
using DualSensePrecision.InputEngine;
using Xunit;

namespace DualSensePrecision.Tests;

public class DiagnosticsTests
{
    [Fact]
    public void StillStickIsGoodWithSmallSuggestion()
    {
        var d = new DriftAnalyzer();
        for (int i = 0; i < 1000; i++) d.Add(0.004f + (i % 3) * 0.002f, -0.004f);
        var r = d.Result();
        Assert.Equal(Verdict.Good, r.Verdict);
        Assert.True(r.MaxDeviation < 0.02);
        Assert.Equal(0.03, r.SuggestedDeadzone, 3); // floor of 3 %
    }

    [Fact]
    public void DriftingStickIsFlaggedAndCovered()
    {
        var d = new DriftAnalyzer();
        for (int i = 0; i < 500; i++) d.Add(0.11f, 0.02f);
        var r = d.Result();
        Assert.Equal(Verdict.Problem, r.Verdict);
        Assert.True(r.SuggestedDeadzone >= r.MaxDeviation + 0.02 - 1e-9);
        Assert.InRange(r.CentreOffset, 0.11, 0.12);
    }

    [Fact]
    public void EmptyDriftIsNotTested() => Assert.Equal(Verdict.NotTested, new DriftAnalyzer().Result().Verdict);

    [Fact]
    public void FullCircleIsGoodRange()
    {
        var a = new RangeAnalyzer();
        for (int i = 0; i < 720; i++)
        {
            double t = i * Math.PI / 180;
            a.Add((float)Math.Cos(t), (float)Math.Sin(t));
        }
        var r = a.Result();
        Assert.Equal(1.0, r.Coverage, 3);
        Assert.Equal(Verdict.Good, r.Verdict);
    }

    [Fact]
    public void ShortDirectionIsReported()
    {
        var a = new RangeAnalyzer();
        for (int i = 0; i < 360; i++)
        {
            double t = i * Math.PI / 180;
            double m = i is > 75 and < 115 ? 0.8 : 1.0; // weak around straight up
            a.Add((float)(Math.Cos(t) * m), (float)(Math.Sin(t) * m));
        }
        var r = a.Result();
        Assert.Equal(Verdict.Problem, r.Verdict);
        Assert.InRange(r.WeakestEdge, 0.79, 0.81);
    }

    [Fact]
    public void PartialCircleIsNotTested()
    {
        var a = new RangeAnalyzer();
        for (int i = 0; i < 90; i++) { double t = i * Math.PI / 180; a.Add((float)Math.Cos(t), (float)Math.Sin(t)); }
        Assert.Equal(Verdict.NotTested, a.Result().Verdict);
        Assert.InRange(a.Result().Coverage, 0.2, 0.3);
    }

    [Fact]
    public void SmallMovementsDoNotCountForRange()
    {
        var a = new RangeAnalyzer();
        a.Add(0.1f, 0.1f);
        Assert.All(a.Maxima(), v => Assert.Equal(0, v));
    }

    [Fact]
    public void TriggerFullRange()
    {
        var t = new TriggerAnalyzer();
        foreach (byte v in new byte[] { 0, 50, 255, 120, 0 }) t.Add(v);
        Assert.Equal(new TriggerResult(0, 255, Verdict.Good), t.Result());
    }

    [Fact]
    public void TriggerThatNeverReleasesIsAWarning()
    {
        var t = new TriggerAnalyzer();
        foreach (byte v in new byte[] { 12, 255, 12 }) t.Add(v);
        Assert.Equal(Verdict.Warning, t.Result().Verdict);
    }

    [Fact]
    public void ButtonCoverageTracksMissing()
    {
        var c = new ButtonCoverage();
        c.Add(DsButtons.Cross | DsButtons.Circle);
        c.Add(DsButtons.Square);
        var missing = c.Missing(edge: false);
        Assert.DoesNotContain(DsButtons.Cross, missing);
        Assert.Contains(DsButtons.Triangle, missing);
        Assert.Equal(ButtonCoverage.Standard.Length - 3, missing.Count);
        Assert.Equal(ButtonCoverage.Standard.Length + 4 - 3, c.Missing(edge: true).Count);
        c.Reset();
        Assert.Equal(ButtonCoverage.Standard.Length, c.Missing(false).Count);
    }

    [Fact]
    public void MotionAtRestIsGood()
    {
        var m = new MotionAnalyzer();
        for (int i = 0; i < 300; i++) m.Add(0.1, -0.2, 0.05 + (i % 2) * 0.1, 0, 1.0, 0.02);
        var r = m.Result();
        Assert.Equal(Verdict.Good, r.Verdict);
        Assert.InRange(r.Gravity, 0.99, 1.01);
    }

    [Fact]
    public void MovingControllerIsNotGood()
    {
        var m = new MotionAnalyzer();
        for (int i = 0; i < 300; i++) m.Add(40, 0, 0, 0, 1.4, 0);
        Assert.Equal(Verdict.Problem, m.Result().Verdict);
    }

    [Fact]
    public void ReportListsEverySection()
    {
        string text = DiagnosticReport.Build("DualSense", "6.30", 250,
            new[] { DsButtons.Mute }, b => b.ToString(),
            new DriftResult(100, 0, 0, 0.01, 0.02, 0.01, 0.04, Verdict.Good), DriftResult.Empty,
            new RangeResult(1, 0.99, 0.97, Verdict.Good), RangeResult.Empty,
            new TriggerResult(0, 255, Verdict.Good), TriggerResult.Empty,
            true, new MotionResult(0.3, 0.2, 1.0, Verdict.Good), new DateTime(2026, 11, 10, 12, 0, 0));
        Assert.Contains("250 Hz", text);
        Assert.Contains("[CHECK] Buttons", text);
        Assert.Contains("Mute", text);
        Assert.Contains("[PASS] Left stick drift", text);
        Assert.Contains("Suggested inner deadzone: 4%", text);
        Assert.Contains("[NOT TESTED] Right stick drift", text);
        Assert.Contains("[PASS] L2", text);
        Assert.Contains("[NOT TESTED] R2", text);
        Assert.Contains("[PASS] Touchpad", text);
        Assert.Contains("[PASS] Motion sensors", text);
    }
}
