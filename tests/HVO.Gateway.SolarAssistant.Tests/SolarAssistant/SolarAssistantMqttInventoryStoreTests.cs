using FluentAssertions;
using HVO.Gateway.SolarAssistant.Configuration;
using HVO.Gateway.SolarAssistant.SolarAssistant;
using HVO.Gateway.SolarAssistant.SolarAssistant.Mqtt;

namespace HVO.Gateway.SolarAssistant.Tests.SolarAssistant;

[TestClass]
public sealed class SolarAssistantMqttInventoryStoreTests
{
    [TestMethod]
    public void IsMatch_MatchesMqttWildcards()
    {
        MqttTopicMatcher.IsMatch("homeassistant/#", "homeassistant/sensor/pv/config").Should().BeTrue();
        MqttTopicMatcher.IsMatch("solar_assistant/+/pv_power/state", "solar_assistant/inverter_1/pv_power/state").Should().BeTrue();
        MqttTopicMatcher.IsMatch("solar_assistant/+/pv_power/state", "solar_assistant/inverter_1/pv_power_1/state").Should().BeFalse();
        MqttTopicMatcher.IsMatch("solar_assistant/#/state", "solar_assistant/total/pv_power/state").Should().BeFalse();
    }

    [TestMethod]
    public void Apply_ParsesHomeAssistantDiscoveryWithoutStateValues()
    {
        var store = new SolarAssistantMqttInventoryStore();
        store.MarkConnecting(["homeassistant/#", "solar_assistant/#"]);
        store.MarkConnected();

        store.Apply(new SolarAssistantMqttMessage
        {
            Topic = "homeassistant/sensor/total_pv_power/config",
            Payload = """
            {
              "name":"PV power",
              "stat_t":"solar_assistant/total/pv_power/state",
              "unit_of_meas":"W",
              "dev_cla":"power",
              "stat_cla":"measurement",
              "dev":{"name":"Axpert Max","mf":"Voltronic","mdl":"Axpert Max","sw":"2026.1"}
            }
            """,
            Retain = true,
            ReceivedAtUtc = DateTime.UtcNow,
        });
        store.Apply(new SolarAssistantMqttMessage
        {
            Topic = "solar_assistant/total/pv_power/state",
            Payload = "1234",
            Retain = true,
            ReceivedAtUtc = DateTime.UtcNow,
        });

        var snapshot = store.Snapshot;

        snapshot.ConnectionState.Should().Be("connected");
        snapshot.EntityCount.Should().Be(1);
        snapshot.StateTopicCount.Should().Be(1);
        snapshot.Devices.Single().Name.Should().Be("Axpert Max");
        snapshot.Entities.Single().Classification.Should().Be(SolarAssistantMetricClassification.DbCandidate);
        snapshot.States.Single().PayloadKind.Should().Be("number");
        snapshot.States.Single().PayloadLength.Should().Be(4);
    }

    [TestMethod]
    public void ParseDiscovery_ClassifiesCommandTopicsAsReadOnlyInventory()
    {
        var entity = SolarAssistantMqttInventoryStore.ParseDiscovery(new SolarAssistantMqttMessage
        {
            Topic = "homeassistant/select/inverter_1_output_source_priority/config",
            Payload = """
            {
              "name":"Output source priority",
              "stat_t":"solar_assistant/inverter_1/output_source_priority/state",
              "cmd_t":"solar_assistant/inverter_1/output_source_priority/set",
              "dev":{"name":"Axpert Max","mf":"Voltronic","mdl":"Axpert Max"}
            }
            """,
        });

        entity.Should().NotBeNull();
        entity!.CommandTopic.Should().Be("solar_assistant/inverter_1/output_source_priority/set");
        entity.Classification.Should().Be(SolarAssistantMetricClassification.DbCandidate);
    }

    [TestMethod]
    public void MapInventoryAndConfiguration_CapturesMqttDeviceAndCommandCapabilities()
    {
        var store = new SolarAssistantMqttInventoryStore();
        store.Apply(new SolarAssistantMqttMessage
        {
            Topic = "homeassistant/select/inverter_1_output_source_priority/config",
            Payload = """
            {
              "name":"Output source priority",
              "stat_t":"solar_assistant/inverter_1/output_source_priority/state",
              "cmd_t":"solar_assistant/inverter_1/output_source_priority/set",
              "dev":{"name":"EG4 6500EX","mf":"EG4","mdl":"6500EX","sw":"2026.1"}
            }
            """,
            ReceivedAtUtc = DateTime.UtcNow,
        });

        var options = new SolarAssistantOptions { TotalSourceId = "configured-source", TotalDeviceId = "configured-device" };
        var inventory = SolarAssistantInventoryConfigurationMapper.MapDeviceInventory([], store.Snapshot, options, DateTime.UtcNow);
        var configuration = SolarAssistantInventoryConfigurationMapper.MapConfiguration([], store.Snapshot, options, DateTime.UtcNow);

        inventory.SourceId.Should().Be("configured-source");
        configuration.SourceId.Should().Be("configured-source");
        inventory.Devices.Single().Name.Should().Be("EG4 6500EX");
        inventory.Devices.Single().Manufacturer.Should().Be("EG4");
        configuration.CommandCapabilities.Single().CommandTopic.Should().Be("solar_assistant/inverter_1/output_source_priority/set");
        configuration.Settings.Should().Contain(s => s.Key == "inverter_1.output_source_priority");
    }

    [TestMethod]
    public void Apply_RemovesHomeAssistantDiscoveryEntity_WhenEmptyRetainedConfigArrives()
    {
        var store = new SolarAssistantMqttInventoryStore();
        var topic = "homeassistant/sensor/total_pv_power/config";

        store.Apply(new SolarAssistantMqttMessage
        {
            Topic = topic,
            Payload = """
            {
              "name":"PV power",
              "stat_t":"solar_assistant/total/pv_power/state",
              "unit_of_meas":"W"
            }
            """,
            Retain = true,
            ReceivedAtUtc = DateTime.UtcNow,
        });

        store.Apply(new SolarAssistantMqttMessage
        {
            Topic = topic,
            Payload = string.Empty,
            Retain = true,
            ReceivedAtUtc = DateTime.UtcNow,
        });

        store.Snapshot.EntityCount.Should().Be(0);
    }
}
