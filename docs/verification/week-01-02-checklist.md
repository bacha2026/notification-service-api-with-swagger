# Week 1–2 Verification Checklist and Evidence Template

Use this file as the repeatable review record for the Week 1 foundation and Week 2 hardening. Copy it into an evidence branch or review folder, fill every evidence link/result, and leave an unchecked item with an owner rather than treating an unproved claim as complete.

## Review metadata

| Field | Value |
| --- | --- |
| Engineer | Local automated verification by Codex; project owner confirmation pending |
| Reviewer / EM | **PENDING** |
| Branch and commit SHA | `main`; baseline SHA `91d94c0cd61d384d819dffae264e7a59dde2bd61`; use repository HEAD for the publication commit; PR/reviewer evidence pending |
| Review date/time/time zone | 2026-07-13 13:06–13:10 Asia/Manila (05:06–05:10 UTC) |
| OS | Windows 10.0.26200, win-x64 |
| `dotnet --version` | `8.0.422` selected by `global.json` |
| SQL Server edition/version | Enterprise Evaluation Edition (64-bit), `17.0.1000.7`; local `MSSQLSERVER` |
| Base URL | `http://127.0.0.1:5099` (local verification only) |
| Postbound mode (`disabled` / deterministic stub / real sandbox) | Disabled in the application; deterministic handlers in tests; real sandbox **not verified** |

## Evidence rules

- Remove/redact secrets, authorization headers, recipient data, and connection-string credentials.
- Record the exact command, UTC timestamp, exit code/status, duration, and artifact link.
- Use a deterministic stub/handler for provider failure tests; do not make a real provider unhealthy.
- For measurements, record hardware, build configuration, sample count, warm-up, concurrency, and calculation method.
- An external activity (assessment, GitHub PR, EM review) cannot be inferred from code; attach its link or mark it pending.

## Baseline command record

Run from the repository root and attach the complete transcript:

```powershell
dotnet --info
dotnet restore
dotnet build --no-restore
dotnet test tests/NSA.Tests/NSA.Tests.csproj
dotnet run --launch-profile https
```

| Command / check | Timestamp (UTC) | Result / exit code | Duration | Evidence link |
| --- | --- | --- | --- | --- |
| SDK/environment record | 2026-07-13 05:06 | PASS / 0 | <1 s | [build.txt](../../evidence/week-01/build.txt) |
| Restore | 2026-07-13 05:06 | PASS / 0 | 1.1 s | [build.txt](../../evidence/week-01/build.txt) |
| Build | 2026-07-13 05:06 | PASS / 0 warnings / 0 errors | 2.36 s | [build.txt](../../evidence/week-01/build.txt) |
| Automated tests | 2026-07-13 05:07 | PASS / 72 passed | 4 s | [build.txt](../../evidence/week-01/build.txt) |
| Startup + migration | 2026-07-13 05:08 | PASS / DB current | ~2 s | [build.txt](../../evidence/week-01/build.txt) |
| Swagger UI load | 2026-07-13 05:09 | PASS / 200 | <1 s | [screenshot](../../evidence/week-01/swagger.png) |
| v1 OpenAPI JSON load/validation | 2026-07-13 05:10 | PASS / 200 | <1 s | [live result](../../evidence/week-02/live-contract-smoke.json) |
| v2 OpenAPI JSON load/validation | 2026-07-13 05:10 | PASS / 200 | <1 s | [live result](../../evidence/week-02/live-contract-smoke.json) |

## Week 1 acceptance checklist

### External baseline and repository proof

- [ ] C#, ASP.NET Core, and React Skill IQ scores are timestamped before course work and linked.
- [ ] The reviewed branch/PR and commit SHA are recorded; unrelated dirty-worktree changes are identified.
- [x] `dotnet build` succeeds without errors; warnings are reviewed and dispositioned.
- [x] Application startup and automatic EF Core migration are repeatable from the README.

### API and OpenAPI foundation

- [x] Notification create, read, update, and delete smoke scenarios pass.
- [x] Product, cart, and order supporting scenarios required by the demo pass.
- [x] Every controller action has an XML summary and each expected success/failure outcome is represented in OpenAPI.
- [x] `/swagger`, `/swagger/v1/swagger.json`, and `/swagger/v2/swagger.json` load.
- [x] OpenAPI documents parse successfully and show the intended paths, request/response schemas, versions, and response codes.
- [x] The README lets a reviewer configure SQL Server, build, run, open Swagger, and run tests without oral instructions.

### Architecture proof

- [x] [`drawio/notification.drawio`](../../drawio/notification.drawio) matches the implemented request/dependency flow and has current readable exports.
- [x] Readable runtime export reviewed: [PDF](../architecture/week-02-runtime-architecture.pdf) / [PNG](../architecture/week-02-runtime-architecture.png) / [SVG](../architecture/week-02-runtime-architecture.svg).
- [ ] The 30-minute order-tracking design exercise date, audience, and notes are recorded: ____________________.
- [x] The [Week 1 architecture decision](../week-01-architecture-decision.md) covers context, constraints, alternatives, tradeoffs, validation, and evolution triggers.

## Week 2 acceptance checklist

### Error-contract matrix

Capture the response status, `Content-Type`, required fields, and test/evidence reference. All Problem Details responses must have a non-empty `traceId`; unexpected failures must not leak internal exception details.

| Scenario | Expected status | Expected contract | Actual result | Evidence/test |
| --- | ---: | --- | --- | --- |
| Model validation / invalid field | 400 | Problem Details/validation details | PASS; 400 + validation trace | [matrix/live probe](../../evidence/week-02/error-matrix.md) |
| Known domain/application validation | 400 | Problem Details | PASS; typed safe exception only | `ApiExceptionContractTests` |
| Malformed JSON | 400 | Problem Details | PASS; 400 + trace | [live probe](../../evidence/week-02/live-contract-smoke.json) |
| Unsupported request media type | 415 | Problem Details | PASS; 415 + trace | [live probe](../../evidence/week-02/live-contract-smoke.json) |
| Missing entity | 404 | Problem Details | PASS; 404 + trace | Live probe / `ApiContractTests` |
| Unknown route | 404 | Problem Details | PASS; 404 + trace | [live probe](../../evidence/week-02/live-contract-smoke.json) |
| Unsupported API version | 4xx | Problem Details | PASS; 404 + trace | [live probe](../../evidence/week-02/live-contract-smoke.json) |
| Unsupported HTTP method | 405 | Problem Details | PASS; 405 + trace | [live probe](../../evidence/week-02/live-contract-smoke.json) |
| Capacity/downstream unavailable | 503 | Sanitized Problem Details + `Retry-After` | PASS; 503 + `Retry-After: 30` | `ApiExceptionContractTests` / bulk capacity tests |
| Unexpected server exception | 500 | Sanitized Problem Details + server log | PASS; details absent | `ApiExceptionContractTests` |

- [x] All rows use `application/problem+json` and the configured JSON naming policy.
- [x] Response `status`, `title`, `instance`, and `traceId` are asserted.
- [x] The 500 tests prove sensitive exception details are absent; server-side structured logging is configured.

### Versioning matrix

| Check | Expected | Actual result | Evidence/test |
| --- | --- | --- | --- |
| Representative `/api/v1/...` call | Success | PASS; 200 | [live result](../../evidence/week-02/live-contract-smoke.json) |
| Representative `/api/v1.0/...` call | Success with the same v1 policy | PASS; same retirement policy also on 404 | Live result / version tests |
| Representative `/api/v2/...` call | Success | PASS; 200 | Live result |
| Unversioned compatibility call | Resolves to documented default | PASS; 200 | Live result |
| v1 deprecation metadata | `Deprecation: true` and approved `Sunset` | PASS; sunset 2026-12-31 23:59:59 GMT | Live result |
| v2 deprecation metadata | Absent | PASS | Live result |
| Reported version headers | Supported/deprecated values are correct | PASS | `ApiContractTests` |
| Create/accepted `Location` | Resolves within requested version | PASS for create and bulk | `ApiContractTests` |
| v1 Swagger path set | v1 operations only | PASS | `ApiContractTests` |
| v2 Swagger path set | v2 operations only | PASS, plus documented unversioned compatibility paths | `ApiContractTests` / ADR 001 |

### Resilient outbound-call proof

Use a deterministic fake HTTP handler/server and record the actual policy composition. Never include the API key or real message body in evidence.

| Scenario | Expected | Actual result | Evidence/test |
| --- | --- | --- | --- |
| Provider disabled | Zero network calls; local log path | PASS; `NotAttempted`, pending intent, PII-free log | `PostboundEmailSenderTests` / `NotificationDispatcherTests` |
| Transient failure then success | Bounded retries, one logical success | PASS | `PostboundResilienceTests` |
| Repeated HTTP 5xx/408/network fault | Exact retry count/backoff is observable | PASS; 400/800/1600 ms configured/tested | `PostboundResilienceTests` |
| Non-transient HTTP 4xx | No retry | PASS | `PostboundResilienceTests` |
| Repeated logical failures | Circuit opens at configured threshold | PASS; three failed logical sends | `PostboundResilienceTests` |
| Call while circuit is open | Fast rejection; no network call | PASS | `PostboundResilienceTests` |
| Break period/recovery trial | Trial behavior and close/reopen are correct | PASS | `PostboundResilienceTests` |
| Per-attempt timeout versus caller cancellation | Each attempt is bounded/retryable; caller cancellation stops without retry; total duration is recorded | PASS | `PostboundResilienceTests` |
| Idempotency header across retries | One stable key per logical send; provider guarantee documented separately | Local behavior PASS; real provider guarantee **PENDING** | [Polly record](../../evidence/week-02/polly-test.txt) |

- [x] Retry delays and total attempts match ADR 003.
- [x] Circuit threshold, break duration, policy order, and state transitions are asserted or logged.
- [x] Outbound calls have a bounded timeout.
- [ ] The same idempotency key is observed across Polly retries of one send; provider idempotency/deduplication behavior is documented separately: ____________________.
- [x] No production enablement is approved while retry safety remains unknown; `Postbound:Enabled` remains `false`.

### Bulk-job contract and latency

- [x] Empty/invalid batches return Problem Details and do not enqueue work.
- [x] Oversized batches return 400; queue/tracked-job capacity rejection returns retryable 503.
- [x] A valid request returns `202 Accepted`, a non-empty job ID, and a resolvable status location.
- [x] Status is observed as `Queued` → `Processing` → `Completed` or `CompletedWithErrors`; graceful shutdown produces `Cancelled` for in-flight work.
- [x] `ProcessedCount = SucceededCount + FailedCount` and never exceeds `TotalCount`.
- [x] A missing job returns 404 Problem Details.
- [x] Evidence describes the implemented contract accurately: every item is persisted; email items use `IEmailSender`; other channels have no external adapter; disabled-provider mode makes no network call and leaves email intent pending.
- [x] Provider failure leaves unsent notification intent and a failed job item; committed orders remain successful; partial multi-recipient outcomes and absent automatic replay are recorded as Week 2 limits.
- [x] Restart loss, process-local state, absent DLQ, atomic bounded admission, terminal payload release, and completed-job retention behavior are tested or explicitly accepted for Week 2.

Latency evidence:

| Field | Recorded value |
| --- | --- |
| Endpoint and payload size | `POST http://127.0.0.1:5099/api/v2/notifications/bulk`; one notification |
| Build configuration / commit | Debug, hardened working tree based on `91d94c0...` |
| Machine / SQL Server location | Windows win-x64; SQL Server local default instance |
| Warm-up count | 5 |
| Measured sample count (minimum 20) | 25 |
| Concurrency | 1, sequential |
| p50 / p95 / maximum | 1.047 / 2.004 / 4.38 ms |
| p95 acceptance (`< 100 ms`) | **Pass** |
| Raw results link | [bulk-latency.json](../../evidence/week-02/bulk-latency.json) |

## Decision-record quality gate

For each ADR, verify status/date, context, drivers, considered options, selected decision, tradeoffs, positive/negative consequences, validation, and revisit trigger.

| Record | Engineer check | Reviewer check | Findings |
| --- | --- | --- | --- |
| [ADR 001](../adr/001-url-api-versioning.md) | ☒ | ☐ | Local content gate passed; reviewer pending |
| [ADR 002](../adr/002-problem-details-errors.md) | ☒ | ☐ | Local content gate passed; reviewer pending |
| [ADR 003](../adr/003-background-bulk-notifications-and-resilience.md) | ☒ | ☐ | Local content gate passed; real provider contract pending |
| [Week 1 architecture decision](../week-01-architecture-decision.md) | ☒ | ☐ | Local content gate passed; reviewer pending |

## EM Code Review #1

| Field | Value |
| --- | --- |
| Review date and recording/PR link | **PENDING** |
| Demo script link | [prepared review record](../../evidence/week-02/em-review-01.md) |
| Blocking findings and disposition | **PENDING REVIEW** |
| Non-blocking debt, owner, due week | Real-provider contract remains disabled/pending; Week 3/4 durability is intentionally deferred |
| Approved deviations | **PENDING REVIEW** |
| Reviewer decision | **PENDING** — Pass / Carry / Replan |
| Reviewer name/sign-off | **PENDING** |

## Final evidence index

| Evidence family | Artifact/link | Verified by/date |
| --- | --- | --- |
| Skill IQ baseline | [pending record](../../evidence/week-01/skill-iq-record.md) | Engineer/EM pending |
| Build/test/start transcript | [build.txt](../../evidence/week-01/build.txt) | Codex local verification / 2026-07-13 |
| CRUD/OpenAPI smoke proof | [record](../../evidence/week-01/crud-smoke.md) / [screenshot](../../evidence/week-01/swagger.png) | Codex local verification / 2026-07-13 |
| Architecture source/export/design notes | [source](../../drawio/notification.drawio) / [PDF](../architecture/week-02-runtime-architecture.pdf) / [notes](../../evidence/week-01/demo-notes.md) | Export reviewed; human exercise pending |
| Error matrix | [matrix](../../evidence/week-02/error-matrix.md) | Codex local verification / 2026-07-13 |
| Versioning checks | [requests](../../evidence/week-02/versioning.http) / [result](../../evidence/week-02/live-contract-smoke.json) | Codex local verification / 2026-07-13 |
| Polly retry/circuit/timeout transcript | [record](../../evidence/week-02/polly-test.txt) | Automated tests / 2026-07-13 |
| Bulk lifecycle and raw latency results | [lifecycle](../../evidence/week-02/job-lifecycle.md) / [raw data](../../evidence/week-02/bulk-latency.json) | Codex local verification / 2026-07-13 |
| PR and EM review record | [pending record](../../evidence/week-02/em-review-01.md) | Reviewer pending |

**Week 1 result:** **Carry** — local implementation checks pass; Skill IQ, PR, 30-minute design session, and reviewer evidence remain pending.

**Week 2 result:** **Carry** — local implementation checks pass; real-provider contract and EM Review #1 remain pending.

**Open remediation owner and due date:** Project owner + EM; schedule before declaring the weekly gates complete.
