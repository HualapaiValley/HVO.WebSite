using FluentAssertions;
using HVO.Edge.Contracts;

namespace HVO.Edge.Exporter.HomeAssistant.Tests;

[TestClass]
public sealed class HomeAssistantExporterDiagnosticsProviderTests
{
    [TestMethod]
    public void GetHealthState_CriticalAlertOverridesConnectedState()
    {
        var alerts = new[]
        {
            new GatewayHealthAlert("session", GatewayAlertSeverity.Critical, "Session failed")
        };

        HomeAssistantExporterDiagnosticsProvider.GetHealthState(connected: true, alerts)
            .Should().Be(GatewayHealthState.Critical);
    }

    [TestMethod]
    public void GetHealthState_ConnectedWarningIsWarning()
    {
        var alerts = new[]
        {
            new GatewayHealthAlert("outbox", GatewayAlertSeverity.Warning, "Delivery delayed")
        };

        HomeAssistantExporterDiagnosticsProvider.GetHealthState(connected: true, alerts)
            .Should().Be(GatewayHealthState.Warning);
    }
}
