using HVO.Edge.Outbox;
using HVO.Hardware.Eg4.Configuration;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.Eg4.Outbox;

internal sealed class Eg4RetryRequeueWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<Eg4Options> options,
    TimeProvider timeProvider,
    ILogger<Eg4RetryRequeueWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(options.Value.RetryExhaustedRequeueMinutes);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<EdgeOutboxStore<DefaultEdgeOutboxDbContext>>();
                var count = await store.RequeueRetryExhaustedAsync(stoppingToken);
                if (count > 0)
                    logger.LogInformation("Requeued {RecordCount} retry-exhausted EG4 observation(s).", count);
                await Task.Delay(interval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to requeue retry-exhausted EG4 observations.");
                try
                {
                    await Task.Delay(interval, timeProvider, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }
}
