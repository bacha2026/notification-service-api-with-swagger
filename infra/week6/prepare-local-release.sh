#!/usr/bin/env bash
set -Eeuo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)
repo_root=$(cd -- "$script_dir/../.." && pwd -P)
runtime_dir="$script_dir/.runtime"
allow_dirty=false

if [[ "${1:-}" == --allow-dirty ]]; then
  allow_dirty=true
elif [[ -n "${1:-}" && "${1:-}" != --help && "${1:-}" != -h ]]; then
  echo "Usage: bash infra/week6/prepare-local-release.sh [--allow-dirty]" >&2
  exit 2
elif [[ "${1:-}" == --help || "${1:-}" == -h ]]; then
  echo "Usage: bash infra/week6/prepare-local-release.sh [--allow-dirty]"
  exit 0
fi

for command in docker node git; do
  command -v "$command" >/dev/null 2>&1 || { echo "Required command is unavailable: $command" >&2; exit 1; }
done
docker info >/dev/null 2>&1 || { echo "Docker's Linux engine is not running." >&2; exit 1; }

git_sha=$(git -C "$repo_root" rev-parse HEAD)
revision=$git_sha
if [[ -n "$(git -C "$repo_root" status --porcelain)" ]]; then
  if ! $allow_dirty; then
    echo "The working tree is dirty. Commit the release or pass --allow-dirty for a clearly labeled local-only proof." >&2
    exit 1
  fi
  revision="${git_sha:0:12}-dirty-$(date -u +%Y%m%d%H%M%S)"
fi

mkdir -p -- "$runtime_dir/local-builds"
api_tag="nsa-local/notification-api:$revision"
next_tag="nsa-local/notification-web:$revision"
migrator_tag="nsa-local/notification-migrator:$revision"

docker build --file "$repo_root/notification-api/Dockerfile" --tag "$api_tag" "$repo_root"
docker build \
  --build-arg "RELEASE_ID=$revision" \
  --file "$repo_root/next/Dockerfile" \
  --tag "$next_tag" \
  "$repo_root/next"
docker build --target migrator --file "$repo_root/notification-api/Dockerfile" --tag "$migrator_tag" "$repo_root"

api_id=$(docker image inspect --format '{{.Id}}' "$api_tag")
next_id=$(docker image inspect --format '{{.Id}}' "$next_tag")
migrator_id=$(docker image inspect --format '{{.Id}}' "$migrator_tag")
manifest="$runtime_dir/local-builds/release-$revision.json"
node "$script_dir/release-control.mjs" create-local \
  "$manifest" "$revision" "$api_id" "$next_id" "$migrator_id"

echo "Local content-addressed release manifest: $manifest"
echo "Deploy without rebuilding: bash infra/week6/deploy.sh '$manifest'"
