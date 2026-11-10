using CommunityToolkit.Mvvm.ComponentModel;
using DualSensePrecision.App.Services;
using DualSensePrecision.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace DualSensePrecision.App.Views;

[INotifyPropertyChanged]
public sealed partial class HomePage : Page
{
    private UiSampler? _sampler;
    private int _tick;

    [ObservableProperty] public partial string StatusLabel { get; set; } = "NOT CONNECTED";
    [ObservableProperty] public partial Brush StatusBrush { get; set; }
    [ObservableProperty] public partial string Headline { get; set; } = "Connect your DualSense";
    [ObservableProperty] public partial string Detail { get; set; } = "";
    [ObservableProperty] public partial string Rate { get; set; } = "—";
    [ObservableProperty] public partial string Battery { get; set; } = "—";
    [ObservableProperty] public partial string Charging { get; set; } = "";
    [ObservableProperty] public partial string ProfileName { get; set; } = "—";
    [ObservableProperty] public partial bool IsMock { get; set; }
    [ObservableProperty] public partial string VirtualStatus { get; set; } = "Off";
    [ObservableProperty] public partial string VirtualHint { get; set; } = "Games read the DualSense directly";

    public HomePage()
    {
        StatusBrush = GetBrush("DsTextTertiaryBrush");
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        AppServices.Controller.StatusChanged += OnStatus;
        RefreshStatus();
        _sampler = new UiSampler(DispatcherQueue, TimeSpan.FromMilliseconds(16));
        _sampler.Tick += OnTick;
        _sampler.Start();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        AppServices.Controller.StatusChanged -= OnStatus;
        _sampler?.Stop();
        _sampler = null;
    }

    private void OnStatus(ConnectionStatus _) => DispatcherQueue.TryEnqueue(RefreshStatus);

    private void RefreshStatus()
    {
        var c = AppServices.Controller;
        IsMock = c.IsMock;
        RefreshVirtual();
        ProfileName = AppServices.ActiveProfile?.Name ?? "None";
        switch (c.Status)
        {
            case ConnectionStatus.Connected:
                StatusLabel = c.IsMock ? "SIMULATED" : "CONNECTED · USB";
                StatusBrush = GetBrush(c.IsMock ? "DsWarningBrush" : "DsSuccessBrush");
                Headline = c.Info?.Name ?? "DualSense";
                Detail = c.Info?.FirmwareVersion is { } fw ? $"Firmware {fw}" : "Reading input";
                break;
            case ConnectionStatus.Connecting:
                StatusLabel = "CONNECTING";
                StatusBrush = GetBrush("DsWarningBrush");
                Headline = "Found a DualSense";
                Detail = "Reading controller information…";
                break;
            case ConnectionStatus.Error:
                StatusLabel = "PROBLEM";
                StatusBrush = GetBrush("DsDangerBrush");
                Headline = "Couldn't talk to the controller";
                Detail = c.LastError ?? "Unknown error. Try unplugging and reconnecting.";
                break;
            default:
                StatusLabel = "NOT CONNECTED";
                StatusBrush = GetBrush("DsTextTertiaryBrush");
                Headline = "Connect your DualSense";
                Detail = "Plug it in with a USB-C data cable. It's detected automatically.";
                Rate = Battery = "—";
                Charging = "";
                break;
        }
    }

    private void OnTick()
    {
        var c = AppServices.Controller;
        if (c.Status != ConnectionStatus.Connected) { PlaceDot(LeftDot, 0, 0); PlaceDot(RightDot, 0, 0); return; }

        c.Pipeline.Sample(out var raw, out var processed);
        PlaceDot(LeftDot, processed.LeftX, processed.LeftY);
        PlaceDot(RightDot, processed.RightX, processed.RightY);

        if (++_tick % 15 != 0) return; // text at ~4 Hz
        RefreshVirtual();
        var stats = c.Telemetry?.Snapshot();
        Rate = stats is { HasData: true } s ? $"{s.MeasuredRateHz:0}" : "—";
        Battery = $"{raw.BatteryPercent}%";
        Charging = raw.Charging switch
        {
            ChargingState.Charging => "Charging",
            ChargingState.Full => "Fully charged",
            ChargingState.Error => "Battery error",
            _ => "On battery",
        };
    }

    private void RefreshVirtual()
    {
        var v = AppServices.Viiper;
        (VirtualStatus, VirtualHint) = v?.State switch
        {
            null or DualSensePrecision.VirtualController.VirtualState.Off => ("Off", "Games read the DualSense directly"),
            DualSensePrecision.VirtualController.VirtualState.Connected => ("Xbox 360", "Games get your tuned settings"),
            DualSensePrecision.VirtualController.VirtualState.Connecting => ("Starting", "Connecting to VIIPER"),
            _ => ("Problem", "Open Virtual Controller for details"),
        };
    }

    /// <summary>Processed stick position (Y up) inside a 96 px ring, 12 px dot.</summary>
    private static void PlaceDot(FrameworkElement dot, float x, float y)
    {
        Canvas.SetLeft(dot, 42 + x * 42);
        Canvas.SetTop(dot, 42 - y * 42);
    }

    private void OnOpenController(object sender, RoutedEventArgs e) => MainWindow.Current?.NavigateTo("Controller");
    private void OnOpenPerformance(object sender, RoutedEventArgs e) => MainWindow.Current?.NavigateTo("Performance");

    private static Brush GetBrush(string key) => (Brush)Application.Current.Resources[key];
}
