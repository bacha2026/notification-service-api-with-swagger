using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSA.Application.Abstractions;
using NSA.Application.Contracts;
using NSA.Application.Exceptions;
using NSA.Domain.Entities;
using NSA.Persistence;

namespace NSA.Service;

public sealed class BulkNotificationJobService(
    NotificationDbContext dbContext,
    IBulkNotificationCommandPublisher publisher,
    IOptions<BulkNotificationOptions> options,
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

        if (request.Notifications.Count > options.Value.MaxBatchSize)
        {
            throw new RequestValidationException(
                $"A bulk notification job cannot contain more than {options.Value.MaxBatchSize} notifications.");
        }

        await RemoveExpiredJobsAsync(cancellationToken);
        var activeJobCount = await dbContext.BulkNotificationJobs
            .CountAsync(job => !BulkNotificationJobStatuses.Terminal.Contains(job.Status), cancellationToken);
        if (activeJobCount >= options.Value.MaxTrackedJobs)
        {
            throw new ServiceUnavailableException("The bulk notification service is at capacity. Try again later.");
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

        dbContext.BulkNotificationJobs.Add(job);
        await dbContext.SaveChangesAsync(cancellationToken);

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
                await dbContext.SaveChangesAsync(CancellationToken.None);
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
            throw new ServiceUnavailableException("The bulk notification broker is unavailable. Try again later.");
        }

        return ToDto(job);
    }

    public async Task<BulkNotificationJobDto?> GetStatusAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await RemoveExpiredJobsAsync(cancellationToken);
        var job = await dbContext.BulkNotificationJobs
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == jobId, cancellationToken);
        return job is null ? null : ToDto(job);
    }

    private async Task RemoveExpiredJobsAsync(CancellationToken cancellationToken)
    {
        var cutoff = timeProvider.GetUtcNow().AddMinutes(-options.Value.CompletedJobRetentionMinutes);
        var expired = await dbContext.BulkNotificationJobs
            .Where(job => job.CompletedAtUtc != null && job.CompletedAtUtc < cutoff)
            .ToListAsync(cancellationToken);
        if (expired.Count == 0)
        {
            return;
        }

        dbContext.BulkNotificationJobs.RemoveRange(expired);
        await dbContext.SaveChangesAsync(cancellationToken);
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
