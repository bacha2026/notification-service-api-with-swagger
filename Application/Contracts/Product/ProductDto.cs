using System.ComponentModel.DataAnnotations;

namespace NSA.Application.Contracts;

public sealed record ProductDto(
    int Id,
    string Name,
    string ShortDescription,
    string Description,
    decimal Price,
    int QuantityAvailable,
    string ImageUrl);

public sealed record CreateProductRequest
{
    public CreateProductRequest()
    {
    }

    public CreateProductRequest(string name, string shortDescription, string description, decimal price, int quantityAvailable, string imageUrl)
    {
        Name = name;
        ShortDescription = shortDescription;
        Description = description;
        Price = price;
        QuantityAvailable = quantityAvailable;
        ImageUrl = imageUrl;
    }

    [Required, StringLength(160, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    [Required, StringLength(280, MinimumLength = 1)]
    public string ShortDescription { get; init; } = string.Empty;

    [Required, StringLength(2000, MinimumLength = 1)]
    public string Description { get; init; } = string.Empty;

    [Range(typeof(decimal), "0", "9999999999999999.99")]
    public decimal Price { get; init; }

    [Range(0, int.MaxValue)]
    public int QuantityAvailable { get; init; }

    [Required, Url, StringLength(1000, MinimumLength = 1)]
    public string ImageUrl { get; init; } = string.Empty;
}

public sealed record UpdateProductRequest
{
    public UpdateProductRequest()
    {
    }

    public UpdateProductRequest(string name, string shortDescription, string description, decimal price, int quantityAvailable, string imageUrl)
    {
        Name = name;
        ShortDescription = shortDescription;
        Description = description;
        Price = price;
        QuantityAvailable = quantityAvailable;
        ImageUrl = imageUrl;
    }

    [Required, StringLength(160, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    [Required, StringLength(280, MinimumLength = 1)]
    public string ShortDescription { get; init; } = string.Empty;

    [Required, StringLength(2000, MinimumLength = 1)]
    public string Description { get; init; } = string.Empty;

    [Range(typeof(decimal), "0", "9999999999999999.99")]
    public decimal Price { get; init; }

    [Range(0, int.MaxValue)]
    public int QuantityAvailable { get; init; }

    [Required, Url, StringLength(1000, MinimumLength = 1)]
    public string ImageUrl { get; init; } = string.Empty;
}
