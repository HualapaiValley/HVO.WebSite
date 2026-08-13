using FluentAssertions;

namespace HVO.Tools.HomeAssistantEntityMigration.Tests;

[TestClass]
public sealed class EnergyPreferencesManifestTests
{
    [TestMethod]
    public async Task ManagedManifest_HasTwoValidatedSolarSourcesOneBatteryAndNoGridSource()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "hvo-energy-preferences.json");

        var manifest = await EnergyPreferencesManifest.LoadAsync(path, CancellationToken.None);

        manifest.EnergySources.Count(source => source.GetProperty("type").GetString() == "solar").Should().Be(2);
        manifest.EnergySources.Count(source => source.GetProperty("type").GetString() == "battery").Should().Be(1);
        manifest.EnergySources.Should().NotContain(source => source.GetProperty("type").GetString() == "grid");
        manifest.ReferencedEntities.Should().Contain("sensor.hvo_smartshunt_battery_charge_energy")
            .And.Contain("sensor.hvo_smartshunt_battery_discharge_energy")
            .And.Contain("sensor.hvo_external_mppt_pv_energy")
            .And.Contain("sensor.hvo_3xhvo_3xeg4_15xcontroller_x2da_11xpv_x5fpower");
    }
}
