using System.Runtime.InteropServices;

namespace DualSensePrecision.App.Services;

/// <summary>
/// Reads what Windows' XInput sees — the API most PC games use for Xbox controllers.
/// Used to prove end to end that the virtual controller exists and carries the processed values.
/// The DualSense itself is not an XInput device, so any pad listed here is the virtual one (or a real Xbox pad).
/// </summary>
public static class XInputProbe
{
    [StructLayout(LayoutKind.Sequential)]
    public struct XInputGamepad
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
    private struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern int XInputGetState(int userIndex, out XInputState state);

    /// <summary>Returns (slot, gamepad) for every connected XInput controller.</summary>
    public static List<(int Slot, XInputGamepad Pad)> Connected()
    {
        var list = new List<(int, XInputGamepad)>();
        try
        {
            for (int i = 0; i < 4; i++)
                if (XInputGetState(i, out var s) == 0) list.Add((i, s.Gamepad));
        }
        catch (DllNotFoundException) { /* xinput1_4 ships with Windows 8+; nothing to report otherwise */ }
        return list;
    }
}
