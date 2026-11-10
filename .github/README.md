# GitHub Actions

The Week 4 pipeline separates untrusted pull-request validation from trusted
publication:

- `pull-request.yml` calls the reusable quality gates with a read-only token. It
  restores, builds, and tests the .NET solution; lints, type-checks, tests, and
  builds Next.js; tests and builds Angular; and tests the infrastructure
  controllers and Compose models. It never containerizes, logs in to a
  registry, publishes an artifact, or pushes an image.
- `publish.yml` runs only for pushes to `main` and tags beginning with `v`. It
  repeats the same gates, validates every runtime Dockerfile, publishes
  attested application archives, then pushes images to GHCR using the
  repository-scoped `GITHUB_TOKEN`.
- `quality-gates.yml` contains the shared validation jobs so pull requests and
  releases cannot drift onto different test paths.

`quality-gates.yml` can also be run manually with `run_week4_live_proof`
enabled. That opt-in job uses generated, ephemeral credentials to run the real
SQL Server/RabbitMQ outage-and-recovery proof and retains only its sanitized JSON
evidence. It is disabled by default and is never enabled by the pull-request
caller. Setting the repository variable `RUN_WEEK4_LIVE_PROOF=true` includes it
in a trusted main/tag publication run.

Published images use an immutable `sha-<40-character-commit>` tag. A valid
semantic-version Git tag such as `v1.2.3` also produces the `1.2.3` image tag.
No mutable `latest`, major, or minor tag is generated. Each image includes OCI
source/revision labels, BuildKit SBOM and provenance attestations, and signed
GitHub build provenance. The pipeline retains each registry digest as a 90-day
JSON release record containing its digest-pinned reference, emitted tags,
revision, workflow run, and attestation flags. A deployment can therefore pin
the exact image manifest instead of resolving a tag again. A final, attested
`release-manifest-<commit>` artifact combines and verifies all six image
records for downstream deployment tooling.

The manifest also carries the release compatibility contract consumed by
`infra/week6/release-control.mjs`: expand/migrate/contract database evolution,
rollback support, one shared Vault JWT-key scope, and accepted message envelope
version 1. Mutable tags are rejected by the deployment controller even when a
digest-pinned record also has friendly tags.

`promote.yml` and `rollback.yml` are manual entry points that call the shared
`deploy-release.yml` workflow. The shared job is pinned to the protected
`production-like` environment; a dispatcher cannot select a weaker environment.
Before downloading anything, it fails unless dispatched from `refs/heads/main`,
checks out that trusted controller branch explicitly, treats both dispatch
strings as untrusted data, and verifies through
the GitHub API that the selected numeric run belongs to this repository, ran
`.github/workflows/publish.yml` for the requested 40-character revision, was
triggered by `push`, and completed successfully. After download, the controller
revalidates the manifest's source, run, revision, six component records, exact
GHCR names/digests, and attestation declarations. `gh attestation verify` then
requires signed provenance from this repository's `publish.yml`, the requested
source commit, and a GitHub-hosted signer before any registry login or deployment.

The shared workflow applies the same run, manifest, and signed-provenance gates
before invoking the independent promote or rollback script. The rollback path
checks the exact prior manifest hash stored at the last cutover. Both operations
target a dedicated self-hosted Linux runner labeled
`nsa-deployment`; that runner must provide current `gh`, Node.js, Bash, Docker,
and the other Week 6 prerequisites. It must retain ignored Week 5 Vault material
and Week 6 deployment state, so checkout deliberately does not clean those
directories. Promotion and rollback set a unique evidence namespace from the
GitHub run ID and attempt; only JSON created by that invocation is uploaded.
The `production-like` environment must independently restrict deployment branches
to `main`; source guards complement rather than replace that organization control.

The repository should require the pull-request validation jobs in branch
protection for `main`. GHCR publishing needs no personal token: GitHub supplies
`GITHUB_TOKEN`, and only the image-publishing job receives `packages: write`.
Configure required reviewers on the `production-like` environment before using
promotion or rollback. Those jobs receive only `packages: read`.
