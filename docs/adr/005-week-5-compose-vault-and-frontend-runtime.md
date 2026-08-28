# ADR 005 — Week 5: Compose, Vault, and Frontend Runtime

**Status:** Accepted

**Decision period:** Week 5

**Last validated:** 2026-08-24

## Context

The full stack must be repeatable without hardcoded credentials or cloud
accounts. Persisted SQL Server and RabbitMQ volumes retain their initial
credentials, so silently rotating generated values on a rerun would make the
platform inaccessible. Frontend runtime behavior and tests must also be visible
inside the same local environment.

## Decision

Use `infra/week5/provision.sh` as a serialized, safely rerunnable local
provisioner for persisted Vault, SQL Server, RabbitMQ, one-shot database
bootstrap and migrator, API, Outbox Relay, primary Worker, DLQ Recovery Worker,
Next.js, and optional Angular.

Compose uses named data volumes, loopback-only published ports, an internal
backend network, separate management/edge connectivity, dependency-aware health
checks, digest-pinned base images, and non-root application containers. Schema
changes run through the dedicated one-shot migrator rather than ordinary API or
worker startup.

Vault is initialized and unsealed explicitly with persisted local Raft storage.
Root bootstrap creates only missing 384-bit generated values and writes
role-specific KV documents. API, worker, and migrator AppRoles can read only
their declared paths; negative authorization checks include cross-role and
provisioning-fingerprint access. Applications receive Key-per-file Docker
secret mounts and never a Vault/root token.

Vault retains an accidental-drift fingerprint for the six managed values using
`generating → pending → established` lifecycle state. Missing or changed values
fail closed before regeneration. New evidence publishes only `initialized` or
`stable`, never the digest. A privileged Vault administrator can still rewrite
both values and state, so this is not tamper evidence.

On POSIX, private modes are enforced. On NTFS-mounted Git Bash/WSL paths, the
bundled PowerShell guard removes inherited ACLs and permits only the current
user, SYSTEM, and Administrators before secret work and after evidence writing.
Reset is explicit, path-checked, volume-scoped, and requires typed confirmation.

Frontend evidence uses corrected React hook lifecycles, a Next rendering lab,
and an Angular OnPush/signals demo whose immutable update renders while the
in-place mutation illustrates why reference identity matters.

## Alternatives considered

- **Ignored `.env` only:** avoids Git leaks but still exposes values through
  Compose interpolation and weakens rerun/recovery semantics.
- **Cloud secret service:** production-capable, but violates the account-free
  local training constraint.
- **Persisted local Vault plus role-scoped file delivery:** selected for explicit
  lifecycle, least privilege, and reproducibility.

## Consequences

- Vault recovery material and its named volume are an inseparable sensitive
  pair and must never be published.
- Local Vault is training infrastructure, not production TLS/HA/auto-unseal.
- Provisioning has more explicit checks, but reruns preserve state safely.
- The testing pyramid spans deterministic unit/contract tests, real SQL/Rabbit
  boundaries, browser tests, and full Compose smoke.

## Verification and Week 5 evidence

- Run `bash infra/week5/provision.sh --with-angular` twice.
- Both runs must finish migrations, health checks, and end-to-end smoke; the
  later evidence must report `secret_set_fingerprint_status=stable`.
- API/Next/Angular/Vault/SQL/RabbitMQ and all three workers must be healthy.
- Secret scanning verifies generated values are absent from source, image
  history, and container metadata.
