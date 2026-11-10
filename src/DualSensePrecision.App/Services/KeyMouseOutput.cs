using System.Runtime.InteropServices;
using DualSensePrecision.Core;
using DualSensePrecision.InputEngine;

namespace DualSensePrecision.App.Services;

/// <summary>
/// Turns DualSense buttons mapped to keyboard keys or mouse buttons into real Windows input (SendInput).
/// Runs on the HID reader thread and only does work when a mapped button changes state.
/// Remembers exactly what it pressed, so releases always match — even if the shift layer or the
/// profile changes while a button is held.
/// </summary>
public sealed class KeyMouseOutput
{
    private readonly Dictionary<DsButtons, ButtonMapping> _held = new();
    private readonly object _lock = new();
    private DsButtons _previous;

    public string? LastError { get; private set; }

    public void Update(DsButtons external, ButtonMapper mapper)
    {
        if (external == _previous) return;
        lock (_lock)
        {
            DsButtons changed = external ^ _previous;
            _previous = external;
            foreach (var b in ButtonMapper.MappableButtons)
            {
                if ((changed & b) == 0) continue;
                if ((external & b) != 0)
                {
                    var m = mapper.Resolve(b);
                    bool usable = m.Kind == MappingKind.Mouse || (m.Kind == MappingKind.Keyboard && m.VirtualKey != 0);
                    if (usable && _held.TryAdd(b, m)) Send(m, down: true);
                }
                else if (_held.Remove(b, out var pressed))
                {
                    Send(pressed, down: false);
                }
            }
        }
    }

    /// <summary>Releases every key/mouse button this class is holding (controller unplugged, app closing).</summary>
    public void ReleaseAll()
    {
        lock (_lock)
        {
            foreach (var m in _held.Values) Send(m, down: false);
            _held.Clear();
            _previous = DsButtons.None;
        }
    }

    /// <summary>Presses or releases one key (used by macros). Returns false if Windows rejected it.</summary>
    public static bool SendKey(ushort vk, bool down)
    {
        Span<INPUT> one = stackalloc INPUT[1];
        one[0] = Key(vk, down);
        return Dispatch(one) == 1;
    }

    /// <summary>Presses or releases one mouse button (used by macros).</summary>
    public static bool SendMouse(MouseButtonTarget b, bool down)
    {
        Span<INPUT> one = stackalloc INPUT[1];
        one[0] = Mouse(b, down);
        return Dispatch(one) == 1;
    }

    private static unsafe uint Dispatch(Span<INPUT> inputs)
    {
        fixed (INPUT* p = inputs) return SendInput((uint)inputs.Length, p, sizeof(INPUT));
    }

    private void Send(ButtonMapping m, bool down)
    {
        Span<INPUT> inputs = stackalloc INPUT[5];
        int n = 0;

        if (m.Kind == MappingKind.Keyboard)
        {
            // Modifiers go down before the key and come up after it.
            if (down) AddModifiers(inputs, ref n, m.Modifiers, true);
            inputs[n++] = Key(m.VirtualKey, down);
            if (!down) AddModifiers(inputs, ref n, m.Modifiers, false);
        }
        else
        {
            inputs[n++] = Mouse(m.MouseButton, down);
        }

        uint sent = Dispatch(inputs[..n]);
        LastError = sent == n ? null
            : "Windows blocked simulated keyboard/mouse input (this happens when an app running as administrator is in front).";
    }

    private static void AddModifiers(Span<INPUT> inputs, ref int n, KeyModifiers mods, bool down)
    {
        if ((mods & KeyModifiers.Ctrl) != 0) inputs[n++] = Key(0x11, down);
        if ((mods & KeyModifiers.Shift) != 0) inputs[n++] = Key(0x10, down);
        if ((mods & KeyModifiers.Alt) != 0) inputs[n++] = Key(0x12, down);
        if ((mods & KeyModifiers.Win) != 0) inputs[n++] = Key(0x5B, down);
    }

    // Keys that need the "extended" flag when sent as scan codes.
    private static bool IsExtended(ushort vk) => vk is
        0x21 or 0x22 or 0x23 or 0x24 or          // Page Up/Down, End, Home
        0x25 or 0x26 or 0x27 or 0x28 or          // arrows
        0x2D or 0x2E or                          // Insert, Delete
        0x5B or 0x5C or 0x5D or                  // Win keys, Apps
        0x6F or 0x90 or 0xA3 or 0xA5;            // Numpad /, Num Lock, right Ctrl, right Alt

    private static INPUT Key(ushort vk, bool down)
    {
        // Scan codes are what most games read (DirectInput / Raw Input), so send those.
        ushort scan = (ushort)MapVirtualKey(vk, 0);
        uint flags = KEYEVENTF_SCANCODE;
        if (IsExtended(vk)) flags |= KEYEVENTF_EXTENDEDKEY;
        if (!down) flags |= KEYEVENTF_KEYUP;
        if (scan == 0) flags &= ~KEYEVENTF_SCANCODE; // no scan code for this key: fall back to the virtual key
        return new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags } } };
    }

    private static INPUT Mouse(MouseButtonTarget b, bool down)
    {
        (uint flags, uint data) = b switch
        {
            MouseButtonTarget.Left => (down ? 0x0002u : 0x0004u, 0u),
            MouseButtonTarget.Right => (down ? 0x0008u : 0x0010u, 0u),
            MouseButtonTarget.Middle => (down ? 0x0020u : 0x0040u, 0u),
            MouseButtonTarget.X1 => (down ? 0x0080u : 0x0100u, 1u),
            _ => (down ? 0x0080u : 0x0100u, 2u),
        };
        return new INPUT { type = INPUT_MOUSE, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = flags, mouseData = data } } };
    }

    // ---------- Win32 ----------
    private const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2, KEYEVENTF_SCANCODE = 0x8;

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion u; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern unsafe uint SendInput(uint count, INPUT* inputs, int size);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);
}
