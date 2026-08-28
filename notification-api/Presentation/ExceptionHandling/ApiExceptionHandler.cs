using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using NSA.Application.Exceptions;

namespace NSA.Presentation.ExceptionHandling;

public sealed class ApiExceptionHandler(
    ILogger<ApiExceptionHandler> logger,
    IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            return false;
        }

        var statusCode = exception switch
        {
            RequestValidationException => StatusCodes.Status400BadRequest,
            EmailAlreadyRegisteredException or IdempotencyConflictException => StatusCodes.Status409Conflict,
            AuthenticationFailedException => StatusCodes.Status401Unauthorized,
            MfaChallengeRequiredException => StatusCodes.Status403Forbidden,
            ServiceUnavailableException => StatusCodes.Status503ServiceUnavailable,
            HttpRequestException => StatusCodes.Status503ServiceUnavailable,
            TaskCanceledException => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError
        };

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Request failed for {Method} {Path} with status {StatusCode}", httpContext.Request.Method, httpContext.Request.Path, statusCode);
        }

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Type = statusCode switch
            {
                StatusCodes.Status400BadRequest => "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                StatusCodes.Status401Unauthorized => "https://tools.ietf.org/html/rfc9110#section-15.5.2",
                StatusCodes.Status403Forbidden => "https://tools.ietf.org/html/rfc9110#section-15.5.4",
                StatusCodes.Status409Conflict => "https://tools.ietf.org/html/rfc9110#section-15.5.10",
                StatusCodes.Status503ServiceUnavailable => "https://tools.ietf.org/html/rfc9110#section-15.6.4",
                _ => "https://tools.ietf.org/html/rfc9110#section-15.6.1"
            },
            Title = statusCode switch
            {
                StatusCodes.Status400BadRequest => "The request could not be processed.",
                StatusCodes.Status401Unauthorized => "Authentication failed.",
                StatusCodes.Status403Forbidden => "Additional authentication is required.",
                StatusCodes.Status409Conflict => "The requested resource conflicts with existing data.",
                StatusCodes.Status503ServiceUnavailable => "A required service is temporarily unavailable.",
                _ => "An unexpected error occurred."
            },
            Detail = exception is RequestValidationException
                or EmailAlreadyRegisteredException
                or IdempotencyConflictException
                or AuthenticationFailedException
                or MfaChallengeRequiredException
                ? exception.Message
                : null,
            Instance = httpContext.Request.Path
        };

        if (statusCode == StatusCodes.Status503ServiceUnavailable)
        {
            httpContext.Response.Headers.RetryAfter = "30";
        }

        if (statusCode == StatusCodes.Status401Unauthorized)
        {
            httpContext.Response.Headers.WWWAuthenticate = "Bearer";
        }

        httpContext.Response.StatusCode = statusCode;
        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem
        });

        return true;
    }
}
