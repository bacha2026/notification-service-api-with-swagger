# ADR 003 — Week 3: RabbitMQ Workers and Dead-Letter Recovery

**Status:** Accepted

**Decision period:** Week 3

**Last validated:** 2026-08-24

## Context

An in-process worker loses queued work when the API restarts, competes for Web
capacity, and has no durable poison-message isolation. The domain is still one
bounded context with one data owner, so a business-microservice split would add
distributed coordination without a justified ownership boundary.

## Decision

Keep a modular monolith in the business sense while deploying thin process
hosts independently:

- The **API** owns HTTP admission and status queries.
- The **Outbox Relay** owns SQL-to-RabbitMQ publication.
- The **primary Worker** owns main-queue processing and bounded retry.
- The **DLQ Recovery Worker** owns safe replay and parking policy.
- The **migrator** owns schema changes outside ordinary application startup.
- SQL Server remains the source of truth; RabbitMQ transports versioned command
  references.

Use durable RabbitMQ topology, persistent messages, manual acknowledgement,
publisher confirms, and prefetch one for deliberate processing order. The
primary Worker persists a retry/dead-letter state before settlement. A final
failure is rejected with `requeue: false`, allowing the main queue's DLX policy
to retain it in the DLQ.

Recovery is conservative. Only a message whose RabbitMQ death metadata proves
it was explicitly rejected from the configured command queue is automatically
replayed. The recovery worker persists `RecoveryPending`, clears the exhausted
main-worker retry budget, confirm-publishes to the delayed recovery queue, and
only then ACKs the DLQ delivery. Malformed, unsupported, unknown, incorrectly
dead-lettered, or replay-exhausted messages are confirm-published to a durable
parking queue.

Broker envelopes contain stable message/job/correlation/schema/timestamp data,
not recipient, subject, or body. RabbitMQ is at least once; Inbox receipts,
terminal-state checks, and item-level uniqueness suppress repeated database
effects.

## Alternatives considered

- **Keep the in-process worker:** lower operational cost, but no durable queue
  ownership or independent scaling.
- **RabbitMQ with separate hosts:** selected for durable transport, explicit
  settlement, replay, and operational visibility.
- **Multiple business microservices now:** deferred until independent ownership,
  data, compliance, scaling, or release cadence exists.

## Consequences

- Queue, DLQ, parking, relay backlog, retry, and worker health become operational
  signals.
- Publish/mark and consume/ACK cannot be globally atomic, so duplicates are an
  expected transport behavior rather than an exceptional case.
- DLQ retention preserves evidence; it does not itself repair a command.
- Shared SQL is intentional because every process belongs to the same bounded
  context.

## Verification and Week 3 evidence

- Automated tests cover validation, settlement order, bounded retries, replay
  eligibility, header reset, parking, and duplicate suppression.
- Docker Compose starts SQL Server, RabbitMQ Management, API, relay, primary
  Worker, and DLQ Recovery Worker.
- The demonstration pauses recovery, inspects a retained DLQ message, resumes
  recovery, and proves one effect per job item.
- The ten corrected React hook examples and the architecture presentation are
  weekly evidence; they do not alter this runtime decision.
