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
control="$script_dir/release-control.mjs"
manifest=${1:-}
observe_seconds=${OBSERVE_SECONDS:-5}
cutover_probe_milliseconds=${CUTOVER_PROBE_MILLISECONDS:-5000}

if [[ "$manifest" == --help || "$manifest" == -h ]]; then
  cat <<'USAGE'
Usage: bash infra/week6/rollback.sh [EXACT_PREVIOUS_RELEASE_MANIFEST.json]

Restores the exact prior API/Next digests recorded at the last cutover. It
performs no image build, database migration, volume operation, or down migration.
With no argument, the command uses the content-addressed copy archived by the
successful deployment. An explicit file is accepted only if it exactly matches
the prior manifest recorded in state.json.
USAGE
  exit 0
fi
[[ "$observe_seconds" =~ ^[0-9]+$ ]] && (( observe_seconds <= 120 )) || exit 2
[[ "$cutover_probe_milliseconds" =~ ^[0-9]+$ ]] \
  && (( cutover_probe_milliseconds >= 1000 && cutover_probe_milliseconds <= 120000 )) || exit 2
# shellcheck source=release-lib.sh
source "$script_dir/release-lib.sh"
release_require_platform
release_prepare_evidence_directory "$evidence_root"
mkdir -p -- "$nginx_runtime"
release_acquire_lock

traffic_switched=false
committed=false
rollback_color=''
probe_pid=''
cleanup() {
  local status=$?
  set +e
  if [[ -n "$probe_pid" ]]; then
    kill "$probe_pid" >/dev/null 2>&1 || true
    wait "$probe_pid" >/dev/null 2>&1 || true
  fi
  if (( status != 0 )) && $traffic_switched && ! $committed; then
    echo "Rollback verification failed; restoring the release that was active when rollback began." >&2
    release_restore_prior_include
  fi
  rmdir "$lock_dir" 2>/dev/null || true
  exit "$status"
}
trap cleanup EXIT

mapfile -t state_fields < <(node "$control" inspect-state "$state_file")
[[ "${state_fields[0]}" == true ]] || { echo "No deployed release state exists." >&2; exit 1; }
current_color=${state_fields[1]}
current_sha=${state_fields[2]}
rollback_color=${state_fields[3]}
rollback_sha=${state_fields[4]}
[[ -n "$rollback_color" && -n "$rollback_sha" ]] || { echo "No verified prior release is recorded." >&2; exit 1; }

if [[ -z "$manifest" ]]; then
  previous_manifest_hash=$(node "$control" previous-manifest-hash "$state_file")
  manifest="$runtime_dir/manifests/$previous_manifest_hash.json"
else
  manifest=$(cd -- "$(dirname -- "$manifest")" && pwd -P)/$(basename -- "$manifest")
fi
[[ -s "$manifest" ]] || {
  echo "The exact prior manifest is unavailable: $manifest" >&2
  echo "Restore the archived manifest or supply the exact attested file explicitly." >&2
  exit 2
}

# This fails closed unless the supplied file hash and revision exactly match
# state.previous, and also rechecks the compatibility declaration.
node "$control" write-rollback-env "$state_file" "$manifest" "$release_environment"
mapfile -t manifest_fields < <(node "$control" inspect-manifest "$manifest")
api_image=${manifest_fields[1]}
next_image=${manifest_fields[2]}
release_compose config --quiet
release_ensure_image "$api_image"
release_ensure_image "$next_image"

echo "Preparing exact prior release $rollback_sha on $rollback_color; no migration will run."
release_compose up --detach --no-deps --force-recreate "api-$rollback_color"
release_wait_for_health "api-$rollback_color" 120
release_compose up --detach --no-deps --force-recreate "next-$rollback_color"
release_wait_for_health "next-$rollback_color" 120

timestamp=$(date -u +%Y%m%dT%H%M%SZ)
release_run_idle_smoke "$rollback_color" "$rollback_sha" "$current_color" \
  > "$evidence_dir/rollback-idle-$rollback_color-$timestamp.json"

cp -- "$active_include" "$prior_include"
release_write_upstream_candidate "$rollback_color" "$rollback_sha" "$current_color"
release_validate_candidate
release_run_cutover_probe "$current_color" "$current_sha" "$rollback_color" "$rollback_sha" \
  "$cutover_probe_milliseconds" "$evidence_dir/rollback-cutover-$timestamp.json" &
probe_pid=$!
sleep 0.5
traffic_switched=true
release_switch_to_candidate
wait "$probe_pid"
probe_pid=''

if (( observe_seconds > 0 )); then
  release_run_cutover_probe "" "" "$rollback_color" "$rollback_sha" "$((observe_seconds * 1000))" \
    "$evidence_dir/rollback-observation-$timestamp.json"
fi

node "$control" commit-rollback "$state_file" "$manifest"
committed=true
traffic_switched=false
echo "Rollback complete: $rollback_sha is active on $rollback_color. No migration or shared-state reversal was performed."
