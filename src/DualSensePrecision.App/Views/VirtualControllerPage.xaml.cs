using DualSensePrecision.App.Services;
using DualSensePrecision.Profiles;
using DualSensePrecision.VirtualController;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace DualSensePrecision.App.Views;

public sealed partial class VirtualControllerPage : Page
{
    private UiSampler? _sampler;
    private bool _loading;
    private long _lastPackets;
    private DateTime _lastSample = DateTime.UtcNow;
    private int _tick;

    public VirtualControllerPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _loading = true;
        EnableSwitch.IsOn = AppServices.ActiveProfile?.VirtualController.Target == VirtualTarget.Xbox360;
        HideSwitch.IsOn = AppServices.Settings.HideRealController;
        _loading = false;
        AppServices.Hide.Changed += OnHideChanged;
        RefreshHide();

        _lastPackets = AppServices.Viiper?.PacketsSent ?? 0;
        _lastSample = DateTime.UtcNow;
        _sampler = new UiSampler(DispatcherQueue, TimeSpan.FromMilliseconds(50));
        _sampler.Tick += OnTick;
        _sampler.Start();
        OnTick();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        AppServices.Hide.Changed -= OnHideChanged;
        _sampler?.Stop();
        _sampler = null;
        AppServices.FlushProfileSave();
    }

    private void OnEnableToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var target = EnableSwitch.IsOn ? VirtualTarget.Xbox360 : VirtualTarget.None;
        AppServices.UpdateActiveProfile(p => p with { VirtualController = p.VirtualController with { Target = target } });
        _lastPackets = AppServices.Viiper?.PacketsSent ?? 0;
        _lastSample = DateTime.UtcNow;
    }

    // ---------------- hiding the real controller ----------------

    private void OnHideChanged() => DispatcherQueue.TryEnqueue(RefreshHide);

    private void RefreshHide()
    {
        var h = AppServices.Hide;
        HideStatus.Text = h.Status;
        HideInstallHelp.Visibility = h.IsInstalled ? Visibility.Collapsed : Visibility.Visible;
        GetHidHideButton.Visibility = h.NeedsRestart ? Visibility.Collapsed : Visibility.Visible;
        HideInstallText.Text = h.NeedsRestart
            ? "HidHide is installed but its driver isn't running yet. Restart Windows, then come back here."
            : "HidHide isn't installed. Get it from its official page (HidHide_1.5.230_x64.exe), run it and click through its screens, then restart Windows. Or run the Dual Precision setup again with 'Download and install HidHide' ticked.";
        DoubleInputBar.IsOpen = !h.IsHiding;
    }

    private async void OnHideToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool on = HideSwitch.IsOn;
        if (on && !AppServices.Settings.HidHideConsent)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Hide your real controller from games?",
                Content = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Text = "While the virtual controller is on, Dual Precision will ask the HidHide driver to hide your real controller " +
                           "from every other program — games, Steam, browsers and controller apps — so they only see the tuned virtual pad.\n\n" +
                           "• Dual Precision itself can still read it.\n" +
                           "• It's undone automatically when you switch the virtual controller off, switch this off, or close the app.\n" +
                           "• If the app ever closes unexpectedly, it's undone the next time the app starts. You can also undo it in HidHide's own Configuration Client.\n" +
                           "• Start (or restart) your game after switching this on — games that are already running keep seeing the controller.",
                },
                PrimaryButtonText = "Hide it",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                _loading = true;
                HideSwitch.IsOn = false;
                _loading = false;
                return;
            }
        }
        try
        {
            AppServices.SaveSettings(AppServices.Settings with { HideRealController = on, HidHideConsent = AppServices.Settings.HidHideConsent || on });
        }
        catch (Exception ex) { HideStatus.Text = $"Couldn't save the setting: {ex.Message}"; return; }
        AppServices.Hide.Reevaluate();
    }

    private async void OnGetHidHide(object sender, RoutedEventArgs e) =>
        await global::Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/nefarius/HidHide/releases/tag/v1.5.230.0"));

    private async void OnCheckGames(object sender, RoutedEventArgs e)
    {
        CheckButton.IsEnabled = false;
        CheckRing.IsActive = true;
        CheckResult.Text = "Checking…";
        CheckResult.Text = await HideService.CheckWhatGamesSeeAsync();
        CheckRing.IsActive = false;
        CheckButton.IsEnabled = true;
    }

    private static readonly (ushort Bit, string Name)[] XboxButtonNames =
    {
        (0x1000, "A"), (0x2000, "B"), (0x4000, "X"), (0x8000, "Y"),
        (0x0100, "LB"), (0x0200, "RB"), (0x0040, "LS (L3)"), (0x0080, "RS (R3)"),
        (0x0010, "Start"), (0x0020, "Back"), (0x0400, "Guide"),
        (0x0001, "D-pad Up"), (0x0002, "D-pad Down"), (0x0004, "D-pad Left"), (0x0008, "D-pad Right"),
    };

    /// <summary>XInput button bits → readable names, e.g. "B, LB".</summary>
    private static string XboxButtons(ushort bits)
    {
        var names = XboxButtonNames.Where(b => (bits & b.Bit) != 0).Select(b => b.Name).ToList();
        return names.Count == 0 ? "none" : string.Join(", ", names);
    }

    private void OnTick()
    {
        var v = AppServices.Viiper;

        // Status
        (string title, string detail, string brush, bool showSetup) = v?.State switch
        {
            null or VirtualState.Off => ("Off", "Games read your controller directly. Turn on to send them your tuned settings.", "DsTextTertiaryBrush", false),
            VirtualState.Connecting => ("Connecting…", v.StatusMessage ?? "", "DsWarningBrush", false),
            VirtualState.Connected => ("Active — games see an Xbox 360 controller", v.StatusMessage ?? "", "DsSuccessBrush", false),
            VirtualState.ServerMissing => ("VIIPER isn't running", "If VIIPER is installed, Dual Precision starts it for you; otherwise follow the setup below. Retrying every 2 seconds.", "DsDangerBrush", true),
            _ => ("Problem", (v.StatusMessage ?? "Unknown error.") + " Retrying automatically.", "DsDangerBrush", true),
        };
        StateTitle.Text = title;
        StateDetail.Text = detail;
        StateDot.Fill = (Brush)Application.Current.Resources[brush];
        SetupCard.Visibility = showSetup || v == null ? Visibility.Visible : Visibility.Collapsed;
        VersionText.Text = v?.ServerVersion ?? "—";

        // Output rate, measured from packets actually written
        if (++_tick % 10 == 0)
        {
            long packets = v?.PacketsSent ?? 0;
            var now = DateTime.UtcNow;
            double secs = (now - _lastSample).TotalSeconds;
            RateText.Text = v is { IsConnected: true } && secs > 0 ? $"{(packets - _lastPackets) / secs:0} Hz" : "—";
            _lastPackets = packets;
            _lastSample = now;
        }

        var (l, r) = AppServices.Output.GameRumble;
        RumbleText.Text = v is { IsConnected: true } ? (l == 0 && r == 0 ? "none" : $"L {l * 100 / 255}%  R {r * 100 / 255}%") : "—";

        // What XInput reports
        var pads = XInputProbe.Connected();
        if (pads.Count == 0)
        {
            XInputText.Text = v is { IsConnected: true }
                ? "VIIPER created the device, but Windows hasn't attached it yet. If this lasts more than a few seconds, the usbip-win2 driver may be missing — reinstall VIIPER with 'Install USBIP' ticked, then restart Windows."
                : "No Xbox-style controllers connected.";
        }
        else
        {
            var xbox = AppServices.Controller.Xbox;
            int virtualSlot = xbox?.VirtualSlot ?? -1;
            int readSlot = AppServices.Controller.IsXbox ? xbox?.ActiveSlot ?? -1 : -1;
            XInputText.Text = string.Join("\n\n", pads.Select(p =>
                $"Xbox controller #{p.Slot + 1}" +
                (p.Slot == virtualSlot ? "  ·  the virtual pad (what games should use)"
                 : p.Slot == readSlot ? "  ·  your real Xbox controller (input to the app)"
                 : "") + "\n" +
                $"  Left stick   X {p.Pad.ThumbLX,6}   Y {p.Pad.ThumbLY,6}\n" +
                $"  Right stick  X {p.Pad.ThumbRX,6}   Y {p.Pad.ThumbRY,6}\n" +
                $"  Triggers     LT {p.Pad.LeftTrigger,3}   RT {p.Pad.RightTrigger,3}\n" +
                $"  Pressed      {XboxButtons(p.Pad.Buttons)}"));
        }
    }
}
