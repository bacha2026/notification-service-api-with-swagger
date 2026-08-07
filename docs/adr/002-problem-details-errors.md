# ADR 002: Process Bulk Notifications Asynchronously

**Status:** Superseded by ADR 003

## Context

Bulk work should not keep an HTTP request open until every notification is sent.

## Decision

The initial design added an in-process `BackgroundService`. `POST /api/v2/notifications/bulk` returns `202 Accepted` with a `jobId`; `GET /api/v2/notifications/bulk/{jobId}` reports progress. The later outbound broker publish uses Polly retry and a circuit breaker.

## Trade-offs

Requests return quickly, but completion is eventually consistent. In-process work is not independently scalable or durable across API restarts, which led to ADR 003.
