# Week 5 Reproducible Platform

`provision.sh` creates the full local platform with persisted Vault, SQL Server,
RabbitMQ, a one-shot migrator, API, Outbox Relay, primary worker, Next.js, and an optional Angular profile. A normal second run preserves
volumes and credentials and repeats bootstrap, migration, health, and smoke
checks safely.

## Prerequisites

- Docker Desktop with the Linux engine and Compose v2
- Git Bash, WSL, or another Bash environment with `curl`, `grep`, `sed`, `awk`,
  and `sha256sum`
- Windows PowerShell when provisioning on an NTFS-mounted path
- Free loopback ports 8200, 14330, 56720, 15672, 8080, and 3000
- Port 4200 as well when using the Angular profile

From the repository root:

```bash
bash infra/week5/provision.sh
bash infra/week5/provision.sh             # required rerun proof
bash infra/week5/provision.sh --with-angular
```

The provisioner refuses to continue when persisted SQL/RabbitMQ state exists
without the matching Vault storage and recovery material. It never deletes a
volume on a normal run. The only destructive path is:

```bash
bash infra/week5/provision.sh --reset
```

That path names its exact project and runtime directory and requires typing
`RESET nsa-week5`.

Application traffic is isolated on the internal `backend` network. Only the
loopback-published Vault, SQL Server, RabbitMQ, and API endpoints also join the
non-internal `management` network; Next.js reaches the host through the
separate `edge` network. No service endpoint is published on a non-loopback
host address.

## Secret boundary

Vault uses persisted integrated Raft storage and is initialized/unsealed
explicitly. Its local recovery file and all rendered service files live under
ignored `.runtime/`. On a native POSIX filesystem the provisioner applies mode
`600`/`700`. On Windows/NTFS, `chmod` is not a confidentiality boundary, so the
bundled ACL guard removes inheritance and limits the generated subtree to the
current user, SYSTEM, and Administrators before Vault writes anything, then
revalidates it after evidence generation. Provisioning fails if that guard is
unavailable or ineffective; WSL ext4 uses the POSIX path. The provisioner logs
into
three distinct AppRoles and uses those tokens—not the root token—to render each
delivery boundary: API can read only `secret/nsa/api`, workers only
`secret/nsa/worker`, and the migrator only `secret/nsa/migrator`. Provisioning
also fails if cross-role reads succeed. Applications receive only their
individual Docker secret files and never any Vault token. .NET reads those
files through the Key-per-file provider. SQL bootstrap reconciles separate
ordinary app and schema-owner logins; only the migrator gets the latter.

This is local training infrastructure. Production Vault additionally requires
TLS, HA, audit devices, automated unseal, protected recovery workflows, and a
real secret-delivery agent/CSI integration.

## Acceptance checks

Provisioning waits for every declared health check, applies pending migrations,
and runs `smoke.sh`. The smoke submits an idempotent bulk job and waits for its
terminal completion through the Outbox/RabbitMQ/worker path. It also proves API
and Next release endpoints and all four rendering modes.

Useful diagnostics:

```bash
docker compose -f infra/week5/compose.yml ps
docker compose -f infra/week5/compose.yml logs --tail 100 api outbox-relay worker
bash infra/week5/smoke.sh
NSA_LIVE_BASE_URL=http://127.0.0.1:8080/ dotnet test \
  notification.tests/NSA.Tests/NSA.Tests.csproj \
  --filter Category=LiveIntegration
```

Non-secret image, timestamp, AppRole authorization, and six-value secret-set stability
status is written beneath `infra/week5/.runtime/evidence/`; the digest itself is
not published. Vault retains the authoritative accidental-drift baseline only
after a successful smoke. A later run records `stable` and fails before
regenerating a missing field or accepting changed values. This detects
accidental drift, not a privileged actor able to rewrite Vault state. Never
publish `vault-init.txt`, the `approle/` directory, or any `secrets/` file.
