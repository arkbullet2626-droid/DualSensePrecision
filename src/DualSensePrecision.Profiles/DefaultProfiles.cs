using DualSensePrecision.InputEngine;

namespace DualSensePrecision.Profiles;

/// <summary>
/// Starter profiles. These are reasonable starting points, not "pro settings" — every value is editable,
/// and none of them change what the controller hardware can do.
/// </summary>
public static class DefaultProfiles
{
    public static IEnumerable<Profile> All()
    {
        yield return new Profile { Name = "Default" };

        var fpsRight = new StickProfile { InnerDeadzone = 0.04, AntiDeadzone = 0.0, CurvePreset = CurvePresets.CompetitiveFps };
        yield return new Profile
        {
            Name = "Competitive FPS",
            LeftStick = new StickProfile { InnerDeadzone = 0.06 },
            RightStick = fpsRight,
            LeftTrigger = new TriggerProfile { Mode = TriggerMode.Digital, ActivationPoint = 0.15 },
            RightTrigger = new TriggerProfile { Mode = TriggerMode.Digital, ActivationPoint = 0.15 },
        };

        yield return new Profile
        {
            Name = "Precision Aiming",
            RightStick = new StickProfile { InnerDeadzone = 0.03, CurvePreset = CurvePresets.Precision },
        };

        yield return new Profile
        {
            Name = "Racing",
            LeftStick = new StickProfile { InnerDeadzone = 0.03, DeadzoneShape = DeadzoneShape.Axial, CurvePreset = CurvePresets.Smooth },
            LeftTrigger = new TriggerProfile { Deadzone = 0.01, CurvePreset = CurvePresets.Smooth },
            RightTrigger = new TriggerProfile { Deadzone = 0.01, CurvePreset = CurvePresets.Smooth },
        };
    }

    /// <summary>Templates offered by "New profile". Same honest caveat: starting points to tune, not magic numbers.</summary>
    public static IReadOnlyList<ProfileTemplate> Templates { get; } = new ProfileTemplate[]
    {
        new("Blank", "App defaults: 5% stick deadzone, linear curves, analog triggers.", () => new Profile()),
        new("Competitive FPS", "Faster aim response and hair triggers for shooters.", () => All().First(p => p.Name == "Competitive FPS")),
        new("Precision Aiming", "Slower small movements on the right stick for fine aim.", () => All().First(p => p.Name == "Precision Aiming")),
        new("Fortnite", "Responsive aim curve and quick triggers for building and editing.", () => new Profile
        {
            LeftStick = new StickProfile { InnerDeadzone = 0.05 },
            RightStick = new StickProfile { InnerDeadzone = 0.05, CurvePreset = CurvePresets.CompetitiveFps },
            LeftTrigger = new TriggerProfile { Mode = TriggerMode.Digital, ActivationPoint = 0.2 },
            RightTrigger = new TriggerProfile { Mode = TriggerMode.Digital, ActivationPoint = 0.2 },
        }),
        new("Call of Duty", "Low right-stick deadzone, competitive curve, hair triggers.", () => new Profile
        {
            LeftStick = new StickProfile { InnerDeadzone = 0.06 },
            RightStick = new StickProfile { InnerDeadzone = 0.04, CurvePreset = CurvePresets.CompetitiveFps },
            LeftTrigger = new TriggerProfile { Mode = TriggerMode.Digital, ActivationPoint = 0.15 },
            RightTrigger = new TriggerProfile { Mode = TriggerMode.Digital, ActivationPoint = 0.15 },
        }),
        new("Rust", "Precision curve for long-range aim; analog triggers kept.", () => new Profile
        {
            LeftStick = new StickProfile { InnerDeadzone = 0.06 },
            RightStick = new StickProfile { InnerDeadzone = 0.05, CurvePreset = CurvePresets.Precision },
        }),
        new("ARC Raiders", "Smooth aim curve for third-person tracking.", () => new Profile
        {
            LeftStick = new StickProfile { InnerDeadzone = 0.05 },
            RightStick = new StickProfile { InnerDeadzone = 0.05, CurvePreset = CurvePresets.Smooth },
        }),
        new("Rocket League", "Small deadzone and square output so diagonals reach full steer; analog throttle.", () => new Profile
        {
            LeftStick = new StickProfile { InnerDeadzone = 0.03, OutputShape = OutputShape.Square },
            RightStick = new StickProfile { InnerDeadzone = 0.05 },
            LeftTrigger = new TriggerProfile { Deadzone = 0.02 },
            RightTrigger = new TriggerProfile { Deadzone = 0.02 },
        }),
        new("Racing", "Per-axis steering deadzone and smooth throttle and brake.", () => All().First(p => p.Name == "Racing")),
    };
}

/// <summary>A starter for "New profile". <see cref="Create"/> returns fresh settings; the caller sets the name.</summary>
public sealed record ProfileTemplate(string Name, string Description, Func<Profile> Create);
