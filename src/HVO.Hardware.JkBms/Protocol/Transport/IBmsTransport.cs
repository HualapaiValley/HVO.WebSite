using Linux.Bluetooth;

namespace HVO.Hardware.JkBms.Protocol.Transport;

/// <summary>
/// Abstraction over the BLE transport layer for a single JK BMS device.
/// </summary>
public interface IBmsTransport : IAsyncDisposable
{
    string DeviceAddress { get; }
    bool IsConnected { get; }
    byte[]? LastSettingsFrame { get; }

    /// <summary>
    /// Complete GATT setup on an already-connected BlueZ Device object.
    /// Called by the scan loop immediately after a DeviceFound event fires,
    /// while the advertising report is still live.
    /// </summary>
    Task ConnectWithDeviceAsync(Device device, CancellationToken ct);

    /// <summary>Gracefully close the BLE connection (best-effort; does not throw).</summary>
    Task DisconnectAsync();

    Task<byte[]> ExchangeAsync(byte[] command, CancellationToken ct);
}
