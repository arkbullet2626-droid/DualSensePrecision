using DualSensePrecision.App.Services;
using DualSensePrecision.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DualSensePrecision.App.Views;

public sealed partial class SettingsPage : Page
{
    private bool _loading = true;

    public SettingsPage()
    {
        InitializeComponent();
        var s = AppServices.Settings;
        AutoDetect.IsOn = s.AutomaticControllerDetection;
        Mock.IsOn = s.UseMockController;
        Animations.IsOn = s.Animations;
        Preferred.SelectedIndex = s.PreferredController == ControllerFamily.Xbox ? 1 : 0;
        var v = typeof(SettingsPage).Assembly.GetName().Version;
        VersionText.Text = $"DualSense Precision {v?.Major}.{v?.Minor}.{v?.Build} · Opifex Studios · data in %LOCALAPPDATA%\\DualSensePrecision";
        _loading = false;
    }

    private void OnPreferredChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || Preferred.SelectedIndex < 0) return;
        var family = Preferred.SelectedIndex == 1 ? ControllerFamily.Xbox : ControllerFamily.DualSense;
        try
        {
            AppServices.SaveSettings(AppServices.Settings with { PreferredController = family });
            AppServices.Controller.SetPreferred(family); // takes effect immediately if both are plugged in
            SavedText.Text = "Saved.";
        }
        catch (Exception ex) { SavedText.Text = $"Could not save settings: {ex.Message}"; }
    }

    private void OnChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var old = AppServices.Settings;
        var updated = old with
        {
            AutomaticControllerDetection = AutoDetect.IsOn,
            UseMockController = Mock.IsOn,
            Animations = Animations.IsOn,
        };

        try
        {
            AppServices.SaveSettings(updated);
            SavedText.Text = "Saved.";
        }
        catch (Exception ex)
        {
            SavedText.Text = $"Could not save settings: {ex.Message}";
            return;
        }

        // Switching real ↔ simulated, or turning detection on/off, restarts the controller service.
        bool restart = old.UseMockController != updated.UseMockController
                    || old.AutomaticControllerDetection != updated.AutomaticControllerDetection;
        if (!restart) return;

        if (updated.AutomaticControllerDetection || updated.UseMockController)
            AppServices.Controller.Start(updated);
        else
            AppServices.Controller.Stop();
    }
}
