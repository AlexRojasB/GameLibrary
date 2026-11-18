# G03 Storage Verification

Date: 2026-11-02. Scope: focused local verification and an **opt-in procedure**.

**G03 remains PARTIAL. Real Supabase permissions and the real adapter contract have
NOT been verified by this task. Do not enable production uploads or infer release
readiness from these tests.** Phase 1/2 reports and approved specifications are
unchanged. G06 whole-API Linux/load work is separate; no `tools/` file was changed.

Subsequent main-agent follow-up: the full 994-case backend and 22-case browser
regression suites pass, and the [G06 harness](g06-operational-verification.md)
executes the actual Storage adapter against a loopback HTTP protocol fixture.
That strengthens local transport/timeout evidence but is still simulated Storage,
not real Supabase permission or live Auth/JWKS evidence. The inventories/counts
below describe this focused G03 subtask; no gate is promoted on that basis.

## Evidence Boundaries

Reviewed `AGENTS.md`, Product v0.7, Domain v0.9, Architecture v0.4, Feature 013,
Phase 1/2 reports, `SupabaseCoverStorage`, the existing Storage/HTTP/transport/fake/
lifecycle tests and factories, and frontend cover/auth test evidence.

| Boundary | Existing deterministic evidence | What it does not prove |
| --- | --- | --- |
| Server credential scoping | `CoverStorageTests.Uses_only_server_authentication_in_headers_for_all_operations`: modern secret uses `apikey`; legacy service-role also uses Bearer; exact generated key, immutable POST, JPEG type, no query credentials/sign endpoint. Invalid configuration, browser keys/JWTs, default client auth and namespace/key mismatch fail before dispatch. | A made-up secret/JWT accepted by a handler is not a valid provider credential. These are adapter request assertions, not provider permission tests. |
| Bucket settings | `Misconfigured_bucket_blocks_write_proxy_and_cleanup`, `Bucket_is_rechecked_not_cached_between_reads`, bounded/malformed metadata checks. Exact private flag, JPEG-only allowlist and 2097152-byte limit required. | Handler JSON is simulated. Private settings alone do not prove effective denial under broad Storage policies. |
| Redirect/retry safety | `Redirect_responses_are_rejected_without_replay` checks 301/302/307/308 and production handler's redirect/cookie/decompression switches. Write conflicts, network failures and wrong acknowledgements are not replayed. | No real gateway, certificate, redirected receiver or hosted Storage was exercised. |
| JWT/two-user API authorization | `CoverHttpEndpointTests.Invalid_identity_is_401_before_body_or_provider_access`, foreign/missing 404s, foreign claim rejection, query/cookie JWT rejection; actual JWT middleware and Core/SQL stay active. `JwtValidationConfigurationTests` inspects production discovery/audience/algorithm settings. | `CoverHttpTestFactory` replaces discovery/keys with deterministic HS256 test validation. No Supabase-issued JWT, live issuer/JWKS discovery, signing-key rotation or provider Auth session was tested. Configuration inspection is not real-token evidence. |
| Proxy/privacy/state | Existing owner Ready/Attached GET/HEAD, conditionals/ranges ignored, no validators/304, privacy headers on errors/challenges, bounded/corrupt fake responses, CORS, expiry, broken current reference, disabled mode and provider outage. Metadata projections return IDs without provider I/O. | TestServer is not a deployed reverse proxy/CDN. Existing content tests do not exercise the real Storage HTTP adapter end-to-end. |
| Leaks/capabilities | `Provider_details_and_credentials_are_not_in_exceptions_or_operator_alerts`, safe HTTP provider errors, exact options/status contracts, no signing adapter requests. | This does not inspect deployment logs, browser bundles, telemetry or gateway logging with real secrets. Fake byte-store tests have no server credential to leak. |
| Fake/provider fencing | `CoverFakeConfigurationTests.Guarded_fake_mode_uses_singleton_storage_real_decoder_SQL_and_authenticated_proxy_without_fallback`: outbound-network trap; guarded Development/Test composition; unsafe environment/auth flag startup rejection; reset denies real/foreign namespaces. | This establishes fake-mode fencing, not real browser-role Storage denial. It is not permission evidence for `anon` or `authenticated`. |
| Browser API-only/foreign images | `cover-uploads.service.spec.ts`, `cover-image.spec.ts` (external images make no HttpClient request, managed Bearer/Blob behavior, revocation/account races), `core/auth/token.interceptor.spec.ts`; `e2e/covers.spec.ts` asserts Bearer content requests without URL queries. | Angular tests mock HTTP/Auth; E2E uses fake Storage and test JWTs. Source assertions are not an exhaustive real browser network/security audit. These frontend suites were inspected, not rerun here. |
| Cleanup/recovery | Real-PostgreSQL `CoverLifecycleTests`: fixed retention/rescheduling/fairness, reference protection, namespace mismatch, late/unknown writes, successful COMMIT then lost acknowledgement and retained receipts. HTTP tests cover restart/retry/purge, deletion/Play Log commit ordering and receipt recovery after replacement/removal/deletion. Transport tests exercise startup/timer behavior. | Failure hooks, clocks, byte-store and commit-ack faults are simulated at their boundaries. Real provider missing-object/delete response semantics and infrastructure outages remain unverified. |

Ordinary suites use real PostgreSQL, not EF InMemory, and fake only provider/clock
or the explicitly identified transport/commit fault. Storage framing fixtures in
`CoverStorageTests` and Core lifecycle tests are not full decoded images; HTTP
uploads run the real image processor. No test fixture or existing assertion changed.

## New Local Gap

The existing invalid-identity matrix targets otherwise unknown IDs; the foreign
HTTP matrix targets Attached objects. Successful owner GET/HEAD and retirement
tests exist, but not the complete HTTP sequence of successful read followed by a
conditional GET/HEAD after each committed retirement while bytes still exist.

`tests/backend/GameLibrary.IntegrationTests/CoverSecurityGateTests.cs` adds:

| Regression | Cases | Assertions |
| --- | ---: | --- |
| Successful Attached read, then absent/expired/forged identity on same client/URL | 6 | GET/HEAD returns 401 despite conditional/range headers; no provider calls; no JPEG; privacy/no Location/no validators; restoring valid identity requires a new read. |
| Successful read, then replacement/removal/Game deletion committed | 6 | GET/HEAD returns 404 while old fake bytes remain; fresh SQL shows irreversible retirement/receipt/no reference; no provider read/delete; replacement returns distinct new bytes. |
| Two real SQL Libraries with distinct image bytes, Ready or Attached | 4 | Each JWT obtains only its own canonical bytes; foreign conditional GET/HEAD returns 404, with no provider I/O or byte crossover and private headers. |

These 16 cases use existing `CoverHttpDatabaseFixture`, `CoverHttpTestFactory`,
`CoverHttpStorage`, `CoverLifecycleClock` and `TestTokens`. No mocked domain or new
production switch/endpoint/dependency is introduced. The expired-token case uses
a separately issued already-expired test JWT for the same subject; it does not
wait for a real Auth token to expire. Conditional headers are adversarial requests,
not actual cached responses or evidence of cache eviction in a deployed browser.

## Local Execution

Executed on the host with the existing `game-library-postgres-test` container,
PostgreSQL port **5433** (not the unrelated port-5432 database). Read Docker inspect
metadata inside a child-process launcher, assert `5432/tcp` maps to host 5433,
extract the container's `POSTGRES_USER`/`POSTGRES_PASSWORD` in memory, safely quote
them into `ConnectionStrings__Test`, then supply that value only in the test
process environment. Never print inspect output, password, connection string or
expanded environment. Captured runner output was password-redacted before display.
No real Supabase credential was read or requested.

```sh
# Commands below assume that private, local-only environment injection is in place.
dotnet test tests/backend/GameLibrary.IntegrationTests/GameLibrary.IntegrationTests.csproj --no-restore --filter 'FullyQualifiedName~CoverSecurityGateTests' --verbosity minimal
dotnet test tests/backend/GameLibrary.IntegrationTests/GameLibrary.IntegrationTests.csproj --no-build --no-restore --filter 'FullyQualifiedName~CoverSecurityGateTests|FullyQualifiedName~CoverStorageTests|FullyQualifiedName~CoverHttpEndpointTests|FullyQualifiedName~CoverFakeConfigurationTests|FullyQualifiedName~CoverLifecycleTests|FullyQualifiedName~CoverHttpTransportTests' --verbosity minimal
```

| Fresh check | Result |
| --- | --- |
| New class, including build of Core/Api/IntegrationTests | 16 passed, 0 failed/skipped; build successful, no reported warnings/errors |
| Focused existing plus new suites, second command | 320 passed, 0 failed/skipped (86 seconds) |
| Whitespace on both new files (`git diff --no-index --check /dev/null <file>`) | Passed |

The 320 includes the same 16 new cases and 304 existing cases. Do not add the two
run totals or present 320 as new coverage. Each class fixture creates/migrates its
own generated disposable database and drops only that database. The new class
resets only that generated database. No source database reset, schema change or
new migration. This is not a fresh full-backend/frontend/G07 or Linux G06 run.

## Real-Provider Procedure

**NOT EXECUTED.** This is a future operator protocol, not an automatic test wired
into normal CI, and not permission to provision resources or alter access policy.
No account queries, remote requests, resources, users, policies or deployment
settings were changed in this pass. Documentation fetches were public docs only.

### Opt-In Preconditions

1. Obtain explicit owner approval for a named disposable local Supabase stack or
   isolated staging project, API instance and database. Record environment identity,
   operator, UTC run window, source revision and provider/CLI/API/runtime versions.
   Never use the production project, credentials, bucket, customer accounts or DB.
   A Docker PostgreSQL-only container is NOT a Supabase Storage/Auth stack.
2. Require an already provisioned dedicated private `game-covers` bucket and two
   existing real Auth test users A/B. If absent, stop and request separate setup
   authorization; this procedure does not create users/projects/buckets or policies.
   Keep all production uploads disabled. Isolated enablement is an explicit
   process/deployment opt-in, never a committed `appsettings.json` change.
3. Validate isolation before sending any credential or mutation. The real API must
   use that environment's primary database with the existing EF migrations applied
   by the approved EF workflow, and that same environment's Auth discovery endpoint
   and Storage base. Do not reuse the repository's default hosted Auth discovery.
   Local HTTP is allowed only with Development/Test, literal `127.0.0.1`/`[::1]` and
   explicit `CoverUploads__AllowLocalHttp=true`. Staging uses HTTPS with certificate
   verification; no `-k`, permissive TLS, proxy-to-production or hosts-file trick.
4. Set `E2E__Auth__Enabled=false` and `E2E__CoverUploads__Enabled=false`; confirm real
   `SupabaseCoverStorage` composition. Supply server-only `ConnectionStrings__Default`,
   `CoverUploads__StorageBaseUrl` (including `/storage/v1/`), `StorageCredential`,
   bucket and isolated enabled flag through private process secrets. Configure
   `Authentication__Schemes__Bearer__MetadataAddress` and audience for this stack;
   normal host permits ES256/RS256, not the fixtures' HS256. If local Auth is HS256
   only or discovery/TLS is incompatible, stop: do not enable E2E auth, weaken
   algorithms, mint a token or substitute test validation to claim real evidence.
5. Use a separately held publishable key (or legacy anon key if that is the
   environment's supported public key). Authenticate A and B through normal Auth
   sign-in. Keep the two returned access JWTs in separate memory/session contexts;
   verify identity with Auth's user endpoint, distinct `sub`, expected issuer,
   audience, `role=authenticated`, signing algorithm and expiry. Never edit/sign
   claims. A public API key is not a user JWT; never send `sb_publishable_*` or
   `sb_secret_*` as a Bearer JWT. Include legacy anon-role tests if available;
   unavailable legacy credentials are N/A with rationale, not a simulated pass.
6. Disable shell tracing, HTTP verbose/trace output, header/body logging and HAR
   persistence. Supply credentials through private environment/secret store and
   in-memory HTTP clients, not command-line literals, URLs, screenshots or reports.
   Keep public-key clients and privileged server clients separate; no inherited
   service-role/default Authorization on negative probes. Never put the server
   secret in a browser. Record key type or a private fingerprint, never key/JWT
   values or refresh tokens. Do not ask anyone to paste secrets into chat.
7. Inspect existing security/health advisories, logs and effective Storage policies
   read-only in the approved environment, including broad `PUBLIC`/`anon`/
   `authenticated` and operation-specific policies. Capture sanitized policy/grant
   evidence. Do not follow suggested grant/policy fixes automatically. Stop on
   misconfiguration; no global policy edit, Storage SQL/EF mutation or public-bucket
   toggle is part of verification. A diagnostic `SET ROLE`/policy tester is not a
   replacement for real HTTP role probes.

### Request And Evidence Rules

For each row below record case ID, principal A/B/no-JWT, credential type only,
method/route template, UTC time, status, sanitized provider error code, response
byte count/hash where appropriate, and expected/observed outcome. Keep exact
project/Library/object locators in a restricted run manifest, not a public report.
Never retain raw Auth responses, signed capabilities, headers or private images.

Use an HTTP client with redirects and automatic retries disabled. Check provider
JSON error codes as well as HTTP status (some Storage errors use 400). Network
failure, 429, 5xx, invalid/expired probe JWT or unknown route is **inconclusive**, not
permission denial. A valid auth control must succeed near each negative batch.
Do not equate an HTTP 2xx empty list with list permission: verify no fixture IDs,
prefixes or object metadata appear, and inspect effective policy/grant evidence.
Batch sign/delete may also return 2xx with per-item denials/empty results; inspect
every item and verify no capability or mutation. Log only the verdict, not a URL.

### Canonical API And Adapter Control

1. Privileged backend/operator client reads `GET {storageBase}/bucket/game-covers`:
   require matching `id` and `name`, `public=false`, `file_size_limit=2097152`, and
   exactly `allowed_mime_types=["image/jpeg"]`. Do not repair it in this run. Confirm
   browser roles cannot update these settings. Settings are necessary, not a proof
   of permission isolation.
2. With A, call API `GET /cover-uploads/options` (authenticated, public limits only).
   Upload a synthetic non-sensitive 160x240 PNG via one multipart `file`; use a
   misleading filename/MIME to prove labels do not control output. Require 201,
   exact Ready/status fields, 24-hour deadline and API-relative metadata Location,
   never provider URL. Repeat with B using visibly different colors. No Game exists
   yet. These two objects E_A/E_B are read/list canaries, not direct-mutation targets.
3. Operator inspects the corresponding EF-owned rows read-only: distinct owned
   Libraries, immutable namespace and `<libraryId:N>/<uploadId:N>/cover.jpg`, positive
   byte size <=2 MiB. Read exact objects using the privileged Storage API; validate
   JPEG decoding, 2:3 integer dimensions within 160x240..960x1440, no original PNG/
   trailing test marker or EXIF/GPS, different bytes from input and canonical byte
   count/hash matching API content. No original bytes may reach Storage. This real
   route exercise must use the unchanged production adapter, not a Storage SDK
   substitute or manually seeded lifecycle row.
4. Observe sanitized server outbound calls or use an isolated adapter contract
   runner with the same implementation: verify bucket GET, immutable object POST
   (`x-upsert:false`, `image/jpeg`), authenticated object GET and object DELETE.
   Modern secret uses `apikey` only; legacy service-role uses `apikey` plus Bearer
   for that separate credential variant. User JWTs never go upstream. Repeat with
   each credential format intended for deployment; an untested format stays pending.
   Acknowledgement must have the expected `Key`; real response-shape incompatibility
   blocks the gate, even if a handcrafted fake response passed.
5. Create only additional **unclaimed disposable API canaries** for attempted
   overwrite/delete/sign tests. Record each locator/hash before a probe and use a
   unique new destination key for insert/upsert-new. Never target an Attached or
   shared object. Respect 5/minute, 20/hour and ten-live-unclaimed limits; split
   batches and use normal cancel/cleanup rather than disabling limits.

### Direct Storage Denial Matrix

Let `S` be the approved Storage base including `/storage/v1`, `b=game-covers`,
`k` a known existing canary key, `new` an unused manifest-tracked key, and `prefix`
the exact known parent prefix. Use valid small canonical JPEG bytes, not a rejected
MIME/size/invalid body that could mask an authorization error. Probe BOTH A's and
B's objects; even an object's API owner has no direct Storage entitlement.

Run every applicable row in separate contexts: **no key/no JWT**, **public key
only/no Authorization**, **legacy anon key with anon Bearer if supported**, **public
key + A Auth JWT**, and **public key + B Auth JWT**. For public URL guesses include
truly unauthenticated requests. Do not rely on CORS: use non-browser HTTP too.

| Operation | Requests | Required result |
| --- | --- | --- |
| List | `POST S/object/list/b` with `{"prefix":"<known prefix>","limit":100,"offset":0}`; also root and the other Library prefix | No canary/name/path/metadata disclosure. Empty authorized-looking results require policy review; no positive list grant for this bucket is acceptable. |
| Read | `GET` and `HEAD S/object/authenticated/b/k`; also `S/object/b/k` | No image bytes, successful existence/size metadata, redirect or capability. Test live known objects, not just unknown keys. |
| Public-route guesses | `GET/HEAD S/object/public/b/k`, `GET S/object/info/public/b/k`; `GET/HEAD S/object/info/b/k` where supported | No bytes/metadata even with exact keys and query `download` variants. Private settings must not expose a public delivery route. Unsupported route alone is not proof of RLS denial. |
| Insert | `POST S/object/b/new`, canonical JPEG, `x-upsert:false` | Denied; privileged follow-up confirms no object at destination. |
| Upsert new/existing | Same POST with `x-upsert:true`, separately for `new` and a disposable existing canary | No creation/overwrite; privileged control confirms original size/hash unchanged. A duplicate-key conflict alone is not permission evidence. |
| Update | `PUT S/object/b/k`, different valid canonical JPEG | Denied; original hash unchanged. Tests UPDATE separately from INSERT. |
| Delete | `DELETE S/object/b/k`; batch `DELETE S/object/b` with `{"prefixes":["<canary key>"]}` | No removed object, including hidden 2xx no-op results; privileged exact-key read/hash still succeeds. Never empty/delete the bucket. |
| Sign read | `POST S/object/sign/b/k` with `{"expiresIn":60}`; batch `POST S/object/sign/b` with paths and expiresIn | No signedURL/signedUrl/token or successful per-item capability. Do not generate a privileged signed URL as a positive control. |
| Sign upload | Current supported `POST S/object/upload/sign/b/new` and upsert variants (`createSignedUploadUrl` equivalent) | Denied, no token/path capability permitting upload, including upsert authorization. Verify exact route/body against installed provider version before the run. |
| Guessed signed delivery | `GET/HEAD S/object/sign/b/k` without token, then with a non-capability placeholder token | No bytes. This checks no unsigned bypass, not signed-token revocation. Never insert a real JWT into a URL. |

Also check bucket metadata/list responses for unexpected useful object information
and any already enabled move/copy/alternate upload or image-transform routes against
the installed provider API. Do not enable paid transforms/TUS/S3 infrastructure to
test them; mark unsupported/disabled surfaces explicitly. Unexpected access to any
canary or capability is a **gate failure**: stop further probes, keep uploads off,
retain a sanitized incident/locator manifest and seek operator review. Do not fetch
or publish an unexpectedly returned signed URL or silently delete evidence.

### Real API Authorization And Delivery

1. With both E_A/E_B still Ready, owner content GET must return only its own JPEG.
   Owner metadata succeeds; B accessing A (and A accessing B) content/status/cancel/
   claim returns generic 404, indistinguishable from random absent IDs. No-JWT calls
   to every cover route return 401 before upload/body/provider use. An API call
   with only a publishable key or legacy anon JWT is not a signed-in user operation.
2. Create normal valid BoardGame/VideoGame records via API using each owned Ready
   handle (Owned VideoGame includes its owned Platform). Expect atomic Attached
   state/receipt and exclusive managed ID/null external URL in management, Library,
   Picker and Play Log projections. Foreign claims/cover mutations remain denied.
   Metadata requests must not read Storage. An Attached cancellation is 409.
3. For Ready and Attached content, run owner/foreign/no-JWT GET and HEAD with
   `If-None-Match:*`, future `If-Modified-Since`, `Range:bytes=0-1` and If-Range.
   Owner gets full 200 JPEG (HEAD empty body with canonical Content-Length), not
   206/304. Every response/error/challenge has `Cache-Control: private, no-store`,
   `Vary: Authorization, Origin`, nosniff, no ETag/Last-Modified/Content-Range and no
   Location/Storage link. Success uses fixed inline `cover.jpg`. Check at the API
   origin AND actual isolated ingress; an ingress-generated cached/challenge page
   lacking these rules fails deployed evidence. Repeat after previous owner success.
4. Test a genuinely expired A access JWT without editing it or refreshing it in that
   client. Wait beyond `exp` plus configured validator clock skew (the local fixture
   uses zero; production defaults may differ). Send it in the Bearer header to a
   formerly readable object: 401 for GET/HEAD and no cached JPEG. Refresh/re-login
   through Auth for positive controls. Logout/account switch must discard browser
   Blob URLs; logout is NOT a guarantee that a still-live captured JWT is revoked.
5. Stage a separate real Ready object for the 24-hour expiry check; read before its
   recorded deadline and after it using a currently valid JWT. Expect 404 content
   and 409 claim, regardless of cleanup having run. Use actual elapsed server time
   for real evidence. A manual clock or edited lifecycle timestamp is only simulated
   evidence and must be labelled as such, not substituted for this row.
6. Replace an Attached cover with a new upload using the exact expected references,
   then remove another and delete another Game through normal API routes. Each old
   content ID immediately returns 404 on GET/HEAD, including conditionals, before
   physical deletion. Keep receipts; replacement serves only new bytes. Refetch
   metadata to verify unchanged unrelated fields and correct Game/LibraryEntry/
   Play Log deletion behavior. Never overwrite an existing object key.
7. In a real browser session inspect only sanitized request destinations/verdicts:
   managed images use API Bearer -> Blob -> local object URL; no `/storage/v1`
   requests/sign links/query tokens; external images remain direct without Bearer.
   Account change/logout/source change must revoke local URLs and ignore stale
   responses. Inspect service-worker Cache Storage/IndexedDB/local/session storage
   without exporting secrets: no managed image/response persistence. Test reload/
   fresh request after retirement/expiry. Already downloaded copies cannot be recalled.

### Cleanup, Recovery And Failure Controls

1. Cancel unclaimed mutation canaries by API (202 means retirement, not erasure),
   allow ten-minute abandoned grace, and observe the production cleanup host at
   startup/15-minute intervals. For retired Game covers, observe committed SQL
   retirement/no active reference before any Storage delete. Record supported API
   exact-key absence, not just an empty listing or a public-route 404.
2. Confirm real delete success and repeated missing-object behavior in the same
   recorded namespace. The adapter accepts 200/204 or a 404 JSON `code=NoSuchKey`
   only after successful bucket verification. A missing bucket/route, 403, provider
   error body or wrong response shape must retain work, not count as deletion.
   A real provider compatibility failure is reported; do not change adapter/policies
   or reinterpret missing in a different namespace to get a pass.
3. Within the isolated environment only, use an owner-approved **local network fault
   around the API's outbound Storage connection** to fail deletion/read and recover
   it. Do not alter real bucket privacy, provider policy, key validity or destination.
   Observe safe 503 content with unchanged metadata; committed Game removal succeeds
   while DeletePending persists, fixed retired_at/receipt and cleanup_after >=15
   minutes after failed attempt. Restart the same API with the same DB/namespace,
   restore connectivity and observe successful retry without deleting another live
   referenced canary. If fault injection is unavailable, mark this row pending.
4. After real 24-hour retention and confirmed delete/missing, observe safe metadata
   purge, status/content 404 and continued readability of an unrelated Attached
   object. Do not shorten clocks/retention or delete rows to accelerate the gate.
   Retained successful tombstones still count toward quota until purge.
5. Optionally drop only the API client response after a real Game Create commits
   using an isolated local client/network interceptor. Reconcile owner status with
   the SAME handle, find its first claimed Game, retire/remove/delete through API
   and recheck receipt `gameId`/`gameExists`; a repeated Create must conflict. Label
   response loss as injected, not an actual unknown PostgreSQL COMMIT. Existing
   deterministic commit-fault tests remain the SQL acknowledgement-fault evidence.
6. Preserve locators on interrupted uploads and check eventual retirement/retry;
   do not delete a possibly committed attachment or blindly repeat Create after
   5xx/cancellation. Purged/unavailable receipts require Library inspection, not a
   conclusion that Create failed. Arbitrarily late provider writes after bounded
   retention remain an operator reconciliation risk, not a zero-orphan guarantee.
7. Reconcile every manifest key after the run against authoritative primary state
   and exact recorded namespace. Use normal game removal/cancel/cleanup first.
   Unexpected directly created probe objects require separately reviewed supported
   Storage API deletion only after fresh no-reference/ownership checks. No prefix/
   age sweep, bucket empty/delete, SQL deletion from `storage.*`, account deletion
   or removal of lifecycle locators before confirmed physical cleanup.

### Data API Denial And Effective Grants

Read-only operator review must verify `public.cover_uploads` is not browser-exposed:
effective privileges for `anon`, `authenticated` and inherited `PUBLIC`/role grants,
column grants, exposed schemas, relevant views/RPCs and migration-creator default
privileges. Inspect catalog/Studio evidence, without modifying schema/policies or
granting browser access. Absence of role names in a plain PostgreSQL fixture does
not prove deployed Supabase permissions. Do not add app-table RLS as a parallel
ownership mechanism; this architecture requires no direct browser access.

For each public/no-JWT/anon/A/B context use `/rest/v1/cover_uploads?select=id` and
known own/foreign `id` filters (GET/HEAD). No rows, counts, object keys, namespace,
receipt or owner metadata may be accessible. A disabled Data API or explicit
permission/schema-exposure denial is valid when configuration/catalog evidence
confirms the table exists but is intentionally inaccessible. A generic wrong URL,
stale schema, invalid JWT, or `200 []` alone is not proof of **no table grants**.

If the Data API is enabled, opt-in negative DML probes must use only isolated,
unclaimed, manifest-recorded canaries with no Game reference: a no-op PATCH of its
existing byte_size, DELETE, and a structurally valid INSERT/upsert attempt with a
new recorded UUID/locator and valid existing test Library. Require access denial,
not a validation/CHECK/FK/duplicate error masking a grant; compare authoritative
state before/after. Do not target live covers. An unexpected success is a failure
even if a no-op; stop and reconcile any lost/new locator using the manifest before
normal cleanup. No SQL DML, policy/grant repair, migrations, auth/storage schema
edits, or additional Supabase migration workflow belongs to this procedure.

### Closeout Record

For each matrix row assign **PASS / FAIL / INCONCLUSIVE / NOT RUN / N/A**, attaching
sanitized observations and supported-version/route references. Keep fake, injected
and real-provider evidence in separate columns. Record resource cleanup and any
retained pending object/receipt explicitly; an interrupted run is not a pass.
Repeat against the actual isolated staging deployment before asserting hosted
ingress/JWKS/permission behavior. A local Supabase pass is local-provider evidence,
not proof of another project's policies or production proxy behavior.

G03 can be proposed passed only after real Auth A/B, effective anonymous/browser
denial, real adapter upload/read/delete, expiration/retirement and deployed proxy/
privacy/leak checks all have evidence. Documentation or green fake-backed tests
cannot satisfy missing rows. Other gates, including physical devices and G06, remain
independent and are not waived by this document.

## Current Documentation

Public docs fetched after loading the Supabase skill; no account MCP queries:

- [Changelog index](https://supabase.com/changelog.md).
- [Storage access control](https://supabase.com/docs/guides/storage/security/access-control): default-deny uploads, upsert needs INSERT + SELECT + UPDATE, service credentials bypass RLS. Do not apply the example browser-allow policies to this bucket.
- [Private/public buckets](https://supabase.com/docs/guides/storage/buckets/fundamentals): public delivery bypasses read access controls; private flag alone is not effective permission verification.
- [API keys](https://supabase.com/docs/guides/getting-started/api-keys): modern keys are not JWTs, public key plus user JWT selects authenticated context; legacy keys coexist/deprecate independently.
- [Storage REST reference](https://supabase.com/docs/reference/self-hosting-storage/introduction): route shapes for single/batch read/sign/list/update/delete. Recheck against the actual installed Storage version; reference rendering does not establish live route compatibility.
- [Signed upload reference](https://supabase.com/docs/reference/javascript/file-buckets-createsigneduploadurl): discovered via docs search; full-page retrieval exceeded fetch limits. Exact installed sign-upload contract must be checked before its negative probes, not guessed from a missing-route response.
- [Delete objects](https://supabase.com/docs/guides/storage/management/delete-objects): supported Storage deletion, not SQL metadata deletion.
- [Securing Data API](https://supabase.com/docs/guides/api/securing-your-api) and [2026-04-28 auto-exposure change](https://supabase.com/changelog/45329-breaking-change-tables-not-exposed-to-data-and-graphql-api-automatically): rollout dated 2026-10-30 affects future default grants, not existing object grants. Never infer this project's deny state from the rollout date; audit inherited/effective access.
- [TLS CBC deprecation](https://supabase.com/changelog/cbc-tls-ciphers-deprecated): removal from 2026-11-10 at earliest affects Auth/Storage HTTP APIs; retain supported modern TLS, no downgrade. This task did not execute a TLS compatibility check.
