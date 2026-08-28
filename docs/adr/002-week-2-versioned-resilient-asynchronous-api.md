# ADR 002 — Week 2: Versioned, Resilient, Asynchronous API

**Status:** Accepted

**Decision period:** Week 2

**Last validated:** 2026-08-24

## Context

Clients need predictable errors, a safe migration path between API versions,
and fast bulk-notification admission. Creating every notification inside the
HTTP request would tie latency and availability to batch size and downstream
systems. A successful response must not falsely claim that delivery finished.

## Decision

Adopt four related API rules:

1. Return RFC 7807 Problem Details for handled error paths so clients receive a
   stable machine-readable error shape.
2. Use URL versions (`/api/v1` and `/api/v2`). Keep v1 functional but mark it
   deprecated with `Deprecation` and `Sunset` headers.
3. Make bulk creation asynchronous. `POST /api/v{version}/notifications/bulk`
   validates and durably admits 1–100 items, then returns `202 Accepted`, a
   `jobId`, `Location`, correlation ID, and initial status. Clients poll
   `GET /api/v{version}/notifications/bulk/{jobId}`.
4. Apply Polly retry and circuit breaking only at outbound I/O boundaries.
   Retries must be bounded and must not wrap non-idempotent work blindly.

The Week 2 teaching implementation used an in-process `BackgroundService`.
Weeks 3–4 replaced its memory queue with SQL-backed admission, an Outbox Relay,
RabbitMQ, and separate workers while preserving the same HTTP contract.

The `<100 ms` acknowledgement objective is a warmed deployment SLO measured
against representative SQL Server infrastructure. Deterministic tests verify
the architectural requirement—that admission does not invoke RabbitMQ—rather
than using a flaky universal wall-clock unit assertion.

## Alternatives considered

- **Synchronous batch processing:** simple state, but unbounded response time
  and downstream failures become HTTP failures.
- **Permanent in-memory queue:** fast, but accepted work can disappear on
  process restart and cannot scale independently.
- **Durable admission with eventual processing:** selected because the client
  gets a stable identifier and accepted work remains observable.

## Consequences

- `202 Accepted` means “stored for processing,” not “delivered.”
- Clients must poll and handle eventual completion states.
- SQL Server is on the final admission path; RabbitMQ is not.
- Version retirement and Problem Details schemas become governed contracts.
- Capacity, retention, idempotency, and sanitized diagnostics are required.

## Verification and Week 2 evidence

- API contract tests assert v1/v2 routes, deprecation headers, Problem Details,
  `202`, `Location`, correlation, and polling behavior.
- The no-broker admission test installs a publisher that fails if called and
  still receives `202`.
- Live provisioning records the warmed admission latency for the real SQL path.
