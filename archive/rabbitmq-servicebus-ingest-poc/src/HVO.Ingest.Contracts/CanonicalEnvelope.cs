using System.ComponentModel.DataAnnotations;

namespace HVO.Ingest.Contracts;

public sealed record CanonicalEnvelope<TPayload>
{
    [Required]
    public required string MessageId { get; init; }

    [Required]
    public required string Schema { get; init; }

    [Required]
    public required string SiteId { get; init; }

    [Required]
    public required string Source { get; init; }

    [Required]
    public required string DeviceExternalId { get; init; }

    public DateTimeOffset ObservedAtUtc { get; init; }

    public DateTimeOffset PublishedAtUtc { get; init; }

    [Required]
    public required TPayload Payload { get; init; }
}
