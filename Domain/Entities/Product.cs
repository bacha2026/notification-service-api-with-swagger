namespace NSA.Domain.Entities;

public sealed class Product
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string ShortDescription { get; set; }
    public required string Description { get; set; }
    public decimal Price { get; set; }
    public int QuantityAvailable { get; set; }
    public required string ImageUrl { get; set; }

    public static Product Create(string name, string shortDescription, string description, decimal price, int quantityAvailable, string imageUrl)
    {
        Validate(name, shortDescription, description, price, quantityAvailable, imageUrl);

        return new Product
        {
            Name = name.Trim(),
            ShortDescription = shortDescription.Trim(),
            Description = description.Trim(),
            Price = price,
            QuantityAvailable = quantityAvailable,
            ImageUrl = imageUrl.Trim()
        };
    }

    public void Update(string name, string shortDescription, string description, decimal price, int quantityAvailable, string imageUrl)
    {
        Validate(name, shortDescription, description, price, quantityAvailable, imageUrl);

        Name = name.Trim();
        ShortDescription = shortDescription.Trim();
        Description = description.Trim();
        Price = price;
        QuantityAvailable = quantityAvailable;
        ImageUrl = imageUrl.Trim();
    }

    private static void Validate(string name, string shortDescription, string description, decimal price, int quantityAvailable, string imageUrl)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(shortDescription) || string.IsNullOrWhiteSpace(description) || string.IsNullOrWhiteSpace(imageUrl))
        {
            throw new ArgumentException("Name, ShortDescription, Description, and ImageUrl are required.");
        }

        if (price < 0)
        {
            throw new ArgumentException("Price must be zero or greater.");
        }

        if (quantityAvailable < 0)
        {
            throw new ArgumentException("QuantityAvailable must be zero or greater.");
        }
    }
}
