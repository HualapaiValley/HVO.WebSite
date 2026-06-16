using System.Text.Json;
using HVO.Hardware.DavisVantagePro2.Configuration;
using HVO.Hardware.DavisVantagePro2.Station.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.DavisVantagePro2.Outbox;

public sealed class StationSettingsSnapshotStore(
    IServiceScopeFactory scopeFactory,
    IOptions<StationOptions> stationOptions)
{
    private const int SnapshotId = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly StationOptions _stationOptions = stationOptions.Value;

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

        var writer = scope.ServiceProvider.GetRequiredService<DavisOutboxWriter>();
        var payload = new
        {
            StationId = _stationOptions.StationId,
            RecordedAt = savedAtUtc,
            Settings = entity.ToStationSettings(),
        };
        await writer.EnqueueConfigAsync(_stationOptions.StationId, JsonSerializer.Serialize(payload, JsonOptions), ct);

        return new StoredStationSettingsSnapshot(entity.ToStationSettings(), entity.SavedAtUtc);
    }
}

public sealed record StoredStationSettingsSnapshot(StationSettings Settings, DateTime SavedAtUtc);
