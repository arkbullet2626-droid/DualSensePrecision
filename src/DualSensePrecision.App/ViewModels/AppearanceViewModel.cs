using CommunityToolkit.Mvvm.ComponentModel;
using DualSensePrecision.App.Services;
using DualSensePrecision.Profiles;
using Windows.UI;

namespace DualSensePrecision.App.ViewModels;

/// <summary>Lightbar, LEDs and motor strength for the active profile. Every change is sent to the controller.</summary>
public sealed partial class AppearanceViewModel : ObservableObject
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

    [ObservableProperty] public partial Color LightbarColor { get; set; }
    [ObservableProperty] public partial double Brightness { get; set; }
    [ObservableProperty] public partial bool Led1 { get; set; }
    [ObservableProperty] public partial bool Led2 { get; set; }
    [ObservableProperty] public partial bool Led3 { get; set; }
    [ObservableProperty] public partial bool Led4 { get; set; }
    [ObservableProperty] public partial bool Led5 { get; set; }
    [ObservableProperty] public partial int LedBrightnessIndex { get; set; }
    [ObservableProperty] public partial int MuteLedIndex { get; set; }
    [ObservableProperty] public partial double RumbleIntensity { get; set; }
    [ObservableProperty] public partial double TriggerIntensity { get; set; }
    [ObservableProperty] public partial string ProfileName { get; set; } = "";

    public static readonly (string Name, string Hex)[] Swatches =
    {
        ("Blue", "#3D7BFF"), ("Cyan", "#00D1FF"), ("Green", "#2EE59D"), ("Lime", "#B6FF3B"),
        ("Yellow", "#FFD23F"), ("Orange", "#FF8A3D"), ("Red", "#FF3B4E"), ("Pink", "#FF4FB8"),
        ("Purple", "#9B5CFF"), ("White", "#FFFFFF"),
    };

    public void Load()
    {
        _detached = false;
        _profileId = AppServices.ActiveProfile?.Id;
        var p = AppServices.ActiveProfile;
        var a = p?.Appearance ?? new AppearanceProfile();
        _loading = true;
        var (r, g, b) = OutputManager.ParseColor(a.LightbarColor);
        LightbarColor = Color.FromArgb(255, r, g, b);
        Brightness = a.LightbarBrightness / 100.0;
        Led1 = (a.PlayerLedMask & 0b00001) != 0;
        Led2 = (a.PlayerLedMask & 0b00010) != 0;
        Led3 = (a.PlayerLedMask & 0b00100) != 0;
        Led4 = (a.PlayerLedMask & 0b01000) != 0;
        Led5 = (a.PlayerLedMask & 0b10000) != 0;
        LedBrightnessIndex = Math.Clamp(a.PlayerLedBrightness, 0, 2);
        MuteLedIndex = Math.Clamp(a.MuteLed, 0, 2);
        RumbleIntensity = a.RumbleIntensity / 100.0;
        TriggerIntensity = a.TriggerIntensity / 100.0;
        ProfileName = p?.Name ?? "No profile";
        _loading = false;
    }

    public void SetSwatch(string hex)
    {
        var (r, g, b) = OutputManager.ParseColor(hex);
        LightbarColor = Color.FromArgb(255, r, g, b);
    }

    public void Reset()
    {
        if (_detached) return;
        AppServices.UpdateActiveProfile(p => p with { Appearance = new AppearanceProfile() });
        Load();
    }

    public void TestRumble() => AppServices.Output.TestRumble(200, 200, 600);

    partial void OnLightbarColorChanged(Color value) => Push();
    partial void OnBrightnessChanged(double value) => Push();
    partial void OnLed1Changed(bool value) => Push();
    partial void OnLed2Changed(bool value) => Push();
    partial void OnLed3Changed(bool value) => Push();
    partial void OnLed4Changed(bool value) => Push();
    partial void OnLed5Changed(bool value) => Push();
    partial void OnLedBrightnessIndexChanged(int value) => Push();
    partial void OnMuteLedIndexChanged(int value) => Push();
    partial void OnRumbleIntensityChanged(double value) => Push();
    partial void OnTriggerIntensityChanged(double value) => Push();

    private void Push()
    {
        if (!CanWrite) return;
        int mask = (Led1 ? 1 : 0) | (Led2 ? 2 : 0) | (Led3 ? 4 : 0) | (Led4 ? 8 : 0) | (Led5 ? 16 : 0);
        var appearance = new AppearanceProfile
        {
            LightbarColor = OutputManager.ToHex(LightbarColor.R, LightbarColor.G, LightbarColor.B),
            LightbarBrightness = (int)Math.Round(Brightness * 100),
            PlayerLedMask = mask,
            PlayerLedBrightness = Math.Clamp(LedBrightnessIndex, 0, 2),
            MuteLed = Math.Clamp(MuteLedIndex, 0, 2),
            RumbleIntensity = (int)Math.Round(RumbleIntensity * 100),
            TriggerIntensity = (int)Math.Round(TriggerIntensity * 100),
        };
        AppServices.UpdateActiveProfile(p => p with { Appearance = appearance });
    }
}
