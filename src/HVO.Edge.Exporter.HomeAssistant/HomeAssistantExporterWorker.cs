using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.Edge.Exporter.HomeAssistant;

internal sealed class HomeAssistantExporterWorker(
    IHomeAssistantEventSource source,
    HomeAssistantObservationCoordinator coordinator,
    HomeAssistantExporterState state,
    IOptions<HomeAssistantExporterOptions> options,
    TimeProvider timeProvider,
    ILogger<HomeAssistantExporterWorker> logger) : BackgroundService
{
    private readonly HomeAssistantExporterOptions options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
            return;
        var delay = TimeSpan.FromSeconds(options.InitialReconnectDelaySeconds);
        var maximum = TimeSpan.FromSeconds(options.MaxReconnectDelaySeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            var reconciled = false;
            try
            {
                using var sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var session = source.RunSessionAsync(async (snapshot, cancellationToken) =>
                {
                    state.SetConnected();
                    await coordinator.ReconcileAsync(snapshot, cancellationToken);
                    reconciled = true;
                }, coordinator.ApplyAsync, sessionCancellation.Token);
                var flushing = FlushLoopAsync(sessionCancellation.Token);
                try
                {
                    await await Task.WhenAny(session, flushing);
                }
                finally
                {
                    await sessionCancellation.CancelAsync();
                    try { await Task.WhenAll(session, flushing); }
                    catch (OperationCanceledException) when (sessionCancellation.IsCancellationRequested) { }
                }
                state.SetDisconnected("session_closed");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                state.SetDisconnected("websocket_session");
                logger.LogWarning(exception, "Home Assistant exporter session failed; reconnecting.");
            }
            if (reconciled)
                delay = TimeSpan.FromSeconds(options.InitialReconnectDelaySeconds);
            try
            {
                await Task.Delay(delay, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, maximum.TotalSeconds));
        }
        state.SetDisconnected("stopped");
    }

    private async Task FlushLoopAsync(CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromMilliseconds(Math.Min(options.CoalescingWindowMilliseconds, 100));
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(interval, timeProvider, cancellationToken);
            await coordinator.FlushAsync(cancellationToken);
        }
    }
}
