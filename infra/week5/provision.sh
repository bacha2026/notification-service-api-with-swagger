#!/usr/bin/env bash
set -Eeuo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)
repo_root=$(cd -- "$script_dir/../.." && pwd -P)
runtime_dir="$script_dir/.runtime"
compose_file="$script_dir/compose.yml"
vault_compose_file="$script_dir/compose.vault.yml"
project_name=nsa-week5
with_angular=false
reset_requested=false

usage() {
  cat <<'USAGE'
Usage: bash infra/week5/provision.sh [--with-angular] [--reset]

  --with-angular  Start the optional Angular OnPush demonstration.
  --reset         Destructively remove only the nsa-week5 containers, named
                  volumes, and generated .runtime directory after confirmation.
USAGE
}

for argument in "$@"; do
  case "$argument" in
    --with-angular) with_angular=true ;;
    --reset) reset_requested=true ;;
    --help|-h) usage; exit 0 ;;
    *) echo "Unknown option: $argument" >&2; usage >&2; exit 2 ;;
  esac
done

compose() {
  docker compose --project-name "$project_name" --file "$compose_file" "$@"
}

vault_compose() {
  COMPOSE_IGNORE_ORPHANS=true \
    docker compose --project-name "$project_name" --file "$vault_compose_file" "$@"
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || { echo "Required command is unavailable: $1" >&2; exit 1; }
}

require_command docker
require_command grep
require_command sed
require_command curl
require_command awk
require_command sha256sum

host_acl_warning=false
acl_script_windows=''
runtime_path_windows=''
case "$(uname -s 2>/dev/null || true)" in
  MINGW*|MSYS*|CYGWIN*) host_acl_warning=true ;;
esac
if [[ -n "${WSL_DISTRO_NAME:-}" ]]; then
  wsl_filesystem=$(stat -f -c %T "$script_dir" 2>/dev/null || true)
  case "$wsl_filesystem" in
    9p|drvfs|fuseblk|ntfs|ntfs3) host_acl_warning=true ;;
  esac
  if [[ "$runtime_dir" == /mnt/?/* ]]; then
    host_acl_warning=true
  fi
fi
if $host_acl_warning; then
  echo "This Windows-mounted path requires protected NTFS ACLs; provisioning will enforce and verify them." >&2
fi

windows_path() {
  local path=$1
  if [[ -n "${WSL_DISTRO_NAME:-}" ]] && command -v wslpath >/dev/null 2>&1; then
    wslpath -w "$path"
  elif command -v cygpath >/dev/null 2>&1; then
    cygpath -w "$path"
  else
    printf '%s' "$path"
  fi
}

protect_runtime_acl() {
  $host_acl_warning || return 0
  require_command powershell.exe
  # Resolve the WSL/MSYS paths once. Docker Desktop can briefly refresh its
  # WSL bind-mount bridge while Compose recreates containers; asking wslpath
  # to resolve the same path again during that window can otherwise turn a
  # successful provision into a false final-ACL failure.
  if [[ -z "$acl_script_windows" || -z "$runtime_path_windows" ]]; then
    acl_script_windows=$(windows_path "$script_dir/Protect-Week5RuntimeAcl.ps1")
    runtime_path_windows=$(windows_path "$runtime_dir")
  fi
  powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass \
    -File "$acl_script_windows" -RuntimePath "$runtime_path_windows"
}

docker info >/dev/null 2>&1 || {
  echo "Docker Desktop's Linux engine is not running. Start it and rerun provisioning." >&2
  exit 1
}
docker compose version >/dev/null

if $reset_requested; then
  expected_runtime="$repo_root/infra/week5/.runtime"
  if [[ "$runtime_dir" != "$expected_runtime" ]]; then
    echo "Refusing reset because the resolved runtime path is unexpected: $runtime_dir" >&2
    exit 1
  fi

  echo "This removes only nsa-week5 containers, the three nsa-week5 data volumes, and:"
  echo "  $runtime_dir"
  read -r -p "Type 'RESET nsa-week5' to continue: " confirmation
  [[ "$confirmation" == "RESET nsa-week5" ]] || { echo "Reset cancelled."; exit 1; }

  compose down --volumes --remove-orphans || true
  vault_compose down --volumes --remove-orphans || true
  rm -rf -- "$runtime_dir"
  echo "nsa-week5 local state was removed and cannot be recovered by this script."
  exit 0
fi

mkdir -p -- "$runtime_dir"
if $host_acl_warning; then
  protect_runtime_acl
else
  chmod 700 "$runtime_dir"
fi
lock_dir="$runtime_dir/provision.lock"
if ! mkdir "$lock_dir" 2>/dev/null; then
  echo "Another provisioning run owns $lock_dir. Wait for it or verify that the prior process ended." >&2
  exit 1
fi
cleanup_lock() { rmdir "$lock_dir" 2>/dev/null || true; }
trap cleanup_lock EXIT

check_port() {
  local port=$1
  local owner
  owner=$(docker ps --format '{{.Names}} {{.Ports}}' | grep "127.0.0.1:${port}->" || true)
  if [[ -n "$owner" && "$owner" != *"$project_name"* ]]; then
    echo "Loopback port $port is already owned by another container: $owner" >&2
    exit 1
  fi
}

for port in 3000 8080 8200 14330 15672 56720; do check_port "$port"; done
if $with_angular; then check_port 4200; fi

sql_volume_exists=false
rabbit_volume_exists=false
vault_volume_exists=false
docker volume inspect nsa-week5-sqlserver-data >/dev/null 2>&1 && sql_volume_exists=true
docker volume inspect nsa-week5-rabbitmq-data >/dev/null 2>&1 && rabbit_volume_exists=true
docker volume inspect nsa-week5-vault-data >/dev/null 2>&1 && vault_volume_exists=true
if { $sql_volume_exists || $rabbit_volume_exists; } && { ! $vault_volume_exists || [[ ! -s "$runtime_dir/vault-init.txt" ]]; }; then
  echo "Persisted SQL Server or RabbitMQ state exists without recoverable Vault bootstrap material." >&2
  echo "Restore infra/week5/.runtime and the matching Vault volume, or use the explicit confirmed --reset path." >&2
  exit 1
fi

vault_compose config --quiet
vault_compose up --detach vault

vault_status=''
for _ in $(seq 1 30); do
  vault_status=$(vault_compose exec -T vault vault status -address=http://127.0.0.1:8200 -format=json 2>/dev/null || true)
  [[ -n "$vault_status" ]] && break
  sleep 1
done
[[ -n "$vault_status" ]] || { echo "Vault did not become reachable within 30 seconds." >&2; exit 1; }

init_file="$runtime_dir/vault-init.txt"
if grep -Eq '"initialized"[[:space:]]*:[[:space:]]*false' <<<"$vault_status"; then
  [[ ! -e "$init_file" ]] || { echo "Vault is uninitialized but bootstrap material already exists; refusing to overwrite it." >&2; exit 1; }
  umask 077
  vault_compose exec -T vault vault operator init \
    -address=http://127.0.0.1:8200 \
    -key-shares=1 \
    -key-threshold=1 > "$init_file"
  chmod 600 "$init_file" 2>/dev/null || true
elif [[ ! -s "$init_file" ]]; then
  echo "Vault storage is initialized but $init_file is missing." >&2
  echo "Recover the original unseal/root material; credentials must not be silently regenerated." >&2
  exit 1
fi

unseal_key=$(sed -n 's/^Unseal Key 1: //p' "$init_file")
root_token=$(sed -n 's/^Initial Root Token: //p' "$init_file")
[[ -n "$unseal_key" && -n "$root_token" ]] || { echo "Vault bootstrap material is incomplete." >&2; exit 1; }

vault_status=$(vault_compose exec -T vault vault status -address=http://127.0.0.1:8200 -format=json 2>/dev/null || true)
if grep -Eq '"sealed"[[:space:]]*:[[:space:]]*true' <<<"$vault_status"; then
  vault_compose exec -T vault vault operator unseal \
    -address=http://127.0.0.1:8200 "$unseal_key" >/dev/null
fi

vault_admin() {
  vault_compose exec -T \
    -e VAULT_ADDR=http://127.0.0.1:8200 \
    -e VAULT_TOKEN="$root_token" \
    vault vault "$@"
}

if ! vault_admin secrets list -format=json | grep -q '"secret/"'; then
  vault_admin secrets enable -path=secret kv-v2 >/dev/null
fi
if ! vault_admin auth list -format=json | grep -q '"approle/"'; then
  vault_admin auth enable approle >/dev/null
fi

vault_admin policy write nsa-api - < "$script_dir/vault/policies/api.hcl" >/dev/null
vault_admin policy write nsa-worker - < "$script_dir/vault/policies/worker.hcl" >/dev/null
vault_admin policy write nsa-migrator - < "$script_dir/vault/policies/migrator.hcl" >/dev/null

for role in api worker migrator; do
  vault_admin write "auth/approle/role/nsa-$role" \
    token_policies="nsa-$role" token_ttl=1h token_max_ttl=4h secret_id_ttl=0 >/dev/null
  role_dir="$runtime_dir/approle/$role"
  mkdir -p -- "$role_dir"
  chmod 700 "$role_dir" 2>/dev/null || true
  vault_admin read -field=role_id "auth/approle/role/nsa-$role/role-id" > "$role_dir/role_id"
  if [[ ! -s "$role_dir/secret_id" ]]; then
    vault_admin write -f -field=secret_id "auth/approle/role/nsa-$role/secret-id" > "$role_dir/secret_id"
  fi
  chmod 600 "$role_dir/role_id" "$role_dir/secret_id" 2>/dev/null || true
done

vault_approle_login() {
  local role=$1
  local role_id secret_id
  role_id=$(tr -d '\r\n' < "$runtime_dir/approle/$role/role_id")
  secret_id=$(tr -d '\r\n' < "$runtime_dir/approle/$role/secret_id")
  vault_compose exec -T \
    -e VAULT_ADDR=http://127.0.0.1:8200 \
    vault vault write -field=token auth/approle/login \
      "role_id=$role_id" "secret_id=$secret_id" | tr -d '\r\n'
}

vault_as_role() {
  local token=$1
  shift
  vault_compose exec -T \
    -e VAULT_ADDR=http://127.0.0.1:8200 \
    -e VAULT_TOKEN="$token" \
    vault vault "$@"
}

random_value() {
  vault_admin write -field=random_bytes sys/tools/random/48 format=base64 | tr -d '\r\n'
}

platform_exists=false
vault_admin kv get secret/nsa/platform >/dev/null 2>&1 && platform_exists=true

provision_state_exists=false
provision_state_lifecycle=''
provision_state_fingerprint=''
if vault_admin kv get secret/nsa/provisioning-state >/dev/null 2>&1; then
  provision_state_exists=true
  provision_state_lifecycle=$(vault_admin kv get -field=lifecycle secret/nsa/provisioning-state | tr -d '\r\n')
  case "$provision_state_lifecycle" in
    generating) ;;
    pending|established)
      provision_state_fingerprint=$(vault_admin kv get -field=fingerprint_sha256 secret/nsa/provisioning-state | tr -d '\r\n')
      [[ "$provision_state_fingerprint" =~ ^[a-f0-9]{64}$ ]] || {
        echo "Vault provisioning state contains an invalid fingerprint; refusing to re-establish trust." >&2
        exit 1
      }
      ;;
    *)
      echo "Vault provisioning state has an unsupported lifecycle; refusing to continue." >&2
      exit 1
      ;;
  esac
fi

if ! $platform_exists; then
  if $provision_state_exists && [[ "$provision_state_lifecycle" != generating ]]; then
    echo "The canonical secret set is missing but an established Vault fingerprint remains; refusing regeneration." >&2
    exit 1
  fi
  if ! $provision_state_exists; then
    vault_admin kv put secret/nsa/provisioning-state \
      lifecycle=generating \
      created_at_utc="$(date -u +%Y-%m-%dT%H:%M:%SZ)" >/dev/null
    provision_state_exists=true
    provision_state_lifecycle=generating
  fi
  vault_admin kv put secret/nsa/platform bootstrap_marker=managed-by-provision-sh >/dev/null
fi

baseline_expected=false
legacy_fingerprint=''
legacy_fingerprint_file="$runtime_dir/secret-set.sha256"
if [[ "$provision_state_lifecycle" == pending || "$provision_state_lifecycle" == established ]]; then
  baseline_expected=true
elif [[ -z "$provision_state_lifecycle" ]]; then
  # One-time migration from provisioner versions that stored the baseline only
  # on the host. Require a matching successful evidence record so a failed
  # first run cannot be promoted silently.
  [[ -s "$legacy_fingerprint_file" ]] || {
    echo "Vault already contains a secret set but its drift baseline is missing." >&2
    echo "Restore matching provisioning state or use the explicit confirmed --reset path." >&2
    exit 1
  }
  legacy_fingerprint=$(tr -d '\r\n' < "$legacy_fingerprint_file")
  [[ "$legacy_fingerprint" =~ ^[a-f0-9]{64}$ ]] || {
    echo "The legacy secret-set fingerprint is invalid; refusing migration." >&2
    exit 1
  }
  legacy_evidence_valid=false
  for prior_evidence in "$runtime_dir"/evidence/provision-*.txt; do
    [[ -f "$prior_evidence" ]] || continue
    if grep -Fqx "secret_set_fingerprint_sha256=$legacy_fingerprint" "$prior_evidence" \
      && grep -Fqx 'secret_set_fingerprint_status=stable' "$prior_evidence"; then
      legacy_evidence_valid=true
      break
    fi
  done
  $legacy_evidence_valid || {
    echo "The legacy fingerprint has no matching successful stable-run evidence; refusing migration." >&2
    exit 1
  }
  baseline_expected=true
fi

validate_managed_value() {
  local field=$1
  local prefix=$2
  local value=$3
  local random_suffix
  [[ "$value" == "$prefix"* ]] || {
    echo "Vault field '$field' is not in the provisioner-managed format." >&2
    exit 1
  }
  random_suffix=${value#"$prefix"}
  [[ "$random_suffix" =~ ^[A-Za-z0-9+/]{64}$ ]] || {
    echo "Vault field '$field' does not contain the expected 384-bit generated suffix." >&2
    exit 1
  }
}

ensure_field() {
  local field=$1
  local prefix=$2
  local value
  if ! value=$(vault_admin kv get -field="$field" secret/nsa/platform 2>/dev/null); then
    if $baseline_expected; then
      echo "Vault field '$field' is missing from a fingerprinted secret set; refusing regeneration." >&2
      exit 1
    fi
    value="${prefix}$(random_value)"
    vault_admin kv patch secret/nsa/platform "$field=$value" >/dev/null
  fi
  validate_managed_value "$field" "$prefix" "$value"
  printf '%s' "$value"
}

sql_sa_password=$(ensure_field sql_sa_password 'Nsa!1')
sql_app_password=$(ensure_field sql_app_password 'Nsa!1')
sql_migrator_password=$(ensure_field sql_migrator_password 'Nsa!1')
rabbitmq_username=$(ensure_field rabbitmq_username 'nsa_service_')
rabbitmq_password=$(ensure_field rabbitmq_password 'Nsa!1')
jwt_signing_key=$(ensure_field jwt_signing_key 'nsa-jwt-')

secret_fingerprint=$(
  {
    printf 'sql_sa_password\0%s\0' "$sql_sa_password"
    printf 'sql_app_password\0%s\0' "$sql_app_password"
    printf 'sql_migrator_password\0%s\0' "$sql_migrator_password"
    printf 'rabbitmq_username\0%s\0' "$rabbitmq_username"
    printf 'rabbitmq_password\0%s\0' "$rabbitmq_password"
    printf 'jwt_signing_key\0%s\0' "$jwt_signing_key"
  } | sha256sum | awk '{print $1}'
)
[[ "$secret_fingerprint" =~ ^[a-f0-9]{64}$ ]] || {
  echo "Could not calculate the generated secret-set fingerprint." >&2
  exit 1
}
secret_fingerprint_status=initialized
if [[ -n "$provision_state_fingerprint" ]]; then
  [[ "$provision_state_fingerprint" == "$secret_fingerprint" ]] || {
    echo "Persisted Vault values changed unexpectedly from the authoritative provisioning baseline." >&2
    exit 1
  }
  [[ "$provision_state_lifecycle" == established ]] && secret_fingerprint_status=stable
elif [[ -n "$legacy_fingerprint" ]]; then
  [[ "$legacy_fingerprint" == "$secret_fingerprint" ]] || {
    echo "Persisted Vault values differ from the verified legacy baseline; refusing migration." >&2
    exit 1
  }
  vault_admin kv put secret/nsa/provisioning-state \
    lifecycle=established \
    fingerprint_sha256="$secret_fingerprint" \
    migrated_at_utc="$(date -u +%Y-%m-%dT%H:%M:%SZ)" >/dev/null
  secret_fingerprint_status=stable
else
  vault_admin kv patch secret/nsa/provisioning-state \
    lifecycle=pending \
    fingerprint_sha256="$secret_fingerprint" >/dev/null
fi

app_db_connection="Server=sqlserver,1433;Database=NotificationServiceDb;User Id=nsa_app;Password=$sql_app_password;Encrypt=False;TrustServerCertificate=True;MultipleActiveResultSets=true;Connect Timeout=5;ConnectRetryCount=1"
migrator_db_connection="Server=sqlserver,1433;Database=NotificationServiceDb;User Id=nsa_migrator;Password=$sql_migrator_password;Encrypt=False;TrustServerCertificate=True;MultipleActiveResultSets=true;Connect Timeout=5;ConnectRetryCount=1"

# Root/operator bootstrap owns the platform source values. Each delivery path
# below is split into a role-specific KV document, then read back only through
# that service's AppRole token before a Docker secret file is rendered.
vault_admin kv put secret/nsa/api \
  app_db_connection="$app_db_connection" \
  jwt_signing_key="$jwt_signing_key" >/dev/null
vault_admin kv put secret/nsa/worker \
  app_db_connection="$app_db_connection" \
  rabbitmq_username="$rabbitmq_username" \
  rabbitmq_password="$rabbitmq_password" >/dev/null
vault_admin kv put secret/nsa/migrator \
  migrator_db_connection="$migrator_db_connection" >/dev/null

api_token=$(vault_approle_login api)
worker_token=$(vault_approle_login worker)
migrator_token=$(vault_approle_login migrator)

api_app_db_connection=$(vault_as_role "$api_token" kv get -field=app_db_connection secret/nsa/api | tr -d '\r\n')
api_jwt_signing_key=$(vault_as_role "$api_token" kv get -field=jwt_signing_key secret/nsa/api | tr -d '\r\n')
worker_app_db_connection=$(vault_as_role "$worker_token" kv get -field=app_db_connection secret/nsa/worker | tr -d '\r\n')
worker_rabbitmq_username=$(vault_as_role "$worker_token" kv get -field=rabbitmq_username secret/nsa/worker | tr -d '\r\n')
worker_rabbitmq_password=$(vault_as_role "$worker_token" kv get -field=rabbitmq_password secret/nsa/worker | tr -d '\r\n')
role_migrator_db_connection=$(vault_as_role "$migrator_token" kv get -field=migrator_db_connection secret/nsa/migrator | tr -d '\r\n')

if vault_as_role "$api_token" kv get secret/nsa/worker >/dev/null 2>&1 \
  || vault_as_role "$worker_token" kv get secret/nsa/api >/dev/null 2>&1 \
  || vault_as_role "$migrator_token" kv get secret/nsa/api >/dev/null 2>&1 \
  || vault_as_role "$api_token" kv get secret/nsa/provisioning-state >/dev/null 2>&1 \
  || vault_as_role "$worker_token" kv get secret/nsa/provisioning-state >/dev/null 2>&1 \
  || vault_as_role "$migrator_token" kv get secret/nsa/provisioning-state >/dev/null 2>&1; then
  echo "A service AppRole can read outside its declared Vault path." >&2
  exit 1
fi

platform_dir="$runtime_dir/secrets/platform"
api_dir="$runtime_dir/secrets/api"
worker_dir="$runtime_dir/secrets/worker"
migrator_dir="$runtime_dir/secrets/migrator"
mkdir -p -- "$platform_dir" "$api_dir" "$worker_dir" "$migrator_dir"
chmod 700 "$runtime_dir/secrets" "$platform_dir" "$api_dir" "$worker_dir" "$migrator_dir" 2>/dev/null || true

write_secret() {
  local path=$1
  local value=$2
  umask 077
  printf '%s' "$value" > "$path"
  chmod 600 "$path" 2>/dev/null || true
}

write_secret "$platform_dir/sql_sa_password" "$sql_sa_password"
write_secret "$platform_dir/sql_app_password" "$sql_app_password"
write_secret "$platform_dir/sql_migrator_password" "$sql_migrator_password"
write_secret "$api_dir/app_db_connection" "$api_app_db_connection"
write_secret "$api_dir/jwt_signing_key" "$api_jwt_signing_key"
write_secret "$worker_dir/app_db_connection" "$worker_app_db_connection"
write_secret "$worker_dir/rabbitmq_username" "$worker_rabbitmq_username"
write_secret "$worker_dir/rabbitmq_password" "$worker_rabbitmq_password"
write_secret "$migrator_dir/migrator_db_connection" "$role_migrator_db_connection"
write_secret "$platform_dir/rabbitmq.conf" "default_user = $rabbitmq_username
default_pass = $rabbitmq_password
default_vhost = /
listeners.tcp.default = 5672
management.tcp.port = 15672
loopback_users.guest = true"

# Migrate the pre-AppRole runtime layout only after every replacement file is
# safely rendered. Delete exactly the five known generated duplicates and
# refuse any broader or unexpected target.
legacy_application_dir="$runtime_dir/secrets/application"
expected_legacy_application_dir="$repo_root/infra/week5/.runtime/secrets/application"
if [[ -d "$legacy_application_dir" ]]; then
  resolved_legacy_application_dir=$(cd -- "$legacy_application_dir" && pwd -P)
  [[ "$resolved_legacy_application_dir" == "$expected_legacy_application_dir" ]] || {
    echo "Refusing legacy-secret cleanup at unexpected path: $resolved_legacy_application_dir" >&2
    exit 1
  }
  legacy_secret_names=(
    app_db_connection jwt_signing_key migrator_db_connection
    rabbitmq_password rabbitmq_username
  )
  shopt -s nullglob dotglob
  legacy_entries=("$legacy_application_dir"/*)
  shopt -u nullglob dotglob
  [[ ${#legacy_entries[@]} -eq ${#legacy_secret_names[@]} ]] || {
    echo "Refusing legacy-secret cleanup because the directory has unexpected entries." >&2
    exit 1
  }
  for legacy_secret_name in "${legacy_secret_names[@]}"; do
    [[ -f "$legacy_application_dir/$legacy_secret_name" ]] || {
      echo "Refusing legacy-secret cleanup because an expected file is missing." >&2
      exit 1
    }
  done
  for legacy_secret_name in "${legacy_secret_names[@]}"; do
    rm -f -- "$legacy_application_dir/$legacy_secret_name"
  done
  rmdir -- "$legacy_application_dir"
  echo "Removed the obsolete five-file pre-AppRole secret layout after verified migration."
fi

export RELEASE_SHA
RELEASE_SHA=$(git -C "$repo_root" rev-parse HEAD 2>/dev/null || printf 'local-week5')
compose config --quiet
compose build
compose up --detach vault sqlserver rabbitmq

wait_for_health() {
  local service=$1
  local timeout_seconds=${2:-180}
  local started_at=$SECONDS
  local container_id status
  while (( SECONDS - started_at < timeout_seconds )); do
    container_id=$(compose ps --quiet "$service")
    if [[ -n "$container_id" ]]; then
      status=$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' "$container_id")
      [[ "$status" == healthy || "$status" == running ]] && return 0
      [[ "$status" == exited || "$status" == dead ]] && break
    fi
    sleep 2
  done
  echo "Service '$service' did not become healthy within ${timeout_seconds}s." >&2
  compose logs --tail 80 "$service" >&2 || true
  return 1
}

wait_for_health vault 60
wait_for_health sqlserver 180
wait_for_health rabbitmq 120

compose run --rm --no-deps database-bootstrap
compose run --rm --no-deps migrator
# The two one-shot commands above are the ordered migration gate. Avoid asking
# Compose to recreate them implicitly as dependencies after their --rm cleanup.
compose up --detach --no-deps api
wait_for_health api 120
compose up --detach --no-deps outbox-relay worker next
wait_for_health outbox-relay 120
wait_for_health worker 120
wait_for_health next 120

if $with_angular; then
  compose --profile angular up --detach angular
  wait_for_health angular 60
fi

bash "$script_dir/smoke.sh"

# Only a complete health/migration/smoke pass promotes a pending fingerprint to
# the authoritative baseline for subsequent runs.
vault_admin kv patch secret/nsa/provisioning-state \
  lifecycle=established \
  fingerprint_sha256="$secret_fingerprint" \
  last_success_at_utc="$(date -u +%Y-%m-%dT%H:%M:%SZ)" >/dev/null
if [[ -n "$legacy_fingerprint" ]]; then
  rm -f -- "$legacy_fingerprint_file"
fi

evidence_dir="$runtime_dir/evidence"
mkdir -p -- "$evidence_dir"
evidence_file="$evidence_dir/provision-$(date -u +%Y%m%dT%H%M%SZ).txt"
{
  echo "release_sha=$RELEASE_SHA"
  echo "provisioned_at_utc=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "vault_approle_paths=api:secret/nsa/api,worker:secret/nsa/worker,migrator:secret/nsa/migrator"
  echo "vault_cross_role_reads=denied"
  echo "secret_set_fingerprint_status=$secret_fingerprint_status"
  for service in vault sqlserver rabbitmq api outbox-relay worker next; do
    container_id=$(compose ps --quiet "$service")
    image_ref=$(docker inspect --format '{{.Config.Image}}' "$container_id")
    image_id=$(docker inspect --format '{{.Image}}' "$container_id")
    echo "$service image=$image_ref id=$image_id"
  done
} > "$evidence_file"

protect_runtime_acl

echo "Week 5 stack is healthy. Web: http://127.0.0.1:3000  API: http://127.0.0.1:8080/swagger"
echo "Non-secret provisioning evidence: $evidence_file"
