# ADR 001: Use URL-segment API versioning

**Status:** Accepted

**Date:** 2026-07-10
**Scope:** Public HTTP API

## Context

Clients need a predictable migration path while version 1 remains available and version 2 becomes the current contract. A version must be visible in documentation, logs, links, and support requests, and callers need an explicit retirement signal rather than a silent breaking change.

## Decision drivers

- Make the selected contract obvious from a copied URL or log entry.
- Support v1 and v2 concurrently during migration.
- Integrate with ASP.NET Core controllers, link generation, and OpenAPI tooling.
- Provide machine-readable deprecation and sunset signals.
- Preserve existing unversioned callers temporarily without making the alias the preferred contract.

## Considered options

| Option | Benefits | Tradeoffs | Outcome |
| --- | --- | --- | --- |
| URL segment (`/api/v2/...`) | Visible, cache-friendly, easy to route/debug, straightforward Swagger grouping | Version is coupled to URLs; generated links must carry the version | **Selected** |
| Request header | Stable resource URLs and a clean URI shape | Harder to discover, test in a browser, cache, and include in copied links | Rejected |
| Query string | Easy to introduce without route changes | Version is less prominent and can be mishandled by caches/proxies | Rejected |
| No explicit version | Least initial configuration | Breaking changes become unsafe and client migration is implicit | Rejected |

## Decision

Use `Asp.Versioning.Mvc` with a URL-segment reader and controller routes shaped as `/api/v{version:apiVersion}/...`.

- Support API versions 1.0 and 2.0.
- Treat the framework-supported `/api/v1/...` and `/api/v1.0/...` spellings as version 1; deprecation behavior is based on the parsed API version rather than path-string matching.
- Mark 1.0 deprecated and treat 2.0 as the default when a compatibility route has no version.
- Publish separate v1 and v2 OpenAPI documents.
- Add `Deprecation: true` and `Sunset: Thu, 31 Dec 2026 23:59:59 GMT` to version 1 responses.
- Retain unversioned `/api/...` routes only as a temporary compatibility surface. They are not a contract for new clients and will not be carried into v3; removal requires a consumer inventory and release notice.
- Introduce breaking contract changes under a new version; do not silently change an existing version's meaning.

The Week 2 v1 and v2 operations are intentionally contract-compatible; this baseline proves version selection, documentation, and retirement mechanics rather than inventing an unnecessary breaking difference.

## Tradeoffs and consequences

Positive consequences:

- Clients can select a contract explicitly and plan migration from response metadata.
- Operations can identify the requested API contract from the request path.
- v1 and v2 can be documented, tested, and retired independently.

Costs and risks:

- Controllers carry version metadata and route templates, and generated `Location` links must retain the requested version.
- Unversioned aliases create ambiguity and must have an explicit removal plan.
- Versioning infrastructure separates contracts but does not itself guarantee that v1 and v2 have meaningful semantic differences.
- Every future version adds documentation, testing, and support overhead.

## Validation

Automated or scripted verification must prove that:

1. Representative v1 and v2 routes return the expected success response.
2. An unsupported version is rejected with the standard Problem Details contract.
3. Both `/api/v1/...` and `/api/v1.0/...` responses contain the exact `Deprecation` and `Sunset` headers; v2 responses do not.
4. `api-supported-versions` and `api-deprecated-versions` are reported as configured.
5. The v1 and v2 OpenAPI documents contain only the operations intended for that document.
6. `Location` headers from create/accepted responses resolve to the same selected version.

Record results in the [Week 1–2 verification checklist](../verification/week-01-02-checklist.md).

## Revisit when

Remove the unversioned routes once all known consumers use explicit versions. Create a superseding ADR if version selection moves to headers, the retirement date changes, or v3 introduces a different compatibility policy.
