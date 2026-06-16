using HVO.Edge.Outbox;

namespace HVO.Hardware.JkBms.Outbox.Forwarders;

/// <summary>
/// A forwarder delivers a batch of outbox records to an external destination.
///
/// Implementations must be idempotent — the same batch may be presented more than
/// once (on retry). On success the implementation should simply return; on failure
/// it should throw so the coordinator can apply back-off and retry logic.
/// </summary>
public interface IReadingForwarder
{
    /// <summary>Human-readable name used in logs and diagnostics.</summary>
    string Name { get; }

    /// <summary>
    /// Deliver <paramref name="batch"/> to the external destination.
    /// </summary>
    /// <exception cref="Exception">Any exception indicates delivery failure.</exception>
    Task ForwardAsync(IReadOnlyList<EdgeOutboxRecord> batch, CancellationToken ct);
}
