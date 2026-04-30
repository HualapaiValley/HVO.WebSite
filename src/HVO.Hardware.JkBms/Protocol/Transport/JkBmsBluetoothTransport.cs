using System.Diagnostics;
using Linux.Bluetooth;
using Linux.Bluetooth.Extensions;
using Microsoft.Extensions.Logging;

namespace HVO.Hardware.JkBms.Protocol.Transport;

/// <summary>
/// BLE transport for JK BMS devices using <c>Linux.Bluetooth</c> (BlueZ D-Bus).
///
/// Uses Linux.Bluetooth directly rather than InTheHand.BluetoothLE so that the
/// GATT connection is made without triggering BlueZ pairing — JK BMS devices
/// use open BLE connections and do not support bonding.
///
/// Connection lifecycle:
///   This transport is long-lived — create one instance per device and reuse it.
///   <see cref="ConnectAsync"/> establishes the initial connection.
///   <see cref="ExchangeAsync"/> connects on the first call if not yet connected,
///   and reconnects automatically if the connection was lost between calls.
///   <see cref="DisposeAsync"/> tears down the connection.
///
/// Thread safety: serialise all calls — do not invoke this transport concurrently.
/// </summary>
internal sealed class JkBmsBluetoothTransport : IBmsTransport
{
    private readonly ILogger<JkBmsBluetoothTransport> _logger;
    private readonly TimeSpan _connectTimeout;
    private readonly string _adapterName;

    private Device? _device;
    private GattCharacteristic? _characteristic;
    private bool _isConnected;
    private bool _disposed;

    // Buffer for accumulating chunked BLE notifications
    private readonly List<byte> _rxBuffer = [];
    private readonly SemaphoreSlim _frameSemaphore = new(0, 1);
    private byte[]? _assembledFrame;

    public string DeviceAddress { get; }
    public bool IsConnected => _isConnected && _characteristic != null;

    public JkBmsBluetoothTransport(
        string deviceAddress,
        string adapterName,
        TimeSpan connectTimeout,
        ILogger<JkBmsBluetoothTransport> logger)
    {
        DeviceAddress = deviceAddress;
        _adapterName = adapterName;
        _connectTimeout = connectTimeout;
        _logger = logger;
    }

    // ── ConnectAsync ──────────────────────────────────────────────────────────

    public async Task ConnectAsync(CancellationToken ct)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(JkBmsBluetoothTransport));

        _logger.LogDebug("BLE connecting to {Address}", DeviceAddress);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_connectTimeout);

        try
        {
            var adapters = await BlueZManager.GetAdaptersAsync();
            var adapter = adapters.FirstOrDefault(
                    a => a.ObjectPath.ToString().EndsWith("/" + _adapterName,
                         StringComparison.OrdinalIgnoreCase))
                ?? throw new JkBmsConnectException(DeviceAddress, 1,
                    new InvalidOperationException(
                        $"Bluetooth adapter '{_adapterName}' not found. " +
                        $"Available: {string.Join(", ", adapters.Select(a => a.ObjectPath.ToString().Split('/').Last()))}"));

            // BlueZ requires the adapter to have received recent advertisement packets from
            // the target device before Device.Connect() will succeed. Without an active scan,
            // the HCI controller aborts the connection attempt with le-connection-abort-by-local
            // (HCI 0x16). We mirror the bluetoothctl pattern exactly:
            //
            //   1. StartDiscoveryAsync  — begin scanning
            //   2. Wait for a fresh advertisement from the target device
            //   3. ConnectAsync         — called while the scan is STILL running
            //   4. StopDiscoveryAsync   — called after ConnectAsync succeeds
            //
            // BlueZ handles the scan → initiating transition internally, so it is safe
            // (and required) to connect while the adapter is still in discovery mode.
            _logger.LogDebug("BLE starting discovery scan for {Address}", DeviceAddress);
            await adapter.StartDiscoveryAsync();
            var scanStart = Stopwatch.GetTimestamp();
            try
            {
                _device = await WaitForDeviceAdvertisementAsync(adapter, DeviceAddress, timeoutCts.Token);

                if (_device is null)
                    throw new JkBmsConnectException(DeviceAddress, 1,
                        new InvalidOperationException($"Device '{DeviceAddress}' not found after BLE discovery scan."));

                // Ensure the scan has run long enough for the HCI controller to have built up
                // fresh LE connection parameters from multiple real advertisement cycles.
                // BlueZ may emit an immediate RSSI property update from its cache when discovery
                // starts, which does not represent an advertisement actually received by the HCI
                // controller. Connecting on that stale data causes le-connection-abort-by-local.
                const int MinScanMs = 3_000;
                var elapsed = Stopwatch.GetElapsedTime(scanStart);
                if (elapsed.TotalMilliseconds < MinScanMs)
                {
                    _logger.LogDebug("BLE scan primed early ({ElapsedMs}ms); waiting for minimum {MinMs}ms",
                        (int)elapsed.TotalMilliseconds, MinScanMs);
                    await Task.Delay((int)(MinScanMs - elapsed.TotalMilliseconds), timeoutCts.Token);
                }

                // Connect directly via BlueZ Device.Connect() — no pairing attempt.
                // JK BMS devices use open BLE connections and do not support bonding.
                // The scan MUST still be running at this point (see comment above).
                //
                // Retry up to 3 times with a pause between attempts. After a previous
                // disconnect the HCI controller may need recovery time; a brief wait and
                // retry (with the scan still running) reliably resolves this.
                const int MaxConnectAttempts = 3;
                const int ConnectRetryDelayMs = 2_000;
                for (int attempt = 1; attempt <= MaxConnectAttempts; attempt++)
                {
                    try
                    {
                        await _device.ConnectAsync();
                        break;
                    }
                    catch (Exception ex) when (attempt < MaxConnectAttempts)
                    {
                        _logger.LogWarning(
                            "BLE connect attempt {Attempt}/{Max} failed ({Message}); retrying after {Delay}ms",
                            attempt, MaxConnectAttempts, ex.Message, ConnectRetryDelayMs);
                        await Task.Delay(ConnectRetryDelayMs, timeoutCts.Token);
                    }
                }
            }
            finally
            {
                try { await adapter.StopDiscoveryAsync(); } catch { /* best-effort */ }
            }

            await _device.WaitForPropertyValueAsync("Connected", value: true, TimeSpan.FromSeconds(8));
            await _device.WaitForPropertyValueAsync("ServicesResolved", value: true, TimeSpan.FromSeconds(8));

            // Discover the UART service and characteristic
            var serviceUuid = JkBmsProtocol.ServiceUuid.ToString();
            var service = await _device.GetServiceAsync(serviceUuid)
                ?? throw new JkBmsConnectException(DeviceAddress, 1,
                    new InvalidOperationException($"JK BMS service {serviceUuid} not found."));

            var charUuid = JkBmsProtocol.CharacteristicUuid.ToString();
            _characteristic = await GattExtensions.GetCharacteristicAsync(service, charUuid)
                ?? throw new JkBmsConnectException(DeviceAddress, 1,
                    new InvalidOperationException($"JK BMS characteristic {charUuid} not found."));

            // Subscribe to notifications
            _characteristic.Value += OnNotificationReceived;
            await _characteristic.StartNotifyAsync();

            _isConnected = true;
            _logger.LogInformation("BLE connected to {Address}", DeviceAddress);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new JkBmsTimeoutException(DeviceAddress);
        }
    }

    // ── DisconnectAsync ───────────────────────────────────────────────────────

    public async Task DisconnectAsync()
    {
        _isConnected = false;

        if (_characteristic != null)
        {
            try
            {
                _characteristic.Value -= OnNotificationReceived;
                await _characteristic.StopNotifyAsync();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Non-fatal error stopping BLE notifications for {Address}", DeviceAddress);
            }
            _characteristic.Dispose();
            _characteristic = null;
        }

        if (_device != null)
        {
            try { await _device.DisconnectAsync(); }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Non-fatal error disconnecting BLE for {Address}", DeviceAddress);
            }
            _device = null;
        }
    }

    // ── ExchangeAsync ─────────────────────────────────────────────────────────

    /// <summary>
    /// Write <paramref name="command"/> to the BLE characteristic and wait for the
    /// complete multi-chunk response. Connects on the first call if not yet connected;
    /// reconnects automatically if the connection was lost since the last call.
    /// </summary>
    public async Task<byte[]> ExchangeAsync(byte[] command, CancellationToken ct)
    {
        // Auto-connect on first call
        if (!IsConnected)
            await ConnectAsync(ct);

        try
        {
            return await DoExchangeAsync(command, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Connection may have dropped since the last successful exchange.
            // Attempt one reconnect and retry before giving up.
            _logger.LogWarning(ex,
                "BLE exchange failed for {Address} ({Message}); reconnecting and retrying",
                DeviceAddress, ex.Message);
            await DisconnectAsync();
            await ConnectAsync(ct);
            return await DoExchangeAsync(command, ct);
        }
    }

    private async Task<byte[]> DoExchangeAsync(byte[] command, CancellationToken ct)
    {
        // Reset receive state
        _rxBuffer.Clear();
        _assembledFrame = null;
        // Drain any leftover permits from a previous exchange
        while (_frameSemaphore.CurrentCount > 0)
            _frameSemaphore.Wait();

        // Write the command to the characteristic (write-without-response)
        await _characteristic!.WriteValueAsync(command, new Dictionary<string, object>());
        _logger.LogTrace("BLE write {Bytes} bytes to {Address}", command.Length, DeviceAddress);

        // Wait for a complete frame to be assembled by OnNotificationReceived
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_connectTimeout);

        try
        {
            await _frameSemaphore.WaitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new JkBmsTimeoutException(DeviceAddress);
        }

        var frame = _assembledFrame
            ?? throw new JkBmsFrameException(
                $"Frame signal received but assembled frame is null for {DeviceAddress}.");

        if (!JkBmsProtocol.ValidateCrc(frame))
            throw new JkBmsCrcException(
                $"CRC validation failed for response from {DeviceAddress}.");

        return frame;
    }

    // ── Notification handler ──────────────────────────────────────────────────

    private Task OnNotificationReceived(GattCharacteristic sender, GattCharacteristicValueEventArgs e)
    {
        if (JkBmsProtocol.TryAccumulateFrame(_rxBuffer, e.Value.AsSpan(), out var frame))
        {
            _assembledFrame = frame;
            try { _frameSemaphore.Release(); } catch { /* semaphore already at max, ignore */ }
        }
        return Task.CompletedTask;
    }

    // ── IAsyncDisposable ──────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            await DisconnectAsync();
            _frameSemaphore.Dispose();
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Waits for the target device to advertise (i.e. for the adapter to receive a fresh
    /// advertisement packet), then returns the <see cref="Device"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The caller is responsible for starting and stopping the discovery scan. This method
    /// only handles the event subscriptions and waits.
    /// </para>
    /// <para>
    /// Two cases are handled:
    /// <list type="bullet">
    ///   <item>Device in BlueZ cache — watch <c>PropertiesChanged</c> for an RSSI update,
    ///         which fires each time an advertisement packet is received during active scan.</item>
    ///   <item>Device not in cache — watch <c>adapter.DeviceFound</c>, which fires when
    ///         BlueZ adds the device to its object tree for the first time during a scan.</item>
    /// </list>
    /// </para>
    /// </remarks>
    private static async Task<Device?> WaitForDeviceAdvertisementAsync(
        Adapter adapter, string deviceAddress, CancellationToken ct)
    {
        // Extract MAC from D-Bus ObjectPath (/org/bluez/hciX/dev_C8_47_8C_EC_1B_0F)
        // to avoid an async D-Bus round-trip inside the event handler (prevents deadlock).
        static string? MacFromObjectPath(string path)
        {
            const string prefix = "/dev_";
            var idx = path.LastIndexOf(prefix, StringComparison.Ordinal);
            return idx < 0 ? null : path[(idx + prefix.Length)..].Replace('_', ':');
        }

        using var readyCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Device? found = await adapter.GetDeviceAsync(deviceAddress);
        IDisposable? rssiWatcher = null;

        async Task OnDeviceFound(Adapter _, DeviceFoundEventArgs args)
        {
            var mac = MacFromObjectPath(args.Device.ObjectPath.ToString());
            if (string.Equals(mac, deviceAddress, StringComparison.OrdinalIgnoreCase))
            {
                found = args.Device;
                readyCts.Cancel();
            }
            await Task.CompletedTask;
        }

        adapter.DeviceFound += OnDeviceFound;

        if (found != null)
        {
            // Device is already in the BlueZ cache. Watch for its RSSI property to change —
            // this fires each time an advertisement packet is received during an active scan.
            rssiWatcher = await found.WatchPropertiesAsync(changes =>
            {
                if (changes.Changed.Any(c => c.Key == "RSSI"))
                    readyCts.Cancel();
            });
        }

        try
        {
            await Task.Delay(Timeout.Infinite, readyCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Cancelled because device was seen advertising — ready to connect.
        }
        finally
        {
            adapter.DeviceFound -= OnDeviceFound;
            rssiWatcher?.Dispose();
        }

        return found;
    }
}
