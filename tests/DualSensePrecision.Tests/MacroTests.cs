using System.Diagnostics;
using DualSensePrecision.Core;
using DualSensePrecision.InputEngine;
using DualSensePrecision.Profiles;
using Xunit;

namespace DualSensePrecision.Tests;

internal sealed class RecordingSink : IMacroSink
{
    public List<string> Events { get; } = new();
    public void Button(DsButtons button, bool down) { lock (Events) Events.Add($"{(down ? "+" : "-")}{button}"); }
    public void Key(ushort virtualKey, bool down) { lock (Events) Events.Add($"{(down ? "+" : "-")}k{virtualKey}"); }
    public void Mouse(MouseButtonTarget button, bool down) { lock (Events) Events.Add($"{(down ? "+" : "-")}m{button}"); }
}

public class MacroTests
{
    private static readonly Func<int, ManualResetEventSlim, bool> Instant = (_, stop) => !stop.IsSet;

    private static MacroDefinition Macro(params MacroStep[] steps) => new() { Name = "t", Steps = steps.ToList() };

    [Fact]
    public void PlaysStepsInOrder()
    {
        var sink = new RecordingSink();
        var m = Macro(
            new MacroStep(MacroStepKind.ButtonDown, DsButtons.Cross),
            new MacroStep(MacroStepKind.Delay, DelayMs: 50),
            new MacroStep(MacroStepKind.ButtonUp, DsButtons.Cross),
            new MacroStep(MacroStepKind.KeyDown, VirtualKey: 0x52),
            new MacroStep(MacroStepKind.KeyUp, VirtualKey: 0x52),
            new MacroStep(MacroStepKind.MouseDown, Mouse: MouseButtonTarget.Right),
            new MacroStep(MacroStepKind.MouseUp, Mouse: MouseButtonTarget.Right));
        new MacroRun(m, sink, repeat: false, Instant).RunToEnd();
        Assert.Equal(new[] { "+Cross", "-Cross", "+k82", "-k82", "+mRight", "-mRight" }, sink.Events);
    }

    [Fact]
    public void ReleasesEverythingLeftHeld()
    {
        var sink = new RecordingSink();
        var m = Macro(
            new MacroStep(MacroStepKind.ButtonDown, DsButtons.R2),
            new MacroStep(MacroStepKind.KeyDown, VirtualKey: 0x10),
            new MacroStep(MacroStepKind.MouseDown, Mouse: MouseButtonTarget.Left));
        var run = new MacroRun(m, sink, repeat: false, Instant);
        run.RunToEnd();
        Assert.Contains("-R2", sink.Events);
        Assert.Contains("-k16", sink.Events);
        Assert.Contains("-mLeft", sink.Events);
        Assert.Equal("Finished", run.StopReason);
    }

    [Fact]
    public void DuplicatePressIsIgnoredAndUnpressedReleaseIsSkipped()
    {
        var sink = new RecordingSink();
        var m = Macro(
            new MacroStep(MacroStepKind.ButtonUp, DsButtons.Square),
            new MacroStep(MacroStepKind.ButtonDown, DsButtons.Cross),
            new MacroStep(MacroStepKind.ButtonDown, DsButtons.Cross));
        new MacroRun(m, sink, repeat: false, Instant).RunToEnd();
        Assert.Equal(new[] { "+Cross", "-Cross" }, sink.Events);
    }

    [Fact]
    [Trait("Category", "Timing")]
    public void RepeatingMacroStopsWhenAskedAndReleases()
    {
        var sink = new RecordingSink();
        var m = Macro(
            new MacroStep(MacroStepKind.ButtonDown, DsButtons.Cross),
            new MacroStep(MacroStepKind.Delay, DelayMs: 5),
            new MacroStep(MacroStepKind.ButtonUp, DsButtons.Cross));
        var run = new MacroRun(m, sink, repeat: true);
        run.Start();
        SpinWait.SpinUntil(() => run.Cycles >= 3, TimeSpan.FromSeconds(5));
        run.Stop("test");
        run.Join();
        Assert.False(run.IsRunning);
        Assert.True(run.Cycles >= 3);
        Assert.Equal("test", run.StopReason);
        lock (sink.Events)
        {
            int downs = sink.Events.Count(e => e == "+Cross");
            int ups = sink.Events.Count(e => e == "-Cross");
            Assert.Equal(downs, ups); // never left held
        }
    }

    [Fact]
    [Trait("Category", "Timing")]
    public void RepeatWithoutDelaysIsThrottled()
    {
        var sink = new RecordingSink();
        var m = Macro(new MacroStep(MacroStepKind.ButtonDown, DsButtons.Cross), new MacroStep(MacroStepKind.ButtonUp, DsButtons.Cross));
        var run = new MacroRun(m, sink, repeat: true);
        var sw = Stopwatch.StartNew();
        run.Start();
        Thread.Sleep(220);
        run.Stop();
        run.Join();
        // At least 20 ms per cycle → at most ~11 cycles in 220 ms (allow scheduling slack).
        Assert.InRange(run.Cycles, 1, 14);
    }

    [Fact]
    [Trait("Category", "Timing")]
    public void StopInterruptsALongDelayQuickly()
    {
        var sink = new RecordingSink();
        var m = Macro(new MacroStep(MacroStepKind.ButtonDown, DsButtons.Cross), new MacroStep(MacroStepKind.Delay, DelayMs: 10_000));
        var run = new MacroRun(m, sink, repeat: false);
        run.Start();
        Thread.Sleep(50);
        var sw = Stopwatch.StartNew();
        run.Stop();
        run.Join();
        Assert.True(sw.ElapsedMilliseconds < 500, $"took {sw.ElapsedMilliseconds} ms to stop");
        Assert.Contains("-Cross", sink.Events);
    }

    [Fact]
    [Trait("Category", "Timing")]
    public void PreciseWaitIsAccurate()
    {
        using var stop = new ManualResetEventSlim(false);
        var sw = Stopwatch.StartNew();
        Assert.True(MacroRun.PreciseWait(30, stop));
        Assert.InRange(sw.Elapsed.TotalMilliseconds, 30.0, 45); // never early
    }

    [Fact]
    public void SanitizeEnforcesLimits()
    {
        var steps = Enumerable.Range(0, 400).Select(_ => new MacroStep(MacroStepKind.Delay, DelayMs: 99_999)).ToArray();
        var m = MacroLimits.Sanitize(Macro(steps) with { Name = new string('x', 100) });
        Assert.Equal(MacroLimits.MaxSteps, m.Steps.Count);
        Assert.All(m.Steps, s => Assert.Equal(MacroLimits.MaxDelayMs, s.DelayMs));
        Assert.Equal(MacroLimits.MaxNameLength, m.Name.Length);
    }

    [Fact]
    public void WarningsFlagUnreleasedInput()
    {
        var w = MacroLimits.Warnings(Macro(new MacroStep(MacroStepKind.ButtonDown, DsButtons.Cross)));
        Assert.Contains(w, x => x.Contains("never released"));
        Assert.Empty(MacroLimits.Warnings(Macro(
            new MacroStep(MacroStepKind.ButtonDown, DsButtons.Cross),
            new MacroStep(MacroStepKind.ButtonUp, DsButtons.Cross))));
    }

    [Fact]
    public void RecorderCapturesPressesAndTiming()
    {
        var r = new MacroRecorder();
        long t0 = Stopwatch.GetTimestamp();
        long Ms(int ms) => t0 + ms * Stopwatch.Frequency / 1000;
        r.Start(DsButtons.None);
        r.OnReport(DsButtons.None, Ms(0));
        r.OnReport(DsButtons.Cross, Ms(4));
        r.OnReport(DsButtons.Cross, Ms(8));
        r.OnReport(DsButtons.None, Ms(104));
        r.OnReport(DsButtons.Square, Ms(304));
        var steps = r.Stop();

        Assert.Equal(MacroStepKind.ButtonDown, steps[0].Kind);
        Assert.Equal(DsButtons.Cross, steps[0].Button);
        Assert.Equal(new MacroStep(MacroStepKind.Delay, DelayMs: 100), steps[1]);
        Assert.Equal(new MacroStep(MacroStepKind.ButtonUp, DsButtons.Cross), steps[2]);
        Assert.Equal(200, steps[3].DelayMs);
        Assert.Equal(new MacroStep(MacroStepKind.ButtonDown, DsButtons.Square), steps[4]);
        Assert.Equal(new MacroStep(MacroStepKind.ButtonUp, DsButtons.Square), steps[^1]); // auto-released at the end
    }

    [Fact]
    public void RecorderIgnoresButtonsHeldWhenStarted()
    {
        var r = new MacroRecorder();
        long t = Stopwatch.GetTimestamp();
        r.Start(DsButtons.Options);
        r.OnReport(DsButtons.Options, t);
        r.OnReport(DsButtons.None, t + Stopwatch.Frequency / 10);
        var steps = r.Stop();
        // Releasing a button that was already held at start is still recorded as a release, but never as a press.
        Assert.DoesNotContain(steps, s => s.Kind == MacroStepKind.ButtonDown);
    }

    [Fact]
    public void MacrosRoundTripThroughProfiles()
    {
        string dir = Path.Combine(Path.GetTempPath(), "dsp-macro-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ProfileStore(dir);
            var macro = new MacroDefinition
            {
                Name = "Build",
                PlayMode = MacroPlayMode.Toggle,
                Steps = { new MacroStep(MacroStepKind.KeyDown, VirtualKey: 0x51), new MacroStep(MacroStepKind.Delay, DelayMs: 30), new MacroStep(MacroStepKind.KeyUp, VirtualKey: 0x51) },
            };
            var p = store.Create("Macro test", new Profile { Macros = { macro } });
            var loaded = store.Get(p.Id)!;
            Assert.Single(loaded.Macros);
            Assert.Equal(MacroPlayMode.Toggle, loaded.Macros[0].PlayMode);
            Assert.Equal(macro.Steps, loaded.Macros[0].Steps);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
