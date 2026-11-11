using DualSensePrecision.Core;
using DualSensePrecision.InputEngine;

namespace DualSensePrecision.App.ViewModels;

/// <summary>Human-readable names for buttons, keys and mappings.</summary>
public static class ButtonNames
{
    /// <summary>The connected controller's family (DualSense when nothing is connected), so names match the buttons in your hands.</summary>
    public static ControllerFamily CurrentFamily =>
        Services.AppServices.Controller.Status == ConnectionStatus.Connected ? Services.AppServices.Controller.Family : ControllerFamily.DualSense;

    public static bool IsXbox => CurrentFamily == ControllerFamily.Xbox;

    public static string Name(DsButtons b) => Name(b, CurrentFamily);

    public static string Name(DsButtons b, ControllerFamily family) => family == ControllerFamily.Xbox ? XboxName(b) : DualSenseName(b);

    /// <summary>Xbox labels for the same physical positions (A = Cross's spot, View = Create, …).</summary>
    public static string XboxName(DsButtons b) => b switch
    {
        DsButtons.Cross => "A",
        DsButtons.Circle => "B",
        DsButtons.Square => "X",
        DsButtons.Triangle => "Y",
        DsButtons.L1 => "LB",
        DsButtons.R1 => "RB",
        DsButtons.L2 => "LT",
        DsButtons.R2 => "RT",
        DsButtons.L3 => "Left stick click",
        DsButtons.R3 => "Right stick click",
        DsButtons.Create => "View",
        DsButtons.Options => "Menu",
        DsButtons.PS => "Xbox button",
        _ => DualSenseName(b),
    };

    public static string DualSenseName(DsButtons b) => b switch
    {
        DsButtons.Cross => "Cross ✕",
        DsButtons.Circle => "Circle ○",
        DsButtons.Square => "Square □",
        DsButtons.Triangle => "Triangle △",
        DsButtons.DpadUp => "D-pad Up",
        DsButtons.DpadDown => "D-pad Down",
        DsButtons.DpadLeft => "D-pad Left",
        DsButtons.DpadRight => "D-pad Right",
        DsButtons.Create => "Create",
        DsButtons.Options => "Options",
        DsButtons.Touchpad => "Touchpad click",
        DsButtons.PS => "PS button",
        DsButtons.Mute => "Mute",
        DsButtons.FnLeft => "Fn left",
        DsButtons.FnRight => "Fn right",
        DsButtons.BackLeft => "Back paddle left",
        DsButtons.BackRight => "Back paddle right",
        _ => b.ToString(),
    };

    /// <summary>What a DualSense button becomes on the virtual Xbox pad by default.</summary>
    public static string XboxEquivalent(DsButtons b) => b switch
    {
        DsButtons.Cross => "A",
        DsButtons.Circle => "B",
        DsButtons.Square => "X",
        DsButtons.Triangle => "Y",
        DsButtons.L1 => "LB",
        DsButtons.R1 => "RB",
        DsButtons.L2 => "LT",
        DsButtons.R2 => "RT",
        DsButtons.L3 => "LS",
        DsButtons.R3 => "RS",
        DsButtons.Options => "Start",
        DsButtons.Create or DsButtons.Touchpad => "Back",
        DsButtons.PS => "Guide",
        DsButtons.DpadUp => "D-pad Up",
        DsButtons.DpadDown => "D-pad Down",
        DsButtons.DpadLeft => "D-pad Left",
        DsButtons.DpadRight => "D-pad Right",
        _ => "nothing",
    };

    /// <summary>Short label shown next to a button on the controller drawing.</summary>
    public static string Short(ButtonMapping m) => m.Kind switch
    {
        MappingKind.Default => "",
        MappingKind.Disabled => "Off",
        MappingKind.Controller => ShortName(m.ControllerTarget),
        MappingKind.Keyboard => KeyCombo(m),
        MappingKind.Mouse => "Mouse " + m.MouseButton,
        MappingKind.Macro => "▶ " + MacroName(m.MacroId),
        _ => "?",
    } + (m.Behavior == MappingBehavior.Toggle && m.Kind != MappingKind.Disabled ? " ⟳" : "");

    public static string Describe(DsButtons source, ButtonMapping m)
    {
        bool xbox = IsXbox;
        string what = m.Kind switch
        {
            MappingKind.Default => xbox ? "Default" : $"Default (Xbox {XboxEquivalent(source)})",
            MappingKind.Disabled => "Disabled",
            MappingKind.Controller => xbox ? Name(m.ControllerTarget) : $"{Name(m.ControllerTarget)} (Xbox {XboxEquivalent(m.ControllerTarget)})",
            MappingKind.Keyboard => "Key " + KeyCombo(m),
            MappingKind.Mouse => "Mouse " + m.MouseButton + " button",
            MappingKind.Macro => "Macro: " + MacroName(m.MacroId),
            _ => "Unknown",
        };
        return m.Behavior == MappingBehavior.Toggle && m.Kind != MappingKind.Disabled ? what + " · toggle" : what;
    }

    public static string MacroName(string? id) =>
        Services.AppServices.ActiveProfile?.Macros.FirstOrDefault(x => x.Id == id)?.Name ?? "(deleted macro)";

    public static string ShortName(DsButtons b) => IsXbox ? XboxShortName(b) : b switch
    {
        DsButtons.Cross => "✕",
        DsButtons.Circle => "○",
        DsButtons.Square => "□",
        DsButtons.Triangle => "△",
        DsButtons.DpadUp => "↑",
        DsButtons.DpadDown => "↓",
        DsButtons.DpadLeft => "←",
        DsButtons.DpadRight => "→",
        DsButtons.Touchpad => "Pad",
        DsButtons.Options => "Opt",
        _ => b.ToString(),
    };

    private static string XboxShortName(DsButtons b) => b switch
    {
        DsButtons.L3 => "LS",
        DsButtons.R3 => "RS",
        DsButtons.Create => "View",
        DsButtons.Options => "Menu",
        DsButtons.PS => "Xbox",
        DsButtons.DpadUp => "↑",
        DsButtons.DpadDown => "↓",
        DsButtons.DpadLeft => "←",
        DsButtons.DpadRight => "→",
        _ => XboxName(b),
    };

    public static string KeyCombo(ButtonMapping m)
    {
        var parts = new List<string>();
        if ((m.Modifiers & KeyModifiers.Ctrl) != 0) parts.Add("Ctrl");
        if ((m.Modifiers & KeyModifiers.Shift) != 0) parts.Add("Shift");
        if ((m.Modifiers & KeyModifiers.Alt) != 0) parts.Add("Alt");
        if ((m.Modifiers & KeyModifiers.Win) != 0) parts.Add("Win");
        parts.Add(KeyName(m.VirtualKey));
        return string.Join("+", parts);
    }

    public static string KeyName(ushort vk)
    {
        if (vk == 0) return "(none)";
        if (vk is >= 0x30 and <= 0x39) return ((char)vk).ToString();         // 0-9
        if (vk is >= 0x41 and <= 0x5A) return ((char)vk).ToString();         // A-Z
        if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x6F);             // F1-F24
        if (vk is >= 0x60 and <= 0x69) return "Num " + (vk - 0x60);          // numpad digits
        return vk switch
        {
            0x08 => "Backspace", 0x09 => "Tab", 0x0D => "Enter", 0x10 => "Shift", 0x11 => "Ctrl", 0x12 => "Alt",
            0x13 => "Pause", 0x14 => "Caps Lock", 0x1B => "Esc", 0x20 => "Space",
            0x21 => "Page Up", 0x22 => "Page Down", 0x23 => "End", 0x24 => "Home",
            0x25 => "Left", 0x26 => "Up", 0x27 => "Right", 0x28 => "Down",
            0x2C => "Print Screen", 0x2D => "Insert", 0x2E => "Delete",
            0xA0 => "Left Shift", 0xA1 => "Right Shift", 0xA2 => "Left Ctrl", 0xA3 => "Right Ctrl", 0xA4 => "Left Alt", 0xA5 => "Right Alt",
            0xBA => ";", 0xBB => "=", 0xBC => ",", 0xBD => "-", 0xBE => ".", 0xBF => "/", 0xC0 => "`",
            0xDB => "[", 0xDC => "\\", 0xDD => "]", 0xDE => "'",
            _ => ((Windows.System.VirtualKey)vk).ToString(),
        };
    }
}
