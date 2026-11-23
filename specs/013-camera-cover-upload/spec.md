# 013 - Camera & Cover Upload

## Status And Authority

`Approved`

Design approved on 2026-10-28 after the user resolved the architecture decisions.
Approval means the specification is internally consistent and ready for a separate
implementation task. It does NOT mean code, native compatibility, tests, migrations
or Storage provisioning have been completed. No such work is performed in this pass.

Authority remains Product -> Domain -> Architecture -> Feature -> Implementation.
Product v0.7, Domain v0.9, Architecture v0.4 and AGENTS.md incorporate the narrow
Feature 013 exception. Historical URL-only/no-upload rules are reconciled there.
No unresolved architecture decision remains; implementation gates G01-G07 below
must pass before completion/release. Failed gates require investigation/review,
not silent dependency, platform or security substitutions. Features 011/012 files
and implementation remain unchanged in this documentation pass.

Subsequent focused implementation, 2026-10-29: the user approved libjpeg-turbo
3.2.1 JPEG prevalidation (D3a), then approved bounded JPEG normalization (D3b)
instead of exhaustive input conformance. G01 Passed fresh verification under the
approved policy (94/94 image tests on host/native Noble); see
[the G01 implementation report](g01-strict-jpeg-implementation-report.md).
Phase 1 remains partial; this does not pass the other gates or enable uploads.

## Objective

Allow an authenticated user to choose a device image or take a mobile photograph,
preview/crop/adjust it, and save it as the single cover of a VideoGame or BoardGame.
Keep manual URLs and explicitly triggered Feature 010 search available through one
cover-selection experience. Keep the application useful when uploads are disabled
or unavailable.

An uploaded cover is private user-owned media, not a public shared game asset. It
does not change game identity, ownership, acquisition, Family Friendly, playtime,
Play Log, filtering or Random Picker history/selection semantics.

## Reviewed Context

Reviewed AGENTS.md, the Product/Domain/Architecture specifications, and Features
006 Library, 008 UX/PWA, 010 Cover Image Search, 011 Steam Integration and 012
Family Friendly, including the actual current implementation.

| Existing behavior | Design consequence |
| --- | --- |
| Angular 22.1 / TypeScript 6, signals and local state | No new state-management layer; the cropper must support the current Angular version. |
| Both game forms use `features/games/cover-image-search/CoverImageSearch` | Extend that implementation into the unified selector; do not add a competing uploader/page. |
| Parent forms own the manual URL; search selection is not a save | Move the existing URL control into the extended selector, with the parent still owning the cover draft and final save. |
| Add has no Game ID; Add/Edit persist only on Create/Save | Keep the cropped Blob local until final form submission, then stage and attach it. Never silently create a Game to obtain an upload target. |
| `Game.CoverImageUrl` is currently the only persisted cover data | Add trusted managed-object identity/lifecycle metadata; never infer deletion ownership from a URL. |
| Library, management, Picker and Play Log display current Game cover URLs | Add the managed upload ID to each read model and fetch its bytes through the authenticated proxy; retain external URLs unchanged. |
| JWT identity comes from validated Supabase `sub`; Angular has no application-table access | All upload, claim, cancel and content authorization stays in ASP.NET Core/Core. |
| Steam enrichment fills a null cover and preserves a non-null external cover | Feature 013 implementation must also check managed-cover identity atomically; no Steam implementation/file change occurs in this documentation pass. |
| Deletion is an EF transaction removing LibraryEntry and Game | Journal object retirement in that transaction; Storage cannot join a PostgreSQL transaction. |
| Existing providers use typed HttpClient plus Core interfaces and deterministic fakes | Use one cover-specific Storage adapter and a fake; no generic media framework or provider registry. |
| PWA caches the shell, not offline mutations | Local drafts are volatile; no offline upload queue, image database or synchronization. |

Important inspected files:

- `src/frontend/src/app/features/games/cover-image-search/cover-image-search.{ts,html}`
- VideoGame and BoardGame form, management-list and service files.
- `src/frontend/src/app/core/auth/token.interceptor.ts`
- `src/backend/GameLibrary.Api/Program.cs` and game/CoverImages controllers.
- `src/backend/GameLibrary.Core/Games/{Game,VideoGameService,BoardGameService}.cs`
- `src/backend/GameLibrary.Core/{Libraries,RandomPicker,PlayLog}` read projections.
- `src/backend/GameLibrary.Core/Steam/SteamIntegrationService.cs`
- `src/backend/GameLibrary.Core/Data/GameLibraryDbContext.cs`

Existing null-cover placeholders must also handle failed image loading in affected
cover displays. Some current displays lack an error fallback; fixing their cover
rendering is directly necessary for authenticated private images, not a page redesign.

## User Stories

1. As a desktop user, I can select a JPEG, PNG or WebP from my device for either
   game type without uploading it immediately.
2. As a mobile/PWA user, I can request a new photograph using the rear camera and
   fall back to file selection if capture is unsupported or canceled.
3. I can inspect a preview, crop to portrait proportions, zoom, reposition, rotate
   by 90 degrees or fit the whole image before choosing it.
4. I can add a game and its chosen uploaded cover without first saving a coverless
   placeholder game.
5. I can replace only an existing cover without resubmitting unrelated metadata.
6. I can replace an uploaded cover with another upload, an external URL or a search
   result, or explicitly remove it.
7. A confirmed failed upload/save or cancellation before Game-write dispatch leaves
   my saved cover unchanged and preserves edits for retry where possible. If the
   Game-write outcome is unknown, I can reconcile it without a blind replacement.
8. My private photos, including uncommitted uploads, cannot be listed, claimed,
   retrieved or deleted by another user through the application.

## Final Architecture Decisions

These decisions resolve the user's approved direction and supersede earlier draft
alternatives, including client-visible signed URLs and equality-inferred intent.

| ID | Approved decision | Boundary / verification |
| --- | --- | --- |
| D1 | Private Supabase Files bucket `game-covers`; backend upload only | No direct browser Storage grants; separate environment namespaces. |
| D2 | Authenticated ASP.NET Core byte proxy; no client-visible signed URLs | JWT and Library/state checks on every request; private,no-store; no redirect/capability URL. |
| D3 | SkiaSharp 4.154.0 plus matching Linux NoDependencies assets | Conditional on G01 native verification; sanitization is authoritative server-side. |
| D3a | libjpeg-turbo 3.2.1 for JPEG prevalidation only, authorized 2026-10-29 | Bounded structural prevalidation, full STOPONWARNING decode, then unchanged Skia normalization; pinned official binaries/public C API, no warning suppression or general media layer. |
| D3b | Bounded JPEG normalization policy, authorized 2026-10-29 | Accept unreported decoder-tolerated coefficient anomalies only after all existing structure, diagnostics and limits pass; no exhaustive T.81 conformance claim. Retain the AC-run fixture with an explicit fresh-canonical-output expectation. Other rejected categories stay rejected. |
| D4 | ngx-image-cropper 9.1.7, lazy-loaded within the existing selector | Conditional on G02 Angular 22 integration, accessibility, mobile and bundle checks. |
| D5 | 5 MiB maximum uploaded file | Pre-buffer request/admission limits and independent decoded/output bounds. |
| D6 | One EF `cover_uploads` table and nullable Game.CoverUploadId | Immutable namespace and first-claim receipt in the same table; no media framework. |
| D7 | Game.CoverImageUrl remains exclusively external | Managed references are IDs, never binary/Base64/proxy/signed URLs in that field. |
| D8 | Explicit user choice takes precedence over Steam | Atomic enrichment only when both current references are null. |
| D9 | Immutable claimed_game_id retained for bounded creation recovery | Survives cover retirement/Game deletion; no general exactly-once CRUD claim. |
| D10 | Optimistic expected-reference compare-and-swap | Stale explicit change is 409; no extra Game version column; lifecycle row locks preserve atomicity. |
| D11 | Explicit coverUpdate intent: keep/external/upload/remove | Omission means keep; null/malformed is 400; never infer clear or intent from field equality. |
| D12 | Bounded API-hosted cleanup, retries and tombstones | No provider call inside DB locks; fair scheduling, safe reference checks, finite late-write caveat. |
| D13 | Per-Library quotas plus provider monitoring/spend controls | Not an application-enforced global/organization spending cap. |
| D14 | No HEIC conversion, AI, galleries or sharing | One active cover; existing manual/search workflows remain. |
| D15 | Ubuntu 24.04 Noble/glibc, linux/amd64, RID linux-x64, conventional .NET 10 container | Concrete image/native strategy below; source support is not a passed runtime smoke test. |

Local editing uses 2:3 output, BoardGame Fit whole image by default, and 90-degree
rotation. Removal saves no cover and no hidden fallback; a later explicit Steam
operation may enrich an otherwise coverless game. The limits/lifecycle in this
document are approved design parameters, not evidence of implementation.

## Image Selection And Editing

### Selected Cropper And Alternatives

Research checked on 2026-10-28:

| Option | Evidence | Assessment |
| --- | --- | --- |
| ngx-image-cropper 9.1.7 | Published 2026-08-28; peers Angular common/core `>=17.3.0`; MIT; `tslib` only | Selected subject to G02 for fixed aspect, scale/movement, keyboard controls, rotation and Blob output. |
| CropperJS 2.3.1 | Published 2026-10-16; active framework-neutral 2.x; MIT; additional `@cropper/*` packages | Reasonable alternative, but requires more Angular lifecycle/state/accessibility integration. No Angular peer range is not an Angular support guarantee. |
| Native Canvas only | No added package | Good encoding primitive, not a complete accessible touch/keyboard crop UI. More application-owned gesture, boundary, orientation and focus code than justified here. |

Published unpacked package sizes are approximately 546 kB for ngx and 557 kB for
CropperJS itself. These are NOT production JavaScript transfer sizes and exclude
CropperJS dependency footprint. Implementation must measure lazy-chunk and initial
bundle impact rather than claiming an unmeasured compressed size or raising budgets.
Pin approved versions and lockfiles after a compatibility/security check; do not
install anything during specification preparation.

Review verification: the npm manifest for 9.1.7 actually declares common/core
`>=17.3.0`, so the repository's locked Angular 22.1.2 satisfies its peer constraints.
That establishes metadata compatibility, not a tested Angular 22 integration,
zoneless interaction, accessible crop UI or measured lazy-chunk size. Those checks
remain implementation/release gates; no package was installed for this review.

### Editing Rules

- Portrait crop uses a fixed width:height ratio of 2:3 with zoom and reposition.
- Provide Fit whole image as an explicit alternative, padding with white rather
  than cutting artwork. BoardGames initially use Fit; VideoGames initially use Crop.
- Both modes export a 2:3 raster. Padding does not imply enlarging source pixels.
- Automatically honor camera orientation. Include a labeled Rotate 90 degrees
  action because EXIF alone cannot correct every sideways photograph. Reset restores
  the initial orientation/crop. Exclude free-angle rotation, flips and perspective.
- Maximum output dimensions: 960 x 1440. Do not upscale image content. Require at
  least 160 x 240 output; explain if the selected image is too small.
- Normalize cropper export rounding rather than requiring floating-point equality:
  accept at most one pixel of height difference from `round(width * 3 / 2)` after
  orientation. Canonical output uses integer dimensions `2*k` by `3*k`, with
  `80 <= k <= 480`, without enlarging content or adding another artistic crop.
  Pad unavoidable rounding gaps with the chosen white background. Materially
  different ratios fail validation. Test odd-sized crop selections and rotation.
- Export a JPEG Blob at approximately quality 90 in the browser. The backend
  independently normalizes it. Do not keep Base64 copies in application state.
- The selected image, original photograph and working image remain local until the
  user submits the game form. No EXIF/GPS/original image is deliberately uploaded.
- Selection limit before crop: 20 MiB and 24 million pixels, maximum edge 8192.
  Check byte size first, validate dimensions as soon as known, reduce the working
  image to an edge no larger than 2048 before giving it to the cropper, and release
  original decode resources. This is a client UX/resource bound, not server security.
- Browser decoding may temporarily allocate the full source despite resizing.
  Handle allocation, decode, canvas/export and orientation failures gracefully;
  a successful desktop crop is not proof of mobile memory safety.
- JPEG/PNG/static WebP selection is supported. Reject SVG, GIF, HEIC/HEIF, AVIF,
  documents and videos with clear instructions to export a supported image. Do not
  add an HEIC transcoder in this feature. A camera that supplies unsupported HEIC
  must degrade to that message and normal file selection, not an application crash.
- Revoke old object URLs and close image bitmaps on replacement, cancel, navigation,
  successful save and component destruction. No Blob/local path is a persisted URL.

The server can validate only the submitted raster, not prove the format/history of
an original source that was cropped locally. Security applies to uploaded bytes.

### Backend Processor Evaluation

No suitable cross-platform .NET image decoder is built into this project's stack.
Do not use System.Drawing.Common, which is Windows-specific.

Select SkiaSharp 4.154.0 and SkiaSharp.NativeAssets.Linux.NoDependencies 4.154.0
in GameLibrary.Api, conditional on G01. This is a bounded single-output operation,
not an on-demand transformation service. No System.Drawing or shell utilities.

G01 follow-up authorization, 2026-10-29: add libjpeg-turbo 3.2.1 exclusively for
JPEG prevalidation. The investigation reproduced Skia's successful recovery
of truncated entropy and implicit Huffman-table repair. Keep SkiaSharp: first
validate bounded segment/table/component/scan structure, then completely decode
through TurboJPEG's public C API with STOPONWARNING enabled, and only then perform
the existing Skia orientation/color/resize/canonical encoding. Reject every native
warning/failure; no default MJPEG tables or structural-validation bypass may authorize storage.
Pin official artifacts/extracted hashes, include IJG/BSD notices, use deterministic
SafeHandle/native-buffer disposal and checked allocation bounds. Missing or
incompatible libraries are unavailable, not a validation bypass. Do not introduce
an entropy decoder, generic parser/media framework or change the deployment target.
This approval does not itself pass G01; see the separate implementation evidence.

Later policy authorization, 2026-10-29 (D3b): the application requires bounded
normalization, not exhaustive JPEG specification conformance. A complete supported
JPEG may contain a decoder-tolerated nonconforming coefficient sequence, such as
an AC run beyond coefficient 63, that produces no decoder warning/error. It may be
accepted only when all existing structural/table/scan checks, full STOPONWARNING
decoding and size/dimension/memory limits pass. This is deliberate compatibility,
not a declaration that the source is standards-compliant. The source AC-run
fixture is retained and renamed to assert fresh bounded canonical JPEG output,
different from the original and accepted again by the same canonical pipeline.

This supersedes the exhaustive malformed-input/no-repair promise only for such
unreported coefficient tolerance. It does NOT broaden acceptance to truncated
files, malformed headers, invalid/implicit tables, animations, unsupported formats,
decoder diagnostics or resource-limit violations. Do not suppress warnings,
catch validation failures as success, special-case input bytes or alter limits.
Only freshly decoded/Skia-re-encoded output may be stored, never original bytes.
Native codec bugs, visual corruption and other unreported nonconformance remain
residual risks; patching, fixed assets and resource/ownership boundaries remain
mandatory. Neither valid output nor a green suite proves an authentic original,
antivirus coverage, full standards compliance or immunity to native vulnerabilities.

Review verification: its published NuGet manifest contains a net10.0 dependency
group, matching this API's target framework. That group references macOS/Win32
native assets, not Linux assets. A matching Linux package exists, but existence of
`SkiaSharp.NativeAssets.Linux.NoDependencies` is not evidence that it runs on every
Linux image, architecture or libc. The production target is now selected below,
but binary/codec behavior was unverified at the initial design review. No container
or native compatibility spike ran in that specification-only pass. Subsequent
D3b/G01 executable results are recorded separately in the implementation report.

The two selected dependencies solve different current requirements: the browser
cropper provides interaction; the server decoder validates and sanitizes untrusted
bytes. Client cropping cannot replace server validation. Canvas can replace the
frontend dependency only with equivalent accessible interaction work; avoiding the
server decoder by trusting browser-produced JPEGs is not an acceptable simplification.

ImageSharp was considered but is not selected: its Six Labors Split License is not
universally permissive. Do not silently substitute it or broaden OS packages if
the selected processor fails its gates; stop for design review.

### Production Runtime And Native Strategy

Approved test-infrastructure exception, 2026-11-23: the combined whole-API verifier
may use 768 MiB memory and memory+swap (no additional swap), independently enforced
against a 640 MiB cgroup peak threshold through verifier teardown. API-only initial
and production memory verification remains 512 MiB as specified below. G01 and both
native negative controls remain at 512 MiB. Diagnostic 512/768/1024 profiles cannot
replace required CI acceptance. Missing/invalid metrics, wrong effective limits,
memory/swap limit or OOM events, nonzero exit and incomplete workload fail closed.
All 94 image cases, 60 operations, two negative controls and 18 API cases remain.
This is approval to implement locally, not evidence of sufficiency or a passed G06.
See [the resource decision](native-runtime-resource-decision.md).

| Setting | Selected target |
| --- | --- |
| Runtime image family | `mcr.microsoft.com/dotnet/aspnet:10.0-noble`, conventional/non-chiseled Ubuntu 24.04 |
| Build image family | `mcr.microsoft.com/dotnet/sdk:10.0-noble` |
| OCI platform / .NET RID | Explicit `linux/amd64` / `linux-x64`; glibc, not musl |
| Publishing | Ordinary framework-dependent .NET 10; no Native AOT, trimming or single-file publish |
| Native asset | NuGet's `runtimes/linux-x64/native/libSkiaSharp.so` from the matching NoDependencies package |
| JPEG prevalidation asset | Official libjpeg-turbo 3.2.1 amd64 library, pinned release/extracted SHA256, public TurboJPEG C ABI; no system-library fallback |
| Execution | Non-root API process, bounded memory/admission; an initial 512 MiB container budget must pass load/memory verification |
| Process lifetime | Conventional long-running container so cleanup can run; downtime pauses cleanup until restart, not a separate durable worker guarantee |

The official image tables support the Noble family/platform. Select the platform
explicitly even on an ARM developer machine. At implementation, record the resolved
supported .NET patch/image digests and exact package lockfiles; mutable tags are
not immutable verification evidence. Do not create a Dockerfile in this docs pass.

The selected native package documents glibc/loader libraries, including libc,
libpthread, libdl and libm, and excludes fontconfig. The official Noble runtime
dependency image includes libc6, libgcc-s1 and libstdc++6; the latter two are base
image dependencies, not an assertion they are direct Skia NoDependencies imports.
Do not install fontconfig, GUI/GPU stacks, image tools or codec development packages
preemptively. Use NuGet RID resolution, not host-copied native binaries or the
musl/ARM asset. Do not add a competing Linux native-assets package.

G01 requires artifact/ELF inspection, ldd/symbol compatibility and actual codec
tests in the final non-root image on native amd64 Linux, without SDK/host libraries
masking dependencies. Emulation-only success on an ARM machine is not the release
gate. Any unexpected ABI/library/codec failure blocks completion; source-level
TFM/tag/asset compatibility is not a claim these executable checks already passed.
file/readelf may inspect the identical published native artifact in a separate
test/inspection stage; ldd and managed/native smoke must run in the unchanged final
runtime image. Do not add inspection/build libraries to production to mask failures.

## Storage Architecture

### Provider And Access Model

Use a dedicated private bucket named `game-covers`, separate per environment.
Only the API writes, reads, lists and deletes objects through the Storage API.
Angular continues to use supabase-js for Auth, not direct Storage mutations.

Public buckets would preserve simple permanent URLs but allow anyone with a URL to
retrieve user photographs; unpredictable names are not authorization. Reject them
for this private-library feature. Authenticated API byte delivery is selected.
Signed delivery was rejected after AR03: token expiry is not a CDN revocation
guarantee. Do not generate signed upload/download URLs or expose Storage URLs to
clients for managed images, including in error responses or redirects.

Brief provider comparison:

| Provider | Benefits | Costs / reasons not selected |
| --- | --- | --- |
| Supabase Storage | Existing provider/account; private buckets, authenticated reads, REST API and local development support | Storage and egress quotas, provider-specific access configuration; Auth reuse does not remove the need for API ownership checks. |
| Cloudflare R2 Standard | S3-compatible private storage; no direct Internet egress charge; useful at higher read volume | Another account/credential/deployment integration and signing implementation; no existing project integration. Lower egress pricing alone does not justify this operational addition at MVP scale. |

Do not add a generic interchangeable-storage framework. A narrow Core interface
for cover-object write/read/delete and its typed HttpClient implementation in Api
are sufficient and follow the current provider-boundary pattern. A whole Supabase
backend SDK is not required solely for these operations; verify current REST auth
requirements when implementing the adapter.

### Upload Versus Signed Direct Upload

Choose backend multipart upload for files at most 5 MiB. It permits authentication,
rate/admission limits, actual byte validation, orientation, metadata stripping and
canonical encoding before any provider write. It is consistent with the existing
ASP.NET Core authorization boundary and standard small-file upload guidance.

Signed direct upload would reduce API ingress, but users could place unvalidated
bytes in Storage. Secure support would need quarantine, completion verification,
server download/re-encode, token expiry/overwrite handling and more cleanup states.
Do not add that complexity or resumable/TUS infrastructure for these small covers.

### Paths And Credentials

Server-generated immutable paths:

```text
<libraryId:N>/<coverUploadId:N>/cover.jpg
```

Both IDs come from server-owned records. Never use a supplied filename, user email,
game name, path, bucket or URL as an object key. Object keys are internal, not an
upload permission. An asset cannot be shared or transferred between Libraries or
attached to two Games.

- Backend Storage credentials are server-only secret/service-role credentials,
  configured through user-secrets/environment/deployment secrets. A credential that
  bypasses Storage RLS makes Core ownership checks mandatory on every operation.
- No Storage `SELECT`, `INSERT`, `UPDATE`, `DELETE`, listing or signing grants/policies
  may give `anon` or browser `authenticated` users access to this bucket. Audit any
  existing broad policies rather than assuming a new bucket is isolated.
- Storage RLS/default-deny is separate from the unchanged application-table security
  model. Do not add direct Angular database access or modify auth/storage schemas.
- Provision bucket privacy, `image/jpeg` allowlist and 2 MiB object limit through
  supported Storage APIs/deployment setup, not direct SQL against storage.objects.
- Use Storage APIs for deletion; deleting storage metadata through SQL can orphan
  the actual object. Never let EF manage Supabase's storage schema.
- Use new keys with overwrite/upsert disabled. Never replace bytes in an existing
  key; immutable objects avoid stale-cache replacement and simplify cleanup races.
- Bind every object's key to an immutable trusted Storage namespace recorded with
  the upload: canonical configured Storage API base plus bucket. A key alone is
  not a complete locator. Reads/deletion must match that recorded namespace to
  an explicitly configured trusted adapter; never reinterpret it through a new
  bucket/base URL, follow arbitrary record URLs, or forward credentials on redirects.
  Namespace mismatch disables affected operations and alerts the operator; a
  missing object in a different bucket is NOT successful cleanup. Credential
  rotation within the same namespace is allowed. Changing project/base URL/bucket
  while records exist requires a reviewed migration/drain plan, not a config edit.

### Retrieval And Privacy

Persist object identity, not a delivery URL. Relevant VideoGame, BoardGame,
Library, Random Picker and Play Log responses return the external field unchanged
and add the nullable managed ID. Managed example:

```json
{
  "coverImageUrl": null,
  "coverUploadId": "00000000-0000-0000-0000-000000000001"
}
```

For external covers, coverUploadId=null and coverImageUrl is the persisted HTTP(S)
URL. For no cover both are null. Never put the proxy path, a Blob URL or a Storage
URL into coverImageUrl. No coverImageUrlExpiresAt or signed-read renewal contract
exists in the final design. Ordinary metadata responses perform no Storage read
and therefore remain available during a Storage outage.

The managed renderer requests `GET /cover-uploads/{id}/content` through Angular
HttpClient with responseType=blob. The existing API-origin interceptor supplies
the Bearer JWT. Native img does not: never set its src directly to that protected
endpoint. After success, create a component-local object URL for the returned JPEG.
External URLs continue to use the existing direct-image behavior, not this proxy.

For EVERY content GET, before provider access or response bytes, the API validates
the JWT/usable sub and reads fresh primary-database authorization data. Access is
allowed only for the caller's Library and either a nonexpired Ready upload or an
Attached upload currently referenced by its caller-owned Game, matching the claim
receipt. Uploading, expired, retired, purged and foreign resources return generic
404; invalid/expired JWT is 401. No identity comes from an object path or query token.
State and even a previously valid ID are not sufficient authorization.

The API downloads only that recorded immutable object through the configured
private Storage adapter using server credentials, never the user's JWT. Do not
redirect, mint a signed link, proxy arbitrary URLs, or expose provider error bodies,
paths or credentials. Verify success/type and bounded actual body length before
returning canonical JPEG bytes: at most 2 MiB and the recorded byte count. Truncated,
oversized or unexpected provider responses fail safely; do not forward arbitrary
Content-Type, Content-Disposition, cache headers or payloads as images.

Success uses 200, Content-Type=image/jpeg, accurate Content-Length, fixed inline
filename cover.jpg and X-Content-Type-Options=nosniff. The content route, metadata
routes and their errors use `Cache-Control: private, no-store` and
`Vary: Authorization, Origin`; CORS is limited to the existing explicit app origins.
Apply this policy before authorization/model-binding failures can short-circuit
the controller, including JWT challenges and overload/validation errors.
JWTs never appear in URLs/cookies for this purpose. No API output cache, shared/CDN
cache, service-worker data cache, browser persistent image store, or disk image
cache is introduced. Reverse proxies must respect the no-store contract.

Do not issue ETag/Last-Modified validators or return 304. Ignore conditional/range
headers and return the full bounded 200 only after authorization; no unauthenticated
cache-validation fast path or partial-image service. HEAD is not a separate
public route; if supported by framework routing it must run identical checks and
no-store headers, without a response body. Provider caching can never bypass the
API's owner/state decision because clients receive no provider capability URL.

Fetch only visible/near-visible managed images, with at most four concurrent
frontend content requests. Deduplicate in-flight requests only within the same
authenticated session and upload ID; do not retain a shared completed-image cache.
Each visible component may hold its already-fetched Blob/object URL. Revoke it and
invalidate pending responses on source change, logout/account change and destruction.
After invalidation a new fetch requires current authorization. On 404/503/429 or
image error show a placeholder and explicit retry; honor Retry-After without an
automatic polling loop. A 401 uses normal session-expired handling. In Picker
history only image presentation changes, not history order, shown IDs or states.

Authorization applies when a request is admitted: an already authorized in-flight
fetch may finish after retirement, token expiry or logout. Already downloaded or
copied bytes cannot be recalled. Subsequent requests must revalidate and deny a
retired/foreign resource or invalid JWT. Frontend cleanup is best effort, not DRM;
no claim of revoking copies or checking deleted Auth sessions beyond the existing
validated-JWT model is made. These limits do not expose a reusable Storage link.

### Configuration And Backend Boundaries

Use a `CoverUploads` configuration section with Enabled defaulting to false,
trusted HTTPS StorageBaseUrl, server-only StorageCredential, and Bucket defaulting
to game-covers. Numeric limits in this specification are application defaults, not values
the client may override. Use a separate bucket/environment credential for tests;
never point deterministic E2E mode at production Storage.

Validate the configured base as an absolute trusted API origin/path with no userinfo,
query or fragment; credentials must not be embedded in URLs or namespace values.
Do not disable certificate verification or forward credentials across redirects.

For an opt-in real local Supabase contract test only, permit HTTP on literal
loopback addresses through an explicit Development/Test-only AllowLocalHttp flag.
It is rejected for Production and for non-loopback hosts; do not weaken production
TLS or use a production credential for localhost. This resolves the conflict with
the local CLI's normal HTTP endpoint without making arbitrary HTTP Storage valid.

Deployment must verify bucket privacy and effective permissions before enabling
uploads. An unexpectedly public/misconfigured bucket disables managed upload/proxy
operations with a safe unavailable response and operator alert, not a public-URL
fallback or application-wide startup failure. Do not change global Storage policies
or existing buckets on a user request.

Api owns multipart transport, early authentication/authorization, configuration,
HttpClient Storage access and the concrete image-decoding adapter. Core owns cover
rules, ownership, quotas, lifecycle, claim/retirement and EF persistence. Pass a
bounded stream/byte representation across that boundary, not IFormFile or HTTP
ProblemDetails into Core. The cleanup host in Api invokes the same Core lifecycle
logic in scoped operations. Retain exactly the two application projects.

Enabled controls accepting new upload POSTs. Disabling new uploads does not clear
references, invalidate an otherwise live Ready claim, disable metadata/retirement,
or intentionally break existing proxy reads/cleanup when their private Storage
configuration remains valid. Missing/bad configuration makes content unavailable
and retains deletion work for retry; metadata-only keep and removal still work.
Claiming an already verified live Ready upload does not perform a new provider read.

Storage's service-created owner_id metadata is not the authenticated user's Library
ownership proof. Authorize with application-owned cover_uploads/Library/Game records,
even when a provider operation uses an unrestricted server credential.

### Costs, Quotas And Operations

Prices checked on 2026-10-28, USD, subject to change and organization-wide usage:

- Supabase Free currently includes 1 GB file storage, 5 GB origin egress and 5 GB
  cached egress. These are not dedicated to Game Library covers.
- Pro starts at $25/month with 100 GB storage, 250 GB origin egress and 250 GB cached
  egress; published overages are $0.0213/GB-month, $0.09/GB origin egress and
  $0.03/GB cached egress. Compute, other services and taxes may add costs.
- R2 Standard currently lists 10 GB-month free storage, then $0.015/GB-month,
  Class A $4.50/million and Class B $0.36/million operations after included quotas;
  direct Internet egress is free. Other connected services may still charge.
- The feature uses no paid Supabase image-transformation product. One preprocessed
  JPEG is stored per active cover; staging and retired objects temporarily add usage.
- An example 1,000 covers averaging 250 kB occupies roughly 250 MB; 20 full reads
  of each is roughly 5 GB delivered, before cache effects. This is an illustration,
  not a measured image size or billing guarantee.
- Proxy delivery also consumes API-host bandwidth: Storage -> API and API -> browser
  are separate metered paths. Do not assume cached-egress pricing or free API egress.
  Include provider origin egress, API memory/requests/egress and cleanup operations
  in monitoring and the deployment budget; no paid plan/overage is enabled here.
- Application quota: 250 MiB managed objects/reservations per Library and
  at most ten unexpired Uploading/Ready uploads. Pending deletion still counts
  conservatively until its lifecycle record is safely purged after confirmed
  deletion/retention. No limit is added to game count or external covers. Report
  quota exhaustion with retry/removal guidance.
- Admission/quota reservations are serialized in PostgreSQL so concurrent uploads
  cannot bypass them. Per-bucket object maximum is 2 MiB, below provider plan limits.
- Verify current plan/global bucket limits and spend-cap behavior before enabling.
  Monitor storage, egress, rejected uploads and oldest cleanup age; do not enable
  paid overages or create provider resources in this specification pass.

The per-Library quota is NOT a project/organization spending cap: several Libraries
can exceed a Free plan's shared quota. Enabling uploads requires an owner-approved
plan/egress budget and provider spend controls where available. A global application
quota would need a separate concurrency-safe design; do not claim one exists or add
a global quota framework silently. Retaining deletion tombstones conservatively
charges application quota until safe purge even after physical deletion. This
conservative accounting is selected; outages can extend the window. Explain it in
quota guidance rather than pretending deletion immediately releases reservations.

## Data Model

### Why CoverImageUrl Alone Is Insufficient

A URL does not prove object ownership, identify a managed object's namespace or
track abandoned/retired uploads. Parsing a URL to decide what
to delete could delete somebody else's object or attempt arbitrary remote deletion.

Keep `Game.CoverImageUrl` for external manual/search/Steam URLs. Add one nullable
`Game.CoverUploadId` referencing the narrow table below. At most one is non-null.
The response keeps coverImageUrl exclusively external as well. The managed renderer
uses coverUploadId to request protected bytes; its local Blob URL is never input
to external-URL persistence.

### cover_uploads

| Field | Type / purpose |
| --- | --- |
| id | UUID, server-generated PK and opaque upload handle. |
| library_id | Required FK to the authenticated user's Library, indexed for owner/quota lookup. |
| storage_namespace | Required bounded text, derived from trusted configured Storage API base and bucket; immutable, contains no credentials. |
| object_key | Required bounded server-generated Storage key; immutable and unique within its recorded namespace. |
| byte_size | Positive bounded canonical byte count; reserves quota before writing Storage. |
| state | Required constrained operational state: Uploading, Ready, Attached, DeletePending. |
| created_at | Required server UTC timestamp. |
| cleanup_after | Required UTC deadline for Uploading/Ready/DeletePending; null for Attached. Also drives bounded retry selection. |
| retired_at | Nullable server UTC timestamp for DeletePending retention/reconciliation. |
| claimed_game_id | Nullable UUID recording the first successful claim, written atomically with Game persistence; immutable once set, retained through cover retirement/Game deletion. Not a restrictive Game FK. |

Game's nullable FK uses delete restriction and a filtered unique index when non-null
so one upload cannot back two Games. Add the either-external-or-upload check. Index
cleanup eligibility on cleanup_after for non-attached states; this has a concrete
bounded-maintenance query, unlike a speculative standalone boolean index.

Use UUIDs for identifiers, an integer for byte_size bounded to 1..2097152, a
constrained varchar state, and timestamp-with-time-zone lifecycle dates. Enforce
allowed states and these row-local shapes: Uploading/Ready have cleanup_after and
no retired_at/claimed_game_id; Attached has claimed_game_id and neither lifecycle
deadline nor retired_at; DeletePending has cleanup_after and retired_at, optionally
claimed_game_id. Deadline/retirement dates cannot precede created_at. Do not put a
moving `now()` expiry test into a CHECK constraint; check expiry under lock in Core.

The current reference remains Game.CoverUploadId. claimed_game_id is a narrow
single-consumption recovery receipt, not a second current association, image history
or permission to reattach. It has no Game FK because Game deletion must not erase
the recovery evidence or be blocked by it. For Attached, Core must keep it equal to
the unique referencing Game ID. DeletePending may retain it after that reference
is gone. No extra ownership column on Game or generic operation-receipt table is
needed. Use the existing Library ownership path and owner-scoped Game lookup when
reporting whether that claimed Game still exists.

Index library_id for actual owner/quota queries, enforce uniqueness on
(storage_namespace, object_key), and order the due-cleanup index/query consistently
by (cleanup_after, id) for non-attached states. Cross-Library ownership and legal
transitions remain authoritative Core rules, not purported cross-table CHECKs.

Core enforces that a Game's Library matches the upload's Library and that only a
live Ready upload may first attach. Attached and DeletePending states are never
recycled into a new Ready upload or transferred to another Game. Metadata fields
such as provider catalogs, original filenames, EXIF, source photographs, crop
history, multiple renditions and image binaries are not persisted.

Use a restrictive Library FK rather than cascading away object locators. There is
no new Library/account-delete UI in this feature; future/operator Library deletion
must retire and remove its managed assets first. Do not silently leak objects by
deleting lifecycle records before Storage cleanup.

All application metadata/constraints/snapshot changes require a generated EF Core
migration during implementation. Existing games get CoverUploadId=null and retain
their URLs unchanged; no URL backfill or destructive image migration is needed.
Do not expose cover_uploads through the Supabase Data API or grant browser roles
table access. Verify effective grants/default privileges; this does not introduce
application-table RLS as a second ownership model.

## Save And Cleanup Lifecycle

### Selection And Staging

Bootstrap the authenticated Library before beginning reservation/claim work, with
no pending upload/Game entities in that bootstrap DbContext. The current
LibraryService.EnsureAsync saves independently and clears tracking after a unique
creation race; do not invoke that failure path inside a transaction holding staged
Game/upload changes. Then revalidate/lock the Library in the real lifecycle
transaction. An empty Library can survive a canceled/failed Add; atomicity covers
the Game, LibraryEntry and claim, not erasing a previously ensured Library.

1. The user selects/captures and edits locally. Use image updates only the form's
   draft selection. Opening the form, cropping, searching and canceling do not write
   a Game or upload a file automatically.
2. On final Create/Save, validate the form. If it contains a new local Blob, call the
   authenticated upload endpoint. Disable duplicate submissions and show upload
   then save phases. Other metadata is not sent to Storage.
3. Authenticate, validate/decode/re-encode, derive the Library from sub, enforce
   quotas and commit an Uploading record before sending the object to Storage.
   Library creation by this mutation is allowed; no Game is created yet.
4. Release DB locks before the provider call. Upload only canonical bytes with
   generated key/type and no upsert. Raw bytes are never stored publicly or privately.
5. After confirmed object success, conditionally change Uploading -> Ready, expiring
   24 hours after completion. Recheck the authoritative write deadline/state: a
   retired or deadline-expired row cannot finalize. A client timeout alone does not
   undo a Ready transition already committed. Return the handle only after Ready
   is durable, with unknown acknowledgements handled as described below.
6. Send the normal game create/update, or cover-only update, referencing that handle.
   Claim and Game persistence occur together in a short EF transaction. On success
   Ready -> Attached, claimed_game_id is set to that Game ID, cleanup_after becomes
   null, and any previous managed cover becomes DeletePending in the same commit.
7. On known game-save failure, leave the previous cover and attachment untouched.
   Keep the valid Ready handle/local preview for an explicit retry until expiry.

Storage and PostgreSQL are NOT one distributed transaction. A committed upload is
not a saved game cover, and a canceled HTTP request is not proof of rollback.

### Lifecycle Transition Rules

Only the following state transitions are permitted, through Core with fresh state,
authorization and the appropriate row locks. Purged means row absent, not a fifth
persisted state. All timestamps are assigned by the server in UTC.

| From -> to | Preconditions / atomic effects |
| --- | --- |
| Absent -> Uploading | Authenticated Library bootstrapped; actual image sanitized, byte quota and ten-unclaimed limit reserved under Library lock; immutable ID/namespace/key/size committed before provider PUT; cleanup_after=created_at+15 minutes; no receipt. |
| Uploading -> Ready | Provider confirms the canonical immutable write; namespace still valid, row still Uploading and deadline still live after lock; cleanup_after=completion+24 hours. Unknown acknowledgement is reconciled, not assumed failed. |
| Ready -> Attached | Unexpired, owned, never claimed; valid Game create/CAS in the same SQL transaction; set claimed_game_id once to that Game; clear cleanup_after. |
| Uploading/Ready -> DeletePending | Known failure, expiry or explicit cancellation; unclaimed and no Game reference, checked under lock; set retired_at once and cleanup_after for the ten-minute abandoned-upload grace. Provider ambiguity never retires an already committed attachment. |
| Attached -> DeletePending | Committed cover replacement/removal or Game deletion clears the current Game reference and retires the old record in one SQL transaction; preserve claimed_game_id; physical deletion occurs after commit. |
| DeletePending -> DeletePending | Idempotent cancel or scheduled delete attempt; no current Game reference; fixed retired_at, advance cleanup_after after every nonterminal attempt, retain receipt. |
| DeletePending -> Purged | Retention elapsed; confirmed missing/delete in the recorded namespace plus fresh primary reference/state check; never purge from an ambiguous provider/database result. |

No Uploading -> Attached shortcut, no claim from expired Ready, no return from
Attached/DeletePending to Ready, and no object/key reuse are allowed. If an Attached
row lacks its matching owner-scoped current Game reference, fail closed for content
and alert on the invariant violation; do not turn it into an automatic reattach or
unsafe object deletion. Metadata-only keep/no-op does not change lifecycle state.

### Unknown Commit Outcomes And Recovery

An exception or timeout while acknowledging COMMIT is also not proof of rollback,
even when the browser receives an explicit 500/503 response. Distinguish confirmed
validation/rollback failures from unknown database outcomes. Do not compensate by
deleting an object solely because COMMIT threw, or automatically replay Create.

Where possible, reconcile with a fresh connection/context against authoritative
primary data before replying. Otherwise use a safe `Cover save outcome unknown`
ProblemDetails with machine-readable code `cover_outcome_unknown`. The client must
also treat an unexplained 5xx, cancellation or lost response after dispatching a
Game write as potentially unknown, regardless of that code. Keep the upload handle
and stop replacement/re-Create until reconciliation; no automatic mutation retry.

Status must retain/report the first claimed Game ID even if another tab has already
replaced/removed the cover or deleted the Game. A never-claimed Ready receipt permits
an explicit retry using the SAME handle; a committed first claim prevents another
Create in every lifecycle state, not only Attached. Concurrent Create retries with
one handle serialize so at most one can create/claim. Refetch the bound owner-scoped
Game rather than claiming that its current fields necessarily equal the old request.

The receipt survives until the stated retirement/tombstone purge, not indefinitely.
If it has been purged, the Game was deleted, or the database is unavailable, report
unknown/deleted as applicable; never interpret missing recovery evidence as proof
the original Create failed or as permission for automatic Create with a fresh
upload. Offer explicit Library inspection/user resolution. This is not general
exactly-once Game CRUD or durable recovery after browser state is lost.

An ambiguous Uploading -> Ready commit needs no new state: leave the durable locator
recoverable and let authoritative reconciliation/expiry determine its lifecycle.
Do not delete a potentially Ready or Attached object as unconditional compensation.

### Explicit Intent And Optimistic Concurrency

Every managed-cover attachment, replacement, removal and Game deletion revalidates
ownership and state inside the transaction. Use a consistent lock order: Library,
existing Game when applicable, then affected upload rows in stable ID order.
Do not hold these locks while uploading, reading or deleting provider objects.

Read current cover/upload state AFTER acquiring those locks. Existing CRUD loads
tracked Games before its delete transaction; an EF query returning an already
tracked entity is not proof its values were refreshed. Reorder loading or explicitly
reload/use a clean transactional context before comparison/retirement. Check expiry
with fresh server UTC time after lock acquisition, not a timestamp captured before
waiting or a PostgreSQL transaction-start `now()` value.

coverUpdate is an explicit intent, not a desired nullable-field shape. Omission or
intent=keep always preserves the CURRENT stored cover, including a newer cover
another request saved. Null/malformed commands fail 400; no omitted field clears a
cover. Do not infer keep/remove from equality or from an empty URL control.

For every explicit external/upload/remove change on an existing Game, require an
expectedCover snapshot of the original persisted external URL and upload ID. Use
a caller-scoped, null-safe conditional update matching those references; verify
one affected Game row. A zero-match stale precondition returns 409 Cover changed
and rolls back metadata/claim/retirement in that transaction. Row locks coordinate
lifecycle operations but do not replace this optimistic client precondition.

No general Game version/timestamp column is needed. Compare normalized external
strings ordinally and UUIDs by value. A return to the identical external URL is the
same cover value for this comparison; the contract does not detect external-URL ABA
history. Managed IDs/keys are immutable, retired IDs never reattach, and no content
overwrite is permitted. The concurrency scope is the cover, not stale non-cover
fields in the existing full-form update model. Rules:

- Explicit changes compare expectedCover even when desired happens to equal it;
  such equality never converts the command into keep or bypasses a stale check.
- Explicitly retaining the currently attached upload for the SAME Game with a
  matching precondition is a no-op success, not a new claim. An Attached upload
  cannot attach to a different Game, even in the same Library.
- A retry referring to the same upload already attached to this same Game is safe
  to reconcile by refetching; never attach it to a second Game. A repeated Create
  with any previously claimed handle returns conflict before creating another Game,
  including when its cover has since been retired.
- All other Game/LibraryEntry validation remains authoritative. Validation or a
  confirmed pre-commit failure rolls back metadata, attachment and retirement as
  one unit; that rollback claim does not apply to an unknown COMMIT outcome.
- The cover-only endpoint updates exactly the cover references and asset lifecycle,
  not name, Family Friendly, player counts, acquisition, Platforms, Genres, Rating,
  Notes, GameStatus, progress, playtime, CreatedAt or Play Log.
- Steam enrichment must use an atomic conditional write that fills only when BOTH
  external URL and managed upload ID are null. A stale load-null-then-write must not
  overwrite an upload that another request attached meanwhile. The rest of the
  approved Steam import/enrichment workflow is unchanged.

### Replacement, Removal And Game Deletion

Only after the new cover/Game transaction commits may the previous managed object
be deleted. Never delete the old object first or overwrite its bytes in place.
Replacing with an external/search URL follows the same retirement rule. Never
delete external URL targets or fetch them for conversion.

Remove cover is a draft action until Save. Saving it clears both references, queues
the previous uploaded object for deletion, and shows the placeholder. It does not
restore a previous external/search/Steam URL or trigger enrichment immediately.
A later explicit Steam preview/import may fill a coverless Steam game under the
existing rules. Permanent suppression of later enrichment is NOT included; approve
a different product rule first if that is desired.

Deleting a Game retains the existing LibraryEntry/Game/Play Log lifecycle and marks
its uploaded cover DeletePending in the same SQL transaction. Storage failure does
not resurrect the Game or fail an already committed deletion; cleanup retries.

### Orphan And Retry Cleanup

One narrowly scoped BackgroundService in the existing API scans at startup and
every 15 minutes, processing at most 100 due rows and starting no further provider
or row-processing operations after a 60-second pass budget. An already-started
bounded operation may finish after that budget; do not abort a commit and falsely
declare its outcome. Never overlap passes within one process. No separate job
platform or worker project. When the API is stopped, cleanup resumes on restart;
this is not a guarantee of wall-clock deletion while the application is unavailable.

- Uploading deadline: 15 minutes. Ready deadline: 24 hours. Expired rows are no
  longer attachable; under row lock mark them DeletePending only if no Game refers
  to them. Explicit cancellation uses the same retirement transition.
- Ready expiry/claim races must have exactly one winner. Once DeletePending, an
  upload can never be claimed. Never sweep by path-prefix/age without checking the
  application's trusted state and references.
- Provider write failure, cancellation or ambiguous timeout leaves its durable key
  recorded for cleanup. Do not lose the locator merely because the browser never
  received the handle. A late provider completion cannot re-enable attachment.
- On retirement, retain the tombstone for 24 hours. Allow a ten-minute grace before
  abandoned-upload deletion, re-delete during retention and confirm deletion again
  before removing the metadata. This accommodates practical late-write/lost-response
  windows; do not promise mathematically atomic cross-service deletion.
- Known replacements/deletions may attempt object deletion immediately after commit;
  durable state survives failure. Missing provider object is idempotent success.
- Failures keep DeletePending with the next attempt no sooner than 15 minutes;
  continue bounded retries and report oldest pending age. Do not delete the record
  while provider outcome is unknown or any Game still references it.
- Advance cleanup_after after EVERY nonterminal attempt, including a confirmed
  delete while the tombstone must remain. Select oldest due (cleanup_after, id)
  first so retained successful tombstones cannot monopolize all 100 slots. Keep
  retired_at fixed: retries must not restart the 24-hour retention interval.
- Final purge requires elapsed retention, a confirmed deletion/missing result in
  the RECORDED namespace, and a fresh primary-database reference/state recheck.
  A CHECK/FK can protect metadata removal but cannot by itself prevent deleting
  bytes from Storage; the irreversible retirement protocol protects that operation.
  Preserve claimed_game_id for recovery until this purge. Never reuse the key.
- Cleanup may lock/claim an asset row, then delete outside the transaction because
  DeletePending is irreversible. Concurrent cleanup attempts may safely repeat an
  idempotent delete. Metadata FKs remain final protection against purging a reference.
- Cleanup/configuration outages can temporarily exceed nominal retention. Document
  operator reconciliation through Storage APIs for persistent failures. Provider
  backups/caches and already-downloaded browser copies are not immediate erasure.
- A finite grace period does not prove that a timed-out provider write can never
  complete later. Recheck an upload's live write deadline immediately before provider
  dispatch; late results cannot finalize/claim. Extremely late untracked objects
  still require namespace-scoped operator reconciliation. Do not advertise absolute
  zero-orphan guarantees or blindly delete a bucket listing by age/prefix alone.

This single cover_uploads lifecycle table is the object journal and retry state;
do not add a general-purpose outbox or upload/media subsystem.

## API Contracts

Use existing no-prefix routing, Bearer JWT auth, requestId-safe ProblemDetails and
caller-derived ownership. IDs below are opaque handles, never permission grants.

| Method | Route | Success | Purpose |
| --- | --- | --- | --- |
| GET | `/cover-uploads/options` | 200 | Enabled state, accepted types and public limits only; no credentials/provider configuration. |
| POST | `/cover-uploads` | 201 | One multipart file -> validated Ready upload; no Game mutation. |
| GET | `/cover-uploads/{id:guid}` | 200 | Owner-only lifecycle/recovery metadata; no provider access or image URL. |
| GET | `/cover-uploads/{id:guid}/content` | 200 JPEG | Authenticated, freshly authorized bounded byte proxy; never a redirect. |
| DELETE | `/cover-uploads/{id:guid}` | 202 | Retire an unclaimed upload; already DeletePending is idempotent. Attached returns 409. |
| PUT | `/games/{gameId:guid}/cover` | 200 | Owner-only cover-only attach/replace/clear for either Game type. |

All routes require Bearer authentication with a nonempty usable sub, including
options and content. No query-token/cookie/signed-link alternative is accepted.
options does not create a Library or call the provider on page load. Exact body:

```json
{
  "enabled": true,
  "maxUploadBytes": 5242880,
  "acceptedMediaTypes": ["image/jpeg", "image/png", "image/webp"],
  "maxOutputBytes": 2097152,
  "maxOutputWidth": 960,
  "maxOutputHeight": 1440,
  "maxLibraryBytes": 262144000,
  "maxUnclaimedUploads": 10
}
```

These advertised types/limits do not make client MIME trustworthy. Browser-only
20 MiB/24-megapixel source preparation is distinct from the 5 MiB upload contract.
When disabled, return enabled=false with the same limits, not credentials or a
Storage exception body. Status/options never fail solely on image-read availability.

POST multipart field is exactly `file`, one non-empty file, with no client owner,
bucket, filename/path instructions or arbitrary extra metadata. Browser-generated
multipart boundaries must not be overwritten by a JSON Content-Type interceptor.
Request media type is multipart/form-data with a bounded valid boundary; reject
wrong shape/extra parts before provider writes. 201 is returned only after Ready
is durable, with Location pointing to the API metadata route, not Storage.

Ready/status response:

```json
{
  "id": "00000000-0000-0000-0000-000000000001",
  "state": "Ready",
  "gameId": null,
  "gameExists": null,
  "expiresAt": "2026-10-29T20:00:00Z"
}
```

gameId is the first-claim receipt ID, null before claim. gameExists is null when
unclaimed; otherwise it is computed through an owner-scoped live Game lookup.
Attached returns that Game ID, gameExists=true and expiresAt=null. A retired upload
retains gameId and reports true/false for live/deleted Game respectively; this never
makes it attachable or readable. Uploading has no readable image. Foreign/missing upload
IDs remain indistinguishable 404s; a purged ID means recovery unavailable, not a
confirmed failed Create. No old cover bytes are retained as a user-facing history.

A Ready upload whose expiresAt has passed cannot be read/claimed even if cleanup
has not changed its stored state. expiresAt is the Uploading/Ready lifecycle
deadline; DeletePending reports null and Attached reports null. It is not an image
URL/token expiry. DELETE is only staged-upload cancellation, never Game cover
removal. Its 202 means durable retirement, not confirmed physical deletion. Unknown
or purged DELETE returns 404; a client already canceling that owned handle may
treat it as terminal, never use it to decide an uncertain Create failed.

### Explicit Cover Update Object

The ONLY writable cover command in Game JSON contracts is optional coverUpdate.
Its exact case-sensitive variants and allowed fields are:

| intent | Allowed fields | Create meaning | Existing Game meaning |
| --- | --- | --- | --- |
| keep | intent only | No cover | Preserve actual current cover, no cover comparison/mutation |
| external | intent, url; expectedCover on updates only | Valid external URL | CAS to normalized external URL; retire any old managed cover |
| upload | intent, uploadId; expectedCover on updates only | Claim owned live Ready upload | CAS/claim replacement; retain same current attachment only as the specified no-op |
| remove | intent; expectedCover on updates only | No cover | CAS to neither reference; retire old managed cover |

Omitted coverUpdate means keep. A present null, unknown intent, extra/incompatible
command field, blank/invalid url, invalid UUID, missing required field or update
precondition is 400 Invalid cover request. expectedCover is a non-null object with
both coverImageUrl and coverUploadId explicitly present, allowing nulls and at most
one non-null reference. It represents stored references, not a proxy/Blob URL.
No expectedCover is accepted on Create. The command is validated before mutations.

Top-level legacy coverImageUrl/coverUploadId/expectedCover write fields are rejected
with 400 and code=cover_intent_required rather than inferred or silently ignored,
including null values. This explicit version transition requires updated Angular
save serialization and actionable stale-PWA-client refresh feedback; it is not
backward-compatible permission to clear covers. Other game fields and validation
remain their existing full-form contracts. Read responses still use coverImageUrl
and coverUploadId as defined above.

Cover-only replacement body (no unrelated metadata accepted):

```json
{
  "coverUpdate": {
    "intent": "upload",
    "uploadId": "00000000-0000-0000-0000-000000000001",
    "expectedCover": {
      "coverImageUrl": "https://example.test/old-external-cover.jpg",
      "coverUploadId": null
    }
  }
}
```

For external, replace intent/uploadId with intent=external and url. For removal,
use intent=remove plus expectedCover, with no url/uploadId. PUT accepts a JSON object
containing only optional coverUpdate; {} is a keep/no-op for an existing owned Game,
while null/missing body is 400. Owner lookup still occurs for keep; foreign/missing
Game is 404. All success variants return the actual current stored reference:

```json
{
  "gameId": "00000000-0000-0000-0000-000000000002",
  "coverImageUrl": null,
  "coverUploadId": "00000000-0000-0000-0000-000000000001"
}
```

Normal POST /video-games and POST /board-games accept the SAME coverUpdate variants
alongside their existing game fields. A live Ready upload can be created before a
Game ID exists; the server allocates Game/LibraryEntry IDs and claims it in one SQL
transaction. Normal PUT /video-games/{id} and PUT /board-games/{id} accept the same
update commands alongside full metadata. A failed precondition/validation rolls
back the complete combined update. No separate public attach/finalize endpoint is
needed; the Game save is the atomic attachment operation.

When only the cover changed in an Edit form, use the cover-only endpoint. If other
fields also changed, use the normal full-form type-specific update and attach in
that transaction. Create always uses the normal type-specific endpoint. No separate
cover save button is required and Cancel still means no Game mutation.

### Content Retrieval Contract

GET /cover-uploads/{id}/content has no body and accepts no upstream URL/path option.
It follows every authorization/cache/byte rule under Retrieval And Privacy. Missing
or foreign/expired/retired IDs are 404 after authentication; 401 is never an image
redirect. A correctly authorized existing record whose provider file is missing,
invalid or unavailable yields a safe 503, preserves its metadata and alerts the
operator; it does not silently clear the cover. Caller cancellation aborts provider
I/O where practical but performs no lifecycle mutation.

## Backend Security And Error Handling

### Validation And Processing

- Require a validated application JWT with usable sub before parsing/decoding the
  multipart image. Do not accept owner/library identity from JSON, headers or paths.
  Enforce the usable-sub requirement before multipart model binding, not only inside
  an action after IFormFile has already been bound.
- Enforce a 5 MiB file limit at ingress and streaming read, plus bounded multipart
  overhead of 64 KiB and one part. Enforce chunked requests too; Content-Length is
  not authoritative. Align reverse-proxy/ASP.NET limits so failures are consistent.
- Apply user rate limits and global upload admission BEFORE multipart buffering or
  model binding. At most two upload pipelines per process may read/buffer/decode/
  write at once, queue zero; a decoder-only semaphore after IFormFile binding is
  insufficient to bound many concurrent 5 MiB buffers or temporary files. Bound
  body-receive time to 60 seconds and configure minimum data-rate/proxy limits as
  supported. Reject overload before consuming the full body; test slow/chunked input.
- Detect JPEG/PNG/WebP from signatures/container structure AND a successful decoder,
  not filename, extension, client MIME or header alone. Reject failed structural
  checks, truncated files, malformed headers/tables, unsupported and multi-frame/
  animated uploads, decompression bombs and all decoder warnings/errors. D3b allows
  unreported JPEG coefficient tolerance after every existing check passes; it does
  not relax these rejected categories or claim exhaustive input conformance.
  A supported raster with a misleading filename/MIME is still processed according
  to detected bytes; those client labels are never reused as output authority.
- Inspect encoded dimensions and orientation before full decode using checked wide
  arithmetic: at most 4 million pixels and maximum edge 4096. Validate 2:3 against
  the post-orientation logical dimensions, not a sideways JPEG's unrotated header.
  Verify the final dimensions again after decoding/orienting. Browser-selected
  source limits differ because the backend receives only the small cropped raster.
- Decode fully, apply any remaining orientation, flatten transparency against white,
  convert supported embedded color profiles to sRGB before stripping them, resize
  without upscaling to at most 960 x 1440, and re-encode a new JPEG at quality
  85. Preserve neither trailing payloads nor original EXIF/GPS/embedded thumbnails/
  application metadata. Final output must be at most 2 MiB and at least 160 x 240.
  If a valid source cannot meet these limits, return validation error rather than
  storing it unchanged or silently accepting dangerous bytes.
- The two-pipeline admission cap also bounds active decoder operations. Use bounded
  buffers and one working image at a time; no unbounded admission/processing queue.
  Native decode is not assumed preemptible just because a CancellationToken exists;
  header/pixel limits, admission and patched codecs are mandatory resource controls.
- Provider operations have a 15-second timeout and use cancellation where supported;
  do not automatically retry uploads or assume cancellation removed a written file.
  Dispose all temporary buffers/resources. Do not write raw uploads under the web root.
- Never execute supplied content or invoke shell utilities. Re-encoding is a strong
  raster boundary, not a claim of antivirus scanning; keep processor/native packages
  patched and security-reviewed. No original-file download endpoint is exposed.
- Server-generated `.jpg` names and `image/jpeg` type only in Storage. The selected
  API proxy uses nosniff and the fixed server-selected type/filename, never a
  client filename or arbitrary provider Content-Disposition.
- Never fetch a user-supplied external URL on the backend as part of upload/editing.
  Existing search stays provider-mediated; do not introduce SSRF via remote cropping.
- External input keeps the existing trimmed HTTP(S)/2048-character URL validation;
  reject data:, blob:, file: and Base64 image values. Reject URLs targeting this
  application's managed bucket or protected content route as external input; use an
  owned upload handle instead. Such URL recognition is input validation only, never
  proof of ownership or permission to delete. Other external URL behavior is unchanged.

### Authorization, Limits And Errors

Core scopes upload metadata, Game operations and content reads to the authenticated
Library. Verify asset state, expiry, ownership and Game references even when the
client obtained an apparently valid ID earlier. Unauthorized foreign identifiers
return the same 404 as nonexistent ones. Storage admin authority must not leak into
arbitrary-key API operations.

Rate limits use existing ASP.NET facilities: upload 5/minute AND 20/hour per
validated sub, queue zero; metadata/content GET 120/minute per sub, queue zero;
options and normal Game CRUD keep their existing general behavior. Proxy reads
also have a per-process cap of eight active bounded provider reads, queue zero,
with safe 429/Retry-After on overload. They do not occupy the two upload slots.
Quotas are durable across instances; in-process rate, upload and proxy admission
limits are per-instance, documented as such. Reassess infrastructure
before horizontal scale rather than claiming distributed rate limiting.

| Condition | HTTP / ProblemDetails |
| --- | --- |
| Missing/expired/invalid JWT or missing sub | 401; existing session-expired handling, no Game/cover mutation. |
| Foreign/missing Game or upload | 404, generic resource-not-found without existence leak. |
| Invalid multipart/intent/variant/missing expected snapshot | 400, Invalid cover request; legacy top-level cover writes use code=cover_intent_required. |
| Actual file bytes exceed limit | 413, Cover image too large. |
| Unsupported detected format | 415, Unsupported cover image format. |
| Decode/frame/dimension/pixel/ratio/output validation fails | 422, Invalid cover image, with safe actionable detail. |
| Stale expected cover / attached elsewhere / expired claim | 409, Cover changed / Cover upload unavailable; no partial Game mutation. |
| Quota, user rate or upload/proxy admission limit | 429 with Retry-After when time-based (one second for admission overload); distinguish quota guidance in safe detail. |
| Storage disabled/unavailable/timeout | 503, Cover uploads unavailable; preserve existing cover references and keep manual URL/search independent. Managed-image rendering may temporarily use a placeholder. |
| Confirmed pre-commit failure / confirmed rollback | Existing generic 500 with requestId; no committed attachment/retirement, retain recovery state. |
| Commit acknowledgement failed / outcome cannot be established | Safe 503 Cover save outcome unknown with `code=cover_outcome_unknown` and requestId where handled; unexplained 5xx after write dispatch is also reconciled, never presumed rolled back. |

Missing upload configuration must not prevent application startup or existing CRUD.
Options reports enabled=false; do not provision a bucket silently on a user request.
Proxy availability does not decide whether a persisted managed cover exists.

## Unified Frontend UX

Extend the existing shared selector rather than create a second workflow. A local
rename to CoverSelector is allowed during implementation only if it updates the
same component/usages; do not retain two competing selectors.

- One cover section contains the current/pending preview and actions Search images,
  Enter image URL, Upload image, Take photo and Remove cover. Move the existing
  manual URL input into it; parent forms still own the selected draft/reference.
- Keep the external URL input visible/editable, preserving Feature 010's explicit
  manual-input requirement; do not replace it with an inaccessible or hidden-only
  tab. For a managed selection, its external field is empty, not a proxy/Blob URL.
  The authoritative cover draft distinguishes unchanged, remove, external, local Blob and
  owned Ready/Attached handle; local previews never enter the persisted URL control.
- Track explicit UI source/removal actions and serialize coverUpdate accordingly.
  Initializing an empty external field on an uploaded Game is keep, not remove.
  An actual user clear of an external URL or Remove cover action selects remove
  explicitly, shows the pending no-cover preview, and sends that intent only on
  Save. Do not serialize omission, a null default or equality as a clear command.
- Search keeps its current explicit trigger, game name/type/Platform hints,
  provider errors and normal Save boundary. Opening Upload/Camera must not call
  search. Changing the search context must not silently remove an uploaded draft.
- Ordinary selection uses a file input with supported types and no capture hint.
  Take photo uses a separate input with `capture="environment"`; this is a browser
  hint, not a promise. Do not add getUserMedia/live-camera permissions or streams.
  Unsupported devices show a chooser/fallback and retain Upload image. Picker/camera
  cancellation leaves the previous draft and saved cover untouched.
- Use an inline editor within the existing dialog/sheet, not nested native dialogs.
  Mobile toolbar/preview must fit a 320 CSS-pixel viewport without horizontal scroll;
  controls have at least 44 CSS-pixel targets and remain usable with the keyboard open.
- Provide native labeled buttons and a keyboard-operable zoom range, positioning
  controls/instructions, rotation, Crop/Fit, Reset, Use image and Cancel editing.
  Do not rely only on pinch/drag; trap/manage focus according to the existing parent
  dialog and return focus to the initiating action. Announce errors/status with
  appropriate live regions; ensure cropper pointer/keyboard controls are tested.
- Local preview is clearly "Not saved". A new draft visibly replaces the pending
  preview, not the saved cover. Cancel editing restores the earlier draft; canceling
  the whole form restores the saved game and releases local resources.
- Search/manual/upload selections replace one another explicitly. Never retain an
  undisclosed fallback stack. A selected staged upload replaced by another source
  becomes eligible for best-effort cancellation; never retire the active Game cover
  merely because a form is edited.
- Show phases Preparing image, Uploading cover and Saving game. Percent progress is
  shown only if the configured Angular HTTP transport actually reports it; otherwise
  use honest indeterminate progress. Do not add a transport dependency solely for it.
- Disable duplicate submissions and source changes during save. Cancel upload can
  unsubscribe/abort where practical. Once a game write may have reached the server,
  explain that cancellation cannot undo a completed save and reconcile its outcome.
- Invalidate the form/save generation on Close, Escape, backdrop dismissal, cancel
  and destruction. A late crop/upload result must not initiate a Game mutation after
  that form was closed, even if the management-list component still owns a live
  subscription. Cancel before Game-write dispatch prevents that dispatch; cancel
  after dispatch is outcome-unknown until reconciled. Late search/content results
  must not replace a newer source or a different authenticated account's preview.
- On 401 use the existing session-expired path. Local image state is not persisted
  through login or application reload. Do not silently retry a mutation after login.
- A small reusable cover-rendering component/directive is justified by the existing
  real display usages for authenticated Blob/placeholder handling; it must not become a generic
  media service or competing selection flow.
- Build token-bearing content requests only from the known API base and upload ID.
  Never feed an arbitrary external coverImageUrl into that authenticated fetch path;
  ordinary external img requests must not receive the application Bearer token.

## Failure And Recovery Matrix

| Failure or event | Required behavior |
| --- | --- |
| File chooser/camera/editor canceled | No upload/Game change; restore prior draft; revoke temporary resources. |
| Invalid/unsupported/too-large file | Actionable validation; existing saved cover untouched; choose another file. |
| Upload interrupted or response lost | Do not submit a guessed handle or change the Game. Retain local Blob for explicit retry. Any server-side unknown upload expires/cleans up; a fresh POST can create another bounded staged upload, not an unbounded automatic retry. |
| Storage unavailable or processor failure during upload | No new Ready handle, therefore no Game write from that upload. Preserve the saved cover and other edits; manual URL/search remain independent. A previously verified live Ready claim does not require another provider read. |
| Upload succeeds, known Game validation/update failure | Previous cover remains attached. Keep Ready handle/preview for retry; rollback did not retire it or the old cover. |
| Game-save response lost or commit outcome unknown, including an explicit 5xx | Reconcile from fresh primary data/status using the SAME handle. A first-claim receipt identifies the Game even after cover retirement; refetch rather than merge fields or repeat Create. A never-claimed Ready handle permits explicit same-handle retry. |
| Duplicate Create with previously claimed handle | Conflict before creating another Game in every lifecycle state. Owner-only status reports the first Game ID and whether it still exists; no silent metadata merge. |
| Recovery receipt purged, unavailable, or associated Game deleted | Report unknown/deleted as applicable; stop automatic retries, including fresh-upload Create. Offer explicit Library inspection/user resolution, not a claim of indefinite exactly-once recovery. |
| Expired Ready handle | 409; retain local crop if available and offer re-upload. No automatic upload after login/reload. |
| Concurrent cover change | 409 for an explicit replacement; retain local draft and refetch current cover for user choice. Metadata-only save preserves the newer current cover. |
| Cancel after staging, before Game write | Best-effort DELETE of the owned unclaimed handle; 409 Attached triggers reconciliation, not forced deletion. TTL cleanup is the backstop. |
| Old-object deletion fails after successful replacement | New cover remains active; durable DeletePending retry. Never report the committed replacement as rolled back. |
| Game deletion while Storage unavailable | SQL deletion succeeds; referenced managed object is journaled for cleanup. External cover targets are never deleted. |
| Content request fails / JWT expires / retained Picker card refers to a retired asset | 401 follows session-expired handling; 404/429/503 shows placeholder/explicit retry. Revoke stale local Blob URLs on source/account changes; no signed-link renewal or Picker-history mutation. |
| API restart or browser close | Local drafts are lost; durable staging/deletion state survives and bounded cleanup resumes. No offline mutation replay. |

## Acceptance Criteria

Each ID needs verification in the implementation review; none is checked off by
design approval. Existing Domain invariants remain mandatory in every scenario.

### Selection And Editing

- AC01: Both VideoGame and BoardGame Add/Edit offer the unified URL/search/upload/
  camera controls without automatic uploading/searching or placeholder Game creation.
- AC02: Device selection and camera hint/fallback work; cancellation preserves the
  prior draft and saved cover.
- AC03: Portrait crop, zoom, reposition, Crop/Fit, orientation, 90-degree rotation and
  reset work with touch and keyboard; square BoardGame artwork can be kept whole.
- AC04: Preview is available before upload/save; local Blob/object URLs are released
  and never sent as persisted coverImageUrl values.
- AC05: Mobile/PWA controls fit the viewport, remain accessible, and fail gracefully
  on unsupported format, camera, decode or memory paths.

### Security And Persistence

- AC06: Upload/status/content/cancel/cover mutation require validated authentication and
  authenticated-Library authorization; foreign/missing IDs do not leak existence.
- AC07: Byte, part, decoded-pixel, dimension, frame, type and output limits are
  authoritative server-side, including deceptive MIME/filenames and chunked bodies;
  admission/rate limits run before multipart buffering and reject slow/overload paths.
  D3b does not change these limits or the structural/header/table/decoder-diagnostic checks.
- AC08: Only sanitized single-frame JPEG output reaches Storage; metadata/original
  bytes do not. Existing truncated/malformed-header/invalid-table/animated/bomb and
  diagnostic-failure regressions remain rejected. Under D3b the complete, structurally
  accepted AC-run fixture with no decoder diagnostic is deliberately normalized to
  fresh bounded canonical output, not preserved as its original bytes. This is
  bounded normalization, not exhaustive JPEG conformance certification.
- AC09: Private bucket denies anonymous/browser-JWT listing/read/write/delete/sign
  operations. Managed content is served only by the authenticated byte proxy with
  fresh owner/state checks, private,no-store on success/errors/challenges, and no
  signed link, redirect, query JWT, shared cache, conditional/HEAD authorization
  bypass or 304 response. Credentials never reach the frontend or logs.
- AC10: Immutable server paths, no upsert, quotas and rate/admission limits hold
  under concurrent requests and do not trust client ownership/path information.
- AC11: EF migration preserves existing URLs/data, adds only the described cover
  metadata/constraints, and leaves binaries/Base64/application-generated signed
  URLs out of PostgreSQL.
- AC12: CoverImageUrl and CoverUploadId cannot both persist; one upload cannot attach
  to two Games or a foreign Library; expired/retired uploads cannot attach. Claims
  retain their original Game receipt; Storage operations use the recorded namespace.

### Save, Precedence And Recovery

- AC13: Successful Add claims the Ready upload and creates normal valid Game plus
  LibraryEntry atomically; confirmed validation/rollback failure leaves no partial
  Game/attachment. Unknown commit outcomes are reconciled, not declared rolled back.
- AC14: Explicit cover-only replacement/removal changes no unrelated metadata.
  Combined form saves use existing validation and atomic attachment/retirement.
- AC15: Manual/search/upload choices replace one another explicitly; confirmed
  failed writes/cancellation before Game-write dispatch preserve the saved cover.
  After dispatch, cancellation/5xx may require reconciliation and never proves undo.
- AC16: Concurrent explicit replacements detect a changed expected cover; stale
  metadata-only updates do not restore/clear a newer cover.
- AC17: Steam cannot overwrite an uploaded or explicit external cover, including a
  concurrent upload/enrichment race; other Steam playtime/import behavior is intact.
- AC18: Saving explicit remove intent produces no cover after commit, not on merely
  changing a form draft. There is no hidden fallback or automatic enrichment;
  only a later explicit Steam operation may enrich an otherwise coverless game.
- AC19: Replacement/Game deletion record retirement in their SQL mutation and
  physically delete bytes only after commit. Cleanup failures are recoverable
  without restoring deleted Games/old covers or deleting a referenced object.
- AC20: Abandoned/expired uploads, late provider completions, cancel/claim races,
  lost responses and process restarts follow the stated lifecycle without deleting
  referenced objects or automatically duplicating Games. Commit-unknown, retired-
  receipt and post-purge recovery paths never assume rollback. Cleanup makes fair
  progress after success as well as failure and observes its work/time budget.
- AC21: Read models return the external URL or managed ID without Storage access.
  Visible managed cards fetch authorized Blobs, revoke local URLs on source/account/
  lifetime changes and show placeholders/retry on failure. External images get no
  Bearer token. Image updates never mutate Picker shown IDs/history/result states.

### Verification And Scope

- AC22: Automated Core, real-PostgreSQL integration, frontend, lint, build, E2E,
  migration-model and whitespace checks pass, including existing 006/008/010/011/012
  regression coverage. Counts must be actual results, not copied baseline numbers.
- AC23: Deterministic upload/save, replacement and failure-recovery E2E journeys
  pass on desktop/mobile without depending on a real remote Storage service.
- AC24: Real-device camera/crop review, chosen-production-runtime native codec smoke
  checks and an isolated real private-Storage/proxy authorization/no-store/delete
  check pass before release; ordinary suites use credential-free fakes. G01-G07 pass.
- AC25: No excluded functionality, generic media framework, direct application-table
  access, additional backend projects or unapproved infrastructure is introduced.
- AC26: Optional coverUpdate is the only cover write command. Omission/keep preserves
  the actual current cover; null/malformed/legacy write shapes fail safely. Every
  explicit existing-cover change uses expected-reference CAS, even when desired
  and expected are equal; stale changes are 409 with no partial metadata/claim.

## Test Strategy

### Backend Core And Image Tests

Use actual small binary fixtures and the real approved decoder for content tests,
not only mocked MIME/header values. Cover JPEG/PNG/WebP, orientation, alpha flatten,
wide-gamut to sRGB conversion, one-pixel crop rounding, metadata/GPS removal,
invalid/truncated files, spoofed names/types, SVG/polyglots,
animated WebP/APNG inputs, decompression dimensions, integer overflow, size/output limits,
resource admission and cleanup of temporary buffers.

Retain the complete nonconforming AC-run fixture as
`Decoder_tolerated_nonconforming_ac_run_is_normalized_as_fresh_bounded_jpeg`.
Its outcome was explicitly changed by D3b, not by suppressing native diagnostics:
assert canonical bounds/metadata-free output, different source/output bytes and
successful canonical reprocessing. Keep every other security regression active
with its original rejection outcome. The source remains nonconforming.

Prove animation detection with actual fixtures for each accepted container. A
codec that silently ignores an unsupported animation extension and decodes only
its first frame has not satisfied the single-frame validation rule. Reject the
unsupported animated form; do not assume successful first-frame decode proves it
was static.

Core lifecycle tests cover draft/staging/claim/retire decisions, immutable keys,
source exclusivity, expected-reference comparison, metadata-only preservation,
quota reservations, timeouts/ambiguous outcomes and deletion retry. Fake only the
external Storage boundary and clock where needed, not the image decoder or Domain
invariants. No automatic PlayLogEntry or metadata inference is permitted.
Keep Core tests pure where appropriate and use the existing integration-test project
for the real Api decoding adapter; do not add a production project or mock away
native validation. Execute the final-image processor smoke separately under G01.

### PostgreSQL Integration

Use real PostgreSQL and at least two users for all applicable operations. Verify
authentication, forged ownership IDs, foreign status/content/cancel/claim, unique/FK/
check constraints, existing-row migration, quotas and state persistence.

Exercise Ready expiry versus claim, two Games racing to claim one upload, two cover
replacements, stale metadata-only save, atomic Game validation failure, repeated
Create/recovery, deletion lifecycle, cleanup versus active reference, durable retry
after provider failure, and upload response lost/late completion after retirement.
Add COMMIT-acknowledgement failure with a successful database commit, then replacement,
removal or deletion before recovery. Verify first-claim evidence survives retirement,
purged evidence never triggers a blind new Create, and no compensation deletes an
Attached object. Test concurrent first-Library bootstrap without tracker loss,
namespace configuration mismatch, and more than 100 retained successful tombstones
mixed with later expired uploads to prove fair rescheduling and purge safety.
Verify failed SQL writes never delete a previously referenced object. Test Steam's
atomic empty-cover predicate racing with attachment and preserve Family Friendly,
playtime, Platforms/status/progress and Play Log invariants.

The Storage fake must retain object keys/bytes and allow controllable success,
timeout, delayed completion, missing objects, invalid/truncated/oversized content
responses and delete failure.
Verify sanitized output, no raw-file write, correct retirement and retry order.
Do not use EF InMemory or require real Supabase Storage for ordinary test runs.

Proxy tests cover valid owner Ready/Attached reads, invalid/expired/foreign/retired
reads, current association checks after replacement/deletion, no provider I/O before
authorization, bounded response/type/byte count, request cancellation and overload.
Assert private,no-store/Vary/nosniff on successful and failed requests, including
early JWT challenges, conditional headers and HEAD. A prior successful read must
not let another user obtain content via validators, local/shared response cache or
an error/redirect path. No response may expose a signed/Storage URL or server key.

Write-contract tests cover every intent in Create and existing-Game endpoints,
omission/keep, explicit remove, null/unknown/extra fields, missing snapshots, legacy
top-level fields, and stale equality cases. They verify source intent is not inferred
from missing/null/equal references. Existing game invariants continue to be tested.

### Frontend

Test file/capture input distinction, canceled chooser/editor, source validation,
crop/fit/zoom/reposition/rotation controls, local preview/object-URL disposal, unified
search/manual behavior and no automatic uploads.

Test Create/Save upload-then-claim order, duplicate-click guards, transport progress
when available, cancellation, 401, 413/415/422/429/503 messages, Ready reuse after
known failure, expired handles, uncertain-save reconciliation, preservation of
existing covers and other edits, explicit removal, stale-cover conflict and choosing
an external URL after a failed/staged upload. Mock Storage via the application API,
not direct browser Storage calls. Test authorized Blob loading/fallback, local URL
revocation, 401 handling, account/session changes and request deduplication. Preserve
Picker history even when an image reference is retired or its content unavailable.

### Playwright And Manual Gates

Extend the small existing suite with shared high-value journeys, not a test for
every format. Use real fixture files through setInputFiles/file chooser and the
actual crop UI; do not bypass cropping by stubbing a final Game response.

1. Upload/crop/save an unsaved Game; verify its rendered cover and unchanged metadata
   in management/Library. Cover both types between browser journeys and integration.
2. Replace that uploaded cover, then remove it; verify immutable replacement, no old
   fallback and unchanged unrelated fields.
3. Simulate one Storage or Game-save failure; verify the old cover survives and an
   explicit retry succeeds without duplicate Game creation.

Use the existing Development-only E2E switches and an in-process deterministic
Storage substitute. Never enable fake upload/storage endpoints or fake auth in
Production. Run desktop Chromium and mobile viewport coverage; check selectors,
focus, overflow and progress/error recovery.

Specify `E2E:CoverUploads:Enabled` explicitly for the future substitute. It requires
Development/Test mode and the existing E2E-auth safety guard, and is unavailable
in Production. In this mode DI replaces only the external Storage boundary with
an in-memory object/read-byte fake; the real decoder, Core validation and
PostgreSQL lifecycle still run. Construct no real Storage client or provider call,
require no Supabase credential, and do not fall back to remote Storage on fake
failure. Use existing test JWTs and the REAL authenticated content route to serve
fake-backed bytes; no anonymous fake-image route or fake signed capability is
necessary. A separate opt-in local contract suite uses local test keys/loopback
HTTP, never live credentials in ordinary CI/E2E.

Frontend regressions must include Close/Escape while upload completion is delayed,
then successful server completion: no Game request follows the invalidated form.
Also test account switch during content fetch and source switch during local
crop/export, not only cancellation after all async work has completed.

Playwright file selection cannot prove a real native camera or permission flow.
Manually test supported iOS Safari/installed PWA and Android Chrome/installed PWA,
rear-camera hint, capture cancellation, orientation, large-source rejection and
keyboard/touch controls. Desktop chooser fallback is required. Run an opt-in local
Supabase or isolated staging Storage contract smoke test for private bucket denial,
authenticated API byte retrieval, direct Storage denial, canonical upload, Ready
expiration, no-store behavior and deletion. Missing credentials
block that release verification, not permission to silently call it tested.

## Explicit Non-Goals

- Multiple images, galleries, image history, sharing or shared/family Libraries.
- AI generation, OCR, recognition, background removal, content moderation or social
  features. Users remain responsible for the images they choose.
- Videos, live camera streaming, HEIC conversion, free-angle/perspective correction,
  arbitrary media uploads or editing/cropping remote search images.
- Original-photo retention, EXIF/GPS storage, Base64 database fields or image binaries
  in PostgreSQL; cross-user/global deduplication or image catalogs.
- Automatic Steam/search overwrites, automatic cover search or automatic Play Log.
- CDN infrastructure, paid on-demand image transformations, multiple generated
  renditions, a generic media framework, brokers, Redis or new backend projects.
- Offline mutation queues, background upload sync or durable browser image drafts.
- Production deployment itself or a new account/Library deletion product workflow.
- Claiming implementation/release readiness from design approval or waiving its
  verification gates. This documentation-only task implements none of the feature.

## Independent Architecture Review

The earlier independent review on 2026-10-28 recommended Revise. The user then
resolved the architecture choices and authorized reconciliation/approval of a
consistent design. That decision closes the design findings as mapped below; it
does NOT convert the review into a passed runtime, security or deployment test.

### AR01-AR12 Final Disposition

| Review finding | Final approved resolution | Implementation verification |
| --- | --- | --- |
| AR01: failed COMMIT acknowledgement is not rollback | Fresh-primary reconciliation, outcome-unknown errors, no blind Create/object compensation | G04 |
| AR02: recovery lost Game identity after retirement | Immutable claimed_game_id in the same row, owner status gameId/gameExists, safe bounded/purged recovery | G04 |
| AR03: signed-token expiry did not bound cached access | Signed delivery REJECTED; authenticated no-store byte proxy selected, no token URL/redirect/304 | G03 |
| AR04: unselected native runtime and untested Angular integration | Noble/glibc amd64 target plus exact native package; metadata support verified, native/Angular execution conditional | G01/G02 |
| AR05: decoder limits did not bound pre-binding buffers | Early auth/rate/two-pipeline admission, bounded multipart/time/bytes; independent eight-read proxy cap | G03/G06 |
| AR06: key lacked immutable provider namespace | Persist namespace/key, reject mismatches, reviewed namespace migration, independent credential rotation | G04/G06 |
| AR07: retained successful tombstones could starve cleanup | Advance every nonterminal attempt, oldest-due order, fixed retirement, count/time budget and safe final purge | G04/G06 |
| AR08: implicit/missing/equal fields and stale EF state | Explicit coverUpdate only; omission=keep, no equality inference; expected-reference CAS and refreshed post-lock state/bootstrap | G04/G05 |
| AR09: late parent-owned upload and manual/search integration | Form/account/source invalidation; no Game write after pre-dispatch cancellation; same visible manual/search selector | G02/G05 |
| AR10: pixel rounding, color and animation assumptions | One-pixel tolerance, integer 2k:3k output, sRGB conversion, actual APNG/animated-WebP rejection | G01/G05 |
| AR11: local HTTP/fake mode and quota scope unclear | Non-Production loopback exception, fenced byte-store fake with real proxy/Core/decoder, per-Library limits plus monitoring | G03/G06 |
| AR12: baseline prohibitions required reconciliation | AGENTS/Product/Domain/Architecture now contain the approved narrow exception; status Approved with gates, no implementation claimed | G07 |

The final decision pass also replaces all expiry/signing DTOs/tests with ID-based
metadata and Blob proxy retrieval, records exact intent variants and stale-client
rejection, and adds an explicit lifecycle transition table. Historical signed-URL
sources below explain the rejected option, not a live alternative/approval question.

### Reconciled Authority Changes

| Document / section | Reconciled approved change | Preserved boundary |
| --- | --- | --- |
| AGENTS.md: stack, security, database and Cover Images | Permit only approved cover Storage/normalization/cleanup, proxy auth/no-store, explicit intent/CAS and conditional packages | No extra projects, broad media, client DB/Storage authority or waived invariants |
| Product v0.7: amendment, section 15, MVP/non-goals | One private uploaded cover, unified local editing, explicit save/remove, proxy privacy and recovery | Manual/search remain optional/explicit; no gallery/sharing/HEIC/AI |
| Domain v0.9: amendment, sections 5/16/34/36 | External-only URL plus exclusive managed FK, namespace/receipt/state rules and unknown-commit/CAS semantics | Existing Game/LibraryEntry ownership, acquisition, Family Friendly, playtime/Play Log/Picker rules |
| Architecture v0.4: stack, 21/30/31 and PWA | Narrow Storage adapter, private byte proxy, bounded API cleanup and Noble/native strategy | Two application projects, EF schema authority, no app-table RLS expansion, brokers or signed delivery |
| Feature 010 integration through 013/higher authority | Same selector, explicit search/visible URL/source attribution; new coverUpdate save boundary | Provider/SafeSearch/search-candidate persistence unchanged; no Feature 010 file modified here |
| Feature 011 integration through 013/higher authority | Atomic BOTH-null empty-cover predicate including managed ID | Import/link/playtime unchanged; cleanup is not Steam sync; no Feature 011 file/code modified here |

In the present staged-save design, cover_uploads is justified: it is needed before a
Game exists and after deletion, when a Game FK alone cannot own/recover orphan
objects. Game.CoverUploadId remains sufficient for the CURRENT managed cover; the
receipt and namespace solve different concrete recovery/locator requirements.
CoverImageUrl remains exclusively external in persistence AND responses; the
managed ID drives protected content requests. The unique/FK/exclusivity constraints are
appropriate; state/ownership transitions must not be falsely described as enforced
by an ordinary cross-table CHECK. These fields are within approved D6/D9, not
permission for generic assets or general exactly-once operation infrastructure.

## Implementation Gates

There are no remaining open architecture choices in this specification. At design
approval all gates below were pending execution, not passed by design approval.
Following D3b approval, G01 Passed fresh full image/Core/build and native
packaging/resource verification under the approved bounded-normalization policy:
94/94 image tests on host/Noble and 337/337 Core tests. Earlier 93/94 strict-input
results are historical. Exact results and residual risks are in the linked report;
this does not pass other gates or complete Phase 1.
Other unexecuted gates remain pending. A failed gate blocks
feature completion/enabling; report it and request a design change if the selected
solution cannot satisfy it. Do not substitute a codec/platform, weaken validation,
expose signed URLs or increase budgets merely to get a passing build.

| Gate | Required evidence before completion/release |
| --- | --- |
| G01 - Native runtime: Passed under D3b, 2026-10-29 | Verified 94/94 image tests on host/non-root Noble amd64, 337/337 Core, build, native pins/ELF/ldd/publish/asset-failure checks and bounded two-pipeline resources. The retained unreported AC-run fixture now explicitly requires fresh canonical output; all other security rejection tests and checks are unchanged. Exact evidence/residual risks: g01-strict-jpeg-implementation-report.md. This is bounded normalization, not exhaustive input conformance or Phase 1/release completion. Re-run after relevant changes. |
| G02 - Angular cropper | Verify pinned ngx peer/install/build compatibility with Angular 22/TypeScript and normal zoneless behavior, lazy loading, real keyboard/touch/rotation/Fit interactions, local-memory cleanup and measured initial/lazy bundle sizes. Preserve budgets and existing dialogs/search/manual behavior. |
| G03 - Security/proxy | Two-user and unauthenticated API checks, JWT expiry, current Ready/Attached association/retirement checks, private Storage direct-browser denial, no leaked credentials/links, bounded provider reads, correct no-store/challenge/error/HEAD/conditional semantics and no external-image Bearer token. Verify the real Storage adapter contract on an isolated local/staging private bucket. |
| G04 - Persistence/recovery | Generated EF migration/designer/snapshot, existing-row preservation, no pending model changes, real PostgreSQL FK/unique/check/state/quota/CAS races, first-Library bootstrap, unknown COMMIT acknowledgements, retained/purged receipts, same-handle at-most-one Create, namespace mismatch, cleanup/reference/late-write/fairness cases. |
| G05 - UX/E2E/manual | Complete deterministic desktop/mobile upload/crop/save, replacement/removal and failure/retry/recovery journeys using the real proxy/Core/decoder with fake Storage; close/source/account races and object-URL revocation. Real iOS Safari/PWA and Android Chrome/PWA camera/orientation/cancel/a11y/large-source review; unsupported HEIC remains actionable rejection. |
| G06 - Operations/resources | Exercise pre-binding auth/admission/rate/5 MiB plus 64 KiB total-body limits, chunked/slow bodies, two upload/eight proxy caps, bounded memory in the selected container budget, provider timeouts, durable quotas, fixed tombstone retention/fair cleanup, API restart and optional-disabled-mode recovery. Record private-bucket settings, server secret/namespace configuration, operator reconciliation/runbook and provider/API egress monitoring/spend limits. No resource provisioning occurs in this docs task. |
| G07 - Regression/closeout | Run complete Core and real-PostgreSQL integration suites, Steam subset, frontend units/lint, production/E2E builds, Playwright, EF pending-model check and git diff --check. Verify every AC with actual results and preserve 006/008/010/011/012 behavior. Do not copy prior milestone counts as new evidence. |

The hosting vendor/region, actual secret values and image digests are deployment
configuration to supply under these gates, not unresolved changes to the selected
container/platform/delivery architecture. No provider signup, deployment, dependency
installation or native test has been executed in this specification finalization.

## Sources And Research Limits

Checked 2026-10-28. No packages were installed, no bucket/policy was provisioned,
no application schema was changed, and no live project credentials were inspected.
Upstream research supports the selected design; executable integration/runtime
compatibility and application acceptance remain the pending implementation gates.

- Supabase private/public buckets: https://supabase.com/docs/guides/storage/buckets/fundamentals
- Signed-URL cache/expiry independence and invalidation: https://supabase.com/docs/guides/storage/cdn/smart-cdn
- Signed read capabilities and Auth-key rotation distinction: https://supabase.com/docs/guides/storage/serving/downloads
- Storage access control/server service-key bypass: https://supabase.com/docs/guides/storage/security/access-control
- Standard small-file uploads/immutable-key recommendation: https://supabase.com/docs/guides/storage/uploads/standard-uploads
- Provider limits: https://supabase.com/docs/guides/storage/uploads/file-limits
- Storage API deletion and SQL-orphan warning: https://supabase.com/docs/guides/storage/management/delete-objects
- Pricing: https://supabase.com/pricing and https://supabase.com/docs/guides/platform/manage-your-usage/storage-size
- Alternative pricing: https://developers.cloudflare.com/r2/pricing/
- ngx package/peers/license: https://registry.npmjs.org/ngx-image-cropper/9.1.7
- ngx capabilities/source: https://github.com/Mawi137/ngx-image-cropper
- CropperJS alternative: https://registry.npmjs.org/cropperjs/2.3.1 and https://fengyuanchen.github.io/cropperjs/guide.html
- Native capture hint: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Attributes/capture
- Canvas limitations: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/canvas
- SkiaSharp version/license/native deployment: https://www.nuget.org/packages/SkiaSharp/4.154.0 and https://github.com/mono/SkiaSharp
- Reviewed SkiaSharp net10.0 dependency manifest: https://api.nuget.org/v3-flatcontainer/skiasharp/4.154.0/skiasharp.nuspec
- Matching Linux package manifest (not proof of runtime compatibility): https://api.nuget.org/v3-flatcontainer/skiasharp.nativeassets.linux.nodependencies/4.154.0/skiasharp.nativeassets.linux.nodependencies.nuspec
- Exact Linux native package-entry inventory: https://api.nuget.org/v3/catalog0/data/2026.10.28.02.00.14/skiasharp.nativeassets.linux.nodependencies.4.154.0.json
- Official runtime/SDK tag support: https://github.com/dotnet/dotnet-docker/blob/main/README.aspnet.md and https://github.com/dotnet/dotnet-docker/blob/main/README.sdk.md
- .NET 10 supported OSes: https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md
- Noble amd64 base dependencies: https://github.com/dotnet/dotnet-docker/blob/main/src/runtime-deps/10.0/noble/amd64/Dockerfile
- RID and native asset distinction: https://learn.microsoft.com/en-us/dotnet/core/rid-catalog
- ImageSharp alternative/license: https://www.nuget.org/packages/SixLabors.ImageSharp/4.1.3/License
- System.Drawing limitation: https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/7.0/system-drawing
- Supabase changelog reviewed: https://supabase.com/changelog.md . Upcoming TLS CBC
  cipher removal affects Storage HTTP APIs from 2026-11-10 at the earliest; use
  supported modern browser/.NET TLS stacks, not compatibility downgrades:
  https://supabase.com/changelog/cbc-tls-ciphers-deprecated

## Next Step

Feature 013 is Approved for a separate implementation task under these contracts
and G01-G07. This specification-only pass has reconciled authority/instructions
and does not implement the feature, generate migrations, create buckets, install
dependencies, commit or push. Report implementation verification separately.
