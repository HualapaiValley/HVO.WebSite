using FluentAssertions;

namespace HVO.WebSite.UnitTests;

[TestClass]
public sealed class HomeAssistantOperationsConfigurationTests
{
    private static string ConfigurationRoot =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "HomeAssistant", "Configuration");

    [TestMethod]
    public void OperationsDashboard_CoversEnvironmentWeatherBatteriesAndGatewayHealth()
    {
        var dashboard = File.ReadAllText(Path.Combine(ConfigurationRoot, "dashboards", "hvo-operations.yaml"));

        dashboard.Should().Contain("title: Environment")
            .And.Contain("title: Weather")
            .And.Contain("title: Batteries")
            .And.Contain("title: Gateway Health")
            .And.Contain("sensor.h5074_8d05_temperature")
            .And.Contain("sensor.davis_wind_gust")
            .And.Contain("SmartShunt State Of Charge")
            .And.Contain("sensor.hvo_eg4_outbox_pending");
    }

    [TestMethod]
    public void CriticalAutomations_CoverAvailabilityFreshnessBatteryGatewayAndOutbox()
    {
        var automations = File.ReadAllText(Path.Combine(ConfigurationRoot, "automations", "hvo.yaml"));

        automations.Should().Contain("id: hvo_source_offline")
            .And.Contain("id: hvo_source_stale")
            .And.Contain("id: hvo_battery_critical")
            .And.Contain("id: hvo_gateway_critical")
            .And.Contain("id: hvo_outbox_backlog")
            .And.Contain("id: hvo_environment_sensor_offline")
            .And.Contain("action: persistent_notification.create")
            .And.Contain("action: persistent_notification.dismiss")
            .And.Contain("for: \"00:00:10\"")
            .And.Contain("replace('.', '_')");
    }
}
