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
            .Should().Contain(entity => entity.ComponentId == "load_power")
            .And.Contain(entity => entity.ComponentId == "pv_mppt_2_power")
            .And.Contain(entity => entity.ComponentId == "ac_output_voltage")
            .And.Contain(entity => entity.ComponentId == "fault_code")
            .And.Contain(entity => entity.ComponentId == "fan_locked");
        mqtt.Definitions.Single(definition => definition.Key.DeviceId == "controller").Entities
            .Should().Contain(entity => entity.ComponentId == "pv_mppt_1_power")
            .And.Contain(entity => entity.ComponentId == "controller_secondary_temperature")
            .And.Contain(entity => entity.ComponentId == "controller_fault" && entity.Name == "Controller diagnostic 201")
            .And.NotContain(entity => entity.ComponentId == "load_power")
            .And.NotContain(entity => entity.ComponentId == "pv_mppt_2_power");
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

    [TestMethod]
    public void Publish_ProjectsIndividualMpptAcLoadAndOperatingStatus()
    {
        var device = Device("a", Eg4DeviceType.Inverter6500Ex);
        var mqtt = new FakeProjection();
        var projector = new Eg4HomeAssistantProjection(
            mqtt,
            Identity(),
            Options.Create(new Eg4Options { Devices = [device] }));
        var observedAt = DateTime.UtcNow;
        var observation = new PowerBatteryObservation(
            device.SourceId,
            device.DeviceId,
            PowerMetricSource.Eg46500Ex,
            PowerMeasurementRole.InverterBranch,
            "inverter-battery-branch",
            observedAt,
            VoltageV: 54,
            CurrentA: -40,
            PowerW: -2160);
        var mppt = new PowerMpptDetailPayload
        {
            Trackers =
            [
                new() { TrackerId = "mppt-1", VoltageV = 310, CurrentA = 4.1, PowerW = 1271 },
                new() { TrackerId = "mppt-2", VoltageV = 320, CurrentA = 3.2, PowerW = 1024 },
            ],
        };
        var inverter = new PowerInverterDetailPayload
        {
            Ac = new() { InputVoltageV = 121, InputFrequencyHz = 60, OutputVoltageV = 120, OutputFrequencyHz = 59.9 },
            Load = new() { LoadPowerW = 850, LoadApparentPowerVa = 920 },
            Operating = new() { Mode = "B", FaultCode = "00", LoadPercentage = 14, StatusFlags = "a/b/c" },
            Temperatures =
            [
                new() { TemperatureId = "scc-pwm", TemperatureC = 40 },
                new() { TemperatureId = "inverter", TemperatureC = 42 },
                new() { TemperatureId = "battery-channel", TemperatureC = 35 },
                new() { TemperatureId = "transformer", TemperatureC = 44 },
            ],
            Statuses =
            [
                new() { Key = "main-firmware", Value = "79.71" },
                new() { Key = "secondary-firmware", Value = "61.13" },
                new() { Key = "charge-stage", Value = "010" },
                new() { Key = "fan-locked", Value = "true" },
                new() { Key = "fan-pwm-percent", Value = "42" },
                new() { Key = "parallel-role", Value = "1" },
                new() { Key = "parallel-warning-flags", Value = "0000" },
                new() { Key = "output-mode", Value = "S" },
                new() { Key = "charger-source-priority", Value = "P" },
                new() { Key = "parallel-total-load-active-w", Value = "850" },
                new() { Key = "parallel-total-load-apparent-va", Value = "920" },
                new() { Key = "parallel-total-load-percent", Value = "14" },
            ],
        };

        projector.Publish(device, Eg4TelemetrySample.Available(observation, mppt, inverter)).Should().BeTrue();

        var values = mqtt.States.Single().ComponentValues;
        values["pv_power"].GetDouble().Should().Be(2295);
        values["pv_mppt_1_voltage"].GetDouble().Should().Be(310);
        values["pv_mppt_2_power"].GetDouble().Should().Be(1024);
        values["ac_input_voltage"].GetDouble().Should().Be(121);
        values["ac_output_frequency"].GetDouble().Should().Be(59.9);
        values["load_apparent_power"].GetDouble().Should().Be(920);
        values["load_percentage"].GetDouble().Should().Be(14);
        values["fault_code"].GetString().Should().Be("00");
        values["fan_locked"].GetBoolean().Should().BeTrue();
        values["fan_pwm_percentage"].GetDouble().Should().Be(42);
        values["transformer_temperature"].GetDouble().Should().Be(44);
        values["charger_source_priority"].GetString().Should().Be("P");
        values["parallel_total_load_power"].GetDouble().Should().Be(850);
    }

    [TestMethod]
    public void Constructor_RejectsMissingSiteIdentityEvenWhenMqttIsDisabled()
    {
        var act = () => new Eg4HomeAssistantProjection(
            new FakeProjection(),
            Identity(siteId: null),
            Options.Create(new Eg4Options { Devices = [Device("a", Eg4DeviceType.Inverter6500Ex)] }));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Edge:Runtime:SiteId*");
    }

    [TestMethod]
    public void Publish_ProjectsControllerTemperaturesAndDiagnostics()
    {
        var device = Device("controller", Eg4DeviceType.ChargeControllerMppt10048Hv);
        var mqtt = new FakeProjection();
        var projector = new Eg4HomeAssistantProjection(
            mqtt,
            Identity(),
            Options.Create(new Eg4Options { Devices = [device] }));
        var observation = new PowerBatteryObservation(
            device.SourceId,
            device.DeviceId,
            PowerMetricSource.Eg4Mppt10048Hv,
            PowerMeasurementRole.ChargeControllerBranch,
            "controller-battery-output",
            DateTime.UtcNow,
            VoltageV: 55,
            CurrentA: -10,
            PowerW: -550);
        var detail = new PowerMpptDetailPayload
        {
            Trackers = [new() { TrackerId = "mppt-1", VoltageV = 390, CurrentA = 4, PowerW = 1560 }],
            Temperatures =
            [
                new() { TemperatureId = "controller", TemperatureC = 40 },
                new() { TemperatureId = "controller-secondary", TemperatureC = 37 },
            ],
            Diagnostics =
            [
                new() { Key = "r200", Value = "2" },
                new() { Key = "r201", Value = "0" },
                new() { Key = "r204", Value = "3" },
                new() { Key = "controllerEstimatedSocPercent", Value = "90" },
                new() { Key = "r206", Value = "4038" },
                new() { Key = "r212", Value = "40" },
                new() { Key = "r215", Value = "13" },
                new() { Key = "r216", Value = "91" },
                new() { Key = "r217", Value = "2" },
            ],
        };

        projector.Publish(device, Eg4TelemetrySample.Available(observation, detail)).Should().BeTrue();

        var values = mqtt.States.Single().ComponentValues;
        values["controller_temperature"].GetDouble().Should().Be(40);
        values["controller_secondary_temperature"].GetDouble().Should().Be(37);
        values["controller_status"].GetString().Should().Be("2");
        values["controller_fault"].GetString().Should().Be("0");
        values["charge_state"].GetString().Should().Be("3");
        values["controller_estimated_soc"].GetDouble().Should().Be(90);
        values["controller_diagnostic_217"].GetString().Should().Be("2");
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

    private static EdgeRuntimeIdentity Identity(string? siteId = "hvo") => new(
        "hvo-eg4", "1", "test", "eg4", "eg4-direct", GatewayDomain.Power,
        "eg4-fleet", siteId, null, "Testing", "test", "EG4");

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
