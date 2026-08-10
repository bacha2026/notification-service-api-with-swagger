using NSA.Application.Abstractions;
using NSA.Application.Configuration;
using NSA.Application.Contracts;
using NSA.Application.Exceptions;
using NSA.Domain.Entities;

namespace NSA.Service;

public sealed class BulkNotificationJobService(
    IBulkNotificationJobRepository jobs,
    IBulkNotificationCommandPublisher publisher,
    BulkNotificationSettings settings,
    TimeProvider timeProvider,
    ILogger<BulkNotificationJobService> logger) : IBulkNotificationJobService
{
    public async Task<BulkNotificationJobDto> QueueAsync(
        CreateBulkNotificationsRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (request.Notifications is null || request.Notifications.Count == 0)
        {
            throw new RequestValidationException("At least one notification is required.");
        }

        if (request.Notifications.Count > settings.MaxBatchSize)
        {
            throw new RequestValidationException(
                $"A bulk notification job cannot contain more than {settings.MaxBatchSize} notifications.");
        }

        await RemoveExpiredJobsAsync(cancellationToken);
        var activeJobCount = await jobs.CountActiveAsync(cancellationToken);
        if (activeJobCount >= settings.MaxTrackedJobs)
        {
            throw new BulkNotificationCapacityException(
                "The bulk notification service is at capacity. Try again later.");
        }

        var now = timeProvider.GetUtcNow();
        var jobId = Guid.NewGuid();
        var normalizedCorrelationId = string.IsNullOrWhiteSpace(correlationId)
            ? jobId.ToString("N")
            : correlationId.Trim()[..Math.Min(correlationId.Trim().Length, 128)];
        var job = new BulkNotificationJob
        {
            Id = jobId,
            Status = BulkNotificationJobStatuses.Queued,
            MessageSchemaVersion = BulkNotificationRequestedV1.CurrentSchemaVersion,
            CorrelationId = normalizedCorrelationId,
            TotalCount = request.Notifications.Count,
            QueuedAtUtc = now,
            Items = request.Notifications.Select((item, index) => new BulkNotificationJobItem
            {
                Sequence = index,
                RecipientEmail = item.RecipientEmail.Trim(),
                Channel = item.Channel,
                Subject = item.Subject.Trim(),
                Body = item.Body.Trim(),
                OrderId = item.OrderId,
                Status = BulkNotificationItemStatuses.Pending
            }).ToList()
        };

        jobs.Add(job);
        await jobs.SaveChangesAsync(cancellationToken);

        var command = new BulkNotificationRequestedV1(
            BulkNotificationRequestedV1.CurrentSchemaVersion,
            Guid.NewGuid(),
            job.Id,
            job.CorrelationId,
            now);

        try
        {
            await publisher.PublishAsync(command, cancellationToken);
        }
        catch (Exception exception)
        {
            job.Status = BulkNotificationJobStatuses.PublishFailed;
            job.CompletedAtUtc = timeProvider.GetUtcNow();
            job.Error = "The persisted job could not be published to the message broker.";

            try
            {
                await jobs.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception statusException)
            {
                logger.LogError(
                    statusException,
                    "Bulk notification job {JobId} publish failure could not be recorded. CorrelationId: {CorrelationId}",
                    job.Id,
                    job.CorrelationId);
            }

            logger.LogError(
                exception,
                "Bulk notification job {JobId} could not be published. CorrelationId: {CorrelationId}",
                job.Id,
                job.CorrelationId);
            throw new BulkNotificationPublishException(
                "The bulk notification broker handoff could not be confirmed. Try again later.",
                ToDto(job));
        }

        return ToDto(job);
    }

    public async Task<BulkNotificationJobDto?> GetStatusAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await RemoveExpiredJobsAsync(cancellationToken);
        var job = await jobs.GetByIdAsync(jobId, cancellationToken);
        return job is null ? null : ToDto(job);
    }

    private async Task RemoveExpiredJobsAsync(CancellationToken cancellationToken)
    {
        var cutoff = timeProvider.GetUtcNow().AddMinutes(-settings.CompletedJobRetentionMinutes);
        var expired = await jobs.GetCompletedBeforeAsync(cutoff, cancellationToken);
        if (expired.Count == 0)
        {
            return;
        }

        jobs.RemoveRange(expired);
        await jobs.SaveChangesAsync(cancellationToken);
    }

    internal static BulkNotificationJobDto ToDto(BulkNotificationJob job) =>
        new(
            job.Id,
            job.Status,
            job.TotalCount,
            job.ProcessedCount,
            job.SucceededCount,
            job.FailedCount,
            job.QueuedAtUtc,
            job.StartedAtUtc,
            job.CompletedAtUtc,
            job.Error,
            job.CorrelationId);
}
