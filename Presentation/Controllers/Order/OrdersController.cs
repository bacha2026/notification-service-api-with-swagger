using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using NSA.Application.Abstractions;
using NSA.Application.Contracts;

namespace NSA.Presentation.Controllers;

[ApiController]
[ApiVersion("1.0", Deprecated = true)]
[ApiVersion("2.0")]
[Route("api/v{version:apiVersion}/orders")]
[Route("api/orders")]
public sealed class OrdersController(IOrderService orderService) : ControllerBase
{
    /// <summary>Gets all orders for a visitor, including product names, prices, quantities, subtotals, totals, and statuses.</summary>
    /// <remarks>Supply the visitor's email address in the visitorEmail query parameter. Use this endpoint for order history and the returned order ids for detailed tracking.</remarks>
    /// <response code="200">Returns the visitor order history.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<OrderDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<OrderDto>>> GetOrders([FromQuery] string visitorEmail, CancellationToken cancellationToken)
    {
        var orders = await orderService.GetOrdersAsync(visitorEmail, cancellationToken);
        return Ok(orders);
    }

    /// <summary>Gets one order with current order, payment, fulfillment, and delivery statuses.</summary>
    /// <remarks>Pass the order id in the route to retrieve its line items, total, and latest tracking statuses. Poll this endpoint when a client needs refreshed order progress.</remarks>
    /// <response code="200">Returns the requested order.</response>
    /// <response code="404">The requested order does not exist.</response>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(OrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDto>> GetOrder(int id, CancellationToken cancellationToken)
    {
        var order = await orderService.GetOrderAsync(id, cancellationToken);
        return order is null ? NotFound() : Ok(order);
    }

    /// <summary>Places an order from the visitor cart and notifies both the admin and visitor by email notification.</summary>
    /// <remarks>Send the visitor email in the request body after the visitor has added items to their cart. The current cart becomes the new order, so the request fails when that cart is empty. The Location response header identifies the created order.</remarks>
    /// <response code="201">The order was placed and notifications were created.</response>
    /// <response code="400">The visitor cart is empty.</response>
    [HttpPost]
    [ProducesResponseType(typeof(OrderDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<OrderDto>> CreateOrder(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        var order = await orderService.CreateOrderAsync(request, cancellationToken);
        var requestedVersion = HttpContext.GetRequestedApiVersion();
        var location = requestedVersion is null
            ? $"/api/orders/{order.Id}"
            : $"/api/v{requestedVersion.MajorVersion}/orders/{order.Id}";

        return Created(location, order);
    }

    /// <summary>Updates order tracking statuses and notifies the visitor and admin.</summary>
    /// <remarks>Pass the order id in the route and send all four status values in the request body. Use this administrative operation whenever payment, fulfillment, or delivery progress changes; it also creates notifications for the visitor and admin.</remarks>
    /// <response code="200">Returns the updated order.</response>
    /// <response code="400">One or more status values are invalid.</response>
    /// <response code="404">The requested order does not exist.</response>
    [HttpPatch("{id:int}/status")]
    [ProducesResponseType(typeof(OrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDto>> UpdateStatus(int id, UpdateOrderStatusRequest request, CancellationToken cancellationToken)
    {
        var order = await orderService.UpdateStatusAsync(id, request, cancellationToken);
        return order is null ? NotFound() : Ok(order);
    }
}
