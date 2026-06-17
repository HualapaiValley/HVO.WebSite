namespace HVO.Edge.Contracts;

public sealed record GatewayDiagnosticStatusResponse(
    string ContractVersion,
    GatewayIdentity Identity,
    GatewayRuntimeInfo Runtime,
    GatewayHealthSnapshot Health,
    GatewayDeviceCounts Devices,
    GatewayOutboxDiagnostics Outbox,
    GatewayTelemetryDiagnostics Telemetry,
    IReadOnlyDictionary<string, string> Links);

public sealed record GatewayRuntimeInfo(
    DateTime StartedAtUtc,
    DateTime EvaluatedAtUtc,
    TimeSpan Uptime,
    string EnvironmentName,
    string? Version = null);

public sealed record GatewayDeviceCounts(
    int Configured,
    int Online,
    int Degraded,
    int Offline)
{
    public static GatewayDeviceCounts SingleSource(bool isFresh, bool isDegraded = false) =>
        new(1, isFresh ? 1 : 0, isDegraded ? 1 : 0, isFresh ? 0 : 1);
}

public sealed record GatewayOutboxDiagnostics(
    int PendingCount,
    int SentCount,
    int FailedCount,
    IReadOnlyDictionary<string, int> FailedCountByKind,
    DateTime? LastSentAtUtc,
    string? LastError,
    string? LastFailureKind,
    GatewayOutboxSchemaState Schema,
    string MaintenanceState = "unknown");

public sealed record GatewayOutboxSchemaState(
    bool IsCompatible,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    DateTime EvaluatedAtUtc);

public sealed record GatewayTelemetryDiagnostics(
    bool OtlpEndpointConfigured,
    string? ServiceName,
    IReadOnlyList<string> MetricNames,
    IReadOnlyList<string> ActivitySourceNames);
