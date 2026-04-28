using Microsoft.EntityFrameworkCore;

namespace HVO.Hardware.DavisVantagePro2.Outbox;

/// <summary>EF Core entity representing a single reading queued for delivery to the web API.</summary>
public sealed class OutboxRecord
{
    public long Id { get; set; }

    /// <summary>UTC timestamp of the reading (used as idempotency key).</summary>
    public DateTime RecordedAtUtc { get; set; }

    /// <summary>JSON-serialized weather reading payload.</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>Current delivery status.</summary>
    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;

    /// <summary>Number of delivery attempts made so far.</summary>
    public int AttemptCount { get; set; }

    /// <summary>UTC time of the last delivery attempt (null if never attempted).</summary>
    public DateTime? LastAttemptedAtUtc { get; set; }

    /// <summary>UTC time when the record was successfully delivered.</summary>
    public DateTime? SentAtUtc { get; set; }

    /// <summary>Earliest UTC time at which the next retry should be attempted (exponential backoff).</summary>
    public DateTime NextRetryAtUtc { get; set; } = DateTime.MinValue;

    /// <summary>Last error message (if delivery failed), for diagnostics.</summary>
    public string? LastError { get; set; }

    /// <summary>UTC time this record was created.</summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>True if this record originated from a DMPAFT archive record; false for live LOOP2 readings.</summary>
    public bool IsArchiveRecord { get; set; }
}

public enum OutboxStatus { Pending, Sent, Failed }

/// <summary>EF Core DbContext for the local SQLite outbox database.</summary>
public sealed class OutboxDbContext(DbContextOptions<OutboxDbContext> options) : DbContext(options)
{
    public DbSet<OutboxRecord> OutboxRecords => Set<OutboxRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxRecord>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).ValueGeneratedOnAdd();
            e.HasIndex(r => r.Status);
            e.HasIndex(r => r.RecordedAtUtc).IsUnique();
            e.HasIndex(r => r.IsArchiveRecord);
            e.Property(r => r.Payload).IsRequired();
        });
    }
}
