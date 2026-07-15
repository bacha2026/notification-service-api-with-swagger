using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using NSA.Application.Abstractions;
using NSA.Application.Contracts;

namespace NSA.Presentation.Controllers;

[ApiController]
[ApiVersion("1.0", Deprecated = true)]
[ApiVersion("2.0")]
[Route("api/v{version:apiVersion}/notifications")]
[Route("api/notifications")]
public sealed class NotificationsController(INotificationService notificationService) : ControllerBase
{
    /// <summary>Gets notifications, optionally filtered by recipient email or order id.</summary>
    /// <remarks>Call without query parameters to list all notifications, or supply recipientEmail, orderId, or both to narrow the results. When both filters are supplied, only notifications matching both are returned.</remarks>
    /// <response code="200">Returns matching notifications.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<NotificationDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<NotificationDto>>> GetNotifications([FromQuery] string? recipientEmail, [FromQuery] int? orderId, CancellationToken cancellationToken)
    {
        var notifications = await notificationService.GetNotificationsAsync(recipientEmail, orderId, cancellationToken);
        return Ok(notifications);
    }

    /// <summary>Gets one notification by id.</summary>
    /// <remarks>Pass the notification id in the route. Use this operation when a client needs the complete current record after obtaining an id from the list or create endpoint.</remarks>
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
    /// <remarks>Send the recipient, delivery channel, subject, and body in the request. Include orderId only when the notification relates to an existing order. The Location response header identifies the created notification.</remarks>
    /// <response code="201">The notification was created.</response>
    /// <response code="400">The notification request is invalid.</response>
    [HttpPost]
    [ProducesResponseType(typeof(NotificationDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<NotificationDto>> CreateNotification(CreateNotificationRequest request, CancellationToken cancellationToken)
    {
        var notification = await notificationService.CreateNotificationAsync(request, cancellationToken);
        var requestedVersion = HttpContext.GetRequestedApiVersion();
        var location = requestedVersion is null
            ? $"/api/notifications/{notification.Id}"
            : $"/api/v{requestedVersion.MajorVersion}/notifications/{notification.Id}";

        return Created(location, notification);
    }

    /// <summary>Updates a notification record.</summary>
    /// <remarks>Pass the notification id in the route and send the complete replacement set of editable fields, including isRead. Omitted fields are not preserved because this is a full update.</remarks>
    /// <response code="200">The notification was updated.</response>
    /// <response code="400">The notification request is invalid.</response>
    /// <response code="404">The requested notification does not exist.</response>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(NotificationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NotificationDto>> UpdateNotification(int id, UpdateNotificationRequest request, CancellationToken cancellationToken)
    {
        var notification = await notificationService.UpdateNotificationAsync(id, request, cancellationToken);
        return notification is null ? NotFound() : Ok(notification);
    }

    /// <summary>Deletes a notification record.</summary>
    /// <remarks>Pass the notification id in the route. A successful request permanently deletes the record and returns no response body.</remarks>
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

    /// <summary>Queues a batch of notification records for asynchronous processing.</summary>
    /// <remarks>Send between 1 and 100 notifications in the request body. Processing continues in the background after the endpoint returns; save the returned jobId or Location header and use the bulk status endpoint to monitor progress.</remarks>
    /// <response code="202">The job was accepted. Use the status endpoint and returned job id to monitor it.</response>
    /// <response code="400">The batch is empty, too large, or contains an invalid notification.</response>
    /// <response code="503">The in-memory queue is temporarily at capacity.</response>
    [HttpPost("bulk")]
    [ProducesResponseType(typeof(BulkNotificationJobDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public ActionResult<BulkNotificationJobDto> CreateBulkNotifications(CreateBulkNotificationsRequest request, [FromServices] IBulkNotificationJobService bulkJobs)
    {
        var job = bulkJobs.Queue(request);
        var requestedVersion = HttpContext.GetRequestedApiVersion();
        var statusLocation = requestedVersion is null
            ? $"/api/notifications/bulk/{job.JobId}"
            : $"/api/v{requestedVersion.MajorVersion}/notifications/bulk/{job.JobId}";

        return Accepted(statusLocation, job);
    }

    /// <summary>Gets the current progress of an asynchronous bulk notification job.</summary>
    /// <remarks>Pass the jobId returned by the bulk creation endpoint. Poll this endpoint until the status is Completed or CompletedWithErrors, then inspect the succeeded and failed counters. Completed jobs are retained only temporarily.</remarks>
    /// <response code="200">Returns the job's current counters and lifecycle state.</response>
    /// <response code="404">The requested job does not exist or is no longer retained.</response>
    [HttpGet("bulk/{jobId:guid}")]
    [ProducesResponseType(typeof(BulkNotificationJobDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<BulkNotificationJobDto> GetBulkNotificationStatus(Guid jobId, [FromServices] IBulkNotificationJobService bulkJobs)
    {
        var job = bulkJobs.GetStatus(jobId);
        return job is null ? NotFound() : Ok(job);
    }
}
