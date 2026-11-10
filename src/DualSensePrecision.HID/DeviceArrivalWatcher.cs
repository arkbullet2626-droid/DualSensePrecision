using System.Runtime.InteropServices;
using static DualSensePrecision.HID.Native.NativeMethods;

namespace DualSensePrecision.HID;

/// <summary>
/// Signals when any HID interface arrives or is removed (CM_Register_Notification).
/// The callback runs on a system thread pool thread; we only set an event there.
/// </summary>
public sealed class DeviceArrivalWatcher : IDisposable
{
    private readonly CmNotifyCallback _callback; // kept alive for the lifetime of the registration
    private IntPtr _handle;
    public event Action? Changed;
    public bool IsActive => _handle != IntPtr.Zero;

    public DeviceArrivalWatcher()
    {
        _callback = OnNotify;
        var filter = new CM_NOTIFY_FILTER
        {
            cbSize = Marshal.SizeOf<CM_NOTIFY_FILTER>(),
            FilterType = CM_NOTIFY_FILTER_TYPE_DEVICEINTERFACE,
            ClassGuid = HidEnumerator.HidInterfaceGuid,
        };
        if (CM_Register_Notification(ref filter, IntPtr.Zero, _callback, out var h) == CR_SUCCESS)
            _handle = h;
        // If registration fails the controller service falls back to a slow rescan while disconnected.
    }

    private int OnNotify(IntPtr hNotify, IntPtr context, int action, IntPtr eventData, int size)
    {
        if (action is CM_NOTIFY_ACTION_DEVICEINTERFACEARRIVAL or CM_NOTIFY_ACTION_DEVICEINTERFACEREMOVAL)
        {
            try { Changed?.Invoke(); } catch { /* never throw into cfgmgr32 */ }
        }
        return 0;
    }

    public void Dispose()
    {
        var h = Interlocked.Exchange(ref _handle, IntPtr.Zero);
        if (h != IntPtr.Zero) CM_Unregister_Notification(h);
    }
}
