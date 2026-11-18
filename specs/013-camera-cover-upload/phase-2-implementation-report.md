# Feature 013 - Phase 2 Implementation Report

Date: 2026-10-30

**Phase 2 implementation and automated verification: COMPLETE.**
**Feature 013 release: NOT READY. Do not deploy or enable production uploads.**

Security/operations follow-up, 2026-11-02: compatible Angular 22.2.1/Zod 4.6.6
updates resolve the reported runtime advisories, with 465 frontend tests and all
22 browser cases passing again. The 18-scenario whole-API Linux run and current
994-case PostgreSQL results are in the [G06 report](g06-operational-verification.md).
This report's original version/audit/count snapshot below is historical; its
physical-device and real-provider release limitations are not waived.

This status is limited to the user's Phase 2 implementation/automation definition.
Physical-device review and outstanding G03/G06 evidence are not passed or waived.
Phase 1 remains partial for those backend release gates. No backend lifecycle,
decoder, migration, native pin or production-upload configuration was changed.

## Implemented

### Contract And Save Boundary

- VideoGame/BoardGame input types and serialization use optional `coverUpdate`:
  keep, external, upload and remove. Omission remains keep. No legacy top-level
  cover fields, Blob URLs or image bytes enter Game JSON. Explicit Edit changes
  include both nullable original references; Create omits expectedCover.
- Both forms retain all existing quick-add, acquisition, Platform, progress,
  player-count, Family Friendly, Rating and Notes behavior. Manual URL/search
  choices are explicit intents, not inferred from equality or empty initialization.
- Parent forms own the local draft; component-scoped `CoverSave` owns the upload,
  claim/save and receipt orchestration shared by both management lists. A local
  Blob is uploaded only after final valid submission. Duplicate submissions and
  cover source changes are disabled during save; progress is honestly indeterminate.
- Cover-only Edit uses `/games/{id}/cover`; metadata changes use the normal
  type-specific update atomically. Create uses the normal type-specific POST.
  No Steam logic was duplicated or changed; explicit user covers retain precedence.
- Known failures retain the prior saved cover, metadata edits, local crop and live
  Ready handle. An explicit retry reuses that handle. Expiry offers explicit
  re-upload. A 409 fetches authoritative current references and offers Keep current
  cover or Use pending cover without resetting other form fields.
- Network/5xx/unknown responses after Game dispatch block blind retry/Create.
  Explicit receipt recovery uses the same handle and refetches its original Game
  even after retirement. Never-claimed live Ready allows explicit same-handle
  retry; missing/purged/deleted/unavailable evidence remains blocked with Library
  inspection guidance. No-handle external/metadata uncertainty is also blocked,
  not falsely classified as rollback. The stale-PWA error provides refresh guidance.
- Close/Escape/backdrop/cancel/destruction invalidate outstanding work. Upload
  completion cannot dispatch a Game write after close. Closing after Game dispatch
  retains uncertain recovery state while the owning page exists, without retaining
  the local photograph. New-account callbacks cannot close or mutate a newer form.
- Staged cancellation is best effort and never applies to a possibly claimed
  receipt; 409 Attached requires reconciliation. TTL cleanup remains the backstop.

### Unified Selection And Editing

- Extended the existing `CoverImageSearch`, not a competing uploader/page. Manual
  URL stays visible/editable alongside existing explicit search/gallery/source
  attribution and the current/pending preview. Search remains independent of upload.
- Separate ordinary file and `capture="environment"` inputs accept JPEG, PNG and
  static WebP. Camera is a hint, not a live stream or guaranteed device capability.
  A canceled chooser/editor retains the previous draft. Disabled/unavailable upload
  options leave external URLs and search usable.
- Pinned and locked the approved `ngx-image-cropper` **9.1.7**, with no additional
  frontend runtime dependency. Installed Angular common/core are 22.1.2 and
  TypeScript is 6.0.3. Metadata peer compatibility is supplemented by actual builds,
  zoneless tests and desktop/mobile browser execution.
- The inline editor is genuinely lazy via Angular `@defer`. It supplies portrait
  Crop 2:3, zoom, drag/resize, keyboard arrows/Shift+arrows, positioning buttons,
  90-degree rotation, Reset, Use image and Cancel editing. VideoGames start in Crop;
  BoardGames start in Fit, preserving whole artwork with white padding.
- A narrow wrapper around the pinned cropper owns cancellable local raster loading
  and URL disposal while using its crop state, keyboard and Blob-export facilities.
  It does not patch the dependency or install a replacement cropper/codec.
- Source byte size is checked before decoding: 20 MiB, 24 million pixels and edge
  8192. Working images are reduced to edge <=2048 before the editor; original decode
  resources are released. Unsupported/animated containers and decode/canvas failures
  show actionable errors; HEIC conversion is not introduced.
- Export is a fresh approximately-quality-90 JPEG Blob with white padding, integer
  `2*k` by `3*k` dimensions, minimum 160x240 and maximum 960x1440, without enlarging
  source content. Rounding/odd sizes and zoom sampling are tested. No Base64 image
  state, original metadata upload, remote-image conversion or persistent draft store.
- Local URLs, image bitmaps, canvas resources and stale exports are disposed on
  replacement/cancel/account change/success/destruction. Async preparation/export
  cannot replace a newer draft. Local previews clearly say Not saved.

### Managed Display And Authentication

- Added the narrowly reusable `CoverImage` renderer to both management lists,
  Library, Picker current/history, Play Log and saved-selector preview. External
  images retain direct `<img>` delivery; they are never sent through authenticated
  HttpClient or downloaded for local editing.
- Managed sources build only a known-API/validated-upload-ID content request, fetch
  a Blob with the existing Bearer interceptor and create a component-local URL.
  There are no signed Storage links, URL tokens or direct browser Storage calls.
- IntersectionObserver admits visible/near-visible content. A cover-specific
  coordinator limits active reads to four and deduplicates only same-session,
  same-ID in-flight work. Completed Blobs are not retained in a shared cache.
  Requests bypass service-worker caching and use no-store transport options.
- Source/session changes, image failure and destruction revoke local URLs and
  invalidate responses/queued work. Current 401 uses normal sign-out/login handling;
  a stale source/account 401 cannot sign out the newer account. Errors use placeholders
  and explicit retry, honoring Retry-After without polling. Picker state is untouched.
- Form/draft/recovery lifetime uses Supabase's stable `session_id` plus user identity,
  not the changing token/session-object reference. Token refresh preserves the same
  login's unsaved work and receipt; a different login/logout/account invalidates it.
  This key is presentation identity only, never client-side authorization. Custom
  development JWTs without session_id conservatively use the session snapshot.
  Managed display may conservatively reauthorize bytes on token replacement.

## Automated Verification

Fresh final results, not copied Phase 1 counts:

| Command/check | Result |
| --- | --- |
| `npm test -- --watch=false` | **465 passed, 38 files**, zero failures/skips |
| `npm run lint` | Passed |
| `npm run build -- --configuration e2e` | Passed |
| `npm run build` | Passed; initial budget now satisfied |
| `npm run e2e -- --reporter=line` | **22 passed**, 11 desktop Chromium + 11 mobile Chromium; zero retries/skips |
| `dotnet build src/backend/GameLibrary.sln --no-restore` | Passed, zero warnings/errors |
| Complete Core suite, `--no-build` | **337 passed**, zero failures/skips |
| Complete IntegrationTests, `--no-build`, real PostgreSQL | **978 passed**, zero failures/skips |
| EF `has-pending-model-changes`, `--no-build` | No pending model changes |
| Tracked and untracked whitespace checks | Passed |

The full IntegrationTests run includes all image, cover HTTP/transport/contract,
lifecycle, isolation, migration, Steam and existing regression suites. It was not
limited to faked domain rules. The native G01 target was not rerun unnecessarily:
its processor/JPEG validator/structure/image-test SHA256 values remain identical
to Phase 1, and all 94 host image cases are included in the 978 current results.

Existing browser regressions in `e2e/app.spec.ts` were not deleted, skipped or
weakened. All previous 14 desktop/mobile cases now pass, including both formerly
failing creation journeys. Four new journeys run on both viewports:

1. VideoGame actual file/crop/keyboard/rotation/zoom -> upload/Create -> managed
   preview -> immutable cover-only replacement -> explicit removal, preserving
   Family Friendly and Platforms; proxy requests carry Bearer headers, not URL tokens.
2. BoardGame camera-input file fallback and default Fit, including a 320px mobile
   viewport and white-padding pixel assertion; known Game-save failure reuses Ready;
   a real concurrent API mutation triggers 409 while preserving edited Notes.
3. Server upload completes while its response is delayed; Escape closes the form,
   then release of the response cannot dispatch a Game Create or restore the draft.
4. Real Game Create commits but its browser response is replaced by an unknown-outcome
   503; explicit receipt recovery finds the original Game, with exactly one Create.

E2E enables fake Storage only through the existing Development/Test/auth-guarded
switches, plus process-local upload enablement. Real decoding, Core, PostgreSQL and
the authenticated content proxy remain active. Fixture bytes are deterministic real
PNGs selected through actual file inputs/editor, not stubbed final Game responses.
The full run honors real rate limits with an explicit Retry-After wait/retry where
needed; no production limiter was relaxed. Tests use only local port-5433 PostgreSQL
and the isolated E2E database. No remote Storage/bucket/policy/schema was touched.

### Bundles And Warnings

Both management routes now use Angular `loadComponent`, keeping form/selector/save
code out of initial navigation. The editor remains a further lazy boundary. This is
a loading-boundary change, not a redesign or new application state architecture.

| Production output | Raw | Estimated transfer |
| --- | --- | --- |
| Initial total | **676.31 kB** | **160.71 kB** |
| Lazy editor | 57.84 kB | 12.93 kB |
| Shared management chunk | 37.47 kB | 9.93 kB |
| VideoGame management chunk | 31.29 kB | 7.54 kB |
| BoardGame management chunk | 26.15 kB | 6.53 kB |

No budgets were raised. The 700 kB initial warning is resolved (Phase 1 baseline
704.66 kB). The existing Random Picker stylesheet warning remains: 4.13 kB versus
4 kB, below its error threshold. Vitest still warns about the pre-existing removed
poolOptions setting. EF tools are 10.0.7 versus runtime 10.0.11; no tooling update.

Dependency review: `npm audit --omit=dev --audit-level=high` reports **two high**
findings, in Angular Router (SSR denial-of-service advisories) and Zod. No advisory
was reported for the pinned cropper. These findings need separate applicability/
dependency-update review before release; this task did not silently upgrade the
Angular/Supabase graph or claim that an audit without cropper findings proves safety.

## Acceptance And Gates

| Scope | Evidence/status |
| --- | --- |
| AC01-04, frontend AC13-18/20/26 | Selector/preparation/editor/form/save unit suites and actual file/crop/save/failure/conflict/receipt E2E pass. Backend invariants remain covered by the full integration run. |
| AC05 | Automated 320px responsive controls and desktop/mobile chooser/crop paths pass; actual camera/PWA, touch/keyboard-open and large-photo physical memory remain manual. |
| Frontend AC09/21 | Content coordinator/renderer/page tests cover auth, stale accounts/sources, four reads, dedup, no completed cache, disposal/fallback/Retry-After. Real Storage denial is not established by these tests. |
| AC22/23 | Requested automated tests/lint/builds/browser/backend/model/whitespace pass, including all pre-existing browser regressions. |
| AC24 and feature-wide closeout | Not satisfied for release until physical devices and G03/G06 pass. |
| G01 | Previously passed; unchanged pipeline/pins; current host images pass. |
| G02 | Pinned install, Angular/zoneless interaction, keyboard/pointer, lazy loading and bundle automation pass. Physical-device accessibility/touch/memory review remains open. |
| G03 | Automated API/proxy/frontend isolation passes; isolated real Storage permission/adapter contract remains pending. |
| G04 | Current complete real-PostgreSQL suite and EF model check pass; lifecycle unchanged. |
| G05 | All deterministic desktop/mobile journeys pass; real iOS/Android camera/PWA review remains pending. |
| G06 | Phase 1 resource/operations gaps remain: full API 512 MiB upload+proxy load, deployed proxy/minimum-rate settings, bucket/configuration/monitoring/spend approval and operator evidence. |
| G07 | Current automated regression closeout passes. Feature/release closeout, dependency applicability review and remaining gate evidence are still outstanding. |

## Physical-Device Checklist

Use supported iOS Safari and installed PWA, and Android Chrome and installed PWA:

- Rear-camera capture hint, permission/chooser fallback, capture cancellation and
  returning focus to the correct action. Native camera behavior is not proven by
  Playwright `setFiles` or Chromium mobile emulation.
- Portrait/landscape camera EXIF orientation, 90-degree rotation, Reset, touch crop
  drag/resize, zoom and BoardGame Fit preserving the complete artwork.
- VoiceOver/TalkBack announcements, keyboard/focus order, 44px controls and the
  320px toolbar/dialog while the native keyboard is open.
- Large camera photos near 20 MiB/24MP/8192-edge bounds, allocation/decode/export
  failures, too-small crops and unsupported HEIC actionable rejection without crash.
- Slow/interrupted/offline upload, cancel/navigation/app backgrounding, session
  expiry, saved-cover preservation, explicit retry/receipt recovery and local URL
  disposal. No offline mutation queue or persisted image draft should appear.

Record actual device/OS/browser/PWA versions and outcomes. No such physical-device
test was executed here; do not mark those checks passed from emulator results.

## Repository Safety

All pre-existing staged, unstaged and untracked work was preserved. The index hash
remains `62092d1ed1bbe35197e650619a83a5a9eed14a4495a4223a795223ed11372439`.
The only runtime dependency added is the explicitly approved pinned cropper. No
new backend project, migration, provider integration, persistent image cache, live
camera stream, HEIC conversion or generic media framework was introduced.

Production upload defaults remain disabled. No resources were provisioned, no
credentials were exposed, and nothing was staged, committed, pushed or deployed.
Do not start release activities without separate authorization.
