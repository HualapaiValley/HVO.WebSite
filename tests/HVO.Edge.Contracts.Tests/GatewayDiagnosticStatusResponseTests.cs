using System.Text.Json;
using FluentAssertions;
using HVO.Edge.Contracts;

namespace HVO.Edge.Contracts.Tests;

[TestClass]
public sealed class GatewayDiagnosticStatusResponseTests
{
    [TestMethod]
    public void Serialize_IncludesStandardDiagnosticSections()
    {
        var evaluatedAt = new DateTime(2026, 6, 17, 12, 0, 0, DateTimeKind.Utc);
        var response = new GatewayDiagnosticStatusResponse(
            ContractVersion: "2026-06-17",
            Identity: new GatewayIdentity("hvo-tplink-kasa", "TP-Link Kasa Gateway", GatewayDomain.Power, "kasa"),
            Runtime: new GatewayRuntimeInfo(evaluatedAt.AddHours(-1), evaluatedAt, TimeSpan.FromHours(1), "Production", "1.2.3"),
            Health: new GatewayHealthSnapshot(
                GatewayHealthState.Warning,
                evaluatedAt,
                [new GatewayHealthAlert("device-offline", GatewayAlertSeverity.Warning, "One configured device is offline.")],
                GatewaySampleState.Stale,
                "pending",
                "healthy"),
            Devices: new GatewayDeviceCounts(Configured: 8, Online: 2, Degraded: 0, Offline: 6),
            Outbox: new GatewayOutboxDiagnostics(
                PendingCount: 3,
                SentCount: 42,
                FailedCount: 1,
                FailedCountByKind: new Dictionary<string, int> { ["Permanent"] = 1 },
                LastSentAtUtc: evaluatedAt.AddMinutes(-5),
                LastError: "HTTP 503",
                LastFailureKind: "Permanent",
                Schema: new GatewayOutboxSchemaState(true, [], [], evaluatedAt),
                MaintenanceState: "failed-records-present"),
            Telemetry: new GatewayTelemetryDiagnostics(
                OtlpEndpointConfigured: true,
                ServiceName: "hvo-tplink-kasa",
                MetricNames: ["hvo.gateway.outbox.depth"],
                ActivitySourceNames: ["hvo.gateway"]),
            Links: new Dictionary<string, string>
            {
                ["health"] = "/diagnostics/health",
                ["outbox"] = "/diagnostics/outbox",
            });

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        json.Should().Contain("contractVersion");
        json.Should().Contain("identity");
        json.Should().Contain("runtime");
        json.Should().Contain("health");
        json.Should().Contain("devices");
        json.Should().Contain("outbox");
        json.Should().Contain("telemetry");
        json.Should().Contain("failedCountByKind");
        json.Should().Contain("maintenanceState");
    }

    [TestMethod]
    public void TelemetryConventions_DefineSharedGatewayNames()
    {
        GatewayTelemetryConventions.ResourceAttributes.GatewayId.Should().Be("hvo.gateway.id");
        GatewayTelemetryConventions.ResourceAttributes.GatewayType.Should().Be("hvo.gateway.type");
        GatewayTelemetryConventions.OperationNames.OutboxForward.Should().Be("gateway.outbox.forward");
        GatewayTelemetryConventions.OperationNames.HealthEvaluate.Should().Be("gateway.health.evaluate");
        GatewayTelemetryConventions.MetricNames.OutboxDepth.Should().Be("gateway.outbox.depth");
        GatewayTelemetryConventions.MetricNames.DevicePollFailure.Should().Be("gateway.device.poll.failure");
        GatewayTelemetryConventions.Tags.FailureKind.Should().Be("hvo.failure.kind");
    }
}
