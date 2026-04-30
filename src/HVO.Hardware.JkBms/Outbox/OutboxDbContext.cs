using Microsoft.EntityFrameworkCore;

namespace HVO.Hardware.JkBms.Outbox;

/// <summary>A single BMS reading queued for delivery to the web API.</summary>
public sealed class OutboxRecord
{
    public long Id { get; set; }

    /// <summary>Bluetooth MAC address of the source device.</summary>
    public string DeviceAddress { get; set; } = string.Empty;

    /// <summary>Human-readable alias of the source device.</summary>
    public string DeviceAlias { get; set; } = string.Empty;

    /// <summary>UTC timestamp of the reading (used as idempotency key per device).</summary>
    public DateTime RecordedAtUtc { get; set; }

    /// <summary>JSON-serialised <c>BmsDeviceReading</c> payload.</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>Current delivery status.</summary>
    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;

    /// <summary>Number of delivery attempts made so far.</summary>
    public int AttemptCount { get; set; }

    /// <summary>UTC time of the last delivery attempt (null if never attempted).</summary>
    public DateTime? LastAttemptedAtUtc { get; set; }

    /// <summary>UTC time when the record was successfully delivered.</summary>
    public DateTime? SentAtUtc { get; set; }

    /// <summary>Earliest UTC time for the next retry (exponential backoff).</summary>
    public DateTime NextRetryAtUtc { get; set; } = DateTime.MinValue;

    /// <summary>Last error message (if delivery failed), for diagnostics.</summary>
    public string? LastError { get; set; }

    /// <summary>UTC time this record was inserted into the outbox.</summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Delivery status of an <see cref="OutboxRecord"/>.</summary>
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
            e.HasIndex(r => new { r.DeviceAddress, r.RecordedAtUtc }).IsUnique();
            e.Property(r => r.Payload).IsRequired();
            e.Property(r => r.DeviceAddress).IsRequired();
            e.Property(r => r.DeviceAlias).IsRequired();
        });
    }
}
