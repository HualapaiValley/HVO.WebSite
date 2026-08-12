using Microsoft.EntityFrameworkCore;

namespace HVO.Hardware.DavisVantagePro2.Outbox;

public interface IDavisArchiveCursorStore
{
    Task<DavisArchiveCursor?> GetAsync(string stationId, CancellationToken cancellationToken);
    Task<bool> AdvanceAsync(string stationId, DateTime consoleRecordedAtLocal, DateTime recordedAtUtc, CancellationToken cancellationToken);
}

public sealed class DavisArchiveCursorStore(IServiceScopeFactory scopeFactory, TimeProvider timeProvider) : IDavisArchiveCursorStore
{
    public async Task<DavisArchiveCursor?> GetAsync(string stationId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var row = await scope.ServiceProvider.GetRequiredService<DavisLocalDbContext>().ArchiveCursors
            .AsNoTracking()
            .SingleOrDefaultAsync(cursor => cursor.StationId == stationId, cancellationToken);
        return row is null ? null : new(
            row.StationId,
            DateTime.SpecifyKind(row.ConsoleRecordedAtLocal, DateTimeKind.Unspecified),
            DateTime.SpecifyKind(row.RecordedAtUtc, DateTimeKind.Utc),
            DateTime.SpecifyKind(row.UpdatedAtUtc, DateTimeKind.Utc));
    }

    public async Task<bool> AdvanceAsync(
        string stationId,
        DateTime consoleRecordedAtLocal,
        DateTime recordedAtUtc,
        CancellationToken cancellationToken)
    {
        var local = DateTime.SpecifyKind(consoleRecordedAtLocal, DateTimeKind.Unspecified);
        var utc = DateTime.SpecifyKind(recordedAtUtc, DateTimeKind.Utc);
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DavisLocalDbContext>();
        var row = await db.ArchiveCursors.SingleOrDefaultAsync(cursor => cursor.StationId == stationId, cancellationToken);
        if (row is not null && (row.ConsoleRecordedAtLocal > local
            || row.ConsoleRecordedAtLocal == local && row.RecordedAtUtc >= utc))
            return false;
        row ??= new DavisArchiveCursorEntity { StationId = stationId };
        row.ConsoleRecordedAtLocal = local;
        row.RecordedAtUtc = utc;
        row.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        if (db.Entry(row).State == EntityState.Detached)
            db.ArchiveCursors.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public sealed record DavisArchiveCursor(
    string StationId,
    DateTime ConsoleRecordedAtLocal,
    DateTime RecordedAtUtc,
    DateTime UpdatedAtUtc);
