using HVO.Edge.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.Edge.Exporter.HomeAssistant;

internal sealed class HomeAssistantRetryRequeueWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<HomeAssistantExporterOptions> options,
    TimeProvider timeProvider,
    ILogger<HomeAssistantRetryRequeueWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(options.Value.RetryExhaustedRequeueMinutes);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RequeueAsync(stoppingToken);
                await Task.Delay(interval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to requeue retry-exhausted Home Assistant observations.");
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

    internal async Task<int> RequeueAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<EdgeOutboxStore<DefaultEdgeOutboxDbContext>>();
        var count = await store.RequeueRetryExhaustedAsync(cancellationToken);
        if (count > 0)
            logger.LogInformation("Requeued {RecordCount} retry-exhausted Home Assistant observation(s).", count);
        return count;
    }
}
