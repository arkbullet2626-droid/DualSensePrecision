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
        _loading = false;

        _lastPackets = AppServices.Viiper?.PacketsSent ?? 0;
        _lastSample = DateTime.UtcNow;
        _sampler = new UiSampler(DispatcherQueue, TimeSpan.FromMilliseconds(50));
        _sampler.Tick += OnTick;
        _sampler.Start();
        OnTick();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
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
            VirtualState.ServerMissing => ("VIIPER isn't running", (v.StatusMessage ?? "") + " Follow the setup below, or start VIIPER if it's already installed. Retrying every 2 seconds.", "DsDangerBrush", true),
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
