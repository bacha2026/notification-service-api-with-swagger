#!/usr/bin/env bash
# release-lib.sh intentionally consumes and assigns these controller globals.
# shellcheck disable=SC2034,SC2154
set -Eeuo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)
runtime_dir="$script_dir/.runtime"
week5_runtime="$script_dir/../week5/.runtime"
compose_file="$script_dir/compose.yml"
release_environment="$runtime_dir/release.env"
state_file="$runtime_dir/state.json"
nginx_runtime="$runtime_dir/nginx"
active_include="$nginx_runtime/upstream-active.conf"
candidate_include="$nginx_runtime/upstream-candidate.conf"
prior_include="$nginx_runtime/upstream-prior.conf"
evidence_root="$runtime_dir/evidence"
manifest_archive="$runtime_dir/manifests"
control="$script_dir/release-control.mjs"
manifest=${1:-}
observe_seconds=${OBSERVE_SECONDS:-10}
cutover_probe_milliseconds=${CUTOVER_PROBE_MILLISECONDS:-5000}

if [[ -z "$manifest" || "$manifest" == --help || "$manifest" == -h ]]; then
  cat <<'USAGE'
Usage: bash infra/week6/deploy.sh RELEASE_MANIFEST.json

Promotes the exact API/Next/migrator digests in a trusted manifest to the idle
color. Week 5 must already be provisioned. The former color remains warm for
the rollback window; this script never rebuilds an application image.
USAGE
  [[ -n "$manifest" ]] && exit 0 || exit 2
fi
[[ "$observe_seconds" =~ ^[0-9]+$ ]] && (( observe_seconds <= 120 )) || {
  echo "OBSERVE_SECONDS must be an integer from 0 through 120." >&2
  exit 2
}
[[ "$cutover_probe_milliseconds" =~ ^[0-9]+$ ]] \
  && (( cutover_probe_milliseconds >= 1000 && cutover_probe_milliseconds <= 120000 )) || {
  echo "CUTOVER_PROBE_MILLISECONDS must be an integer from 1000 through 120000." >&2
  exit 2
}
manifest=$(cd -- "$(dirname -- "$manifest")" && pwd -P)/$(basename -- "$manifest")
[[ -s "$manifest" ]] || { echo "Release manifest not found: $manifest" >&2; exit 2; }

# shellcheck source=release-lib.sh
source "$script_dir/release-lib.sh"
release_require_platform
release_prepare_evidence_directory "$evidence_root"
mkdir -p -- "$runtime_dir" "$nginx_runtime" "$manifest_archive"
chmod 700 "$runtime_dir" "$nginx_runtime" "$manifest_archive" 2>/dev/null || true
release_acquire_lock

traffic_switched=false
committed=false
idle_color=''
probe_pid=''
cleanup() {
  local status=$?
  set +e
  if [[ -n "$probe_pid" ]]; then
    kill "$probe_pid" >/dev/null 2>&1 || true
    wait "$probe_pid" >/dev/null 2>&1 || true
  fi
  if (( status != 0 )) && $traffic_switched && ! $committed; then
    echo "A post-switch gate failed; restoring the prior Nginx release." >&2
    release_restore_prior_include
  fi
  if (( status != 0 )) && [[ -n "$idle_color" ]] && ! $committed; then
    release_compose logs --no-color --tail 120 "api-$idle_color" "next-$idle_color" \
      > "$evidence_dir/failed-candidate-$(date -u +%Y%m%dT%H%M%SZ).log" 2>&1 || true
    release_compose stop "next-$idle_color" "api-$idle_color" >/dev/null 2>&1 || true
  fi
  rmdir "$lock_dir" 2>/dev/null || true
  exit "$status"
}
trap cleanup EXIT

mapfile -t manifest_fields < <(node "$control" inspect-manifest "$manifest")
revision=${manifest_fields[0]}
api_image=${manifest_fields[1]}
next_image=${manifest_fields[2]}
migrator_image=${manifest_fields[3]}
manifest_hash=${manifest_fields[7]}
archived_manifest="$manifest_archive/$manifest_hash.json"
if [[ ! -e "$archived_manifest" ]]; then
  cp -- "$manifest" "$archived_manifest"
  chmod 600 "$archived_manifest" 2>/dev/null || true
fi
mapfile -t state_fields < <(node "$control" inspect-state "$state_file")
initialized=${state_fields[0]}
current_color=${state_fields[1]:-}
current_sha=${state_fields[2]:-}
if [[ "$initialized" == true ]]; then
  [[ "$current_color" == blue ]] && idle_color=green || idle_color=blue
else
  idle_color=blue
fi

node "$control" write-deploy-env "$state_file" "$manifest" "$idle_color" "$release_environment"
release_compose config --quiet

echo "Promoting release $revision to idle color $idle_color from immutable images."
release_ensure_image "$api_image"
release_ensure_image "$next_image"
release_ensure_image "$migrator_image"

echo "Applying compatible database migrations once with the release migrator."
release_compose run --rm --no-deps migrator

release_compose up --detach --no-deps --force-recreate "api-$idle_color"
release_wait_for_health "api-$idle_color" 120
release_compose up --detach --no-deps --force-recreate "next-$idle_color"
release_wait_for_health "next-$idle_color" 120

timestamp=$(date -u +%Y%m%dT%H%M%SZ)
idle_evidence="$evidence_dir/idle-$idle_color-$timestamp.json"
release_run_idle_smoke "$idle_color" "$revision" "$current_color" > "$idle_evidence"

if [[ -s "$active_include" ]]; then
  cp -- "$active_include" "$prior_include"
else
  rm -f -- "$prior_include"
fi
release_write_upstream_candidate "$idle_color" "$revision" "$current_color"
release_validate_candidate

cutover_evidence="$evidence_dir/cutover-$current_color-to-$idle_color-$timestamp.json"
if [[ "$initialized" == true ]]; then
  release_run_cutover_probe "$current_color" "$current_sha" "$idle_color" "$revision" \
    "$cutover_probe_milliseconds" "$cutover_evidence" &
  probe_pid=$!
  sleep 0.5
  traffic_switched=true
  release_switch_to_candidate
  wait "$probe_pid"
  probe_pid=''
else
  traffic_switched=true
  release_switch_to_candidate
  release_run_cutover_probe "" "" "$idle_color" "$revision" 1500 "$cutover_evidence"
fi

if (( observe_seconds > 0 )); then
  release_run_cutover_probe "" "" "$idle_color" "$revision" "$((observe_seconds * 1000))" \
    "$evidence_dir/observation-$idle_color-$timestamp.json"
fi

node "$control" commit-deploy "$state_file" "$manifest" "$idle_color"
committed=true
traffic_switched=false

echo "Release $revision is active on $idle_color at http://127.0.0.1:8088."
echo "The former color remains warm. Evidence: $evidence_dir"
if [[ "$initialized" == true ]]; then
  echo "One-command rollback: bash infra/week6/rollback.sh"
fi
