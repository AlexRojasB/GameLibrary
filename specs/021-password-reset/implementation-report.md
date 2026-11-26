# GL-08 — Password reset, implementation review

Date: 2026-11-25 (local). Branch `feature/password-reset`, created from freshly
fetched origin/dev `d7554317ffb7b5cce6e06fd622a5996e69d3ee10` (merged PR #21).
The initial implementation was local and uncommitted. The owner's subsequent
authorization permits scoped review/corrections, commit/push and a PR targeting
dev, followed by one standard required CI run. This is not release approval.

## Implemented flow

- Sign in links to `/forgot-password`. The labelled email form calls the existing
  SDK's resetPasswordForEmail with a fixed same-origin `/reset-password` callback.
  Existing/missing-account and account-related 4xx outcomes share one neutral
  confirmation. Rate limits/network errors have safe retry text, no raw provider
  messages or account-existence claims. Duplicate requests are blocked.
- A separately namespaced SDK client uses supported PKCE and SDK-owned storage.
  The existing normal client retains implicit sign-in/signup/session restoration.
  No custom token system, storage adapter, backend endpoint or dependency.
- A single code on the exact callback path is transiently consumed before
  bootstrap; query/fragment values are removed before Angular/router/normal SDK
  initialization. Reject extra/duplicate parameters, URL error text, implicit
  tokens and arbitrary destinations. A fresh successful exchange AND matching
  PASSWORD_RECOVERY event are necessary; normal/restored sessions and URL type
  flags never authorize updating. Bind the local grant to original expiry/user;
  check current SDK identity and grant again before updateUser.
- New-password form reuses the existing required/minimum-six-character validation,
  GL-07 hidden-by-default Show/Hide behavior, autocomplete=new-password, native
  non-submit button, styles and responsive layout. No password copied into extra
  component state. Pending updates are read-only and duplicate submits blocked.
- Invalid/expired/used links offer another email. Failed/uncertain updates fail
  closed, erase the draft and offer sign-in/new-link inspection rather than
  claiming success or automatically replaying a mutation.
- Acknowledged update clears password/visibility state and signs out **only the
  isolated recovery client** via supported SDK local sign-out. The normal session
  is never signed out/replaced/cleared, including another account or a new login
  while cleanup was pending. Failure
  offers cleanup-only retry, never another password update. Return to fixed Sign
  in with confirmation only after local cleanup succeeds. No global logout or
  immediate access-JWT revocation claim. Destruction suppresses late navigation;
  abandoned recovery cleanup is best effort.

## Exact change areas

- `src/frontend/src/app/core/auth/password-recovery-client.ts`: isolated SDK
  provider, one-shot callback handoff, fixed secure-origin redirect helper and
  status-only error inspection. `src/main.ts` invokes callback scrubbing before
  bootstrap; `src/index.html` adds no-referrer metadata.
- `features/auth/forgot-password/` and `features/auth/reset-password/`: two new
  small feature-local forms; share existing Login SCSS rather than refactor forms.
- `features/auth/login/login.html` / `login.ts`: Forgot password link and safe,
  non-sensitive navigation-state success message. Existing sign-in/GL-07 unchanged.
- `core/auth/auth.guard.ts`: a display-only recovery-completion exception on the
  login route prevents redirecting away from confirmation when an unrelated normal
  session remains active. No flag/query grants password-update authority. Default
  login/register guard behavior is unchanged; AuthService and its existing tests
  are identical to origin/dev.
- `app.routes.ts` / `app.ts`: two public recovery routes and guest-layout handling.
  `app.html`: one stable outlet while shell chrome changes. This required local
  correction avoids re-creating the callback component/consuming its one-shot code
  twice during initial navigation or losing confirmation on return to Sign in.
- Tests: callback boundary, request/update components, SDK cleanup, stable outlet,
  and provider-mocked desktop/mobile E2E. The shared Supabase mock gains the three
  used SDK methods and meaningful subscription removal. Existing assertions remain.
- `specs/021-password-reset/spec.md`, configuration note and this report record
  scope, operation prerequisites and verification. No higher-authority Domain change.

## Local validation

| Check | Result |
| --- | --- |
| Initial focused recovery + existing auth suites | **79 PASS**, 8 files, before final destruction/outlet regressions |
| Complete frontend suite before publication security review | **612 PASS**, 43 files; historical result, before the corrections below |
| Final publication review: affected recovery/auth/shell suites | **89 PASS**, 9 files, including session-preservation and callback-alias regressions |
| Focused recovery E2E | **4 PASS**, two scenarios each on desktop Chromium and emulated Pixel 7; explicit retries=0 |
| Angular production build before publication review | PASS; initial 690.76 kB, estimated transfer 165.24 kB; historical result |
| Final publication production build | PASS; initial 690.65 kB, estimated transfer 165.42 kB |
| `npm run lint` | PASS |
| `npx tsc --noEmit -p tsconfig.spec.json` | PASS; production/template types also checked by Angular build |
| `git diff --check` | PASS |

Implementation-phase commands from `src/frontend`: `npm test -- --watch=false`,
`npm run build`, `npm run lint`, `npx tsc --noEmit -p tsconfig.spec.json`, and
`npm run e2e -- e2e/password-reset.spec.ts --retries=0`.

Final publication review repeated build/lint/types and the focused E2E, then ran
only affected unit suites with:
`npm test -- --watch=false --include='src/app/features/auth/password-reset.spec.ts' --include='src/app/features/auth/password-visibility.spec.ts' --include='src/app/features/auth/login/login.spec.ts' --include='src/app/features/auth/register/register.spec.ts' --include='src/app/core/auth/**/*.spec.ts' --include='src/app/app.spec.ts'`.

E2E intercepts only Supabase HTTP, not the application recovery boundary. It uses
the real installed SDK to generate a PKCE verifier/challenge (asserted via SHA-256),
exchange a synthetic code/emit PASSWORD_RECOVERY, authenticate updateUser, clear
SDK state and sign out. It asserts neutral known/missing email output, rate-limit
feedback, fixed redirects, callback URL removal, password validation/toggle/native
keyboard behavior, no horizontal overflow, one update despite cleanup retry, success
on Sign in, cleared SDK session/verifier, and rejection of replay/expired links.
Unit tests additionally cover ordinary/restored/different-session rejection,
expiry/identity changes, safe network failures, cleanup failure, duplicate
submits, delayed destruction and no replay after uncertain updates.

The unchanged E2E harness starts its local API with a syntactically valid loopback
ConnectionStrings__E2E for its existing isolation guard, but these tests never call
database-backed endpoints or reset/seeding. **No PostgreSQL instance was started,
no migration applied, no real recovery email/provider or hosted account used.**
No local backend suite, deployment or diagnostic campaign was run. Standard
required CI is authorized only for publication of the final scoped SHA.

### Concrete failures resolved without relaxed checks

- Initial lint caught a useless final assignment to a transient variable; remove
  the unused assignment while retaining subscription disposal.
- HTTP mock initially expected only the password in updateUser's transport body;
  installed SDK source proved it also sends null code_challenge/method. The exact
  expectation now includes those fields; production payload intent is unchanged.
- Browser tests exposed the existing dual-outlet component recreation issue.
  Fixed with one stable outlet and added a regression, not callback retries or
  persistent authorization state.
- The initial logout-POST-count premise was wrong: installed SDK clears local
  session even when its POST fails, so retry has no credential to POST again.
  The test now asserts exactly one logout POST, exactly one password update,
  required retry/return confirmation and cleared session/verifier. No remote
  revocation claim or extra retained credentials is substituted for SDK semantics.

## Configuration, warnings and remaining verification

See `supabase-recovery-configuration.md` for exact development/recorded QA
redirects, the standard ConfirmationURL template, same-browser/latest-link
constraints, SDK cleanup semantics and pre-rollout privacy gates. Production
origin/Auth configuration is not specified in the repository; the owner must
supply it before release. No remote allowlist, Site URL, SMTP/template, CAPTCHA,
password-policy or delivery/redirect behavior was inspected or changed. Real
email delivery and redemption remain **unverified**, as requested.

PKCE callback queries necessarily reach the frontend hosting edge before JS. The
app scrubs them locally and uses no-referrer; operator verification/suppression
of host/proxy request queries, analytics and session replay is a release gate,
not something frontend code can certify. Local sign-out does not prove remote
refresh-token revocation after an unknown logout outcome, nor recall issued JWTs.

Existing warnings: Picker SCSS 4.13 kB vs 4.00 kB (130 bytes over); Vite future
native-loader/CommonJS config warning; Vitest test.poolOptions deprecation.
No warning suppression or budget/tooling changes. Mobile is emulation, not a
physical-device or manual screen-reader/password-manager audit.

## Publication security correction / review

The requested review found a concrete issue in the original cleanup: it called
endLocalSession on the normal AuthService even when that session belonged to a
different account or had changed during recovery. Remove that call/helper and
its superseded tests, restoring AuthService and its upstream tests exactly to dev.
The recovery component no longer imports or injects the normal AuthService. End
only the recovery SDK session; permit the fixed Sign in confirmation as a cosmetic
guard exception, not an authentication or recovery capability. New tests assert
normal SDK sign-out/sign-in are never called and the actual restored normal
session is unchanged for same-account, different-account and changed-during-cleanup
cases, as well as invalid callbacks, cleanup retry and destruction. The existing
mocked E2E now carries an unrelated ordinary session through success and replay
and asserts its stored value remains identical. Upstream assertions are unchanged;
the old new-helper tests expecting normal sign-out were replaced by stronger
normal-session preservation coverage because that helper was the bug.

Also scrub and reject Angular-recognized noncanonical recovery paths (encoded,
trailing slash and matrix variants). Without this correction the early exact-path
check could leave injected URL credentials for the normal SDK to interpret before
the recovery screen rejected the callback. Add four precise boundary regressions
and retain all canonical, duplicate, malformed, mismatch and non-recovery tests.
The E2E additionally requires no Referer header on every mocked Supabase request.

Neutral confirmation, the fresh PKCE exchange + matching recovery event, bound
identity/expiry, one-shot callback, stable outlet, fixed redirects, early URL
removal and default sign-in/signup/session-restoration behavior were reviewed.
The guard flag is ignored outside login and cannot authorize a password update.
Only the scoped feature/security tests and build/lint/type checks are rerun locally;
the normal required workflow validates the complete final SHA once after publishing.

No unresolved implementation/product decisions. Pending operator configuration,
production-origin selection and real-delivery/privacy checks are not passed
gates or deployment approval. Backend/database/dependencies/Steam/QA configuration
remain unchanged; pre-existing untracked QA files are untouched. Publish only the
scoped GL-08 implementation/tests/specification/report/configuration note. Record
final SHA/CI results in the PR, without another source commit just for CI evidence.
No merge, deployment, remote settings/email, Steam/QA change or migration. Stop for review.
