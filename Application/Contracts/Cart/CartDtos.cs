using System.ComponentModel.DataAnnotations;

namespace NSA.Application.Contracts;

public sealed record AddCartItemRequest
{
    public AddCartItemRequest()
    {
    }

    public AddCartItemRequest(string visitorEmail, int productId, int quantity)
    {
        VisitorEmail = visitorEmail;
        ProductId = productId;
        Quantity = quantity;
    }

    [EmailAddress, StringLength(320)]
    public string VisitorEmail { get; init; } = string.Empty;

    [Required, Range(1, int.MaxValue)]
    public int ProductId { get; init; }

    [Required, Range(1, int.MaxValue)]
    public int Quantity { get; init; }
}

public sealed record UpdateCartItemRequest
{
    public UpdateCartItemRequest()
    {
    }

    public UpdateCartItemRequest(int quantity)
    {
        Quantity = quantity;
    }

    [Required, Range(1, int.MaxValue)]
    public int Quantity { get; init; }
}

public sealed record CartItemDto(int Id, int ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal Subtotal, string ImageUrl);

public sealed record CartDto(string VisitorEmail, IReadOnlyCollection<CartItemDto> Items, decimal TotalAmount);
