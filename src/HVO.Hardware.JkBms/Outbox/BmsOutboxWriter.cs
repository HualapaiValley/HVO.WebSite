using System.Text.Json;
using HVO.Edge.Contracts;
using HVO.Edge.Outbox;
using HVO.Hardware.JkBms.Bms;

namespace HVO.Hardware.JkBms.Outbox;

public interface IBmsOutboxWriter
{
    Task<bool> EnqueueAsync(
        string sourceId,
        string deviceId,
        DateTime recordedAtUtc,
        BmsIngressRecord record,
        CancellationToken cancellationToken);
}

public sealed class BmsOutboxWriter(
    EdgeOutboxStore<DefaultEdgeOutboxDbContext> store,
    ILogger<BmsOutboxWriter> logger) : IBmsOutboxWriter
{
    public async Task<bool> EnqueueAsync(
        string sourceId,
        string deviceId,
        DateTime recordedAtUtc,
        BmsIngressRecord record,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(record.Reading);
        if (string.IsNullOrWhiteSpace(sourceId) || string.IsNullOrWhiteSpace(deviceId))
            throw new InvalidOperationException("JK BMS observations require stable source and device identities.");
        if (recordedAtUtc == default || recordedAtUtc.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("JK BMS observations require a UTC RecordedAtUtc value.");
        if (!string.Equals(record.Reading.DeviceAddress, sourceId, StringComparison.OrdinalIgnoreCase)
            || record.Reading.RecordedAtUtc != recordedAtUtc)
            throw new InvalidOperationException("JK BMS reading identity must match its outbox record.");

        var inserted = await store.EnqueueAsync(new EdgeOutboxMessage(
            sourceId,
            recordedAtUtc,
            EdgePayloadTypes.BmsReading,
            "1",
            JsonSerializer.Serialize(record, JsonSerializerOptions.Web),
            deviceId), cancellationToken);
        if (!inserted)
            logger.LogDebug("JK BMS outbox duplicate skipped for {SourceId} at {RecordedAt:O}", sourceId, recordedAtUtc);
        return inserted;
    }
}
