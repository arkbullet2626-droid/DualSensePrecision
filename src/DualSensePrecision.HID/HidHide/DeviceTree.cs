using System.Runtime.InteropServices;

namespace DualSensePrecision.HID.HidHide;

/// <summary>Read-only Plug and Play lookups (cfgmgr32): interface → device instance, parents, hardware IDs. No admin needed.</summary>
public static class DeviceTree
{
    /// <summary>GUID_DEVINTERFACE_XUSB: what XInput opens for Xbox controllers.</summary>
    public static readonly Guid XusbInterface = new("EC87F1E3-C13B-4100-B5F7-8B84D54260CB");

    /// <summary>GUID_DEVINTERFACE_HID.</summary>
    public static readonly Guid HidInterface = new("4D1E55B2-F16F-11CF-88CB-001111000030");

    private const int CR_SUCCESS = 0;
    private const uint CM_GET_DEVICE_INTERFACE_LIST_PRESENT = 0;
    private const uint CM_DRP_HARDWAREID = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct DEVPROPKEY { public Guid fmtid; public uint pid; }

    // DEVPKEY_Device_InstanceId
    private static DEVPROPKEY InstanceIdKey = new() { fmtid = new Guid("78c34fc8-104a-4aca-9ea4-524d52996e57"), pid = 256 };

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_Interface_List_SizeW(out uint length, ref Guid classGuid, string? deviceId, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_Interface_ListW(ref Guid classGuid, string? deviceId, char[] buffer, uint length, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_Interface_PropertyW(string iface, ref DEVPROPKEY key, out uint type, byte[]? buffer, ref uint size, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_Parent(out uint parent, uint devInst, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_Device_ID_Size(out uint length, uint devInst, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_IDW(uint devInst, char[] buffer, uint length, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_DevNode_Registry_PropertyW(uint devInst, uint property, out uint type, byte[]? buffer, ref uint length, uint flags);

    /// <summary>Device interface paths currently present for a class.</summary>
    public static List<string> Interfaces(Guid cls)
    {
        var g = cls;
        if (CM_Get_Device_Interface_List_SizeW(out uint len, ref g, null, CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != CR_SUCCESS || len <= 1)
            return new List<string>();
        var buf = new char[len];
        if (CM_Get_Device_Interface_ListW(ref g, null, buf, len, CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != CR_SUCCESS)
            return new List<string>();
        return DualSensePrecision.HID.Protocol.HidHideLogic.ParseMultiSz(buf);
    }

    /// <summary>Device instance ID (e.g. HID\VID_054C&amp;PID_0CE6&amp;MI_03\7&amp;…) behind an interface path.</summary>
    public static string? InstanceIdOf(string interfacePath)
    {
        uint size = 0;
        CM_Get_Device_Interface_PropertyW(interfacePath, ref InstanceIdKey, out _, null, ref size, 0);
        if (size == 0) return null;
        var buf = new byte[size];
        if (CM_Get_Device_Interface_PropertyW(interfacePath, ref InstanceIdKey, out _, buf, ref size, 0) != CR_SUCCESS) return null;
        return System.Text.Encoding.Unicode.GetString(buf, 0, (int)size).TrimEnd('\0');
    }

    private static string? IdOf(uint devInst)
    {
        if (CM_Get_Device_ID_Size(out uint len, devInst, 0) != CR_SUCCESS) return null;
        var buf = new char[len + 1];
        return CM_Get_Device_IDW(devInst, buf, len + 1, 0) == CR_SUCCESS ? new string(buf, 0, (int)len) : null;
    }

    public static string? ParentOf(string instanceId)
    {
        if (CM_Locate_DevNodeW(out uint dev, instanceId, 0) != CR_SUCCESS) return null;
        return CM_Get_Parent(out uint parent, dev, 0) == CR_SUCCESS ? IdOf(parent) : null;
    }

    /// <summary>Every ancestor's instance ID and hardware IDs, nearest first, up to the root.</summary>
    public static List<string> AncestorIds(string instanceId)
    {
        var result = new List<string>();
        if (CM_Locate_DevNodeW(out uint dev, instanceId, 0) != CR_SUCCESS) return result;
        for (int depth = 0; depth < 32 && CM_Get_Parent(out uint parent, dev, 0) == CR_SUCCESS; depth++)
        {
            if (IdOf(parent) is { } id) result.Add(id);
            result.AddRange(HardwareIds(parent));
            dev = parent;
        }
        return result;
    }

    private static List<string> HardwareIds(uint devInst)
    {
        uint len = 0;
        CM_Get_DevNode_Registry_PropertyW(devInst, CM_DRP_HARDWAREID, out _, null, ref len, 0);
        if (len == 0) return new List<string>();
        var buf = new byte[len];
        if (CM_Get_DevNode_Registry_PropertyW(devInst, CM_DRP_HARDWAREID, out _, buf, ref len, 0) != CR_SUCCESS) return new List<string>();
        return DualSensePrecision.HID.Protocol.HidHideLogic.ParseMultiSz(System.Text.Encoding.Unicode.GetString(buf, 0, (int)len));
    }
}
