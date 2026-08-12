using System.Text.Json;
using FluentAssertions;
using HVO.Edge.Contracts;
using HVO.Edge.Contracts.PowerSystem;
using HVO.Edge.HomeAssistant.Mqtt;
using HVO.Edge.Hosting;
using HVO.Edge.Hosting.Telemetry;
using HVO.Hardware.Eg4.Configuration;
using HVO.Hardware.Eg4.Diagnostics;
using HVO.Hardware.Eg4.HomeAssistant;
using HVO.Hardware.Eg4.Outbox;
using HVO.Hardware.Eg4.Telemetry;
using HVO.Hardware.Eg4.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.Eg4.Tests.Workers;

[TestClass]
public sealed class Eg4FleetWorkerTests
{
    [TestMethod]
    public async Task PollOnce_PublishesCanonicalCurrentStateAndOneDurableBundle()
    {
        var device = Device();
        var observation = Observation(device, -20);
        var detail = new PowerInverterDetailPayload
        {
            SourceId = device.SourceId,
            DeviceId = device.DeviceId,
            SourceSystem = "eg4-6500ex",
            RecordedAtUtc = observation.ObservedAtUtc,
            Load = new PowerInverterLoadDetail { LoadPowerW = 900 }
        };
        var writer = new FakeWriter();
        var mqtt = new FakeMqttProjection();
        var options = Options.Create(new Eg4Options { Devices = [device] });
        var runtime = new Eg4RuntimeState(options);
        using var telemetry = new GatewayTelemetry(new("eg4-test", "eg4-direct"));
        using var services = Services(writer);
        var worker = new Eg4FleetWorker(
            new FakeSource(Eg4TelemetrySample.Available(observation, inverterDetail: detail)),
            services.GetRequiredService<IServiceScopeFactory>(),
            options,
            runtime,
            new Eg4HomeAssistantProjection(mqtt, Identity(), options),
            telemetry,
            TimeProvider.System,
            NullLogger<Eg4FleetWorker>.Instance);

        (await worker.PollOnceAsync(device, CancellationToken.None)).Should().BeTrue();

        writer.Bundles.Should().ContainSingle();
        writer.Bundles[0].Reading.BatteryCurrentA.Should().Be(-20);
        writer.Bundles[0].Reading.BatteryPowerW.Should().Be(-1080);
        writer.Bundles[0].InverterDetail.Should().BeSameAs(detail);
        mqtt.Definitions.Should().ContainSingle();
        mqtt.States.Should().ContainSingle();
        mqtt.States[0].Available.Should().BeTrue();
        mqtt.States[0].ComponentValues["battery_net_current"].GetDouble().Should().Be(-20);
        runtime.Snapshot().Single().FailureCategory.Should().BeNull();
    }

    [TestMethod]
    public async Task PollOnce_UnavailableMarksOnlyCurrentStateAndEnqueuesNothing()
    {
        var device = Device();
        var writer = new FakeWriter();
        var mqtt = new FakeMqttProjection();
        var options = Options.Create(new Eg4Options { Devices = [device] });
        var runtime = new Eg4RuntimeState(options);
        using var telemetry = new GatewayTelemetry(new("eg4-test", "eg4-direct"));
        using var services = Services(writer);
        var worker = new Eg4FleetWorker(
            new FakeSource(Eg4TelemetrySample.Unavailable("nighttime silence")),
            services.GetRequiredService<IServiceScopeFactory>(),
            options,
            runtime,
            new Eg4HomeAssistantProjection(mqtt, Identity(), options),
            telemetry,
            TimeProvider.System,
            NullLogger<Eg4FleetWorker>.Instance);

        (await worker.PollOnceAsync(device, CancellationToken.None)).Should().BeFalse();

        writer.Bundles.Should().BeEmpty();
        mqtt.States.Should().ContainSingle().Which.Available.Should().BeFalse();
        mqtt.States[0].ComponentValues.Should().BeEmpty();
        runtime.Snapshot().Single().FailureCategory.Should().Be("unavailable");
    }

    private static ServiceProvider Services(FakeWriter writer) => new ServiceCollection()
        .AddSingleton<IEg4PowerOutboxWriter>(writer)
        .BuildServiceProvider();

    private static EdgeRuntimeIdentity Identity() => new(
        "hvo-eg4", "1", "test", "eg4", "eg4-direct", GatewayDomain.Power,
        "eg4-fleet", "hvo", null, "Testing", "test", "EG4");

    private static Eg4DeviceOptions Device() => new()
    {
        Type = Eg4DeviceType.Inverter6500Ex,
        SourceId = "eg4-a",
        DeviceId = "a",
        Alias = "Inverter A",
        Port = "/dev/hvo/eg4-a",
        UnitId = 0
    };

    private static PowerBatteryObservation Observation(Eg4DeviceOptions device, double current) => new(
        device.SourceId,
        device.DeviceId,
        PowerMetricSource.Eg46500Ex,
        PowerMeasurementRole.InverterBranch,
        "inverter-battery-branch",
        DateTime.UtcNow,
        54,
        current,
        54 * current,
        80,
        PowerObservationProvenance.Derived);

    private sealed class FakeSource(Eg4TelemetrySample sample) : IEg4TelemetrySource
    {
        public bool Supports(Eg4DeviceType deviceType) => true;
        public ValueTask<Eg4TelemetrySample> ReadAsync(Eg4DeviceOptions device, CancellationToken cancellationToken) =>
            ValueTask.FromResult(sample);
    }

    private sealed class FakeWriter : IEg4PowerOutboxWriter
    {
        public List<Eg4ObservationBundle> Bundles { get; } = [];
        public Task<bool> EnqueueAsync(Eg4ObservationBundle bundle, CancellationToken cancellationToken)
        {
            Bundles.Add(bundle);
            return Task.FromResult(true);
        }
    }

    private sealed class FakeMqttProjection : IHomeAssistantMqttProjection
    {
        public List<HomeAssistantDeviceDefinition> Definitions { get; } = [];
        public List<HomeAssistantCurrentState> States { get; } = [];
        public void UpsertDevice(HomeAssistantDeviceDefinition definition) => Definitions.Add(definition);
        public bool PublishCurrentState(HomeAssistantCurrentState state) { States.Add(state); return true; }
        public bool RemoveDevice(HomeAssistantDeviceKey key) => false;
        public HomeAssistantMqttStatus GetStatus() => new(false, false, Definitions.Count, null, null, null);
    }
}
