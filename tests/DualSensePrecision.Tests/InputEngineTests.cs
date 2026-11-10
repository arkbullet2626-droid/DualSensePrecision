using DualSensePrecision.Core;
using DualSensePrecision.InputEngine;
using Xunit;

namespace DualSensePrecision.Tests;

public class ResponseCurveTests
{
    [Fact]
    public void LinearIsIdentity()
    {
        for (int i = 0; i <= 100; i++)
            Assert.Equal(i / 100f, ResponseCurve.Linear.Evaluate(i / 100f), 3);
    }

    [Fact]
    public void EndpointsArePinned()
    {
        var c = new ResponseCurve(new[] { new CurvePoint(0.5, 0.2) });
        Assert.Equal(2, c.Points.Count - 1); // (0,0) and (1,1) added
        Assert.Equal(0, c.Evaluate(0), 4);
        Assert.Equal(1, c.Evaluate(1), 4);
    }

    [Theory]
    [MemberData(nameof(Presets))]
    public void EveryPresetIsMonotoneAndBounded(string preset)
    {
        var c = CurvePresets.Create(preset);
        float prev = -1;
        for (int i = 0; i <= 1000; i++)
        {
            float y = c.Evaluate(i / 1000f);
            Assert.InRange(y, 0f, 1f);
            Assert.True(y >= prev - 1e-6f, $"{preset} decreased at x={i / 1000f}");
            prev = y;
        }
    }

    public static IEnumerable<object[]> Presets() => CurvePresets.All.Select(p => new object[] { p });

    [Fact]
    public void DescendingPointsAreForcedMonotone()
    {
        var c = new ResponseCurve(new[] { new CurvePoint(0, 0), new CurvePoint(0.3, 0.8), new CurvePoint(0.6, 0.2), new CurvePoint(1, 1) });
        Assert.True(c.Evaluate(0.6f) >= c.Evaluate(0.3f) - 1e-4f);
    }

    [Fact]
    public void PrecisionIsSlowerNearCentre() =>
        Assert.True(CurvePresets.Create(CurvePresets.Precision).Evaluate(0.3f) < 0.3f);

    [Fact]
    public void AggressiveIsFasterNearCentre() =>
        Assert.True(CurvePresets.Create(CurvePresets.Aggressive).Evaluate(0.3f) > 0.3f);

    [Fact]
    public void PresetPointsRoundTripAsCustom()
    {
        // The stick editor copies a preset's points into a Custom curve; the shape must be identical.
        foreach (var name in CurvePresets.All)
        {
            var preset = CurvePresets.Create(name);
            var custom = new ResponseCurve(preset.Points);
            for (int i = 0; i <= 50; i++)
                Assert.Equal(preset.Evaluate(i / 50f), custom.Evaluate(i / 50f), 4);
        }
    }

    [Fact]
    public void PassesThroughControlPoints()
    {
        var c = new ResponseCurve(new[] { new CurvePoint(0, 0), new CurvePoint(0.5, 0.3), new CurvePoint(1, 1) });
        Assert.Equal(0.3, c.Evaluate(0.5f), 2);
    }
}

public class StickProcessorTests
{
    private static readonly StickSettings Plain = new() { InnerDeadzone = 0.1, OuterDeadzone = 1.0 };

    [Fact]
    public void InsideInnerDeadzoneIsZero()
    {
        Assert.Equal((0f, 0f), StickProcessor.Process(0.05f, 0.05f, Plain));
    }

    [Fact]
    public void FullDeflectionReachesOne()
    {
        var (x, y) = StickProcessor.Process(1f, 0f, Plain);
        Assert.Equal(1f, x, 3);
        Assert.Equal(0f, y, 3);
    }

    [Fact]
    public void RadialDeadzoneRescalesWithoutJump()
    {
        // Just past the deadzone, output should be near zero (no discontinuity) with no anti-deadzone.
        var (x, _) = StickProcessor.Process(0.11f, 0f, Plain);
        Assert.InRange(x, 0f, 0.02f);
    }

    [Fact]
    public void AntiDeadzoneLiftsSmallInputs()
    {
        var s = Plain with { AntiDeadzone = 0.2 };
        var (x, _) = StickProcessor.Process(0.11f, 0f, s);
        Assert.InRange(x, 0.2f, 0.22f);
    }

    [Fact]
    public void OuterDeadzoneSaturatesEarly()
    {
        var s = Plain with { OuterDeadzone = 0.8 };
        var (x, _) = StickProcessor.Process(0.85f, 0f, s);
        Assert.Equal(1f, x, 3);
    }

    [Fact]
    public void AxialDeadzoneTreatsAxesIndependently()
    {
        var s = Plain with { DeadzoneShape = DeadzoneShape.Axial };
        var (x, y) = StickProcessor.Process(0.05f, 0.8f, s);
        Assert.Equal(0f, x);
        Assert.True(y > 0.7f);
    }

    [Fact]
    public void CircularOutputNeverExceedsUnitCircle()
    {
        var s = Plain with { SensitivityX = 2, SensitivityY = 2 };
        var (x, y) = StickProcessor.Process(0.7f, 0.7f, s);
        Assert.True(x * x + y * y <= 1.0001f);
    }

    [Fact]
    public void SquareOutputReachesCorner()
    {
        var s = Plain with { OutputShape = OutputShape.Square, InnerDeadzone = 0 };
        var (x, y) = StickProcessor.Process(0.7071f, 0.7071f, s);
        Assert.Equal(1f, x, 2);
        Assert.Equal(1f, y, 2);
    }

    [Fact]
    public void SensitivityScalesAxes()
    {
        var s = Plain with { InnerDeadzone = 0, SensitivityY = 0.5 };
        var (_, y) = StickProcessor.Process(0f, 1f, s);
        Assert.Equal(0.5f, y, 3);
    }

    [Fact]
    public void CalibrationRemovesCentreOffset()
    {
        var cal = new StickCalibration(0.05, 0, 1, 1, 1, 1);
        var s = Plain with { Calibration = cal };
        Assert.Equal((0f, 0f), StickProcessor.Process(0.05f, 0f, s));
    }

    [Fact]
    public void InvertYFlips()
    {
        var s = Plain with { InvertY = true, InnerDeadzone = 0 };
        Assert.True(StickProcessor.Process(0, 1f, s).y < 0);
    }

    [Fact]
    public void NormalizeAxisMapsRawBytes()
    {
        Assert.Equal(-1f, ControllerState.NormalizeAxis(0), 3);
        Assert.Equal(1f, ControllerState.NormalizeAxis(255), 3);
        Assert.InRange(ControllerState.NormalizeAxis(128), 0f, 0.01f);
    }
}

public class TriggerProcessorTests
{
    [Fact]
    public void DeadzoneAndMaxPoint()
    {
        var s = new TriggerSettings { Deadzone = 0.1, MaxPoint = 0.9 };
        Assert.Equal(0f, TriggerProcessor.Process(20, s).Value);    // 7.8 %
        Assert.Equal(1f, TriggerProcessor.Process(240, s).Value);   // 94 %
        Assert.InRange(TriggerProcessor.Process(128, s).Value, 0.45f, 0.55f);
    }

    [Fact]
    public void DigitalModeIsAllOrNothing()
    {
        var s = new TriggerSettings { Mode = TriggerMode.Digital, ActivationPoint = 0.2 };
        Assert.Equal(0f, TriggerProcessor.Process(40, s).Value);
        var on = TriggerProcessor.Process(60, s);
        Assert.Equal(1f, on.Value);
        Assert.True(on.Pressed);
    }

    [Fact]
    public void DualZoneReportsFullPress()
    {
        var s = new TriggerSettings { Mode = TriggerMode.DualZone, SecondStagePoint = 0.9 };
        Assert.False(TriggerProcessor.Process(200, s).FullPress);
        Assert.True(TriggerProcessor.Process(250, s).FullPress);
    }
}

public class ButtonMapperTests
{
    [Fact]
    public void DefaultPassesThrough()
    {
        var m = new ButtonMapper();
        Assert.Equal(DsButtons.Cross | DsButtons.L1, m.Map(DsButtons.Cross | DsButtons.L1, out var ext));
        Assert.Equal(DsButtons.None, ext);
    }

    [Fact]
    public void RemapsAndDisables()
    {
        var m = new ButtonMapper(new Dictionary<DsButtons, ButtonMapping>
        {
            [DsButtons.Cross] = new() { Kind = MappingKind.Controller, ControllerTarget = DsButtons.Circle },
            [DsButtons.Square] = new() { Kind = MappingKind.Disabled },
        });
        Assert.Equal(DsButtons.Circle, m.Map(DsButtons.Cross | DsButtons.Square, out _));
    }

    [Fact]
    public void KeyboardBindingIsRoutedExternally()
    {
        var m = new ButtonMapper(new Dictionary<DsButtons, ButtonMapping>
        {
            [DsButtons.Triangle] = new() { Kind = MappingKind.Keyboard, VirtualKey = 0x52 },
        });
        Assert.Equal(DsButtons.None, m.Map(DsButtons.Triangle, out var ext));
        Assert.Equal(DsButtons.Triangle, ext);
    }

    [Fact]
    public void ToggleLatchesOnRisingEdge()
    {
        var m = new ButtonMapper(new Dictionary<DsButtons, ButtonMapping>
        {
            [DsButtons.L3] = new() { Behavior = MappingBehavior.Toggle },
        });
        Assert.Equal(DsButtons.L3, m.Map(DsButtons.L3, out _));    // press → on
        Assert.Equal(DsButtons.L3, m.Map(DsButtons.None, out _));  // release → stays on
        Assert.Equal(DsButtons.None, m.Map(DsButtons.L3, out _));  // press → off
        Assert.Equal(DsButtons.None, m.Map(DsButtons.None, out _));
    }

    [Fact]
    public void ShiftLayerAppliesWhileHeldAndConsumesShiftKey()
    {
        var m = new ButtonMapper(
            baseLayer: null,
            shiftLayer: new Dictionary<DsButtons, ButtonMapping>
            {
                [DsButtons.Cross] = new() { Kind = MappingKind.Controller, ControllerTarget = DsButtons.Triangle },
            },
            shiftButton: DsButtons.L1);
        Assert.Equal(DsButtons.Cross, m.Map(DsButtons.Cross, out _));
        Assert.Equal(DsButtons.Triangle, m.Map(DsButtons.Cross | DsButtons.L1, out _));
    }
}

public class TriggerRemapTests
{
    private static ProcessedState Run(ButtonMapper mapper, ControllerState s)
    {
        var p = new InputPipeline { Settings = new PipelineSettings { Mapper = mapper } };
        return p.Process(s);
    }

    [Fact]
    public void ButtonRemappedOntoTriggerDrivesAnalogFully()
    {
        var mapper = new ButtonMapper(new Dictionary<DsButtons, ButtonMapping>
        {
            [DsButtons.R1] = new() { Kind = MappingKind.Controller, ControllerTarget = DsButtons.R2 },
        });
        var o = Run(mapper, new ControllerState { LeftX = 128, LeftY = 128, RightX = 128, RightY = 128, Buttons = DsButtons.R1 });
        Assert.Equal(1f, o.R2);
    }

    [Fact]
    public void RemappedTriggerNoLongerLeaksAnalog()
    {
        var mapper = new ButtonMapper(new Dictionary<DsButtons, ButtonMapping>
        {
            [DsButtons.L2] = new() { Kind = MappingKind.Controller, ControllerTarget = DsButtons.L1 },
        });
        var o = Run(mapper, new ControllerState { LeftX = 128, LeftY = 128, RightX = 128, RightY = 128, L2 = 255 });
        Assert.Equal(0f, o.L2);
        Assert.True((o.Buttons & DsButtons.L1) != 0);
    }

    [Fact]
    public void TriggerUsedAsShiftIsSilent()
    {
        var mapper = new ButtonMapper(null, null, shiftButton: DsButtons.L2);
        var o = Run(mapper, new ControllerState { LeftX = 128, LeftY = 128, RightX = 128, RightY = 128, L2 = 255 });
        Assert.Equal(0f, o.L2);
    }

    [Fact]
    public void ResolveUsesLayerFromLastMap()
    {
        var key = new ButtonMapping { Kind = MappingKind.Keyboard, VirtualKey = 0x52 };
        var mapper = new ButtonMapper(null, new Dictionary<DsButtons, ButtonMapping> { [DsButtons.Cross] = key }, DsButtons.L1);
        mapper.Map(DsButtons.Cross, out _);
        Assert.Equal(MappingKind.Default, mapper.Resolve(DsButtons.Cross).Kind);
        mapper.Map(DsButtons.Cross | DsButtons.L1, out var ext);
        Assert.Equal(key, mapper.Resolve(DsButtons.Cross));
        Assert.Equal(DsButtons.Cross, ext);
    }
}

public class InputPipelineTests
{
    [Fact]
    public void ProcessesAndPublishes()
    {
        var p = new InputPipeline();
        var s = new ControllerState { LeftX = 255, LeftY = 128, RightX = 128, RightY = 0, R2 = 255, Buttons = DsButtons.Cross, ArrivalTimestamp = 99 };
        var o = p.Process(s);
        Assert.True(o.LeftX > 0.95f);
        Assert.True(o.RightY > 0.95f);   // raw Y=0 is "up" → positive
        Assert.Equal(1f, o.R2, 2);
        Assert.True((o.Buttons & DsButtons.R2) != 0); // derived from our activation point
        Assert.True((o.Buttons & DsButtons.Cross) != 0);

        long v = p.Sample(out var raw, out var proc);
        Assert.Equal(1, v);
        Assert.Equal(99, raw.ArrivalTimestamp);
        Assert.Equal(o.LeftX, proc.LeftX);
    }
}
