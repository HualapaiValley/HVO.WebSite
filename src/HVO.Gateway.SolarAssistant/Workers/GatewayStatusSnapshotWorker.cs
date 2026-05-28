using HVO.Gateway.SolarAssistant.Configuration;
using HVO.Gateway.SolarAssistant.Outbox;
using HVO.Gateway.SolarAssistant.SolarAssistant.Health;
using Microsoft.Extensions.Options;

namespace HVO.Gateway.SolarAssistant.Workers;

public sealed class GatewayStatusSnapshotWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IGatewayStatusPayloadProvider _statusProvider;
    private readonly SolarAssistantOptions _options;
    private readonly ILogger<GatewayStatusSnapshotWorker> _logger;

    public GatewayStatusSnapshotWorker(
        IServiceScopeFactory scopeFactory,
        IGatewayStatusPayloadProvider statusProvider,
        IOptions<SolarAssistantOptions> options,
        ILogger<GatewayStatusSnapshotWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _statusProvider = statusProvider;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Gateway status snapshot worker starting.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await QueueOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gateway status snapshot queue failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.GatewayStatusIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Gateway status snapshot worker stopped.");
    }

    internal async Task<bool> QueueOnceAsync(CancellationToken ct)
    {
        var payload = _statusProvider.CreatePayload();
        await using var scope = _scopeFactory.CreateAsyncScope();
        var writer = scope.ServiceProvider.GetRequiredService<PowerInventoryConfigurationWriter>();
        var inserted = await writer.EnqueueGatewayStatusAsync(payload, ct);
        if (inserted)
        {
            _logger.LogDebug(
                "Queued gateway status snapshot for {SourceId} at {RecordedAt:O}",
                payload.SourceId,
                payload.RecordedAtUtc);
        }

        return inserted;
    }
}
