using NSA.Application.Abstractions;
using NSA.Application.Contracts;
using NSA.Domain.Entities;
using NSA.Persistence.Interfaces;

namespace NSA.Service;

public sealed class CartService(ICartRepository cartRepository, IConfiguration configuration) : ICartService
{
    public Task<CartDto> GetCartAsync(string visitorEmail, CancellationToken cancellationToken)
    {
        return BuildCartDtoAsync(visitorEmail, cancellationToken);
    }

    public async Task<CartDto?> AddItemAsync(AddCartItemRequest request, CancellationToken cancellationToken)
    {
        var productExists = await cartRepository.ProductExistsAsync(request.ProductId, cancellationToken);
        if (!productExists)
        {
            return null;
        }

        var visitorEmail = ResolveVisitorEmail(request.VisitorEmail);
        var cartItem = await cartRepository.GetByVisitorAndProductAsync(visitorEmail, request.ProductId, cancellationToken);
        if (cartItem is null)
        {
            cartRepository.Add(CartItem.Create(visitorEmail, request.ProductId, request.Quantity, DateTimeOffset.UtcNow));
        }
        else
        {
            cartItem.AddQuantity(request.Quantity, DateTimeOffset.UtcNow);
        }

        await cartRepository.SaveChangesAsync(cancellationToken);
        return await BuildCartDtoAsync(visitorEmail, cancellationToken);
    }

    public async Task<CartDto?> UpdateItemAsync(int cartItemId, UpdateCartItemRequest request, CancellationToken cancellationToken)
    {
        var cartItem = await cartRepository.GetByIdAsync(cartItemId, cancellationToken);
        if (cartItem is null)
        {
            return null;
        }

        cartItem.UpdateQuantity(request.Quantity, DateTimeOffset.UtcNow);
        await cartRepository.SaveChangesAsync(cancellationToken);
        return await BuildCartDtoAsync(cartItem.VisitorEmail, cancellationToken);
    }

    public async Task<bool> RemoveItemAsync(int cartItemId, CancellationToken cancellationToken)
    {
        var cartItem = await cartRepository.GetByIdAsync(cartItemId, cancellationToken);
        if (cartItem is null)
        {
            return false;
        }

        cartRepository.Remove(cartItem);
        await cartRepository.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<CartDto> BuildCartDtoAsync(string visitorEmail, CancellationToken cancellationToken)
    {
        var resolvedEmail = ResolveVisitorEmail(visitorEmail);
        var items = await cartRepository.GetCartItemsWithProductsAsync(resolvedEmail, cancellationToken);

        var itemDtos = items
            .Select(item => new CartItemDto(item.Id, item.ProductId, item.Product!.Name, item.Product.Price, item.Quantity, item.Product.Price * item.Quantity, item.Product.ImageUrl))
            .ToList();

        return new CartDto(resolvedEmail, itemDtos, itemDtos.Sum(item => item.Subtotal));
    }

    private string ResolveVisitorEmail(string visitorEmail)
    {
        return string.IsNullOrWhiteSpace(visitorEmail)
            ? configuration["NotificationEmails:DefaultVisitorEmail"] ?? "bmacha2015@gmail.com"
            : visitorEmail.Trim();
    }

}
