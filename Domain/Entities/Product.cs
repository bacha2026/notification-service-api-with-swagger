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
}
