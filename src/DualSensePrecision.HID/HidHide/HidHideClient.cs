using System.ComponentModel;
using System.Runtime.InteropServices;
using DualSensePrecision.HID.Protocol;
using Microsoft.Win32.SafeHandles;

namespace DualSensePrecision.HID.HidHide;

public sealed class HidHideException(string message) : Exception(message);

/// <summary>
/// Talks to the HidHide filter driver (Nefarius, signed, optional) through its control device \\.\HidHide.
/// Documented API, no administrator rights needed. HidHide allows one open handle at a time, so every call opens,
/// does its work and closes straight away.
///
/// Blacklist = device instance IDs hidden from every program; whitelist = programs (by image path) that can still see them.
/// </summary>
public static class HidHideClient
{
    private const uint IOCTL_GET_WHITELIST = 0x80016000;
    private const uint IOCTL_SET_WHITELIST = 0x80016004;
    private const uint IOCTL_GET_BLACKLIST = 0x80016008;
    private const uint IOCTL_SET_BLACKLIST = 0x8001600C;
    private const uint IOCTL_GET_ACTIVE = 0x80016010;
    private const uint IOCTL_SET_ACTIVE = 0x80016014;

    private const uint GENERIC_READ = 0x80000000;
    private const uint FILE_SHARE_READ_WRITE = 0x3;
    private const uint OPEN_EXISTING = 3;
    private const int ERROR_FILE_NOT_FOUND = 2, ERROR_ACCESS_DENIED = 5, ERROR_SHARING_VIOLATION = 32;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle h, uint code, byte[]? inBuf, int inSize, byte[]? outBuf, int outSize, out int returned, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint QueryDosDeviceW(string deviceName, char[] target, int max);

    /// <summary>True if the HidHide driver is installed and running.</summary>
    public static bool IsInstalled
    {
        get
        {
            using var h = CreateFile(@"\\.\HidHide", GENERIC_READ, FILE_SHARE_READ_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (!h.IsInvalid) return true;
            int err = Marshal.GetLastWin32Error();
            return err is ERROR_ACCESS_DENIED or ERROR_SHARING_VIOLATION; // present but busy
        }
    }

    private static SafeFileHandle Open()
    {
        var h = CreateFile(@"\\.\HidHide", GENERIC_READ, FILE_SHARE_READ_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (!h.IsInvalid) return h;
        int err = Marshal.GetLastWin32Error();
        h.Dispose();
        throw new HidHideException(err switch
        {
            ERROR_FILE_NOT_FOUND => "HidHide isn't installed (or needs a restart to finish installing).",
            ERROR_ACCESS_DENIED or ERROR_SHARING_VIOLATION => "HidHide is busy — close the HidHide Configuration Client if it's open, then try again.",
            _ => $"Couldn't open HidHide: {new Win32Exception(err).Message}",
        });
    }

    private static byte[] Get(SafeFileHandle h, uint code)
    {
        DeviceIoControl(h, code, null, 0, null, 0, out int needed, IntPtr.Zero); // asks for the size
        if (needed <= 0) needed = 2;
        var buf = new byte[needed];
        if (!DeviceIoControl(h, code, null, 0, buf, buf.Length, out int got, IntPtr.Zero))
            throw new HidHideException($"HidHide request failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
        return buf.AsSpan(0, got).ToArray();
    }

    private static void Set(SafeFileHandle h, uint code, byte[] data)
    {
        if (!DeviceIoControl(h, code, data, data.Length, null, 0, out _, IntPtr.Zero))
            throw new HidHideException($"HidHide didn't accept the change: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
    }

    private static List<string> GetList(SafeFileHandle h, uint code) =>
        HidHideLogic.ParseMultiSz(MemoryMarshal.Cast<byte, char>(Get(h, code).AsSpan()));

    private static byte[] Encode(IEnumerable<string> items) => MemoryMarshal.AsBytes(HidHideLogic.ToMultiSz(items).AsSpan()).ToArray();

    public static List<string> GetBlacklist() { using var h = Open(); return GetList(h, IOCTL_GET_BLACKLIST); }
    public static List<string> GetWhitelist() { using var h = Open(); return GetList(h, IOCTL_GET_WHITELIST); }

    public static bool GetActive()
    {
        using var h = Open();
        var b = Get(h, IOCTL_GET_ACTIVE);
        return b.Length > 0 && b[0] != 0;
    }

    public static void SetActive(bool on) { using var h = Open(); Set(h, IOCTL_SET_ACTIVE, new[] { (byte)(on ? 1 : 0) }); }
    public static void SetBlacklist(IEnumerable<string> ids) { using var h = Open(); Set(h, IOCTL_SET_BLACKLIST, Encode(ids)); }
    public static void SetWhitelist(IEnumerable<string> paths) { using var h = Open(); Set(h, IOCTL_SET_WHITELIST, Encode(paths)); }

    /// <summary>This program's path in the NT form HidHide's application list uses.</summary>
    public static string? OwnImagePath() =>
        Environment.ProcessPath is { } p ? HidHideLogic.ToDosDevicePath(p, QueryDosDevice) : null;

    private static string? QueryDosDevice(string drive)
    {
        var buf = new char[1024];
        uint n = QueryDosDeviceW(drive, buf, buf.Length);
        return n == 0 ? null : HidHideLogic.ParseMultiSz(buf.AsSpan(0, (int)n)).FirstOrDefault();
    }
}
