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
        inventory.ClassificationCounts[SolarAssistantMetricClassification.DbCandidate].Should().Be(1);
        inventory.ClassificationCounts[SolarAssistantMetricClassification.Review].Should().Be(1);
        inventory.ClassificationCounts[SolarAssistantMetricClassification.LocalOnly].Should().Be(1);
        inventory.Topics.Single(t => t.Topic == "total/pv_power").Classification.Should().Be(SolarAssistantMetricClassification.DbCandidate);
        inventory.Topics.Single(t => t.Topic == "inverter_1/pv_power_1").Classification.Should().Be(SolarAssistantMetricClassification.Review);
        inventory.Topics.Single(t => t.Topic == "inverter_1/serial_number").Classification.Should().Be(SolarAssistantMetricClassification.LocalOnly);
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
