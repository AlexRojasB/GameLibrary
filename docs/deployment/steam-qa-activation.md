# Steam QA activation — proposal, not an executed cloud change

Local stabilization does not enable the deployed QA application. Retain the
working runtime password, Supabase Auth, existing schemas, manual CRUD and
uploads-disabled cleanup configuration. Do not rerun the 11 applied EF migrations.

## Existing API service: future Railway configuration

Only after separate owner approval, on the existing Game Library QA API service:

```text
SteamIntegration__Enabled=true
SteamIntegration__PublicApiBaseUrl=https://game-library-qa-api-qa.up.railway.app
SteamIntegration__FrontendBaseUrl=https://game-library-qa-web.vercel.app
SteamIntegration__WebApiKey=<owner-installed server-only Steam Web API user key>
```

Keep official provider URLs, five-second timeout, ten-minute state lifetime and
server-owned success/failure paths unless separately reviewed. Keep Production
and all E2E authentication/provider switches disabled. The key belongs only in
the API's secret store, never Angular, Vercel, Git, chat or a URL supplied by the
client. Each Game Library user links their own Steam account; testers need not
provide individual API keys. OpenID linking needs no key; preview/import do.

No Steam Supabase login provider, Auth redirect-list change, extra CORS origin,
Storage enablement or new service is required. Preserve the exact existing QA
frontend allowlist. Local API code grants credentialed CORS **only** to the Steam
link-request endpoint so the browser can receive its correlation cookie.

## Proposed narrow Neon grants — NOT APPLIED

Target the existing `game_library_qa_runtime` on the already verified dedicated
QA database. Inspect current effective privileges before any separately approved
write. Existing Library row-lock and CRUD grants remain unchanged. Additional
capabilities needed by current Steam code are:

```sql
GRANT INSERT ON TABLE public.steam_accounts TO game_library_qa_runtime;
GRANT SELECT, INSERT ON TABLE public.steam_link_requests TO game_library_qa_runtime;
GRANT UPDATE (consumed_at) ON TABLE public.steam_link_requests TO game_library_qa_runtime;
```

UPDATE(consumed_at) also satisfies the pending-state SELECT FOR UPDATE permission
check. No UPDATE on Steam account identity, DELETE on link requests, migration-
history access, ownership, role membership, sequence, schema or blanket grant is
needed. Existing SELECT/DELETE on steam_accounts supports status/unlink. Validate
the exact path locally under the reduced runtime role before remote approval.
This is an ACL proposal, not an EF schema change or password-reset instruction.

## Local setup and remaining activation gates

Use isolated local PostgreSQL only for tests; never point test fixtures or E2E
reset at Neon/QA. Keep local frontend/API host names identical (for example
127.0.0.1 on different ports) for HTTP SameSite=Lax correlation. Production uses
Secure, HttpOnly, host-only SameSite=None correlation and HTTPS origins.

QA's Vercel/Railway domains are cross-site. A supported browser may block this
cookie as third-party storage. Such a flow deliberately fails instead of accepting
a transferable URL. Verify hosted browser/PWA behavior and the failure guidance
before activation; do not silently weaken binding or add hosting infrastructure.
The cookie is flow proof, not a login session; existing in-flight consent is
bounded by its SQL deadline rather than automatic Supabase JWT/logout revocation.

After code review, local tests, separate grant/configuration/deployment approval,
and private owner key installation, authorize one bounded real OpenID/preview/
selected-import smoke with explicit cleanup expectations. Verify two-user
isolation, private/empty feedback, repeat imports, manual-entry preservation and
no Play Log creation. Steam game details must be visible to the API caller; a
successful OpenID link does not grant access to otherwise private libraries.
