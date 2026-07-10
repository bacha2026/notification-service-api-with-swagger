namespace NSA.Application.Contracts;

public sealed record ProductDto(
    int Id,
    string Name,
    string ShortDescription,
    string Description,
    decimal Price,
    int QuantityAvailable,
    string ImageUrl);

public sealed record CreateProductRequest(
    string Name,
    string ShortDescription,
    string Description,
    decimal Price,
    int QuantityAvailable,
    string ImageUrl);

public sealed record UpdateProductRequest(
    string Name,
    string ShortDescription,
    string Description,
    decimal Price,
    int QuantityAvailable,
    string ImageUrl);
