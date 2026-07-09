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
                new CartItem { Id = 1, VisitorEmail = "bmacha2015@gmail.com", ProductId = 1, Quantity = 2, CreatedAtUtc = new DateTimeOffset(2026, 7, 9, 2, 10, 0, TimeSpan.Zero), UpdatedAtUtc = new DateTimeOffset(2026, 7, 9, 2, 10, 0, TimeSpan.Zero) },
                new CartItem { Id = 2, VisitorEmail = "bmacha2015@gmail.com", ProductId = 4, Quantity = 1, CreatedAtUtc = new DateTimeOffset(2026, 7, 9, 2, 12, 0, TimeSpan.Zero), UpdatedAtUtc = new DateTimeOffset(2026, 7, 9, 2, 12, 0, TimeSpan.Zero) });
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.Property(x => x.VisitorEmail).HasMaxLength(320).IsRequired();
            entity.Property(x => x.TotalAmount).HasColumnType("decimal(18,2)");
            entity.Property(x => x.OrderStatus).HasConversion<string>().HasMaxLength(40);
            entity.Property(x => x.PaymentStatus).HasConversion<string>().HasMaxLength(40);
            entity.Property(x => x.FulfillmentStatus).HasConversion<string>().HasMaxLength(40);
            entity.Property(x => x.DeliveryStatus).HasConversion<string>().HasMaxLength(40);
            entity.HasMany(x => x.Items).WithOne(x => x.Order).HasForeignKey(x => x.OrderId);
            entity.HasData(
                new Order { Id = 1, VisitorEmail = "bmacha2015@gmail.com", OrderStatus = OrderStatus.Delivered, PaymentStatus = PaymentStatus.Paid, FulfillmentStatus = FulfillmentStatus.AssignedToRider, DeliveryStatus = DeliveryStatus.Delivered, TotalAmount = 1277.00m, CreatedAtUtc = new DateTimeOffset(2026, 7, 8, 3, 30, 0, TimeSpan.Zero), UpdatedAtUtc = new DateTimeOffset(2026, 7, 8, 5, 15, 0, TimeSpan.Zero) },
                new Order { Id = 2, VisitorEmail = "bmacha2015@gmail.com", OrderStatus = OrderStatus.Preparing, PaymentStatus = PaymentStatus.Paid, FulfillmentStatus = FulfillmentStatus.Packing, DeliveryStatus = DeliveryStatus.WaitingForRider, TotalAmount = 1398.00m, CreatedAtUtc = new DateTimeOffset(2026, 7, 9, 1, 45, 0, TimeSpan.Zero), UpdatedAtUtc = new DateTimeOffset(2026, 7, 9, 2, 5, 0, TimeSpan.Zero) });
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
            entity.HasData(
                new Notification { Id = 1, RecipientEmail = "bmacha2026@gmail.com", Channel = NotificationChannel.Email, Subject = "New order #1", Body = "Order #1 for bmacha2015@gmail.com. Status: Delivered; Payment: Paid; Fulfillment: AssignedToRider; Delivery: Delivered. Total: PHP 1277.00.", OrderId = 1, IsRead = true, CreatedAtUtc = new DateTimeOffset(2026, 7, 8, 3, 31, 0, TimeSpan.Zero), SentAtUtc = new DateTimeOffset(2026, 7, 8, 3, 31, 0, TimeSpan.Zero) },
                new Notification { Id = 2, RecipientEmail = "bmacha2015@gmail.com", Channel = NotificationChannel.Email, Subject = "Order #1 received", Body = "Your order #1 was received and is now delivered. Total: PHP 1277.00.", OrderId = 1, IsRead = true, CreatedAtUtc = new DateTimeOffset(2026, 7, 8, 3, 32, 0, TimeSpan.Zero), SentAtUtc = new DateTimeOffset(2026, 7, 8, 3, 32, 0, TimeSpan.Zero) },
                new Notification { Id = 3, RecipientEmail = "bmacha2015@gmail.com", Channel = NotificationChannel.Email, Subject = "Order #1 status updated", Body = "Order #1 is delivered. Thank you for your purchase.", OrderId = 1, IsRead = false, CreatedAtUtc = new DateTimeOffset(2026, 7, 8, 5, 15, 0, TimeSpan.Zero), SentAtUtc = new DateTimeOffset(2026, 7, 8, 5, 15, 0, TimeSpan.Zero) },
                new Notification { Id = 4, RecipientEmail = "bmacha2026@gmail.com", Channel = NotificationChannel.Email, Subject = "New order #2", Body = "Order #2 for bmacha2015@gmail.com. Status: Preparing; Payment: Paid; Fulfillment: Packing; Delivery: WaitingForRider. Total: PHP 1398.00.", OrderId = 2, IsRead = false, CreatedAtUtc = new DateTimeOffset(2026, 7, 9, 1, 46, 0, TimeSpan.Zero), SentAtUtc = new DateTimeOffset(2026, 7, 9, 1, 46, 0, TimeSpan.Zero) },
                new Notification { Id = 5, RecipientEmail = "bmacha2015@gmail.com", Channel = NotificationChannel.Email, Subject = "Order #2 received", Body = "Your order #2 was received and is being prepared. Total: PHP 1398.00.", OrderId = 2, IsRead = false, CreatedAtUtc = new DateTimeOffset(2026, 7, 9, 1, 47, 0, TimeSpan.Zero), SentAtUtc = new DateTimeOffset(2026, 7, 9, 1, 47, 0, TimeSpan.Zero) });
        });
    }
}
