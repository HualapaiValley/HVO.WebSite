using System.Text.Json;
using HVO.Edge.Contracts.PowerSystem;
using HVO.Edge.Outbox;

namespace HVO.Hardware.Eg4.Outbox;

public interface IEg4PowerOutboxWriter
{
    Task<bool> EnqueueAsync(PowerReadingPayload payload, CancellationToken cancellationToken);
}

public sealed class PowerOutboxWriter(
    EdgeOutboxStore<OutboxDbContext> store,
    ILogger<PowerOutboxWriter> logger) : IEg4PowerOutboxWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<bool> EnqueueAsync(PowerReadingPayload payload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var sourceId = payload.SourceId?.Trim() ?? string.Empty;
        var deviceId = payload.DeviceId?.Trim() ?? string.Empty;
        if (sourceId.Length == 0 || deviceId.Length == 0)
            throw new InvalidOperationException("EG4 power readings require stable source and device identities.");
        if (payload.RecordedAtUtc == default || payload.RecordedAtUtc.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("EG4 power readings require a UTC RecordedAtUtc value.");

        var inserted = await store.EnqueueAsync(new EdgeOutboxMessage(
            SourceId: sourceId,
            RecordedAtUtc: payload.RecordedAtUtc,
            PayloadType: Eg4OutboxPayloadTypes.Reading,
            PayloadVersion: Eg4OutboxPayloadTypes.ReadingVersion,
            PayloadJson: JsonSerializer.Serialize(payload, JsonOptions),
            DeviceId: deviceId), cancellationToken);
        if (!inserted)
            logger.LogDebug("EG4 outbox duplicate skipped for {SourceId} at {RecordedAt:O}", sourceId, payload.RecordedAtUtc);
        return inserted;
    }
}
