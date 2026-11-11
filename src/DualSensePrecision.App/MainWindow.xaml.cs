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
