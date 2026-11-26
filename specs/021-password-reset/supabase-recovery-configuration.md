# GL-08 — Supabase recovery configuration / operator prerequisites

This is documentation, not an executed configuration change. No dashboard/remote
settings were read or modified, no email sent, and no deployment performed.
Repository URLs are recorded configuration/evidence, not proof of live settings.

## Flow and exact redirect entries

The application calls resetPasswordForEmail with `window.location.origin` plus
the fixed root path `/reset-password`. It never accepts a returnTo/next/redirect
parameter. The app must be hosted at an approved root origin, not a subpath.
Use exact additional Redirect URL entries in Supabase Authentication → URL
Configuration; do not add wildcard hosts/paths or arbitrary preview deployments.

| Environment | Existing configuration inspected | Required entry on the relevant Auth project |
| --- | --- | --- |
| Local Angular default, `npm start` | `http://localhost:4200`; development Auth URL `https://iramzxpjbnldhebykzhx.supabase.co` in environment.development.ts | `http://localhost:4200/reset-password` |
| Explicit local loopback host | `http://127.0.0.1:4200`, used by existing browser-test server | `http://127.0.0.1:4200/reset-password` **only if manually testing real local recovery on that origin** |
| QA stable frontend recorded in repository | `https://game-library-qa-web.vercel.app`; QA build reads QA_SUPABASE_URL/public key via scripts/build-qa.mjs; repository QA plan selects `https://yqngxhryzklrhhryzzoi.supabase.co` | `https://game-library-qa-web.vercel.app/reset-password` on the separately approved QA Auth project, only after operator confirmation |
| Production | environment.ts contains empty public placeholders; repository has no approved production frontend origin or populated production Auth configuration | **Not currently configured / exact domain unknown.** Before release, the owner must supply the approved HTTPS root origin and Auth project, then allow exactly that origin followed by `/reset-password`. Do not substitute the QA/dev project or invent a production domain. |
| Automated E2E | `http://127.0.0.1:4200` + SDK URL `http://localhost:54321`, fully intercepted by Playwright | No real Supabase allowlist or email settings: all Auth responses are mocked. |

The Site URL is the environment's approved frontend root (default local
`http://localhost:4200`, recorded QA `https://game-library-qa-web.vercel.app`,
production pending owner selection). Do not replace existing signup redirect
entries, change Site URL for another environment, broaden allowed hosts, or point
QA at development merely to enable recovery. Verify the saved project/origin
before any separately authorized change. A developer using another port/origin
must explicitly approve/add that exact callback; this task did not do so.

## Email configuration

- Existing email/password authentication must be enabled on the selected project.
  Keep current signup/confirmation policy. GL-08 does not require social providers,
  admin/service-role keys, MFA, a new SMTP provider or any paid service.
- Use the standard Reset Password email template link `{{ .ConfirmationURL }}`.
  Supabase must redeem its own recovery token and honor the supplied redirectTo,
  returning a PKCE `?code=...` to `/reset-password`. Do not replace it with a direct
  token-hash/implicit access-token link, a root-only SiteURL link or arbitrary
  next-destination template. If a template was customized, review it with the owner
  before enabling; no email-template changes were made here.
- Existing SMTP/sender authorization, rate limits, secure password-change policy,
  CAPTCHA and project password rules were **not remotely inspected**. The UI uses
  the existing required/minimum-six-character client validation; Supabase remains
  authoritative and may reject stronger-policy violations. Such provider errors
  fail safely with a new-link/sign-in path; raw provider details are never rendered.
- Default Supabase email delivery has testing/recipient/rate restrictions and is
  not evidence of general production deliverability. Existing authorized sender or
  custom SMTP may be required operationally; no service or paid plan was added.
  If the project requires CAPTCHA, a separately approved supported UI integration
  is a prerequisite rather than silently disabling the provider control.
- Test email scanning/link prefetch behavior separately: provider recovery links
  and PKCE codes are one-use. The app does not bypass expiry or implement scanner
  workarounds, resend retries or a custom token system.

## Browser, storage and session behavior

- The normal SDK client retains its existing implicit flow and storage key.
  Recovery uses the same installed SDK/configured public URL/key, with a separate
  `game-library-password-recovery` storage key, `flowType: pkce`, URL detection
  disabled and automatic refresh disabled. SDK storage/locks and PKCE verifiers
  are used unchanged; there is no custom storage adapter/token manager.
- The email must be opened in the **same browser/device/origin** in which recovery
  was requested, with browser storage available. Another tab on that origin can
  use the SDK verifier. Another device/private-browser profile cannot. Latest
  overlapping requests replace prior verifiers; experimental multi-flow support
  is not enabled. Request again from the browser/device that will open the email.
- A new SDK code exchange and matching PASSWORD_RECOVERY event authorize only the
  current component. A restored/ordinary session or a `type=recovery` URL cannot
  authorize this form. Refreshing the scrubbed callback requires another email.
- After acknowledged update, the app clears the form and uses supported SDK
  `signOut({scope:'local'})` **only for the isolated recovery client**. It never
  signs out or replaces an unrelated normal session (or a newer normal session
  for the same account). The normal AuthService implementation stays unchanged.
  A display-only navigation-state flag lets Sign in show recovery confirmation
  without the guest guard redirecting an already-authenticated normal user away.
  This flag grants no recovery authority and is not a return-destination input;
  explicit sign-in still uses the existing normal authentication behavior.
  No global sign-out or all-devices/access-JWT revocation is claimed.
- In installed auth-js 2.112.3, local sign-out clears SDK browser state even if the
  remote logout POST fails. The app shows the failure and allows cleanup-only
  retry; SDK retry may confirm an already-empty local session without another
  POST. It cannot establish remote refresh-token revocation after an unknown
  logout response. No credential is retained to manufacture another revocation
  request. Previously issued JWTs may remain valid until expiry. Abandonment
  cleanup is best effort; persisted SDK state alone never restores update authority.

## Privacy and hosting gates (before any real rollout)

- PKCE necessarily returns a short-lived authorization code in the callback
  **query**. The app removes all query/fragment values before Angular routing,
  normal SDK initialization or application error reporting. It never puts tokens
  or passwords in UI messages, analytics, app logs or persistent application state.
  Sessions/verifiers remain exclusively in supported SDK storage.
  Noncanonical recovery paths (encoded spelling, trailing slash or matrix
  parameters recognized by Angular) are scrubbed and rejected too, before the
  normal SDK could interpret an injected implicit callback on such a path.
- A browser cannot prevent the hosting edge from seeing the incoming callback URL.
  Before real testing/release, configure the frontend host/proxy/monitoring to
  omit query strings/fragment data and Auth request bodies/Authorization headers
  from logs, request tracing, session replay and analytics. Do not log raw
  `/reset-password` requests; do not rely on client-side scrubbing to sanitize
  pre-JavaScript server logs. Review provider retention separately. This task
  cannot certify those remote settings and did not change QA hosting configuration.
- `index.html` adds no-referrer metadata; existing Vercel config already specifies
  Referrer-Policy: no-referrer and SPA rewrites. Keep SPA fallback working for
  direct `/forgot-password` and `/reset-password` requests. Do not introduce
  query logging in error pages or caching of authenticated Auth responses.
- No data cache/service-worker rule is added; static shell caching is unchanged.
  Real email delivery, exact remote redirect/template behavior, privacy settings,
  production origin, physical-mobile and screen-reader validation remain gates.

## References inspected

Supabase changelog index (no relevant JS password-flow migration identified),
password-based Auth, PKCE and Redirect URLs guides at supabase.com/docs, and
the installed auth-js 2.112.3 source: resetPasswordForEmail,
exchangeCodeForSession, updateUser and signOut. The version-pinned source confirms
recovery verifier tagging/PASSWORD_RECOVERY and local sign-out failure semantics.
