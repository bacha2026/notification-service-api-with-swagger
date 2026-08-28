# Notification Service Training System

This repository implements the six-week engineering schedule as one coherent
system: a versioned .NET 8 notification API, durable asynchronous delivery,
React/Next.js and Angular teaching surfaces, reproducible Vault-backed local
infrastructure, and immutable blue/green release control.

## Repository map

| Area | Responsibility |
| --- | --- |
| `notification.core` | Reusable application, domain, persistence, infrastructure, and service layers |
| `notification-api` | ASP.NET Core HTTP host, OpenAPI, versioning, auth, RFC 7807, health/version endpoints |
| `notification.migrator` | One-shot EF Core schema owner |
| `notification.outbox.worker` | Leased SQL Outbox relay with confirmed RabbitMQ publication |
| `notification.worker` | Primary bulk command consumer with durable Inbox deduplication |
| `notification.dlq.worker` | Controlled recovery/retry/parking consumer |
| `notification.tests` | Unit, API contract, workflow, resilience, and durable-admission tests |
| `next` | Storefront, ten hook corrections, four rendering-mode lab, BFF, and browser tests |
| `angular` | OnPush/signals/immutable-input demonstration and tests |
| `infra/week5` | Persisted Vault, SQL Server, RabbitMQ, full Compose stack, and rerunnable provisioning |
| `infra/week6` | Digest-only paired blue/green deployment, Nginx cutover, probes, state, and rollback |
| `.github/workflows` | PR gates, trusted publication, protected promotion, and protected rollback |

## Fast verification

```powershell
dotnet restore notification-api/NSA.sln
dotnet build notification-api/NSA.sln --configuration Release --no-restore
dotnet test notification-api/NSA.sln --configuration Release --no-build
npm ci --prefix next
npm --prefix next test
npm --prefix next run build
npm ci --prefix angular
npm --prefix angular test -- --watch=false
npm --prefix angular run build
node --test infra/week6/tests/*.test.mjs
```

For the real infrastructure path, start with
[`infra/week5/README.md`](infra/week5/README.md). For release promotion and
rollback, use [`infra/week6/README.md`](infra/week6/README.md). The historical
Week 3 presentation decision is in [`docs/ADR-Week3.md`](docs/ADR-Week3.md);
its publication gap is explicitly superseded by the current numbered Week 4
Outbox/Inbox ADR.

Week 1 architecture evidence is the **Real-Time Tracking Architecture** page in
[`drawio/notification.drawio`](drawio/notification.drawio) and its concise
[`one-page decision record`](docs/week1-real-time-order-tracking-decision.md).
The first three numbered records in [`docs/adr`](docs/adr) satisfy the Week 2
three-ADR deliverable; the later records keep Weeks 4-6 decisions auditable.

Generated credentials, Vault recovery material, deployment state, and evidence
are under ignored `infra/**/.runtime/` directories. Do not commit or paste their
contents into tickets, logs, or demonstrations.
