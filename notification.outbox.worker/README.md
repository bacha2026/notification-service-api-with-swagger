# Notification outbox worker

A standalone .NET background worker that leases pending SQL outbox rows, validates the command and referenced job/items, and publishes to RabbitMQ with confirmation tracking.

- `Program.cs` registers the database, publisher, relay, and hosted worker.
- `OutboxWorker.cs` runs the polling loop and readiness checks.
- `OutboxService.cs` claims rows, reads and validates jobs/items, routes failures, and saves publication status or database retry backoff.
- `OutboxOptions.cs` defines polling, lease, batch, and backoff settings.
- `Messaging/` contains the command format, validation, failure policy, connection/topology, publisher, and publisher retry/circuit-breaker policy.
- `Data/` contains plain database records and mappings for only outbox rows, jobs, items, and order IDs.

There are no application/domain/service layers, business repositories, notification-processing services, API DTOs, or authentication dependencies. `IOutboxMessagePublisher` is a small broker boundary used to verify confirmation and outage behavior. All worker code uses the `NSA.Outbox.Worker` namespace.

The API owns schema migrations and consumes the main command queue. Valid commands go to the main queue. Invalid JSON, missing required data, and permanent business errors go directly to the DLQ. Other failures enter delayed retry queues, whose ready messages are forwarded to the main queue by `notification.worker`. Permanent failures save an active job as `DeadLettered` before DLQ publication; retry handoffs mark active jobs `Retrying`. A row is marked published only after confirmed handoff. Broker outages or failed terminal status updates leave it in SQL for another attempt. Notification details remain in SQL; failure publications preserve the original payload and durable message/job identity for diagnosis. Since this worker reads SQL rows rather than RabbitMQ deliveries, `PublishedAtUtc` is its settlement marker; `BasicAckAsync` belongs to a queue consumer.

The outbox worker declares main, retry, and dead-letter exchanges. The API and retry forwarder declare the shared queue topology and bindings. Publications are persistent, mandatory, and confirmed, so rows remain pending when the destination is unavailable. Readiness passively checks that the main, retry, and DLQ queues exist. All processes must use matching destination settings and retry-delay buckets.

Compose explicitly connects to RabbitMQ's internal port `5672`. Its readiness check requires a successful relay/probe heartbeat within the last 15 seconds, so a stalled connection or DNS lookup cannot leave an old readiness file reporting healthy indefinitely.

Run the full local stack through `notification-api/compose.week3.yml`, or provide the database and RabbitMQ settings and run `dotnet run --project notification.outbox.worker/NSA.Outbox.Worker.csproj` from the repository root. The container uses the .NET runtime without ASP.NET Core.
