using Microsoft.EntityFrameworkCore;

namespace HVO.Gateway.SolarAssistant.Outbox;

/// <summary>A power reading queued for delivery to the HVO website API.</summary>
public sealed class OutboxRecord
{
    public long Id { get; set; }

    /// <summary>Stable source id sent to the website API.</summary>
    public string SourceId { get; set; } = string.Empty;

    /// <summary>Optional logical device id for diagnostics.</summary>
    public string? DeviceId { get; set; }

    /// <summary>UTC timestamp of the snapshot. Used with SourceId as the website idempotency key.</summary>
    public DateTime RecordedAtUtc { get; set; }

    /// <summary>JSON-serialized PowerReadingPayload.</summary>
    public string Payload { get; set; } = string.Empty;

    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;
    public int AttemptCount { get; set; }
    public DateTime? LastAttemptedAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public DateTime NextRetryAtUtc { get; set; } = DateTime.MinValue;
    public string? LastError { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public enum OutboxStatus { Pending, Sent, Failed }

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
            e.HasIndex(r => new { r.SourceId, r.RecordedAtUtc }).IsUnique();
            e.Property(r => r.SourceId).IsRequired();
            e.Property(r => r.Payload).IsRequired();
        });
    }
}
