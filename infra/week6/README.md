# Week 6 Reversible Releases

This directory switches a paired API/Next release between blue and green behind
one stable Nginx endpoint. SQL Server, RabbitMQ, Vault, data volumes, and queue
workers remain singular. Promotion and rollback consume immutable manifests;
neither path builds application images on the target host.

## Preconditions

1. Run `bash infra/week5/provision.sh` and leave the Week 5 stateful services
   and workers healthy.
2. Supply a trusted `release-manifest.json` independently. It must name exact
   API, Next, and migrator registry digests and declare rollback/database, JWT,
   and message-envelope compatibility. The GitHub Actions workflow does not
   create this manifest.
3. Keep `infra/week5/.runtime` and `infra/week6/.runtime` on the dedicated host.
   The latter is operational state; the former contains secrets and Vault
   recovery material. Neither belongs in Git or CI artifacts.
4. Keep loopback port 8088 free.

## Local release drill

The local build command is intentionally separate from deployment. It records
Docker's content-addressed image IDs in a manifest, after which deploy never
rebuilds them.

```bash
bash infra/week6/prepare-local-release.sh
bash infra/week6/deploy.sh infra/week6/.runtime/local-builds/release-<revision>.json
```

For an explicitly local proof from an uncommitted training tree:

```bash
bash infra/week6/prepare-local-release.sh --allow-dirty
```

Create a second release, then promote it with the new manifest. The controller
selects the idle color automatically:

```bash
bash infra/week6/deploy.sh <second-release-manifest.json>
curl -i http://127.0.0.1:8088/health/version
```

After the second successful cutover, rollback is exactly one command because
deploy archived the prior manifest by its SHA-256 hash:

```bash
bash infra/week6/rollback.sh
```

An operator or protected workflow may supply the exact prior manifest
explicitly. A different file—even one with the same revision—is rejected.

## Promotion gates

`deploy.sh` performs these steps under one `deploy.lock`:

1. Validate manifest schema, compatibility declarations, unique components,
   safe release label, and immutable references.
2. Preserve the manifest under its content hash and render a permission-limited
   Compose environment for both colors.
3. Pull the exact candidate API/Next/migrator digests (or verify local image
   IDs) and run that migrator once.
4. Recreate only the idle API and Next, then wait for container health.
5. Execute `smoke-idle.mjs` inside the idle Next container. It checks API SQL
   readiness, exact color/SHA, SSG/SSR/ISR/CSR, a versioned static asset,
   Next-to-API routing, a token across colors, login, and keyed durable bulk
   admission/replay/status.
6. Generate a complete Nginx candidate include and validate it without touching
   the live include.
7. Run a continuous request probe, atomically move the include, perform a
   graceful Nginx reload, and reject any connection error or unexpected release.
8. Observe the target color for the configured window, then atomically commit
   `state.json`. Until that commit, any post-switch failure restores the old
   include automatically.

The former color stays warm. Its static chunks are the backup for 404s from the
new release, protecting in-flight browser documents. After the agreed rollback
window, it may be stopped without removing it:

```bash
bash infra/week6/retire-previous.sh --confirm
```

Rollback restarts it from the exact archived IDs if required.

## Failure behavior

| Failure | Controller response |
| --- | --- |
| Invalid/mutable manifest | Stop before pull or migration |
| Missing Week 5 network/secret | Stop before deployment |
| Migration fails | Keep current color active; do not start/switch candidate |
| Idle readiness/smoke fails | Capture candidate logs, stop candidate, keep public traffic unchanged |
| Candidate `nginx -t` fails | Leave active include and traffic unchanged |
| Cutover/observation probe fails | Restore prior include and gracefully reload |
| Simultaneous deploy/rollback | Second command fails on the shared lock |
| Rollback manifest mismatch | Fail closed without changing traffic |

Rollback never runs a migration or down migration. A release that violates
expand/migrate/contract is a no-go; an HTTP switch cannot repair an incompatible
database.

## Evidence and tuning

Sanitized JSON is stored under `infra/week6/.runtime/evidence/`. It records idle
checks, the exact observed before/after colors and SHAs, request counts, and any
failure. It contains no token, account password, secret, or request body.

Local drills append timestamped files to that directory. Operators may set
`RELEASE_EVIDENCE_NAMESPACE` to a unique label; the scripts reject unsafe labels
and nonempty run-scoped directories.

```bash
OBSERVE_SECONDS=30 CUTOVER_PROBE_MILLISECONDS=10000 \
  bash infra/week6/deploy.sh <release-manifest.json>
```

The stable URL is `http://127.0.0.1:8088`. Nginx emits structured access logs
with correlation ID, release, status, upstream, and timing. SQL/Outbox/RabbitMQ
operational checks remain available through the Week 5 stack.

## CI publishing boundary

The GitHub Actions workflow runs quality gates, uploads the .NET and web build
artifacts, then builds and pushes six images to GHCR. It does not create a
release manifest or invoke promotion and rollback. The scripts in this folder
remain available for manual operations and require a separately supplied
release manifest.

Workers deliberately do not have colors: Nginx cannot route queue delivery.
Roll Outbox Relay and primary worker separately from their manifest
digests only while envelope version 1 remains accepted; Inbox deduplication and
Outbox leases make brief compatible overlap safe.
