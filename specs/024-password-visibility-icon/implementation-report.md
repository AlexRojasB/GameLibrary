# Password visibility icon — implementation review

Date: 2026-11-26 UTC.
Branch: `feature/password-visibility-icon`, from freshly fetched origin/dev
**`81c4d0e5f398aea99e5ecb6fb9aceb17ae807ba8`** (merged PR #24).
Status: **implementation validated locally; scoped publication authorized, not deployed**.
The implementation was initially unstaged/uncommitted; see Publication review for
the subsequent authorization. Final SHA/PR/CI receipts are recorded on the PR.

## Preservation / inspection / approved icon source

Created a separate clean worktree at
`/Users/mac/Documents/Projects/GameLibrary-Password-Visibility-Icon-2026-11-26`.
The preceding navigation feature checkout and unrelated experimental QA checkout
were not switched, reset, cleaned or copied into this branch. A private pre-task
manifest records their branches/HEADs and the experimental checkout's 32 changed
or untracked paths, including its QA release report; final comparisons confirm
they remain unchanged.

Read GL-07, its implementation report, GL-08 recovery, and the current templates,
local signals, styles and tests. Product/Domain/Architecture and agent instructions
are unchanged from the preceding reviewed base; no Domain or architecture change.
Inventory found exactly three existing visibility controls:

| Screen | Field | Preserved autocomplete |
| --- | --- | --- |
| Sign in | `login-password` | `current-password` |
| Register | `register-password` | `new-password` |
| Reset password | `reset-password` | `new-password` |

There is **no existing icon library**: checked dependencies/lockfile, HTML imports,
source/styles, shared components and public assets for icon packages, fonts or an
SVG set. The existing Back arrow is a decorative text glyph; public icons are PWA
artwork, not a UI icon set. No new password field/screen is needed.

Reported the existing-library mismatch before implementation. The operator chose
**“Inline SVG (Recommended)”**, explicitly authorizing small dependency-free
eye/eye-off SVGs instead. No package, external asset, font/CDN or icon framework was
introduced. Adjacent `spec.md` records this narrow approved exception to the
original instruction and supersedes GL-07's visible-action-text requirement only.

## Implementation

- `features/auth/password-visibility-icon.ts`: a tiny reusable **auth-local,
  decorative presentation component**, justified by three real usages. An eye
  outline/pupil gets a diagonal slash when the password is visible. Hidden means
  eye / Show action; visible means crossed eye / Hide action. It receives only the
  existing boolean, never a password, form control, session or auth capability.
  Its SVG uses `currentColor`, `aria-hidden="true"` and `focusable="false"`; no
  nested interactive element or competing accessible name.
- Login/Register/Reset password import this component and render it inside their
  **existing native `type="button"`** controls. Visible Show/Hide text is replaced
  with the SVG; dynamic `aria-label="Show password"` / `"Hide password"` describes
  the action, while existing `aria-controls` retains the field association.
- Existing per-form `passwordVisible = signal(false)` state and click expression
  are unchanged. The same input node, label, reactive control, value, validation,
  autocomplete, readonly/description and explicit submission paths remain.
- Existing Login/Register password-row styles retain 2.75rem minimum height and
  add 2.75rem minimum width, nonshrinking button sizing and small icon padding.
  Recovery already reuses Login styles. Existing secondary-button colors and
  global 3px `:focus-visible` outline remain; no focus redirection or prevention of
  native keyboard events. Mobile targets measure at least **44×44px**.
- Exactly nine production files: one icon component, three templates, three
  component import declarations and two existing stylesheets. No auth-service,
  guard, route/history, recovery-grant/exchange/cleanup, password-storage, PWA,
  backend/database or Steam logic change. The recent navigation fix stays intact.

## Tests / actual results

| Verification | Result |
| --- | --- |
| Affected frontend auth/recovery/guard/login/register/home, shell and Back regressions | **151 PASS / 12 files** |
| Focused browser E2E | **8/8 PASS: 4 desktop + 4 Pixel 7-emulated mobile**, retries 0 |
| Production Angular build | **PASS**, initial **695.52 kB**, estimated transfer **166.56 kB** |
| Application/template lint | **PASS** |
| Explicit changed E2E/config ESLint | **PASS** |
| Configured application and unit-spec TypeScript checks | **PASS** |
| Whitespace/scope and unrelated-work preservation | **PASS** |

Updated GL-07 parameterized tests retain all previous form/value/validation and
independence assertions, replacing text assertions with accessible-name plus
decorative SVG/eye/slash-state checks. Clicking the SVG bubbles through the native
button and must not submit a valid login or registration form; explicit submit
still forwards the unchanged credentials. Fresh instances remain hidden.

Recovery tests retain fresh-event/grant/expiry/cleanup/normal-session protections,
verify its icon/name/default state, and add two focused regressions: valid-field
SVG clicks preserve focus, input identity and all form metadata without invoking
update/submit; recovery visibility is independent of Login and Register. Explicit
recovery submission still sends exactly the existing new-password payload once.

Browser E2E verifies all three controls' icon-only rendering and action names,
decorative attributes, Enter/Space activation, retained focus and actual
`:focus-visible`/nonzero outline, input value/autocomplete, measured 44px target,
no horizontal overflow and no toggle-triggered authentication/update. It exercises
an SVG click as well as native keyboard activation and explicit successful login.
Existing mocked PKCE/update/cleanup and recovery→login→Home→Back regression remains
passing; no callback replay or session-behavior change is introduced.

The ordinary visibility E2E now uses the existing mocked Auth/API fixture instead
of `/e2e/reset` and a database. `playwright.password-visibility.config.ts` reuses the
existing frontend-only server/desktop/mobile config and selects the visibility and
recovery specs. **Only Angular starts**; no backend, PostgreSQL, fake Steam process,
hosted provider/account or email is needed. Recovery still uses the real SDK's
PKCE implementation against mocked HTTP. Main Playwright/CI configuration is not
changed, and no unrelated backend suites or remote CI were run.

Commands from `src/frontend` (all validation commands below passed):

```sh
npm test -- --watch=false \
  --include='src/app/features/auth/**/*.spec.ts' \
  --include='src/app/core/auth/**/*.spec.ts' \
  --include='src/app/core/navigation/back-navigation.spec.ts' \
  --include='src/app/app.spec.ts'
npx playwright test --config playwright.password-visibility.config.ts
npm run build
npm run lint
npx eslint e2e/password-visibility.spec.ts e2e/password-reset.spec.ts \
  playwright.password-visibility.config.ts
npx tsc --noEmit -p tsconfig.app.json
npx tsc --noEmit -p tsconfig.spec.json
git diff --check
```

Locked dependencies installed with `npm ci` using a private temporary npm cache;
package manifest/lockfile remain unchanged. All affected/test/build runs passed
without retries or weakened assertions. No screenshots/recordings contain real
credentials; tests use synthetic values only.

## Warnings / limitations / handoff

- Existing unrelated Picker SCSS warning: **4.13 kB vs 4.00 kB**, 130 bytes over.
  Existing Vite CommonJS/native-loader and Vitest poolOptions warnings remain;
  none suppressed, no budget or dependency change.
- Type-check claims cover the configured application and unit-spec projects.
  There is still no standalone E2E TS setup with Node typings in locked
  dependencies; no dependency was added for it. Browser E2E is transpiled/executed
  by Playwright and explicitly linted, not claimed as standalone static typing.
- Browser accessibility assertions are not a manual screen-reader audit. Physical
  device / installed-PWA, Safari/Firefox, forced-color/OS behavior and third-party
  password-manager extension testing remain manual follow-up. Existing input
  identity/autocomplete/form semantics and visible keyboard outline are preserved.
- At the end of implementation, all changes remained local/unstaged/uncommitted,
  with no push, PR, deployment, QA/Supabase/logging setting, real email/link, Steam
  operation, schema/migration or unrelated feature change. Original experimental
  report and preceding navigation checkout remained preserved. Publication was
  separately authorized afterward, as recorded below.

## Publication review — 2026-11-26 UTC

Fresh origin/dev remains **`81c4d0e5f398aea99e5ecb6fb9aceb17ae807ba8`**. Reviewed
the complete tracked diff and new files: exactly **16 scoped files** — nine
production presentation/import/style files, four focused test files, one
frontend-only Playwright config, the feature specification and this report.
No dependencies/lockfile, auth/guard/callback/session/history behavior, route map,
PWA, backend/database, Steam, workflow or CI budget change. The only TypeScript
changes in the three existing form components are icon imports/declarations.

Confirmed all three controls start hidden, render eye for Show / crossed eye for
Hide and expose the corresponding dynamic accessible action name. Every control
retains native `type="button"`, aria-controls, its original input and independent
visibility signal. SVG is decorative/nonfocusable; focus styling and 44px targets
remain. Focused tests retain same-input/value/validation/autocomplete assertions,
keyboard/focus and SVG-click behavior, no accidental submissions and unchanged
explicit auth/update payloads. Physical-device and manual screen-reader validation
remain pending and must be stated in the PR.

No additional implementation/test issue was found. The recorded passing local
validation applies to this reviewed source; only publication scope/status
documentation changed during this review. No repeated local validation campaign
or unrelated local backend suite. Rechecked both unrelated worktrees' branches,
HEADs and the **32 experimental path fingerprints including the QA report**;
unchanged and excluded from publication.

The owner now authorizes one scoped commit, a push of only
`feature/password-visibility-icon`, a PR targeting **dev**, and the standard
required CI run once for its final SHA. Record actual run/attempt/check results
on the PR, without another source commit solely for receipts. Investigate only
concrete failures; no manual reruns or diagnostic workflows. **Do not merge,
deploy, change QA/Supabase settings, send real email/links, alter Steam or push
the experimental branch. Stop for review after reporting the PR/SHA/results.**
