# ADR 004 — Week 4: Idempotency, Outbox/Inbox, Next.js, and CI

**Status:** Accepted locally; hosted pipeline evidence requires commit/push

**Decision period:** Week 4

**Last validated:** 2026-08-24

## Context

A client can retry after the server committed but before the response arrived.
Separately writing SQL state and a RabbitMQ message can leave an accepted job
without a command. RabbitMQ can redeliver after relay or consumer crashes. The
system therefore needs durable request identity, atomic command admission, and
duplicate-safe consumption.

## Decision: scoped HTTP idempotency

Support optional `X-Idempotency-Key` on duplicate-sensitive resource creation,
including v2 bulk admission and authenticated checkout/order creation.

For a supplied key, the API validates a maximum 128-character printable value,
builds operation and actor scopes, canonicalizes the typed request, hashes the
operation/payload and raw key separately, and stores only the key hash. Business
state and the completed response snapshot commit in one SQL transaction. A
same-scope/same-payload retry replays the original status, body, `Location`, job,
and correlation headers with `Idempotency-Replayed: true`. Reusing the key with
a different payload returns RFC 7807 `409 Conflict`.

A unique database constraint is the concurrency arbiter. Records have bounded
retention but survive resource deletion during that window. The public bulk
route uses operation-scoped high-entropy keys; production multi-tenancy still
requires authenticated service identity.

## Decision: transactional Outbox and Inbox

One EF Core transaction stores the job, items, versioned Outbox command, and
idempotency response. Authenticated checkout also stores the order and cart
removal in that transaction. The API returns after SQL commits and never
publishes directly to RabbitMQ.

The relay leases due rows, commits the lease, publishes the persisted envelope
with its stable message ID, requires publisher confirmation, then marks it
published. Failures store sanitized diagnostics and exponential backoff with
jitter; expired leases are reclaimable.

The primary consumer uses `(messageId, consumerName)` as a durable Inbox key.
Inbox completion and notification effects commit together before ACK. Thus the
system guarantees atomic SQL admission, at-least-once publication/delivery, and
effectively-once database effects for a stable message ID—not exactly-once
external delivery.

| Failure | Durable result |
| --- | --- |
| SQL transaction fails | Resource, Outbox, and idempotency changes roll back. |
| RabbitMQ is unavailable | API can accept; the Outbox remains pending. |
| Relay lease owner crashes | Another relay reclaims the expired lease. |
| Relay crashes after confirm | It may republish; Inbox suppresses the effect. |
| Consumer fails before commit | Inbox/effects roll back; delivery remains retryable. |
| Consumer commits but ACK is lost | Redelivery observes Inbox/terminal state. |
| Poison command exhausts retries | The command remains in the terminal DLQ. |

## Companion Week 4 decisions

- The Next.js lab exposes distinct SSG, SSR, ISR, and CSR pages with visible
  timestamps so rendering behavior can be demonstrated rather than asserted.
- Pull requests run build/test gates only. Trusted main pushes may publish six
  immutable container artifacts and the release manifest; promotion and
  rollback are manual protected workflows.

## Verification and Week 4 evidence

- Postman/Newman proves first admission, exact replay, payload conflict, and
  terminal polling.
- The broker-outage verifier stops RabbitMQ, proves API readiness and one
  durable unpublished Outbox row with zero effects, restores RabbitMQ, and
  proves one published row, Inbox receipt, and effect.
- Next lint/type-check/build, unit/component, and browser rendering tests pass.
- Local workflow lint and controller tests pass. A hosted Actions/GHCR run is an
  external gate until these repository-local changes are committed and pushed.
