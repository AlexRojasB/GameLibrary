# GL-07 — Password visibility toggle

Status: owner-requested local implementation for review. Branch
`feature/password-visibility-toggle`, based on freshly fetched `origin/dev`
`0a531c076e4b1b11cd3d4b1129a6f3f657541a0c` (merged PR #20).
GL-07 is the product task ID; Feature 007 remains Random Picker.

## Acceptance / scope

- Cover the existing user-editable password inputs in `/login` and `/register`.
  Repository inspection found no confirmation, change-password or recovery field.
  Do not introduce new screens or password recovery.
- Each input starts with type=password. A native type=button control switches
  that same input between password/text, with visible accessible action text
  Show password / Hide password and aria-controls identifying its field.
- Preserve input identity, label, formControlName, required and autocomplete
  (current-password for login; new-password for registration). No field replacement,
  value copying, form-control recreation, validator or submission change.
- Keep visibility as a boolean signal local to each existing form component,
  initially false and never persisted. Fields/components must not share state.
  Keep password values exclusively in their existing inputs/reactive form and
  existing authentication submission path; no logging, analytics, URL or extra
  password-bearing state.
- Reuse existing button/input/focus styles with a small wrapping layout that is
  usable on desktop/mobile. Native keyboard activation must not submit the form.
  No shared form component exists to reuse; two small local additions do not
  justify a new form abstraction or broad refactor.
- Keep authentication services, Supabase calls, registration confirmation/errors,
  guards and all other application behavior unchanged.

## Verification / boundary

Focused frontend tests verify both fields' default, name/control association,
autocomplete/required attributes, same-node value/validation preservation while
showing/hiding, independent components, fresh-instance reset, no toggle-triggered
submission and unchanged explicit submit payloads. Retain existing login/register
behavior tests. Focused desktop/mobile E2E verifies keyboard Enter/Space activation,
visible action text, value preservation, no accidental authentication requests,
mobile sizing/no overflow, route reset and explicit login after toggling.

Run affected frontend tests, Angular build, lint and type checks. Use existing
isolated E2E auth/PostgreSQL infrastructure only; no unrelated backend suites.
No backend/database/dependency/Steam/QA source changes or remote resource changes.
The initial implementation was local and uncommitted. The owner's subsequent
publication authorization permits scoped commit/push, a PR targeting dev and one
normal required CI run on the new SHA. Investigate concrete failures only; preserve
assertions and avoid unrelated diagnostic campaigns. Record the final SHA/results
in the PR. No merge, deployment, Steam/QA changes or remote migrations. Stop for review.
