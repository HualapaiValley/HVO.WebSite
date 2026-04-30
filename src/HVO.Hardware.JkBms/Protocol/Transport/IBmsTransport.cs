namespace HVO.Hardware.JkBms.Protocol.Transport;

/// <summary>
/// Abstraction over the BLE transport layer for a single JK BMS device.
/// Implementations must be single-use: create a new instance per poll cycle
/// (or at least after a disconnect). Thread-safety within a single instance
/// is not required — the caller serialises access via a semaphore.
/// </summary>
public interface IBmsTransport : IAsyncDisposable
{
    /// <summary>Bluetooth MAC address of the device (e.g. "C8:47:8C:E4:58:37").</summary>
    string DeviceAddress { get; }

    /// <summary>True if the BLE connection is currently established.</summary>
    bool IsConnected { get; }

    /// <summary>
    /// Establish the BLE connection to the device.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="JkBmsConnectException">If connection cannot be established.</exception>
    Task ConnectAsync(CancellationToken ct);

    /// <summary>Gracefully close the BLE connection (best-effort; does not throw).</summary>
    Task DisconnectAsync();

    /// <summary>
    /// Write <paramref name="command"/> to the BLE characteristic and wait for
    /// the complete multi-chunk response to be reassembled into a single frame.
    /// </summary>
    /// <returns>Complete raw frame bytes (header + data + CRC).</returns>
    /// <exception cref="JkBmsTimeoutException">If the response is not received in time.</exception>
    /// <exception cref="JkBmsFrameException">If the assembled frame is structurally invalid.</exception>
    Task<byte[]> ExchangeAsync(byte[] command, CancellationToken ct);
}
