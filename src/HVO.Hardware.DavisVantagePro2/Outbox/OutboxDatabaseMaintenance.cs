using Microsoft.EntityFrameworkCore;

namespace HVO.Hardware.DavisVantagePro2.Outbox;

internal static class OutboxDatabaseMaintenance
{
    public static async Task EnsureFailureKindAndRequeueRetryableFailuresAsync(OutboxDbContext db, CancellationToken ct = default)
    {
        if (!await OutboxFailureKindColumnExistsAsync(db, ct))
        {
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE OutboxRecords ADD COLUMN FailureKind INTEGER NOT NULL DEFAULT 0;",
                ct);
        }

        await db.Database.ExecuteSqlRawAsync(
            @"UPDATE OutboxRecords
              SET Status = 0,
                  FailureKind = 0,
                  NextRetryAtUtc = '0001-01-01 00:00:00',
                  LastError = CASE
                      WHEN LastError IS NULL OR LastError = '' THEN 'Requeued historical failed outbox record after startup retry classification was added.'
                      ELSE LastError || ' Requeued after startup retry classification was added.'
                  END
              WHERE Status = 2 AND FailureKind IN (0, 1);",
            ct);
    }

    private static async Task<bool> OutboxFailureKindColumnExistsAsync(OutboxDbContext db, CancellationToken ct)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA table_info(OutboxRecords);";

        if (command.Connection!.State != System.Data.ConnectionState.Open)
        {
            await command.Connection.OpenAsync(ct);
        }

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (string.Equals(reader.GetString(1), nameof(OutboxRecord.FailureKind), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
