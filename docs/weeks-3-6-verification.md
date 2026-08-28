# Weeks 3-6 Requirements and Deliverables Verification

Verified locally on 2026-08-28 against the requirements in
`bethovenacha-training-plan.html`. This report records repository evidence and
repeatable technical checks. It does not claim completion of activities that
require a person, an external account, or an EM decision.

## Result summary

| Week | Repository and runtime result | Human/external gate |
| --- | --- | --- |
| 3 | Complete and verified | Present the ADR to the EM/team and retain attendance or review evidence |
| 4 | Complete and verified | Obtain EM Code Review #2 approval and retain the hosted GitHub Actions run URL |
| 5 | Complete and verified | Obtain the frontend audit checkpoint/sign-off |
| 6 | Complete and verified | Complete Skill IQ retakes, live EM walkthrough, and Grade 4 go/no-go decision |

## Week 3 - Message queues and architecture presentation

- RabbitMQ, SQL Server, API, Outbox Relay, primary Worker Service, and DLQ
  Recovery Worker run as separate containers through
  `notification-api/compose.week3.yml` and `infra/week5/compose.yml`.
- Main, retry, recovery, DLQ, and parking behavior is implemented by the
  worker projects and shared RabbitMQ topology. Unit and workflow tests cover
  retry limits, dead-letter recovery, message validation, and deduplication.
- The ten corrected React hooks snippets and explanations are indexed in
  `next/snippets-fixed/README.md`.
- The presentation-ready decision is `docs/ADR-Week3.md`; the maintained
  numbered decision is `docs/adr/003-week-3-rabbitmq-workers-and-dlq.md`.
- `ExplanationWeek3.pdf` and `Demonstration guide.pdf` are readable supporting
  deliverables.

The repository can provide the presentation material, but it cannot prove that
the required 15-minute presentation was actually delivered. That remains an
EM/team evidence item.

## Week 4 - Idempotency, Outbox, Next.js, and CI

- `X-Idempotency-Key`, transactional EF Core Outbox records, leased relay,
  publisher confirms, and durable consumer Inbox deduplication are implemented.
- `notification-api/Specification/Week4-Idempotency-Outbox.postman_collection.json`
  verifies acceptance, deterministic replay, RFC 7807 conflict handling, and
  terminal processing.
- The live broker-outage proof passed with RabbitMQ stopped: HTTP admission and
  replay returned `202`, the same job identity was returned, admission took
  46.235 ms, one unpublished Outbox row was durable with zero effects, and
  restart recovery produced one published, Inbox-deduplicated effect. Sanitized
  evidence is generated under ignored `infra/week5/.runtime/evidence/`.
- The Next.js rendering lab exposes SSG, SSR, ISR, and CSR pages with visible
  snapshots. Its production Playwright suite verifies the actual cache and
  hydration behavior of every mode.
- Pull-request, quality-gate, publish, promote, and rollback workflows are in
  `.github/workflows/`. PR validation does not publish; trusted publication
  builds/tests before producing digest-pinned images and a release manifest.
- `docs/adr/004-week-4-idempotency-outbox-inbox-and-delivery-pipeline.md`,
  `ExplanationWeek4.pdf`, and `ExplanationWeek4To6.pdf` document the decisions
  and demonstration path.

The workflows pass local static validation, but only GitHub can provide a
hosted successful run URL and only the EM can complete Code Review #2.

## Week 5 - Frontend polish and Vault-backed Compose

- The corrected React hooks remain covered by lint, TypeScript, unit, production
  build, and browser tests.
- The Angular OnPush demo shows same-reference mutation, immutable replacement,
  and signal-driven updates. Its behavioral tests, production build, and
  unprivileged Nginx container build pass.
- `infra/week5/provision.sh` provisions persisted Vault, separate least-privilege
  AppRoles, SQL Server, RabbitMQ, one-shot migrations, API, Outbox Relay, both
  consumers, Next.js, and optional Angular. Secrets are rendered from Vault to
  ignored runtime files and protected with NTFS ACLs on Windows.
- Two consecutive non-reset provisioning runs completed. Both preserved state,
  applied no unnecessary migrations, and passed API, rendering-mode,
  Outbox/RabbitMQ/worker smoke checks. The second run verified protected ACLs on
  47 runtime objects.
- xUnit unit/API/workflow coverage and the real SQL/RabbitMQ integration case
  pass. The testing split is documented in `notification-api/README.md`.
- `docs/adr/005-week-5-compose-vault-and-frontend-runtime.md` and
  `ExplanationWeek5.pdf` provide the design and demonstration material.

The remaining frontend audit checkpoint is a reviewer sign-off, not a missing
software artifact.

## Week 6 - Zero-downtime deployment and final readiness

- `infra/week6/prepare-local-release.sh` creates a content-addressed immutable
  manifest separately from deployment.
- `infra/week6/deploy.sh` migrates once, starts only the idle API/Next pair,
  verifies exact release identity, SQL readiness, all four rendering modes,
  static assets, BFF routing, cross-color authentication, login, and durable
  idempotent admission before validating and atomically reloading Nginx.
- A local blue-to-green promotion passed its continuous request/observation
  gate. `infra/week6/rollback.sh` then restored the exact archived blue manifest
  in one command without running a migration or reversing shared state.
- Seven release-controller tests cover allowed cutovers, unexpected responses,
  immutable manifests, exact rollback state, malformed state, and mutable-image
  rejection.
- `docs/adr/006-week-6-paired-blue-green-release-control.md`,
  `ExplanationWeek6.pdf`, and `Demonstration guide.pdf` document the release
  and assessment walkthrough.

Skill IQ retakes, the live EM walkthrough, and the final Grade 4 go/no-go
decision require external records and signatures. They must not be inferred
from passing repository checks.

## Verification commands and outcomes

- `dotnet restore/build/test notification-api/NSA.sln`: 8 projects built with
  0 warnings and 0 errors; 123 non-live tests passed.
- Live xUnit category against provisioned SQL/RabbitMQ: 1 test passed.
- Next.js: ESLint, TypeScript, 21 unit tests, production build, and 5 Playwright
  tests passed.
- Angular: 3 tests, production build, and production container build passed.
- Newman 6.2.2: 5 requests and 8 assertions passed.
- Week 6 Node tests: 7 passed.
- Week 5/6 shell syntax and all Compose model validations passed.
- All GitHub Actions workflows passed `actionlint`.
- All six PDF guides loaded with PDF.js and contained extractable text.
