using DualSensePrecision.Core;
using DualSensePrecision.HID.Protocol;
using DualSensePrecision.InputEngine;
using DualSensePrecision.Telemetry;
using Xunit;

namespace DualSensePrecision.Tests;

public class XboxMappingTests
{
    [Fact]
    public void ButtonsMapByPosition()
    {
        var b = XboxProtocol.MapButtons((ushort)(XboxProtocol.A | XboxProtocol.B | XboxProtocol.X | XboxProtocol.Y));
        Assert.Equal(DsButtons.Cross | DsButtons.Circle | DsButtons.Square | DsButtons.Triangle, b);

        b = XboxProtocol.MapButtons((ushort)(XboxProtocol.LeftShoulder | XboxProtocol.RightShoulder | XboxProtocol.LeftThumb | XboxProtocol.RightThumb));
        Assert.Equal(DsButtons.L1 | DsButtons.R1 | DsButtons.L3 | DsButtons.R3, b);

        b = XboxProtocol.MapButtons((ushort)(XboxProtocol.Back | XboxProtocol.Start | XboxProtocol.Guide));
        Assert.Equal(DsButtons.Create | DsButtons.Options | DsButtons.PS, b);

        b = XboxProtocol.MapButtons((ushort)(XboxProtocol.DpadUp | XboxProtocol.DpadDown | XboxProtocol.DpadLeft | XboxProtocol.DpadRight));
        Assert.Equal(DsButtons.DpadUp | DsButtons.DpadDown | DsButtons.DpadLeft | DsButtons.DpadRight, b);
    }

    [Fact]
    public void NoButtonsMeansNone() => Assert.Equal(DsButtons.None, XboxProtocol.MapButtons(0));

    [Fact]
    public void XboxButtonSetHasNoDualSenseOnlyButtons()
    {
        Assert.DoesNotContain(DsButtons.Touchpad, XboxProtocol.Buttons);
        Assert.DoesNotContain(DsButtons.Mute, XboxProtocol.Buttons);
        Assert.Equal(17, XboxProtocol.Buttons.Length);
    }

    [Fact]
    public void AxesKeepFullResolutionAndYUp()
    {
        var s = XboxProtocol.ToState(0, 0, 0, lx: 32767, ly: 32767, rx: -32768, ry: 1, arrival: 42);
        Assert.True(s.HasPreciseSticks);
        Assert.Equal(1f, s.LX);
        Assert.Equal(1f, s.LY);    // up is positive
        Assert.Equal(-1f, s.RX);   // -32768 clamps to -1
        Assert.Equal(1 / 32767f, s.RY, 6); // one step of 16-bit resolution survives (8-bit couldn't show it)
        Assert.Equal(42, s.ArrivalTimestamp);
    }

    [Fact]
    public void ByteFieldsFollowDualSenseConvention()
    {
        var s = XboxProtocol.ToState(0, 0, 0, lx: 0, ly: 32767, rx: 32767, ry: -32768, arrival: 0);
        Assert.Equal(128, s.LeftX);   // centre
        Assert.Equal(0, s.LeftY);     // stick up = small Y byte, like the DualSense
        Assert.Equal(255, s.RightX);
        Assert.Equal(255, s.RightY);  // stick down = large Y byte (and -(-32768) must not overflow)
    }

    [Fact]
    public void TriggersAreAnalogWithDigitalBitPastThreshold()
    {
        var s = XboxProtocol.ToState(0, lt: 30, rt: 31, 0, 0, 0, 0, 0);
        Assert.Equal(30, s.L2);
        Assert.Equal(31, s.R2);
        Assert.False(s.IsPressed(DsButtons.L2)); // Microsoft's documented threshold is "greater than 30"
        Assert.True(s.IsPressed(DsButtons.R2));
    }

    [Fact]
    public void PipelineUsesPreciseSticks()
    {
        var pipeline = new InputPipeline
        {
            Settings = new PipelineSettings { LeftStick = new StickSettings { InnerDeadzone = 0, OuterDeadzone = 1 } },
        };
        // 300/32767 ≈ 0.009: lost entirely at 8 bits (it would round to the centre byte), kept at 16 bits.
        var p = pipeline.Process(XboxProtocol.ToState(0, 0, 0, lx: 300, ly: 0, 0, 0, 0));
        Assert.True(p.LeftX > 0.005f, $"expected fine movement to survive, got {p.LeftX}");
    }

    [Fact]
    public void BatteryIsALevelNotAPercentage()
    {
        Assert.Equal(BatteryKind.Wired, XboxProtocol.Battery(XboxProtocol.BatteryTypeWired, 0).Kind);
        Assert.Equal(BatteryKind.Unknown, XboxProtocol.Battery(XboxProtocol.BatteryTypeUnknown, 3).Kind);
        var (kind, pct) = XboxProtocol.Battery(XboxProtocol.BatteryTypeNimh, 2);
        Assert.Equal(BatteryKind.Level, kind);
        Assert.Equal("Medium", XboxProtocol.BatteryLevelName(pct));
        Assert.Equal("Full", XboxProtocol.BatteryLevelName(XboxProtocol.Battery(XboxProtocol.BatteryTypeAlkaline, 3).Percent));
        Assert.Equal("Low", XboxProtocol.BatteryLevelName(XboxProtocol.Battery(XboxProtocol.BatteryTypeAlkaline, 1).Percent));
        Assert.Equal("Empty", XboxProtocol.BatteryLevelName(XboxProtocol.Battery(XboxProtocol.BatteryTypeAlkaline, 0).Percent));
    }

    [Fact]
    public void VibrationScalesToXInputRangeAndStrength()
    {
        Assert.Equal(((ushort)65535, (ushort)0), XboxProtocol.Vibration(255, 0, 100));
        Assert.Equal(((ushort)32768, (ushort)16448), XboxProtocol.Vibration(255, 128, 50)); // 50 % of full, 50 % of half
        Assert.Equal(((ushort)0, (ushort)0), XboxProtocol.Vibration(255, 255, 0));
    }

    [Fact]
    public void RawStateIsLittleEndian()
    {
        var raw = new byte[XboxProtocol.RawLength];
        XboxProtocol.WriteRaw(raw, 0x01020304, 0x1000, 7, 9, -1, 2, 3, 4);
        Assert.Equal(new byte[] { 4, 3, 2, 1, 0x00, 0x10, 7, 9, 0xFF, 0xFF, 2, 0, 3, 0, 4, 0 }, raw);
    }

    [Theory]
    [InlineData(0x045E, 0x0B12, "Xbox Series X|S Controller")]
    [InlineData(0x045E, 0x02EA, "Xbox One Controller")]
    [InlineData(0x045E, 0x028E, "Xbox 360 Controller")]
    [InlineData(0x045E, 0x0B00, "Xbox Elite Controller")]
    [InlineData(0, 0, "Xbox controller")]
    [InlineData(0x0E6F, 0x0001, "Xbox-compatible controller")]
    public void ModelNames(int vid, int pid, string name) => Assert.Equal(name, XboxProtocol.ModelName((ushort)vid, (ushort)pid));

    [Fact]
    public void XboxDiagnosticReportSkipsTouchpadAndMotion()
    {
        string text = DiagnosticReport.Build("Xbox Series X|S Controller", "not available through XInput", 248,
            Array.Empty<DsButtons>(), b => b.ToString(),
            DriftResult.Empty, DriftResult.Empty, RangeResult.Empty, RangeResult.Empty,
            new TriggerResult(0, 255, Verdict.Good), TriggerResult.Empty,
            false, MotionResult.Empty, new DateTime(2026, 11, 10), xbox: true);
        Assert.Contains("Update rate (XInput", text);
        Assert.Contains("not a hardware report rate", text);
        Assert.Contains("[PASS] LT", text);
        Assert.DoesNotContain("[NOT TESTED] Touchpad", text);
        Assert.Contains("not on Xbox controllers", text);
    }

    [Fact]
    public void ButtonCoverageAgainstXboxSet()
    {
        var c = new ButtonCoverage();
        foreach (var b in XboxProtocol.Buttons) c.Add(b);
        Assert.Empty(c.Missing(XboxProtocol.Buttons));
        Assert.Contains(DsButtons.Touchpad, c.Missing(edge: false)); // the DualSense list still expects it
    }

    [Fact]
    public void ChangeOnlySourcesDontCountIdleAsStalls()
    {
        var t = new ReportTelemetry { DetectStalls = false };
        long tick = (long)(HighResClock.TicksPerMicrosecond * 4000);
        long now = 1;
        for (int i = 0; i < 200; i++) t.Record(now += tick, 0, 0);
        t.Record(now += tick * 500, 0, 0); // 2 s with the controller untouched
        Assert.Equal(0, t.Snapshot().Stalls);
    }
}

public class XboxSlotFilterTests
{
    private static XInputSlotInfo Real360(int slot) => new(slot, 0x045E, 0x028E);
    private static XInputSlotInfo Series(int slot) => new(slot, 0x045E, 0x0B12);

    [Fact]
    public void WithoutVirtualPadEverythingIsReal()
    {
        var f = new XboxSlotFilter();
        f.Update(VirtualPadPhase.Off, new[] { Real360(0), Series(1) });
        Assert.False(f.IsExcluded(Real360(0)));
        Assert.False(f.IsExcluded(Series(1)));
    }

    [Fact]
    public void PadAppearingAfterVirtualStartsIsTheVirtualOne()
    {
        var f = new XboxSlotFilter();
        f.Update(VirtualPadPhase.Off, new[] { Series(0) });
        f.Update(VirtualPadPhase.Connecting, new[] { Series(0) });
        f.Update(VirtualPadPhase.Connected, new[] { Series(0), Real360(1) });
        Assert.Equal(1, f.VirtualSlot);
        Assert.True(f.IsExcluded(Real360(1)));
        Assert.False(f.IsExcluded(Series(0)));
    }

    [Fact]
    public void Real360PadPresentBeforehandStaysUsable()
    {
        var f = new XboxSlotFilter();
        f.Update(VirtualPadPhase.Off, new[] { Real360(0) });
        f.Update(VirtualPadPhase.Connected, new[] { Real360(0), Real360(1) });
        Assert.Equal(1, f.VirtualSlot);
        Assert.False(f.IsExcluded(Real360(0)));
        Assert.True(f.IsExcluded(Real360(1)));
    }

    [Fact]
    public void Unidentified360PadIsHeldBackWhileVirtualStarts()
    {
        var f = new XboxSlotFilter();
        f.Update(VirtualPadPhase.Off, Array.Empty<XInputSlotInfo>());
        f.Update(VirtualPadPhase.Connecting, Array.Empty<XInputSlotInfo>());
        // Two new 360-type pads at once: can't tell which is ours, so neither is read.
        f.Update(VirtualPadPhase.Connected, new[] { Real360(0), Real360(1) });
        Assert.Equal(-1, f.VirtualSlot);
        Assert.True(f.IsExcluded(Real360(0)));
        Assert.True(f.IsExcluded(Real360(1)));
        // But a Series pad is never mistaken for the virtual 360 pad.
        Assert.False(f.IsExcluded(Series(2)));
    }

    [Fact]
    public void Real360PluggedInAfterIdentificationIsUsable()
    {
        var f = new XboxSlotFilter();
        f.Update(VirtualPadPhase.Off, Array.Empty<XInputSlotInfo>());
        f.Update(VirtualPadPhase.Connected, new[] { Real360(0) });
        Assert.Equal(0, f.VirtualSlot);
        f.Update(VirtualPadPhase.Connected, new[] { Real360(0), Real360(1) });
        Assert.False(f.IsExcluded(Real360(1)));
        Assert.True(f.IsExcluded(Real360(0)));
    }

    [Fact]
    public void SwitchedOffVirtualPadStaysExcludedUntilWindowsRemovesIt()
    {
        var f = new XboxSlotFilter();
        f.Update(VirtualPadPhase.Off, new[] { Series(0) });
        f.Update(VirtualPadPhase.Connected, new[] { Series(0), Real360(1) });
        f.Update(VirtualPadPhase.Off, new[] { Series(0), Real360(1) }); // still listed for a moment
        Assert.True(f.IsExcluded(Real360(1)));
        f.Update(VirtualPadPhase.Off, new[] { Series(0) });            // gone
        f.Update(VirtualPadPhase.Off, new[] { Series(0), Real360(1) }); // a real 360 pad later takes slot 2
        Assert.False(f.IsExcluded(Real360(1)));
    }

    [Fact]
    public void VirtualPadThatSurvivesAReconnectIsStillRecognised()
    {
        var f = new XboxSlotFilter();
        f.Update(VirtualPadPhase.Off, Array.Empty<XInputSlotInfo>());
        f.Update(VirtualPadPhase.Connected, new[] { Real360(2) });
        f.Update(VirtualPadPhase.Off, new[] { Real360(2) });        // VIIPER connection dropped, device still there
        f.Update(VirtualPadPhase.Connected, new[] { Real360(2) });  // reconnected to the same device
        Assert.Equal(2, f.VirtualSlot);
        Assert.True(f.IsExcluded(Real360(2)));
    }

    [Fact]
    public void SteamVirtualPadsAreAlwaysIgnored()
    {
        var f = new XboxSlotFilter();
        var steam = new XInputSlotInfo(0, 0x28DE, 0x11FF);
        f.Update(VirtualPadPhase.Off, new[] { steam });
        Assert.True(f.IsExcluded(steam));
    }
}
