using HVO.Edge.Contracts;

namespace HVO.Edge.Hosting.Diagnostics;

public interface IEdgeDiagnosticsSnapshotProvider
{
    ValueTask<EdgeDiagnosticsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
}

public sealed record EdgeDiagnosticsSnapshot(
    GatewayHealthSnapshot Health,
    GatewayDeviceCounts Devices,
    IReadOnlyList<GatewayExternalDeliveryDiagnostics>? ExternalDeliveries = null);

internal sealed class DefaultEdgeDiagnosticsSnapshotProvider : IEdgeDiagnosticsSnapshotProvider
{
    public ValueTask<EdgeDiagnosticsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(new EdgeDiagnosticsSnapshot(
            new GatewayHealthSnapshot(
                GatewayHealthState.Critical,
                DateTime.UtcNow,
                [new GatewayHealthAlert(
                    "device-health-provider-missing",
                    GatewayAlertSeverity.Critical,
                    "No device-specific health provider is registered.")],
                GatewaySampleState.Unknown,
                "unknown",
                "unknown"),
            new GatewayDeviceCounts(0, 0, 0, 0)));
}
