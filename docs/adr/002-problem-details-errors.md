# ADR 002: Return Problem Details for API failures

**Status:** Accepted

**Date:** 2026-07-10
**Scope:** HTTP error contract

## Context

Controllers previously returned a mixture of strings, framework-generated validation payloads, and empty error responses. Clients then needed endpoint-specific parsing and could not reliably correlate an error with server logs. Unexpected failures also need to avoid exposing implementation details.

The training requirement calls this the RFC 7807 format. The decision is to use ASP.NET Core's standard Problem Details support and the `application/problem+json` media type.

## Decision drivers

- Give clients one machine-readable shape across validation, missing resources, unsupported versions, and exceptions.
- Include a trace identifier for support and log correlation.
- Keep controller actions focused on application behavior rather than repeated error serialization.
- Preserve useful client-safe validation details while hiding stack traces and infrastructure details.
- Use framework components so JSON settings and content negotiation remain consistent.

## Considered options

| Option | Benefits | Tradeoffs | Outcome |
| --- | --- | --- | --- |
| ASP.NET Core Problem Details service plus global exception handling | Standard media type, framework integration, centralized policy | Requires an explicit exception-to-status mapping and contract tests | **Selected** |
| Error objects built in every controller | Endpoint-level control | Duplicated logic and inconsistent shapes are likely | Rejected |
| Custom global error envelope | Can model project-specific fields directly | Creates a proprietary client contract and duplicates standard behavior | Rejected |
| Empty status-code responses | Minimal server code | No actionable or correlatable client body | Rejected |

## Decision

- Register ASP.NET Core Problem Details and a global `IExceptionHandler`.
- Let `[ApiController]` produce validation Problem Details for invalid model binding/validation.
- Convert known, client-correctable application validation failures to HTTP 400.
- Convert temporary capacity and downstream-availability failures to sanitized HTTP 503 responses with `Retry-After`.
- Preserve not-found and other empty non-success status codes through status-code handling that writes Problem Details.
- Return HTTP 500 for unexpected/configuration/infrastructure failures, log the exception, and omit sensitive exception details from the response.
- Include `status`, `title`, `instance`, and `traceId` consistently. Include `detail` only when it is safe and useful to the caller.
- Write error responses as `application/problem+json` through the configured Problem Details service rather than hand-built endpoint envelopes.

Exception types are not a substitute for domain error semantics. Broad framework exceptions such as `InvalidOperationException` must not automatically be classified as a client error; known application failures should have an explicit mapping.

## Tradeoffs and consequences

Positive consequences:

- Clients share one parser and can report `traceId` during support incidents.
- Error policy, logging, and disclosure rules are reviewed in one place.
- Controllers contain less repetitive response-shaping code.

Costs and risks:

- Changing an exception mapping changes a public API contract and requires tests.
- A generic status-code page has less domain detail than an endpoint-specific failure.
- Stable, dereferenceable `type` URIs and application error codes are not yet defined; clients should not infer domain meaning from human-readable titles.
- Failures after response headers have started cannot always be rewritten safely.

## Validation

Contract tests or a scripted error matrix must cover:

1. Model-binding/validation failure (400).
2. A known client-correctable application failure (400).
3. Missing resource and unknown route (404).
4. Unsupported API version (4xx).
5. Unsupported method (405).
6. Malformed JSON (400).
7. Unsupported request media type (415).
8. Temporary capacity/downstream failure (503), with `Retry-After` and no leaked internal detail.
9. Unexpected exception (500), with server logging and without leaked exception detail.

Each response must use `application/problem+json`, contain the expected status, title, instance, and non-empty `traceId`, and follow the application's configured JSON naming policy. Record the matrix in the [Week 1–2 verification checklist](../verification/week-01-02-checklist.md).

## Revisit when

Introduce a superseding decision when the API needs stable domain error codes/type URIs, localization, distributed trace identifiers, or a different disclosure policy.
