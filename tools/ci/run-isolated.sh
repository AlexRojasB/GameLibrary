#!/usr/bin/env bash
set -euo pipefail

# Linux-only test runner. Restores/downloads must finish BEFORE calling this script.
# No host network, repository secrets, inherited app configuration or Docker socket
# is passed to test processes. Only containers created by this invocation are stopped.
script=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/run-isolated.sh

if [[ "${1:-}" == __namespace ]]; then
  shift
  uid=$1; gid=$2; home=$3; path=$4; dotnet_root=$5; shift 5
  [[ "$EUID" == 0 && "$uid" != 0 && "$gid" != 0 ]] || exit 1
  ip link set lo up
  # Fail closed if a connected interface or default route was introduced.
  [[ $(ip -o link show | wc -l) -eq 1 && -z $(ip route show default) ]] || exit 1
  connection='Host=127.0.0.1;Port=5432;Database=game_library_test;Username=postgres;Password=ci-only-disposable;GSS Encryption Mode=Disable'
  e2e='Host=127.0.0.1;Port=5432;Database=game_library_e2e;Username=postgres;Password=ci-only-disposable;GSS Encryption Mode=Disable'
  # A runner's PRIMARY group can itself be docker. Use nogroup, not the host GID.
  # Drop supplementary groups, capabilities and sudo escalation as well.
  exec setpriv --reuid="$uid" --regid="$gid" --clear-groups --no-new-privs \
    --bounding-set=-all --inh-caps=-all --ambient-caps=-all \
    env -i HOME="$home" PATH="$path" DOTNET_ROOT="$dotnet_root" \
    CI=true NG_CLI_ANALYTICS=false DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 \
    DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true NuGetAudit=false \
    DOTNET_CLI_USE_MSBUILD_SERVER=false DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1 \
    MSBUILDDISABLENODEREUSE=1 MSBUILDUSESERVER=0 UseSharedCompilation=false \
    ASPNETCORE_ENVIRONMENT=Test DOTNET_ENVIRONMENT=Test \
    ConnectionStrings__Default="$connection" ConnectionStrings__Test="$connection" \
    ConnectionStrings__E2E="$e2e" python3 "$(dirname "$script")/assert-isolated.py" "$@"
fi

mode=${1:-}; shift || true
case "$mode" in
  none|postgres|e2e) [[ $# -gt 0 ]] || { echo 'A test command is required.' >&2; exit 2; } ;;
  native-api) [[ $# == 0 ]] || exit 2 ;;
  *) echo 'Usage: bash tools/ci/run-isolated.sh {none|postgres|e2e} command [args...] | native-api' >&2; exit 2 ;;
esac
[[ $(uname -s) == Linux && $(uname -m) == x86_64 && "$EUID" != 0 ]] || {
  echo 'Requires a non-root native Linux amd64 runner with sudo/network namespaces.' >&2; exit 1;
}
test_gid=$(getent group nogroup | cut -d: -f3)
[[ "$test_gid" =~ ^[1-9][0-9]*$ ]] || exit 1

container=''
native_container=''
cleanup() {
  status=$?
  trap - EXIT
  if [[ -n "$native_container" ]]; then docker rm --force "$native_container" >/dev/null 2>&1 || true; fi
  if [[ -n "$container" ]]; then docker stop "$container" >/dev/null 2>&1 || true; fi
  exit "$status"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

if [[ "$mode" == none ]]; then
  sudo -n unshare --net -- bash "$script" __namespace \
    "$(id -u)" "$test_gid" "$HOME" "$PATH" "${DOTNET_ROOT:-}" "$@"
else
  image=$(cat "$(dirname "$script")/postgres-image.txt")
  database=game_library_test
  if [[ "$mode" == e2e ]]; then database=game_library_e2e; fi
  # No published ports or bridge route: only this container's loopback is reachable.
  container=$(docker run --detach --rm --platform linux/amd64 --network none \
    --label com.gamelibrary.ci=disposable --memory 1g --cpus 2 --pids-limit 256 \
    --stop-timeout 10 --tmpfs /var/lib/postgresql/data:rw,size=1g \
    --env POSTGRES_PASSWORD=ci-only-disposable --env POSTGRES_DB="$database" "$image")
  ready=false
  for ((attempt=0; attempt<60; attempt++)); do
    if docker exec "$container" pg_isready -h 127.0.0.1 -U postgres -d "$database" >/dev/null 2>&1; then
      ready=true; break
    fi
    sleep 1
  done
  [[ "$ready" == true ]] || { echo 'Disposable PostgreSQL did not become ready.' >&2; exit 1; }
  [[ $(docker inspect --format '{{.HostConfig.NetworkMode}}' "$container") == none ]] || exit 1
  if [[ "$mode" == native-api ]]; then
    # Fixed combined-verifier profile; no resource arguments or inherited overrides.
    # Keep our container until final metrics and Docker exit/OOM checks are collected.
    native_container=$(docker create --platform linux/amd64 --network "container:$container" \
      --memory 768m --memory-swap 768m --cpus 2 --pids-limit 256 --read-only \
      --cap-drop ALL --security-opt no-new-privileges --tmpfs /tmp:rw,noexec,nosuid,size=32m \
      --env 'ConnectionStrings__Test=Host=127.0.0.1;Port=5432;Database=game_library_test;Username=postgres;Password=ci-only-disposable;GSS Encryption Mode=Disable' \
      ci-cover-api:local --local-bounded-verification)
    attach_status=0
    docker start --attach "$native_container" || attach_status=$?
    native_status=$(docker wait "$native_container")
    [[ "$native_status" =~ ^[0-9]+$ ]] || exit 1
    # Inspect only allowlisted fields, never container Env/configuration/secrets.
    python3 "$(dirname "$script")/native-runtime-evidence.py" --inspect-container "$native_container" || {
      if (( native_status != 0 )); then exit "$native_status"; fi
      exit 1
    }
    if (( attach_status != 0 && native_status == 0 )); then exit 1; fi
    exit "$native_status"
  else
    pid=$(docker inspect --format '{{.State.Pid}}' "$container")
    [[ "$pid" =~ ^[1-9][0-9]*$ ]] || exit 1
    sudo -n nsenter --target "$pid" --net -- bash "$script" __namespace \
      "$(id -u)" "$test_gid" "$HOME" "$PATH" "${DOTNET_ROOT:-}" "$@"
  fi
fi
