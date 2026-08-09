using HVO.Edge.Outbox;
using HVO.Hardware.Eg4.Configuration;
using HVO.Hardware.Eg4.Outbox;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.Eg4.Dashboard;

public sealed class Eg4RuntimeOutboxDashboardProvider(
    PowerApiForwarder forwarder,
    RuntimeOutboxSettings runtimeSettings,
    IOptions<OutboxOptions> options) : IEg4OutboxDashboardProvider
{
    public Eg4OutboxDashboard GetSnapshot()
    {
        var configured = options.Value;
        return new Eg4OutboxDashboard(
            forwarder.PendingCount,
            forwarder.FailedCount,
            forwarder.LastSentAtUtc,
            forwarder.LastBatchCount,
            !forwarder.IsConfigured ? "Not configured" : forwarder.LastError is null ? "Ready" : "Degraded",
            forwarder.EndpointHost,
            forwarder.LastError,
            runtimeSettings.EffectiveBatchSize(configured.BatchSize),
            runtimeSettings.EffectiveSweepIntervalSeconds(configured.SweepIntervalSeconds),
            runtimeSettings.BatchSizeOverride.HasValue || runtimeSettings.SweepIntervalSecondsOverride.HasValue,
            true,
            forwarder.PermanentFailedCount,
            forwarder.RetryExhaustedCount);
    }

    public Eg4OutboxSettingsResponse Update(Eg4OutboxSettingsUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (update.Reset)
            runtimeSettings.Reset();
        else
        {
            runtimeSettings.BatchSizeOverride = update.BatchSize;
            runtimeSettings.SweepIntervalSecondsOverride = update.SweepIntervalSeconds;
        }
        return new Eg4OutboxSettingsResponse(
            runtimeSettings.EffectiveBatchSize(options.Value.BatchSize),
            runtimeSettings.EffectiveSweepIntervalSeconds(options.Value.SweepIntervalSeconds),
            runtimeSettings.BatchSizeOverride.HasValue || runtimeSettings.SweepIntervalSecondsOverride.HasValue);
    }
}
