using System.Reflection;
using FluentAssertions;
using HVO.Edge.Contracts;
using HVO.Edge.HomeAssistant.Mqtt;
using HVO.Edge.Hosting;
using HVO.Edge.Hosting.Telemetry;
using HVO.Hardware.JkBms.Bms;
using HVO.Hardware.JkBms.Configuration;
using HVO.Hardware.JkBms.HomeAssistant;
using HVO.Hardware.JkBms.Hosting;
using HVO.Hardware.JkBms.Outbox;
using HVO.Hardware.JkBms.Protocol;
using HVO.Hardware.JkBms.Protocol.Packets;
using HVO.Hardware.JkBms.Protocol.Transport;
using HVO.Hardware.JkBms.Tests.Fakes;
using HVO.Hardware.JkBms.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Linux.Bluetooth;

namespace HVO.Hardware.JkBms.Tests.Workers;

[TestClass]
public sealed class BmsPollerWorkerTests
{
    [TestMethod]
    public void Constructor_CreatesIndependentStateForEachEnabledDevice()
    {
        using var fixture = WorkerFixture.Create(new JkBmsOptions
        {
            HciAdapter = "hci0",
            CentralIngestEndpoint = "https://central.test/api/v1/bms/readings",
            Devices =
            [
                Device("01", "a", hci: "hci1"),
                Device("02", "b"),
                Device("03", "disabled", enabled: false),
            ],
        });

        fixture.Worker.DeviceStates.Should().HaveCount(2);
        fixture.Worker.DeviceStates.Select(state => state.DeviceId).Should().Equal("a", "b");
        fixture.Worker.DeviceStates.Select(state => state.AdapterName).Should().Equal("hci1", "hci0");
        fixture.Worker.DeviceStates[0].Should().NotBeSameAs(fixture.Worker.DeviceStates[1]);
    }

    [TestMethod]
    public async Task SuccessfulPoll_PublishesCurrentStateAndOneDurableIngressBundle()
    {
        using var fixture = WorkerFixture.Create();
        var state = fixture.Worker.DeviceStates.Single();
        var packet = CellPacket();

        await fixture.InvokeSuccessfulPollAsync(state, packet);

        fixture.Writer.Records.Should().ContainSingle();
        fixture.Writer.Records[0].Reading.CurrentMa.Should().Be(-1500);
        fixture.Writer.Records[0].Reading.CellVoltagesMv.Should().Equal(3301, 3302, 3303, 3304);
        fixture.Mqtt.States.Should().ContainSingle().Which.Available.Should().BeTrue();
        fixture.Mqtt.States[0].ComponentValues["battery_net_current"].GetDouble().Should().Be(-1.5);
    }

    [TestMethod]
    public async Task ExecuteAsync_OneDeviceConnectionFailureDoesNotInterruptHealthySession()
    {
        var failed = Device("01", "failed");
        var healthy = Device("02", "healthy");
        var transportFactory = new FakeBmsTransportFactory(address =>
            string.Equals(address, healthy.Address, StringComparison.OrdinalIgnoreCase)
                ? new FakeBmsTransport(address, frameSequence:
                [
                    TestFrameBuilder.BuildDeviceInfoFrame(serialNumber: "HEALTHY"),
                    TestFrameBuilder.BuildCellInfoFrame(cellCount: 4),
                ])
                : new FakeBmsTransport(address));
        using var fixture = WorkerFixture.Create(
            new JkBmsOptions
            {
                CentralIngestEndpoint = "https://central.test/api/v1/bms/readings",
                Devices = [failed, healthy],
            },
            transportFactory,
            new AddressAwareCoordinator(failed.Address));

        await fixture.Worker.StartAsync(CancellationToken.None);
        await fixture.Writer.FirstWrite.WaitAsync(TimeSpan.FromSeconds(2));
        await fixture.Worker.StopAsync(CancellationToken.None);

        fixture.Writer.Records.Should().ContainSingle(record => record.Reading.DeviceAddress == healthy.Address);
        fixture.Worker.DeviceStates.Single(state => state.DeviceId == "failed").SessionRequestFailureCount.Should().BeGreaterThan(0);
        fixture.Worker.DeviceStates.Single(state => state.DeviceId == "healthy").LastPollAt.Should().NotBeNull();
    }

    private static BmsDeviceConfig Device(string suffix, string id, string? hci = null, bool enabled = true) => new()
    {
        Address = $"AA:BB:CC:DD:EE:{suffix}",
        DeviceId = id,
        Alias = $"Bank {id}",
        HciAdapter = hci,
        Enabled = enabled,
    };

    private static CellInfoPacket CellPacket()
    {
        var packet = CellInfoPacket.Parse(JkBmsProtocol.GetData(TestFrameBuilder.BuildCellInfoFrame(
            cellCount: 4,
            cellVoltagesMv: [3301, 3302, 3303, 3304],
            totalVoltageMv: 52_000,
            currentMa: -1_500,
            socPercent: 82)));
        typeof(CellInfoPacket).GetProperty(nameof(CellInfoPacket.RecordedAtUtc))!
            .SetValue(packet, new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc));
        return packet;
    }

    private sealed class WorkerFixture : IDisposable
    {
        private readonly ServiceProvider services;
        private readonly GatewayTelemetry telemetry;

        private WorkerFixture(
            ServiceProvider services,
            GatewayTelemetry telemetry,
            BmsPollerWorker worker,
            FakeWriter writer,
            FakeMqttProjection mqtt)
        {
            this.services = services;
            this.telemetry = telemetry;
            Worker = worker;
            Writer = writer;
            Mqtt = mqtt;
        }

        public BmsPollerWorker Worker { get; }
        public FakeWriter Writer { get; }
        public FakeMqttProjection Mqtt { get; }

        public static WorkerFixture Create(
            JkBmsOptions? configured = null,
            IBmsTransportFactory? transportFactory = null,
            IBluetoothAdapterCoordinator? coordinator = null)
        {
            var options = Options.Create(configured ?? new JkBmsOptions
            {
                CentralIngestEndpoint = "https://central.test/api/v1/bms/readings",
                Devices = [Device("01", "a")],
            });
            var writer = new FakeWriter();
            var mqtt = new FakeMqttProjection();
            var services = new ServiceCollection()
                .AddSingleton<IBmsOutboxWriter>(writer)
                .BuildServiceProvider();
            var telemetry = new GatewayTelemetry(new("jkbms-test", "jk-bms-direct"));
            var worker = new BmsPollerWorker(
                transportFactory ?? FakeBmsTransportFactory.AlwaysSucceed(),
                coordinator ?? new FakeBluetoothAdapterCoordinator(),
                NullLoggerFactory.Instance,
                services.GetRequiredService<IServiceScopeFactory>(),
                options,
                new JkBmsHomeAssistantProjection(mqtt, Identity(), options),
                new FakeCommandRouter(),
                new JkBmsSettingsPasswordCredentials(),
                telemetry,
                TimeProvider.System,
                NullLogger<BmsPollerWorker>.Instance);
            return new(services, telemetry, worker, writer, mqtt);
        }

        public async Task InvokeSuccessfulPollAsync(DevicePollState state, CellInfoPacket packet)
        {
            await using var client = new JkBmsClient(new FakeBmsTransport(state.Address), NullLogger<JkBmsClient>.Instance);
            var method = typeof(BmsPollerWorker).GetMethod("OnSuccessfulPollAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)method.Invoke(Worker, [state, client, packet, CancellationToken.None])!;
        }

        public void Dispose()
        {
            telemetry.Dispose();
            services.Dispose();
        }
    }

    private sealed class FakeWriter : IBmsOutboxWriter
    {
        public List<BmsIngressRecord> Records { get; } = [];
        public Task FirstWrite => firstWrite.Task;
        private readonly TaskCompletionSource firstWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<bool> EnqueueAsync(string sourceId, string deviceId, DateTime recordedAtUtc, BmsIngressRecord record, CancellationToken cancellationToken)
        {
            Records.Add(record);
            firstWrite.TrySetResult();
            return Task.FromResult(true);
        }
    }

    private sealed class AddressAwareCoordinator(string failedAddress) : IBluetoothAdapterCoordinator
    {
        public async Task ConnectAsync(
            string adapterName,
            string address,
            Func<Device, CancellationToken, Task> connectAsync,
            CancellationToken ct)
        {
            if (string.Equals(address, failedAddress, StringComparison.OrdinalIgnoreCase))
                throw new TimeoutException("isolated connect failure");
            await connectAsync(null!, ct);
        }
    }

    private sealed class FakeMqttProjection : IHomeAssistantMqttProjection
    {
        public List<HomeAssistantCurrentState> States { get; } = [];
        public void UpsertDevice(HomeAssistantDeviceDefinition definition) { }
        public bool PublishCurrentState(HomeAssistantCurrentState state) { States.Add(state); return true; }
        public bool RemoveDevice(HomeAssistantDeviceKey key) => false;
        public HomeAssistantMqttStatus GetStatus() => new(false, false, 0, null, null, null);
    }

    private sealed class FakeCommandRouter : IHomeAssistantMqttCommandRouter
    {
        public void Register(HomeAssistantDeviceKey key, string componentId, Action handler) { }
    }

    private static EdgeRuntimeIdentity Identity() => new(
        "hvo-jkbms", "1", "test", "jkbms", "jk-bms-direct", GatewayDomain.Power,
        "jkbms-fleet", "hvo", null, "Testing", "test", "JK BMS");
}
