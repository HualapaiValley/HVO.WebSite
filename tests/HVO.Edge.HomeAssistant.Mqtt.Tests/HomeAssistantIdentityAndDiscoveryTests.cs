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
        voltage.GetProperty("value_template").GetString().Should().Be("{{ value_json.components.get(\"voltage\") }}");
        voltage.GetProperty("availability_mode").GetString().Should().Be("all");
        voltage.GetProperty("availability").EnumerateArray().Select(item => item.GetProperty("topic").GetString()).Should()
            .Equal(TestSupport.Topics.GatewayAvailability(TestSupport.Key), TestSupport.Topics.DeviceAvailability(TestSupport.Key));
        var charging = root.GetProperty("components").GetProperty("charging");
        charging.GetProperty("payload_on").GetString().Should().Be("ON");
        charging.GetProperty("payload_off").GetString().Should().Be("OFF");
        charging.GetProperty("value_template").GetString().Should().Contain("ON").And.Contain("OFF");
    }

    [TestMethod]
    public void Discovery_ReadableDefaultEntityId_DoesNotChangeUniqueId()
    {
        var entity = new HomeAssistantSensorDefinition(
            "outside_temperature",
            "Outside temperature",
            "°F",
            "temperature",
            "measurement",
            defaultEntityId: "sensor.davis_outside_temperature");
        var payload = HomeAssistantDiscoverySerializer.Serialize(
            TestSupport.Device(TestSupport.Key, entity),
            TestSupport.Topics);
        using var document = JsonDocument.Parse(payload);
        var component = document.RootElement.GetProperty("components").GetProperty("outside_x5ftemperature");

        component.GetProperty("unique_id").GetString().Should()
            .Be(HomeAssistantMqttIdentity.EntityUniqueId(TestSupport.Key, "outside_temperature"));
        component.GetProperty("default_entity_id").GetString().Should().Be("sensor.davis_outside_temperature");
        component.GetProperty("value_template").GetString().Should()
            .Be("{{ value_json.components.get(\"outside_x5ftemperature\") }}");
    }

    [TestMethod]
    public void Discovery_ValueTemplate_JsonEncodesComponentId()
    {
        var entity = new HomeAssistantSensorDefinition("sensor'quote", "Quoted sensor");
        var payload = HomeAssistantDiscoverySerializer.Serialize(
            TestSupport.Device(TestSupport.Key, entity),
            TestSupport.Topics);
        using var document = JsonDocument.Parse(payload);

        document.RootElement.GetProperty("components").EnumerateObject().Single().Value
            .GetProperty("value_template").GetString().Should()
            .Be("{{ value_json.components.get(\"sensor_x27quote\") }}");
    }

    [TestMethod]
    public void Discovery_DuplicateReadableDefaultEntityIds_AreRejected()
    {
        var definition = TestSupport.Device(
            TestSupport.Key,
            new HomeAssistantSensorDefinition("temperature_a", "Temperature A", defaultEntityId: "sensor.davis_temperature"),
            new HomeAssistantSensorDefinition("temperature_b", "Temperature B", defaultEntityId: "sensor.davis_temperature"));

        var act = () => HomeAssistantDiscoverySerializer.Serialize(definition, TestSupport.Topics);

        act.Should().Throw<ArgumentException>().WithMessage("*Default entity ID collision*");
    }

    [TestMethod]
    public void Discovery_CustomAndGeneratedDefaultEntityIdCollision_IsRejected()
    {
        var generatedEntity = new HomeAssistantSensorDefinition("voltage", "Voltage");
        var generatedDefault = $"sensor.{HomeAssistantMqttIdentity.EntityUniqueId(TestSupport.Key, "voltage")}";
        var definition = TestSupport.Device(
            TestSupport.Key,
            generatedEntity,
            new HomeAssistantSensorDefinition("current", "Current", defaultEntityId: generatedDefault));

        var act = () => HomeAssistantDiscoverySerializer.Serialize(definition, TestSupport.Topics);

        act.Should().Throw<ArgumentException>().WithMessage("*Default entity ID collision*");
    }
}
