# Password visibility icon — QA feedback

Status: owner-requested local implementation for review, 2026-11-26.
Branch `feature/password-visibility-icon`, from freshly fetched origin/dev
`81c4d0e5f398aea99e5ecb6fb9aceb17ae807ba8` (merged authenticated Back fix).

## Approved scope

- Apply the same eye / eye-off convention to all three existing password visibility
  buttons: Sign in, Register and Reset password. No additional field or screen.
- Inspection found **no existing icon library, SVG set or icon font** in package
  dependencies/lockfile, source or assets. The operator explicitly selected
  **Inline SVG (Recommended)** when asked: this authorizes small dependency-free
  eye icons instead of the original existing-library requirement. No dependency,
  icon font, CDN or generic icon framework may be added.
- Replace visible Show/Hide text, not the existing controls/forms. Keep a native
  `type="button"`, dynamic accessible action name **Show password / Hide password**,
  existing aria-controls, keyboard activation, focus and validation/submission.
  Decorative SVG must be hidden from assistive technology and not focusable.
- Hidden input: eye (Show action). Visible input: crossed eye (Hide action).
  Keep each form's independent, initially false visibility signal. No value copies,
  state persistence, new auth/session behavior, field/node replacement, autocomplete
  or validator changes. Reuse a tiny auth-local presentation component across the
  three real usages, not a shared form-control abstraction.
- Reuse current secondary-button/focus conventions, with at least 44px by 44px
  mobile target and no horizontal overflow. Preserve the recent authenticated Back
  fix and isolated one-shot recovery path unchanged.

## Acceptance / verification

- Focused tests verify icon state, decorative semantics, dynamic accessible name,
  same-node toggling/value/validation/autocomplete, independent fields and no
  accidental login/registration/password update (including clicking the SVG).
- Run affected frontend tests, production build, lint and configured app/spec type
  checks. Focused desktop/mobile E2E verifies real keyboard/focus/touch sizing and
  explicit submit with mocked Auth/API, including recovery. No backend suite.
- Record actual results, warnings, icon-source approval and physical-device /
  screen-reader limitations in adjacent `implementation-report.md`.

Initial implementation was local and uncommitted. The owner's subsequent
publication instruction authorizes scoped review, commit/push of this feature
branch, a PR targeting dev and one standard required CI run on the final SHA.
Investigate concrete failures only; record final SHA/check results in the PR
without a second source commit solely for CI receipts. No merge, deployment,
QA/Supabase setting change, real recovery email/link, unrelated local backend
suite, database or Steam work. Preserve unrelated feature/experimental checkouts
and the existing QA report; stop for review.
