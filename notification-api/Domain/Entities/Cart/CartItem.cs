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

    public static CartItem Create(string visitorEmail, int productId, int quantity, DateTimeOffset createdAtUtc)
    {
        ValidateQuantity(quantity);

        return new CartItem
        {
            VisitorEmail = visitorEmail.Trim(),
            ProductId = productId,
            Quantity = quantity,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc
        };
    }

    public void AddQuantity(int quantity, DateTimeOffset updatedAtUtc)
    {
        ValidateQuantity(quantity);

        Quantity += quantity;
        UpdatedAtUtc = updatedAtUtc;
    }

    public void UpdateQuantity(int quantity, DateTimeOffset updatedAtUtc)
    {
        ValidateQuantity(quantity);

        Quantity = quantity;
        UpdatedAtUtc = updatedAtUtc;
    }

    private static void ValidateQuantity(int quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentException("Quantity must be greater than zero.");
        }
    }
}
