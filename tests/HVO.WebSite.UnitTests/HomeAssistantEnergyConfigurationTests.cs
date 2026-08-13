using FluentAssertions;

namespace HVO.WebSite.UnitTests;

[TestClass]
public sealed class HomeAssistantEnergyConfigurationTests
{
    private static string ConfigurationRoot =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "HomeAssistant", "Configuration");

    [TestMethod]
    public void EnergyHelpers_PreserveAvailabilityAndKeepDirectionsSeparate()
    {
        var templates = File.ReadAllText(Path.Combine(ConfigurationRoot, "templates", "hvo.yaml"));
        var integrations = File.ReadAllText(Path.Combine(ConfigurationRoot, "sensors", "hvo-energy.yaml"));

        templates.Should().Contain("| has_value")
            .And.Contain("HVO SmartShunt Battery Charge Power")
            .And.Contain("HVO SmartShunt Battery Discharge Power");
        integrations.Should().Contain("HVO External MPPT PV Energy")
            .And.Contain("HVO SmartShunt Battery Charge Energy")
            .And.Contain("HVO SmartShunt Battery Discharge Energy")
            .And.Contain("unit_prefix: k")
            .And.Contain("max_sub_interval:");
    }

    [TestMethod]
    public void EnergyDashboard_ShowsThreePvInputsWithoutDoubleCountingCombinedSource()
    {
        var dashboard = File.ReadAllText(Path.Combine(ConfigurationRoot, "dashboards", "hvo-energy.yaml"));

        dashboard.Should().Contain("name: 6500EX MPPT 1")
            .And.Contain("name: 6500EX MPPT 2")
            .And.Contain("name: External MPPT")
            .And.Contain("name: 6500EX Combined PV")
            .And.Contain("statistics-graph");
        dashboard.Should().NotContain("grid_energy")
            .And.NotContain("Grid Import")
            .And.NotContain("Grid Export");
    }

    [TestMethod]
    public void UtilityMeters_DeriveDailyValuesFromCumulativeCounters()
    {
        var meters = File.ReadAllText(Path.Combine(ConfigurationRoot, "utility-meters", "hvo-energy.yaml"));

        meters.Should().Contain("hvo_6500ex_pv_energy_daily:")
            .And.Contain("hvo_external_mppt_pv_energy_daily:")
            .And.Contain("hvo_battery_charge_energy_daily:")
            .And.Contain("hvo_battery_discharge_energy_daily:");
        meters.Split("cycle: daily", StringSplitOptions.None).Should().HaveCount(6);
    }
}
