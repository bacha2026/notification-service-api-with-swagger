using NSA.Domain.Entities;

namespace NSA.Application.Abstractions;

/// <summary>Persistence port required by product use cases.</summary>
public interface IProductRepository
{
    Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken);
    Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task<bool> ExistsAsync(int id, CancellationToken cancellationToken);
    void Add(Product product);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
