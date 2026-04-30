using HVO.DataModels.Models.V9;
using Microsoft.EntityFrameworkCore;

namespace HVO.DataModels.Data;

public class HvoV9DbContext : DbContext
{
    public HvoV9DbContext(DbContextOptions<HvoV9DbContext> options)
        : base(options)
    {
    }

    public DbSet<WeatherRaw> WeatherRaw { get; set; }

    public DbSet<WeatherMinute> WeatherMinute { get; set; }

    public DbSet<WeatherHourly> WeatherHourly { get; set; }

    public DbSet<ImageMetadata> ImageMetadata { get; set; }

    public DbSet<AlertLog> AlertLog { get; set; }

    public DbSet<SiteConfiguration> SiteConfiguration { get; set; }

    public DbSet<ApiKey> ApiKeys { get; set; }

    public DbSet<ApiKeyClaim> ApiKeyClaims { get; set; }

    public DbSet<ApiKeyOwner> ApiKeyOwners { get; set; }

    // ── BMS ──────────────────────────────────────────────────────────────────

    public DbSet<BmsSite> BmsSites { get; set; }

    public DbSet<BmsDevice> BmsDevices { get; set; }

    public DbSet<BmsDeviceConfig> BmsDeviceConfigs { get; set; }

    public DbSet<BmsDeviceInfo> BmsDeviceInfos { get; set; }

    public DbSet<BmsReading> BmsReadings { get; set; }

    public DbSet<BmsCellVoltage> BmsCellVoltages { get; set; }

    public DbSet<BmsCellResistance> BmsCellResistances { get; set; }

    public DbSet<BmsAlarm> BmsAlarms { get; set; }

    public DbSet<BmsReadingMinute> BmsReadingsMinute { get; set; }

    public DbSet<BmsReadingHourly> BmsReadingsHourly { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("v9");

        modelBuilder.Entity<WeatherRaw>(entity =>
        {
            entity.HasIndex(e => e.RecordedAt);
            entity.HasIndex(e => new { e.StationId, e.RecordedAt }).IsUnique();
            entity.Property(e => e.RecordedAt).HasColumnType("datetime2");
        });

        modelBuilder.Entity<WeatherMinute>(entity =>
        {
            entity.HasIndex(e => e.PeriodStart).IsUnique();
            entity.HasIndex(e => new { e.StationId, e.PeriodStart });
            entity.Property(e => e.PeriodStart).HasColumnType("datetime2");
        });

        modelBuilder.Entity<WeatherHourly>(entity =>
        {
            entity.HasIndex(e => e.PeriodStart).IsUnique();
            entity.HasIndex(e => new { e.StationId, e.PeriodStart });
            entity.Property(e => e.PeriodStart).HasColumnType("datetime2");
        });

        modelBuilder.Entity<ImageMetadata>(entity =>
        {
            entity.HasIndex(e => e.CapturedAt);
            entity.HasIndex(e => new { e.CameraId, e.CapturedAt });
            entity.Property(e => e.CapturedAt).HasColumnType("datetime2");
        });

        modelBuilder.Entity<AlertLog>(entity =>
        {
            entity.HasIndex(e => e.OccurredAt);
            entity.HasIndex(e => e.Acknowledged);
            entity.Property(e => e.OccurredAt).HasColumnType("datetime2");
            entity.Property(e => e.AcknowledgedAt).HasColumnType("datetime2");
        });

        modelBuilder.Entity<SiteConfiguration>(entity =>
        {
            entity.Property(e => e.UpdatedAt).HasColumnType("datetime2");
        });

        modelBuilder.Entity<ApiKeyOwner>(entity =>
        {
            entity.HasIndex(e => e.EntraObjectId).IsUnique();
            entity.Property(e => e.CreatedAt).HasColumnType("datetime2");
        });

        modelBuilder.Entity<ApiKey>(entity =>
        {
            entity.HasIndex(e => e.KeyHash).IsUnique();
            entity.HasIndex(e => e.IsActive);
            entity.Property(e => e.CreatedAt).HasColumnType("datetime2");
            entity.Property(e => e.ExpiresAt).HasColumnType("datetime2");
            entity.Property(e => e.LastUsedAt).HasColumnType("datetime2");
            entity.Property(e => e.Type).HasConversion<int>();
            entity.HasOne(e => e.Owner)
                  .WithMany(o => o.ApiKeys)
                  .HasForeignKey(e => e.OwnerId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ApiKeyClaim>(entity =>
        {
            entity.HasIndex(e => new { e.ApiKeyId, e.ClaimType });
            entity.HasOne(e => e.ApiKey)
                  .WithMany(k => k.Claims)
                  .HasForeignKey(e => e.ApiKeyId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── BMS ──────────────────────────────────────────────────────────────

        modelBuilder.Entity<BmsSite>(entity =>
        {
            entity.HasIndex(e => e.Name).IsUnique();
        });

        modelBuilder.Entity<BmsDevice>(entity =>
        {
            entity.HasIndex(e => e.Address).IsUnique();
            entity.Property(e => e.FirstSeenAt).HasColumnType("datetime2");
            entity.HasOne(e => e.Site)
                  .WithMany(s => s.Devices)
                  .HasForeignKey(e => e.SiteId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<BmsDeviceConfig>(entity =>
        {
            entity.HasIndex(e => new { e.DeviceId, e.RecordedAt }).IsUnique();
            entity.Property(e => e.RecordedAt).HasColumnType("datetime2");
            entity.HasOne(e => e.Device)
                  .WithMany(d => d.Configs)
                  .HasForeignKey(e => e.DeviceId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BmsDeviceInfo>(entity =>
        {
            entity.HasIndex(e => new { e.DeviceId, e.RecordedAt }).IsUnique();
            entity.Property(e => e.RecordedAt).HasColumnType("datetime2");
            entity.HasOne(e => e.Device)
                  .WithMany(d => d.DeviceInfos)
                  .HasForeignKey(e => e.DeviceId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BmsReading>(entity =>
        {
            entity.HasIndex(e => e.RecordedAt);
            entity.HasIndex(e => new { e.DeviceId, e.RecordedAt }).IsUnique();
            entity.Property(e => e.RecordedAt).HasColumnType("datetime2");
            entity.HasOne(e => e.Device)
                  .WithMany(d => d.Readings)
                  .HasForeignKey(e => e.DeviceId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BmsCellVoltage>(entity =>
        {
            entity.HasKey(e => new { e.ReadingId, e.CellIndex });
            entity.HasOne(e => e.Reading)
                  .WithMany(r => r.CellVoltages)
                  .HasForeignKey(e => e.ReadingId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BmsCellResistance>(entity =>
        {
            entity.HasKey(e => new { e.ReadingId, e.CellIndex });
            entity.HasOne(e => e.Reading)
                  .WithMany(r => r.CellResistances)
                  .HasForeignKey(e => e.ReadingId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BmsAlarm>(entity =>
        {
            entity.HasIndex(e => new { e.DeviceId, e.ClearedAt });
            entity.Property(e => e.ActivatedAt).HasColumnType("datetime2");
            entity.Property(e => e.ClearedAt).HasColumnType("datetime2");
            entity.HasOne(e => e.Device)
                  .WithMany(d => d.Alarms)
                  .HasForeignKey(e => e.DeviceId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BmsReadingMinute>(entity =>
        {
            entity.HasIndex(e => new { e.DeviceId, e.PeriodStart }).IsUnique();
            entity.Property(e => e.PeriodStart).HasColumnType("datetime2");
            entity.HasOne(e => e.Device)
                  .WithMany()
                  .HasForeignKey(e => e.DeviceId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BmsReadingHourly>(entity =>
        {
            entity.HasIndex(e => new { e.DeviceId, e.PeriodStart }).IsUnique();
            entity.Property(e => e.PeriodStart).HasColumnType("datetime2");
            entity.HasOne(e => e.Device)
                  .WithMany()
                  .HasForeignKey(e => e.DeviceId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
