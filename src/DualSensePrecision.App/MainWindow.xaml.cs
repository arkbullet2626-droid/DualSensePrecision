using DualSensePrecision.App.Services;
using DualSensePrecision.App.Views;
using DualSensePrecision.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace DualSensePrecision.App;

public sealed partial class MainWindow : Window
{
    public static new MainWindow? Current { get; private set; }

    public MainWindow()
    {
        InitializeComponent();
        Current = this;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1360, 860));
        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(icon)) AppWindow.SetIcon(icon); // taskbar + Alt-Tab icon
        if (AppWindow.Presenter is OverlappedPresenter p) { p.PreferredMinimumWidth = 1024; p.PreferredMinimumHeight = 680; }

        AppServices.Controller.StatusChanged += _ => DispatcherQueue.TryEnqueue(() => { UpdateStatus(); CheckFamilyChange(); });
        UpdateStatus();

        foreach (var w in AppServices.StartupWarnings) ShowError(w, InfoBarSeverity.Warning);

        AppServices.ProfilesChanged += OnProfilesChanged;
        FillProfileSwitcher();

        Nav.SelectedItem = Nav.MenuItems[0];
        ScalerHost.Scale = AppServices.Settings.UiScale;
        ThemeService.Applied += ApplyThemeMode;
        ApplyThemeMode();

        // Tray icon: always there while the app runs, so closing the window can keep the controller working.
        _tray = new TrayIcon("DualSense Precision", icon);
        _tray.OpenRequested += ShowFromTray;
        _tray.ExitRequested += ExitApp;
        _tray.Show();
        AppWindow.Closing += OnClosing;
        Closed += (_, _) =>
        {
            _closed = true;
            ThemeService.Applied -= ApplyThemeMode; // a late colour change must not touch a closed window
            _tray.Dispose();
        };

        Activated += OnFirstActivated;
    }

    /// <summary>Light palettes switch the window's built-in controls to their light style; also recolours the title-bar buttons.</summary>
    private bool _closed;

    private void ApplyThemeMode()
    {
        if (_closed) return;
        if (Content is FrameworkElement root) root.RequestedTheme = ThemeService.IsLight ? ElementTheme.Light : ElementTheme.Dark;
        try
        {
            var tb = AppWindow.TitleBar;
            var text = ((Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["DsTextPrimaryBrush"]).Color;
            var stroke = ((Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["DsStrokeBrush"]).Color;
            tb.ButtonForegroundColor = text;
            tb.ButtonHoverForegroundColor = text;
            tb.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            tb.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
            tb.ButtonHoverBackgroundColor = stroke;
        }
        catch { }
    }

    /// <summary>Settings → Interface → Interface scale (applied live).</summary>
    public void SetUiScale(double scale) => ScalerHost.Scale = scale;

    /// <summary>The scaling container around the whole window content (for the scale slider's smooth preview).</summary>
    public Controls.ScaleHost Scaler => ScalerHost;

    private readonly TrayIcon _tray;
    private bool _exiting;

    /// <summary>Start hidden with just the tray icon (used by "Start with Windows").</summary>
    public void StartInTray() => AppLog.Info("Started minimised to the tray");

    public void ShowFromTray()
    {
        AppWindow.Show();
        Activate();
        SingleInstanceFront();
    }

    private void SingleInstanceFront()
    {
        if (AppWindow.Presenter is OverlappedPresenter p && p.State == OverlappedPresenterState.Minimized) p.Restore();
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_exiting || !AppServices.Settings.MinimizeToTray) return;
        args.Cancel = true;
        AppWindow.Hide();
        if (!AppServices.Settings.TrayHintShown)
        {
            _tray.Notify("DualSense Precision is still running",
                "Your controller settings and the virtual controller keep working. Right-click the tray icon and choose Exit to close it completely.");
            try { AppServices.SaveSettings(AppServices.Settings with { TrayHintShown = true }); } catch { }
        }
    }

    /// <summary>Really quit: stops the virtual controller, un-hides the real one, removes the tray icon.</summary>
    public void ExitApp()
    {
        _exiting = true;
        Close();
        Application.Current.Exit();
    }

    private async void OnFirstActivated(object sender, WindowActivatedEventArgs e)
    {
        Activated -= OnFirstActivated;
        if (!AppServices.Settings.FirstRunDone)
        {
            await Task.Delay(400); // let the window finish drawing
            await ShowWelcomeAsync();
        }
        if (AppServices.Settings.CheckForUpdates)
        {
            await Task.Delay(3000);
            var r = await UpdateChecker.CheckAsync();
            if (r.UpdateAvailable) ShowUpdate(r.Message);
        }
    }

    public void ShowUpdate(string message)
    {
        UpdateBar.Message = message + " Updates are free and keep your profiles.";
        UpdateBar.IsOpen = true;
    }

    private async void OnDownloadUpdate(object sender, RoutedEventArgs e) =>
        await global::Windows.System.Launcher.LaunchUriAsync(new Uri(UpdateChecker.ReleasesPage));

    /// <summary>The first-run guide (also reachable from About).</summary>
    public async Task ShowWelcomeAsync()
    {
        var body = new StackPanel { Spacing = 12, MaxWidth = 520 };
        void Step(string n, string title, string text)
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var badge = new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(3),
                Background = (Brush)Application.Current.Resources["DsVoltGradientBrush"],
                Child = new TextBlock
                {
                    Text = n, FontWeight = Microsoft.UI.Text.FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)Application.Current.Resources["DsOnAccentBrush"],
                },
            };
            var txt = new StackPanel { Spacing = 2 };
            txt.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            txt.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["DsTextSecondaryBrush"] });
            Grid.SetColumn(txt, 1);
            row.Children.Add(badge);
            row.Children.Add(txt);
            body.Children.Add(row);
        }
        Step("1", "Connect your controller", "Plug in a DualSense with a USB-C data cable, or connect an Xbox controller (USB, Bluetooth or Xbox Wireless Adapter). It's picked up automatically.");
        Step("2", "Tune it", "Stick Settings, Trigger Settings and Button Mapping change how it feels. Everything saves to the profile shown in the sidebar.");
        Step("3", "Send it to games", "On Virtual Controller, switch the virtual Xbox controller on: games then get your tuned input. Needs VIIPER (Setup can install it).");
        Step("4", "Stop doubled input", "Also on Virtual Controller, switch on 'Hide your real controller from games' so games see only the tuned pad. Needs HidHide (Setup can install it).");
        body.Children.Add(new TextBlock
        {
            Text = "Some games ban macros or remapping in online play. Check a game's rules before using them there.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = (Brush)Application.Current.Resources["DsWarningBrush"],
        });

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Welcome to DualSense Precision",
            Content = body,
            PrimaryButtonText = "Open Virtual Controller",
            CloseButtonText = "Got it",
            DefaultButton = ContentDialogButton.Close,
        };
        var result = await dialog.ShowAsync();
        try { AppServices.SaveSettings(AppServices.Settings with { FirstRunDone = true }); } catch { }
        if (result == ContentDialogResult.Primary) NavigateTo("Virtual");
    }

    public void ShowError(string message, InfoBarSeverity severity = InfoBarSeverity.Error)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ErrorBar.Severity = severity;
            ErrorBar.Title = severity == InfoBarSeverity.Error ? "Something went wrong" : "Heads up";
            ErrorBar.Message = message;
            ErrorBar.IsOpen = true;
        });
    }

    public void NavigateTo(string tag)
    {
        foreach (var item in Nav.MenuItems.OfType<NavigationViewItem>())
            if ((string)item.Tag == tag) { Nav.SelectedItem = item; return; }
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var transition = AppServices.Settings.Animations
            ? new EntranceNavigationTransitionInfo()
            : (NavigationTransitionInfo)new SuppressNavigationTransitionInfo();

        if (args.SelectedItem is NavigationViewItem { Tag: "About" })
        {
            ContentFrame.Navigate(typeof(AboutPage), null, transition);
            return;
        }
        if (args.IsSettingsSelected)
        {
            ContentFrame.Navigate(typeof(SettingsPage), null, transition);
            return;
        }
        if (args.SelectedItem is not NavigationViewItem { Tag: string tag }) return;

        switch (tag)
        {
            case "Home": ContentFrame.Navigate(typeof(HomePage), null, transition); break;
            case "Controller": ContentFrame.Navigate(typeof(ControllerPage), null, transition); break;
            case "Performance": ContentFrame.Navigate(typeof(PerformancePage), null, transition); break;
            case "Virtual": ContentFrame.Navigate(typeof(VirtualControllerPage), null, transition); break;
            case "Buttons": ContentFrame.Navigate(typeof(ButtonMappingPage), null, transition); break;
            case "Macros": ContentFrame.Navigate(typeof(MacrosPage), null, transition); break;
            case "Tester": ContentFrame.Navigate(typeof(ControllerTesterPage), null, transition); break;
            case "Profiles": ContentFrame.Navigate(typeof(ProfilesPage), null, transition); break;
            case "Sticks": ContentFrame.Navigate(typeof(StickSettingsPage), null, transition); break;
            case "Triggers": ContentFrame.Navigate(typeof(TriggerSettingsPage), null, transition); break;
            case "Appearance": ContentFrame.Navigate(typeof(AppearancePage), null, transition); break;
            default:
                ContentFrame.Navigate(typeof(FeatureStatusPage), SectionCatalog.Get(tag), transition);
                break;
        }
    }

    // ---------------- quick profile switcher ----------------

    private bool _fillingSwitcher;
    private List<Profiles.Profile> _switcherProfiles = new();

    private void FillProfileSwitcher()
    {
        _fillingSwitcher = true;
        AppServices.FlushProfileSave();
        _switcherProfiles = AppServices.Profiles.LoadAll(out _).ToList();
        ProfileSwitcher.Items.Clear();
        foreach (var p in _switcherProfiles) ProfileSwitcher.Items.Add(p.Name);
        ProfileSwitcher.SelectedIndex = _switcherProfiles.FindIndex(p => p.Id == AppServices.ActiveProfile?.Id);
        _fillingSwitcher = false;
    }

    private ControllerFamily _shownFamily = ControllerFamily.DualSense;

    /// <summary>
    /// When a different kind of controller takes over (DualSense ↔ Xbox), re-open pages whose layout depends on
    /// the hardware (button labels, hidden DualSense-only sections).
    /// </summary>
    private void CheckFamilyChange()
    {
        var c = AppServices.Controller;
        if (c.Status != ConnectionStatus.Connected || c.Family == _shownFamily) return;
        _shownFamily = c.Family;
        if (ContentFrame.Content is ButtonMappingPage or AppearancePage or TriggerSettingsPage or ControllerTesterPage)
            ContentFrame.Navigate(ContentFrame.Content.GetType(), null, new SuppressNavigationTransitionInfo());
    }

    private void OnProfilesChanged()
    {
        FillProfileSwitcher();
        // Pages that edit "the active profile" re-open so they show the new one. The Profiles page refreshes itself.
        if (ContentFrame.Content is Page page && page is not ProfilesPage && page is not SettingsPage)
        {
            var type = page.GetType();
            var param = Nav.SelectedItem is NavigationViewItem { Tag: string tag } && type == typeof(FeatureStatusPage) ? SectionCatalog.Get(tag) : null;
            ContentFrame.Navigate(type, param, new SuppressNavigationTransitionInfo());
        }
    }

    private void OnProfileSwitcherChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_fillingSwitcher) return;
        int i = ProfileSwitcher.SelectedIndex;
        if (i < 0 || i >= _switcherProfiles.Count) return;
        var target = AppServices.Profiles.Get(_switcherProfiles[i].Id) ?? _switcherProfiles[i];
        AppServices.SwitchProfile(target);
    }

    private void UpdateStatus()
    {
        var c = AppServices.Controller;
        MockBadge.Visibility = c.IsMock ? Visibility.Visible : Visibility.Collapsed;

        (string text, string detail, string brush) = c.Status switch
        {
            ConnectionStatus.Connected => (c.Info?.Name ?? "Controller", c.IsMock ? "Simulated input — not real hardware" : c.Info?.ConnectionLabel ?? "Connected", "DsSuccessBrush"),
            ConnectionStatus.Connecting => ("Connecting…", "Reading controller information", "DsWarningBrush"),
            ConnectionStatus.Error => ("Connection problem", c.LastError ?? "Unknown error", "DsDangerBrush"),
            _ => ("No controller", "Connect a DualSense (USB-C) or an Xbox controller", "DsTextTertiaryBrush"),
        };
        StatusText.Text = text;
        StatusDetail.Text = detail;
        StatusDot.Fill = (Brush)Application.Current.Resources[brush];
    }
}
