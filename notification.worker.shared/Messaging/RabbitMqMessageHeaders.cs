using System.Collections;
using System.Text;

namespace NSA.Workers.Shared.Messaging;

/// <summary>Reads and clones AMQP headers without leaking broker-specific casts through handlers.</summary>
public static class RabbitMqMessageHeaders
{
    public static int GetInt32(IDictionary<string, object?>? headers, string headerName)
    {
        if (headers is null || !headers.TryGetValue(headerName, out var value) || value is null)
        {
            return 0;
        }

        return value switch
        {
            byte number => number,
            short number when number >= 0 => number,
            int number when number >= 0 => number,
            long number when number is >= 0 and <= int.MaxValue => (int)number,
            byte[] bytes when TryParseNonNegativeInt32(Encoding.UTF8.GetString(bytes), out var number) => number,
            ReadOnlyMemory<byte> bytes when TryParseNonNegativeInt32(Encoding.UTF8.GetString(bytes.Span), out var number) => number,
            _ => 0
        };
    }

    public static Dictionary<string, object?> Clone(IDictionary<string, object?>? source) =>
        source is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(source);

    public static bool TryGetString(
        IDictionary<string, object?> headers,
        string headerName,
        out string? value)
    {
        if (headers.TryGetValue(headerName, out var candidate)
            && TryConvertToString(candidate, out value))
        {
            return true;
        }

        value = null;
        return false;
    }

    public static bool TryGetTableString(object? table, string key, out string? value)
    {
        if (table is IDictionary<string, object?> typed
            && typed.TryGetValue(key, out var candidate)
            && TryConvertToString(candidate, out value))
        {
            return true;
        }

        if (table is IDictionary untyped)
        {
            foreach (DictionaryEntry entry in untyped)
            {
                if (entry.Key is string entryKey
                    && string.Equals(entryKey, key, StringComparison.Ordinal)
                    && TryConvertToString(entry.Value, out value))
                {
                    return true;
                }
            }
        }

        value = null;
        return false;
    }

    private static bool TryParseNonNegativeInt32(string value, out int number) =>
        int.TryParse(value, out number) && number >= 0;

    private static bool TryConvertToString(object? source, out string? value)
    {
        switch (source)
        {
            case string text:
                value = text;
                return true;
            case byte[] bytes:
                value = Encoding.UTF8.GetString(bytes);
                return true;
            case ReadOnlyMemory<byte> memory:
                value = Encoding.UTF8.GetString(memory.Span);
                return true;
            default:
                value = null;
                return false;
        }
    }
}
