using Microsoft.EntityFrameworkCore;

namespace HVO.Hardware.DavisVantagePro2.Outbox;

internal static class DavisLocalDatabaseInitializer
{
    public static async Task EnsureCreatedAsync(DavisLocalDbContext db, CancellationToken cancellationToken = default)
    {
        await db.Database.EnsureCreatedAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS ArchiveCursors (
                StationId TEXT NOT NULL CONSTRAINT PK_ArchiveCursors PRIMARY KEY,
                ConsoleRecordedAtLocal TEXT NOT NULL,
                RecordedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            )
            """, cancellationToken);
    }
}
