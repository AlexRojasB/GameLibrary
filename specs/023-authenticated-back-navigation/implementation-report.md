# Authenticated Back navigation — implementation review

Date: 2026-11-26 UTC.
Branch: `feature/authenticated-back-navigation`.
Base: freshly fetched `origin/dev`
`5e06c2529794186b8d7935ce0606f847d906961a`.
Status: **implementation validated locally; scoped publication authorized, not deployed**.
The initial work was unstaged/uncommitted. See Publication review for the subsequent
authorization; final commit/PR/CI receipts are recorded in the PR, not inferred here.

## Working-tree preservation

Inspected the original `release/qa-5e06c25-without-gl08` working tree first. Its
experimental edits/deletions, untracked files and QA deployment report were not
reset, stashed, cleaned, merged or copied into this fix. Created a separate worktree:

`/Users/mac/Documents/Projects/GameLibrary-Authenticated-Back-Navigation-2026-11-26`

The fetched dev commit happens to be the same original candidate SHA, but the new
branch was created from the **clean fetched commit**, not the experimental branch
contents. A private pre-task manifest fingerprints all 32 changed/untracked paths
in the original checkout, including its report; final comparison confirms they
are unchanged and the original checkout still has its experimental branch.

Read Product, Domain, Architecture, Feature 002 authentication, GL-08 recovery and
GL-13 Back specifications. This task's narrow scope is recorded in adjacent
`spec.md`; no architecture, domain or schema amendment was needed.

## Investigation / observed root cause

Before changing production code, added and ran the exact browser regression
against the clean base, using real Angular Router, AuthService and recovery SDK
with mocked local Auth/API HTTP operations:

1. Request synthetic recovery (intercepted; no email).
2. Load the synthetic callback; the existing pre-bootstrap scrub removes its query.
3. Exchange through the real SDK's mocked PKCE endpoint, update password and end
   only the isolated recovery session.
4. Reach Sign in with the completion confirmation; explicitly sign in and reach Home.
5. Use actual browser Back (`page.goBack`).

**Reproduced failure:** one visible Sign in heading where zero was expected.
Immediately before that failing assertion, checks confirmed the ordinary session
was **unchanged**, unexpired, and the mocked authenticated `/auth/me` boundary
returned **200 with the expected subject**. This is a demonstrated presentation /
history defect, not demonstrated session loss. It does **not** assert that the
tester's particular hosted JWT/session was inspected; no real account, email,
recovery link or QA reproduction was used.

The router/history chain on the base is:

```text
Forgot password → reset callback (scrubbed)
  --replace--> /login { history.state.passwordResetComplete: true }
  --successful sign-in pushes--> Home
  --Back--> /login { restored completion flag }
```

- `ResetPassword.finish()` correctly preserved normal auth and replaced the
  callback, but passed its display exception in persistent navigation `state`.
- Angular restores that state into `currentNavigation().extras.state` on Back.
  `guestGuard` checked the restored flag **before** its authenticated-user redirect
  and therefore allowed Sign in. This explains why a valid session could coexist
  with a Sign in screen. The stable outlet and GL-13's in-app Back ledger were not
  causing an SDK exchange or logout on this traversal.
- `Login.onSubmit()` used a push navigation to Home, leaving its just-submitted
  authentication entry available to Back. With ordinary login, the existing guest
  guard already rejects it, but retaining it is avoidable history churn.
- Reviewed `AuthService`: normal state is SDK-derived, initialization is awaited
  by both guards, and a revision check prevents stale initialization from replacing
  a newer event. No restoration/session code change was necessary. Deferred
  restoration and restored direct-link regressions pass with those paths unchanged.

## Small implementation / safety boundary

Exactly **three production files** change:

| File | Change |
| --- | --- |
| `src/frontend/src/app/features/auth/reset-password/reset-password.ts` | Completion uses Angular's transient `info: 'passwordResetComplete'`, retaining `replaceUrl: true`, instead of persistent `state`. |
| `src/frontend/src/app/core/auth/auth.guard.ts` | Immediate completion display exception reads `info` only. Ordinary authenticated login/register navigation still returns the existing fixed Home UrlTree, after initialization. Legacy persisted flags are ignored. |
| `src/frontend/src/app/features/auth/login/login.ts` | Confirmation presentation reads the same transient info. Successful explicit login navigates to fixed Home with `replaceUrl: true`; validation/failure/submission behavior is unchanged. |

Angular's documented [`NavigationExtras.info`](https://angular.dev/api/router/NavigationExtras#info)
is transient to a navigation, unlike `state` written to browser history. No custom
marker service, token, persistence, navigation stack, return URL, global popstate
interceptor or history rewriting framework is added. The info string conveys
**presentation only**, never recovery or password-update authority.

Immediate completion Sign in remains available even when an unrelated ordinary
session exists, as required by GL-08; it does not sign that account out. On later
Back/Forward/reload there is no restored exception, so the normal guest guard
applies **before Login activation**. Successful login replaces only its current
authentication entry. Previous legitimate screens and Forward remain usable.
In the exact email-request/callback regression, Back returns to the preceding
**Forgot password** screen (public under GL-08), not Sign in; in a Health→login
sequence it returns to Health. This is intentionally not an always-Home/global
skip policy that would erase legitimate history.

Unchanged: normal AuthService/SDK storage/refresh, isolated recovery client,
callback capture/scrubbing/one-shot exchange, recovery grant validation, update and
cleanup, protected authGuard, route definitions, stable shell/outlet, GL-13 ledger,
normal sign-out, register behavior, backend, PWA manifest/service worker and Steam.
No new dependency, package/lockfile modification or database artifact.

## Regression verification

| Coverage | Result |
| --- | --- |
| Affected auth/guard/recovery/login/register/home, shell and GL-13 unit/router tests | **149 PASS / 12 files** |
| New real-router suite | **6 PASS**, using actual route guards/Login/ResetPassword, MockPlatformLocation entries and mocked SDK boundaries |
| Focused browser E2E | **24/24 PASS: 12 desktop + 12 Pixel 7-emulated mobile**, retries 0 |
| Production Angular build | **PASS**, initial **693.91 kB**, estimated transfer **166.09 kB** |
| Configured application/spec TypeScript checks | **PASS** |
| Application/template lint and explicit changed E2E/config ESLint | **PASS** |
| Whitespace, scope and original-worktree fingerprint checks | **PASS** |

Coverage includes the exact recovery→explicit new-password sign-in→Home→Back
sequence and valid normal session, no extra exchange/update/cleanup, empty recovery
session/verifier afterward, normal sign-in replacement, legacy persisted flags on
Back/reload, immediate completion with an existing session, delayed restoration
without Login activation, restored protected/direct guest links, failed login,
signed-out access and logout→Back/Forward guards. Multiple Back/Forward cycles
retain history length/Forward and no additional push; legitimate Library/Manage
history and existing contextual Back behavior remain covered. A DOM observer
asserts no unnecessary Login component insertion during authenticated traversal.

`playwright.auth-navigation.config.ts` starts **only the existing Angular E2E
frontend**, because every Auth/API operation in these selected tests is mocked.
It does not start the broad standard harness's backend or fake integrations, need a
database connection, or enable/call Steam. Existing main Playwright configuration
is untouched. New API fixture reuses the existing normal E2E auth boundary; recovery
continues through the **real Supabase SDK PKCE** against intercepted HTTP. Session
checks verify subject/expiry, retained ordinary-session bytes, authenticated API
acceptance and normal app interceptor use, **not real hosted signature validation**.

Commands from `src/frontend`:

```sh
# Baseline before production edits: expected regression failure on visible Login.
npx playwright test --config playwright.auth-navigation.config.ts \
  e2e/password-reset.spec.ts --grep 'recovery then sign-in' --project chromium-desktop

# Final passing checks:
npm test -- --watch=false \
  --include='src/app/core/auth/**/*.spec.ts' \
  --include='src/app/features/auth/**/*.spec.ts' \
  --include='src/app/core/navigation/back-navigation.spec.ts' \
  --include='src/app/app.spec.ts'
npx playwright test --config playwright.auth-navigation.config.ts
npm run build
npm run lint
npx eslint e2e/authenticated-back-navigation.spec.ts e2e/password-reset.spec.ts \
  e2e/fixtures/authenticated-navigation.ts playwright.auth-navigation.config.ts
npx tsc --noEmit -p tsconfig.app.json
npx tsc --noEmit -p tsconfig.spec.json
git diff --check
```

## Warnings, setup issues and limits

- Existing Picker SCSS budget warning remains: **4.13 kB vs 4.00 kB**, 130 bytes
  over. Existing Vite native-loader/CommonJS and Vitest poolOptions deprecation
  warnings remain. No budget, dependency, timeout or assertion suppression.
- First dependency install timed out; the next exposed permissions in the existing
  user npm cache. Installation succeeded using a fresh private temporary cache,
  without modifying the user's old cache or dependency manifest/lockfile.
- An intermediate browser run found a **test observer issue**: full-document Back
  discards a window-only instrumentation counter. Fixed the test to install its
  observer on every document and retain only a nonsecret test counter in tab
  sessionStorage. Missing observation is not accepted as zero; assertions remain.
  The final 24-test run passed without retries, distinct from the initial failures.
- The repository has no standalone E2E TypeScript configuration. A supplementary
  explicit-file `tsc` attempt (with TS6's required `--ignoreConfig`) could not run
  with the locked dependencies because optional **`@types/node` is absent**.
  No type stubs, suppression or dependency were added merely for it. Configured
  **application and unit-spec** type checks pass; Playwright transpiled/executed
  all focused E2E files, and explicit ESLint passes. Standalone E2E static type
  checking is **not claimed**.
- **Native Back gesture in an installed PWA is not verified.** Browser Back/Forward
  and mobile viewport/touch emulation are not Android/iOS installed standalone,
  OS lifecycle/BFCache, Safari/Firefox, old installed-shell update or physical-device
  validation. After a separately authorized deployment, manually repeat the
  reported sequence on the affected installed PWA and verify its live session.
- No QA/production log reads or changes, real Auth/email/link, hosted redirect,
  provider privacy, remote JWT, database, Steam, or backend/native verification was
  performed for this local frontend fix. Existing deployment/privacy receipts in
  the other checkout are preserved, not reinterpreted as validation of this fix.

## Publication review — 2026-11-26 UTC

The owner subsequently authorized reviewing/committing this fix, pushing only
`feature/authenticated-back-navigation`, opening a PR targeting **dev**, and letting
the standard required checks run **once on the final SHA**. No merge, deployment,
QA/Supabase settings, real recovery email/link, Steam change or experimental-branch
push is authorized.

Fresh fetch confirms origin/dev is still the base SHA above. Reviewed every
tracked diff and new file: exactly **13 scoped files**, consisting of the three
production files listed above, guard/login/recovery regressions, new real-router
and browser tests/helper, frontend-only focused Playwright config, adjacent spec
and this report. No dependency/lockfile, workflow/CI budget, route map, SDK/client,
AuthService, callback scrubber, Back ledger, PWA, backend/schema or Steam change.
No further implementation/test correction was required by publication review;
the local validation above applies to the reviewed source. Only scope/status
documentation changed during this review; no repeated validation campaign.

Verified that the guest guard and Login read only transient navigation **info**,
not persisted `state`; completion still replaces the callback and grants no new
authority. Successful login replaces the current form with fixed Home. Existing
guard/restoration/session/recovery semantics stay intact. Retained tests directly
cover restored legacy flags, exact recovery→sign-in→Home→Back with an unchanged
valid mocked session, no callback replay, normal sign-in/sign-out/direct links,
delayed restoration, and ordinary Back/Forward without history growth or Login
activation. Installed-PWA/native-gesture limitations and unavailable standalone
E2E Node typings remain explicitly documented above and must appear in the PR.

Original experimental checkout's **32 path fingerprints**, including its QA
report, remain unchanged; its branch is not included in the publication. The
first read-only fetch hit a connection reset; a subsequent fetch succeeded.
No remote write or CI retry was performed to resolve that transient read failure.

Publish one scoped commit, then add the final SHA and the one standard CI run's
observed results to the PR without another source commit just for receipts.
The standard workflow supplies backend, postgres, frontend, e2e and native-runtime
checks using its existing isolated disposable test infrastructure; no additional
workflow or manual diagnostic campaign is requested. **Do not merge or deploy;
stop for review after reporting the actual PR/SHA/check results.**
