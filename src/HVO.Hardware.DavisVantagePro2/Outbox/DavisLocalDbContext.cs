using HVO.Hardware.DavisVantagePro2.Station.Models;
using Microsoft.EntityFrameworkCore;

namespace HVO.Hardware.DavisVantagePro2.Outbox;

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

public sealed class StationInfoSnapshotEntity
{
    public int Id { get; set; }
    public DateTime SavedAtUtc { get; set; }
    public string HardwareName { get; set; } = string.Empty;
    public int HardwareType { get; set; }
    public int ModelType { get; set; }
    public string FirmwareVersion { get; set; } = string.Empty;
    public string FirmwareDate { get; set; } = string.Empty;
    public DateTime ConsoleTime { get; set; }

    public StationInfo ToStationInfo() => new()
    {
        HardwareName = HardwareName,
        HardwareType = HardwareType,
        ModelType = ModelType,
        FirmwareVersion = FirmwareVersion,
        FirmwareDate = FirmwareDate,
        ConsoleTime = ConsoleTime,
    };

    public void Apply(StationInfo stationInfo, DateTime savedAtUtc)
    {
        SavedAtUtc = savedAtUtc;
        HardwareName = stationInfo.HardwareName;
        HardwareType = stationInfo.HardwareType;
        ModelType = stationInfo.ModelType;
        FirmwareVersion = stationInfo.FirmwareVersion;
        FirmwareDate = stationInfo.FirmwareDate;
        ConsoleTime = stationInfo.ConsoleTime;
    }
}

public sealed class DavisArchiveCursorEntity
{
    public string StationId { get; set; } = string.Empty;
    public DateTime ConsoleRecordedAtLocal { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class DavisLocalDbContext(DbContextOptions<DavisLocalDbContext> options) : DbContext(options)
{
    public DbSet<StationSettingsSnapshotEntity> StationSettingsSnapshots => Set<StationSettingsSnapshotEntity>();
    public DbSet<StationInfoSnapshotEntity> StationInfoSnapshots => Set<StationInfoSnapshotEntity>();
    public DbSet<DavisArchiveCursorEntity> ArchiveCursors => Set<DavisArchiveCursorEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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

        modelBuilder.Entity<StationInfoSnapshotEntity>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).ValueGeneratedNever();
            e.Property(r => r.SavedAtUtc).IsRequired();
            e.Property(r => r.HardwareName).IsRequired();
            e.Property(r => r.FirmwareVersion).IsRequired();
            e.Property(r => r.FirmwareDate).IsRequired();
        });

        modelBuilder.Entity<DavisArchiveCursorEntity>(e =>
        {
            e.HasKey(r => r.StationId);
            e.Property(r => r.StationId).HasMaxLength(64);
            e.Property(r => r.ConsoleRecordedAtLocal).IsRequired();
            e.Property(r => r.RecordedAtUtc).IsRequired();
            e.Property(r => r.UpdatedAtUtc).IsRequired();
        });
    }
}
