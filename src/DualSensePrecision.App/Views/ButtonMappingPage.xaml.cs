using DualSensePrecision.App.Services;
using DualSensePrecision.App.ViewModels;
using DualSensePrecision.Core;
using DualSensePrecision.InputEngine;
using DualSensePrecision.Profiles;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace DualSensePrecision.App.Views;

/// <summary>
/// Visual remap editor. Edits a working copy of the active profile's mappings and pushes every change
/// to the live pipeline (and the profile file) immediately.
/// </summary>
public sealed partial class ButtonMappingPage : Page
{
    private sealed record Spot(DsButtons Button, double X, double Y, double W, double H, double Radius, string Text, double LabelDx, double LabelDy);

    // Centre positions on the 620×390 drawing. Label offsets put the mapping text beside each button.
    private static readonly Spot[] Spots =
    {
        new(DsButtons.L2, 150, 30, 70, 26, 8, "L2", -82, 0),
        new(DsButtons.R2, 470, 30, 70, 26, 8, "R2", 82, 0),
        new(DsButtons.L1, 150, 66, 70, 20, 6, "L1", -82, 0),
        new(DsButtons.R1, 470, 66, 70, 20, 6, "R1", 82, 0),
        new(DsButtons.Create, 210, 106, 26, 18, 6, "", 0, -20),
        new(DsButtons.Options, 410, 106, 26, 18, 6, "", 0, -20),
        new(DsButtons.Touchpad, 310, 122, 150, 66, 10, "Touchpad", 0, 22),
        new(DsButtons.DpadUp, 150, 122, 30, 30, 5, "↑", 0, -26),
        new(DsButtons.DpadDown, 150, 186, 30, 30, 5, "↓", 0, 26),
        new(DsButtons.DpadLeft, 118, 154, 30, 30, 5, "←", -46, 0),
        new(DsButtons.DpadRight, 182, 154, 30, 30, 5, "→", 0, 26),
        new(DsButtons.Triangle, 470, 118, 34, 34, 17, "△", 0, -28),
        new(DsButtons.Cross, 470, 190, 34, 34, 17, "✕", 0, 28),
        new(DsButtons.Square, 434, 154, 34, 34, 17, "□", 0, 28),
        new(DsButtons.Circle, 506, 154, 34, 34, 17, "○", 46, 0),
        new(DsButtons.Mute, 310, 202, 36, 12, 6, "", -46, 0),
        new(DsButtons.PS, 310, 230, 30, 30, 15, "PS", 0, 26),
        new(DsButtons.L3, 230, 252, 56, 56, 28, "L3", 0, 40),
        new(DsButtons.R3, 390, 252, 56, 56, 28, "R3", 0, 40),
        // DualSense Edge only
        new(DsButtons.FnLeft, 262, 300, 26, 14, 7, "Fn", 0, 18),
        new(DsButtons.FnRight, 358, 300, 26, 14, 7, "Fn", 0, 18),
        new(DsButtons.BackLeft, 196, 334, 44, 16, 8, "BL", -56, 0),
        new(DsButtons.BackRight, 424, 334, 44, 16, 8, "BR", 56, 0),
    };

    private static readonly DsButtons[] EdgeOnly = { DsButtons.FnLeft, DsButtons.FnRight, DsButtons.BackLeft, DsButtons.BackRight };
    private static readonly DsButtons[] Targets = ButtonMapper.MappableButtons.Where(b => !EdgeOnly.Contains(b)).ToArray();

    private readonly Dictionary<DsButtons, Border> _spots = new();
    private readonly Dictionary<DsButtons, TextBlock> _labels = new();
    private Dictionary<DsButtons, ButtonMapping> _base = new();
    private Dictionary<DsButtons, ButtonMapping> _shift = new();
    private DsButtons _shiftButton;
    private bool _shiftLayer;
    private DsButtons _selected = DsButtons.Cross;
    private ushort _key;
    private bool _capturing;
    private bool _loading;
    private UiSampler? _sampler;

    public ButtonMappingPage()
    {
        InitializeComponent();
        BuildDrawing();

        foreach (var t in Targets) TargetBox.Items.Add(ButtonNames.Name(t));
        ShiftBox.Items.Add("None (no shift layer)");
        foreach (var t in Targets) ShiftBox.Items.Add(ButtonNames.Name(t));
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var m = AppServices.ActiveProfile?.Mappings ?? new MappingProfile();
        _base = new Dictionary<DsButtons, ButtonMapping>(m.Base);
        _shift = new Dictionary<DsButtons, ButtonMapping>(m.Shift);
        _shiftButton = m.ShiftButton;

        bool edge = AppServices.Controller.Info?.Model == DualSenseModel.DualSenseEdge;
        foreach (var b in EdgeOnly)
        {
            _spots[b].Visibility = edge ? Visibility.Visible : Visibility.Collapsed;
            _labels[b].Visibility = edge ? Visibility.Visible : Visibility.Collapsed;
        }
        ApplyFamilyLabels();

        _loading = true;
        ShiftBox.SelectedIndex = _shiftButton == DsButtons.None ? 0 : Array.IndexOf(Targets, _shiftButton) + 1;
        _loading = false;

        SubtitleText.Text = $"Editing profile {AppServices.ActiveProfile?.Name ?? "—"}. Changes apply instantly and save automatically.";
        RefreshAll();

        _sampler = new UiSampler(DispatcherQueue, TimeSpan.FromMilliseconds(33));
        _sampler.Tick += OnTick;
        _sampler.Start();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _sampler?.Stop();
        _sampler = null;
        AppServices.FlushProfileSave();
    }

    /// <summary>Same drawing, Xbox labels when an Xbox controller is connected (it has no touchpad or mute button).</summary>
    private void ApplyFamilyLabels()
    {
        bool xbox = ButtonNames.IsXbox;
        foreach (var s in Spots)
        {
            var spot = _spots[s.Button];
            ((TextBlock)spot.Child).Text = !xbox ? s.Text : s.Button switch
            {
                DsButtons.L2 => "LT", DsButtons.R2 => "RT", DsButtons.L1 => "LB", DsButtons.R1 => "RB",
                DsButtons.Triangle => "Y", DsButtons.Cross => "A", DsButtons.Square => "X", DsButtons.Circle => "B",
                DsButtons.PS => "Xbox", DsButtons.L3 => "LS", DsButtons.R3 => "RS",
                _ => s.Text,
            };
            ToolTipService.SetToolTip(spot, ButtonNames.Name(s.Button));
            if (s.Button is DsButtons.Touchpad or DsButtons.Mute)
            {
                spot.Visibility = xbox ? Visibility.Collapsed : Visibility.Visible;
                _labels[s.Button].Visibility = spot.Visibility;
            }
        }
        if (xbox && _selected is DsButtons.Touchpad or DsButtons.Mute) _selected = DsButtons.Cross;

        _loading = true;
        int ti = TargetBox.SelectedIndex, si = ShiftBox.SelectedIndex;
        for (int i = 0; i < Targets.Length; i++)
        {
            TargetBox.Items[i] = ButtonNames.Name(Targets[i]);
            ShiftBox.Items[i + 1] = ButtonNames.Name(Targets[i]);
        }
        TargetBox.SelectedIndex = ti;
        ShiftBox.SelectedIndex = si;
        _loading = false;
    }

    private Dictionary<DsButtons, ButtonMapping> Layer => _shiftLayer ? _shift : _base;
    private ButtonMapping MappingOf(DsButtons b) => Layer.TryGetValue(b, out var m) ? m : ButtonMapping.Default;

    // ---------------- drawing ----------------

    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    private void BuildDrawing()
    {
        foreach (var s in Spots)
        {
            var border = new Border
            {
                Width = s.W,
                Height = s.H,
                CornerRadius = new CornerRadius(s.Radius),
                Background = Res("DsSurfaceBrush"),
                BorderBrush = Res("DsStrokeBrush"),
                BorderThickness = new Thickness(1.5),
                Tag = s.Button,
                Child = new TextBlock
                {
                    Text = s.Text,
                    FontSize = s.Button == DsButtons.Touchpad ? 12 : 13,
                    Foreground = Res("DsTextSecondaryBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            ToolTipService.SetToolTip(border, ButtonNames.Name(s.Button));
            border.Tapped += OnSpotTapped;
            Canvas.SetLeft(border, s.X - s.W / 2);
            Canvas.SetTop(border, s.Y - s.H / 2);
            Pad.Children.Add(border);
            _spots[s.Button] = border;

            var align = s.LabelDx < 0 ? TextAlignment.Right : s.LabelDx > 0 ? TextAlignment.Left : TextAlignment.Center;
            const double lw = 90;
            var label = new TextBlock
            {
                Width = lw,
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = Res("DsWarningBrush"),
                TextAlignment = align,
                TextTrimming = TextTrimming.CharacterEllipsis,
                IsHitTestVisible = false,
            };
            double left = align switch
            {
                TextAlignment.Right => s.X + s.LabelDx + 20 - lw,
                TextAlignment.Left => s.X + s.LabelDx - 20,
                _ => s.X - lw / 2,
            };
            Canvas.SetLeft(label, left);
            Canvas.SetTop(label, s.Y + s.LabelDy - 8);
            Pad.Children.Add(label);
            _labels[s.Button] = label;
        }
    }

    private void RefreshAll()
    {
        foreach (var (b, label) in _labels)
        {
            var m = MappingOf(b);
            label.Text = b == _shiftButton && _shiftButton != DsButtons.None ? "SHIFT" : ButtonNames.Short(m);
        }
        foreach (var (b, spot) in _spots)
        {
            bool sel = b == _selected;
            spot.BorderBrush = sel ? Res("DsTextPrimaryBrush") : Res("DsStrokeBrush");
            spot.BorderThickness = new Thickness(sel ? 2.5 : 1.5);
        }
        LoadEditor();
        RefreshSummary();
    }

    private void OnTick()
    {
        var c = AppServices.Controller;
        DsButtons pressed = DsButtons.None;
        if (c.Status == ConnectionStatus.Connected)
        {
            c.Pipeline.Sample(out var raw, out _);
            pressed = raw.Buttons;
            if (raw.L2 > 20) pressed |= DsButtons.L2;
            if (raw.R2 > 20) pressed |= DsButtons.R2;
        }
        foreach (var (b, spot) in _spots)
            spot.Background = (pressed & b) != 0 ? Res("DsAccentBrush") : Res("DsSurfaceBrush");

        // Pressing a physical button while nothing is being captured selects it — quick way to find a button.
        if (!_capturing && pressed != DsButtons.None)
        {
            var first = Spots.FirstOrDefault(s => (pressed & s.Button) != 0 && _spots[s.Button].Visibility == Visibility.Visible);
            if (first != null && first.Button != _selected && (pressed & _selected) == 0 && AutoSelectOnPress)
            {
                _selected = first.Button;
                RefreshAll();
            }
        }

        var err = c.KeyOutput.LastError;
        if (err != null) EffectNote.Text = err;
    }

    /// <summary>Selecting by pressing is handy, but not while holding the shift button to preview its layer.</summary>
    private bool AutoSelectOnPress => _shiftButton == DsButtons.None || (AppServices.Controller.Pipeline.Settings.Mapper.LastShiftActive == false);

    private void OnSpotTapped(object sender, TappedRoutedEventArgs e)
    {
        _selected = (DsButtons)((Border)sender).Tag;
        StopCapture();
        RefreshAll();
    }

    // ---------------- editor ----------------

    private void LoadEditor()
    {
        _loading = true;
        var m = MappingOf(_selected);
        SelectedTitle.Text = ButtonNames.Name(_selected) + (_shiftLayer ? " — shift layer" : "");
        SelectedDefault.Text = ButtonNames.IsXbox
            ? $"Default: {ButtonNames.Name(_selected)} · Now: {ButtonNames.Describe(_selected, m)}"
            : $"Default: Xbox {ButtonNames.XboxEquivalent(_selected)} · Now: {ButtonNames.Describe(_selected, m)}";

        bool isShiftKey = _selected == _shiftButton && _shiftButton != DsButtons.None;
        KindBox.IsEnabled = !isShiftKey;
        KindBox.SelectedIndex = m.Kind switch
        {
            MappingKind.Disabled => 1,
            MappingKind.Controller => 2,
            MappingKind.Keyboard => 3,
            MappingKind.Mouse => 4,
            MappingKind.Macro => 5,
            _ => 0,
        };
        var macros = AppServices.ActiveProfile?.Macros ?? new List<MacroDefinition>();
        MacroBox.Items.Clear();
        foreach (var mac in macros) MacroBox.Items.Add(mac.Name + (mac.Enabled ? "" : " (disabled)"));
        MacroBox.SelectedIndex = macros.FindIndex(x => x.Id == m.MacroId);
        int t = Array.IndexOf(Targets, m.ControllerTarget);
        TargetBox.SelectedIndex = t >= 0 ? t : 0;
        _key = m.VirtualKey;
        ModCtrl.IsChecked = (m.Modifiers & KeyModifiers.Ctrl) != 0;
        ModShift.IsChecked = (m.Modifiers & KeyModifiers.Shift) != 0;
        ModAlt.IsChecked = (m.Modifiers & KeyModifiers.Alt) != 0;
        ModWin.IsChecked = (m.Modifiers & KeyModifiers.Win) != 0;
        MouseBox.SelectedIndex = (int)m.MouseButton;
        ToggleSwitchBehavior.IsOn = m.Behavior == MappingBehavior.Toggle;
        _loading = false;

        UpdateEditorVisibility(isShiftKey);
    }

    private void UpdateEditorVisibility(bool isShiftKey = false)
    {
        int k = KindBox.SelectedIndex;
        TargetBox.Visibility = k == 2 ? Visibility.Visible : Visibility.Collapsed;
        KeyPanel.Visibility = k == 3 ? Visibility.Visible : Visibility.Collapsed;
        MouseBox.Visibility = k == 4 ? Visibility.Visible : Visibility.Collapsed;
        MacroBox.Visibility = k == 5 ? Visibility.Visible : Visibility.Collapsed;
        // Macros have their own play modes (once / while held / toggle), set on the Macros page.
        ToggleSwitchBehavior.Visibility = k is 1 or 5 || isShiftKey ? Visibility.Collapsed : Visibility.Visible;
        CaptureButton.Content = _capturing ? "Press any key…" : _key == 0 ? "Choose key" : $"Key: {ButtonNames.KeyName(_key)}";
        KeyHint.Text = _capturing ? "Press the key you want (click again to cancel)." : "Click, then press a key.";

        EffectNote.Text = isShiftKey
            ? "This button is the shift button. Change it in the Shift layer box below."
            : k switch
            {
                2 => "Controller remaps reach games through the Virtual Controller. Turn it on there if it's off.",
                3 or 4 => "Keyboard and mouse output works in any app, with or without the virtual controller. Some games' anti-cheat ignores simulated keyboard/mouse input.",
                1 => "This button will do nothing.",
                5 => (AppServices.ActiveProfile?.Macros.Count ?? 0) == 0
                    ? "You don't have any macros yet — create one on the Macros page."
                    : "How the macro plays (once, while held, or toggle) is set on the Macros page.",
                _ => "",
            };
    }

    private void OnEditorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (ReferenceEquals(sender, KindBox)) UpdateEditorVisibility();
        Commit();
    }

    private void OnEditorClick(object sender, RoutedEventArgs e) { if (!_loading) Commit(); }
    private void OnEditorToggled(object sender, RoutedEventArgs e) { if (!_loading) Commit(); }

    private void Commit()
    {
        var kind = KindBox.SelectedIndex switch
        {
            1 => MappingKind.Disabled,
            2 => MappingKind.Controller,
            3 => MappingKind.Keyboard,
            4 => MappingKind.Mouse,
            5 => MappingKind.Macro,
            _ => MappingKind.Default,
        };
        var macroList = AppServices.ActiveProfile?.Macros ?? new List<MacroDefinition>();
        string? macroId = MacroBox.SelectedIndex >= 0 && MacroBox.SelectedIndex < macroList.Count ? macroList[MacroBox.SelectedIndex].Id : null;
        var mods = KeyModifiers.None;
        if (ModCtrl.IsChecked == true) mods |= KeyModifiers.Ctrl;
        if (ModShift.IsChecked == true) mods |= KeyModifiers.Shift;
        if (ModAlt.IsChecked == true) mods |= KeyModifiers.Alt;
        if (ModWin.IsChecked == true) mods |= KeyModifiers.Win;

        var m = new ButtonMapping
        {
            Kind = kind,
            ControllerTarget = Targets[Math.Clamp(TargetBox.SelectedIndex, 0, Targets.Length - 1)],
            VirtualKey = _key,
            Modifiers = mods,
            MouseButton = (MouseButtonTarget)Math.Clamp(MouseBox.SelectedIndex, 0, 4),
            MacroId = kind == MappingKind.Macro ? macroId : null,
            Behavior = ToggleSwitchBehavior.IsOn && kind is not (MappingKind.Disabled or MappingKind.Macro) ? MappingBehavior.Toggle : MappingBehavior.Normal,
        };

        if (m.Kind == MappingKind.Default && m.Behavior == MappingBehavior.Normal) Layer.Remove(_selected);
        else Layer[_selected] = m;
        Push();
    }

    private void Push()
    {
        var mappings = new MappingProfile
        {
            Base = new Dictionary<DsButtons, ButtonMapping>(_base),
            Shift = new Dictionary<DsButtons, ButtonMapping>(_shift),
            ShiftButton = _shiftButton,
        };
        AppServices.UpdateActiveProfile(p => p with { Mappings = mappings });
        RefreshAll();
    }

    private void RefreshSummary()
    {
        SummaryTitle.Text = _shiftLayer ? "Changed buttons — shift layer" : "Changed buttons — normal layer";
        var lines = Layer.OrderBy(kv => Array.IndexOf(ButtonMapper.MappableButtons, kv.Key))
                         .Select(kv => $"{ButtonNames.Name(kv.Key),-18} → {ButtonNames.Describe(kv.Key, kv.Value)}")
                         .ToList();
        if (_shiftButton != DsButtons.None) lines.Insert(0, $"{ButtonNames.Name(_shiftButton),-18} → hold for shift layer");
        SummaryText.Text = lines.Count == 0 ? "Everything is default." : string.Join("\n", lines);
    }

    // ---------------- key capture ----------------

    private void OnCaptureClick(object sender, RoutedEventArgs e)
    {
        _capturing = !_capturing;
        UpdateEditorVisibility();
    }

    private void StopCapture()
    {
        if (!_capturing) return;
        _capturing = false;
        UpdateEditorVisibility();
    }

    /// <summary>Tunnelling handler: grabs the key before the focused button can react to Space/Enter.</summary>
    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_capturing) return;
        _key = (ushort)e.Key;
        _capturing = false;
        e.Handled = true;
        Commit();
    }

    // ---------------- layers ----------------

    private void OnLayerSelected(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        _shiftLayer = sender.Items.IndexOf(sender.SelectedItem) == 1;
        StopCapture();
        if (_spots.Count > 0) RefreshAll();
    }

    private void OnShiftChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        int i = ShiftBox.SelectedIndex;
        _shiftButton = i <= 0 ? DsButtons.None : Targets[i - 1];
        Push();
    }

    private void OnResetButton(object sender, RoutedEventArgs e)
    {
        StopCapture();
        Layer.Remove(_selected);
        Push();
    }

    private void OnResetLayer(object sender, RoutedEventArgs e)
    {
        StopCapture();
        Layer.Clear();
        Push();
    }
}
