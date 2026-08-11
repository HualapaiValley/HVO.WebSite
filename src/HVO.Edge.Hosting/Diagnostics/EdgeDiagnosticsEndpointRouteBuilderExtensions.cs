using HVO.Edge.Contracts;
using HVO.Edge.Hosting.Telemetry;
using HVO.Edge.Outbox;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HVO.Edge.Hosting.Diagnostics;

public static class EdgeDiagnosticsEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapHvoEdgeRuntimeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
        endpoints.MapGet("/health", GetHealthAsync);
        endpoints.MapGet("/health/ready", GetHealthAsync);

        var diagnostics = endpoints.MapGroup("/diagnostics")
            .AddEndpointFilter<EdgeDiagnosticsAuthorizationFilter>();
        diagnostics.MapGet("/health", GetHealthAsync);
        diagnostics.MapGet("/outbox", async (EdgeOutboxDiagnostics outbox, CancellationToken ct) =>
            Results.Ok(await outbox.ReadAsync(ct).ConfigureAwait(false)));
        diagnostics.MapGet("/status", GetStatusAsync);
        diagnostics.MapPut("/outbox/settings", UpdateOutboxSettings);
        return endpoints;
    }

    private static async Task<IResult> GetHealthAsync(
        IEdgeDiagnosticsSnapshotProvider provider,
        CancellationToken cancellationToken)
    {
        var snapshot = await provider.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return snapshot.Health.State == GatewayHealthState.Critical
            ? Results.Json(snapshot.Health, statusCode: StatusCodes.Status503ServiceUnavailable)
            : Results.Ok(snapshot.Health);
    }

    private static async Task<IResult> GetStatusAsync(
        EdgeRuntimeIdentity identity,
        EdgeRuntimeState runtime,
        IHostEnvironment environment,
        IConfiguration configuration,
        IEdgeDiagnosticsSnapshotProvider provider,
        EdgeOutboxDiagnostics outbox,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var snapshot = await provider.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var outboxDiagnostics = await outbox.ReadAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(new GatewayDiagnosticStatusResponse(
            GatewayTelemetryConventions.Version,
            identity.ToGatewayIdentity(),
            new GatewayRuntimeInfo(runtime.StartedAtUtc, now, now - runtime.StartedAtUtc, environment.EnvironmentName, identity.ServiceVersion),
            snapshot.Health,
            snapshot.Devices,
            outboxDiagnostics,
            new GatewayTelemetryDiagnostics(
                EdgeTelemetryServiceCollectionExtensions.HasConfiguredOtlpEndpoint(configuration),
                identity.ServiceName,
                GatewayTelemetryConventions.MetricNames.All,
                [GatewayTelemetryConventions.ActivitySourceName]),
            new Dictionary<string, string>
            {
                ["health"] = "/diagnostics/health",
                ["outbox"] = "/diagnostics/outbox"
            }));
    }

    private static IResult UpdateOutboxSettings(
        OutboxSettingsUpdate? update,
        RuntimeOutboxSettings runtime,
        IOptions<EdgeOutboxOptions> configured)
    {
        if (update is null)
            return Results.BadRequest(new { error = "Request body is required." });

        try
        {
            if (update.Reset == true)
                runtime.Reset();
            else
            {
                if (update.BatchSize.HasValue)
                    runtime.BatchSizeOverride = update.BatchSize;
                if (update.SweepIntervalSeconds.HasValue)
                    runtime.SweepIntervalSecondsOverride = update.SweepIntervalSeconds;
            }

            return Results.Ok(new OutboxSettingsResponse(
                runtime.EffectiveBatchSize(configured.Value.BatchSize),
                runtime.EffectiveSweepIntervalSeconds(configured.Value.SweepIntervalSeconds),
                runtime.BatchSizeOverride.HasValue || runtime.SweepIntervalSecondsOverride.HasValue));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }
}
