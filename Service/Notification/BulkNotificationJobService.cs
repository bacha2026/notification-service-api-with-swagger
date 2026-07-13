using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using NSA.Application.Abstractions;
using NSA.Application.Contracts;
using NSA.Application.Exceptions;

namespace NSA.Service;

public sealed class BulkNotificationJobService : IBulkNotificationJobService
{
    private readonly ConcurrentDictionary<Guid, BulkNotificationJob> jobs = new();
    private readonly object jobsSync = new();
    private readonly Channel<BulkNotificationJob> queue;
    private readonly BulkNotificationOptions options;
    private readonly TimeProvider timeProvider;

    public BulkNotificationJobService()
        : this(Options.Create(new BulkNotificationOptions()), TimeProvider.System)
    {
    }

    public BulkNotificationJobService(IOptions<BulkNotificationOptions> options)
        : this(options, TimeProvider.System)
    {
    }

    public BulkNotificationJobService(IOptions<BulkNotificationOptions> options, TimeProvider timeProvider)
    {
        this.options = options.Value;
        this.timeProvider = timeProvider;
        queue = Channel.CreateBounded<BulkNotificationJob>(new BoundedChannelOptions(this.options.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public BulkNotificationJobDto Queue(CreateBulkNotificationsRequest request)
    {
        if (request.Notifications is null || request.Notifications.Count == 0)
        {
            throw new RequestValidationException("At least one notification is required.");
        }

        if (request.Notifications.Count > options.MaxBatchSize)
        {
            throw new RequestValidationException($"A bulk notification job cannot contain more than {options.MaxBatchSize} notifications.");
        }

        var job = new BulkNotificationJob(Guid.NewGuid(), request.Notifications.ToArray(), timeProvider.GetUtcNow(), timeProvider);
        lock (jobsSync)
        {
            RemoveExpiredJobs();

            if (jobs.Count >= options.MaxTrackedJobs)
            {
                throw new ServiceUnavailableException("The bulk notification service is at capacity. Try again later.");
            }

            if (!jobs.TryAdd(job.Id, job))
            {
                throw new ServiceUnavailableException("The bulk notification job could not be registered. Try again later.");
            }

            if (!queue.Writer.TryWrite(job))
            {
                jobs.TryRemove(job.Id, out _);
                throw new ServiceUnavailableException("The bulk notification queue is full. Try again later.");
            }
        }

        return job.ToDto();
    }

    public BulkNotificationJobDto? GetStatus(Guid jobId)
    {
        BulkNotificationJob? job;
        lock (jobsSync)
        {
            RemoveExpiredJobs();
            jobs.TryGetValue(jobId, out job);
        }

        return job?.ToDto();
    }

    public IAsyncEnumerable<BulkNotificationJob> ReadAllAsync(CancellationToken cancellationToken) => queue.Reader.ReadAllAsync(cancellationToken);

    private void RemoveExpiredJobs()
    {
        var cutoff = timeProvider.GetUtcNow().AddMinutes(-options.CompletedJobRetentionMinutes);
        foreach (var (jobId, job) in jobs)
        {
            if (job.CompletedBefore(cutoff))
            {
                jobs.TryRemove(jobId, out _);
            }
        }
    }
}

public sealed class BulkNotificationJob
{
    private readonly object sync = new();
    private readonly TimeProvider timeProvider;
    private readonly int totalCount;
    private IReadOnlyList<BulkNotificationItemRequest> notifications;

    public BulkNotificationJob(Guid id, IReadOnlyList<BulkNotificationItemRequest> notifications, DateTimeOffset queuedAtUtc)
        : this(id, notifications, queuedAtUtc, TimeProvider.System)
    {
    }

    public BulkNotificationJob(
        Guid id,
        IReadOnlyList<BulkNotificationItemRequest> notifications,
        DateTimeOffset queuedAtUtc,
        TimeProvider timeProvider)
    {
        Id = id;
        this.notifications = notifications;
        totalCount = notifications.Count;
        QueuedAtUtc = queuedAtUtc;
        this.timeProvider = timeProvider;
    }

    public Guid Id { get; }
    public IReadOnlyList<BulkNotificationItemRequest> Notifications
    {
        get
        {
            lock (sync)
            {
                return notifications;
            }
        }
    }

    public DateTimeOffset QueuedAtUtc { get; }
    public DateTimeOffset? StartedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public int ProcessedCount { get; private set; }
    public int SucceededCount { get; private set; }
    public int FailedCount { get; private set; }
    public string Status { get; private set; } = "Queued";
    public string? Error { get; private set; }

    public void Start()
    {
        lock (sync)
        {
            Status = "Processing";
            StartedAtUtc = timeProvider.GetUtcNow();
        }
    }

    public void RecordSuccess()
    {
        lock (sync)
        {
            ProcessedCount++;
            SucceededCount++;
        }
    }

    public void RecordFailure(Exception exception)
    {
        lock (sync)
        {
            ProcessedCount++;
            FailedCount++;
            Error ??= "One or more notifications could not be processed.";
        }
    }

    public void Cancel()
    {
        lock (sync)
        {
            Status = "Cancelled";
            CompletedAtUtc = timeProvider.GetUtcNow();
            ReleasePayload();
        }
    }

    public void Complete()
    {
        lock (sync)
        {
            Status = FailedCount == 0 ? "Completed" : "CompletedWithErrors";
            CompletedAtUtc = timeProvider.GetUtcNow();
            ReleasePayload();
        }
    }

    public BulkNotificationJobDto ToDto()
    {
        lock (sync)
        {
            return new BulkNotificationJobDto(Id, Status, totalCount, ProcessedCount, SucceededCount, FailedCount, QueuedAtUtc, StartedAtUtc, CompletedAtUtc, Error);
        }
    }

    public bool CompletedBefore(DateTimeOffset cutoff)
    {
        lock (sync)
        {
            return CompletedAtUtc is not null && CompletedAtUtc < cutoff;
        }
    }

    private void ReleasePayload()
    {
        notifications = Array.Empty<BulkNotificationItemRequest>();
    }
}
