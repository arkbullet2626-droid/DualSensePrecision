using DualSensePrecision.App.Services;
using DualSensePrecision.Configuration;
using DualSensePrecision.Core;
using Microsoft.UI.Xaml.Media;
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
        ScaleSlider.Value = Math.Round(s.UiScale * 100);
        ScaleValue.Text = $"{ScaleSlider.Value:0} %";
        _palette = (s.Palette ?? new ThemePalette()) with { Accent = ThemePalette.OpifexAccent, Accent2 = ThemePalette.OpifexAccent2 };
        foreach (var (name, _) in ThemePalette.Presets) PresetBox.Items.Add(name);
        PresetBox.SelectedIndex = PresetIndex(_palette);
        BuildColorRows();
        // Drag start/end on the slider (handledEventsToo: the slider's thumb handles these itself).
        ScaleSlider.AddHandler(PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(OnScalePointerPressed), true);
        ScaleSlider.AddHandler(PointerReleasedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(OnScalePointerReleased), true);
        ScaleSlider.AddHandler(PointerCaptureLostEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(OnScalePointerReleased), true);
        Unloaded += (_, _) => { if (_animating) StopAnimating(); Host?.ClearPreview(); Host?.Settle(0, 0); };
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

    // ---------------- interface scale ----------------

    private DispatcherQueueTimerHolder? _saveLater;

    // Dragging the scale slider previews the new size by zooming everything around the slider (which itself is
    // counter-scaled so it stays exactly where it is under the mouse), with eased animation. Letting go commits
    // the real layout scale and smoothly settles the page back into place.
    private bool _scaleDragging;
    private double _dragBase = 1, _k = 1, _kTarget = 1, _sliderK = 1;
    private global::Windows.Foundation.Point _anchor;
    private double _settleX, _settleY;
    private bool _animating;

    private Controls.ScaleHost? Host => MainWindow.Current?.Scaler;

    private void OnScaleChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (ScaleValue == null) return;
        ScaleValue.Text = $"{e.NewValue:0} %";
        if (_loading) return;
        if (_scaleDragging)
        {
            _kTarget = e.NewValue / 100.0 / _dragBase;
            StartAnimating();
        }
        else
        {
            CommitScale(); // keyboard arrows or the 100 % button
        }
        SaveSoon();
    }

    private void OnScalePointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (Host is not { } host) return;
        _scaleDragging = true;
        _dragBase = host.Scale;
        _k = _kTarget = 1;
        // Anchor on the slider's centre, in the scale host's coordinates (i.e. on screen inside the window).
        var t = ScaleSlider.TransformToVisual(host);
        _anchor = t.TransformPoint(new global::Windows.Foundation.Point(ScaleSlider.ActualWidth / 2, ScaleSlider.ActualHeight / 2));
    }

    private void OnScalePointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_scaleDragging) return;
        _scaleDragging = false;
        CommitScale();
    }

    /// <summary>Applies the real layout scale, keeping the slider where it was on screen, then eases into place.</summary>
    private void CommitScale()
    {
        if (Host is not { } host) return;
        double target = ScaleSlider.Value / 100.0;
        var before = ScaleSlider.TransformToVisual(host).TransformPoint(new global::Windows.Foundation.Point(ScaleSlider.ActualWidth / 2, ScaleSlider.ActualHeight / 2));

        host.ClearPreview();
        _k = _kTarget = 1;
        _sliderK = host.Scale / target; // slider currently looks this size relative to its new real size → ease to 1
        host.Scale = target;
        host.UpdateLayout();

        // Scroll so the slider ends up where it was; whatever the scroll can't fix is eased away visually.
        var after = ScaleSlider.TransformToVisual(host).TransformPoint(new global::Windows.Foundation.Point(ScaleSlider.ActualWidth / 2, ScaleSlider.ActualHeight / 2));
        double dy = after.Y - before.Y;
        double wanted = Math.Clamp(PageScroll.VerticalOffset + dy / target, 0, PageScroll.ScrollableHeight);
        PageScroll.ChangeView(null, wanted, null, disableAnimation: true);
        PageScroll.UpdateLayout();
        after = ScaleSlider.TransformToVisual(host).TransformPoint(new global::Windows.Foundation.Point(ScaleSlider.ActualWidth / 2, ScaleSlider.ActualHeight / 2));
        _settleX = before.X - after.X;
        _settleY = before.Y - after.Y;
        StartAnimating();
    }

    private void StartAnimating()
    {
        if (_animating) return;
        _animating = true;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnFrame;
    }

    private void OnFrame(object? sender, object e)
    {
        if (Host is not { } host) { StopAnimating(); return; }
        const double ease = 0.22; // fraction of the remaining distance per frame: quick but smooth

        if (_scaleDragging)
        {
            _k += (_kTarget - _k) * ease;
            host.SetPreview(_k, _anchor);
            // Counter-scale the slider so it doesn't move or resize under the mouse.
            ScaleSlider.RenderTransformOrigin = new global::Windows.Foundation.Point(0.5, 0.5);
            ScaleSlider.RenderTransform = new ScaleTransform { ScaleX = 1 / _k, ScaleY = 1 / _k };
            if (Math.Abs(_kTarget - _k) < 0.0005) { _k = _kTarget; host.SetPreview(_k, _anchor); }
            return; // keep running while dragging
        }

        // Settling after commit: the slider grows back to its natural size and the page slides into place.
        _sliderK += (1 - _sliderK) * ease;
        _settleX += (0 - _settleX) * ease;
        _settleY += (0 - _settleY) * ease;
        ScaleSlider.RenderTransform = new ScaleTransform { ScaleX = _sliderK, ScaleY = _sliderK };
        host.Settle(_settleX, _settleY);
        if (Math.Abs(1 - _sliderK) < 0.002 && Math.Abs(_settleX) < 0.5 && Math.Abs(_settleY) < 0.5)
        {
            ScaleSlider.RenderTransform = null;
            host.Settle(0, 0);
            StopAnimating();
        }
    }

    private void StopAnimating()
    {
        _animating = false;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnFrame;
    }

    private void OnScaleReset(object sender, RoutedEventArgs e) => ScaleSlider.Value = 100;

    // ---------------- colours ----------------

    private ThemePalette _palette = new();
    private readonly Dictionary<string, (Border Swatch, TextBox Hex, ColorPicker Picker)> _rows = new();
    private bool _syncing;

    private static readonly (string Slot, string Label, string Hint)[] Slots =
    {
        ("Background", "Background", "Behind everything"),
        ("Panel", "Panels and cards", "Cards, the sidebar footer, inputs"),
        ("Border", "Borders and lines", "Card edges, dividers"),
        ("Text", "Main text", "Titles and body text"),
        ("Logo", "Logo", "The DP mark in the title bar and on About"),
    };

    private static string Get(ThemePalette p, string slot) => slot switch
    {
        "Background" => p.Background, "Panel" => p.Panel, "Border" => p.Border,
        "Text" => p.Text, "Logo" => p.Logo, "Accent" => p.Accent, _ => p.Accent2,
    };

    private static ThemePalette With(ThemePalette p, string slot, string hex) => slot switch
    {
        "Background" => p with { Background = hex }, "Panel" => p with { Panel = hex }, "Border" => p with { Border = hex },
        "Text" => p with { Text = hex }, "Logo" => p with { Logo = hex }, "Accent" => p with { Accent = hex }, _ => p with { Accent2 = hex },
    };

    private static int PresetIndex(ThemePalette p)
    {
        p = p with { Accent = ThemePalette.OpifexAccent, Accent2 = ThemePalette.OpifexAccent2 }; // older saves may hold custom accents
        for (int i = 0; i < ThemePalette.Presets.Count; i++)
            if (ThemePalette.Presets[i].Palette == p) return i;
        return -1; // custom
    }

    private void BuildColorRows()
    {
        ColorRows.Children.Clear();
        _rows.Clear();
        foreach (var (slot, label, hint) in Slots)
        {
            var c = ColorMath.Parse(Get(_palette, slot), Get(ThemePalette.Opifex, slot));

            var picker = new ColorPicker
            {
                IsAlphaEnabled = false,
                IsMoreButtonVisible = true,
                IsColorChannelTextInputVisible = true,
                IsHexInputVisible = true,
                ColorSpectrumShape = ColorSpectrumShape.Box,
                Color = ThemeService.W(c),
            };
            picker.ColorChanged += (_, e) =>
            {
                if (_syncing) return;
                var hex = $"#{e.NewColor.R:X2}{e.NewColor.G:X2}{e.NewColor.B:X2}";
                SetSlot(slot, hex, fromPicker: true);
            };

            var swatch = new Border
            {
                Width = 44, Height = 26, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1),
                BorderBrush = (Brush)Application.Current.Resources["DsStrokeBrush"],
                Background = new SolidColorBrush(ThemeService.W(c)),
            };
            var swatchButton = new Button
            {
                Content = swatch, Padding = new Thickness(4),
                Flyout = new Flyout { Content = picker, Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom },
            };
            ToolTipService.SetToolTip(swatchButton, "Open the colour picker");

            var hexBox = new TextBox { Text = c.Hex, Width = 110, FontFamily = (FontFamily)Application.Current.Resources["DsMonoFont"], MaxLength = 7 };
            hexBox.KeyDown += (_, e) => { if (e.Key == global::Windows.System.VirtualKey.Enter) CommitHex(slot, hexBox); };
            hexBox.LostFocus += (_, _) => CommitHex(slot, hexBox);

            var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            labels.Children.Add(new TextBlock { Text = label, Foreground = (Brush)Application.Current.Resources["DsTextPrimaryBrush"] });
            labels.Children.Add(new TextBlock { Text = hint, FontSize = 12, Foreground = (Brush)Application.Current.Resources["DsTextTertiaryBrush"] });

            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(swatchButton, 1);
            Grid.SetColumn(hexBox, 2);
            row.Children.Add(labels);
            row.Children.Add(swatchButton);
            row.Children.Add(hexBox);
            ColorRows.Children.Add(row);
            _rows[slot] = (swatch, hexBox, picker);
        }
        UpdateContrast();
    }

    private void CommitHex(string slot, TextBox box)
    {
        if (ColorMath.TryParse(box.Text, out var c)) SetSlot(slot, c.Hex, fromPicker: false);
        else box.Text = Get(_palette, slot); // invalid → put the current colour back
    }

    private void SetSlot(string slot, string hex, bool fromPicker)
    {
        _palette = With(_palette, slot, hex);
        if (_rows.TryGetValue(slot, out var r) && ColorMath.TryParse(hex, out var c))
        {
            _syncing = true;
            ((SolidColorBrush)r.Swatch.Background).Color = ThemeService.W(c);
            r.Hex.Text = c.Hex;
            if (!fromPicker) r.Picker.Color = ThemeService.W(c);
            _syncing = false;
        }
        ApplyPalette();
    }

    private void ApplyPalette()
    {
        ThemeService.Apply(_palette);
        _loading = true;
        PresetBox.SelectedIndex = PresetIndex(_palette);
        _loading = false;
        UpdateContrast();
        SaveSoon();
    }

    private void UpdateRestartHint()
    {
        bool startedLight = Application.Current.RequestedTheme == ApplicationTheme.Light;
        RestartHint.Visibility = _palette.IsLight != startedLight ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnRestartForTheme(object sender, RoutedEventArgs e)
    {
        try { AppServices.SaveSettings(AppServices.Settings with { Palette = _palette, UiScale = ScaleSlider.Value / 100.0 }); } catch { }
        App.Restart();
    }

    private void UpdateContrast()
    {
        UpdateRestartHint();
        double ratio = ThemeService.TextContrast(_palette);
        ContrastText.Text = ratio >= 7 ? $"Text contrast {ratio:0.0}:1 — easy to read."
            : ratio >= 4.5 ? $"Text contrast {ratio:0.0}:1 — readable."
            : $"Text contrast {ratio:0.0}:1 — this may be hard to read. Try a lighter text colour or a darker background.";
        ContrastText.Foreground = (Brush)Application.Current.Resources[ratio >= 4.5 ? "DsTextSecondaryBrush" : "DsWarningBrush"];
    }

    private void OnPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || PresetBox.SelectedIndex < 0) return;
        _palette = ThemePalette.Presets[PresetBox.SelectedIndex].Palette;
        BuildColorRows();
        ApplyPalette();
    }

    private void OnColorsReset(object sender, RoutedEventArgs e)
    {
        _palette = ThemePalette.Opifex;
        BuildColorRows();
        ApplyPalette();
    }

    /// <summary>Colour dragging and scaling fire many changes; save once things settle.</summary>
    private void SaveSoon()
    {
        _saveLater ??= new DispatcherQueueTimerHolder(DispatcherQueue, () =>
        {
            try
            {
                AppServices.SaveSettings(AppServices.Settings with { Palette = _palette, UiScale = ScaleSlider.Value / 100.0 });
                SavedText.Text = "Saved.";
            }
            catch (Exception ex) { SavedText.Text = $"Could not save settings: {ex.Message}"; }
        });
        _saveLater.Restart();
    }

    private sealed class DispatcherQueueTimerHolder
    {
        private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _timer;
        public DispatcherQueueTimerHolder(Microsoft.UI.Dispatching.DispatcherQueue q, Action action)
        {
            _timer = q.CreateTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(500);
            _timer.IsRepeating = false;
            _timer.Tick += (_, _) => action();
        }
        public void Restart() { _timer.Stop(); _timer.Start(); }
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
