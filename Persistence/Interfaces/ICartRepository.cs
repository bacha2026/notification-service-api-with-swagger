using NSA.Domain.Entities;

namespace NSA.Persistence.Interfaces;

public interface ICartRepository
{
    Task<IReadOnlyList<CartItem>> GetCartItemsWithProductsAsync(string visitorEmail, CancellationToken cancellationToken);
    Task<CartItem?> GetByVisitorAndProductAsync(string visitorEmail, int productId, CancellationToken cancellationToken);
    Task<CartItem?> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task<bool> ProductExistsAsync(int productId, CancellationToken cancellationToken);
    void Add(CartItem cartItem);
    void Remove(CartItem cartItem);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
