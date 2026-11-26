# GL-08 — Password reset

Status: owner-requested local implementation for review. Branch
`feature/password-reset`, based on freshly fetched origin/dev
`d7554317ffb7b5cce6e06fd622a5996e69d3ee10` (merged PR #21).
GL-08 is the product task ID; Feature 008 remains UX/PWA polish.

## Approved scope / decisions

- Supersede Feature 002's historical password-recovery-UI deferral only. Add a
  Forgot password link on Sign in, public email request and recovery-only new
  password routes. No account/profile/password-change screen or backend endpoint.
- Request with the existing Supabase SDK resetPasswordForEmail, fixed same-origin
  `/reset-password` redirect. Neutral confirmation for existing/missing accounts
  and account-specific 4xx errors; safe generic network/rate-limit messages.
- Use supported PKCE exclusively for recovery in a separately namespaced SDK
  client, preserving the normal Auth client's implicit sign-in/signup/restoration.
  SDK owns verifier/session storage; no custom token or persistence mechanism.
  Disable automatic URL detection and automatic refresh in the recovery client.
- Capture a single code on the exact callback path and remove query/fragment
  before Angular/Supabase bootstrap. Reject duplicates, extra parameters,
  implicit tokens, provider errors and arbitrary destinations; never display/log
  callback values. A fresh successful exchangeCodeForSession AND matching SDK
  PASSWORD_RECOVERY event are required. INITIAL_SESSION, normal sign-in, URL type
  and restored sessions alone never enable updating. No grant persistence/reload
  recovery: a stripped/reloaded callback must request another email.
- Keep a single stable application router outlet. The previous conditional
  auth/shell outlets recreated the callback screen while changing layouts, which
  would consume a one-shot callback twice. This small required layout correction
  preserves existing navigation and puts the two new pages in the public layout.
- The original recovered session expiry and identity bound the one in-memory
  update grant. Check fresh SDK session/identity before updateUser; authentication
  changes invalidate it. Existing required/minimum-six-character validation and
  GL-07 Show/Hide controls apply; password hidden initially, no password copies.
- After update success clear the form and end only the isolated recovery session
  through supported SDK local sign-out. Never replace, clear or sign out a normal
  session, even for the same account or one changed while recovery was pending.
  Cleanup failure must not
  claim completion or repeat the password update: offer cleanup-only retry.
  Then navigate to fixed Sign in with a non-sensitive confirmation. The guest
  guard permits this display-only login navigation even with an existing normal
  session; that flag grants no recovery/password-update authority and is ignored
  on other routes. Ordinary guard behavior remains unchanged. No claim of
  revoking already-issued access JWTs or globally signing out other devices.
- Expired/used/invalid links fail closed with a new-email path. Never render raw
  provider errors. Duplicate submissions are blocked. Abandoned/pending work
  cannot navigate or restore a form after destruction; cleanup is best effort on
  abandonment and required/explicitly retriable after successful update.
- PKCE requires the same browser/device and SDK storage used for the request.
  Only the latest overlapping request is supported (no experimental flow IDs).
  Document these constraints and exact per-environment redirects/email settings;
  do not change any hosted settings or send email.

## Verification / boundary

Mock SDK unit/component tests cover neutral requests, validation, network/rate
errors, callback sanitization, fresh recovery event/identity/expiry, restored and
ordinary-session rejection, show/hide, explicit update, cleanup failure/retry,
destruction and account changes. Desktop/mobile E2E mocks Supabase HTTP only and
exercises the real SDK PKCE verifier/exchange/update/signout flow, not a fake app
recovery gate. Retain affected auth tests, build/lint/types. No unrelated local
backend suites; publication uses only the standard required CI checks.

No backend/schema/migration/dependency/paid-service/Steam/QA changes. The initial
implementation was local and uncommitted. The owner's subsequent publication
authorization permits scoped security corrections, commit/push, a PR targeting dev
and one standard required CI run on the final SHA. Preserve assertions and inspect
concrete failures only; no unrelated diagnostic campaign, merge, deployment,
remote Supabase change, real email, Steam/QA change or migration. Record final
SHA/check results in the PR and stop for review. Keep mocked local evidence
separate from outstanding hosted delivery/redirect/configuration/privacy gates.
