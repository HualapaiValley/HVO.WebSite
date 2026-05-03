using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HVO.Enterprise.Telemetry.Abstractions;
using HVO.Hardware.JkBms.Bms;
using HVO.Hardware.JkBms.Configuration;
using HVO.Hardware.JkBms.Outbox;
using HVO.Hardware.JkBms.Protocol;
using HVO.Hardware.JkBms.Protocol.Packets;
using HVO.Hardware.JkBms.Protocol.Transport;
using HVO.Hardware.JkBms.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.JkBms.Workers;

/// <summary>
/// Per-device mutable state tracked by the poller worker.
/// Written only by the worker's single event loop; read by Blazor UI components
/// (single writer, multiple readers — no locking needed for individual field reads).
/// </summary>
public sealed class DevicePollState
{
    public string Address { get; init; } = string.Empty;
    public string Alias { get; init; } = string.Empty;
    public int PollIntervalSeconds { get; init; }
    public DateTime NextPollAt { get; set; } = DateTime.UtcNow;
    public int ConsecutiveErrors { get; set; }
    public int BackoffLevel { get; set; }
    public string? LastError { get; set; }
    public DateTime? LastPollAt { get; set; }
    public CellInfoPacket? LatestReading { get; set; }

    /// <summary>Device configuration parsed from the spontaneous 0x01 settings frame.</summary>
    public SettingsPacket? LatestSettings { get; set; }

    /// <summary>Device info (firmware, serial number, etc.) from the 0x03 device-info frame.</summary>
    public DeviceInfoPacket? LatestDeviceInfo { get; set; }

    /// <summary>SHA-256 hash of the last config payload successfully written to the outbox.</summary>
    public string? LastSentConfigHash { get; set; }

    /// <summary>SHA-256 hash of the last device-info payload successfully written to the outbox.</summary>
    public string? LastSentDeviceInfoHash { get; set; }
}

/// <summary>
/// Background service that polls each configured JK BMS device on its own schedule
/// and writes readings to the SQLite outbox.
///
/// Design:
/// - Single event loop iterates over devices and polls the soonest-due one.
/// - One long-lived <see cref="JkBmsClient"/> is created per device at startup;
///   each client holds a persistent BLE connection via its transport.
/// - Per-device exponential backoff on consecutive errors (capped at ~10 min).
/// - Raises <see cref="DeviceStateChanged"/> after each successful or failed poll so the
///   Blazor status page can refresh in real-time.
/// </summary>
public sealed class BmsPollerWorker : BackgroundService
{
    private readonly Dictionary<string, JkBmsClient> _clients;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBmsAlarmHandler _alarmHandler;
    private readonly JkBmsOptions _options;
    private readonly BmsTelemetry _telemetry;
    private readonly ITelemetryService _telemetryService;
    private readonly ILogger<BmsPollerWorker> _logger;

    private readonly List<DevicePollState> _devices;

    // ── Public state (Blazor status page reads these) ─────────────────────────

    /// <summary>
    /// Device states for the status page. The list is created once at startup; individual
    /// <see cref="DevicePollState"/> entries are updated in place by the worker event loop.
    /// UI consumers read the current state without locking — reads of individual fields are
    /// safe because the worker is the sole writer.
    /// </summary>
    public IReadOnlyList<DevicePollState> DeviceStates => _devices;

    /// <summary>Raised after each poll attempt (success or failure). Subscribers update the UI.</summary>
    public event Action? DeviceStateChanged;

    public BmsPollerWorker(
        IBmsTransportFactory transportFactory,
        ILoggerFactory loggerFactory,
        IServiceScopeFactory scopeFactory,
        IBmsAlarmHandler alarmHandler,
        IOptions<JkBmsOptions> options,
        BmsTelemetry telemetry,
        ITelemetryService telemetryService,
        ILogger<BmsPollerWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _alarmHandler = alarmHandler;
        _options = options.Value;
        _telemetry = telemetry;
        _telemetryService = telemetryService;
        _logger = logger;

        // Create one persistent client per enabled device
        _clients = _options.Devices
            .Where(d => d.Enabled)
            .ToDictionary(
                d => d.Address,
                d =>
                {
                    var adapterName = string.IsNullOrWhiteSpace(d.HciAdapter)
                        ? _options.HciAdapter
                        : d.HciAdapter;
                    var transport = transportFactory.Create(d.Address, adapterName);
                    return new JkBmsClient(transport,
                        loggerFactory.CreateLogger<JkBmsClient>());
                });

        var enabledDevices = _options.Devices.Where(d => d.Enabled).ToList();
        _devices = enabledDevices
            .Select(d => new DevicePollState
            {
                Address = d.Address,
                Alias = d.Alias,
                PollIntervalSeconds = d.PollIntervalSeconds > 0
                    ? d.PollIntervalSeconds
                    : _options.DefaultPollIntervalSeconds,
                // All devices connect in parallel at startup; first polls are staggered
                // slightly so the HCI adapter isn't hit with 7 simultaneous exchanges.
                NextPollAt = DateTime.UtcNow.AddSeconds(enabledDevices.IndexOf(d) * 2),
            })
            .ToList();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "BmsPollerWorker starting. {Count} device(s).",
            _devices.Count);

        // Connect all devices concurrently at startup so the first poll cycle
        // doesn't block on sequential BLE scan + connect (3-10s per device).
        // Failures are non-fatal here — ExchangeAsync will retry on the first poll.
        _logger.LogInformation("BmsPollerWorker connecting all devices in parallel...");
        await Task.WhenAll(_clients.Select(async kvp =>
        {
            var (address, client) = (kvp.Key, kvp.Value);
            try
            {
                await client.ConnectAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Shutdown before all connections established — fine, the loop won't run.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Startup connect failed for {Address}; will retry on first poll.", address);
            }
        }));

        _logger.LogInformation("BmsPollerWorker startup connect phase complete.");

        // Fetch device info and capture settings for each device that connected successfully.
        // These are stored in DevicePollState so the UI can display configuration data.
        await Task.WhenAll(_clients.Select(async kvp =>
        {
            var (address, client) = (kvp.Key, kvp.Value);
            var state = _devices.First(d => d.Address == address);
            try
            {
                state.LatestDeviceInfo = await client.PollDeviceInfoAsync(stoppingToken);
                state.LatestSettings = client.GetLatestSettings();
            }
            catch (OperationCanceledException)
            {
                // Shutdown before info fetch — fine.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Startup info fetch failed for {Alias} ({Address}); UI will show N/A.",
                    state.Alias, address);
            }
        }));

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;

                // Find the device most overdue for a poll
                var device = _devices
                    .OrderBy(d => d.NextPollAt)
                    .FirstOrDefault();

                if (device is null)
                {
                    await Task.Delay(1_000, stoppingToken);
                    continue;
                }

                var waitMs = (int)(device.NextPollAt - now).TotalMilliseconds;
                if (waitMs > 0)
                {
                    await Task.Delay(Math.Min(waitMs, 1_000), stoppingToken);
                    continue;
                }

                await PollDeviceAsync(device, stoppingToken);
            }
        }
        finally
        {
            // Dispose all persistent clients on shutdown
            foreach (var client in _clients.Values)
                await client.DisposeAsync();
        }

        _logger.LogInformation("BmsPollerWorker stopped.");
    }

    private async Task PollDeviceAsync(DevicePollState device, CancellationToken ct)
    {
        using var pollScope = _telemetryService.StartOperation("BMS.Poll");
        pollScope.WithTag("device", device.Alias);

        _logger.LogDebug("Polling {Alias} ({Address})", device.Alias, device.Address);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var packet = await _clients[device.Address].PollCellInfoAsync(ct);
            sw.Stop();

            _telemetry.DevicePollCount.Add(1,
                new KeyValuePair<string, object?>("device", device.Alias),
                new KeyValuePair<string, object?>("result", "success"));
            _telemetry.DevicePollDurationMs.Record(sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("device", device.Alias));

            device.LatestReading = packet;
            device.LastPollAt = DateTime.UtcNow;
            device.ConsecutiveErrors = 0;
            device.BackoffLevel = 0;
            device.LastError = null;
            device.NextPollAt = DateTime.UtcNow.AddSeconds(device.PollIntervalSeconds);

            // Refresh settings from the spontaneous 0x01 frame the transport captures
            // each time the BMS connects.  We update on every successful poll so that
            // the UI eventually shows settings even if the frame wasn't in the buffer
            // when GetLatestSettings() was called at startup.
            var freshSettings = _clients[device.Address].GetLatestSettings();
            if (freshSettings is not null)
                device.LatestSettings = freshSettings;

            await WriteToOutboxAsync(device, packet, ct);

            if (packet.HasAlarms)
            {
                var reading = MapToReading(device, packet);
                await _alarmHandler.HandleAsync(reading, ct);
            }

            pollScope
                .WithTag("soc_pct", packet.StateOfChargePercent)
                .WithTag("voltage_mv", packet.TotalVoltageMv)
                .WithTag("current_ma", packet.CurrentMa)
                .WithTag("alarms", packet.HasAlarms)
                .Succeed();

            DeviceStateChanged?.Invoke();

            // Log a structured summary with all key metrics.
            // Cell voltages are emitted as a scope property so they appear as a JSON array
            // in the structured log file — useful for post-run per-cell drift analysis.
            using (_logger.BeginScope(new Dictionary<string, object?>
            {
                ["CellVoltagesMv"] = string.Join(",", packet.CellVoltagesMv),
                ["CellCount"] = packet.CellCount,
            }))
            {
                _logger.LogInformation(
                    "Polled {Alias}: SOC={Soc}%, V={VoltageMv}mV, I={CurrentMa}mA, Δ={DeltaMv}mV, " +
                    "T1={T1C}°C T2={T2C}°C Tmos={TmosC}°C",
                    device.Alias,
                    packet.StateOfChargePercent,
                    packet.TotalVoltageMv,
                    packet.CurrentMa,
                    packet.DeltaCellVoltageMv,
                    packet.BatteryTemperature1C,
                    packet.BatteryTemperature2C,
                    packet.PowerTubeTemperatureC);
            }
        }
        catch (OperationCanceledException ex)
        {
            pollScope.Fail(ex);
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _telemetry.DevicePollCount.Add(1,
                new KeyValuePair<string, object?>("device", device.Alias),
                new KeyValuePair<string, object?>("result", "error"));
            _telemetry.DevicePollDurationMs.Record(sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("device", device.Alias));

            device.ConsecutiveErrors++;
            device.LastError = ex.Message;
            device.BackoffLevel = Math.Min(device.BackoffLevel + 1, 10);

            // Exponential backoff: 2^level seconds, capped at 10 minutes
            int backoffSeconds = Math.Min((int)Math.Pow(2, device.BackoffLevel), 600);
            device.NextPollAt = DateTime.UtcNow.AddSeconds(backoffSeconds);

            pollScope.RecordException(ex);
            pollScope.Fail(ex);
            DeviceStateChanged?.Invoke();

            _logger.LogWarning(
                ex,
                "Poll failed for {Alias} ({Address}), error #{N}. Backoff {Backoff}s.",
                device.Alias, device.Address, device.ConsecutiveErrors, backoffSeconds);
        }
    }

    private async Task WriteToOutboxAsync(DevicePollState device, CellInfoPacket packet, CancellationToken ct)
    {
        var reading = MapToReading(device, packet);

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

        var payload = JsonSerializer.Serialize(record);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();

        // Upsert logic: ignore duplicate readings for the same device+timestamp
        var exists = await db.OutboxRecords.AnyAsync(
            r => r.DeviceAddress == device.Address && r.RecordedAtUtc == packet.RecordedAtUtc,
            ct);

        if (!exists)
        {
            db.OutboxRecords.Add(new OutboxRecord
            {
                DeviceAddress = device.Address,
                DeviceAlias = device.Alias,
                RecordedAtUtc = packet.RecordedAtUtc,
                Payload = payload,
                CreatedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct);

            // Update hashes only after the outbox record is durably persisted.
            // If SaveChangesAsync threw, the hashes stay unchanged so the next poll
            // will re-include the config/deviceInfo snapshot.
            if (pendingConfigHash is not null)
                device.LastSentConfigHash = pendingConfigHash;
            if (pendingInfoHash is not null)
                device.LastSentDeviceInfoHash = pendingInfoHash;
        }
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
