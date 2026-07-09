namespace NSA.Application.Contracts;

public sealed record ProductDto(
    int Id,
    string Name,
    string ShortDescription,
    string Description,
    decimal Price,
    int QuantityAvailable,
    string ImageUrl);
