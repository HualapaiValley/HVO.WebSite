using FluentAssertions;
using HVO.Edge.Contracts;
using HVO.Edge.HomeAssistant.Mqtt;
using HVO.Edge.Hosting;
using HVO.Hardware.JkBms.Bms;
using HVO.Hardware.JkBms.Configuration;
using HVO.Hardware.JkBms.HomeAssistant;
using HVO.Hardware.JkBms.Protocol.Packets;
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
        mqtt.Definitions.Should().OnlyContain(definition => definition.Entities.Length == 25);
        mqtt.Definitions.Single(definition => definition.Key.DeviceId == "a").Name.Should().Be("JK BMS Bank 1");
        mqtt.Definitions.Single(definition => definition.Key.DeviceId == "b").Name.Should().Be("JK BMS Bank 2");
        var entities = mqtt.Definitions[0].Entities;
        entities.Should().ContainSingle(entity => entity.ComponentId == "battery_net_power")
            .Which.Should().BeEquivalentTo(new
            {
                UnitOfMeasurement = "W",
                DeviceClass = "power",
                StateClass = "measurement",
                SuggestedDisplayPrecision = 2,
            });
        entities.Should().ContainSingle(entity => entity.ComponentId == "charging")
            .Which.DeviceClass.Should().Be("battery_charging");
        entities.Should().ContainSingle(entity => entity.ComponentId == "cycle_capacity")
            .Which.As<HomeAssistantSensorDefinition>().StateClass.Should().Be("total_increasing");
        mqtt.Definitions.SelectMany(definition => definition.Entities)
            .Should().NotContain(entity => entity.ComponentId.StartsWith("cell_", StringComparison.Ordinal)
                && entity.ComponentId.Length > "cell_".Length
                && char.IsDigit(entity.ComponentId["cell_".Length]));
    }

    [TestMethod]
    public void Publish_UpdatesDeviceNameAndMetadataFromReportedModelCapacityAndFirmwareWithoutChangingIdentity()
    {
        var device = Device("a");
        var mqtt = new FakeProjection();
        var projection = new JkBmsHomeAssistantProjection(
            mqtt,
            Identity(),
            Options.Create(new JkBmsOptions { Devices = [device] }));
        var reading = new BmsDeviceReading
        {
            DeviceAddress = device.Address,
            DeviceAlias = device.Alias,
            RecordedAtUtc = DateTime.UtcNow,
            TotalVoltageMv = 52_123,
            NominalCapacityMah = 230_500,
        };
        var info = new DeviceInfoPacket
        {
            ManufacturerName = " JK_B2A24S20P ",
            HardwareName = "11.XW",
            FirmwareVersion = "11.26",
        };

        projection.Publish(device, reading, info).Should().BeTrue();

        mqtt.Definitions.Should().HaveCount(2);
        mqtt.Definitions.Select(definition => definition.Key).Should().OnlyContain(key => key.DeviceId == "a");
        var updated = mqtt.Definitions[^1];
        updated.Name.Should().Be("JK_B2A24S20P 230.5 Ah - Bank 1");
        updated.Manufacturer.Should().Be("Jikong");
        updated.Model.Should().Be("JK_B2A24S20P");
        updated.SoftwareVersion.Should().Be("11.26");
        updated.HardwareVersion.Should().Be("11.XW");
    }

    [TestMethod]
    public void Definitions_UseDecimalPrecisionOnlyForProtocolValuesThatHaveDecimalResolution()
    {
        var mqtt = new FakeProjection();
        _ = new JkBmsHomeAssistantProjection(
            mqtt,
            Identity(),
            Options.Create(new JkBmsOptions { Devices = [Device("a")] }));

        var sensors = mqtt.Definitions.Single().Entities.OfType<HomeAssistantSensorDefinition>()
            .ToDictionary(entity => entity.ComponentId, StringComparer.Ordinal);
        sensors["battery_voltage"].SuggestedDisplayPrecision.Should().Be(3);
        sensors["battery_net_current"].SuggestedDisplayPrecision.Should().Be(3);
        sensors["battery_net_power"].SuggestedDisplayPrecision.Should().Be(2);
        sensors["battery_temperature_1"].SuggestedDisplayPrecision.Should().Be(1);
        sensors["remaining_capacity"].SuggestedDisplayPrecision.Should().Be(3);
        sensors["state_of_charge"].SuggestedDisplayPrecision.Should().Be(0);
        sensors["cycle_count"].SuggestedDisplayPrecision.Should().Be(0);
        sensors["alarm_bitmask"].SuggestedDisplayPrecision.Should().Be(0);
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
        mqtt.States[0].ComponentValues["charging"].GetBoolean().Should().BeTrue();
        mqtt.States[0].ComponentValues["discharging"].GetBoolean().Should().BeFalse();
        mqtt.States[1].ComponentValues["battery_net_current"].GetDouble().Should().Be(-1.5);
        mqtt.States[1].ComponentValues["battery_net_power"].GetDouble().Should().Be(-78);
        mqtt.States[1].ComponentValues["charging"].GetBoolean().Should().BeFalse();
        mqtt.States[1].ComponentValues["discharging"].GetBoolean().Should().BeTrue();
        mqtt.States[2].Available.Should().BeFalse();
        mqtt.States[2].ComponentValues.Should().BeEmpty();
    }

    [TestMethod]
    public void Publish_ProjectsBoundedCellHealthCapacityTemperatureAndAlarmDetails()
    {
        var device = Device("a");
        var mqtt = new FakeProjection();
        var projection = new JkBmsHomeAssistantProjection(
            mqtt,
            Identity(),
            Options.Create(new JkBmsOptions { Devices = [device] }));
        var reading = new BmsDeviceReading
        {
            DeviceAddress = "AA:BB:CC:DD:EE:01",
            DeviceAlias = "Bank a",
            RecordedAtUtc = DateTime.UtcNow,
            CellCount = 4,
            CellVoltagesMv = [3300, 3320, 3290, 3310],
            AverageCellVoltageMv = 3305,
            DeltaCellVoltageMv = 30,
            MaxVoltageCellIndex = 2,
            MinVoltageCellIndex = 3,
            BalancingCurrentMa = -450,
            BalancingActive = true,
            PowerTubeTemperatureC = 42.5,
            BatteryTemperature1C = 31.2,
            BatteryTemperature2C = 30.8,
            RemainingCapacityMah = 185_500,
            NominalCapacityMah = 200_000,
            CycleCount = 42,
            CycleCapacityMah = 8_400_000,
            StateOfHealthPercent = 96,
            AlarmBitmask = 0x40,
            TotalVoltageMv = 52_000,
            StateOfChargePercent = 80,
        };

        projection.Publish(device, reading).Should().BeTrue();

        var values = mqtt.States.Single().ComponentValues;
        values["cell_count"].GetInt32().Should().Be(4);
        values["average_cell_voltage"].GetDouble().Should().Be(3.305);
        values["max_cell_voltage"].GetDouble().Should().Be(3.32);
        values["max_voltage_cell"].GetInt32().Should().Be(2);
        values["min_cell_voltage"].GetDouble().Should().Be(3.29);
        values["min_voltage_cell"].GetInt32().Should().Be(3);
        values["balancing_current"].GetDouble().Should().Be(-0.45);
        values["mosfet_temperature"].GetDouble().Should().Be(42.5);
        values["remaining_capacity"].GetDouble().Should().Be(185.5);
        values["nominal_capacity"].GetDouble().Should().Be(200);
        values["cycle_capacity"].GetDouble().Should().Be(8400);
        values["state_of_health"].GetInt32().Should().Be(96);
        values["alarm"].GetBoolean().Should().BeTrue();
        values["alarm_bitmask"].GetUInt32().Should().Be(0x40);
    }

    [TestMethod]
    public void Publish_OmitsMinAndMaxCellVoltageWhenCellArrayIsUnavailable()
    {
        var device = Device("a");
        var mqtt = new FakeProjection();
        var projection = new JkBmsHomeAssistantProjection(
            mqtt,
            Identity(),
            Options.Create(new JkBmsOptions { Devices = [device] }));

        projection.Publish(device, Reading(DateTime.UtcNow, 0)).Should().BeTrue();

        var values = mqtt.States.Single().ComponentValues;
        values.Should().NotContainKey("max_cell_voltage").And.NotContainKey("min_cell_voltage");
        values["charging"].GetBoolean().Should().BeFalse();
        values["discharging"].GetBoolean().Should().BeFalse();
    }

    [TestMethod]
    public void Constructor_RejectsMissingSiteIdentityEvenWhenMqttIsDisabled()
    {
        var act = () => new JkBmsHomeAssistantProjection(
            new FakeProjection(),
            Identity(siteId: null),
            Options.Create(new JkBmsOptions { Devices = [Device("a")] }));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Edge:Runtime:SiteId*");
    }

    [TestMethod]
    public void Constructor_RejectsMissingOrUntrimmedEnabledDeviceIdWithClearMessage()
    {
        var missing = Device("a");
        missing.DeviceId = " ";
        var untrimmed = Device("b");
        untrimmed.DeviceId = " bank-b ";

        var missingAct = () => new JkBmsHomeAssistantProjection(
            new FakeProjection(),
            Identity(),
            Options.Create(new JkBmsOptions { Devices = [missing] }));
        var untrimmedAct = () => new JkBmsHomeAssistantProjection(
            new FakeProjection(),
            Identity(),
            Options.Create(new JkBmsOptions { Devices = [untrimmed] }));

        missingAct.Should().Throw<InvalidOperationException>().WithMessage("*JkBms:Devices[].DeviceId*non-empty and trimmed*");
        untrimmedAct.Should().Throw<InvalidOperationException>().WithMessage("*JkBms:Devices[].DeviceId*non-empty and trimmed*");
    }

    [TestMethod]
    public void Constructor_RejectsCaseVariantDuplicateEnabledDeviceIdsWithClearMessage()
    {
        var first = Device("a");
        first.DeviceId = "bank-a";
        var duplicate = Device("b");
        duplicate.DeviceId = "BANK-A";

        var act = () => new JkBmsHomeAssistantProjection(
            new FakeProjection(),
            Identity(),
            Options.Create(new JkBmsOptions { Devices = [first, duplicate] }));

        act.Should().Throw<InvalidOperationException>().WithMessage("*JkBms:Devices[].DeviceId*unique ignoring case*");
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

    private static EdgeRuntimeIdentity Identity(string? siteId = "hvo") => new(
        "hvo-jkbms", "1", "test", "jkbms", "jk-bms-direct", GatewayDomain.Power,
        "jkbms-fleet", siteId, null, "Testing", "test", "JK BMS");

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
