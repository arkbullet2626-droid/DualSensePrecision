using DualSensePrecision.App.Services;
using DualSensePrecision.App.ViewModels;
using DualSensePrecision.Core;
using DualSensePrecision.InputEngine;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace DualSensePrecision.App.Views;

public sealed partial class TriggerSettingsPage : Page
{
    private UiSampler? _sampler;

    public TriggerEditorViewModel ViewModel { get; } = new();

    public TriggerSettingsPage()
    {
        InitializeComponent();
        ViewModel.CurveReset += () => Curve.SetPoints(ViewModel.Points);
        ViewModel.SettingsApplied += () => Meter.SetMarkers(CurrentSettings());
        Curve.PointsEdited += pts => ViewModel.SetCustomCurve(pts);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        ViewModel.Load(left: true);
        bool xbox = DualSensePrecision.App.ViewModels.ButtonNames.IsXbox;
        XboxBar.IsOpen = xbox;
        AdaptiveCard.Visibility = xbox ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
        LeftTab.Text = xbox ? "LT (left)" : "L2 (left)";
        RightTab.Text = xbox ? "RT (right)" : "R2 (right)";
        AppServices.ProfileSaveFailed += OnSaveFailed;
        _sampler = new UiSampler(DispatcherQueue, TimeSpan.FromMilliseconds(16));
        _sampler.Tick += OnTick;
        _sampler.Start();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ViewModel.Detach();
        AppServices.ProfileSaveFailed -= OnSaveFailed;
        _sampler?.Stop();
        _sampler = null;
        AppServices.FlushProfileSave();
    }

    private void OnSaveFailed(string message) => DispatcherQueue.TryEnqueue(() => SaveStatus.Text = message);

    private TriggerSettings CurrentSettings()
    {
        var s = AppServices.Controller.Pipeline.Settings;
        return ViewModel.IsLeft ? s.LeftTrigger : s.RightTrigger;
    }

    private void OnTriggerSelected(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        bool left = sender.Items.IndexOf(sender.SelectedItem) <= 0;
        if (left != ViewModel.IsLeft) ViewModel.Load(left);
    }

    private void OnReset(object sender, RoutedEventArgs e) => ViewModel.ResetTrigger();

    private void OnTick()
    {
        var c = AppServices.Controller;
        if (c.Status != ConnectionStatus.Connected)
        {
            Meter.SetValues(-1, 0);
            Curve.ClearInput();
            StateText.Text = "No controller";
            if (c.Status == ConnectionStatus.Disconnected) return;
            return;
        }

        c.Pipeline.Sample(out var raw, out var proc);
        bool left = ViewModel.IsLeft;
        byte rawByte = left ? raw.L2 : raw.R2;
        double rawV = rawByte / 255.0;
        double outV = left ? proc.L2 : proc.R2;
        Meter.SetValues(rawV, outV);

        var s = CurrentSettings();
        var r = TriggerProcessor.Process(rawByte, s);
        StateText.Text = r.FullPress ? "FULL PRESS" : r.Pressed ? "PRESSED" : "released";

        double dz = Math.Clamp(s.Deadzone, 0, 0.9);
        double max = Math.Clamp(s.MaxPoint, dz + 0.01, 1.0);
        if (s.Mode == TriggerMode.Digital || rawV <= dz)
        {
            Curve.ClearInput();
        }
        else
        {
            double t = Math.Clamp((rawV - dz) / (max - dz), 0, 1);
            Curve.SetInput(t, s.Curve.Evaluate((float)t));
        }
    }
}
