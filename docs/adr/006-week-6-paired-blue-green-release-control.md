# ADR 006 — Week 6: Paired Blue/Green Release Control

**Status:** Accepted locally; protected production-like approval remains external

**Decision period:** Week 6

**Last validated:** 2026-08-24

## Context

The public application is a paired Next.js server and API. They must move
together because the same-origin BFF is part of the contract. SQL Server,
RabbitMQ, Vault, queues, and volumes are shared state and cannot be duplicated
per color. Pre-cutover tokens must remain valid, and old/new binaries overlap on
one schema during the rollback window.

## Decision

Run `api-blue/next-blue` and `api-green/next-green` behind one digest-pinned
Nginx edge. Both colors use the same Vault-rendered JWT key and ordinary SQL
credential. Stateful services remain Week 5 singletons. Queue workers roll
separately because Nginx cannot route queue delivery; versioned envelopes and
Inbox/Outbox identifiers make brief compatible overlap safe.

Promotion accepts only a validated immutable release manifest containing exact
registry digests or local content-addressed image IDs and explicit database,
JWT, and envelope compatibility declarations. It runs that release's migrator
once, starts only the idle pair, and proves exact release identity, SQL
readiness, SSG/SSR/ISR/CSR, static assets, BFF routing, cross-color JWT validity,
login, and idempotent durable admission.

The controller renders a complete candidate Nginx include, validates it without
touching live configuration, atomically replaces the include, and gracefully
reloads Nginx. A continuous fresh-connection probe accepts only the exact
before/after color and revision pairs. Until the observation gate commits
deployment state, any failure restores the prior include automatically.

Rollback is an independent one-command operation. It accepts only the exact
archived prior-manifest hash, re-proves that pair, switches traffic, and records
evidence. It does not rebuild, migrate, down-migrate, or alter shared volumes.

## Alternatives considered

- **Stop/start replacement:** operationally simple but causes an avoidable
  outage and has a slower recovery path.
- **Duplicate the whole platform per color:** isolates more state but risks
  split data and queue ownership.
- **Paired stateless colors over shared durable state:** selected, with strict
  backward-compatible schema and message contracts.

## Consequences and residual risks

- Expand/migrate/contract discipline is mandatory until rollback closes.
- The prior color consumes resources while it remains warm.
- One API instance per color proves planned cutover, not instance-failure HA.
- Fresh HTTP connections were continuously tested; persistent streaming and
  long-lived connections require a production-shaped load test.
- Local Vault and Nginx are training topology, not production HA architecture.
- Protected reviewers, the deployment runner, Skill IQ retakes, live walkthrough,
  and final Grade 4 signature are external/human gates.

## Verification and Week 6 evidence

- `node --test infra/week6/tests/*.test.mjs`
- `bash infra/week6/prepare-local-release.sh --allow-dirty`
- `bash infra/week6/deploy.sh <immutable-manifest.json>`
- Verify `http://127.0.0.1:8088/health/version`, rendering modes, BFF, auth,
  idempotency, and exact active release.
- Promote a second manifest, observe zero probe failures, then run
  `bash infra/week6/rollback.sh` and prove the archived prior release is active.
