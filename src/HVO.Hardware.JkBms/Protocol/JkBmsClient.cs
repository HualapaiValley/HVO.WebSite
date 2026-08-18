using HVO.Hardware.JkBms.Protocol.Packets;
using HVO.Hardware.JkBms.Protocol.Transport;
using Linux.Bluetooth;
using Microsoft.Extensions.Logging;

namespace HVO.Hardware.JkBms.Protocol;

/// <summary>
/// High-level client for a single JK BMS device.
///
/// Lifecycle:
///   This client is long-lived — create one instance per device at startup and
///   hold it for the application lifetime. The caller must use
///   <see cref="ConnectWithDeviceAsync"/> with a coordinator-resolved BlueZ
///   <see cref="Device"/> before issuing polls. Call <see cref="DisconnectAsync"/>
///   when the caller needs to release the BLE session.
///
///   Dispose the client (via <see cref="DisposeAsync"/>) when the application shuts
///   down to cleanly close the BLE connection.
///
/// Thread safety: serialise all calls — do not invoke this client concurrently.
/// </summary>
public sealed class JkBmsClient : IAsyncDisposable
{
    private readonly IBmsTransport _transport;
    private readonly ILogger<JkBmsClient> _logger;
    private bool _disposed;

    /// <summary>Bluetooth MAC address of the bound device.</summary>
    public string DeviceAddress => _transport.DeviceAddress;

    public JkBmsClient(IBmsTransport transport, ILogger<JkBmsClient> logger)
    {
        _transport = transport;
        _logger = logger;
    }

    /// <summary>True if the underlying BLE transport is currently connected.</summary>
    public bool IsConnected => _transport.IsConnected;

    /// <summary>
    /// Complete GATT setup on a pre-connected BlueZ Device supplied by the scan loop.
    /// Delegates directly to <see cref="IBmsTransport.ConnectWithDeviceAsync"/>.
    /// </summary>
    public Task ConnectWithDeviceAsync(Device device, CancellationToken ct) =>
        _transport.ConnectWithDeviceAsync(device, ct);

    /// <summary>
    /// Explicitly release the BLE connection. Polling remains unavailable until the
    /// caller establishes a new session with <see cref="ConnectWithDeviceAsync"/>.
    /// </summary>
    public Task DisconnectAsync() => _transport.DisconnectAsync();

    /// <summary>
    /// Send the cell-info command and return the parsed response.
    ///
    /// Requires an active transport connection established by
    /// <see cref="ConnectWithDeviceAsync"/>.
    /// </summary>
    /// <exception cref="JkBmsConnectException">Thrown when the device cannot be reached.</exception>
    /// <exception cref="JkBmsTimeoutException">Thrown when the device does not respond in time.</exception>
    /// <exception cref="OperationCanceledException">Propagated when <paramref name="ct"/> is cancelled.</exception>
    public async Task<CellInfoPacket> PollCellInfoAsync(CancellationToken ct)
    {
        _logger.LogDebug("Polling cell info for {Address}", DeviceAddress);

        // Send the cell-info command and accept the response frame.
        // NOTE: The JK BMS FFE1 characteristic requires GATT Write Command (write-without-response).
        // The transport is configured with {"type","command"} in WriteValueAsync options.
        byte[] frame = await _transport.ExchangeAsync(JkBmsProtocol.BuildCellInfoCommand(), ct);
        byte frameType = JkBmsProtocol.GetFrameType(frame);
        var data = JkBmsProtocol.GetData(frame);

        // Always log frame type + first 40 data bytes (hex) for protocol analysis.
        _logger.LogDebug(
            "Cell info response from {Address}: FrameType=0x{FrameType:X2} Data[0..39]={DataHex}",
            DeviceAddress, frameType,
            Convert.ToHexString(data[..Math.Min(40, data.Length)]));

        if (frameType != JkBmsProtocol.FrameTypeCellInfo)
            _logger.LogWarning(
                "Expected cell info frame type 0x{Expected:X2} from {Address} but received 0x{Actual:X2}; " +
                "parsing anyway — data may have different offsets on this firmware",
                JkBmsProtocol.FrameTypeCellInfo, DeviceAddress, frameType);

        var packet = CellInfoPacket.Parse(data);

        _logger.LogDebug(
            "Cell info parsed for {Address}: SOC={Soc}%, V={VoltageMv}mV, I={CurrentMa}mA",
            DeviceAddress, packet.StateOfChargePercent, packet.TotalVoltageMv, packet.CurrentMa);

        return packet;
    }

    /// <summary>
    /// Send the device-info command and return the parsed response.
    ///
    /// Requires an active transport connection established by
    /// <see cref="ConnectWithDeviceAsync"/>.
    /// </summary>
    public async Task<DeviceInfoPacket> PollDeviceInfoAsync(CancellationToken ct)
    {
        _logger.LogDebug("Polling device info for {Address}", DeviceAddress);

        byte[] frame = await _transport.ExchangeAsync(JkBmsProtocol.BuildDeviceInfoCommand(), ct);
        byte frameType = JkBmsProtocol.GetFrameType(frame);
        var data = JkBmsProtocol.GetData(frame);

        _logger.LogDebug(
            "Device info response from {Address}: FrameType=0x{FrameType:X2} Data[0..39]={DataHex}",
            DeviceAddress, frameType,
            Convert.ToHexString(data[..Math.Min(40, data.Length)]));

        if (frameType != JkBmsProtocol.FrameTypeDeviceInfo)
            _logger.LogWarning(
                "Expected device info frame type 0x{Expected:X2} from {Address} but received 0x{Actual:X2}; " +
                "parsing anyway — data may have different offsets on this firmware",
                JkBmsProtocol.FrameTypeDeviceInfo, DeviceAddress, frameType);

        return DeviceInfoPacket.Parse(data);
    }

    public async Task ChangeSettingsPasswordAsync(string password, CancellationToken ct)
    {
        _logger.LogInformation("Changing JK BMS settings password for {Address}", DeviceAddress);
        var acknowledgement = await _transport.ExchangeAcknowledgedAsync(
            JkBmsProtocol.BuildSetSettingsPasswordCommand(password), ct);
        if (acknowledgement[6] != 0x01)
            throw new JkBmsFrameException($"JK BMS at {DeviceAddress} rejected the settings-password change.");
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            await _transport.DisposeAsync();
        }
    }

    /// <summary>
    /// Returns the <see cref="SettingsPacket"/> parsed from the most recently captured
    /// spontaneous settings frame (type 0x01), or <see langword="null"/> if no valid
    /// settings frame has been received on the current connection.
    /// </summary>
    public SettingsPacket? GetLatestSettings()
    {
        var frame = _transport.LastSettingsFrame;
        if (frame is null) return null;
        try
        {
            var data = JkBmsProtocol.GetData(frame);
            return SettingsPacket.Parse(data);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse cached settings frame for {Address}", DeviceAddress);
            return null;
        }
    }
}
