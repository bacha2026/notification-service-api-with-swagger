using Microsoft.EntityFrameworkCore;

namespace NSA.Outbox.Worker;

/// <summary>Maps only the existing SQL tables used by this worker. The API owns migrations.</summary>
public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    public DbSet<OrderReference> Orders => Set<OrderReference>();
    public DbSet<BulkNotificationJob> BulkNotificationJobs => Set<BulkNotificationJob>();
    public DbSet<BulkNotificationJobItem> BulkNotificationJobItems => Set<BulkNotificationJobItem>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrderReference>().ToTable("Orders");
        modelBuilder.Entity<BulkNotificationJob>(entity =>
        {
            entity.Property(x => x.Status)
                .HasMaxLength(40)
                .IsRequired()
                .IsConcurrencyToken();
            entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Error).HasMaxLength(1000);
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.CompletedAtUtc);
            entity.HasMany(x => x.Items)
                .WithOne(x => x.Job)
                .HasForeignKey(x => x.JobId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BulkNotificationJobItem>(entity =>
        {
            entity.Property(x => x.RecipientEmail).HasMaxLength(320).IsRequired();
            entity.Property(x => x.Channel).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.Subject).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Body).HasMaxLength(4000).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
            entity.Property(x => x.LastError).HasMaxLength(500);
            entity.HasIndex(x => new { x.JobId, x.Sequence }).IsUnique();
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.Property(x => x.AggregateType).HasMaxLength(100).IsRequired();
            entity.Property(x => x.AggregateId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.MessageType).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Payload).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(x => x.Exchange).HasMaxLength(255).IsRequired();
            entity.Property(x => x.RoutingKey).HasMaxLength(255).IsRequired();
            entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.LockedBy).HasMaxLength(128);
            entity.Property(x => x.LastError).HasMaxLength(2000);
            entity.HasIndex(x => new { x.PublishedAtUtc, x.NextAttemptAtUtc });
            entity.HasIndex(x => new { x.AggregateType, x.AggregateId });
            entity.HasIndex(x => x.LockedUntilUtc);
        });
    }
}
