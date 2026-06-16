using HVO.Edge.Outbox;

namespace HVO.Hardware.DavisVantagePro2.Outbox;

public sealed class DavisOutboxWriter(
    EdgeOutboxStore<OutboxDbContext> store,
    ILogger<DavisOutboxWriter> logger)
{
    public async Task<bool> EnqueueRawAsync(string stationId, DateTime recordedAtUtc, string payloadJson, CancellationToken ct)
    {
        var inserted = await store.EnqueueAsync(new EdgeOutboxMessage(
            SourceId: stationId,
            RecordedAtUtc: recordedAtUtc,
            PayloadType: DavisOutboxPayloadTypes.Raw,
            PayloadVersion: DavisOutboxPayloadTypes.RawVersion,
            PayloadJson: payloadJson), ct);

        if (!inserted)
            logger.LogDebug("Davis raw outbox duplicate skipped for {StationId} at {RecordedAt:O}", stationId, recordedAtUtc);

        return inserted;
    }

    public async Task<bool> EnqueueArchiveAsync(string stationId, DateTime recordedAtUtc, string payloadJson, CancellationToken ct)
    {
        var inserted = await store.EnqueueAsync(new EdgeOutboxMessage(
            SourceId: stationId,
            RecordedAtUtc: recordedAtUtc,
            PayloadType: DavisOutboxPayloadTypes.Archive,
            PayloadVersion: DavisOutboxPayloadTypes.ArchiveVersion,
            PayloadJson: payloadJson), ct);

        if (!inserted)
            logger.LogDebug("Davis archive outbox duplicate skipped for {StationId} at {RecordedAt:O}", stationId, recordedAtUtc);

        return inserted;
    }

    public async Task<bool> EnqueueConfigAsync(string stationId, string payloadJson, CancellationToken ct)
    {
        var recordedAtUtc = DateTime.UtcNow;
        var inserted = await store.EnqueueAsync(new EdgeOutboxMessage(
            SourceId: stationId,
            RecordedAtUtc: recordedAtUtc,
            PayloadType: DavisOutboxPayloadTypes.Config,
            PayloadVersion: DavisOutboxPayloadTypes.ConfigVersion,
            PayloadJson: payloadJson), ct);

        if (!inserted)
            logger.LogDebug("Davis config outbox duplicate skipped for {StationId} at {RecordedAt:O}", stationId, recordedAtUtc);

        return inserted;
    }
}
