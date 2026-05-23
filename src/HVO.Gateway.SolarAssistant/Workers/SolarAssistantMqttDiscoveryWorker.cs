using HVO.Gateway.SolarAssistant.Configuration;
using HVO.Gateway.SolarAssistant.SolarAssistant.Mqtt;
using Microsoft.Extensions.Options;

namespace HVO.Gateway.SolarAssistant.Workers;

/// <summary>Read-only SolarAssistant MQTT subscriber for Home Assistant discovery and state availability.</summary>
public sealed class SolarAssistantMqttDiscoveryWorker : BackgroundService
{
    private static readonly string[] Subscriptions =
    [
        "homeassistant/#",
        "solar_assistant/#",
    ];

    private readonly SolarAssistantOptions _options;
    private readonly SolarAssistantMqttInventoryStore _store;
    private readonly ILogger<SolarAssistantMqttDiscoveryWorker> _logger;

    public SolarAssistantMqttDiscoveryWorker(
        IOptions<SolarAssistantOptions> options,
        SolarAssistantMqttInventoryStore store,
        ILogger<SolarAssistantMqttDiscoveryWorker> logger)
    {
        _options = options.Value;
        _store = store;
        _logger = logger;
    }

    public SolarAssistantMqttInventory Inventory => _store.Snapshot;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableMqttDiscovery)
        {
            _store.MarkDisabled("MQTT discovery is disabled.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.Host))
        {
            _store.MarkDisabled("SolarAssistant host is not configured.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.MqttUsername))
            _logger.LogWarning("SolarAssistant MQTT username is not configured; anonymous MQTT discovery may fail.");

        while (!stoppingToken.IsCancellationRequested)
        {
            _store.MarkConnecting(Subscriptions);
            try
            {
                _logger.LogInformation("SolarAssistant MQTT discovery connecting to {Host}:{Port}", _options.Host, _options.MqttPort);
                var client = new SolarAssistantMqttClient(_options);
                await client.RunAsync(Subscriptions, OnMessageAsync, _store.MarkConnected, stoppingToken);
                _store.MarkDisconnected(null);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _store.MarkDisconnected(ex.Message);
                _logger.LogWarning(ex, "SolarAssistant MQTT discovery connection failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.MqttReconnectDelaySeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private Task OnMessageAsync(SolarAssistantMqttMessage message, CancellationToken ct)
    {
        _store.Apply(message);
        return Task.CompletedTask;
    }
}
