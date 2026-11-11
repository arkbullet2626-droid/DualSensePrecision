using DualSensePrecision.Core;
using DualSensePrecision.HID.Protocol;

namespace DualSensePrecision.App.ViewModels;

/// <summary>Wording that depends on which controller is connected.</summary>
public static class ControllerText
{
    /// <summary>Battery as (value, detail). XInput only gives a level, so Xbox pads show "Medium", never a fake percentage.</summary>
    public static (string Value, string Detail) Battery(in ControllerState s) => s.BatteryKind switch
    {
        BatteryKind.Wired => ("Wired", "Powered by USB"),
        BatteryKind.Unknown => ("—", "Not reported by this controller"),
        BatteryKind.Level => (XboxProtocol.BatteryLevelName(s.BatteryPercent), "Level reported by Windows (no percentage)"),
        _ => ($"{s.BatteryPercent}%", s.Charging switch
        {
            ChargingState.Charging => "Charging",
            ChargingState.Full => "Fully charged",
            ChargingState.Error => "Battery error",
            _ => "On battery",
        }),
    };

    /// <summary>The note shown on DualSense-only features when an Xbox controller is in use.</summary>
    public const string NotOnXbox = "Not available on Xbox controllers — this needs DualSense hardware.";
}
