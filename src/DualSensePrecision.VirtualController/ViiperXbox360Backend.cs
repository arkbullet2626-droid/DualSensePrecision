using System.Buffers.Binary;
using System.Net.Sockets;
using DualSensePrecision.Core;
using DualSensePrecision.InputEngine;

namespace DualSensePrecision.VirtualController;

public enum VirtualState { Off, Connecting, Connected, ServerMissing, Error }

/// <summary>
/// Virtual Xbox 360 controller via VIIPER + usbip-win2.
///
/// Hot path: <see cref="Submit"/> runs on the HID reader thread and writes one 20-byte packet straight to the
/// localhost stream (no queue, no extra thread hop). A supervisor thread handles connecting, reconnecting
/// after VIIPER restarts, and removing the virtual device when switched off.
/// </summary>
public sealed class ViiperXbox360Backend : IVirtualControllerBackend
{
    public const int PacketSize = 20;

    private readonly ViiperClient _client;
    private readonly AutoResetEvent _wake = new(false);
    private readonly object _sendLock = new();
    private readonly byte[] _packet = new byte[PacketSize];
    private readonly Thread _supervisor;
    private volatile bool _want, _stop;
    private volatile Socket? _stream;
    private Thread? _rumbleReader;
    private uint? _busId;
    private string? _devId;
    private long _packetsSent;

    public string Name => "Xbox 360 (VIIPER)";
    public string Description => "A virtual Xbox 360 controller created through VIIPER and the usbip-win2 driver.";

    public VirtualState State { get; private set; } = VirtualState.Off;
    public string? StatusMessage { get; private set; }
    public string? ServerVersion { get; private set; }
    public string? DeviceAddress => _busId is { } b && _devId != null ? $"{b}-{_devId}" : null;
    public long PacketsSent => Interlocked.Read(ref _packetsSent);
    public bool IsConnected => _stream != null;

    public event Action<byte, byte>? RumbleRequested;
    public event Action<VirtualState>? StateChanged;

    public ViiperXbox360Backend(ViiperClient? client = null)
    {
        _client = client ?? new ViiperClient();
        _supervisor = new Thread(Supervise) { IsBackground = true, Name = "VIIPER supervisor" };
        _supervisor.Start();
    }

    public BackendAvailability CheckAvailability()
    {
        try { ServerVersion = _client.Ping(); return BackendAvailability.Ready; }
        catch { return BackendAvailability.DriverMissing; }
    }

    public void Connect() { _want = true; _wake.Set(); }
    public void Disconnect() { _want = false; _wake.Set(); }

    public void Submit(in ProcessedState state)
    {
        var s = _stream;
        if (s == null) return;
        lock (_sendLock)
        {
            Encode(state, _packet);
            try
            {
                s.Send(_packet);
                Interlocked.Increment(ref _packetsSent);
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
            {
                Broken("Lost the connection to VIIPER. Reconnecting…");
            }
        }
    }

    /// <summary>DualSense processed state → VIIPER xbox360 input packet (little-endian).</summary>
    public static void Encode(in ProcessedState p, Span<byte> dst)
    {
        dst[..PacketSize].Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(dst, MapButtons(p.Buttons));
        dst[4] = ToByte(p.L2);
        dst[5] = ToByte(p.R2);
        BinaryPrimitives.WriteInt16LittleEndian(dst[6..], ToShort(p.LeftX));
        BinaryPrimitives.WriteInt16LittleEndian(dst[8..], ToShort(p.LeftY));
        BinaryPrimitives.WriteInt16LittleEndian(dst[10..], ToShort(p.RightX));
        BinaryPrimitives.WriteInt16LittleEndian(dst[12..], ToShort(p.RightY));
    }

    // Xbox 360 button bits (VIIPER docs/devices/xbox360.md)
    public const uint XDpadUp = 0x0001, XDpadDown = 0x0002, XDpadLeft = 0x0004, XDpadRight = 0x0008,
        XStart = 0x0010, XBack = 0x0020, XLeftThumb = 0x0040, XRightThumb = 0x0080,
        XLeftShoulder = 0x0100, XRightShoulder = 0x0200, XGuide = 0x0400,
        XA = 0x1000, XB = 0x2000, XX = 0x4000, XY = 0x8000;

    public static uint MapButtons(DsButtons b)
    {
        uint x = 0;
        if ((b & DsButtons.Cross) != 0) x |= XA;
        if ((b & DsButtons.Circle) != 0) x |= XB;
        if ((b & DsButtons.Square) != 0) x |= XX;
        if ((b & DsButtons.Triangle) != 0) x |= XY;
        if ((b & DsButtons.L1) != 0) x |= XLeftShoulder;
        if ((b & DsButtons.R1) != 0) x |= XRightShoulder;
        if ((b & DsButtons.L3) != 0) x |= XLeftThumb;
        if ((b & DsButtons.R3) != 0) x |= XRightThumb;
        if ((b & DsButtons.Options) != 0) x |= XStart;
        if ((b & (DsButtons.Create | DsButtons.Touchpad)) != 0) x |= XBack; // touchpad click also = Back, as most PC titles expect
        if ((b & DsButtons.PS) != 0) x |= XGuide;
        if ((b & DsButtons.DpadUp) != 0) x |= XDpadUp;
        if ((b & DsButtons.DpadDown) != 0) x |= XDpadDown;
        if ((b & DsButtons.DpadLeft) != 0) x |= XDpadLeft;
        if ((b & DsButtons.DpadRight) != 0) x |= XDpadRight;
        // L2/R2 digital bits are not sent: Xbox triggers are analog-only. Mute has no Xbox equivalent.
        return x;
    }

    private static byte ToByte(float v) => (byte)Math.Clamp(MathF.Round(v * 255f), 0, 255);
    private static short ToShort(float v) => (short)Math.Clamp(MathF.Round(v * 32767f), -32768, 32767);

    // ---------------- connection management ----------------

    private void Supervise()
    {
        while (!_stop)
        {
            _wake.WaitOne(TimeSpan.FromSeconds(2));
            if (_stop) break;
            try
            {
                if (_want && _stream == null) TryConnect();
                else if (!_want && (_stream != null || _busId != null)) Teardown(VirtualState.Off, null);
            }
            catch (Exception ex)
            {
                SetState(VirtualState.Error, ex.Message);
            }
        }
        Teardown(VirtualState.Off, null);
    }

    private void TryConnect()
    {
        SetState(VirtualState.Connecting, "Connecting to VIIPER…");
        try
        {
            ServerVersion = _client.Ping();
        }
        catch (ViiperException ex)
        {
            SetState(VirtualState.ServerMissing, ex.Message);
            return;
        }

        // Clean up a bus left behind by a previous broken connection.
        if (_busId is { } old) { try { _client.RemoveBus(old); } catch { } _busId = null; _devId = null; }

        try
        {
            uint bus = _client.CreateBus();
            _busId = bus;
            string dev = _client.AddDevice(bus, "xbox360");
            _devId = dev;
            var stream = _client.OpenStream(bus, dev);

            _stream = stream;
            Submit(default); // centred sticks, nothing pressed
            _rumbleReader = new Thread(() => ReadRumble(stream)) { IsBackground = true, Name = "VIIPER rumble" };
            _rumbleReader.Start();
            SetState(VirtualState.Connected, $"Virtual Xbox 360 controller active (VIIPER {ServerVersion}, device {DeviceAddress}).");
        }
        catch (Exception ex) when (ex is ViiperException or SocketException or KeyNotFoundException or InvalidOperationException)
        {
            Teardown(VirtualState.Error, $"Couldn't create the virtual controller: {ex.Message}");
        }
    }

    private void ReadRumble(Socket s)
    {
        var buf = new byte[2];
        try
        {
            while (!_stop)
            {
                int got = 0;
                while (got < 2)
                {
                    int n = s.Receive(buf, got, 2 - got, SocketFlags.None);
                    if (n <= 0) throw new SocketException((int)SocketError.ConnectionReset);
                    got += n;
                }
                try { RumbleRequested?.Invoke(buf[0], buf[1]); } catch { }
            }
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            if (ReferenceEquals(_stream, s)) Broken("VIIPER closed the connection. Reconnecting…");
        }
    }

    private void Broken(string message)
    {
        var s = _stream;
        _stream = null;
        try { s?.Dispose(); } catch { }
        try { RumbleRequested?.Invoke(0, 0); } catch { }
        SetState(VirtualState.Error, message);
        _wake.Set();
    }

    private void Teardown(VirtualState state, string? message)
    {
        var s = _stream;
        _stream = null;
        if (s != null)
        {
            lock (_sendLock) { try { s.Dispose(); } catch { } }
        }
        if (_busId is { } bus)
        {
            try { _client.RemoveBus(bus); } catch { /* VIIPER will also drop it after its 5 s timeout */ }
        }
        _busId = null;
        _devId = null;
        try { RumbleRequested?.Invoke(0, 0); } catch { }
        SetState(state, message);
    }

    private void SetState(VirtualState s, string? message)
    {
        StatusMessage = message;
        if (State == s) return;
        State = s;
        try { StateChanged?.Invoke(s); } catch { }
    }

    public void Dispose()
    {
        _stop = true;
        _want = false;
        _wake.Set();
        _supervisor.Join(3000);
        _wake.Dispose();
    }
}
