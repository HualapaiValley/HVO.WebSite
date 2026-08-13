namespace HVO.Edge.Exporter.HomeAssistant;

internal sealed class HomeAssistantExporterState
{
    private readonly object sync = new();
    private bool connected;
    private DateTime? lastObservationUtc;
    private string? failure;

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

    public void RecordObservation(DateTime observedAtUtc)
    {
        lock (sync)
            lastObservationUtc = observedAtUtc;
    }

    public (bool Connected, DateTime? LastObservationUtc, string? Failure) Snapshot()
    {
        lock (sync)
            return (connected, lastObservationUtc, failure);
    }
}
