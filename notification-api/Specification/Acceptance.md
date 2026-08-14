# Notification Service API Acceptance Specification

## Week 1–2 application and API acceptance

- Product catalog exposes at least five products with image URLs, names, prices, descriptions, and quantities.
- Product detail lookup returns one product suitable for a detail page with an add-to-cart action.
- Product management endpoints allow creating and updating products through POST /api/products and PUT /api/products/{id}.
- Product creation and update requests validate required text fields, non-negative prices, and non-negative available quantities.
- Cart endpoints allow visitors to add products, update quantity, remove products, and view subtotals and total amount.
- Cart item creation and quantity changes enforce positive quantities through the CartItem domain entity.
- Order placement converts the cart into an order, persists line items, clears the cart, and creates email notification records for admin@example.test and the visitor.
- Order status updates track order, payment, fulfillment, and delivery statuses independently.
- Order creation, order item creation, order totals, and order status changes are owned by the Order and OrderItem domain entities.
- Notification CRUD endpoints support create, read, update, and delete operations.
- Notification creation, updates, required-field validation, and sent-state changes are owned by the Notification domain entity.
- Controllers contain endpoint routing and HTTP response shaping while application services handle persistence, DTO mapping, and workflow coordination.
- The worker creates persisted notification records and does not call an external email provider. Polly retry and circuit breaking protect outbound RabbitMQ notification-command publication.
- Bulk email items are persisted and passed through `INotificationDispatcher`; non-email bulk items are persisted as notification records. A bulk item succeeds when its Week 2 record-processing flow completes, while `SentAtUtc` means an enabled provider accepted the email. Job status exposes only safe summary errors.
- API v1 and v2 publish separate OpenAPI documents with concrete versioned paths; legacy unversioned routes remain documented in v2 only.
- All versioned and compatibility business-API error paths return RFC 7807 Problem Details, and deprecated v1 responses include `Deprecation` and `Sunset` headers. Operational `/health/*` endpoints intentionally use the health-check status contract instead of the business API error contract.
- Outbound email attempts use a per-attempt timeout, three exponential retries, and a circuit breaker around each exhausted logical operation.
- Database seed data includes products, cart items, orders, order items, and notification records for visitor@example.test and admin@example.test.
- The API creates or updates NotificationServiceDb on startup through EF Core migrations.
- OpenAPI 3.0 is exposed through Swashbuckle at /swagger with XML endpoint comments.

## Week 3 messaging and deployment acceptance

- The API, `NSA.Worker`, and `NSA.Dlq.Worker` are separate executable projects and are included in the root solution build.
- Bulk jobs and items are persisted in SQL Server before publication; status is read from SQL and remains queryable independently of the worker process.
- The API publishes a persistent, versioned RabbitMQ command with publisher-confirmation tracking and returns `202 Accepted` only after the broker accepts the command.
- Broker commands contain stable job/message/correlation identifiers and timestamps, but no recipient address, subject, or body.
- RabbitMQ declares durable command, dead-letter, recovery-delay, and parking exchanges/queues with stable names.
- The WorkerService uses manual acknowledgement and acknowledges successful work only after durable SQL progress is saved.
- Retryable command failures are bounded to three application attempts; exhausted commands and non-retryable malformed, unsupported-schema, or wrong-type commands reach the named DLQ.
- Duplicate commands for completed jobs are acknowledged without replay; commands for already dead-lettered jobs are rejected back to the DLQ.
- The DLQ Recovery Worker only replays commands explicitly rejected from the main queue. It marks a matching SQL job `RecoveryPending`, clears the exhausted application retry header, publish-confirms to the recovery queue, and then acknowledges the DLQ delivery. Malformed, unknown, non-rejected, and replay-limit-exhausted messages are parked durably.
- The five-service Docker Compose stack starts SQL Server, RabbitMQ Management, API, WorkerService, and DLQ Recovery Worker with named volumes, a named network, dependency health checks, ignored local credentials, and loopback-only host ports.
- `/health/live` is process-only. `/health/ready` verifies both SQL Server and RabbitMQ with bounded timeouts. Worker readiness requires an active consumer and recent SQL connectivity.
- Runtime evidence covers happy completion, worker stop-before-ack redelivery, broker reconnection, in-flight broker restart, publish failure, malformed/schema/type rejection, bounded poison handling, identifier correlation, and DLQ inspection.

## Week 4–6 compatibility boundaries

- The stack is explicitly at-least-once. Transactional Outbox publication, Inbox/deduplication, `X-Idempotency-Key`, stable provider idempotency, and full reconciliation are not claimed.
- Public contracts and versioned broker messages evolve additively; database changes use expand/contract rules so old and new deployment colors can overlap safely.
- API and worker remain stateless apart from SQL/RabbitMQ state, expose dependency-aware readiness, and keep secrets in configuration seams suitable for later Vault integration.
- The `Projects` workspace is the Git root, so the API, Worker, tests, documentation, Next.js, and Angular applications can share one CI checkout.
