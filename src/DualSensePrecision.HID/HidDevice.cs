using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using static DualSensePrecision.HID.Native.NativeMethods;

namespace DualSensePrecision.HID;

/// <summary>
/// One opened HID device with overlapped (asynchronous) I/O.
/// Reads block on a kernel event — no polling, no timers — and can be cancelled instantly.
/// </summary>
public sealed unsafe class HidDevice : IDisposable
{
    private readonly SafeFileHandle _handle;
    private readonly IntPtr _readEvent;
    private readonly IntPtr _writeEvent;
    private readonly IntPtr _stopEvent;
    private readonly NativeOverlapped* _readOv;
    private readonly NativeOverlapped* _writeOv;
    private readonly byte* _readBuf;
    private readonly byte* _writeBuf;
    private readonly object _writeLock = new();
    private readonly IntPtr[] _readWaitHandles;
    private int _disposed;

    public string Path { get; }
    public int InputReportLength { get; }
    public int OutputReportLength { get; }
    public int FeatureReportLength { get; }
    public string? ProductName { get; }
    public string? SerialNumber { get; }

    private HidDevice(string path, SafeFileHandle handle, HIDP_CAPS caps)
    {
        Path = path;
        _handle = handle;
        InputReportLength = caps.InputReportByteLength;
        OutputReportLength = caps.OutputReportByteLength;
        FeatureReportLength = caps.FeatureReportByteLength;
        ProductName = ReadString(HidD_GetProductString);
        SerialNumber = ReadString(HidD_GetSerialNumberString);

        _readEvent = CreateEvent(IntPtr.Zero, true, false, null);
        _writeEvent = CreateEvent(IntPtr.Zero, true, false, null);
        _stopEvent = CreateEvent(IntPtr.Zero, true, false, null);
        _readWaitHandles = new[] { _readEvent, _stopEvent };
        _readOv = (NativeOverlapped*)NativeMemory.AllocZeroed((nuint)sizeof(NativeOverlapped));
        _writeOv = (NativeOverlapped*)NativeMemory.AllocZeroed((nuint)sizeof(NativeOverlapped));
        _readOv->EventHandle = _readEvent;
        _writeOv->EventHandle = _writeEvent;
        _readBuf = (byte*)NativeMemory.AllocZeroed((nuint)Math.Max(InputReportLength, 1));
        _writeBuf = (byte*)NativeMemory.AllocZeroed((nuint)Math.Max(OutputReportLength, 1));

        // A small driver-side queue: enough to absorb scheduler hiccups without building up stale backlog.
        HidD_SetNumInputBuffers(_handle, 8);
    }

    public static HidDevice Open(string path)
    {
        var h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        if (h.IsInvalid)
            throw new HidException($"Could not open the controller ({Describe(Marshal.GetLastWin32Error())}).");

        if (!HidD_GetPreparsedData(h, out IntPtr pre))
        {
            h.Dispose();
            throw new HidException("Could not read the controller's HID descriptor.");
        }
        try
        {
            var caps = new HIDP_CAPS { Reserved = new ushort[17] };
            if (HidP_GetCaps(pre, ref caps) != HIDP_STATUS_SUCCESS)
            {
                h.Dispose();
                throw new HidException("Could not read the controller's HID capabilities.");
            }
            return new HidDevice(path, h, caps);
        }
        finally
        {
            HidD_FreePreparsedData(pre);
        }
    }

    /// <summary>
    /// Blocks until one input report arrives (written into <paramref name="destination"/>), Stop() is called, or the device fails.
    /// Returns bytes read, 0 if stopped. Throws HidException on device loss.
    /// </summary>
    public int ReadReport(Span<byte> destination)
    {
        ResetEvent(_readEvent);
        _readOv->InternalLow = IntPtr.Zero;
        _readOv->InternalHigh = IntPtr.Zero;
        _readOv->OffsetLow = 0;
        _readOv->OffsetHigh = 0;

        if (!ReadFile(_handle, _readBuf, InputReportLength, IntPtr.Zero, _readOv))
        {
            int err = Marshal.GetLastWin32Error();
            if (err != ERROR_IO_PENDING) throw Lost(err);

            uint w = WaitForMultipleObjects(2, _readWaitHandles, false, INFINITE);
            if (w == WAIT_OBJECT_0 + 1)
            {
                CancelIoEx(_handle, _readOv);
                GetOverlappedResult(_handle, _readOv, out _, true); // wait for cancellation to finish before buffers can be freed
                return 0;
            }
        }

        if (!GetOverlappedResult(_handle, _readOv, out int n, false))
        {
            int err = Marshal.GetLastWin32Error();
            if (err == ERROR_OPERATION_ABORTED) return 0;
            throw Lost(err);
        }
        n = Math.Min(n, destination.Length);
        new ReadOnlySpan<byte>(_readBuf, n).CopyTo(destination);
        return n;
    }

    /// <summary>Writes an output report (report ID at [0]). Padded/truncated to the device's output length.</summary>
    public bool Write(ReadOnlySpan<byte> report, int timeoutMs = 100)
    {
        if (OutputReportLength == 0 || Volatile.Read(ref _disposed) != 0) return false;
        lock (_writeLock)
        {
            var dst = new Span<byte>(_writeBuf, OutputReportLength);
            dst.Clear();
            report[..Math.Min(report.Length, dst.Length)].CopyTo(dst);

            ResetEvent(_writeEvent);
            _writeOv->InternalLow = IntPtr.Zero;
            _writeOv->InternalHigh = IntPtr.Zero;
            if (!WriteFile(_handle, _writeBuf, OutputReportLength, IntPtr.Zero, _writeOv))
            {
                int err = Marshal.GetLastWin32Error();
                if (err != ERROR_IO_PENDING) return false;
                if (WaitForSingleObject(_writeEvent, (uint)timeoutMs) != WAIT_OBJECT_0)
                {
                    CancelIoEx(_handle, _writeOv);
                    GetOverlappedResult(_handle, _writeOv, out _, true);
                    return false;
                }
            }
            return GetOverlappedResult(_handle, _writeOv, out _, false);
        }
    }

    /// <summary>Reads a feature report. Returns null if the controller rejects it (some revisions don't implement every report).</summary>
    public byte[]? GetFeature(byte reportId, int length)
    {
        var buf = new byte[Math.Max(length, FeatureReportLength)];
        buf[0] = reportId;
        return HidD_GetFeature(_handle, buf, buf.Length) ? buf : null;
    }

    /// <summary>Unblocks a pending ReadReport from another thread.</summary>
    public void SignalStop() => SetEvent(_stopEvent);

    private delegate bool StringGetter(SafeFileHandle h, char[] buffer, int lengthBytes);
    private string? ReadString(StringGetter getter)
    {
        var buf = new char[127];
        if (!getter(_handle, buf, buf.Length * 2)) return null;
        int end = Array.IndexOf(buf, '\0');
        var s = new string(buf, 0, end < 0 ? buf.Length : end).Trim();
        return s.Length == 0 ? null : s;
    }

    private static HidException Lost(int err) =>
        new($"Controller connection lost ({Describe(err)}).", err is ERROR_DEVICE_NOT_CONNECTED or ERROR_GEN_FAILURE or ERROR_BAD_COMMAND or ERROR_INVALID_HANDLE);

    private static string Describe(int err) => $"{new Win32Exception(err).Message} [0x{err:X}]";

    /// <summary>Call only after the reader thread has exited.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        SetEvent(_stopEvent);
        CancelIoEx(_handle, null);
        lock (_writeLock)
        {
            _handle.Dispose();
            CloseHandle(_readEvent);
            CloseHandle(_writeEvent);
            CloseHandle(_stopEvent);
            NativeMemory.Free(_readOv);
            NativeMemory.Free(_writeOv);
            NativeMemory.Free(_readBuf);
            NativeMemory.Free(_writeBuf);
        }
    }
}

public sealed class HidException(string message, bool deviceRemoved = true) : Exception(message)
{
    public bool DeviceRemoved { get; } = deviceRemoved;
}
