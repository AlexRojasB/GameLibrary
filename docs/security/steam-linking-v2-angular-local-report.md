# Steam OpenID v2 PR 4 — Angular/browser local report

2026-11-24. Independent branch `feat/steam-linking-v2-angular` from fetched
`origin/dev` **d19d96a1145d770cb9659ea57d462bd7757ef1ff**, merge of API/protocol
PR #12. Verified all five baseline checks in
[run 45427965775](https://github.com/AlexRojasB/GameLibrary/actions/runs/45427965775).
Those are baseline results, not PR 4 hosted/native evidence.

**Local implementation only. Normal Steam v2 linking/UI remains unavailable;
legacy remains retired. No activation, push, merge, deployment or remote migration.
Global legacy retirement and real Steam interoperability are not established.**

## Implementation

Implements approved redesign sections 10/14 PR 4 under
[linking-angular-browser.md](../../specs/011-steam-integration/linking-angular-browser.md),
with bounded authority amendments. No protocol alternative, extra confirmation
token, dependency, Core/schema change, CI workflow/budget change or unrelated feature.

- `SteamService`: fixed v2 create/review/confirm/cancel POST contracts, private JSON
  bodies and `withCredentials` only for those four operations. Status, preview,
  import and unlink retain Bearer-only transport; token interceptor unchanged.
- Feature-local `SteamLinkBrowser`: exact HTTPS Steam host/path allowlist, no
  userinfo/explicit port/fragment/normalization trick; native `location.replace`,
  never authenticated HttpClient navigation. Session storage contains only the
  validated `{transactionId, initiatingUserId, expiresAt}` triple for this tab.
  No S/B/URL/assertion/new JWT copy in storage/router state/analytics/logging.
  Storage failure refuses navigation and guides restart, not cookie bypass.
- Feature-local root `SteamLinkFlow`: single pending operation, generation-bound
  responses, immutable review presentation and explicit expected-ID confirmation.
  No auto-confirm, provider retry, preview/import or transaction discovery. Lost
  confirmation/cancel acknowledgments retain only a live handle and hide consent;
  explicit durable review (or reload) is required before another confirm choice.
  Creation failure requires deliberate new initiation; it never recovers/reuses S/URL.
- `SteamPage`: gated by available **protocol version 2**, not legacy linkAvailable
  alone. After auth initialization it reviews the tab handle, including on reload
  or fixed review/failure return. Only recognized callback flags are removed with
  replaceUrl; flags themselves grant no authority. Full escaped canonical SteamID
  and current Game Library account are displayed with explicit Confirm/Cancel,
  wrong-account guidance and replacement/across-tabs warning. Numeric review does
  not attest human intent. No auto profile/avatar request.
- Local expiry removes handle/identity and prevents new UI confirmation; backend
  PostgreSQL time remains authoritative. Missing/invalid proof or terminal/not-current
  result removes metadata and offers restart. 401 requests reauthentication/fresh
  review; 429/errors/unknown outcomes never enter retry/poll loops. Cancel never
  means unlink. Existing linked-account/import/unlink/catalog behavior remains.
- `AuthService` adds synchronous subject generation/notification and guards stale
  initialization snapshots. Same-subject token refresh retains generation and uses
  the latest JWT; U→W→U/logout invalidates old work even between signal effect runs.
  Root composition instantiates the feature observer away from the Steam route too.
  Initial session restoration does not erase a same-tab live return handle. Observed
  subject changes clear metadata and page status/preview/selection/import/private
  review results; stale responses cannot restore or navigate them. Page destruction
  and BFCache restoration suppress old UI responses without claiming server rollback.
- Repository frontend headers add only CSP `frame-ancestors 'none'` and
  `X-Frame-Options: DENY` for all frontend responses, covering the review route and
  SPA fallback. Existing no-referrer/static caching headers and Git deployment-off
  setting remain. The local E2E server sends the same framing headers. No hosted
  settings changed; actual Vercel/QA response headers remain unverified.
- PWA static-shell/data-cache configuration is unchanged and tested: no dataGroups
  or remote asset URLs cache Steam/private API responses. Existing manifest remains
  standalone. Mobile/desktop use the same feature route and no popup/opener/native
  app bridge. A return in another browsing context without this tab's metadata/proof
  must restart; do not treat installed-PWA/browser context transfer as authentication.

## Isolated browser composition (not runtime activation)

`SteamSecureLinkRules.BuildReady` and shipped readiness are unchanged and **false**.
The normal unavailable UI remains for unavailable/missing/old protocol status.

Playwright explicitly opts into `E2E:SteamLinking:Enabled`, requiring Development/Test,
existing explicit E2E auth + Steam provider switches, literal 127.0.0.1 frontend/API
bases, the local-HTTP exception and a loopback `game_library_e2e` database. Invalid
configuration and Production/Staging throw before protocol I/O. Request admission
additionally requires a loopback peer and literal loopback Host. Existing normal
admission tests still pass; true ordinary runtime flags do not admit shipped v2.
Playwright no longer reuses arbitrary running servers. CI isolation/budgets unchanged.

The browser performs real native navigation to the pinned Steam URL; Playwright
intercepts **only at the provider boundary** before external network dispatch, serves
a controlled approval/decline document and returns realistic signed fields to the
real callback. The E2E client validates a public synthetic HMAC and spends each nonce
once instead of accepting arbitrary assertions. Real parser/proof/JWT/Origin/Core/
PostgreSQL handlers are used. Public synthetic identities permit two-user tests and
independent rate-limit subjects without relaxing the production limit. Existing fake
owned-game responses are retained. No Steam/Auth/Storage identity, hosted DB or user's
Library was accessed. Public Supabase changelog/auth-event documentation was reviewed;
no client SDK change, user_metadata authority or session-revocation claim is added.

## Verification

| Check | Result |
| --- | --- |
| Complete PostgreSQL/API suite | **1289 passed**, final Release suite, zero failures/skips; 5m49s |
| Core | **353 passed**, no failures/skips |
| Angular | **516 passed**, 39 files, no failures/skips |
| QA configuration/header/cache tests | **17 passed** |
| Existing CI guard unit suite | **32 passed** |
| Release solution build | PASS, zero warnings/errors |
| Angular production build | PASS; **692.98 kB** initial bundle, unchanged 700 kB warning / 1 MB error budgets |
| Lint / TypeScript spec noEmit / EF pending-model | PASS |
| Full desktop/mobile E2E, zero retries | **Not green**: first run **47/52**, isolated confirmation **45/52**; see failures below |
| Final focused Steam desktop/mobile E2E | **27/28 passed**, zero retries/skips; account-switch cases passed, one startup timeout remains |

No native resource campaign or hosted PR 4 CI was run. Existing Random Picker SCSS
budget warning and Vite/Vitest notices remain unsuppressed. Local logs/artifacts are
retained only in the approved session temporary directory under `steam-pr4-*`.

### Implementation-stage failures and corrections

- First Angular run: one new expiry-notice test failed because a later no-handle
  guard overwrote the specific expiry guidance. Guard now preserves existing safe
  guidance; no deadline/authorization assertion was weakened.
- First browser run: **2/12 passed**. Nine failures shared a helper error: it sent
  credentials on ordinary status, whose CORS contract intentionally is Bearer-only.
  Helper corrected; no production CORS expansion. Framing used browser console text
  that was not observed; the test now requires real route/fallback headers, an
  unprotected positive frame control and a blocked protected error frame.
- Second browser run: **12/14 passed**. One fixture selected the first sorted game
  but expected Dota 2; it now selects the named game. A real UI mismatch left unlink
  visually enabled while its handler refused during pending durable review. Button
  and handler now agree; a focused Angular regression covers it.
- Targeted follow-up: **2/3 passed**; a preview assertion failed after confirmation.
  Source edits occurred while that dev-server run was active, so hot reload cannot
  be excluded; no definitive flake/root-cause claim is made. Duplicate activation
  now targets the same consent control (not a pointer double-click that can retarget
  a replacement button), and the browser test explicitly awaits the preview response.
  The final complete run is performed with source frozen and no retries. Green
  results are not a claim that historical cropper/native OOM intermittency is solved.
- First full browser run: **47/52 passed**, all **28 new Steam desktop/mobile cases
  passed**. Five existing cover/cropper tests failed in preparation or image export
  at their unchanged expectations. Snapshot evidence shows `Preparing image...`;
  no proven cause is assigned. Builds/other suites were executing concurrently.
  One bounded complete confirmation is run with no concurrent builds/test suites,
  no retry, timeout increase, cover refactor or budget change. The final Steam fixture
  additionally denies incidental external requests (including provider favicons).
- Isolated complete confirmation: **45/52 passed**, zero retries/skips. Three existing
  desktop cover tests still failed in preparation; concurrent build load therefore
  cannot explain away the issue. Four new account-switch cases stopped during login
  (two desktop/two mobile), with a blank email field in the failure snapshot. The
  fixture now waits for completed logout navigation and reloads that login document
  before entering the next identity; it also asserts the entered email and the real
  successful auth response, instead of weakening the account-switch assertions.
  A bounded focused Steam run verifies that fixture correction. No cover source or
  test, expectation timeout, retry policy or resource budget is changed, and no
  further full-suite repetition/cover diagnostic campaign is performed under PR 4.
- Final focused Steam desktop/mobile run after logout synchronization: **27/28
  passed**, zero retries/skips (3m48s). Both account-switch scenarios passed on both
  projects. The desktop cleared/refused-cookie case timed out waiting for the initial
  Link button, before its cookie-clearing/refusal steps. No useful page snapshot or
  proven cause establishes why that startup did not complete. Its earlier passing
  runs do not erase this current failure. This is an additional unresolved E2E
  verification blocker; no assertion, timeout, isolation guard or production gate
  was weakened and no further run was used to hide it.

## Threat mapping and boundaries

- **T06/T19/T23/T30:** full verified ID/account context, fake/old callback hints,
  malformed/malicious destination/storage, same-browser injected valid identity,
  wrong expected-ID rejection, declined immutable review and actual foreign framing
  controls. No auto-confirm; direct authenticated matching expected-ID confirmation
  still needs no extra review token, as specified.
- **T08/T09/T17/T24:** two cookie contexts authenticated as the same U, transferred
  assertion and canonical derived proof fail; sender still stages. Local cookie
  host/HttpOnly/Lax/path/deadline and unchanged proof after completion are checked.
  Cleared proof and deterministic issuance-refusal fail closed. Latest committed
  tab wins; isolated response delivery reverses Set-Cookie order and forces safe
  restart for both tabs. This is not a physical-device/privileged-theft guarantee.
- **T10/T11/T16/T21/T26:** U→W→U generation guards, pending creation/review during
  cross-tab account changes, stale ordinary/private result suppression, latest JWT
  on same-U refresh, fixed empty-query JSON targets, no credentials to Steam, old
  clients/unavailable flags, normal admission regressions and actual Production/
  Staging composition rejection. Previous backend Origin/CSRF/retirement tests remain.
- **T04/T05/T07/T12/T14/T20:** callback replay/immutable stage, UI expiry, provider
  decline vs explicit cancellation, two direct browser confirmations returning one
  current receipt, post-unlink no resurrection and lost committed-confirmation
  recovery via fresh review/reload. Existing real-PostgreSQL DB-time/lock/commit-fault
  and no-Angular authority tests remain unchanged, not replaced by UI assertions.
- **T15/T22/T29:** safe constant UI errors and minimal storage; no new telemetry,
  capture, automatic retries or polling. Existing backchannel/privacy/rate/resource
  tests remain; this task does not audit all provider/ingress/extension stores.

## Remaining gates / publication readiness

**NOT ready to publish as a fully validated PR:** complete E2E regression remains
red in the existing cover preparation cases; their relationship to the shared
auth/header changes has not been disproved. The latest focused Steam run also has
one unresolved startup timeout. Earlier green Steam cases are not a replacement
for current passing acceptance evidence or a green complete suite. Local implementation is committed for owner
review with this verification blocker explicit, not marked acceptance-complete.
No publication is authorized by this task. Five required hosted CI checks and target Noble/linux-amd64
execution must run for this PR's exact published SHA after separate authorization.
The baseline's green native result is not this branch's evidence. Native 768/640
policy remains unchanged; API-only 512 MiB/G06 and intermittent OOM are not closed.

Real Steam interoperability, hosted HTTPS/cross-site cookie behavior (including
actual third-party policy blocking), Firefox/WebKit/Safari and physical installed-PWA
return/OS handoff verification remain separately authorized release evidence. Local
Chromium desktop/mobile and simulated issuance refusal do not certify those layers.
Repository/local framing headers are not proof of actual hosted response headers.

PR 5 complete pause/drain/invalidation, mixed-replica/schema/runtime inventory,
hosted exposure/log/collection assessment and explicit owner activation approval
remain pending. A stolen valid JWT can initiate its own flow; same-browser injected
identity can be mistakenly confirmed; UI cannot attest intent or revoke admitted
work. No real Steam, MFA, global revocation, all-store privacy, global legacy
retirement or production activation readiness is claimed. Stop after local commit.
