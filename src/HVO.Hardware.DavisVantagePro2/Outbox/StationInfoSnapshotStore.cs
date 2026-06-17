using HVO.Hardware.DavisVantagePro2.Station.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HVO.Hardware.DavisVantagePro2.Outbox;

public sealed class StationInfoSnapshotStore(IServiceScopeFactory scopeFactory)
{
    private const int SnapshotId = 1;

    public async Task<StoredStationInfoSnapshot?> GetAsync(CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DavisLocalDbContext>();

        var entity = await db.StationInfoSnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(snapshot => snapshot.Id == SnapshotId, ct);

        return entity is null
            ? null
            : new StoredStationInfoSnapshot(entity.ToStationInfo(), entity.SavedAtUtc);
    }

    public async Task<StoredStationInfoSnapshot> SaveAsync(StationInfo stationInfo, CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DavisLocalDbContext>();

        var entity = await db.StationInfoSnapshots
            .SingleOrDefaultAsync(snapshot => snapshot.Id == SnapshotId, ct)
            ?? new StationInfoSnapshotEntity { Id = SnapshotId };

        DateTime savedAtUtc = DateTime.UtcNow;
        entity.Apply(stationInfo, savedAtUtc);

        if (db.Entry(entity).State == EntityState.Detached)
        {
            db.StationInfoSnapshots.Add(entity);
        }

        await db.SaveChangesAsync(ct);
        return new StoredStationInfoSnapshot(entity.ToStationInfo(), entity.SavedAtUtc);
    }
}

public sealed record StoredStationInfoSnapshot(StationInfo StationInfo, DateTime SavedAtUtc);
