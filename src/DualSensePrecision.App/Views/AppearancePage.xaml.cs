using System.ComponentModel;
using DualSensePrecision.App.Services;
using DualSensePrecision.App.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.UI;

namespace DualSensePrecision.App.Views;

public sealed partial class AppearancePage : Page
{
    private readonly ToggleButton[] _leds = new ToggleButton[5];
    private bool _syncingPicker = true; // ignore the picker's own events until the page is fully loaded
    private UiSampler? _sampler;

    public AppearanceViewModel ViewModel { get; } = new();

    public AppearancePage()
    {
        InitializeComponent();
        BuildSwatches();
        BuildLeds();
        ViewModel.PropertyChanged += OnVmChanged;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        ViewModel.Load();
        SyncLeds();
        SyncPicker();
        UpdatePreview();
        // The picker can raise ColorChanged while it builds its template; only listen after that settles.
        Loaded += (_, _) => DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => _syncingPicker = false);
        // Surface output failures (e.g. controller rejected a report) instead of failing silently.
        _sampler = new UiSampler(DispatcherQueue, TimeSpan.FromMilliseconds(500));
        _sampler.Tick += () =>
        {
            var err = AppServices.Output.LastError;
            OutputError.Message = err ?? "";
            OutputError.IsOpen = err != null;
        };
        _sampler.Start();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _syncingPicker = true;
        ViewModel.Detach();
        ViewModel.PropertyChanged -= OnVmChanged;
        _sampler?.Stop();
        _sampler = null;
        AppServices.FlushProfileSave();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppearanceViewModel.LightbarColor) or nameof(AppearanceViewModel.Brightness))
            UpdatePreview();
        if (e.PropertyName == nameof(AppearanceViewModel.LightbarColor)) SyncPicker(); // e.g. a quick-colour swatch
    }

    /// <summary>Shows the profile's colour in the picker without that counting as an edit.</summary>
    private void SyncPicker()
    {
        if (Picker.Color.Equals(ViewModel.LightbarColor)) return;
        bool was = _syncingPicker;
        _syncingPicker = true;
        Picker.Color = ViewModel.LightbarColor;
        _syncingPicker = was;
    }

    private void OnPickerColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_syncingPicker) return;
        var c = args.NewColor;
        ViewModel.LightbarColor = Color.FromArgb(255, c.R, c.G, c.B);
    }

    private void UpdatePreview()
    {
        var c = ViewModel.LightbarColor;
        double k = Math.Clamp(ViewModel.Brightness, 0, 1);
        var shown = Color.FromArgb(255, (byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k));
        var brush = new SolidColorBrush(shown);
        PreviewLeft.Background = brush;
        PreviewRight.Background = brush;
    }

    private void BuildSwatches()
    {
        foreach (var (name, hex) in AppearanceViewModel.Swatches)
        {
            var (r, g, b) = OutputManager.ParseColor(hex);
            var btn = new Button
            {
                Width = 32,
                Height = 32,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(16),
                Background = new SolidColorBrush(Color.FromArgb(255, r, g, b)),
                BorderBrush = (Brush)Application.Current.Resources["DsStrokeBrush"],
                BorderThickness = new Thickness(2),
                Tag = hex,
            };
            ToolTipService.SetToolTip(btn, name);
            btn.Click += (s, _) => ViewModel.SetSwatch((string)((Button)s).Tag);
            SwatchPanel.Children.Add(btn);
        }
    }

    private void BuildLeds()
    {
        for (int i = 0; i < 5; i++)
        {
            var t = new ToggleButton
            {
                Width = 44,
                Height = 18,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(9),
                Tag = i,
            };
            AutomationProperties(t, $"Player light {i + 1}");
            t.Click += OnLedClicked;
            _leds[i] = t;
            LedPanel.Children.Add(t);
        }
    }

    private static void AutomationProperties(UIElement e, string name) =>
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(e, name);

    private void SyncLeds()
    {
        bool[] state = { ViewModel.Led1, ViewModel.Led2, ViewModel.Led3, ViewModel.Led4, ViewModel.Led5 };
        for (int i = 0; i < 5; i++) _leds[i].IsChecked = state[i];
    }

    private void OnLedClicked(object sender, RoutedEventArgs e)
    {
        var t = (ToggleButton)sender;
        bool on = t.IsChecked == true;
        switch ((int)t.Tag)
        {
            case 0: ViewModel.Led1 = on; break;
            case 1: ViewModel.Led2 = on; break;
            case 2: ViewModel.Led3 = on; break;
            case 3: ViewModel.Led4 = on; break;
            case 4: ViewModel.Led5 = on; break;
        }
    }

    private void OnTestRumble(object sender, RoutedEventArgs e) => ViewModel.TestRumble();

    private void OnReset(object sender, RoutedEventArgs e)
    {
        ViewModel.Reset();
        SyncLeds();
        SyncPicker();
        UpdatePreview();
    }
}
