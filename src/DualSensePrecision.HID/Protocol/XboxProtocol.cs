using DualSensePrecision.Core;

namespace DualSensePrecision.HID.Protocol;

/// <summary>
/// Pure translation between XInput's gamepad state and the app's controller model. No Windows calls here,
/// so it's unit-tested on any OS.
///
/// Button layout follows position, not label: A (bottom face) = Cross, B = Circle, X = Square, Y = Triangle,
/// LB/RB = L1/R1, LT/RT = L2/R2, View = Create, Menu = Options, Xbox = PS. So every profile, mapping and macro
/// works unchanged on either controller.
/// </summary>
public static class XboxProtocol
{
    // XINPUT_GAMEPAD button bits
    public const ushort DpadUp = 0x0001, DpadDown = 0x0002, DpadLeft = 0x0004, DpadRight = 0x0008;
    public const ushort Start = 0x0010, Back = 0x0020, LeftThumb = 0x0040, RightThumb = 0x0080;
    public const ushort LeftShoulder = 0x0100, RightShoulder = 0x0200, Guide = 0x0400;
    public const ushort A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000;

    /// <summary>XINPUT_GAMEPAD_TRIGGER_THRESHOLD: the "pressed" point Microsoft documents for the triggers.</summary>
    public const byte TriggerThreshold = 30;

    public const ushort MicrosoftVendorId = 0x045E;
    public const ushort Xbox360WiredProductId = 0x028E; // also what the VIIPER virtual pad reports
    public const ushort ValveVendorId = 0x28DE;         // Steam Input's virtual pads

    /// <summary>Size of the raw XInput state shown in the raw monitor: packet number (4) + XINPUT_GAMEPAD (12).</summary>
    public const int RawLength = 16;

    public static DsButtons MapButtons(ushort x)
    {
        var b = DsButtons.None;
        if ((x & A) != 0) b |= DsButtons.Cross;
        if ((x & B) != 0) b |= DsButtons.Circle;
        if ((x & X) != 0) b |= DsButtons.Square;
        if ((x & Y) != 0) b |= DsButtons.Triangle;
        if ((x & LeftShoulder) != 0) b |= DsButtons.L1;
        if ((x & RightShoulder) != 0) b |= DsButtons.R1;
        if ((x & LeftThumb) != 0) b |= DsButtons.L3;
        if ((x & RightThumb) != 0) b |= DsButtons.R3;
        if ((x & Back) != 0) b |= DsButtons.Create;
        if ((x & Start) != 0) b |= DsButtons.Options;
        if ((x & Guide) != 0) b |= DsButtons.PS;
        if ((x & DpadUp) != 0) b |= DsButtons.DpadUp;
        if ((x & DpadDown) != 0) b |= DsButtons.DpadDown;
        if ((x & DpadLeft) != 0) b |= DsButtons.DpadLeft;
        if ((x & DpadRight) != 0) b |= DsButtons.DpadRight;
        return b;
    }

    /// <summary>The buttons an Xbox controller can report through XInput (Share on Series pads isn't exposed by XInput).</summary>
    public static readonly DsButtons[] Buttons =
    {
        DsButtons.Cross, DsButtons.Circle, DsButtons.Square, DsButtons.Triangle,
        DsButtons.L1, DsButtons.R1, DsButtons.L2, DsButtons.R2, DsButtons.L3, DsButtons.R3,
        DsButtons.DpadUp, DsButtons.DpadDown, DsButtons.DpadLeft, DsButtons.DpadRight,
        DsButtons.Create, DsButtons.Options, DsButtons.PS,
    };

    /// <summary>Full-resolution axis in [-1, 1]. XInput Y is already up-positive.</summary>
    public static float AxisToFloat(short v) => Math.Clamp(v / 32767f, -1f, 1f);

    /// <summary>8-bit axis in the DualSense convention (0..255, Y grows downward).</summary>
    public static byte AxisToByte(short v, bool isY)
    {
        int signed = isY ? Math.Min(-(int)v, short.MaxValue) : v; // flip Y; -(-32768) would overflow
        return (byte)((signed + 32768) >> 8);
    }

    public static ControllerState ToState(ushort buttons, byte lt, byte rt, short lx, short ly, short rx, short ry, long arrival)
    {
        var s = new ControllerState
        {
            LeftX = AxisToByte(lx, false), LeftY = AxisToByte(ly, true),
            RightX = AxisToByte(rx, false), RightY = AxisToByte(ry, true),
            L2 = lt, R2 = rt,
            Buttons = MapButtons(buttons),
            HasPreciseSticks = true,
            PreciseLeftX = AxisToFloat(lx), PreciseLeftY = AxisToFloat(ly),
            PreciseRightX = AxisToFloat(rx), PreciseRightY = AxisToFloat(ry),
            ArrivalTimestamp = arrival,
            Touch0 = new TouchPoint(), Touch1 = new TouchPoint(),
        };
        if (lt > TriggerThreshold) s.Buttons |= DsButtons.L2;
        if (rt > TriggerThreshold) s.Buttons |= DsButtons.R2;
        return s;
    }

    /// <summary>Raw bytes for the monitor: little-endian packet number, then XINPUT_GAMEPAD exactly as Windows returned it.</summary>
    public static void WriteRaw(Span<byte> dst, uint packet, ushort buttons, byte lt, byte rt, short lx, short ly, short rx, short ry)
    {
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(dst, packet);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(dst[4..], buttons);
        dst[6] = lt; dst[7] = rt;
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(dst[8..], lx);
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(dst[10..], ly);
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(dst[12..], rx);
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(dst[14..], ry);
    }

    // XINPUT_BATTERY_INFORMATION
    public const byte BatteryTypeDisconnected = 0x00, BatteryTypeWired = 0x01, BatteryTypeAlkaline = 0x02,
                      BatteryTypeNimh = 0x03, BatteryTypeUnknown = 0xFF;

    /// <summary>XInput gives a four-step level, not a percentage. The percent is that level's midpoint, shown as a level.</summary>
    public static (BatteryKind Kind, byte Percent) Battery(byte type, byte level) => type switch
    {
        BatteryTypeWired => (BatteryKind.Wired, (byte)100),
        BatteryTypeAlkaline or BatteryTypeNimh => (BatteryKind.Level, level switch { 0 => (byte)5, 1 => (byte)25, 2 => (byte)60, _ => (byte)100 }),
        _ => (BatteryKind.Unknown, (byte)0),
    };

    public static string BatteryLevelName(byte percent) => percent switch
    {
        <= 10 => "Empty",
        <= 30 => "Low",
        <= 70 => "Medium",
        _ => "Full",
    };

    /// <summary>XInput vibration: left = big low-frequency motor, right = small high-frequency motor. Strength 0–100 % scales both.</summary>
    public static (ushort Left, ushort Right) Vibration(byte left, byte right, int strengthPercent)
    {
        double k = Math.Clamp(strengthPercent, 0, 100) / 100.0;
        return ((ushort)Math.Round(left * 257 * k), (ushort)Math.Round(right * 257 * k));
    }

    /// <summary>A friendly name from the vendor/product ID (when Windows reports it).</summary>
    public static string ModelName(ushort vid, ushort pid)
    {
        if (vid != MicrosoftVendorId) return vid == 0 ? "Xbox controller" : "Xbox-compatible controller";
        return pid switch
        {
            0x028E or 0x028F or 0x0291 or 0x0719 or 0x02A1 => "Xbox 360 Controller",
            0x02D1 or 0x02DD or 0x02EA or 0x02FF or 0x02E0 or 0x02FD or 0x0B20 => "Xbox One Controller",
            0x02E3 or 0x0B00 or 0x0B05 or 0x0B22 => "Xbox Elite Controller",
            0x0B12 or 0x0B13 => "Xbox Series X|S Controller",
            _ => "Xbox Controller",
        };
    }
}

/// <summary>What the virtual Xbox pad is doing, as far as the slot filter needs to know.</summary>
public enum VirtualPadPhase { Off, Connecting, Connected }

public readonly record struct XInputSlotInfo(int Slot, ushort VendorId, ushort ProductId)
{
    /// <summary>The VIIPER virtual pad reports 045E:028E. If Windows didn't tell us the IDs (0:0) we can't rule it out.</summary>
    public bool CouldBeVirtualPad =>
        (VendorId == XboxProtocol.MicrosoftVendorId && ProductId == XboxProtocol.Xbox360WiredProductId) || (VendorId == 0 && ProductId == 0);
}

/// <summary>
/// Keeps the app's own virtual Xbox pad (and Steam's) from being read back as a physical controller, which would
/// make the app feed its own output into its input.
///
/// XInput gives no device path, so the virtual pad is identified by timing: slots present while the virtual pad
/// is off are real; a 360-type slot that appears after the virtual pad starts is the virtual one. Until that's
/// settled, any new 360-type slot is treated as possibly virtual and ignored. A real Xbox Series/One pad is never
/// affected (different product ID).
/// </summary>
public sealed class XboxSlotFilter
{
    private uint _knownReal;      // slot bits seen while the virtual pad was off (or confirmed not virtual)
    private int _virtualSlot = -1;
    private int _retiringSlot = -1; // the virtual pad's slot after switching off, until Windows removes it
    private VirtualPadPhase _phase;
    private readonly object _lock = new();

    public int VirtualSlot { get { lock (_lock) return _virtualSlot; } }

    public void Update(VirtualPadPhase phase, IReadOnlyList<XInputSlotInfo> connected)
    {
        lock (_lock)
        {
            uint mask = 0;
            foreach (var c in connected) mask |= 1u << c.Slot;

            if (_retiringSlot >= 0 && (mask & (1u << _retiringSlot)) == 0) _retiringSlot = -1;
            if (_virtualSlot >= 0 && (mask & (1u << _virtualSlot)) == 0) _virtualSlot = -1; // pad removed / reconnecting
            _knownReal &= mask; // an unplugged slot number may be reused by anything

            if (phase == VirtualPadPhase.Off)
            {
                if (_virtualSlot >= 0) { _retiringSlot = _virtualSlot; _virtualSlot = -1; }
                _knownReal = mask & ~(_retiringSlot >= 0 ? 1u << _retiringSlot : 0);
            }
            else if (_virtualSlot < 0)
            {
                var candidates = connected.Where(c => (_knownReal & (1u << c.Slot)) == 0 && c.CouldBeVirtualPad && c.Slot != _retiringSlot).ToList();
                if (candidates.Count == 0 && _retiringSlot >= 0)
                {
                    // The virtual pad was never removed (e.g. VIIPER reconnected to the same device): it's still ours.
                    _virtualSlot = _retiringSlot;
                    _retiringSlot = -1;
                }
                else if (candidates.Count == 1)
                {
                    _virtualSlot = candidates[0].Slot;
                    _knownReal = mask & ~(1u << _virtualSlot) & ~(_retiringSlot >= 0 ? 1u << _retiringSlot : 0);
                }
                // Several new 360-type pads at once: can't tell which is ours, so all stay excluded until one goes away.
            }
            else
            {
                // Virtual pad identified: anything else that's connected is real, including a 360 pad plugged in later.
                _knownReal = mask & ~(1u << _virtualSlot) & ~(_retiringSlot >= 0 ? 1u << _retiringSlot : 0);
            }

            // Real non-360 pads are always fine to remember.
            foreach (var c in connected)
                if (!c.CouldBeVirtualPad && c.Slot != _virtualSlot && c.Slot != _retiringSlot) _knownReal |= 1u << c.Slot;

            _phase = phase;
        }
    }

    public bool IsExcluded(XInputSlotInfo s)
    {
        lock (_lock)
        {
            if (s.VendorId == XboxProtocol.ValveVendorId) return true; // Steam Input's virtual pad
            if (s.Slot == _virtualSlot || s.Slot == _retiringSlot) return true;
            if ((_knownReal & (1u << s.Slot)) != 0) return false;
            // Not known to be real: while the virtual pad is (becoming) active, a new 360-type pad might be it.
            return _phase != VirtualPadPhase.Off && s.CouldBeVirtualPad;
        }
    }
}
