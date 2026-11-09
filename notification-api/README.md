# Notification Service API

An ASP.NET Core 8 API for product catalog, cart, order tracking, notification workflows, and local account authentication. The API uses SQL Server through Entity Framework Core, publishes versioned OpenAPI documents through Swagger, and supports URL-based API versions 1 and 2.

Week 4 makes notification admission durable across broker outages. The API commits each job, its items, and a versioned command in one SQL transaction. A dedicated Outbox Relay publishes that command through RabbitMQ with publisher confirms. The API consumes the main command queue and processes jobs; the separate notification worker only forwards ready retry messages back to the main queue.

## Architecture at a glance

There are three independent deployable processes:

- The API is built from [NSA.csproj](NSA.csproj). It owns HTTP contracts, local account authentication, idempotent job admission, SQL persistence, and status queries. It does not connect to RabbitMQ.
- The Outbox Relay is built from [NSA.Outbox.Worker.csproj](../notification.outbox.worker/NSA.Outbox.Worker.csproj). It leases pending SQL outbox rows, publishes their stable payloads with confirms, and schedules bounded retries.
- The Retry Forwarder is built from [NSA.Worker.csproj](../notification.worker/NSA.Worker.csproj). It only consumes ready retry messages and republishes them to the main command queue. It has no database connection.

The API owns SQL persistence and main-queue processing. The retry forwarder and Outbox Relay are separate executables with no project reference to the API. The API declares the shared topology and registers its queue consumer when `RabbitMq:Enabled` is true. See the [retry-forwarder layout](../notification.worker/README.md) and [relay layout](../notification.outbox.worker/README.md).

The following layered folders belong to the API:

- `Presentation` owns controllers, OpenAPI, and global API error handling.
- `Application` owns use-case contracts, immutable settings, and the persistence/host ports consumed by workflows.
- `Domain` owns entities, enums, and bulk-job status vocabulary.
- `Service` owns workflow orchestration and bulk-job processing.
- `Persistence` owns EF Core repository adapters, the DbContext, and migrations.
- `Infrastructure` owns authentication, configuration, and SQL health checks.

The API has no RabbitMQ client, publisher, or broker configuration. It commits bulk jobs, items, outbox commands, and idempotency receipts in a SQL transaction. The outbox worker reads those rows and owns publication routing. Legacy outbox `Exchange` and `RoutingKey` columns remain for schema compatibility; new API rows leave them empty. Deploy the updated outbox worker together with this API because older relay validation required those values.

```text
POST bulk ----> API ----> SQL job/items + Outbox + idempotency commit
POST order ---> API ----> SQL order/cart + job/items + Outbox commit
GET status ---> API ----> SQL
                              |
                        Outbox Relay -- confirmed success --> main queue
                              +-------- permanent failure --> DLQ
                              +-------- other failure --> delayed retry queue
                                                                  |
                                                            Worker Service
                                                                  |
                         SQL notification effects + Inbox receipt <-+
                                                                  |
                                          ACK on success; confirmed failure publication
                                          delay queues -> retry queue; permanent/exhausted -> DLQ
```

RabbitMQ messages contain identifiers and timestamps, not recipient addresses, subjects, or message bodies. Those item details remain in SQL Server.

See [Week 4 ADR](../docs/adr/004-week-4-idempotency-outbox-inbox-and-delivery-pipeline.md).

The `Projects` workspace root is the Git repository, so the API, Worker, tests, documentation, and frontend applications can be versioned and validated from one checkout.

## Prerequisites

For a direct host run:

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- A reachable SQL Server instance
- A reachable RabbitMQ instance only when running the separate workers
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

.NET maps a double underscore in environment-variable names to a configuration colon. For example, `ConnectionStrings__NotificationDb` overrides `ConnectionStrings:NotificationDb`.

The API and each worker use the `appsettings.json` stored in their own project directory. Environment variables remain the higher-precedence place for deployment-specific values and credentials.

| Key | Purpose | Checked-in behavior |
| --- | --- | --- |
| `ConnectionStrings:NotificationDb` | Shared SQL database for API records, jobs, and worker progress | Local SQL Server with Windows authentication |
| `Database:ApplyMigrationsOnStartup` | Applies pending EF Core migrations | `true` for the API; Compose explicitly sets `false` for the worker |
| `Authentication:Jwt:SigningKey` | At least 32 UTF-8 bytes used to sign bearer tokens | **Not tracked. Required** through an environment variable or secret provider. |
| `Authentication:Jwt:Issuer` / `Audience` | Token issuer and intended client audience | Non-secret defaults in `appsettings.json` |
| `Authentication:Jwt:AccessTokenLifetimeMinutes` | Short bearer-token lifetime | `15` minutes |
| `Authentication:MaxFailedAccessAttempts` / `LockoutMinutes` | Local credential lockout policy | `5` attempts / `15` minutes |
| `PasswordReset:FrontendUrl` | Public web origin used to build password-reset links | `http://localhost:3000` |
| `PasswordReset:Smtp:Host` / `Port` / `EnableSsl` | SMTP server used to deliver one-time reset links | Host must be configured; port `587`, TLS enabled |
| `PasswordReset:Smtp:From` | Verified sender address for reset messages | Must be configured |
| `PasswordReset:Smtp:UserName` / `Password` | SMTP authentication credentials (keep in environment/secrets) | Optional only if the server permits unauthenticated relay |
| `BulkNotifications:MaxTrackedJobs` | Maximum active persisted jobs admitted by the API | `1000` |
| `BulkNotifications:MaxBatchSize` | Maximum items in one bulk request | `100` |
| `BulkNotifications:CompletedJobRetentionMinutes` | Retention before terminal jobs are removed opportunistically | `60` |
| `Durability:IdempotencyRetentionHours` | Replay/conflict receipt lifetime; raw keys are never stored | `24` |

Worker-only settings (configure these in the worker projects):

| Key | Purpose | Checked-in behavior |
| --- | --- | --- |
| `Outbox:BatchSize` / `LeaseSeconds` | Relay claim batch and multi-instance lease | `50` / `30` seconds |
| `Outbox:PollIntervalSeconds` / `MaximumBackoffSeconds` | Empty/error polling and retry cap | `2` / `300` seconds |
| `RabbitMq:HostName` / `Port` | Broker connection | `localhost:5672` |
| `RabbitMq:UserName` / `Password` | Broker credential | No checked-in value; supply environment-specific credentials |
| `RabbitMq:VirtualHost` | Broker virtual host | `/` |
| `RabbitMq:MaxDeliveryAttempts` | Total application processing attempts | `3` |
| `RabbitMq:RetryDelayMilliseconds` | Fixed timeout delay and initial backoff delay | `5000` |
| `RabbitMq:MaximumRetryDelayMilliseconds` | Backoff cap | `300000` |
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
| Retry exchange | `nsa.notifications.retry` |
| Ready retry queue | `nsa.notifications.bulk.retry` |
| Retry holding queues (default settings) | `nsa.notifications.bulk.retry.delay.5000`, `.delay.10000`, `.delay.20000` |

The application does not call an external email provider. The worker creates persisted notification records. Polly retry and circuit breaking protect the relay's RabbitMQ publication. A confirmation can still be ambiguous, so retries may duplicate a command; the durable Inbox suppresses repeated database effects.

## Build and test

Run from the repository root:

```powershell
dotnet restore notification-api/NSA.sln
dotnet build notification-api/NSA.sln --configuration Release --no-restore
dotnet test notification-api/NSA.sln --configuration Release --no-build
dotnet list notification.tests/NSA.Tests/NSA.Tests.csproj package --vulnerable --include-transitive
```

The solution file intentionally includes the API, Outbox Relay, both consumer projects, the shared worker library, and the test project so a root build cannot silently omit a deployable process or its runtime dependency.

The automated suite covers HTTP idempotency, atomic outbox admission, relay leases/backoff, inbox deduplication, API workflows, bulk-job processing, retry forwarding, retry limits, and confirmed settlement. Compose verification covers the API, relay, retry forwarder, SQL Server, and RabbitMQ.

Most automated tests use EF Core's in-memory provider and a recording outbox publisher. They exercise API contracts, persisted job behavior, relay state transitions, processor deduplication, provider resilience, and error handling, but they do not prove a live SQL Server lock, RabbitMQ confirm, consumer acknowledgement, container readiness, restart redelivery, or DLQ path. Those scenarios require Compose verification.

## Run the API and worker directly

Start SQL Server before the API. RabbitMQ may be unavailable during HTTP admission; start it before the relay and consumers when queued work should drain. The configured SQL account must be able to migrate the database, and the broker account must be able to declare the documented exchanges and quorum queues.

Set `RabbitMq__UserName` and `RabbitMq__Password` in each worker process. The API does not need broker credentials. Broker credentials are intentionally absent from tracked application settings.

The API also requires `Authentication__Jwt__SigningKey` (at least 32 UTF-8 bytes) from an environment variable or secret provider. It intentionally has no checked-in fallback. The workers do not issue or validate API bearer tokens.

Start the API:

```powershell
dotnet run --project notification-api/NSA.csproj --launch-profile https
```

After the API has applied migrations, start the relay and both consumers in separate terminals:

```powershell
dotnet run --project notification.outbox.worker/NSA.Outbox.Worker.csproj
dotnet run --project notification.worker/NSA.Worker.csproj
```

With the HTTPS launch profile:

- Swagger UI: `https://localhost:7286/swagger`
- OpenAPI v1: `https://localhost:7286/swagger/v1/swagger.json`
- OpenAPI v2: `https://localhost:7286/swagger/v2/swagger.json`
- Liveness: `https://localhost:7286/health/live`
- Readiness: `https://localhost:7286/health/ready`

The API's `/health/live` endpoint reports process liveness. `/health/ready` probes SQL Server, the durable admission dependency, but intentionally does not fail when RabbitMQ is offline. The relay readiness marker requires both SQL access and a live RabbitMQ channel even when its backlog is empty. Consumer markers require an active broker consumer and recent SQL connectivity.

## Start the Week 3 Compose stack

[compose.week3.yml](compose.week3.yml) owns SQL Server, RabbitMQ, and the API, and includes the project-local [retry forwarder Compose file](../notification.worker/compose.yml) and [Outbox worker Compose file](../notification.outbox.worker/compose.yml). The assembled application builds and starts all five services while retaining named volumes for SQL Server and RabbitMQ data and a named bridge network.

Copy the environment template, then replace all blank values in `.env` with strong local credentials and a random JWT signing key of at least 32 characters:

```powershell
Copy-Item notification-api/.env.example notification-api/.env
```

The Compose file has no credential fallback and fails closed when the four required values are absent. `.env.example` documents the variable names without containing usable credentials.

Start and inspect the stack:

```powershell
docker compose --env-file notification-api/.env -f notification-api/compose.week3.yml config --quiet
docker compose --env-file notification-api/.env -f notification-api/compose.week3.yml up --build --detach
docker compose --env-file notification-api/.env -f notification-api/compose.week3.yml ps
docker compose --env-file notification-api/.env -f notification-api/compose.week3.yml logs --follow api worker
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
| Authentication (v2) | Sign up, login, get the bearer-token profile, and use account-scoped cart, orders, and notifications |

Swagger is the source of truth for request schemas and documented response codes. Non-success responses use `application/problem+json` and include a `traceId`.

The legacy `/api/notifications` and `/api/v{version}/notifications` CRUD routes remain an anonymous compatibility/training surface, not an account-privacy boundary. Production exposure requires a separately designed admin/service authorization policy or gateway restriction. The account-facing notification guarantees below apply specifically to the bearer-protected `/api/v2/auth/notifications` routes.

## Authentication

`POST /api/v2/auth/signup` creates a `Users` profile and linked `UserCredentials` row. It stores a PBKDF2 password hash, never a password, and enforces a normalized-email unique index. `POST /api/v2/auth/login` returns the same `401` response for an unknown email, incorrect password, or temporary lockout. Both public endpoints are rate-limited.

Successful registration and login return a 15-minute JWT access token. Send it as `Authorization: Bearer <token>` to `GET /api/v2/auth/me`, the `/api/v2/auth/cart` routes, and the `/api/v2/auth/orders` routes. Those routes derive the account identity and email from the verified token rather than accepting a client-supplied email. `GET /api/v2/auth/notifications` returns only records addressed to that verified email whose bulk job is `Completed` and whose job item is `Succeeded`; direct, incomplete, partial-success, admin-recipient, and other-recipient records remain outside the account feed.

`Users.IsAdmin` defaults to `0` for every new account and is returned as `isAdmin` in the profile. The JWT includes the corresponding `Admin` or `User` role claim. Public signup cannot set or change this flag. To promote an account, first register it, then run this SQL as a trusted database operator, replacing the email with the intended account:

```sql
UPDATE u
SET IsAdmin = 1
FROM dbo.Users AS u
JOIN dbo.UserCredentials AS c ON c.UserId = u.Id
WHERE c.NormalizedEmail = UPPER(LTRIM(RTRIM('replace-with-admin-email@example.com')));
```

The promoted account must sign in again to receive an updated role claim. This flag does not by itself protect existing endpoints; admin-only API authorization must be added explicitly where needed.

The migration also creates `UserExternalIdentities` (provider plus immutable provider subject) and `UserMfaMethods` (protected second-factor enrollment metadata). These are forward-compatible data structures only: OAuth provider sign-in, MFA enrollment/challenge, recovery codes, and account recovery flows are deliberately not enabled until their dedicated verified workflows are implemented. OAuth provider tokens are not persisted.

## Bulk-job behavior

`POST /api/v2/notifications/bulk` validates the request and atomically commits a `BulkNotificationJobs` row, its `BulkNotificationJobItems`, and an `OutboxMessages` row containing `nsa.notifications.bulk-requested.v1`. It returns `202 Accepted` after SQL commits, with a stable status URL and `X-Correlation-ID`; broker availability is not on the request path.

The relay conditionally leases due rows and validates the command, job, and notification items. It publishes valid messages to the configured main queue, sends invalid JSON, missing required data, and permanent business errors directly to the DLQ, and sends other failures through delayed retry. Terminal job status is saved before DLQ publication; retry handoffs record an active job as `Retrying`. Only confirmed publication marks the Outbox row published. If publication or terminal status persistence fails, the row remains unpublished and its lease is released with bounded database backoff. A crash after publish but before the SQL mark can duplicate a delivery; the consumer's composite `(MessageId, Handler)` Inbox receipt makes that redelivery harmless. `PublishFailed` remains readable only for legacy rows.

Order placement reuses the same job/outbox flow for its admin and visitor notifications. On SQL Server, order/items, cart removal, job/items, outbox, and an optional idempotency receipt share one transaction. A successful `201 Created` reports `X-Notification-Handoff: Confirmed`, `X-Notification-Job-ID`, and a status link. Capacity or SQL failure rejects the whole checkout rather than committing an order without its notification intent.

Both bulk creation and guest/authenticated order creation accept an optional `X-Idempotency-Key`. The server scopes it by operation and actor, stores only its SHA-256 hash, and retains the original response for the configured lifetime. The same key and semantic request returns the original IDs/body with `Idempotency-Replayed: true`; a different bulk payload returns RFC 7807 `409 Conflict`. Keys are 1-128 characters from the documented ASCII set.

Poll `GET /api/v2/notifications/bulk/{jobId}`. Persisted states include:

- `Queued`
- `Processing`
- `Retrying`
- `Completed`
- `CompletedWithErrors`
- `PublishFailed`
- `DeadLettered`

The API consumes the main queue with `autoAck: false` and prefetch `1`. Notification records, the Inbox receipt, and `Completed` status commit before acknowledgement. Duplicate committed commands are acknowledged without repeating effects. Malformed commands and permanent business failures go directly to the DLQ; other failures use a bounded retry budget. The retry forwarder consumes only the ready retry queue and republishes to the main queue after confirmed handoff. Notifications are exposed through the authenticated `GET /api/v2/auth/notifications` feed and `GET /api/v2/auth/notifications/{id}` endpoint. The `PATCH /api/v2/auth/notifications/{id}/read` endpoint persists `IsRead` when the user marks an item as read.

Retention cleanup preserves `Completed` jobs that still back visible account notifications and never opportunistically removes `DeadLettered` or legacy `PublishFailed` jobs. Expired `CompletedWithErrors` aggregates are removed with their invisible partial notifications and Outbox rows. An explicit account notification delete removes only the selected recipient's notification; the shared job, job items, Outbox rows, and other recipients' notifications remain available for retention cleanup and independent recipient actions.

Application retries use the `x-retry-count` header:

| Attempt | Header on delivery | Action after failure |
| --- | ---: | --- |
| 1 | absent or `0` | Republish with `1`; ACK the original only after confirmed publication |
| 2 | `1` | Republish with `2`; ACK the original only after confirmed publication |
| 3 | `2` | Record `DeadLettered`, confirm-publish to DLQ, then ACK |

The API consumes `CommandQueue`; the retry forwarder consumes `RetryQueue`. Separate `RetryQueue.delay.<milliseconds>` queues hold delayed attempts using queue-level TTLs, then route them into `RetryQueue` through its base routing key. Holding queues have no application consumer and use [RabbitMQ TTL](https://www.rabbitmq.com/docs/ttl) with [at-least-once dead lettering](https://www.rabbitmq.com/docs/quorum-queues#dead-lettering). The retry forwarder preserves message identity and retry headers, confirms publication to the main exchange, then acknowledges the ready retry delivery. The DLQ has no automatic consumer.

Main and ready retry queues disable automatic broker delivery limits and DLQ routing so the API can persist terminal job status before a DLQ transfer. **An existing main queue must be drained and recreated before deploying this topology.** Follow the [queue upgrade instructions](../notification.worker/README.md); startup never purges queues or SQL data.

## Opt-in poison-message demonstration

Failure injection is disabled unless `WEEK3_FAILURE_INJECTION_SUBJECT` is explicitly set for the Compose worker. Use it only for a controlled local DLQ demonstration:

```powershell
$env:WEEK3_FAILURE_INJECTION_SUBJECT = '[week3-poison]'
docker compose --env-file notification-api/.env -f notification-api/compose.week3.yml up --detach --force-recreate api
```

Submit a notification with subject `[week3-poison]`. It fails three times, waiting five then ten seconds in retry queues, and remains in `nsa.notifications.bulk.dlq`. Disable injection and recreate the worker after the demonstration. Retained DLQ messages require operator investigation and deliberate correction before any manual replay.

## Delivery guarantees and remaining boundary

Week 4 provides transactional SQL admission, a leased Outbox Relay, persistent RabbitMQ messages with confirms, and a consumer Inbox. The transport remains at-least-once; committed application effects are deduplicated by message and handler.

Explicit remaining gaps:

- Relay publication and the SQL `PublishedAtUtc` update cannot be atomic; the broker may deliver the same stable message more than once after a relay crash.
- Retry republish and acknowledgement are not atomic.
- Status concurrency detects some conflicting writes but is not a durable processing claim/lease for multiple workers.
- The demo provider's local idempotency header is not a verified provider deduplication guarantee.
- The DLQ is terminal. Saving terminal SQL status and publishing to RabbitMQ are separate operations; failed publication keeps the original delivery available for another transfer attempt.

The API and all two workers are independent build units with no shared project
references. The API owns EF Core migrations and exposes a one-shot `--migrate`
mode used by the existing migrator container; normal production API startup
disables automatic migration, and workers contain no migration logic. Week 6 consumes digest-pinned
API/Next/migration artifacts and never rebuilds on the target host.

The ignored `.env` credentials are coupled to the existing named SQL/RabbitMQ volumes. If `.env` is removed or rotated while those volumes remain, newly configured values will not automatically change credentials stored inside the services. Do not delete volumes implicitly; Week 5 provisioning must reconcile both sides or require an explicitly approved recoverable reset.

## Architecture decision records

- [ADR 001 - Week 1: OpenAPI and Clean Architecture](../docs/adr/001-week-1-openapi-and-clean-architecture.md)
- [ADR 002 - Week 2: Versioned, Resilient, Asynchronous API](../docs/adr/002-week-2-versioned-resilient-asynchronous-api.md)
- [ADR 003 - Week 3: RabbitMQ Workers and Dead-Letter Recovery](../docs/adr/003-week-3-rabbitmq-workers-and-dlq.md)
- [ADR 004 - Week 4: Idempotency, Outbox/Inbox, Next.js, and CI](../docs/adr/004-week-4-idempotency-outbox-inbox-and-delivery-pipeline.md)
- [ADR 005 - Week 5: Compose, Vault, and Frontend Runtime](../docs/adr/005-week-5-compose-vault-and-frontend-runtime.md)
- [ADR 006 - Week 6: Paired Blue/Green Release Control](../docs/adr/006-week-6-paired-blue-green-release-control.md)

## Troubleshooting

- **API startup fails while applying migrations:** verify the SQL connection, server readiness, credentials, and migration permissions.
- **Bulk submission returns 503:** inspect SQL health and active-job capacity. RabbitMQ downtime does not reject a durably admitted request.
- **Outbox Relay is not healthy:** inspect `docker compose --env-file notification-api/.env -f notification-api/compose.week3.yml logs outbox-relay rabbitmq sqlserver`; readiness probes the broker even with an empty backlog.
- **Worker is not healthy in Compose:** inspect `docker compose --env-file notification-api/.env -f notification-api/compose.week3.yml logs worker rabbitmq sqlserver`; the readiness marker is created only after consumption starts.
- **Job status remains `Queued` or `Retrying`:** verify the worker is consuming `nsa.notifications.bulk.v1` and can update the shared SQL database.
- **A job status returns 404 later:** cleanup-eligible completed jobs are removed opportunistically after the configured retention window; visible completed notifications and recoverable failure states are retained as described above.
- **HTTPS certificate error during a direct run:** run `dotnet dev-certs https --trust`, or use the `http` launch profile.
- **RabbitMQ publisher circuit is open:** restore broker connectivity and wait for the configured break duration; pending outbox rows remain durable and retry automatically.
