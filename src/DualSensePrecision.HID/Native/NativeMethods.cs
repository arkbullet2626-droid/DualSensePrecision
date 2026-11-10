using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DualSensePrecision.HID.Native;

/// <summary>Win32 HID / SetupAPI / CfgMgr32 / Kernel32 interop. Only what the app actually uses.</summary>
internal static class NativeMethods
{
    public const uint GENERIC_READ = 0x80000000;
    public const uint GENERIC_WRITE = 0x40000000;
    public const uint FILE_SHARE_READ = 0x1;
    public const uint FILE_SHARE_WRITE = 0x2;
    public const uint OPEN_EXISTING = 3;
    public const uint FILE_FLAG_OVERLAPPED = 0x40000000;

    public const int DIGCF_PRESENT = 0x2;
    public const int DIGCF_DEVICEINTERFACE = 0x10;

    public const int ERROR_IO_PENDING = 997;
    public const int ERROR_OPERATION_ABORTED = 995;
    public const int ERROR_DEVICE_NOT_CONNECTED = 1167;
    public const int ERROR_BAD_COMMAND = 22;
    public const int ERROR_GEN_FAILURE = 31;
    public const int ERROR_INVALID_HANDLE = 6;
    public const int ERROR_NO_MORE_ITEMS = 259;

    public const uint WAIT_OBJECT_0 = 0;
    public const uint WAIT_TIMEOUT = 0x102;
    public const uint INFINITE = 0xFFFFFFFF;

    public const int HIDP_STATUS_SUCCESS = 0x00110000;

    // ---------- hid.dll ----------
    [StructLayout(LayoutKind.Sequential)]
    public struct HIDD_ATTRIBUTES
    {
        public int Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [DllImport("hid.dll")] public static extern void HidD_GetHidGuid(out Guid hidGuid);
    [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES attributes);
    [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr preparsed);
    [DllImport("hid.dll")] public static extern bool HidD_FreePreparsedData(IntPtr preparsed);
    [DllImport("hid.dll")] public static extern int HidP_GetCaps(IntPtr preparsed, ref HIDP_CAPS caps);
    [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_GetFeature(SafeFileHandle h, byte[] buffer, int length);
    [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_SetNumInputBuffers(SafeFileHandle h, int count);
    [DllImport("hid.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool HidD_GetProductString(SafeFileHandle h, char[] buffer, int lengthBytes);
    [DllImport("hid.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool HidD_GetSerialNumberString(SafeFileHandle h, char[] buffer, int lengthBytes);

    // ---------- setupapi.dll ----------
    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, int memberIndex, ref SP_DEVICE_INTERFACE_DATA data);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int detailSize, out int requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    // ---------- kernel32.dll ----------
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern SafeFileHandle CreateFile(string fileName, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern unsafe bool ReadFile(SafeFileHandle h, byte* buffer, int toRead, IntPtr read, NativeOverlapped* overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern unsafe bool WriteFile(SafeFileHandle h, byte* buffer, int toWrite, IntPtr written, NativeOverlapped* overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern unsafe bool GetOverlappedResult(SafeFileHandle h, NativeOverlapped* overlapped, out int transferred, bool wait);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern unsafe bool CancelIoEx(SafeFileHandle h, NativeOverlapped* overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr CreateEvent(IntPtr attributes, bool manualReset, bool initialState, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetEvent(IntPtr h);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ResetEvent(IntPtr h);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr h);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForMultipleObjects(int count, IntPtr[] handles, bool waitAll, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForSingleObject(IntPtr h, uint milliseconds);

    // ---------- cfgmgr32.dll (device arrival/removal) ----------
    public const int CM_NOTIFY_FILTER_TYPE_DEVICEINTERFACE = 0;
    public const int CM_NOTIFY_ACTION_DEVICEINTERFACEARRIVAL = 0;
    public const int CM_NOTIFY_ACTION_DEVICEINTERFACEREMOVAL = 1;
    public const int CR_SUCCESS = 0;

    /// <summary>CM_NOTIFY_FILTER is 416 bytes: 4 DWORDs + a union whose largest member is WCHAR[200].</summary>
    [StructLayout(LayoutKind.Explicit, Size = 416)]
    public struct CM_NOTIFY_FILTER
    {
        [FieldOffset(0)] public int cbSize;
        [FieldOffset(4)] public int Flags;
        [FieldOffset(8)] public int FilterType;
        [FieldOffset(12)] public int Reserved;
        [FieldOffset(16)] public Guid ClassGuid;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate int CmNotifyCallback(IntPtr hNotify, IntPtr context, int action, IntPtr eventData, int eventDataSize);

    [DllImport("cfgmgr32.dll")]
    public static extern int CM_Register_Notification(ref CM_NOTIFY_FILTER filter, IntPtr context, CmNotifyCallback callback, out IntPtr hNotify);

    [DllImport("cfgmgr32.dll")]
    public static extern int CM_Unregister_Notification(IntPtr hNotify);
}
