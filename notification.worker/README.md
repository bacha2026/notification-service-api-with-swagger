# Notification retry forwarder

`notification.worker` is a RabbitMQ-only background service. It consumes ready messages from `RetryQueue`, republishes each message to the main command exchange, and acknowledges the retry delivery only after the broker confirms the main-queue publication. It does not connect to SQL Server and does not process notification jobs.

The notification API owns main-queue consumption and bulk-job processing. Its database-backed processor creates notification records and records job progress. The authenticated API returns completed notifications to the front end; `PATCH /api/v2/auth/notifications/{id}/read` changes the read state when the user marks a notification as read.

Delayed retry queues use queue TTLs and dead-letter into the ready retry queue. This worker only consumes the ready retry queue; it does not consume the main queue, DLQ, or delay buckets. Messages retain their persistent AMQP properties and retry headers while moving back to the main queue.

If publishing to the main queue fails, the worker leaves the original retry message unacknowledged and asks RabbitMQ to redeliver it. The worker declares the shared topology on startup so the API, outbox relay, and retry forwarder use the same queue definitions. Queue declarations are durable and startup never deletes or purges broker data.

Run the full local stack through `notification-api/compose.week3.yml`. To run this process separately, provide RabbitMQ settings and run `dotnet run --project notification.worker/NSA.Worker.csproj` from the repository root. No database connection string is used.
