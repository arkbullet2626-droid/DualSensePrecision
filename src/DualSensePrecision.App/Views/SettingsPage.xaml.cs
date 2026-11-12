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
        CloseToTray.IsOn = s.MinimizeToTray;
        CheckUpdates.IsOn = s.CheckForUpdates;
        StartWithWindows.IsOn = StartupRegistration.IsEnabled;
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
            MinimizeToTray = CloseToTray.IsOn,
            CheckForUpdates = CheckUpdates.IsOn,
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

    private void OnStartupToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        try
        {
            StartupRegistration.Set(StartWithWindows.IsOn);
            AppServices.SaveSettings(AppServices.Settings with { LaunchOnStartup = StartWithWindows.IsOn });
            AppLog.Info($"Start with Windows: {(StartWithWindows.IsOn ? "on" : "off")}");
            SavedText.Text = "Saved.";
        }
        catch (Exception ex) { SavedText.Text = $"Couldn't change Start with Windows: {ex.Message}"; }
    }

    private void OnOpenLogs(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(DualSensePrecision.Configuration.AppPaths.LogsDir);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(DualSensePrecision.Configuration.AppPaths.LogsDir) { UseShellExecute = true });
    }

    private async void OnExport(object sender, RoutedEventArgs e) => ToolsText.Text = await ExportDiagnosticsAsync();

    /// <summary>Asks where to save, then writes the diagnostics .zip. Returns a line for the UI.</summary>
    public static async Task<string> ExportDiagnosticsAsync()
    {
        try
        {
            var picker = new global::Windows.Storage.Pickers.FileSavePicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Current!));
            picker.SuggestedFileName = $"DualSense Precision diagnostics {DateTime.Now:yyyy-MM-dd HHmm}";
            picker.FileTypeChoices.Add("Zip file", new List<string> { ".zip" });
            var file = await picker.PickSaveFileAsync();
            if (file == null) return "";
            await Task.Run(() => DiagnosticsExport.Write(file.Path));
            return $"Saved {file.Name}. Nothing was sent anywhere — share it only if you want to.";
        }
        catch (Exception ex) { return $"Couldn't export diagnostics: {ex.Message}"; }
    }

    private async void OnReset(object sender, RoutedEventArgs e)
    {
        var deleteProfiles = new CheckBox { Content = "Also delete my profiles and macros" };
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = "This puts every setting back to how it was on first install and restarts the app. " +
                   "Your real controller is un-hidden first, and Start with Windows is switched off. VIIPER and HidHide stay installed.",
        });
        body.Children.Add(deleteProfiles);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Reset DualSense Precision?",
            Content = body,
            PrimaryButtonText = "Reset and restart",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        AppLog.Info($"Reset app requested (delete profiles: {deleteProfiles.IsChecked == true})");
        try
        {
            AppServices.SaveSettings(AppServices.Settings with { HideRealController = false });
            AppServices.Hide.Reevaluate();
            StartupRegistration.Set(false);
            AppServices.Shutdown(); // un-hides, removes the virtual pad, saves nothing further
            File.Delete(DualSensePrecision.Configuration.AppPaths.SettingsFile);
            if (deleteProfiles.IsChecked == true && Directory.Exists(DualSensePrecision.Configuration.AppPaths.ProfilesDir))
                Directory.Delete(DualSensePrecision.Configuration.AppPaths.ProfilesDir, true);
            App.Restart();
        }
        catch (Exception ex) { ToolsText.Text = $"Couldn't reset: {ex.Message}"; }
    }
}
