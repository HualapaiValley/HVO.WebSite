using HVO.WebSite.v9.Models;

namespace HVO.WebSite.v9.Services;

public interface IPowerReadingIngestService
{
    Task<PowerReadingIngestResult> IngestReadingsAsync(
        IReadOnlyList<PowerReadingIngestRequest> requests,
        CancellationToken ct);
}

public sealed record PowerReadingIngestResult(
    PowerReadingBatchResponse Response,
    bool PersistenceFailed = false);
