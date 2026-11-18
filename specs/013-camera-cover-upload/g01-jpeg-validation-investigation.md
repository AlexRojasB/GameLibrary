# G01 - Strict JPEG Validation Investigation

Date: 2026-10-29

Historical investigation result: **Blocked pending dependency/architecture approval.**

Follow-up: authorization was subsequently granted and the three-stage validator
was integrated. The original cases passed; an additional unreported AC-run anomaly
then blocked the earlier exhaustive-conformance requirement. The user has now
approved D3b bounded normalization, preserving all structural/diagnostic/resource
checks and other rejection tests. The AC-run fixture remains nonconforming but
has an explicit canonical-output expectation. Fresh host/Noble 94/94 image tests,
Core/build and native packaging/resource checks now pass G01 under D3b. This is
not retrospective exhaustive-conformance proof or Phase 1 completion. See
[G01 implementation evidence](g01-strict-jpeg-implementation-report.md).
The investigation/results below are retained as history, not rewritten as passes.
Phase 1 is still not complete.

## Scope And Decision

This investigation reproduces the failure, adds adversarial regression coverage,
and evaluates alternatives in an isolated test executable/container. It does not
change production decoding, package references, HTTP/DI integration, Angular or
the approved specification. No existing security assertion was weakened/skipped.

Recommended next implementation, subject to approval: retain SkiaSharp for
orientation/color/normalization and add a narrow JPEG-only strict preflight using
libjpeg-turbo 3.2.1's public TurboJPEG API, with stop-on-warning enabled. Combine
it with bounded explicit structural/table validation. Neither marker checking
alone nor TurboJPEG alone is sufficient. The isolated combined prototype passes
39/39 investigated JPEG cases; that is evidence for the approach, not a completed
production validator or proof of universal JPEG conformance.

The added native library/interop/distribution obligations are a significant new
dependency beyond the approved SkiaSharp asset strategy. Per the user's stop rule,
it has NOT been added to the application. Approval is requested before integration.
The Noble/glibc linux/amd64, linux-x64, non-root .NET 10 target stays unchanged.

## 1. Root Cause

The existing ten regression fixtures preserve SOI, tables, frame and scan headers,
truncate nonempty entropy-coded data, and insert a syntactically recognizable EOI.
They retain 1%, 10%, 50%, 90% or 99% of scans from two complete valid 320x480 JPEGs.
Both full-image and scanline SkiaSharp APIs still report success and 480/480 rows.
This was reproduced again on macOS and native Linux amd64, not inferred from code.

libjpeg's Huffman bit reader treats an unexpected marker while more coefficient
bits are needed as recoverable corruption: it emits `JWRN_HIT_MARKER`, sets
`insufficient_data`, supplies zero bits and can produce placeholder remaining
blocks. Full output dimensions/row counts therefore do not prove that the source
contained all the required MCU data.

Skia m154's `JpegDecoderMgr` installs the normal libjpeg error manager and a fatal
error exit, but does not promote recoverable warnings to errors. Its overridden
`output_message` routes diagnostics through `SkCodecPrintf`, normally compiled
away without `SK_PRINT_CODEC_MESSAGES`. SkiaSharp 4.154.0's public codec/options
surface exposes no per-decode warning count/callback or strict-JPEG switch.
Changing GetPixels overloads or counting scanlines does not fix this boundary.
Global stderr capture would be neither reliable nor per-request/thread-safe.

Additional fixtures found acceptance of missing/empty Huffman definitions and an
invalid sequential SOS spectral range. This is not exclusively an EOI-presence bug.

## 2. Structural Validation Evaluation

Useful bounded pre-decoder checks include segment lengths/order, one supported
frame, valid/unique component IDs and sampling factors, DQT/DHT payload shapes,
defined referenced tables, legal SOS component/table selectors and spectral/
successive-approximation parameters, byte stuffing, restart framing and scan
presence. Dimensions must be checked before allocating decoded/coefficient data.

These checks can reject malformed containers and implicit default-table repair.
They cannot establish whether an otherwise plausible entropy stream describes
the expected MCUs. That requires interpreting Huffman codes, run lengths, block
counts and restart intervals; progressive JPEG additionally needs coefficient
significance/refinement/EOB-run state across scans. A home-grown strict entropy
implementation would be substantial security-critical codec code, not a small
header validator. It is not recommended for this single-developer application.

SOI/EOI checks, minimum compressed-byte thresholds, pixel heuristics, stream-position
checks or decode/re-encode size comparisons are not completeness proofs. A full
low-complexity scan can be smaller than 1% of another valid scan. No such heuristic
has been implemented.

## 3. Alternatives Evaluated

| Option | Actual evidence | Security / maintenance / deployment assessment |
| --- | --- | --- |
| Existing SkiaSharp 4.154.0 | Reproduced all ten truncation failures; expanded production suite has 13 failures | Maintained approved package/native strategy, but no strict warning interface; cannot be the only JPEG validator. |
| Custom structural + entropy validator | Structural insufficiency established by adversarial scans and source review; no custom entropy engine written | No new native asset, but application would own a baseline/progressive JPEG parser, bounds, CPU behavior and fuzzing burden. Not the smallest maintainable fix. |
| BitMiracle.LibJpeg.NET 1.5.324 with a throwing warning handler | Rejects all ten original truncations; 37/39 probe cases correct | Managed, BSD/IJG terms and no new native asset. Accepts duplicate frame component IDs in the probe and rejects a valid progressive/restart image. Latest listed package is from 2020-05-08; inspected repository HEAD is 2023-05-25. Poor current maintenance/security-update fit. |
| JpegLibrary 0.4.32 | Rejects all ten original truncations; 37/39 probe cases correct | Managed MIT, no new native asset. Accepts missing SOS and invalid sequential SOS parameters; zero sampling throws DivideByZeroException. Latest listed package/repository activity inspected is 2021-11-16. Not demonstrated as a maintained strict security validator. |
| ImageSharp | Source/options/license review only, NOT an executable compatibility result | Maintained managed decoder, but the reviewed JPEG scan decoder returns on NoData paths. SegmentIntegrityHandling.Strict is not established as strict entropy validation. Split License review and explicit architecture/dependency approval would still be required. No production substitution or package installation was made. |
| Rebuild/fork Skia to promote warnings | Source-level approach only, not implemented | Potentially avoids a second decode, but requires custom native Skia builds, ABI/package maintenance, cross-platform artifacts and upstream tracking. A larger departure from the approved prebuilt assets than a narrow public TurboJPEG boundary. |
| TurboJPEG 3.2.1 stop-on-warning | 38/39 raw probe cases correct; all original truncations rejected; all six valid inputs accepted | Actively supported upstream release published 2026-10-27, public warning/resource controls, IJG/BSD-style terms. Adds a separately patched native library and interop boundary. Raw API still supplies missing standard MJPEG Huffman tables silently, so it is not a complete validator by itself. |
| Structural/table preflight + strict TurboJPEG + existing Skia pipeline | **39/39** isolated probe cases correct | Recommended approval candidate. Keeps entropy interpretation in a maintained codec and existing canonicalization in Skia. Additional bounded JPEG decode costs CPU/memory and must be measured in the actual two-pipeline API before enabling. |

ImageSharp source review used repository commit
`751037d` (2026-10-09). Managed candidate experiments used the listed NuGet
versions, not unpinned repository builds. Package age alone is not proof of an
exploit; it is a maintenance risk alongside the observed behavioral failures.

The probe's cold/JIT-including 39-case elapsed times were approximately 228 ms for
the existing decode/encode processor, 322 ms for BitMiracle, 292 ms for JpegLibrary,
44 ms for raw TurboJPEG and 105 ms for the combined prototype. These are NOT a
controlled benchmark: the paths do different work, native/library initialization
is unequal, and Skia includes encoding. No production speed or memory guarantee
is inferred from those numbers.

## 4. Recommended Boundary

If approved, validate in this order without storing or repairing rejected input:

1. Keep the actual 5 MiB byte bound, detected JPEG type and existing checked frame/
   dimension/pixel limits before full decode; never trust filename/MIME.
2. Run bounded structural checks, including explicit tables for a self-contained
   JPEG. Do not allow an absent DHT to turn the upload into an abbreviated/MJPEG
   stream completed using implicit defaults. Validate actual referenced tables,
   not merely whether any DHT marker exists.
3. Use a fresh native handle per operation and public TurboJPEG APIs. Configure
   STOPONWARNING before header/decode calls, check every return, bound scans and
   intermediates, decode the full unscaled image, and reject warnings as well as
   fatal errors. Do not accept a repaired pixel buffer on a nonzero return.
4. Dispose the validation buffer/handle, then use the existing Skia orientation,
   sRGB conversion, white alpha flattening, sizing and fresh quality-85 JPEG
   encoding. Keep the 2 MiB output bound and metadata/trailing-data stripping.
5. Treat native loading/configuration failure as unavailable, never as permission
   to skip validation. No shell decoder, stderr scraping or undocumented pointer
   access to Skia's bundled private libjpeg implementation.

The prototype uses MAXPIXELS=4000000, independent max edge 4096, SCANLIMIT=99
(matching Skia's abort at scan 100), MAXMEMORY=64 MiB for codec intermediates,
and no retained native source markers. These are experiment settings; an approved
implementation must preserve all existing limits and validate the combined 512 MiB
two-pipeline budget. No API admission controls were implemented in this task.

The prototype's extra table check is intentionally not presented as a production
parser. Production integration still needs complete reviewed structural checks,
SafeHandle/lifetime/error mapping, exact dependency packaging, license notices,
malformed table/restart/progressive corpus expansion and resource verification.

## 5. Security Tests And Results

The application regression file now has fourteen additional cases: segment length
0/1/overrun, missing SOF/SOS/DQT/DHT, empty DQT/DHT, duplicate SOF/component IDs,
zero sampling, unknown SOS component and invalid sequential spectral range.
Eleven already reject safely. Missing DHT, empty DHT and invalid sequential SOS
expose three additional failures. The original ten truncation tests are untouched.

| Verification executed in this task | Result |
| --- | --- |
| Existing image suite before added tests | 43 passed, **10 failed**, none skipped |
| All image-processing tests after added adversarial cases | 54 passed, **13 failed**, none skipped |
| Same tests in unchanged approved native final runtime | 54 passed, **13 failed** |
| Complete Core suite | 337 passed, zero failed/skipped |
| Backend solution build | Passed, zero warnings/errors |
| Isolated strict candidate comparison | Combined prototype 39/39, raw TurboJPEG 38/39; see alternatives table |
| git diff --check | Passed |

The 39-case JPEG probe contains six valid inputs (two Skia-generated images plus
four baseline/progressive and restart/no-restart combinations generated by
TurboJPEG), ten original entropy truncations, two no-entropy cases, two missing
EOI cases, fourteen structural mutations, four truncated first scans and one
standard-coded JPEG with every DHT removed. Raw TurboJPEG accepts that last input;
the combined prototype rejects it before native decoding. Thus the recommendation
is deliberately NOT based only on the original ten failing fixtures.

Passing application fixtures continue to exercise actual JPEG/PNG/static WebP,
all eight EXIF orientations, alpha flattening, Display-P3-to-sRGB conversion,
canonical rounding/resize, quality 85, metadata/trailing-data removal, unsupported
formats, animation rejection and dimension/byte bounds. Their success does not
offset the failing JPEG rejection tests.

## 6. Linux Evidence

Both runs used native x86_64 Linux, explicit linux/amd64 and RID linux-x64,
framework-dependent .NET 10.0.14 on Ubuntu 24.04.5, non-root UID/GID 1654.

- SDK digest: `sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b`.
- Runtime digest: `sha256:de0ee358cdd672f7017a003bed21e9ac9ed48a27c922a59b18175b6e82fa7fee`.
- Approved smoke image: `sha256:48c5e160cb5dac390859ddf1156f8878293aac421f581f47d15fe96f4de5ee92`.
- Approved final libSkiaSharp SHA256:
  `25b0e8dcaf75444af01e0aa3b29286614ceb56adc80deadb511d3503896fd86b`.
- Approved final-runtime `ldd -r` resolved all dependencies/symbols; no SDK or
  additional native/inspection packages were present. Under 512 MiB, the existing
  processor completed 40 operations on two pipelines in 6549 ms, process peak
  working set 170393600 bytes, cgroup memory.peak 129953792 bytes, zero OOM events.
  Those distinct measurements are recorded as reported, not equated.
- Separate experimental image:
  `sha256:a75ff778a20257cb43bae38fd7417001971e738dedfeb927bc9eece7f4c3936f`.
- Candidate official amd64 DEB SHA256:
  `21c7af6e505386dda0fa05e9eab5a8d288947f2e09ade8d7a48ff393ed08182d`.
- Extracted candidate libturbojpeg.so.0.5.0 SHA256:
  `dffb4dc98ac21a0bdb18ba5987b9ac97fb8ab6da92c9679add294c7cb50caa8f`.
- Candidate `ldd -r` resolved libc/loader without additional system packages in
  the Noble runtime. The experimental image copied only the candidate library;
  it did not install the DEB or change the application's approved final image.

Both containers ran with 512 MiB memory/swap limits, two CPUs, network disabled,
read-only root and bounded 32 MiB /tmp tmpfs. The candidate corpus run passed its
checks under that ceiling; it is not a full concurrent API load/resource gate.

## 7. Files And Reproduction

Workspace changes in this focused task only:

- `tests/backend/GameLibrary.IntegrationTests/CoverImageProcessorTests.cs`: fourteen
  added regression cases and their shared bounded fixture builder.
- This investigation report.
- `phase-1-implementation-report.md`: link to this subsequent investigation; its
  earlier results remain historical rather than rewritten as new successes.

Experimental project/Program/Dockerfile are outside the workspace at
`/var/folders/fd/h4rhj_wn5_71qy5q79xm8r_m0000gn/T/opencode/g01-jpeg-probe/`.
Its NuGet dependencies and extracted native binary are investigation-only. The
existing approved-runtime harness is at the sibling `feature013-native-smoke/`.
The candidate executable prints a comparison and exits normally even when a
candidate misclassifies fixtures; inspect each SUMMARY/FAIL, not only its exit
code. The application suite and approved smoke exit nonzero for their failures.

From the repository root:

```sh
dotnet test tests/backend/GameLibrary.IntegrationTests/GameLibrary.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~CoverImageProcessorTests
dotnet test tests/backend/GameLibrary.Core.Tests/GameLibrary.Core.Tests.csproj --no-restore
dotnet build src/backend/GameLibrary.sln --no-restore
git diff --check
docker buildx build --platform linux/amd64 --build-context harness=/var/folders/fd/h4rhj_wn5_71qy5q79xm8r_m0000gn/T/opencode/feature013-native-smoke --file /var/folders/fd/h4rhj_wn5_71qy5q79xm8r_m0000gn/T/opencode/feature013-native-smoke/Dockerfile --target final --tag feature013-native-smoke:local --load .
docker run --rm --platform linux/amd64 --memory 512m --memory-swap 512m --cpus 2 --network none --read-only --tmpfs /tmp:rw,noexec,nosuid,size=32m feature013-native-smoke:local
docker buildx build --platform linux/amd64 --build-context probe=/var/folders/fd/h4rhj_wn5_71qy5q79xm8r_m0000gn/T/opencode/g01-jpeg-probe --file /var/folders/fd/h4rhj_wn5_71qy5q79xm8r_m0000gn/T/opencode/g01-jpeg-probe/Dockerfile --target candidate --tag feature013-g01-jpeg-probe:local --load .
docker run --rm --platform linux/amd64 --memory 512m --memory-swap 512m --cpus 2 --network none --read-only --tmpfs /tmp:rw,noexec,nosuid,size=32m feature013-g01-jpeg-probe:local
```

## 8. Limitations And Approval Request

At this historical investigation stage, G01 was **Blocked**, not Passed, with no
integrated fix within the then-approved dependency set. The combined prototype was a promising measured solution,
not authorization to add a library or declare the gate complete. It has not had
production interop review, broad fuzz/corpus coverage, dev-platform packaging or
full two-pipeline maximum-input performance validation.

JPEG has no general integrity checksum: no decoder can identify every byte edit
that happens to produce another valid JPEG. A progressive stream that is valid at
lower precision cannot reveal that additional complete refinement scans once
existed and were removed before a new EOI was inserted. This is different from
accepting insufficient entropy for the declared scans or silently repairing an
invalid stream; no claim of recovering an upload's unknowable original history
is made. Native-code bounds, patching, admission and metadata sanitization remain
mandatory even after strict validation.

Request approval for the narrow libjpeg-turbo 3.2.1 JPEG validation dependency and
structural preflight, retaining SkiaSharp canonicalization, all current limits and
the existing Linux target. If approved, first update the approved dependency/native
strategy and then implement/test the focused boundary before rerunning G01. Do
not continue HTTP/DI or Angular as part of that follow-up without separate scope.

No production source/dependency, existing migration, HTTP/DI or Angular file was
changed by this focused task. No commits, staging or pushes. Existing index SHA256
remains `62092d1ed1bbe35197e650619a83a5a9eed14a4495a4223a795223ed11372439`.

## Sources

- Skia m154 error/warning bridge:
  https://raw.githubusercontent.com/google/skia/chrome/m154/src/codec/SkJpegDecoderMgr.cpp
- Skia diagnostic macro:
  https://raw.githubusercontent.com/google/skia/chrome/m154/src/codec/SkCodecPriv.h
- Exact SkiaSharp public API:
  https://raw.githubusercontent.com/mono/SkiaSharp/v4.154.0/binding/SkiaSharp/SKCodec.cs
- libjpeg Huffman warning/zero-fill behavior:
  https://raw.githubusercontent.com/libjpeg-turbo/libjpeg-turbo/3.2.1/src/jdhuff.c
- Public TurboJPEG strict/resource controls:
  https://github.com/libjpeg-turbo/libjpeg-turbo/blob/3.2.1/src/turbojpeg.h
- Native version/support/release assets:
  https://github.com/libjpeg-turbo/libjpeg-turbo/releases/tag/3.2.1
- License/distribution notices:
  https://github.com/libjpeg-turbo/libjpeg-turbo/blob/3.2.1/LICENSE.md
- Managed release dates:
  https://api.nuget.org/v3/registration5-gz-semver2/bitmiracle.libjpeg.net/1.5.324.json
  and https://api.nuget.org/v3/registration5-gz-semver2/jpeglibrary/0.4.32.json
- ImageSharp source reviewed (not a tested substitute):
  https://github.com/SixLabors/ImageSharp/blob/751037d/src/ImageSharp/Formats/Jpeg/Components/Decoder/HuffmanScanDecoder.cs
