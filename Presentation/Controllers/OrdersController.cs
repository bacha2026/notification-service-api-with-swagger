using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSA.Application.Abstractions;
using NSA.Application.Contracts;
using NSA.Domain.Entities;
using NSA.Domain.Enums;
using NSA.Persistence;

namespace NSA.Presentation.Controllers;

[ApiController]
[Route("api/orders")]
[Produces("application/json")]
public sealed class OrdersController(NotificationDbContext dbContext, INotificationDispatcher notificationDispatcher, IConfiguration configuration) : ControllerBase
{
    /// <summary>Gets all orders for a visitor, including product names, prices, quantities, subtotals, totals, and statuses.</summary>
    /// <response code="200">Returns the visitor order history.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<OrderDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<OrderDto>>> GetOrders([FromQuery] string visitorEmail, CancellationToken cancellationToken)
    {
        var email = ResolveVisitorEmail(visitorEmail);
        var orders = await dbContext.Orders
            .Include(order => order.Items)
            .Where(order => order.VisitorEmail == email)
            .OrderByDescending(order => order.CreatedAtUtc)
            .Select(order => ToDto(order))
            .ToListAsync(cancellationToken);

        return Ok(orders);
    }

    /// <summary>Gets one order with current order, payment, fulfillment, and delivery statuses.</summary>
    /// <response code="200">Returns the requested order.</response>
    /// <response code="404">The requested order does not exist.</response>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(OrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDto>> GetOrder(int id, CancellationToken cancellationToken)
    {
        var order = await dbContext.Orders.Include(order => order.Items).SingleOrDefaultAsync(order => order.Id == id, cancellationToken);
        return order is null ? NotFound() : Ok(ToDto(order));
    }

    /// <summary>Places an order from the visitor cart and notifies both the admin and visitor by email notification.</summary>
    /// <response code="201">The order was placed and notifications were created.</response>
    /// <response code="400">The visitor cart is empty.</response>
    [HttpPost]
    [ProducesResponseType(typeof(OrderDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<OrderDto>> CreateOrder(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        var visitorEmail = ResolveVisitorEmail(request.VisitorEmail);
        var cartItems = await dbContext.CartItems.Include(item => item.Product).Where(item => item.VisitorEmail == visitorEmail).ToListAsync(cancellationToken);
        if (cartItems.Count == 0)
        {
            return BadRequest("The cart is empty.");
        }

        var order = new Order
        {
            VisitorEmail = visitorEmail,
            OrderStatus = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Unpaid,
            FulfillmentStatus = FulfillmentStatus.NotStarted,
            DeliveryStatus = DeliveryStatus.WaitingForRider,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        foreach (var cartItem in cartItems)
        {
            var subtotal = cartItem.Product!.Price * cartItem.Quantity;
            order.Items.Add(new OrderItem
            {
                ProductId = cartItem.ProductId,
                ProductName = cartItem.Product.Name,
                UnitPrice = cartItem.Product.Price,
                Quantity = cartItem.Quantity,
                Subtotal = subtotal
            });
        }

        order.TotalAmount = order.Items.Sum(item => item.Subtotal);
        dbContext.Orders.Add(order);
        dbContext.CartItems.RemoveRange(cartItems);
        await dbContext.SaveChangesAsync(cancellationToken);

        var body = BuildOrderMessage(order);
        await notificationDispatcher.CreateEmailNotificationAsync(AdminEmail, $"New order #{order.Id}", body, order.Id, cancellationToken);
        await notificationDispatcher.CreateEmailNotificationAsync(visitorEmail, $"Order #{order.Id} received", body, order.Id, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetOrder), new { id = order.Id }, ToDto(order));
    }

    /// <summary>Updates order tracking statuses and notifies the visitor and admin.</summary>
    /// <response code="200">Returns the updated order.</response>
    /// <response code="404">The requested order does not exist.</response>
    [HttpPatch("{id:int}/status")]
    [ProducesResponseType(typeof(OrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDto>> UpdateStatus(int id, UpdateOrderStatusRequest request, CancellationToken cancellationToken)
    {
        var order = await dbContext.Orders.Include(order => order.Items).SingleOrDefaultAsync(order => order.Id == id, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        order.OrderStatus = request.OrderStatus;
        order.PaymentStatus = request.PaymentStatus;
        order.FulfillmentStatus = request.FulfillmentStatus;
        order.DeliveryStatus = request.DeliveryStatus;
        order.UpdatedAtUtc = DateTimeOffset.UtcNow;

        var body = BuildOrderMessage(order);
        await notificationDispatcher.CreateEmailNotificationAsync(AdminEmail, $"Order #{order.Id} status updated", body, order.Id, cancellationToken);
        await notificationDispatcher.CreateEmailNotificationAsync(order.VisitorEmail, $"Order #{order.Id} status updated", body, order.Id, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToDto(order));
    }

    private string AdminEmail => configuration["NotificationEmails:AdminEmail"] ?? "bmacha2026@gmail.com";

    private string ResolveVisitorEmail(string visitorEmail)
    {
        return string.IsNullOrWhiteSpace(visitorEmail)
            ? configuration["NotificationEmails:DefaultVisitorEmail"] ?? "bmacha2015@gmail.com"
            : visitorEmail.Trim();
    }

    private static OrderDto ToDto(Order order)
    {
        return new OrderDto(
            order.Id,
            order.VisitorEmail,
            order.OrderStatus,
            order.PaymentStatus,
            order.FulfillmentStatus,
            order.DeliveryStatus,
            order.TotalAmount,
            order.CreatedAtUtc,
            order.Items.Select(item => new OrderItemDto(item.ProductId, item.ProductName, item.UnitPrice, item.Quantity, item.Subtotal)).ToList());
    }

    private static string BuildOrderMessage(Order order)
    {
        var lines = order.Items.Select(item => $"{item.ProductName} x {item.Quantity} @ {item.UnitPrice:C} = {item.Subtotal:C}");
        return $"Order #{order.Id} for {order.VisitorEmail}. Status: {order.OrderStatus}; Payment: {order.PaymentStatus}; Fulfillment: {order.FulfillmentStatus}; Delivery: {order.DeliveryStatus}. Total: {order.TotalAmount:C}. Items: {string.Join("; ", lines)}";
    }
}
