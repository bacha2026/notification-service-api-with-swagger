using NSA.Application.Contracts;

namespace NSA.Application.Abstractions;

public interface ICartService
{
    Task<CartDto> GetCartAsync(string visitorEmail, CancellationToken cancellationToken);
    Task<CartDto> GetCartForAuthenticatedUserAsync(string visitorEmail, CancellationToken cancellationToken);
    Task<CartDto?> AddItemAsync(AddCartItemRequest request, CancellationToken cancellationToken);
    Task<CartDto?> AddItemForAuthenticatedUserAsync(
        string visitorEmail,
        AddCurrentUserCartItemRequest request,
        CancellationToken cancellationToken);
    Task<CartDto?> UpdateItemAsync(int cartItemId, UpdateCartItemRequest request, CancellationToken cancellationToken);
    Task<CartDto?> UpdateItemForAuthenticatedUserAsync(
        int cartItemId,
        string visitorEmail,
        UpdateCartItemRequest request,
        CancellationToken cancellationToken);
    Task<bool> RemoveItemAsync(int cartItemId, CancellationToken cancellationToken);
    Task<bool> RemoveItemForAuthenticatedUserAsync(
        int cartItemId,
        string visitorEmail,
        CancellationToken cancellationToken);
}
