using HVO.Gateway.SolarAssistant.Configuration;
using HVO.Gateway.SolarAssistant.Outbox;
using HVO.Gateway.SolarAssistant.SolarAssistant;
using Microsoft.Extensions.Options;

namespace HVO.Gateway.SolarAssistant.Workers;

/// <summary>Polls SolarAssistant REST metrics and writes normalized aggregate power snapshots to the outbox.</summary>
public sealed class SolarAssistantSnapshotWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISolarAssistantClient _client;
    private readonly SolarAssistantOptions _options;
    private readonly ILogger<SolarAssistantSnapshotWorker> _logger;

    private volatile string? _lastError;
    private long _lastSnapshotAtTicks;
    private volatile int _lastMetricCount;

    public string? LastError => _lastError;
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
            return false;
        }

        var recordedAt = DateTime.UtcNow;
        var payload = SolarAssistantPowerMapper.MapTotalSnapshot(metrics, _options, recordedAt);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var writer = scope.ServiceProvider.GetRequiredService<PowerOutboxWriter>();
        var inserted = await writer.EnqueueAsync(payload, ct);

        Volatile.Write(ref _lastSnapshotAtTicks, recordedAt.Ticks);
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
}
