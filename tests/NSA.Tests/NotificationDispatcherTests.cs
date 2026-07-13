using NSA.Application.Abstractions;
using NSA.Application.Exceptions;
using NSA.Domain.Entities;
using NSA.Persistence.Interfaces;
using NSA.Service;

namespace NSA.Tests;

public sealed class NotificationDispatcherTests
{
    [Fact]
    public async Task Successful_email_is_persisted_before_delivery_then_marked_sent()
    {
        var events = new List<string>();
        var repository = new RecordingNotificationRepository(events, orderExists: true);
        var emailSender = new RecordingEmailSender(events);
        var dispatcher = new NotificationDispatcher(repository, emailSender);

        var notification = await dispatcher.CreateEmailNotificationAsync(
            "person@example.com",
            "Subject",
            "Body",
            42,
            CancellationToken.None);

        Assert.Same(notification, Assert.Single(repository.Added));
        Assert.NotNull(notification.SentAtUtc);
        Assert.Equal(new[] { "order-exists", "add", "save", "send", "save" }, events);
        Assert.Equal(1, emailSender.CallCount);
    }

    [Fact]
    public async Task Provider_failure_leaves_a_persisted_unsent_notification_for_recovery()
    {
        var events = new List<string>();
        var repository = new RecordingNotificationRepository(events, orderExists: true);
        var emailSender = new RecordingEmailSender(
            events,
            new HttpRequestException("Provider unavailable."));
        var dispatcher = new NotificationDispatcher(repository, emailSender);

        await Assert.ThrowsAsync<HttpRequestException>(() => dispatcher.CreateEmailNotificationAsync(
            "person@example.com",
            "Subject",
            "Body",
            null,
            CancellationToken.None));

        var notification = Assert.Single(repository.Added);
        Assert.Null(notification.SentAtUtc);
        Assert.Equal(new[] { "add", "save", "send" }, events);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task Delivery_not_attempted_leaves_the_persisted_notification_pending()
    {
        var events = new List<string>();
        var repository = new RecordingNotificationRepository(events, orderExists: true);
        var emailSender = new RecordingEmailSender(
            events,
            outcome: EmailDeliveryOutcome.NotAttempted);
        var dispatcher = new NotificationDispatcher(repository, emailSender);

        var notification = await dispatcher.CreateEmailNotificationAsync(
            "person@example.com",
            "Subject",
            "Body",
            null,
            CancellationToken.None);

        Assert.Same(notification, Assert.Single(repository.Added));
        Assert.Null(notification.SentAtUtc);
        Assert.Equal(new[] { "add", "save", "send" }, events);
        Assert.Equal(1, repository.SaveCount);
        Assert.Equal(1, emailSender.CallCount);
    }

    [Fact]
    public async Task Missing_order_is_rejected_before_persistence_or_delivery()
    {
        var events = new List<string>();
        var repository = new RecordingNotificationRepository(events, orderExists: false);
        var emailSender = new RecordingEmailSender(events);
        var dispatcher = new NotificationDispatcher(repository, emailSender);

        await Assert.ThrowsAsync<RequestValidationException>(() => dispatcher.CreateEmailNotificationAsync(
            "person@example.com",
            "Subject",
            "Body",
            999,
            CancellationToken.None));

        Assert.Empty(repository.Added);
        Assert.Equal(0, repository.SaveCount);
        Assert.Equal(0, emailSender.CallCount);
        Assert.Equal(new[] { "order-exists" }, events);
    }

    private sealed class RecordingEmailSender(
        List<string> events,
        Exception? exception = null,
        EmailDeliveryOutcome outcome = EmailDeliveryOutcome.AcceptedByProvider) : IEmailSender
    {
        private int callCount;

        public int CallCount => callCount;

        public Task<EmailDeliveryOutcome> SendAsync(
            string recipientEmail,
            string subject,
            string body,
            CancellationToken cancellationToken)
        {
            callCount++;
            events.Add("send");
            return exception is null
                ? Task.FromResult(outcome)
                : Task.FromException<EmailDeliveryOutcome>(exception);
        }
    }

    private sealed class RecordingNotificationRepository(List<string> events, bool orderExists)
        : INotificationRepository
    {
        public List<Notification> Added { get; } = new();
        public int SaveCount { get; private set; }

        public Task<IReadOnlyList<Notification>> GetNotificationsAsync(
            string? recipientEmail,
            int? orderId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Notification?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> OrderExistsAsync(int orderId, CancellationToken cancellationToken)
        {
            events.Add("order-exists");
            return Task.FromResult(orderExists);
        }

        public void Add(Notification notification)
        {
            Added.Add(notification);
            events.Add("add");
        }

        public void Remove(Notification notification) => throw new NotSupportedException();

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCount++;
            events.Add("save");
            return Task.CompletedTask;
        }
    }
}
