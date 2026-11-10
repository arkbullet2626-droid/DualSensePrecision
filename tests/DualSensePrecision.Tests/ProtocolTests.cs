using DualSensePrecision.Core;
using DualSensePrecision.HID.Protocol;
using Xunit;

namespace DualSensePrecision.Tests;

public class ProtocolTests
{
    private static byte[] BaseReport()
    {
        var r = new byte[64];
        r[0] = 0x01;
        r[1] = r[2] = r[3] = r[4] = 0x80;
        r[8] = 0x08;           // d-pad released
        r[33] = r[37] = 0x80;  // no touches
        return r;
    }

    [Fact]
    public void RejectsWrongReportIdOrLength()
    {
        var r = BaseReport();
        r[0] = 0x31;
        Assert.False(DualSenseProtocol.TryParseUsbInput(r, DualSenseModel.DualSense, 0, out _));
        Assert.False(DualSenseProtocol.TryParseUsbInput(new byte[10], DualSenseModel.DualSense, 0, out _));
    }

    [Fact]
    public void ParsesAxesTriggersAndSequence()
    {
        var r = BaseReport();
        r[1] = 0; r[2] = 255; r[3] = 10; r[4] = 200; r[5] = 77; r[6] = 254; r[7] = 42;
        Assert.True(DualSenseProtocol.TryParseUsbInput(r, DualSenseModel.DualSense, 1234, out var s));
        Assert.Equal(0, s.LeftX); Assert.Equal(255, s.LeftY);
        Assert.Equal(10, s.RightX); Assert.Equal(200, s.RightY);
        Assert.Equal(77, s.L2); Assert.Equal(254, s.R2);
        Assert.Equal(42, s.Sequence);
        Assert.Equal(1234, s.ArrivalTimestamp);
        Assert.Equal(DsButtons.None, s.Buttons);
    }

    [Theory]
    [InlineData(0, DsButtons.DpadUp)]
    [InlineData(1, DsButtons.DpadUp | DsButtons.DpadRight)]
    [InlineData(2, DsButtons.DpadRight)]
    [InlineData(3, DsButtons.DpadDown | DsButtons.DpadRight)]
    [InlineData(4, DsButtons.DpadDown)]
    [InlineData(5, DsButtons.DpadDown | DsButtons.DpadLeft)]
    [InlineData(6, DsButtons.DpadLeft)]
    [InlineData(7, DsButtons.DpadUp | DsButtons.DpadLeft)]
    [InlineData(8, DsButtons.None)]
    public void DecodesDpadHat(int hat, DsButtons expected) =>
        Assert.Equal(expected, DualSenseProtocol.DpadFromHat(hat));

    [Fact]
    public void DecodesEveryButtonBit()
    {
        var r = BaseReport();
        r[8] = 0xF8; r[9] = 0xFF; r[10] = 0x07;
        DualSenseProtocol.TryParseUsbInput(r, DualSenseModel.DualSense, 0, out var s);
        var expected = DsButtons.Square | DsButtons.Cross | DsButtons.Circle | DsButtons.Triangle |
                       DsButtons.L1 | DsButtons.R1 | DsButtons.L2 | DsButtons.R2 | DsButtons.Create | DsButtons.Options |
                       DsButtons.L3 | DsButtons.R3 | DsButtons.PS | DsButtons.Touchpad | DsButtons.Mute;
        Assert.Equal(expected, s.Buttons);
    }

    [Fact]
    public void EdgeButtonsOnlyDecodedForEdge()
    {
        var r = BaseReport();
        r[10] = 0xF0;
        DualSenseProtocol.TryParseUsbInput(r, DualSenseModel.DualSense, 0, out var std);
        DualSenseProtocol.TryParseUsbInput(r, DualSenseModel.DualSenseEdge, 0, out var edge);
        Assert.Equal(DsButtons.None, std.Buttons);
        Assert.Equal(DsButtons.FnLeft | DsButtons.FnRight | DsButtons.BackLeft | DsButtons.BackRight, edge.Buttons);
    }

    [Fact]
    public void ParsesMotionAsLittleEndianSigned()
    {
        var r = BaseReport();
        r[16] = 0x34; r[17] = 0x12;   // gyro pitch 0x1234
        r[18] = 0xFF; r[19] = 0xFF;   // gyro yaw -1
        r[26] = 0x00; r[27] = 0x80;   // accel Z -32768
        r[28] = 0x78; r[29] = 0x56; r[30] = 0x34; r[31] = 0x12;
        DualSenseProtocol.TryParseUsbInput(r, DualSenseModel.DualSense, 0, out var s);
        Assert.Equal(0x1234, s.GyroPitch);
        Assert.Equal(-1, s.GyroYaw);
        Assert.Equal(short.MinValue, s.AccelZ);
        Assert.Equal(0x12345678u, s.SensorTimestamp);
    }

    [Fact]
    public void ParsesTouchPoints()
    {
        var r = BaseReport();
        // contact id 5, active, X = 1919 (0x77F), Y = 1079 (0x437)
        r[33] = 0x05; r[34] = 0x7F; r[35] = 0x77; r[36] = 0x43;
        DualSenseProtocol.TryParseUsbInput(r, DualSenseModel.DualSense, 0, out var s);
        Assert.True(s.Touch0.Active);
        Assert.Equal(5, s.Touch0.Id);
        Assert.Equal(1919, s.Touch0.X);
        Assert.Equal(1079, s.Touch0.Y);
        Assert.False(s.Touch1.Active);
    }

    [Theory]
    [InlineData(0x00, 5, ChargingState.Discharging)]
    [InlineData(0x07, 75, ChargingState.Discharging)]
    [InlineData(0x0A, 100, ChargingState.Discharging)]
    [InlineData(0x13, 35, ChargingState.Charging)]
    [InlineData(0x2A, 100, ChargingState.Full)]
    [InlineData(0xF0, 5, ChargingState.Error)]
    public void ParsesBattery(byte status, int percent, ChargingState state)
    {
        var r = BaseReport();
        r[53] = status;
        DualSenseProtocol.TryParseUsbInput(r, DualSenseModel.DualSense, 0, out var s);
        Assert.Equal(percent, s.BatteryPercent);
        Assert.Equal(state, s.Charging);
    }

    [Theory]
    [InlineData(10, 11, 0)]
    [InlineData(10, 13, 2)]
    [InlineData(255, 0, 0)]
    [InlineData(254, 1, 2)]
    public void DroppedReportsHandleWraparound(byte prev, byte cur, int dropped) =>
        Assert.Equal(dropped, DualSenseProtocol.DroppedBetween(prev, cur));

    [Fact]
    public void IdentifiesSupportedDevices()
    {
        Assert.True(DualSenseProtocol.IsSupported(0x054C, 0x0CE6));
        Assert.True(DualSenseProtocol.IsSupported(0x054C, 0x0DF2));
        Assert.False(DualSenseProtocol.IsSupported(0x054C, 0x09CC)); // DualShock 4 v2
        Assert.False(DualSenseProtocol.IsSupported(0x045E, 0x0CE6));
    }

    [Fact]
    public void ParsesFirmwareInfo()
    {
        var r = new byte[64];
        r[0] = 0x20;
        System.Text.Encoding.ASCII.GetBytes("Jun 10 2023").CopyTo(r, 1);
        System.Text.Encoding.ASCII.GetBytes("12:34:56").CopyTo(r, 12);
        BitConverter.GetBytes(0x00000415u).CopyTo(r, 24);
        BitConverter.GetBytes(0x01000067u).CopyTo(r, 28);
        BitConverter.GetBytes((ushort)0x0224).CopyTo(r, 44);
        var fw = FirmwareInfo.TryParse(r)!;
        Assert.Equal("Jun 10 2023", fw.BuildDate);
        Assert.Equal("12:34:56", fw.BuildTime);
        Assert.Equal(0x01000067u, fw.FirmwareVersion);
        Assert.True(fw.SupportsImprovedRumble);
        Assert.StartsWith("2.24", fw.FirmwareDisplay);
    }

    [Fact]
    public void ParsesMac()
    {
        var r = new byte[20];
        r[0] = 0x09; r[1] = 0x66; r[2] = 0x55; r[3] = 0x44; r[4] = 0x33; r[5] = 0x22; r[6] = 0x11;
        Assert.Equal("11:22:33:44:55:66", PairingInfo.TryParseMac(r));
    }

    [Fact]
    public void BadCalibrationFallsBackToIdentity()
    {
        var r = new byte[41];
        r[0] = 0x05; // all zero ranges → zero denominators
        var cal = ImuCalibration.Parse(r);
        Assert.False(cal.IsValid);
        Assert.Equal(1.0f, cal.GyroDegPerSec(0, 1024), 3);
    }
}

public class OutputReportTests
{
    [Fact]
    public void EmptyBuilderOnlySetsReportId()
    {
        var b = new OutputReportBuilder().Build();
        Assert.Equal(48, b.Length);
        Assert.Equal(0x02, b[0]);
        Assert.All(b.Skip(1), x => Assert.Equal(0, x));
    }

    [Fact]
    public void LightbarSetsFlagAndColour()
    {
        var b = new OutputReportBuilder().Lightbar(10, 20, 30).Build();
        Assert.Equal(0x04, b[2] & 0x04);
        Assert.Equal(new byte[] { 10, 20, 30 }, b[45..48]);
    }

    [Fact]
    public void RumbleSetsMotorsAndFlags()
    {
        var b = new OutputReportBuilder().Rumble(200, 100, improvedEmulation: true).Build();
        Assert.Equal(0x03, b[1] & 0x03);
        Assert.Equal(100, b[3]);  // right
        Assert.Equal(200, b[4]);  // left
        Assert.Equal(0x04, b[39] & 0x04);
    }

    [Fact]
    public void PlayerLedsMaskedToFiveBits()
    {
        var b = new OutputReportBuilder().PlayerIndicators(0xFF, brightness: 1, instant: false).Build();
        Assert.Equal(0x1F, b[44]);
        Assert.Equal(1, b[43]);
        Assert.Equal(0x10, b[2] & 0x10);
    }

    [Fact]
    public void TriggerEffectsLandAtCorrectOffsets()
    {
        var b = new OutputReportBuilder()
            .RightTriggerEffect(TriggerEffect.Weapon(2, 5, 8))
            .LeftTriggerEffect(TriggerEffect.Off())
            .Build();
        Assert.Equal(0x0C, b[1] & 0x0C);
        Assert.Equal(TriggerEffect.ModeWeapon, b[11]);
        Assert.Equal((1 << 2) | (1 << 5), b[12] | (b[13] << 8));
        Assert.Equal(7, b[14]);
        Assert.Equal(TriggerEffect.ModeOff, b[22]);
    }

    [Fact]
    public void FeedbackEffectEncodesZonesAndForce()
    {
        var e = TriggerEffect.Feedback(position: 8, strength: 8).Bytes.ToArray();
        Assert.Equal(TriggerEffect.ModeFeedback, e[0]);
        Assert.Equal(0b11_0000_0000, e[1] | (e[2] << 8));     // zones 8 and 9 active
        uint force = (uint)(e[3] | (e[4] << 8) | (e[5] << 16) | (e[6] << 24));
        Assert.Equal((7u << 24) | (7u << 27), force);
    }

    [Fact]
    public void VibrationEffectCarriesFrequency()
    {
        var e = TriggerEffect.Vibration(0, 4, 40).Bytes.ToArray();
        Assert.Equal(TriggerEffect.ModeVibration, e[0]);
        Assert.Equal(40, e[9]);
        Assert.Equal(0x3FF, e[1] | (e[2] << 8));
    }

    [Fact]
    public void FromParametersMatchesProfileKinds()
    {
        Assert.Equal(TriggerEffect.ModeOff, TriggerEffect.FromParameters(0, 3, 6, 5, 20).Bytes[0]);
        Assert.Equal(TriggerEffect.ModeFeedback, TriggerEffect.FromParameters(1, 3, 6, 5, 20).Bytes[0]);
        Assert.Equal(TriggerEffect.ModeWeapon, TriggerEffect.FromParameters(2, 3, 6, 5, 20).Bytes[0]);
        Assert.Equal(TriggerEffect.ModeVibration, TriggerEffect.FromParameters(3, 3, 6, 5, 20).Bytes[0]);
        Assert.Equal(TriggerEffect.ModeOff, TriggerEffect.FromParameters(99, 0, 0, 0, 0).Bytes[0]);
    }

    [Fact]
    public void FullStateReportCombinesAllFlags()
    {
        var b = new OutputReportBuilder()
            .Lightbar(1, 2, 3).PlayerIndicators(0b00100).MuteLight(2).MotorPowerReduction(3, 1)
            .LeftTriggerEffect(TriggerEffect.Feedback(2, 4)).RightTriggerEffect(TriggerEffect.Off())
            .Build();
        Assert.Equal(0x0C, b[1] & 0x0C);                 // both trigger enables
        Assert.Equal(0x01 | 0x04 | 0x10 | 0x40, b[2] & (0x01 | 0x04 | 0x10 | 0x40)); // mute, lightbar, player, motor power
        Assert.Equal(2, b[9]);
        Assert.Equal((3 << 4) | 1, b[37]);
        Assert.Equal(TriggerEffect.ModeFeedback, b[22]);
        Assert.Equal(TriggerEffect.ModeOff, b[11]);
        Assert.Equal(new byte[] { 1, 2, 3 }, b[45..48]);
    }

    [Fact]
    public void EffectParametersAreClamped()
    {
        var e = TriggerEffect.Weapon(start: 0, end: 99, strength: 99).Bytes.ToArray();
        Assert.Equal((1 << 2) | (1 << 8), e[1] | (e[2] << 8));
        Assert.Equal(7, e[3]);
    }
}
