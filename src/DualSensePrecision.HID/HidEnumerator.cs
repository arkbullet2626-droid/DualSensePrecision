using System.Runtime.InteropServices;
using DualSensePrecision.HID.Native;
using DualSensePrecision.HID.Protocol;
using static DualSensePrecision.HID.Native.NativeMethods;

namespace DualSensePrecision.HID;

public sealed record HidDeviceCandidate(string Path, ushort VendorId, ushort ProductId, ushort Version);

/// <summary>Enumerates present HID interfaces and filters for supported DualSense controllers.</summary>
public static class HidEnumerator
{
    public static Guid HidInterfaceGuid
    {
        get { HidD_GetHidGuid(out var g); return g; }
    }

    public static IReadOnlyList<HidDeviceCandidate> FindDualSenseControllers()
    {
        var result = new List<HidDeviceCandidate>();
        foreach (var path in EnumerateHidPaths())
        {
            // Open with zero access: enough to read attributes without contending with other apps.
            using var h = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h.IsInvalid) continue;
            var attr = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
            if (!HidD_GetAttributes(h, ref attr)) continue;
            if (DualSenseProtocol.IsSupported(attr.VendorID, attr.ProductID))
                result.Add(new HidDeviceCandidate(path, attr.VendorID, attr.ProductID, attr.VersionNumber));
        }
        return result;
    }

    public static IEnumerable<string> EnumerateHidPaths()
    {
        Guid guid = HidInterfaceGuid;
        IntPtr set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == IntPtr.Zero || set == new IntPtr(-1)) yield break;
        try
        {
            for (int i = 0; ; i++)
            {
                var data = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data))
                    break; // ERROR_NO_MORE_ITEMS
                string? path = GetPath(set, ref data);
                if (path != null) yield return path;
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
    }

    private static string? GetPath(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data)
    {
        SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out int size, IntPtr.Zero);
        if (size <= 0) return null;
        IntPtr buf = Marshal.AllocHGlobal(size);
        try
        {
            // SP_DEVICE_INTERFACE_DETAIL_DATA_W.cbSize: 8 on 64-bit, 6 on 32-bit. Path starts at offset 4.
            Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
            if (!SetupDiGetDeviceInterfaceDetail(set, ref data, buf, size, out _, IntPtr.Zero)) return null;
            return Marshal.PtrToStringUni(buf + 4);
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }
}
