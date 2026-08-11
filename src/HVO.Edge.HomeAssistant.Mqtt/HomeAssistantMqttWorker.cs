using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.Edge.HomeAssistant.Mqtt;

internal sealed class HomeAssistantMqttWorker(
    HomeAssistantMqttProjection projection,
    IMqttSession session,
    MqttRuntimeCredential credential,
    IOptions<HomeAssistantMqttOptions> options,
    ILogger<HomeAssistantMqttWorker> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions StateSerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HomeAssistantMqttTopics topics = new(options.Value.DiscoveryPrefix, options.Value.TopicPrefix);
    private int birthPending;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
            return;

        session.Disconnected += OnDisconnected;
        session.MessageReceived += OnMessageReceived;
        var reconnectDelay = TimeSpan.FromSeconds(options.Value.InitialReconnectDelaySeconds);
        var maxReconnectDelay = TimeSpan.FromSeconds(options.Value.MaxReconnectDelaySeconds);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (!session.IsConnected)
                    {
                        await session.ConnectAsync(credential.Settings!, stoppingToken);
                        await session.SubscribeAsync(topics.Birth, stoppingToken);
                        projection.SetConnected(true);
                        projection.DiscardChanges();
                        await RepublishAllAsync(stoppingToken);
                        projection.SetConnected(true);
                        reconnectDelay = TimeSpan.FromSeconds(options.Value.InitialReconnectDelaySeconds);
                    }

                    await projection.WaitForChangeAsync(stoppingToken);
                    if (!session.IsConnected)
                        continue;
                    await RepublishAllAsync(stoppingToken);
                    Interlocked.Exchange(ref birthPending, 0);
                    projection.SetConnected(true);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    projection.SetFailure("mqtt_transport");
                    logger.LogWarning(exception, "Home Assistant MQTT operation failed; retrying independently of acquisition.");
                    if (session.IsConnected)
                    {
                        try
                        {
                            await session.DisconnectAsync(stoppingToken);
                        }
                        catch (Exception disconnectException)
                        {
                            logger.LogDebug(disconnectException, "Failed to reset incomplete Home Assistant MQTT session.");
                        }
                    }
                    await DelayIgnoringFailureAsync(reconnectDelay, stoppingToken);
                    reconnectDelay = TimeSpan.FromSeconds(Math.Min(reconnectDelay.TotalSeconds * 2, maxReconnectDelay.TotalSeconds));
                    projection.Signal();
                }
            }
        }
        finally
        {
            session.Disconnected -= OnDisconnected;
            session.MessageReceived -= OnMessageReceived;
            if (session.IsConnected)
            {
                try
                {
                    using var shutdownTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await PublishAsync(credential.Settings!.WillTopic, "offline", shutdownTimeout.Token);
                    await session.DisconnectAsync(shutdownTimeout.Token);
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Home Assistant MQTT graceful shutdown failed.");
                }
            }
            projection.SetConnected(false);
        }
    }

    private async Task RepublishAllAsync(CancellationToken cancellationToken)
    {
        var snapshot = projection.Snapshot();
        var completedRemovals = new List<HomeAssistantDeviceDefinition>();
        var hadDeviceFailure = false;
        foreach (var definition in snapshot.Removals)
        {
            try
            {
                await RemoveDeviceAsync(definition, cancellationToken);
                completedRemovals.Add(definition);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to remove Home Assistant MQTT device {DeviceId}; other devices will continue.", definition.Key.DeviceId);
                hadDeviceFailure = true;
            }
        }
        projection.CompleteRemovals(completedRemovals);

        foreach (var entry in snapshot.Devices)
        {
            try
            {
                await PublishAsync(
                    topics.Discovery(entry.Definition.Key),
                    HomeAssistantDiscoverySerializer.Serialize(entry.Definition, topics, entry.RemovedComponents),
                    cancellationToken);
                projection.CompleteComponentRemovals(entry.Definition.Key, entry.RemovedComponents);
                if (entry.State is not null)
                    await PublishAsync(topics.State(entry.Definition.Key), SerializeState(entry.State), cancellationToken);
                await PublishAsync(topics.DeviceAvailability(entry.Definition.Key), entry.State?.Available == true ? "online" : "offline", cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to publish Home Assistant MQTT device {DeviceId}; other devices will continue.", entry.Definition.Key.DeviceId);
                hadDeviceFailure = true;
            }
        }

        await PublishAsync(credential.Settings!.WillTopic, "online", cancellationToken);
        if (hadDeviceFailure)
            throw new InvalidOperationException("One or more Home Assistant MQTT devices could not be published.");
    }

    private async Task RemoveDeviceAsync(HomeAssistantDeviceDefinition definition, CancellationToken cancellationToken)
    {
        await PublishAsync(topics.DeviceAvailability(definition.Key), "offline", cancellationToken);
        await PublishAsync(topics.Discovery(definition.Key), string.Empty, cancellationToken);
        await PublishAsync(topics.State(definition.Key), string.Empty, cancellationToken);
        await PublishAsync(topics.DeviceAvailability(definition.Key), string.Empty, cancellationToken);
    }

    private Task PublishAsync(string topic, string payload, CancellationToken cancellationToken) =>
        session.PublishAsync(new(topic, payload), cancellationToken);

    private static string SerializeState(HomeAssistantCurrentState state) => JsonSerializer.Serialize(new
    {
        observed_at_utc = state.ObservedAtUtc,
        components = state.ComponentValues
    }, StateSerializerOptions);

    private void OnDisconnected()
    {
        projection.SetConnected(false);
        projection.Signal();
    }

    private void OnMessageReceived(string topic, string payload)
    {
        if (string.Equals(topic, topics.Birth, StringComparison.Ordinal)
            && string.Equals(payload.Trim(), "online", StringComparison.OrdinalIgnoreCase)
            && Interlocked.Exchange(ref birthPending, 1) == 0)
        {
            projection.Signal();
        }
    }

    private static async Task DelayIgnoringFailureAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
