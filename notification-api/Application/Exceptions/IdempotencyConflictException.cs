namespace NSA.Application.Exceptions;

/// <summary>An idempotency key was reused for a semantically different request.</summary>
public sealed class IdempotencyConflictException(string message) : Exception(message);
