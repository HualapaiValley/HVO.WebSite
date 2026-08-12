using HVO.Hardware.DavisVantagePro2.Station.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HVO.Hardware.DavisVantagePro2.Outbox;

public interface IStationSettingsSnapshotStore
{
    Task<StoredStationSettingsSnapshot?> GetAsync(CancellationToken cancellationToken = default);
    Task<StoredStationSettingsSnapshot> SaveAsync(Station.Models.StationSettings settings, CancellationToken cancellationToken = default);
}

public sealed class StationSettingsSnapshotStore(
    IServiceScopeFactory scopeFactory) : IStationSettingsSnapshotStore
{
    private const int SnapshotId = 1;

    public async Task<StoredStationSettingsSnapshot?> GetAsync(CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DavisLocalDbContext>();

        var entity = await db.StationSettingsSnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(snapshot => snapshot.Id == SnapshotId, ct);

        return entity is null
            ? null
            : new StoredStationSettingsSnapshot(entity.ToStationSettings(), entity.SavedAtUtc);
    }

    public async Task<StoredStationSettingsSnapshot> SaveAsync(StationSettings settings, CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DavisLocalDbContext>();

        var entity = await db.StationSettingsSnapshots
            .SingleOrDefaultAsync(snapshot => snapshot.Id == SnapshotId, ct)
            ?? new StationSettingsSnapshotEntity { Id = SnapshotId };

        DateTime savedAtUtc = DateTime.UtcNow;
        entity.Apply(settings, savedAtUtc);

        if (db.Entry(entity).State == EntityState.Detached)
        {
            db.StationSettingsSnapshots.Add(entity);
        }

        await db.SaveChangesAsync(ct);

        return new StoredStationSettingsSnapshot(entity.ToStationSettings(), entity.SavedAtUtc);
    }
}

public sealed record StoredStationSettingsSnapshot(StationSettings Settings, DateTime SavedAtUtc);
