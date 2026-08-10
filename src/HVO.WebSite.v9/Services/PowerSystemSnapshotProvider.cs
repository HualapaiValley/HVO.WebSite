using System.Text.Json;
using HVO.DataModels.Data;
using HVO.DataModels.Models.V9;
using HVO.Edge.Contracts.PowerSystem;
using HVO.WebSite.v9.Models;
using HVO.WebSite.v9.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HVO.WebSite.v9.Services;

public sealed class PowerSystemSnapshotProvider(
    HvoV9DbContext db,
    IOptions<PowerCompositionOptions> options,
    TimeProvider timeProvider) : IPowerSystemSnapshotProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public PowerSystemSnapshotProvider(HvoV9DbContext db)
        : this(db, Options.Create(new PowerCompositionOptions()), TimeProvider.System) { }

    public async Task<PowerSystemSnapshot?> GetLatestAsync(int lookbackMinutes = 60, CancellationToken ct = default)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var cutoffUtc = nowUtc.AddMinutes(-lookbackMinutes);
        var futureCutoffUtc = nowUtc.AddSeconds(options.Value.MaxFutureClockSkewSeconds);
        var latestReadingIds = await db.PowerReadings.AsNoTracking()
            .Where(r => r.RecordedAt >= cutoffUtc && r.RecordedAt <= futureCutoffUtc)
            .GroupBy(r => new { r.SourceId, r.DeviceId })
            .Select(group => group.OrderByDescending(r => r.RecordedAt).ThenByDescending(r => r.Id).Select(r => r.Id).First())
            .ToArrayAsync(ct);
        var readings = latestReadingIds.Length == 0 ? [] : await db.PowerReadings.AsNoTracking()
            .Where(r => latestReadingIds.Contains(r.Id)).ToArrayAsync(ct);

        var latestBmsReadingIds = await db.BmsReadings
                .AsNoTracking()
                .Where(r => r.RecordedAt >= cutoffUtc && r.RecordedAt <= futureCutoffUtc)
                .GroupBy(r => r.DeviceId)
                .Select(g => g
                    .OrderByDescending(r => r.RecordedAt)
                    .ThenByDescending(r => r.Id)
                    .Select(r => r.Id)
                    .First())
                .ToArrayAsync(ct);

        var bmsReadings = latestBmsReadingIds.Length == 0
            ? []
            : await db.BmsReadings
                .AsNoTrackingWithIdentityResolution()
                .Include(r => r.Device)
                .Include(r => r.CellVoltages)
                .Where(r => latestBmsReadingIds.Contains(r.Id))
                .ToArrayAsync(ct);

        var latestMpptDetailIds = await db.PowerMpptDetailSnapshots
            .AsNoTracking()
            .Where(r => r.RecordedAt >= cutoffUtc && r.RecordedAt <= futureCutoffUtc)
            .GroupBy(r => r.SourceId)
            .Select(group => group.OrderByDescending(r => r.RecordedAt).ThenByDescending(r => r.Id)
                .Select(r => r.Id).First())
            .ToArrayAsync(ct);
        var mpptDetailRows = latestMpptDetailIds.Length == 0
            ? []
            : await db.PowerMpptDetailSnapshots.AsNoTracking()
                .Where(r => latestMpptDetailIds.Contains(r.Id))
                .ToArrayAsync(ct);
        var mpptDetails = mpptDetailRows
            .Select(row => JsonSerializer.Deserialize<PowerMpptDetailPayload>(row.PayloadJson, JsonOptions))
            .OfType<PowerMpptDetailPayload>()
            .ToArray();

        return readings.Length == 0 && bmsReadings.Length == 0 && mpptDetails.Length == 0
            ? null
            : PowerSystemSnapshotComposer.Compose(readings, nowUtc, bmsReadings, options.Value, mpptDetails);
    }
}
