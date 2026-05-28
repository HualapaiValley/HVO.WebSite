using FluentAssertions;
using HVO.Gateway.SolarAssistant.Configuration;
using HVO.Gateway.SolarAssistant.SolarAssistant;

namespace HVO.Gateway.SolarAssistant.Tests.SolarAssistant;

[TestClass]
public sealed class SolarAssistantMetricInventoryBuilderTests
{
    [TestMethod]
    public void Build_ClassifiesMetricsWithoutValues()
    {
        var inventory = SolarAssistantMetricInventoryBuilder.Build(
            [
                new SolarAssistantMetric { Topic = "total/pv_power", Unit = "W", Name = "PV power" },
                new SolarAssistantMetric { Topic = "inverter_1/pv_power_1", Unit = "W", Name = "PV power 1" },
                new SolarAssistantMetric { Topic = "inverter_1/serial_number", Name = "Serial number" },
            ],
            new DateTime(2026, 5, 23, 12, 0, 0, DateTimeKind.Utc));

        inventory.MetricCount.Should().Be(3);
        inventory.Topics.Should().HaveCount(3);
        inventory.ClassificationCounts[SolarAssistantMetricClassification.DbCandidate].Should().Be(2);
        inventory.ClassificationCounts[SolarAssistantMetricClassification.LocalOnly].Should().Be(1);
        inventory.Topics.Single(t => t.Topic == "total/pv_power").Classification.Should().Be(SolarAssistantMetricClassification.DbCandidate);
        inventory.Topics.Single(t => t.Topic == "inverter_1/pv_power_1").Classification.Should().Be(SolarAssistantMetricClassification.DbCandidate);
        inventory.Topics.Single(t => t.Topic == "inverter_1/serial_number").Classification.Should().Be(SolarAssistantMetricClassification.LocalOnly);
    }

    [TestMethod]
    public void MapEnergy_DetectsCounterResetAndKeepsSignedFlowsAsSeparateCounters()
    {
        var options = new SolarAssistantOptions { TotalSourceId = "configured-source", TotalDeviceId = "total" };
        var previous = SolarAssistantEnergyInverterDetailMapper.MapEnergy(
            [
                new SolarAssistantMetric { Topic = "total/pv_energy", Value = 100.5 },
                new SolarAssistantMetric { Topic = "total/grid_energy_in", Value = 10.0 },
                new SolarAssistantMetric { Topic = "total/grid_energy_out", Value = 3.0 },
            ],
            options,
            new DateTime(2026, 5, 28, 4, 0, 0, DateTimeKind.Utc));

        var current = SolarAssistantEnergyInverterDetailMapper.MapEnergy(
            [
                new SolarAssistantMetric { Topic = "total/pv_energy", Value = 1.5 },
                new SolarAssistantMetric { Topic = "total/grid_energy_in", Value = "11.0" },
                new SolarAssistantMetric { Topic = "total/grid_energy_out", Value = 4.0 },
            ],
            options,
            new DateTime(2026, 5, 28, 4, 5, 0, DateTimeKind.Utc),
            previous);

        current.SourceId.Should().Be("configured-source");
        current.CounterResetDetected.Should().BeTrue();
        current.Counters.Single(c => c.Key == "grid_energy_in").ValueKwh.Should().Be(11.0);
        current.Counters.Single(c => c.Key == "grid_energy_out").ValueKwh.Should().Be(4.0);
    }

    [TestMethod]
    public void MapInverterDetail_CapturesPvStringsLoadBatteryAndStatusWithoutRawDump()
    {
        var payload = SolarAssistantEnergyInverterDetailMapper.MapInverterDetail(
            [
                new SolarAssistantMetric { Topic = "inverter_1/pv_power_1", Value = 600.0 },
                new SolarAssistantMetric { Topic = "inverter_1/pv_voltage_1", Value = 120.0 },
                new SolarAssistantMetric { Topic = "inverter_1/pv_current_1", Value = 5.0 },
                new SolarAssistantMetric { Topic = "inverter_1/load_power", Value = 550.0 },
                new SolarAssistantMetric { Topic = "inverter_1/load_apparent_power", Value = 700.0 },
                new SolarAssistantMetric { Topic = "inverter_1/battery_power", Value = -200.0 },
                new SolarAssistantMetric { Topic = "inverter_1/temperature", Value = 31.2 },
                new SolarAssistantMetric { Topic = "inverter_1/status_1", Value = "normal" },
                new SolarAssistantMetric { Topic = "inverter_1/serial_number", Value = "not-forwarded-here" },
            ],
            new SolarAssistantOptions { TotalSourceId = "configured-source" },
            new DateTime(2026, 5, 28, 4, 0, 0, DateTimeKind.Utc));

        payload.SourceId.Should().Be("configured-source");
        payload.DeviceId.Should().Be("inverter_1");
        payload.PvStrings.Single().PowerW.Should().Be(600.0);
        payload.Load!.LoadApparentPowerVa.Should().Be(700.0);
        payload.Battery!.PowerW.Should().Be(-200.0);
        payload.TemperatureC.Should().Be(31.2);
        payload.Statuses.Single().Key.Should().Be("inverter_1.status_1");
        payload.Statuses.Should().NotContain(s => s.Key.Contains("serial", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void MapConfiguration_CapturesReadOnlySettingsFromRestMetrics()
    {
        var payload = SolarAssistantInventoryConfigurationMapper.MapConfiguration(
            [
                new SolarAssistantMetric { Topic = "inverter_1/output_source_priority", Name = "Output source priority", Value = "Solar/Battery/Utility" },
                new SolarAssistantMetric { Topic = "inverter_1/max_charge_current", Name = "Max charge current", Value = 80, Unit = "A" },
            ],
            new HVO.Gateway.SolarAssistant.SolarAssistant.Mqtt.SolarAssistantMqttInventory(),
            new SolarAssistantOptions { TotalSourceId = "configured-source", TotalDeviceId = "configured-device" },
            new DateTime(2026, 5, 28, 4, 0, 0, DateTimeKind.Utc));

        payload.SourceId.Should().Be("configured-source");
        payload.DeviceId.Should().Be("configured-device");
        payload.Settings.Should().Contain(s => s.Key == "inverter_1.output_source_priority" && s.Value == "Solar/Battery/Utility");
        payload.Settings.Should().Contain(s => s.Key == "inverter_1.max_charge_current" && s.Unit == "A");
        payload.CommandCapabilities.Should().BeEmpty();
    }
}
