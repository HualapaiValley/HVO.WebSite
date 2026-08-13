using FluentAssertions;
using HVO.Edge.Contracts;
using HVO.Edge.HomeAssistant.Mqtt;
using HVO.Edge.Hosting;
using HVO.Hardware.VictronSmartShunt.Configuration;
using HVO.Hardware.VictronSmartShunt.HomeAssistant;
using HVO.Hardware.VictronSmartShunt.SmartShunt;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.VictronSmartShunt.Tests.HomeAssistant;

[TestClass]
public sealed class SmartShuntHomeAssistantProjectionTests
{
    [TestMethod]
    public void Publish_ProjectsRequiredReadOnlyStateAndAvailability()
    {
        var mqtt = new CaptureProjection();
        var projection = new SmartShuntHomeAssistantProjection(mqtt,
            new EdgeRuntimeIdentity("service", "1", "instance", "smartshunt", "direct", GatewayDomain.Power, "source", "hvo", "source", "Testing", "host", "SmartShunt"),
            Options.Create(new SmartShuntOptions { DeviceId = "battery" }));
        projection.Publish(new() { RecordedAtUtc = DateTime.UtcNow, VoltageV = 52, CurrentA = -5, PowerW = -260, StateOfChargePercent = 80, ConsumedAh = -20, RemainingMinutes = 90 });
        projection.PublishUnavailable(DateTime.UtcNow);

        mqtt.Definition!.Entities.Should().HaveCount(6);
        var sensors = mqtt.Definition.Entities.OfType<HomeAssistantSensorDefinition>()
            .ToDictionary(entity => entity.ComponentId, StringComparer.Ordinal);
        sensors["battery_voltage"].SuggestedDisplayPrecision.Should().Be(2);
        sensors["battery_net_current"].SuggestedDisplayPrecision.Should().Be(3);
        sensors["battery_net_power"].SuggestedDisplayPrecision.Should().Be(0);
        sensors["state_of_charge"].SuggestedDisplayPrecision.Should().Be(2);
        sensors["consumed_ah"].SuggestedDisplayPrecision.Should().Be(1);
        sensors["remaining_time"].SuggestedDisplayPrecision.Should().Be(0);
        mqtt.States.Should().HaveCount(2);
        mqtt.States[0].Available.Should().BeTrue();
        mqtt.States[0].ComponentValues.Keys.Should().BeEquivalentTo("battery_voltage", "battery_net_current", "battery_net_power", "state_of_charge", "consumed_ah", "remaining_time");
        mqtt.States[1].Available.Should().BeFalse();
    }

    private sealed class CaptureProjection : IHomeAssistantMqttProjection
    {
        public HomeAssistantDeviceDefinition? Definition { get; private set; }
        public List<HomeAssistantCurrentState> States { get; } = [];
        public void UpsertDevice(HomeAssistantDeviceDefinition definition) => Definition = definition;
        public bool PublishCurrentState(HomeAssistantCurrentState state) { States.Add(state); return true; }
        public bool RemoveDevice(HomeAssistantDeviceKey key) => false;
        public HomeAssistantMqttStatus GetStatus() => new(false, false, 0, null, null, null);
    }
}
