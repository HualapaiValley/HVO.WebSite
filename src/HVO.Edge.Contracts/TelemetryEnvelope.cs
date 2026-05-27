namespace HVO.Edge.Contracts;

public sealed record TelemetryEnvelope<TPayload>(
    string SourceId,
    DateTime RecordedAtUtc,
    string PayloadType,
    string PayloadVersion,
    TPayload Payload,
    string? DeviceId = null);
