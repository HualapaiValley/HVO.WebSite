using System.Diagnostics;
using System.Threading.Channels;
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
    private const int MaxConnectAttempts  = 3;
    private const int ConnectRetryDelayMs = 2_000;
    private const int MinScanMs           = 3_000;

    private readonly ILogger<JkBmsBluetoothTransport> _logger;
    private readonly TimeSpan _connectTimeout;
    private readonly string _adapterName;

    private Device? _device;
    private IGattCharacteristic1? _writeCharacteristic;  // FFE1 (write + notify — same characteristic)
    private IGattCharacteristic1? _notifyCharacteristic; // FFE1 (write + notify — same characteristic)
    private IDisposable? _notifyWatcher;                  // property watcher for notifications
    private bool _isConnected;
    private bool _disposed;

    // Buffer for accumulating chunked BLE notifications.
    // _rxBuffer is written only from OnPropertyChanged (D-Bus dispatch thread) and reset
    // at the start of each DoExchangeAsync call on the worker thread. Both operations are
    // never concurrent: the worker sends the write command then blocks on the channel;
    // notifications arrive and are processed while the worker waits.
    private readonly List<byte> _rxBuffer = [];

    // Assembled frames are enqueued here. Using an unbounded Channel ensures that if
    // multiple frames arrive before the consumer reads them, none are lost and the
    // earlier frame is never silently overwritten — fixing the race in the prior
    // SemaphoreSlim + single-field design.
    private readonly Channel<byte[]> _frameChannel =
        Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });

    // Per-adapter reference count of concurrent ConnectAsync calls in the discovery phase.
    // Prevents the first connection from stopping the BLE scan while later parallel
    // connections (that piggybacked on the same scan) are still waiting for advertisements.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int>
        s_discoveryRefCount = new();

    public string DeviceAddress { get; }
    public bool IsConnected => _isConnected && _writeCharacteristic != null && _notifyCharacteristic != null;

    /// <summary>
    /// The most recently captured raw settings frame (type 0x01).
    /// Populated when the BMS pushes a 0x01 frame during an exchange (typically on connect).
    /// Reset at the start of each new connection so stale settings from a prior session
    /// are never returned after a reconnect.
    /// </summary>
    public byte[]? LastSettingsFrame { get; private set; }

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

        // Reset stale settings from any prior connection so GetLatestSettings() cannot
        // return settings captured before a disconnect/reconnect cycle.
        LastSettingsFrame = null;

        _logger.LogDebug("BLE connecting to {Address}", DeviceAddress);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_connectTimeout);

        try
        {
            var adapters = await BlueZManager.GetAdaptersAsync();
            var adapter = adapters.FirstOrDefault(
                    a => a.ObjectPath.ToString().EndsWith("/" + _adapterName,
                         StringComparison.OrdinalIgnoreCase))
                ?? throw new JkBmsConnectException(DeviceAddress, 0,
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
            // Increment the per-adapter ref count before entering the discovery phase.
            // The last concurrent ConnectAsync to exit will stop the scan, preventing the
            // first connection from halting discovery while later parallel connections
            // (piggybacking on the same scan) still need advertisements.
            s_discoveryRefCount.AddOrUpdate(_adapterName, 1, (_, n) => n + 1);
            try
            {
                await adapter.StartDiscoveryAsync();
            }
            catch (Tmds.DBus.DBusException ex) when (ex.ErrorName == "org.bluez.Error.InProgress")
            {
                // Another parallel connect already started discovery on this adapter.
                // The HCI scan is already running — we can still wait for advertisements.
                _logger.LogDebug(
                    "BLE discovery already in progress on {Adapter} (shared); piggybacking for {Address}",
                    _adapterName, DeviceAddress);
            }
            var scanStart = Stopwatch.GetTimestamp();
            try
            {
                _device = await WaitForDeviceAdvertisementAsync(adapter, DeviceAddress, timeoutCts.Token);

                if (_device is null)
                    throw new JkBmsConnectException(DeviceAddress, 0,
                        new InvalidOperationException($"Device '{DeviceAddress}' not found after BLE discovery scan."));

                // Ensure the scan has run long enough for the HCI controller to have built up
                // fresh LE connection parameters from multiple real advertisement cycles.
                // BlueZ may emit an immediate RSSI property update from its cache when discovery
                // starts, which does not represent an advertisement actually received by the HCI
                // controller. Connecting on that stale data causes le-connection-abort-by-local.
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
                // Retry up to MaxConnectAttempts times with a pause between attempts. After a
                // previous disconnect the HCI controller may need recovery time; a brief wait
                // and retry (with the scan still running) reliably resolves this.
                Exception? lastConnectEx = null;
                for (int attempt = 1; attempt <= MaxConnectAttempts; attempt++)
                {
                    try
                    {
                        await _device.ConnectAsync();
                        lastConnectEx = null;
                        break;
                    }
                    catch (Exception ex)
                    {
                        lastConnectEx = ex;
                        if (attempt < MaxConnectAttempts)
                        {
                            _logger.LogWarning(
                                "BLE connect attempt {Attempt}/{Max} failed ({Message}); retrying after {Delay}ms",
                                attempt, MaxConnectAttempts, ex.Message, ConnectRetryDelayMs);
                            await Task.Delay(ConnectRetryDelayMs, timeoutCts.Token);
                        }
                    }
                }
                if (lastConnectEx is not null)
                    throw new JkBmsConnectException(DeviceAddress, MaxConnectAttempts, lastConnectEx);
            }
            finally
            {
                // Decrement the ref count. When this is the last concurrent ConnectAsync
                // exiting the discovery phase, stop the scan. Any adapter proxy can issue
                // StopDiscovery on the same BlueZ adapter object, so the task that stops
                // need not be the one that originally started it.
                var remaining = s_discoveryRefCount.AddOrUpdate(
                    _adapterName, 0, (_, n) => Math.Max(0, n - 1));
                if (remaining == 0)
                    try { await adapter.StopDiscoveryAsync(); } catch { /* best-effort */ }
            }

            await _device.WaitForPropertyValueAsync("Connected", value: true, TimeSpan.FromSeconds(8));
            await _device.WaitForPropertyValueAsync("ServicesResolved", value: true, TimeSpan.FromSeconds(8));

            // Discover the UART service and characteristic
            var serviceUuid = JkBmsProtocol.ServiceUuid.ToString();
            var service = await _device.GetServiceAsync(serviceUuid)
                ?? throw new JkBmsConnectException(DeviceAddress, MaxConnectAttempts,
                    new InvalidOperationException($"JK BMS service {serviceUuid} not found."));

            // The JK BMS "old BLE module" (MAC prefix C8:47:8C) exposes two characteristics
            // in service FFE0:
            //   - char000f: UUID FFE2, flags: write-without-response only
            //   - char0011: UUID FFE1, flags: write + write-without-response + notify
            //
            // The esphome-jk-bms reference implementation writes commands to the FFE1
            // characteristic (the one located via service/characteristic UUID lookup) and
            // subscribes to the same FFE1 characteristic for notifications.  FFE2 exists on
            // these devices but is NOT the correct command channel — writing to it produces no
            // BMS response.  Both write and notify roles therefore use the FFE1 characteristic.
            var charUuid = JkBmsProtocol.CharacteristicUuid.ToString();  // FFE1
            var allServiceChars = await GattExtensions.GetCharacteristicsAsync(service) ?? [];
            if (allServiceChars.Count == 0)
                throw new JkBmsConnectException(DeviceAddress, MaxConnectAttempts,
                    new InvalidOperationException($"JK BMS service {serviceUuid} has no characteristics."));

            // Enumerate all characteristics for debug logging.
            _notifyCharacteristic = null;
            foreach (var c in allServiceChars)
            {
                var cUuid  = await c.GetUUIDAsync();
                var flags  = await c.GetFlagsAsync();
                _logger.LogDebug("  Service char UUID={Uuid} Flags=[{Flags}]",
                    cUuid, string.Join(", ", flags ?? []));
                if (string.Equals(cUuid, charUuid, StringComparison.OrdinalIgnoreCase))
                    _notifyCharacteristic ??= c;
            }

            // Fall back to the first available characteristic if FFE1 was not found.
            _notifyCharacteristic ??= allServiceChars.FirstOrDefault();

            // Both write and notify use the same characteristic (FFE1), matching the
            // esphome reference implementation.
            _writeCharacteristic = _notifyCharacteristic;

            if (_notifyCharacteristic == null)
                throw new JkBmsConnectException(DeviceAddress, MaxConnectAttempts,
                    new InvalidOperationException($"JK BMS characteristic {charUuid} not found."));

            _logger.LogDebug("BLE characteristic (write + notify): {Uuid}", charUuid);

            // Subscribe to property changes on the notify characteristic for BLE notifications.
            _notifyWatcher = await _notifyCharacteristic.WatchPropertiesAsync(OnPropertyChanged);
            await _notifyCharacteristic.StartNotifyAsync();

            _isConnected = true;

            // The JK BMS old-module firmware requires a device-info request (0x97) immediately
            // after notification subscription before it will respond to cell-info commands (0x96).
            // This mirrors the esphome-jk-bms behaviour: it sends 0x97 on ESP_GATTC_REG_FOR_NOTIFY_EVT,
            // waits for the 0x03 response, then begins issuing 0x96 on each update cycle.
            // Any spontaneous 0x01 (settings) frame pushed by the BMS during this exchange is
            // silently discarded by the discard loop in DoExchangeAsync.
            _logger.LogDebug("BLE sending device-info init (0x97) to {Address}", DeviceAddress);
            try
            {
                var initFrame = await DoExchangeAsync(JkBmsProtocol.BuildDeviceInfoCommand(), timeoutCts.Token);
                _logger.LogDebug(
                    "BLE device-info init response from {Address}: FrameType=0x{Type:X2}",
                    DeviceAddress, initFrame[4]);
            }
            catch (Exception ex)
            {
                // Non-fatal: log and continue. If the BMS doesn't respond to 0x97 on this firmware,
                // we'll still attempt 0x96 — this matches the esphome fallback behaviour.
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
            _writeCharacteristic  = null;
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
        // Reset receive state and drain any frames left over from a previous exchange.
        _rxBuffer.Clear();
        while (_frameChannel.Reader.TryRead(out _)) { }

        // Write the command using Write Command (GATT opcode 0x52, write-without-response).
        // Commands are written to the FFE1 characteristic, which also carries notifications.
        // This matches the esphome-jk-bms reference implementation.
        await _writeCharacteristic!.WriteValueAsync(command,
            new Dictionary<string, object> { { "type", "command" } });
        _logger.LogTrace("BLE write {Bytes} bytes to {Address}", command.Length, DeviceAddress);

        // Derive the expected response frame type from the command function code so we
        // can discard spontaneous frames (e.g. the settings frame the BMS pushes on
        // every connection) that arrive before — or interleaved with — the actual response.
        byte expectedType = GetExpectedResponseFrameType(command);

        // Use the overall connect timeout for the whole wait loop, not per-frame.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_connectTimeout);

        // Collect frames until we receive one with the expected type.
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

            // Filter by type BEFORE checking CRC. The BMS pushes a spontaneous 0x01 settings
            // frame on every new connection; that frame may have a bad CRC (or may be partially
            // assembled) and must be discarded without throwing, or the exchange never reaches
            // the actual response frame.
            if (expectedType != 0 && frame[4] != expectedType)
            {
                bool crcValid = JkBmsProtocol.ValidateCrc(frame);
                // Capture the settings frame while discarding it — it contains all device
                // configuration and will be exposed as LastSettingsFrame for the UI.
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
                    "CRC mismatch for 0x{Type:X2} frame from {Address}: computed=0x{Computed:X2} expected=0x{Expected:X2} len={Len} head=[{Head}] tail=[{Tail}]",
                    frame.Length > 4 ? frame[4] : (byte)0,
                    DeviceAddress,
                    computed,
                    frame[^1],
                    frame.Length,
                    BitConverter.ToString(frame, 0, Math.Min(8, frame.Length)),
                    BitConverter.ToString(frame, Math.Max(0, frame.Length - 8), Math.Min(8, frame.Length)));
                throw new JkBmsCrcException(
                    $"CRC validation failed for response from {DeviceAddress}.");
            }

            return frame;
        }
    }

    /// <summary>
    /// Returns the BMS response frame type expected for the given command, derived from
    /// the command function code at byte 4. Returns 0 if the type is unknown.
    /// </summary>
    private static byte GetExpectedResponseFrameType(byte[] command) =>
        command.Length > 4 ? command[4] switch
        {
            0x95 => JkBmsProtocol.FrameTypeSettings,
            0x96 => JkBmsProtocol.FrameTypeCellInfo,
            0x97 => JkBmsProtocol.FrameTypeDeviceInfo,
            _    => (byte)0,
        } : (byte)0;

    // ── Notification handler ──────────────────────────────────────────────────

    private void OnPropertyChanged(Tmds.DBus.PropertyChanges changes)
    {
        foreach (var pair in changes.Changed)
        {
            if (pair.Key == "Value" && pair.Value is byte[] value)
            {
                if (JkBmsProtocol.TryAccumulateFrame(_rxBuffer, value.AsSpan(), out var frame))
                    _frameChannel.Writer.TryWrite(frame);
            }
        }
    }

    // ── IAsyncDisposable ──────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            await DisconnectAsync();
            _frameChannel.Writer.TryComplete();
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
