namespace NSA.Domain.Entities;

public sealed class CartItem
{
    public int Id { get; set; }
    public required string VisitorEmail { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public int Quantity { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
