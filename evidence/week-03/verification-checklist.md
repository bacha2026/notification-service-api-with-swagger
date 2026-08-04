# Week 3 verification checklist

- **Recorded:** 2026-07-20, Asia/Manila
- **Repository baseline:** `main` at `c0f145c` plus uncommitted Week 3 implementation and evidence
- **Overall status:** Final builds, all 76 tests, hardened four-service Compose startup, and all planned RabbitMQ/SQL reliability paths passed; external-delivery gates remain pending

This checklist separates source inspection from captured runtime proof. Wrong-AMQP-type delivery and an in-flight main-queue command across broker restart are now captured in [the final rerun](final-rerun.md). It does not claim Gist publication, EM-provided snippets/review, or live presentation delivery/feedback.

## Status meanings

| Status | Meaning |
| --- | --- |
| **PASS (static)** | Source directly contains the stated structure or ordering. |
| **PASS (runtime)** | A linked Week 3 artifact records the live outcome. |
| **PASS (static + runtime)** | Source ordering and a linked live result jointly support the claim. |
| **PENDING** | No current artifact proves the scenario. |
| **PENDING EXTERNAL** | Completion requires an authorized person, account, reviewer, or presentation. |

## Implementation inventory

| Requirement | Status | Evidence / boundary |
| --- | --- | --- |
| API and worker are separate executable projects | **PASS (static + runtime)** | `NSA.csproj`; `src/NSA.Worker/NSA.Worker.csproj`; [both build successfully](build-test.txt) |
| Jobs/items and API status use SQL-backed entities | **PASS (static + runtime)** | Migration and service source; [happy path](happy-path.md) and [paused-worker status](restart-redelivery.md) |
| Versioned broker command excludes notification content | **PASS (static)** | `BulkNotificationRequestedV1`; live body inspection not captured |
| Persistent mandatory publication uses confirmation tracking before 202 | **PASS (static + runtime)** | Publisher/service ordering plus [202 happy path](happy-path.md) |
| Stable direct exchange, quorum queue, DLX, and DLQ names exist | **PASS (static + runtime)** | [Topology](topology.md) and [RabbitMQ UI](dlq.md) |
| Worker consumes with manual ACK and prefetch 1 | **PASS (static + runtime)** | Worker source; [unacknowledged delivery before kill](restart-redelivery.md) |
| Successful completion is persisted before ACK | **PASS (static + runtime)** | Processor/worker ordering; completion and unacknowledged count `0` after redelivery |
| Job status detects competing API/worker updates | **PASS (static)** | `BulkNotificationJobs.Status` is configured as an EF Core concurrency token |
| Ambiguously accepted command can recover `PublishFailed` | **PASS (static + runtime)** | Processor source and final regression suite cover `PublishFailed -> Retrying -> DeadLettered` under repeated failure; [runtime evidence](publish-failure.md) records 503, SQL state, and consumer recovery but does not claim that the intentionally unavailable publish produced a command |
| Completed duplicates ACK; already-`DeadLettered` redeliveries reject to DLQ | **PASS (static)** | Processor disposition and worker ACK/reject handling |
| Unhandled delivery/bookkeeping failures cannot silently wedge prefetch 1 | **PASS (static)** | Worker NACKs with requeue or closes the channel so an unacknowledged delivery returns |
| Three attempts use `x-retry-count` then reject to DLQ | **PASS (runtime)** | [Poison attempt record](poison-attempts.md) |
| Malformed matching-type JSON rejects to DLQ | **PASS (runtime)** | [Malformed-message record](malformed-message.md) |
| Unsupported schema rejects to DLQ | **PASS (static + runtime)** | Worker validation path plus [schema-version-99 record](unsupported-message.md) |
| Wrong AMQP type rejects to DLQ | **PASS (static + runtime)** | Worker validation path plus [wrong-type rerun](final-rerun.md) |
| Failure injection is opt-in and bounded | **PASS (static + runtime)** | Empty default, [proof helper](../../scripts/Invoke-Week3PoisonProof.ps1), and [controlled poison record](poison-attempts.md) |
| API/worker Dockerfiles and four-service Compose definition exist | **PASS (static + runtime)** | Dockerfiles, `compose.week3.yml`, and [successful image/start record](compose-start.txt) |
| Named volumes/network and readiness ordering exist | **PASS (static + runtime)** | Compose gates on SQL/Rabbit health; API readiness probes both dependencies; Worker readiness requires an active consumer and SQL connectivity; [final rerun](final-rerun.md) |
| HTTPS redirect remains the default and is disabled only for HTTP Compose | **PASS (static + runtime)** | Application default plus Compose override; [healthy HTTP stack](compose-start.txt) |
| Outbox, Inbox, idempotency, and automatic replay are not claimed | **PASS (static)** | ADR, README, and [topology](topology.md); Week 4 work |

## Command transcripts

| Command | Status | Evidence |
| --- | --- | --- |
| `dotnet restore NSA.sln` | **PASS (runtime)** | [Exit 0](build-test.txt) |
| `dotnet build NSA.sln --configuration Release` | **PASS (runtime)** | [Exit 0; API, Worker, and tests; 0 warnings/errors](build-test.txt) |
| `dotnet test NSA.sln --configuration Release --no-build --no-restore` | **PASS (runtime)** | [76 passed, 0 failed/skipped](build-test.txt) |
| `docker compose -f compose.week3.yml config --quiet` | **PASS (runtime)** | [Exit 0](compose-start.txt) |
| `docker compose -f compose.week3.yml build` | **PASS (runtime)** | [API and worker images built](compose-start.txt) |
| `docker compose -f compose.week3.yml up -d` plus health inspection | **PASS (runtime)** | [All four services healthy](compose-start.txt) |

The final `76 / 76` test result is captured. Those tests use EF Core InMemory and test publishers, so they complement rather than replace the live SQL/RabbitMQ evidence.

## Runtime acceptance scenarios

| Scenario | Status | Evidence / exact boundary |
| --- | --- | --- |
| Full four-service Compose image build and healthy-state proof | **PASS (runtime)** | [Final API/worker images built; SQL Server, RabbitMQ, API, and worker healthy](compose-start.txt) |
| HTTP 202 through worker completion | **PASS (runtime)** | [Latest happy-path record](happy-path.md), `Completed` with `1 / 1` processed/succeeded |
| SQL-backed status remains queryable while worker is paused | **PASS (runtime)** | [Queued status during one unacknowledged delivery](restart-redelivery.md) |
| Worker force-kill before ACK causes redelivery | **PASS (runtime)** | [Unacknowledged `1` before kill; completed and `0` after restart](restart-redelivery.md) |
| RabbitMQ restart recovers consumer and retains existing DLQ entry | **PASS (runtime)** | [Broker restart record](broker-restart.md) |
| In-flight main-queue command survives RabbitMQ restart | **PASS (runtime)** | [One unacknowledged command before restart; same job completed after recovery](final-rerun.md) |
| RabbitMQ loss preserves liveness, fails readiness/worker health, then recovers | **PASS (runtime)** | [200 liveness, 503 readiness, marker removal, unhealthy containers, full recovery](readiness-recovery.md) |
| SQL Server loss preserves liveness, fails readiness/worker health, then recovers | **PASS (runtime)** | [200 liveness, 503 readiness, marker removal, unhealthy containers, full recovery](readiness-recovery.md) |
| Initial API publish failure returns 503 and records `PublishFailed` | **PASS (static + runtime)** | [Unavailable-broker record](publish-failure.md): 503, sanitized problem response, persisted SQL state, and recovered consumer |
| Malformed matching-type JSON reaches DLQ | **PASS (runtime)** | [DLQ count `2 -> 3`](malformed-message.md) |
| Unsupported schema reaches DLQ | **PASS (runtime)** | [Schema version 99 increased DLQ ready count `3 -> 4`](unsupported-message.md) |
| Wrong AMQP type reaches DLQ | **PASS (runtime)** | [Wrong type increased DLQ ready count `2 -> 3`](final-rerun.md) |
| Opt-in poison reaches `DeadLettered` after three attempts | **PASS (runtime)** | [Final retry header `2`, x-death `rejected`](poison-attempts.md), run with the [repeatable helper](../../scripts/Invoke-Week3PoisonProof.ps1) |
| Job/message/correlation identifiers connect recorded paths | **PASS (runtime)** | [Correlation matrix](correlation.md) |
| Named durable quorum DLQ is visible with queued evidence | **PASS (runtime)** | [12:53 Asia/Manila record with `Ready=5`, `Total=5`](dlq.md) and [PNG](dlq.png) |
| Operational logs exclude exercised message content and credentials | **PASS (static + runtime)** | [Zero-match API/worker log review](log-review.md) plus logging-call source inspection |

## Architecture and frontend deliverables

| Deliverable | Status | Evidence / limitation |
| --- | --- | --- |
| Queue-topology ADR | **PASS (static)** | [ADR 004](../../docs/adr/004-rabbitmq-worker-and-dlq.md) |
| Prepared 15-minute presentation | **PASS (static)** | [Script](../../docs/presentations/week-03-rabbitmq-architecture.md), [printable HTML](../../docs/presentations/week-03-rabbitmq-architecture.html), and [9-page PDF](presentation.pdf) |
| Live presentation and reviewer decision | **PENDING EXTERNAL** | [Feedback record](feedback.md) has no delivered session or outcome |
| Local ten-example React hooks substitute | **PASS (static)** | [Prepared local training set](react-hooks-audit.md) |
| EM-provided React snippets and review | **PENDING EXTERNAL** | The requested EM snippets were not supplied, so the local substitute cannot prove work against them |
| Published Gist and submission | **PENDING EXTERNAL** | [Publication record](react-gist-link.md) has no URL or review |

## Captured runtime evidence

- [API/worker build and 76-test transcript](build-test.txt)
- [Final hardened-stack and missing-case rerun](final-rerun.md)
- [Compose build and healthy-start transcript](compose-start.txt)
- [Happy path](happy-path.md)
- [Worker restart/redelivery](restart-redelivery.md)
- [RabbitMQ restart/recovery](broker-restart.md)
- [Unavailable-broker publish failure](publish-failure.md)
- [Dependency-readiness failure and recovery](readiness-recovery.md)
- [Malformed JSON](malformed-message.md)
- [Unsupported schema](unsupported-message.md)
- [Bounded poison attempts](poison-attempts.md)
- [Correlation matrix](correlation.md)
- [Operational-log content review](log-review.md)
- [DLQ screenshot record](dlq.md) and [PNG](dlq.png)

## Exit assessment

Core Week 3 paths now have evidence: final API/worker image builds, 76 passing tests, hardened healthy four-service Compose startup, confirmed-acceptance happy path, SQL-backed status, true unacknowledged worker redelivery, in-flight broker recovery, publish-failure handling, malformed/unsupported-schema/wrong-type routing, bounded poison attempts, identifier correlation, a zero-match operational-log review, and RabbitMQ UI inspection.

Gist publication, work against EM-provided snippets, live presentation delivery, and reviewer feedback remain external pending. The printable HTML and 9-page PDF deck are prepared, not delivered.
