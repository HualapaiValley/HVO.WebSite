using MQTTnet;
using MQTTnet.Protocol;

namespace HVO.Edge.HomeAssistant.Mqtt;

internal sealed record MqttConnectionSettings(
    string Host,
    int Port,
    string ClientId,
    string Username,
    string Password,
    string WillTopic);

internal sealed record MqttPublishMessage(string Topic, string Payload, bool Retain = true, int QualityOfService = 1);
internal sealed record MqttReceivedMessage(string Topic, string Payload, bool Retain);

internal interface IMqttSession : IAsyncDisposable
{
    bool IsConnected { get; }
    event Action? Disconnected;
    event Action<MqttReceivedMessage>? MessageReceived;
    Task ConnectAsync(MqttConnectionSettings settings, CancellationToken cancellationToken);
    Task SubscribeAsync(string topic, CancellationToken cancellationToken);
    Task PublishAsync(MqttPublishMessage message, CancellationToken cancellationToken);
    Task DisconnectAsync(CancellationToken cancellationToken);
}

internal sealed class MqttNetSession : IMqttSession
{
    private readonly IMqttClient client;

    public MqttNetSession()
    {
        client = new MqttClientFactory().CreateMqttClient();
        client.DisconnectedAsync += _ =>
        {
            Disconnected?.Invoke();
            return Task.CompletedTask;
        };
        client.ApplicationMessageReceivedAsync += arguments =>
        {
            var payload = arguments.ApplicationMessage.ConvertPayloadToString();
            MessageReceived?.Invoke(new(
                arguments.ApplicationMessage.Topic,
                payload,
                arguments.ApplicationMessage.Retain));
            return Task.CompletedTask;
        };
    }

    public bool IsConnected => client.IsConnected;
    public event Action? Disconnected;
    public event Action<MqttReceivedMessage>? MessageReceived;

    public async Task ConnectAsync(MqttConnectionSettings settings, CancellationToken cancellationToken)
    {
        var options = new MqttClientOptionsBuilder()
            .WithClientId(settings.ClientId)
            .WithTcpServer(settings.Host, settings.Port)
            .WithCredentials(settings.Username, settings.Password)
            .WithWillTopic(settings.WillTopic)
            .WithWillPayload("offline")
            .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithWillRetain()
            .Build();
        await client.ConnectAsync(options, cancellationToken);
    }

    public async Task SubscribeAsync(string topic, CancellationToken cancellationToken)
    {
        var options = new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(filter => filter.WithTopic(topic).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
            .Build();
        await client.SubscribeAsync(options, cancellationToken);
    }

    public async Task PublishAsync(MqttPublishMessage message, CancellationToken cancellationToken)
    {
        if (!message.Retain || message.QualityOfService != 1)
            throw new ArgumentException("Home Assistant MQTT messages must use retained QoS 1 delivery.", nameof(message));
        var applicationMessage = new MqttApplicationMessageBuilder()
            .WithTopic(message.Topic)
            .WithPayload(message.Payload)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithRetainFlag()
            .Build();
        await client.PublishAsync(applicationMessage, cancellationToken);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        if (client.IsConnected)
            await client.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().Build(), cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (client.IsConnected)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await DisconnectAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
            }
        }
        client.Dispose();
    }
}
