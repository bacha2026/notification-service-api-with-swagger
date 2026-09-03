namespace NSA.Domain.Entities;

/// <summary>A durable HTTP request receipt used to replay an accepted command safely.</summary>
public sealed class IdempotencyRecord
{
    public Guid Id { get; set; }
    public required string Scope { get; set; }
    public required string ActorId { get; set; }
    public required string KeyHash { get; set; }
    public required string RequestHash { get; set; }
    public int ResponseStatusCode { get; set; }
    public required string ResponseBody { get; set; }
    public string? ResourceType { get; set; }
    public string? ResourceId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
}

/// <summary>A broker command committed atomically with the state that produced it.</summary>
public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public required string AggregateType { get; set; }
    public required string AggregateId { get; set; }
    public required string MessageType { get; set; }
    public int SchemaVersion { get; set; }
    public required string Payload { get; set; }
    public required string Exchange { get; set; }
    public required string RoutingKey { get; set; }
    public required string CorrelationId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public DateTimeOffset NextAttemptAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }
    public string? LockedBy { get; set; }
    public DateTimeOffset? LockedUntilUtc { get; set; }
    public string? LastError { get; set; }
}

/// <summary>A consumer receipt committed only after all message effects complete.</summary>
public sealed class InboxMessage
{
    public Guid MessageId { get; set; }
    public required string Handler { get; set; }
    public Guid AggregateId { get; set; }
    public DateTimeOffset ProcessedAtUtc { get; set; }
}

