namespace NSA.Application.Contracts;

public sealed record AddCartItemRequest(string VisitorEmail, int ProductId, int Quantity);

public sealed record UpdateCartItemRequest(int Quantity);

public sealed record CartItemDto(int Id, int ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal Subtotal, string ImageUrl);

public sealed record CartDto(string VisitorEmail, IReadOnlyCollection<CartItemDto> Items, decimal TotalAmount);
