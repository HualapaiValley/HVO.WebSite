namespace HVO.Hardware.Eg4.Dashboard;

public sealed record Eg4OutboxSettingsUpdate(int? BatchSize = null, int? SweepIntervalSeconds = null, bool Reset = false);
public sealed record Eg4OutboxSettingsResponse(int BatchSize, int SweepIntervalSeconds, bool IsOverride);

public interface IEg4OutboxDashboardProvider
{
    Eg4OutboxDashboard GetSnapshot();
    Eg4OutboxSettingsResponse Update(Eg4OutboxSettingsUpdate update);
}

public sealed class UnavailableEg4OutboxDashboardProvider : IEg4OutboxDashboardProvider
{
    private static readonly Eg4OutboxDashboard Snapshot = new(
        0, 0, null, 0, "Collector not active", null, null, 50, 5, false, false);

    public Eg4OutboxDashboard GetSnapshot() => Snapshot;

    public Eg4OutboxSettingsResponse Update(Eg4OutboxSettingsUpdate update) =>
        throw new InvalidOperationException("Outbox runtime controls are unavailable until the collector is active.");
}
