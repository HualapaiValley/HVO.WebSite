using System.Globalization;
using System.Text.Json;
using HVO.Hardware.DavisVantagePro2.Configuration;
using HVO.Ingest.Contracts;
using HVO.Ingest.Contracts.Weather;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace HVO.Hardware.DavisVantagePro2.Ingest;

public sealed class RabbitMqWeatherRawPublisher(
    IOptions<RabbitMqIngestOptions> options,
    ILogger<RabbitMqWeatherRawPublisher> logger) : IWeatherRawPublisher, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly RabbitMqIngestOptions _options = options.Value;
    private readonly SemaphoreSlim _publishGate = new(1, 1);
    private IConnection? _connection;
    private IModel? _channel;

    public async Task PublishAsync(WeatherRawV1 payload, CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            return;
        }

        await _publishGate.WaitAsync(ct);
        try
        {
            var envelope = CreateEnvelope(payload);
            var body = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
            var channel = EnsureChannel();
            var properties = channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.DeliveryMode = 2;
            properties.ContentType = "application/json";
            properties.Type = envelope.Schema;
            properties.MessageId = envelope.MessageId;
            properties.AppId = _options.Source;
            properties.Timestamp = new AmqpTimestamp(envelope.PublishedAtUtc.ToUnixTimeSeconds());
            properties.Headers = new Dictionary<string, object>
            {
                ["schema"] = envelope.Schema,
                ["siteId"] = envelope.SiteId,
                ["source"] = envelope.Source,
                ["deviceExternalId"] = envelope.DeviceExternalId
            };

            channel.BasicPublish(
                exchange: _options.Exchange,
                routingKey: CreateRoutingKey(envelope),
                mandatory: true,
                basicProperties: properties,
                body: body);
            channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(_options.PublishTimeoutSeconds));

            logger.LogDebug(
                "Published {Schema} message {MessageId} for station {StationId}",
                envelope.Schema,
                envelope.MessageId,
                payload.StationId);
        }
        catch (AlreadyClosedException)
        {
            ResetConnection();
            throw;
        }
        catch (BrokerUnreachableException)
        {
            ResetConnection();
            throw;
        }
        finally
        {
            _publishGate.Release();
        }
    }

    public void Dispose()
    {
        _publishGate.Dispose();
        ResetConnection();
    }

    private IModel EnsureChannel()
    {
        if (_channel?.IsOpen == true)
        {
            return _channel;
        }

        ResetConnection();

        if (string.IsNullOrWhiteSpace(_options.AmqpUri))
        {
            throw new InvalidOperationException("RabbitMqIngest:AmqpUri is required when RabbitMQ ingest is enabled.");
        }

        var factory = new ConnectionFactory
        {
            Uri = new Uri(_options.AmqpUri),
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(10),
            ClientProvidedName = "hvo-davis-vantage-pro2"
        };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();
        _channel.ExchangeDeclare(
            exchange: _options.Exchange,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false);
        _channel.ConfirmSelect();
        return _channel;
    }

    private void ResetConnection()
    {
        try { _channel?.Dispose(); } catch { }
        try { _connection?.Dispose(); } catch { }
        _channel = null;
        _connection = null;
    }

    private CanonicalEnvelope<WeatherRawV1> CreateEnvelope(WeatherRawV1 payload)
    {
        var observedAt = (payload.RecordedAt ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var siteId = string.IsNullOrWhiteSpace(_options.SiteId) ? "hvo" : _options.SiteId;
        var source = string.IsNullOrWhiteSpace(_options.Source) ? "davis-vantage-pro2" : _options.Source;

        return new CanonicalEnvelope<WeatherRawV1>
        {
            MessageId = $"{HvoSchemas.WeatherRawV1}:{payload.StationId}:{FormatTimestamp(observedAt)}",
            Schema = HvoSchemas.WeatherRawV1,
            SiteId = siteId,
            Source = source,
            DeviceExternalId = payload.StationId,
            ObservedAtUtc = observedAt,
            PublishedAtUtc = DateTimeOffset.UtcNow,
            Payload = payload
        };
    }

    private static string CreateRoutingKey(CanonicalEnvelope<WeatherRawV1> envelope) =>
        $"hvo.ingest.weather.raw.v1.{envelope.SiteId}.{envelope.DeviceExternalId}";

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
}
