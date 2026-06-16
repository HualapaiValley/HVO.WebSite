using HVO.WebSite.v9.Models;

namespace HVO.WebSite.v9.Services;

public interface IBmsIngestService
{
    Task<BmsIngestBatchResponse> IngestReadingsAsync(
        IReadOnlyList<BmsIngestRequest> requests,
        CancellationToken ct);
}
