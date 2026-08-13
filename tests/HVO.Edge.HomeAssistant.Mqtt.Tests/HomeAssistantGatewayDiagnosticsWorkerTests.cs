using FluentAssertions;
using HVO.Edge.Contracts;
using HVO.Edge.Hosting.Diagnostics;

namespace HVO.Edge.HomeAssistant.Mqtt.Tests;

[TestClass]
public sealed class HomeAssistantGatewayDiagnosticsWorkerTests
{
    [TestMethod]
    public void Definition_UsesStableReadableDiagnosticEntityIds()
    {
        var definition = HomeAssistantGatewayDiagnosticsWorker.CreateDefinition(TestSupport.Identity());

        definition.Name.Should().Be("Test Gateway Diagnostics");
        definition.Entities.Should().Contain(entity => entity.DefaultEntityId == "sensor.hvo_gateway_1_gateway_health");
        definition.Entities.Should().Contain(entity => entity.DefaultEntityId == "sensor.hvo_gateway_1_source_freshness");
        definition.Entities.Should().Contain(entity => entity.DefaultEntityId == "sensor.hvo_gateway_1_outbox_pending");
        definition.Entities.Should().Contain(entity => entity.DefaultEntityId == "binary_sensor.hvo_gateway_1_outbox_problem");
    }

    [TestMethod]
    public void State_ProjectsHealthFreshnessDeviceCountsAndOutboxProblems()
    {
        var evaluatedAt = DateTimeOffset.Parse("2026-08-13T12:00:00Z");
        var snapshot = new EdgeDiagnosticsSnapshot(
            new GatewayHealthSnapshot(
                GatewayHealthState.Warning,
                evaluatedAt.UtcDateTime,
                [new("source-stale", GatewayAlertSeverity.Warning, "Source is stale.")],
                GatewaySampleState.Stale),
            new GatewayDeviceCounts(3, 1, 1, 1));
        var outbox = new GatewayOutboxDiagnostics(
            12,
            100,
            1,
            new Dictionary<string, int> { ["permanent"] = 1 },
            evaluatedAt.UtcDateTime,
            "Forwarding failed.",
            "transient",
            new GatewayOutboxSchemaState(true, [], [], evaluatedAt.UtcDateTime),
            "pending-forward");

        var state = HomeAssistantGatewayDiagnosticsWorker.CreateState(TestSupport.Key, snapshot, outbox, evaluatedAt);

        state.ComponentValues["gateway_health"].GetString().Should().Be("warning");
        state.ComponentValues["source_freshness"].GetString().Should().Be("stale");
        state.ComponentValues["devices_offline"].GetInt32().Should().Be(1);
        state.ComponentValues["outbox_pending"].GetInt32().Should().Be(12);
        state.ComponentValues["gateway_problem"].GetBoolean().Should().BeTrue();
        state.ComponentValues["outbox_problem"].GetBoolean().Should().BeTrue();
    }

    [TestMethod]
    public void State_ReportsWaitingInsteadOfCriticalBeforeFirstAcquisition()
    {
        var evaluatedAt = DateTimeOffset.Parse("2026-08-13T12:00:00Z");
        var snapshot = new EdgeDiagnosticsSnapshot(
            new GatewayHealthSnapshot(GatewayHealthState.Critical, evaluatedAt.UtcDateTime, [], GatewaySampleState.Waiting),
            new GatewayDeviceCounts(1, 0, 1, 0));
        var outbox = new GatewayOutboxDiagnostics(
            0, 0, 0, new Dictionary<string, int>(), null, null, null,
            new GatewayOutboxSchemaState(true, [], [], evaluatedAt.UtcDateTime), "current");

        var state = HomeAssistantGatewayDiagnosticsWorker.CreateState(TestSupport.Key, snapshot, outbox, evaluatedAt);

        state.ComponentValues["gateway_health"].GetString().Should().Be("waiting");
    }
}
