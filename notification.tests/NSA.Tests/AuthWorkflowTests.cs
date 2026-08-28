using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSA.Application.Abstractions;
using NSA.Domain.Entities;
using NSA.Domain.Enums;
using NSA.Persistence;

namespace NSA.Tests;

public sealed class AuthWorkflowTests
{
    [Fact]
    public async Task Signup_creates_a_hashed_account_and_returns_a_bearer_token()
    {
        using var factory = new NsaApiFactory();
        using var client = CreateClient(factory);
        var email = $"signup-{Guid.NewGuid():N}@example.test";

        using var response = await client.PostAsJsonAsync("/api/v2/auth/signup", ValidSignUp(email));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.Equal("/api/v2/auth/me", GetLocationPath(response.Headers.Location!));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("accessToken").GetString()));
        Assert.Equal(email, body.RootElement.GetProperty("user").GetProperty("email").GetString());
        Assert.False(body.RootElement.GetProperty("user").GetProperty("mfaRequired").GetBoolean());
        Assert.DoesNotContain("password", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        using var scope = factory.Services.CreateScope();
        var credential = scope.ServiceProvider.GetRequiredService<NotificationDbContext>()
            .UserCredentials
            .Single(candidate => candidate.Email == email);
        Assert.NotEqual("SafePassword!123", credential.PasswordHash);
        Assert.False(string.IsNullOrWhiteSpace(credential.PasswordHash));
    }

    [Fact]
    public async Task Signup_rejects_a_duplicate_normalized_email_with_conflict_problem_details()
    {
        using var factory = new NsaApiFactory();
        using var client = CreateClient(factory);
        var email = $"duplicate-{Guid.NewGuid():N}@example.test";

        using var first = await client.PostAsJsonAsync("/api/v2/auth/signup", ValidSignUp(email));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var duplicate = await client.PostAsJsonAsync("/api/v2/auth/signup", ValidSignUp(email.ToUpperInvariant()));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("application/problem+json", duplicate.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await duplicate.Content.ReadAsStringAsync());
        Assert.Equal("An account with that email address already exists.", body.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Login_returns_the_same_generic_error_for_unknown_and_incorrect_credentials()
    {
        using var factory = new NsaApiFactory();
        using var client = CreateClient(factory);
        var email = $"login-{Guid.NewGuid():N}@example.test";
        using var signUp = await client.PostAsJsonAsync("/api/v2/auth/signup", ValidSignUp(email));
        Assert.Equal(HttpStatusCode.Created, signUp.StatusCode);

        using var wrongPassword = await client.PostAsJsonAsync("/api/v2/auth/login", new
        {
            email,
            password = "IncorrectPassword!123"
        });
        using var unknownAccount = await client.PostAsJsonAsync("/api/v2/auth/login", new
        {
            email = $"unknown-{Guid.NewGuid():N}@example.test",
            password = "IncorrectPassword!123"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownAccount.StatusCode);
        Assert.Equal("Bearer", Assert.Single(wrongPassword.Headers.WwwAuthenticate).Scheme);
        Assert.Equal(
            GetProblemDetail(await wrongPassword.Content.ReadAsStringAsync()),
            GetProblemDetail(await unknownAccount.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Login_locks_an_account_after_repeated_failures_without_disclosing_lockout_state()
    {
        using var factory = new NsaApiFactory();
        using var client = CreateClient(factory);
        var email = $"lockout-{Guid.NewGuid():N}@example.test";
        using var signUp = await client.PostAsJsonAsync("/api/v2/auth/signup", ValidSignUp(email));
        Assert.Equal(HttpStatusCode.Created, signUp.StatusCode);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var failed = await client.PostAsJsonAsync("/api/v2/auth/login", new
            {
                email,
                password = "IncorrectPassword!123"
            });
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
            Assert.Equal("Invalid email or password.", GetProblemDetail(await failed.Content.ReadAsStringAsync()));
        }

        using var locked = await client.PostAsJsonAsync("/api/v2/auth/login", new
        {
            email,
            password = "SafePassword!123"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        Assert.Equal("Invalid email or password.", GetProblemDetail(await locked.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Login_bearer_token_returns_current_profile_and_excludes_standalone_email_notifications()
    {
        using var factory = new NsaApiFactory();
        using var client = CreateClient(factory);
        var email = $"profile-{Guid.NewGuid():N}@example.test";
        using var signUp = await client.PostAsJsonAsync("/api/v2/auth/signup", ValidSignUp(email));
        Assert.Equal(HttpStatusCode.Created, signUp.StatusCode);
        using var login = await client.PostAsJsonAsync("/api/v2/auth/login", new
        {
            email,
            password = "SafePassword!123"
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = GetAccessToken(await login.Content.ReadAsStringAsync());

        using var createNotification = await client.PostAsJsonAsync("/api/v2/notifications", new
        {
            recipientEmail = email,
            channel = 1,
            subject = "Order update",
            body = "Your order is being prepared.",
            orderId = (int?)null
        });
        Assert.Equal(HttpStatusCode.Created, createNotification.StatusCode);
        using var createOtherNotification = await client.PostAsJsonAsync("/api/v2/notifications", new
        {
            recipientEmail = $"other-{Guid.NewGuid():N}@example.test",
            channel = 1,
            subject = "Another user's order",
            body = "This notification must not appear in the current user's feed.",
            orderId = (int?)null
        });
        Assert.Equal(HttpStatusCode.Created, createOtherNotification.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var me = await client.GetAsync("/api/v2/auth/me");
        using var notifications = await client.GetAsync("/api/v2/auth/notifications");

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(HttpStatusCode.OK, notifications.StatusCode);
        using var profile = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
        Assert.Equal(email, profile.RootElement.GetProperty("email").GetString());
        using var notificationBody = JsonDocument.Parse(await notifications.Content.ReadAsStringAsync());
        Assert.DoesNotContain(notificationBody.RootElement.EnumerateArray(), item =>
            item.GetProperty("subject").GetString() == "Order update");
        Assert.DoesNotContain(notificationBody.RootElement.EnumerateArray(), item =>
            item.GetProperty("subject").GetString() == "Another user's order");
    }

    [Fact]
    public async Task Completed_bulk_notification_for_account_email_can_be_read_and_deleted_without_deleting_its_job()
    {
        using var factory = new NsaApiFactory();
        using var client = CreateClient(factory);
        var email = $"notifications-{Guid.NewGuid():N}@example.test";
        using var signUp = await client.PostAsJsonAsync("/api/v2/auth/signup", ValidSignUp(email));
        var token = GetAccessToken(await signUp.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        int completedNotificationId;
        Guid completedJobId;
        Guid incompleteJobId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
            var now = DateTimeOffset.UtcNow;
            var completedItem = new BulkNotificationJobItem
            {
                Sequence = 0,
                RecipientEmail = email,
                Channel = NotificationChannel.InApp,
                Subject = "Your completed notification",
                Body = "This body is available only after processing completes.",
                Status = BulkNotificationItemStatuses.Succeeded,
                AttemptCount = 1
            };
            var pendingItem = new BulkNotificationJobItem
            {
                Sequence = 1,
                RecipientEmail = email,
                Channel = NotificationChannel.InApp,
                Subject = "Pending item must remain hidden",
                Body = "This body must not be returned.",
                Status = BulkNotificationItemStatuses.Pending
            };
            var completedJob = new BulkNotificationJob
            {
                Id = Guid.NewGuid(),
                Status = BulkNotificationJobStatuses.Completed,
                MessageSchemaVersion = 1,
                CorrelationId = Guid.NewGuid().ToString("N"),
                TotalCount = 2,
                ProcessedCount = 1,
                SucceededCount = 1,
                QueuedAtUtc = now.AddMinutes(-1),
                CompletedAtUtc = now,
                Items = [completedItem, pendingItem]
            };
            var incompleteJobItem = new BulkNotificationJobItem
            {
                Sequence = 0,
                RecipientEmail = email,
                Channel = NotificationChannel.InApp,
                Subject = "Incomplete job must remain hidden",
                Body = "This body must not be returned either.",
                Status = BulkNotificationItemStatuses.Succeeded,
                AttemptCount = 1
            };
            var incompleteJob = new BulkNotificationJob
            {
                Id = Guid.NewGuid(),
                Status = BulkNotificationJobStatuses.Processing,
                MessageSchemaVersion = 1,
                CorrelationId = Guid.NewGuid().ToString("N"),
                TotalCount = 1,
                QueuedAtUtc = now,
                StartedAtUtc = now,
                Items = [incompleteJobItem]
            };
            dbContext.BulkNotificationJobs.AddRange(completedJob, incompleteJob);
            await dbContext.SaveChangesAsync();
            completedJobId = completedJob.Id;
            incompleteJobId = incompleteJob.Id;

            var completedNotification = Notification.Create(
                email,
                NotificationChannel.InApp,
                "Your completed notification",
                "This body is available only after processing completes.",
                orderId: null,
                now);
            completedNotification.AssociateWithBulkJob(completedJob.Id, completedItem.Id);
            var pendingNotification = Notification.Create(
                email,
                NotificationChannel.InApp,
                "Pending item must remain hidden",
                "This body must not be returned.",
                orderId: null,
                now.AddSeconds(1));
            pendingNotification.AssociateWithBulkJob(completedJob.Id, pendingItem.Id);
            var incompleteJobNotification = Notification.Create(
                email,
                NotificationChannel.InApp,
                "Incomplete job must remain hidden",
                "This body must not be returned either.",
                orderId: null,
                now.AddSeconds(2));
            incompleteJobNotification.AssociateWithBulkJob(incompleteJob.Id, incompleteJobItem.Id);
            dbContext.Notifications.AddRange(completedNotification, pendingNotification, incompleteJobNotification);
            await dbContext.SaveChangesAsync();
            completedNotificationId = completedNotification.Id;

            var jobs = scope.ServiceProvider.GetRequiredService<IBulkNotificationJobRepository>();
            var eligibleForCleanup = await jobs.GetCompletedBeforeAsync(now.AddMinutes(1), CancellationToken.None);
            Assert.DoesNotContain(eligibleForCleanup, candidate => candidate.Id == completedJob.Id);
        }

        using var notifications = await client.GetAsync("/api/v2/auth/notifications");
        Assert.Equal(HttpStatusCode.OK, notifications.StatusCode);
        using var notificationBody = JsonDocument.Parse(await notifications.Content.ReadAsStringAsync());
        var returned = Assert.Single(notificationBody.RootElement.EnumerateArray());
        Assert.Equal(email, returned.GetProperty("recipientEmail").GetString());
        Assert.Equal("Your completed notification", returned.GetProperty("subject").GetString());
        Assert.Equal("This body is available only after processing completes.", returned.GetProperty("body").GetString());
        Assert.False(returned.GetProperty("isRead").GetBoolean());

        using var markRead = await client.PatchAsync($"/api/v2/auth/notifications/{completedNotificationId}/read", content: null);
        Assert.Equal(HttpStatusCode.OK, markRead.StatusCode);
        using var markedBody = JsonDocument.Parse(await markRead.Content.ReadAsStringAsync());
        Assert.True(markedBody.RootElement.GetProperty("isRead").GetBoolean());

        using var otherClient = CreateClient(factory);
        var otherEmail = $"other-notifications-{Guid.NewGuid():N}@example.test";
        using var otherSignUp = await otherClient.PostAsJsonAsync("/api/v2/auth/signup", ValidSignUp(otherEmail));
        otherClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            GetAccessToken(await otherSignUp.Content.ReadAsStringAsync()));
        using var forbiddenDelete = await otherClient.DeleteAsync($"/api/v2/auth/notifications/{completedNotificationId}");
        Assert.Equal(HttpStatusCode.NotFound, forbiddenDelete.StatusCode);

        using var delete = await client.DeleteAsync($"/api/v2/auth/notifications/{completedNotificationId}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        using var repeatedDelete = await client.DeleteAsync($"/api/v2/auth/notifications/{completedNotificationId}");
        Assert.Equal(HttpStatusCode.NotFound, repeatedDelete.StatusCode);

        await using (var verificationScope = factory.Services.CreateAsyncScope())
        {
            var verificationDb = verificationScope.ServiceProvider.GetRequiredService<NotificationDbContext>();
            Assert.False(await verificationDb.Notifications.AnyAsync(
                notification => notification.Id == completedNotificationId));
            Assert.True(await verificationDb.Notifications.AnyAsync(
                notification => notification.BulkNotificationJobId == completedJobId));
            Assert.True(await verificationDb.BulkNotificationJobItems.AnyAsync(item => item.JobId == completedJobId));
            Assert.True(await verificationDb.BulkNotificationJobs.AnyAsync(job => job.Id == completedJobId));
            Assert.True(await verificationDb.BulkNotificationJobs.AnyAsync(job => job.Id == incompleteJobId));
        }

        using var notificationsAfterDelete = await client.GetAsync("/api/v2/auth/notifications");
        Assert.Equal(HttpStatusCode.OK, notificationsAfterDelete.StatusCode);
        using var afterDeleteBody = JsonDocument.Parse(await notificationsAfterDelete.Content.ReadAsStringAsync());
        Assert.Empty(afterDeleteBody.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task Deleting_a_notification_is_isolated_to_the_authenticated_recipient()
    {
        using var factory = new NsaApiFactory();
        using var adminClient = CreateClient(factory);
        using var visitorClient = CreateClient(factory);
        var adminEmail = $"admin-notification-{Guid.NewGuid():N}@example.test";
        var visitorEmail = $"visitor-notification-{Guid.NewGuid():N}@example.test";

        using var adminSignUp = await adminClient.PostAsJsonAsync("/api/v2/auth/signup", ValidSignUp(adminEmail));
        using var visitorSignUp = await visitorClient.PostAsJsonAsync("/api/v2/auth/signup", ValidSignUp(visitorEmail));
        Assert.Equal(HttpStatusCode.Created, adminSignUp.StatusCode);
        Assert.Equal(HttpStatusCode.Created, visitorSignUp.StatusCode);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            GetAccessToken(await adminSignUp.Content.ReadAsStringAsync()));
        visitorClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            GetAccessToken(await visitorSignUp.Content.ReadAsStringAsync()));

        int adminNotificationId;
        int visitorNotificationId;
        Guid jobId;
        await using (var setupScope = factory.Services.CreateAsyncScope())
        {
            var dbContext = setupScope.ServiceProvider.GetRequiredService<NotificationDbContext>();
            var now = DateTimeOffset.UtcNow;
            var adminItem = new BulkNotificationJobItem
            {
                Sequence = 0,
                RecipientEmail = adminEmail,
                Channel = NotificationChannel.InApp,
                Subject = "Admin copy",
                Body = "Admin body",
                Status = BulkNotificationItemStatuses.Succeeded,
                AttemptCount = 1
            };
            var visitorItem = new BulkNotificationJobItem
            {
                Sequence = 1,
                RecipientEmail = visitorEmail,
                Channel = NotificationChannel.InApp,
                Subject = "Visitor copy",
                Body = "Visitor body",
                Status = BulkNotificationItemStatuses.Succeeded,
                AttemptCount = 1
            };
            var job = new BulkNotificationJob
            {
                Id = Guid.NewGuid(),
                Status = BulkNotificationJobStatuses.Completed,
                MessageSchemaVersion = 1,
                CorrelationId = Guid.NewGuid().ToString("N"),
                TotalCount = 2,
                ProcessedCount = 2,
                SucceededCount = 2,
                QueuedAtUtc = now.AddMinutes(-1),
                CompletedAtUtc = now,
                Items = [adminItem, visitorItem]
            };
            dbContext.BulkNotificationJobs.Add(job);
            await dbContext.SaveChangesAsync();

            var adminNotification = Notification.Create(
                adminEmail, NotificationChannel.InApp, "Admin copy", "Admin body", null, now);
            adminNotification.AssociateWithBulkJob(job.Id, adminItem.Id);
            var visitorNotification = Notification.Create(
                visitorEmail, NotificationChannel.InApp, "Visitor copy", "Visitor body", null, now);
            visitorNotification.AssociateWithBulkJob(job.Id, visitorItem.Id);
            dbContext.Notifications.AddRange(adminNotification, visitorNotification);
            await dbContext.SaveChangesAsync();

            adminNotificationId = adminNotification.Id;
            visitorNotificationId = visitorNotification.Id;
            jobId = job.Id;
        }

        using var deleteAdmin = await adminClient.DeleteAsync(
            $"/api/v2/auth/notifications/{adminNotificationId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteAdmin.StatusCode);

        await using (var afterAdminDeleteScope = factory.Services.CreateAsyncScope())
        {
            var dbContext = afterAdminDeleteScope.ServiceProvider.GetRequiredService<NotificationDbContext>();
            Assert.False(await dbContext.Notifications.AnyAsync(item => item.Id == adminNotificationId));
            Assert.True(await dbContext.Notifications.AnyAsync(item => item.Id == visitorNotificationId));
            Assert.Equal(2, await dbContext.BulkNotificationJobItems.CountAsync(item => item.JobId == jobId));
            Assert.True(await dbContext.BulkNotificationJobs.AnyAsync(item => item.Id == jobId));
        }

        using var deleteVisitor = await visitorClient.DeleteAsync(
            $"/api/v2/auth/notifications/{visitorNotificationId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteVisitor.StatusCode);

        await using var afterVisitorDeleteScope = factory.Services.CreateAsyncScope();
        var verification = afterVisitorDeleteScope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        Assert.False(await verification.Notifications.AnyAsync(item => item.Id == visitorNotificationId));
        Assert.Equal(2, await verification.BulkNotificationJobItems.CountAsync(item => item.JobId == jobId));
        Assert.True(await verification.BulkNotificationJobs.AnyAsync(item => item.Id == jobId));
    }

    [Fact]
    public async Task Protected_auth_endpoints_require_a_bearer_token()
    {
        using var factory = new NsaApiFactory();
        using var client = CreateClient(factory);

        using var me = await client.GetAsync("/api/v2/auth/me");
        using var notifications = await client.GetAsync("/api/v2/auth/notifications");
        using var deleteNotification = await client.DeleteAsync("/api/v2/auth/notifications/1");
        using var orders = await client.GetAsync("/api/v2/auth/orders");
        using var createOrder = await client.PostAsync("/api/v2/auth/orders", content: null);
        using var cart = await client.GetAsync("/api/v2/auth/cart");
        using var addCartItem = await client.PostAsJsonAsync("/api/v2/auth/cart/items", new
        {
            productId = 1,
            quantity = 1
        });

        foreach (var response in new[]
                 {
                     me,
                     notifications,
                     deleteNotification,
                     orders,
                     createOrder,
                     cart,
                     addCartItem
                 })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(401, problem.RootElement.GetProperty("status").GetInt32());
            Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
        }
    }

    [Fact]
    public async Task Authentication_rate_limit_uses_problem_details()
    {
        using var factory = new NsaApiFactory();
        using var client = CreateClient(factory);
        HttpResponseMessage? limited = null;

        try
        {
            for (var attempt = 0; attempt <= 10; attempt++)
            {
                var response = await client.PostAsJsonAsync("/api/v2/auth/login", new { });

                if (attempt < 10)
                {
                    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                    response.Dispose();
                }
                else
                {
                    limited = response;
                }
            }

            Assert.NotNull(limited);
            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
            Assert.Equal("application/problem+json", limited.Content.Headers.ContentType?.MediaType);
            using var problem = JsonDocument.Parse(await limited.Content.ReadAsStringAsync());
            Assert.Equal(429, problem.RootElement.GetProperty("status").GetInt32());
            Assert.Equal("/api/v2/auth/login", problem.RootElement.GetProperty("instance").GetString());
            Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
        }
        finally
        {
            limited?.Dispose();
        }
    }

    private static HttpClient CreateClient(NsaApiFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static object ValidSignUp(string email) => new
    {
        email,
        password = "SafePassword!123",
        firstName = "Taylor",
        lastName = "Customer",
        address = "1 Notification Way",
        dateOfBirth = "1990-01-02",
        gender = 1,
        contactNumber = "+639171234567",
        photoUrl = "https://example.test/profile.png"
    };

    private static string GetAccessToken(string content)
    {
        using var document = JsonDocument.Parse(content);
        return document.RootElement.GetProperty("accessToken").GetString()!;
    }

    private static string? GetProblemDetail(string content)
    {
        using var document = JsonDocument.Parse(content);
        return document.RootElement.GetProperty("detail").GetString();
    }

    private static string GetLocationPath(Uri location) =>
        location.IsAbsoluteUri ? location.AbsolutePath : location.OriginalString.Split('?', '#')[0];
}
