namespace NSA.Application.Abstractions;

/// <summary>Supplies an optional ambient correlation identifier to application workflows.</summary>
public interface ICorrelationIdProvider
{
    string? CurrentCorrelationId { get; }
}
