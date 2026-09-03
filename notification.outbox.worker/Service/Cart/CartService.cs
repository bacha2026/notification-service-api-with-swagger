using NSA.Application.Abstractions;
using NSA.Application.Configuration;
using NSA.Application.Contracts;
using NSA.Application.Exceptions;
using NSA.Domain.Entities;

namespace NSA.Service;

public sealed class CartService(
    ICartRepository cartRepository,
    NotificationRecipientSettings recipientSettings,
    TimeProvider timeProvider) : ICartService
{
    public Task<CartDto> GetCartAsync(string visitorEmail, CancellationToken cancellationToken)
    {
        return BuildCartDtoAsync(visitorEmail, cancellationToken);
    }

    public Task<CartDto> GetCartForAuthenticatedUserAsync(string visitorEmail, CancellationToken cancellationToken)
    {
        return BuildCartDtoAsync(NormalizeAuthenticatedVisitorEmail(visitorEmail), cancellationToken);
    }

    public Task<CartDto?> AddItemAsync(AddCartItemRequest request, CancellationToken cancellationToken)
    {
        var visitorEmail = ResolveVisitorEmail(request.VisitorEmail);
        return AddItemAsync(visitorEmail, request.ProductId, request.Quantity, cancellationToken);
    }

    public Task<CartDto?> AddItemForAuthenticatedUserAsync(
        string visitorEmail,
        AddCurrentUserCartItemRequest request,
        CancellationToken cancellationToken) =>
        AddItemAsync(
            NormalizeAuthenticatedVisitorEmail(visitorEmail),
            request.ProductId,
            request.Quantity,
            cancellationToken);

    public async Task<CartDto?> UpdateItemAsync(int cartItemId, UpdateCartItemRequest request, CancellationToken cancellationToken)
    {
        var cartItem = await cartRepository.GetByIdAsync(cartItemId, cancellationToken);
        if (cartItem is null)
        {
            return null;
        }

        cartItem.UpdateQuantity(request.Quantity, timeProvider.GetUtcNow());
        await cartRepository.SaveChangesAsync(cancellationToken);
        return await BuildCartDtoAsync(cartItem.VisitorEmail, cancellationToken);
    }

    public async Task<CartDto?> UpdateItemForAuthenticatedUserAsync(
        int cartItemId,
        string visitorEmail,
        UpdateCartItemRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedVisitorEmail = NormalizeAuthenticatedVisitorEmail(visitorEmail);
        var cartItem = await cartRepository.GetByIdForVisitorAsync(
            cartItemId,
            normalizedVisitorEmail,
            cancellationToken);
        if (cartItem is null)
        {
            return null;
        }

        cartItem.UpdateQuantity(request.Quantity, timeProvider.GetUtcNow());
        await cartRepository.SaveChangesAsync(cancellationToken);
        return await BuildCartDtoAsync(normalizedVisitorEmail, cancellationToken);
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

    public async Task<bool> RemoveItemForAuthenticatedUserAsync(
        int cartItemId,
        string visitorEmail,
        CancellationToken cancellationToken)
    {
        var cartItem = await cartRepository.GetByIdForVisitorAsync(
            cartItemId,
            NormalizeAuthenticatedVisitorEmail(visitorEmail),
            cancellationToken);
        if (cartItem is null)
        {
            return false;
        }

        cartRepository.Remove(cartItem);
        await cartRepository.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<CartDto?> AddItemAsync(
        string visitorEmail,
        int productId,
        int quantity,
        CancellationToken cancellationToken)
    {
        var productExists = await cartRepository.ProductExistsAsync(productId, cancellationToken);
        if (!productExists)
        {
            return null;
        }

        var cartItem = await cartRepository.GetByVisitorAndProductAsync(visitorEmail, productId, cancellationToken);
        if (cartItem is null)
        {
            cartRepository.Add(CartItem.Create(
                visitorEmail,
                productId,
                quantity,
                timeProvider.GetUtcNow()));
        }
        else
        {
            cartItem.AddQuantity(quantity, timeProvider.GetUtcNow());
        }

        await cartRepository.SaveChangesAsync(cancellationToken);
        return await BuildCartDtoAsync(visitorEmail, cancellationToken);
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

    private string ResolveVisitorEmail(string visitorEmail) =>
        recipientSettings.ResolveVisitorEmail(visitorEmail);

    private static string NormalizeAuthenticatedVisitorEmail(string visitorEmail)
    {
        if (string.IsNullOrWhiteSpace(visitorEmail))
        {
            throw new RequestValidationException("An authenticated visitor email is required.");
        }

        return visitorEmail.Trim();
    }

}

