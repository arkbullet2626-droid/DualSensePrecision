namespace DualSensePrecision.Core;

public enum ConnectionStatus { Disconnected, Connecting, Connected, Error }

public sealed record ControllerInfo(
    DualSenseModel Model,
    string Name,
    ushort VendorId,
    ushort ProductId,
    string DevicePath,
    string? SerialOrMac,
    string? FirmwareVersion,
    string? HardwareVersion,
    string? FirmwareBuildDate,
    int InputReportLength,
    int OutputReportLength,
    bool IsMock);

/// <summary>
/// Anything that delivers controller reports: the real USB DualSense or the clearly-labelled mock.
/// Events fire on the reader thread — handlers must be fast and must not touch UI.
/// </summary>
public interface IControllerSource : IDisposable
{
    ConnectionStatus Status { get; }
    ControllerInfo? Info { get; }
    string? LastError { get; }

    /// <summary>Raised on the reader thread for every report. Keep it cheap.</summary>
    event ReportHandler? ReportReceived;
    event Action<ConnectionStatus>? StatusChanged;

    void Start();
    void Stop();

    /// <summary>Sends a raw output report (report ID at [0]). Returns false if not connected.</summary>
    bool SendOutputReport(ReadOnlySpan<byte> report);
}

public delegate void ReportHandler(in ControllerState state, ReadOnlySpan<byte> raw);
