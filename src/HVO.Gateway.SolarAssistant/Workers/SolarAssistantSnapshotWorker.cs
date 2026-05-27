using System.Text.Json;
using HVO.Gateway.SolarAssistant.Configuration;
using HVO.Gateway.SolarAssistant.Outbox;
using HVO.Gateway.SolarAssistant.SolarAssistant;
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
    private volatile PowerReadingPayload? _lastSnapshot;
    private long _lastSnapshotAtTicks;
    private volatile int _lastMetricCount;

    public string? LastError => _lastError;
    public SolarAssistantMetricInventory? LastInventory => _lastInventory;
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

        await using var scope = _scopeFactory.CreateAsyncScope();
        var writer = scope.ServiceProvider.GetRequiredService<PowerOutboxWriter>();
        var inserted = await writer.EnqueueAsync(payload, ct);

        Volatile.Write(ref _lastSnapshotAtTicks, recordedAt.Ticks);
        _lastSnapshot = payload;
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

    private async Task<bool> TryHydrateLatestSnapshotAsync(CancellationToken ct)
    {
        if (_lastSnapshot is not null)
            return false;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
        var payloadJson = await db.OutboxRecords
            .AsNoTracking()
            .OrderByDescending(r => r.RecordedAtUtc)
            .Select(r => r.Payload)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(payloadJson))
            return false;

        PowerReadingPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<PowerReadingPayload>(payloadJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to hydrate SolarAssistant snapshot from local outbox payload.");
            return false;
        }

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
