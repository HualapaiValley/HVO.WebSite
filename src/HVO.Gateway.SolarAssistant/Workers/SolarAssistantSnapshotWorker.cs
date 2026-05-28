using System.Text.Json;
using HVO.Edge.Contracts.PowerSystem;
using HVO.Edge.Outbox;
using HVO.Gateway.SolarAssistant.Configuration;
using HVO.Gateway.SolarAssistant.Outbox;
using HVO.Gateway.SolarAssistant.SolarAssistant;
using HVO.Gateway.SolarAssistant.SolarAssistant.Mqtt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HVO.Gateway.SolarAssistant.Workers;

/// <summary>Polls SolarAssistant REST metrics and writes normalized aggregate power snapshots to the outbox.</summary>
public sealed class SolarAssistantSnapshotWorker : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISolarAssistantClient _client;
    private readonly SolarAssistantOptions _options;
    private readonly ILogger<SolarAssistantSnapshotWorker> _logger;
    private readonly object _historyLock = new();
    private readonly Queue<PowerSnapshotHistoryPoint> _history = new();

    private volatile string? _lastError;
    private volatile SolarAssistantMetricInventory? _lastInventory;
    private long _lastInventoryConfigQueuedAtTicks;
    private volatile PowerEnergyPayload? _lastEnergy;
    private volatile PowerReadingPayload? _lastSnapshot;
    private long _lastSnapshotAtTicks;
    private volatile int _lastMetricCount;

    public string? LastError => _lastError;
    public SolarAssistantMetricInventory? LastInventory => _lastInventory;
    public DateTime? LastInventoryConfigQueuedAtUtc
    {
        get
        {
            var ticks = Volatile.Read(ref _lastInventoryConfigQueuedAtTicks);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }
    public PowerReadingPayload? LastSnapshot => _lastSnapshot;
    public IReadOnlyList<PowerSnapshotHistoryPoint> History
    {
        get
        {
            lock (_historyLock)
                return _history.ToArray();
        }
    }
    public DateTime? LastSnapshotAt
    {
        get
        {
            var ticks = Volatile.Read(ref _lastSnapshotAtTicks);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }
    public int LastMetricCount => _lastMetricCount;

    public SolarAssistantSnapshotWorker(
        IServiceScopeFactory scopeFactory,
        ISolarAssistantClient client,
        IOptions<SolarAssistantOptions> options,
        ILogger<SolarAssistantSnapshotWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Host))
        {
            _logger.LogWarning("SolarAssistant host is not configured; snapshot polling is disabled.");
            return;
        }

        _logger.LogInformation("SolarAssistant snapshot worker starting for host {Host}", _options.Host);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                _logger.LogWarning(ex, "SolarAssistant snapshot poll failed");
                await TryHydrateLatestSnapshotAsync(stoppingToken);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.SnapshotIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("SolarAssistant snapshot worker stopped.");
    }

    internal async Task<bool> PollOnceAsync(CancellationToken ct)
    {
        var metrics = await _client.GetMetricsAsync(ct);
        _lastMetricCount = metrics.Count;

        if (metrics.Count == 0)
        {
            _logger.LogDebug("SolarAssistant snapshot poll returned no metrics.");
            await TryHydrateLatestSnapshotAsync(ct);
            return false;
        }

        var recordedAt = DateTime.UtcNow;
        _lastInventory = SolarAssistantMetricInventoryBuilder.Build(metrics, recordedAt);
        var payload = SolarAssistantPowerMapper.MapTotalSnapshot(metrics, _options, recordedAt);
        var energy = SolarAssistantEnergyInverterDetailMapper.MapEnergy(metrics, _options, recordedAt, _lastEnergy);
        var inverterDetail = SolarAssistantEnergyInverterDetailMapper.MapInverterDetail(metrics, _options, recordedAt);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var writer = scope.ServiceProvider.GetRequiredService<PowerOutboxWriter>();
        var inserted = await writer.EnqueueAsync(payload, ct);
        await TryEnqueueInventoryConfigurationAsync(scope.ServiceProvider, metrics, recordedAt, ct);
        await TryEnqueueEnergyInverterDetailAsync(scope.ServiceProvider, energy, inverterDetail, ct);

        Volatile.Write(ref _lastSnapshotAtTicks, recordedAt.Ticks);
        _lastSnapshot = payload;
        if (energy.Counters.Count > 0)
            _lastEnergy = energy;
        AddHistory(payload);
        _lastError = null;
        if (inserted)
        {
            _logger.LogInformation(
                "Queued SolarAssistant power snapshot for {SourceId} at {RecordedAt:O} from {MetricCount} metrics",
                payload.SourceId,
                recordedAt,
                metrics.Count);
        }

        return inserted;
    }

    private async Task TryEnqueueInventoryConfigurationAsync(
        IServiceProvider serviceProvider,
        IReadOnlyList<SolarAssistantMetric> metrics,
        DateTime recordedAt,
        CancellationToken ct)
    {
        var mqttInventory = serviceProvider.GetRequiredService<SolarAssistantMqttInventoryStore>().Snapshot;
        var inventory = SolarAssistantInventoryConfigurationMapper.MapDeviceInventory(metrics, mqttInventory, _options, recordedAt);
        var configuration = SolarAssistantInventoryConfigurationMapper.MapConfiguration(metrics, mqttInventory, _options, recordedAt);
        if (inventory.Devices.Count == 0 && configuration.Settings.Count == 0 && configuration.CommandCapabilities.Count == 0)
            return;

        var writer = serviceProvider.GetRequiredService<PowerInventoryConfigurationWriter>();
        var inventoryInserted = await writer.EnqueueDeviceInventoryAsync(inventory, ct);
        var configurationInserted = await writer.EnqueueConfigurationAsync(configuration, ct);
        if (inventoryInserted || configurationInserted)
        {
            Volatile.Write(ref _lastInventoryConfigQueuedAtTicks, recordedAt.Ticks);
            _logger.LogInformation(
                "Queued SolarAssistant inventory/config snapshot at {RecordedAt:O}: {DeviceCount} device(s), {SettingCount} setting(s), {CapabilityCount} command capabilit(ies)",
                recordedAt,
                inventory.Devices.Count,
                configuration.Settings.Count,
                configuration.CommandCapabilities.Count);
        }
    }

    private static async Task TryEnqueueEnergyInverterDetailAsync(
        IServiceProvider serviceProvider,
        PowerEnergyPayload energy,
        PowerInverterDetailPayload inverterDetail,
        CancellationToken ct)
    {
        var writer = serviceProvider.GetRequiredService<PowerInventoryConfigurationWriter>();
        if (energy.Counters.Count > 0)
            await writer.EnqueueEnergyAsync(energy, ct);

        if (inverterDetail.PvStrings.Count > 0 || inverterDetail.Load is not null || inverterDetail.Battery is not null ||
            inverterDetail.TemperatureC is not null || inverterDetail.Statuses.Count > 0)
        {
            await writer.EnqueueInverterDetailAsync(inverterDetail, ct);
        }
    }

    private async Task<bool> TryHydrateLatestSnapshotAsync(CancellationToken ct)
    {
        if (_lastSnapshot is not null)
            return false;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
            var payloadJson = await db.OutboxRecords
                .AsNoTracking()
                .Where(r => r.PayloadType == PowerOutboxPayloadTypes.PowerReading)
                .OrderByDescending(r => r.RecordedAtUtc)
                .Select(r => r.PayloadJson)
                .FirstOrDefaultAsync(ct);
            if (string.IsNullOrWhiteSpace(payloadJson))
                return false;

            var payload = JsonSerializer.Deserialize<PowerReadingPayload>(payloadJson, JsonOptions);
            if (payload is null || payload.RecordedAtUtc == default)
                return false;

            var recordedAt = payload.RecordedAtUtc.ToUniversalTime();
            Volatile.Write(ref _lastSnapshotAtTicks, recordedAt.Ticks);
            _lastSnapshot = payload;
            AddHistory(payload);
            _logger.LogInformation(
                "Hydrated SolarAssistant dashboard snapshot from local outbox record at {RecordedAt:O}",
                recordedAt);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to hydrate SolarAssistant snapshot from local outbox payload.");
            return false;
        }
    }

    private void AddHistory(PowerReadingPayload payload)
    {
        lock (_historyLock)
        {
            _history.Enqueue(new PowerSnapshotHistoryPoint
            {
                RecordedAtUtc = payload.RecordedAtUtc,
                PvPowerW = payload.PvPowerW,
                LoadPowerW = payload.LoadPowerW,
                GridPowerW = payload.GridPowerW,
                BatteryPowerW = payload.BatteryPowerW,
            });

            while (_history.Count > _options.HistoryCapacity)
                _history.Dequeue();
        }
    }
}
