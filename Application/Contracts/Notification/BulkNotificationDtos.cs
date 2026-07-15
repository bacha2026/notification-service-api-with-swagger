using System.ComponentModel.DataAnnotations;
using NSA.Domain.Enums;

namespace NSA.Application.Contracts;

/// <summary>A single notification included in a bulk notification job.</summary>
public sealed record BulkNotificationItemRequest
{
    public BulkNotificationItemRequest()
    {
    }

    public BulkNotificationItemRequest(string recipientEmail, NotificationChannel channel, string subject, string body, int? orderId)
    {
        RecipientEmail = recipientEmail;
        Channel = channel;
        Subject = subject;
        Body = body;
        OrderId = orderId;
    }

    /// <summary>Email address of the intended recipient.</summary>
    [Required, EmailAddress, StringLength(320, MinimumLength = 1)]
    public string RecipientEmail { get; init; } = string.Empty;

    /// <summary>Channel to use for delivery.</summary>
    [Required, EnumDataType(typeof(NotificationChannel))]
    public NotificationChannel Channel { get; init; }

    /// <summary>Notification subject or title.</summary>
    [Required, StringLength(200, MinimumLength = 1)]
    public string Subject { get; init; } = string.Empty;

    /// <summary>Notification message content.</summary>
    [Required, StringLength(4000, MinimumLength = 1)]
    public string Body { get; init; } = string.Empty;

    /// <summary>Related order identifier, when applicable.</summary>
    [Range(1, int.MaxValue)]
    public int? OrderId { get; init; }
}

/// <summary>Request to queue a collection of notifications for background processing.</summary>
public sealed record CreateBulkNotificationsRequest
{
    public CreateBulkNotificationsRequest()
    {
    }

    public CreateBulkNotificationsRequest(IReadOnlyList<BulkNotificationItemRequest> notifications)
    {
        Notifications = notifications;
    }

    /// <summary>Notifications to process; between 1 and 100 items are allowed.</summary>
    [Required, MinLength(1), MaxLength(100)]
    public IReadOnlyList<BulkNotificationItemRequest> Notifications { get; init; } = Array.Empty<BulkNotificationItemRequest>();
}

/// <summary>Progress and outcome of a queued bulk notification job.</summary>
public sealed record BulkNotificationJobDto
{
    public BulkNotificationJobDto(Guid jobId, string status, int totalCount, int processedCount, int succeededCount, int failedCount, DateTimeOffset queuedAtUtc, DateTimeOffset? startedAtUtc, DateTimeOffset? completedAtUtc, string? error) =>
        (JobId, Status, TotalCount, ProcessedCount, SucceededCount, FailedCount, QueuedAtUtc, StartedAtUtc, CompletedAtUtc, Error) = (jobId, status, totalCount, processedCount, succeededCount, failedCount, queuedAtUtc, startedAtUtc, completedAtUtc, error);

    /// <summary>Unique identifier used to query the job.</summary>
    public Guid JobId { get; init; }
    /// <summary>Current job status.</summary>
    public string Status { get; init; }
    /// <summary>Total number of notifications submitted.</summary>
    public int TotalCount { get; init; }
    /// <summary>Number of notifications processed so far.</summary>
    public int ProcessedCount { get; init; }
    /// <summary>Number of notifications processed successfully.</summary>
    public int SucceededCount { get; init; }
    /// <summary>Number of notifications that failed.</summary>
    public int FailedCount { get; init; }
    /// <summary>Date and time when the job was queued, in UTC.</summary>
    public DateTimeOffset QueuedAtUtc { get; init; }
    /// <summary>Date and time when processing started, in UTC; otherwise null.</summary>
    public DateTimeOffset? StartedAtUtc { get; init; }
    /// <summary>Date and time when processing finished, in UTC; otherwise null.</summary>
    public DateTimeOffset? CompletedAtUtc { get; init; }
    /// <summary>Job-level error message when processing could not complete; otherwise null.</summary>
    public string? Error { get; init; }
}
