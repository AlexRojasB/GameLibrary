#!/usr/bin/env bash
set -euo pipefail

# Only the existing local disposable PostgreSQL container is eligible. Credentials
# stay in the process environment; never enable shell tracing or print docker Env.
root=$(git -C "$(dirname "${BASH_SOURCE[0]}")" rev-parse --show-toplevel)
container=game-library-postgres-test
ip=$(docker inspect "$container" --format '{{(index .NetworkSettings.Networks "bridge").IPAddress}}')
if [[ -z "$ip" || "$ip" != 172.* ]]; then
  printf '%s\n' 'Expected local bridge-connected test PostgreSQL; refusing another target.' >&2
  exit 1
fi
password=$(docker inspect "$container" --format '{{json .Config.Env}}' | jq -er '.[] | select(startswith("POSTGRES_PASSWORD=")) | ltrimstr("POSTGRES_PASSWORD=") | gsub("\""; "\"\"")')
export ConnectionStrings__Test="Host=$ip;Port=5432;Database=game_library_test;Username=postgres;GSS Encryption Mode=Disable;Password=\"$password\""
unset password
BUILDX_GIT_INFO=false docker buildx build --platform linux/amd64 \
  --file "$root/tools/cover-api/Dockerfile" --tag game-library-cover-api:local --load "$root"
docker run --rm --platform linux/amd64 --memory 512m --memory-swap 512m --cpus 2 \
  --pids-limit 256 --read-only --cap-drop ALL --security-opt no-new-privileges \
  --tmpfs /tmp:rw,noexec,nosuid,size=32m --network bridge \
  -e ConnectionStrings__Test game-library-cover-api:local --local-bounded-verification
