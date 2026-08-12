using System.Text.Json;
using HVO.Edge.Contracts;
using HVO.Edge.Contracts.PowerSystem;
using HVO.Edge.Outbox;

namespace HVO.Hardware.VictronSmartShunt.Outbox;

public interface ISmartShuntOutboxWriter
{
    Task<bool> EnqueueAsync(SmartShuntObservationPayload bundle, CancellationToken cancellationToken);
}

public sealed class SmartShuntOutboxWriter(
    EdgeOutboxStore<DefaultEdgeOutboxDbContext> store,
    ILogger<SmartShuntOutboxWriter> logger) : ISmartShuntOutboxWriter
{
    public async Task<bool> EnqueueAsync(SmartShuntObservationPayload bundle, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        var sourceId = bundle.Summary.SourceId?.Trim() ?? string.Empty;
        var deviceId = bundle.Summary.DeviceId?.Trim() ?? string.Empty;
        if (sourceId.Length == 0 || deviceId.Length == 0 || bundle.Summary.RecordedAtUtc.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("SmartShunt observations require stable identities and a UTC timestamp.");
        if (!string.Equals(bundle.Detail.SourceId, sourceId, StringComparison.Ordinal)
            || !string.Equals(bundle.Detail.DeviceId, deviceId, StringComparison.Ordinal)
            || bundle.Detail.RecordedAtUtc != bundle.Summary.RecordedAtUtc)
            throw new InvalidOperationException("SmartShunt detail identity must match its power summary.");

        var inserted = await store.EnqueueAsync(new EdgeOutboxMessage(
            sourceId,
            bundle.Summary.RecordedAtUtc,
            EdgePayloadTypes.SmartShuntObservation,
            "1",
            JsonSerializer.Serialize(bundle, JsonSerializerOptions.Web),
            deviceId), cancellationToken);
        if (!inserted)
            logger.LogDebug("SmartShunt outbox duplicate skipped for {SourceId} at {RecordedAt:O}", sourceId, bundle.Summary.RecordedAtUtc);
        return inserted;
    }
}
