using HVO.Edge.Contracts;
using HVO.Edge.Hosting.Diagnostics;
using HVO.Edge.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HVO.Edge.Exporter.HomeAssistant;

internal sealed class HomeAssistantExporterDiagnosticsProvider(
    HomeAssistantExporterState state,
    IOptions<HomeAssistantExporterOptions> options,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider) : IEdgeDiagnosticsSnapshotProvider
{
    public async ValueTask<EdgeDiagnosticsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return new EdgeDiagnosticsSnapshot(
                new GatewayHealthSnapshot(
                    GatewayHealthState.Warning,
                    timeProvider.GetUtcNow().UtcDateTime,
                    [new GatewayHealthAlert("home-assistant-exporter-disabled", GatewayAlertSeverity.Info, "Home Assistant export is disabled.")],
                    GatewaySampleState.Waiting,
                    "idle",
                    "disabled"),
                new GatewayDeviceCounts(0, 0, 0, 0));
        }
        var snapshot = state.Snapshot();
        await using var scope = scopeFactory.CreateAsyncScope();
        var outbox = await scope.ServiceProvider.GetRequiredService<EdgeOutboxDiagnostics>().ReadAsync(cancellationToken);
        var alerts = new List<GatewayHealthAlert>();
        if (snapshot.Failure is not null)
            alerts.Add(new GatewayHealthAlert("home-assistant", GatewayAlertSeverity.Critical, snapshot.Failure));
        if (!snapshot.LastObservationUtc.HasValue && snapshot.Connected)
            alerts.Add(new GatewayHealthAlert("home-assistant-no-valid-observation", GatewayAlertSeverity.Warning, "No complete approved observation has been persisted."));
        if (outbox.FailedCount > 0)
            alerts.Add(new GatewayHealthAlert("outbox-failed", GatewayAlertSeverity.Warning, $"{outbox.FailedCount} outbox record(s) require attention."));
        if (!string.IsNullOrWhiteSpace(outbox.LastError))
            alerts.Add(new GatewayHealthAlert("outbox-forwarding", GatewayAlertSeverity.Warning, "Central forwarding is currently degraded."));
        var healthState = GetHealthState(snapshot.Connected, alerts);
        return new EdgeDiagnosticsSnapshot(
            new GatewayHealthSnapshot(
                healthState,
                timeProvider.GetUtcNow().UtcDateTime,
                alerts,
                snapshot.LastObservationUtc.HasValue ? GatewaySampleState.Live : GatewaySampleState.Waiting,
                outbox.PendingCount > 0 ? "pending" : outbox.FailedCount > 0 ? "degraded" : "current",
                !string.IsNullOrWhiteSpace(outbox.LastError) ? "degraded" : snapshot.Connected ? "subscribed" : "disconnected"),
            new GatewayDeviceCounts(
                options.Value.Mappings.Count,
                snapshot.Connected ? options.Value.Mappings.Count : 0,
                0,
                snapshot.Connected ? 0 : options.Value.Mappings.Count));
    }

    internal static GatewayHealthState GetHealthState(bool connected, IReadOnlyList<GatewayHealthAlert> alerts)
    {
        if (!connected || alerts.Any(static alert => alert.Severity == GatewayAlertSeverity.Critical))
            return GatewayHealthState.Critical;
        return alerts.Any(static alert => alert.Severity == GatewayAlertSeverity.Warning)
            ? GatewayHealthState.Warning
            : GatewayHealthState.Healthy;
    }
}
