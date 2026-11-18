# Feature 013 - Phase 1 Implementation Report

Updated: 2026-10-30

Status: **PARTIAL. Do not deploy or enable uploads.**

Operations/security follow-up, 2026-11-02: bounded whole-API Noble Linux execution
now passes 18 scenarios, and the full integration suite passes 994 cases including
16 new security regressions. Real Storage/hosting/monitoring/budget evidence still
keeps G03/G06 partial. The current status matrix and exact results are in the
[G06 report](g06-operational-verification.md); results below retain the Phase 1
snapshot and must not be mistaken for the current automated results.

Phase 2 follow-up, 2026-10-30: the separately authorized Angular implementation
has migrated the write contract and restored all existing browser regressions.
Current frontend/desktop/mobile results are in the
[Phase 2 report](phase-2-implementation-report.md). The frontend incompatibility
recorded below is historical evidence from Phase 1, not the current frontend.
Real Storage and whole-API resource/operations verification still keep Phase 1
partial; this follow-up does not authorize deployment or production uploads.

## Current Backend Integration

The HTTP/DI gap recorded in the historical sections below is now implemented and
verified. This is not Phase 1/release completion: real private-Storage permissions,
whole-API resource evidence and the frontend contract transition remain open.
G01 remains Passed under approved D3b; its processor, JPEG checks, pins and image
regressions were not changed by this integration task.

### Implemented

- Authenticated `GET /cover-uploads/options`, `POST /cover-uploads`,
  `GET /cover-uploads/{id}`, `GET/HEAD /cover-uploads/{id}/content`,
  `DELETE /cover-uploads/{id}` and `PUT /games/{gameId}/cover`.
- Composition registers the existing Core lifecycle service, clock, image processor,
  typed private Supabase Storage adapter and startup/15-minute hosted cleanup.
  The deterministic byte-store fake requires both the existing E2E authentication
  guard and Development/Test; unsafe fake configuration fails startup.
- `CoverUploads.Enabled` remains false by default. Missing/invalid Storage settings
  disable new uploads safely, without making the application fail startup or
  disabling ordinary metadata operations. Disabled new uploads do not disable
  saved-cover delivery, claims, removal or cleanup when Storage remains configured.
- Explicit `coverUpdate` intent and expected-reference CAS use the existing parser
  and Core implementation. Omission means keep; stale replacement returns 409.
  Legacy top-level cover fields, including null, return 400 with
  `code=cover_intent_required`; no compatibility alias was added.
- JWT validation requires a usable authenticated `sub`. Authorization and chained
  rate/admission limits execute before MVC/body parsing: five uploads/minute and
  twenty/hour/sub, two upload pipelines/process with no queue, 120 status/content
  reads/minute/sub and eight content reads/process.
- Streaming multipart accepts exactly one nonempty `file`, bounds actual/chunked
  bytes to 5 MiB plus 64 KiB overhead and applies a 60-second receive deadline.
  A resource filter prevents MVC form value providers from buffering the request
  before these checks. Kestrel 413 errors retain their status rather than becoming
  malformed-multipart 400 errors. Valid 32 KiB epilogues within the overhead budget
  are accepted; invalid/oversized bodies are bounded without a second body buffer.
- Content uses a fresh Core ownership/current-state check and bounded provider
  read on every GET/HEAD, including conditional/range requests. Responses and
  errors use `private, no-store`, `Vary: Authorization, Origin` and `nosniff`;
  there are no signed links, query JWTs, redirects, validators or 304/range bypass.
  Safe ProblemDetails include requestId and the applicable cover failure code.
  CORS exposes `Retry-After` and `Location` only to configured origins.
- The existing reservation/quota, transactional claim/retirement, immutable receipt,
  fresh-primary unknown-COMMIT recovery, Steam both-null fill and deferred physical
  deletion paths are reused, not duplicated. Cleanup resolves a fresh scope, runs
  at startup/every 15 minutes and emits safe failure/overdue-work warnings.
- The E2E reset helper now rejects real/foreign-namespace managed-cover data, resets
  matching fake metadata transactionally and clears fake bytes only after commit.

### Fresh Verification

Backend checks ran in the requested order: solution build, Core, images,
PostgreSQL lifecycle/migrations, complete non-image integration, EF model check,
Linux native verification, then whitespace. Frontend and an explicit Steam subset
were additionally run. No tests were skipped or weakened to obtain these results.

| Check | Current result |
| --- | --- |
| `dotnet build src/backend/GameLibrary.sln --no-restore` | Passed, zero warnings/errors |
| Complete Core suite | 337 passed, zero failed/skipped |
| Host image suite | 94 passed, zero failed/skipped |
| PostgreSQL migration/lifecycle subset | 39 passed, zero failed/skipped |
| Complete IntegrationTests excluding images | 884 passed, zero failed/skipped |
| New HTTP/config/transport coverage, included in the 884 | 171 endpoint + 9 transport/timer + 11 fake-configuration cases = 191 passed |
| Explicit Steam integration subset, included in the 884 | 77 passed, zero failed/skipped |
| EF `has-pending-model-changes` | No pending model changes |
| Fresh Noble amd64 native final image | 94 image cases passed; non-root 1654, no SDK, both libraries resolved by `ldd -r` |
| Separate ELF inspection target | Passed for both native libraries |
| Fresh missing-native and tampered-native images | Both failed unavailable safely without decoder fallback |
| Angular `npm test -- --watch=false` | 289 passed across 29 files |
| Angular `npm run lint` | Passed |
| Angular production build | Passed with budget warnings: initial 704.66 kB vs 700 kB; Random Picker SCSS 4.13 kB vs 4 kB |
| Angular E2E build | Passed |
| Full Playwright desktop/mobile suite | 10 passed, 4 failed, no retries/skips |
| `git diff --check` | Passed |

The integration project has **978 distinct passing cases** across the two full
filters (884 non-image + 94 image). The 39 relational, 191 new HTTP and 77 Steam
subset counts overlap these totals and must not be added again. Tests use the
local PostgreSQL 16 instance on port 5433 and isolated fixture/E2E databases, not
EF InMemory or a remote project. EF reports the existing tools/runtime mismatch
(10.0.7 vs 10.0.11). Vitest reports an existing removed `poolOptions` configuration
deprecation; neither tool configuration was opportunistically changed.

Playwright executed all 14 journeys against the integrated backend. Both desktop
and mobile fail VideoGame creation in the cover-search and Family Friendly
journeys at `e2e/app.spec.ts:105` and `:142`. The actual visible API error is
`Refresh the application and use coverUpdate to change a cover.` The unchanged
frontend form still sends the deliberately rejected legacy cover field, even
without a selected cover. Navigation, Play Log, management dialogs, picker history
and Steam linking/import pass on both viewports. This is an observed release
blocker, not a waived regression or permission to weaken backend contracts. No
Angular functionality, serializer or E2E assertion was changed by this task.

The fresh native final image ID is
`sha256:2a4b9a827c129b65d0bb99367449e67f5cf858a33aeab6b8735e60dd65fc18a1`.
It uses the unchanged pinned Noble SDK/runtime and exact native assets documented
in the G01 report. Two processor pipelines completed 60 bounded near-4MP
baseline/progressive JPEG and PNG operations in 7149 ms under 512 MiB:
process peak 291151872 bytes, cgroup peak 247787520 bytes, memory.max 536870912,
with max/oom/oom_kill events all zero. This is **processor-only**, not whole-API
worst-case upload/proxy load evidence. `BUILDX_GIT_INFO=false` bypassed a transient
Docker build metadata/worker timeout; no target/runtime/codec was substituted.

### Gate Assessment

| Gate | Current assessment |
| --- | --- |
| G01 native | Passed under approved D3b; fresh native/image verification passed again, implementation unchanged. |
| G02 Angular cropper | Not started; separately scoped frontend phase. |
| G03 security/proxy | Local HTTP/JWT/two-user/current-state/privacy/provider-bound tests pass. Real isolated Storage adapter and direct anonymous/browser-role denial evidence remain unverified; gate not complete. |
| G04 persistence/recovery | Local real-PostgreSQL evidence passes, including migrations/model, constraints, bootstrap, quotas/CAS/claims, lost COMMIT acknowledgement/retained receipts, namespace and cleanup/late-write cases. |
| G05 UX/E2E/manual | Not complete. Existing browser suite has the four recorded contract failures; managed-cover journeys and real iOS/Android camera/PWA/manual checks belong to the frontend phase. |
| G06 operations/resources | HTTP admission/body/rate/concurrency, real-socket slow/chunked deadline, cleanup startup/timer/retry, quotas and disabled-mode tests pass. Whole-API 512 MiB worst-case load, deployed minimum-data-rate/proxy settings, actual bucket/secret/monitoring/spend configuration and operational evidence remain pending. |
| G07 regression/closeout | Backend, Steam, frontend units/lint/builds, EF and whitespace pass. Playwright is not green and feature-wide acceptance/remaining gates are not complete. |

### Safe Configuration And Operations

Keep `CoverUploads__Enabled=false` until all applicable gates and the coordinated
frontend transition pass. Runtime Storage configuration is server-only:
`CoverUploads__StorageBaseUrl` is the trusted HTTPS Storage API base including its
`/storage/v1/` path; `CoverUploads__StorageCredential` is a server secret/service
credential supplied by secret configuration, never committed or returned;
`CoverUploads__Bucket` must be `game-covers`. `AllowLocalHttp` is false by default
and allows literal-loopback HTTP only with explicit Development/Test opt-in.
No actual values or provider resources were provisioned by this task.

Before enabling, an operator must record private bucket settings (JPEG only,
2097152-byte limit), prove direct browser-role read/write/delete/list denial and
execute the adapter contract against an isolated bucket through supported Storage
APIs. A private-bucket settings GET alone is not effective-permission evidence.
Do not edit `storage.*` through SQL or expose the server credential to Angular.
Apply application schema changes only through the included EF migration workflow.

Preserve the configured namespace and recorded immutable object locator. Credential
rotation is independent of namespace migration; never change namespace and assume
old rows can use the new provider. For uncertain Game writes, use the owned status
receipt plus authoritative Game state; do not retry Create blindly or delete bytes
because an exception was observed. For deferred cleanup, monitor safe overdue-work
warnings, provider/API errors, retained-byte quotas and egress/spend limits. Any
operator reconciliation must use the recorded namespace and supported Storage
APIs with a fresh no-reference/state check, never infer ownership from a URL.
Permanent provider failure or writes after tombstone purge require operator review;
bounded in-process cleanup is not a promise of absolute orphan elimination.

### Repository And Remaining Work

This integration task modified `Program.cs`, `Auth/ClaimsPrincipalExtensions.cs`,
`Controllers/E2eController.cs` and `appsettings.json`; it added the two cover
controllers and `CoverHttp`, `CoverMultipartReader`, `CoverMultipartAttribute` and
`CoverCleanupService`. It added `CoverHttpTestFactory`, `CoverHttpEndpointTests`,
`CoverHttpTransportTests`, `CoverHttpManualClock` and `CoverFakeConfigurationTests`.
This report, the README release warning and the native README evidence link were
updated. The existing Core/lifecycle/migration/Storage/image implementation was
reused. No new dependency, migration edit or specification decision was introduced.

All pre-existing staged/unstaged/untracked work, including Features 011/012 and
frontend changes, was preserved. No staging, commit, push, deployment, real bucket
or policy change occurred. The index fingerprint remains
`62092d1ed1bbe35197e650619a83a5a9eed14a4495a4223a795223ed11372439`.

**Final status: PARTIAL.** Remaining backend release evidence is the isolated real
Storage permission/adapter contract and full API resource/hosting/operations gate.
The separately authorized frontend phase must migrate explicit cover intents and
expected snapshots, implement the approved selector/cropper/Blob lifecycle, restore
browser-save regressions and execute G02/G05 plus final G07 closeout. None of these
gaps is resolved by passing processor-only tests or disabling new uploads.

## Historical Evidence: 2026-10-29

The following sections preserve earlier implementation results, including resolved
blockers. Statements about missing HTTP/DI and failed JPEG cases are historical,
not the current implementation status above.

Bounded-normalization approval, 2026-10-29: the user explicitly approved D3b and
the single AC-run regression outcome change. The processor and native checks/limits
are unchanged; other rejected categories remain rejected. **G01 Passed** fresh
verification under D3b: image tests 94/94 on host/non-root Noble amd64, Core 337/337,
backend build, native packaging/dependencies/asset-failure checks and bounded
two-pipeline resource verification. See
[the current G01 report](g01-strict-jpeg-implementation-report.md).
All blocker/review/test counts below are historical. Passed G01 does not complete
HTTP/DI, Storage integration or the rest of Phase 1; do not enable uploads yet.

JPEG compatibility review, 2026-10-29: see
[the focused technical review](g01-jpeg-compatibility-review.md).
It confirms that the AC sequence is nonconforming and that the library deliberately
tolerates this path without a warning. No configuration-only correction was found.
The review recommends an explicit acceptance-policy decision for this application's
maintenance budget; it does not approve changing the specification or regression.
At that review point G01 was Blocked. No selected solution was implemented by
the review; the later approved D3b outcome and fresh results are recorded above.

G01 follow-up, 2026-10-29: the authorized strict JPEG pipeline is now integrated
and fixed the original 13 cases, but initially left G01 Blocked by the AC-run
regression under the earlier rejection policy. D3b supersedes that policy, not the
native checks. See [the separate implementation evidence](g01-strict-jpeg-implementation-report.md).
The earlier results and blocker below are historical; HTTP/DI and the rest of
Phase 1 remain incomplete and were not implemented by that focused follow-up.

Subsequent focused G01 investigation: see
[Strict JPEG Validation Investigation](g01-jpeg-validation-investigation.md).
It preserves this pass's historical results, records fourteen additional
adversarial cases (three additional failures), and evaluates a structural plus
strict libjpeg-turbo prototype. That investigation preceded the subsequent
dependency approval and G01 implementation; Phase 1 is not complete.

The approved specification is unchanged. This report records implementation work
and failed verification, not approval to weaken image validation or change codecs.

## Historical G01 Blocker

The pinned SkiaSharp 4.154.0 decoder accepts JPEGs with nonempty but truncated
entropy-coded scans when an EOI marker is inserted after the truncated data.
`SKCodec.GetPixels` reports `Success`; independent scanline decoding also reports
`Success` and returns all 480 rows. Successful native decoding is therefore not
sufficient to enforce the specification's malformed/truncated JPEG rejection.

The regression uses two real 320x480 JPEGs generated by the pinned codec: colored
quadrants and seeded high-entropy pixels. Both complete originals pass processing.
It preserves headers/tables/SOF/SOS, keeps 1%, 10%, 50%, 90% or 99% of each entropy
scan at a byte boundary outside FF00 stuffing, discards the rest and inserts EOI.
All ten damaged inputs are incorrectly accepted on macOS and in the approved
native amd64 Noble runtime. Example retained scan lengths are 11/1115 bytes and
125417/126684 bytes.

The initial zero-entropy parser gap was fixed with structural scan checks. The ten
nonempty-truncation regressions still fail and remain active, not skipped or
weakened. Minimum byte-size, pixel-content or re-encoding-size heuristics cannot
prove JPEG entropy/MCU completeness. The public SkiaSharp API has no strict JPEG
option or per-decode libjpeg warning callback/count. Capturing global stderr is
not an acceptable concurrent-request validation boundary.

Per the approved specification's G01 stop rule, implementation stopped for review.
No alternate codec, native wrapper, custom entropy decoder, platform substitution
or relaxed acceptance rule has been introduced.

Reproduce the blocker:

```sh
dotnet test tests/backend/GameLibrary.IntegrationTests/GameLibrary.IntegrationTests.csproj --filter FullyQualifiedName~CoverImageProcessorTests
```

## Implemented Slices

- Core cover entity/rules/service, reservations, quotas, claims, expected-reference
  CAS, immutable first-claim receipts, retirement and bounded cleanup.
- Transactional integration with VideoGame/BoardGame create/update/delete, atomic
  both-null Steam cover fill, and managed IDs in Core/API read projections.
- Generated EF migration `20261029134813_AddCoverUploads`, designer and snapshot;
  exclusive Game references, restrictive FKs, uniqueness/lifecycle checks/indexes
  and conditional Data API privilege revocation.
- Strict JSON cover command parsing and game request contracts, including explicit
  legacy-field rejection. Existing backend request fixtures were adjusted for the
  approved breaking write-contract transition.
- Private Supabase HTTP Storage adapter, safe configuration/namespace validation,
  bounded transport and deterministic byte-store substitute.
- Partial image normalization with pinned SkiaSharp/Linux assets and NuGet lockfile.
  Positive format/orientation/color/alpha/normalization tests pass; strict JPEG
  corruption rejection does not.

These slices are not an integrated working Phase 1 API. In particular,
`Program.cs` does not yet register `CoverUploadService`, `ICoverStorage` or the
clock. Game services now require that dependency, so existing HTTP game operations
cannot be considered usable until composition is completed. New cover endpoints,
early upload admission/rate/body limits, proxy/no-store/error middleware and hosted
cleanup have not been wired. Cover exceptions are not yet mapped globally.

No Angular UI or save serializer was changed for Phase 1. Current frontend
serializers still use the old write contract and require the separately scoped
frontend transition. This partial backend must not be released independently.

## Verification Results

Actual results during this implementation pass:

| Check | Result |
| --- | --- |
| Full backend solution build | Passed, zero warnings/errors |
| Complete Core suite | 337 passed |
| New real-PostgreSQL cover lifecycle suite | 33 passed |
| Storage adapter tests | 80 passed |
| Cover JSON parser/contract tests | 219 passed |
| Image processor suite | 43 passed, **10 failed** |
| Combined parser/Storage/processor rerun | 342 passed, **10 failed**, none skipped |
| Native final-runtime processor smoke | 43 passed, **10 failed**, same JPEG cases |
| ELF inspection and final-runtime `ldd -r` | Passed |
| EF pending-model check | No pending changes |
| `git diff --check` | Passed |

The complete HTTP/PostgreSQL integration regression suite has not been rerun after
these changes. API composition is unfinished; an earlier Steam HTTP smoke failed
at startup on the missing cover-service registration. Frontend units/lint/build,
Playwright and real Supabase permission contracts were not run in this pass.
Historical milestone counts are not new evidence. EF tooling also reports the
existing tools/runtime version mismatch (10.0.7 versus 10.0.11).

Relational coverage includes two-user ownership, constraints, reservation/claim/CAS
races, expiry after lock waits, rollback, successful COMMIT followed by lost
acknowledgement, retained receipts, late writes, cleanup fairness, fixed retention,
monotonic pass budgets/nonoverlap and stale-tracked Steam refresh.

## Native Evidence

- SDK: `mcr.microsoft.com/dotnet/sdk:10.0-noble@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b`.
- Runtime: `mcr.microsoft.com/dotnet/aspnet:10.0-noble@sha256:de0ee358cdd672f7017a003bed21e9ac9ed48a27c922a59b18175b6e82fa7fee`.
- Explicit `linux/amd64`, RID `linux-x64`, native Linux x86_64, Ubuntu 24.04.5,
  framework-dependent .NET 10.0.14, non-root UID/GID 1654.
- Final runtime has no SDK or added inspection/codec/font libraries. Separate
  inspection stage uses `file`/`readelf`; `ldd -r` runs in the unchanged final image.
- Identical package/published/final `libSkiaSharp.so` SHA256:
  `25b0e8dcaf75444af01e0aa3b29286614ceb56adc80deadb511d3503896fd86b`.
- Under 512 MiB, two processor pipelines completed 40 operations without OOM.
  This is processor-only resource evidence, not the whole-API G06 gate.
- Full harness, Dockerfile, commands, measurements and investigation are retained
  at `/var/folders/fd/h4rhj_wn5_71qy5q79xm8r_m0000gn/T/opencode/feature013-native-smoke/REPORT.md`.

Passing fixtures cover actual JPEG/PNG/static WebP, all eight EXIF orientations,
Display-P3-to-sRGB conversion, alpha flattened white, canonical sizing/padding,
quality-85 JPEG, metadata/trailing-data stripping, APNG/animated-WebP rejection,
unsupported formats and several malformed/bomb cases. They do not cancel out the
ten failing strict JPEG cases.

## Remaining Work And Safety

Resolve G01 through investigation and an approved design decision before further
implementation. Then finish API composition/endpoints/resource limits/privacy,
run full regression verification, and record isolated real Storage permission
evidence. Bucket privacy checks alone do not prove anonymous/browser-role denial.
G02 and frontend/manual-camera parts of G05 remain outside Phase 1; other unexecuted
G03-G07 requirements remain pending, not waived.

No real Supabase bucket, policy or production schema was modified. No credentials
were printed or committed. Earlier migrations and unrelated dirty work were
preserved. Nothing was staged, committed or pushed. The index fingerprint remained
`62092d1ed1bbe35197e650619a83a5a9eed14a4495a4223a795223ed11372439`.
