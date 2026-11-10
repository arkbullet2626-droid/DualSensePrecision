using CommunityToolkit.Mvvm.ComponentModel;
using DualSensePrecision.App.Services;
using DualSensePrecision.InputEngine;
using DualSensePrecision.Profiles;

namespace DualSensePrecision.App.ViewModels;

/// <summary>Edits L2 or R2 of the active profile: input shaping + the adaptive (resistance) effect.</summary>
public sealed partial class TriggerEditorViewModel : ObservableObject
{
    private bool _loading;
    private string? _profileId;   // the profile this editor was loaded from
    private bool _detached;

    /// <summary>
    /// Call when the page closes. After this, late events from its controls (e.g. a colour picker firing as it
    /// unloads) can't write into whatever profile is active by then.
    /// </summary>
    public void Detach() => _detached = true;

    /// <summary>True if edits may be written: the page is open and the active profile is still the one shown.</summary>
    private bool CanWrite => !_loading && !_detached && _profileId != null && AppServices.ActiveProfile?.Id == _profileId;

    public bool IsLeft { get; private set; } = true;
    public List<CurvePoint> Points { get; private set; } = new();

    public event Action? CurveReset;
    public event Action? SettingsApplied;

    // Input shaping
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSecondStage), nameof(ShowAnalogControls))]
    public partial int ModeIndex { get; set; }
    [ObservableProperty] public partial double Deadzone { get; set; }
    [ObservableProperty] public partial double MaxPoint { get; set; }
    [ObservableProperty] public partial double ActivationPoint { get; set; }
    [ObservableProperty] public partial double SecondStagePoint { get; set; }
    [ObservableProperty] public partial double Sensitivity { get; set; }
    [ObservableProperty] public partial int PresetIndex { get; set; }

    // Adaptive effect (sent to the controller)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEffectParams), nameof(ShowEnd), nameof(ShowFrequency), nameof(StartLabel), nameof(StrengthLabel), nameof(StartMin), nameof(StartMax), nameof(EffectDescription))]
    public partial int EffectIndex { get; set; }
    [ObservableProperty] public partial double EffectStart { get; set; }
    [ObservableProperty] public partial double EffectEnd { get; set; }
    [ObservableProperty] public partial double EffectStrength { get; set; }
    [ObservableProperty] public partial double EffectFrequency { get; set; }

    [ObservableProperty] public partial string ProfileName { get; set; } = "";

    public bool ShowSecondStage => ModeIndex == (int)TriggerMode.DualZone;
    public bool ShowAnalogControls => ModeIndex != (int)TriggerMode.Digital;

    private AdaptiveEffectKind Kind => (AdaptiveEffectKind)Math.Clamp(EffectIndex, 0, 3);
    public bool ShowEffectParams => Kind != AdaptiveEffectKind.Off;
    public bool ShowEnd => Kind == AdaptiveEffectKind.Weapon;
    public bool ShowFrequency => Kind == AdaptiveEffectKind.Vibration;
    public double StartMin => Kind == AdaptiveEffectKind.Weapon ? 2 : 0;
    public double StartMax => Kind == AdaptiveEffectKind.Weapon ? 7 : 9;
    public string StartLabel => Kind == AdaptiveEffectKind.Weapon ? "Click starts at (zone)" : "Starts at (zone)";
    public string StrengthLabel => Kind == AdaptiveEffectKind.Vibration ? "Vibration strength" : "Resistance strength";

    public string EffectDescription => Kind switch
    {
        AdaptiveEffectKind.Feedback => "Constant resistance from the start zone to the end of the pull. Zones run 0 (top) to 9 (fully pressed).",
        AdaptiveEffectKind.Weapon => "Resistance builds between the start and end zones, then gives way with a click — like a gun trigger.",
        AdaptiveEffectKind.Vibration => "The trigger buzzes from the start zone onward at the chosen frequency.",
        _ => "No resistance — the trigger feels like a normal trigger.",
    };

    public void Load(bool left)
    {
        _detached = false;
        _profileId = AppServices.ActiveProfile?.Id;
        IsLeft = left;
        var p = AppServices.ActiveProfile;
        var t = Current(p);

        _loading = true;
        ModeIndex = (int)t.Mode;
        Deadzone = t.Deadzone;
        MaxPoint = t.MaxPoint;
        ActivationPoint = t.ActivationPoint;
        SecondStagePoint = t.SecondStagePoint;
        Sensitivity = t.Sensitivity;
        int preset = Array.IndexOf(CurvePresets.All, t.CurvePreset);
        PresetIndex = preset < 0 ? 0 : preset;
        Points = t.CurvePreset == CurvePresets.Custom
            ? t.CurvePoints.Where(a => a is { Length: 2 }).Select(a => new CurvePoint(a[0], a[1])).ToList()
            : CurvePresets.Create(t.CurvePreset).Points.ToList();

        EffectIndex = (int)t.Adaptive.Effect;
        EffectStart = t.Adaptive.Start;
        EffectEnd = t.Adaptive.End;
        EffectStrength = t.Adaptive.Strength;
        EffectFrequency = t.Adaptive.Frequency;

        ProfileName = p?.Name ?? "No profile";
        _loading = false;

        CurveReset?.Invoke();
        SettingsApplied?.Invoke();
    }

    public void SetCustomCurve(IReadOnlyList<CurvePoint> points)
    {
        Points = points.ToList();
        int custom = Array.IndexOf(CurvePresets.All, CurvePresets.Custom);
        if (PresetIndex != custom)
        {
            _loading = true;
            PresetIndex = custom;
            _loading = false;
        }
        Push();
    }

    public void ResetTrigger()
    {
        if (_detached) return;
        var fresh = new TriggerProfile();
        AppServices.UpdateActiveProfile(p => IsLeft ? p with { LeftTrigger = fresh } : p with { RightTrigger = fresh });
        Load(IsLeft);
    }

    partial void OnModeIndexChanged(int value) => Push();
    partial void OnDeadzoneChanged(double value)
    {
        if (!_loading && MaxPoint < value + 0.05) MaxPoint = Math.Min(1, value + 0.05);
        Push();
    }
    partial void OnMaxPointChanged(double value)
    {
        if (!_loading && value < Deadzone + 0.05) Deadzone = Math.Max(0, value - 0.05);
        Push();
    }
    partial void OnActivationPointChanged(double value) => Push();
    partial void OnSecondStagePointChanged(double value) => Push();
    partial void OnSensitivityChanged(double value) => Push();

    partial void OnPresetIndexChanged(int value)
    {
        if (_loading || value < 0 || value >= CurvePresets.All.Length) return;
        string name = CurvePresets.All[value];
        if (name != CurvePresets.Custom)
        {
            Points = CurvePresets.Create(name).Points.ToList();
            CurveReset?.Invoke();
        }
        Push();
    }

    partial void OnEffectIndexChanged(int value)
    {
        if (_loading) return;
        // Snap parameters into the new effect's valid range so the sliders never show impossible values.
        _loading = true;
        EffectStart = Math.Clamp(EffectStart, StartMin, StartMax);
        if (Kind == AdaptiveEffectKind.Weapon) EffectEnd = Math.Clamp(EffectEnd, EffectStart + 1, 8);
        _loading = false;
        Push();
    }

    partial void OnEffectStartChanged(double value)
    {
        if (!_loading && Kind == AdaptiveEffectKind.Weapon && EffectEnd < value + 1) EffectEnd = Math.Min(8, value + 1);
        Push();
    }

    partial void OnEffectEndChanged(double value)
    {
        if (!_loading && Kind == AdaptiveEffectKind.Weapon && value < EffectStart + 1) EffectStart = Math.Max(2, value - 1);
        Push();
    }

    partial void OnEffectStrengthChanged(double value) => Push();
    partial void OnEffectFrequencyChanged(double value) => Push();

    private TriggerProfile Current(Profile? p) => p == null ? new TriggerProfile() : IsLeft ? p.LeftTrigger : p.RightTrigger;

    private void Push()
    {
        if (!CanWrite) return;
        string preset = CurvePresets.All[Math.Clamp(PresetIndex, 0, CurvePresets.All.Length - 1)];
        var existing = Current(AppServices.ActiveProfile);
        var updated = existing with
        {
            Mode = (TriggerMode)Math.Clamp(ModeIndex, 0, 2),
            Deadzone = Deadzone,
            MaxPoint = MaxPoint,
            ActivationPoint = ActivationPoint,
            SecondStagePoint = SecondStagePoint,
            Sensitivity = Sensitivity,
            CurvePreset = preset,
            CurvePoints = Points.Select(p => new[] { p.X, p.Y }).ToList(),
            Adaptive = new AdaptiveTriggerProfile
            {
                Effect = Kind,
                Start = (int)Math.Round(EffectStart),
                End = (int)Math.Round(EffectEnd),
                Strength = (int)Math.Round(EffectStrength),
                Frequency = (int)Math.Round(EffectFrequency),
            },
        };
        AppServices.UpdateActiveProfile(p => IsLeft ? p with { LeftTrigger = updated } : p with { RightTrigger = updated });
        SettingsApplied?.Invoke();
    }
}
