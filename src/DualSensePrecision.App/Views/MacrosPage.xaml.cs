using System.Text.Json;
using DualSensePrecision.App.Services;
using DualSensePrecision.App.ViewModels;
using DualSensePrecision.Configuration;
using DualSensePrecision.Core;
using DualSensePrecision.InputEngine;
using DualSensePrecision.Profiles;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace DualSensePrecision.App.Views;

public sealed partial class MacrosPage : Page
{
    private static readonly DsButtons[] ButtonChoices =
        ButtonMapper.MappableButtons.Where(b => b is not (DsButtons.FnLeft or DsButtons.FnRight or DsButtons.BackLeft or DsButtons.BackRight)).ToArray();

    private static readonly string[] KindNames = { "Press button", "Release button", "Press key", "Release key", "Press mouse", "Release mouse", "Wait" };
    private static readonly string[] MouseNames = { "Left", "Right", "Middle", "Back (X1)", "Forward (X2)" };

    private string? _selectedId;
    private List<MacroStep> _steps = new();
    private bool _loading;
    private int _captureIndex = -1;
    private bool _recording;
    private UiSampler? _sampler;
    private DispatcherTimer? _countdown;

    public MacrosPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        SubtitleText.Text = $"Macros for profile {AppServices.ActiveProfile?.Name ?? "—"}. Assign one to a button on the Button Mapping page.";
        _selectedId = Macros.FirstOrDefault()?.Id;
        RefreshList();
        LoadEditor();
        AppServices.Macros.RunningChanged += OnRunningChanged;
        _sampler = new UiSampler(DispatcherQueue, TimeSpan.FromMilliseconds(100));
        _sampler.Tick += OnTick;
        _sampler.Start();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        AppServices.Macros.RunningChanged -= OnRunningChanged;
        if (_recording) FinishRecording();
        // The Play-now countdown keeps running so you can switch to another page (e.g. Virtual Controller) to watch.
        _sampler?.Stop();
        _sampler = null;
        AppServices.FlushProfileSave();
    }

    private static List<MacroDefinition> Macros => AppServices.ActiveProfile?.Macros ?? new List<MacroDefinition>();
    private MacroDefinition? Selected => Macros.FirstOrDefault(m => m.Id == _selectedId);

    private void OnRunningChanged() => DispatcherQueue.TryEnqueue(UpdateRunningText);

    private void UpdateRunningText()
    {
        int n = AppServices.Macros.RunningCount;
        RunningText.Text = n == 0 ? "No macros running." : $"{n} macro{(n == 1 ? "" : "s")} running.";
    }

    // ---------------- list ----------------

    private void RefreshList()
    {
        _loading = true;
        var list = Macros;
        MacroList.Items.Clear();
        foreach (var m in list)
            MacroList.Items.Add($"{m.Name}{(m.Enabled ? "" : "  (off)")}   · {m.Steps.Count} steps");
        MacroList.SelectedIndex = list.FindIndex(m => m.Id == _selectedId);
        EmptyText.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EditorCard.Visibility = list.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        _loading = false;
        UpdateRunningText();
    }

    private void OnMacroSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || MacroList.SelectedIndex < 0) return;
        if (_recording) FinishRecording();
        _captureIndex = -1;
        _selectedId = Macros[MacroList.SelectedIndex].Id;
        LoadEditor();
    }

    private void SaveMacros(List<MacroDefinition> macros)
    {
        AppServices.UpdateActiveProfile(p => p with { Macros = macros });
    }

    /// <summary>Replaces the selected macro with an edited copy.</summary>
    private void SaveSelected(Func<MacroDefinition, MacroDefinition> change, bool refreshList = true)
    {
        var list = Macros.ToList();
        int i = list.FindIndex(m => m.Id == _selectedId);
        if (i < 0) return;
        list[i] = MacroLimits.Sanitize(change(list[i]));
        SaveMacros(list);
        if (refreshList) RefreshList();
        RefreshSummary();
    }

    private void SaveSteps() => SaveSelected(m => m with { Steps = _steps.ToList() });

    private string UniqueName(string baseName)
    {
        var names = Macros.Select(m => m.Name).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        if (!names.Contains(baseName)) return baseName;
        for (int i = 2; ; i++) if (!names.Contains($"{baseName} {i}")) return $"{baseName} {i}";
    }

    private void OnNew(object sender, RoutedEventArgs e)
    {
        var m = new MacroDefinition
        {
            Name = UniqueName("New macro"),
            Steps = new()
            {
                new(MacroStepKind.ButtonDown, DsButtons.Cross),
                new(MacroStepKind.Delay, DelayMs: 50),
                new(MacroStepKind.ButtonUp, DsButtons.Cross),
            },
        };
        SaveMacros(Macros.Append(m).ToList());
        _selectedId = m.Id;
        RefreshList();
        LoadEditor();
    }

    private void OnDuplicate(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } s) return;
        var copy = s with { Id = Guid.NewGuid().ToString("N"), Name = UniqueName(s.Name + " copy"), Steps = s.Steps.ToList() };
        SaveMacros(Macros.Append(copy).ToList());
        _selectedId = copy.Id;
        RefreshList();
        LoadEditor();
    }

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } s) return;
        var assigned = AssignedButtons(s.Id);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Delete \"{s.Name}\"?",
            Content = assigned.Count == 0
                ? "This can't be undone."
                : $"It's assigned to {string.Join(", ", assigned)}. Those buttons will go back to their default action. This can't be undone.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        AppServices.Macros.StopAll("Macro deleted");
        string id = s.Id;
        AppServices.UpdateActiveProfile(p =>
        {
            Dictionary<DsButtons, ButtonMapping> Clean(Dictionary<DsButtons, ButtonMapping> d) =>
                d.Where(kv => !(kv.Value.Kind == MappingKind.Macro && kv.Value.MacroId == id)).ToDictionary(kv => kv.Key, kv => kv.Value);
            return p with
            {
                Macros = p.Macros.Where(m => m.Id != id).ToList(),
                Mappings = p.Mappings with { Base = Clean(p.Mappings.Base), Shift = Clean(p.Mappings.Shift) },
            };
        });
        _selectedId = Macros.FirstOrDefault()?.Id;
        RefreshList();
        LoadEditor();
    }

    private List<string> AssignedButtons(string id)
    {
        var p = AppServices.ActiveProfile;
        if (p == null) return new();
        var result = new List<string>();
        foreach (var kv in p.Mappings.Base) if (kv.Value.Kind == MappingKind.Macro && kv.Value.MacroId == id) result.Add(ButtonNames.Name(kv.Key));
        foreach (var kv in p.Mappings.Shift) if (kv.Value.Kind == MappingKind.Macro && kv.Value.MacroId == id) result.Add(ButtonNames.Name(kv.Key) + " (shift layer)");
        return result;
    }

    // ---------------- import / export ----------------

    private async void OnExport(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } s) return;
        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Current!));
            picker.FileTypeChoices.Add("DualSense Precision macro", new List<string> { ".json" });
            picker.SuggestedFileName = s.Name;
            var file = await picker.PickSaveFileAsync();
            if (file == null) return;
            await Windows.Storage.FileIO.WriteTextAsync(file, JsonSerializer.Serialize(s, Json.Options));
            StatusText.Text = "";
            PlayText.Text = $"Exported to {file.Path}";
        }
        catch (Exception ex) { StatusText.Text = $"Export failed: {ex.Message}"; }
    }

    private async void OnImport(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Current!));
            picker.FileTypeFilter.Add(".json");
            var file = await picker.PickSingleFileAsync();
            if (file == null) return;
            string text = await Windows.Storage.FileIO.ReadTextAsync(file);
            var m = JsonSerializer.Deserialize<MacroDefinition>(text, Json.Options)
                    ?? throw new InvalidDataException("The file is empty.");
            if (m.Steps == null) throw new InvalidDataException("This isn't a DualSense Precision macro file.");
            // Always imported as a new macro: never overwrites an existing one.
            m = MacroLimits.Sanitize(m with { Id = Guid.NewGuid().ToString("N"), Name = UniqueName(m.Name) });
            SaveMacros(Macros.Append(m).ToList());
            _selectedId = m.Id;
            RefreshList();
            LoadEditor();
            StatusText.Text = "";
        }
        catch (Exception ex) { StatusText.Text = $"Import failed: {ex.Message}"; }
    }

    // ---------------- editor ----------------

    private void LoadEditor()
    {
        var m = Selected;
        if (m == null) return;
        _loading = true;
        NameBox.Text = m.Name;
        EnabledSwitch.IsOn = m.Enabled;
        ModeBox.SelectedIndex = (int)m.PlayMode;
        _steps = m.Steps.ToList();
        _loading = false;
        StatusText.Text = "";
        RebuildSteps();
        RefreshSummary();
        UpdateRecordUi();
    }

    private void OnNameChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        string name = NameBox.Text;
        if (string.IsNullOrWhiteSpace(name)) return;
        SaveSelected(m => m with { Name = name });
    }

    private void OnEnabledToggled(object sender, RoutedEventArgs e)
    {
        if (!_loading) SaveSelected(m => m with { Enabled = EnabledSwitch.IsOn });
    }

    private void OnModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && ModeBox.SelectedIndex >= 0) SaveSelected(m => m with { PlayMode = (MacroPlayMode)ModeBox.SelectedIndex });
    }

    private void RefreshSummary()
    {
        var m = Selected;
        if (m == null) return;
        int total = MacroLimits.TotalDelayMs(m with { Steps = _steps });
        TotalsText.Text = $"{_steps.Count} / {MacroLimits.MaxSteps} steps · {total} ms";
        WarningsText.Text = string.Join("\n", MacroLimits.Warnings(m with { Steps = _steps }).Select(w => "• " + w));
        var assigned = AssignedButtons(m.Id);
        AssignedText.Text = assigned.Count == 0
            ? "Not assigned to a button yet. On Button Mapping, pick a button → Action: Run a macro."
            : $"Assigned to: {string.Join(", ", assigned)}.";
    }

    // ---------------- step rows ----------------

    private void RebuildSteps()
    {
        StepsPanel.Children.Clear();
        for (int i = 0; i < _steps.Count; i++) StepsPanel.Children.Add(BuildRow(i));
        if (_steps.Count == 0)
            StepsPanel.Children.Add(new TextBlock { Text = "No steps. Record from the controller or add steps below.", Style = (Style)Application.Current.Resources["DsBody"] });
    }

    private UIElement BuildRow(int i)
    {
        var step = _steps[i];
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var num = new TextBlock { Text = (i + 1).ToString(), VerticalAlignment = VerticalAlignment.Center, Style = (Style)Application.Current.Resources["DsMono"] };
        grid.Children.Add(num);

        var kind = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var k in KindNames) kind.Items.Add(k);
        kind.SelectedIndex = (int)step.Kind;
        kind.SelectionChanged += (_, _) =>
        {
            if (kind.SelectedIndex < 0) return;
            var newKind = (MacroStepKind)kind.SelectedIndex;
            _steps[i] = newKind == MacroStepKind.Delay
                ? new MacroStep(MacroStepKind.Delay, DelayMs: step.DelayMs > 0 ? step.DelayMs : 50)
                : step with { Kind = newKind, Button = step.Button == DsButtons.None ? DsButtons.Cross : step.Button };
            SaveSteps();
            RebuildSteps();
        };
        Grid.SetColumn(kind, 1);
        grid.Children.Add(kind);

        FrameworkElement param = step.Kind switch
        {
            MacroStepKind.ButtonDown or MacroStepKind.ButtonUp => ButtonPicker(i, step),
            MacroStepKind.KeyDown or MacroStepKind.KeyUp => KeyPicker(i, step),
            MacroStepKind.MouseDown or MacroStepKind.MouseUp => MousePicker(i, step),
            _ => DelayBox(i, step),
        };
        Grid.SetColumn(param, 2);
        grid.Children.Add(param);

        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        tools.Children.Add(IconButton("", "Move up", i > 0, () => Move(i, -1)));
        tools.Children.Add(IconButton("", "Move down", i < _steps.Count - 1, () => Move(i, 1)));
        tools.Children.Add(IconButton("", "Delete step", true, () => { _steps.RemoveAt(i); SaveSteps(); RebuildSteps(); }));
        Grid.SetColumn(tools, 3);
        grid.Children.Add(tools);
        return grid;
    }

    private static Button IconButton(string glyph, string tip, bool enabled, Action click)
    {
        var b = new Button { Content = new FontIcon { Glyph = glyph, FontSize = 12 }, Padding = new Thickness(8, 6, 8, 6), IsEnabled = enabled };
        ToolTipService.SetToolTip(b, tip);
        b.Click += (_, _) => click();
        return b;
    }

    private void Move(int i, int delta)
    {
        int j = i + delta;
        if (j < 0 || j >= _steps.Count) return;
        (_steps[i], _steps[j]) = (_steps[j], _steps[i]);
        SaveSteps();
        RebuildSteps();
    }

    private FrameworkElement ButtonPicker(int i, MacroStep step)
    {
        var box = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var b in ButtonChoices) box.Items.Add(ButtonNames.Name(b));
        box.SelectedIndex = Math.Max(0, Array.IndexOf(ButtonChoices, step.Button));
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedIndex < 0) return;
            var newButton = ButtonChoices[box.SelectedIndex];
            var old = _steps[i];
            _steps[i] = old with { Button = newButton };
            // Changing a press also changes its matching release, so the pair stays together.
            if (old.Kind == MacroStepKind.ButtonDown)
            {
                int up = _steps.FindIndex(i + 1, s => s.Kind == MacroStepKind.ButtonUp && s.Button == old.Button);
                if (up >= 0) _steps[up] = _steps[up] with { Button = newButton };
            }
            SaveSteps();
            RebuildSteps();
        };
        return box;
    }

    private FrameworkElement MousePicker(int i, MacroStep step)
    {
        var box = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var n in MouseNames) box.Items.Add(n);
        box.SelectedIndex = (int)step.Mouse;
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedIndex < 0) return;
            var old = _steps[i];
            var nb = (MouseButtonTarget)box.SelectedIndex;
            _steps[i] = old with { Mouse = nb };
            if (old.Kind == MacroStepKind.MouseDown)
            {
                int up = _steps.FindIndex(i + 1, s => s.Kind == MacroStepKind.MouseUp && s.Mouse == old.Mouse);
                if (up >= 0) _steps[up] = _steps[up] with { Mouse = nb };
            }
            SaveSteps();
            RebuildSteps();
        };
        return box;
    }

    private FrameworkElement KeyPicker(int i, MacroStep step)
    {
        var b = new Button
        {
            Content = _captureIndex == i ? "Press any key…" : step.VirtualKey == 0 ? "Choose key" : "Key: " + ButtonNames.KeyName(step.VirtualKey),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        b.Click += (_, _) =>
        {
            _captureIndex = _captureIndex == i ? -1 : i;
            RebuildSteps();
        };
        return b;
    }

    private FrameworkElement DelayBox(int i, MacroStep step)
    {
        var box = new NumberBox
        {
            Value = step.DelayMs,
            Minimum = 0,
            Maximum = MacroLimits.MaxDelayMs,
            SmallChange = 10,
            LargeChange = 100,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            PlaceholderText = "ms",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var panel = new Grid { ColumnSpacing = 8 };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        panel.Children.Add(box);
        var unit = new TextBlock { Text = "ms", VerticalAlignment = VerticalAlignment.Center, Style = (Style)Application.Current.Resources["DsBody"] };
        Grid.SetColumn(unit, 1);
        panel.Children.Add(unit);
        box.ValueChanged += (_, args) =>
        {
            if (double.IsNaN(args.NewValue)) return;
            _steps[i] = _steps[i] with { DelayMs = (int)Math.Clamp(Math.Round(args.NewValue), 0, MacroLimits.MaxDelayMs) };
            SaveSteps(); // no rebuild: keep focus in the box while typing
        };
        return panel;
    }

    /// <summary>Tunnelling: catches the key for the step being captured before buttons react to Space/Enter.</summary>
    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_captureIndex < 0 || _captureIndex >= _steps.Count) return;
        int i = _captureIndex;
        ushort vk = (ushort)e.Key;
        var old = _steps[i];
        _steps[i] = old with { VirtualKey = vk };
        if (old.Kind == MacroStepKind.KeyDown)
        {
            int up = _steps.FindIndex(i + 1, s => s.Kind == MacroStepKind.KeyUp && s.VirtualKey == old.VirtualKey);
            if (up >= 0) _steps[up] = _steps[up] with { VirtualKey = vk };
        }
        _captureIndex = -1;
        e.Handled = true;
        SaveSteps();
        RebuildSteps();
    }

    private bool Full(int adding)
    {
        if (_steps.Count + adding <= MacroLimits.MaxSteps) return false;
        StatusText.Text = $"A macro can have at most {MacroLimits.MaxSteps} steps.";
        return true;
    }

    private void AddSteps(params MacroStep[] steps)
    {
        if (Full(steps.Length)) return;
        // Separate presses with a short wait so they register as separate presses.
        if (_steps.Count > 0 && _steps[^1].Kind != MacroStepKind.Delay && steps[0].Kind != MacroStepKind.Delay)
            _steps.Add(new MacroStep(MacroStepKind.Delay, DelayMs: 50));
        _steps.AddRange(steps);
        SaveSteps();
        RebuildSteps();
    }

    private void OnAddButton(object sender, RoutedEventArgs e) => AddSteps(
        new MacroStep(MacroStepKind.ButtonDown, DsButtons.Cross), new MacroStep(MacroStepKind.Delay, DelayMs: 50), new MacroStep(MacroStepKind.ButtonUp, DsButtons.Cross));

    private void OnAddKey(object sender, RoutedEventArgs e)
    {
        AddSteps(new MacroStep(MacroStepKind.KeyDown, VirtualKey: 0x41), new MacroStep(MacroStepKind.Delay, DelayMs: 50), new MacroStep(MacroStepKind.KeyUp, VirtualKey: 0x41));
    }

    private void OnAddMouse(object sender, RoutedEventArgs e) => AddSteps(
        new MacroStep(MacroStepKind.MouseDown, Mouse: MouseButtonTarget.Left), new MacroStep(MacroStepKind.Delay, DelayMs: 50), new MacroStep(MacroStepKind.MouseUp, Mouse: MouseButtonTarget.Left));

    private void OnAddDelay(object sender, RoutedEventArgs e)
    {
        if (Full(1)) return;
        _steps.Add(new MacroStep(MacroStepKind.Delay, DelayMs: 100));
        SaveSteps();
        RebuildSteps();
    }

    private void OnClearSteps(object sender, RoutedEventArgs e)
    {
        _steps.Clear();
        SaveSteps();
        RebuildSteps();
    }

    // ---------------- recording ----------------

    private async void OnRecord(object sender, RoutedEventArgs e)
    {
        if (_recording) { FinishRecording(); return; }

        var c = AppServices.Controller;
        if (c.Status != ConnectionStatus.Connected || c.IsMock)
        {
            StatusText.Text = "Connect your DualSense to record (recording isn't available with the simulated controller).";
            return;
        }
        if (_steps.Count > 0)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Replace the current steps?",
                Content = "Recording replaces this macro's steps with what you press on the controller.",
                PrimaryButtonText = "Record",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        }

        c.Pipeline.Sample(out var raw, out _);
        c.Recorder.Start(raw.Buttons);
        _recording = true;
        StatusText.Text = "";
        UpdateRecordUi();
    }

    private void FinishRecording()
    {
        _recording = false;
        var recorded = AppServices.Controller.Recorder.Stop();
        if (recorded.Count > 0)
        {
            _steps = recorded;
            SaveSteps();
            RebuildSteps();
        }
        UpdateRecordUi(recorded.Count == 0 ? "Nothing was recorded." : $"Recorded {recorded.Count} steps.");
    }

    private void UpdateRecordUi(string? message = null)
    {
        RecordButton.Content = _recording ? "■ Stop recording" : "● Record from controller";
        RecordText.Text = message ?? (_recording
            ? $"Recording… press buttons on your controller. {AppServices.Controller.Recorder.StepCount} steps so far."
            : "Records button presses and the exact timing between them. Sticks aren't recorded.");
    }

    private void OnTick()
    {
        if (!_recording) return;
        if (!AppServices.Controller.Recorder.IsRecording) { FinishRecording(); return; } // hit a limit
        UpdateRecordUi();
    }

    // ---------------- playback ----------------

    private void OnPlayNow(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } m) return;
        int remaining = 3;
        PlayButton.IsEnabled = false;
        PlayText.Text = $"Starting in {remaining}… switch to the window you want to test in.";
        _countdown?.Stop();
        _countdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdown.Tick += (_, _) =>
        {
            remaining--;
            if (remaining > 0) { PlayText.Text = $"Starting in {remaining}…"; return; }
            _countdown!.Stop();
            PlayButton.IsEnabled = true;
            var fresh = Selected ?? m;
            AppServices.Macros.PlayNow(fresh with { Steps = _steps.ToList() });
            PlayText.Text = "Played. Controller buttons go to the virtual controller; keys go to whichever window is in front.";
        };
        _countdown.Start();
    }

    private void OnStopAll(object sender, RoutedEventArgs e)
    {
        AppServices.Macros.StopAll("Stopped from the Macros page");
        UpdateRunningText();
    }
}
