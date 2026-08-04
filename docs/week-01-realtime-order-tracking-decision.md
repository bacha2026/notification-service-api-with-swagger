# Week 1 decision: event-driven real-time order tracking

**Status:** Proposed training design; implementation evolves incrementally through Weeks 3–6  
**Decision date:** 2026-07-20  
**Editable diagram:** `drawio/notification.drawio`, page **Real-Time Tracking Architecture**

## Context and constraints

Customers need a current order snapshot and timely status changes, while couriers and operators need authorized commands. Delivery must tolerate reconnects, duplicate messages, broker outages, and rolling deployments. SQL Server remains the source of truth. The design must start as the current .NET API and grow without replacing its public contracts during the six-week exercise.

Key constraints are at-least-once messaging, personally identifiable order data, multiple API/worker instances, intermittent clients, a shared database during blue/green cutover, and a later requirement for repeatable Docker/Vault provisioning.

## Decision

Keep commands and snapshot queries behind an authenticated Order Tracking API. Persist each accepted state transition, an ordered status-history entry, and an Outbox message in one SQL transaction. An Outbox relay publishes versioned events to RabbitMQ with publisher confirms. Consumers maintain an Inbox/processed-message record, apply events in per-order sequence, update a disposable read model, and invoke notification adapters idempotently.

A stateless SignalR/WebSocket gateway authorizes subscriptions per order. On initial connection or reconnect, the client supplies its last observed sequence, fetches the latest authorized snapshot, and then resumes ordered live events. Stable `eventId`, `messageId`, `orderId`, `correlationId`, `schemaVersion`, and sequence fields connect HTTP, SQL, broker, worker, and client telemetry.

Week 1 implements the documented HTTP foundation. Week 3 supplies the durable broker, separate worker, manual acknowledgement, DLQ, and SQL-backed job state. Week 4 adds idempotency, Outbox, and Inbox. Week 5 moves local secrets behind Vault. Week 6 uses stateless processes, readiness gates, and expand/contract migrations for blue/green deployment.

## Alternatives and tradeoffs

- **Client polling only:** operationally simple and remains a fallback, but creates repeated reads and slower updates. Rejected as the primary real-time path.
- **Push directly from the request-handling process:** low latency, but a crash between commit and push loses an accepted event and scale-out coordination is weak. Rejected.
- **Change-data capture as the event source:** avoids application Outbox code, but introduces platform-specific operations and makes the contract implicit. Deferred.
- **Full event sourcing:** excellent history and replay, but adds projection, migration, and operational complexity beyond the training scope. Rejected; an explicit status history plus Outbox provides the required auditability.
- **Redis as source of truth:** fast reads, but weakens transactional order correctness. Rejected; Redis/read models are disposable accelerators.

The selected design adds broker, relay, deduplication, and sequence-management complexity. In return, accepted state changes survive broker downtime, duplicate delivery is safe, real-time gateways scale horizontally, and future deployment colors can share compatible storage.

## Validation and evolution triggers

Validate authorization on every subscription; monotonic order versions; reconnect from a stale sequence; broker-down Outbox recovery; duplicate Inbox delivery; poison routing; consumer restart; and blue/green compatibility. Operational targets and retention must be set from measured traffic rather than guessed.

Revisit transport choice when concurrent subscriptions or latency exceed SignalR capacity; introduce partitioned streaming when strict per-order ordering cannot be sustained in RabbitMQ; and split services only when scale, ownership, or release cadence demonstrates a real boundary.
