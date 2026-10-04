using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.Edge.Exporter.HomeAssistant;

/// <summary>
/// Serializes state changes and durable acknowledgement. A fixed receive-time window
/// gathers an entity burst without inventing source timestamps or waiting indefinitely.
/// </summary>
internal sealed class HomeAssistantObservationCoordinator(
    HomeAssistantStateProjector projector,
    IHomeAssistantObservationWriter writer,
    HomeAssistantExporterState state,
    IOptions<HomeAssistantExporterOptions> options,
    TimeProvider timeProvider,
    ILogger<HomeAssistantObservationCoordinator> logger) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, long> pending = new(StringComparer.Ordinal);
    private readonly TimeSpan window = TimeSpan.FromMilliseconds(options.Value.CoalescingWindowMilliseconds);

    public void Dispose() => gate.Dispose();

    public async Task ReconcileAsync(IReadOnlyList<HomeAssistantState> snapshot, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            pending.Clear();
            foreach (var observation in projector.Reconcile(snapshot))
                pending[observation.MappingId] = timeProvider.GetTimestamp();
        }
        finally { gate.Release(); }
    }

    public async Task ApplyAsync(HomeAssistantState changed, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (projector.Apply(changed) is { } observation)
                pending.TryAdd(observation.MappingId, timeProvider.GetTimestamp());
        }
        finally { gate.Release(); }
    }

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            foreach (var mappingId in pending.Keys.ToArray())
            {
                if (timeProvider.GetElapsedTime(pending[mappingId]) < window)
                    continue;
                // Re-project at persistence time: fields may have changed or expired.
                var observation = projector.Project(mappingId);
                if (observation is null)
                {
                    pending.Remove(mappingId);
                    continue;
                }
                var outcome = await writer.EnqueueAsync(observation, cancellationToken);
                if (outcome == HomeAssistantPersistenceOutcome.Conflict)
                {
                    if (state.RecordConflict(mappingId, observation.Signature))
                        logger.LogWarning("Home Assistant mapping {MappingId} has conflicting durable content at {RecordedAtUtc}; the observation remains unacknowledged.",
                            mappingId, observation.RecordedAtUtc);
                    pending[mappingId] = timeProvider.GetTimestamp();
                    continue;
                }
                projector.Acknowledge(observation);
                state.RecordObservation(mappingId, observation.RecordedAtUtc.UtcDateTime);
                pending.Remove(mappingId);
            }
        }
        finally { gate.Release(); }
    }
}
