# GL-07 — Password visibility toggle, implementation review

Date: 2026-11-25. Branch: `feature/password-visibility-toggle`, created from freshly
fetched `origin/dev` at `0a531c076e4b1b11cd3d4b1129a6f3f657541a0c` (merged PR #20).
The initial implementation was local and uncommitted. The owner's subsequent
authorization permits scoped commit/push and a PR targeting dev with one normal
required CI run. The PR records the final published SHA and observed check results.

## Fields covered / inspection

- `/login`: `#login-password`, existing required reactive control,
  `autocomplete="current-password"`.
- `/register`: `#register-password`, existing required/minimum-six-character
  reactive control, `autocomplete="new-password"`.
- Repository-wide frontend inspection found exactly these two editable password
  fields. There is no existing confirmation-password, password-change or recovery
  field/screen. None was introduced.
- No shared password/form-input component exists. Login and Register already have
  separate small templates, styles and local signals. Retain that structure rather
  than introducing a control-value accessor, wrapper component or form refactor.
- Reviewed repository instructions and the existing authentication specification;
  authoritative Product/Domain/Architecture documents are unchanged from the
  preceding review. Existing auth services, validation and routing stay authoritative.

## Exact changes

Six production files under `src/frontend/src/app/features/auth/`:

- `login/login.ts` and `register/register.ts`: one independent
  `passwordVisible = signal(false)` per component. No password string is copied
  into this state; it stores only a boolean. No service/storage/URL persistence.
- `login/login.html` and `register/register.html`: bind type on the existing input
  to password/text, preserving its node, ID, associated label, formControlName,
  required and autocomplete. Add a native `type="button"` with visible action text
  Show password / Hide password and `aria-controls` pointing to that input. Native
  keyboard activation does not trigger submit. No replacement input or binding
  to password values beyond the existing reactive form.
- `login/login.scss` and `register/register.scss`: a small wrapping input/button
  row, flexible input with min-width zero and a 44px minimum button height. Reuse
  existing secondary button/input colors, border styles and focus-visible outline.
  No global styles, budgets or layout abstractions changed.

Verification/documentation additions:

- `src/frontend/src/app/features/auth/password-visibility.spec.ts`: **13 new test
  executions**, parameterized across both screens. Verify default hidden/native
  attributes/accessibility names; the same input/value/control state after show
  and hide for empty, short and valid values; required/minLength validity; no value
  or status events during toggling; unchanged dirty/touched/errors; no accidental
  submit; explicit submit forwards the unchanged synthetic credentials; independent
  Login/Register visibility; fresh instances start hidden. Existing login/register
  tests and assertions are unchanged.
- `src/frontend/e2e/password-visibility.spec.ts`: one focused scenario executed
  on desktop and mobile. Verify both existing screens, actual Tab order and
  Enter/Space activation, click activation, action names/control association,
  same value and autocomplete, focus retention, 44px button dimensions and no
  horizontal document overflow. Navigate away while shown and confirm the next
  screen starts hidden. Observe zero authentication POSTs from toggles, then one
  explicit successful sign-in after showing/hiding.
- `specs/020-password-visibility-toggle/spec.md` and this report record scope and
  verification. No higher-authority behavior or Domain invariant changes.

## Local validation

| Check | Result |
| --- | --- |
| Affected frontend suites: new visibility tests, Login, Register, AuthService, guards and token interceptor | **41 PASS**, 6 files; 13 new +28 existing tests |
| Final publication review: repeat the affected frontend suites | **41 PASS**, no assertion or runtime changes |
| Angular production build, `npm run build` | PASS; initial bundle 678.21 kB (estimated transfer 163.42 kB) |
| `npm run lint` | PASS |
| `npx tsc --noEmit -p tsconfig.spec.json` | PASS; application/template types also checked by Angular build |
| Focused Playwright desktop + mobile, explicit `--retries=0` | **2 PASS**, no failures/retries |
| `git diff --check` | PASS |

Frontend test command from `src/frontend`:

```text
npm test -- --watch=false --include='src/app/features/auth/password-visibility.spec.ts' --include='src/app/features/auth/login/login.spec.ts' --include='src/app/features/auth/register/register.spec.ts' --include='src/app/core/auth/**/*.spec.ts'
```

E2E command from the same directory:

```text
npm run e2e -- e2e/password-visibility.spec.ts --retries=0
```

ConnectionStrings__E2E pointed only to the task-owned loopback PostgreSQL 17
`game_library_e2e` database. Existing guarded E2E setup applied existing migrations
and seeded disposable data; there is no schema/migration change in this diff.
The test uses existing fake authentication and synthetic credentials, never hosted
Auth/accounts or real provider calls. The temporary container and its anonymous
volume were removed after validation. No unrelated backend unit/integration/native
suites or remote CI were run. Starting the unchanged API for E2E is not a backend
source/configuration change.

## Warnings / limitations / review boundary

- Existing unrelated Picker SCSS budget warning: 4.13 kB versus 4.00 kB, 130 bytes
  over. No Picker source or budget changes.
- Existing Vite future native-config-loader warning for ESM in CommonJS-loaded
  `vitest-base.config.ts`, and Vitest 4 `test.poolOptions` deprecation. No tooling
  changes or warning suppression.
- Mobile coverage is emulated Pixel 7 Chromium, not a physical-device or manual
  screen-reader audit. Password-manager compatibility is protected by preserving
  native input identity/autocomplete/form semantics; third-party password-manager
  extensions and Safari were not manually tested.
- No unresolved product/architecture decisions. No shared state, extra credential
  storage, logging, analytics, URL values, dependencies or broad form refactor.
- No backend/database schema/auth-service/Steam/QA source changes. Existing
  untracked QA review/deployment documents remain untouched and uncommitted.
- Publication follow-up: reviewed the final diff against freshly fetched origin/dev.
  Only the six GL-07 UI files, two focused test files, specification and report are
  included. Both forms start hidden; visibility is an independent, non-persisted
  local boolean. Dynamic action names, same-node values/validation/autocomplete and
  explicit type=button/no-accidental-submit coverage are verified. No production
  source or assertion changes were needed during publication review.
- The owner authorizes scoped commit/push, a PR targeting dev and one standard CI
  run. Record final SHA/check results in the PR, without another source commit just
  for CI evidence. No merge, deployment, Steam/QA changes or remote migration.
  Stop for review; publication is not release approval.
