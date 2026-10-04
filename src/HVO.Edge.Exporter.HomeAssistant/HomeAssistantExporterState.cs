namespace HVO.Edge.Exporter.HomeAssistant;

internal sealed class HomeAssistantExporterState
{
    private readonly object sync = new();
    private bool connected;
    private DateTime? lastObservationUtc;
    private string? failure;
    private readonly Dictionary<string, string> conflicts = new(StringComparer.Ordinal);

    public void SetConnected()
    {
        lock (sync)
        {
            connected = true;
            failure = null;
        }
    }

    public void SetDisconnected(string category)
    {
        lock (sync)
        {
            connected = false;
            failure = category;
        }
    }

    public bool RecordConflict(string mappingId, string signature)
    {
        lock (sync)
        {
            var changed = !conflicts.TryGetValue(mappingId, out var previous) || previous != signature;
            conflicts[mappingId] = signature;
            return changed;
        }
    }

    public void RecordObservation(string mappingId, DateTime observedAtUtc)
    {
        lock (sync)
        {
            lastObservationUtc = observedAtUtc;
            conflicts.Remove(mappingId);
        }
    }

    public (bool Connected, DateTime? LastObservationUtc, string? Failure, IReadOnlyList<string> ConflictingMappings) Snapshot()
    {
        lock (sync)
            return (connected, lastObservationUtc, failure, conflicts.Keys.ToArray());
    }
}
