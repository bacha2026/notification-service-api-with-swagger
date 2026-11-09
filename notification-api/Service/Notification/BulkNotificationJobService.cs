using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NSA.Application.Abstractions;
using NSA.Application.Configuration;
using NSA.Application.Contracts;
using NSA.Application.Exceptions;
using NSA.Domain.Entities;
using NSA.Persistence;

namespace NSA.Service;

public sealed class BulkNotificationJobService(
    IBulkNotificationJobRepository jobs,
    NotificationDbContext dbContext,
    IdempotencyStore idempotencyStore,
    BulkNotificationSettings settings,
    TimeProvider timeProvider) : IBulkNotificationJobService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<BulkNotificationJobDto> QueueAsync(
        CreateBulkNotificationsRequest request,
        string correlationId,
        CancellationToken cancellationToken) =>
        (await QueueAsync(request, correlationId, idempotency: null, cancellationToken)).Value;

    public async Task<IdempotentResult<BulkNotificationJobDto>> QueueAsync(
        CreateBulkNotificationsRequest request,
        string correlationId,
        IdempotencyContext? idempotency,
        CancellationToken cancellationToken)
    {
        if (request.Notifications is null || request.Notifications.Count == 0) throw new RequestValidationException("At least one notification is required.");
     

        if (request.Notifications.Count > settings.MaxBatchSize) throw new RequestValidationException($"A bulk notification job cannot contain more than {settings.MaxBatchSize} notifications.");
        
        //Removes leading and trailing whitespace from the email, subject, and body and converts the result into a consistent BulkNotificationItemRequest
        var normalizedItems = request.Notifications.Select(item => new BulkNotificationItemRequest(
            item.RecipientEmail.Trim(),
            item.Channel,
            item.Subject.Trim(),
            item.Body.Trim(),
            item.OrderId)).ToArray();
        //The email is converted to uppercase before hashing so email casing does not cause different idempotency hashes
        var requestHash = IdempotencyRequestHasher.Hash(normalizedItems.Select(item => new
        {
            RecipientEmail = item.RecipientEmail.ToUpperInvariant(),
            item.Channel,
            item.Subject,
            item.Body,
            item.OrderId
        }));

        await using var transaction = await idempotencyStore.BeginSerializableTransactionAsync(cancellationToken);
        await idempotencyStore.PurgeExpiredAsync(idempotency, cancellationToken);// purges expired idempotency records to prevent stale data from affecting the current operation
        await idempotencyStore.AcquireLockAsync(idempotency, cancellationToken); // acquires a lock for the current idempotency scope to prevent concurrent requests with the same idempotency key from creating duplicate jobs
        // replays a previously stored result using the request hash. If a replay is found, the method commits the transaction and returns the original result instead of creating a duplicate job
        var replay = await idempotencyStore.TryReplayAsync<BulkNotificationJobDto>(
            idempotency,
            requestHash,
            cancellationToken);

        if (replay is not null)
        {
            await CommitAsync(transaction, cancellationToken);// if replay is found, commit the transaction and return the original result instead of creating a duplicate job
            return replay;
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
        var messageId = Guid.NewGuid();

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
            Items = normalizedItems.Select((item, index) => new BulkNotificationJobItem
            {
                Sequence = index,
                RecipientEmail = item.RecipientEmail,
                Channel = item.Channel,
                Subject = item.Subject,
                Body = item.Body,
                OrderId = item.OrderId,
                Status = BulkNotificationItemStatuses.Pending
            }).ToList()
        };

        jobs.Add(job);

        var command = new BulkNotificationRequestedV1(
            BulkNotificationRequestedV1.CurrentSchemaVersion,
            messageId,
            job.Id,
            job.CorrelationId,
            now);
        // this is for durable broker command, Save job + outbox command in one transaction
        dbContext.OutboxMessages.Add(new OutboxMessage
        {
            Id = messageId,
            AggregateType = nameof(BulkNotificationJob),
            AggregateId = job.Id.ToString("D"),
            MessageType = BulkNotificationRequestedV1.MessageType,
            SchemaVersion = BulkNotificationRequestedV1.CurrentSchemaVersion,
            Payload = JsonSerializer.Serialize(command, SerializerOptions),
            // Legacy non-null columns remain for schema compatibility. The relay owns routing.
            Exchange = string.Empty,
            RoutingKey = string.Empty,
            CorrelationId = job.CorrelationId,
            OccurredAtUtc = now,
            NextAttemptAtUtc = now
        });

        var response = ToDto(job);
        // response is stored in the idempotency record to allow replaying the same result for subsequent requests with the same idempotency key and request hash
        // this is for durable HTTP request receipt and response.
        idempotencyStore.Add(
            idempotency,
            requestHash,
            StatusCodes.Status202Accepted,
            response,
            nameof(BulkNotificationJob),
            job.Id.ToString("D"));
        await jobs.SaveChangesAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);

        return new IdempotentResult<BulkNotificationJobDto>(response, Replayed: false);
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

        if (expired.Count == 0) return;
       
        var aggregateIds = expired.Select(job => job.Id.ToString("D")).ToArray();
        var jobIds = expired.Select(job => job.Id).ToArray();
        var expiredNotifications = await dbContext.Notifications.Where(notification => 
                                    notification.BulkNotificationJobId.HasValue  && 
                                    jobIds.Contains(notification.BulkNotificationJobId.Value))
                                    .ToListAsync(cancellationToken);
        var expiredOutbox = await dbContext.OutboxMessages.Where(message => message.AggregateType == nameof(BulkNotificationJob)
                && aggregateIds.Contains(message.AggregateId)).ToListAsync(cancellationToken);
        
        dbContext.Notifications.RemoveRange(expiredNotifications);
        dbContext.OutboxMessages.RemoveRange(expiredOutbox);
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

    private static Task CommitAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken) =>
        transaction is null ? Task.CompletedTask : transaction.CommitAsync(cancellationToken);
}
