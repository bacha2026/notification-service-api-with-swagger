namespace NSA.Application.Exceptions;

/// <summary>Represents a temporary capacity or downstream-availability failure that a caller may retry later.</summary>
public sealed class ServiceUnavailableException(string message) : Exception(message);
