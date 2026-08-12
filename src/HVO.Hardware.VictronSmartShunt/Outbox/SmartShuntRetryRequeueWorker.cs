using HVO.Edge.Outbox;
using HVO.Hardware.VictronSmartShunt.Configuration;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.VictronSmartShunt.Outbox;

internal sealed class SmartShuntRetryRequeueWorker(IServiceScopeFactory scopeFactory, IOptions<SmartShuntOptions> options, TimeProvider timeProvider, ILogger<SmartShuntRetryRequeueWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(options.Value.RetryExhaustedRequeueMinutes);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var count = await scope.ServiceProvider.GetRequiredService<EdgeOutboxStore<DefaultEdgeOutboxDbContext>>().RequeueRetryExhaustedAsync(stoppingToken);
                if (count > 0) logger.LogInformation("Requeued {RecordCount} retry-exhausted SmartShunt observation(s).", count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogWarning(exception, "Failed to requeue retry-exhausted SmartShunt observations."); }
            await Task.Delay(interval, timeProvider, stoppingToken);
        }
    }
}
