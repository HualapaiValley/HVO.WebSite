using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HVO.Edge.Hosting.Telemetry;
using HVO.Hardware.JkBms.Bms;
using HVO.Hardware.JkBms.Configuration;
using HVO.Hardware.JkBms.Outbox;
using HVO.Hardware.JkBms.HomeAssistant;
using HVO.Hardware.JkBms.Hosting;
using HVO.Hardware.JkBms.Protocol;
using HVO.Hardware.JkBms.Protocol.Packets;
using HVO.Hardware.JkBms.Protocol.Transport;
using HVO.Edge.HomeAssistant.Mqtt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.JkBms.Workers;

/// <summary>
/// Per-device mutable state tracked by the poller worker.
/// Written by one device session and read by diagnostics.
///
/// Mutable fields are marked <c>volatile</c> so that writes by the worker thread are
/// immediately visible to diagnostics threads reading them, satisfying the C# memory
/// model without a full lock. <c>volatile</c> is sufficient here because each field is
/// written atomically (reference swap or 32-bit value) and reads never need to be
/// consistent across multiple fields simultaneously.
/// </summary>
public sealed class DevicePollState
{
    public string Address { get; init; } = string.Empty;
    public string Alias { get; init; } = string.Empty;
    public string DeviceId { get; init; } = string.Empty;
    public string AdapterName { get; init; } = string.Empty;
    public int PollIntervalSeconds { get; init; }

    // These fields are read by diagnostics while the worker loop writes them.
    // volatile is used where the type allows it (reference types, int).
    // For DateTime (a 64-bit struct), we store ticks as a long and use Volatile.Read/Write,
    // which provides the same acquire/release semantics as the volatile keyword.
    private volatile int _consecutiveErrors;
    private volatile int _backoffLevel;
    private volatile string? _lastError;
    private long _nextPollAtTicks = DateTime.UtcNow.Ticks;
    private long _lastPollAtTicks;     // 0 means "never polled" (null)
    private volatile CellInfoPacket? _latestReading;
    private volatile SettingsPacket? _latestSettings;
    private volatile DeviceInfoPacket? _latestDeviceInfo;
    private volatile string? _lastSentConfigHash;
    private volatile string? _lastSentDeviceInfoHash;
    private volatile int _sessionEstablishedCount;
    private volatile int _sessionDisconnectedCount;
    private volatile int _sessionRequestFailureCount;
    private volatile int _isSessionConnected;
    private volatile string? _lastDisconnectReason;
    private volatile string _settingsPasswordChangeStatus = "idle";
    private volatile int _lastSessionDurationSeconds;
    private long _lastConnectedAtTicks;
    private long _lastDisconnectedAtTicks;

    public DateTime NextPollAt
    {
        get => new DateTime(Volatile.Read(ref _nextPollAtTicks), DateTimeKind.Utc);
        set => Volatile.Write(ref _nextPollAtTicks, value.Ticks);
    }

    public int ConsecutiveErrors
    {
        get => _consecutiveErrors;
        set => _consecutiveErrors = value;
    }

    public int BackoffLevel
    {
        get => _backoffLevel;
        set => _backoffLevel = value;
    }

    public string? LastError
    {
        get => _lastError;
        set => _lastError = value;
    }

    public DateTime? LastPollAt
    {
        get
        {
            var t = Volatile.Read(ref _lastPollAtTicks);
            return t == 0 ? null : new DateTime(t, DateTimeKind.Utc);
        }
        set => Volatile.Write(ref _lastPollAtTicks, value?.Ticks ?? 0L);
    }

    public CellInfoPacket? LatestReading
    {
        get => _latestReading;
        set => _latestReading = value;
    }

    /// <summary>Device configuration parsed from the spontaneous 0x01 settings frame.</summary>
    public SettingsPacket? LatestSettings
    {
        get => _latestSettings;
        set => _latestSettings = value;
    }

    /// <summary>Device info (firmware, serial number, etc.) from the 0x03 device-info frame.</summary>
    public DeviceInfoPacket? LatestDeviceInfo
    {
        get => _latestDeviceInfo;
        set => _latestDeviceInfo = value;
    }

    /// <summary>SHA-256 hash of the last config payload successfully written to the outbox.</summary>
    public string? LastSentConfigHash
    {
        get => _lastSentConfigHash;
        set => _lastSentConfigHash = value;
    }

    /// <summary>SHA-256 hash of the last device-info payload successfully written to the outbox.</summary>
    public string? LastSentDeviceInfoHash
    {
        get => _lastSentDeviceInfoHash;
        set => _lastSentDeviceInfoHash = value;
    }

    public int SessionEstablishedCount
    {
        get => _sessionEstablishedCount;
        set => _sessionEstablishedCount = value;
    }

    public int SessionDisconnectedCount
    {
        get => _sessionDisconnectedCount;
        set => _sessionDisconnectedCount = value;
    }

    public int SessionRequestFailureCount
    {
        get => _sessionRequestFailureCount;
        set => _sessionRequestFailureCount = value;
    }

    public bool IsSessionConnected
    {
        get => _isSessionConnected != 0;
        set => _isSessionConnected = value ? 1 : 0;
    }

    public DateTime? LastConnectedAt
    {
        get
        {
            var t = Volatile.Read(ref _lastConnectedAtTicks);
            return t == 0 ? null : new DateTime(t, DateTimeKind.Utc);
        }
        set => Volatile.Write(ref _lastConnectedAtTicks, value?.Ticks ?? 0L);
    }

    public DateTime? LastDisconnectedAt
    {
        get
        {
            var t = Volatile.Read(ref _lastDisconnectedAtTicks);
            return t == 0 ? null : new DateTime(t, DateTimeKind.Utc);
        }
        set => Volatile.Write(ref _lastDisconnectedAtTicks, value?.Ticks ?? 0L);
    }

    public string? LastDisconnectReason
    {
        get => _lastDisconnectReason;
        set => _lastDisconnectReason = value;
    }

    public int LastSessionDurationSeconds
    {
        get => _lastSessionDurationSeconds;
        set => _lastSessionDurationSeconds = value;
    }

    public string SettingsPasswordChangeStatus
    {
        get => _settingsPasswordChangeStatus;
        set => _settingsPasswordChangeStatus = value;
    }

    public void RecordSessionEstablished(DateTime connectedAtUtc)
    {
        SessionEstablishedCount++;
        IsSessionConnected = true;
        LastConnectedAt = connectedAtUtc;
        LastDisconnectReason = null;
        LastSessionDurationSeconds = 0;
    }

    public void RecordSessionDisconnected(DateTime disconnectedAtUtc, string reason)
    {
        SessionDisconnectedCount++;
        IsSessionConnected = false;
        LastDisconnectedAt = disconnectedAtUtc;
        LastDisconnectReason = reason;

        var connectedAt = LastConnectedAt;
        if (connectedAt.HasValue && disconnectedAtUtc >= connectedAt.Value)
            LastSessionDurationSeconds = (int)(disconnectedAtUtc - connectedAt.Value).TotalSeconds;
    }
}

/// <summary>
/// Background service that polls each configured JK BMS device on its own schedule
/// and writes readings to the SQLite outbox.
///
/// Design:
/// - One long-lived <see cref="JkBmsDevice"/> session is created per device at startup.
/// - Each session owns its BLE connection, poll schedule, reconnect/backoff state, and
///   serial command lane for that specific BMS.
/// - Per-device exponential backoff on consecutive errors (capped at ~10 min).
/// - Isolates each device in its own long-lived reconnecting session.
/// </summary>
public sealed class BmsPollerWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBmsTransportFactory _transportFactory;
    private readonly IBluetoothAdapterCoordinator _adapterCoordinator;
    private readonly ILoggerFactory _loggerFactory;
    private readonly JkBmsOptions _options;
    private readonly JkBmsHomeAssistantProjection _homeAssistant;
    private readonly GatewayTelemetry _telemetry;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BmsPollerWorker> _logger;

    private readonly List<DevicePollState> _devices;
    private readonly List<JkBmsDevice> _sessions;

    // ── Public diagnostics state ─────────────────────────────────────────────

    /// <summary>
    /// Device states for diagnostics. The list is created once at startup; individual
    /// <see cref="DevicePollState"/> entries are updated in place by the worker event loop.
    /// UI consumers read the current state without locking — reads of individual fields are
    /// safe because the worker is the sole writer.
    /// </summary>
    public IReadOnlyList<DevicePollState> DeviceStates => _devices;

    public BmsPollerWorker(
        IBmsTransportFactory transportFactory,
        IBluetoothAdapterCoordinator adapterCoordinator,
        ILoggerFactory loggerFactory,
        IServiceScopeFactory scopeFactory,
        IOptions<JkBmsOptions> options,
        JkBmsHomeAssistantProjection homeAssistant,
        IHomeAssistantMqttCommandRouter commandRouter,
        JkBmsSettingsPasswordCredentials settingsPasswordCredentials,
        GatewayTelemetry telemetry,
        TimeProvider timeProvider,
        ILogger<BmsPollerWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _transportFactory = transportFactory;
        _adapterCoordinator = adapterCoordinator;
        _loggerFactory = loggerFactory;
        _options = options.Value;
        _homeAssistant = homeAssistant;
        _telemetry = telemetry;
        _timeProvider = timeProvider;
        _logger = logger;

        var configuredDevices = _options.Devices
            .Select((device, index) => new { Device = device, Index = index })
            .ToList();

        foreach (var entry in configuredDevices.Where(static x => x.Device.Enabled &&
            (string.IsNullOrWhiteSpace(x.Device.Address) || string.IsNullOrWhiteSpace(x.Device.Alias))))
        {
            _logger.LogWarning(
                "Skipping invalid JK BMS device config at index {Index}. Enabled={Enabled} Address='{Address}' Alias='{Alias}'",
                entry.Index,
                entry.Device.Enabled,
                entry.Device.Address,
                entry.Device.Alias);
        }

        var enabledDevices = configuredDevices
            .Where(static x =>
                x.Device.Enabled &&
                !string.IsNullOrWhiteSpace(x.Device.Address) &&
                !string.IsNullOrWhiteSpace(x.Device.Alias))
            .Select(static x => x.Device)
            .ToList();

        _devices = enabledDevices
            .Select(d => new DevicePollState
            {
                Address = d.Address,
                Alias = d.Alias,
                DeviceId = d.DeviceId,
                AdapterName = string.IsNullOrWhiteSpace(d.HciAdapter) ? _options.HciAdapter : d.HciAdapter,
                PollIntervalSeconds = d.PollIntervalSeconds > 0
                    ? d.PollIntervalSeconds
                    : _options.DefaultPollIntervalSeconds,
                // First poll fires immediately after the scan loop signals connect-ready.
                NextPollAt = DateTime.UtcNow,
            })
            .ToList();

        _sessions = enabledDevices
            .Select(d =>
            {
                var adapterName = string.IsNullOrWhiteSpace(d.HciAdapter)
                    ? _options.HciAdapter
                    : d.HciAdapter;
                var state = _devices.First(s => s.Address == d.Address);
                return new JkBmsDevice(
                    d,
                    adapterName,
                    state,
                    _options,
                    _transportFactory,
                    _adapterCoordinator,
                    _loggerFactory,
                    _telemetry,
                    _timeProvider,
                    () => settingsPasswordCredentials.Get(d.DeviceId),
                    OnSuccessfulPollAsync,
                    failedAt => _homeAssistant.PublishUnavailable(d, failedAt),
                    () => PublishLatestState(d, state),
                    _loggerFactory.CreateLogger<JkBmsDevice>());
            })
            .ToList();

        for (var index = 0; index < enabledDevices.Count; index++)
        {
            var device = enabledDevices[index];
            if (string.IsNullOrWhiteSpace(device.SettingsPasswordSecret))
                continue;
            var session = _sessions[index];
            commandRouter.Register(
                _homeAssistant.KeyFor(device),
                "change_settings_password",
                () => session.TryQueueSettingsPasswordChange());
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "BmsPollerWorker starting {Count} device session(s).",
            _devices.Count);

        try
        {
            var sessionTasks = _sessions.Select(session => SuperviseSessionAsync(session, stoppingToken)).ToArray();
            await Task.WhenAll(sessionTasks);
        }
        finally
        {
            foreach (var session in _sessions)
                await session.DisposeAsync();
        }

        _logger.LogInformation("BmsPollerWorker stopped.");
    }

    private async Task SuperviseSessionAsync(JkBmsDevice session, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await session.RunAsync(stoppingToken);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Unexpected JK BMS session failure for {Address}; healthy device sessions remain active",
                    session.Address);
                await Task.Delay(TimeSpan.FromSeconds(5), _timeProvider, stoppingToken);
            }
        }
    }

    private async Task OnSuccessfulPollAsync(
        DevicePollState device,
        JkBmsClient client,
        CellInfoPacket packet,
        CancellationToken ct)
    {
        var reading = MapToReading(device, packet);
        _homeAssistant.Publish(
            _options.Devices.Single(config => string.Equals(config.Address, device.Address, StringComparison.OrdinalIgnoreCase)),
            reading,
            device.LatestDeviceInfo,
            device.SettingsPasswordChangeStatus);

        var attempt = 0;
        while (true)
        {
            try
            {
                await EnqueueOutboxAsync(device, packet, reading, ct);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                attempt++;
                _logger.LogWarning(exception,
                    "JK BMS observation enqueue attempt {Attempt} failed for {DeviceId}; the same observation will be retried",
                    attempt,
                    device.DeviceId);
                var delaySeconds = Math.Min(1 << Math.Min(attempt - 1, 5), 30);
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), _timeProvider, ct);
            }
        }
    }

    private void PublishLatestState(BmsDeviceConfig config, DevicePollState state)
    {
        if (state.LatestReading is null)
            return;
        _homeAssistant.Publish(
            config,
            MapToReading(state, state.LatestReading),
            state.LatestDeviceInfo,
            state.SettingsPasswordChangeStatus);
    }

    private async Task EnqueueOutboxAsync(
        DevicePollState device,
        CellInfoPacket packet,
        BmsDeviceReading reading,
        CancellationToken ct)
    {
        // Build config/deviceInfo snapshots only when they have changed.
        // Track pending hashes separately — only commit them to device state AFTER the
        // outbox record is successfully persisted to avoid skipping snapshots on retry.
        BmsConfigPayload? configPayload = null;
        string? pendingConfigHash = null;
        if (device.LatestSettings is not null)
        {
            var cfg = MapToConfigPayload(device.LatestSettings);
            var hash = ComputeHash(cfg);
            if (hash != device.LastSentConfigHash)
            {
                configPayload = cfg;
                pendingConfigHash = hash;
            }
        }

        BmsDeviceInfoPayload? infoPayload = null;
        string? pendingInfoHash = null;
        if (device.LatestDeviceInfo is not null)
        {
            var info = MapToDeviceInfoPayload(device.LatestDeviceInfo);
            var hash = ComputeHash(info);
            if (hash != device.LastSentDeviceInfoHash)
            {
                infoPayload = info;
                pendingInfoHash = hash;
            }
        }

        var record = new BmsIngressRecord
        {
            Reading = reading,
            Config = configPayload,
            DeviceInfo = infoPayload,
        };

        await using var scope = _scopeFactory.CreateAsyncScope();
        var writer = scope.ServiceProvider.GetRequiredService<IBmsOutboxWriter>();

        await writer.EnqueueAsync(
            device.Address,
            device.DeviceId,
            packet.RecordedAtUtc,
            record,
            ct);

        // A duplicate means this exact observation is already durable, so its embedded
        // snapshots are durable as well and should not be repeated on every later poll.
        if (configPayload is not null && pendingConfigHash is not null)
            device.LastSentConfigHash = pendingConfigHash;
        if (infoPayload is not null && pendingInfoHash is not null)
            device.LastSentDeviceInfoHash = pendingInfoHash;
    }

    private static BmsDeviceReading MapToReading(DevicePollState device, CellInfoPacket packet) =>
        new()
        {
            DeviceAddress = device.Address,
            DeviceAlias = device.Alias,
            RecordedAtUtc = packet.RecordedAtUtc,
            CellCount = packet.CellCount,
            CellVoltagesMv = packet.CellVoltagesMv,
            CellResistancesMOhm = packet.CellResistancesMOhm,
            AverageCellVoltageMv = packet.AverageCellVoltageMv,
            DeltaCellVoltageMv = packet.DeltaCellVoltageMv,
            MaxVoltageCellIndex = packet.MaxVoltageCellIndex,
            MinVoltageCellIndex = packet.MinVoltageCellIndex,
            BalancingCurrentMa = packet.BalancingCurrentMa,
            BalancingActive = packet.BalancingActive,
            PowerTubeTemperatureC = packet.PowerTubeTemperatureC,
            BatteryTemperature1C = packet.BatteryTemperature1C,
            BatteryTemperature2C = packet.BatteryTemperature2C,
            TotalVoltageMv = packet.TotalVoltageMv,
            CurrentMa = packet.CurrentMa,
            StateOfChargePercent = packet.StateOfChargePercent,
            RemainingCapacityMah = packet.RemainingCapacityMah,
            NominalCapacityMah = packet.NominalCapacityMah,
            CycleCount = packet.CycleCount,
            CycleCapacityMah = packet.CycleCapacityMah,
            StateOfHealthPercent = packet.StateOfHealthPercent,
            AlarmBitmask = packet.AlarmBitmask,
        };

    private static BmsConfigPayload MapToConfigPayload(SettingsPacket s) =>
        new()
        {
            CellCount = s.CellCount,
            NominalCapacityMah = s.NominalCapacityMah,
            ChargingEnabled = s.ChargingEnabled,
            DischargingEnabled = s.DischargingEnabled,
            BalancingEnabled = s.BalancingEnabled,
            CellOvpMv = s.CellOvervoltageProtectionMv,
            CellOvpRecoveryMv = s.CellOvervoltageRecoveryMv,
            CellUvpMv = s.CellUndervoltageProtectionMv,
            CellUvpRecoveryMv = s.CellUndervoltageRecoveryMv,
            BalanceTriggerMv = s.BalancePressureDifferenceMv,
            BalanceStartVoltageMv = s.BalanceStartingVoltageMv,
            ChargeOcpMa = s.ChargingOvercurrentProtectionMa,
            ChargeOcpDelayS = s.ChargingOvercurrentProtectionDelayS,
            ChargeOcpRecoveryS = s.ChargingOvercurrentProtectionRecoveryS,
            DischargeOcpMa = s.DischargingOvercurrentProtectionMa,
            DischargeOcpDelayS = s.DischargingOvercurrentProtectionDelayS,
            DischargeOcpRecoveryS = s.DischargingOvercurrentProtectionRecoveryS,
            ShortCircuitDelayUs = s.ShortCircuitProtectionDelayUs,
            ShortCircuitRecoveryS = s.ShortCircuitProtectionRecoveryS,
            ChargeOtpC = s.ChargingOvertemperatureProtectionC,
            ChargeOtpRecoveryC = s.ChargingOvertemperatureRecoveryC,
            ChargeUtpC = s.ChargingUndertemperatureProtectionC,
            ChargeUtpRecoveryC = s.ChargingUndertemperatureRecoveryC,
            DischargeOtpC = s.DischargingOvertemperatureProtectionC,
            DischargeOtpRecoveryC = s.DischargingOvertemperatureRecoveryC,
            MosOtpC = s.PowerTubeOvertemperatureProtectionC,
            MosOtpRecoveryC = s.PowerTubeOvertemperatureRecoveryC,
        };

    private static BmsDeviceInfoPayload MapToDeviceInfoPayload(DeviceInfoPacket d) =>
        new()
        {
            Manufacturer = d.ManufacturerName,
            Hardware = d.HardwareName,
            Firmware = d.FirmwareVersion,
            SerialNumber = d.SerialNumber,
            DeviceName = d.DeviceName,
            ManufacturingDate = d.ManufacturingDate,
            UserData = d.UserData,
        };

    private static string ComputeHash<T>(T value)
    {
        var json = JsonSerializer.Serialize(value);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }
}
