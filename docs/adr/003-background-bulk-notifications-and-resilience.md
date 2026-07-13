# ADR 003: Process bulk notifications asynchronously and protect outbound calls

**Status:** Accepted for the Week 2 baseline

**Date:** 2026-07-10
**Scope:** Bulk job submission and Postbound email adapter

## Context

A bulk request can exceed a reasonable HTTP request duration, while an external email provider can fail transiently or remain unavailable. The Week 2 target is a fast `202 Accepted` workflow and an observable status endpoint. Durable delivery, process isolation, and dead-letter handling are explicitly later-week outcomes. The Postbound-named adapter is a training seam; no production provider endpoint or contract has been verified.

## Decision drivers

- Return a job ID quickly instead of holding the caller open for the whole batch.
- Expose queued, processing, and terminal progress to the caller.
- Keep Week 2 runnable without a broker or external provider account.
- Isolate outbound HTTP details behind `IEmailSender`.
- Recover from short transient provider failures without continually calling an unhealthy dependency.
- Make the temporary durability and duplicate-delivery risks explicit.

## Considered options

### Bulk execution

| Option | Benefits | Tradeoffs | Outcome |
| --- | --- | --- | --- |
| Process synchronously in the request | Simple and durable only as far as the request succeeds | Slow responses, request timeouts, no independent progress | Rejected |
| Bounded in-memory `Channel` plus hosted worker | Small Week 2 footprint, fast enqueue, explicit admission limits, status is easy to expose | Lost on restart, one process, no DLQ or cross-instance coordination | **Selected temporarily** |
| Durable broker and separate worker | Restart-safe delivery, isolation, broker retry/DLQ features | More infrastructure and topology work | Deferred to Week 3 |
| Database-backed job polling | Durable without a broker | Polling/locking complexity and database load | Rejected for Week 2 |

### Outbound-call resilience

| Option | Benefits | Tradeoffs | Outcome |
| --- | --- | --- | --- |
| No policy | One attempt and simple behavior | Transient failures immediately fail the operation | Rejected |
| Retry only | Handles brief faults | Amplifies load during a sustained outage | Rejected |
| Circuit breaker only | Stops repeated calls to an unhealthy dependency | Does not recover an individual transient request | Rejected |
| Bounded timeout, retry, and circuit breaker | Covers slow, brief, and sustained failure modes | More states to observe; retries can duplicate a POST | **Selected for the disabled-by-default baseline** |

## Decision

Accept `POST /notifications/bulk` work into a bounded in-memory channel and return `202 Accepted` with a job ID/status location. A hosted `BackgroundService` processes the batch through scoped application services; callers poll the status endpoint. Configuration bounds queue capacity, tracked jobs, batch size, and completed-job retention. Invalid/oversized input returns 400; exhausted capacity returns retryable 503 Problem Details rather than growing memory without limit.

Keep email delivery behind `IEmailSender`. Configure the demo Postbound adapter as a typed `HttpClient` and keep it disabled. When disabled, the adapter returns an explicit `NotAttempted` outcome, writes a metadata-free operational log, and makes no network call. A deterministic fake HTTP handler is the supported way to demonstrate resilience behavior.

For enabled provider calls:

- Apply an innermost optimistic Polly timeout of 10 seconds to each provider attempt by default; disable the `HttpClient`-wide timeout so it does not cancel the complete retry sequence.
- Handle network errors, HTTP 408, HTTP 5xx, and Polly `TimeoutRejectedException` as transient. Caller cancellation is not classified as a retryable policy failure.
- Retry three times by default with exponential delays of 400 ms, 800 ms, and 1,600 ms.
- Place the circuit breaker outside the retry handler. By default it opens after three handled failed logical sends and rejects calls for 30 seconds before a trial request. One failed logical send can make four provider attempts, so opening can require up to twelve failed provider attempts.
- Log retry attempts and circuit open/half-open/reset transitions without credentials or message bodies.
- Bind and validate timeout/retry/breaker settings at startup so invalid limits fail configuration early.
- Add an `Idempotency-Key` once per application send so it remains stable across that send's Polly retries. A new application send gets a new key, and the header has no safety value until a selected provider guarantees it honors the contract.
- Do not enable retrying email POSTs in production until a stable idempotency key or an equivalent provider deduplication contract is implemented and tested.

Before any real-provider enablement, replace or verify the base URL, relative path, authentication, payload, provider-appropriate per-attempt timeout and total retry budget, telemetry integration, and idempotency behavior. With defaults, four fully timed-out attempts plus backoff can consume about 42.8 seconds before overhead; there is no separate total-send timeout other than caller cancellation.

The worker persists every item. Email-channel items go through `INotificationDispatcher` and `IEmailSender`; other channel types currently create records without an external delivery adapter. The dispatcher saves an unsent intent before calling the provider and records `SentAt` only after the provider returns `AcceptedByProvider`. A provider failure therefore leaves recoverable intent and a failed job item. A disabled adapter returns `NotAttempted`, so record processing succeeds but the intent remains pending. Order workflows catch provider-only failures so the committed order succeeds with pending notification intent. This is still best-effort in-process processing, not durable delivery.

## Tradeoffs and consequences

Positive consequences:

- Submission latency is mostly independent of batch processing time.
- Job progress is observable, and provider concerns stay outside application orchestration.
- Short provider failures can recover automatically; sustained failures stop consuming dependency capacity temporarily.

Costs and risks:

- Queue and status state disappear on restart and cannot be shared across instances.
- Atomic admission, time-based cleanup, and releasing message payloads at terminal completion bound local memory growth, but job tracking and cleanup remain process-local and opportunistic.
- A crash can leave callers without status, and there is no dead-letter path.
- Retrying an email POST after an ambiguous failure may deliver duplicates without provider idempotency.
- The locally stable idempotency header does not prove provider deduplication and does not deduplicate a caller repeating the application operation.
- Database persistence and provider delivery are not atomic. Admin/visitor sends can have partial outcomes, and pending intents have no automatic replay until a durable Outbox/worker design is implemented.
- The outer breaker counts failed logical sends rather than individual provider attempts, which permits up to twelve provider attempts before opening.
- A per-attempt timeout multiplied by retries can make the total logical send much longer than the configured timeout value.
- Optimistic timeout depends on the underlying HTTP operation honoring cancellation; tests must cover both Polly timeout and caller-abort paths.

These limitations are accepted only for the Week 2 learning baseline, not for production.

## Validation

Automated tests and captured evidence must prove that:

1. A valid bulk request returns `202`, a non-empty job ID, and a resolvable status URL; local p95 submission latency for at least 20 stated samples is below 100 ms.
2. Status moves from `Queued` to `Processing` to `Completed`/`CompletedWithErrors`; graceful shutdown marks in-flight work `Cancelled`; counts remain internally consistent.
3. Invalid/empty/oversized batches are rejected with Problem Details; capacity exhaustion returns 503; raw worker exception messages are not disclosed.
4. A deterministic HTTP handler proves the exact retry count/delays, outer-breaker threshold, maximum provider attempts, break duration behavior, and recovery trial.
5. Non-transient 4xx responses are not retried.
6. Disabled-provider mode performs no network request.
7. Provider failure leaves an unsent persisted intent, reports the job item failure without raw exception disclosure, and does not roll back a previously committed order.
8. The idempotency header stays identical across physical retries of one send, and base URL/path, authentication, payload/response, provider idempotency/deduplication, configured timeout, policy telemetry, and caller-cancellation behavior are documented and tested before enabled-provider use.

Record the results in the [Week 1–2 verification checklist](../verification/week-01-02-checklist.md).

## Revisit when

Week 3 replaces the channel with a broker and separate worker; Week 4 introduces idempotency and Outbox/Inbox processing. Supersede this ADR when durable job state, DLQ policy, provider-specific idempotency, queue capacity, retention, or multi-instance operation is defined.
