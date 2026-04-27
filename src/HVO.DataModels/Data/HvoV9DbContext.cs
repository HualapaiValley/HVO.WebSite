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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("v9");

        modelBuilder.Entity<WeatherRaw>(entity =>
        {
            entity.HasIndex(e => e.RecordedAt);
            entity.HasIndex(e => new { e.StationId, e.RecordedAt });
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
    }
}
