# Notification Service API

An ASP.NET Core 8 API for product catalog, cart, order tracking, and notification workflows. The project uses SQL Server through Entity Framework Core, publishes an OpenAPI document through Swagger UI, supports URL-based API versions 1 and 2, and includes an in-process background job for bulk notification processing.

## Architecture at a glance

The solution is a single deployable application organized into layers, with business-capability folders inside each layer:

- `Presentation` owns HTTP controllers and global API error handling.
- `Application` owns service abstractions and request/response contracts.
- `Domain` owns entities and enums.
- `Service` owns use-case orchestration.
- `Persistence` owns EF Core repositories, the DbContext, and migrations.
- `Infrastructure` owns the email-provider adapter and hosted background worker.

See the [Week 1 architecture decision](docs/week-01-architecture-decision.md), the editable [draw.io source](drawio/notification.drawio), the exported [Week 2 runtime architecture](docs/architecture/week-02-runtime-architecture.pdf), and the [ADRs](docs/adr) for the reasons and tradeoffs behind this design.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- A reachable SQL Server instance
- PowerShell examples below assume Windows; equivalent environment variables work in other shells
- Optional: a trusted ASP.NET Core development certificate for the HTTPS launch profile

Confirm the SDK before continuing:

```powershell
dotnet --version
```

The checked-in `global.json` selects the latest installed .NET 8 feature band, so the reported major version must be `8`.

## Configuration

The checked-in `appsettings.json` contains local-development defaults and no provider key. Override environment-specific values with environment variables; .NET maps a double underscore (`__`) to a configuration colon (`:`).

| Key | Purpose | Checked-in behavior |
| --- | --- | --- |
| `ConnectionStrings:NotificationDb` | SQL Server connection used by EF Core | Local default SQL Server instance (`Server=.`) with Windows authentication |
| `Database:ApplyMigrationsOnStartup` | Applies pending EF Core migrations during application startup | `true`; tests override it |
| `NotificationEmails:AdminEmail` | Recipient for administrator order notifications | Development address |
| `NotificationEmails:DefaultVisitorEmail` | Fallback visitor identity when none is supplied | Development address |
| `Postbound:BaseUrl` | Absolute base URL used by the demo adapter | `https://api.postbound.com/` placeholder; no production provider contract is verified |
| `Postbound:ApiKey` | Bearer credential expected by the demo adapter | Empty |
| `Postbound:Enabled` | Allows the demo adapter to make an outbound call | `false`; delivery is not attempted and the saved intent remains pending |
| `Postbound:RequestTimeoutSeconds` | Polly timeout applied to each provider attempt | `10` |
| `Postbound:RetryCount` | Retries after the initial provider attempt | `3` |
| `Postbound:InitialRetryDelayMilliseconds` | Base used for exponential retry delay | `200` (produces 400/800/1600 ms) |
| `Postbound:CircuitBreakerFailures` | Failed logical sends before the circuit opens | `3` |
| `Postbound:CircuitBreakDurationSeconds` | Open-circuit duration | `30` |
| `BulkNotifications:QueueCapacity` | Maximum jobs waiting in the in-memory channel | `100` |
| `BulkNotifications:MaxTrackedJobs` | Maximum in-memory job records before admission is rejected | `1000` |
| `BulkNotifications:MaxBatchSize` | Maximum notification items in one request | `100` |
| `BulkNotifications:CompletedJobRetentionMinutes` | In-memory retention window for completed status | `60` |

For example, override the database for the current PowerShell session:

```powershell
$env:ConnectionStrings__NotificationDb = 'Server=(localdb)\MSSQLLocalDB;Database=NotificationServiceDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true'
```

The checked-in Postbound-named adapter is a training seam, not a verified production integration. Keep `Postbound:Enabled` false. The only supported resilience demonstration uses the deterministic fake HTTP handler in the automated tests and requires no credential or network call. Each provider attempt has a Polly timeout; timeout failures can be retried, while caller cancellation is not a transient policy failure. The client logs retry/circuit transitions without credentials or message bodies and emits one `Idempotency-Key` that remains stable across Polly retries of that send. This header is only a local safety seam; no real provider is known to honor it, and a new application-level send receives a new key. Before connecting any provider, replace or verify the base URL, relative path, authentication, request/response schema, provider-appropriate timeout, total retry budget, log/metric integration, and idempotency/deduplication contract. Never commit its credentials. Retrying an email `POST` without provider-enforced deduplication can deliver duplicates after an ambiguous failure.

## Build and run

From the repository root:

```powershell
dotnet restore
dotnet build --no-restore
dotnet run --launch-profile https
```

On startup, the application connects to SQL Server and applies pending migrations automatically. Startup fails if the configured database cannot be reached or migrated.

If the local HTTPS certificate is not trusted, trust it once and restart the application:

```powershell
dotnet dev-certs https --trust
```

With the HTTPS profile, open:

- Swagger UI: `https://localhost:7286/swagger`
- OpenAPI v1: `https://localhost:7286/swagger/v1/swagger.json`
- OpenAPI v2: `https://localhost:7286/swagger/v2/swagger.json`

The same profile also listens at `http://localhost:5280` and redirects HTTP requests to HTTPS. The `http` launch profile is available when HTTPS is not required.

## Run automated checks

```powershell
dotnet build
dotnet test tests/NSA.Tests/NSA.Tests.csproj
```

The test project is the repeatable verification entry point for the Week 1–2 API contracts. With the API running locally at port 5099, the live smoke and latency checks are:

```powershell
.\scripts\Invoke-Week12Smoke.ps1 -BaseUrl http://127.0.0.1:5099
.\scripts\Measure-BulkNotificationLatency.ps1 -BaseUrl http://127.0.0.1:5099 -Samples 25
```

Captured local results are indexed in [evidence/README.md](evidence/README.md). Manual review and external evidence are tracked separately in the [Week 1–2 verification checklist](docs/verification/week-01-02-checklist.md).

## API conventions

The preferred routes contain the API version:

```text
/api/v1/products
/api/v2/products
```

Version 1 is deprecated. Its responses include `Deprecation` and `Sunset` headers. Version 2 is the current default. Temporary unversioned compatibility routes such as `/api/products` remain available and resolve to the default API version.

The endpoint groups are:

| Resource | Operations |
| --- | --- |
| Products | List, get, create, and update products |
| Cart | Get a visitor cart; add, update, and remove items |
| Orders | List/get orders, place an order, and update tracking status |
| Notifications | List/get, create, update, and delete notification records |
| Bulk notifications | Queue record creation and poll job status |

Swagger is the source of truth for request schemas and documented response codes. Non-success responses use `application/problem+json` and include a `traceId` for correlation.

## Bulk-job behavior

`POST /api/v2/notifications/bulk` writes a job to a bounded in-memory channel and returns `202 Accepted` with a job ID. Poll `GET /api/v2/notifications/bulk/{jobId}` until the status reaches `Completed`, `CompletedWithErrors`, or `Cancelled`. Invalid/oversized batches return 400; exhausted queue/job capacity returns 503. Completed job records are removed opportunistically after the configured retention window.

This Week 2 implementation intentionally has operational limits:

- Queue and job status are lost when the process restarts.
- The bounded channel is in process and is not a durable message broker.
- Every item is persisted. Email-channel items also pass through `IEmailSender`; with the demo provider disabled, the adapter records a metadata-free operational log, performs no network call, and returns `NotAttempted`. Other channel types currently have no external delivery adapter.
- Email intent is saved before the provider call and marked sent only when a provider accepts it. Disabled mode therefore leaves the intent pending while still completing the record-processing item; a provider exception leaves a pending record and a failed job item. Week 2 has no automatic replay.
- Terminal job snapshots retain counts and timestamps but release recipient, subject, and body payloads; admission and retention remain bounded per process.
- Capacity and retention are per process; they are not coordinated across application instances.

Durability, a separate worker process, retries/dead-letter handling, and persisted status are later-week architecture work rather than properties of this implementation.

## Decision and review records

- [ADR 001 — URL-segment API versioning](docs/adr/001-url-api-versioning.md)
- [ADR 002 — Problem Details errors](docs/adr/002-problem-details-errors.md)
- [ADR 003 — Background bulk notifications and resilient outbound calls](docs/adr/003-background-bulk-notifications-and-resilience.md)
- [Week 1 architecture decision](docs/week-01-architecture-decision.md)
- [Week 1–2 verification checklist and recorded local evidence](docs/verification/week-01-02-checklist.md)
- [Evidence index](evidence/README.md)

## Troubleshooting

- **Startup fails while applying migrations:** verify `ConnectionStrings__NotificationDb`, SQL Server availability, Windows/SQL authentication, and database permissions.
- **HTTPS certificate error:** run `dotnet dev-certs https --trust`, or use the `http` launch profile for a local-only session.
- **Postbound is enabled but no key is configured:** return `Postbound__Enabled` to `false`; real-provider use is blocked until the adapter contract and retry safety are verified.
- **A bulk job disappears:** process restart loss is expected for the current in-memory implementation.
