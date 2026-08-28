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

    /// <summary>Places an order and durably queues admin and visitor notifications.</summary>
    /// <remarks>Send the visitor email after adding cart items. The order, cart removal, notification job, and Outbox command commit atomically. An optional X-Idempotency-Key safely replays the original order response.</remarks>
    /// <response code="201">The order was placed. Response headers describe its asynchronous notification handoff.</response>
    /// <response code="400">The visitor identity or idempotency key is invalid, or the visitor cart is empty.</response>
    /// <response code="409">The idempotency key was already used for a different checkout request.</response>
    /// <response code="503">The durable notification job store is at capacity or temporarily unavailable.</response>
    [HttpPost]
    [ProducesResponseType(typeof(OrderDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<OrderDto>> CreateOrder(
        CreateOrderRequest request,
        [FromHeader(Name = IdempotencyContext.HeaderName)] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var requestedVersion = HttpContext.GetRequestedApiVersion();
        var actor = string.IsNullOrWhiteSpace(request.VisitorEmail)
            ? "default-visitor"
            : request.VisitorEmail.Trim().ToUpperInvariant();
        var idempotency = IdempotencyContext.Create(
            $"orders.create.v{requestedVersion?.MajorVersion ?? 2}",
            actor,
            idempotencyKey);
        var result = await orderService.CreateOrderAsync(request, idempotency, cancellationToken);
        if (result.IdempotencyReplayed)
        {
            Response.Headers[IdempotencyContext.ReplayHeaderName] = "true";
        }

        var order = result.Order;
        var location = requestedVersion is null
            ? $"/api/orders/{order.Id}"
            : $"/api/v{requestedVersion.MajorVersion}/orders/{order.Id}";

        Response.Headers["X-Notification-Handoff"] = result.NotificationHandoff.ToString();
        if (result.NotificationJob is { } job)
        {
            var statusLocation = requestedVersion is null
                ? $"/api/notifications/bulk/{job.JobId}"
                : $"/api/v{requestedVersion.MajorVersion}/notifications/bulk/{job.JobId}";
            Response.Headers["X-Notification-Job-ID"] = job.JobId.ToString("D");
            Response.Headers["X-Correlation-ID"] = job.CorrelationId;
            Response.Headers["Link"] = $"<{statusLocation}>; rel=\"notification-status\"";
        }

        return Created(location, order);
    }

    /// <summary>Cancels an order on behalf of its visitor and notifies the admin.</summary>
    /// <remarks>Pass the order id in the route and the owning visitor's email in the request body. The operation changes only the overall order status to Cancelled, preserves payment, fulfillment, and delivery progress, and creates one in-app notification for the admin. Repeating an already completed cancellation does not create another notification.</remarks>
    /// <response code="200">Returns the cancelled order.</response>
    /// <response code="400">The visitor email is invalid.</response>
    /// <response code="404">The order does not exist or does not belong to the supplied visitor.</response>
    [HttpPatch("{id:int}/cancel")]
    [ProducesResponseType(typeof(OrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDto>> CancelOrder(
        int id,
        CancelOrderRequest request,
        CancellationToken cancellationToken)
    {
        var order = await orderService.CancelOrderAsync(id, request, cancellationToken);
        return order is null ? NotFound() : Ok(order);
    }

    /// <summary>Updates order tracking statuses and notifies the visitor and admin.</summary>
    /// <remarks>Pass the order id in the route and send all four status values in the request body. This administrative operation saves the order, payment, fulfillment, and delivery values and creates in-app notifications for both the visitor who placed the order and the admin.</remarks>
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
