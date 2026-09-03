# ADR 001 — Week 1: OpenAPI and Clean Architecture

**Status:** Accepted

**Decision period:** Week 1

**Last validated:** 2026-08-24

## Context

The Notification Service needs a documented HTTP contract and boundaries that
can survive the later addition of SQL Server, RabbitMQ, background processes,
frontends, and deployment tooling. Business rules must not depend on an ASP.NET
Core controller or a broker client, and worker processes must reuse use cases
without referencing the executable Web host.

## Decision

Use a modular Clean Architecture with these responsibilities:

- **Domain** owns entities, status vocabularies, and business invariants.
- **Application** owns use-case contracts, DTOs, settings, exceptions, and
  ports implemented by outer layers.
- **Service** coordinates use cases and domain behavior.
- **Persistence** and **Infrastructure** implement SQL, authentication,
  messaging, health, and observability adapters.
- **Presentation** owns controllers, API versioning, OpenAPI, and HTTP error
  shaping.
- Each executable `Program.cs` remains a thin composition root.

Each deployable project owns and compiles its application, domain, persistence,
infrastructure, and service code locally. The API additionally owns Presentation
and EF migrations. No deployable executable references another executable or a
shared implementation project.

Expose OpenAPI 3.0 and Swagger UI at `/swagger`. Generate XML documentation for
API and Core types, document every endpoint and response code, and keep the
contract covered by automated tests. Maintain the draw.io architecture diagram
as the visual companion to this decision.

## Alternatives considered

- **One Web project used by every process:** initially simple, but it couples
  workers to Presentation and Web runtime assets.
- **One assembly per layer immediately:** strongest compile-time isolation, but
  adds packaging overhead before separate ownership justifies it.
- **Reusable Core plus thin hosts:** previously selected, then replaced when
  independent deployment and source ownership became the stronger requirement.

## Consequences

- Use cases can run and be tested without an HTTP server.
- Worker publish output does not need the API executable.
- Dependency injection remains explicit at every process boundary.
- Some inner-layer separation is enforced by review and architecture tests
  rather than separate assemblies.
- Shared EF Core units of work are allowed inside the reusable implementation
  boundary for atomic durability, but cannot leak into Domain or controllers.

## Verification and Week 1 evidence

- `dotnet build notification-api/NSA.sln --configuration Release`
- Open `http://127.0.0.1:8080/swagger` and inspect the documented endpoints.
- Confirm the API and worker project files contain no `ProjectReference` items.
- Validate `drawio/notification.drawio` as XML and use it in the architecture
  walkthrough.
