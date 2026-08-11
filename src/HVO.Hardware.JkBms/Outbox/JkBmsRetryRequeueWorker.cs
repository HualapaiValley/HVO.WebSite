using HVO.Edge.Outbox;
using HVO.Hardware.JkBms.Configuration;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.JkBms.Outbox;

internal sealed class JkBmsRetryRequeueWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<JkBmsOptions> options,
    TimeProvider timeProvider,
    ILogger<JkBmsRetryRequeueWorker> logger) : BackgroundService
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
                    logger.LogInformation("Requeued {RecordCount} retry-exhausted JK BMS observation(s).", count);
                await Task.Delay(interval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to requeue retry-exhausted JK BMS observations.");
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
