using DualSensePrecision.Core;
using DualSensePrecision.HID.Protocol;
using DualSensePrecision.Profiles;
using DualSensePrecision.Telemetry;

namespace DualSensePrecision.App.Services;

/// <summary>
/// Owns everything the app writes TO the controller: lightbar, player LEDs, mute LED, motor strength,
/// adaptive trigger effects and test rumble.
///
/// One background thread sends the latest state (latest-wins, at most ~60 reports/s), so dragging a colour
/// picker never blocks the UI and never floods USB. State is re-sent automatically after every reconnect,
/// because the controller forgets it when unplugged.
/// </summary>
public sealed class OutputManager : IDisposable
{
    private readonly ControllerService _controller;
    private readonly AutoResetEvent _signal = new(false);
    private readonly Thread _thread;
    private volatile bool _stop;
    private volatile bool _dirty;
    private volatile bool _needsLightbarRelease;

    private readonly object _rumbleLock = new();
    private byte _rumbleLeft, _rumbleRight;
    private long _rumbleUntil;
    private bool _rumbleActive, _rumbleDirty;
    private byte _gameLeft, _gameRight;

    public string? LastError { get; private set; }

    public OutputManager(ControllerService controller)
    {
        _controller = controller;
        _controller.StatusChanged += OnStatus;
        _thread = new Thread(Run) { IsBackground = true, Name = "DualSense output" };
        _thread.Start();
        if (_controller.Status == ConnectionStatus.Connected) OnStatus(ConnectionStatus.Connected);
    }

    /// <summary>Re-send lightbar / LEDs / triggers from the active profile.</summary>
    public void Refresh()
    {
        _dirty = true;
        _signal.Set();
    }

    /// <summary>Vibrate for <paramref name="milliseconds"/>, then stop. Motor strength from the profile applies.</summary>
    public void TestRumble(byte left, byte right, int milliseconds)
    {
        lock (_rumbleLock)
        {
            _rumbleLeft = left;
            _rumbleRight = right;
            _rumbleUntil = HighResClock.Now() + (long)(milliseconds * HighResClock.TicksPerMicrosecond * 1000);
            _rumbleActive = true;
            _rumbleDirty = true;
        }
        _signal.Set();
    }

    /// <summary>Rumble a game sent to the virtual controller; forwarded to the DualSense until the game changes it.</summary>
    public void SetGameRumble(byte left, byte right)
    {
        lock (_rumbleLock)
        {
            if (_gameLeft == left && _gameRight == right) return;
            _gameLeft = left;
            _gameRight = right;
            if (!_rumbleActive) { _rumbleLeft = left; _rumbleRight = right; _rumbleDirty = true; }
        }
        _signal.Set();
    }

    public (byte left, byte right) GameRumble { get { lock (_rumbleLock) return (_gameLeft, _gameRight); } }

    private void OnStatus(ConnectionStatus s)
    {
        if (s != ConnectionStatus.Connected) return;
        _needsLightbarRelease = true;
        _dirty = true;
        _signal.Set();
    }

    private void Run()
    {
        while (!_stop)
        {
            _signal.WaitOne(50); // the timeout is only so test rumble can switch itself off
            if (_stop) break;
            if (_controller.Status != ConnectionStatus.Connected) continue;

            if (_needsLightbarRelease)
            {
                _needsLightbarRelease = false;
                // Fade out the firmware's own blue animation so our colour takes over.
                Send(new OutputReportBuilder().ReleaseDefaultLightbar());
                Thread.Sleep(30);
            }

            if (_dirty)
            {
                _dirty = false;
                var p = AppServices.ActiveProfile;
                if (p != null) Send(BuildState(p));
                Thread.Sleep(16);
            }

            byte l = 0, r = 0;
            bool sendRumble = false;
            lock (_rumbleLock)
            {
                if (_rumbleActive && HighResClock.Now() >= _rumbleUntil)
                {
                    _rumbleActive = false;
                    _rumbleLeft = _gameLeft;   // test over: hand back to whatever the game asked for
                    _rumbleRight = _gameRight;
                    _rumbleDirty = true;
                }
                if (_rumbleDirty)
                {
                    _rumbleDirty = false;
                    sendRumble = true;
                    l = _rumbleLeft;
                    r = _rumbleRight;
                }
            }
            if (sendRumble) SendRumble(l, r);
        }
    }

    public static OutputReportBuilder BuildState(Profile p)
    {
        var a = p.Appearance;
        var (r, g, b) = ParseColor(a.LightbarColor);
        double k = Math.Clamp(a.LightbarBrightness, 0, 100) / 100.0;

        return new OutputReportBuilder()
            .Lightbar((byte)Math.Round(r * k), (byte)Math.Round(g * k), (byte)Math.Round(b * k))
            .PlayerIndicators((byte)a.PlayerLedMask, a.PlayerLedBrightness)
            .MuteLight(a.MuteLed)
            .MotorPowerReduction(Reduction(a.RumbleIntensity), Reduction(a.TriggerIntensity))
            .LeftTriggerEffect(Effect(p.LeftTrigger.Adaptive))
            .RightTriggerEffect(Effect(p.RightTrigger.Adaptive));
    }

    private void SendRumble(byte left, byte right)
    {
        var p = AppServices.ActiveProfile;
        bool improved = _controller.Firmware?.SupportsImprovedRumble ?? false;
        var b = new OutputReportBuilder().Rumble(left, right, improved);
        if (p != null) b.MotorPowerReduction(Reduction(p.Appearance.RumbleIntensity), Reduction(p.Appearance.TriggerIntensity));
        Send(b);
    }

    private void Send(OutputReportBuilder b)
    {
        if (_controller.Send(b)) { LastError = null; return; }
        if (_controller.Status == ConnectionStatus.Connected)
            LastError = "The controller didn't accept an output report. Unplug and reconnect it if lights or triggers stop responding.";
    }

    public static TriggerEffect Effect(AdaptiveTriggerProfile t) =>
        TriggerEffect.FromParameters((int)t.Effect, t.Start, t.End, t.Strength, t.Frequency);

    /// <summary>100 % → 0 (no reduction) … 0 % → 7 (weakest the controller allows; it can't be fully off this way).</summary>
    public static int Reduction(int percent) => (int)Math.Round((100 - Math.Clamp(percent, 0, 100)) / 100.0 * 7);

    public static (byte r, byte g, byte b) ParseColor(string? hex)
    {
        if (hex is { Length: 7 } && hex[0] == '#' &&
            int.TryParse(hex.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out int v))
            return ((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return (0x3D, 0x7B, 0xFF);
    }

    public static string ToHex(byte r, byte g, byte b) => $"#{r:X2}{g:X2}{b:X2}";

    /// <summary>Turns trigger resistance off and stops rumble so the controller isn't left stiff after the app closes.</summary>
    public void Dispose()
    {
        _stop = true;
        _signal.Set();
        _thread.Join(500);
        _controller.StatusChanged -= OnStatus;
        if (_controller.Status == ConnectionStatus.Connected)
        {
            _controller.Send(new OutputReportBuilder()
                .Rumble(0, 0, improvedEmulation: false)
                .LeftTriggerEffect(TriggerEffect.Off())
                .RightTriggerEffect(TriggerEffect.Off()));
        }
        _signal.Dispose();
    }
}
