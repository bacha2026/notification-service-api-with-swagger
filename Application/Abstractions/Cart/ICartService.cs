using NSA.Application.Contracts;

namespace NSA.Application.Abstractions;

public interface ICartService
{
    Task<CartDto> GetCartAsync(string visitorEmail, CancellationToken cancellationToken);
    Task<CartDto?> AddItemAsync(AddCartItemRequest request, CancellationToken cancellationToken);
    Task<CartDto?> UpdateItemAsync(int cartItemId, UpdateCartItemRequest request, CancellationToken cancellationToken);
    Task<bool> RemoveItemAsync(int cartItemId, CancellationToken cancellationToken);
}
