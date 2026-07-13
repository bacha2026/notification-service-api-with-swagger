using Microsoft.EntityFrameworkCore;
using NSA.Domain.Entities;
using NSA.Persistence.Interfaces;

namespace NSA.Persistence.Concrete;

public sealed class OrderRepository(NotificationDbContext dbContext) : IOrderRepository
{
    public async Task<IReadOnlyList<Order>> GetOrdersForVisitorAsync(string visitorEmail, CancellationToken cancellationToken)
    {
        return await dbContext.Orders
            .Include(order => order.Items)
            .Where(order => order.VisitorEmail == visitorEmail)
            .OrderByDescending(order => order.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public Task<Order?> GetByIdWithItemsAsync(int id, CancellationToken cancellationToken)
    {
        return dbContext.Orders
            .Include(order => order.Items)
            .SingleOrDefaultAsync(order => order.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<CartItem>> GetCartItemsForOrderAsync(string visitorEmail, CancellationToken cancellationToken)
    {
        return await dbContext.CartItems
            .Include(item => item.Product)
            .Where(item => item.VisitorEmail == visitorEmail)
            .ToListAsync(cancellationToken);
    }

    public void Add(Order order)
    {
        dbContext.Orders.Add(order);
    }

    public void RemoveCartItems(IEnumerable<CartItem> cartItems)
    {
        dbContext.CartItems.RemoveRange(cartItems);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
