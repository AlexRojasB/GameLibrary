# Steam OpenID secure linking redesign

## 1. Status, authority and scope

2026-11-24 PR 5 authorization: after merged PR #13, local final review,
necessary corrections/tests, operational/QA procedures and commit are authorized
from origin/dev 0d96b06cc715d67522f8e0ccb3203845264eca4f. Section 14 PR 5 governs.
Keep shipped readiness false and normal v2 inaccessible while external evidence
is missing; no legacy reactivation, push, deployment, remote migration or CI-budget
change. See linking-final-validation.md and the final-validation report/runbook.
Earlier task-boundary verdicts below are historical, not current activation approval.

2026-11-24 PR 4 authorization: following merged PR #12, the owner authorizes local
Angular/browser integration, tests and commit on an independent branch from
origin/dev d19d96a1145d770cb9659ea57d462bd7757ef1ff. Sections 10 and 14 PR 4 govern;
Option A and all backend invariants remain. Keep normal UI unavailable and shipped
readiness false. No activation, legacy reactivation, CI-budget change, hosted work,
deployment, remote migration, push or merge. See linking-angular-browser.md.

2026-11-24 PR 3 authorization: after merged PR #11 (persistence/Core), the owner
authorized local API/protocol implementation, tests and commit only, on an
independent branch from origin/dev 81ef59dd6b109d280150e8838e154a397c22399e.
Section 14 PR 3 and the approved Option A baseline govern. Shipped readiness stays
false; no Angular confirmation, legacy reactivation, CI-budget modification, push,
merge, deployment or remote migration. See linking-api-protocol.md for active scope.

**Owner-approved v2 baseline; PR 5 local final validation authorized.
Activation and production release NOT approved. Production blocker OPEN.**

Earlier PR 2 authorization, 2026-11-24: following merged legacy retirement (PR #9), the owner
approved the recommended Option A / D01–D11 baseline and PR 2 only: additive EF
state and private Core proof/lifecycle, with real disposable PostgreSQL tests.
No new public handlers, Angular, runtime activation, deployment, remote migration,
push or merge is authorized. The historical review/approval requests below describe
the original review, not a new choice of protocol. Product/Domain/Architecture and
Feature 011 now record this bounded authorization in linking-persistence-core.md.
Revision: **v2 security design review, 2026-11-21**. Technical recommendations below
are explicit and testable, but are not owner approval or evidence of an implemented
secure protocol. Re-reviewed the complete prior document, current API/Core/Angular,
EF mappings, logging, auth composition and Feature 011 stabilization in this same
worktree. No remote service, user record or real Steam authentication was accessed.
Prepared 2026-11-21 against fetched `origin/dev`, commit
`503766763fed33a839d37f4107dcd8d1b1a3c59c`, in isolated branch
`docs/steam-openid-linking-redesign`. [PR #8](https://github.com/AlexRojasB/GameLibrary/pull/8)
is merged at that commit; its head
`5f8d039e33a6d54cf872792436dc6aa073519b72` is an ancestor. Inspection used repository
source and sanitized prior findings, not live databases, user data or Steam login.

This document specifies a candidate for owner/security approval. It does not
silently supersede the approved [Product](../product/product-specification.md),
[Domain](../domain/domain-specification.md),
[Architecture](../architecture/architecture-specification.md),
[Feature 011](../../specs/011-steam-integration/spec.md) or its
[linking stabilization](../../specs/011-steam-integration/linking-stabilization.md).
In particular, the latter currently prescribes a deterministic cookie, callback
link creation and state-before-Library locking. Those details require explicit
amendment before implementing the replacement below. The extra confirmation and
temporary verified-identity persistence must also be approved; implementers must
not choose between incompatible versions themselves.

Scope: link initiation, browser possession, OpenID verification, owner confirmation,
transaction persistence, retirement of insecure endpoints, and their verification.
Retain Angular, the existing API/Core projects, EF/Npgsql and Supabase JWT validation.
No new login provider, application authentication cookie, infrastructure, dependency,
Steam synchronization or direct Angular database access is proposed. Application
authorization stays in the API/Core, not a newly introduced RLS workflow. Existing
database exposure/grants must not be broadened; verifiers are backend-only data.

**Recommendation:** Option A (authenticated POST returning a trusted Steam URL),
an independent random HttpOnly browser secret, and a two-phase completion:
anonymous callback stages a verified SteamID; an explicit authenticated confirmation
by the initiating user creates the account link. Use a clean, fail-closed v2 cutover.

### Review findings, ordered by severity

These are source/design findings, not newly demonstrated production exploitation.
No critical new signature/authentication bypass was established by this review.

| Finding | Severity | Review result / required resolution |
| --- | --- | --- |
| F01 Existing implementation remains vulnerable | High / release blocker | PR #8 changes logging only. Current start paths and deterministic proof remain accepted; no specification edit fixes deployed behavior. Retire all legacy mutation paths before activating v2. |
| F02 State disclosure can influence the initiating browser | High / guarantee correction | An attacker can obtain a valid assertion for their Steam account with disclosed S, then navigate the original browser to it; its cookie B attaches automatically. B blocks a separate client, not this injection. Immutable staging + explicit identity review limit impact; remove state-changing unsigned provider cancellation. Backend cannot attest an actual human click. |
| F03 Flag is not revocation or a distributed barrier | High / rollout gap | Requests admitted while enabled can still commit after a flag changes. Pause, drain all admitted mutators, reconcile uncertain commits, then invalidate rows before reactivation; keep intermediate builds hard-disabled. |
| F04 Deadline checks missed implicit waits | High / atomicity gap | A unique-index insert can block after the explicit Library/row locks. A pre-write process-clock check alone can authorize an expired write. Specify PostgreSQL clock and final post-write eligibility check, with precise authorization linearization. |
| F05 Browser proof is not MFA or stolen-JWT defense | Medium / trust-boundary correction | JWT alone cannot confirm an existing T without B. But an actor with a valid stolen JWT can initiate a fresh flow and receive their own B. Never claim protection requires joint theft of the original B and JWT. |
| F06 Unlocked primary read does not settle COMMIT | Medium / recovery gap | MVCC can expose Pending while a mutator still owns locks and may commit. Reconciliation must wait on the same Library/row lock barrier or remain OUTCOME_UNKNOWN; absence/old data from a plain SELECT is insufficient. |
| F07 SQL and write-once semantics underspecified | Medium / persistence gap | PostgreSQL CHECK accepts NULL/UNKNOWN. V2 requirements need explicit IS NOT NULL guards, legacy-version scoping, and a receipt written once at consumption (not immutable-null from creation). |
| F08 Cleanup conflicts with current authority | Medium / architecture gate | Architecture section 29 prohibits background jobs. A new Steam metadata worker is not covered by the cover-cleanup exception. Recommend bounded owner-scoped opportunistic cleanup, with no inactivity deletion SLA; a worker needs an explicit architecture amendment. |
| F09 Error/transport contracts conflict | Medium / implementation gap | Cancel was both terminal 204 and terminal 409; OPTIONS cannot require JWT/JSON; concurrency limiters may not supply Retry-After; provider redirects/cookie containers and decompression need explicit bounds. Resolved below. |
| F10 PR boundaries lacked executable activation safety | Medium / delivery gap | A default-off flag alone can accidentally enable an unfinished PR. PRs 1–4 must ship an unconditionally false v2 readiness policy; PR #5 may make the completed protocol eligible for an independent runtime opt-in, never automatically enable it. |
| F11 Privacy tests confused transport with collection | Medium / contradictory acceptance | S/T necessarily appear in specified private protocol bodies and B/JWT in designated headers. Require absence from telemetry/body capture, not impossible absence from all actual transport messages. Publish the allowed-flow table below. |
| F12 Confirmation UI had no framing requirement | Medium / defense-in-depth gap | Current frontend hosting header configuration has no frame-ancestors/X-Frame-Options protection. Given injectable staged identity, protect the review route and SPA fallback from foreign framing; browser/hosted tests must verify actual response headers. No clickjacking exploit was attempted. |

### Architecture decision register (recommendations pending approval)

| Decision | Recommended resolution and rationale | Alternative / consequence |
| --- | --- | --- |
| D01 Transport | A only: fixed authenticated initiation POST → trusted Steam URL in body. Minimum endpoints/failure states. | B retained as evaluated alternative, not an additional implementation/fallback; would need dispatch marker and extra review. |
| D02 Browser possession | Independent fresh 32-byte B on every successful creation; only contextual SHA-256 verifier V persisted. Never reuse an incoming cookie or derive from S. | JWT-only correlation removes same-browser binding and is rejected for this protocol. |
| D03 Cookie | Host-only __Host- v2 cookie, Secure/HttpOnly/None, Path=/, absolute deadline, no callback refresh/delete. Preserve strict local-only exception. | Lax works for top-level return but not cross-site credentialed frontend requests; same-site hosting or different tab policy needs separate approval, not a bypass. |
| D04 Confirmation authorization | JWT U + matching B + exact T/U/L + immutable staged SteamID + expectedSteamId64 + Origin + live eligibility. JWT alone is insufficient for an existing flow. | Requiring recent password/MFA could address stolen-session risk but is a separate product/auth design, not silently added. |
| D05 Extra confirmation token | **Do not add one**: T is a locator, expectedSteamId64 is a nonsecret compare-and-confirm precondition, SQL supplies single use. Review may be repeated without minting authority. | A separate review token can prove a review request occurred, not human intent; adds storage/rotation/retry races without preventing XSS or a stolen JWT creating a new flow. Reassess only if an approved requirement demands a server-recorded review step. |
| D06 User intent / presentation | Trusted Angular shows full verified SteamID and current Game Library account; explicit Confirm, no auto-submit, non-frameable review page. | Backend enforces the request's identity precondition, not proof that a person read/clicked it. Product must accept numeric-ID recognition limits or separately approve richer verified provider metadata. |
| D07 Concurrency | One live row per Library and one browser cookie slot; most recent committed creation replaces older live row. | Per-tab cookie sets can improve availability but expand cookie/quota complexity; not selected. Reordered responses safely require restart. |
| D08 Cancellation / expiry | Explicit cancel requires authenticated owner+proof; replacement/unlink use their existing authenticated owner authority and pause invalidation is controlled maintenance. Provider negative/cancel returns are nonmutating. Database clock and final eligibility point govern deadline; no sliding lifetime. | Automatic cancellation from unsigned provider return creates avoidable state-disclosure-triggered DoS in the original browser. |
| D09 Retention | Purge eligible rows in bounded owner-scoped existing-use-case work, no new worker. No promise of deletion for inactive Libraries. | A strict inactivity retention SLA requires a narrow approved Architecture exception for a bounded API worker before implementation. |
| D10 Cutover / rollback | No legacy acceptance window; additive schema; pause/drain/invalidate; safe compatible rollback, no down-migration. Intermediate builds cannot activate v2. | Keeping legacy callback compatibility restores the known weakness and is rejected. |
| D11 Privacy | State-disclosure-tolerant **link authorization**, not state secrecy or absence of influence. B/JWT must not be collected; callback/provider URL retention is a separate privacy gate. | An absolute no-secret-in-any-provider-URL requirement is not met by A or B and requires a different approved solution. |

Once the owner approves these recommendations and authoritative amendments, they
are the implementation baseline; alternatives are not implementer discretion.

## 2. Current architecture and evidence

### Source inventory and lifecycle

| Boundary | Current behavior and source |
| --- | --- |
| Angular → API | `features/manage/steam/steam.service.ts` POSTs `{}` to `/steam/link-requests` with Bearer JWT and `withCredentials: true`. `steam-page.ts` navigates to response `startUrl`. |
| Auth | `core/auth/auth.service.ts` observes Supabase auth changes; `token.interceptor.ts` attaches the current access token only to the API origin. There is no current Steam-specific account-generation/confirmation guard. |
| Creation | `SteamController.CreateLinkRequest` derives `sub` from validated JWT. `SteamIntegrationService.CreateLinkRequestAsync` ensures the Library, locks it, rejects an existing link, generates 32 random bytes of state, stores only SHA-256, commits, then returns raw state to the API boundary. |
| Cookie | `SteamLinkBrowserBinding.cs` sets the **same deterministic state hash** in a host-only HttpOnly cookie. HTTPS: `__Host-GameLibrary.SteamLink`, Secure, SameSite=None, Path=/; local HTTP: `GameLibrary.SteamLink`, Lax. Deadline matches state. |
| Start | Anonymous GET `/steam/link/start/{rawState}` checks the cookie and pending state, then redirects to the configured Steam OpenID URL. `SteamOptions.cs` constructs callback `?state=…`, realm and trusted frontend redirects. |
| Callback | Anonymous GET `/steam/link/callback` checks the deterministic cookie, flattens query values, then calls Core. `SteamClient.VerifyOpenIdAsync` POSTs `openid.*` with `check_authentication` to the configured verification URL. |
| Assertion rules | Core checks positive direct verification, expected provider, exact return_to, matching identity/claimed_id with Steam prefix and numeric ID; checks namespace/realm when present. It does not explicitly enforce required signed-field coverage, response-nonce freshness or reject duplicate query values. These are inspection findings, not claims of a demonstrated signature bypass. |
| Commit | Provider I/O occurs outside SQL locks. Core locks state then Library, rechecks expiry with fresh UTC after waiting, checks uniqueness, atomically sets `consumed_at` and inserts `SteamAccount`. Known Library/SteamID unique violations roll back. |
| Failure | Missing/wrong cookie, invalid assertion or unavailable linking redirects to a fixed failure location. Verification failures do not consume state. There is no explicit transaction cancellation endpoint. Abandonment expires. |
| Unlink | Bearer-authorized, Library-locked deletion of the link; imported games and their Steam references remain. Current pending requests are not cancelled by unlink. |
| Concurrency | One cookie slot; later response overwrites earlier proof. Callback responses never set/delete it. SQL gives one winner, but a callback already in flight can win after a newer initiation. |

Persistence inspected: `SteamLinkRequest.cs`, `SteamAccount.cs`, Library ownership,
`GameLibraryDbContext.cs`, original migration
`20260826034524_AddSteamIntegration.cs`, later migration designers and current
model snapshot. `steam_link_requests` has only `id`, `library_id`, `state_hash`,
`created_at`, `expires_at`, `consumed_at`. It has unique state hash, a Library index,
cascading Library FK and hash-length/deadline checks. `steam_accounts` has unique
Library and SteamID indexes. **No independent browser verifier or staged result exists.**

Relevant existing tests: `SteamEndpointTests`, `SteamTestFactory`, `SteamClientTests`,
`SteamLogPrivacyTests`, `SteamLogPrivacyEndpointTests`, Core Steam rule tests,
Angular Steam service/page tests and `src/frontend/e2e/app.spec.ts`. They cover
normal linking, invalid assertions, expiry, replay, two-user isolation, concurrency,
cookie transfer, delayed callbacks, no-store and logging. The transfer tests use an
unmodified second cookie jar; they do not model deriving a cookie from leaked state.

### Confirmed findings versus limitations

1. [Sanitized historical evidence](../deployment/logging-investigation-2026-11-19.md)
   identified Railway-managed HTTP records containing the start-path state. The
   [application mitigation](../deployment/steam-openid-log-privacy.md) cannot redact
   a target already seen by Railway's edge. PR #8 disables the named raw hosting
   diagnostic source and substitutes payload-free Steam completion/error events;
   retain those protections and useful unrelated auth/provider/EF warnings.
2. A prior database/network-free synthetic probe of the actual compiled browser
   check confirmed that a cookie computed from known synthetic state passes.
   HttpOnly/Secure/__Host- restrict browsers, not an attacker's HTTP client.
3. Live-state disclosure therefore permits attempting first-use link substitution
   with an eligible, valid Steam assertion before expiry/consumption. It does not
   create a Supabase session or authorize private API access. Valid Steam proof,
   uniqueness and winning the SQL race remain necessary. No complete real exploit
   or evidence of abuse is claimed. Expired/consumed historical state is unusable.
4. Railway's public schema omits query/body/arbitrary-header values; that is not
   an all-stores privacy guarantee. Employee guidance says public edge HTTP logs
   cannot be disabled/redacted per service/plan or selectively deleted; stated
   stored retention differs from the plan's shorter visible window. Do not assume
   moving a secret to a query, custom domain, redirect or no-store fixes collection.

References (reviewed during the investigation; revalidate before activation):

- [Railway HTTP logs](https://docs.railway.com/cli/logs#http-logs)
- [Railway employee logging/retention answer](https://station.railway.com/questions/can-http-request-logging-be-disabled-or-30c1f581)
- [Visible callback-query answer](https://station.railway.com/questions/o-auth-callback-query-parameters-in-railw-c4e27725)
- [Railway tracing](https://docs.railway.com/observability/tracing)
- [Steam website OpenID](https://partner.steamgames.com/doc/features/auth#website)
- [OpenID 2.0 indirect communication](https://openid.net/specs/openid-authentication-2_0.html#indirect_comm)
- [OpenID positive-assertion verification](https://openid.net/specs/openid-authentication-2_0.html#verification)

Other dynamic application paths carry correlatable resource UUIDs, including cover
upload IDs, protected by JWT and fresh Library/state checks. They are not equivalent
bearer capabilities. This source inventory is not a certification of Supabase Auth,
external image sites, arbitrary malformed requests or provider-internal records.

## 3. Trust model and invariants

Trust boundaries: Angular and browser-controlled inputs → JWT API boundary → Core
and PostgreSQL; browser cookie jar → API; Steam indirect assertion → fixed trusted
backchannel verifier; application → Railway ingress/telemetry/export/support stores.
The browser, callback parameters, arbitrary Origin/Host/forwarded headers and URLs
are not authorities for identity, ownership or destination configuration.

Use distinct terms throughout implementation and tests:

- **S:** 256-bit random state, used only for exact OpenID transaction correlation.
- **B:** separately generated 256-bit random browser secret. Independent RNG draw;
  never `hash(S)`, a public function of S, a user ID, or a deterministic transaction value.
- **T:** random UUID transaction ID, a non-authorizing lookup handle. Knowing T or
  S cannot replace B or a valid JWT. Keep T out of URLs too to reduce correlation.
- **U/L:** initiating validated JWT subject and its Library. Never client-selected.
- **V:** server-side browser verifier; only V, never B, is persisted.

Required invariants:

1. No generated API request path contains S, B, assertion, JWT or a replacement
   bearer handle. No initial API query carries them either. B never enters any URL.
2. Possessing S alone, its hash, T, or an assertion alone grants no linking ability.
   Assume S and the complete callback query are disclosed from creation onward;
   security tests must not rely on their obscurity. Disclosure can still enable
   browser-mediated injection/denial of service, as distinguished below.
3. Accept an assertion only with matching independent B for that exact live v2 row.
4. An anonymous callback **never creates `SteamAccount`**. Confirmation requires
   current valid Bearer JWT for U, Library ownership still U/L, matching B, and an
   matching expectedSteamId64 precondition in an explicit confirm request. The trusted
   UI must display/review that ID; the backend cannot verify a human saw or chose it.
5. A verified identity cannot be replaced within a transaction. State accepts at
   most one assertion; confirmation creates at most one account link atomically.
6. Deadline never slides; cancellation, replacement or expiry blocks new authorization
   at the final database eligibility point, not necessarily COMMIT acknowledgment.
   A previous failure cannot extend lifetime or relax checks.
7. One SteamID per Library and one Library per SteamID remain enforced in SQL.
   No callback, confirmation or retry overwrites an existing account.
8. Steam linking never authenticates the application or triggers preview/import.

An application account switch is defined at the API by the JWT used for the final
request, not by trusting Angular's cached user ID. A callback begun as U cannot
link another user W, nor silently link U after a callback while W is signed in:
there is no link until a request authenticated as U passes confirmation checks.
An honest frontend will not issue that request while W is signed in. A switch back
to U may allow a fresh review/confirmation before expiry; the backend cannot detect
every historical logout without introducing session tracking. An already authorized
confirmation in flight may commit for U while logout happens elsewhere; it can
never commit for W. Immediate JWT revocation/recall is outside this design.

### Backend versus frontend authority

| Protection | Enforced by backend / SQL | Frontend responsibility and limit |
| --- | --- | --- |
| Application user | Existing issuer/audience/signature/algorithm/lifetime/sub validation; Core compares initiating U with current Library.UserId. Recheck signed JWT exp at the final mutation eligibility point. | Auth-generation guard hides stale data/navigation; it is not authorization and cannot invalidate a previously admitted request. |
| Browser binding | Core recomputes V using the presented B and immutable row context for callback, review, confirm and cancel. | withCredentials requests cookie transport, not authority; JS cannot read B. Browser possession is not physical-device attestation. |
| Steam identity | Fixed trusted verifier, strict fields/signature coverage/nonce, one write-once staged ID and server-side expected-ID equality. | Escape/display the complete ID, current application account and explicit button; no auto-confirm. Backend does not observe a click or require a review API call before confirm. |
| Single use / expiry | Library/row locks, existing account unique indexes, state transitions, final DB-clock checks and receipt matching. | Disabled buttons and tab metadata improve UX only; duplicate/tampered direct requests must remain safe. |
| CSRF / UI integrity | Origin check plus Bearer on JSON; no cookie-only mutation. Origin is not a secret and can be spoofed by non-browser clients. | Serve review route with CSP frame-ancestors 'none' (or an explicitly approved equally restrictive policy) and X-Frame-Options DENY; a JS frame-buster is not equivalent. Does not defeat same-origin XSS. |
| Secret handling | No B/JWT forwarding to Steam; no body/header/raw-query diagnostics; private/no-store through application handlers. | No secret copies/analytics/PWA data caching. Neither layer controls all Railway/Steam internal storage. |

**Stolen-session boundary:** B is anti-transfer/flow binding, not a second factor.
For an existing T, a stolen JWT without B fails. An attacker able to use a valid JWT
as U can instead call authenticated creation, supply an allowed Origin header from
their own HTTP client, receive a new B, replace U's old live flow and link a Steam
account they control. That is normal bearer-session compromise, not prevented by
this redesign or by an additional token minted from that same JWT. Do not claim
resistance without a separately approved step-up/reauthentication design.

## 4. Options comparison

Both options use the same proof, verification, staging and confirmation protocol.
Only initiation-to-provider transport differs. Implement **one**, not both by default.

| Criterion | A — direct Steam navigation (recommended) | B — fixed-path POST handoff |
| --- | --- | --- |
| Authenticated initiation | POST `/steam/link/v2/requests`; body contains trusted `authorizationUrl` | Same POST; body contains a fixed handoff URL and form fields |
| Next browser request | Direct top-level navigation to Steam; no API start hop | Native top-level POST to `/steam/link/v2/handoff`; S in form body, then 303 to Steam |
| State-bearing API start path/query | None | None |
| Browser proof | Cookie B issued at authenticated creation, checked at callback/review/confirm | Same; additionally checked at handoff |
| Additional attack surface | Validate returned navigation target; no extra API endpoint | Form parser, Origin enforcement, body redaction, handoff replay/unknown-result handling |
| Server dispatch checkpoint | Not available/needed; replaying provider URL cannot bypass one-time callback | Atomic one-time dispatch marker required; failure after dispatch requires restart |
| Cross-site cookie problem | Credentialed initiation and later API review may be blocked | Same; top-level handoff is not a guaranteed workaround for a cookie blocked at creation |
| Data visible to frontend | Authorization URL includes encoded return_to/S; B inaccessible to JS | Handoff body includes S; B inaccessible to JS |
| Residual URL exposure | Steam authorization URL, encoded return_to, callback query | Same after the handoff; not eliminated by its POST body |
| Recommendation | Smallest transport change, fewer failure states | Choose only for a concrete approved need for a dispatch endpoint |

**Feasibility limit:** if “no sensitive value enters a provider-logged URL” means
every URL at Railway/Steam/internal stores, neither option can presently guarantee
it. Standard Steam OpenID returns the assertion and transaction-bound return_to
through the browser. B eliminates S from the *initial handoff request target*, not
from the subsequent Steam authorization URL or callback query. Do not claim Steam
supports OAuth/OIDC `response_mode=form_post`; OpenID's general form transport is
not evidence of Steam callback support. A total callback-URL prohibition would
require a separately approved, interoperable protocol/hosting solution; stop rather
than remove state, invent a proxy, or silently weaken exact return_to binding.

## 5. Proposed Option A sequence

```mermaid
sequenceDiagram
    actor User
    participant UI as Angular/browser
    participant API as ASP.NET API + Core
    participant DB as PostgreSQL
    participant Steam as Trusted Steam OpenID
    User->>UI: Link Steam (signed into Game Library as U)
    UI->>API: POST /steam/link/v2/requests (JWT U, Origin, credentials)
    API->>DB: Lock Library; cancel prior live row; insert T/hash(S)/V/U/L/deadline
    DB-->>API: COMMIT
    API-->>UI: 201 {transactionId, authorizationUrl, expiresAt}; Set-Cookie B
    Note over UI: Store only T/U/deadline per tab; retain no URL/state
    UI->>Steam: Top-level navigation to trusted authorization URL
    User->>Steam: Authenticate/approve the intended Steam account
    Steam-->>UI: Redirect to configured callback with S + OpenID assertion
    UI->>API: GET /steam/link/v2/callback?state=… (cookie B)
    API->>DB: Preliminary v2/live-row/proof checks (no locks held over I/O)
    API->>Steam: POST check_authentication to fixed verification endpoint
    Steam-->>API: Valid/invalid direct verification
    API->>DB: Lock Library then row; recheck proof/expiry/state; stage SteamID once
    DB-->>API: COMMIT (no SteamAccount yet)
    API-->>UI: 303 fixed /manage/steam?steamLink=review (no T/S/assertion)
    UI->>API: POST /steam/link/v2/review {transactionId} (JWT U, cookie B)
    API-->>UI: AwaitingConfirmation + verified SteamID/deadline
    User->>UI: Confirm linking displayed SteamID to current Game Library account
    UI->>API: POST /steam/link/v2/confirm {transactionId, expectedSteamId64} (JWT U, B)
    API->>DB: Lock Library then row; recheck all; consume + insert link atomically
    DB-->>API: COMMIT
    API-->>UI: 200 Linked
    UI->>API: GET /steam/status (ordinary Bearer request)
```

Only fixed review/failure flags reach the frontend; they are presentation hints,
never authorization. A fabricated flag cannot create or confirm a staged result.
No callbacks write/delete cookies, even on failure or success.

## 6. v2 HTTP contracts

All v2 JSON endpoints require `Content-Type: application/json`, exact configured
frontend Origin, valid Supabase Bearer JWT with usable `sub`, credentialed request
and narrow exact-origin credentialed CORS (`POST`/needed headers only, never `*`).
Reject missing, `null`, untrusted or malformed Origin with 403; CORS alone is not
server-side request validation. Supported clients here are the browser application
and explicit test fixtures. No cookie-only JSON authorization. Reject duplicate
JSON properties, unknown ownership/destination fields and malformed UUIDs. Body
limit: 4 KiB. No client-supplied owner, Library, callback, provider or redirect URL.
These authorization/body requirements apply to actual JSON operations, **not** CORS
OPTIONS: allow an unauthenticated empty preflight only for exact configured Origin,
POST and Authorization/Content-Type headers; no transaction lookup, proof issuance
or mutation. A rejected preflight has no credentialed allow-origin response.
With only the two documented JSON fields/types allowed, any unknown property is 400.

Apply `Cache-Control: private, no-store`, `Referrer-Policy: no-referrer`, and
`X-Content-Type-Options: nosniff` at an early Steam privacy boundary, including
401/403/404/405/409/413/429/500/503, OPTIONS and redirects, not just action success.
Vary credentialed responses by Origin. Exclude these requests/responses from PWA
data caching, body capture and browser analytics. Existing static shell caching stays.
Use allowlisted ProblemDetails codes/messages; never echo input/model-binding values,
exceptions, SQL details or provider bodies. Anonymous callback failures are generic.
These headers cover requests handled by the API pipeline; malformed targets rejected
earlier by ingress/server parsing require separate hosting-layer review. Do not claim
an application middleware can control those responses or their diagnostics.

Retain the existing authenticated Steam initiation limit (five requests/minute per
validated subject, no queue). Use a separate per-subject limit of 30 requests/minute
for v2 review/confirm/cancel, no polling/retry loops. Callback and optional handoff
use a separate four-in-flight-per-instance concurrency limit, no queue, returning
429 before provider I/O when full; do not put all anonymous traffic in the existing
single authenticated-policy fallback bucket. These are bounded resource controls,
not authorization or a distributed quota. Preserve existing preview/import limits.
Fixed-window rejections use the limiter's available Retry-After; concurrency
rejections explicitly emit `Retry-After: 1` as guidance, not a reservation/guarantee.

| Endpoint | Input and successful result |
| --- | --- |
| `POST /steam/link/v2/requests` | `{}` → **201** `{protocolVersion: 2, transactionId: T, authorizationUrl: string, expiresAt: UTC ISO-8601}`; one Set-Cookie B after known successful commit. No Location header. |
| `GET /steam/link/v2/callback` | Anonymous top-level provider return; S and OpenID fields in query, cookie B. On staged success **303** to trusted fixed `/manage/steam?steamLink=review`; on failure **303** to fixed failure route. No callback query copied to Location. No successful JSON response. |
| `POST /steam/link/v2/review` | `{transactionId: T}` → **200** `{state: "Pending" \| "AwaitingConfirmation" \| "Linked", steamId64: string-or-null, expiresAt}`. SteamID only for staged/linked result. JWT U + B + exact row binding required. Linked means this receipt is still the current link, not merely some account exists. |
| `POST /steam/link/v2/confirm` | `{transactionId: T, expectedSteamId64: string}` → **200** `{state: "Linked", steamId64, linkedAt}` after atomic commit, or identical verified receipt for a safe retry. Server links only its staged ID; expectedSteamId64 is a review precondition, not identity authority. |
| `POST /steam/link/v2/cancel` | `{transactionId: T}` → **204** for an owned/proven row, whether Pending, staged, expired or already terminal. With proof still present an expired unconsumed row may be cancelled; cancellation never authorizes linking. Never unlinks a consumed row. Must serialize against confirm. |

No list/resume endpoint discovers transactions from a cookie alone. Invalid/missing
proof and unknown/other-user rows receive indistinguishable **404 LINK_REQUEST_NOT_FOUND**
on review/confirm/cancel. No JWT → 401. On **review/confirm**, owned/proven expired, cancelled, replaced,
wrong phase or mismatched expected SteamID → **409 LINK_REQUEST_NOT_ACTIVE** (or
**409 REVIEW_REQUIRED** for the expected-ID mismatch). Do not reveal which other
Library owns a SteamID. A uniqueness loss → **409 LINK_CONFLICT**, with staged row
unchanged by the rolled-back mutation; UI offers cancel/restart, never overwrites.
Consumed receipt whose link was subsequently removed/replaced → **409 LINK_NOT_CURRENT**.
Malformed bodies → 400; oversize → 413; throttling → 429 + Retry-After; unavailable
configuration/provider/database → 503 with constant text. Unexpected defects → safe
500. Never map arbitrary database failures to ordinary uniqueness conflict.

Resolve overlaps in this order: retired legacy route → 410; supported v2
method/body envelope and admission gate; validate JWT/Origin; lookup + verify row
ownership and B before disclosing lifecycle details; then operation-specific phase
rules below. Normal middleware 401/403/405/413/429 errors remain possible before
lookup. Codes never include secrets or another account's owner.

| Proven row | Review | Confirm (expected ID must match on success/retry) | Cancel |
| --- | --- | --- | --- |
| Live Pending | 200 Pending | 409 LINK_REQUEST_NOT_ACTIVE | 204, mark cancelled |
| Live staged | 200 AwaitingConfirmation | Atomic 200, or 409 REVIEW_REQUIRED / LINK_CONFLICT | 204, mark cancelled |
| Live consumed, receipt still current | 200 Linked | 200 same receipt; wrong expected ID → 409 REVIEW_REQUIRED | 204 no-op |
| Live consumed, receipt not current | 409 LINK_NOT_CURRENT | 409 LINK_NOT_CURRENT, never reinsert | 204 no-op |
| Cancelled/replaced or deadline reached | 409 LINK_REQUEST_NOT_ACTIVE | 409 LINK_REQUEST_NOT_ACTIVE, even if an old receipt exists | 204 mark cancelled if unconsumed, otherwise no-op |

This table does not bypass proof: after browser expiry B will normally be missing
and the response is generic 404, including cancel. It only defines behavior for a
client still presenting the correct B. An expired JWT never authorizes cancellation.
Review is repeatable; there is no confirmToken, review nonce or requirement that a
review request occurred. The backend validates identity/authority, not UI history.

Creation with an already linked Library is 409; no cookie/transaction replacement.
No automatic HTTP retry of creation: an explicit retry is a new transaction and
cancels the prior live one. No raw state/authorization URL recovery endpoint.

Keep `/steam/status`, `/steam/link` DELETE, preview and import public contracts
otherwise unchanged. Add `linkProtocolVersion: 2` to status when v2 is available.
Separate a default-off **SecureLinkingEnabled** activation gate from global Steam
configuration: disabling *new linking* must not hide existing links or unnecessarily
disable configured preview/import/unlink. Status derives `linked` independently of
this new gate; import availability still requires its existing provider configuration.
Unlink additionally cancels all live v2 transactions for that Library under its lock.
With the gate off, newly admitted creation/review/confirm/cancel return safe 503 and callback/handoff
return the fixed failure redirect without verification/mutation; legacy remains 410.
Gate changes cannot resurrect previously outstanding rows: before any reactivation
after a pause, cancel all unconsumed/uncancelled v2 rows through a controlled,
Library-lock-ordered maintenance operation while all replicas remain paused.
The gate is not a global revocation event: already admitted mutations may finish.
Section 11 defines the mandatory admission/drain barrier before invalidation.

Eligibility is the conjunction of **build readiness** (hard false in PRs 1–4),
runtime SecureLinkingEnabled, validated v2 configuration and matching EF schema.
Missing/incompatible schema makes only secure linking unavailable (503 / fixed
failure), without falling back to v1 or breaking manual-library/status/unlink paths.
Schema/readiness checks must not return live accounts or secrets to an anonymous
diagnostic endpoint. A false readiness policy cannot be overridden by a public
configuration/test flag in a shipped Production build.
Integration test hosts may replace the admission policy through test-only DI;
local E2E may use explicit Development/Test + existing E2E auth/provider guards +
loopback-only configuration/network isolation. Production and ordinary Staging
composition must reject all such bypasses regardless of flags. This test admission
does not constitute readiness of the shipped protocol in PRs 1–4.

### Option B contract delta (only if selected)

Creation instead returns **201** `{protocolVersion: 2, transactionId, expiresAt,
handoff: {url: <trusted fixed API /steam/link/v2/handoff>, method: "POST",
fields: {state: S}}}` with the same cookie. Never return an API state-bearing URL.
Angular builds/submits a native same-tab form (not a fetch followed by cross-origin
redirect following), sending only this field, no JWT in HTML/form/URL. Do not accept
arbitrary HTML from the API. Validate exact action/origin/method; constrain CSP
form-action appropriately without broadening unrelated sources.

Handoff accepts only POST `application/x-www-form-urlencoded`, max 4 KiB, exactly
one canonical state field, exact configured frontend Origin (no missing/null bypass),
v2 active row and matching B. It is deliberately anonymous with respect to JWT:
the authenticated creation + S + independent B bind the handoff, not a reusable
application auth session. No GET handoff and no query-state fallback. Check all
conditions before provider dispatch, atomically set nullable `dispatched_at` once
under the shared locks, then **303** to the trusted Steam URL. Repeats fail closed;
lost dispatch response requires fresh initiation. No cookie issuance there. Callback
additionally requires dispatch recorded. Wrong Origin → 403, invalid proof/state or
repeat → generic failure; no existence details. Same privacy/size/rate protections
apply. B needs one extra timestamp/mapping/test set; A does not add it.

## 7. Independent browser possession and browser behavior

### Secret and verifier

Generate S and B using separate `RandomNumberGenerator` draws of 32 bytes. Canonical
unpadded Base64url is exactly 43 ASCII characters; reject noncanonical encodings,
padding, whitespace, overlong values, multiple state parameters or duplicate cookie
names. Decode/re-encode validation must round-trip. Persist SHA-256(S) as existing
lowercase hex for lookup, never S. No password KDF is needed for a 256-bit random secret.

Define V as SHA-256 of an unambiguous, versioned encoding:

`UTF8("GameLibrary/SteamLink/v2/browser\0") || UUID-bytes(T) || length-prefixed UTF8(U) || UUID-bytes(L) || B-bytes`

UUID bytes use RFC 4122/network order; the string length prefix is unsigned 32-bit
big-endian byte length, not character count. Hash the decoded 32-byte B, not its
presentation text. Persist V as 32-byte `bytea`; compare recomputed V with
`CryptographicOperations.FixedTimeEquals`. Immutable T/U/L and protocol version
are rechecked for every use. Do not log V, state hash, or object stringifications.
V is not reusable as B. No signing-key distribution/rotation mechanism is required.
Creation **ignores any incoming B for generation** and never adopts/reuses it,
including a correct cookie from an older row. Only a new independent RNG draw is
eligible for the new V. Reject duplicate v2 cookie occurrences in the raw Cookie
headers before a framework parser could collapse them; accept only the exact
case-sensitive name and canonical value, without percent-decoding it. A client
cannot fix the new verifier by choosing a cookie value in its request.

Cookie value is **only B**, not S, V, U, T, JWT or provider credential. Core generates
and checks the verifier in its use cases; API handles cookie transport. Core must
not expose a use case that can link merely because a controller supplied an unchecked
`browserMatched=true`. The proof is enforced before provider calls and again against
the locked row before state mutations, with no raw-proof exception/log emission.

### Cookie policy

- New HTTPS name **`__Host-GameLibrary.SteamLink.v2`**, HttpOnly, Secure, Path=/,
  **no Domain**, SameSite=None, IsEssential=true. Do not read either legacy name as
  fallback. None is needed for credentialed cross-site frontend/API calls; Lax is
  insufficient on those fetches even if a top-level GET callback could carry it.
- Expires is the original transaction deadline; Max-Age is its remaining lifetime,
  never longer. Default ten minutes; retain current configured clamp of 1–60 minutes
  unless the owner separately approves reducing it. Staging/review never extends it.
  Derive remaining lifetime from authoritative creation/deadline and elapsed time;
  round down rather than extending it. Clock-skewed/late browser cookies never
  override the SQL deadline; only B is emitted, not verifier/context metadata.
- Only known-successful authenticated creation issues a fresh B. Callback, review,
  confirmation, cancellation and errors **never set/delete/refresh that cookie**.
  Avoid delayed responses erasing a newer flow. Let the bounded cookie expire.
- Explicit local Development/E2E exception: `GameLibrary.SteamLink.v2`, HttpOnly,
  Path=/, SameSite=Lax, non-Secure only for configured loopback HTTP on one host name
  across ports. No production/non-loopback downgrade and no auto-detection from
  attacker-controlled forwarded scheme. Production frontend and API require HTTPS.
- No CHIPS/partitioned-cookie assumption: a partition established under the frontend
  may not be available on the top-level Steam return. No JavaScript-readable proof,
  query-token fallback or auto cookie-disable bypass when browsers block cookies.

### Concurrency policy: one live row per Library, one browser cookie slot

Prefer this bounded policy over many per-transaction cookies. Starting a new flow
cancels any previous Pending/AwaitingConfirmation row for **that Library**, even in
another browser. Explain this in UI. A same-browser initiation for a different user
replaces the cookie but does not mutate the other user's rows; those expire and
cannot be used with the new B or wrong JWT. Do not infer cross-user ownership from B.

The latest **committed** creation wins in SQL, not the last HTTP response received.
Reversed/delayed Set-Cookie responses can leave a cookie for an already cancelled
row, causing *all* outstanding flows in that browser to fail. That is an accepted
availability failure: show restart guidance; never accept old proof or resurrect a
row to make it work. Per-tab request disabling reduces but cannot eliminate this
race. A callback in provider verification when replacement commits must fail its
post-lock checks, not stage or link the cancelled row.

Transfer to another browser/profile/device, missing B, blocked third-party cookies,
cleared site data or expired cookie fails closed, even with S and a valid assertion.
User must restart there while authenticated. A correct B copied by a privileged
attacker is not browser fingerprinting resistance; final JWT U is still required.
Host-only __Host-/Secure/HttpOnly prevents ordinary sibling-domain cookie tossing,
script reading/writing of this cookie and plaintext delivery in supported browsers;
it does not stop an arbitrary HTTP client from sending a header, privileged cookie
theft or a fully compromised API origin. Cookie replay after cancellation/consumption
must fail the server lifecycle checks even if the cookie has not expired.

## 8. Assertion processing and transaction state machine

### Strict OpenID boundary

Build authorization URL/return_to/realm from validated server configuration, never
request Host, arbitrary forwarded headers or a request body destination. Production
provider and verification URLs outside explicitly gated loopback test composition are pinned to
`https://steamcommunity.com/openid/login`; disable automatic backchannel redirects
to avoid forwarding assertions elsewhere. Non-production loopback fakes require the
existing explicit E2E/test gates and must remain impossible to enable in Production.
Public API/frontend bases are trusted explicit HTTPS origins/base paths, with no
userinfo, fragment or unexpected query. A configuration change affecting an in-flight
expected URL invalidates that flow rather than accepting both old and new values.
This is a Steam-only pinned identity profile, not a general OpenID discovery client:
accept no delegated identity, arbitrary claimed-ID host, user-entered provider or
callback-selected discovery/verification URL. Trusted Steam must vouch directly for
the exact numeric Steam identifier under its documented namespace.

Callback target is fixed `/steam/link/v2/callback?state=<canonical S>`; retain S in
the exact expected return_to. Reject duplicate state/OpenID keys, overlong or unknown
callback fields (only state and defined OpenID 2.0 verification fields accepted),
and ambiguous parsed values **before** contacting Steam. Request-target/query cap:
16 KiB total, at most 32 parameters, each key at most 128 bytes and each value at
most 8 KiB. Enforce application limits without silently truncating; ensure hosting
limits permit the approved envelope and still fail privately when rejecting it.
For positive responses the only allowed keys are state and openid.ns, mode,
op_endpoint, claimed_id, identity, return_to, response_nonce, assoc_handle, signed,
sig, plus optional realm/invalidate_handle. Every required value is single/nonempty.
For negative responses use state/ns/mode, with error/contact/reference only in an
error message; ignore their text for presentation/logging. No unsolicited extension
namespace, arbitrary pass-through field, comma-joined duplicate or POST/body fallback.
Reject duplicates after HTTP key decoding as well as conflicting raw encodings.

Require correct explicit `openid.ns`, mode `id_res`, exact pinned op_endpoint,
exact return_to, identical identity/claimed_id, canonical valid SteamID64 with exact
trusted claimed-ID prefix and no extra path/query/fragment. If realm is present it
must match; do not require an echo not mandated by OpenID. Require sig, assoc_handle,
response_nonce and signed-field list covering **op_endpoint, claimed_id, identity,
return_to, response_nonce, assoc_handle**; reject duplicates/missing coverage. Treat
all these values as untrusted until direct verification succeeds. Do not use a
callback-provided endpoint for the verification POST.

Parse response_nonce UTC timestamp; reject older than ten minutes, earlier than
row creation minus 60 seconds, or more than 60 seconds in the future. Also enforce
the original transaction deadline independently, including after lock waits. A
transaction-bound signed return_to plus one accepted assertion per row prevents
replay across transactions without a new global nonce table; do not claim nonce
uniqueness independent of that binding. Validate this reasoning with two-transaction
replay tests. Additionally, OpenID 2.0 section 11.3 requires the OP in stateless
`check_authentication` mode not to return success twice for the same response_nonce.
This design relies on that provider guarantee, not an undocumented local nonce
cache; no association/local-signature mode is introduced. A future move to local
signature checking would require a durable nonce uniqueness design and new approval.
Disclosure of a complete signed assertion can also let an attacker spend its nonce
by calling the provider's public check_authentication endpoint first. API-side B
checks cannot prevent that external denial of service; no link is granted and a
fresh flow/assertion may be required. State secrecy is not the mitigation claimed.
At staging re-evaluate nonce freshness using the same fresh DB clock after provider
I/O/lock/write waits. Confirmation relies on the accepted immutable identity and
transaction deadline; it does not re-verify/replay the already spent provider nonce.

Use bounded backchannel response reading (8 KiB) and existing short configured
timeout. Require HTTP 200 and a well-formed OpenID direct key-value response, correct namespace and exactly
one `is_valid:true`; contradictory/duplicate validity fields, truncation, malformed
response, redirects, non-success HTTP, timeout or network error fail closed. Do not
replace signature verification with parsing. POST only the validated `openid.*`
fields with mode replaced by `check_authentication`; never forward application
cookies/JWTs, owner/Library/T or the separate callback state field. The protocol's
openid.return_to still contains S. Fakes must supply realistic mandatory
fields and signed coverage, not bypass them to keep old tests green.

For backchannel verification set `UseCookies=false` as well as
`AllowAutoRedirect=false`; ignore provider Set-Cookie and never reuse a CookieContainer
across users. Keep RemoveAllLoggers and exclude Activity/HTTP body capture; don't
silence unrelated useful provider warnings. No retry/resilience handler repeats
check_authentication after network errors. The 8 KiB cap applies while streaming
decoded/decompressed bytes even for chunked, missing or false Content-Length; use
strict UTF-8 and a deadline covering response headers **and** body reading. The
timeout remains default five seconds, clamped 1–30 as in current configuration.

`openid.mode=cancel`, setup_needed and provider error/negative responses are
**nonmutating**, even with correct S/B: fixed failure feedback, no stage/consumption,
no implicit cancellation and no error text echoed. They are not signed evidence of
user intent. Positive/negative fields mixed together are rejected, not reinterpreted.
The user may explicitly POST authenticated cancel or restart; otherwise the row
expires. No browser proof → no provider call or lifecycle side effect. Invalid
positive assertions likewise cannot consume or cancel a row.

### Authoritative phases

| Phase | Predicate/transition |
| --- | --- |
| Pending | v2, no cancellation/consumption/assertion acceptance, deadline still future. Creation enters it. |
| AwaitingConfirmation | One verified SteamID + `assertion_accepted_at`, still live. Callback atomically moves Pending here. State cannot accept another assertion, even the same one. No SteamAccount exists because of staging alone. |
| Linked/consumed | Confirmation sets `consumed_at` and receipt `linked_account_id` atomically with SteamAccount insert. Immutable terminal success; callback replays fail. |
| Cancelled/replaced | `cancelled_at` set by authenticated explicit cancel, replacement, unlink or controlled pause invalidation. Unsigned provider cancellation never writes it. Terminal, never revived. |
| Expired | `now >= expires_at` blocks active operations even if no cleanup has run. No sliding extension. |

Review/confirm scope by T, U/L and B. Review is an informational snapshot, not a
reservation; confirm checks everything again and checks expectedSteamId64 against
the immutable staged ID. Review/confirm after expiry fail; a cookie still present
cannot override expiry. The UI displays both the current application account and
verified SteamID before an explicit Confirm button. Choosing the wrong Steam account
at Steam is not detectable from initiation (there is no expected SteamID yet); user
can cancel and restart. Never use a client-selected SteamID as evidence of control.
Produce review's row/owner/current-account/deadline classification from a coherent
primary SQL snapshot, verify proof before returning fields, and treat it as stale
as soon as it is read. Signed JWT exp must still be future at that read decision.
A successful review is not a reservation; confirm's locked checks remain mandatory.

### Locks and atomicity

Change all involved v2 mutations to a **consistent Library → transaction row** lock
order, including creation/replacement, staging, cancellation, confirmation and unlink.
An initial untracked lookup by hash/T may discover immutable LibraryId; then lock
Library, reload/lock row and recheck that relationship and U. Creation locks Library
then old rows in UUID order. No path may hold a row lock while waiting for Library.
Existing preview/import keep their Library lock; do not refactor their domain logic.
Provider verification is outside SQL transactions/locks. There are no process-only
locks or in-memory proof authorities; multiple replicas/restarts must work.
Do not let EF tracking return an earlier entity snapshot after a FOR UPDATE query:
preliminary lookups are AsNoTracking/projections and locked ownership/phase checks
reload actual database values. Reconciliation always uses a new context.

Use PostgreSQL **READ COMMITTED** for these mutations and reconciliation. For each
v2 transaction use bounded lock/statement timeouts (recommended 5s / 10s) and a 15s
total SQL transaction budget/cancellation deadline; timeout rolls back or enters
outcome reconciliation, never blind automatic execution-strategy retry. Provider
I/O retains its separate 1–30s configured timeout and stays outside that budget.
Cleanup must stop its batch within the same bounded transaction budget.

**Authoritative time / linearization:** PostgreSQL `clock_timestamp()` is the
current time source for created_at/deadline, eligibility and persisted lifecycle
timestamps. Do not use `now()`/transaction_timestamp() (fixed at transaction start),
statement_timestamp() after an earlier statement, or replica process clocks as the
final decision. Unit tests may inject a clock; relational tests must exercise the
real database semantics. JWT exp comes from the already validated signed token,
passed with U at the API/Core boundary, not from a body/user_metadata field.

1. After acquiring Library and row locks, read fresh DB time and check row deadline,
   all proof/ownership/lifecycle conditions and, for authenticated mutations, **exp >
   DB time**. Middleware validation remains mandatory but admission-time validation
   alone is insufficient after a wait. No sliding refresh or new JWT creation.
2. Perform proposed stage/consumption/account writes inside that transaction.
   A unique-index check may wait implicitly **inside SaveChanges** even after all
   explicit locks. After all writes and waits return, read fresh DB time again and
   require deadline and signed JWT exp (where applicable) still future; otherwise
   **roll back every write**. Use DB timestamps within the eligible interval.
3. This final successful eligibility check, with all authority rows still locked,
   is the authorization linearization point. Proceed directly to COMMIT, with no
   further provider call, lock acquisition or application work. A COMMIT/network
   acknowledgment can complete after expiry; that does not authorize a new attempt
   after expiry. Do not promise wall-clock completion before deadline. Disconnection
   at COMMIT still requires durable reconciliation.

Explicit cancellation may mark an already expired row and so omits the row-deadline
requirement, but still requires unexpired JWT + U/L/B and locks. Cleanup/controlled
invalidation are narrowly scoped maintenance operations, not user confirmation,
and do not use a browser JWT. Staging has B but no JWT: check its row deadline only.
Any time-based checks in SQL CHECK constraints validate recorded timestamp ordering,
not current expiration; Core transactions enforce current-time eligibility.

After every lock wait obtain fresh UTC and recheck protocol version, exact owner,
V, expiry, cancellation, phase and uniqueness. Callback stages only if still Pending;
concurrent valid callbacks yield exactly one accepted assertion and no link. Concurrent
confirmations yield exactly one insert/consumption. Distinct Libraries claiming the
same SteamID serialize through existing SQL uniqueness: only known account-index
violations become LINK_CONFLICT; rollback preserves the losing staged row. No need
to reserve a SteamID at staging or reveal its other owner.

### Retries and unknown outcomes

- Callback replay is not a second success; it may show generic failure. If the
  first callback staged but its response was lost, the owning original tab can
  POST review using T/B/JWT before expiry; do not rerun provider verification for
  an already accepted row. Never replace its staged identity.
- A verifier request may have consumed the provider's one-use nonce even if its
  response was lost, or staging later definitely rolled back. Do not promise that
  replaying the same assertion can then succeed or retry that backchannel POST
  automatically. Review durable state; if still Pending/unresolved, guide the user
  to a fresh initiation/assertion. Keep the original row unconsumed, not weakened.
- Lost creation response: B/URL may not have arrived. User explicitly restarts;
  the next committed creation cancels the old row. No recovery by storing raw S/B.
- Confirmation may be retried with the same T and expected ID **only after reading
  durable state**. A consumed row is a receipt, not permission for a new insert:
  return success only if its `linked_account_id` still identifies the current
  SteamAccount of the same U/L/SteamID. If unlink/relink occurred, return LINK_NOT_CURRENT,
  never recreate. Receipt access still needs live deadline, B and JWT U.
  The expected ID must match on the receipt branch too; it cannot become optional
  just because consumed_at is set. Ordinary GET status only describes the current
  account and is not proof that a specific T committed.
- A COMMIT exception, disconnect or request cancellation does not prove rollback.
  Reconcile on a fresh primary connection/context by T and receipt; if the outcome
  remains unknown return safe 503, then review/status guidance. Never blindly replay
  a mutation, issue an assumed-success cookie, delete a winner, or report a fabricated
  rollback. DB unavailability cannot turn into a provider retry loop.
- Reconciliation uses a new primary context/READ COMMITTED transaction, locks the
  known Library then T, and reloads after the previous writer's locks resolve.
  Only then classify the durable outcome. A plain unlocked SELECT observing an old
  Pending phase or missing row is **not** evidence of rollback: MVCC can return an
  older version while COMMIT is unresolved. If the lock barrier cannot be acquired
  within its timeout, return **503 LINK_OUTCOME_UNKNOWN**, no automatic write replay.
  A deliberate same-T confirmation retry enters the normal lock-ordered handler;
  it first reads the durable phase and returns the current receipt or performs the
  still-eligible original mutation, never an unconditional insert. If ownership/
  proof/deadline no longer permits receipt access, use generic failure/status guidance.
- After a known creation COMMIT, if the deadline has already passed before delivery,
  do not issue B or an authorization URL; safe 503/restart guidance, no claim that the
  row did not exist. Do not prolong its deadline to compensate for delivery delay.
- Cancel races with confirmation: lock winner decides. If cancel commits first,
  confirm fails; if confirm commits first, cancel is terminal no-op and UI refreshes
  status. Cancel never means unlink. Unlink locks Library, removes only its current
  account and cancels live rows in the same transaction; a consumed receipt cannot
  relink afterward. An intentional new creation after unlink remains permitted.

## 9. Persistence and EF migration proposal

**An EF Core migration is required.** Do not change the original migration, create
a parallel Supabase migration or edit application tables manually. No migration is
created/applied by this specification task. Keep table `steam_link_requests` and
its Library cascade, UUID key, state hash and deadline columns; add:

| Column | Proposed PostgreSQL type and semantics |
| --- | --- |
| `protocol_version` | `smallint NOT NULL DEFAULT 1`; existing rows remain legacy. New v2 inserts explicitly use 2; unrecognized versions rejected. |
| `initiating_user_id` | nullable `text` for legacy; required/nonempty for v2. Snapshot of validated `sub`; never user_metadata or client input. Must still equal Library.UserId at every use. |
| `browser_binding_hash` | nullable `bytea` for legacy; exactly 32 bytes required for v2. V only. |
| `verified_steam_id64` | nullable `text`; staged canonical numeric SteamID64, not raw assertion or provider response. |
| `assertion_accepted_at` | nullable `timestamptz`, set exactly once with verified ID. |
| `cancelled_at` | nullable `timestamptz`; replacement uses cancellation, no separate bearer handle/reason payload. |
| `linked_account_id` | nullable `uuid`; immutable receipt of the account created by this transaction. No cascading FK to SteamAccount: receipt must survive unlink and cannot identify a later replacement link. |

If B is selected, also add `dispatched_at timestamptz NULL`; omit it for A.

Proposed constraints, in addition to existing constraints:

- Version is 1 or 2. V2 requires nonempty initiating user and 32-byte V, and a
  64-character lowercase hexadecimal state hash. Legacy null fields must not be
  interpreted as a bypass; v2 code refuses every version-1 row.
  Write required-field checks with explicit IS NOT NULL, not just length/regex
  comparisons: PostgreSQL CHECK treats UNKNOWN as passing. All v2 lifecycle checks
  must be guarded by protocol_version=2 (or have an explicit legacy branch) so a
  consumed legacy row with no verifier/staged identity/receipt still migrates.
- Verified ID and assertion timestamp are either both null or both present;
  ID must be a canonical positive decimal UInt64, using a range/format constraint
  consistent with the Core parser: ASCII `^[1-9][0-9]{0,19}$`, numeric range
  1..18446744073709551615; no sign/leading zero/overflow. Any numeric cast must be
  safely guarded by CASE/validated representation so malformed text cannot cause
  an uncontrolled conversion error. V2 consumed requires these fields plus receipt;
  v2 unconsumed requires receipt null.
- **Creation-immutable:** T/U/L, V, state hash, created_at, expires_at and version.
  **Write-once later:** verified ID + acceptance timestamp together at staging;
  linked_account_id + consumed_at together at successful confirmation; cancelled_at
  only on an unconsumed row. Receipt is not fixed to NULL forever from creation.
  Retrying cannot overwrite timestamps/IDs or clear terminal state. These transition
  restrictions are Core use-case guarantees tested through competing mutations,
  not claims that SQL CHECK compares OLD/NEW values or that new triggers are approved.
- Cancellation and consumption are mutually exclusive. Recorded acceptance and
  consumption timestamps must lie within `[created_at, expires_at)`; consumption
  cannot precede acceptance. Cancellation is at/after creation, may occur after
  expiry. Retaining a verified ID on a cancelled staged row is valid until purge.
- Keep unique hash, Library FK/index and both existing unique account indexes.
  Add partial unique index on LibraryId for version 2 where `cancelled_at IS NULL
  AND consumed_at IS NULL`. It intentionally includes expired rows: creation first
  cancels any such prior row before inserting. Do not use `now()` in an index predicate.
  Add index on expires_at for bounded cleanup; no verifier index or raw-secret column.

Migration is additive: no backfill of fake verifiers from S/hash, no change to
SteamAccounts, imported entries, platforms, playtime, covers or existing link dates.
Test upgrade from prior schema with linked accounts plus pending/consumed legacy
rows, and fresh database creation. Existing legacy rows remain physically present
but unusable after cutover; no live-data query is necessary for design work.
Add explicit relational negative tests inserting v2 rows with NULL required fields,
orphan receipt, half-populated identity, incompatible terminal flags and invalid
timestamp ordering; all must fail a constraint, not pass via NULL semantics. Test
legacy Pending **and consumed** rows against every added constraint.
Use existing backend/migration roles only; no anonymous/authenticated Data API table
or column grants. Verify backend-only access in disposable-role tests and later
authorized exposure review. If direct client table mutation is possible, treat it
as a blocker rather than assuming the API invariant is sufficient; do not silently
add a second RLS/schema workflow in this task.

Recommended retention mechanism: rows become eligible for purge 24 hours after
expires_at, including success receipts. No new hosted/background service is included
under the current Architecture. During an authenticated creation or explicit cancel,
after holding **that user's Library lock**, optionally delete at most 100 eligible
rows of that same Library, locking rows in UUID order and rechecking DB eligibility
inside the bounded transaction. A failed/unknown commit of the enclosing operation
does not justify separate destructive compensation. No cross-owner scan/provider
call/raw-data diagnostics. Cleanup never touches active rows or SteamAccounts.

This opportunistic mechanism cannot guarantee deletion for an inactive Library;
eligibility at expiry+24h is **not a deletion SLA** and must be approved as such.
Inactive staged identities/verifiers/receipts may remain longer in SQL/backups. If
the owner requires a bounded inactivity retention period, stop and approve a narrow
Architecture section 29 exception for an API-owned metadata cleanup worker before
implementing that alternative; the private-cover worker permission is not inherited.
Do not add scheduled Steam sync, jobs, queues or infrastructure. Existing linked
accounts never depend on transaction-row retention.

## 10. Angular and account lifecycle

Update feature-local Steam contracts/service/page/template and tests; keep signals
and local state, no NgRx. `createLinkRequest` calls v2 and handles 201. Before
navigating validate the returned URL is HTTPS, exact Steam host/path, no userinfo,
port override or fragment. This is defense in depth; API remains authoritative.
Navigate at top level, preferably `location.replace`, not an authenticated HttpClient
call to Steam. Never attach JWT/B to the provider. Keep the existing API-origin
token interceptor boundary and external-image behavior unchanged.

Store only `{transactionId, initiatingUserId, expiresAt}` in feature-specific
**sessionStorage for this tab** so the same-tab full navigation can return and review.
These are non-authorizing correlation metadata, not proof. Do not store S, B, URL,
assertion or a separate copy of the JWT in local/session storage, router state,
analytics or console. Remove the handle on terminal result, expiry, cancellation or
observed subject change/logout. If storage is unavailable or handle lost, fail with
restart guidance; do not discover another tab's transaction or add a token to URL.

Capture an auth-generation marker for all initiation/review/confirmation responses;
ignore stale responses/navigation if `sub` changed or session ended. Token refresh
with the same subject does not count as switching users; every server request still
uses the latest valid JWT. Clear review/preview/private result signals on subject
change, and do not auto-confirm on page load, callback flag, token refresh, login or
status refresh. Best-effort explicit cancellation before an intentional logout may
use the still-valid initiating JWT; failure to cancel cannot permit anonymous linking.
No guarantee of global cancellation on an unobserved logout in a closed tab.

On `steamLink=review`, remove only the recognized query flag with replaceUrl, load
the tab's handle after auth initialization and POST review with credentials. Display
current application account context and verified SteamID as escaped text; require an
explicit Confirm or Cancel. If user changed, no review/confirm for that other user's
row; show “Account changed; start again.” A 401 asks for reauthentication; resuming as
the same U is allowed only by a fresh authorized review and explicit choice before
expiry. Lost response/reload can review a live handle; closing tab requires restart.
An old callback flag may prompt a review but never picks/changes the staged identity.
Display the full canonical SteamID64 (not truncated or a claim that it is the intended
account), a clear wrong-account/restart option, and the current authenticated Game
Library account label. Do not fetch/store a Steam display name/avatar automatically
to make an unfamiliar numeric ID look verified; richer identity UX is a separate
approved provider-data decision. A trusted UI declaration is necessary UX, not a
server-verifiable record of human intent. Prevent foreign-origin framing via actual
HTTP response headers on the review route and its SPA fallback; test the deployed
headers, not a meta tag or only a JavaScript frame-busting check. No general CSP
rewrite or hosting change is performed by this design task.

All four v2 JSON calls use `withCredentials`; ordinary status/preview/import remain
Bearer-only. No global credentials switch. No automatic provider retry/preview/import.
Update mocks and real browser journeys to distinguish staged from linked status.

## 11. Security transition, compatibility and rollback

**No compatibility window accepting legacy linking is safe.** Version the linking
protocol with fixed `/steam/link/v2/*` paths; do not version unrelated Steam endpoints.
Never reinterpret old `startUrl` as a Steam URL or accept old cookies/rows in v2.
Old frontend builds must fail safely and request refresh, not choose legacy fallback.

Owner-authorized deployment sequence (a plan, not actions performed here):

1. Approve this protocol and reconcile authoritative specs. Establish a linking-only
   retirement/activation gate and a tested safe rollback artifact.
2. Retire old creation, state-path start and old callback on **every serving replica**.
   They return constant 410 with no state echo, no provider redirect and no mutation.
   Privacy logging still covers unmatched/encoded old Steam paths. Do not leave an
   old callback callable merely because old creation is disabled. Drain already
   executing legacy callbacks and eliminate old binaries before declaring cutover.
   Before v2 exists, retirement is unconditional code behavior, not a flag that an
   operator/old configuration can turn back on. A rolling overlap is still an open
   exposure window: do not declare the blocker closed while any old instance lives.
3. Apply reviewed additive EF migration through the normal authorized release process.
   Legacy rows remain version 1 and are rejected. A downtime/restart for new linking
   is preferable to routing some requests through insecure replicas. Existing valid
   links/status/import/unlink must remain usable through a linking-only pause.
4. Deploy v2 backend with SecureLinkingEnabled=false; verify only v2-capable instances
   are reachable and legacy routes cannot mutate. Deploy v2 frontend; old tabs fail
   closed. No need to unlink any already linked account or alter its SteamID.
5. Separately authorized synthetic hosted QA, cookie/browser/provider evidence and
   release approval precede enabling v2. Real Steam interoperability smoke, if needed,
   requires separately approved test identity/scope; never use the owner's Library.

### Pause/drain/invalidate barrier and failure recovery

1. Stop new admission to **all** v2 mutation endpoints on every replica, and verify
   deployed build/configuration inventory. A local options flag is not an atomic
   distributed kill switch; admission is evaluated once and in-flight authorized
   work may finish. Neither a callback nor confirm is retroactively revoked by UI
   logout or a flag flip alone.
2. Drain already admitted creation, staging, confirmation and cancel work.
   Ordinary unlink may remain available and serialize on the same Library lock:
   it can only remove a link/cancel rows, never create or revive authority. Track
   completion of the drained endpoints with payload-free handler counters
   and bounded request/SQL/provider lifetimes. An uncertain disconnected writer must
   be reconciled behind its database lock barrier; do not assume it has stopped.
   Before legacy cutover, include every old callback handler in this inventory.
3. Once no authority-granting mutators remain, invalidate all unconsumed/uncancelled v2 rows in
   bounded Library→row ordered transactions; verify no eligible outstanding rows
   remain. Preserving consumed receipts is intentional. No new requests can race
   this pass to create/revive rows because admission remains stopped and all earlier
   authority-granting mutators drained. Unlink only removes authority under the lock.
4. Verify the fully implemented v2 build, additive schema and approved runtime gate
   state on every serving replica before re-admitting new creations. Any incomplete
   drain, failed batch, schema uncertainty or mixed readiness keeps linking disabled.
   Resume a failed invalidation pass idempotently, never reactivate first.

If quiescence cannot be demonstrated, keep linking disabled and request approval
for stopping/rerouting all relevant API instances or a narrow wider maintenance
window. Do not invent a distributed lock/epoch service or claim ordinary rolling
configuration updates provide a revocation barrier. Existing linked data is preserved;
temporary API availability impact must be explicit rather than hidden in a linking-only
availability promise. A request already past its final authorization point may have
committed; never delete its legitimate link as compensation for a pause.

```mermaid
sequenceDiagram
    participant Ops as Authorized release operator
    participant API as All API replicas
    participant DB as PostgreSQL
    Ops->>API: Stop new linking admission (readiness/gate)
    Note over API: Previously admitted handlers may still finish
    API->>DB: Drain/reconcile earlier transactions with Library-row lock barrier
    DB-->>API: Durable outcomes known; no outstanding authority-granting mutators
    Ops->>DB: Bounded Library-row ordered invalidation of unconsumed v2 rows
    DB-->>Ops: Invalidation verified
    Ops->>API: Verify complete build/schema/config, then enable new admission
    Note over Ops,API: Any failure keeps admission disabled; no legacy fallback
```

All in-flight legacy transactions are deliberately abandoned; users restart. Do not
derive a verifier from their state or migrate a legacy cookie into authority. Legacy
cookies are ignored and expire naturally; optionally remove only legacy names during
a separately reviewed safe cleanup, never the v2 slot from a late response.

Rollback disables v2 initiation/callback/staging/confirmation (review/cancel may also
be disabled), leaves legacy routes permanently retired, and preserves already linked
accounts and the additive schema. In-flight v2 rows become inert/expire. Re-enabling
after a rollback must cancel/invalidate outstanding v2 rows before accepting new
work; do not revive them. Prefer roll-forward corrections. **Never roll back to an
old binary that can accept legacy callbacks**, including the PR #8-only build, unless
the approved retirement protection remains enforceable for all requests. A linking
pause and compatible safe backend are the supported rollback, not restoring the flaw.
No down-migration/data destruction during operational rollback.

Old/manual/malicious requests may still put secrets in ingress URLs after retirement;
an application 410 cannot retroactively redact them. Historical records are not erased.
The guarantee is that the new application no longer **generates/accepts** that path
contract, not control over every request an external actor sends Railway.

## 12. Threat model and automated regression matrix

Every row is an acceptance obligation, not a claim of tests executed by this task.
U = Core/unit; P = real PostgreSQL integration; F = fake HTTP/OpenID provider;
B = browser E2E; L = logging capture; H = separately authorized synthetic hosted QA.

| ID / scenario | Security invariant and expected regression |
| --- | --- |
| T01 Leaked S | U/P/F: attacker knows S, state hash and T, supplies valid attacker Steam assertion but no B; no provider call/stage/link. Legitimate B still works. |
| T02 Derived/forged B | U/P: SHA-256(S), double-hash, V-as-cookie, malformed/padded cookie, duplicate names, random cookie and B from another row/user all fail. Assert V not derivable from S and no raw S/B persisted. |
| T03 Valid B alone | P: B with unknown S/T, wrong JWT or no JWT cannot review/confirm/link. Ordinary private APIs do not authenticate with it. |
| T04 Callback replay | P/F: same valid callback twice/in parallel stages once, creates no account; later callback after confirmation cannot relink. Cross-transaction replay fails exact signed return_to. |
| T05 Expired transaction | U/P: equality at the DB eligibility deadline rejects; test provider delay, Library/row and implicit unique-index waits for staging/confirm/handoff. Cookie presence, process clock skew and later cleanup do not change result. COMMIT acknowledgment may arrive later than the final eligible authorization point. |
| T06 Wrong Steam identity | U/P/F/B: identity/claimed_id mismatch, wrong endpoint/namespace/realm/return_to/signed coverage/nonce and false verification rejected. Valid different Steam account is shown for explicit review, never silently substituted after staging; mismatched expectedSteamId64 cannot confirm. |
| T07 Concurrent callbacks/confirm | P: synchronized barriers show one stage per S and one account insert/consumption per T. Two Libraries confirming same SteamID give one controlled winner, no partial consumption. No real provider. |
| T08 Concurrent initiation/tabs | P/B: latest committed initiation cancels previous live row; reverse response delivery leaves safe failure, not old-row acceptance. Delayed callback cannot stage replacement; no callback Set-Cookie erases newer B. Test different users sharing the browser. |
| T09 Cross-browser completion | P/B: two real cookie contexts; copied authorization/callback URL cannot stage in receiver even if authenticated as U. Receiver-derived cookie also fails; sender can complete if not replaced/expired. |
| T10 Application user switch | P/B: create as U, callback after login as W, forged/reused review handle and confirm W fail; neither account linked. Stale initiation/review responses cannot navigate/show old private results. Switch back U requires fresh review and explicit confirm, no auto-link. |
| T11 CSRF/Origin | P/B: hostile/missing/null Origin on JSON creation/confirm/cancel/review rejected even if cookies sent; anonymous create cannot set proof. Credentialed CORS only exact origins/methods. B additionally tests hostile HTML forms, GET handoff and query fallback. |
| T12 Cancellation/unlink races | P/B: provider cancel/error/setup_needed and malformed assertions never mutate, including in the original browser with B. Explicit cancel requires JWT U+B; creation replacement/unlink require their authenticated Library owner; pause invalidation is controlled maintenance. Cancel versus confirm and unlink versus confirm have lock-ordered outcomes. No cancel/receipt replay resurrects an unlinked account. |
| T13 Provider outage/partial failure | F/P: timeout, non-success, redirect, malformed/oversized/contradictory verification body and request cancellation produce no stage/consumption and preserve deadline. Simulate one-use provider nonce accepted before lost response/SQL rollback, and attacker-side nonce spending before the API verifies: no automatic repeated verification or claimed replay success; recover by durable review or fresh initiation. Verify no SQL lock held during provider wait. |
| T14 Unknown COMMIT | P: fault injection around creation/staging/confirmation commit and response loss; reconcile fresh durable state, no false rollback, duplicate link, reissued assumed-success proof or overwritten staged ID. Receipt retry after unlink/relink yields not-current. |
| T15 Logging leakage | L/F: synthetic canaries for raw/encoded S/B/V/state hash, T, assertions, cookies, JWT and authorization URL absent from application messages/scopes/exceptions/DiagnosticListener/traces and diagnostic request/response body capture. Assert allowed protocol transport separately, not a false ban on required JSON/header values. Fixed safe templates/status/duration and useful warnings remain. H inventories edge, ingress, exports, trace/error/support layers. |
| T16 URL contract | U/P/B: generated initiation/review/confirm/cancel (and B handoff) targets are constant with empty queries; no new `/start/{…}`; no JWT/B anywhere in navigation. Retired legacy routes return 410 and cannot call provider/write DB. Callback query/Steam URL exposure documented rather than falsely asserted absent. |
| T17 Cookie policy | P/B/H: exact host-only name/attributes/deadline, no Domain/fallback; callback/review/confirm/cancel never modify slot. Blocked cookies, different host names, cross-site redirects, local exception, HTTPS enforcement and expiry tested. |
| T18 Schema/transition | P: migrate previous schema with linked accounts + pending/consumed legacy rows; null/legacy/unknown version rejected; constraints, partial unique index, Library cascade, minimum runtime grants and no Data API access broadened. Existing account/game/cover data unchanged. |
| T19 Frontend tampering | U/B: fake success/review flags, missing/stale/other-user T, malicious authorization URL and lost sessionStorage cannot navigate off-allowlist/confirm automatically. Ordinary provider requests carry no application Authorization header. |
| T20 Replay/receipt lifetime | P: retries before expiry recover only current immutable receipt; expired/deleted receipt never authorizes a new insert. Replaying old confirmation after unlink and a new link of the same SteamID cannot match a new account UUID. |
| T21 Rollback/isolation | P/B: gate off, rollback artifact and mixed/old-client fixtures never accept legacy or new completion; linked data remains available. Production rejects all test-provider bypasses; no automated real network/database access. |
| T22 Resource bounds | U/P/F: body/query/field/verification limits fail without echo, cancellation propagation works; per-Library partial unique constraint and rate limits bound live state; cleanup cannot delete active rows or accounts. |
| T23 Same-browser injected return | P/B: attacker knowing S navigates the initiating browser to a valid assertion for an attacker Steam account; automatically attached B is not proof of intended Steam identity. At most stage that identity; no link without an authorized JWT U + B + expected-ID confirm request. Honest UI never auto-submits it; declined review and forged callback flags cannot confirm. Mistaken/misled wrong-account confirmation remains a user-intent risk, not just deliberate selection. |
| T24 Cookie fixation / session boundary | U/P/B: incoming chosen or old B is never adopted by creation; duplicate raw cookie occurrences fail before parser collapse; sibling-domain cookie tossing cannot set __Host- proof in supported browsers. Stolen JWT alone fails on an existing T without B, but a new flow initiated with that valid JWT can obtain its own B: explicitly test/document that this is not MFA. |
| T25 Database time / implicit waits | P: hold a competing SteamID unique insert, let the blocked transaction's deadline or JWT exp pass, roll back the blocker so insert becomes possible, and prove final recheck rolls all writes back. Test transaction_timestamp staleness, skewed API clock and late COMMIT acknowledgment separately. |
| T26 Pause/readiness barrier | P/B: toggling runtime true cannot activate PRs 1–4 shipped builds; Production test flags cannot bypass readiness. Simulate admitted callback/create/confirm across pause, drain/reconcile, failed invalidation batch and mixed replicas. Never reactivate until all granting handlers are quiescent and outstanding rows invalidated. |
| T27 Reconciliation snapshot | P: while a writer owns Library/row locks an unlocked reader sees old Pending; reconciliation must block or return LINK_OUTCOME_UNKNOWN, then reload the real committed/rolled-back phase. No missing/old row classification without that barrier. |
| T28 Constraint truth / immutability | P: explicit NULL violations must fail despite SQL three-valued logic; consumed legacy rows migrate; receipt NULL→UUID only at confirmation, then never changes. No orphan/half-pair/overflow/time-invalid v2 rows; no fake CHECK-based OLD/NEW enforcement claims. |
| T29 Bounded backchannel | F: verify UseCookies=false, no auto redirect/retry, ignored provider Set-Cookie, streaming/decompressed byte and header/body time limits, strict UTF-8, signed-field/duplicate parsing; no cookie/JWT forwarding or newly enabled body tracing. |
| T30 Review intent / framing | P/B/H: same T can be confirmed with valid JWT+B+expected ID without a prior review API call (no extra token is claimed); wrong expected ID fails on fresh and receipt paths. Honest UI never auto-confirms, displays full IDs/account context, and cannot be clickjacked in a foreign-origin frame via either direct route or SPA fallback. Backend does not claim to attest human consent. |

### Allowed transport versus forbidden collection

| Data | Necessary allowed transport/storage | Forbidden additional collection/disclosure |
| --- | --- | --- |
| S / authorization URL | Private initiation response body, browser→Steam request with encoded return_to, Steam→API callback query; only SHA-256(S) in SQL | Initial API path/query, frontend URL/storage, referrers, application diagnostics/body capture. Callback/Steam internal URL logging remains a separate external privacy risk. |
| B | API Set-Cookie after creation, browser cookie jar, browser→API Cookie header for proof requests | JavaScript, response JSON, any URL, Steam, SQL raw bytes, application/edge header capture or support exports |
| JWT | Existing Supabase client session, Authorization header to API only | New flow-local copies, URLs, cookies for linking, Steam/backchannel, diagnostics/header capture |
| T / U / L | T in private v2 JSON and per-tab metadata; U/L in private backend persistence and minimal current-account UI context | New provider fields/URLs, telemetry labels/scopes/body capture; no private Library identifier sent to Steam |
| V / state hash | Backend SQL fields/parameters and short-lived process memory | Client/provider messages, diagnostic EF parameter values, analytics or error strings |
| Verified SteamID / receipt | Private v2/status responses and bounded lifecycle persistence; the already linked SteamAccount persists by Domain rules | Raw assertion persistence, public result pages or diagnostic payloads; no automatic extra profile fetch |

No test should forbid a value in a message where the contract intentionally carries
it. Conversely, moving it into an allowed body does not authorize logging that body.
Keep the validation harness's synthetic sensitive payloads out of ordinary CI output.

### Adversarial cases: prerequisites, enforcement and residual risk

This table reviews the ten requested attack classes explicitly. IDs above remain
the regression traceability source; all original T01–T23 were reviewed, not dropped.

| Case / attacker prerequisites | Expected invariant / backend enforcement | Required tests | Residual risk |
| --- | --- | --- | --- |
| A01 Leaked S, hash/T and forged/derived cookie in a separate client | V cannot be recomputed as B; reject before provider call or write; no account link | T01/T02/T24 with correct Steam assertion and SHA-256(S)/V-as-cookie/wrong-row B | Stolen true B changes the prerequisites; S remains useful for targeting/DoS, not a credential substitute. |
| A02 Leaked S + valid attacker Steam assertion, ability to navigate victim browser | Cookie attaches in that browser, so staging may be influenced; only write-once identity accepted, no callback link; confirm requires U/B/expected ID | T06/T23/T30, two Steam identities, forged review flag, later callback cannot replace the first | User may be misled into confirming an unfamiliar numeric ID; injected first assertion can deny legitimate progress. No claim of intended-identity proof before review. |
| A03 Callback in another browser/profile, even JWT U | Missing/wrong B fails Core verifier and leaves phase unchanged | T09/T17 with real cookie jars and valid callback | Copying true B via privileged compromise defeats physical-browser distinction; this is bearer proof, not attestation. |
| A04 Account switches U→W before confirmation | JWT W cannot operate T bound to U/L even if B survives; U/L checked again under locks | T10, direct API attempts bypassing Angular and stale-response E2E | A previously admitted U confirmation may finish for U; switch-back U can review again before expiry. No immediate token revocation guarantee. |
| A05 JWT U + T/SteamID but not initiating browser secret | Existing-T confirm requires B; Origin alone and UI flags do not grant authority | T03/T24/T30, with/without prior review and forged Origin from HTTP client | Valid stolen JWT can initiate its own new flow and obtain B. Stronger stolen-session resistance needs approved step-up auth. |
| A06 Concurrent valid confirmations / competing Libraries for SteamID | Library→row ordering plus unique account indexes; one insert/consumption, rollback loser, final DB-time/exp check after implicit waits | T07/T25, barriers including unique-index wait released by rollback | Ordinary resource contention/429/timeouts; no distributed anti-DoS guarantee. |
| A07 Replay callback/confirm after success, including unlink/relink | Accepted phase rejects callback; same expected ID only returns current receipt UUID; never creates again from a terminal T | T04/T20/T30 on current, removed and replacement account UUIDs | Cookie/receipt retained briefly but non-authorizing after expiry/current-link change; status is not a T commit receipt. |
| A08 Expired/cancelled/replaced T with stale cookie/JWT | Fresh DB eligibility and signed exp, terminal checks and locks; cookie presence cannot revive; cancel is non-granting | T05/T12/T25 including exact equality, old response overwrites and cancelled staged rows | Commit acknowledgment can arrive after eligibility time; expired proven cancel may safely succeed, never link. |
| A09 Valid verification followed by SQL failure/disconnect | No SQL locks during provider I/O; stage/link only in atomic SQL; provider nonce may already be spent; reconcile under fresh lock barrier or report unknown | T13/T14/T27, before/after COMMIT faults and old MVCC snapshot | Restart may be required; no guarantee a second check_authentication of the same nonce will succeed. |
| A10 Read access to Railway/application telemetry | State-only disclosure cannot authorize a separate-client link; secret-flow table, redaction/no capture, strict callback verification and final confirm | T13/T15/T16/T29/H, signed-return_to tampering and attacker-side nonce spending despite full callback disclosure | Cookie/JWT capture, hosting/operator compromise, nonce-burning DoS, browser-mediated injection and privacy retention are separate risks; all-store exclusion not established by visible logs. |

### Synthetic review evidence and limits

Ephemeral Python standard-library reasoning exercises used only fresh synthetic
bytes/UUIDs, no application/server/database/provider/network. They checked the
specified contextual verifier against four proof cases, demonstrated original-browser
cookie attachment can permit staging without linking, and exhibited pre-write-only
expiry, entry-sampled flag and stale MVCC-snapshot counterexamples. Only boolean
outcomes/counts were emitted; no proof, state, identity or verifier values retained.
These exercises invalidate broad reasoning assumptions; **they do not execute Core,
PostgreSQL locking, browser policy or OpenID verification and do not satisfy T01–T30**.
The real regression suite and hosted gates remain future implementation obligations.

### Verification layers and execution rules

- **Unit:** encoding/verifier independence and comparison inputs, state machine,
  signed/nonce parsing, trusted URL construction, receipt rules and Angular generation
  guards. Statistical tests cannot prove entropy: assert independent RNG calls and
  known-input verifier separation; use a controlled clock for boundary tests.
- **PostgreSQL:** use disposable PostgreSQL 17 and migrated prior/fresh schema, real
  locks/indexes/transactions and at least two users. EF InMemory is not a substitute.
  Use synchronization barriers, not timing-only race assertions. Faults must cover
  definite rollback and possible committed outcomes independently.
- **Fake provider:** in-memory HTTP handler/loopback Steam fixture checks outgoing
  form and exact fixed verification destination, mandatory fields, invalid assertions,
  refusal of redirects, size/timeout/error behavior. Fake identity never bypasses
  Core/browser/owner/SQL checks. Test Production composition rejecting all fakes.
- **Browser:** extend small existing deterministic desktop/mobile journeys with
  explicit confirmation, two contexts/tabs, logout/switch, cookie rejection and stale
  responses. Browser automation must exercise actual cookie policy/native navigation,
  not only inject a Cookie header or mock Angular's next callback.
- **CI:** all five existing checks (`backend`, `postgres`, `frontend`, `e2e`,
  `native-runtime`) on the exact implementation SHA; retain `tools/ci/run-isolated.sh`
  deny-external/loopback/disposable-Postgres modes, guard unit tests, production fake
  gating, budgets and no hosted credentials. No calls to real Steam, Supabase data,
  Railway/Vercel APIs or owner's Library. Build, lint and migration tests included;
  zero silently skipped security tests. Do not weaken isolation/retry away failures.
- **Hosted QA (separate authorization):** dedicated synthetic identities/data and
  approved QA origins only. Prefer controlled provider fixtures; any real Steam smoke
  is separately authorized. Capture generated request-target classes (not publishable
  raw values), cookies and same-tab cross-site behavior in supported Chromium,
  Firefox and WebKit/Safari desktop/mobile policies, including third-party blocking.
  Audit ingress/container/proxy/log exports/trace/error instrumentation and obtain
  scoped Railway assurance for non-visible/internal/support stores. Synthetic canary
  results remain restricted evidence, not raw logs in issues/PRs. Missing/sampled/
  dropped/truncated logs do not prove absence. Reconcile evidence scope explicitly.

## 13. Acceptance criteria

Approval gates are separate from future implementation acceptance:

1. Owner approves Option A or B, explicit owner confirmation, temporary staged
   SteamID, cookie/concurrency/expiry/retention policy and cutover/rollback; authoritative
   specs are amended consistently before implementation. Any contradiction stops work.
2. Every new initial application request target is constant and secret-free; legacy
   generation/acceptance is removed on all replicas. No query-relocation-only workaround.
3. Independent B/V binding to T/U/L enforced by Core and proven by T01–T03, not merely
   ordinary browser-transfer tests. No raw S/B or provider assertion persisted/logged.
4. Strict trusted Steam verification and exact return_to remain mandatory; duplicates,
   unsigned required fields, stale nonce and provider/network/parse failures fail closed.
5. Callback only stages. JWT U + live B + immutable staged identity + explicit review
   precondition are mandatory for the one atomic link write; wrong-user confirmation
   fails regardless of client controls. One assertion and one link winner proven.
6. Expiry after waits, cancellation/replacement/unlink races, browser switching,
   reversed responses and unknown commits have the specified safe outcomes. No retries
   silently resurrect an old transaction or overwrite an existing link.
7. Additive EF migration and rollback/upgrade verification preserve existing linked
   accounts and unrelated data. No fake backfill or vulnerable compatibility window.
8. All T01–T30 obligations mapped to passing tests/evidence, all five CI gates pass,
   configured builds/lint pass, no unrelated dependency/architecture/security changes.
9. Privacy diagnostics remain useful without exposing secrets; browser support and
   residual callback/provider collection risk have explicit scoped evidence/approval.
   Do not label an unverified all-store exclusion as established by visible HTTP logs.
10. Activation/release requires separate approval; documentation completion or green
    local/CI tests alone do not close the production blocker.
11. Backend claims remain true when the test knows all of S/T/assertion, uses direct
    HTTP requests without Angular, skips review, spoofs Origin outside a browser,
    delays implicit SQL waits and reorders responses. Tests distinguish intended
    request preconditions from unobservable human consent or session theft.
12. Final PostgreSQL time/exp/nonce checks, NULL-safe constraints, lock-barrier
    reconciliation, nonmutating provider cancellation and full pause/drain/invalidate
    verification are implemented; a flag flip or an unlocked SELECT is not accepted
    as an equivalent shortcut.
13. PRs 1–4 cannot activate production v2 even with runtime/test flags set true;
    PR #5's readiness eligibility leaves runtime activation off by default. Retirement
    has no legacy re-enable switch. Cleanup introduces no unapproved background job.

## 14. Small reviewable implementation PRs (future authorization only)

Each implementation PR must include its own tests, acceptance mapping and no enabled
half-protocol. The split is a review plan, not permission to push/open/merge/deploy.

### Common merge criteria

Every code PR requires the five unchanged required CI contexts on its exact SHA:
backend build/Core/guard units, real disposable-PostgreSQL suite including migration
tests, Angular tests/config/lint/build, isolated desktop/mobile E2E and native-runtime.
No security skips, weakened guards, real hosted credentials or automatic deployment.
Each PR adds its relevant regressions and preserves previous applicable obligations.
No intermediate merge or option setting may expose a half protocol; backend
readiness policy returns false in ordinary shipped environments until PR #5. Tests
exercise real handlers via narrowly guarded test admission, never production bypass.

### PR #1 — approved authority alignment and unconditional legacy retirement

- **Dependencies/scope:** explicit owner approval for this bounded PR, authoritative
  amendments for the chosen v2 baseline and linking downtime. No v2 functionality.
  Retire all three old routes unconditionally; v2 paths return unavailable/not
  implemented, never fall back. Decouple existing-link status/preview/import/unlink
  from linking-only availability and preserve PR #8 privacy protections.
- **Files/components:** Product/Domain/Architecture clarifications as required,
  Feature 011/stabilization, API SteamController/SteamOptions/SteamLogPrivacy,
  SteamLinkActivation policy/composition, related endpoint/privacy/frontend fixtures
  and rollback runbook. Remove runtime reachability of legacy Core completion,
  never merely hide its UI. No global logging/auth or provider-configuration rewrite.
- **Migration:** none. Old database and future additive schema must both work for
  existing-account operations; application still has only the pre-v2 EF model.
- **Tests:** legacy POST/start/callback with valid state, derived cookie and valid
  assertion → constant 410, zero provider calls/writes/Set-Cookie. Setting every
  existing/new enable flag cannot restore them or enable v2. Linked status/import/
  unlink remain functional. Update disposable test setup to seed linked accounts
  for unrelated import regressions rather than relying on retired legacy linking;
  do not delete their isolation/domain assertions or create a new app seeding API.
- **CI/security acceptance:** common five checks plus T16/T21/T26 retirement subset,
  application diagnostic canaries, no migration dependency and no live accounts.
  All applicable legacy security intent is retained as retirement tests or explicitly
  mapped to future v2 tests, not silently lost with obsolete success expectations.
- **Independent merge:** **yes after explicit approval**, as a safe linking pause,
  not an operational release approval. It is the minimum rollback artifact. A
  deployment remains exposed while old replicas/callbacks are serving; retirement
  evidence must cover all replicas, not only merged source.

### PR #2 — additive EF state and private Core proof/lifecycle

- **Dependencies/scope:** PR #1 and owner-approved v2 decisions. Add independent
  verifier/state encoding, immutable/write-once phases and private Core use cases
  with Library→row ordering, DB-clock eligibility, receipts and reconciliation.
  No publicly usable v2 handlers or background worker.
- **Files/components:** Core SteamLinkRequest/SteamIntegrationRules/
  SteamIntegrationService/result/error contracts, DbContext mappings; relevant Core
  and PostgreSQL test fixtures. Remove obsolete unchecked legacy-completion entry
  points; Core cannot create a link from state or a browserMatched boolean alone.
- **Migration:** the single additive EF migration plus designer/snapshot in section
  9, including partial unique Library index, binary verifier and NULL-safe/versioned
  checks. No SteamAccount/library-entry schema change, fake backfill or auth.* edit.
  Applying it to a real service is separately authorized, not part of merging it.
- **Tests:** fresh/previous-schema upgrades with legacy pending/consumed and existing
  links, required-field NULLs, valid/invalid UInt64/time/phase combinations, contextual
  proof/fixation cases, cancellation/replacement, receipt transitions, same/different
  Library races, implicit unique-index wait expiry and stale-MVCC reconciliation.
- **CI/security acceptance:** common checks plus Core/P coverage of T01–T08,
  T12/T14/T18/T20/T24–T28 as applicable. Runtime linking remains unconditionally
  ineligible regardless of a true flag; PR #1 rollback works with migrated schema.
- **Independent merge:** **yes after PR #1**, with proof/use cases private and
  readiness false. A migration/schema failure keeps linking unavailable, never v1.

### PR #3 — complete API/OpenID boundary, still ineligible for activation

- **Dependencies/scope:** PRs #1–#2. Implement A only: authenticated creation/review/
  confirm/cancel, nonmutating callback verification/staging, cookie transport,
  Origin/CORS/preflight/rate/limits, safe errors, strict backchannel and unknown
  outcomes. Integrate unlink invalidation. No automatic confirmation or handoff B.
- **Files/components:** API SteamController/SteamContracts/SteamOptions/
  SteamLinkBrowserBinding/SteamClient/SteamLogPrivacy/SteamLinkActivation, Program,
  existing fake client/test-mode composition and API/Core/HTTP test fixtures.
- **Migration:** none additional; requires PR #2's exact migrated schema. Schema
  mismatch is a linking-specific unavailable state. Any discovered schema change
  returns for review rather than being patched manually.
- **Tests:** endpoint attacks independent of Angular, real PostgreSQL proof/owner/
  phase/expiry/unique/commit-fault behavior; fake HTTP signed/nonce/redirect/cookie/
  body-bounds tests; OPTIONS, missing/null Origin, errors and privacy captures.
  Production composition refuses every test admission/provider bypass, including
  combinations of true flags. E2E test admission must be Development/Test + existing
  explicit guards + loopback/isolation, not ordinary Staging or Production.
- **CI/security acceptance:** common checks, all relevant backend T01–T30; no
  shipped route usable while readiness false even if runtime flag=true. Code may
  exercise complete API protocol in disposable test hosts only.
- **Independent merge:** **yes after PR #2**, with hard readiness false and no
  vulnerable legacy acceptance. Merge does not authorize exposing a backend-only
  journey to users while the required trusted review UI is absent.

### PR #4 — trusted Angular confirmation and browser boundaries

- **Dependencies/scope:** PR #3 contracts and complete backend. Implement feature-local
  per-tab metadata, auth-generation cleanup, trusted navigation, explicit full-ID/
  current-account review, cancellation/restart/unknown-response handling, no fallback.
- **Files/components:** Angular Steam service/page/template/tests, smallest auth
  lifecycle integration, token-boundary/cache configuration tests, E2E journeys;
  proposed frontend response-header rules in `src/frontend/vercel.json` or the
  approved host-equivalent for review-route/SPA framing protection. Repository
  header changes are reviewed code; applying hosted settings/deploying is separate.
- **Migration:** none; PR #2 remains the only planned schema addition.
- **Tests:** T08–T11/T17/T19/T23/T24/T30 browser/UI subsets, two cookie contexts,
  changed accounts during pending responses, unknown COMMIT/reload recovery, blocked
  cookies, malicious URL/flag, no JWT to Steam, actual cross-origin framing attempt
  and route/fallback response headers. Backend test suite must still pass without
  trusting any Angular check. Local host headers are test evidence, not proof of
  production-hosted browser support.
- **CI/security acceptance:** common checks, no auto-confirm/preview/import; all four
  v2 JSON calls credentialed but no global withCredentials change. Hard shipped
  readiness remains false; isolated E2E exercises the entire gated test journey.
- **Independent merge:** **yes after PR #3**, but disabled linking remains the safe
  public state; old clients show unavailable/refresh rather than invoking v1.

### PR #5 — readiness eligibility, complete evidence and release runbook

- **Dependencies/scope:** PRs #1–#4, decisions recorded as approved, all T01–T30 mapped
  to evidence. Add approved opportunistic cleanup if not already covered in Core,
  finalize authority/readiness policy, metadata-only pause/drain observation and
  fault-tested invalidation/rollback instructions. No new cleanup worker under the
  selected baseline; a different retention requirement needs architecture approval.
- **Files/components:** final SteamLinkActivation/composition and small Core cleanup
  integration/tests, source specifications, security/deployment/QA evidence docs,
  existing browser/privacy/CI fixtures as needed. No infrastructure/dependency
  addition or changes to branch protection/required check settings.
- **Migration:** none beyond PR #2. Run old-schema/migrated-schema rollback checks;
  don't generate a down-migration or erase accounts/receipts for convenience.
- **Tests:** all T01–T30, pause with admitted work/unknown commits, interrupted
  invalidation, failed schema checks, flags on incomplete/mixed builds, opportunistic
  owner-scoped purge, rollback preserving links/import data. Five CI gates on exact
  final SHA plus separately authorized synthetic hosted browser/header/collection
  evidence. Real Steam smoke remains an independent permission/data-scope gate.
- **CI/security acceptance:** only the completed policy may become readiness=true;
  SecureLinkingEnabled stays **false by default** and is never auto-set in code/CI.
  Enabling an isolated QA deployment for evidence requires separate authorization.
  Unverified all-store exclusion is not labeled a pass; privacy acceptance or an
  explicitly approved scoped residual risk is needed for activation, not inferred
  from missing visible logs. If evidence cannot be obtained, keep the gate off.
- **Independent merge:** **yes after PR #4 and acceptance review**, with runtime
  default off. **Not activation/deployment approval.** Any later activation needs
  the complete build/schema/replica/drain checklist and owner release authorization.

Expected files (future edits, not changed by this task):

- `src/backend/GameLibrary.Api/Controllers/SteamController.cs` and `Steam/SteamContracts.cs`.
- `Steam/SteamOptions.cs`, `SteamLinkBrowserBinding.cs`, `SteamClient.cs`,
  `SteamLogPrivacy.cs`, proposed `SteamLinkActivation.cs`, `Program.cs` for gates, fixed routes, cookie/CORS/privacy and
  provider boundary. `Steam/E2eSteamClient.cs` and existing test-mode composition
  must track new assertion contracts without weakening Production gating.
- `src/backend/GameLibrary.Core/Steam/SteamIntegrationService.cs`,
  `SteamIntegrationRules.cs`, `SteamLinkRequest.cs`, use-case result/exception contracts;
  `Data/GameLibraryDbContext.cs` and **new generated EF migration + designer/snapshot**.
  Do not rewrite `20260826034524_AddSteamIntegration.cs` or change SteamAccount schema.
- `src/frontend/src/app/features/manage/steam/steam.service.ts`, `steam-page.ts`,
  `steam-page.html`, feature tests and the necessary small auth-change integration
  around `core/auth/auth.service.ts`; preserve token-interceptor API-origin limits.
  `src/frontend/e2e/app.spec.ts`, test fixtures, and cache/telemetry rules if required
  to prove these sensitive flows are excluded. `src/frontend/vercel.json` (or approved
  host-equivalent) supplies response-header framing protection for review/SPA routes;
  it is not modified or deployed by this review.
- `tests/backend/GameLibrary.Core.Tests/` Steam rules and
  `tests/backend/GameLibrary.IntegrationTests/` Steam endpoints/client/log/privacy/
  migration fixtures; no weakening of `.github/workflows/ci.yml` or `tools/ci/` guards.
- Approved source specs, deployment/privacy guidance and future QA receipts must
  distinguish this replacement from historical PR #8-only mitigation assumptions.

## 15. Remaining risks and approval decisions

### Proposed defaults requiring explicit approval

1. **A plus staged callback and explicit JWT owner confirmation.** A callback-only
   variant with independent B would fix known-state proof derivation, but would still
   link original U after an unnoticed application account switch. It is not the
   recommended equivalent. Do not silently drop confirmation for implementation ease.
2. **One live transaction per Library + one cookie slot**, including cross-browser
   replacement and fail-closed out-of-order response handling, instead of parallel
   independent tab completion. This deliberately favors simple bounded authority.
3. **v2 namespace, complete legacy retirement, additive migration and safe linking
   downtime**, not compatibility acceptance; separate linking gate preserves accounts.
4. **Existing 1–60 minute configured deadline** (default ten), ten-minute nonce age
   with 60-second skew, body/query/provider bounds and strict field/signature policy.
   Confirm real Steam interoperability before release; any necessary adjustment
   needs review, not a permissive parser fallback.
5. **Temporary staged SteamID + receipt** in the existing request table, backend-only;
   proposed retention eligibility at expiry +24h, owner-scoped opportunistic cleanup
   and **no inactivity deletion SLA**. A worker/strict retention alternative is a
   separate Architecture amendment, not assumed approved.
6. **Supported hosted browser/origin matrix and provider-data risk acceptance.** If
   cross-site cookies are blocked in a required environment, do not ship a bypass;
  request an explicitly approved same-site hosting change or keep linking unavailable.
7. **No extra confirmation token or mandatory review-call receipt**, strict signed
   exp/DB-clock final eligibility, nonmutating provider negative responses, fail-closed
   readiness/drain protocol and full-ID/non-frameable trusted UI. These clarify the
   authorization/intent boundary; approving them does not approve an MFA claim.

### Risks not claimed solved

- Steam authorization and callback URLs still carry S/assertion data; browser history,
  extensions, Steam and hosting internals may observe them. Independent B plus final
  JWT protects existing-flow link authorization against state-only disclosure,
  **not** a stolen valid session creating its own new flow, or joint proof/session theft.
  HttpOnly is not a defense against full same-origin XSS issuing authorized actions.
- SameSite=None increases the importance of exact Origin + Bearer checks; third-party
  cookie blocking can prevent completion. No extra proxy/domain/service is authorized.
- A user may mistakenly or under misleading circumstances confirm the wrong Steam
  account after seeing an unfamiliar ID; the protocol cannot infer their intent.
  Framing protection reduces clickjacking, not phishing or same-origin compromise.
  A shared browser/session or stolen JWT has the
  usual authentication risks. No continuous global Supabase session revocation is added.
- A log-reading attacker who can navigate the *initiating browser* to an injected
  valid callback may get B attached automatically. Independent B alone is not an
  intended-Steam-account check. Immutable staging and explicit authenticated identity
  review prevent automatic linking by the honest application, not a malicious authorized
  client, misleading human confirmation or same-origin XSS issuing the confirm request.
- Timing/rate-limit denial of service and malicious cancellations with stolen S+B
  remain possible. Limits are defense in depth; existing in-process rate limiting
  is not a distributed multi-replica quota guarantee.
- Historical edge retention is not undone. Logs may retain private correlatable
  UUIDs/IP/UA/timing metadata. No full provider privacy certification is claimed.
  V2 also does not retroactively certify that every existing legacy link was free
  from substitution; no abuse evidence was obtained and no real links inspected.
  Any incident investigation/remediation would need separate scope/authorization.
- Hosted absence and non-visible provider stores remain unverified; callback data
  handling needs assurance or an explicit scoped residual-risk decision, not an
  assertion that “queries are private.” An absolute all-URL/all-store prohibition
  remains incompatible with the evidenced Steam transport until separately resolved.

## 16. Implementation-readiness verdict

**NO-GO for starting application PR #1 under the current authorization.** This user
request authorizes security/specification review only. Technical **conditional GO**
for the bounded PR #1 after the owner explicitly authorizes implementation and the
required authoritative amendments/linking pause. Its retirement work does not need
production browser/provider evidence to begin locally; it must still pass its own
CI/security criteria before merge and requires separate deployment authorization.

Before full v2 implementation, record owner acceptance of D01–D11 (or an explicitly
reviewed alternative), particularly staging/confirmation semantics, state-disclosure
influence limits, one-slot concurrency, no extra token, numeric-ID review, retention
without an inactivity SLA and downtime/drain handling. If a stricter privacy, consent,
session-theft or retention guarantee is required, the selected design does not meet
it automatically: return for product/architecture/security decision, not a code shortcut.

No unresolved algorithm choice remains delegated to the implementer under the
recommended baseline. Remaining uncertainty concerns owner acceptance and empirical
interop/hosting/browser evidence. **NO-GO for production activation/release** until
the complete PR sequence and acceptance evidence, explicit residual-risk disposition
and owner release authorization exist. A local spec, ephemeral reasoning exercise
or future green CI alone cannot close the blocker.

**Task completion boundary:** only this existing local specification is revised. No application
code, migration, service/configuration change, deployment, real authentication, user
data access, push or PR is part of this task. No implementation acceptance tests or
hosted security gates are claimed run. The ephemeral reasoning exercises above are
not application regression results. Production release blocker remains **OPEN**.
