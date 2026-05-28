using Microsoft.EntityFrameworkCore;

namespace HVO.Edge.Outbox;

public abstract class EdgeOutboxDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<EdgeOutboxRecord> OutboxRecords => Set<EdgeOutboxRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EdgeOutboxRecord>(e =>
        {
            e.ToTable("OutboxRecords");
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).ValueGeneratedOnAdd();
            e.Property(r => r.SourceId).IsRequired();
            e.Property(r => r.PayloadType).IsRequired();
            e.Property(r => r.PayloadVersion).IsRequired();
            e.Property(r => r.PayloadJson).HasColumnName("Payload").IsRequired();
            e.HasIndex(r => r.Status);
            e.HasIndex(r => new { r.SourceId, r.PayloadType, r.RecordedAtUtc }).IsUnique();
        });

        base.OnModelCreating(modelBuilder);
    }
}
