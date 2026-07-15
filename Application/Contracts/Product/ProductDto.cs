using System.ComponentModel.DataAnnotations;

namespace NSA.Application.Contracts;

/// <summary>A product available in the catalog.</summary>
public sealed record ProductDto
{
    public ProductDto(int id, string name, string shortDescription, string description, decimal price, int quantityAvailable, string imageUrl) =>
        (Id, Name, ShortDescription, Description, Price, QuantityAvailable, ImageUrl) = (id, name, shortDescription, description, price, quantityAvailable, imageUrl);

    /// <summary>Unique identifier of the product.</summary>
    public int Id { get; init; }
    /// <summary>Display name of the product.</summary>
    public string Name { get; init; }
    /// <summary>Brief product description used in catalog listings.</summary>
    public string ShortDescription { get; init; }
    /// <summary>Detailed description of the product.</summary>
    public string Description { get; init; }
    /// <summary>Current price per unit.</summary>
    public decimal Price { get; init; }
    /// <summary>Number of units currently available.</summary>
    public int QuantityAvailable { get; init; }
    /// <summary>URL of the product image.</summary>
    public string ImageUrl { get; init; }
}

/// <summary>Request to create a product in the catalog.</summary>
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

    /// <summary>Display name of the product.</summary>
    [Required, StringLength(160, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    /// <summary>Brief product description used in catalog listings.</summary>
    [Required, StringLength(280, MinimumLength = 1)]
    public string ShortDescription { get; init; } = string.Empty;

    /// <summary>Detailed description of the product.</summary>
    [Required, StringLength(2000, MinimumLength = 1)]
    public string Description { get; init; } = string.Empty;

    /// <summary>Price per unit.</summary>
    [Range(typeof(decimal), "0", "9999999999999999.99")]
    public decimal Price { get; init; }

    /// <summary>Number of units available for sale.</summary>
    [Range(0, int.MaxValue)]
    public int QuantityAvailable { get; init; }

    /// <summary>URL of the product image.</summary>
    [Required, Url, StringLength(1000, MinimumLength = 1)]
    public string ImageUrl { get; init; } = string.Empty;
}

/// <summary>Request to replace the editable details of a product.</summary>
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

    /// <summary>Display name of the product.</summary>
    [Required, StringLength(160, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    /// <summary>Brief product description used in catalog listings.</summary>
    [Required, StringLength(280, MinimumLength = 1)]
    public string ShortDescription { get; init; } = string.Empty;

    /// <summary>Detailed description of the product.</summary>
    [Required, StringLength(2000, MinimumLength = 1)]
    public string Description { get; init; } = string.Empty;

    /// <summary>Price per unit.</summary>
    [Range(typeof(decimal), "0", "9999999999999999.99")]
    public decimal Price { get; init; }

    /// <summary>Number of units available for sale.</summary>
    [Range(0, int.MaxValue)]
    public int QuantityAvailable { get; init; }

    /// <summary>URL of the product image.</summary>
    [Required, Url, StringLength(1000, MinimumLength = 1)]
    public string ImageUrl { get; init; } = string.Empty;
}
