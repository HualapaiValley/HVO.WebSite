using System.Text.Json;
using HVO.Edge.Contracts.Weather;
using HVO.Edge.Outbox;

namespace HVO.Hardware.DavisVantagePro2.Outbox;

public interface IDavisOutboxWriter
{
    Task<bool> EnqueueRawAsync(DavisWeatherLivePayload payload, CancellationToken cancellationToken);
    Task<bool> EnqueueArchiveAsync(DavisWeatherArchivePayload payload, CancellationToken cancellationToken);
}

public sealed class DavisOutboxWriter(
    EdgeOutboxStore<DefaultEdgeOutboxDbContext> store,
    ILogger<DavisOutboxWriter> logger) : IDavisOutboxWriter
{
    public async Task<bool> EnqueueRawAsync(DavisWeatherLivePayload payload, CancellationToken ct)
    {
        var inserted = await store.EnqueueAsync(new EdgeOutboxMessage(
            SourceId: payload.StationId,
            RecordedAtUtc: payload.RecordedAtUtc,
            PayloadType: DavisOutboxPayloadTypes.Raw,
            PayloadVersion: DavisOutboxPayloadTypes.RawVersion,
            PayloadJson: JsonSerializer.Serialize(payload, JsonSerializerOptions.Web)), ct);

        if (!inserted)
            logger.LogDebug("Davis raw outbox duplicate skipped for {StationId} at {RecordedAt:O}", payload.StationId, payload.RecordedAtUtc);

        return inserted;
    }

    public async Task<bool> EnqueueArchiveAsync(DavisWeatherArchivePayload payload, CancellationToken ct)
    {
        var inserted = await store.EnqueueAsync(new EdgeOutboxMessage(
            SourceId: payload.StationId,
            RecordedAtUtc: payload.RecordedAtUtc,
            PayloadType: DavisOutboxPayloadTypes.Archive,
            PayloadVersion: DavisOutboxPayloadTypes.ArchiveVersion,
            PayloadJson: JsonSerializer.Serialize(payload, JsonSerializerOptions.Web)), ct);

        if (!inserted)
            logger.LogDebug("Davis archive outbox duplicate skipped for {StationId} at {RecordedAt:O}", payload.StationId, payload.RecordedAtUtc);

        return inserted;
    }

}
