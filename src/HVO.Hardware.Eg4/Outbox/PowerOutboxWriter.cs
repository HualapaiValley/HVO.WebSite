using System.Text.Json;
using System.Text.Json.Serialization;
using HVO.Edge.Contracts;
using HVO.Edge.Contracts.PowerSystem;
using HVO.Edge.Outbox;

namespace HVO.Hardware.Eg4.Outbox;

public sealed record Eg4ObservationBundle(
    PowerReadingPayload Reading,
    PowerMpptDetailPayload? MpptDetail = null,
    PowerInverterDetailPayload? InverterDetail = null);

public interface IEg4PowerOutboxWriter
{
    Task<bool> EnqueueAsync(Eg4ObservationBundle bundle, CancellationToken cancellationToken);
}

public sealed class PowerOutboxWriter(
    EdgeOutboxStore<DefaultEdgeOutboxDbContext> store,
    ILogger<PowerOutboxWriter> logger) : IEg4PowerOutboxWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<bool> EnqueueAsync(Eg4ObservationBundle bundle, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(bundle.Reading);
        var sourceId = bundle.Reading.SourceId?.Trim() ?? string.Empty;
        var deviceId = bundle.Reading.DeviceId?.Trim() ?? string.Empty;
        if (sourceId.Length == 0 || deviceId.Length == 0)
            throw new InvalidOperationException("EG4 observations require stable source and device identities.");
        if (bundle.Reading.RecordedAtUtc == default || bundle.Reading.RecordedAtUtc.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("EG4 observations require a UTC RecordedAtUtc value.");
        ValidateDetailIdentity(sourceId, deviceId, bundle.Reading.RecordedAtUtc, bundle.MpptDetail);
        ValidateDetailIdentity(sourceId, deviceId, bundle.Reading.RecordedAtUtc, bundle.InverterDetail);

        var inserted = await store.EnqueueAsync(new EdgeOutboxMessage(
            sourceId,
            bundle.Reading.RecordedAtUtc,
            EdgePayloadTypes.Eg4Observation,
            "1",
            JsonSerializer.Serialize(bundle, JsonOptions),
            deviceId), cancellationToken);
        if (!inserted)
            logger.LogDebug("EG4 outbox duplicate skipped for {SourceId} at {RecordedAt:O}", sourceId, bundle.Reading.RecordedAtUtc);
        return inserted;
    }

    private static void ValidateDetailIdentity<T>(string sourceId, string deviceId, DateTime recordedAtUtc, T? detail)
    {
        if (detail is null)
            return;
        var identity = detail switch
        {
            PowerMpptDetailPayload mppt => (mppt.SourceId, mppt.DeviceId, mppt.RecordedAtUtc),
            PowerInverterDetailPayload inverter => (inverter.SourceId, inverter.DeviceId, inverter.RecordedAtUtc),
            _ => throw new InvalidOperationException("Unsupported EG4 detail payload.")
        };
        if (!string.Equals(identity.SourceId, sourceId, StringComparison.Ordinal)
            || !string.Equals(identity.DeviceId, deviceId, StringComparison.Ordinal)
            || identity.RecordedAtUtc != recordedAtUtc)
            throw new InvalidOperationException("EG4 detail identity must match its power reading.");
    }
}
