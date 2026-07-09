using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSA.Application.Contracts;
using NSA.Domain.Entities;
using NSA.Persistence;

namespace NSA.Presentation.Controllers;

[ApiController]
[Route("api/cart")]
[Produces("application/json")]
public sealed class CartController(NotificationDbContext dbContext, IConfiguration configuration) : ControllerBase
{
    /// <summary>Gets the current cart for a visitor.</summary>
    /// <response code="200">Returns the visitor cart with line-item subtotals and total amount.</response>
    [HttpGet("{visitorEmail}")]
    [ProducesResponseType(typeof(CartDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CartDto>> GetCart(string visitorEmail, CancellationToken cancellationToken)
    {
        return Ok(await BuildCartDtoAsync(visitorEmail, cancellationToken));
    }

    /// <summary>Adds a product to a visitor cart or increases the existing quantity.</summary>
    /// <response code="200">Returns the updated visitor cart.</response>
    /// <response code="400">The product quantity is invalid.</response>
    /// <response code="404">The requested product does not exist.</response>
    [HttpPost("items")]
    [ProducesResponseType(typeof(CartDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CartDto>> AddItem(AddCartItemRequest request, CancellationToken cancellationToken)
    {
        if (request.Quantity <= 0)
        {
            return BadRequest("Quantity must be greater than zero.");
        }

        var productExists = await dbContext.Products.AnyAsync(product => product.Id == request.ProductId, cancellationToken);
        if (!productExists)
        {
            return NotFound();
        }

        var visitorEmail = ResolveVisitorEmail(request.VisitorEmail);
        var cartItem = await dbContext.CartItems.SingleOrDefaultAsync(item => item.VisitorEmail == visitorEmail && item.ProductId == request.ProductId, cancellationToken);
        if (cartItem is null)
        {
            dbContext.CartItems.Add(new CartItem
            {
                VisitorEmail = visitorEmail,
                ProductId = request.ProductId,
                Quantity = request.Quantity,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
        }
        else
        {
            cartItem.Quantity += request.Quantity;
            cartItem.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(await BuildCartDtoAsync(visitorEmail, cancellationToken));
    }

    /// <summary>Updates the quantity of a cart line item.</summary>
    /// <response code="200">Returns the updated visitor cart.</response>
    /// <response code="400">The product quantity is invalid.</response>
    /// <response code="404">The requested cart line item does not exist.</response>
    [HttpPut("items/{cartItemId:int}")]
    [ProducesResponseType(typeof(CartDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CartDto>> UpdateItem(int cartItemId, UpdateCartItemRequest request, CancellationToken cancellationToken)
    {
        if (request.Quantity <= 0)
        {
            return BadRequest("Quantity must be greater than zero.");
        }

        var cartItem = await dbContext.CartItems.FindAsync([cartItemId], cancellationToken);
        if (cartItem is null)
        {
            return NotFound();
        }

        cartItem.Quantity = request.Quantity;
        cartItem.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(await BuildCartDtoAsync(cartItem.VisitorEmail, cancellationToken));
    }

    /// <summary>Removes a cart line item.</summary>
    /// <response code="204">The cart line item was removed.</response>
    /// <response code="404">The requested cart line item does not exist.</response>
    [HttpDelete("items/{cartItemId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveItem(int cartItemId, CancellationToken cancellationToken)
    {
        var cartItem = await dbContext.CartItems.FindAsync([cartItemId], cancellationToken);
        if (cartItem is null)
        {
            return NotFound();
        }

        dbContext.CartItems.Remove(cartItem);
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<CartDto> BuildCartDtoAsync(string visitorEmail, CancellationToken cancellationToken)
    {
        var resolvedEmail = ResolveVisitorEmail(visitorEmail);
        var items = await dbContext.CartItems
            .Include(item => item.Product)
            .Where(item => item.VisitorEmail == resolvedEmail)
            .OrderBy(item => item.Product!.Name)
            .Select(item => new CartItemDto(item.Id, item.ProductId, item.Product!.Name, item.Product.Price, item.Quantity, item.Product.Price * item.Quantity, item.Product.ImageUrl))
            .ToListAsync(cancellationToken);

        return new CartDto(resolvedEmail, items, items.Sum(item => item.Subtotal));
    }

    private string ResolveVisitorEmail(string visitorEmail)
    {
        return string.IsNullOrWhiteSpace(visitorEmail)
            ? configuration["NotificationEmails:DefaultVisitorEmail"] ?? "bmacha2015@gmail.com"
            : visitorEmail.Trim();
    }
}
