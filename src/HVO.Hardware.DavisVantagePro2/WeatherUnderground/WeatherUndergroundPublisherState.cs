namespace HVO.Hardware.DavisVantagePro2.WeatherUnderground;

internal sealed class WeatherUndergroundPublisherState
{
    private readonly object sync = new();
    private WeatherUndergroundPublisherSnapshot snapshot = new(null, null, null, 0, null);

    public WeatherUndergroundPublisherSnapshot Snapshot()
    {
        lock (sync)
            return snapshot;
    }

    public void Observed(DateTime observedAtUtc)
    {
        lock (sync)
            snapshot = snapshot with { LastObservationAtUtc = observedAtUtc };
    }

    public void Attempted(DateTime attemptedAtUtc)
    {
        lock (sync)
            snapshot = snapshot with { LastAttemptAtUtc = attemptedAtUtc };
    }

    public void Succeeded(DateTime succeededAtUtc)
    {
        lock (sync)
            snapshot = snapshot with
            {
                LastSuccessAtUtc = succeededAtUtc,
                ConsecutiveFailures = 0,
                LastError = null,
            };
    }

    public void Failed(WeatherUndergroundOutcome outcome)
    {
        lock (sync)
            snapshot = snapshot with
            {
                ConsecutiveFailures = snapshot.ConsecutiveFailures + 1,
                LastError = outcome.Category(),
            };
    }

    public void Skipped(WeatherUndergroundOutcome outcome)
    {
        lock (sync)
            snapshot = snapshot with { LastError = outcome.Category() };
    }
}

internal sealed record WeatherUndergroundPublisherSnapshot(
    DateTime? LastObservationAtUtc,
    DateTime? LastAttemptAtUtc,
    DateTime? LastSuccessAtUtc,
    int ConsecutiveFailures,
    string? LastError);
