using System.Text.Json;
using HVO.Hardware.JkBms.Bms;
using HVO.Hardware.JkBms.Configuration;
using HVO.Hardware.JkBms.Outbox;
using HVO.Hardware.JkBms.Protocol;
using HVO.Hardware.JkBms.Protocol.Packets;
using HVO.Hardware.JkBms.Protocol.Transport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.JkBms.Workers;

/// <summary>
/// Per-device mutable state tracked by the poller worker.
/// Not thread-safe — accessed only from the worker's event loop.
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
    private readonly ILogger<BmsPollerWorker> _logger;

    private readonly List<DevicePollState> _devices;

    // ── Public state (Blazor status page reads these) ─────────────────────────

    /// <summary>Snapshot of device states for the status page. Replaced atomically on each poll.</summary>
    public IReadOnlyList<DevicePollState> DeviceStates => _devices;

    /// <summary>Raised after each poll attempt (success or failure). Subscribers update the UI.</summary>
    public event Action? DeviceStateChanged;

    public BmsPollerWorker(
        IBmsTransportFactory transportFactory,
        ILoggerFactory loggerFactory,
        IServiceScopeFactory scopeFactory,
        IBmsAlarmHandler alarmHandler,
        IOptions<JkBmsOptions> options,
        ILogger<BmsPollerWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _alarmHandler = alarmHandler;
        _options = options.Value;
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
                // Stagger first polls across devices to avoid simultaneous BLE contention on startup
                NextPollAt = DateTime.UtcNow.AddSeconds(enabledDevices.IndexOf(d) * 3),
            })
            .ToList();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "BmsPollerWorker starting. {Count} device(s).",
            _devices.Count);

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
        _logger.LogDebug("Polling {Alias} ({Address})", device.Alias, device.Address);

        try
        {
            var packet = await _clients[device.Address].PollCellInfoAsync(ct);

            device.LatestReading = packet;
            device.LastPollAt = DateTime.UtcNow;
            device.ConsecutiveErrors = 0;
            device.BackoffLevel = 0;
            device.LastError = null;
            device.NextPollAt = DateTime.UtcNow.AddSeconds(device.PollIntervalSeconds);

            await WriteToOutboxAsync(device, packet, ct);

            if (packet.HasAlarms)
            {
                var reading = MapToReading(device, packet);
                await _alarmHandler.HandleAsync(reading, ct);
            }

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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            device.ConsecutiveErrors++;
            device.LastError = ex.Message;
            device.BackoffLevel = Math.Min(device.BackoffLevel + 1, 10);

            // Exponential backoff: 2^level seconds, capped at 10 minutes
            int backoffSeconds = Math.Min((int)Math.Pow(2, device.BackoffLevel), 600);
            device.NextPollAt = DateTime.UtcNow.AddSeconds(backoffSeconds);

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
        var payload = JsonSerializer.Serialize(reading);

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
}
