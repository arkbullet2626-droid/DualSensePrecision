using DualSensePrecision.App.Services;
using DualSensePrecision.App.ViewModels;
using DualSensePrecision.Core;
using DualSensePrecision.InputEngine;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace DualSensePrecision.App.Views;

public sealed partial class StickSettingsPage : Page
{
    private UiSampler? _sampler;

    public StickEditorViewModel ViewModel { get; } = new();

    public StickSettingsPage()
    {
        InitializeComponent();
        ViewModel.CurveReset += () => Curve.SetPoints(ViewModel.Points);
        ViewModel.SettingsApplied += () => Visualizer.SetZones(CurrentSettings());
        Curve.PointsEdited += pts => ViewModel.SetCustomCurve(pts);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        ViewModel.Load(left: true);
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

    private StickSettings CurrentSettings()
    {
        var s = AppServices.Controller.Pipeline.Settings;
        return ViewModel.IsLeft ? s.LeftStick : s.RightStick;
    }

    private void OnStickSelected(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        bool left = sender.Items.IndexOf(sender.SelectedItem) <= 0;
        if (left != ViewModel.IsLeft) ViewModel.Load(left);
    }

    private void OnResetStick(object sender, RoutedEventArgs e) => ViewModel.ResetStick();

    private void OnTick()
    {
        var c = AppServices.Controller;
        if (c.Status != ConnectionStatus.Connected)
        {
            Visualizer.SetDots(null);
            Curve.ClearInput();
            RawText.Text = ProcessedText.Text = DeflectionText.Text = "—";
            return;
        }

        c.Pipeline.Sample(out var raw, out var proc);
        bool left = ViewModel.IsLeft;
        float rx = left ? raw.LX : raw.RX;
        float ry = left ? raw.LY : raw.RY;
        float px = left ? proc.LeftX : proc.RightX;
        float py = left ? proc.LeftY : proc.RightY;

        Visualizer.SetDots((rx, ry, px, py));
        RawText.Text = $"X {rx,7:+0.000;-0.000; 0.000}   Y {ry,7:+0.000;-0.000; 0.000}";
        ProcessedText.Text = $"X {px,7:+0.000;-0.000; 0.000}   Y {py,7:+0.000;-0.000; 0.000}";

        double outMag = Math.Min(1.0, Math.Sqrt(px * px + py * py));
        DeflectionText.Text = $"{Math.Min(1.0, Math.Sqrt(rx * rx + ry * ry)) * 100,5:0}% in  →  {outMag * 100,5:0}% out";

        // Where the stick sits on the curve, computed exactly the way StickProcessor does it.
        var s = CurrentSettings();
        var (cx, cy) = s.Calibration.Apply(rx, ry);
        if (s.InvertY) cy = -cy;
        double mag = s.DeadzoneShape == DeadzoneShape.Radial
            ? Math.Sqrt(cx * cx + cy * cy)
            : Math.Max(Math.Abs(cx), Math.Abs(cy));
        double inner = Math.Clamp(s.InnerDeadzone, 0, 0.95);
        double outer = Math.Clamp(s.OuterDeadzone, inner + 0.01, 1.0);
        if (mag <= inner)
        {
            Curve.ClearInput();
        }
        else
        {
            double t = Math.Clamp((mag - inner) / (outer - inner), 0, 1);
            Curve.SetInput(t, s.Curve.Evaluate((float)t));
        }
    }
}
