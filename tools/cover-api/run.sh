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
native_container=''
cleanup() {
  status=$?
  trap - EXIT
  if [[ -n "$native_container" ]]; then docker rm --force "$native_container" >/dev/null 2>&1 || true; fi
  exit "$status"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
native_container=$(docker create --platform linux/amd64 --memory 768m --memory-swap 768m --cpus 2 \
  --pids-limit 256 --read-only --cap-drop ALL --security-opt no-new-privileges \
  --tmpfs /tmp:rw,noexec,nosuid,size=32m --network bridge \
  -e ConnectionStrings__Test game-library-cover-api:local --local-bounded-verification)
attach_status=0
docker start --attach "$native_container" || attach_status=$?
native_status=$(docker wait "$native_container")
[[ "$native_status" =~ ^(0|[1-9][0-9]{0,2})$ ]] && (( native_status <= 255 )) || exit 1
python3 "$root/tools/ci/native-runtime-evidence.py" --inspect-container "$native_container" || {
  if (( native_status != 0 )); then exit "$native_status"; fi
  exit 1
}
if (( attach_status != 0 && native_status == 0 )); then exit 1; fi
exit "$native_status"
