using Microsoft.Extensions.DependencyInjection;
using NSA.Application.Abstractions;
using NSA.Application.Contracts;
using NSA.Domain.Enums;
using NSA.Service;

namespace NSA.Infrastructure.BackgroundServices;

public sealed class BulkNotificationWorker(BulkNotificationJobService jobs, IServiceScopeFactory scopeFactory, ILogger<BulkNotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in jobs.ReadAllAsync(stoppingToken))
        {
            job.Start();
            try
            {
                foreach (var item in job.Notifications)
                {
                    try
                    {
                        await using var scope = scopeFactory.CreateAsyncScope();
                        if (item.Channel == NotificationChannel.Email)
                        {
                            var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();
                            await dispatcher.CreateEmailNotificationAsync(
                                item.RecipientEmail,
                                item.Subject,
                                item.Body,
                                item.OrderId,
                                stoppingToken);
                        }
                        else
                        {
                            var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
                            await notificationService.CreateNotificationAsync(
                                new CreateNotificationRequest(item.RecipientEmail, item.Channel, item.Subject, item.Body, item.OrderId),
                                stoppingToken);
                        }

                        job.RecordSuccess();
                    }
                    catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                    {
                        logger.LogError(exception, "Bulk notification job {JobId} failed to process one notification.", job.Id);
                        job.RecordFailure(exception);
                    }
                }

                job.Complete();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                job.Cancel();
                throw;
            }
        }
    }
}
