using HVO.Edge.Contracts.PowerSystem;

namespace HVO.WebSite.v9.Services;

public interface IPowerSystemSnapshotProvider
{
    Task<PowerSystemSnapshot?> GetLatestAsync(int lookbackMinutes = 60, CancellationToken ct = default);
}
