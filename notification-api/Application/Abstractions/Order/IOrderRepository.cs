using NSA.Domain.Entities;

namespace NSA.Application.Abstractions;

/// <summary>Persistence port required by order use cases.</summary>
public interface IOrderRepository
{
    Task<IReadOnlyList<Order>> GetOrdersForVisitorAsync(string visitorEmail, CancellationToken cancellationToken);
    Task<IReadOnlyList<Order>> GetOrdersForUserAsync(Guid userId, CancellationToken cancellationToken);
    Task<Order?> GetByIdWithItemsAsync(int id, CancellationToken cancellationToken);
    Task<Order?> GetByIdForUserWithItemsAsync(int id, Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<CartItem>> GetCartItemsForOrderAsync(string visitorEmail, CancellationToken cancellationToken);
    void Add(Order order);
    void RemoveCartItems(IEnumerable<CartItem> cartItems);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
