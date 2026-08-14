using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSA.Application.Abstractions;
using NSA.Application.Contracts;
using NSA.Application.Exceptions;
using NSA.Domain.Entities;
using NSA.Domain.Enums;
using NSA.Infrastructure.Messaging;
using NSA.Persistence;
using NSA.Persistence.Concrete;
using NSA.Service;

namespace NSA.Tests;

public sealed class BulkNotificationProcessorTests
{
    [Fact]
    public async Task Processor_persists_progress_and_completes_a_successful_job()
    {
        await using var context = CreateContext();
        var job = CreateJob("first", "second");
        context.BulkNotificationJobs.Add(job);
        await context.SaveChangesAsync();
        var dispatcher = new ControlledNotificationService();
        var processor = CreateProcessor(context, dispatcher);

        await processor.ProcessAsync(job.Id, CancellationToken.None);

        Assert.Equal(BulkNotificationJobStatuses.Completed, job.Status);
        Assert.Equal(2, job.ProcessedCount);
        Assert.Equal(2, job.SucceededCount);
        Assert.Equal(0, job.FailedCount);
        Assert.NotNull(job.StartedAtUtc);
        Assert.NotNull(job.CompletedAtUtc);
        Assert.All(job.Items, item => Assert.Equal(BulkNotificationItemStatuses.Succeeded, item.Status));
        Assert.Equal(new[] { "first", "second" }, dispatcher.Subjects);

        await processor.ProcessAsync(job.Id, CancellationToken.None);
        Assert.Equal(2, dispatcher.Subjects.Count);
    }

    [Fact]
    public async Task Processor_recovers_a_publish_failed_job_when_an_ambiguous_command_arrives()
    {
        await using var context = CreateContext();
        var job = CreateJob("ambiguous-confirm");
        job.Status = BulkNotificationJobStatuses.PublishFailed;
        job.CompletedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        job.Error = "The persisted job could not be published to the message broker.";
        context.BulkNotificationJobs.Add(job);
        await context.SaveChangesAsync();
        var dispatcher = new ControlledNotificationService();
        var processor = CreateProcessor(context, dispatcher);

        var disposition = await processor.ProcessAsync(job.Id, CancellationToken.None);

        Assert.Equal(BulkNotificationProcessingResult.Completed, disposition);
        Assert.Equal(BulkNotificationJobStatuses.Completed, job.Status);
        Assert.Equal(1, job.ProcessedCount);
        Assert.Equal(new[] { "ambiguous-confirm" }, dispatcher.Subjects);
        Assert.NotNull(job.CompletedAtUtc);
        Assert.Null(job.Error);
    }

    [Fact]
    public async Task Processor_returns_dead_letter_for_a_redelivered_dead_lettered_job()
    {
        await using var context = CreateContext();
        var job = CreateJob("already-dead-lettered");
        job.Status = BulkNotificationJobStatuses.DeadLettered;
        job.CompletedAtUtc = DateTimeOffset.UtcNow;
        context.BulkNotificationJobs.Add(job);
        await context.SaveChangesAsync();
        var dispatcher = new ControlledNotificationService();
        var processor = CreateProcessor(context, dispatcher);

        var disposition = await processor.ProcessAsync(job.Id, CancellationToken.None);

        Assert.Equal(BulkNotificationProcessingResult.AlreadyDeadLettered, disposition);
        Assert.Empty(dispatcher.Subjects);
    }

    [Fact]
    public async Task Processor_records_permanent_validation_failure_and_continues()
    {
        await using var context = CreateContext();
        var job = CreateJob("good", "invalid", "also-good");
        context.BulkNotificationJobs.Add(job);
        await context.SaveChangesAsync();
        var dispatcher = new ControlledNotificationService(permanentFailureSubject: "invalid");
        var processor = CreateProcessor(context, dispatcher);

        await processor.ProcessAsync(job.Id, CancellationToken.None);

        Assert.Equal(BulkNotificationJobStatuses.CompletedWithErrors, job.Status);
        Assert.Equal(3, job.ProcessedCount);
        Assert.Equal(2, job.SucceededCount);
        Assert.Equal(1, job.FailedCount);
        Assert.Equal(BulkNotificationItemStatuses.Failed, job.Items.Single(item => item.Subject == "invalid").Status);
        Assert.Equal(new[] { "good", "invalid", "also-good" }, dispatcher.Subjects);
    }

    [Fact]
    public async Task Processor_leaves_transient_failure_for_command_level_retry()
    {
        await using var context = CreateContext();
        var job = CreateJob("transient");
        context.BulkNotificationJobs.Add(job);
        await context.SaveChangesAsync();
        var processor = CreateProcessor(
            context,
            new ControlledNotificationService(transientFailureSubject: "transient"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            processor.ProcessAsync(job.Id, CancellationToken.None));

        context.ChangeTracker.Clear();
        var persisted = await context.BulkNotificationJobs
            .Include(candidate => candidate.Items)
            .SingleAsync(candidate => candidate.Id == job.Id);
        Assert.Equal(BulkNotificationJobStatuses.Processing, persisted.Status);
        Assert.Equal(0, persisted.ProcessedCount);
        Assert.Equal(BulkNotificationItemStatuses.Pending, Assert.Single(persisted.Items).Status);
    }

    [Fact]
    public async Task Opt_in_failure_injection_provides_a_deterministic_poison_path()
    {
        await using var context = CreateContext();
        var job = CreateJob("[week3-poison]");
        context.BulkNotificationJobs.Add(job);
        await context.SaveChangesAsync();
        var processor = CreateProcessor(
            context,
            new ControlledNotificationService(),
            failureInjectionSubject: "[week3-poison]");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            processor.ProcessAsync(job.Id, CancellationToken.None));

        Assert.Equal(BulkNotificationJobStatuses.Queued, job.Status);
        Assert.Equal(0, job.ProcessedCount);
    }

    [Fact]
    public async Task Retry_and_dead_letter_transitions_are_persisted_for_the_status_endpoint()
    {
        await using var context = CreateContext();
        var job = CreateJob("retry");
        context.BulkNotificationJobs.Add(job);
        await context.SaveChangesAsync();
        var processor = CreateProcessor(context, new ControlledNotificationService());

        await processor.RecordRetryAsync(job.Id, completedAttempts: 1, maxAttempts: 3, CancellationToken.None);
        Assert.Equal(BulkNotificationJobStatuses.Retrying, job.Status);
        Assert.Null(job.CompletedAtUtc);
        Assert.Contains("1 of 3", job.Error, StringComparison.Ordinal);

        await processor.MarkDeadLetteredAsync(job.Id, attempts: 3, CancellationToken.None);
        Assert.Equal(BulkNotificationJobStatuses.DeadLettered, job.Status);
        Assert.NotNull(job.CompletedAtUtc);
        Assert.Contains("3 attempts", job.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ambiguous_publish_failure_can_transition_through_retry_to_dead_letter()
    {
        await using var context = CreateContext();
        var job = CreateJob("ambiguous-poison");
        job.Status = BulkNotificationJobStatuses.PublishFailed;
        job.CompletedAtUtc = DateTimeOffset.UtcNow;
        context.BulkNotificationJobs.Add(job);
        await context.SaveChangesAsync();
        var processor = CreateProcessor(context, new ControlledNotificationService());

        await processor.RecordRetryAsync(job.Id, completedAttempts: 1, maxAttempts: 3, CancellationToken.None);
        Assert.Equal(BulkNotificationJobStatuses.Retrying, job.Status);

        await processor.MarkDeadLetteredAsync(job.Id, attempts: 3, CancellationToken.None);
        Assert.Equal(BulkNotificationJobStatuses.DeadLettered, job.Status);
        Assert.Contains("3 attempts", job.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Processor_rejects_a_command_for_an_unknown_job()
    {
        await using var context = CreateContext();
        var processor = CreateProcessor(context, new ControlledNotificationService());

        await Assert.ThrowsAsync<BulkNotificationJobNotFoundException>(() =>
            processor.ProcessAsync(Guid.NewGuid(), CancellationToken.None));
    }

    private static NotificationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<NotificationDbContext>()
            .UseInMemoryDatabase($"bulk-processor-{Guid.NewGuid():N}")
            .Options);

    private static BulkNotificationProcessor CreateProcessor(
        NotificationDbContext context,
        INotificationService dispatcher,
        string? failureInjectionSubject = null) =>
        new(
            new BulkNotificationJobRepository(context),
            dispatcher,
            new ConfiguredBulkNotificationFailureInjector(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["RabbitMq:FailureInjectionSubject"] = failureInjectionSubject
                    })
                    .Build()),
            TimeProvider.System,
            NullLogger<BulkNotificationProcessor>.Instance);

    private static BulkNotificationJob CreateJob(params string[] subjects)
    {
        var jobId = Guid.NewGuid();
        return new BulkNotificationJob
        {
            Id = jobId,
            Status = BulkNotificationJobStatuses.Queued,
            MessageSchemaVersion = BulkNotificationRequestedV1.CurrentSchemaVersion,
            CorrelationId = $"correlation-{jobId:N}",
            TotalCount = subjects.Length,
            QueuedAtUtc = DateTimeOffset.UtcNow,
            Items = subjects.Select((subject, sequence) => new BulkNotificationJobItem
            {
                JobId = jobId,
                Sequence = sequence,
                RecipientEmail = $"{sequence}@example.com",
                Channel = NotificationChannel.InApp,
                Subject = subject,
                Body = "Body",
                Status = BulkNotificationItemStatuses.Pending
            }).ToList()
        };
    }

    private sealed class ControlledNotificationService(
        string? permanentFailureSubject = null,
        string? transientFailureSubject = null) : INotificationService
    {
        public List<string> Subjects { get; } = new();

        public Task<NotificationDto> CreateNotificationAsync(
            CreateNotificationRequest request,
            CancellationToken cancellationToken)
        {
            Subjects.Add(request.Subject);
            if (request.Subject == permanentFailureSubject)
            {
                throw new RequestValidationException("The item is invalid.");
            }

            if (request.Subject == transientFailureSubject)
            {
                throw new InvalidOperationException("Transient dependency failure.");
            }

            return Task.FromResult(new NotificationDto(
                0, request.RecipientEmail, request.Channel, request.Subject, request.Body,
                request.OrderId, false, DateTimeOffset.UtcNow, null));
        }

        public Task<IReadOnlyList<NotificationDto>> GetNotificationsAsync(string? recipientEmail, int? orderId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<NotificationDto?> GetNotificationAsync(int id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<NotificationDto?> UpdateNotificationAsync(int id, UpdateNotificationRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> DeleteNotificationAsync(int id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> DeleteNotificationsForVisitorAsync(
            string visitorEmail,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
