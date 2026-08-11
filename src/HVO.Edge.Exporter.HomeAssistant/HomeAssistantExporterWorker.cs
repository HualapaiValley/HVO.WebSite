using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.Edge.Exporter.HomeAssistant;

internal sealed class HomeAssistantExporterWorker(
    IHomeAssistantEventSource source,
    HomeAssistantStateProjector projector,
    IHomeAssistantObservationWriter writer,
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
                await source.RunSessionAsync(async (snapshot, cancellationToken) =>
                {
                    await ProcessSnapshotAsync(snapshot, cancellationToken);
                    reconciled = true;
                }, ProcessChangeAsync, stoppingToken);
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

    private async Task ProcessSnapshotAsync(IReadOnlyList<HomeAssistantState> snapshot, CancellationToken cancellationToken)
    {
        state.SetConnected();
        foreach (var observation in projector.Reconcile(snapshot))
            await PersistAsync(observation, cancellationToken);
    }

    private async Task ProcessChangeAsync(HomeAssistantState changed, CancellationToken cancellationToken)
    {
        var observation = projector.Apply(changed);
        if (observation is not null)
            await PersistAsync(observation, cancellationToken);
    }

    private async Task PersistAsync(HomeAssistantMappedObservation observation, CancellationToken cancellationToken)
    {
        await writer.EnqueueAsync(observation, cancellationToken);
        projector.Acknowledge(observation);
        state.RecordObservation(observation.RecordedAtUtc.UtcDateTime);
    }
}
