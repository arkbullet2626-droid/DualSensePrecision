using DualSensePrecision.Core;

namespace DualSensePrecision.InputEngine;

public enum MappingKind { Default, Disabled, Controller, Keyboard, Mouse, Macro }
public enum MappingBehavior { Normal, Toggle }
public enum MouseButtonTarget { Left, Right, Middle, X1, X2 }

[Flags]
public enum KeyModifiers { None = 0, Ctrl = 1, Shift = 2, Alt = 4, Win = 8 }

/// <summary>What one physical button does. Keyboard/mouse targets are executed by the output layer (Milestone 5).</summary>
public sealed record ButtonMapping
{
    public MappingKind Kind { get; init; } = MappingKind.Default;
    public DsButtons ControllerTarget { get; init; }
    public ushort VirtualKey { get; init; }
    public KeyModifiers Modifiers { get; init; }
    public MouseButtonTarget MouseButton { get; init; }
    public string? MacroId { get; init; }
    public MappingBehavior Behavior { get; init; } = MappingBehavior.Normal;

    public static ButtonMapping Default { get; } = new();
}

/// <summary>
/// Base layer plus one optional shift layer activated by holding a chosen button.
/// Holds per-button toggle state, so one instance belongs to one reader thread.
/// </summary>
public sealed class ButtonMapper
{
    public static readonly DsButtons[] MappableButtons =
    {
        DsButtons.Cross, DsButtons.Circle, DsButtons.Square, DsButtons.Triangle,
        DsButtons.L1, DsButtons.R1, DsButtons.L2, DsButtons.R2, DsButtons.L3, DsButtons.R3,
        DsButtons.DpadUp, DsButtons.DpadDown, DsButtons.DpadLeft, DsButtons.DpadRight,
        DsButtons.Create, DsButtons.Options, DsButtons.Touchpad, DsButtons.PS, DsButtons.Mute,
        DsButtons.FnLeft, DsButtons.FnRight, DsButtons.BackLeft, DsButtons.BackRight,
    };

    private DsButtons _previous;
    private DsButtons _toggled;

    public IReadOnlyDictionary<DsButtons, ButtonMapping> BaseLayer { get; }
    public IReadOnlyDictionary<DsButtons, ButtonMapping> ShiftLayer { get; }
    public DsButtons ShiftButton { get; }

    public ButtonMapper(IReadOnlyDictionary<DsButtons, ButtonMapping>? baseLayer = null,
                        IReadOnlyDictionary<DsButtons, ButtonMapping>? shiftLayer = null,
                        DsButtons shiftButton = DsButtons.None)
    {
        BaseLayer = baseLayer ?? new Dictionary<DsButtons, ButtonMapping>();
        ShiftLayer = shiftLayer ?? new Dictionary<DsButtons, ButtonMapping>();
        ShiftButton = shiftButton;
    }

    /// <summary>
    /// Computes controller-button output. Keyboard/mouse/macro-bound buttons are removed from the controller
    /// output and reported in <paramref name="externalPressed"/> for the output layer.
    /// </summary>
    public DsButtons Map(DsButtons physical, out DsButtons externalPressed)
    {
        externalPressed = DsButtons.None;
        bool shift = ShiftButton != DsButtons.None && (physical & ShiftButton) != 0;
        LastShiftActive = shift;
        var layer = shift ? ShiftLayer : BaseLayer;
        DsButtons rising = physical & ~_previous;
        _previous = physical;

        DsButtons output = DsButtons.None;
        DsButtons forced = DsButtons.None;
        // A trigger whose button is remapped (or is the shift key) must not also leak its analog value.
        DsButtons suppressed = ShiftButton & (DsButtons.L2 | DsButtons.R2);
        foreach (var b in MappableButtons)
        {
            var m = layer.TryGetValue(b, out var mm) ? mm : ButtonMapping.Default;
            if (b is DsButtons.L2 or DsButtons.R2 && m.Kind != MappingKind.Default) suppressed |= b;

            if (shift && b == ShiftButton) continue; // the shift key itself is consumed
            bool down = (physical & b) != 0;

            if (m.Behavior == MappingBehavior.Toggle)
            {
                if ((rising & b) != 0) _toggled ^= b;
                down = (_toggled & b) != 0;
            }
            if (!down) continue;

            switch (m.Kind)
            {
                case MappingKind.Default: output |= b; break;
                case MappingKind.Controller:
                    output |= m.ControllerTarget;
                    forced |= m.ControllerTarget & (DsButtons.L2 | DsButtons.R2);
                    break;
                case MappingKind.Disabled: break;
                default: externalPressed |= b; break;
            }
        }
        ForcedTriggers = forced;
        SuppressedTriggers = suppressed & ~forced;
        return output;
    }

    /// <summary>True if the shift layer was used for the most recent <see cref="Map"/> call.</summary>
    public bool LastShiftActive { get; private set; }

    /// <summary>L2/R2 bits that another button was remapped to: the analog trigger should read 100 %.</summary>
    public DsButtons ForcedTriggers { get; private set; }

    /// <summary>L2/R2 whose own analog value must be zeroed (remapped elsewhere, disabled, or used as shift).</summary>
    public DsButtons SuppressedTriggers { get; private set; }

    /// <summary>The mapping a button currently resolves to, in the layer used by the last Map call.</summary>
    public ButtonMapping Resolve(DsButtons button) =>
        (LastShiftActive ? ShiftLayer : BaseLayer).TryGetValue(button, out var m) ? m : ButtonMapping.Default;

    public void ResetState() { _previous = DsButtons.None; _toggled = DsButtons.None; }
}
