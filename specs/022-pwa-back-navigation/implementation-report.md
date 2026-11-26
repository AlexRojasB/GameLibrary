# GL-13 — PWA Back navigation, implementation review

Date: 2026-11-25 local (test output 2026-11-26 UTC).
Branch: `feature/pwa-back-navigation`, created from freshly fetched origin/dev
`85af24579317d638b78369654ca3d12258a0ad39` (merged GL-08).
The initial implementation was local, unstaged and uncommitted. The owner's
subsequent publication authorization permits scoped review, commit/push, a PR
targeting dev and one standard required CI run on the final SHA. This is not
merge, deployment or release approval.

## Screen / standalone-mode inventory

The manifest is `src/frontend/public/manifest.webmanifest`: `display: standalone`,
`./` start/scope on the root-hosted app. Standalone chrome cannot be relied on for browser Back.
The shell already provides desktop Home/Library/Pick/Play Log/Manage and mobile
Home/Library/Pick/Manage. It keeps the single stable outlet introduced by GL-08.

| Screen | Before | GL-13 result / missing-history fallback |
| --- | --- | --- |
| Video games | Static Back to Manage, even when opened from empty Library | Replace with the shared contextual Back; `/manage` fallback |
| Board games | Same static return | Same contextual replacement; `/manage` fallback |
| Platforms | Home link did not return to its Manage parent | Replace Home link with shared contextual Back; `/manage` fallback |
| Play Log, mobile (<760px) | No contextual return; not a mobile primary destination | Add shared contextual Back; guarded `/` fallback |
| Play Log, desktop | Already a primary destination | No visible Back; mobile control is hidden at the shell's existing 760px breakpoint |
| Health | Public diagnostic page without shell or any exit | Add contextual Back; guarded `/` fallback (Sign in when unauthenticated) |
| Home, Library, Pick, Manage | Adequate primary navigation | Unchanged; no Back controls added |
| Login/Register/Forgot password/Reset password | Explicit account/Sign in/new-email routes | Unchanged; no history-driven auth/recovery control |
| Steam | Existing shell/Manage exit | Explicitly excluded from this task; no UI, callback or protocol change |
| Add/Edit, cover selection/crop, Log Play dialogs/sheets | Existing Cancel/Close/Escape | Unchanged; no route, global Back interception or duplicate close control |

## Implementation and safety boundaries

- `core/navigation/back-navigation.ts`: one small Angular service, started by an
  environment initializer in `app.config.ts` before initial navigation. It records
  **only successful, canonical known application destinations and numeric navigation
  IDs**, with a cursor over the browser entries observed during this boot.
- `shared/back-button/back-button.ts`: reused by five screens; a native non-submit
  secondary button, descriptive visible `Back to <destination>` text, decorative
  arrow hidden from assistive technology, existing focus styles, minimum 44px height
  and disabled while navigating. No dependency, design system or global UI redesign.
- The closest known different predecessor is traversed with public
  `Location.historyGo`, not pushed as a new route. Back/Forward preserve the browser
  chain; navigating after Back discards the abandoned forward branch. Replacements
  and same-URL reloads update the current slot instead of creating phantom entries.
  Imperative cancel/error/skip release the pending state without adding successful
  visits. Unsuccessful browser traversals discard the ledger: Angular's default
  cancellation recovery may replace the visited slot without restoring the cursor,
  so reusing an old offset could otherwise eventually reach an unknown destination.
- Direct entry, reload, unknown history, mismatched browser state and absent safe
  predecessors use the fixed screen parent with `replaceUrl`. A leaf does not keep
  reappearing on Back from its fallback. No use of `history.length` in production,
  `document.referrer`, external URLs, URL-derived fallback, caller returnTo or saved
  navigation extras. Even an invalid runtime fallback resolves only to Home/Manage.
- Auth/recovery routes (including encoded aliases), Steam and query/fragment/matrix
  URLs are **history barriers**, not stripped URLs retained for replay. Back does not traverse
  across them. This conservative exact-path policy matches current screens (which
  do not have query-driven browse state). New routes/query-based state will require
  explicit review before becoming eligible.
- Normal subject/logout changes invalidate the ledger synchronously using the
  existing AuthService notification. Token refresh for the same subject does not.
  No callback parameters, codes, tokens, form drafts, sensitive extras or arbitrary
  URLs are retained/logged/persisted by GL-13. Unknown outside-router changes and
  `skipLocationChange` also invalidate evidence of contiguous browser entries.
- Both known-history traversal and fallback still go through Angular guards. No
  AuthService, guard, PKCE client, callback scrubber, shell/outlet or Steam change.
  The existing recovery isolation/one-shot/outlet tests and mocked PKCE E2E remain
  passing with the initializer active.
- Ordinary browser Back/Forward are **not intercepted or globally rewritten**.
  They can revisit old/auth entries as before, subject to existing guards and the
  one-shot recovery boundary; the new in-app Back never chooses such a target.
- Navigation knowledge is memory-only and intentionally lost on reload. No route
  reuse, component caching or new state persistence. Library criteria, Manage search
  and forms remain component-local and reset when their component is recreated,
  exactly as before. Picker current-round recovery remains the existing backend-
  backed mechanism; no picker state/history/filter/eligibility code was changed.

## Local validation

All commands run from `src/frontend`; final runs succeeded with no retries.

| Verification | Final result |
| --- | --- |
| Focused real-router/control tests | **30 PASS** in the new navigation spec |
| Affected frontend + auth/recovery/shell regressions | **196 PASS**, 12 files (includes the 30 above) |
| Focused navigation E2E | **5 desktop + 5 Pixel 7-emulated mobile PASS** |
| Existing password-reset E2E regression | **2 desktop + 2 mobile PASS** |
| Combined E2E command | **14/14 PASS**, `--retries=0` |
| Production Angular build | PASS; initial **693.91 kB**, estimated transfer **166.20 kB** |
| Lint | PASS |
| TypeScript application + spec checks | PASS |
| Whitespace/scope check | PASS; no backend/dependency/PWA/Steam changes |

### Commands

```sh
npm test -- --watch=false \
  --include='src/app/core/navigation/back-navigation.spec.ts' \
  --include='src/app/core/health/health-check/health-check.spec.ts' \
  --include='src/app/features/platforms/platforms-list/platforms-list.spec.ts' \
  --include='src/app/features/games/video-games-list/video-games-list.spec.ts' \
  --include='src/app/features/games/board-games/board-games-list/board-games-list.spec.ts' \
  --include='src/app/features/play-log/play-log-page/play-log-page.spec.ts' \
  --include='src/app/core/auth/**/*.spec.ts' \
  --include='src/app/features/auth/password-reset.spec.ts' \
  --include='src/app/app.spec.ts'
npm run build
npm run lint
npx tsc --noEmit -p tsconfig.app.json
npx tsc --noEmit -p tsconfig.spec.json
npm run e2e -- e2e/back-navigation.spec.ts e2e/password-reset.spec.ts --retries=0
```

E2E used the unchanged harness and an explicitly synthetic unused local connection
string (`ConnectionStrings__E2E`, loopback port 55432), as required to start its
guarded local API. Navigation tests seed the established E2E fake session and mock
API **GETs only**. No database instance, seed/reset endpoint, application mutation,
migration, real account/provider or Steam call was used. The unchanged password-
reset fixture exercises the real SDK PKCE flow against mocked HTTP, not real email.

Router coverage uses the real Router, real authGuard and Angular's browser-entry
model (`MockPlatformLocation`), not synthetic NavigationEnd-only event tests:
normal return; retained Forward; Back then branching; replace/same-URL reload/skip;
direct/reload/unknown-ID/no-ID fallback; outside-router gap; auth/query/fragment/
matrix/encoded recovery barriers; imperative cancel/error; cancelled/skipped
browser traversals (invalidated offsets); account invalidation vs token refresh;
expired guarded predecessor; protected fallback; duplicate activation and native
button/pending semantics. Existing recovery tests cover canonical/trailing-slash
pre-bootstrap scrubbing and one-shot callbacks. A trailing-slash router request
itself is not a successful route in the synthetic router configuration.

Browser E2E covers the actual startup initializer, Library→catalog contextual return,
Manage→all three subpages, Back/Forward/new branch, no history growth on Back,
direct-link query rejection/fixed fallback, reload reset and no fallback loop,
mobile-only Play Log behavior, Health exit/Space activation and logged-out guards.
It also checks Enter/focus, a >=44px target and viewport bounds at desktop/mobile
sizes. It does **not** claim exhaustive accessibility or real-device review.

Initial validation caught test-harness issues which were fixed without weakening
assertions: install the Router's location-change listener for real traversal tests;
select Play Log Retry by text rather than its old first-button position; wait for
catalog rendering before sampling browser history length. Final affected/E2E runs
are the passing results above, not those initial failures. No timeouts raised,
production budgets changed or assertions removed to hide a failure.

## Existing warnings and unverified behavior

- Unchanged Picker SCSS budget warning: **4.13 kB vs 4.00 kB**, 130 bytes over.
- Existing Vite native-loader/CommonJS warning and Vitest `test.poolOptions`
  deprecation. No suppression, dependency or tooling change.
- The inspected manifest requests standalone and the unchanged service worker
  caches only the static/application shell; no offline CRUD or navigation-history
  persistence was introduced. Playwright runs the existing development/E2E server,
  **not an installed standalone PWA or a production service-worker session**.
- **Installed-PWA validation was not performed.** OS Back/gestures, physical
  Android/iOS/desktop installs, Safari/Firefox, device screen readers and offline
  shell navigation remain manual follow-up; no installed-device claim is made.
- No complete frontend/backend/native suite or hosted recovery/privacy/operational
  campaign was run locally for this scoped frontend task. Publication uses the
  standard required CI checks once on the final SHA, without manual reruns or an
  unrelated diagnostic campaign.

## Publication review

Reviewed the complete scoped diff against freshly fetched origin/dev
`85af24579317d638b78369654ca3d12258a0ad39`. No additional concrete implementation
issue was found; no production code or test assertions were changed during the
publication review. The implementation-phase validation above applies to the
same reviewed source; only this report and the scope authorization were updated.

Confirmed intended screen placement and the desktop Play Log hiding breakpoint;
fixed Home/Manage fallbacks rather than client/external return destinations;
browser traversal/replacement/Forward handling; invalidation after unknown or
failed traversals; account boundaries; callback/credential-bearing route barriers;
and unchanged guards, recovery scrubbing and single outlet. Confirmed no Steam,
Picker-rule, dependency, backend, migration, PWA-config or QA changes. The precise
scope includes the GL-13 implementation, focused tests, specification and report
only. Existing local validation and warnings remain as recorded above; installed-
PWA/device behavior remains unverified.

Publish the scoped files, then record the final SHA and the one standard CI run's
results in the PR, without another source commit solely for CI evidence. Do not
merge, deploy, modify QA/remote configuration or apply migrations. Stop for review.

No implementation blockers remain. Backend, dependency
manifests/lockfile, primary navigation, PWA config, Steam and Picker logic are
unchanged. Pre-existing untracked QA/deployment review files remain untouched.
