using DualSensePrecision.App.Services;
using DualSensePrecision.Configuration;
using DualSensePrecision.InputEngine;
using DualSensePrecision.Profiles;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace DualSensePrecision.App.Views;

public sealed partial class ProfilesPage : Page
{
    private List<Profile> _profiles = new();
    private string? _selectedId;
    private bool _loading;

    public ProfilesPage()
    {
        InitializeComponent();
        foreach (var t in DefaultProfiles.Templates)
        {
            var item = new MenuFlyoutItem { Text = t.Name, Tag = t };
            ToolTipService.SetToolTip(item, t.Description);
            item.Click += OnNewFromTemplate;
            TemplateMenu.Items.Add(item);
        }
        TemplateMenu.Items.Add(new MenuFlyoutSeparator());
        var copy = new MenuFlyoutItem { Text = "Copy of the active profile" };
        copy.Click += (_, _) => { if (AppServices.ActiveProfile is { } a) CreateFrom(a, a.Name + " copy"); };
        TemplateMenu.Items.Add(copy);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _selectedId = AppServices.ActiveProfile?.Id;
        Reload();
        AppServices.ProfilesChanged += OnProfilesChanged;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e) => AppServices.ProfilesChanged -= OnProfilesChanged;

    private void OnProfilesChanged() => Reload();

    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    // ---------------- list ----------------

    private void Reload()
    {
        AppServices.FlushProfileSave(); // make sure the active profile on disk matches what's in memory
        _profiles = AppServices.Profiles.LoadAll(out var errors).ToList();
        if (errors.Count > 0) Show(InfoBarSeverity.Warning, "Some profile files couldn't be read", string.Join("\n", errors));

        // The active profile in memory is the source of truth (it may have unsaved edits).
        if (AppServices.ActiveProfile is { } active)
        {
            int i = _profiles.FindIndex(p => p.Id == active.Id);
            if (i >= 0) _profiles[i] = active;
        }

        _loading = true;
        ProfileList.Items.Clear();
        foreach (var p in _profiles) ProfileList.Items.Add(BuildItem(p));
        if (_profiles.All(p => p.Id != _selectedId)) _selectedId = AppServices.ActiveProfile?.Id ?? _profiles.FirstOrDefault()?.Id;
        ProfileList.SelectedIndex = _profiles.FindIndex(p => p.Id == _selectedId);
        _loading = false;
        LoadDetails();
    }

    private UIElement BuildItem(Profile p)
    {
        bool active = p.Id == AppServices.ActiveProfile?.Id;
        bool startup = p.Id == AppServices.Settings.DefaultProfileId;

        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        title.Children.Add(new TextBlock
        {
            Text = p.Name,
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = Res("DsTextPrimaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 260,
        });
        if (active) title.Children.Add(Badge("ACTIVE", "DsSuccessBrush"));
        if (startup) title.Children.Add(Badge("STARTUP", "DsAccentBrush"));

        var panel = new StackPanel { Spacing = 2, Padding = new Thickness(6, 8, 6, 8) };
        panel.Children.Add(title);
        panel.Children.Add(new TextBlock
        {
            Text = ShortSummary(p) + $" · edited {p.ModifiedUtc.ToLocalTime():MMM d, h:mm tt}",
            FontSize = 12,
            Foreground = Res("DsTextSecondaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        return panel;
    }

    private static Border Badge(string text, string brushKey) => new()
    {
        BorderBrush = Res(brushKey),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(6, 1, 6, 1),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Res(brushKey) },
    };

    private static string ShortSummary(Profile p)
    {
        int remaps = p.Mappings.Base.Count + p.Mappings.Shift.Count;
        var parts = new List<string>
        {
            $"R-stick {p.RightStick.CurvePreset}",
            p.VirtualController.Target == VirtualTarget.Xbox360 ? "virtual pad on" : "virtual pad off",
        };
        if (remaps > 0) parts.Add($"{remaps} remap{(remaps == 1 ? "" : "s")}");
        if (p.Macros.Count > 0) parts.Add($"{p.Macros.Count} macro{(p.Macros.Count == 1 ? "" : "s")}");
        return string.Join(" · ", parts);
    }

    private void OnSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ProfileList.SelectedIndex < 0) return;
        _selectedId = _profiles[ProfileList.SelectedIndex].Id;
        LoadDetails();
    }

    private Profile? Selected => _profiles.FirstOrDefault(p => p.Id == _selectedId);

    private void LoadDetails()
    {
        var p = Selected;
        if (p == null) return;
        bool active = p.Id == AppServices.ActiveProfile?.Id;
        _loading = true;
        NameBox.Text = p.Name;
        StartupSwitch.IsOn = p.Id == AppServices.Settings.DefaultProfileId;
        _loading = false;

        SwitchButton.Content = active ? "Active now" : "Switch to this profile";
        SwitchButton.IsEnabled = !active;
        ActiveNote.Text = active ? "Edits on the other pages go into this profile." : "";

        static string Stick(StickProfile s) =>
            $"deadzone {s.InnerDeadzone * 100:0.#}%–{s.OuterDeadzone * 100:0.#}%, {s.CurvePreset} curve, {s.OutputShape.ToString().ToLower()}" +
            (s.SensitivityX != 1 || s.SensitivityY != 1 ? $", sens {s.SensitivityX:0.00}×/{s.SensitivityY:0.00}×" : "");
        static string Trig(TriggerProfile t) =>
            t.Mode == TriggerMode.Digital ? $"hair trigger at {t.ActivationPoint * 100:0}%"
            : $"{t.Mode.ToString().ToLower()}, deadzone {t.Deadzone * 100:0.#}%" + (t.Adaptive.Effect != AdaptiveEffectKind.Off ? $", adaptive {t.Adaptive.Effect}" : "");

        int remaps = p.Mappings.Base.Count + p.Mappings.Shift.Count;
        SummaryText.Text =
            $"Left stick   {Stick(p.LeftStick)}\n" +
            $"Right stick  {Stick(p.RightStick)}\n" +
            $"L2           {Trig(p.LeftTrigger)}\n" +
            $"R2           {Trig(p.RightTrigger)}\n" +
            $"Buttons      {(remaps == 0 ? "all default" : $"{remaps} changed")}{(p.Mappings.ShiftButton != Core.DsButtons.None ? $", shift layer on {p.Mappings.ShiftButton}" : "")}\n" +
            $"Macros       {(p.Macros.Count == 0 ? "none" : string.Join(", ", p.Macros.Select(m => m.Name)))}\n" +
            $"Lightbar     {p.Appearance.LightbarColor} at {p.Appearance.LightbarBrightness}%\n" +
            $"Virtual pad  {(p.VirtualController.Target == VirtualTarget.Xbox360 ? "Xbox 360 on" : "off")}";
    }

    // ---------------- actions ----------------

    private void OnSwitch(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } p) return;
        AppServices.SwitchProfile(p);
        Show(InfoBarSeverity.Success, $"Switched to {p.Name}", "Everything on the controller and the virtual pad now uses this profile.");
    }

    private void OnNewFromTemplate(object sender, RoutedEventArgs e)
    {
        var t = (ProfileTemplate)((MenuFlyoutItem)sender).Tag;
        CreateFrom(t.Create(), t.Name == "Blank" ? "New profile" : t.Name);
    }

    private void CreateFrom(Profile template, string baseName)
    {
        try
        {
            AppServices.FlushProfileSave();
            var created = AppServices.Profiles.Create(AppServices.Profiles.UniqueName(baseName), template);
            _selectedId = created.Id;
            AppServices.NotifyProfilesChanged();
            Reload();
            Show(InfoBarSeverity.Success, $"Created {created.Name}", "Select it and click Switch to use it.");
        }
        catch (Exception ex) { Show(InfoBarSeverity.Error, "Couldn't create the profile", ex.Message); }
    }

    private void OnNameKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) { OnRename(sender, e); e.Handled = true; }
    }

    private void OnRename(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } p) return;
        string name = NameBox.Text.Trim();
        if (name == p.Name) return;
        try
        {
            AppServices.FlushProfileSave();
            if (AppServices.Profiles.FindByName(name) is { } clash && clash.Id != p.Id)
                throw new ProfileConflictException($"A profile named \"{name}\" already exists.", clash);
            if (name.Length == 0) throw new ArgumentException("Profile name cannot be empty.");

            if (p.Id == AppServices.ActiveProfile?.Id)
            {
                AppServices.UpdateActiveProfile(x => x with { Name = name });
                AppServices.FlushProfileSave();
            }
            else
            {
                AppServices.Profiles.Rename(p.Id, name);
            }
            AppServices.NotifyProfilesChanged();
            Reload();
        }
        catch (Exception ex)
        {
            Show(InfoBarSeverity.Error, "Couldn't rename", ex.Message);
            NameBox.Text = p.Name;
        }
    }

    private void OnStartupToggled(object sender, RoutedEventArgs e)
    {
        if (_loading || Selected is not { } p) return;
        string? id = StartupSwitch.IsOn ? p.Id : (AppServices.Settings.DefaultProfileId == p.Id ? null : AppServices.Settings.DefaultProfileId);
        try
        {
            AppServices.SaveSettings(AppServices.Settings with { DefaultProfileId = id });
            Reload();
        }
        catch (Exception ex) { Show(InfoBarSeverity.Error, "Couldn't save the startup profile", ex.Message); }
    }

    private void OnDuplicate(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } p) return;
        CreateFrom(p, p.Name + " copy");
    }

    private async void OnRestore(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } p) return;
        if (!await Confirm($"Restore \"{p.Name}\" to defaults?",
                "Its sticks, triggers, button mappings, macros, lights and virtual controller setting go back to the app defaults. The name stays. This can't be undone — export it first if you might want it back.",
                "Restore defaults")) return;
        try
        {
            var fresh = new Profile { Id = p.Id, Name = p.Name, CreatedUtc = p.CreatedUtc };
            if (p.Id == AppServices.ActiveProfile?.Id)
            {
                AppServices.Macros.StopAll("Profile restored to defaults");
                var previousTarget = p.VirtualController.Target;
                AppServices.UpdateActiveProfile(_ => fresh);
                AppServices.FlushProfileSave();
                if (previousTarget != fresh.VirtualController.Target) AppServices.ApplyVirtualController();
            }
            else
            {
                AppServices.Profiles.Update(fresh);
            }
            AppServices.NotifyProfilesChanged();
            Reload();
            Show(InfoBarSeverity.Success, $"{p.Name} restored to defaults", "");
        }
        catch (Exception ex) { Show(InfoBarSeverity.Error, "Couldn't restore defaults", ex.Message); }
    }

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } p) return;
        if (_profiles.Count <= 1)
        {
            Show(InfoBarSeverity.Warning, "Can't delete the last profile", "Create another profile first.");
            return;
        }
        bool active = p.Id == AppServices.ActiveProfile?.Id;
        if (!await Confirm($"Delete \"{p.Name}\"?",
                (active ? "This is the active profile — the app will switch to another one first. " : "") + "This can't be undone. Export it first if you might want it back.",
                "Delete")) return;
        try
        {
            AppServices.FlushProfileSave();
            if (active)
            {
                var next = _profiles.First(x => x.Id != p.Id);
                AppServices.SwitchProfile(next);
            }
            AppServices.Profiles.Delete(p.Id);
            if (AppServices.Settings.DefaultProfileId == p.Id)
                AppServices.SaveSettings(AppServices.Settings with { DefaultProfileId = null });
            _selectedId = AppServices.ActiveProfile?.Id;
            AppServices.NotifyProfilesChanged();
            Reload();
            Show(InfoBarSeverity.Informational, $"Deleted {p.Name}", active ? $"Now using {AppServices.ActiveProfile?.Name}." : "");
        }
        catch (Exception ex) { Show(InfoBarSeverity.Error, "Couldn't delete", ex.Message); }
    }

    private async void OnExport(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } p) return;
        try
        {
            AppServices.FlushProfileSave();
            var picker = new Windows.Storage.Pickers.FileSavePicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Current!));
            picker.FileTypeChoices.Add("Dual Precision profile", new List<string> { ".json" });
            picker.SuggestedFileName = p.Name;
            var file = await picker.PickSaveFileAsync();
            if (file == null) return;
            AppServices.Profiles.Export(p.Id, file.Path);
            Show(InfoBarSeverity.Success, "Exported", file.Path);
        }
        catch (Exception ex) { Show(InfoBarSeverity.Error, "Export failed", ex.Message); }
    }

    private async void OnImport(object sender, RoutedEventArgs e)
    {
        string? path = null;
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Current!));
            picker.FileTypeFilter.Add(".json");
            var file = await picker.PickSingleFileAsync();
            if (file == null) return;
            path = file.Path;
            AppServices.FlushProfileSave();
            Imported(AppServices.Profiles.Import(path));
        }
        catch (ProfileConflictException conflict) when (path != null)
        {
            // Never overwrite silently: ask.
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "A profile with this name or ID already exists",
                Content = $"\"{conflict.Existing.Name}\" is already in your profiles. Keep both, or replace yours with the imported one?",
                PrimaryButtonText = "Keep both",
                SecondaryButtonText = "Replace mine",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
            };
            var choice = await dialog.ShowAsync();
            if (choice == ContentDialogResult.None) return;
            try
            {
                bool replace = choice == ContentDialogResult.Secondary;
                var p = AppServices.Profiles.Import(path, overwrite: replace, asCopy: !replace);
                if (replace && p.Id == AppServices.ActiveProfile?.Id) AppServices.ReloadActive();
                Imported(p);
            }
            catch (Exception ex) { Show(InfoBarSeverity.Error, "Import failed", ex.Message); }
        }
        catch (Exception ex) { Show(InfoBarSeverity.Error, "Import failed", ex.Message); }
    }

    private void Imported(Profile p)
    {
        _selectedId = p.Id;
        AppServices.NotifyProfilesChanged();
        Reload();
        Show(InfoBarSeverity.Success, $"Imported {p.Name}", "Select it and click Switch to use it.");
    }

    // ---------------- helpers ----------------

    private async Task<bool> Confirm(string title, string body, string action)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = body,
            PrimaryButtonText = action,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void Show(InfoBarSeverity severity, string title, string message)
    {
        MessageBar.Severity = severity;
        MessageBar.Title = title;
        MessageBar.Message = message;
        MessageBar.IsOpen = true;
    }
}
