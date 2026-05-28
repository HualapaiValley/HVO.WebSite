namespace HVO.Edge.Outbox;

public sealed record EdgeOutboxMessage(
    string SourceId,
    DateTime RecordedAtUtc,
    string PayloadType,
    string PayloadVersion,
    string PayloadJson,
    string? DeviceId = null);
