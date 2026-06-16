namespace HVO.Edge.Outbox;

public class EdgeOutboxRecord
{
    public long Id { get; set; }
    public string SourceId { get; set; } = string.Empty;
    public string? DeviceId { get; set; }
    public string PayloadType { get; set; } = string.Empty;
    public string PayloadVersion { get; set; } = string.Empty;
    public DateTime RecordedAtUtc { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public EdgeOutboxStatus Status { get; set; } = EdgeOutboxStatus.Pending;
    public int AttemptCount { get; set; }
    public DateTime? LastAttemptedAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public DateTime NextRetryAtUtc { get; set; } = DateTime.MinValue;
    public string? LastError { get; set; }
    public EdgeOutboxFailureKind FailureKind { get; set; } = EdgeOutboxFailureKind.None;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
