# Week 1 Architecture Decision: Real-Time Order Tracking

**Status:** Accepted for the training architecture exercise  
**Diagram:** `drawio/notification.drawio`, page **Real-Time Tracking Architecture**

## Context

Customers need a current order snapshot and timely status updates while stores,
couriers, and support staff change an order concurrently. The design must not
lose an accepted transition, show one order's events out of order, disclose an
order to an unauthorized subscriber, or require a permanently connected client.
The first release can use one region and SQL Server, but it must leave clear
boundaries for independent scaling and later multi-region work.

## Decision

Keep SQL Server as the source of truth. The Order Tracking API authenticates
commands, checks the caller's relationship to the order, and applies an
optimistic order version. In one transaction it writes the new order state, an
append-only status-history row with the next per-order sequence, and an Outbox
event. A committed API response therefore never depends on RabbitMQ being
available and never creates an event that can be lost between separate database
and broker writes.

An Outbox Relay leases unpublished rows, sends versioned events to durable
RabbitMQ infrastructure with publisher confirmation, and marks them published.
At-least-once delivery is intentional. Every envelope carries stable event,
message, order, correlation, schema-version, and per-order sequence values.
Projection consumers use an Inbox/deduplication record and reject sequence gaps
instead of silently applying an event out of order. They update a bounded
snapshot/read model used for fast reconnects and queries.

A stateless real-time gateway owns SignalR/WebSocket connections. It authorizes
every order subscription and sends only order identifiers and status data the
caller may see. Clients remember the last sequence. On first connection or
reconnect they fetch the latest authorized snapshot, request any available
events after that sequence, then resume the live stream. Duplicate events are
safe, and a retention gap falls back to a fresh snapshot.

Notification processing is a separate consumer. It creates idempotent in-app
notification records without extending the order transaction or delaying the
tracking stream. Poison events move to a DLQ for controlled investigation and
replay; they are not retried forever on the main queue.

## Alternatives and trade-offs

- Polling SQL alone is operationally simple, but it increases read load and
  makes the experience less timely. Polling remains the reconnect fallback.
- Publishing directly after committing an order is simple, but a crash between
  those operations loses the event. The transactional Outbox is selected.
- Making RabbitMQ the source of truth improves replay flexibility but adds an
  event-sourcing model the team does not yet need. SQL remains authoritative.
- Exactly-once delivery is not claimed. Stable identities, Inbox records, and
  sequence checks provide effectively-once state effects over at-least-once
  transport.

The design adds relay, broker, projection, and gateway operations. In return it
separates command durability from real-time delivery, scales connection-heavy
and worker workloads independently, and provides explicit recovery behavior.

## Validation

The implementation should prove: atomic order/history/Outbox writes; broker
outage followed by lossless relay recovery; duplicate and out-of-order consumer
handling; unauthorized subscription rejection; reconnect from a last-seen
sequence; DLQ retention; and observable end-to-end correlation and lag. Load
tests should measure command latency, relay lag, connected clients per gateway,
fan-out delay, reconnect time, and snapshot/cache hit rate.
