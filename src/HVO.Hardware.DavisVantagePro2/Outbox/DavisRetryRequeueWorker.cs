using HVO.Edge.Outbox;
using HVO.Hardware.DavisVantagePro2.Configuration;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.DavisVantagePro2.Outbox;

internal sealed class DavisRetryRequeueWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<StationOptions> options,
    TimeProvider timeProvider,
    ILogger<DavisRetryRequeueWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(options.Value.RetryExhaustedRequeueMinutes);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var count = await scope.ServiceProvider.GetRequiredService<EdgeOutboxStore<DefaultEdgeOutboxDbContext>>()
                    .RequeueRetryExhaustedAsync(stoppingToken);
                if (count > 0)
                    logger.LogInformation("Requeued {RecordCount} retry-exhausted Davis observation(s)", count);
                await Task.Delay(interval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to requeue retry-exhausted Davis observations");
                await Task.Delay(interval, timeProvider, stoppingToken);
            }
        }
    }
}
