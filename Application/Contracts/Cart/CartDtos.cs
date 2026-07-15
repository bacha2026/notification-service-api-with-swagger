using System.ComponentModel.DataAnnotations;

namespace NSA.Application.Contracts;

/// <summary>Request to add a product to a visitor's cart.</summary>
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

    /// <summary>Email address that identifies the visitor's cart.</summary>
    [EmailAddress, StringLength(320)]
    public string VisitorEmail { get; init; } = string.Empty;

    /// <summary>Identifier of the product to add.</summary>
    [Required, Range(1, int.MaxValue)]
    public int ProductId { get; init; }

    /// <summary>Number of product units to add.</summary>
    [Required, Range(1, int.MaxValue)]
    public int Quantity { get; init; }
}

/// <summary>Request to change the quantity of an item in a cart.</summary>
public sealed record UpdateCartItemRequest
{
    public UpdateCartItemRequest()
    {
    }

    public UpdateCartItemRequest(int quantity)
    {
        Quantity = quantity;
    }

    /// <summary>New quantity for the cart item.</summary>
    [Required, Range(1, int.MaxValue)]
    public int Quantity { get; init; }
}

/// <summary>A product line in a visitor's cart.</summary>
public sealed record CartItemDto
{
    public CartItemDto(int id, int productId, string productName, decimal unitPrice, int quantity, decimal subtotal, string imageUrl) =>
        (Id, ProductId, ProductName, UnitPrice, Quantity, Subtotal, ImageUrl) = (id, productId, productName, unitPrice, quantity, subtotal, imageUrl);

    /// <summary>Identifier of the cart item.</summary>
    public int Id { get; init; }
    /// <summary>Identifier of the product.</summary>
    public int ProductId { get; init; }
    /// <summary>Display name of the product.</summary>
    public string ProductName { get; init; }
    /// <summary>Price of one product unit.</summary>
    public decimal UnitPrice { get; init; }
    /// <summary>Number of product units in the cart.</summary>
    public int Quantity { get; init; }
    /// <summary>Line total calculated as unit price multiplied by quantity.</summary>
    public decimal Subtotal { get; init; }
    /// <summary>URL of the product image.</summary>
    public string ImageUrl { get; init; }
}

/// <summary>A visitor's cart and its current totals.</summary>
public sealed record CartDto
{
    public CartDto(string visitorEmail, IReadOnlyCollection<CartItemDto> items, decimal totalAmount) =>
        (VisitorEmail, Items, TotalAmount) = (visitorEmail, items, totalAmount);

    /// <summary>Email address that identifies the visitor's cart.</summary>
    public string VisitorEmail { get; init; }
    /// <summary>Product lines currently in the cart.</summary>
    public IReadOnlyCollection<CartItemDto> Items { get; init; }
    /// <summary>Total value of all items in the cart.</summary>
    public decimal TotalAmount { get; init; }
}
