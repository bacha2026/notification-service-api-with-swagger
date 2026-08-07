# ADR 004: Adopt Domain-Aligned Event-Driven Microservices

**Status:** Proposed for EM/team review

## Context

The system will grow beyond Orders into Inventory, Accounting, Analytics, Notifications, and other domains. Containers package deployments; they do not create service or data boundaries.

## Decision

Incrementally extract domain-aligned microservices. Each service owns its logic, deployment, and data. Order events flow through RabbitMQ to a separate queue per consumer; the existing Notification WorkerService is the first step. Use Outbox publishing, idempotent Inbox processing, versioned contracts, bounded retries, DLQs, and correlation IDs. Keep synchronous calls only where an immediate response is required.

## Options

- **Modular monolith:** simpler operations, but one deployment and scaling boundary.
- **Synchronous microservices:** independent deployments, but stronger runtime coupling.
- **Event-driven microservices:** selected for independent evolution and fan-out, accepting eventual consistency.

## Trade-offs

Services can deploy and scale independently, but require contract governance, observability, duplicate handling, data duplication, and more operational expertise.
