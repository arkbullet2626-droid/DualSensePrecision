using CommunityToolkit.Mvvm.ComponentModel;
using DualSensePrecision.App.Services;
using DualSensePrecision.InputEngine;
using DualSensePrecision.Profiles;

namespace DualSensePrecision.App.ViewModels;

/// <summary>
/// Edits one stick of the active profile. Every change is applied to the live pipeline immediately
/// and saved to the profile file shortly after (see AppServices.UpdateActiveProfile).
/// </summary>
public sealed partial class StickEditorViewModel : ObservableObject
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

    /// <summary>Current curve control points (copy of what's in the profile).</summary>
    public List<CurvePoint> Points { get; private set; } = new();

    /// <summary>The curve was replaced (preset picked, stick switched, reset) — the editor should reload points.</summary>
    public event Action? CurveReset;

    /// <summary>Settings were applied to the pipeline — visuals should refresh deadzone overlays.</summary>
    public event Action? SettingsApplied;

    [ObservableProperty] public partial double InnerDeadzone { get; set; }
    [ObservableProperty] public partial double OuterDeadzone { get; set; }
    [ObservableProperty] public partial double AntiDeadzone { get; set; }
    [ObservableProperty] public partial double SensitivityX { get; set; }
    [ObservableProperty] public partial double SensitivityY { get; set; }
    [ObservableProperty] public partial int DeadzoneShapeIndex { get; set; }
    [ObservableProperty] public partial int OutputShapeIndex { get; set; }
    [ObservableProperty] public partial bool InvertY { get; set; }
    [ObservableProperty] public partial int PresetIndex { get; set; }
    [ObservableProperty] public partial string ProfileName { get; set; } = "";

    public void Load(bool left)
    {
        _detached = false;
        _profileId = AppServices.ActiveProfile?.Id;
        IsLeft = left;
        var p = AppServices.ActiveProfile;
        var s = Current(p);

        _loading = true;
        InnerDeadzone = s.InnerDeadzone;
        OuterDeadzone = s.OuterDeadzone;
        AntiDeadzone = s.AntiDeadzone;
        SensitivityX = s.SensitivityX;
        SensitivityY = s.SensitivityY;
        DeadzoneShapeIndex = (int)s.DeadzoneShape;
        OutputShapeIndex = (int)s.OutputShape;
        InvertY = s.InvertY;
        int preset = Array.IndexOf(CurvePresets.All, s.CurvePreset);
        PresetIndex = preset < 0 ? 0 : preset;
        Points = s.CurvePreset == CurvePresets.Custom
            ? s.CurvePoints.Where(a => a is { Length: 2 }).Select(a => new CurvePoint(a[0], a[1])).ToList()
            : CurvePresets.Create(s.CurvePreset).Points.ToList();
        ProfileName = p?.Name ?? "No profile";
        _loading = false;

        CurveReset?.Invoke();
        SettingsApplied?.Invoke();
    }

    /// <summary>Called by the curve editor while the user drags points.</summary>
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

    /// <summary>Restores this stick (only) to the app defaults, keeping its calibration.</summary>
    public void ResetStick()
    {
        if (_detached) return;
        var keep = Current(AppServices.ActiveProfile).Calibration;
        var fresh = new StickProfile { Calibration = keep };
        AppServices.UpdateActiveProfile(p => IsLeft ? p with { LeftStick = fresh } : p with { RightStick = fresh });
        Load(IsLeft);
    }

    partial void OnInnerDeadzoneChanged(double value)
    {
        // Keep at least 5 % between inner and outer so the active range never collapses.
        if (!_loading && OuterDeadzone < value + 0.05) OuterDeadzone = Math.Min(1.0, value + 0.05);
        Push();
    }

    partial void OnOuterDeadzoneChanged(double value)
    {
        if (!_loading && value < InnerDeadzone + 0.05) InnerDeadzone = Math.Max(0, value - 0.05);
        Push();
    }

    partial void OnAntiDeadzoneChanged(double value) => Push();
    partial void OnSensitivityXChanged(double value) => Push();
    partial void OnSensitivityYChanged(double value) => Push();
    partial void OnDeadzoneShapeIndexChanged(int value) => Push();
    partial void OnOutputShapeIndexChanged(int value) => Push();
    partial void OnInvertYChanged(bool value) => Push();

    partial void OnPresetIndexChanged(int value)
    {
        if (_loading || value < 0 || value >= CurvePresets.All.Length) return;
        string name = CurvePresets.All[value];
        // Picking "Custom" keeps the current shape as the starting point; any other preset replaces it.
        if (name != CurvePresets.Custom)
        {
            Points = CurvePresets.Create(name).Points.ToList();
            CurveReset?.Invoke();
        }
        Push();
    }

    private StickProfile Current(Profile? p) => p == null ? new StickProfile() : IsLeft ? p.LeftStick : p.RightStick;

    private void Push()
    {
        if (!CanWrite) return;
        string preset = CurvePresets.All[Math.Clamp(PresetIndex, 0, CurvePresets.All.Length - 1)];
        var existing = Current(AppServices.ActiveProfile);
        var updated = existing with
        {
            InnerDeadzone = InnerDeadzone,
            OuterDeadzone = OuterDeadzone,
            AntiDeadzone = AntiDeadzone,
            SensitivityX = SensitivityX,
            SensitivityY = SensitivityY,
            DeadzoneShape = (DeadzoneShape)Math.Clamp(DeadzoneShapeIndex, 0, 1),
            OutputShape = (OutputShape)Math.Clamp(OutputShapeIndex, 0, 1),
            InvertY = InvertY,
            CurvePreset = preset,
            CurvePoints = Points.Select(p => new[] { p.X, p.Y }).ToList(),
        };
        AppServices.UpdateActiveProfile(p => IsLeft ? p with { LeftStick = updated } : p with { RightStick = updated });
        SettingsApplied?.Invoke();
    }
}
