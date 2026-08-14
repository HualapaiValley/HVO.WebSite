using FluentAssertions;
using System.Text.Json;

namespace HVO.Tools.HomeAssistantEntityMigration.Tests;

[TestClass]
public sealed class EnergyPreferencesManifestTests
{
    [TestMethod]
    public async Task ManagedManifest_HasValidatedSourcesAndNonOverlappingParentLoadBreakdowns()
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
        manifest.DeviceConsumption.Should().HaveCount(7);
        manifest.DeviceConsumption.Select(entry => entry.GetProperty("stat_consumption").GetString())
            .Should().OnlyHaveUniqueItems()
            .And.NotContain(entityId => entityId != null && entityId.Contains("workshop_mr_cool"));
        manifest.DeviceConsumption.Skip(1).Should().OnlyContain(entry =>
            entry.GetProperty("included_in_stat").GetString() ==
            "sensor.hvo_3xhvo_3xeg4_13xinverter_x2da_23xload_x5fenergy_x5ftotal");
        manifest.RemovedDeviceConsumptionEntities.Should()
            .ContainSingle("sensor.workshop_power_strip_plug_1_workshop_mr_cool_ac_today_s_consumption");
        manifest.ReplacedManagedNames.Should().ContainSingle("HVO AC Load");
        manifest.ManagedNames.Should().Contain("HVO AC Load").And.Contain("HVO 6500EX AC Load");
    }

    [TestMethod]
    public async Task IncludedInStat_UnconfiguredParent_IsRejected()
    {
        var path = await WriteManifestAsync(
            Device("sensor.child_energy", "Child", "sensor.missing_parent"));

        var act = () => EnergyPreferencesManifest.LoadAsync(path, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*parent is not configured*sensor.missing_parent*");
    }

    [TestMethod]
    public async Task IncludedInStat_SelfReference_IsRejected()
    {
        var path = await WriteManifestAsync(
            Device("sensor.child_energy", "Child", "sensor.child_energy"));

        var act = () => EnergyPreferencesManifest.LoadAsync(path, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*cannot include itself*");
    }

    [TestMethod]
    public async Task IncludedInStat_Cycle_IsRejected()
    {
        var path = await WriteManifestAsync(
            Device("sensor.first_energy", "First", "sensor.second_energy"),
            Device("sensor.second_energy", "Second", "sensor.first_energy"));

        var act = () => EnergyPreferencesManifest.LoadAsync(path, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*cycle*");
    }

    private static object Device(string entityId, string name, string? parent = null)
    {
        var values = new Dictionary<string, object?>
        {
            ["stat_consumption"] = entityId,
            ["name"] = name,
        };
        if (parent is not null)
            values["included_in_stat"] = parent;
        return values;
    }

    private static async Task<string> WriteManifestAsync(params object[] devices)
    {
        var path = Path.Combine(Path.GetTempPath(), $"hvo-energy-manifest-{Guid.NewGuid():N}.json");
        var manifest = new
        {
            homeAssistantVersion = "2026.8.1",
            energySources = new[]
            {
                new { type = "solar", stat_energy_from = "sensor.solar_energy", name = "Solar" },
            },
            deviceConsumption = devices,
            removedDeviceConsumptionEntities = Array.Empty<string>(),
            replacedManagedNames = Array.Empty<string>(),
        };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(manifest));
        return path;
    }
}
