using Microsoft.EntityFrameworkCore;
using HVO.Hardware.DavisVantagePro2.Station.Models;

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

public sealed class StationSettingsSnapshotEntity
{
    public int Id { get; set; }
    public DateTime SavedAtUtc { get; set; }
    public int ArchiveIntervalSeconds { get; set; }
    public double? LatitudeDegrees { get; set; }
    public double? LongitudeDegrees { get; set; }
    public double? AltitudeFeet { get; set; }
    public int RainYearStartMonth { get; set; }
    public int RainBucketType { get; set; }
    public string DstSetting { get; set; } = string.Empty;
    public bool UseTimezoneCode { get; set; }
    public int TimezoneCode { get; set; }
    public double GmtOffsetHours { get; set; }
    public string TemperatureLogging { get; set; } = string.Empty;
    public string BarometerUnits { get; set; } = string.Empty;
    public string TemperatureUnits { get; set; } = string.Empty;
    public string RainUnits { get; set; } = string.Empty;
    public string WindUnits { get; set; } = string.Empty;

    public StationSettings ToStationSettings() => new()
    {
        ArchiveIntervalSeconds = ArchiveIntervalSeconds,
        LatitudeDegrees = LatitudeDegrees,
        LongitudeDegrees = LongitudeDegrees,
        AltitudeFeet = AltitudeFeet,
        RainYearStartMonth = RainYearStartMonth,
        RainBucketType = RainBucketType,
        DstSetting = DstSetting,
        UseTimezoneCode = UseTimezoneCode,
        TimezoneCode = TimezoneCode,
        GmtOffsetHours = GmtOffsetHours,
        TemperatureLogging = TemperatureLogging,
        BarometerUnits = BarometerUnits,
        TemperatureUnits = TemperatureUnits,
        RainUnits = RainUnits,
        WindUnits = WindUnits,
    };

    public void Apply(StationSettings settings, DateTime savedAtUtc)
    {
        SavedAtUtc = savedAtUtc;
        ArchiveIntervalSeconds = settings.ArchiveIntervalSeconds;
        LatitudeDegrees = settings.LatitudeDegrees;
        LongitudeDegrees = settings.LongitudeDegrees;
        AltitudeFeet = settings.AltitudeFeet;
        RainYearStartMonth = settings.RainYearStartMonth;
        RainBucketType = settings.RainBucketType;
        DstSetting = settings.DstSetting;
        UseTimezoneCode = settings.UseTimezoneCode;
        TimezoneCode = settings.TimezoneCode;
        GmtOffsetHours = settings.GmtOffsetHours;
        TemperatureLogging = settings.TemperatureLogging;
        BarometerUnits = settings.BarometerUnits;
        TemperatureUnits = settings.TemperatureUnits;
        RainUnits = settings.RainUnits;
        WindUnits = settings.WindUnits;
    }
}

/// <summary>EF Core DbContext for the local SQLite outbox database.</summary>
public sealed class OutboxDbContext(DbContextOptions<OutboxDbContext> options) : DbContext(options)
{
    public DbSet<OutboxRecord> OutboxRecords => Set<OutboxRecord>();
    public DbSet<StationSettingsSnapshotEntity> StationSettingsSnapshots => Set<StationSettingsSnapshotEntity>();

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

        modelBuilder.Entity<StationSettingsSnapshotEntity>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).ValueGeneratedNever();
            e.Property(r => r.SavedAtUtc).IsRequired();
            e.Property(r => r.DstSetting).IsRequired();
            e.Property(r => r.TemperatureLogging).IsRequired();
            e.Property(r => r.BarometerUnits).IsRequired();
            e.Property(r => r.TemperatureUnits).IsRequired();
            e.Property(r => r.RainUnits).IsRequired();
            e.Property(r => r.WindUnits).IsRequired();
        });
    }
}
