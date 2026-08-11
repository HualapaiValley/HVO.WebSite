using FluentAssertions;
using HVO.Edge.Contracts;
using HVO.Edge.Contracts.PowerSystem;
using HVO.Edge.HomeAssistant.Mqtt;
using HVO.Edge.Hosting;
using HVO.Hardware.Eg4.Configuration;
using HVO.Hardware.Eg4.HomeAssistant;
using HVO.Hardware.Eg4.Telemetry;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.Eg4.Tests.HomeAssistant;

[TestClass]
public sealed class Eg4HomeAssistantProjectionTests
{
    [TestMethod]
    public void Definitions_UseStableDeviceIdentityIndependentOfConfigurationOrder()
    {
        var first = Device("a", Eg4DeviceType.Inverter6500Ex);
        var second = Device("controller", Eg4DeviceType.ChargeControllerMppt10048Hv);
        var mqtt = new FakeProjection();

        _ = new Eg4HomeAssistantProjection(
            mqtt,
            Identity(),
            Options.Create(new Eg4Options { Devices = [second, first] }));

        mqtt.Definitions.Select(static definition => definition.Key.DeviceId)
            .Should().BeEquivalentTo("a", "controller");
        mqtt.Definitions.Single(definition => definition.Key.DeviceId == "a").Entities
            .Should().Contain(entity => entity.ComponentId == "load_power");
        mqtt.Definitions.Single(definition => definition.Key.DeviceId == "controller").Entities
            .Should().NotContain(entity => entity.ComponentId == "load_power");
    }

    [TestMethod]
    public void Publish_PreservesCanonicalZeroAndOmitsUnknownMeasurements()
    {
        var device = Device("a", Eg4DeviceType.Inverter6500Ex);
        var mqtt = new FakeProjection();
        var projector = new Eg4HomeAssistantProjection(
            mqtt,
            Identity(),
            Options.Create(new Eg4Options { Devices = [device] }));
        var observation = new PowerBatteryObservation(
            device.SourceId,
            device.DeviceId,
            PowerMetricSource.Eg46500Ex,
            PowerMeasurementRole.InverterBranch,
            "inverter-battery-branch",
            DateTime.UtcNow,
            VoltageV: 54,
            CurrentA: 0,
            PowerW: 0);

        projector.Publish(device, Eg4TelemetrySample.Available(observation)).Should().BeTrue();

        var state = mqtt.States.Single();
        state.Available.Should().BeTrue();
        state.ComponentValues["battery_net_current"].GetDouble().Should().Be(0);
        state.ComponentValues["battery_net_power"].GetDouble().Should().Be(0);
        state.ComponentValues.Should().NotContainKey("pv_power");
        state.ComponentValues.Should().NotContainKey("load_power");
        state.ComponentValues.Should().NotContainKey("temperature");
    }

    private static Eg4DeviceOptions Device(string id, Eg4DeviceType type) => new()
    {
        Type = type,
        SourceId = $"eg4-{id}",
        DeviceId = id,
        Alias = $"EG4 {id}",
        Port = type == Eg4DeviceType.Inverter6500Ex ? $"/dev/hvo/eg4-{id}" : $"/dev/serial/by-id/eg4-{id}",
        UnitId = type == Eg4DeviceType.Inverter6500Ex ? (byte)0 : (byte)1
    };

    private static EdgeRuntimeIdentity Identity() => new(
        "hvo-eg4", "1", "test", "eg4", "eg4-direct", GatewayDomain.Power,
        "eg4-fleet", "hvo", null, "Testing", "test", "EG4");

    private sealed class FakeProjection : IHomeAssistantMqttProjection
    {
        public List<HomeAssistantDeviceDefinition> Definitions { get; } = [];
        public List<HomeAssistantCurrentState> States { get; } = [];
        public void UpsertDevice(HomeAssistantDeviceDefinition definition) => Definitions.Add(definition);
        public bool PublishCurrentState(HomeAssistantCurrentState state) { States.Add(state); return true; }
        public bool RemoveDevice(HomeAssistantDeviceKey key) => false;
        public HomeAssistantMqttStatus GetStatus() => new(false, false, Definitions.Count, null, null, null);
    }
}
