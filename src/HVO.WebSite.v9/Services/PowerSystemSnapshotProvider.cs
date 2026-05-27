using HVO.DataModels.Data;
using HVO.DataModels.Models.V9;
using HVO.Edge.Contracts.PowerSystem;
using HVO.WebSite.v9.Models;
using Microsoft.EntityFrameworkCore;

namespace HVO.WebSite.v9.Services;

public sealed class PowerSystemSnapshotProvider(HvoV9DbContext db) : IPowerSystemSnapshotProvider
{
    private static readonly string[] SourceSystems = ["solarassistant", "victron-smartshunt"];

    public async Task<PowerSystemSnapshot?> GetLatestAsync(int lookbackMinutes = 60, CancellationToken ct = default)
    {
        var nowUtc = DateTime.UtcNow;
        var cutoffUtc = nowUtc.AddMinutes(-lookbackMinutes);
        var readings = new List<PowerReading>(SourceSystems.Length);

        foreach (var sourceSystem in SourceSystems)
        {
            var reading = await db.PowerReadings
                .AsNoTracking()
                .Where(r => r.RecordedAt >= cutoffUtc && r.SourceSystem == sourceSystem)
                .OrderByDescending(r => r.RecordedAt)
                .ThenByDescending(r => r.Id)
                .FirstOrDefaultAsync(ct);

            if (reading is not null)
                readings.Add(reading);
        }

        return readings.Count == 0
            ? null
            : PowerSystemSnapshotComposer.Compose(readings, nowUtc);
    }
}
