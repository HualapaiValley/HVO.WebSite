using System.Text.Json;
using HVO.Edge.Contracts;
using HVO.Edge.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HVO.Edge.HomeAssistant.Mqtt.Tests;

internal static class TestSupport
{
    public static readonly HomeAssistantDeviceKey Key = new("observatory", "gateway-1", "device-1");
    public static readonly HomeAssistantMqttTopics Topics = new("homeassistant", "hvo");

    public static EdgeRuntimeIdentity Identity(string? siteId = "observatory") => new(
        "test-service",
        "1.0.0",
        "instance-1",
        "gateway-1",
        "test",
        GatewayDomain.Power,
        "source-1",
        siteId,
        null,
        "Testing",
        "test-host",
        "Test Gateway");

    public static HomeAssistantDeviceDefinition Device(
        HomeAssistantDeviceKey? key = null,
        params HomeAssistantEntityDefinition[]? entities) => new(
        key ?? Key,
        "Battery One",
        entities is { Length: > 0 }
            ? entities
            : [
                new HomeAssistantSensorDefinition("voltage", "Voltage", "V", "voltage", "measurement"),
                new HomeAssistantBinarySensorDefinition("charging", "Charging", "battery_charging")
            ],
        "HVO",
        "Test Device",
        "2.0");

    public static HomeAssistantCurrentState State(
        DateTimeOffset observedAtUtc,
        double voltage,
        HomeAssistantDeviceKey? key = null,
        bool available = true) => new(
        key ?? Key,
        observedAtUtc,
        new Dictionary<string, JsonElement>
        {
            ["voltage"] = JsonSerializer.SerializeToElement(voltage),
            ["charging"] = JsonSerializer.SerializeToElement(true)
        },
        available);

    public static HomeAssistantMqttOptions Options() => new()
    {
        Enabled = true,
        Host = "broker",
        UsernameSecret = "mqtt-user",
        PasswordSecret = "mqtt-password",
        DiscoveryPrefix = "homeassistant",
        TopicPrefix = "hvo",
        InitialReconnectDelaySeconds = 1,
        MaxReconnectDelaySeconds = 1
    };

    public static WorkerHarness Worker(FakeMqttSession? session = null)
    {
        var options = Microsoft.Extensions.Options.Options.Create(Options());
        var projection = new HomeAssistantMqttProjection(Identity(), options);
        var commandRouter = new HomeAssistantMqttCommandRouter(options);
        var fake = session ?? new FakeMqttSession();
        var credential = new MqttRuntimeCredential
        {
            Settings = new("broker", 1883, "client", "user", "password", Topics.GatewayAvailability(Key))
        };
        var worker = new HomeAssistantMqttWorker(
            projection,
            commandRouter,
            fake,
            credential,
            Identity(),
            options,
            NullLogger<HomeAssistantMqttWorker>.Instance);
        return new(worker, projection, fake);
    }

    public static async Task WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        using var cancellation = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(3));
        while (!condition())
            await Task.Delay(10, cancellation.Token);
    }
}

internal sealed record WorkerHarness(
    HomeAssistantMqttWorker Worker,
    HomeAssistantMqttProjection Projection,
    FakeMqttSession Session) : IAsyncDisposable
{
    public Task StartAsync() => Worker.StartAsync(CancellationToken.None);

    public async ValueTask DisposeAsync()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await Worker.StopAsync(cancellation.Token);
        Worker.Dispose();
    }
}

internal sealed class FakeMqttSession : IMqttSession
{
    private readonly object sync = new();
    private readonly List<MqttPublishMessage> messages = [];
    private readonly List<string> subscriptions = [];
    private int connectCount;

    public bool IsConnected { get; private set; }
    public TaskCompletionSource ConnectGate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool DelayConnect { get; set; }
    public bool FailNextSubscribe { get; set; }
    public Func<MqttPublishMessage, bool>? FailPublish { get; set; }
    public int ConnectCount => Volatile.Read(ref connectCount);
    public IReadOnlyList<MqttPublishMessage> Messages
    {
        get
        {
            lock (sync)
                return messages.ToArray();
        }
    }
    public IReadOnlyList<string> Subscriptions
    {
        get
        {
            lock (sync)
                return subscriptions.ToArray();
        }
    }

    public event Action? Disconnected;
    public event Action<MqttReceivedMessage>? MessageReceived;

    public async Task ConnectAsync(MqttConnectionSettings settings, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref connectCount);
        if (DelayConnect)
            await ConnectGate.Task.WaitAsync(cancellationToken);
        IsConnected = true;
    }

    public Task SubscribeAsync(string topic, CancellationToken cancellationToken)
    {
        if (FailNextSubscribe)
        {
            FailNextSubscribe = false;
            throw new InvalidOperationException("Simulated subscribe failure.");
        }
        lock (sync)
            subscriptions.Add(topic);
        return Task.CompletedTask;
    }

    public Task PublishAsync(MqttPublishMessage message, CancellationToken cancellationToken)
    {
        if (FailPublish?.Invoke(message) == true)
            throw new InvalidOperationException("Simulated MQTT failure.");
        lock (sync)
            messages.Add(message);
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        IsConnected = false;
        return Task.CompletedTask;
    }

    public void ReleaseConnect() => ConnectGate.TrySetResult();

    public void EmitBirth() => MessageReceived?.Invoke(new("homeassistant/status", "online", false));

    public void DropConnection()
    {
        IsConnected = false;
        Disconnected?.Invoke();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
