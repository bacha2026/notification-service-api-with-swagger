# Week 1-3 final technical rerun

- **Recorded:** 2026-07-20, Asia/Manila
- **Workspace:** `C:\Users\Full Scale\projects\notification\notification-api`
- **Baseline:** `main` at `c0f145c` plus the reviewed Week 3 working tree
- **Scope:** reproducible engineering checks only; human/account deliverables remain explicitly external

## Build, test, schema, and dependency gates

| Check | Result |
| --- | --- |
| `dotnet restore NSA.sln` | PASS |
| `dotnet build NSA.sln --configuration Release` | PASS - 0 warnings, 0 errors |
| `dotnet test NSA.sln --configuration Release --no-build --no-restore` | PASS - 76/76 |
| EF pending-model check | PASS - no drift |
| Solution-wide NuGet vulnerability scan | PASS - API, Worker, and tests have no reported vulnerable packages |
| Next.js clean install, full audit, lint, and production build | PASS - 0 vulnerabilities; lint and production build completed |
| Angular clean install, full audit, tests, and production build | PASS - 0 vulnerabilities; 2/2 tests and production build completed |

The OpenAPI contract test asserts that both documents are OpenAPI `3.0.x`, every operation has an XML-derived summary, every response has a description, and every generated 4xx/5xx business-API response has an RFC 7807 schema. The current documents report `3.0.1`.

## Hardened Compose gate

- SQL and RabbitMQ credentials were generated into an ignored `.env`; values were neither committed nor printed.
- Compose fails closed when required credential variables are absent.
- Published API and RabbitMQ Management ports bind to `127.0.0.1` only.
- API image `sha256:1ed8cecd579d...` and Worker image `sha256:ffe9cb388a60...` were built from the final source at 18:08 Asia/Manila.
- SQL Server, RabbitMQ, API, and Worker were recreated from the current Compose definition and reached `healthy`.
- `/health/live` reports process liveness; `/health/ready` probes SQL Server and RabbitMQ with bounded timeouts.
- Worker readiness requires both its RabbitMQ consumer and a working SQL connection and is removed on dependency loss.
- The forward privacy migration `20260720180000_RedactTrainingSeedPersonalData` applied successfully; the live API returned the reserved seed identities (`visitor@example.test` and `admin@example.test`).

Dependency failure/recovery was then proved on those images:

| Dependency stopped | Liveness | Readiness | Worker marker | Docker health | Full recovery |
| --- | ---: | ---: | --- | --- | ---: |
| RabbitMQ | 200 | 503 | removed in 8.592 s | API + worker unhealthy in 26.441 s | 36.781 s |
| SQL Server | 200 | 503 | removed in 9.968 s | API + worker unhealthy in 23.923 s | 49.464 s |

See [the exact readiness record](readiness-recovery.md). Both final states were liveness/readiness 200, API/worker healthy, and worker marker restored.

## HTTP and latency gate

The live Week 1-2 smoke helper passed all 12 probes against `http://127.0.0.1:8080`, including Swagger v1/v2, retirement headers, validation, malformed JSON, missing route/resource, method mismatch, unsupported media type, and unsupported API version.

Twenty-five measured bulk requests after five warmups produced:

| Metric | Result |
| --- | ---: |
| p50 | 13.165 ms |
| p95 | 19.071 ms |
| maximum | 21.491 ms |
| acceptance | PASS (`19.071 ms < 100 ms`) |

## RabbitMQ recovery and DLQ gate

| Scenario | Observed result |
| --- | --- |
| Worker stop before ACK | One unacknowledged command; same SQL job `bedc3d6d-6907-4355-9e29-5cfac1a4e97f` completed after force-kill/restart |
| Malformed matching-type JSON | Routed; DLQ ready `4 -> 5` |
| Unsupported schema version 99 | Routed; DLQ ready `5 -> 6` |
| Wrong AMQP message type | Routed; DLQ ready `6 -> 7` |
| Opt-in poison message | `DeadLettered` after three attempts; retry header `2`; `x-death` reason `rejected`; DLQ `7 -> 8` |
| In-flight broker restart | One unacknowledged command before restart; same SQL job completed `1/1` after RabbitMQ recovery; consumer count returned to `1`; ready/unacknowledged returned to `0/0` |
| Broker unavailable during API publish | Sanitized RFC 7807 503; job `e083777a-a008-4ea7-931b-e1a7e37722df` persisted as `PublishFailed`; consumer recovered to `1` |

In-flight broker-restart identifiers:

- Job ID: `e7716477-ae09-4419-8250-8301a450f6bc`
- Correlation ID: `0HNN68RP41LIS:00000001`

Wrong-type proof identifiers:

- Message ID: `ffb7b1a8-602f-4267-95a5-a3c63920984b`
- Correlation ID: `week3-wrongmessagetype-a9e4f370441945409b2a5c9c6024f233`

## Acceptance boundary

The technical Week 1-3 checks pass. The following are not code-generated evidence and remain pending until performed by the authorized people/accounts: Skill IQ submissions, EM-confirmed schedule and daily check-in records, timed design-session attendance, PR/reviewer/tag records, EM Code Review #1, real-provider deduplication approval, the actual EM-supplied React snippets or an approved substitute, Gist publication, and live architecture-presentation feedback.

The Next.js and Angular applications are working future-week scaffolds, not claims that Week 4 rendering modes or the Week 5 OnPush demo are complete. They remain workspace siblings outside the backend Git root; [ADR 005](../../docs/adr/005-workspace-repository-boundary.md) requires an owner decision before one-checkout CI/GHCR work.
