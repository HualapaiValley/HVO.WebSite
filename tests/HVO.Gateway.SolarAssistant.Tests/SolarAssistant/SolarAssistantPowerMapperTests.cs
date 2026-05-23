using System.Text.Json;
using FluentAssertions;
using HVO.Gateway.SolarAssistant.Configuration;
using HVO.Gateway.SolarAssistant.SolarAssistant;

namespace HVO.Gateway.SolarAssistant.Tests.SolarAssistant;

[TestClass]
public sealed class SolarAssistantPowerMapperTests
{
    [TestMethod]
    public void MapTotalSnapshot_MapsKnownAggregateAndDeviceTopics()
    {
        var recordedAt = DateTime.Parse("2026-05-23T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);
        var options = new SolarAssistantOptions
        {
            TotalSourceId = "sa-total",
            TotalDeviceId = "total",
        };

        var payload = SolarAssistantPowerMapper.MapTotalSnapshot(
            [
                Metric("total/pv_power", 1200),
                Metric("total/load_power", "900.5"),
                Metric("total/grid_power", -50),
                Metric("total/battery_power", -250),
                Metric("total/battery_state_of_charge", 82),
                Metric("battery_1/voltage", 53.2),
                Metric("battery_1/current", -4.7),
                Metric("inverter_1/grid_voltage", 240),
                Metric("inverter_1/grid_frequency", 60),
                Metric("inverter_1/output_voltage", 120),
                Metric("inverter_1/output_frequency", 60),
                Metric("inverter_1/load_percentage", 23),
                Metric("inverter_1/device_mode", "Online"),
                Metric("inverter_1/output_source_priority", "Solar first"),
                Metric("inverter_1/charger_source_priority", "Solar and utility"),
            ],
            options,
            recordedAt);

        payload.SourceId.Should().Be("sa-total");
        payload.SourceSystem.Should().Be("solarassistant");
        payload.DeviceId.Should().Be("total");
        payload.RecordedAtUtc.Should().Be(recordedAt);
        payload.PvPowerW.Should().Be(1200);
        payload.LoadPowerW.Should().Be(900.5);
        payload.GridPowerW.Should().Be(-50);
        payload.BatteryPowerW.Should().Be(-250);
        payload.BatteryStateOfChargePercent.Should().Be(82);
        payload.BatteryVoltageV.Should().Be(53.2);
        payload.BatteryCurrentA.Should().Be(-4.7);
        payload.GridVoltageV.Should().Be(240);
        payload.GridFrequencyHz.Should().Be(60);
        payload.OutputVoltageV.Should().Be(120);
        payload.OutputFrequencyHz.Should().Be(60);
        payload.LoadPercentage.Should().Be(23);
        payload.InverterMode.Should().Be("Online");
        payload.OutputSourcePriority.Should().Be("Solar first");
        payload.ChargerSourcePriority.Should().Be("Solar and utility");
    }

    [TestMethod]
    public void MapTotalSnapshot_MapsSolarAssistantRestAliases()
    {
        var payload = SolarAssistantPowerMapper.MapTotalSnapshot(
            [
                Metric("total/system_power", 120),
                Metric("total/battery_voltage", 53.1),
                Metric("total/battery_current", -6.2),
                Metric("total/battery_capacity", 95.2),
                Metric("total/grid_voltage", 0),
                Metric("total/grid_frequency", 0),
                Metric("total/ac_output_voltage", 120.4),
                Metric("total/ac_output_frequency", 60.1),
                Metric("total/load_percentage", 8),
                Metric("total/inverter_mode", "Solar/Battery"),
                Metric("total/output_source_priority", "Solar/Battery/Utility"),
            ],
            new SolarAssistantOptions(),
            DateTime.UtcNow);

        payload.SystemPowerW.Should().Be(120);
        payload.BatteryVoltageV.Should().Be(53.1);
        payload.BatteryCurrentA.Should().Be(-6.2);
        payload.BatteryCapacityKwh.Should().Be(95.2);
        payload.GridVoltageV.Should().Be(0);
        payload.GridFrequencyHz.Should().Be(0);
        payload.OutputVoltageV.Should().Be(120.4);
        payload.OutputFrequencyHz.Should().Be(60.1);
        payload.LoadPercentage.Should().Be(8);
        payload.InverterMode.Should().Be("Solar/Battery");
        payload.OutputSourcePriority.Should().Be("Solar/Battery/Utility");
    }

    [TestMethod]
    public void MapTotalSnapshot_ParsesJsonElementValues()
    {
        using var doc = JsonDocument.Parse("{\"value\":123.45}");
        var metric = new SolarAssistantMetric
        {
            Topic = "total/pv_power",
            Value = doc.RootElement.GetProperty("value").Clone(),
        };

        var payload = SolarAssistantPowerMapper.MapTotalSnapshot(
            [metric],
            new SolarAssistantOptions(),
            DateTime.UtcNow);

        payload.PvPowerW.Should().Be(123.45);
    }

    private static SolarAssistantMetric Metric(string topic, object value) => new()
    {
        Topic = topic,
        Value = value,
    };
}
