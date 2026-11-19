#!/usr/bin/env bash
set -euo pipefail
[[ $(uname -s) == Linux && $(uname -m) == x86_64 ]] || {
  echo 'Native gates require actual Linux amd64, not macOS or emulation.' >&2; exit 1;
}
root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
cd "$root"
export BUILDX_GIT_INFO=false
# Build-time downloads are allowed; verification containers have no external network.
docker buildx build --platform linux/amd64 --file tools/cover-native/Dockerfile \
  --target final --tag ci-cover-native:local --load .
docker buildx build --platform linux/amd64 --file tools/cover-native/Dockerfile \
  --target inspection --output type=cacheonly .
docker run --rm --platform linux/amd64 --network none --read-only \
  --memory 512m --memory-swap 512m --cpus 2 --pids-limit 256 \
  --cap-drop ALL --security-opt no-new-privileges --tmpfs /tmp:rw,noexec,nosuid,size=32m \
  ci-cover-native:local
for target in missing-native tampered-native; do
  docker buildx build --platform linux/amd64 --file tools/cover-native/Dockerfile \
    --target "$target" --tag "ci-cover-$target:local" --load .
  docker run --rm --platform linux/amd64 --network none --memory 512m --memory-swap 512m \
    --read-only --cap-drop ALL --security-opt no-new-privileges "ci-cover-$target:local"
done
docker buildx build --platform linux/amd64 --file tools/cover-api/Dockerfile \
  --tag ci-cover-api:local --load .
bash tools/ci/run-isolated.sh native-api
