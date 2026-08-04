# ADR 004: Use RabbitMQ and a separate worker for bulk notifications

**Status:** Accepted and implemented for Week 3; technical validation passed, with external delivery/review pending

**Date:** 2026-07-20  
**Scope:** Bulk-notification job persistence, broker topology, worker boundary, retry, and dead-letter handling  
**Supersedes:** The in-memory queue and in-process worker decision in [ADR 003](003-background-bulk-notifications-and-resilience.md). ADR 003's outbound-provider abstraction and Polly policies remain in force.

## Context

The Week 2 bulk endpoint returns `202 Accepted` with a job identifier and processes the request through a bounded in-memory `Channel<T>`. A hosted worker in the API process consumes the channel, while a process-local dictionary supplies job status. This kept submission fast—the recorded Week 2 p95 was 2.004 ms—and made the asynchronous contract easy to demonstrate.

That design is intentionally temporary. Queue contents and job status disappear when the API restarts, another API instance cannot observe the same jobs, the worker cannot be deployed or scaled independently, and poison work has no quarantine path. Week 3 requires the API and worker to be separate processes, RabbitMQ to host the work queue, SQL Server to hold shared job progress, and exhausted or invalid work to be visible in a dead-letter queue.

The design must preserve two boundaries from the existing application:

- the public bulk endpoint continues to return a job identifier and status location; and
- provider delivery remains behind `IEmailSender`, including the disabled-by-default provider behavior and resilience rules retained from ADR 003.

Week 3 does not add transactional publication or exactly-once processing. Those are explicit Week 4 concerns.

## Decision drivers

- Keep HTTP submission independent of batch-processing duration.
- Make job status queryable from any API instance and while the worker is stopped.
- Preserve broker-accepted commands across API or worker restart.
- Deploy and scale the API and worker independently.
- Acknowledge a command only after its processing progress is durably recorded.
- Bound retries and quarantine poison or exhausted commands for inspection.
- Carry a versioned contract and stable identifiers through API, broker, worker, SQL, and logs.
- Keep recipient addresses, subjects, and bodies out of the broker command and operational logs.
- Run the complete Week 3 topology locally through Docker Desktop.
- Describe at-least-once behavior and the remaining failure windows without claiming exactly-once delivery.

## Considered options

| Option | Benefits | Costs and risks | Outcome |
| --- | --- | --- | --- |
| Keep the bounded `Channel<T>` and hosted worker | Lowest operational cost; already fast and tested | Queue and status are process-local; restart loses work; no independent scaling or DLQ | Rejected beyond Week 2 |
| Persist work in SQL and poll from a worker | Durable state with one infrastructure dependency | Polling delay, lease/locking complexity, database load, and custom retry/DLQ behavior | Rejected for Week 3 |
| Use RabbitMQ with a separate worker and SQL job state | Native competing consumers, manual acknowledgement, redelivery, DLX/DLQ, and local Docker support | Additional infrastructure, eventual consistency, duplicate risk, topology and schema operations | **Selected** |
| Use a streaming platform such as Kafka | Durable replayable event history and high throughput | A work-queue command is the immediate need; consumer offsets and platform weight do not earn their cost here | Rejected |

## Decision

### Process and persistence boundaries

Run the ASP.NET Core API and a .NET Worker Service as separately deployable processes.

The API validates a bulk request and persists the job and all of its items in SQL Server before publishing work. The API status endpoint reads the persisted job and counters from SQL rather than process memory. The worker reads the same job/items through application and persistence abstractions and keeps provider delivery behind `IEmailSender`.

RabbitMQ transports only a small command that points to the SQL job. Notification recipient, subject, and body are not copied into the broker message.

```text
Client ──POST bulk──> API ──persist job/items──> SQL Server
                         └──publish command───> RabbitMQ ──> Worker

Client ──GET status──> API ──read status──────> SQL Server <──progress── Worker
                                                             └──> IEmailSender

RabbitMQ main queue ──final reject──> DLX ──> DLQ
```

This split provides process isolation and independent deployment, but it deliberately retains a shared SQL data boundary for the Week 3 anchor project.

### Command contract

Use a version 1 JSON command with these fields:

```json
{
  "schemaVersion": 1,
  "messageId": "d613f2e8-4286-4cfb-82f5-5f17839469bd",
  "jobId": "29c210a0-b10f-45e5-ae11-46d9837fc17a",
  "correlationId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
  "createdAtUtc": "2026-07-20T08:00:00Z"
}
```

- `schemaVersion` selects the consumer contract. Version 1 is immutable except for backward-compatible additions; a breaking change requires a new version and a coexistence plan.
- `messageId` identifies the logical command and remains stable when the application republishes it for a broker-level retry.
- `jobId` identifies the persisted job and is the lookup key used by the worker.
- `correlationId` joins HTTP, publisher, consumer, SQL status, and DLQ evidence. When a trace context is available, it is propagated rather than replaced.
- `createdAtUtc` records when the logical command was created; it is not changed for retry deliveries.

The publisher also sets the AMQP content type to JSON, marks the message persistent, and mirrors the logical message and correlation identifiers into the available RabbitMQ message properties. Publisher channels enable RabbitMQ confirmation tracking and mandatory routing so a publish call can detect broker rejection or an unroutable command. A confirmation is transport evidence only; it does not make the preceding SQL commit and RabbitMQ publish one transaction. Consumers reject malformed JSON, missing required identifiers, and unsupported schema versions as poison commands.

The implementation uses `RabbitMQ.Client` version `7.2.1`. Publisher and consumer code use its asynchronous APIs and dispose connections/channels through their host lifetimes.

### Broker topology

Declare the following topology idempotently at startup:

| Element | Name | Decision |
| --- | --- | --- |
| Command exchange | `nsa.notifications.commands.v1` | Durable direct exchange |
| Routing key | `bulk.requested.v1` | Routes version 1 bulk-job commands |
| Main queue | `nsa.notifications.bulk.v1` | Durable quorum queue bound to the command exchange |
| Dead-letter exchange | `nsa.notifications.dead-letter` | Durable direct exchange |
| Dead-letter routing key | `bulk.dead-letter.v1` | Routes final bulk-command failures from the main queue to the DLQ |
| Dead-letter queue | `nsa.notifications.bulk.dlq` | Durable quorum queue bound to the DLX with `bulk.dead-letter.v1` |

The main queue is configured with `nsa.notifications.dead-letter` as its dead-letter exchange and `bulk.dead-letter.v1` as its dead-letter routing key. A final negative acknowledgement with `requeue: false` therefore routes the command to `nsa.notifications.bulk.dlq`. Queue names are stable so operators, tests, and dashboards refer to one vocabulary.

RabbitMQ data uses a Docker named volume in the Week 3 local stack. Queue durability and persistent messages protect messages that RabbitMQ has accepted, subject to the direct-publication limitation below.

### Consumer acknowledgement and progress

The Worker Service consumes with automatic acknowledgement disabled.

- On success, the worker commits the job's durable progress and terminal state to SQL before manually acknowledging the RabbitMQ delivery.
- Interim item progress is also persisted so the API can report cross-process status while the job runs.
- `BulkNotificationJobs.Status` is an EF Core concurrency token, preventing competing API/worker status writes from silently overwriting each other.
- A duplicate command for `Completed` or `CompletedWithErrors` work is acknowledged without repeating completed work.
- A command that arrives for a `PublishFailed` job is treated as evidence of an ambiguous but broker-accepted publish and may recover the job into processing.
- A redelivery for an already `DeadLettered` job is rejected without requeue so it reaches the configured DLQ.
- If the worker stops before acknowledgement, RabbitMQ may redeliver the command. This chooses possible duplication over silent message loss.
- If SQL cannot durably record progress, the worker must not acknowledge the delivery as successful.
- An unhandled delivery or acknowledgement/bookkeeping error is NACKed with requeue when possible. If that operation cannot be completed, the worker closes the channel so RabbitMQ returns the unacknowledged delivery and a prefetch-1 consumer is not left wedged.

A worker crash after an external side effect but before the SQL commit or broker acknowledgement can repeat that side effect on redelivery. Week 3 does not claim that provider calls are idempotent.

### Retry and dead-letter policy

Broker-level processing retries use the application header `x-retry-count` and allow three total processing attempts:

| Delivery | `x-retry-count` | Failure action |
| --- | ---: | --- |
| Initial attempt | `0` or absent | Republish with `1` when the failure is retryable |
| Second attempt | `1` | Republish with `2` when the failure is retryable |
| Third attempt | `2` | Record the terminal failure and reject with `requeue: false` |

For a retry, the worker republishes the same logical command to `nsa.notifications.commands.v1` with routing key `bulk.requested.v1`, preserves the command identifiers, increments `x-retry-count`, and acknowledges the prior delivery only after the retry publication is confirmed. The confirm and acknowledgement are still not one atomic action. Transactional Outbox/Inbox processing and reconciliation remain Week 4 work.

Malformed JSON, missing required identifiers, unsupported schema versions, and wrong AMQP message types are non-retryable and are rejected without requeue after the failure is safely logged. They do not consume three attempts because repeating the same invalid bytes cannot repair them.

An optional, explicitly enabled development setting may treat a documented subject value as a deterministic poison item. It exists only to demonstrate three bounded processing attempts and the DLQ path locally. It is disabled by default, must not be enabled outside controlled local evidence runs, and must not write the subject or other message content to operational logs.

Broker-level retries are separate from the outbound HTTP timeout/retry/circuit-breaker policy in ADR 003. Telemetry must distinguish a command attempt from an individual provider HTTP attempt because composing both policies can multiply physical provider calls.

### Observability and operations

Application logs and persisted status use `jobId`, `messageId`, `correlationId`, schema version, retry count, and a safe failure classification. They do not contain recipient addresses, subjects, bodies, credentials, or raw provider responses.

The RabbitMQ Management UI supplies local evidence for the main queue, consumer, redelivery, and DLQ. A DLQ entry is quarantined work requiring inspection and an explicit remediation or replay decision; its presence is not automatic recovery.

The API's `/health/live` endpoint is process-only. `/health/ready` probes SQL Server and RabbitMQ with bounded timeouts. Compose gates startup on infrastructure health and probes API readiness. The worker readiness marker requires broker consumer registration and SQL connectivity and is removed before reconnecting after either dependency is lost.

## Failure semantics and accepted gaps

| Failure point | Week 3 behavior |
| --- | --- |
| Validation or SQL commit fails before publication | The operation is not represented as accepted work. |
| RabbitMQ publish call fails | Record `PublishFailed` when possible and return retryable 503 rather than claim acceptance. If an ambiguously accepted command later arrives, the worker may recover that persisted job and process it. |
| API crashes between SQL commit and RabbitMQ publication | A persisted job can exist without a command. There is no automatic relay in Week 3. |
| RabbitMQ accepts a command but the API crashes before completing the HTTP response/state transition | The command can run even though the caller did not receive a conclusive response. |
| API and worker race to update job status | The status property is an EF Core concurrency token; a conflicting write fails instead of silently replacing the observed state. |
| Worker crashes before acknowledgement | RabbitMQ redelivers; persisted status remains queryable. Duplicate processing is possible. |
| Worker side effect succeeds but durable progress/acknowledgement does not | Redelivery can repeat the side effect. |
| Duplicate reaches a completed job | Acknowledge it without repeating completed item work. This is not a general Inbox/deduplication guarantee. |
| Redelivery reaches an already `DeadLettered` job | Reject again with `requeue: false` so the command follows the DLX route to the DLQ. |
| Unhandled delivery or bookkeeping error occurs | NACK/requeue when possible; otherwise close the channel so the broker can return the unacknowledged delivery. |
| Command is malformed or uses an unsupported schema | Reject without requeue; route through the DLX to the DLQ. |
| Retryable processing fails on all three attempts | Record safe terminal failure, reject without requeue, and route to the DLQ. |
| RabbitMQ restarts | The recorded run proves broker/consumer recovery, retention of DLQ work, and completion of a command that was unacknowledged when the broker restarted. |

SQL persistence plus a direct RabbitMQ publish is a dual write. It is not atomic, and durable queues alone do not close the gap. The API must not advertise an exactly-once or lossless-acceptance guarantee for this Week 3 design.

## Consequences

Positive consequences:

- API restarts no longer erase the authoritative job status.
- Worker restarts can cause broker redelivery instead of silent in-memory loss.
- API and worker can be deployed and scaled independently.
- Poison and exhausted commands have a named, inspectable quarantine path.
- A small, versioned command minimizes broker payload size and PII exposure.
- Stable correlation fields make the HTTP-to-DLQ path reviewable.

Costs and risks:

- RabbitMQ adds credentials, topology, health, storage, monitoring, and recovery work.
- SQL and RabbitMQ create eventual consistency and a direct-publication failure window.
- At-least-once delivery permits duplicates; an acknowledgement is not an exactly-once guarantee.
- Application-level republish plus acknowledgement has its own non-atomic handoff.
- The Worker Service remains coupled to the shared SQL schema and application contracts.
- Broker retries can multiply provider retries if attempt budgets are not observed separately.
- DLQ retention and replay require an operational policy; unattended DLQ growth is a failure mode.
- Competing consumers improve throughput but weaken any assumption of global processing order.

## Validation

The Week 3 decision is considered implemented only when automated checks and captured evidence show that:

1. The API and Worker Service build and run as independent processes.
2. Docker Desktop starts the API, worker, RabbitMQ Management, and SQL Server with readiness checks and named persistence volumes.
3. A valid bulk request persists its job/items, returns the expected asynchronous contract, reaches `nsa.notifications.bulk.v1`, and completes through the worker.
4. Job status and counters remain queryable through the API while the worker is stopped or processing.
5. Stopping the worker before acknowledgement and restarting it demonstrates redelivery without losing the persisted job.
6. Restarting RabbitMQ with one unacknowledged command demonstrates durable in-flight recovery and consumer reconnection.
7. Malformed or unsupported commands reach `nsa.notifications.bulk.dlq` without exposing their contents in logs.
8. The opt-in deterministic poison scenario records attempts with `x-retry-count` values `0`, `1`, and `2`, then reaches the DLQ after the third failure.
9. `jobId`, `messageId`, and `correlationId` connect API response, logs, SQL state, RabbitMQ delivery, and DLQ evidence.
10. Successful deliveries are manually acknowledged only after durable SQL progress; automatic acknowledgement is not enabled.

The final technical evidence records `76 / 76` passing tests; successful API and worker image builds; all four Compose containers healthy; dependency-aware readiness; happy-path completion; stop-before-ACK redelivery; in-flight broker recovery; malformed JSON, unsupported schema, and wrong AMQP type dead-lettering; bounded poison attempts; identifier correlation; an unavailable-broker 503 with persisted `PublishFailed` plus a regression-tested `PublishFailed → Retrying → DeadLettered` path; and a zero-match PII/credential log review.

The printable HTML and 9-page PDF presentation deck are prepared, but live delivery, reviewer feedback, Gist publication, and work against EM-provided React snippets remain external pending items. Preparing those artifacts does not prove delivery or approval.

## Deferred to Week 4

- Transactional Outbox publication from SQL.
- An Outbox relay that records confirm-backed publication state and reconciles unpublished rows.
- Inbox/processed-message deduplication in the worker.
- Client idempotency and safe response replay.
- Protection against duplicate external side effects, including a verified provider deduplication contract.
- Automated, policy-controlled DLQ replay.

## Revisit when

Revisit this decision when Week 4 adds Outbox/Inbox and idempotency; when a managed broker replaces local RabbitMQ; when throughput, ordering, tenant isolation, retention, or compliance requirements change; when the command must carry more than a job reference; or when API and worker data ownership no longer justifies a shared SQL schema.

## Related records

- [ADR 003: Process bulk notifications asynchronously and protect outbound calls](003-background-bulk-notifications-and-resilience.md)
- [Week 1 architecture decision](../week-01-architecture-decision.md)
- [Week 3 architecture presentation script](../presentations/week-03-rabbitmq-architecture.md)
- [Printable Week 3 presentation](../presentations/week-03-rabbitmq-architecture.html)
- [Prepared 9-page presentation PDF](../../evidence/week-03/presentation.pdf)
- [Week 3 topology record](../../evidence/week-03/topology.md)
- [Week 3 verification checklist](../../evidence/week-03/verification-checklist.md)
- [Week 3 presentation feedback record](../../evidence/week-03/feedback.md)
