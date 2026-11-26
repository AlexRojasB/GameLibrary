# Authenticated Back navigation — QA correction

Status: owner-requested local implementation for review, 2026-11-26.
Branch `feature/authenticated-back-navigation`, from freshly fetched origin/dev
`5e06c2529794186b8d7935ce0606f847d906961a`, not the experimental QA-release branch.

## Scope / acceptance

- Investigate recovery → Sign in → Home → browser Back using real Angular routing
  and mocked authentication. Check the session separately from the rendered screen;
  do not infer logout from the login UI. No hosted reproduction/email is authorized.
- An authenticated user must not be left on Sign in by Back/Forward. Await normal
  session restoration before guest/protected decisions. Preserve ordinary in-app
  history and guards; do not globally intercept gestures/popstate or disable Back.
- Preserve GL-08's immediate, display-only recovery-completion Sign in even when an
  unrelated ordinary session exists. That exception/message is transient to the
  completion navigation, not permission replayable from browser history. Legacy
  persisted confirmation flags must not bypass the guest guard on traversal/reload.
- Successful explicit Sign in replaces its authentication entry with fixed Home,
  rather than adding an avoidable return to the submitted form. Failed Sign in
  stays on the form. Older legitimate entries and Forward remain browser-managed.
- Do not exchange/replay callback material, modify normal/recovery session storage
  or cleanup, bypass protected guards, add a return URL, or change GL-13's in-app
  Back ledger. Direct links, ordinary sign-in/sign-out/restoration remain working.
- Regression coverage: exact recovery/update/cleanup/sign-in/Home/Back sequence,
  resulting screen **and** valid ordinary session; ordinary sign-in/Home/Back;
  legacy confirmation entries; restoration/direct links; signed-out guards and
  Back/Forward without loops/history growth or unnecessary Sign in activation.
- Run affected frontend tests, build, lint, application/spec type checks, focused
  desktop/mobile mocked-auth browser E2E. Record baseline reproduction, results,
  warnings and limitations in `implementation-report.md`.

No dependency, backend/schema, SDK/session-authority, Steam, PWA configuration,
QA/provider setting or deployment change. Initial implementation was local and
uncommitted. The owner's subsequent publication instruction authorizes scoped
review, commit/push, a PR targeting dev and one standard required CI run on the
final SHA. Investigate concrete failures only; record SHA/check results in the PR
without a second source commit solely for CI evidence. No merge, deployment,
real email/link, hosted account or QA/provider setting change. Browser Back
simulation is not the native gesture in an actually installed PWA; that remains
manual follow-up.
