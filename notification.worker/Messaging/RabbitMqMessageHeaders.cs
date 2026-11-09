using System.Text;

namespace NSA.Worker;

/// <summary>Reads and clones AMQP headers without leaking broker-specific casts through handlers.</summary>
public static class RabbitMqMessageHeaders
{
    public static int GetInt32(IDictionary<string, object?>? headers, string headerName)
    {
        return TryGetInt32(headers, headerName, out var number) ? number : 0;
    }

    public static bool TryGetInt32(IDictionary<string, object?>? headers, string headerName, out int number)
    {
        number = 0;
        if (headers is null || !headers.TryGetValue(headerName, out var value) || value is null)
        {
            return false;
        }

        switch (value)
        {
            case byte valueByte:
                number = valueByte;
                return true;
            case short valueShort when valueShort >= 0:
                number = valueShort;
                return true;
            case int valueInt when valueInt >= 0:
                number = valueInt;
                return true;
            case long valueLong when valueLong is >= 0 and <= int.MaxValue:
                number = (int)valueLong;
                return true;
            case byte[] bytes:
                return TryParseNonNegativeInt32(Encoding.UTF8.GetString(bytes), out number);
            case ReadOnlyMemory<byte> bytes:
                return TryParseNonNegativeInt32(Encoding.UTF8.GetString(bytes.Span), out number);
            default:
                return false;
        }
    }

    public static Dictionary<string, object?> Clone(IDictionary<string, object?>? source) =>
        source is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(source);

    private static bool TryParseNonNegativeInt32(string value, out int number) =>
        int.TryParse(value, out number) && number >= 0;
}
