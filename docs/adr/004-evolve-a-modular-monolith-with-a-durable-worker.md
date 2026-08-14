# ADR 004: Evolve a Modular Monolith with a Durable Worker

**Status:** 

## Context

The Notification Service provides versioned HTTP APIs while processing bulk notifications asynchronously and reliably. An in-process `BackgroundService` is simple, but queued work can be lost when the API restarts and cannot scale independently from HTTP traffic. A separate worker and RabbitMQ provide durable, isolated processing, but message delivery is at-least-once: retries or uncertain broker acknowledgements.

The service also needs repeatable local infrastructure, secure configuration, and releases that do not interrupt traffic. The current system is one bounded domain with one ownership model.

## Decision

Keep the Notification Service as a modular monolith: one API codebase with clear internal boundaries and a separately deployed `Notification WorkerService`.

The API accepts bulk requests, persists job state, and sends notification commands through RabbitMQ. The primary worker consumes commands independently, uses bounded retries for transient failures, and sends malformed or exhausted messages to a dead-letter queue (DLQ). A dedicated recovery worker safely redrives only commands explicitly rejected by the primary queue; malformed, unknown, non-rejected, and replay-limit-exhausted commands are retained in a parking queue for investigation.

Introduce reliability and operations in stages:

1. Use RabbitMQ, a separate worker, retries, and a DLQ for durable asynchronous processing.
2. Add a Transactional Outbox so the job and its outbound command are saved in one database transaction; a relay publishes pending Outbox messages to RabbitMQ.
3. Add client idempotency keys and worker-side message deduplication so retries and at-least-once delivery do not create duplicate jobs or notifications.
4. Use Docker Compose and Vault-backed configuration for repeatable environments and secret management.
5. Deploy API and worker versions with health checks, blue/green cutover, and rollback support.

Defer microservice extraction until a domain requires independent ownership, scaling, data ownership, or release cadence.

## Options

- **In-process worker:** simplest implementation and local operation, but work can be lost on API restart, competes with HTTP traffic, and cannot scale or deploy independently.
- **Microservices now:** creates independent deployment and data boundaries, but requires service ownership, contract versioning, distributed observability, eventual-consistency handling, and greater operational support.
- **Modular API plus durable worker:** selected. It provides durable, independently scalable processing while keeping the current domain and operational model manageable. The worker boundary can be extracted later if justified.

## Trade-offs

### More components to operate

**Trade-off:** The system is more complex than an in-process background worker.

**Why:** RabbitMQ, a separate worker, an Outbox relay, and deployment tooling are additional runtime components.

**How:** Run the stack through Docker Compose; monitor broker connectivity, queue depth, DLQ count, relay failures, and worker health. Maintain runbooks for broker outages and message recovery.

### Eventual completion

**Trade-off:** A successful API request does not mean notifications have already been sent.

**Why:** The API stores the job and delegates processing to an asynchronous worker through RabbitMQ.

**How:** Return `202 Accepted` with a job ID. Persist job status and expose a status endpoint so clients can track queued, processing, completed, and failed work.

### Duplicate delivery must be safe

**Trade-off:** A command may be delivered or processed more than once.

**Why:** RabbitMQ provides at-least-once delivery; a worker crash or ambiguous acknowledgement can cause redelivery. Publisher retries can also create duplicate commands.

**How:** Give every command a stable message ID. Store processed IDs in an Inbox/deduplication table and skip duplicates. Pass an idempotency key to the notification provider when it supports one.

### Reliable publishing requires persistence and relay work

**Trade-off:** The Outbox adds database records, a relay process, and cleanup work.

**Why:** Saving a job and publishing a RabbitMQ command are separate actions. A crash between them can otherwise leave a saved job with no command.

**How:** Save the job and Outbox message in one database transaction. A relay publishes pending records, marks confirmed messages as dispatched, retries failures, and removes or archives old dispatched records under a retention policy.

### Failed messages require human handling

**Trade-off:** A DLQ does not automatically resolve failed messages.

**Why:** Some failures are permanent, such as malformed data, invalid configura
