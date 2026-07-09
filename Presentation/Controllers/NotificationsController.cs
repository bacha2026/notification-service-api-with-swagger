using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSA.Application.Contracts;
using NSA.Domain.Entities;
using NSA.Persistence;

namespace NSA.Presentation.Controllers;

[ApiController]
[Route("api/notifications")]
[Produces("application/json")]
public sealed class NotificationsController(NotificationDbContext dbContext) : ControllerBase
{
    /// <summary>Gets notifications, optionally filtered by recipient email or order id.</summary>
    /// <response code="200">Returns matching notifications.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<NotificationDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<NotificationDto>>> GetNotifications([FromQuery] string? recipientEmail, [FromQuery] int? orderId, CancellationToken cancellationToken)
    {
        var query = dbContext.Notifications.AsQueryable();
        if (!string.IsNullOrWhiteSpace(recipientEmail))
        {
            query = query.Where(notification => notification.RecipientEmail == recipientEmail.Trim());
        }

        if (orderId.HasValue)
        {
            query = query.Where(notification => notification.OrderId == orderId.Value);
        }

        var notifications = await query.OrderByDescending(notification => notification.CreatedAtUtc).Select(notification => ToDto(notification)).ToListAsync(cancellationToken);
        return Ok(notifications);
    }

    /// <summary>Gets one notification by id.</summary>
    /// <response code="200">Returns the requested notification.</response>
    /// <response code="404">The requested notification does not exist.</response>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(NotificationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NotificationDto>> GetNotification(int id, CancellationToken cancellationToken)
    {
        var notification = await dbContext.Notifications.FindAsync([id], cancellationToken);
        return notification is null ? NotFound() : Ok(ToDto(notification));
    }

    /// <summary>Creates a notification record.</summary>
    /// <response code="201">The notification was created.</response>
    /// <response code="400">The notification request is invalid.</response>
    [HttpPost]
    [ProducesResponseType(typeof(NotificationDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<NotificationDto>> CreateNotification(CreateNotificationRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RecipientEmail) || string.IsNullOrWhiteSpace(request.Subject) || string.IsNullOrWhiteSpace(request.Body))
        {
            return BadRequest("RecipientEmail, Subject, and Body are required.");
        }

        var notification = new Notification
        {
            RecipientEmail = request.RecipientEmail.Trim(),
            Channel = request.Channel,
            Subject = request.Subject.Trim(),
            Body = request.Body.Trim(),
            OrderId = request.OrderId,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            IsRead = false
        };

        dbContext.Notifications.Add(notification);
        await dbContext.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetNotification), new { id = notification.Id }, ToDto(notification));
    }

    /// <summary>Updates a notification record.</summary>
    /// <response code="200">The notification was updated.</response>
    /// <response code="400">The notification request is invalid.</response>
    /// <response code="404">The requested notification does not exist.</response>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(NotificationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NotificationDto>> UpdateNotification(int id, UpdateNotificationRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RecipientEmail) || string.IsNullOrWhiteSpace(request.Subject) || string.IsNullOrWhiteSpace(request.Body))
        {
            return BadRequest("RecipientEmail, Subject, and Body are required.");
        }

        var notification = await dbContext.Notifications.FindAsync([id], cancellationToken);
        if (notification is null)
        {
            return NotFound();
        }

        notification.RecipientEmail = request.RecipientEmail.Trim();
        notification.Channel = request.Channel;
        notification.Subject = request.Subject.Trim();
        notification.Body = request.Body.Trim();
        notification.OrderId = request.OrderId;
        notification.IsRead = request.IsRead;

        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToDto(notification));
    }

    /// <summary>Deletes a notification record.</summary>
    /// <response code="204">The notification was deleted.</response>
    /// <response code="404">The requested notification does not exist.</response>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteNotification(int id, CancellationToken cancellationToken)
    {
        var notification = await dbContext.Notifications.FindAsync([id], cancellationToken);
        if (notification is null)
        {
            return NotFound();
        }

        dbContext.Notifications.Remove(notification);
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private static NotificationDto ToDto(Notification notification)
    {
        return new NotificationDto(notification.Id, notification.RecipientEmail, notification.Channel, notification.Subject, notification.Body, notification.OrderId, notification.IsRead, notification.CreatedAtUtc, notification.SentAtUtc);
    }
}
