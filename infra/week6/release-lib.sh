#!/usr/bin/env bash
# This sourced library receives its controller paths from deploy/rollback.
# shellcheck disable=SC2154

# Shared operations for deploy.sh and rollback.sh. Callers deliberately own
# error handling so a failed post-cutover gate can restore the prior include.

release_compose() {
  docker compose \
    --project-name nsa-week6 \
    --env-file "$release_environment" \
    --file "$compose_file" \
    "$@"
}

release_require_command() {
  command -v "$1" >/dev/null 2>&1 || {
    echo "Required command is unavailable: $1" >&2
    return 1
  }
}

release_require_platform() {
  for command in docker node curl find sed; do release_require_command "$command"; done
  docker info >/dev/null 2>&1 || {
    echo "Docker Desktop's Linux engine is not running. Start it before release work." >&2
    return 1
  }
  docker compose version >/dev/null
  docker network inspect nsa-week5-backend >/dev/null 2>&1 || {
    echo "The Week 5 backend network does not exist. Run infra/week5/provision.sh first." >&2
    return 1
  }
  docker network inspect nsa-week5-edge >/dev/null 2>&1 || {
    echo "The Week 5 edge network does not exist. Run infra/week5/provision.sh first." >&2
    return 1
  }
  for secret in \
    "$week5_runtime/secrets/api/app_db_connection" \
    "$week5_runtime/secrets/migrator/migrator_db_connection" \
    "$week5_runtime/secrets/api/jwt_signing_key"; do
    [[ -s "$secret" ]] || {
      echo "Required Vault-rendered secret is missing: $secret" >&2
      return 1
    }
  done
}

release_prepare_evidence_directory() {
  local root=$1
  local namespace=${RELEASE_EVIDENCE_NAMESPACE:-}
  if [[ -n "$namespace" && ! "$namespace" =~ ^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$ ]]; then
    echo "RELEASE_EVIDENCE_NAMESPACE must be a safe 1-128 character label." >&2
    return 1
  fi

  evidence_dir=$root
  if [[ -n "$namespace" ]]; then
    evidence_dir="$root/$namespace"
    if [[ -d "$evidence_dir" ]] \
      && [[ -n "$(find "$evidence_dir" -mindepth 1 -maxdepth 1 -print -quit)" ]]; then
      echo "Run-scoped evidence directory is not empty: $evidence_dir" >&2
      return 1
    fi
  fi
  mkdir -p -- "$evidence_dir"
  chmod 700 "$evidence_dir" 2>/dev/null || true
}

release_acquire_lock() {
  lock_dir="$runtime_dir/deploy.lock"
  if ! mkdir "$lock_dir" 2>/dev/null; then
    echo "Another deployment or rollback owns $lock_dir." >&2
    echo "Wait for it to finish; remove the directory only after proving the prior process ended." >&2
    return 1
  fi
}

release_wait_for_health() {
  local service=$1
  local timeout_seconds=${2:-120}
  local started_at=$SECONDS
  local container_id status
  while (( SECONDS - started_at < timeout_seconds )); do
    container_id=$(release_compose ps --quiet "$service")
    if [[ -n "$container_id" ]]; then
      status=$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' "$container_id")
      [[ "$status" == healthy ]] && return 0
      [[ "$status" == exited || "$status" == dead || "$status" == unhealthy ]] && break
    fi
    sleep 2
  done
  echo "Release service '$service' did not become healthy within ${timeout_seconds}s." >&2
  release_compose logs --tail 100 "$service" >&2 || true
  return 1
}

release_ensure_image() {
  local reference=$1
  if [[ "$reference" == sha256:* ]]; then
    docker image inspect "$reference" >/dev/null 2>&1 || {
      echo "Local content-addressed image is unavailable: $reference" >&2
      return 1
    }
  else
    docker pull "$reference"
  fi
}

release_write_upstream_candidate() {
  local active_color=$1
  local active_sha=$2
  local previous_color=${3:-}
  [[ "$active_color" == blue || "$active_color" == green ]] || return 1
  [[ "$active_sha" =~ ^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$ ]] || return 1
  [[ -z "$previous_color" || "$previous_color" == blue || "$previous_color" == green ]] || return 1

  {
    printf 'map $host $nsa_release_color { default "%s"; }\n' "$active_color"
    printf 'map $host $nsa_release_sha { default "%s"; }\n\n' "$active_sha"
    printf 'upstream nsa_active_web {\n'
    printf '    server next-%s:3000 max_fails=3 fail_timeout=10s;\n' "$active_color"
    printf '    keepalive 32;\n}\n\n'
    printf 'upstream nsa_static_assets {\n'
    printf '    server next-%s:3000 max_fails=2 fail_timeout=5s;\n' "$active_color"
    if [[ -n "$previous_color" && "$previous_color" != "$active_color" ]]; then
      printf '    server next-%s:3000 backup max_fails=2 fail_timeout=5s;\n' "$previous_color"
    fi
    printf '    keepalive 16;\n}\n'
  } > "$candidate_include"
  chmod 600 "$candidate_include" 2>/dev/null || true
}

release_validate_candidate() {
  local validation_config="$nginx_runtime/nginx-candidate.conf"
  sed 's|include /etc/nginx/release/upstream-active.conf;|include /etc/nginx/release/upstream-candidate.conf;|' \
    "$script_dir/nginx/nginx.conf" > "$validation_config"
  grep -q 'upstream-candidate.conf' "$validation_config" || {
    echo "Could not generate the isolated Nginx candidate configuration." >&2
    return 1
  }
  # The doubled leading slash is equivalent inside Linux containers and keeps
  # MSYS/Git Bash from rewriting the container path into a Windows host path.
  release_compose run --rm --no-deps nginx nginx -t -c //etc/nginx/release/nginx-candidate.conf
}

release_switch_to_candidate() {
  mv -f -- "$candidate_include" "$active_include"
  if [[ -n "$(release_compose ps --quiet nginx)" ]]; then
    release_compose exec -T nginx nginx -t
    release_compose exec -T nginx nginx -s reload
  else
    release_compose up --detach --no-deps nginx
    release_wait_for_health nginx 60
  fi
}

release_restore_prior_include() {
  if [[ -s "$prior_include" ]]; then
    cp -- "$prior_include" "$candidate_include"
    mv -f -- "$candidate_include" "$active_include"
    release_compose exec -T nginx nginx -t >/dev/null
    release_compose exec -T nginx nginx -s reload
  else
    release_compose stop nginx >/dev/null 2>&1 || true
  fi
}

release_run_idle_smoke() {
  local color=$1
  local sha=$2
  local token_source_color=${3:-}
  local token_source_url="http://api-${color}:8080"
  if [[ -n "$token_source_color" ]]; then
    token_source_url="http://api-${token_source_color}:8080"
  fi
  release_compose exec -T \
    -e NEXT_URL=http://127.0.0.1:3000 \
    -e "API_URL=http://api-${color}:8080" \
    -e "TOKEN_SOURCE_API_URL=$token_source_url" \
    -e "EXPECTED_COLOR=$color" \
    -e "EXPECTED_SHA=$sha" \
    "next-${color}" node - < "$script_dir/smoke-idle.mjs"
}

release_run_cutover_probe() {
  local from_color=$1
  local from_sha=$2
  local to_color=$3
  local to_sha=$4
  local duration_milliseconds=$5
  local output_file=$6
  node "$script_dir/cutover-probe.mjs" \
    http://127.0.0.1:8088 "$from_color" "$from_sha" "$to_color" "$to_sha" "$duration_milliseconds" \
    > "$output_file"
}
