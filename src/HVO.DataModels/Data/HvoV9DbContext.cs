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
    }
}
