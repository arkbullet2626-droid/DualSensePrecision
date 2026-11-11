using DualSensePrecision.Core;
using DualSensePrecision.HID.Xbox;

namespace DualSensePrecision.HID;

/// <summary>
/// Watches for both a wired DualSense and an Xbox controller and passes through input from one of them:
/// whichever is connected, or the preferred one when both are. Switching between them looks like a quick
/// disconnect + reconnect to the rest of the app, so the virtual pad is centred, held keys are released and
/// pages refresh for the new controller.
/// </summary>
public sealed class AutoControllerSource : IControllerSource
{
    private readonly object _lock = new();
    private IControllerSource? _active;
    private ConnectionStatus _status = ConnectionStatus.Disconnected;
    private ControllerFamily _preferred;

    public DualSenseUsbController DualSense { get; }
    public XboxControllerSource Xbox { get; }

    public AutoControllerSource(DualSenseUsbController dualSense, XboxControllerSource xbox, ControllerFamily preferred = ControllerFamily.DualSense)
    {
        DualSense = dualSense;
        Xbox = xbox;
        _preferred = preferred;
        DualSense.ReportReceived += (in ControllerState s, ReadOnlySpan<byte> raw) => { if (ReferenceEquals(_active, DualSense)) ReportReceived?.Invoke(in s, raw); };
        Xbox.ReportReceived += (in ControllerState s, ReadOnlySpan<byte> raw) => { if (ReferenceEquals(_active, Xbox)) ReportReceived?.Invoke(in s, raw); };
        DualSense.StatusChanged += _ => Reevaluate();
        Xbox.StatusChanged += _ => Reevaluate();
    }

    /// <summary>The controller currently delivering input, or null.</summary>
    public IControllerSource? Active => _active;

    /// <summary>Which controller wins when both are plugged in.</summary>
    public ControllerFamily Preferred
    {
        get => _preferred;
        set { _preferred = value; Reevaluate(); }
    }

    public ConnectionStatus Status => _status;
    public ControllerInfo? Info => _active?.Info;

    public string? LastError =>
        _active?.LastError ?? (DualSense.Status == ConnectionStatus.Error ? DualSense.LastError : null) ?? DualSense.LastError ?? Xbox.LastError;

    public event ReportHandler? ReportReceived;
    public event Action<ConnectionStatus>? StatusChanged;

    public void Start()
    {
        DualSense.Start();
        Xbox.Start();
        Reevaluate();
    }

    public void Stop()
    {
        Xbox.Stop();
        DualSense.Stop();
        Reevaluate();
    }

    public bool SendOutputReport(ReadOnlySpan<byte> report) => _active?.SendOutputReport(report) ?? false;

    private void Reevaluate()
    {
        var raise = new List<ConnectionStatus>(2);
        lock (_lock)
        {
            bool ds = DualSense.Status == ConnectionStatus.Connected;
            bool xb = Xbox.Status == ConnectionStatus.Connected;
            IControllerSource? next = (ds, xb) switch
            {
                (true, true) => _preferred == ControllerFamily.Xbox ? Xbox : DualSense,
                (true, false) => DualSense,
                (false, true) => Xbox,
                _ => null,
            };

            var status = next != null ? ConnectionStatus.Connected
                : DualSense.Status == ConnectionStatus.Connecting ? ConnectionStatus.Connecting
                : DualSense.Status == ConnectionStatus.Error ? ConnectionStatus.Error
                : ConnectionStatus.Disconnected;

            if (!ReferenceEquals(next, _active))
            {
                // Switching straight from one controller to the other: report the gap so everything resets cleanly.
                if (_active != null && next != null) raise.Add(ConnectionStatus.Disconnected);
                _active = next;
                _status = status;
                raise.Add(status);
            }
            else if (status != _status)
            {
                _status = status;
                raise.Add(status);
            }
        }
        foreach (var s in raise)
        {
            try { StatusChanged?.Invoke(s); } catch { /* listeners must not break the readers */ }
        }
    }

    public void Dispose()
    {
        Xbox.Dispose();
        DualSense.Dispose();
    }
}
