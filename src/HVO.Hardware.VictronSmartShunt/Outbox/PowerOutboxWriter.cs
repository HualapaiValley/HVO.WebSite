using System.Text.Json;
using HVO.Edge.Outbox;
using HVO.Hardware.VictronSmartShunt.SmartShunt;

namespace HVO.Hardware.VictronSmartShunt.Outbox;

public sealed class PowerOutboxWriter(EdgeOutboxStore<OutboxDbContext> store, ILogger<PowerOutboxWriter> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly EdgeOutboxStore<OutboxDbContext> _store = store;
    private readonly ILogger<PowerOutboxWriter> _logger = logger;

    public async Task<bool> EnqueueAsync(PowerReadingPayload payload, CancellationToken ct)
    {
        var sourceId = payload.SourceId?.Trim() ?? string.Empty;
        if (sourceId.Length == 0)
            throw new InvalidOperationException("Power reading payload must include SourceId.");

        var recordedAt = payload.RecordedAtUtc.ToUniversalTime();
        if (recordedAt == default)
            throw new InvalidOperationException("Power reading payload must include RecordedAtUtc.");

        var inserted = await _store.EnqueueAsync(new EdgeOutboxMessage(
            SourceId: sourceId,
            DeviceId: payload.DeviceId,
            RecordedAtUtc: recordedAt,
            PayloadType: SmartShuntOutboxPayloadTypes.Reading,
            PayloadVersion: SmartShuntOutboxPayloadTypes.ReadingVersion,
            PayloadJson: JsonSerializer.Serialize(payload, JsonOptions)), ct);

        if (!inserted)
        {
            _logger.LogDebug(
                "SmartShunt outbox duplicate skipped for {SourceId} at {RecordedAt:O}",
                sourceId,
                recordedAt);
        }

        return inserted;
    }
}
