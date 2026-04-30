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
    /// <exception cref="OperationCanceledException">Propagated when <paramref name="ct"/> is cancelled.</exception>
    public async Task<CellInfoPacket> PollCellInfoAsync(CancellationToken ct)
    {
        _logger.LogDebug("Polling cell info for {Address}", DeviceAddress);

        var command = JkBmsProtocol.BuildCellInfoCommand();
        byte[] frame = await _transport.ExchangeAsync(command, ct);

        var data = JkBmsProtocol.GetData(frame);
        var packet = CellInfoPacket.Parse(data);

        _logger.LogDebug(
            "Cell info polled for {Address}: SOC={Soc}%, V={VoltageMv}mV, I={CurrentMa}mA",
            DeviceAddress, packet.StateOfChargePercent, packet.TotalVoltageMv, packet.CurrentMa);

        return packet;
    }

    /// <summary>
    /// Send the device-info command and return the parsed response.
    ///
    /// The transport connects on the first call and reconnects automatically if
    /// the connection was lost since the previous call.
    /// </summary>
    public async Task<DeviceInfoPacket> PollDeviceInfoAsync(CancellationToken ct)
    {
        _logger.LogDebug("Polling device info for {Address}", DeviceAddress);

        var command = JkBmsProtocol.BuildDeviceInfoCommand();
        byte[] frame = await _transport.ExchangeAsync(command, ct);

        var data = JkBmsProtocol.GetData(frame);
        return DeviceInfoPacket.Parse(data);
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
