namespace NSA.Application.Exceptions;

/// <summary>
/// Represents a caller-correctable request failure whose message is safe to return to the client.
/// </summary>
public sealed class RequestValidationException(string message) : Exception(message);
