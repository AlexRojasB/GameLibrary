# G01 - Bounded JPEG Normalization And Verification

Date: 2026-10-29

**G01: Passed under approved D3b bounded normalization. Phase 1 remains partial; do not deploy or enable uploads.**

## Approved Policy Follow-Up

On 2026-10-29 the user approved bounded JPEG normalization instead of exhaustive
input conformance. This is a deliberate policy change, not a native-code fix or a
declaration that the AC-run source is standards-compliant. The processor, structural
checks, STOPONWARNING handling, native pins, all resource limits and Skia encoding
are unchanged. Only the single formerly failing regression is renamed to
`Decoder_tolerated_nonconforming_ac_run_is_normalized_as_fresh_bounded_jpeg` and
now requires fresh canonical output distinct from the source and canonical
reprocessing. Its fixture and same-header positive control are retained.

All other security regressions retain their rejection outcomes. No tolerance is
introduced for truncation, malformed headers/tables, animation, unsupported types,
decoder diagnostics or limit violations. Original upload bytes must never be the
managed cover. The fresh image/Core/build and Noble packaging/resource results
below pass the focused G01 gate under D3b; this is not exhaustive JPEG conformance
verification, a fix to the native coefficient decoder, or whole-feature approval.

## Current D3b Verification Results

| Executed check | Actual result |
| --- | --- |
| Complete image-processing suite, development host | **94 passed, 0 failed, 0 skipped** |
| Same exact test sources, non-root Noble linux/amd64 runtime | **94 passed, 0 failed** |
| Retained decoder-tolerated AC-run fixture | Canonical 160x240 output, different bytes from source, successful canonical reprocessing |
| Every other rejection test, including original 13 security cases | Unchanged and passing; no truncation/header/table/diagnostic exception added |
| Existing 39-case corpus and 1000 mutation attempts | Active and passing as part of the same suite |
| Core suite | **337 passed, 0 failed, 0 skipped** |
| Backend solution build | Passed, zero warnings/errors |
| Normal linux-x64 API and harness publish | Passed; native library and notices matched |
| Official package version/hash and extracted native hash | Passed, existing 3.2.1 pins unchanged |
| Both native ELF/file/readelf and final-runtime ldd -r checks | Passed, no SDK/inspection libraries masking final runtime dependencies |
| Missing and tampered native asset targets | Both failed safely as Unavailable, without fallback |
| Two concurrent processor pipelines under 512 MiB | 60 operations completed, all outputs bounded; no OOM events |
| git diff --check | Passed |

Native target remains native Linux x86_64, Ubuntu 24.04.5, .NET 10.0.14,
RID linux-x64, UID/GID 1654, framework-dependent/non-root, no SDK in the final image.
The same pinned Noble SDK/runtime digests and native hashes recorded in the
provenance section below were used. Current verified final image configuration:
`sha256:039e5b1c3f4798f4997ab87184ada7de193442a218b0cd908f8025862882fdc0`.

The current resource run used two CPUs, read-only root, network disabled, 32 MiB
/tmp tmpfs, memory and memory-swap both 512 MiB. It completed 60 operations in
17606 ms using the existing 1632x2448 baseline/progressive JPEGs (3020130/2781404
bytes) and 960x1440 high-entropy PNG (4156060 bytes). Reported process peak working
set was 292511744 bytes (about 279 MiB); cgroup memory.peak was 249569280 bytes
(about 238 MiB), memory.max 536870912, and max/oom/oom_kill events were all zero.
The distinct measurements are recorded as reported, not equated. Timing included
other verification work and is not a benchmark or whole-API load/admission proof.

Production processor/structure/native-binding files, Program.cs, API project,
native preparation script/Dockerfile and native fixture encoder had identical
SHA256 checksums before and after this task. There is no new bypass or diagnostic
suppression. The only test code change is the explicitly authorized AC-run name/
outcome; all other fixtures/assertions remain intact. Documentation reconciles the
approved policy in Product/Architecture/AGENTS/Feature 013, acceptance criteria and
the investigation/history reports. No dependency/native/platform change was made.

The processor still returns only freshly Skia-encoded canonical bytes. Wiring
that result to Storage and verifying the complete upload/HTTP path remain Phase 1
work, not evidence claimed by this processor gate. No HTTP/DI, Angular, database,
remote-resource, staging, commit or push operation was performed in this task.

Residual risk is deliberately accepted, not eliminated: the source fixture remains
nonconforming; unreported decoder tolerance and visual corruption may normalize
without a per-file warning. Resource limits, structural validation, all reported
diagnostic rejection, native lifetime/pinning and sanitization remain mandatory.
This policy does not authorize original-byte storage, native forks, an entropy
engine, disabled warnings, truncated files, malformed headers/tables or other
currently rejected categories. Native bugs and source authenticity are not proven
away by successful decoding or a green finite corpus. Re-run G01 after changes to
the policy, codec pins, platform or processor. G01 Passed does not complete Phase 1.

## Historical D3a Implementation And Results

The following implementation and 93/94 results describe the earlier strict-input
requirement and are retained as history. Where they require exhaustive rejection
of unreported coefficient anomalies, D3b now supersedes that requirement. The
existing native/structural checks are not bypassed or relaxed.

The user approved libjpeg-turbo 3.2.1 exclusively for strict JPEG validation.
This follow-up implements that boundary and verifies it. It does not implement
HTTP/DI, early upload admission, proxy/cleanup hosting, Angular or Storage resources.
Other feature/release gates remain separate and are not marked passed here.

## Historical Blocker Under The Earlier Policy

The approved three-stage approach fixes the original 13 security failures and
passes the investigated 39-case corpus. Expanded testing nevertheless found a
complete, structurally plausible JPEG whose AC run crosses the legal 64-coefficient
block boundary. TurboJPEG 3.2.1 returns success without a warning; Skia also accepts
it. STOPONWARNING cannot reject corruption that the native decoder does not report.

The active regression is `Ac_run_cannot_cross_the_64_coefficient_block_boundary`.
It uses a 160x240 grayscale frame, explicit legal quantization/Huffman tables and
600 fully supplied blocks. AC code 0 means run=15,size=10; code 10 means EOB.
Each damaged block contains DC category 0 and four run=15,size=10 AC codes, targeting
coefficient positions 16,32,48,64. Position 64 is outside 0..63. The complete 3375-byte
entropy payload ends normally; this is neither missing EOI nor ordinary truncation.
A same-header/table positive control using DC 0 + EOB decodes and normalizes first,
proving this is not simply an invalid table declaration or unsupported image type.

This fails identically on the development host and the approved native Noble
runtime. libjpeg-turbo's `src/jdhuff.c` uses extra `jpeg_natural_order` entries for
out-of-range runs without issuing a warning on this path. This is observed input
acceptance, not an observed memory overrun/process crash. A table-presence or
marker validator cannot distinguish it from a valid stream using the same tables.
Hard-coding this fixture, rejecting legal tables, inspecting repaired pixels or
weakening the assertion would not satisfy the requirement.

The new test remains active and failing. No custom entropy decoder, native fork,
alternate codec or relaxed validity profile was introduced to hide this result.
Further design review is needed for a native/upstream strict-mode change that
reports this corruption, or separately reviewed entropy validation. Expanding a
header preflight into a general JPEG engine exceeds the approved scope.

Source: https://github.com/libjpeg-turbo/libjpeg-turbo/blob/3.2.1/src/jdhuff.c

## Implemented Pipeline

1. Check the actual 5 MiB bound before allocation and work on a private bounded
   byte snapshot. Detect the container from bytes, not a filename or MIME label.
2. For JPEG, run the bounded supported-cover structural preflight: frame precision/
   dimensions/components/sampling, segment bounds/order, nonempty legal DQT/DHT,
   explicit referenced tables, SOS parameters/progression, restart framing/order,
   scan limit and real entropy presence. It is not an entropy decoder or general
   media/parser framework. Missing MJPEG tables cannot bypass the explicit-table
   checks; the remaining unreported entropy corruption is described above.
3. Completely decode through libjpeg-turbo 3.2.1's public TurboJPEG C API with
   STOPONWARNING set before header parsing. Check every status, reject warning or
   failure, retain no source markers, and use no scaling/cropping/skipped rows.
4. Only after strict validation, run the existing SkiaSharp 4.154.0 normalization:
   EXIF orientation, sRGB, white alpha flattening, 2:3 rounding/resize, fresh JPEG
   quality 85 and metadata/trailing-data stripping. Skia has not been replaced.

Unchanged limits include 5 MiB input, 4 million pixels, max input edge 4096,
160x240 minimum and 960x1440 maximum output, 2 MiB output. Strict JPEG scans are
bounded to 99, matching Skia's existing abort at scan 100. Native intermediates
are capped at 64 MiB. The measured processor gate uses two concurrent pipelines
under 512 MiB; no claim is made that HTTP admission is already integrated.

## Native Binding And Lifetime

The binding uses ordinary .NET Cdecl P/Invoke against the maintained public
TurboJPEG 3 ABI, not a third-party managed wrapper or private Skia codec structures.
Every decode has a fresh SafeHandle; Dispose runs on success and exceptions.
The exact checked RGB/CMYK validation buffer is allocated after authoritative
dimension checks and freed in finally before Skia allocates its working raster.
No validation pixels are returned, encoded or stored after a warning/failure.

The application-local library is verified against its pinned SHA256 before loading.
There is no system/PATH fallback or runtime acquisition. The process-lifetime module
handle stays loaded so live native-handle finalizers cannot call an unloaded module.
Missing/tampered libraries fail unavailable with a safe error and no inner native
diagnostic. Invalid JPEGs fail InvalidImage, not successful repaired output.

These controls and tests prevent the known malformed-input and interop failure
paths from escaping as process crashes. They are not a promise that arbitrary bugs
in native libraries can be caught by a managed exception handler. Bounds, patches,
read-only deployment and continuing adversarial verification remain necessary.

## Provenance, Version And Packaging

Official upstream release:
https://github.com/libjpeg-turbo/libjpeg-turbo/releases/tag/3.2.1

The reproducible build helper verifies both the official artifact and the extracted
library; Linux package metadata is exactly version 3.2.1-20261027, architecture
amd64. It extracts without installing package scripts/system libraries. Only the
required binary and IJG/Modified-BSD distribution notices enter publish output.
Prepared assets are ignored obj files, not committed binaries. Runtime hash pinning
prevents silently loading an older or substituted native version.

| Asset | SHA256 |
| --- | --- |
| Official Linux amd64 DEB | 21c7af6e505386dda0fa05e9eab5a8d288947f2e09ade8d7a48ff393ed08182d |
| Extracted/published Linux TurboJPEG | dffb4dc98ac21a0bdb18ba5987b9ac97fb8ab6da92c9679add294c7cb50caa8f |
| Official macOS development DMG | 10cf9233f771089166da8f0d6cd014294ebae93e1212ef033fbc548dc622ce74 |
| Extracted/published universal macOS TurboJPEG | d82a04634d7e9c5a714f573c422d3d8d7f1c49de966f3af59ec9f6dc928997ae |
| Unchanged Linux Skia asset | 25b0e8dcaf75444af01e0aa3b29286614ceb56adc80deadb511d3503896fd86b |

The API project packages assets transitively into test output and normal API publish.
The Docker gate separately publishes the API and harness and compares the JPEG
library and license notices between those outputs. The maintained native library
is independently inventoried; a NuGet audit alone does not audit this native binary.
The API NuGet audit reported no vulnerable packages from current configured sources.

macOS x64 was verified for development. The upstream universal asset also contains
arm64, but no macOS arm64 execution or alternative production target is claimed.
Production remains Noble/glibc linux/amd64, RID linux-x64, framework-dependent .NET
10, no trimming/AOT/single-file. Windows/Alpine/other targets are not approved.

## Security Regression Results

| Executed check | Actual result |
| --- | --- |
| Complete image-processing suite on development host | 93 passed, **1 failed**, 0 skipped; 94 total |
| Same exact test sources in final native Noble runtime | 93 passed, **1 failed**; same AC-run regression |
| Original 13 failing security cases | All active and passing; assertions not weakened |
| Repeated investigated corpus | 39/39 integrated cases correct, inside one explicit regression test |
| Deterministic JPEG mutation exercise | 1000 attempts within one regression test, no escaped exceptions or process crash |
| Missing native library target | Safe Unavailable; no permissive decoder fallback |
| Tampered native library target | Safe Unavailable; no permissive decoder fallback |
| Core suite | 337 passed, 0 failed, 0 skipped |
| Backend solution build | Passed, 0 warnings/errors |
| Normal linux-x64 API publish and asset/notices comparison | Passed |
| Shell script syntax and git diff --check | Passed |

The 27 added test cases beyond the previous 67 include the exact investigated
39-item corpus as one test, valid baseline/progressive/restart JPEGs, subsampling,
grayscale/CMYK/YCCK positives, premature scan endings, missing standard Huffman
tables, malformed Huffman/quantization data, duplicate SOS components, incorrect
restart order/absent DRI, deterministic mutations and the newly failing AC-run
overflow with a valid same-table positive control. Existing orientation/color,
JPEG/PNG/static-WebP, animation, dimension/byte/output, metadata and canonicalization
tests continue to run. The mutation exercise is not an exhaustive fuzzer and accepts
valid altered images only when their output satisfies the canonical bounds.

## Native Runtime And Resources

- SDK: `mcr.microsoft.com/dotnet/sdk:10.0-noble@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b`.
- Runtime: `mcr.microsoft.com/dotnet/aspnet:10.0-noble@sha256:de0ee358cdd672f7017a003bed21e9ac9ed48a27c922a59b18175b6e82fa7fee`.
- Verified final image configuration ID:
  `sha256:b73dc898437988190a333cc1165b5f070af96a7b4217a60f15f82da856a8b61e`.
- Native x86_64 Linux, Ubuntu 24.04.5, .NET 10.0.14, UID/GID 1654, no SDK installed.
- file/readelf inspected the identical published x86-64 ELF libraries in a separate
  inspection stage. No inspection/codec/development/font packages were installed
  into the final runtime. Final-runtime ldd -r resolved both libraries completely.
- TurboJPEG's direct requirements were libc/loader with GLIBC requirements through
  2.14. Skia's established glibc/libstdc++/loader requirements resolved unchanged.
- The final container ran read-only, network disabled, two CPUs, 32 MiB /tmp tmpfs,
  memory and memory-swap both limited to 512 MiB.
- Two concurrent pipelines completed 60 operations in the final repeat in 15699 ms.
  Inputs were baseline/progressive 1632x2448 (3995136 pixels) JPEGs of 3020130 and
  2781404 bytes, plus a 960x1440 high-entropy PNG of 4156060 bytes. All stayed within
  the 5 MiB ingress limit and all outputs stayed within canonical limits.
- Reported process peak working set: 327778304 bytes (about 313 MiB).
- Reported cgroup memory.peak: 282492928 bytes (about 270 MiB).
- cgroup memory.max: 536870912; memory.events max/oom/oom_kill: all zero.

Process and cgroup measurements are distinct observations, not equivalent values.
This is bounded processor/native evidence, not a full application HTTP load test
or proof of distributed/per-process admission implementation. Both the initial
and repeat 60-operation runs passed without OOM; timing is not a formal benchmark.

## Reproduction And Files

Durable commands, artifact hashes and update instructions are in
`tools/cover-native/README.md`. The Dockerfile has final, inspection, missing-native
and tampered-native targets. Smoke.csproj is a test executable linking the real
image test sources; no production project or alternate processor was added.

Production changes are local to the image boundary, its two JPEG-specific helper
files, and API native packaging metadata. Supporting changes are the build helper,
license notices/harness, regression fixtures, and the approved dependency/native
documentation in AGENTS, Architecture and Feature 013. Existing earlier code and
dirty work were preserved. No HTTP/DI, frontend, schema/migration or remote Storage
resources were modified by this follow-up. Nothing was staged, committed or pushed.

Index fingerprint remains:
`62092d1ed1bbe35197e650619a83a5a9eed14a4495a4223a795223ed11372439`.

## Guarantee Limits And Remaining Scope

The integrated pipeline rejects the original 39-case corpus, but does not reject
the new AC-run corruption. Its strictness is therefore incomplete. Separately, JPEG
has no general integrity checksum: a byte edit that produces another valid image,
or removal of complete optional progressive refinements that leaves a valid
lower-precision image, cannot prove an unknowable original history. Metadata is
stripped from accepted rasters, not certified as authentic. This is not antivirus,
a process sandbox or proof that every possible native vulnerability is absent.

At this historical D3a stage, native loading/packaging/lifetimes/resources passed,
but the AC-run regression did not satisfy the earlier exhaustive-rejection policy.
Native load success did not cancel that failure. D3b subsequently approved a
different acceptance policy; its fresh verification is recorded above. Native
patch/security monitoring and full gate reruns after relevant changes remain required.
The remaining HTTP/DI/security/resource/Storage/UI/release work is still pending.
Do not enable uploads or treat this partial implementation as Phase 1 completion.
