# ADR 003: Use RabbitMQ and a Separate WorkerService

**Status:**

## Context

The in-process worker could lose queued work on API restart and could not scale independently or isolate failed messages.

## Decision

Use RabbitMQ in Docker and a separate .NET `WorkerService`. The API applies Polly retry and a circuit breaker when publishing to RabbitMQ. The worker uses bounded attempts and dead-letters malformed or exhausted messages. Demonstrate the DLQ path in the RabbitMQ Management UI.

## Trade-offs

Work is durable and independently scalable, but RabbitMQ adds operations and at-least-once delivery requires idempotent handling.
