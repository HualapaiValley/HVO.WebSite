using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace HVO.Edge.HomeAssistant.Mqtt.Tests;

[TestClass]
public sealed class HomeAssistantProjectionTests
{
    [TestMethod]
    public void UpsertDevice_PreservesPreviouslyCollidingDeviceIdentifiers()
    {
        var projection = new HomeAssistantMqttProjection(
            TestSupport.Identity(),
            Microsoft.Extensions.Options.Options.Create(TestSupport.Options()));
        projection.UpsertDevice(TestSupport.Device(TestSupport.Key with { DeviceId = "battery-one" }));

        projection.UpsertDevice(TestSupport.Device(TestSupport.Key with { DeviceId = "battery one" }));

        projection.GetStatus().DeviceCount.Should().Be(2);
    }

    [TestMethod]
    public void CurrentState_LatestWinsAndOlderObservationIsRejected()
    {
        var projection = new HomeAssistantMqttProjection(TestSupport.Identity(), Options.Create(TestSupport.Options()));
        projection.UpsertDevice(TestSupport.Device());
        var newest = DateTimeOffset.Parse("2026-08-11T12:00:00Z");

        projection.PublishCurrentState(TestSupport.State(newest, 52.4)).Should().BeTrue();
        projection.PublishCurrentState(TestSupport.State(newest.AddSeconds(-1), 10)).Should().BeFalse();

        var state = projection.Snapshot().Devices.Single().State!;
        state.ObservedAtUtc.Should().Be(newest);
        state.ComponentValues["voltage"].GetDouble().Should().Be(52.4);
    }

    [TestMethod]
    public void CurrentState_RejectsComponentsNotInDefinition()
    {
        var projection = new HomeAssistantMqttProjection(TestSupport.Identity(), Options.Create(TestSupport.Options()));
        projection.UpsertDevice(TestSupport.Device());
        var state = new HomeAssistantCurrentState(
            TestSupport.Key,
            DateTimeOffset.UtcNow,
            new Dictionary<string, JsonElement> { ["secret"] = JsonSerializer.SerializeToElement(1) });

        var act = () => projection.PublishCurrentState(state);

        act.Should().Throw<ArgumentException>().WithMessage("*unknown component*");
    }

    [TestMethod]
    public void DiscoveryTemplate_ReferencesSerializedStateComponentKey()
    {
        var definition = TestSupport.Device(
            TestSupport.Key,
            new HomeAssistantSensorDefinition("outside_temperature", "Outside temperature"));
        var projection = new HomeAssistantMqttProjection(TestSupport.Identity(), Options.Create(TestSupport.Options()));
        projection.UpsertDevice(definition);
        projection.PublishCurrentState(new(
            TestSupport.Key,
            DateTimeOffset.UtcNow,
            new Dictionary<string, JsonElement>
            {
                ["outside_temperature"] = JsonSerializer.SerializeToElement(72.5)
            }));
        using var discovery = JsonDocument.Parse(HomeAssistantDiscoverySerializer.Serialize(definition, TestSupport.Topics));

        var stateKey = projection.Snapshot().Devices.Single().State!.ComponentValues.Keys.Single();
        discovery.RootElement.GetProperty("components").GetProperty(stateKey)
            .GetProperty("value_template").GetString().Should()
            .Be($"{{{{ value_json.components.get(\"{stateKey}\") }}}}");
    }

    [TestMethod]
    public void DeviceIdentity_MustMatchConfiguredSiteAndGateway()
    {
        var projection = new HomeAssistantMqttProjection(TestSupport.Identity(), Options.Create(TestSupport.Options()));
        var definition = TestSupport.Device(new("other-site", "gateway-1", "device-1"));

        var act = () => projection.UpsertDevice(definition);

        act.Should().Throw<ArgumentException>().WithMessage("*configured edge runtime identity*");
    }
}
