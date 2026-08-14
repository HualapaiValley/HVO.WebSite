namespace HVO.Hardware.DavisVantagePro2.Cwop;

internal sealed class CwopPublisherState
{
    private readonly object sync = new();
    private CwopPublisherSnapshot snapshot = new(null, null, null, 0, null);

    public CwopPublisherSnapshot Snapshot() { lock (sync) return snapshot; }
    public void Observed(DateTime value) { lock (sync) snapshot = snapshot with { LastObservationAtUtc = value }; }
    public void Attempted(DateTime value) { lock (sync) snapshot = snapshot with { LastAttemptAtUtc = value }; }
    public void Succeeded(DateTime value) { lock (sync) snapshot = snapshot with { LastSuccessAtUtc = value, ConsecutiveFailures = 0, LastError = null }; }
    public void Failed(string error) { lock (sync) snapshot = snapshot with { ConsecutiveFailures = snapshot.ConsecutiveFailures + 1, LastError = error }; }
}

internal sealed record CwopPublisherSnapshot(
    DateTime? LastObservationAtUtc,
    DateTime? LastAttemptAtUtc,
    DateTime? LastSuccessAtUtc,
    int ConsecutiveFailures,
    string? LastError);
