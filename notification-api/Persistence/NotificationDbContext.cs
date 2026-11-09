using Microsoft.EntityFrameworkCore;
using NSA.Domain.Entities;
using NSA.Domain.Enums;

namespace NSA.Persistence;

public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<BulkNotificationJob> BulkNotificationJobs => Set<BulkNotificationJob>();
    public DbSet<BulkNotificationJobItem> BulkNotificationJobItems => Set<BulkNotificationJobItem>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserCredential> UserCredentials => Set<UserCredential>();
    public DbSet<UserExternalIdentity> UserExternalIdentities => Set<UserExternalIdentity>();
    public DbSet<UserMfaMethod> UserMfaMethods => Set<UserMfaMethod>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
            entity.Property(x => x.ShortDescription).HasMaxLength(280).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.ImageUrl).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.Price).HasColumnType("decimal(18,2)");
            entity.HasData(
                new Product { Id = 1, Name = "Artisan Coffee Beans", ShortDescription = "Small-batch roasted arabica beans.", Description = "A balanced medium roast with cocoa notes for daily brewing.", Price = 349.00m, QuantityAvailable = 100, ImageUrl = "https://images.unsplash.com/photo-1447933601403-0c6688de566e?auto=format&fit=crop&w=900&q=80" },
                new Product { Id = 2, Name = "Butter Croissant Box", ShortDescription = "Flaky croissants baked fresh.", Description = "A six-piece box of layered butter croissants for breakfast or gifting.", Price = 429.00m, QuantityAvailable = 50, ImageUrl = "https://images.unsplash.com/photo-1555507036-ab1f4038808a?auto=format&fit=crop&w=900&q=80" },
                new Product { Id = 3, Name = "Gourmet Sandwich Kit", ShortDescription = "Premium sandwich ingredients ready to assemble.", Description = "Includes artisan bread, deli cuts, crisp greens, and house dressing.", Price = 549.00m, QuantityAvailable = 35, ImageUrl = "https://images.unsplash.com/photo-1528735602780-2552fd46c7af?auto=format&fit=crop&w=900&q=80" },
                new Product { Id = 4, Name = "Fresh Berry Bowl", ShortDescription = "Seasonal berries with granola.", Description = "A chilled fruit bowl with strawberries, blueberries, yogurt, and granola.", Price = 299.00m, QuantityAvailable = 70, ImageUrl = "https://images.unsplash.com/photo-1490474418585-ba9bad8fd0ea?auto=format&fit=crop&w=900&q=80" },
                new Product { Id = 5, Name = "Signature Pasta Tray", ShortDescription = "Family-size pasta with tomato basil sauce.", Description = "Pasta tray prepared for sharing, finished with parmesan and herbs.", Price = 699.00m, QuantityAvailable = 25, ImageUrl = "https://images.unsplash.com/photo-1473093295043-cdd812d0e601?auto=format&fit=crop&w=900&q=80" });
        });

        modelBuilder.Entity<CartItem>(entity =>
        {
            entity.Property(x => x.VisitorEmail).HasMaxLength(320).IsRequired();
            entity.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId);
            entity.HasIndex(x => new { x.VisitorEmail, x.ProductId }).IsUnique();
            entity.HasData(
                new CartItem { Id = 1, VisitorEmail = "visitor@example.test", ProductId = 1, Quantity = 2, CreatedAtUtc = new DateTimeOffset(2026, 7, 9, 2, 10, 0, TimeSpan.Zero), UpdatedAtUtc = new DateTimeOffset(2026, 7, 9, 2, 10, 0, TimeSpan.Zero) },
                new CartItem { Id = 2, VisitorEmail = "visitor@example.test", ProductId = 4, Quantity = 1, CreatedAtUtc = new DateTimeOffset(2026, 7, 9, 2, 12, 0, TimeSpan.Zero), UpdatedAtUtc = new DateTimeOffset(2026, 7, 9, 2, 12, 0, TimeSpan.Zero) });
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.Property(x => x.VisitorEmail).HasMaxLength(320).IsRequired();
            entity.Property(x => x.TotalAmount).HasColumnType("decimal(18,2)");
            entity.Property(x => x.OrderStatus).HasConversion<string>().HasMaxLength(40);
            entity.Property(x => x.PaymentStatus).HasConversion<string>().HasMaxLength(40);
            entity.Property(x => x.FulfillmentStatus).HasConversion<string>().HasMaxLength(40);
            entity.Property(x => x.DeliveryStatus).HasConversion<string>().HasMaxLength(40);
            entity.HasOne(order => order.User)
                .WithMany()
                .HasForeignKey(order => order.UserId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(order => order.UserId);
            entity.HasMany(x => x.Items).WithOne(x => x.Order).HasForeignKey(x => x.OrderId);
            entity.HasData(
                new Order { Id = 1, VisitorEmail = "visitor@example.test", OrderStatus = OrderStatus.Delivered, PaymentStatus = PaymentStatus.Paid, FulfillmentStatus = FulfillmentStatus.AssignedToRider, DeliveryStatus = DeliveryStatus.Delivered, TotalAmount = 1277.00m, CreatedAtUtc = new DateTimeOffset(2026, 7, 8, 3, 30, 0, TimeSpan.Zero), UpdatedAtUtc = new DateTimeOffset(2026, 7, 8, 5, 15, 0, TimeSpan.Zero) },
                new Order { Id = 2, VisitorEmail = "visitor@example.test", OrderStatus = OrderStatus.Preparing, PaymentStatus = PaymentStatus.Paid, FulfillmentStatus = FulfillmentStatus.Packing, DeliveryStatus = DeliveryStatus.WaitingForRider, TotalAmount = 1398.00m, CreatedAtUtc = new DateTimeOffset(2026, 7, 9, 1, 45, 0, TimeSpan.Zero), UpdatedAtUtc = new DateTimeOffset(2026, 7, 9, 2, 5, 0, TimeSpan.Zero) });
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.Property(x => x.ProductName).HasMaxLength(160).IsRequired();
            entity.Property(x => x.UnitPrice).HasColumnType("decimal(18,2)");
            entity.Property(x => x.Subtotal).HasColumnType("decimal(18,2)");
            entity.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.NoAction);
            entity.HasData(
                new OrderItem { Id = 1, OrderId = 1, ProductId = 2, ProductName = "Butter Croissant Box", UnitPrice = 429.00m, Quantity = 1, Subtotal = 429.00m },
                new OrderItem { Id = 2, OrderId = 1, ProductId = 3, ProductName = "Gourmet Sandwich Kit", UnitPrice = 549.00m, Quantity = 1, Subtotal = 549.00m },
                new OrderItem { Id = 3, OrderId = 1, ProductId = 4, ProductName = "Fresh Berry Bowl", UnitPrice = 299.00m, Quantity = 1, Subtotal = 299.00m },
                new OrderItem { Id = 4, OrderId = 2, ProductId = 5, ProductName = "Signature Pasta Tray", UnitPrice = 699.00m, Quantity = 2, Subtotal = 1398.00m });
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.Property(x => x.RecipientEmail).HasMaxLength(320).IsRequired();
            entity.Property(x => x.Subject).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Body).HasMaxLength(4000).IsRequired();
            entity.Property(x => x.Channel).HasConversion<string>().HasMaxLength(20);
            entity.HasOne(x => x.Order).WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(x => x.BulkNotificationJobId);
            entity.HasIndex(x => x.BulkNotificationJobItemId).IsUnique().HasFilter("[BulkNotificationJobItemId] IS NOT NULL");
            entity.HasData(
                new Notification { Id = 2, RecipientEmail = "visitor@example.test", Channel = NotificationChannel.Email, Subject = "Order #1 received", Body = "Your order #1 was received and is now delivered. Total: PHP 1277.00.", OrderId = 1, IsRead = true, CreatedAtUtc = new DateTimeOffset(2026, 7, 8, 3, 32, 0, TimeSpan.Zero), SentAtUtc = new DateTimeOffset(2026, 7, 8, 3, 32, 0, TimeSpan.Zero) },
                new Notification { Id = 3, RecipientEmail = "visitor@example.test", Channel = NotificationChannel.Email, Subject = "Order #1 status updated", Body = "Order #1 is delivered. Thank you for your purchase.", OrderId = 1, IsRead = false, CreatedAtUtc = new DateTimeOffset(2026, 7, 8, 5, 15, 0, TimeSpan.Zero), SentAtUtc = new DateTimeOffset(2026, 7, 8, 5, 15, 0, TimeSpan.Zero) },
                new Notification { Id = 5, RecipientEmail = "visitor@example.test", Channel = NotificationChannel.Email, Subject = "Order #2 received", Body = "Your order #2 was received and is being prepared. Total: PHP 1398.00.", OrderId = 2, IsRead = false, CreatedAtUtc = new DateTimeOffset(2026, 7, 9, 1, 47, 0, TimeSpan.Zero), SentAtUtc = new DateTimeOffset(2026, 7, 9, 1, 47, 0, TimeSpan.Zero) });
        });

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

        modelBuilder.Entity<IdempotencyRecord>(entity =>
        {
            entity.Property(x => x.Scope).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ActorId).HasMaxLength(320).IsRequired();
            entity.Property(x => x.KeyHash).HasMaxLength(64).IsFixedLength().IsRequired();
            entity.Property(x => x.RequestHash).HasMaxLength(64).IsFixedLength().IsRequired();
            entity.Property(x => x.ResponseBody).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(x => x.ResourceType).HasMaxLength(100);
            entity.Property(x => x.ResourceId).HasMaxLength(128);
            entity.HasIndex(x => new { x.Scope, x.ActorId, x.KeyHash }).IsUnique();
            entity.HasIndex(x => x.ExpiresAtUtc);
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

        modelBuilder.Entity<InboxMessage>(entity =>
        {
            entity.HasKey(x => new { x.MessageId, x.Handler });
            entity.Property(x => x.Handler).HasMaxLength(200).IsRequired();
            entity.HasIndex(x => new { x.Handler, x.AggregateId });
            entity.HasIndex(x => x.ProcessedAtUtc);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.Property(user => user.FirstName).HasMaxLength(100).IsRequired();
            entity.Property(user => user.LastName).HasMaxLength(100).IsRequired();
            entity.Property(user => user.Address).HasMaxLength(500);
            entity.Property(user => user.DateOfBirth).HasColumnType("date");
            entity.Property(user => user.Gender).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(user => user.ContactNumber).HasMaxLength(50);
            entity.Property(user => user.PhotoUrl).HasMaxLength(1000);
            entity.HasOne(user => user.Credential)
                .WithOne(credential => credential.User)
                .HasForeignKey<UserCredential>(credential => credential.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            entity.HasMany(user => user.ExternalIdentities)
                .WithOne(identity => identity.User)
                .HasForeignKey(identity => identity.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(user => user.MfaMethods)
                .WithOne(method => method.User)
                .HasForeignKey(method => method.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserCredential>(entity =>
        {
            entity.ToTable("UserCredentials");
            entity.Property(credential => credential.Email).HasMaxLength(320).IsRequired();
            entity.Property(credential => credential.NormalizedEmail).HasMaxLength(320).IsRequired();
            entity.Property(credential => credential.PasswordHash).HasMaxLength(1000);
            entity.Property(credential => credential.PasswordResetTokenHash).HasMaxLength(64).IsFixedLength();
            entity.HasIndex(credential => credential.NormalizedEmail).IsUnique();
            entity.HasIndex(credential => credential.LockoutEndUtc);
        });

        modelBuilder.Entity<UserExternalIdentity>(entity =>
        {
            entity.ToTable("UserExternalIdentities");
            entity.Property(identity => identity.Provider).HasMaxLength(64).IsRequired();
            entity.Property(identity => identity.ProviderSubject).HasMaxLength(512).IsRequired();
            entity.Property(identity => identity.ProviderEmail).HasMaxLength(320);
            entity.HasIndex(identity => new { identity.Provider, identity.ProviderSubject }).IsUnique();
            entity.HasIndex(identity => identity.UserId);
        });

        modelBuilder.Entity<UserMfaMethod>(entity =>
        {
            entity.ToTable("UserMfaMethods");
            entity.Property(method => method.Type).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(method => method.Destination).HasMaxLength(320);
            entity.Property(method => method.ProtectedSecret).HasMaxLength(2048);
            entity.HasIndex(method => new { method.UserId, method.Type });
        });
    }
}
