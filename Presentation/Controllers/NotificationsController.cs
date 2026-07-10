using Microsoft.AspNetCore.Mvc;
using NSA.Application.Abstractions;
using NSA.Application.Contracts;

namespace NSA.Presentation.Controllers;

[ApiController]
[Route("api/notifications")]
[Produces("application/json")]
public sealed class NotificationsController(INotificationService notificationService) : ControllerBase
{
    /// <summary>Gets notifications, optionally filtered by recipient email or order id.</summary>
    /// <response code="200">Returns matching notifications.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<NotificationDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<NotificationDto>>> GetNotifications([FromQuery] string? recipientEmail, [FromQuery] int? orderId, CancellationToken cancellationToken)
    {
        var notifications = await notificationService.GetNotificationsAsync(recipientEmail, orderId, cancellationToken);
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
        var notification = await notificationService.GetNotificationAsync(id, cancellationToken);
        return notification is null ? NotFound() : Ok(notification);
    }

    /// <summary>Creates a notification record.</summary>
    /// <response code="201">The notification was created.</response>
    /// <response code="400">The notification request is invalid.</response>
    [HttpPost]
    [ProducesResponseType(typeof(NotificationDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<NotificationDto>> CreateNotification(CreateNotificationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var notification = await notificationService.CreateNotificationAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetNotification), new { id = notification.Id }, notification);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
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
        try
        {
            var notification = await notificationService.UpdateNotificationAsync(id, request, cancellationToken);
            return notification is null ? NotFound() : Ok(notification);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    /// <summary>Deletes a notification record.</summary>
    /// <response code="204">The notification was deleted.</response>
    /// <response code="404">The requested notification does not exist.</response>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteNotification(int id, CancellationToken cancellationToken)
    {
        var deleted = await notificationService.DeleteNotificationAsync(id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}
