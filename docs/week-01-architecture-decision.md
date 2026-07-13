# Week 1 Architecture Decision: A capability-organized layered monolith

**Decision:** Keep the Notification Service API as one ASP.NET Core 8 deployable for the Week 1–2 baseline, separate responsibilities by layer, and organize code within each layer by business capability.

**Status:** Accepted for the training baseline

**Diagram evidence:** editable [`drawio/notification.drawio`](../drawio/notification.drawio) plus readable Week 2 runtime exports in [PDF](architecture/week-02-runtime-architecture.pdf), [PNG](architecture/week-02-runtime-architecture.png), and [SVG](architecture/week-02-runtime-architecture.svg).

## Context and constraints

The anchor project must demonstrate a documented CRUD API, order tracking, notifications, SQL persistence, and a path to asynchronous processing. It must keep HTTP, workflow, persistence, and provider concerns separate while remaining runnable by one engineer. Independent scaling, a durable broker, and separate deployment pipelines are outside the Week 1–2 baseline; splitting early would add operational work before a scaling or ownership boundary is proven.

## Chosen design

Use a layered modular monolith with feature/screaming folders inside the layers:

| Boundary | Responsibility | Examples |
| --- | --- | --- |
| `Presentation` | HTTP routes, API versions, OpenAPI metadata, Problem Details | Product, Cart, Order, Notification controllers |
| `Application` | Use-case interfaces and transport-neutral contracts | `IOrderService`, `IEmailSender`, DTOs |
| `Domain` | Business state and vocabulary | Product, Cart, Order, Notification entities/enums |
| `Service` | Workflow orchestration and validation | Cart/order/notification services |
| `Persistence` | EF Core mapping, repositories, migrations | `NotificationDbContext`, SQL repositories |
| `Infrastructure` | Runtime adapters and hosted processing | Postbound adapter, bulk worker |

Dependencies point toward application abstractions rather than from application logic toward a concrete provider. `Program.cs` is the composition root. SQL Server stores business records, and EF Core migrations run at startup. Because one project cannot enforce these boundaries at compile time, review and tests guard them until a split earns its cost.

```text
HTTP client → Controller → Application interface → Service → Repository → SQL Server
                                      └──────────→ IEmailSender → Postbound/log adapter

Bulk POST → in-memory Channel → hosted worker → Notification service/dispatcher → SQL + provider abstraction
```

The bulk path persists every item and routes email items through the provider abstraction, but it is not durable provider delivery. API versioning, error contracts, background processing, and provider resilience are refined in [ADR 001](adr/001-url-api-versioning.md), [ADR 002](adr/002-problem-details-errors.md), and [ADR 003](adr/003-background-bulk-notifications-and-resilience.md).

## Alternatives and tradeoffs

- **Controllers call EF/provider APIs directly:** fewer files, but couples HTTP, persistence, and vendor behavior. Rejected.
- **Separate class-library projects now:** stronger compile-time boundaries, but excessive baseline plumbing. Deferred until coupling or ownership justifies it.
- **Microservices per capability:** independent scaling, but premature distributed transactions, messaging, and operations. Rejected.
- **Capability folders without layers:** strong feature locality, but adapter and persistence boundaries become less visible. The selected hybrid keeps both signals.

The choice optimizes learning speed and reviewability, not independent scaling. Its accepted weakness is convention-only boundaries in one assembly. Later worker extraction must preserve application contracts instead of causing a cosmetic rewrite.

## Validation and evolution trigger

The decision is valid when a reviewer can run from the README, trace requests across boundaries, replace an adapter through dependency injection, verify Swagger/error contracts, and test without a real provider. The draw.io labels and the dedicated Week 2 runtime export now show the bounded Channel/worker, provider-policy, versioning, persistence, and Problem Details flow; they must be reviewed again whenever those boundaries change.

Revisit this decision when the Week 3 requirement introduces RabbitMQ and a separately deployable worker, or earlier if independent scaling, release cadence, security isolation, or team ownership makes the single deployable a measurable constraint.
