using Microsoft.EntityFrameworkCore;
using NSA.Domain.Entities;
using NSA.Persistence.Interfaces;

namespace NSA.Persistence.Concrete;

public sealed class ProductRepository(NotificationDbContext dbContext) : IProductRepository
{
    public async Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Products
            .OrderBy(product => product.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        return dbContext.Products.FindAsync([id], cancellationToken).AsTask();
    }

    public Task<bool> ExistsAsync(int id, CancellationToken cancellationToken)
    {
        return dbContext.Products.AnyAsync(product => product.Id == id, cancellationToken);
    }

    public void Add(Product product)
    {
        dbContext.Products.Add(product);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
