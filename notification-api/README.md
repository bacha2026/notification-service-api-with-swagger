# Notification Service API

An ASP.NET Core 8 API for product catalog, cart, order tracking, and notification workflows. The API uses SQL Server through Entity Framework Core, publishes versioned OpenAPI documents through Swagger, and supports URL-based API versions 1 and 2.

Week 3 moves notification-job processing out of the API process. Both the public bulk endpoint and order placement persist job items in SQL Server and publish a small versioned command through RabbitMQ with publisher confirmations enabled. Separate .NET workers process commands and safely recover explicitly rejected commands from the dead-letter queue.

## Architecture at a glance

There are three deployable processes and one shared worker library:

- The API is built from [NSA.csproj](NSA.csproj). It owns HTTP contracts, job admission, SQL persistence, status queries, and RabbitMQ publication.
- The Worker Service is built from [NSA.Worker.csproj](../notification.worker/NSA.Worker.csproj). It owns RabbitMQ consumption, command retries, dead-letter routing, and notification processing.
- The DLQ Recovery Worker is built from [NSA.Dlq.Worker.csproj](../notification.dlq.worker/NSA.Dlq.Worker.csproj). It replays valid commands rejected by the main worker after resetting their persisted job state.
- [NSA.Worker.Shared.csproj](../notification.worker.shared/NSA.Worker.Shared.csproj) is not deployable. It centralizes the RabbitMQ connection lifecycle, topology/readiness checks, safe fallback requeueing, AMQP header/property handling, and command-envelope validation. Each executable retains only its own delivery policy.

The existing folders remain shared code boundaries:

- `Presentation` owns controllers, OpenAPI, and global API error handling.
- `Application` owns use-case contracts, immutable settings, and the persistence/host ports consumed by workflows.
- `Domain` owns entities, enums, and bulk-job status vocabulary.
- `Service` owns workflow orchestration and bulk-job processing.
- `Persistence` owns EF Core repository adapters, the DbContext, and migrations.
- `Infrastructure` owns the RabbitMQ publisher, topology, health checks, and publication-resilience policy.

```text
POST bulk ----> API -> SQL job/items commit -----------> RabbitMQ -> main queue
POST order ---> API -> SQL order commit -> job/items commit -----------|
GET status ---> API -> SQL                                              |
                                                              v
                                                        Worker Service
                                                              |
                                    SQL progress <- notification processor
                                                              |
                                      manual ACK, retry, or DLX -> DLQ
                                                               |
                                             DLQ Recovery Worker -> recovery delay queue -> main queue
```

RabbitMQ messages contain identifiers and timestamps, not recipient addresses, subjects, or message bodies. Those item details remain in SQL Server.

See [ADR 004](../docs/adr/004-rabbitmq-worker-and-dlq.md).

The `Projects` workspace root is the Git repository, so the API, Worker, tests, documentation, and frontend applications can be versioned and validated from one checkout.

## Prerequisites

For a direct host run:

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- A reachable SQL Server instance
- A reachable RabbitMQ instance
- Optional: a trusted ASP.NET Core development certificate

For the complete local Week 3 stack:

- Docker Desktop with Linux containers and Docker Compose
- Ports `8080` and `15672` available on the host

Confirm the local tool versions:

```powershell
dotnet --version
docker --version
docker compose version
```

The checked-in [global.json](../global.json) selects the latest installed .NET 8 feature band.

## Configuration

.NET maps a double underscore in environment-variable names to a configuration colon. For example, `RabbitMq__HostName` overrides `RabbitMq:HostName`.

The API uses its root `appsettings.json`. The identical RabbitMQ defaults are copied to [notification.worker.shared/appsettings.json](../notification.worker.shared/appsettings.json) and linked into both worker publish outputs as their root `appsettings.json`. Keep the two files synchronized; environment variables remain the higher-precedence place for deployment-specific values and credentials.

| Key | Purpose | Checked-in behavior |
| --- | --- | --- |
| `ConnectionStrings:NotificationDb` | Shared SQL database for API records, jobs, and worker progress | Local SQL Server with Windows authentication |
| `Database:ApplyMigrationsOnStartup` | Applies pending EF Core migrations | `true` for the API; Compose explicitly sets `false` for the worker |
| `BulkNotifications:MaxTrackedJobs` | Maximum active persisted jobs admitted by the API | `1000` |
| `BulkNotifications:MaxBatchSize` | Maximum items in one bulk request | `100` |
| `BulkNotifications:CompletedJobRetentionMinutes` | Retention before terminal jobs are removed opportunistically | `60` |
| `RabbitMq:HostName` / `Port` | Broker connection | `localhost:5672` |
| `RabbitMq:UserName` / `Password` | Broker credential | No checked-in value; supply environment-specific credentials |
| `RabbitMq:VirtualHost` | Broker virtual host | `/` |
| `RabbitMq:MaxDeliveryAttempts` | Total application processing attempts | `3` |
| `RabbitMq:MaxDeadLetterReplayAttempts` | Bounded automatic replay attempts for rejected DLQ commands | `3` |
| `RabbitMq:DeadLetterReplayDelayMilliseconds` | Delay before a recovered command returns to the main queue | `5000` |
| `RabbitMq:BrokerDeliveryLimit` | Quorum-queue safeguard for broker requeues | `20` |
| `RabbitMq:PrefetchCount` | Concurrent unacknowledged commands per consumer | `1` |
| `RabbitMq:FailureInjectionSubject` | Exact subject that triggers the local poison demonstration | Disabled by default |

The stable broker names are:

| Element | Name |
| --- | --- |
| Command exchange | `nsa.notifications.commands.v1` |
| Command routing key | `bulk.requested.v1` |
| Main quorum queue | `nsa.notifications.bulk.v1` |
| Dead-letter exchange | `nsa.notifications.dead-letter` |
| Dead-letter routing key | `bulk.dead-letter.v1` |
| Dead-letter quorum queue | `nsa.notifications.bulk.dlq` |
| Recovery exchange / queue | `nsa.notifications.recovery` / `nsa.notifications.bulk.recovery` |
| Parking exchange / queue | `nsa.notifications.parking` / `nsa.notifications.bulk.parking` |

The application does not call an external email provider. The worker creates persisted notification records. Polly retry and circuit breaking protect the API's outbound RabbitMQ command publication; publisher confirmation can still be ambiguous and retries can produce duplicate commands until Inbox deduplication is added.

## Build and test

Run from the repository root:

```powershell
dotnet restore notification-api/NSA.sln
dotnet build notification-api/NSA.sln --configuration Release --no-restore
dotnet test notification-api/NSA.sln --configuration Release --no-build
dotnet list notification.tests/NSA.Tests/NSA.Tests.csproj package --vulnerable --include-transitive
```

The solution file intentionally includes the API, both worker projects, the shared worker library, and the test project so a root build cannot silently omit a deployable process or its runtime dependency.

The automated suite covers API workflows, worker processing, DLQ recovery state transitions, eligibility checks, and replay-header reset. Compose verification covers the API, primary worker, DLQ recovery worker, SQL Server, and RabbitMQ.

The automated tests use EF Core's in-memory provider and replace the real RabbitMQ publisher. They exercise API contracts, persisted job behavior, processor state transitions, provider resilience, and error handling, but they do not prove SQL Server, RabbitMQ confirms, consumer acknowledgements, container readiness, restart redelivery, or the live DLQ path. Those scenarios require the Week 3 Compose verification.

## Run the API and worker directly

Start SQL Server and RabbitMQ first. The configured account must be able to migrate the database, and the broker account must be able to declare the documented exchanges and quorum queues.

Set `RabbitMq__UserName` and `RabbitMq__Password` in each host process. Broker credentials are intentionally absent from tracked application settings.

Start the API:

```powershell
dotnet run --project notification-api/NSA.csproj --launch-profile https
```

After the API has applied migrations, start both workers in separate terminals:

```powershell
dotnet run --project notification.worker/NSA.Worker.csproj
dotnet run --project notification.dlq.worker/NSA.Dlq.Worker.csproj
```

With the HTTPS launch profile:

- Swagger UI: `https://localhost:7286/swagger`
- OpenAPI v1: `https://localhost:7286/swagger/v1/swagger.json`
- OpenAPI v2: `https://localhost:7286/swagger/v2/swagger.json`
- Liveness: `https://localhost:7286/health/live`
- Readiness: `https://localhost:7286/health/ready`

The API's `/health/live` endpoint reports process liveness. `/health/ready` separately probes SQL Server and RabbitMQ with bounded timeouts. Each worker writes its readiness marker only after opening its broker connection/channel, registering its consumer, and connecting to SQL; it removes the marker and reconnects when either dependency is lost.

## Start the Week 3 Compose stack

[compose.week3.yml](compose.week3.yml) builds and starts SQL Server 2022, RabbitMQ 4.3 Management, the API, the notification worker, and the DLQ recovery worker. It uses named volumes for SQL Server and RabbitMQ data and a named bridge network.

Copy the environment template, then replace all blank values in `.env` with strong local credentials:

```powershell
Copy-Item notification-api/.env.example notification-api/.env
```

The Compose file has no credential fallback and fails closed when the three required values are absent. `.env.example` documents the variable names without containing usable credentials.

Start and inspect the stack:

```powershell
docker compose --env-file notification-api/.env -f notification-api/compose.week3.yml config --quiet
docker compose --env-file notification-api/.env -f notification-api/compose.week3.yml up --build --detach
docker compose --env-file notification-api/.env -f notification-api/compose.week3.yml ps
docker compose --env-file notification-api/.env -f notification-api/compose.week3.yml logs --follow api worker dlq-recovery-worker
```

Local URLs:

- API and Swagger: `http://localhost:8080/swagger`
- API liveness: `http://localhost:8080/health/live`
- API readiness: `http://localhost:8080/health/ready`
- RabbitMQ Management: `http://localhost:15672`

The HTTP-only Compose profile explicitly disables HTTPS redirection. HTTPS redirection remains enabled by default for direct runs and is disabled only through the Compose environment setting. Host ports `8080` and `15672` bind to loopback only.

Use the configured `WEEK3_RABBITMQ_USERNAME` and `WEEK3_RABBITMQ_PASSWORD` values from the ignored `.env` file for RabbitMQ Management. SQL Server and AMQP are available to containers on `sqlserver:1433` and `rabbitmq:5672`; this Compose file does not publish those two ports to the host.

Stop containers while retaining the named data volumes:

```powershell
docker compose --env-file notification-api/.env -f notification-api/compose.week3.yml down
```

Do not use `down --volumes` unless intentionally discarding all Week 3 SQL and RabbitMQ local data.

## API conventions

The preferred routes contain the API version:

```text
/api/v1/products
/api/v2/products
```

Version 1 is deprecated and includes `Deprecation` and `Sunset` response headers. Version 2 is the current default. Temporary unversioned compatibility routes remain available and resolve to the default version.

| Resource | Operations |
| --- | --- |
| Products | List, get, create, and update products |
| Cart | Get a visitor cart; add, update, and remove items |
| Orders | List/get orders, place an order with queued admin/visitor notifications, and update tracking status |
| Notifications | List/get, create, update, and delete notification records |
| Bulk notifications | Persist and publish a job; query its SQL-backed status |

Swagger is the source of truth for request schemas and documented response codes. Non-success responses use `application/problem+json` and include a `traceId`.

## Bulk-job behavior

`POST /api/v2/notifications/bulk` validates the request, persists a `BulkNotificationJobs` row and its `BulkNotificationJobItems`, and then publishes `nsa.notifications.bulk-requested.v1`. The publisher uses a persistent JSON message, mandatory routing, and a confirmation-tracked channel. A successful call returns `202 Accepted`, a stable status URL, and `X-Correlation-ID`.

If publication fails, the API records `PublishFailed` when it can and returns retryable `503 Service Unavailable`; it does not claim that the work was accepted. If the broker accepted the command but the API observed an ambiguous failure, an arriving command can recover the persisted `PublishFailed` job and continue processing.

Order placement reuses the same persisted job and broker command for its admin and visitor in-app notifications. `POST /api/v2/orders` remains `201 Created` because the order is complete, while `X-Notification-Handoff` reports `Confirmed`, `Unconfirmed`, or `Rejected`. Confirmed and unconfirmed outcomes include `X-Notification-Job-ID` plus a `Link` with `rel="notification-status"`; an unconfirmed publisher outcome retains the observable job as `PublishFailed`. Rejected means capacity or the bounded handoff timeout prevented a usable job reference. Notification records are created by the Worker Service, not inside the API request.

Poll `GET /api/v2/notifications/bulk/{jobId}`. Persisted states include:

- `Queued`
- `Processing`
- `Retrying`
- `Completed`
- `CompletedWithErrors`
- `PublishFailed`
- `DeadLettered`
- `RecoveryPending`

The worker consumes with `autoAck: false` and prefetch `1`. It saves item progress and the terminal job state before manually acknowledging a successful command. `BulkNotificationJobs.Status` is an EF Core concurrency token so competing API and worker state transitions do not silently overwrite one another. A duplicate for an already completed job is acknowledged without repeating completed work; a redelivery for an already `DeadLettered` job is rejected without requeue so RabbitMQ routes it to the DLQ. Permanent request-validation failures are recorded per item and processing continues; unexpected processing failures use command-level retry.

Application retries use the `x-retry-count` header:

| Attempt | Header on delivery | Action after failure |
| --- | ---: | --- |
| 1 | absent or `0` | Republish with `1`; ACK the original only after confirmed publication |
| 2 | `1` | Republish with `2`; ACK the original only after confirmed publication |
| 3 | `2` | Persist `DeadLettered`, then reject without requeue |

The main queue's dead-letter settings route the final rejection to `nsa.notifications.bulk.dlq`. The DLQ Recovery Worker accepts only commands whose broker death metadata says they were rejected from the main queue. It changes an eligible `DeadLettered` job to `RecoveryPending`, removes its exhausted `x-retry-count`, publishes it with confirms to a durable five-second recovery queue, and only then acknowledges the original DLQ delivery. The recovery queue dead-letters the command back to the main exchange. This lets the primary worker send the pending admin and visitor notifications without duplicating already-succeeded job items. Malformed, unknown, non-rejected, and replay-limit-exhausted messages are published with confirms to the durable parking queue for inspection. If any recovery persistence or publish operation fails, the DLQ delivery is NACKed with requeue.

## Opt-in poison-message demonstration

Failure injection is disabled unless `WEEK3_FAILURE_INJECTION_SUBJECT` is explicitly set for the Compose worker. Use it only for a controlled local DLQ demonstration:

```powershell
$env:WEEK3_FAILURE_INJECTION_SUBJECT = '[week3-poison]'
docker compose --env-file notification-api/.env -f notification-api/compose.week3.yml up --detach --force-recreate worker
```

Submitting a bulk item whose subject exactly equals `[week3-poison]` then forces the command-level failure path. The worker should make three attempts and route the command to the named DLQ. This is a test hook, not production behavior.

Disable it and recreate the worker after the demonstration:

```powershell
Remove-Item Env:WEEK3_FAILURE_INJECTION_SUBJECT -ErrorAction SilentlyContinue
docker compose --env-file notification-api/.env -f notification-api/compose.week3.yml up --detach --force-recreate worker
```

## Delivery guarantees and Week 4 boundary

Week 3 provides durable SQL status, RabbitMQ durable quorum queues, persistent messages, publisher confirms, and at-least-once consumer behavior. It does not provide exactly-once processing.

Explicit remaining gaps:

- SQL persistence and direct RabbitMQ publication are a dual write. There is no transactional Outbox or relay, so an API crash can leave a persisted job without a command.
- Order persistence precedes notification-job admission because the generated order ID is part of each item. Capacity, timeout, or broker uncertainty after that commit cannot be made atomic in Week 3; the API preserves the committed `201` order and exposes the handoff outcome in response headers. The post-commit attempt is bounded to 15 seconds and is not canceled merely because the HTTP client disconnects.
- Publisher confirmation and the HTTP response are not atomic; the broker may accept work even if the caller receives an inconclusive failure.
- Retry republish and acknowledgement are not atomic.
- There is no Inbox or processed-message table to deduplicate command deliveries.
- The broker `MessageId` is generated after the job commit and is not yet persisted with the job; the Week 4 Outbox record must own a stable ID.
- Status concurrency detects some conflicting writes but is not a durable processing claim/lease for multiple workers.
- There is no client idempotency key or safe response replay for repeated bulk submissions.
- A worker crash after an external side effect but before durable progress and ACK can repeat that side effect.
- The demo provider's local idempotency header is not a verified provider deduplication guarantee.
- Automatic replay is intentionally limited to commands explicitly rejected from the main queue. Broker delivery-limit, malformed, unsupported, unknown-job, and replay-limit-exhausted messages remain in the parking queue for operator investigation. Full reconciliation remains future work.

Transactional Outbox publication, Inbox/consumer deduplication, client idempotency, and full reconciliation remain future work.

Before Week 5–6 delivery automation, split shared application/domain/persistence/infrastructure code from the executable Web project so the Worker no longer references and publishes the API project. Tighten publish contents so test settings, launch metadata, and evidence files are absent from runtime images. Move startup `MigrateAsync` to a gated expand/contract migration job, and use immutable application/base-image references before blue/green promotion.

The ignored `.env` credentials are coupled to the existing named SQL/RabbitMQ volumes. If `.env` is removed or rotated while those volumes remain, newly configured values will not automatically change credentials stored inside the services. Do not delete volumes implicitly; Week 5 provisioning must reconcile both sides or require an explicitly approved recoverable reset.

## Architecture decision records

- [ADR 001 - Clean Architecture](../docs/adr/001-url-api-versioning.md)
- [ADR 002 - Asynchronous bulk notifications](../docs/adr/002-problem-details-errors.md)
- [ADR 003 - RabbitMQ, WorkerService, and publication resilience](../docs/adr/003-background-bulk-notifications-and-resilience.md)
- [ADR 004 - Domain-aligned event-driven microservices](../docs/adr/004-rabbitmq-worker-and-dlq.md)

## Troubleshooting

- **API startup fails while applying migrations:** verify the SQL connection, server readiness, credentials, and migration permissions.
- **Bulk submission returns 503:** inspect API logs and RabbitMQ health/routing; the persisted job may be marked `PublishFailed`.
- **Worker is not healthy in Compose:** inspect `docker compose --env-file notification-api/.env -f notification-api/compose.week3.yml logs worker rabbitmq sqlserver`; the readiness marker is created only after consumption starts.
- **Job status remains `Queued` or `Retrying`:** verify the worker is consuming `nsa.notifications.bulk.v1` and can update the shared SQL database.
- **A job status returns 404 later:** terminal jobs are removed opportunistically after the configured retention window.
- **HTTPS certificate error during a direct run:** run `dotnet dev-certs https --trust`, or use the `http` launch profile.
- **RabbitMQ publisher circuit is open:** restore broker connectivity and wait for the configured break duration before the half-open trial; the committed order remains valid and its handoff header reports the notification outcome.
