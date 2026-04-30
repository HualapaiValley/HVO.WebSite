using HVO.Hardware.JkBms.Protocol.Packets;
using HVO.Hardware.JkBms.Protocol.Transport;
using Microsoft.Extensions.Logging;

namespace HVO.Hardware.JkBms.Protocol;

/// <summary>
/// High-level client for a single JK BMS device.
///
/// Lifecycle:
///   This client is long-lived — create one instance per device at startup and
///   hold it for the application lifetime. The underlying <see cref="IBmsTransport"/>
///   maintains a persistent BLE connection; it connects on the first exchange and
///   reconnects automatically if the connection is lost between calls.
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

    /// <summary>
    /// Send the cell-info command and return the parsed response.
    ///
    /// The transport connects on the first call and reconnects automatically if
    /// the connection was lost since the previous call.
    /// </summary>
    /// <exception cref="JkBmsConnectException">Thrown when the device cannot be reached.</exception>
    /// <exception cref="JkBmsTimeoutException">Thrown when the device does not respond in time.</exception>
    /// <exception cref="JkBmsFrameException">Thrown when the device persistently sends wrong frame types.</exception>
    /// <exception cref="OperationCanceledException">Propagated when <paramref name="ct"/> is cancelled.</exception>
    public async Task<CellInfoPacket> PollCellInfoAsync(CancellationToken ct)
    {
        _logger.LogDebug("Polling cell info for {Address}", DeviceAddress);

        // The BMS sends a spontaneous settings frame (type 0x01) before the cell info
        // response (type 0x02). We send the command ONCE then drain frames from the
        // notification stream until we get the expected type. Sending repeated commands
        // causes the BMS to restart its burst sequence, yielding only settings frames.
        const int MaxAttempts = 5;
        byte[] frame = await _transport.ExchangeAsync(JkBmsProtocol.BuildCellInfoCommand(), ct);

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            byte frameType = JkBmsProtocol.GetFrameType(frame);

            if (frameType == JkBmsProtocol.FrameTypeCellInfo)
            {
                var data = JkBmsProtocol.GetData(frame);
                var packet = CellInfoPacket.Parse(data);

                _logger.LogDebug(
                    "Cell info polled for {Address}: SOC={Soc}%, V={VoltageMv}mV, I={CurrentMa}mA",
                    DeviceAddress, packet.StateOfChargePercent, packet.TotalVoltageMv, packet.CurrentMa);

                return packet;
            }

            _logger.LogDebug(
                "Expected cell info frame (0x{Expected:X2}) from {Address} but received 0x{Actual:X2}; " +
                "reading next frame (attempt {Attempt}/{Max})",
                JkBmsProtocol.FrameTypeCellInfo, DeviceAddress, frameType, attempt, MaxAttempts);

            if (attempt < MaxAttempts)
                frame = await _transport.ReadNextFrameAsync(ct);
        }

        throw new JkBmsFrameException(
            $"Failed to receive cell info frame from {DeviceAddress} after {MaxAttempts} attempt(s).");
    }

    /// <summary>
    /// Send the device-info command and return the parsed response.
    ///
    /// The transport connects on the first call and reconnects automatically if
    /// the connection was lost since the previous call.
    /// </summary>
    /// <exception cref="JkBmsFrameException">Thrown when the device persistently sends wrong frame types.</exception>
    public async Task<DeviceInfoPacket> PollDeviceInfoAsync(CancellationToken ct)
    {
        _logger.LogDebug("Polling device info for {Address}", DeviceAddress);

        const int MaxAttempts = 5;
        byte[] frame = await _transport.ExchangeAsync(JkBmsProtocol.BuildDeviceInfoCommand(), ct);

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            byte frameType = JkBmsProtocol.GetFrameType(frame);

            if (frameType == JkBmsProtocol.FrameTypeDeviceInfo)
            {
                var data = JkBmsProtocol.GetData(frame);
                return DeviceInfoPacket.Parse(data);
            }

            _logger.LogDebug(
                "Expected device info frame (0x{Expected:X2}) from {Address} but received 0x{Actual:X2}; " +
                "reading next frame (attempt {Attempt}/{Max})",
                JkBmsProtocol.FrameTypeDeviceInfo, DeviceAddress, frameType, attempt, MaxAttempts);

            if (attempt < MaxAttempts)
                frame = await _transport.ReadNextFrameAsync(ct);
        }

        throw new JkBmsFrameException(
            $"Failed to receive device info frame from {DeviceAddress} after {MaxAttempts} attempt(s).");
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            await _transport.DisposeAsync();
        }
    }
}
