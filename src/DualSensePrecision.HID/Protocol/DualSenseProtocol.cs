using System.Buffers.Binary;
using System.Text;
using DualSensePrecision.Core;

namespace DualSensePrecision.HID.Protocol;

/// <summary>
/// DualSense USB protocol constants and the input-report parser.
/// Layout cross-checked against Linux hid-playstation.c and SDL_hidapi_ps5.c (see docs/ARCHITECTURE.md §3).
/// This file is platform-neutral so it can be unit tested anywhere.
/// </summary>
public static class DualSenseProtocol
{
    public const ushort SonyVendorId = 0x054C;
    public const ushort DualSenseProductId = 0x0CE6;
    public const ushort DualSenseEdgeProductId = 0x0DF2;

    public const byte UsbInputReportId = 0x01;
    public const byte UsbOutputReportId = 0x02;
    public const byte FeatureCalibration = 0x05;
    public const byte FeaturePairingInfo = 0x09;
    public const byte FeatureFirmwareInfo = 0x20;

    public const int UsbInputReportLength = 64;
    public const int UsbOutputReportLength = 48;   // ID + 47-byte common block
    public const int TouchpadWidth = 1920;
    public const int TouchpadHeight = 1080;

    public static DualSenseModel ModelFromProductId(ushort pid) => pid switch
    {
        DualSenseProductId => DualSenseModel.DualSense,
        DualSenseEdgeProductId => DualSenseModel.DualSenseEdge,
        _ => DualSenseModel.Unknown,
    };

    public static bool IsSupported(ushort vid, ushort pid) =>
        vid == SonyVendorId && ModelFromProductId(pid) != DualSenseModel.Unknown;

    /// <summary>
    /// Parses a USB input report (report ID at [0]). Returns false if the buffer is not a USB 0x01 report.
    /// Allocation-free.
    /// </summary>
    public static bool TryParseUsbInput(ReadOnlySpan<byte> r, DualSenseModel model, long arrivalTimestamp, out ControllerState s)
    {
        s = default;
        if (r.Length < UsbInputReportLength || r[0] != UsbInputReportId)
            return false;

        s.LeftX = r[1]; s.LeftY = r[2]; s.RightX = r[3]; s.RightY = r[4];
        s.L2 = r[5]; s.R2 = r[6];
        s.Sequence = r[7];

        byte b0 = r[8], b1 = r[9], b2 = r[10];
        DsButtons btn = DpadFromHat(b0 & 0x0F);
        if ((b0 & 0x10) != 0) btn |= DsButtons.Square;
        if ((b0 & 0x20) != 0) btn |= DsButtons.Cross;
        if ((b0 & 0x40) != 0) btn |= DsButtons.Circle;
        if ((b0 & 0x80) != 0) btn |= DsButtons.Triangle;
        if ((b1 & 0x01) != 0) btn |= DsButtons.L1;
        if ((b1 & 0x02) != 0) btn |= DsButtons.R1;
        if ((b1 & 0x04) != 0) btn |= DsButtons.L2;
        if ((b1 & 0x08) != 0) btn |= DsButtons.R2;
        if ((b1 & 0x10) != 0) btn |= DsButtons.Create;
        if ((b1 & 0x20) != 0) btn |= DsButtons.Options;
        if ((b1 & 0x40) != 0) btn |= DsButtons.L3;
        if ((b1 & 0x80) != 0) btn |= DsButtons.R3;
        if ((b2 & 0x01) != 0) btn |= DsButtons.PS;
        if ((b2 & 0x02) != 0) btn |= DsButtons.Touchpad;
        if ((b2 & 0x04) != 0) btn |= DsButtons.Mute;
        if (model == DualSenseModel.DualSenseEdge)
        {
            if ((b2 & 0x10) != 0) btn |= DsButtons.FnLeft;
            if ((b2 & 0x20) != 0) btn |= DsButtons.FnRight;
            if ((b2 & 0x40) != 0) btn |= DsButtons.BackLeft;
            if ((b2 & 0x80) != 0) btn |= DsButtons.BackRight;
        }
        s.Buttons = btn;

        s.GyroPitch = BinaryPrimitives.ReadInt16LittleEndian(r[16..]);
        s.GyroYaw = BinaryPrimitives.ReadInt16LittleEndian(r[18..]);
        s.GyroRoll = BinaryPrimitives.ReadInt16LittleEndian(r[20..]);
        s.AccelX = BinaryPrimitives.ReadInt16LittleEndian(r[22..]);
        s.AccelY = BinaryPrimitives.ReadInt16LittleEndian(r[24..]);
        s.AccelZ = BinaryPrimitives.ReadInt16LittleEndian(r[26..]);
        s.SensorTimestamp = BinaryPrimitives.ReadUInt32LittleEndian(r[28..]);

        s.Touch0 = ParseTouch(r.Slice(33, 4));
        s.Touch1 = ParseTouch(r.Slice(37, 4));

        byte status = r[53];
        int level = status & 0x0F;
        s.BatteryPercent = (byte)Math.Min(level * 10 + 5, 100);
        s.Charging = (status >> 4) switch
        {
            0x0 => ChargingState.Discharging,
            0x1 => ChargingState.Charging,
            0x2 => ChargingState.Full,
            _ => ChargingState.Error,
        };
        if (s.Charging == ChargingState.Full) s.BatteryPercent = 100;

        s.ArrivalTimestamp = arrivalTimestamp;
        return true;
    }

    public static DsButtons DpadFromHat(int hat) => hat switch
    {
        0 => DsButtons.DpadUp,
        1 => DsButtons.DpadUp | DsButtons.DpadRight,
        2 => DsButtons.DpadRight,
        3 => DsButtons.DpadDown | DsButtons.DpadRight,
        4 => DsButtons.DpadDown,
        5 => DsButtons.DpadDown | DsButtons.DpadLeft,
        6 => DsButtons.DpadLeft,
        7 => DsButtons.DpadUp | DsButtons.DpadLeft,
        _ => DsButtons.None,
    };

    private static TouchPoint ParseTouch(ReadOnlySpan<byte> p) => new()
    {
        Active = (p[0] & 0x80) == 0,
        Id = (byte)(p[0] & 0x7F),
        X = (ushort)(p[1] | ((p[2] & 0x0F) << 8)),
        Y = (ushort)((p[2] >> 4) | (p[3] << 4)),
    };

    /// <summary>Number of reports skipped between two sequence counters (0 = none). Counter wraps at 256.</summary>
    public static int DroppedBetween(byte previous, byte current) => (byte)(current - previous - 1);
}

/// <summary>Firmware info from feature report 0x20.</summary>
public sealed record FirmwareInfo(string BuildDate, string BuildTime, uint HardwareVersion, uint FirmwareVersion, ushort UpdateVersion)
{
    public string FirmwareDisplay => $"{UpdateVersion >> 8:X}.{UpdateVersion & 0xFF:X2} (fw 0x{FirmwareVersion:X8})";
    public string HardwareDisplay => $"0x{HardwareVersion:X8}";

    /// <summary>Firmware ≥ 2.24 supports improved rumble emulation (flag2 bit2).</summary>
    public bool SupportsImprovedRumble => UpdateVersion >= 0x0224;

    public static FirmwareInfo? TryParse(ReadOnlySpan<byte> r)
    {
        if (r.Length < 46 || r[0] != DualSenseProtocol.FeatureFirmwareInfo) return null;
        string date = Encoding.ASCII.GetString(r.Slice(1, 11)).TrimEnd('\0', ' ');
        string time = Encoding.ASCII.GetString(r.Slice(12, 8)).TrimEnd('\0', ' ');
        return new FirmwareInfo(
            date, time,
            BinaryPrimitives.ReadUInt32LittleEndian(r[24..]),
            BinaryPrimitives.ReadUInt32LittleEndian(r[28..]),
            BinaryPrimitives.ReadUInt16LittleEndian(r[44..]));
    }
}

public static class PairingInfo
{
    /// <summary>Controller MAC from feature report 0x09 (stored little-endian at [1..6]).</summary>
    public static string? TryParseMac(ReadOnlySpan<byte> r)
    {
        if (r.Length < 7 || r[0] != DualSenseProtocol.FeaturePairingInfo) return null;
        return $"{r[6]:X2}:{r[5]:X2}:{r[4]:X2}:{r[3]:X2}:{r[2]:X2}:{r[1]:X2}";
    }
}

/// <summary>IMU calibration from feature report 0x05, used to convert raw gyro/accel to deg/s and g.</summary>
public sealed class ImuCalibration
{
    public const int GyroResPerDegS = 1024;
    public const int AccelResPerG = 8192;

    private readonly (int bias, int numer, int denom)[] _gyro = new (int, int, int)[3];
    private readonly (int bias, int numer, int denom)[] _accel = new (int, int, int)[3];
    public bool IsValid { get; private set; }

    public static ImuCalibration Default()
    {
        var c = new ImuCalibration();
        for (int i = 0; i < 3; i++) { c._gyro[i] = (0, 1, 1); c._accel[i] = (0, 1, 1); }
        return c;
    }

    public static ImuCalibration Parse(ReadOnlySpan<byte> r)
    {
        var c = Default();
        if (r.Length < 35 || r[0] != DualSenseProtocol.FeatureCalibration) return c;

        int pitchBias = S(r, 1), yawBias = S(r, 3), rollBias = S(r, 5);
        int pitchPlus = S(r, 7), pitchMinus = S(r, 9), yawPlus = S(r, 11), yawMinus = S(r, 13), rollPlus = S(r, 15), rollMinus = S(r, 17);
        int speed2x = S(r, 19) + S(r, 21);
        c._gyro[0] = (pitchBias, speed2x * GyroResPerDegS, pitchPlus - pitchMinus);
        c._gyro[1] = (yawBias, speed2x * GyroResPerDegS, yawPlus - yawMinus);
        c._gyro[2] = (rollBias, speed2x * GyroResPerDegS, rollPlus - rollMinus);

        for (int i = 0; i < 3; i++)
        {
            int plus = S(r, 23 + i * 4), minus = S(r, 25 + i * 4);
            int range2g = plus - minus;
            c._accel[i] = (plus - range2g / 2, 2 * AccelResPerG, range2g);
        }

        bool ok = true;
        for (int i = 0; i < 3; i++) ok &= c._gyro[i].denom != 0 && c._accel[i].denom != 0;
        if (!ok) return Default(); // bad calibration blob: fall back to identity and report invalid
        c.IsValid = true;
        return c;
    }

    private static short S(ReadOnlySpan<byte> r, int offset) => BinaryPrimitives.ReadInt16LittleEndian(r[offset..]);

    /// <summary>Gyro in degrees/second (axis 0 pitch, 1 yaw, 2 roll).</summary>
    public float GyroDegPerSec(int axis, short raw)
    {
        var (bias, numer, denom) = _gyro[axis];
        if (!IsValid) return raw / (float)GyroResPerDegS;
        return (raw - bias) * (float)numer / denom / GyroResPerDegS;
    }

    /// <summary>Acceleration in g (axis 0 X, 1 Y, 2 Z).</summary>
    public float AccelG(int axis, short raw)
    {
        var (bias, numer, denom) = _accel[axis];
        if (!IsValid) return raw / (float)AccelResPerG;
        return (raw - bias) * (float)numer / denom / AccelResPerG;
    }
}
