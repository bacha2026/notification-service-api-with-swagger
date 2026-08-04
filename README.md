# Notification Service API

An ASP.NET Core 8 API for product catalog, cart, order tracking, and notification workflows. The API uses SQL Server through Entity Framework Core, publishes versioned OpenAPI documents through Swagger, and supports URL-based API versions 1 and 2.

Week 3 moves bulk notification processing out of the API process. The API persists each job and its items in SQL Server, publishes a small versioned command through RabbitMQ with publisher confirmations enabled, and returns a status URL. A separate .NET Worker Service consumes the command and writes progress back to the shared database.

## Architecture at a glance

There are two deployable processes:

- The API is built from [NSA.csproj](NSA.csproj). It owns HTTP contracts, job admission, SQL persistence, status queries, and RabbitMQ publication.
- The Worker Service is built from [src/NSA.Worker/NSA.Worker.csproj](src/NSA.Worker/NSA.Worker.csproj). It owns RabbitMQ consumption, command retries, dead-letter routing, and notification processing.

The existing folders remain shared code boundaries:

- `Presentation` owns controllers, OpenAPI, and global API error handling.
- `Application` owns service abstractions and request, response, and message contracts.
- `Domain` owns entities, enums, and bulk-job status vocabulary.
- `Service` owns workflow orchestration and bulk-job processing.
- `Persistence` owns EF Core repositories, the DbContext, and migrations.
- `Infrastructure` owns the RabbitMQ and outbound-email adapters.

```text
POST bulk -> API -> SQL job/items -> RabbitMQ command exchange -> main queue
GET status -> API -> SQL                                      |
                                                              v
                                                        Worker Service
                                                              |
                                    SQL progress <- processor + IEmailSender
                                                              |
                                      manual ACK, retry, or DLX -> DLQ
```

RabbitMQ messages contain identifiers and timestamps, not recipient addresses, subjects, or message bodies. Those item details remain in SQL Server.

See [ADR 004](docs/adr/004-rabbitmq-worker-and-dlq.md), the [Week 3 topology record](evidence/week-03/topology.md), and the [Week 3 verification checklist](evidence/week-03/verification-checklist.md).

The Next.js `rendering-lab` and Angular `angular-onpush-demo` applications are workspace siblings, as requested, but the existing Git root is still this `notification-api` directory. [ADR 005](docs/adr/005-workspace-repository-boundary.md) records why the repository boundary must be approved before Week 4 CI: a workflow in this repository cannot version or build sibling folders from a normal checkout. No Git history or application location was changed implicitly.

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

The checked-in [global.json](global.json) selects the latest installed .NET 8 feature band.

## Configuration

.NET maps a double underscore in environment-variable names to a configuration colon. For example, `RabbitMq__HostName` overrides `RabbitMq:HostName`.

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

The checked-in Postbound-named email adapter remains a training seam and is disabled by default. In disabled mode it makes no network request, returns `NotAttempted`, and leaves the saved notification intent pending. Do not enable it until the provider URL, authentication, schema, timeout budget, telemetry, and deduplication contract are verified. Never commit provider credentials.

## Build and test

Run from the repository root:

```powershell
dotnet restore NSA.sln
dotnet build NSA.sln --configuration Release --no-restore
dotnet test NSA.sln --configuration Release --no-build
dotnet list tests/NSA.Tests/NSA.Tests.csproj package --vulnerable --include-transitive
```

The solution file intentionally includes the API, Worker, and test projects so a root build cannot silently omit either deployable process.

The final Week 1-3 rerun passed all `76 / 76` automated tests. The API and worker container images also built successfully, and SQL Server, RabbitMQ, the API, and the worker all reached healthy state in Compose. The captured results are linked from the [Week 3 verification checklist](evidence/week-03/verification-checklist.md).

The automated tests use EF Core's in-memory provider and replace the real RabbitMQ publisher. They exercise API contracts, persisted job behavior, processor state transitions, provider resilience, and error handling, but they do not prove SQL Server, RabbitMQ confirms, consumer acknowledgements, container readiness, restart redelivery, or the live DLQ path. Those scenarios require the Week 3 Compose verification.

The existing Week 1-2 live probes remain available when the API is listening on port 5099:

```powershell
.\scripts\Invoke-Week12Smoke.ps1 -BaseUrl http://127.0.0.1:5099
.\scripts\Measure-BulkNotificationLatency.ps1 -BaseUrl http://127.0.0.1:5099 -Samples 25
```

## Run the API and worker directly

Start SQL Server and RabbitMQ first. The configured account must be able to migrate the database, and the broker account must be able to declare the documented exchanges and quorum queues.

Set `RabbitMq__UserName` and `RabbitMq__Password` in each host process. Broker credentials are intentionally absent from tracked application settings.

Start the API:

```powershell
dotnet run --project NSA.csproj --launch-profile https
```

After the API has applied migrations, start the worker in another terminal:

```powershell
dotnet run --project src/NSA.Worker/NSA.Worker.csproj
```

With the HTTPS launch profile:

- Swagger UI: `https://localhost:7286/swagger`
- OpenAPI v1: `https://localhost:7286/swagger/v1/swagger.json`
- OpenAPI v2: `https://localhost:7286/swagger/v2/swagger.json`
- Liveness: `https://localhost:7286/health/live`
- Readiness: `https://localhost:7286/health/ready`

The API's `/health/live` endpoint reports process liveness. `/health/ready` separately probes SQL Server and RabbitMQ with bounded timeouts. The worker writes its readiness marker only after opening the broker connection/channel, registering its consumer, and connecting to SQL; it removes the marker and reconnects when either dependency is lost.

## Start the Week 3 Compose stack

[compose.week3.yml](compose.week3.yml) builds and starts SQL Server 2022, RabbitMQ 4.3 Management, the API, and the separate worker. It uses named volumes for SQL Server and RabbitMQ data and a named bridge network.

Generate the required local SQL and RabbitMQ credentials in an ignored `.env` file. The initializer is idempotent and never prints secret values:

```powershell
.\scripts\Initialize-Week3Environment.ps1
```

The Compose file has no credential fallback and fails closed when the three required values are absent. `.env.example` documents the variable names without containing usable credentials.

Start and inspect the stack:

```powershell
docker compose -f compose.week3.yml config --quiet
docker compose -f compose.week3.yml up --build --detach
docker compose -f compose.week3.yml ps
docker compose -f compose.week3.yml logs --follow api worker
```

Local URLs:

- API and Swagger: `http://localhost:8080/swagger`
- API liveness: `http://localhost:8080/health/live`
- API readiness: `http://localhost:8080/health/ready`
- RabbitMQ Management: `http://localhost:15672`

The HTTP-only Compose profile explicitly disables HTTPS redirection. HTTPS redirection remains enabled by default for direct runs and is disabled only through the Compose environment setting. Host ports `8080` and `15672` bind to loopback only.

Use the generated `WEEK3_RABBITMQ_USERNAME` and `WEEK3_RABBITMQ_PASSWORD` values from the ignored `.env` file for RabbitMQ Management. SQL Server and AMQP are available to containers on `sqlserver:1433` and `rabbitmq:5672`; this Compose file does not publish those two ports to the host.

Stop containers while retaining the named data volumes:

```powershell
docker compose -f compose.week3.yml down
```

Do not use `down --volumes` unless intentionally discarding all Week 3 SQL and RabbitMQ local data.

After the stack is healthy, the following helper registers the consumer, pauses the worker, submits a job, waits until RabbitMQ exposes one unacknowledged delivery, force-kills the paused worker, starts a replacement, and waits for completion:

```powershell
.\scripts\Invoke-Week3RestartProof.ps1
```

This is a deliberate stop-before-ACK redelivery demonstration. Its captured result is recorded in [restart-redelivery.md](evidence/week-03/restart-redelivery.md).

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
| Orders | List/get orders, place an order, and update tracking status |
| Notifications | List/get, create, update, and delete notification records |
| Bulk notifications | Persist and publish a job; query its SQL-backed status |

Swagger is the source of truth for request schemas and documented response codes. Non-success responses use `application/problem+json` and include a `traceId`.

## Bulk-job behavior

`POST /api/v2/notifications/bulk` validates the request, persists a `BulkNotificationJobs` row and its `BulkNotificationJobItems`, and then publishes `nsa.notifications.bulk-requested.v1`. The publisher uses a persistent JSON message, mandatory routing, and a confirmation-tracked channel. A successful call returns `202 Accepted`, a stable status URL, and `X-Correlation-ID`.

If publication fails, the API records `PublishFailed` when it can and returns retryable `503 Service Unavailable`; it does not claim that the work was accepted. If the broker accepted the command but the API observed an ambiguous failure, an arriving command can recover the persisted `PublishFailed` job and continue processing.

Poll `GET /api/v2/notifications/bulk/{jobId}`. Persisted states include:

- `Queued`
- `Processing`
- `Retrying`
- `Completed`
- `CompletedWithErrors`
- `PublishFailed`
- `DeadLettered`

The worker consumes with `autoAck: false` and prefetch `1`. It saves item progress and the terminal job state before manually acknowledging a successful command. `BulkNotificationJobs.Status` is an EF Core concurrency token so competing API and worker state transitions do not silently overwrite one another. A duplicate for an already completed job is acknowledged without repeating completed work; a redelivery for an already `DeadLettered` job is rejected without requeue so RabbitMQ routes it to the DLQ. Permanent request-validation failures are recorded per item and processing continues; unexpected processing failures use command-level retry.

Application retries use the `x-retry-count` header:

| Attempt | Header on delivery | Action after failure |
| --- | ---: | --- |
| 1 | absent or `0` | Republish with `1`; ACK the original only after confirmed publication |
| 2 | `1` | Republish with `2`; ACK the original only after confirmed publication |
| 3 | `2` | Persist `DeadLettered`, then reject without requeue |

The main queue's dead-letter settings route the final rejection to `nsa.notifications.bulk.dlq`. Malformed JSON, missing identifiers, unsupported schema versions, and wrong message types are rejected without application retry and routed through the same DLX. If retry publication fails, the original delivery is requeued instead of acknowledged. Other unhandled delivery or bookkeeping failures are NACKed with requeue when possible; if the acknowledgement operation cannot be completed, the worker closes the channel so RabbitMQ returns the unacknowledged delivery instead of leaving the prefetch-1 consumer wedged.

## Opt-in poison-message demonstration

Failure injection is disabled unless `WEEK3_FAILURE_INJECTION_SUBJECT` is explicitly set for the Compose worker. Use it only for a controlled local DLQ demonstration:

```powershell
$env:WEEK3_FAILURE_INJECTION_SUBJECT = '[week3-poison]'
docker compose -f compose.week3.yml up --detach --force-recreate worker
```

Submitting a bulk item whose subject exactly equals `[week3-poison]` then forces the command-level failure path. The worker should make three attempts and route the command to the named DLQ. This is a test hook, not production behavior.

The repeatable helper used for the captured run is `scripts/Invoke-Week3PoisonProof.ps1`.

Disable it and recreate the worker after the demonstration:

```powershell
Remove-Item Env:WEEK3_FAILURE_INJECTION_SUBJECT -ErrorAction SilentlyContinue
docker compose -f compose.week3.yml up --detach --force-recreate worker
```

The captured three-attempt result, persisted status, identifiers, final DLQ headers, RabbitMQ Management image, and passed runtime log-content review are indexed in [evidence/week-03](evidence/week-03).

## Delivery guarantees and Week 4 boundary

Week 3 provides durable SQL status, RabbitMQ durable quorum queues, persistent messages, publisher confirms, and at-least-once consumer behavior. It does not provide exactly-once processing.

Explicit remaining gaps:

- SQL persistence and direct RabbitMQ publication are a dual write. There is no transactional Outbox or relay, so an API crash can leave a persisted job without a command.
- Publisher confirmation and the HTTP response are not atomic; the broker may accept work even if the caller receives an inconclusive failure.
- Retry republish and acknowledgement are not atomic.
- There is no Inbox or processed-message table to deduplicate command deliveries.
- The broker `MessageId` is generated after the job commit and is not yet persisted with the job; the Week 4 Outbox record must own a stable ID.
- Status concurrency detects some conflicting writes but is not a durable processing claim/lease for multiple workers.
- There is no client idempotency key or safe response replay for repeated bulk submissions.
- A worker crash after an external side effect but before durable progress and ACK can repeat that side effect.
- The demo provider's local idempotency header is not a verified provider deduplication guarantee.
- DLQ inspection is available, but automated replay and reconciliation are not implemented. Broker delivery-limit or invalid-command dead-lettering can leave SQL status behind until Week 4 reconciliation exists.

Transactional Outbox publication, Inbox/consumer deduplication, client idempotency, reconciliation, and controlled replay are Week 4 work.

Before Week 5–6 delivery automation, split shared application/domain/persistence/infrastructure code from the executable Web project so the Worker no longer references and publishes the API project. Tighten publish contents so test settings, launch metadata, and evidence files are absent from runtime images. Move startup `MigrateAsync` to a gated expand/contract migration job, and use immutable application/base-image references before blue/green promotion.

The ignored `.env` credentials are coupled to the existing named SQL/RabbitMQ volumes. If `.env` is removed or rotated while those volumes remain, newly generated credentials will not automatically change credentials stored inside the services. Do not delete volumes implicitly; Week 5 provisioning must reconcile/rotate both sides or require an explicitly approved recoverable reset. Current local proof helpers are Week 3-only and must also stop passing credentials through process arguments before claiming Vault-grade secret handling.

## Decision and evidence records

- [ADR 001 - URL-segment API versioning](docs/adr/001-url-api-versioning.md)
- [ADR 002 - Problem Details errors](docs/adr/002-problem-details-errors.md)
- [ADR 003 - Background bulk notifications and resilient outbound calls](docs/adr/003-background-bulk-notifications-and-resilience.md)
- [ADR 004 - RabbitMQ, separate worker, retries, and DLQ](docs/adr/004-rabbitmq-worker-and-dlq.md)
- [ADR 005 - proposed workspace/repository boundary for Week 4 CI](docs/adr/005-workspace-repository-boundary.md)
- [Current Week 1–3 smoke-testing guide](docs/SmokeTestingGuide.html) and [PDF](SmokeTestingGuide.pdf)
- [Repository-aware six-week execution plan](docs/plans/NSA-6-Week-Execution-Plan.html) and [PDF](docs/plans/NSA-6-Week-Execution-Plan.pdf)
- [Week 3 architecture presentation script](docs/presentations/week-03-rabbitmq-architecture.md)
- [Printable Week 3 presentation](docs/presentations/week-03-rabbitmq-architecture.html)
- [Prepared 9-page presentation PDF](evidence/week-03/presentation.pdf) - prepared but not delivered live
- [Week 3 topology record](evidence/week-03/topology.md)
- [Week 3 verification checklist](evidence/week-03/verification-checklist.md)
- [Evidence index](evidence/README.md)

## Troubleshooting

- **API startup fails while applying migrations:** verify the SQL connection, server readiness, credentials, and migration permissions.
- **Bulk submission returns 503:** inspect API logs and RabbitMQ health/routing; the persisted job may be marked `PublishFailed`.
- **Worker is not healthy in Compose:** inspect `docker compose -f compose.week3.yml logs worker rabbitmq sqlserver`; the readiness marker is created only after consumption starts.
- **Job status remains `Queued` or `Retrying`:** verify the worker is consuming `nsa.notifications.bulk.v1` and can update the shared SQL database.
- **A job status returns 404 later:** terminal jobs are removed opportunistically after the configured retention window.
- **HTTPS certificate error during a direct run:** run `dotnet dev-certs https --trust`, or use the `http` launch profile.
- **Postbound is enabled without a key:** set `Postbound__Enabled=false`; real-provider use remains intentionally blocked.
