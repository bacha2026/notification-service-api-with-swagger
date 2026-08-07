# ADR 001: Use Clean Architecture

**Status:** Accepted

## Context

Business rules should remain independent of ASP.NET Core, EF Core, RabbitMQ, and external providers.

## Decision

Organize the API into Domain, Application, Service, Persistence, Infrastructure, and Presentation boundaries. Dependencies point inward; outer layers implement inner abstractions, and `Program.cs` composes them through dependency injection.

## Trade-offs

This improves testability and replaceability, but adds interfaces and dependency-mapping code.
