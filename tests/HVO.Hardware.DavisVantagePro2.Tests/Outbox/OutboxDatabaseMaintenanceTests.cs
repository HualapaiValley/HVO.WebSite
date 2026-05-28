using HVO.Hardware.DavisVantagePro2.Outbox;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HVO.Hardware.DavisVantagePro2.Tests.Outbox;

[TestClass]
public class OutboxDatabaseMaintenanceTests
{
    [TestMethod]
    public async Task EnsureFailureKindAndRequeueRetryableFailuresAsync_AddsMissingColumnAndRequeuesExistingFailedRows()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = CreateOptions(connection);

        await using (var db = new OutboxDbContext(options))
        {
            await db.Database.ExecuteSqlRawAsync(
                @"CREATE TABLE OutboxRecords (
                    Id INTEGER NOT NULL CONSTRAINT PK_OutboxRecords PRIMARY KEY AUTOINCREMENT,
                    RecordedAtUtc TEXT NOT NULL,
                    Payload TEXT NOT NULL,
                    Status INTEGER NOT NULL,
                    AttemptCount INTEGER NOT NULL,
                    LastAttemptedAtUtc TEXT NULL,
                    SentAtUtc TEXT NULL,
                    NextRetryAtUtc TEXT NOT NULL,
                    LastError TEXT NULL,
                    CreatedAtUtc TEXT NOT NULL,
                    IsArchiveRecord INTEGER NOT NULL
                );");
            await db.Database.ExecuteSqlRawAsync(
                @"INSERT INTO OutboxRecords
                  (RecordedAtUtc, Payload, Status, AttemptCount, NextRetryAtUtc, LastError, CreatedAtUtc, IsArchiveRecord)
                  VALUES ('2026-05-28 22:00:00', '{{}}', 2, 10, '2026-05-28 23:00:00', 'HTTP 503', '2026-05-28 22:00:00', 0);");

            await OutboxDatabaseMaintenance.EnsureFailureKindAndRequeueRetryableFailuresAsync(db);
        }

        await using (var db = new OutboxDbContext(options))
        {
            var row = await db.OutboxRecords.SingleAsync();
            row.Status.Should().Be(OutboxStatus.Pending);
            row.FailureKind.Should().Be(OutboxFailureKind.None);
            row.NextRetryAtUtc.Should().Be(DateTime.MinValue);
            row.LastError.Should().Contain("HTTP 503");
            row.LastError.Should().Contain("Requeued after startup retry classification was added.");
        }
    }

    [TestMethod]
    public async Task EnsureFailureKindAndRequeueRetryableFailuresAsync_RequeuesTransientAndPreservesPermanentFailures()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = CreateOptions(connection);

        await using (var db = new OutboxDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            db.OutboxRecords.AddRange(
                CreateFailedRecord(OutboxFailureKind.None, "unclassified"),
                CreateFailedRecord(OutboxFailureKind.TransientExhausted, "cloud down"),
                CreateFailedRecord(OutboxFailureKind.ApiValidation, "bad format"),
                CreateFailedRecord(OutboxFailureKind.InvalidPayload, "invalid json"));
            await db.SaveChangesAsync();

            await OutboxDatabaseMaintenance.EnsureFailureKindAndRequeueRetryableFailuresAsync(db);
        }

        await using (var db = new OutboxDbContext(options))
        {
            var rows = await db.OutboxRecords.OrderBy(r => r.Id).ToListAsync();

            rows[0].Status.Should().Be(OutboxStatus.Pending);
            rows[0].FailureKind.Should().Be(OutboxFailureKind.None);
            rows[0].LastError.Should().Contain("Requeued after startup retry classification was added.");

            rows[1].Status.Should().Be(OutboxStatus.Pending);
            rows[1].FailureKind.Should().Be(OutboxFailureKind.None);
            rows[1].LastError.Should().Contain("Requeued after startup retry classification was added.");

            rows[2].Status.Should().Be(OutboxStatus.Failed);
            rows[2].FailureKind.Should().Be(OutboxFailureKind.ApiValidation);
            rows[2].LastError.Should().Be("bad format");

            rows[3].Status.Should().Be(OutboxStatus.Failed);
            rows[3].FailureKind.Should().Be(OutboxFailureKind.InvalidPayload);
            rows[3].LastError.Should().Be("invalid json");
        }
    }

    private static DbContextOptions<OutboxDbContext> CreateOptions(SqliteConnection connection) =>
        new DbContextOptionsBuilder<OutboxDbContext>()
            .UseSqlite(connection)
            .Options;

    private static OutboxRecord CreateFailedRecord(OutboxFailureKind failureKind, string lastError) => new()
    {
        RecordedAtUtc = new DateTime(2026, 5, 28, 22, 0, 0, DateTimeKind.Utc).AddMinutes((int)failureKind),
        Payload = "{}",
        Status = OutboxStatus.Failed,
        AttemptCount = 10,
        NextRetryAtUtc = new DateTime(2026, 5, 28, 23, 0, 0, DateTimeKind.Utc),
        LastError = lastError,
        FailureKind = failureKind,
        CreatedAtUtc = new DateTime(2026, 5, 28, 22, 0, 0, DateTimeKind.Utc),
    };
}
