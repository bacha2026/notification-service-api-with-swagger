# ADR: Week 3 — RabbitMQ Queue, Separate Workers, and Safe Failure Handling

**Status:** 
**Scope:** Week 3

## Context

Before this decision, the app did its background work inside the same process as the API. The work was tied to the API running. If the API restarted, some queued jobs could disappear. The work could not be scaled up or down separately. And when a message failed, there was no clean place to keep it for investigation.

This made the system fragile. The API was responsible for both receiving the request and doing the heavy processing, which made the system harder to scale and harder to recover when things failed.

## Decision

At the Week 3 checkpoint, move the actual work out of the API and into a more reliable queue-based system.

- The API saves each job and its items in SQL Server first.
- The Week 3 implementation then sent a small, versioned message to RabbitMQ. 
- RabbitMQ acts like a durable mailbox between the API and the worker.
- A separate `notification.worker` reads the queue and processes the work.
- The worker only marks the job as complete after it has saved its progress in SQL.
- If a message fails, it is retried a limited number of times. After that, it is sent to a dead-letter queue (DLQ) instead of being endlessly retried.
- The `notification.dlq.worker` is part of this design and is responsible for handling the failed messages that land in the DLQ. 
- The historical Compose checkpoint ran SQL Server, RabbitMQ, the API, and the main worker. The cumulative Compose stack now also runs the Outbox Relay and DLQ Recovery Worker.

## Options

- Keep the work inside the API process. The app does the background work itself while the API is running. Work can disappear if the app restarts, the API and processing are stuck together, and failed jobs do not have a safe place to go.

- Use a SQL polling worker. The app keeps checking SQL to see what still needs doing. This works, but it is more complicated because the app has to keep checking, retrying, and cleaning up failed jobs itself. The worker must keep checking SQL repeatedly and it can be inefficient.
It is harder to handle retries and failed jobs cleanly. It is less scalable and less reliable than a queue-based approach

- Use RabbitMQ with a separate worker. The API hands durable work to a reliable queue, and a separate worker does the work. If a job fails repeatedly, RabbitMQ retains it in a dead-letter queue for inspection and controlled recovery.

## Trade-offs

Use RabbitMQ with a separate worker is safer and more dependable, but it is also more complicated than doing everything in one app.

- RabbitMQ needs to be installed, configured, and monitored so it keeps working properly.
- The same message may sometimes be handled more than once, so the system has to be built to handle that safely.
- The Week 3 API saved the job in SQL and sent the message separately, so the two operations were not atomic. ADR 004 closes that accepted Week 3 gap with a transactional Outbox; RabbitMQ delivery remains at least once.

## Supersession note

The process-isolation, durable-queue, bounded-retry, manual-acknowledgement,
and DLQ decisions remain active. Only the publication boundary changed:
`POST /notifications/bulk` no longer contacts RabbitMQ. It commits the job,
items, idempotency receipt, and versioned Outbox row in one SQL transaction;
`NSA.Outbox.Worker` publishes later with confirms, and the consumer Inbox
deduplicates ambiguous redelivery.
