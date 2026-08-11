using FluentAssertions;
using HVO.Edge.Contracts;
using HVO.Edge.HomeAssistant.Mqtt;
using HVO.Edge.Hosting;
using HVO.Hardware.JkBms.Bms;
using HVO.Hardware.JkBms.Configuration;
using HVO.Hardware.JkBms.HomeAssistant;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.JkBms.Tests.HomeAssistant;

[TestClass]
public sealed class JkBmsHomeAssistantProjectionTests
{
    [TestMethod]
    public void Definitions_AreBoundedAndUseStableDeviceIdentityIndependentOfOrder()
    {
        var mqtt = new FakeProjection();
        _ = new JkBmsHomeAssistantProjection(
            mqtt,
            Identity(),
            Options.Create(new JkBmsOptions { Devices = [Device("b"), Device("a")] }));

        mqtt.Definitions.Select(definition => definition.Key.DeviceId).Should().BeEquivalentTo("a", "b");
        mqtt.Definitions.Should().OnlyContain(definition => definition.Entities.Length == 10);
        mqtt.Definitions.SelectMany(definition => definition.Entities)
            .Should().NotContain(entity => entity.ComponentId.Contains("cell_", StringComparison.Ordinal) && entity.ComponentId != "cell_delta");
    }

    [TestMethod]
    public void Publish_UsesPositiveChargeNegativeDischargeAndExplicitAvailability()
    {
        var device = Device("a");
        var mqtt = new FakeProjection();
        var projection = new JkBmsHomeAssistantProjection(
            mqtt,
            Identity(),
            Options.Create(new JkBmsOptions { Devices = [device] }));
        var observedAt = new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);

        projection.Publish(device, Reading(observedAt, 2_500)).Should().BeTrue();
        projection.Publish(device, Reading(observedAt.AddSeconds(1), -1_500)).Should().BeTrue();
        projection.PublishUnavailable(device, observedAt.AddSeconds(2)).Should().BeTrue();

        mqtt.States[0].ComponentValues["battery_net_current"].GetDouble().Should().Be(2.5);
        mqtt.States[0].ComponentValues["battery_net_power"].GetDouble().Should().Be(130);
        mqtt.States[1].ComponentValues["battery_net_current"].GetDouble().Should().Be(-1.5);
        mqtt.States[1].ComponentValues["battery_net_power"].GetDouble().Should().Be(-78);
        mqtt.States[2].Available.Should().BeFalse();
        mqtt.States[2].ComponentValues.Should().BeEmpty();
    }

    private static BmsDeviceConfig Device(string id) => new()
    {
        Address = $"AA:BB:CC:DD:EE:{(id == "a" ? "01" : "02")}",
        DeviceId = id,
        Alias = $"Bank {id}",
    };

    private static BmsDeviceReading Reading(DateTime recordedAt, int currentMa) => new()
    {
        DeviceAddress = "AA:BB:CC:DD:EE:01",
        DeviceAlias = "Bank a",
        RecordedAtUtc = recordedAt,
        TotalVoltageMv = 52_000,
        CurrentMa = currentMa,
        StateOfChargePercent = 80,
    };

    private static EdgeRuntimeIdentity Identity() => new(
        "hvo-jkbms", "1", "test", "jkbms", "jk-bms-direct", GatewayDomain.Power,
        "jkbms-fleet", "hvo", null, "Testing", "test", "JK BMS");

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
