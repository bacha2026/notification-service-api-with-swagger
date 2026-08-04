# Week 3 RabbitMQ topology and message flow

- **Recorded:** 2026-07-20
- **Evidence level:** Source inspection, passing API/worker image builds and 76 tests, hardened healthy four-service Compose startup, plus live happy-path, worker-redelivery, in-flight broker-recovery, unavailable-broker publish-failure, malformed/unsupported-schema/wrong-type, poison-attempt, correlation, log-review, and DLQ evidence
- **Decision record:** [ADR 004](../../docs/adr/004-rabbitmq-worker-and-dlq.md)

## Process and data boundaries

The Week 3 implementation has two executable processes:

| Process | Project | Responsibility |
| --- | --- | --- |
| API | `NSA.csproj` | Validate bulk requests, persist jobs/items, publish confirmed commands, return status URLs, and query SQL-backed progress |
| Worker | `src/NSA.Worker/NSA.Worker.csproj` | Consume RabbitMQ commands, process pending SQL items, persist progress, retry failures, manually ACK, and dead-letter exhausted work |

Both processes use `NotificationDbContext` and the same SQL Server schema. Only the API applies migrations in the Week 3 Compose stack; the worker starts after the API is healthy with `Database__ApplyMigrationsOnStartup=false`.

```text
Client
  |
  | POST /api/v2/notifications/bulk
  v
API --1. persist job and items--------------------------> SQL Server
  |
  | 2. publish persistent BulkNotificationRequestedV1
  |    using mandatory routing + publisher confirms
  v
nsa.notifications.commands.v1
  |
  | bulk.requested.v1
  v
nsa.notifications.bulk.v1  --manual delivery-->  Worker
                                                      |
                                                      | load pending items
                                                      | persist each result
                                                      | persist terminal status
                                                      v
Client --GET status--> API ------------------------> SQL Server

Successful command: Worker --BasicAck--> main queue
Retryable failure:  Worker --confirmed republish--> command exchange
                              \--BasicAck original--> main queue
Final/poison failure: Worker --BasicReject(requeue:false)--> DLX --> DLQ
```

## Exact broker names and declarations

The API publisher and worker both declare the topology idempotently through `RabbitMqTopology.DeclareAsync`.

| Element | Name | Declaration |
| --- | --- | --- |
| Command exchange | `nsa.notifications.commands.v1` | Durable, non-auto-delete direct exchange |
| Command routing key | `bulk.requested.v1` | Binding and publish key for schema version 1 |
| Main queue | `nsa.notifications.bulk.v1` | Durable, non-exclusive, non-auto-delete quorum queue |
| Main queue delivery safeguard | `x-delivery-limit=20` | Broker-level limit for repeated quorum-queue deliveries |
| Main queue DLX | `x-dead-letter-exchange=nsa.notifications.dead-letter` | Routes rejected/exhausted deliveries |
| Main queue DLQ key | `x-dead-letter-routing-key=bulk.dead-letter.v1` | Stable dead-letter route |
| Dead-letter exchange | `nsa.notifications.dead-letter` | Durable, non-auto-delete direct exchange |
| Dead-letter routing key | `bulk.dead-letter.v1` | Binding key for final bulk failures |
| Dead-letter queue | `nsa.notifications.bulk.dlq` | Durable, non-exclusive, non-auto-delete quorum queue |

The worker requests prefetch `1` and consumes the main queue with `autoAck=false`.

## Version 1 command

The JSON body is `BulkNotificationRequestedV1`:

| Field | Meaning |
| --- | --- |
| `schemaVersion` | Must equal `1` |
| `messageId` | Stable logical command identifier, preserved during application retry |
| `jobId` | Primary key of the persisted job |
| `correlationId` | HTTP-to-publisher-to-worker correlation value |
| `createdAtUtc` | Original logical command creation time, preserved during retry |

AMQP properties set by the publisher:

- `ContentType=application/json`
- `ContentEncoding=utf-8`
- persistent delivery mode
- `MessageId`, `CorrelationId`, type `nsa.notifications.bulk-requested.v1`, and timestamp
- mandatory routing on a channel with publisher confirmations and confirmation tracking enabled

Recipients, subjects, bodies, and order identifiers are not included in the RabbitMQ message. They are stored in `BulkNotificationJobItems`.

## SQL state

Migration `20260720041904_AddWeek3BulkNotificationJobs` creates:

- `BulkNotificationJobs`, keyed by job GUID, with schema version, correlation ID, counts, status, timestamps, and a sanitized error;
- `BulkNotificationJobItems`, keyed by bigint, with a unique `(JobId, Sequence)` index, notification input, item status, attempt count, and sanitized last error.

Job states are `Queued`, `Processing`, `Retrying`, `Completed`, `CompletedWithErrors`, `PublishFailed`, and `DeadLettered`. Item states are `Pending`, `Succeeded`, and `Failed`.

`BulkNotificationJobs.Status` is configured as an EF Core concurrency token. A competing API/worker status write therefore raises a concurrency conflict instead of silently overwriting the state read by that process.

The API persists the job and all items before publishing. It returns `202 Accepted` only after the confirmation-tracked publish completes. If publication throws, the service attempts to persist `PublishFailed` and returns a sanitized retryable 503 response. If the broker accepted the command despite an ambiguous API-side failure, the arriving command can recover that `PublishFailed` job into processing.

## Worker acknowledgement order

### Successful or partially successful job

1. Validate the command schema, identifiers, correlation value, and AMQP type.
2. Load the job and its items from SQL.
3. Resolve persisted state: acknowledge a duplicate for `Completed` or `CompletedWithErrors`; return the dead-letter disposition for `DeadLettered`; or recover `PublishFailed` into processing.
4. Set `Processing` and persist.
5. Process only `Pending` items in sequence order.
6. Persist every item result and counter update.
7. Persist `Completed` or `CompletedWithErrors`.
8. Send `BasicAck` for the RabbitMQ delivery.

A request-validation or argument failure is an item-level permanent failure. It is sanitized, persisted, and does not stop later items. An unexpected exception leaves the item pending and enters command-level retry.

### Retryable command failure

Application retry uses header `x-retry-count` and permits three total attempts:

| Delivery | Header | Failure action |
| --- | ---: | --- |
| First | absent or `0` | Persist `Retrying`, republish with `1`, then ACK the original after confirmed publication |
| Second | `1` | Persist `Retrying`, republish with `2`, then ACK the original after confirmed publication |
| Third | `2` | Persist `DeadLettered`, then reject without requeue |

Retry publication preserves the JSON body, message ID, correlation ID, type, timestamp, and persistence flag. If retry publication fails, the original delivery is rejected with `requeue=true`; it is not acknowledged.

The worker rejects an already-`DeadLettered` redelivery with `requeue=false`, which sends it through the same DLX route instead of acknowledging it off the queue. Other unhandled delivery or bookkeeping failures are NACKed with requeue when the channel is usable. If that operation fails or the channel is no longer open, the worker closes the channel so RabbitMQ can return the unacknowledged delivery; this avoids a prefetch-1 consumer remaining wedged behind one delivery.

### Non-retryable command

Malformed JSON, an unsupported schema version, empty identifiers/correlation, or the wrong AMQP message type is rejected immediately with `requeue=false`. RabbitMQ routes the rejected delivery through the configured DLX to `nsa.notifications.bulk.dlq`.

## Opt-in deterministic poison path

`RabbitMq:FailureInjectionSubject` is null in checked-in application settings. Compose maps the worker-only environment variable `WEEK3_FAILURE_INJECTION_SUBJECT`; it defaults to empty and therefore disabled.

For a controlled demo only, setting:

```text
WEEK3_FAILURE_INJECTION_SUBJECT=[week3-poison]
```

causes a job containing a pending item with that exact subject to fail before processing. This allows the three command attempts and final DLQ route to be demonstrated without enabling the external provider. The subject is not written to the worker's operational failure log.

## Compose wiring

| Service | Image/build | Host exposure | Readiness dependency |
| --- | --- | --- | --- |
| `sqlserver` | SQL Server 2022 | Internal `1433` only | `sqlcmd SELECT 1` health check |
| `rabbitmq` | `rabbitmq:4.3-management` | Management `15672`; AMQP internal `5672` | `rabbitmq-diagnostics -q ping` |
| `api` | `deploy/week-03/Dockerfile.api` | loopback `8080` | Starts after healthy SQL/RabbitMQ; `/health/ready` probes both dependencies |
| `worker` | `deploy/week-03/Dockerfile.worker` | None | Starts after healthy SQL/RabbitMQ/API; health checks `/tmp/nsa-worker-ready` |

Named resources:

- SQL volume: `nsa-week3-sqlserver-data-v2`
- RabbitMQ volume: `nsa-week3-rabbitmq-data-v2`
- network: `nsa-week3-network`

The API's `/health/live` endpoint is process-only; `/health/ready` probes SQL Server and RabbitMQ with bounded timeouts. Compose separately gates on SQL Server and RabbitMQ container health. Its HTTP-only API configuration disables HTTPS redirection; the normal application default remains enabled outside Compose.

The worker creates its readiness file only after it has opened its connection/channel, declared topology, applied QoS, registered its consumer, and connected to SQL Server. While connected, it rechecks SQL every five seconds. It deletes the marker when either dependency fails or the host stops, so the marker represents both an active consumer and recent SQL connectivity.

## Runtime validation evidence

The following live paths have been captured:

- [Build and test transcript](build-test.txt): solution-wide API, Worker, and test build succeeded with zero warnings/errors; all 76 tests passed.
- [Compose transcript](compose-start.txt): configuration and final API/worker image builds succeeded; SQL Server, RabbitMQ, API, and worker reached healthy state with named volumes/network present.
- [Happy path](happy-path.md): HTTP 202 followed by persisted `Completed` status with `1 / 1` processed/succeeded counters.
- [Worker restart/redelivery](restart-redelivery.md): one unacknowledged delivery existed before a force-kill, then the replacement worker completed the same job.
- [RabbitMQ restart](broker-restart.md): the worker consumer recovered, subsequent work completed, and an existing DLQ entry remained present.
- [Dependency readiness](readiness-recovery.md): RabbitMQ and SQL Server outages independently kept liveness at 200, changed readiness to 503, removed worker readiness, made API/worker Docker health unhealthy, and recovered fully.
- [Publish failure](publish-failure.md): stopping RabbitMQ caused HTTP 503 and a persisted SQL `PublishFailed` state; restarting RabbitMQ restored the worker consumer. The processor source and regression suite cover recovery when an ambiguously accepted command actually arrives, including `PublishFailed → Retrying → DeadLettered` when processing keeps failing; this unavailable-broker run did not itself claim an enqueued command.
- [Malformed JSON](malformed-message.md): a correctly routed, matching-type invalid JSON delivery increased the DLQ count.
- [Unsupported schema](unsupported-message.md): a correctly typed schema-version-99 command was rejected through the DLX and increased the DLQ count.
- [Bounded poison attempts](poison-attempts.md): three attempts ended in persisted `DeadLettered` status with final `x-retry-count=2` and `x-death` reason `rejected`.
- [Correlation](correlation.md): recorded job, message, and correlation identifiers connect the exercised paths.
- [Runtime log review](log-review.md): combined API/worker logs produced zero matches for exercised message content, recipient addresses, or SQL/RabbitMQ credentials.
- [DLQ screenshot](dlq.md): at 12:53 Asia/Manila, RabbitMQ 4.3.2 displayed the named durable quorum DLQ with `Ready=5`, `Unacknowledged=0`, and `Total=5`.

The poison scenario is repeatable with `scripts/Invoke-Week3PoisonProof.ps1`. Malformed JSON, unsupported schema, and wrong-AMQP-type deliveries each have runtime DLQ proof in the final rerun.

## Guarantees and open failure windows

- SQL status survives API restarts, subject to the configured terminal-job retention cleanup.
- The RabbitMQ restart run retained an existing DLQ message, recovered the worker consumer, and completed a command that was unacknowledged when RabbitMQ restarted.
- Consumer delivery is at least once. Manual ACK prevents intentional acknowledgement before durable progress but does not create exactly-once processing.
- The status concurrency token, terminal-state dispositions, ambiguous `PublishFailed` recovery, and unhandled-delivery requeue/channel-close behavior reduce known race and stuck-delivery risks; they do not provide general deduplication or exactly-once behavior.
- SQL commit and initial RabbitMQ publish are not atomic.
- Confirmed retry publication and ACK of the prior delivery are not atomic.
- There is no Outbox relay, Inbox/processed-message deduplication, client idempotency, or automatic DLQ replay.
- An external side effect can repeat when the worker crashes after the side effect but before SQL progress and ACK.

These gaps are explicitly deferred to Week 4.

## Source references

- `Infrastructure/Messaging/RabbitMqTopology.cs`
- `Infrastructure/Messaging/RabbitMqBulkNotificationPublisher.cs`
- `Application/Contracts/Notification/BulkNotificationMessages.cs`
- `Service/Notification/BulkNotificationJobService.cs`
- `Service/Notification/BulkNotificationProcessor.cs`
- `src/NSA.Worker/RabbitMqBulkNotificationWorker.cs`
- `Persistence/Migrations/20260720041904_AddWeek3BulkNotificationJobs.cs`
- `compose.week3.yml`

Detailed pass/pending boundaries are tracked in [verification-checklist.md](verification-checklist.md).
