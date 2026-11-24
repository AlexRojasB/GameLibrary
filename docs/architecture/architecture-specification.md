Architecture Specification v0.4 --- Game Library

Status: Approved
Version: 0.4

Native verification amendment, 2026-11-23 -- Approved CI publication/PR validation:

The test-only combined verifier (API + HTTP driver + image fixtures + simulated
Storage in one process/cgroup) may use a 768 MiB memory and memory+swap cap, with
no additional swap and a mandatory 640 MiB cgroup peak regression threshold.
Effective cgroup limits, complete coverage, teardown measurements and exit/OOM
state must be verified fail-closed. G01 and both asset-failure controls retain
512 MiB. Diagnostic 512/768/1024 profiles are comparisons, not alternate acceptance
profiles; no escalation or profile override is allowed in the required CI path.
The API-only initial/production verification budget remains 512 MiB. This exception
does not certify API-only compliance, guarantee future stability, close G06 or
change production configuration. The completed 18-trial Linux matrix supports
768/640 for normal required PR validation (6/6 at 768, maximum 531.296875 MiB).
The owner explicitly replaces the planned twelve diagnostic confirmations with
the five normal required checks; no further diagnostic workloads are authorized.
Publication is an independent PR to dev, not a PR #9 update, merge or deployment.
The measured peak covers awaited verifier teardown, not the remaining runtime-exit
tail: logging/validation/.NET exit occur after the final read. Docker exit/OOM
checks cover termination but cannot certify a brief tail peak above 640 and below
768 MiB. This limitation is accepted for PR validation, not silently resolved.
See specs/013-camera-cover-upload/native-runtime-resource-decision.md.

Feature 013 amendment, 2026-10-28 -- Approved design:

Sections 21/30/31 approve the narrow private-cover upload/normalization exception:
backend multipart upload to private Supabase Storage bucket game-covers, one
authenticated API image-byte proxy, one EF lifecycle table/current Game reference,
and bounded cleanup in the existing API process. No signed URLs are generated or
exposed. Angular -> ASP.NET Core/Core -> EF/PostgreSQL remains authoritative, with
exactly GameLibrary.Api and GameLibrary.Core and no direct Angular DB/Storage access.

The approved dependency choices are ngx-image-cropper 9.1.7 and API-side SkiaSharp
4.154.0 with SkiaSharp.NativeAssets.Linux.NoDependencies 4.154.0, conditioned on
the target-runtime verification in section 30. This approves DESIGN, not completed
implementation, provisioning or verified native compatibility. No dependencies,
resources or migrations are created by this documentation amendment. All tests,
Storage permission/provisioning checks, real amd64 codec/ABI smoke, mobile/a11y
and bundle-budget gates remain pending. Agent instructions and the Feature 013
specification must be reconciled with these decisions before implementation.

G01 amendment, 2026-10-29 -- Authorized bounded JPEG normalization:
Add libjpeg-turbo 3.2.1 exclusively as an API-side JPEG validation dependency.
Skia/libjpeg can recover truncated entropy while reporting successful decoding.
Use bounded structural/table/scan checks, full unscaled TurboJPEG decoding with
STOPONWARNING, then the unchanged Skia normalization/encoding pipeline. Reject
structural validation failures and all reported decoder warnings/errors. This
approved policy accepts unreported decoder-tolerated coefficient anomalies only
when the existing structure/diagnostic/resource checks pass; it does not certify
the input as fully T.81-compliant. Do not generalize this to truncation, malformed
headers/tables, animation, unsupported types or limit violations. Never store raw
uploads, suppress diagnostics or add a permissive fallback. Use the maintained public C
API with deterministic SafeHandle/native-buffer disposal, checked allocation bounds,
pinned official release artifacts and binary hashes, packaged license notices, and
fail-closed loading. No general JPEG engine, media framework or Skia replacement.
The Noble/glibc linux/amd64 target and all upload/resource limits remain unchanged.
This amendment authorizes implementation, not a passed G01 or completed Phase 1.

The later 2026-10-29 compatibility decision supersedes the original exhaustive
malformed-input/no-repair promise, not the existing validation checks. The AC-run
fixture remains nonconforming and is retained with an explicit canonical-output
expectation. G01 Passed fresh verification under this policy (94/94 image tests on
host/native Noble, Core/build and pinned native packaging/resource checks); see
specs/013-camera-cover-upload/g01-strict-jpeg-implementation-report.md. Phase 1
and the remaining HTTP/Storage/UI/release gates are not complete.

Manual review amendment, 2026-08-19:

Architecture remains unchanged at the component/project level. The amendment
clarifies that VideoGame player counts reuse existing `games.minimum_players` /
`games.maximum_players`, Completed-progress normalization is backend/domain
behavior, and Random Picker visible history is frontend-only volatile state.

Feature 010 amendment, 2026-08-24:

Cover image search, when implemented, is a backend-mediated external HTTP
integration. Angular calls the Game Library API; the API calls the configured
image-search provider. Provider credentials are server-side secrets and are never
sent to Angular. For Feature 010 itself the persisted model was the existing
optional external CoverImageUrl, requiring no schema change. This is historical
search-only scope; it does not exclude Feature 013's approved managed-cover model.

Feature 011 amendment, 2026-08-25:

Steam integration, when implemented, is backend-mediated and explicit. Angular
calls the Game Library API for link, status, preview and import operations. The
API verifies Steam OpenID callback data with Steam, stores the verified SteamID64
for the caller's Library, and calls Steam Web API from the backend only. Steam Web
API keys are server-side secrets and are never sent to Angular. Steam linking uses
browser redirects only for user consent/authentication with Steam; Steam is not an
application identity provider for Game Library. Automated tests must substitute
Steam at the provider boundary and must not call real Steam services.

1. Purpose

This document defines the approved technical architecture for the Game
Library MVP.

It translates the approved Product and Domain Specifications into the
smallest maintainable implementation structure that supports the current
requirements.

2. Authoritative Inputs

This architecture must remain consistent with:

docs/product/product-specification.md

docs/domain/domain-specification.md

Product and Domain rules take precedence over architecture. Conflicts
must be reported, not silently resolved.

3. Architecture Principles

Prefer the simplest implementation that satisfies approved
requirements.

Use one Angular frontend, one ASP.NET Core backend, and one managed
PostgreSQL database.

Use managed authentication instead of building identity
infrastructure.

Keep domain rules authoritative on the backend.

Do not add infrastructure for hypothetical future scale or
integrations.

Prefer framework capabilities over unnecessary third-party
abstractions.

4. Approved High-Level Architecture

                    +----------------------+
                    |    Supabase Auth     |
                    | Registration / Login |
                    | Sessions / JWT       |
                    +----------+-----------+
                               |
                               | Access JWT
                               v
+------------------------------------------------+
| Angular PWA                                    |
|                                                |
| UI / UX                                        |
| Supabase Auth client                           |
| Services / Signals / Local state               |
| Volatile Random Picker session                 |
+----------------------+-------------------------+
                       |
                       | HTTPS / JSON + cover multipart/bytes
                       | Authorization: Bearer JWT
                       v
+------------------------------------------------+
| ASP.NET Core Web API                           |
|                                                |
| Supabase JWT validation                        |
| User isolation                                 |
| Domain/application rules                       |
| Search / filtering / sorting                   |
| Random Picker                                  |
| EF Core                                        |
+----------------------+-------------------------+
                       |
                       | Npgsql / TLS
                       v
+------------------------------------------------+
| Supabase PostgreSQL                            |
|                                                |
| Application schema                             |
| Supabase-managed auth schema                   |
+------------------------------------------------+

Supabase is used as:

Managed PostgreSQL hosting.

Authentication provider.

Private object storage for Feature 013's single managed cover per Game, accessed
only through the ASP.NET Core backend (section 21), not directly by Angular.

Supabase is not the application backend.

Angular does not directly access application database tables in the MVP.

Angular does not directly access Supabase Storage for managed covers. The API
accesses the private game-covers bucket through a narrow Storage HTTP adapter.

5. Technology Choices

Frontend

Angular.

TypeScript.

Angular Router.

Angular forms.

Angular PWA/service worker.

supabase-js for authentication flows.

Use Angular services, signals and local/component state where
appropriate.

Do not introduce NgRx unless a future approved requirement demonstrates
a concrete need.

Backend

ASP.NET Core Web API.

.NET 10; Feature 013's concrete Linux runtime/native target is fixed in section 30.

EF Core.

Npgsql PostgreSQL provider.

JWT bearer authentication validating Supabase-issued access tokens.

ASP.NET Core Identity is not used.

Database, Auth and Private Storage

Supabase PostgreSQL.

Supabase Auth.

Supabase Storage, private game-covers bucket for the bounded cover flow in section 21.

6. Repository Structure

game-library/
├── AGENTS.md
├── README.md
├── docs/
│   ├── product/
│   │   └── product-specification.md
│   ├── domain/
│   │   └── domain-specification.md
│   └── architecture/
│       └── architecture-specification.md
├── specs/
├── src/
│   ├── frontend/
│   └── backend/
└── tests/

Feature specifications live under specs/.

7. Backend Project Structure

Use two .NET projects:

src/backend/
├── GameLibrary.Api/
└── GameLibrary.Core/

Do not create additional Domain, Application, Infrastructure, Contracts
or SharedKernel projects for the MVP.

GameLibrary.Api

Responsibilities:

HTTP endpoints.

Supabase JWT validation.

Authentication/authorization boundary.

CORS.

Request/response contracts.

Dependency injection.

Configuration.

Mapping HTTP concerns to Core operations.

HTTP-specific types should remain out of Core.

GameLibrary.Core

Responsibilities:

Domain entities and concepts.

Domain invariants.

Application services/use cases.

EF Core DbContext.

EF Core mappings.

EF Core migrations.

PostgreSQL persistence behavior.

The Core project may use EF Core directly.

Do not introduce generic repository or Unit of Work abstractions over EF
Core.

8. Logical Backend Areas

Organize code around capabilities where useful:

Library.

Games.

Platforms.

RandomPicker.

Authentication belongs primarily at the API boundary because user
identity is supplied by Supabase.

VideoGame and BoardGame are logical areas within Games, not separate
projects or services.

9. Authentication

Supabase Auth is the approved identity provider.

It handles:

Registration.

Login.

Logout.

Sessions.

Password reset.

Email verification where configured.

Angular uses supabase-js for authentication.

After authentication, Angular sends the Supabase access token to the
API:

Authorization: Bearer <access-token>

The API validates the token and derives the authenticated user identity
from the token's sub claim.

The API must never trust a user ID supplied by the client for
authorization.

Authentication secrets or privileged Supabase credentials must never be
exposed to Angular.

Prefer standards-based JWT/JWKS validation supported by the current
Supabase configuration rather than hard-coding long-lived signing
secrets when possible.

10. Authorization and User Isolation

The ASP.NET Core API is the only application component permitted to
access application database tables.

Every Library, Game, LibraryEntry and user-owned Platform operation must
be scoped to the authenticated Supabase user ID.

User isolation is authoritative on the backend.

Client-side filtering is not authorization.

One user must never be able to read or modify another user's private
library by manipulating identifiers.

11. Row Level Security

RLS is not part of the MVP application security model.

Reason:

Angular does not directly access application tables.

The API is the sole application database accessor.

User isolation is enforced in ASP.NET Core using the authenticated
Supabase user ID.

Adding RLS would introduce a second authorization mechanism that must
remain synchronized with backend rules.

If direct browser-to-Supabase application-data access is introduced in
the future, this decision must be revisited and RLS would become a
required security consideration.

12. Database Access

The API accesses Supabase PostgreSQL using EF Core + Npgsql.

Database credentials are server-side secrets.

Angular must not receive database credentials or privileged service-role
credentials.

Use normal relational constraints where they naturally protect data
integrity, while keeping transitional/cross-record business behavior in
the application/domain layer when that is clearer.

13. Schema and Migration Authority

EF Core migrations are the single source of truth for the
application database schema.

Rules:

Application tables are created/changed through EF Core migrations.

Do not manually edit application tables in the Supabase dashboard.

Do not use Supabase CLI database migrations for the application
schema alongside EF migrations.

Supabase-managed authentication schemas such as auth.* remain
managed by Supabase and are not modified by application EF
migrations.

Migrations are committed to source control.

Schema migrations should use an appropriate direct PostgreSQL connection
rather than relying on a transaction-pooling connection that is
unsuitable for migration tooling.

14. Transactions

The application uses one PostgreSQL database.

Operations that must preserve multiple related invariants execute
atomically using EF Core/PostgreSQL transaction behavior.

No distributed transactions are required.

15. Domain Validation

Backend validation is authoritative.

Examples include:

Owned VideoGame requires at least one Platform.

Wishlist/Interested cannot contain GameStatus or ProgressPercentage.

AcquisitionStatus transition rules.

BoardGame player-count rules.

VideoGame optional player-count rules.

Owned VideoGame Completed => ProgressPercentage = 100 normalization.

Platform deletion protection.

Rating and progress ranges.

Game/LibraryEntry deletion behavior.

Random Picker eligibility and filter semantics.

Angular should mirror useful validation for immediate UX feedback but
cannot be the only enforcement layer.

16. API Principles

Use a JSON HTTP API, with Feature 013's bounded multipart upload and authenticated
image-byte response exceptions (section 21).

Exact endpoints belong to feature specifications.

General rules:

Resource-oriented HTTP design.

Appropriate HTTP verbs and status codes.

Consistent validation/error response format.

API contracts are separate from EF entities.

Do not add GraphQL.

Do not add pagination until a feature specification demonstrates
that the MVP needs it.

17. Error Handling

Use centralized API error handling.

Expected validation/domain failures return predictable client-readable
responses.

Unexpected exceptions must not expose stack traces or sensitive details
to clients.

18. Frontend Structure

Proposed Angular organization:

src/frontend/src/app/
├── core/
├── shared/
├── features/
│   ├── auth/
│   ├── library/
│   ├── games/
│   ├── platforms/
│   └── random-picker/
└── app.routes.ts

Keep core and shared small.

Do not move code into shared abstractions until it is genuinely shared.

Keep feature-specific code close to its feature.

19. Frontend State

Use:

Component/local state.

Angular services.

Angular signals where useful.

No global state-management library is required for the MVP.

Random Picker session state is frontend-only and volatile.

Refreshing/leaving the picker clears that session according to the
Domain Specification.

Visible Random Picker result history is part of that frontend-only volatile
session state. It is not stored in PostgreSQL, localStorage, or sessionStorage.

20. PWA

Use Angular-supported PWA/service-worker tooling.

MVP goals:

Responsive UI.

Installable PWA where supported.

Appropriate static/application-shell caching.

The MVP does not require:

Offline CRUD.

Offline database.

Offline synchronization.

Conflict resolution.

Authenticated data operations may require network connectivity.

Feature 013 private image bytes, upload/status responses and local image drafts
must not enter service-worker caches or persistent browser storage. Only volatile
component Blob/object-URL presentation is allowed; section 21 governs disposal.

21. Cover Images

Each Game has at most one current cover: an external CoverImageUrl, a private
managed CoverUploadId, or neither. CoverImageUrl remains external-only in BOTH
database and every response model, including Library, management, Picker history
and Play Log; it is null for managed covers. Expose the managed ID separately,
never a signed URL or a managed display URL substituted into CoverImageUrl.
Absent/unavailable images use a placeholder without mutating the saved selection.

An approved feature may provide optional cover-image search assistance through
the ASP.NET Core API. This integration follows the normal boundary:

Angular PWA -> Game Library API -> external image-search provider.

Angular must not call the external provider directly and must not receive provider
API keys, subscription tokens or other provider credentials.

The backend uses normal `HttpClient` / `IHttpClientFactory` conventions,
server-side configuration, short timeouts and predictable ProblemDetails error
responses for provider unavailability. Missing provider configuration must not
prevent the rest of the application from starting or using manual CoverImageUrl
entry.

Feature 013 private Storage boundary

Use one private Supabase Storage bucket named game-covers per environment.
Api owns multipart transport, early JWT/usable-sub authentication, HTTP contracts,
configuration, typed HttpClient Storage adapter, concrete image decoder and cleanup
BackgroundService host. Core owns Library authorization, quota, lifecycle,
claim/retirement, EF transactions and persistence. Keep HTTP types such as
IFormFile/ProblemDetails out of Core; use a bounded byte/stream representation and
a narrow cover-specific provider boundary. No SDK or generic media framework is
required solely for Storage HTTP calls. Retain exactly the two application projects.

Only the backend may read/write/list/delete cover objects using server-only
credentials. Browser anon/authenticated roles must have no direct bucket read,
write, list, delete or signing access. Audit broad existing policies/default
grants; bucket privacy alone is insufficient. Storage default-deny/access setup
is separate from unchanged application-table authorization, not application RLS.
Do not expose cover_uploads through browser Data API grants/default privileges.
Do not modify auth.* or Storage-managed tables/schemas through SQL/EF. Provision
privacy, image/jpeg allowlist and 2 MiB object limit through supported Storage APIs
or deployment setup; use those APIs for physical deletion too.

Use server-generated immutable keys <libraryId:N>/<coverUploadId:N>/cover.jpg,
with overwrite/upsert disabled. Persist immutable storage_namespace (canonical
trusted Storage API base plus bucket), not credentials or arbitrary record URLs.
Every read/write/delete must match it to an explicitly configured trusted adapter.
Namespace mismatch disables affected operations and alerts the operator; a missing
object in a different bucket is NOT deletion success. Credential rotation in the
same namespace is permitted; project/base/bucket changes need reviewed migration
or drain, not silent configuration reinterpretation. Never use filenames, email,
Game names, client paths/URLs or Storage owner_id as Library authorization.

Uploads default disabled. Missing configuration must not prevent startup or
external-URL CRUD/search. Validate the trusted Storage base without userinfo/query/
fragment, require production HTTPS, and do not follow provider redirects or forward
credentials across them. Explicit Development/Test-only literal-loopback HTTP may
support local Supabase; it is invalid in Production or for other hosts. Verify
bucket privacy/effective permissions before enabling; misconfiguration fails
managed operations safely with operator alert, never a public-URL fallback.

Authenticated byte delivery

The ONE managed-image byte route is GET /cover-uploads/{id}/content on ASP.NET
Core. Every request requires the normal validated Bearer JWT/usable sub and fresh
caller-Library authorization BEFORE reading Storage or serving bytes. A Ready
upload must be owned and unexpired; Attached additionally requires the current
same-Library Game reference. Uploading/DeletePending content is unavailable.
Foreign/missing handles are indistinguishable 404s. Status/options endpoints
return lifecycle/recovery information, not alternate byte links or read tokens.

The API reads the recorded object through its server-authenticated Storage adapter
and returns bounded canonical JPEG bytes as image/jpeg with
X-Content-Type-Options: nosniff. Never generate/expose signed read URLs, redirect
to Storage, accept query-string access tokens, or expose Storage credentials.
Set Cache-Control: private, no-store on ALL content responses, errors/challenges
included. Disable ETag/Last-Modified validators, conditional/304 handling, response
caching and CDN delivery for this route; authorize even conditional requests and
never answer with cached bytes or 304. No range/alternate public delivery is needed.
Managed upload/lifecycle/status responses and their errors also use private,
no-store and must not be persistently cached.
The API/proxy deployment must preserve these headers/error rules and bypass cache.
Provider-internal transport caching is not a client access path or authorization.

Angular uses HttpClient with the existing API Bearer interceptor to fetch a Blob,
then creates a component-local object URL for img presentation. Native img requests
must not fetch the authenticated API path directly without that header. Revoke
the URL, drop Blob state and cancel/ignore pending old-generation responses on
logout/account switch, source change and destroy. Apply equivalent disposal to
local editing previews on cancel/success/navigation. No service-worker, CDN,
localStorage, sessionStorage, IndexedDB or other persistent image/response cache;
no shared cross-account Blob cache. An explicit retry may refetch a visible failed
image; no signed-link renewal or background polling is needed. Presentation changes
must not reset Picker order, shown IDs or result states. These rules do not recall
already downloaded copies or turn logout into identity-provider JWT revocation.

Local crop and authoritative normalization

Approve ngx-image-cropper 9.1.7, lazy-loaded for local image editing. Its published
Angular common/core >=17.3.0 peers admit Angular 22 at the metadata level ONLY;
actual Angular 22 builds, mobile memory/touch/keyboard/a11y and bundle-budget checks
remain pending. Measure initial/lazy chunks without silently raising budgets.
Keep one selector with visible manual URL entry and explicit search; source changes
record explicit intent rather than inferring it from URL equality. Local crop uses
2:3, zoom/reposition/reset and 90-degree rotation. BoardGames default to Fit whole
image with white padding to preserve square art; VideoGames default to Crop.
Support JPEG/PNG/static WebP, not HEIC conversion, AI/gallery or remote-image copying.
The original/working image stays local until Save; no Base64 copies in app state.

Client selection bounds are 20 MiB, 24 million pixels and maximum edge 8192;
reduce working edge to at most 2048 before crop. These are UX bounds, not proof
of safe browser allocation. Handle decode/export/orientation/memory failure.
Final API upload is one non-empty multipart file named file, at most 5 MiB plus
64 KiB bounded multipart overhead, including chunked requests. Authenticate and
apply user rate/admission limits BEFORE binding/buffering the body. At most two
whole upload pipelines per API process, queue zero; body receive budget 60 seconds
with proxy/minimum-rate controls. Upload rate limits are 5/minute AND 20/hour per
validated sub, queue zero; status/content reads 120/minute per sub, queue zero.
These in-process controls are not distributed rate limits.

Use API-side SkiaSharp 4.154.0 and matching
SkiaSharp.NativeAssets.Linux.NoDependencies 4.154.0 under section 30's native gate,
plus libjpeg-turbo 3.2.1 for JPEG validation only, not System.Drawing or a shell
image utility. Before Skia, JPEG requires bounded self-contained structure/table/
scan validation and complete TurboJPEG STOPONWARNING decoding; decoder success
without warning handling or SOI/EOI alone is insufficient. This is bounded raster
normalization, not exhaustive JPEG conformance: unreported coefficient tolerance
is accepted only after all existing checks pass. No warning suppression or new
exceptions for rejected structural/truncated/header/table cases are allowed.
Detect actual JPEG/PNG/WebP signatures
and container/frame structure, fully decode, reject animation/multiframe,
truncation/bombs and unsupported formats, and bound checked dimensions before
full decode to 4 million pixels and edge 4096. Validate oriented 2:3 dimensions;
one-pixel crop rounding may be normalized to integer 2*k by 3*k (80 <= k <= 480)
with white padding, no extra artistic crop or upscaling. Apply orientation, flatten
alpha onto white, convert supported color profiles to sRGB, then strip EXIF/GPS,
thumbnails/trailing payloads and other metadata. Encode fresh JPEG quality 85,
160 x 240 minimum, 960 x 1440 maximum, <=2 MiB; never store original bytes.
Use actual animated APNG/WebP fixtures: first-frame decode is not animation
validation. Bound resources and dispose buffers; cancellation alone does not
make native decode preemptible. Provider calls have 15-second timeouts, without
automatic upload replay. Never fetch user-supplied external URLs for processing.

EF lifecycle and current reference

EF Core migrations remain the sole application-schema authority. During
implementation add only nullable games.cover_upload_id and one cover_uploads table,
plus its constraints/indexes/model snapshot. Existing URLs and 011/012 data remain
unchanged; no URL backfill, image binary column or extra Game version column.

cover_uploads fields: id UUID PK; required library_id FK; bounded immutable
storage_namespace and object_key; byte_size integer 1..2097152; constrained state
Uploading/Ready/Attached/DeletePending; created_at, cleanup_after, retired_at as
UTC timestamp-with-time-zone; nullable claimed_game_id UUID. Reserve canonical
bytes before provider write. Index library_id, unique (storage_namespace,
object_key), and due non-attached cleanup ordered by (cleanup_after, id).
Game's current FK is restrictive and unique when non-null; check that external
URL and managed ID cannot both be non-null. Use a restrictive Library FK to keep
locators until cleanup, not cascade-away orphan creation.

Row-local constraints: Uploading/Ready have a cleanup deadline and no retirement/
claim; Attached has claimed_game_id with no cleanup/retirement time; DeletePending
has cleanup_after and retired_at and may retain claimed_game_id. Dates cannot
precede created_at. Do not use moving now() expiry CHECKs or claim that cross-table
Library/transition rules are enforced by row CHECKs. Core checks them under locks.
claimed_game_id is immutable after first claim and has NO restrictive Game FK:
it survives cover retirement and Game deletion until safe bounded purge. For
Attached it equals the unique current referencing Game. It is a recovery receipt,
not another current association, permission to reattach or general operation log.

Explicit intent, transactions and unknown outcomes

Create/update and cover-only requests accept optional coverUpdate intents keep,
external, upload and remove. Omitted is keep, NEVER clear; null/unknown/malformed
is 400. keep preserves the current stored references (none on Create). external
requires one valid non-empty external URL; upload requires one owned live Ready
handle; remove explicitly chooses neither. Reject incompatible payloads. Legacy
URL fields cannot implicitly clear/replace a cover; explicit changes use this
intent contract, not desired-value/original-value equality.

external/upload/remove on existing Games require expectedCover containing BOTH
nullable persisted external URL and upload ID, mutually exclusive. Normalize the
URL using existing rules; compare ordinally and IDs by value against refreshed
locked current references. Missing/null/malformed required snapshots are 400;
mismatch is 409 with no partial mutation. Metadata-only keep preserves any newer
current cover without CAS; no extra Game version column.
Cover-only saves touch no unrelated metadata. Combined form saves and Create
persist Game + LibraryEntry + claim/old-cover retirement atomically. Omitted keep
does not weaken validation of any unrelated full-form field.

External input keeps trimmed HTTP(S)/2048-character validation, but rejects
data:/blob:/file:/Base64 values and this application's managed bucket/content
URLs; an owned upload handle is required instead. URL recognition is validation,
never ownership proof or deletion authority.

Bootstrap the caller's Library in a clean context before staging/lifecycle
transactions; do not invoke an independently saving/tracker-clearing bootstrap
while holding unsaved Game/upload changes. Then use consistent lock order Library,
existing Game, affected uploads in stable ID order. Re-read/reload AFTER locking
(a tracked EF entity is not automatically fresh), and test expiry against fresh
server UTC after lock waits, not transaction-start time. Do not hold SQL locks
during provider write/read/delete. Commit Uploading/locator/reservation before
dispatch, release locks, upload immutable canonical bytes, then conditionally
finalize live Uploading -> Ready; no late completion may revive a retired/expired
row. Claim live Ready -> Attached with immutable claimed_game_id in the Game commit.

A COMMIT acknowledgement exception or lost/canceled response is NOT proof of
rollback, even if HTTP reports 5xx. Reconcile through a fresh context/connection
against the authoritative primary using the SAME handle/first-claim receipt.
Never automatically replay Create or delete a possibly Ready/Attached object as
compensation. Confirmed never-claimed live Ready permits deliberate same-handle
retry; any previously claimed handle prevents another Create in EVERY state.
Refetch its original owner-scoped Game; later replacement/removal/deletion must
not erase the receipt prematurely or imply the old request still matches current
metadata. If primary/receipt is unavailable or purged, return safe unknown/deleted
status and require explicit user resolution/Library inspection, not fresh-upload
Create. Use safe cover_outcome_unknown ProblemDetails where outcome cannot be
established. This is bounded receipt recovery, NOT general exactly-once CRUD.

Confirmed pre-commit failure/rollback preserves the old cover and rolls back Game,
claim and retirement together; keep a still-live Ready handle for explicit retry.
Only after committed retirement may bytes be deleted. Never delete/overwrite an
active FK target, external URL target or an old object before replacement commit.
Storage failure cannot resurrect a SQL-deleted Game or undo a committed save.
Explicit choices beat Steam: fill CoverImageUrl only with an atomic predicate
requiring BOTH references null. Remove saves neither, without old fallback or
immediate enrichment; later EXPLICIT Steam fill is allowed. All other Feature
011/012 invariants, Steam playtime and Play Log behavior remain unchanged.

Bounded cleanup and cost controls

One cover-specific BackgroundService in the existing API runs at startup and every
15 minutes, at most 100 due rows per pass, starting no further operations after
60 seconds. An already-started bounded operation may finish; do not abort a commit
and call it rollback. No overlapping passes per process, new worker/project,
broker, distributed transaction, generic outbox or external scheduler. Database
state/locks protect multi-request correctness, not in-memory locks.

Uploading expires after 15 minutes; Ready after 24 hours from completion. Expired
or canceled unclaimed records become irreversible DeletePending only under fresh
state/reference checks. Keep fixed retired_at and a 24-hour tombstone/receipt
retention interval, with ten-minute grace for abandoned uploads. Known committed
replacement/deletion may attempt immediate physical deletion. Retry/re-delete
during retention; missing in the RECORDED namespace is idempotent success. Advance
cleanup_after on EVERY nonterminal attempt, success included, at least 15 minutes
ahead; oldest due (cleanup_after, id) first prevents retained successful tombstones
from starving other uploads. Retries must not restart retired_at/retention.

Final purge requires elapsed retention, confirmed deletion/missing in the recorded
namespace and fresh primary state/no-active-reference recheck. Never purge while
provider outcome is unknown or a Game FK is active. Irreversible retirement protects
physical deletion outside SQL; the FK protects metadata purge, not provider bytes.
Record failed/unknown writes durably, and recheck live write deadline before
provider dispatch. Finite grace cannot prove arbitrarily late provider writes
impossible: retain operator namespace-scoped Storage API reconciliation for
persistent/late orphans. API/provider outages delay cleanup; no zero-orphan or
wall-clock erasure guarantee. No new account/Library-delete UI is introduced.

Serialize admission/reservations per Library in PostgreSQL: at most 250 MiB and
ten unclaimed unexpired Uploading/Ready uploads. DeletePending bytes conservatively
count until safe purge, including retained successfully deleted tombstones. No
game-count/external-cover limit. This is NOT a global organization/project cost
cap. Before enabling, approve the provider plan/storage/egress budget, verify
current limits/spend controls, and monitor storage, API/provider egress, rejected
uploads and oldest cleanup age. Proxying means provider-to-API AND API-to-browser
bandwidth matters; no CDN cache-saving assumption or unapproved paid overages.

Design approval and verification gates

All implementation verification is future work: Core/real-PostgreSQL tests for
two-user isolation, constraints, CAS/keep/explicit intent, concurrent quotas/claim/
expiry/retirement, unknown COMMIT after successful commit, receipt survival after
replacement/deletion/purge, >100-row cleanup fairness, namespace mismatch and atomic
Steam fill races. Add byte-proxy auth/errors/no-store/no-304 tests, Blob disposal/
logout/source-generation tests, real image/animation/metadata fixtures, frontend
build/lint/bundle and desktop/mobile E2E. Ordinary suites fake only Storage at the
provider boundary and use real decoding/lifecycle/PostgreSQL, not remote credentials
or a fake content route bypassing the authenticated production route.

Resource provisioning, isolated real Storage permission/upload/read/delete checks,
actual amd64 native/ABI/codec smoke (section 30), real-device camera and accessible
crop review remain release gates, not results claimed by this amendment. Stop for
design review if the chosen decoder/runtime cannot satisfy them; no silent
ImageSharp/license, Alpine, public/signed-link or new-infrastructure substitution.

22. Search, Filtering and Sorting

The backend performs authoritative library search/filter/sort according
to the Domain Specification.

Prefer EF Core/PostgreSQL queries.

Do not load the complete library into Angular merely to implement domain
querying.

Do not introduce Elasticsearch/OpenSearch.

23. Genre Catalog

The approved Genre catalog is deterministic application-owned reference
data.

All environments must expose the same approved catalog.

Users cannot mutate it.

The exact simple representation is selected during persistence/feature
design.

Do not create genre-management endpoints.

24. Random Picker Architecture

Random Picker domain behavior is authoritative on the backend.

The backend handles:

Candidate eligibility.

Mode-specific filters.

Filter semantics.

Exclusion of previously shown IDs.

Random selection.

The frontend handles:

Current picker mode.

Current filters.

Current displayed result.

Temporary list of already-shown LibraryEntry IDs.

Visible temporary result history, newest first.

For a random request, Angular sends the current filters and
already-shown IDs to the API.

Angular does not send the visible result history collection to the API. The
backend continues to require only `shownLibraryEntryIds` for exclusion.

No RandomPickerSession is stored in PostgreSQL.

Conceptual result states

The Random Picker API must distinguish at least:

SUCCESS
NO_CANDIDATES
ALL_ALREADY_SHOWN

NO_CANDIDATES means the current filters match no eligible games.

ALL_ALREADY_SHOWN means eligible games match the current filters, but
every matching candidate has already appeared in the current temporary
session.

The exact DTO and endpoint contract belong to the Random Picker feature
specification.

25. Testing Strategy

Testing should protect important behavior without maximizing test
volume.

Backend unit tests

Focus on:

Domain invariants.

AcquisitionStatus transitions.

Platform rules.

BoardGame rules.

Filter semantics.

Random Picker eligibility.

Backend integration tests

Run against real PostgreSQL semantics.

Use:

Local Supabase/PostgreSQL, or

Docker PostgreSQL.

Do not rely on EF Core InMemory as the authoritative integration-test
database.

Important integration coverage includes:

Persistence.

Transactions.

User isolation with at least two users.

Platform deletion protection.

Game/LibraryEntry lifecycle.

Search/filter behavior.

Random Picker result states.

VideoGame player-count persistence/constraints and Completed-progress
normalization.

Volatile Random Picker visible-history synchronization with shown IDs.

Frontend tests

Test meaningful feature behavior and critical UX.

Do not require exhaustive tests for trivial presentation-only
components.

End-to-end tests

Maintain a small high-value set, such as:

Register/login.

Add Owned VideoGame.

Add BoardGame.

Browse/filter library.

Use Random Picker and Another.

Exact test frameworks are selected during project foundation.

26. Local Development

Preferred local stack:

Angular dev server.

ASP.NET Core API.

Local Supabase stack where practical, providing PostgreSQL + Auth and isolated
private Storage for opt-in Feature 013 contract verification.

Supabase CLI may be used to start the local Supabase services through
Docker.

The API connects to local PostgreSQL and validates local Supabase-issued
JWTs.

The project foundation specification must document the exact startup
commands and required environment variables.

27. CORS

During development and separate-origin deployments, the API must
explicitly allow the configured Angular origin.

Do not use unrestricted production CORS policies.

JWT bearer authentication avoids cookie SameSite coupling between
frontend and API origins.

28. Configuration and Secrets

Secrets are never committed to source control.

Frontend configuration may contain only values intended to be public,
such as the Supabase project URL and publishable/anon key as appropriate
for Supabase client authentication.

Privileged database credentials, service-role credentials and
server-only secrets remain backend/deployment secrets. External provider
credentials, including cover-image search API keys or subscription tokens, are
also server-only secrets.

Steam Web API keys are external provider credentials and are also server-only
secrets. Steam passwords, Steam session cookies and Steam access tokens are not
collected or stored by the application.

29. Steam Integration

An approved feature may provide optional Steam library import assistance through
the ASP.NET Core API. This integration follows the normal application boundary:

Angular PWA -> Game Library API -> Steam Web API.

Steam OpenID browser redirects are allowed only to verify that the user controls a
SteamID64 during explicit link flow. Steam OpenID does not replace Supabase Auth;
all application endpoints still require normal Supabase JWT bearer authentication
except the dedicated Steam redirect/callback endpoints protected by one-time link
state.

Rules:

Steam Web API calls are made only by the ASP.NET Core API.

Angular must not call Steam Web API endpoints directly and must not receive Steam
Web API keys or publisher keys.

Use normal `HttpClient` / `IHttpClientFactory` conventions, server-side
configuration, short timeouts and predictable ProblemDetails errors for Steam
unavailability.

Missing Steam provider configuration must not prevent the rest of the application
from starting or using manual game entry.

Steam integration may add EF Core migrations for a linked Steam account, one-time
link state, and per-Library Steam AppID duplicate detection. EF Core migrations
remain the only application schema authority.

Steam import is explicitly user-triggered. Do not add background jobs, scheduled
sync, webhooks, long-running sync workers, Redis, queues or distributed locks.

Automated tests must use deterministic provider substitution and must not call
real Steam OpenID or Steam Web API services.

30. Deployment

The MVP assumes managed Supabase hosting is acceptable.

Runtime components:

Static Angular PWA hosting.

One ASP.NET Core API.

Supabase managed Auth + PostgreSQL + private Storage for Feature 013.

The frontend and API may be hosted on separate origins.

HTTPS is required in production.

Exact hosting providers for Angular/API are deferred to deployment
decisions.

Feature 013 fixes the API runtime target even though the hosting provider remains
deferred: conventional non-chiseled Ubuntu 24.04 (Noble), glibc, Linux amd64,
RID linux-x64, .NET 10. Build with mcr.microsoft.com/dotnet/sdk:10.0-noble and run
with mcr.microsoft.com/dotnet/aspnet:10.0-noble, explicitly selecting linux/amd64
for BOTH stages. Record resolved image digests during implementation for repeatable
verification. Publish framework-dependent, run non-root, and do not use Native
AOT, trimming or single-file publishing. No Alpine/musl, chiseled/distroless or
ARM64 target is approved by this native-asset decision.

Pin SkiaSharp 4.154.0 plus SkiaSharp.NativeAssets.Linux.NoDependencies 4.154.0 in
GameLibrary.Api, not Core. Additionally pin official libjpeg-turbo 3.2.1 artifacts
and extracted binary hashes, distribute the IJG/BSD notices, and copy only the
required runtime library, not development tools, into the final runtime. A narrow
Cdecl public-API binding must not access private Skia/libjpeg structures. Native
handles and bounded output buffers are scoped/disposed before Skia allocates its
working raster. Missing/tampered/incompatible assets fail unavailable, never fall
back to permissive decoding or arbitrary system libraries. Native bugs cannot be
made impossible by a managed catch: bounds, current patches and executable
adversarial/resource checks are mandatory. JPEG has no integrity checksum, so
structurally valid edits or valid lower-precision progressive streams cannot prove
the original file's history. net10.0/package/peer metadata is not native deployment
proof. Approval is conditional on checking the actual libSkiaSharp ABI and ldd
dependency resolution and running real JPEG/PNG/static WebP decode/encode,
orientation/color/metadata and animation-rejection smoke tests as non-root in the
REAL linux/amd64 target image. A macOS/ARM host run or package existence is not
that verification. Record target, digests and actual outcomes during implementation.
The focused G01 implementation report records verification under the approved
bounded-normalization policy; native load success alone does not pass G01. Resource
provisioning and other unexecuted release gates remain pending.

No Kubernetes or distributed platform is required.

If Supabase managed hosting is later rejected, PostgreSQL/Auth hosting
can be reconsidered without changing the approved domain model.

31. Explicitly Rejected for MVP

Do not introduce without an approved architecture change:

Microservices.

CQRS frameworks.

MediatR solely for layering.

Event sourcing.

Message brokers.

Redis.

Distributed cache.

GraphQL.

Elasticsearch/OpenSearch.

Kubernetes.

Generic repositories over EF Core.

Custom Unit of Work abstraction over EF Core.

Additional backend layering projects.

Separate databases per module.

Direct Angular access to application database tables.

RLS as a parallel MVP authorization system.

Full offline synchronization.

Image-storage infrastructure beyond section 21's private single-cover Storage
adapter, bounded normalization and existing-API cleanup. CDN/private-image caches,
signed read delivery, generic media services and additional worker projects remain
rejected; Angular may not access managed Storage directly.

Direct Angular access to external image-search providers or provider credentials.

Direct Angular access to Steam Web API endpoints or Steam Web API credentials.

Using Steam as the Game Library login provider instead of Supabase Auth.

Storing Steam passwords, Steam session cookies or Steam access tokens.

Background Steam library synchronization.

Calling real Steam services from automated tests.

Generic multi-provider search plugin/fallback frameworks.

Persistent Random Picker sessions.

AI/recommendation infrastructure.

ASP.NET Core Identity.

32. SDD and Agent Workflow

Implementation is driven by feature specifications under specs/.

Agents must:

Read approved Product, Domain and Architecture specifications.

Read the active feature specification.

Implement only approved feature scope.

Preserve existing architecture and domain invariants.

Add/update verification required by acceptance criteria.

Report conflicts or ambiguity instead of inventing behavior.

Architecture changes require an explicit architecture-spec update.

33. Initial Feature Delivery Order

Recommended order:

Project foundation and local development.

Authentication.

Platform management.

VideoGame management.

BoardGame management.

Library browse/search/filter/sort.

Random Picker.

PWA polish and MVP end-to-end verification.

Each step receives its own feature specification before implementation.

34. Architecture Approval

This architecture is approved for MVP feature specification and
implementation.

Approved core decisions:

Angular PWA.

ASP.NET Core API remains the application/domain boundary.

Supabase Auth.

Supabase PostgreSQL.

Private Supabase Storage for Feature 013 under sections 21/30, with per-request
JWT + Library-authorized API byte delivery and no signed URLs or persistent cache.

JWT bearer authentication.

Two .NET backend projects.

EF Core + Npgsql.

EF Core migrations as the sole application-schema authority.

Backend-enforced user isolation.

No RLS in the MVP security model.

Volatile Random Picker session in Angular.

Authoritative Random Picker selection in ASP.NET Core.

Real PostgreSQL integration testing.
