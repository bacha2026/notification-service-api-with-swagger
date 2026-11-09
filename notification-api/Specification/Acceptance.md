# Notification Service API Acceptance Specification

## Week 1–2 application and API acceptance

- Product catalog exposes at least five products with image URLs, names, prices, descriptions, and quantities.
- Product detail lookup returns one product suitable for a detail page with an add-to-cart action.
- Product management endpoints allow creating and updating products through POST /api/products and PUT /api/products/{id}.
- Product creation and update requests validate required text fields, non-negative prices, and non-negative available quantities.
- Cart endpoints allow visitors to add products, update quantity, remove products, and view subtotals and total amount.
- Cart item creation and quantity changes enforce positive quantities through the CartItem domain entity.
- Guest order placement converts the cart into an order, persists line items, clears the cart, and queues in-app notifications for every account with the Admin role and for the visitor.
- Order status updates track order, payment, fulfillment, and delivery statuses independently.
- Order creation, order item creation, order totals, and order status changes are owned by the Order and OrderItem domain entities.
- Notification CRUD endpoints support create, read, update, and delete operations.
- Notification creation, updates, required-field validation, and sent-state changes are owned by the Notification domain entity.
- Controllers contain endpoint routing and HTTP response shaping while application services handle persistence, DTO mapping, and workflow coordination.
- The worker creates persisted notification records and does not claim delivery through an external email provider. A bulk item succeeds when its notification record and durable Inbox receipt commit.
- Polly retry and circuit breaking protect the Outbox Relay's outbound RabbitMQ publication, including connection failures and broker negative acknowledgements or mandatory returns. Failed Outbox rows remain pending with bounded backoff.
- API v1 and v2 publish separate OpenAPI documents with concrete versioned paths; legacy unversioned routes remain documented in v2 only.
- All versioned and compatibility business-API error paths return RFC 7807 Problem Details, and deprecated v1 responses include `Deprecation` and `Sunset` headers. Operational `/health/*` endpoints intentionally use the health-check status contract instead of the business API error contract.
- Database seed data includes products, cart items, orders, order items, and visitor notification records. Admin notifications are addressed to registered Admin-role accounts.
- The Development and Week 3 training profiles can apply EF Core migrations on API startup. Production-style profiles disable that behavior and invoke the API's one-shot `--migrate` mode.
- OpenAPI 3.0 is exposed through Swashbuckle at /swagger with XML endpoint comments.

## Week 3 messaging and deployment acceptance

- The API, `NSA.Outbox.Worker`, and `NSA.Worker` are separate executable projects and are included in the root solution build.
- Bulk jobs and items are persisted in SQL Server before publication; status is read from SQL and remains queryable independently of the worker process.
- The API atomically persists each job, its items, and a stable versioned Outbox command and returns `202 Accepted` after SQL commits; the relay publishes with confirmation tracking.
- Broker commands contain stable job/message/correlation identifiers and timestamps, but no recipient address, subject, or body.
- RabbitMQ declares durable command, terminal dead-letter, and TTL retry exchanges/queues with stable names.
- The WorkerService uses manual acknowledgement and acknowledges successful work only after durable SQL progress is saved.
- The outbox routes every loaded-command failure to retry and has no DLQ configuration. The notification worker consumes main and retry queues only; all failure categories receive at most three processing attempts by default. Recoverable messages complete; exhausted messages receive persisted `DeadLettered` status and confirmed DLQ publication before acknowledgement. An unavailable terminal status update keeps the message on delayed retry without another processing attempt.
- Duplicate commands for completed jobs are acknowledged without replay; commands for already dead-lettered jobs are confirm-published back to the DLQ.
- Permanent failures go directly to the DLQ. HTTP/database timeouts use fixed delay; HTTP 429/503 use backoff. Bugs and unknown exceptions reach the DLQ after bounded retries. Confirmed retry publication precedes acknowledgement.
- The five-service Docker Compose stack starts SQL Server, RabbitMQ Management, API, Outbox Relay, WorkerService with named volumes, a named network, dependency health checks, ignored local credentials, and loopback-only host ports.
- `/health/live` is process-only. API `/health/ready` verifies SQL Server so durable admission stays available during broker downtime. Relay readiness verifies SQL and RabbitMQ even with an empty backlog; consumer readiness requires an active consumer and recent SQL connectivity.
- The live runtime verification procedures must cover happy completion, worker stop-before-ack redelivery, broker reconnection, in-flight broker restart, publish failure, malformed/schema/type rejection, bounded poison handling, identifier correlation, and retained-DLQ inspection. Generated evidence is run-specific and is not treated as source code.

## Week 4 idempotency and Outbox acceptance

- `POST /api/v2/notifications/bulk` and guest/authenticated order creation accept an optional `X-Idempotency-Key` without breaking clients that omit it.
- The same scoped key and semantic request replays the original response and IDs with `Idempotency-Replayed: true`. A changed bulk payload returns RFC 7807 `409 Conflict`, and an invalid key returns `400`.
- Only a SHA-256 key hash is stored. Receipts survive explicit notification/job deletion for the configured replay lifetime.
- Authenticated checkout commits the order, cart removal, notification job/items, Outbox command, and optional idempotency receipt in one SQL transaction.
- Outbox commands own the stable broker `MessageId`, versioned payload, correlation ID, exchange, and routing key. The relay uses conditional leases, publisher confirms, exponential backoff with jitter, and never drops a failed row.
- Broker downtime does not fail API readiness or durable HTTP admission. After RabbitMQ returns, the relay drains pending rows.
- The consumer records a composite `(MessageId, Handler)` Inbox receipt in the same successful transaction as message effects, before ACK. Retryable failures do not create a receipt.
- Explicit notification deletion removes the notification aggregate, job/items, and matching pending/published Outbox rows; it retains Inbox and idempotency receipts to suppress late duplicates and preserve replay semantics.
- The authenticated feed derives the recipient email from the JWT and returns fields only for `Completed` jobs with `Succeeded` items; it does not claim order-ownership authorization for notification rows.
- Completed jobs referenced by visible notifications are excluded from opportunistic retention cleanup. Expired `CompletedWithErrors` graphs are removed with their hidden notifications and Outbox rows, while `DeadLettered` and legacy `PublishFailed` jobs remain recoverable/readable.
- Legacy notification CRUD remains an anonymous compatibility/training surface and requires a future admin/service authorization policy before production exposure; it is not part of the account-feed privacy boundary.
- The transport remains explicitly at-least-once because broker publish and the Outbox published mark cannot be atomic. Inbox processing makes committed application effects idempotent.

## Week 5–6 compatibility boundaries

- Stable provider-side idempotency and full reconciliation are not claimed.
- Public contracts and versioned broker messages evolve additively; database changes use expand/contract rules so old and new deployment colors can overlap safely.
- API and worker remain stateless apart from SQL/RabbitMQ state, expose dependency-aware readiness, and keep secrets in configuration seams suitable for later Vault integration.
- The `Projects` workspace is the Git root, so the API, Worker, tests, documentation, Next.js, and Angular applications can share one CI checkout.
