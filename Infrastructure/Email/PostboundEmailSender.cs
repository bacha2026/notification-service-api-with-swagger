using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using NSA.Application.Abstractions;

namespace NSA.Infrastructure.Email;

/// <summary>Submits email to Postbound and reports whether a provider accepted the message.</summary>
public sealed class PostboundEmailSender(HttpClient httpClient, IOptions<PostboundOptions> options, EmailNotificationLogger logger) : IEmailSender
{
    public async Task<EmailDeliveryOutcome> SendAsync(
        string recipientEmail,
        string subject,
        string body,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogDeliveryNotAttempted();
            return EmailDeliveryOutcome.NotAttempted;
        }

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException("Postbound is enabled but no API key has been configured.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "emails")
        {
            Content = JsonContent.Create(new { to = recipientEmail, subject, body })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        // The key remains stable across Polly retries of this logical send. The provider must
        // explicitly guarantee that it honors this header before real delivery is enabled.
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("N"));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return EmailDeliveryOutcome.AcceptedByProvider;
    }
}
