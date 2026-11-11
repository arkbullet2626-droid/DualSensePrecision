using System.Runtime.InteropServices;

namespace DualSensePrecision.Core;

/// <summary>Every digital input on a DualSense / DualSense Edge.</summary>
[Flags]
public enum DsButtons : uint
{
    None = 0,
    Square = 1u << 0,
    Cross = 1u << 1,
    Circle = 1u << 2,
    Triangle = 1u << 3,
    L1 = 1u << 4,
    R1 = 1u << 5,
    L2 = 1u << 6,   // digital "pressed" bit reported by firmware
    R2 = 1u << 7,
    Create = 1u << 8,
    Options = 1u << 9,
    L3 = 1u << 10,
    R3 = 1u << 11,
    PS = 1u << 12,
    Touchpad = 1u << 13,
    Mute = 1u << 14,
    DpadUp = 1u << 15,
    DpadRight = 1u << 16,
    DpadDown = 1u << 17,
    DpadLeft = 1u << 18,
    // DualSense Edge only
    FnLeft = 1u << 19,
    FnRight = 1u << 20,
    BackLeft = 1u << 21,
    BackRight = 1u << 22,
}

public enum DualSenseModel { Unknown, DualSense, DualSenseEdge }

/// <summary>Which kind of physical controller is delivering input.</summary>
public enum ControllerFamily { DualSense, Xbox }

/// <summary>How precise the battery reading is. DualSense reports a percentage; XInput only reports a coarse level.</summary>
public enum BatteryKind : byte { Percent, Level, Wired, Unknown }

public enum ChargingState : byte { Discharging, Charging, Full, Error, Unknown }

[StructLayout(LayoutKind.Sequential)]
public struct TouchPoint
{
    public bool Active;
    public byte Id;
    public ushort X; // 0..1919
    public ushort Y; // 0..1079
}

/// <summary>
/// One parsed input report. A value type so the hot path does not allocate.
/// Stick axes are raw bytes (0..255, 128 ≈ centre, Y grows downward) exactly as the controller sent them.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct ControllerState
{
    public byte LeftX, LeftY, RightX, RightY;
    public byte L2, R2;
    public byte Sequence;
    public DsButtons Buttons;

    public short GyroPitch, GyroYaw, GyroRoll;
    public short AccelX, AccelY, AccelZ;
    public uint SensorTimestamp;

    public TouchPoint Touch0, Touch1;

    public byte BatteryPercent;      // 0..100 (for BatteryKind.Level an approximate midpoint of the level)
    public ChargingState Charging;
    public BatteryKind BatteryKind;  // Percent for DualSense

    /// <summary>
    /// Set by sources whose sticks have more than 8 bits of resolution (Xbox: 16-bit). The byte fields above are
    /// still filled (rounded) so byte-based views keep working; processing uses the precise values.
    /// </summary>
    public bool HasPreciseSticks;
    public float PreciseLeftX, PreciseLeftY, PreciseRightX, PreciseRightY; // [-1, 1], Y up positive

    /// <summary>QueryPerformanceCounter ticks (Stopwatch.GetTimestamp) taken when the report arrived.</summary>
    public long ArrivalTimestamp;

    public readonly bool IsPressed(DsButtons b) => (Buttons & b) == b;

    /// <summary>Left/right stick normalised to [-1, 1], Y up positive, at the controller's full resolution.</summary>
    public readonly float LX => HasPreciseSticks ? PreciseLeftX : NormalizeAxis(LeftX);
    public readonly float LY => HasPreciseSticks ? PreciseLeftY : -NormalizeAxis(LeftY);
    public readonly float RX => HasPreciseSticks ? PreciseRightX : NormalizeAxis(RightX);
    public readonly float RY => HasPreciseSticks ? PreciseRightY : -NormalizeAxis(RightY);

    /// <summary>Stick value normalised to [-1, 1], Y up positive.</summary>
    public static float NormalizeAxis(byte raw) => Math.Clamp((raw - 127.5f) / 127.5f, -1f, 1f);
}

/// <summary>Implementation status shown next to every major feature (spec §15).</summary>
public enum FeatureStatus { Complete, InProgress, Blocked, UnsupportedByHardware, Planned }
