using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using DualSensePrecision.Core;
using DualSensePrecision.InputEngine;
using DualSensePrecision.VirtualController;
using Xunit;

namespace DualSensePrecision.Tests;

public class Xbox360EncodingTests
{
    [Fact]
    public void NeutralStateIsAllZeros()
    {
        var buf = new byte[20];
        ViiperXbox360Backend.Encode(default, buf);
        Assert.All(buf, b => Assert.Equal(0, b));
    }

    [Fact]
    public void AxesAndTriggersUseFullRange()
    {
        var p = new ProcessedState { LeftX = 1f, LeftY = -1f, RightX = -0.5f, RightY = 0.25f, L2 = 1f, R2 = 0.5f };
        var buf = new byte[20];
        ViiperXbox360Backend.Encode(p, buf);
        Assert.Equal(255, buf[4]);
        Assert.Equal(128, buf[5]);
        Assert.Equal(32767, BinaryPrimitives.ReadInt16LittleEndian(buf.AsSpan(6)));
        Assert.Equal(-32767, BinaryPrimitives.ReadInt16LittleEndian(buf.AsSpan(8)));   // Y up positive on both sides
        Assert.Equal(-16384, BinaryPrimitives.ReadInt16LittleEndian(buf.AsSpan(10)));
        Assert.Equal(8192, BinaryPrimitives.ReadInt16LittleEndian(buf.AsSpan(12)));
    }

    [Fact]
    public void ButtonsMapToXboxLayout()
    {
        uint x = ViiperXbox360Backend.MapButtons(DsButtons.Cross | DsButtons.Circle | DsButtons.Square | DsButtons.Triangle |
                                                 DsButtons.Options | DsButtons.Create | DsButtons.PS | DsButtons.L1 | DsButtons.R3 | DsButtons.DpadLeft);
        Assert.Equal(ViiperXbox360Backend.XA | ViiperXbox360Backend.XB | ViiperXbox360Backend.XX | ViiperXbox360Backend.XY |
                     ViiperXbox360Backend.XStart | ViiperXbox360Backend.XBack | ViiperXbox360Backend.XGuide |
                     ViiperXbox360Backend.XLeftShoulder | ViiperXbox360Backend.XRightThumb | ViiperXbox360Backend.XDpadLeft, x);
    }

    [Fact]
    public void AnalogOnlyInputsHaveNoButtonBits()
    {
        Assert.Equal(0u, ViiperXbox360Backend.MapButtons(DsButtons.L2 | DsButtons.R2 | DsButtons.Mute));
    }
}

/// <summary>A tiny in-process stand-in for the VIIPER server, speaking its documented protocol.</summary>
internal sealed class FakeViiper : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _cts = new();
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public ConcurrentQueue<string> Requests { get; } = new();
    public BlockingCollection<byte[]> Packets { get; } = new();
    public Socket? Stream { get; private set; }
    public ManualResetEventSlim StreamOpened { get; } = new();

    public FakeViiper()
    {
        _listener.Start();
        Task.Run(AcceptLoop);
    }

    private async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            Socket s;
            try { s = await _listener.AcceptSocketAsync(_cts.Token); } catch { return; }
            _ = Task.Run(() => Handle(s));
        }
    }

    private void Handle(Socket s)
    {
        var sb = new StringBuilder();
        var one = new byte[1];
        while (s.Receive(one) == 1 && one[0] != 0) sb.Append((char)one[0]);
        string req = sb.ToString();
        Requests.Enqueue(req);

        string? reply = req switch
        {
            "ping" => """{"server":"VIIPER","version":"test-1.0"}""",
            "bus/create" => """{"busId":7}""",
            _ when req.StartsWith("bus/remove") => """{"busId":7}""",
            _ when req.StartsWith("bus/7/add ") => """{"busId":7,"devId":"1","vid":"0x045e","pid":"0x028e","type":"xbox360"}""",
            "bus/7/1" => null, // stream handshake: keep the socket open
            _ => """{"status":404,"title":"Not Found","detail":"unknown path"}""",
        };

        if (reply != null)
        {
            s.Send(Encoding.UTF8.GetBytes(reply + "\n"));
            s.Shutdown(SocketShutdown.Both);
            s.Dispose();
            return;
        }

        Stream = s;
        StreamOpened.Set();
        var buf = new byte[20];
        try
        {
            while (true)
            {
                int got = 0;
                while (got < 20)
                {
                    int n = s.Receive(buf, got, 20 - got, SocketFlags.None);
                    if (n <= 0) return;
                    got += n;
                }
                Packets.Add((byte[])buf.Clone());
            }
        }
        catch { }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        try { Stream?.Dispose(); } catch { }
    }
}

public class ViiperProtocolTests
{
    [Fact]
    public void ClientParsesResponsesAndErrors()
    {
        using var server = new FakeViiper();
        var c = new ViiperClient(port: server.Port);
        Assert.Equal("test-1.0", c.Ping());
        Assert.Equal(7u, c.CreateBus());
        Assert.Equal("1", c.AddDevice(7, "xbox360"));
        Assert.Contains(server.Requests, r => r == """bus/7/add {"type":"xbox360"}""");
        var ex = Assert.Throws<ViiperException>(() => c.AddDevice(99, "xbox360"));
        Assert.Contains("Not Found", ex.Message);
    }

    [Fact]
    public void ReportsMissingServerClearly()
    {
        // Grab a free port, then close it so nothing is listening.
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        var ex = Assert.Throws<ViiperException>(() => new ViiperClient(port: port) { TimeoutMs = 500 }.Ping());
        Assert.Contains("isn't running", ex.Message);
    }

    [Fact]
    public void BackendStreamsInputAndForwardsRumble()
    {
        using var server = new FakeViiper();
        using var backend = new ViiperXbox360Backend(new ViiperClient(port: server.Port));
        var rumble = new BlockingCollection<(byte, byte)>();
        backend.RumbleRequested += (l, r) => rumble.Add((l, r));

        backend.Connect();
        Assert.True(server.StreamOpened.Wait(TimeSpan.FromSeconds(5)), "stream was never opened");
        SpinWait.SpinUntil(() => backend.State == VirtualState.Connected, TimeSpan.FromSeconds(5));
        Assert.Equal(VirtualState.Connected, backend.State);

        // First packet is the neutral state sent on connect.
        Assert.True(server.Packets.TryTake(out var neutral, TimeSpan.FromSeconds(2)));
        Assert.All(neutral!, b => Assert.Equal(0, b));

        backend.Submit(new ProcessedState { LeftX = 1f, Buttons = DsButtons.Cross });
        Assert.True(server.Packets.TryTake(out var pkt, TimeSpan.FromSeconds(2)));
        Assert.Equal(ViiperXbox360Backend.XA, BinaryPrimitives.ReadUInt32LittleEndian(pkt));
        Assert.Equal(32767, BinaryPrimitives.ReadInt16LittleEndian(pkt.AsSpan(6)));
        Assert.True(backend.PacketsSent >= 2);

        // Game rumble: server → client 2-byte packet.
        server.Stream!.Send(new byte[] { 200, 50 });
        Assert.True(rumble.TryTake(out var rm, TimeSpan.FromSeconds(2)));
        Assert.Equal(((byte)200, (byte)50), rm);

        // Switching off removes the bus so the virtual pad disappears.
        backend.Disconnect();
        SpinWait.SpinUntil(() => server.Requests.Contains("bus/remove 7"), TimeSpan.FromSeconds(5));
        Assert.Contains("bus/remove 7", server.Requests);
        Assert.False(backend.IsConnected);
    }

    [Fact]
    public void BackendReportsServerMissing()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        using var backend = new ViiperXbox360Backend(new ViiperClient(port: port) { TimeoutMs = 300 });
        backend.Connect();
        SpinWait.SpinUntil(() => backend.State == VirtualState.ServerMissing, TimeSpan.FromSeconds(5));
        Assert.Equal(VirtualState.ServerMissing, backend.State);
        backend.Submit(default); // must be a harmless no-op while disconnected
    }
}
