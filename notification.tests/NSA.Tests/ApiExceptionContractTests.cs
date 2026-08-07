using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSA.Application.Abstractions;
using NSA.Application.Contracts;
using NSA.Application.Exceptions;

namespace NSA.Tests;

public sealed class ApiExceptionContractTests
{
    [Fact]
    public async Task Typed_request_validation_exception_returns_400_and_preserves_v1_deprecation_headers()
    {
        using var factory = new ExceptionApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/api/v1/notifications?recipientEmail=domain%40example.com");

        var problem = await AssertProblemDetailsAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal("The request could not be processed.", problem.GetProperty("title").GetString());
        Assert.Equal("Simulated domain validation failure.", problem.GetProperty("detail").GetString());
        Assert.True(response.Headers.TryGetValues("Deprecation", out var values));
        Assert.Equal("true", Assert.Single(values));
        Assert.True(response.Headers.Contains("Sunset"));
    }

    [Fact]
    public async Task Generic_argument_exception_returns_sanitized_500()
    {
        using var factory = new ExceptionApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/api/v2/notifications?recipientEmail=argument%40example.com");

        var problem = await AssertProblemDetailsAsync(response, HttpStatusCode.InternalServerError);
        Assert.Equal("An unexpected error occurred.", problem.GetProperty("title").GetString());
        Assert.False(problem.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String);
        Assert.DoesNotContain("sensitive", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unexpected_exception_returns_sanitized_500_problem_details()
    {
        using var factory = new ExceptionApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/api/v2/notifications?recipientEmail=unexpected%40example.com");

        var problem = await AssertProblemDetailsAsync(response, HttpStatusCode.InternalServerError);
        Assert.Equal("An unexpected error occurred.", problem.GetProperty("title").GetString());
        Assert.False(problem.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String);
        Assert.DoesNotContain("sensitive", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Temporary_capacity_exception_returns_sanitized_503_with_retry_guidance()
    {
        using var factory = new ExceptionApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/api/v2/notifications?recipientEmail=unavailable%40example.com");

        var problem = await AssertProblemDetailsAsync(response, HttpStatusCode.ServiceUnavailable);
        Assert.Equal("A required service is temporarily unavailable.", problem.GetProperty("title").GetString());
        Assert.False(problem.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String);
        Assert.Equal("30", Assert.Single(response.Headers.GetValues("Retry-After")));
    }

    private static async Task<JsonElement> AssertProblemDetailsAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement.Clone();
        Assert.Equal((int)expectedStatus, root.GetProperty("status").GetInt32());
        Assert.Equal(response.RequestMessage!.RequestUri!.AbsolutePath, root.GetProperty("instance").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
        return root;
    }

    private sealed class ExceptionApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<INotificationService>();
                services.AddScoped<INotificationService, ThrowingNotificationService>();
            });
        }
    }

    private sealed class ThrowingNotificationService : INotificationService
    {
        public Task<IReadOnlyList<NotificationDto>> GetNotificationsAsync(
            string? recipientEmail,
            int? orderId,
            CancellationToken cancellationToken) => recipientEmail switch
            {
                "domain@example.com" => Task.FromException<IReadOnlyList<NotificationDto>>(
                    new RequestValidationException("Simulated domain validation failure.")),
                "argument@example.com" => Task.FromException<IReadOnlyList<NotificationDto>>(
                    new ArgumentException("Sensitive framework argument details.")),
                "unexpected@example.com" => Task.FromException<IReadOnlyList<NotificationDto>>(
                    new Exception("Sensitive database host and credential details.")),
                "unavailable@example.com" => Task.FromException<IReadOnlyList<NotificationDto>>(
                    new ServiceUnavailableException("Internal queue capacity is 123.")),
                _ => Task.FromResult<IReadOnlyList<NotificationDto>>(Array.Empty<NotificationDto>())
            };

        public Task<NotificationDto?> GetNotificationAsync(int id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<NotificationDto> CreateNotificationAsync(
            CreateNotificationRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<NotificationDto?> UpdateNotificationAsync(
            int id,
            UpdateNotificationRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> DeleteNotificationAsync(int id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
