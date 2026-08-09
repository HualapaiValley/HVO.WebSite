using HVO.Edge.Contracts.PowerSystem;
using HVO.WebSite.v9.Models;

namespace HVO.WebSite.v9.Services;

public interface IPowerReadingIngestService
{
    Task<PowerReadingIngestResult> IngestReadingsAsync(
        IReadOnlyList<PowerReadingPayload> requests,
        CancellationToken ct);
}

public sealed record PowerReadingIngestResult(
    PowerReadingBatchResponse Response,
    bool PersistenceFailed = false);
