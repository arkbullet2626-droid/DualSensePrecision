using System.Runtime.InteropServices;

namespace DualSensePrecision.HID.Xbox;

/// <summary>
/// XInput 1.4 (part of Windows 8 and later; no install needed). Two undocumented-but-stable exports are used when
/// present: XInputGetStateEx (ordinal 100, adds the Xbox/Guide button) and XInputGetCapabilitiesEx (ordinal 108,
/// adds vendor/product IDs). Both fall back cleanly if missing.
/// </summary>
internal static class XInputNative
{
    public const int ErrorSuccess = 0;
    public const int ErrorDeviceNotConnected = 1167;
    public const int MaxSlots = 4;

    [StructLayout(LayoutKind.Sequential)]
    public struct Gamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLX;
        public short ThumbLY;
        public short ThumbRX;
        public short ThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct State
    {
        public uint PacketNumber;
        public Gamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Vibration
    {
        public ushort LeftMotorSpeed;
        public ushort RightMotorSpeed;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Capabilities
    {
        public byte Type;
        public byte SubType;
        public ushort Flags;
        public Gamepad Gamepad;
        public Vibration Vibration;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CapabilitiesEx
    {
        public Capabilities Capabilities;
        public ushort VendorId;
        public ushort ProductId;
        public ushort ProductVersion;
        public ushort Unknown1;
        public uint Unknown2;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BatteryInformation
    {
        public byte BatteryType;
        public byte BatteryLevel;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern int GetStateNative(int userIndex, out State state);

    [DllImport("xinput1_4.dll", EntryPoint = "#100")]
    private static extern int GetStateExNative(int userIndex, out State state);

    [DllImport("xinput1_4.dll", EntryPoint = "XInputSetState")]
    private static extern int SetStateNative(int userIndex, ref Vibration vibration);

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetCapabilities")]
    private static extern int GetCapabilitiesNative(int userIndex, int flags, out Capabilities caps);

    [DllImport("xinput1_4.dll", EntryPoint = "#108")]
    private static extern int GetCapabilitiesExNative(int one, int userIndex, int flags, out CapabilitiesEx caps);

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetBatteryInformation")]
    private static extern int GetBatteryInformationNative(int userIndex, byte devType, out BatteryInformation info);

    private static volatile bool _noStateEx, _noCapsEx;

    /// <summary>True if XInput itself is available on this PC.</summary>
    public static bool Available
    {
        get
        {
            try { GetStateNative(0, out _); return true; }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
        }
    }

    public static int GetState(int slot, out State state)
    {
        if (!_noStateEx)
        {
            try { return GetStateExNative(slot, out state); }
            catch (EntryPointNotFoundException) { _noStateEx = true; }
        }
        return GetStateNative(slot, out state);
    }

    /// <summary>Vendor/product ID of a connected slot, or (0, 0) if Windows won't say.</summary>
    public static (ushort Vid, ushort Pid) GetIds(int slot)
    {
        if (!_noCapsEx)
        {
            try
            {
                if (GetCapabilitiesExNative(1, slot, 0, out var ex) == ErrorSuccess) return (ex.VendorId, ex.ProductId);
            }
            catch (EntryPointNotFoundException) { _noCapsEx = true; }
        }
        return (0, 0);
    }

    public static bool SetVibration(int slot, ushort left, ushort right)
    {
        var v = new Vibration { LeftMotorSpeed = left, RightMotorSpeed = right };
        try { return SetStateNative(slot, ref v) == ErrorSuccess; }
        catch (DllNotFoundException) { return false; }
    }

    public static BatteryInformation? GetBattery(int slot)
    {
        try { return GetBatteryInformationNative(slot, 0 /* gamepad */, out var b) == ErrorSuccess ? b : null; }
        catch (EntryPointNotFoundException) { return null; }
    }
}
