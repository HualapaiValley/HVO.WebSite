using FluentAssertions;

namespace HVO.Edge.HomeAssistant.Mqtt.Tests;

[TestClass]
public sealed class HomeAssistantMqttWorkerTests
{
    [TestMethod]
    public async Task InitialConnection_PublishesRetainedQosOneInRequiredOrder()
    {
        await using var harness = TestSupport.Worker();
        harness.Projection.UpsertDevice(TestSupport.Device());
        harness.Projection.PublishCurrentState(TestSupport.State(DateTimeOffset.UtcNow, 52.1));

        await harness.StartAsync();
        await TestSupport.WaitUntilAsync(() => harness.Session.Messages.Count >= 4);

        harness.Session.Subscriptions.Should().ContainSingle().Which.Should().Be("homeassistant/status");
        harness.Session.Messages.Should().OnlyContain(message => message.Retain && message.QualityOfService == 1);
        harness.Session.Messages.Select(message => message.Topic).Should().StartWith(
            TestSupport.Topics.Discovery(TestSupport.Key),
            TestSupport.Topics.State(TestSupport.Key),
            TestSupport.Topics.DeviceAvailability(TestSupport.Key),
            TestSupport.Topics.GatewayAvailability(TestSupport.Key));
        harness.Session.Messages[2].Payload.Should().Be("online");
        harness.Session.Messages[3].Payload.Should().Be("online");
    }

    [TestMethod]
    public async Task DisconnectedUpdates_CoalesceToOneLatestStatePublish()
    {
        var session = new FakeMqttSession { DelayConnect = true };
        await using var harness = TestSupport.Worker(session);
        harness.Projection.UpsertDevice(TestSupport.Device());
        await harness.StartAsync();
        await TestSupport.WaitUntilAsync(() => session.ConnectCount == 1);

        var observed = DateTimeOffset.Parse("2026-08-11T12:00:00Z");
        harness.Projection.PublishCurrentState(TestSupport.State(observed.AddSeconds(-2), 50));
        harness.Projection.PublishCurrentState(TestSupport.State(observed.AddSeconds(-1), 51));
        harness.Projection.PublishCurrentState(TestSupport.State(observed, 52));
        session.ReleaseConnect();

        await TestSupport.WaitUntilAsync(() => session.Messages.Any(message => message.Topic.EndsWith("/state", StringComparison.Ordinal)));
        var states = session.Messages.Where(message => message.Topic.EndsWith("/state", StringComparison.Ordinal)).ToList();
        states.Should().ContainSingle();
        states[0].Payload.Should().Contain("\"voltage\":52");
        states[0].Payload.Should().Contain("2026-08-11T12:00:00");
    }

    [TestMethod]
    public async Task HomeAssistantBirth_CoalescesAndRepublishesCurrentSnapshot()
    {
        await using var harness = TestSupport.Worker();
        harness.Projection.UpsertDevice(TestSupport.Device());
        harness.Projection.PublishCurrentState(TestSupport.State(DateTimeOffset.UtcNow, 52));
        await harness.StartAsync();
        await TestSupport.WaitUntilAsync(() => DiscoveryCount(harness.Session) == 1);

        harness.Session.EmitBirth();
        harness.Session.EmitBirth();

        await TestSupport.WaitUntilAsync(() => DiscoveryCount(harness.Session) == 2);
        await Task.Delay(50);
        DiscoveryCount(harness.Session).Should().Be(2);
    }

    [TestMethod]
    public async Task BrokerDisconnect_ReconnectsAndGracefulStopPublishesGatewayOffline()
    {
        var harness = TestSupport.Worker();
        harness.Projection.UpsertDevice(TestSupport.Device());
        await harness.StartAsync();
        await TestSupport.WaitUntilAsync(() => harness.Session.ConnectCount == 1 && DiscoveryCount(harness.Session) == 1);

        harness.Session.DropConnection();

        await TestSupport.WaitUntilAsync(() => harness.Session.ConnectCount == 2 && DiscoveryCount(harness.Session) == 2);
        await harness.DisposeAsync();

        harness.Session.Messages.Last().Should().Be(new MqttPublishMessage(
            TestSupport.Topics.GatewayAvailability(TestSupport.Key),
            "offline"));
    }

    [TestMethod]
    public async Task SubscribeFailure_ResetsSessionAndRestoresBirthSubscription()
    {
        var session = new FakeMqttSession { FailNextSubscribe = true };
        await using var harness = TestSupport.Worker(session);
        harness.Projection.UpsertDevice(TestSupport.Device());

        await harness.StartAsync();

        await TestSupport.WaitUntilAsync(() => session.ConnectCount >= 2 && session.Subscriptions.Count == 1);
        session.EmitBirth();
        await TestSupport.WaitUntilAsync(() => DiscoveryCount(session) >= 2);
    }

    [TestMethod]
    public async Task Removal_PublishesOfflineAndRetainedTombstones()
    {
        await using var harness = TestSupport.Worker();
        harness.Projection.UpsertDevice(TestSupport.Device());
        harness.Projection.PublishCurrentState(TestSupport.State(DateTimeOffset.UtcNow, 52));
        await harness.StartAsync();
        await TestSupport.WaitUntilAsync(() => harness.Session.Messages.Count >= 4);
        var baseline = harness.Session.Messages.Count;

        harness.Projection.RemoveDevice(TestSupport.Key).Should().BeTrue();

        await TestSupport.WaitUntilAsync(() => harness.Session.Messages.Count >= baseline + 5);
        var removal = harness.Session.Messages.Skip(baseline).Take(4).ToList();
        removal.Select(message => (message.Topic, message.Payload)).Should().Equal(
            (TestSupport.Topics.DeviceAvailability(TestSupport.Key), "offline"),
            (TestSupport.Topics.Discovery(TestSupport.Key), string.Empty),
            (TestSupport.Topics.State(TestSupport.Key), string.Empty),
            (TestSupport.Topics.DeviceAvailability(TestSupport.Key), string.Empty));
        removal.Should().OnlyContain(message => message.Retain && message.QualityOfService == 1);
    }

    [TestMethod]
    public async Task DefinitionUpdate_PublishesRemovedComponentMarker()
    {
        await using var harness = TestSupport.Worker();
        harness.Projection.UpsertDevice(TestSupport.Device());
        await harness.StartAsync();
        await TestSupport.WaitUntilAsync(() => DiscoveryCount(harness.Session) == 1);

        harness.Projection.UpsertDevice(TestSupport.Device(
            TestSupport.Key,
            new HomeAssistantSensorDefinition("voltage", "Voltage", "V", "voltage", "measurement")));

        await TestSupport.WaitUntilAsync(() => DiscoveryCount(harness.Session) == 2);
        var payload = harness.Session.Messages.Last(message => message.Topic == TestSupport.Topics.Discovery(TestSupport.Key)).Payload;
        using var document = System.Text.Json.JsonDocument.Parse(payload);
        var charging = document.RootElement.GetProperty("components").GetProperty("charging");
        charging.EnumerateObject().Select(property => property.Name).Should().Equal("platform");
        charging.GetProperty("platform").GetString().Should().Be("binary_sensor");
    }

    [TestMethod]
    public async Task DevicePublishFailure_DoesNotPreventOtherDevicePublishing()
    {
        var failed = false;
        var session = new FakeMqttSession
        {
            FailPublish = message => !failed
                && message.Topic.Contains("bad_device", StringComparison.Ordinal)
                && (failed = true)
        };
        await using var harness = TestSupport.Worker(session);
        var badKey = TestSupport.Key with { DeviceId = "bad-device" };
        var goodKey = TestSupport.Key with { DeviceId = "good-device" };
        harness.Projection.UpsertDevice(TestSupport.Device(badKey));
        harness.Projection.UpsertDevice(TestSupport.Device(goodKey));

        await harness.StartAsync();

        await TestSupport.WaitUntilAsync(() => session.Messages.Any(message =>
            message.Topic == TestSupport.Topics.Discovery(goodKey)));
        session.Messages.Should().Contain(message =>
            message.Topic == TestSupport.Topics.DeviceAvailability(goodKey));
    }

    private static int DiscoveryCount(FakeMqttSession session) => session.Messages.Count(message =>
        message.Topic == TestSupport.Topics.Discovery(TestSupport.Key));
}
