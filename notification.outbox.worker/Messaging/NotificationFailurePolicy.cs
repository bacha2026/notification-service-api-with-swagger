using System.Net;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace NSA.Outbox.Worker;

/// <summary>Permanent failures go directly to the DLQ; other failures enter delayed retry.</summary>
public static class NotificationFailurePolicy
{
    public static NotificationFailureKind Classify(Exception exception)
    {
        //The payload is malformed or invalid in a way that retrying is unlikely to fix.
        if (exception is JsonException or PermanentMessageException)
            return NotificationFailureKind.Permanent;
        //HTTP 429 or 503 indicate the broker is overloaded and the worker should back off before retrying.
        if (exception is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable })
            return NotificationFailureKind.Backoff;
        //These indicate a timeout or deadline-related failure. SQL error -2 is SQL Server’s command-timeout error.
        if (exception is TimeoutException or OperationCanceledException
            or HttpRequestException { StatusCode: HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout }
            or SqlException { Number: -2 })
            return NotificationFailureKind.Timeout;
        //Recursively classifying the inner exception can reveal, for example, a timeout hidden inside another exception. If no recognized exception is found, the result is Unknown
        return exception.InnerException is { } inner ? Classify(inner) : NotificationFailureKind.Unknown;
    }
}
