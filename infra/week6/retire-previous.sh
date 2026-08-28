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
control="$script_dir/release-control.mjs"

if [[ "${1:-}" != --confirm ]]; then
  echo "Usage: bash infra/week6/retire-previous.sh --confirm" >&2
  echo "This stops (but does not remove) the warm previous API/Next color after the rollback window." >&2
  exit 2
fi

# shellcheck source=release-lib.sh
source "$script_dir/release-lib.sh"
release_require_platform
release_acquire_lock
trap 'rmdir "$lock_dir" 2>/dev/null || true' EXIT

mapfile -t state_fields < <(node "$control" inspect-state "$state_file")
[[ "${state_fields[0]}" == true && -n "${state_fields[3]:-}" ]] || {
  echo "No previous release color is recorded." >&2
  exit 1
}
previous_color=${state_fields[3]}
release_compose stop --timeout 30 "next-$previous_color" "api-$previous_color"
echo "Previous color $previous_color is stopped, not removed. rollback.sh can restart its exact archived digests."
