using System.Text.Json;
using FluentAssertions;

namespace HVO.Edge.HomeAssistant.Mqtt.Tests;

[TestClass]
public sealed class HomeAssistantIdentityAndDiscoveryTests
{
    [TestMethod]
    public void Identity_IsStableAndNormalizesConfiguredSegments()
    {
        var key = new HomeAssistantDeviceKey(" Hualapai Valley ", "Gateway/One", "Battery #1");

        HomeAssistantMqttIdentity.DeviceId(key).Should().Be(
            "hvo_32x_x20_x48ualapai_x20_x56alley_x20_20x_x47ateway_x2f_x4fne_19x_x42attery_x20_x231");
        HomeAssistantMqttIdentity.EntityUniqueId(key, "DC Voltage").Should().EndWith("22x_x44_x43_x20_x56oltage");
        HomeAssistantMqttIdentity.DeviceId(key).Should().Be(HomeAssistantMqttIdentity.DeviceId(key));
    }

    [TestMethod]
    public void IdentityEncoding_PreservesPreviouslyCollidingComponents()
    {
        var definition = TestSupport.Device(
            TestSupport.Key,
            new HomeAssistantSensorDefinition("dc-voltage", "Voltage A"),
            new HomeAssistantSensorDefinition("DC Voltage", "Voltage B"));

        var payload = HomeAssistantDiscoverySerializer.Serialize(definition, TestSupport.Topics);
        using var document = JsonDocument.Parse(payload);
        document.RootElement.GetProperty("components").EnumerateObject().Select(property => property.Name)
            .Should().OnlyHaveUniqueItems().And.HaveCount(2);
    }

    [TestMethod]
    public void Discovery_ContainsStableDeviceOriginComponentsAndAvailabilityContract()
    {
        var topics = new HomeAssistantMqttTopics("homeassistant", "hvo");
        var payload = HomeAssistantDiscoverySerializer.Serialize(TestSupport.Device(), topics);
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        root.GetProperty("device").GetProperty("identifiers")[0].GetString().Should()
            .Be(HomeAssistantMqttIdentity.DeviceId(TestSupport.Key));
        root.GetProperty("origin").GetProperty("name").GetString().Should().Be("HVO Edge");
        var voltage = root.GetProperty("components").GetProperty("voltage");
        voltage.GetProperty("platform").GetString().Should().Be("sensor");
        voltage.GetProperty("unique_id").GetString().Should().Be(HomeAssistantMqttIdentity.EntityUniqueId(TestSupport.Key, "voltage"));
        voltage.GetProperty("default_entity_id").GetString().Should().Be($"sensor.{HomeAssistantMqttIdentity.EntityUniqueId(TestSupport.Key, "voltage")}");
        voltage.GetProperty("unit_of_measurement").GetString().Should().Be("V");
        voltage.GetProperty("device_class").GetString().Should().Be("voltage");
        voltage.GetProperty("state_class").GetString().Should().Be("measurement");
        voltage.GetProperty("state_topic").GetString().Should().Be(TestSupport.Topics.State(TestSupport.Key));
        voltage.GetProperty("value_template").GetString().Should().Be("{{ value_json.components.voltage }}");
        voltage.GetProperty("availability_mode").GetString().Should().Be("all");
        voltage.GetProperty("availability").EnumerateArray().Select(item => item.GetProperty("topic").GetString()).Should()
            .Equal(TestSupport.Topics.GatewayAvailability(TestSupport.Key), TestSupport.Topics.DeviceAvailability(TestSupport.Key));
        var charging = root.GetProperty("components").GetProperty("charging");
        charging.GetProperty("payload_on").GetString().Should().Be("ON");
        charging.GetProperty("payload_off").GetString().Should().Be("OFF");
        charging.GetProperty("value_template").GetString().Should().Contain("ON").And.Contain("OFF");
    }
}
