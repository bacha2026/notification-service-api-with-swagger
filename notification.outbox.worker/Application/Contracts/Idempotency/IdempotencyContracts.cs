using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NSA.Application.Exceptions;

namespace NSA.Application.Contracts;

/// <summary>Optional HTTP idempotency metadata supplied by a command endpoint.</summary>
public sealed record IdempotencyContext(string Scope, string ActorId, string Key)
{
    public const string HeaderName = "X-Idempotency-Key";
    public const string ReplayHeaderName = "Idempotency-Replayed";

    public static IdempotencyContext? Create(
        string scope,
        string actorId,
        string? key)
    {
        if (key is null)
        {
            return null;
        }

        var normalizedKey = key.Trim();
        if (normalizedKey.Length is 0 or > 128
            || normalizedKey.Any(character => !(char.IsAsciiLetterOrDigit(character)
                || character is '-' or '_' or '.' or ':')))
        {
            throw new RequestValidationException(
                $"{HeaderName} must contain 1-128 ASCII letters, digits, periods, underscores, colons, or hyphens.");
        }

        if (string.IsNullOrWhiteSpace(scope) || string.IsNullOrWhiteSpace(actorId))
        {
            throw new ArgumentException("Idempotency scope and actor are required.");
        }

        return new IdempotencyContext(scope.Trim(), actorId.Trim(), normalizedKey);
    }
}

/// <summary>A command result plus whether it came from a durable prior response.</summary>
public sealed record IdempotentResult<T>(T Value, bool Replayed);

public static class IdempotencyRequestHasher
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static string Hash<T>(T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, SerializerOptions);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}

