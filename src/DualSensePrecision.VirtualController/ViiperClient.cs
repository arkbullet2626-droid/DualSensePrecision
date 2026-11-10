using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace DualSensePrecision.VirtualController;

public sealed class ViiperException(string message) : Exception(message);

/// <summary>
/// Minimal client for the VIIPER server's management API (https://github.com/Alia5/VIIPER, client protocol MIT).
/// Protocol: one TCP connection per request, "path[ payload]\0" in, one JSON response out, server closes.
/// Localhost connections need no authentication by default.
/// </summary>
public sealed class ViiperClient
{
    public const int DefaultPort = 3242;

    public string Host { get; }
    public int Port { get; }
    public int TimeoutMs { get; set; } = 1500;

    public ViiperClient(string host = "127.0.0.1", int port = DefaultPort)
    {
        Host = host;
        Port = port;
    }

    /// <summary>Returns the server version, or throws if VIIPER isn't reachable.</summary>
    public string Ping()
    {
        using var doc = Request("ping");
        var root = doc.RootElement;
        string server = root.TryGetProperty("server", out var s) ? s.GetString() ?? "" : "";
        if (!server.Contains("VIIPER", StringComparison.OrdinalIgnoreCase))
            throw new ViiperException($"Port {Port} answered, but it isn't a VIIPER server.");
        return root.TryGetProperty("version", out var v) ? v.GetString() ?? "unknown" : "unknown";
    }

    public uint CreateBus()
    {
        using var doc = Request("bus/create");
        return doc.RootElement.GetProperty("busId").GetUInt32();
    }

    public void RemoveBus(uint busId)
    {
        using var _ = Request("bus/remove", busId.ToString());
    }

    /// <summary>Adds a device and returns its device ID on the bus (e.g. "1").</summary>
    public string AddDevice(uint busId, string type)
    {
        using var doc = Request($"bus/{busId}/add", JsonSerializer.Serialize(new { type }));
        var id = doc.RootElement.GetProperty("devId");
        return id.ValueKind == JsonValueKind.String ? id.GetString()! : id.ToString();
    }

    /// <summary>Opens the long-lived binary stream for a device. Must happen within ~5 s of AddDevice.</summary>
    public Socket OpenStream(uint busId, string devId)
    {
        var s = Connect();
        try
        {
            s.Send(Encoding.ASCII.GetBytes($"bus/{busId}/{devId}\0"));
            s.ReceiveTimeout = 0; // stream reads block until rumble arrives
            return s;
        }
        catch
        {
            s.Dispose();
            throw;
        }
    }

    private JsonDocument Request(string path, string? payload = null)
    {
        using var s = Connect();
        string line = payload == null ? path : $"{path} {payload}";
        s.Send(Encoding.UTF8.GetBytes(line + "\0"));

        var buf = new byte[4096];
        using var ms = new MemoryStream();
        int n;
        while ((n = s.Receive(buf)) > 0) ms.Write(buf, 0, n);

        string text = Encoding.UTF8.GetString(ms.ToArray()).TrimEnd('\n', '\0', ' ');
        if (text.Length == 0) throw new ViiperException($"VIIPER returned an empty response to '{path}'.");

        JsonDocument doc;
        try { doc = JsonDocument.Parse(text); }
        catch (JsonException) { throw new ViiperException($"VIIPER returned an unexpected response to '{path}': {text}"); }

        // Errors are RFC 7807 problem objects: {"status":404,"title":"...","detail":"..."}
        if (doc.RootElement.ValueKind == JsonValueKind.Object &&
            doc.RootElement.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.Number && st.GetInt32() >= 400)
        {
            string title = doc.RootElement.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
            string detail = doc.RootElement.TryGetProperty("detail", out var d) ? d.GetString() ?? "" : "";
            doc.Dispose();
            throw new ViiperException($"VIIPER: {title} {detail}".Trim());
        }
        return doc;
    }

    private string NotRunning => $"VIIPER isn't running (nothing answered on port {Port}).";

    private Socket Connect()
    {
        var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true,
            SendTimeout = TimeoutMs,
            ReceiveTimeout = TimeoutMs,
        };
        try
        {
            var connect = s.ConnectAsync(Host, Port);
            // On Windows a refused localhost connection can take ~2 s to surface (TCP retries), so a timeout
            // here almost always means nothing is listening, not a slow server.
            if (!connect.Wait(TimeoutMs)) throw new ViiperException(NotRunning);
            return s;
        }
        catch (AggregateException ex) when (ex.InnerException is SocketException)
        {
            s.Dispose();
            throw new ViiperException(NotRunning);
        }
        catch
        {
            s.Dispose();
            throw;
        }
    }
}
