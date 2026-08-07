using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using NSA.Application.Abstractions;
using NSA.Application.Contracts;

namespace NSA.Presentation.Controllers;

[ApiController]
[ApiVersion("1.0", Deprecated = true)]
[ApiVersion("2.0")]
[Route("api/v{version:apiVersion}/cart")]
[Route("api/cart")]
public sealed class CartController(ICartService cartService) : ControllerBase
{
    /// <summary>Gets the current cart for a visitor.</summary>
    /// <remarks>Pass the visitor's email address in the route. Use the returned cart item ids when updating or removing individual items.</remarks>
    /// <response code="200">Returns the visitor cart with line-item subtotals and total amount.</response>
    [HttpGet("{visitorEmail}")]
    [ProducesResponseType(typeof(CartDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CartDto>> GetCart(string visitorEmail, CancellationToken cancellationToken)
    {
        return Ok(await cartService.GetCartAsync(visitorEmail, cancellationToken));
    }

    /// <summary>Adds a product to a visitor cart or increases the existing quantity.</summary>
    /// <remarks>Send the visitor email, product id, and quantity in the request body. If the product is already in the visitor's cart, the supplied quantity is added to its current quantity.</remarks>
    /// <response code="200">Returns the updated visitor cart.</response>
    /// <response code="400">The product quantity is invalid.</response>
    /// <response code="404">The requested product does not exist.</response>
    [HttpPost("items")]
    [ProducesResponseType(typeof(CartDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CartDto>> AddItem(AddCartItemRequest request, CancellationToken cancellationToken)
    {
        var cart = await cartService.AddItemAsync(request, cancellationToken);
        return cart is null ? NotFound() : Ok(cart);
    }

    /// <summary>Updates the quantity of a cart line item.</summary>
    /// <remarks>Pass the cart item id—not the product id—in the route and send the desired total quantity in the request body. Use the cart lookup endpoint to find the cart item id.</remarks>
    /// <response code="200">Returns the updated visitor cart.</response>
    /// <response code="400">The product quantity is invalid.</response>
    /// <response code="404">The requested cart line item does not exist.</response>
    [HttpPut("items/{cartItemId:int}")]
    [ProducesResponseType(typeof(CartDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CartDto>> UpdateItem(int cartItemId, UpdateCartItemRequest request, CancellationToken cancellationToken)
    {
        var cart = await cartService.UpdateItemAsync(cartItemId, request, cancellationToken);
        return cart is null ? NotFound() : Ok(cart);
    }

    /// <summary>Removes a cart line item.</summary>
    /// <remarks>Pass the cart item id—not the product id—in the route. A successful request permanently removes that line from the visitor's cart and returns no response body.</remarks>
    /// <response code="204">The cart line item was removed.</response>
    /// <response code="404">The requested cart line item does not exist.</response>
    [HttpDelete("items/{cartItemId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveItem(int cartItemId, CancellationToken cancellationToken)
    {
        var removed = await cartService.RemoveItemAsync(cartItemId, cancellationToken);
        return removed ? NoContent() : NotFound();
    }
}
