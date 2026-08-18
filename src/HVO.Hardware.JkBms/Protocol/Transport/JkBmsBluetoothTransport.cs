using System.Diagnostics;
using System.Threading.Channels;
using Linux.Bluetooth;
using Linux.Bluetooth.Extensions;
using Microsoft.Extensions.Logging;

namespace HVO.Hardware.JkBms.Protocol.Transport;

/// <summary>
/// BLE transport for JK BMS devices using Linux.Bluetooth (BlueZ D-Bus).
///
/// Connection lifecycle:
///   <see cref="ConnectWithDeviceAsync"/> is called with a coordinator-resolved BlueZ
///   Device while adapter discovery is active for the target. This transport completes
///   the GATT setup (ServicesResolved, characteristic discovery, notifications).
///   <see cref="ExchangeAsync"/> requires that established connection; if the link drops,
///   the caller must request a new coordinator-managed session.
///
/// Thread safety: serialise all calls — do not invoke concurrently.
/// </summary>
internal sealed class JkBmsBluetoothTransport : IBmsTransport
{
    private const int MaxConnectAttempts = 3;
    private readonly ILogger<JkBmsBluetoothTransport> _logger;
    private readonly TimeSpan _connectTimeout;
    private readonly TimeSpan _exchangeTimeout;

    private Device? _device;
    private IGattCharacteristic1? _writeCharacteristic;
    private IGattCharacteristic1? _notifyCharacteristic;
    private IDisposable? _notifyWatcher;
    private bool _isConnected;
    private bool _disposed;

    private readonly object _rxBufferLock = new();
    private readonly List<byte> _rxBuffer = [];
    private readonly Channel<byte[]> _frameChannel =
        Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Channel<byte[]> _acknowledgementChannel =
        Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });

    public string DeviceAddress { get; }
    public bool IsConnected => _isConnected && _writeCharacteristic != null && _notifyCharacteristic != null;
    public byte[]? LastSettingsFrame { get; private set; }

    public JkBmsBluetoothTransport(
        string deviceAddress,
        TimeSpan connectTimeout,
        TimeSpan exchangeTimeout,
        ILogger<JkBmsBluetoothTransport> logger)
    {
        DeviceAddress = deviceAddress;
        _connectTimeout = connectTimeout;
        _exchangeTimeout = exchangeTimeout;
        _logger = logger;
    }

    // ── ConnectWithDeviceAsync ────────────────────────────────────────────────

    /// <summary>
    /// Complete GATT setup on an already-connected BlueZ Device object supplied
    /// by the adapter coordinator. The device is connected at the HCI level; this method
    /// waits for ServicesResolved then discovers characteristics and starts notifications.
    /// </summary>
    public async Task ConnectWithDeviceAsync(Device device, CancellationToken ct)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(JkBmsBluetoothTransport));

        LastSettingsFrame = null;
        _device = device;

        _logger.LogDebug("BLE GATT setup starting for {Address}", DeviceAddress);

        try
        {
            // Initiate the HCI-level connection while adapter discovery is still active.
            // BlueZ is more reliable when the target has been observed recently and the
            // coordinator keeps discovery running through this call.
            _logger.LogInformation("BLE calling ConnectAsync on {Address}", DeviceAddress);
            try
            {
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                connectCts.CancelAfter(_connectTimeout);
                await _device.ConnectAsync().WaitAsync(connectCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                await DisconnectAsync();
                throw new JkBmsConnectException(DeviceAddress, MaxConnectAttempts,
                    new TimeoutException($"Device.ConnectAsync() timed out after {_connectTimeout.TotalSeconds}s for {DeviceAddress}."));
            }
            catch (Exception ex)
            {
                await DisconnectAsync();
                throw new JkBmsConnectException(DeviceAddress, MaxConnectAttempts,
                    new InvalidOperationException($"Device.ConnectAsync() failed for {DeviceAddress}.", ex));
            }

            _logger.LogInformation("BLE waiting for Connected=true on {Address}", DeviceAddress);
            try
            {
                await WaitForDevicePropertyAsync(
                    () => _device.GetConnectedAsync(),
                    "Connected",
                    TimeSpan.FromSeconds(8),
                    ct);
            }
            catch (Exception ex)
            {
                await DisconnectAsync();
                throw new JkBmsConnectException(DeviceAddress, MaxConnectAttempts,
                    new TimeoutException("Timed out waiting for BlueZ Device1.Connected=true.", ex));
            }

            _logger.LogInformation("BLE waiting for ServicesResolved=true on {Address}", DeviceAddress);
            try
            {
                await WaitForDevicePropertyAsync(
                    () => _device.GetServicesResolvedAsync(),
                    "ServicesResolved",
                    TimeSpan.FromSeconds(20),
                    ct);
            }
            catch (Exception ex)
            {
                await DisconnectAsync();
                throw new JkBmsConnectException(DeviceAddress, MaxConnectAttempts,
                    new TimeoutException("Timed out waiting for BlueZ Device1.ServicesResolved=true.", ex));
            }

            var serviceUuid = JkBmsProtocol.ServiceUuid.ToString();
            var service = await _device.GetServiceAsync(serviceUuid)
                ?? throw new JkBmsConnectException(DeviceAddress, MaxConnectAttempts,
                    new InvalidOperationException($"JK BMS service {serviceUuid} not found."));

            var charUuid = JkBmsProtocol.CharacteristicUuid.ToString();
            var allServiceChars = await GattExtensions.GetCharacteristicsAsync(service) ?? [];
            if (allServiceChars.Count == 0)
                throw new JkBmsConnectException(DeviceAddress, MaxConnectAttempts,
                    new InvalidOperationException($"JK BMS service {serviceUuid} has no characteristics."));

            _notifyCharacteristic = null;
            foreach (var c in allServiceChars)
            {
                var cUuid = await c.GetUUIDAsync();
                var flags = await c.GetFlagsAsync();
                _logger.LogDebug("  Service char UUID={Uuid} Flags=[{Flags}]",
                    cUuid, string.Join(", ", flags ?? []));
                if (string.Equals(cUuid, charUuid, StringComparison.OrdinalIgnoreCase))
                    _notifyCharacteristic ??= c;
            }

            _notifyCharacteristic ??= allServiceChars.FirstOrDefault();
            _writeCharacteristic = _notifyCharacteristic;

            if (_notifyCharacteristic == null)
                throw new JkBmsConnectException(DeviceAddress, MaxConnectAttempts,
                    new InvalidOperationException($"JK BMS characteristic {charUuid} not found."));

            _notifyWatcher = await _notifyCharacteristic.WatchPropertiesAsync(OnPropertyChanged);
            await _notifyCharacteristic.StartNotifyAsync();

            _isConnected = true;

            _logger.LogDebug("BLE sending device-info init (0x97) to {Address}", DeviceAddress);
            try
            {
                using var initCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                initCts.CancelAfter(_exchangeTimeout);
                var initFrame = await DoExchangeAsync(JkBmsProtocol.BuildDeviceInfoCommand(), initCts.Token);
                _logger.LogDebug(
                    "BLE device-info init response from {Address}: FrameType=0x{Type:X2}",
                    DeviceAddress, initFrame[4]);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "BLE device-info init (0x97) did not complete for {Address}; continuing",
                    DeviceAddress);
            }

            _logger.LogInformation("BLE connected to {Address}", DeviceAddress);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new JkBmsTimeoutException(DeviceAddress);
        }
    }

    private static async Task WaitForDevicePropertyAsync(
        Func<Task<bool>> readProperty,
        string propertyName,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            if (await readProperty())
                return;

            var delay = deadline - DateTime.UtcNow;
            if (delay > TimeSpan.FromMilliseconds(500))
                delay = TimeSpan.FromMilliseconds(500);
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, ct);
        }

        if (await readProperty())
            return;

        throw new TimeoutException($"Timed out waiting for '{propertyName}' to become true.");
    }

    // ── DisconnectAsync ───────────────────────────────────────────────────────

    public async Task DisconnectAsync()
    {
        _isConnected = false;

        if (_notifyCharacteristic != null)
        {
            try
            {
                _notifyWatcher?.Dispose();
                _notifyWatcher = null;
                await _notifyCharacteristic.StopNotifyAsync();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Non-fatal error stopping BLE notifications for {Address}", DeviceAddress);
            }
            _notifyCharacteristic = null;
            _writeCharacteristic = null;
        }

        if (_device != null)
        {
            try
            {
                await _device.DisconnectAsync();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Non-fatal error disconnecting BLE for {Address}", DeviceAddress);
            }
            _device = null;
        }
    }

    // ── ExchangeAsync ─────────────────────────────────────────────────────────

    public async Task<byte[]> ExchangeAsync(byte[] command, CancellationToken ct)
    {
        if (!IsConnected)
            throw new JkBmsConnectException(DeviceAddress, 0,
                new InvalidOperationException("Transport is not connected. Wait for the adapter coordinator to establish a new BLE session."));

        try
        {
            return await DoExchangeAsync(command, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "BLE exchange failed for {Address} ({Message}); disconnecting",
                DeviceAddress, ex.Message);
            await DisconnectAsync();
            throw;
        }
    }

    public async Task<byte[]> ExchangeAcknowledgedAsync(byte[] command, CancellationToken ct)
    {
        if (!IsConnected)
            throw new JkBmsConnectException(DeviceAddress, 0,
                new InvalidOperationException("Transport is not connected. Wait for the adapter coordinator to establish a new BLE session."));

        while (_acknowledgementChannel.Reader.TryRead(out _)) { }
        try
        {
            await _writeCharacteristic!.WriteValueAsync(command,
                new Dictionary<string, object> { { "type", "command" } });
            _logger.LogTrace("BLE write {Bytes} bytes to {Address}", command.Length, DeviceAddress);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_exchangeTimeout);
            byte[] acknowledgement;
            try
            {
                acknowledgement = await _acknowledgementChannel.Reader.ReadAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new JkBmsTimeoutException(DeviceAddress);
            }

            if (!JkBmsProtocol.ValidateAcknowledgement(acknowledgement))
                throw new JkBmsFrameException($"Invalid write acknowledgement from {DeviceAddress}.");
            return acknowledgement;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "BLE acknowledged exchange failed for {Address}; disconnecting", DeviceAddress);
            await DisconnectAsync();
            throw;
        }
    }

    private async Task<byte[]> DoExchangeAsync(byte[] command, CancellationToken ct)
    {
        lock (_rxBufferLock) { _rxBuffer.Clear(); }
        while (_frameChannel.Reader.TryRead(out _)) { }

        await _writeCharacteristic!.WriteValueAsync(command,
            new Dictionary<string, object> { { "type", "command" } });
        _logger.LogTrace("BLE write {Bytes} bytes to {Address}", command.Length, DeviceAddress);

        byte expectedType = GetExpectedResponseFrameType(command);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_exchangeTimeout);

        while (true)
        {
            byte[] frame;
            try
            {
                frame = await _frameChannel.Reader.ReadAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new JkBmsTimeoutException(DeviceAddress);
            }

            if (expectedType != 0 && frame[4] != expectedType)
            {
                bool crcValid = JkBmsProtocol.ValidateCrc(frame);
                if (frame[4] == JkBmsProtocol.FrameTypeSettings && crcValid)
                    LastSettingsFrame = frame;

                _logger.LogDebug(
                    "Discarding spontaneous frame type 0x{Actual:X2} (CRC valid: {CrcValid}) while waiting for 0x{Expected:X2} from {Address}",
                    frame[4], crcValid, expectedType, DeviceAddress);
                continue;
            }

            if (!JkBmsProtocol.ValidateCrc(frame))
            {
                var computed = CrcByteSum.Compute(frame.AsSpan(0, frame.Length - 1));
                _logger.LogDebug(
                    "CRC mismatch for 0x{Type:X2} frame from {Address}: computed=0x{Computed:X2} expected=0x{Expected:X2} len={Len}",
                    frame.Length > 4 ? frame[4] : (byte)0, DeviceAddress, computed, frame[^1], frame.Length);
                throw new JkBmsCrcException(
                    $"CRC validation failed for response from {DeviceAddress}.");
            }

            return frame;
        }
    }

    private static byte GetExpectedResponseFrameType(byte[] command) =>
        command.Length > 4 ? command[4] switch
        {
            0x95 => JkBmsProtocol.FrameTypeSettings,
            0x96 => JkBmsProtocol.FrameTypeCellInfo,
            0x97 => JkBmsProtocol.FrameTypeDeviceInfo,
            _ => (byte)0,
        } : (byte)0;

    private void OnPropertyChanged(Tmds.DBus.PropertyChanges changes)
    {
        foreach (var pair in changes.Changed)
        {
            if (pair.Key == "Value" && pair.Value is byte[] value)
            {
                if (JkBmsProtocol.ValidateAcknowledgement(value))
                {
                    _ = _acknowledgementChannel.Writer.TryWrite(value);
                    continue;
                }

                byte[]? frame = null;
                lock (_rxBufferLock)
                {
                    if (JkBmsProtocol.TryAccumulateFrame(_rxBuffer, value.AsSpan(), out var completedFrame))
                        frame = completedFrame;
                }

                if (frame is not null)
                    _ = _frameChannel.Writer.TryWrite(frame);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            await DisconnectAsync();
            _frameChannel.Writer.TryComplete();
            _acknowledgementChannel.Writer.TryComplete();
        }
    }
}
