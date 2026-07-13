using Microsoft.EntityFrameworkCore;
using NSA.Domain.Entities;
using NSA.Persistence.Interfaces;

namespace NSA.Persistence.Concrete;

public sealed class CartRepository(NotificationDbContext dbContext) : ICartRepository
{
    public async Task<IReadOnlyList<CartItem>> GetCartItemsWithProductsAsync(string visitorEmail, CancellationToken cancellationToken)
    {
        return await dbContext.CartItems
            .Include(item => item.Product)
            .Where(item => item.VisitorEmail == visitorEmail)
            .OrderBy(item => item.Product!.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<CartItem?> GetByVisitorAndProductAsync(string visitorEmail, int productId, CancellationToken cancellationToken)
    {
        return dbContext.CartItems.SingleOrDefaultAsync(item => item.VisitorEmail == visitorEmail && item.ProductId == productId, cancellationToken);
    }

    public Task<CartItem?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        return dbContext.CartItems.FindAsync([id], cancellationToken).AsTask();
    }

    public Task<bool> ProductExistsAsync(int productId, CancellationToken cancellationToken)
    {
        return dbContext.Products.AnyAsync(product => product.Id == productId, cancellationToken);
    }

    public void Add(CartItem cartItem)
    {
        dbContext.CartItems.Add(cartItem);
    }

    public void Remove(CartItem cartItem)
    {
        dbContext.CartItems.Remove(cartItem);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
