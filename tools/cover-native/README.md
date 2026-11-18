# Cover Native Verification

libjpeg-turbo 3.2.1 is approved only for JPEG prevalidation before SkiaSharp.
The acceptance policy is bounded normalization, not exhaustive T.81 conformance:
unreported coefficient tolerance may be normalized after every existing structure,
decoder-diagnostic and resource check passes. STOPONWARNING remains mandatory;
truncation, malformed headers/tables and other rejected categories stay rejected.
Only fresh canonical output may be stored, never original upload bytes.
These are build/test tools, not HTTP endpoints or a third production application.
Never invoke the downloader or a shell decoder from the API.

## Preparation

From the repository root, run `bash tools/cover-native/prepare.sh` before a local
backend build. The script defaults to the supported host RID. Linux production
builds use `bash tools/cover-native/prepare.sh linux-x64` in the Noble SDK image.
macOS x64/arm64 assets support development only, not an alternative production gate.
Windows/Alpine/other production architectures are not approved by this decision.

The script downloads only the official 3.2.1 release over HTTPS, verifies the
pinned release-asset SHA256, checks Linux package version `3.2.1-20261027` and
architecture, extracts without installing it system-wide, and verifies the native
binary's SHA256. No downloaded installer or package script is executed. The macOS package is
expanded, not installed; its read-only disk image is detached by the cleanup trap.
Prepared binaries are ignored build artifacts under the API's obj/cover-native.

The API project copies the selected binary and THIRD-PARTY-NOTICES.txt into build,
test-reference and publish outputs. Build fails with preparation guidance if the
selected asset is missing. At runtime the JPEG binding verifies the expected hash
and loads only the application-local absolute path; there is no system-library
fallback or runtime download. Deploy output and native assets read-only to prevent
local modification between verification and loading. Startup/other API features do
not load TurboJPEG; the validation path fails unavailable if it cannot load safely.

The smoke project links the actual image test sources and references the production
API. It does not fork or mock the processor. The Docker build also publishes the
API normally and compares its native asset/notices with the harness publish.

## Reproduce G01

```sh
bash tools/cover-native/prepare.sh
dotnet test tests/backend/GameLibrary.IntegrationTests/GameLibrary.IntegrationTests.csproj --filter FullyQualifiedName~CoverImageProcessorTests
docker buildx build --platform linux/amd64 --file tools/cover-native/Dockerfile --target final --tag game-library-cover-native:local --load .
docker buildx build --platform linux/amd64 --file tools/cover-native/Dockerfile --target inspection --output type=cacheonly .
docker run --rm --platform linux/amd64 --memory 512m --memory-swap 512m --cpus 2 --network none --read-only --tmpfs /tmp:rw,noexec,nosuid,size=32m game-library-cover-native:local
docker buildx build --platform linux/amd64 --file tools/cover-native/Dockerfile --target missing-native --tag game-library-cover-native-missing:local --load .
docker run --rm --platform linux/amd64 --memory 512m --network none --read-only game-library-cover-native-missing:local
docker buildx build --platform linux/amd64 --file tools/cover-native/Dockerfile --target tampered-native --tag game-library-cover-native-tampered:local --load .
docker run --rm --platform linux/amd64 --memory 512m --network none --read-only game-library-cover-native-tampered:local
```

The final runtime is the pinned unchanged non-root Noble aspnet image plus published
application/harness assets. Only the separate inspection stage installs file and
binutils. Both libraries are inspected there and checked with ldd -r in the final
runtime. No SDK, dev libraries, image tools, font stack or codec packages are
installed in the final runtime. Run on native amd64 Linux, not emulation-only.

The resource run uses two pipelines and bounded near-4MP baseline/progressive JPEGs
plus a high-entropy PNG, then reports process/cgroup peak measurements and OOM
events. It is a processor/native gate, NOT HTTP pre-binding admission verification
or a complete worst-case application load test. HTTP/DI is covered separately by
the [Phase 1 integration report](../../specs/013-camera-cover-upload/phase-1-implementation-report.md).
Focused native evidence is in the Feature 013 G01 implementation report.

## Provenance And Updates

Official release: https://github.com/libjpeg-turbo/libjpeg-turbo/releases/tag/3.2.1

| Artifact | SHA256 |
| --- | --- |
| Official amd64 DEB | 21c7af6e505386dda0fa05e9eab5a8d288947f2e09ade8d7a48ff393ed08182d |
| Extracted Linux library | dffb4dc98ac21a0bdb18ba5987b9ac97fb8ab6da92c9679add294c7cb50caa8f |
| Official macOS DMG | 10cf9233f771089166da8f0d6cd014294ebae93e1212ef033fbc548dc622ce74 |
| Extracted universal macOS library | d82a04634d7e9c5a714f573c422d3d8d7f1c49de966f3af59ec9f6dc928997ae |

Hashes are pinned from official upstream release assets and verified extractions;
they establish exact artifact identity, not protection against a compromised
upstream account. Binary releases are unmodified. Distribution includes the full
Modified BSD notice and required IJG acknowledgement. See THIRD-PARTY-NOTICES.txt
and the upstream LICENSE.md/README.ijg for terms.

Review security releases and the public C ABI when updating. Update artifact and
binary pins together, rerun all image/adversarial/native/resource checks, and record
new image digests. Do not silently accept another native version, increase budgets,
or waive warning rejection to make a test pass. NuGet audit does not audit this
separately distributed native library. Keep its provenance/version in deployment
inventory alongside the Skia and .NET pins.
