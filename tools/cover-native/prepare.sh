#!/usr/bin/env bash
set -euo pipefail

# Build-time acquisition only. No downloads or shell decoders run in the API.
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
rid="${1:-}"
if [[ -z "$rid" ]]; then
    case "$(uname -s)-$(uname -m)" in
        Linux-x86_64) rid=linux-x64 ;;
        Darwin-x86_64) rid=osx-x64 ;;
        Darwin-arm64) rid=osx-arm64 ;;
        *) printf '%s\n' 'Unsupported cover native platform.' >&2; exit 1 ;;
    esac
fi
case "$rid" in
    linux-x64)
        artifact=libjpeg-turbo-official_3.2.1_amd64.deb
        artifact_hash=21c7af6e505386dda0fa05e9eab5a8d288947f2e09ade8d7a48ff393ed08182d
        binary_hash=dffb4dc98ac21a0bdb18ba5987b9ac97fb8ab6da92c9679add294c7cb50caa8f
        name=libgamelibrary-turbojpeg.so
        ;;
    osx-x64|osx-arm64)
        artifact=libjpeg-turbo-3.2.1.dmg
        artifact_hash=10cf9233f771089166da8f0d6cd014294ebae93e1212ef033fbc548dc622ce74
        binary_hash=d82a04634d7e9c5a714f573c422d3d8d7f1c49de966f3af59ec9f6dc928997ae
        name=libgamelibrary-turbojpeg.dylib
        ;;
    *) printf '%s\n' 'Only linux-x64 production and macOS development assets are supported.' >&2; exit 1 ;;
esac
verify() {
    local actual
    if command -v sha256sum >/dev/null; then actual="$(sha256sum "$1")";
    else actual="$(shasum -a 256 "$1")"; fi
    [[ "${actual%% *}" == "$2" ]]
}
out="$root/src/backend/GameLibrary.Api/obj/cover-native/3.2.1/$rid"
mkdir -p "$out"
if [[ -f "$out/$name" ]] && verify "$out/$name" "$binary_hash"; then exit 0; fi
temp="$(mktemp -d "$out/acquire.XXXXXX")"
mounted=false
cleanup() {
    if [[ "$mounted" == true ]]; then hdiutil detach "$temp/mount" >/dev/null; fi
    rm -rf "$temp"
}
trap cleanup EXIT
curl --fail --silent --show-error --location --proto '=https' --proto-redir '=https' --tlsv1.2 \
    --output "$temp/$artifact" "https://github.com/libjpeg-turbo/libjpeg-turbo/releases/download/3.2.1/$artifact"
verify "$temp/$artifact" "$artifact_hash" || { printf '%s\n' 'Native artifact hash mismatch.' >&2; exit 1; }
if [[ "$rid" == linux-x64 ]]; then
    [[ "$(dpkg-deb --field "$temp/$artifact" Version)" == 3.2.1-20261027 ]] || { printf '%s\n' 'Unexpected package version.' >&2; exit 1; }
    [[ "$(dpkg-deb --field "$temp/$artifact" Architecture)" == amd64 ]] || { printf '%s\n' 'Unexpected package architecture.' >&2; exit 1; }
    dpkg-deb --extract "$temp/$artifact" "$temp/extracted"
    source="$temp/extracted/opt/libjpeg-turbo/lib64/libturbojpeg.so.0.5.0"
else
    mkdir "$temp/mount"
    hdiutil attach -readonly -nobrowse -mountpoint "$temp/mount" "$temp/$artifact" >/dev/null
    mounted=true
    pkgutil --expand-full "$temp/mount/libjpeg-turbo.pkg" "$temp/extracted"
    source="$temp/extracted/libjpeg-turbo.pkg/Payload/opt/libjpeg-turbo/lib/libturbojpeg.0.5.0.dylib"
fi
verify "$source" "$binary_hash" || { printf '%s\n' 'Extracted native binary hash mismatch.' >&2; exit 1; }
install -m 644 "$source" "$out/$name"
printf 'Prepared libjpeg-turbo 3.2.1 for %s\n' "$rid"
