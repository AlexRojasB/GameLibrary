# Existing Supabase `game-library` — private QA preparation

**2026-11-07 update:** the owner reports both toggles were completed manually.
The current session exposes no `supabase_qa` tools, so authenticated identity and
saved post-change settings cannot be refreshed. Public QA Auth discovery/JWKS pass;
the 11 migrations are locally reviewed. **NO-GO for migrations.** See the current
[pre-migration verification and exact remaining gates](qa-pre-migration-verification.md).
The 2026-11-06 observations/instructions below are historical; in particular,
`disable_signup=false` is not a fresh observation after the owner's manual changes.

2026-11-06. **The owner approved only disabling public signup and Data API on QA.
Neither toggle has been applied; dashboard handoff is required. NO-GO for migration
or deployment.** Other configuration is preparation only.
The owner reports creating `game-library` in **Connective**, with exact QA reference
**`yqngxhryzklrhhryzzoi`**. Public HTTPS discovery and the authenticated
`supabase_qa.get_project_url` both confirm that exact target. The read-only database
responds; remaining billing/security/runtime-connection gates are still pending.
This replaces the proposal to
create a Supabase project named `game-library-qa`; do not create another project.
These names are owner-reported, not yet independently discovered account metadata.

## Verified evidence and unresolved identity

| Item | Result |
| --- | --- |
| Original `supabase` MCP configuration | **`iramzxpjbnldhebykzhx` — protected development**; preserved and not called in this QA connection task |
| Separate project-local `supabase_qa` | **Connected**, target **`yqngxhryzklrhhryzzoi`**, `read_only=true`; ten tools, no migration tool, SQL runs read-only |
| Authenticated QA `get_project_url` | **`https://yqngxhryzklrhhryzzoi.supabase.co`**, exact expected match; original server was not called |
| Account metadata | Project-scoped MCP excludes account lookup tools; organization/region/plan need authorized metadata access or dashboard evidence |
| Local authenticated fallback | No `supabase` CLI executable or Supabase access-token environment variable available; no credentials extracted or tool installed |
| Existing QA project name / organization | Owner reports `game-library` / Connective |
| Exact QA reference | **`yqngxhryzklrhhryzzoi`**, supplied by owner; opaque platform ID field remains unverified/null |
| Separation from development | References differ and the authenticated QA connection returns the expected distinct reference |
| Organization ID/slug, region, plan, service status | **Unverified** |
| Public QA Auth discovery/JWKS | Both **HTTP 200**, HTTPS certificate/hostname verification passed; no credentials sent |
| Public signing metadata | Discovery advertises RS256/HS256/ES256; JWKS advertises **one ES256 key**. Active user-token signer is not established by public key advertisement |
| PostgreSQL availability | Expected direct host resolves IPv6 only. No runtime/operator driver TCP/TLS/login test; runtime pooler host/user still unknown. MCP SQL verification is separate below |
| MCP database round trip | **Passed**, database `postgres`, PostgreSQL **17.11**, `supabase_read_only_user`, `transaction_read_only=on`, server SSL on and MCP backend session SSL true |
| Application schema | `public` has **zero tables**; `public.__EFMigrationsHistory` is absent. No migration was applied |
| Security/performance/health advisors | **Zero findings** returned for each; not a waiver of QA configuration gates |
| Public Auth settings | **HTTP 200** using an enabled QA publishable key privately in memory; email on, email autoconfirm off, anonymous/phone/other external providers off, **public signup allowed** (`disable_signup=false`) |
| Data API probe | REST root returned **401**, no OpenAPI document or application rows. This does **not** prove the Data API is disabled |

Only a fixed read-only PostgreSQL metadata query, public-schema metadata, advisor
results and public Auth settings were read on QA after target verification. No
application/Auth user rows, passwords, private keys, server keys or credential
values were printed or persisted. A QA publishable key was obtained through the
verified QA server and used privately for the Auth-settings/REST probes, with no
privileged/legacy-key fallback. The only connection change was adding a separate
project-local, read-only QA server; the original/global connections were preserved.
No remote mutation, migration, provisioning or deployment occurred. No request to
the protected project's API, database or MCP tools was made in this task. Public
QA discovery alone does not prove platform status. The separate authenticated
URL/SQL checks establish QA access and database responsiveness, not the platform's
formal ACTIVE status, region, subscription, runtime pooler or direct-driver TLS.

## Immediate safe handoff

1. **Completed:** the owner signed in through OpenCode's MCP interface; no PAT,
   password, callback code or OAuth credential was requested in chat or extracted.
   No interactive auth shell was started.
2. **Completed:** discovered `supabase_qa` tools and verified **that server's** URL
   equals `https://yqngxhryzklrhhryzzoi.supabase.co` before database/settings reads.
   Keep this assertion before future QA operations. Do not call the original
   `supabase` server to test QA; its development configuration remains unchanged.
3. Confirm the name/org/region/plan/service status in
   <https://supabase.com/dashboard/project/yqngxhryzklrhhryzzoi/settings/general>
   and Connective's Billing/Subscription, or separately authorized account metadata.
   Keep those fields null until verified. Project-scoped URL discovery alone cannot
   verify them, even after sign-in. Do not include credential-bearing screenshots/URLs.
4. **Partly completed:** metadata confirms an unmigrated public application schema,
   a responsive read-only database and zero advisor findings. Public signing/Auth
   settings are recorded, but active user-token verification and the Data API
   toggle remain unverified. Project tools do not expose all Auth/plan settings;
   use dashboard/authorized metadata for the remainder. Only the two security
   toggles below are approved; the current MCP cannot apply them.

The current local plan is
[`deploy/qa/supabase-project-plan.json`](../../deploy/qa/supabase-project-plan.json).
It records the exact supplied QA reference and publicly verified HTTPS endpoints,
protects development and leaves unverified opaque ID/org/region/plan fields **null**,
with generic remote execution off and the narrow, unapplied two-toggle approval
recorded separately. `identityVerified` now records the authenticated
QA reference check, not independent confirmation of the owner-reported display
name, organization, region or plan. Public metadata and authenticated database
evidence remain separately labeled.
It is review metadata, not an executable or auto-applied Supabase configuration.

Only the **Supabase** project choice changed. Keep the proposed Railway project
`game-library-qa`, API `game-library-qa-api`, environment `qa`, and Vercel project
`game-library-qa-web` names unchanged. Vercel's separately verified Hobby workspace
access is not Supabase organization/plan evidence. No application environment,
checked-in development default or live provider setting was rewritten.

### Configured OpenCode V2 connection

The documented CLI was used from this project:

```sh
opencode mcp add supabase_qa --url 'https://mcp.supabase.com/mcp?project_ref=yqngxhryzklrhhryzzoi&read_only=true&features=docs,development,database,debugging'
opencode mcp list
```

The first command completed, adding only `mcp.servers.supabase_qa` to the existing
local `opencode.json`. All prior settings were preserved as parsed JSON; the global
development server definition was not changed. This connection uses OAuth, with
no credential headers or token values in configuration. It enables only docs,
development metadata, read-only database and debugging groups; no branching,
Functions, Storage or account-management group. It is not a schema migration tool
authorization: do not call mutation tools even if exposed by a future catalog.

Initially `opencode mcp list` returned **needs authentication** for `supabase_qa`.
After the owner completed sign-in, the server exposed ten read-only tools and its
authenticated URL check passed. Configuration, sign-in and reference verification
are now **complete**. Use `/mcps` if future authentication is required, not an
interactive shell auth command. Config reloads
automatically; `/reload` can refresh tools if needed, without restarting the
background service. No development API/MCP operation is needed for any of this.

The local `opencode.json` remains excluded from the proposed application snapshot;
do not publish local provider configuration or credentials with the QA source.

## QA-only configuration review

### Approved security toggles — dashboard handoff

The security-configuration task freshly reconfirmed the authenticated QA URL and
the ten-tool read-only catalog. It has no supported Auth/Data API configuration
interface or account metadata tools; no Supabase CLI is installed. MCP remains
read-only. **No remote setting has been changed.** Do not extract OAuth credentials,
use SQL against managed schemas, install a new management path or remove read-only
mode to work around this limitation.

Before **each** save, require a fresh successful `supabase_qa.get_project_url`
equal to `https://yqngxhryzklrhhryzzoi.supabase.co`, and independently check the
dashboard URL contains `/project/yqngxhryzklrhhryzzoi/`. Stop on any mismatch,
including protected development `iramzxpjbnldhebykzhx`.

1. **Confirm available platform metadata without changing it.** Open
   [QA General settings](https://supabase.com/dashboard/project/yqngxhryzklrhhryzzoi/settings/general).
   Confirm project reference/name and Connective organization. Inspect
   [QA Infrastructure](https://supabase.com/dashboard/project/yqngxhryzklrhhryzzoi/settings/infrastructure)
   for region/service information and the selected organization's
   **Billing / Subscription** for its plan. Record only non-secret values actually
   shown; organization ID/slug, region, plan and formal service status remain
   unverified here. Do not change billing, pause/restart or transfer the project.
2. **Disable public signup only.** After the identity check, open
   [QA Authentication → Sign In / Providers](https://supabase.com/dashboard/project/yqngxhryzklrhhryzzoi/auth/providers).
   Under general/user-signup settings, turn **Allow new users to sign up** **OFF**.
   Save that setting if the UI requires a save. Do not disable email/password,
   change **Confirm Email**, enable providers, change SMTP or create/invite users.
   Reload the page and read back **Allow new users to sign up = OFF**. Record the
   QA reference, time and resulting setting without keys, tokens or personal data.
   A supplemental read-only `/auth/v1/settings` check should then report
   `disable_signup=true`, `mailer_autoconfirm=false`, and email enabled. Do not test
   this by submitting a real signup.
3. **Disable Data API only.** Repeat the authenticated/dashboard identity checks,
   then open [QA Integrations → Data API → Overview](https://supabase.com/dashboard/project/yqngxhryzklrhhryzzoi/integrations/data_api/overview).
   Turn **Enable Data API** **OFF**, confirming/saving only that change if prompted.
   Reload/reopen the integration and read back **Enable Data API = OFF** from the
   saved project setting. If already off, record it as verified, not newly changed.
   Do not disable Auth, rotate keys, alter grants/RLS/schemas or substitute a
   different integration switch. A REST-root **401 is not evidence** of this toggle.
4. **Read back preservation after each change.** Confirm email/password remains
   enabled and **Confirm Email** remains on. Read-only QA Auth settings, discovery
   and JWKS must still respond and discovery must identify the QA issuer. Do not
   change application JWT validation (ES256/RS256, HTTPS metadata, issuer, audience,
   lifetime and signature checks). No user-token test is claimed until separately
   approved testers are available. Preserve current URLs/redirects and SMTP.

If the dashboard does not show these exact settings or either read-back fails,
stop and report the observed state; do not guess a replacement operation. Return
non-secret saved-setting evidence for both toggles. Current authoritative
post-change evidence is **absent**. Fresh pre-change public checks still show
`disable_signup=false`, email enabled, confirmation required, no anonymous/phone/
other external providers, HTTP 200 discovery/JWKS, the matching QA issuer and one
advertised ES256 public key. This does not establish an active login signer or
formal platform status. No new REST probe was needed; the earlier 401 remains
inconclusive. No post-change verification is claimed.

### Prepared values — do not apply yet

- In [QA Auth URL Configuration](https://supabase.com/dashboard/project/yqngxhryzklrhhryzzoi/auth/url-configuration),
  **Site URL** will be the owner-confirmed stable HTTPS QA frontend URL. Add only
  the exact redirect destinations required by the reviewed client flows, matching
  path and trailing slash. No origin or callback path has been assigned here;
  leave plan values null/empty until known. No localhost, wildcard previews,
  development or BarBook URLs. Do not save URL changes in this task.
- API `AllowedOrigins__0` will be that exact frontend **origin** (no path).
  Frontend `QA_API_BASE_URL` waits for the verified QA API URL;
  `QA_SUPABASE_URL=https://yqngxhryzklrhhryzzoi.supabase.co` and
  `QA_SUPABASE_PUBLISHABLE_KEY` must come from QA only, through the future secure
  build interface. No checked-in development defaults are changed.
- Prepared API metadata override:
  `Authentication__Schemes__Bearer__MetadataAddress=https://yqngxhryzklrhhryzzoi.supabase.co/auth/v1/.well-known/openid-configuration`.
  Keep `RequireHttpsMetadata=true`, audience `authenticated`, existing JWT checks,
  uploads/E2E/Steam disabled. Variable names are in
  [`deploy/qa/.env.example`](../../deploy/qa/.env.example); no environment was set.
- Prepare an owner-approved roster of **two distinct QA-only testers** for later
  isolation checks. Keep email/password details out of the repository/chat, verify
  their eligibility under existing SMTP restrictions, and decide their supported
  invitation/confirmation flow before separately authorizing creation. Do not
  create users, invite, send recovery mail, alter membership or modify SMTP now.
- Prepare the actual QA **session-pooler** host/username from **Connect**, port 5432,
  plus the existing password privately. The future server-only Npgsql value is
  `Host=<QA-session-host>;Port=5432;Database=postgres;Username=<QA-session-user>;Password=<private-value>;SSL Mode=VerifyFull;GSS Encryption Mode=Disable;Minimum Pool Size=0;Maximum Pool Size=5;Timeout=15;Command Timeout=30`.
  Placeholders are not executable values; never use a copied development string.
  Direct migration connection is separate (see below), not authorized to execute.

### PostgreSQL and TLS

- Runtime: actual QA **shared session-pooler** host/user from **Connect**, port
  **5432**, Npgsql key/value format, server-only `ConnectionStrings__Default`.
  Do not invent a pooler hostname from the region or use a copied development URL.
- Require **`SSL Mode=VerifyFull`**, valid hostname/trust chain in the final API
  runtime, `GSS Encryption Mode=Disable`, pool minimum 0/maximum 5 and initial
  15-second connection / 30-second command budgets. Do not weaken certificate checks.
- Operator migrations: direct **QA primary** with TLS. Free direct endpoints use
  IPv6; availability from the chosen operator environment is a later verification
  gate. Stop if unavailable; do not substitute port-6543 transaction pooling or buy
  an IPv4/paid add-on. The direct hostname's IPv6 DNS resolution is confirmed,
  but that is not TCP reachability, database TLS validation, successful Npgsql
  authentication or a migration-ready session. Public HTTPS TLS checks do not
  validate PostgreSQL TLS.
- An existing project does not prove Free billing or an empty database. Verify the
  actual Connective subscription, add-ons, service state and target schema before
  use. No billing, role, project transfer, restart or password reset is authorized.

### Auth, issuer and browser configuration

- Use **only QA Auth identities**, email/password and approved test users. Public
  signup, anonymous sign-in and unused providers **must be disabled** in the desired
  QA configuration; public signup is not disabled yet. Retain email confirmation.
  Existing signup UI may intentionally be denied once private QA is configured.
- Verify current **ES256 or RS256** signing with public JWKS. API code already
  restricts production validation to those algorithms; do not compensate for HS256
  by adding a signing secret/test bypass. No development key rotation is allowed.
- Override the API's checked-in development metadata address with the verified QA
  HTTPS discovery endpoint; validate the matching issuer, signature, lifetime,
  usable `sub` and audience **`authenticated`**. Never authorize from user-editable
  metadata or a client-supplied owner ID; existing Library isolation remains authoritative.
- Set exact QA Site URL, approved redirect URLs and API CORS origin after the
  actual stable frontend origin is known. No localhost, wildcard previews or
  BarBook origins. No hostname is guessed from the proposed Vercel project name.
- Frontend build receives only `QA_API_BASE_URL`, `QA_SUPABASE_URL` and the correct
  QA `QA_SUPABASE_PUBLISHABLE_KEY`. The existing build rejects the development
  project. No server secret, database connection or signing key enters the browser.
- Default SMTP limits recipients to organization-team addresses with tight rate
  limits and best-effort delivery. Verify approved tester email delivery; do not
  change membership/roles, disable confirmation or add custom SMTP without approval.

### Data API, Storage and managed schemas

- **Disable the QA Data API before application migrations**, preserving Auth and
  direct backend PostgreSQL access. Later verify REST and GraphQL cannot access
  application data with publishable keys or user JWTs. Do not rely on changing
  automatic-grant defaults or equate a public client key with database isolation.
- No direct Angular table access, second schema workflow or unapproved RLS changes.
  If application data remains exposed, stop rather than proceed without the
  approved boundary. Never re-enable the Data API as a convenience workaround.
- The observed REST-root **401** does not establish the toggle or application-data
  isolation. No privileged key was used to bypass that denial. Verify the QA Data
  API dashboard setting and later REST/GraphQL data denial before migration/traffic.
- Keep uploads, all E2E modes and initial Steam integration **off**. No Storage
  bucket, secret/service-role key, copied cover references or production data is
  needed for initial manual QA. Do not alter Supabase-managed `auth.*`/`storage.*`.

### EF migration procedure — review only

1. Use the owner-approved immutable application source and existing **11 EF Core
   migrations**, through `20261029134813_AddCoverUploads`; review generated SQL
   and target state. An empty new-project claim is not permission to overwrite
   unexpected tables or history. Verify appropriate recovery evidence if populated.
2. Check/align the operator's EF tool with the approved runtime version under a
   separately approved tool setup. The previously observed 10.0.7/10.0.11 mismatch
   is not silently fixed here; no package/tool upgrade was performed.
3. Privately load the **QA-only direct** `QA_MIGRATION_CONNECTION`. The EF factory
   reads **`ConnectionStrings__Default`**, then `ConnectionStrings__Test`, not
   `QA_MIGRATION_CONNECTION`. Map the operator secret explicitly to Default in an
   isolated process, remove unrelated defaults/test configuration, and verify the
   actual QA target. Do not pass a secret as a visible CLI argument or log its value.
4. Only after separate migration approval apply the reviewed EF workflow to that
   verified QA primary. No startup/predeploy migration, Supabase application-schema
   migration, manual application-table dashboard edit or destructive reset.
5. Verify EF history and actual target/schema afterward. Reconcile an unknown
   COMMIT outcome before retrying; exceptions do not prove rollback. Do not run EF
   Down, restore or wipe without separately approved recovery instructions.

## Values to provide through secure interfaces — not in chat

| Value | Source and future destination |
| --- | --- |
| QA database password / runtime connection | Existing QA project password in the owner's secret store; actual session host/user from Connect; enter the complete Npgsql value in the **future dedicated Railway service's secret variable** `ConnectionStrings__Default` after approval |
| Direct operator connection | QA Connect details plus privately held password; inject `QA_MIGRATION_CONNECTION` only in the isolated operator environment, never Vercel or the API's migration startup |
| QA project URL and publishable key | Verified QA Settings/API Keys; enter `QA_SUPABASE_URL` and `QA_SUPABASE_PUBLISHABLE_KEY` through the **future Game Library Vercel project's** environment interface; public client data, but do not read it from the wrong MCP target |
| API/issuer/origin metadata | Exact QA API/frontend origins and QA discovery URL; configure only the dedicated QA projects after approval |
| Test user passwords/invitations | QA Auth dashboard/supported Auth administration only after approval; never commit or print credentials |
| Root CA certificate, if needed | Public trust material from the verified QA database settings, with issuer/hostname verification; not a password or private key |

No Supabase service-role/secret key, JWT signing private key or Storage credential
is requested for this uploads-disabled QA path. Do not reset existing credentials
to supply missing values without explicit approval. No private/server secret was
retrieved. The public QA publishable key was used only in memory for the described
read-only checks; no key value was printed, persisted or placed in frontend config.

## Remaining readiness gates and preservation

**GO for read-only QA access; NO-GO for migration/deployment.** Authenticated target,
reference separation, public discovery/JWKS TLS and the MCP read-only SQL round
trip are verified. Organization/region/plan/formal status, runtime/direct-driver
connectivity/TLS, active token flow, redirects/SMTP and Data API disablement remain
pending. **Public signup is currently enabled**, contrary to the private-QA plan;
disabling it and Data API are now explicitly approved but **not applied** because
the current tools cannot safely change those settings. Project creation
and Free-slot planning are superseded for this selected existing project; no new
creation is needed. Billing must still be checked, not assumed from successful
manual creation. No provider changes follow automatically from this document.

Next actions are dashboard execution/read-back of the two already-approved toggles
and verification of missing metadata/connections. Separate approvals remain for
applying prepared URLs/redirects, testers/secret placement, reviewed EF execution; then
bounded two-user/login/JWKS/CORS/Data-API-denial/CRUD verification and separate
deployment approval. No development operation is permitted by any of those gates.

Only local plan/readiness metadata is updated. The frozen 407-path application
source, exact dependency candidate and durable historical manifests/checksums
remain unchanged. Add this plan/report to an explicitly reviewed companion list
and regenerate the future publication manifest before staging; do not overwrite
historical 418-path evidence or claim it includes this new project selection.

Prior connection-task verification passed: JSON/target-isolation/unknown-value checks, all **10**
offline Railway checks and **15** QA build-configuration tests. Of **424** task-entry
files, only the QA plan/readiness report, security-preparation report and local
`opencode.json` were updated; the other **420** files/modes, HEAD, raw index and
staged diff were preserved. No new repository file was added. Existing OpenCode
settings were preserved; only the separate QA server entry was added.
The 407 frozen application hashes and both durable checksum sets still matched.
No frontend/backend build or runtime/operator PostgreSQL connectivity test is
claimed for this metadata/connection-only change. The authenticated MCP read-only
database and public QA HTTPS checks above are fresh results.

Security-toggle handoff task: only this readiness report and the existing QA plan
were edited. The separate approval record does not enable generic remote writes.
HEAD, raw Git index, staged diff and all other task-entry files/modes are preserved;
no application source or local/global MCP configuration was changed. The two
toggles, users, SMTP, EF migrations, Railway/Vercel provisioning and deployment
remain unperformed. Relevant offline checks are rerun for this documentation-only
handoff; no new application build or live PostgreSQL driver test is claimed.

Sources: [MCP project scoping](https://supabase.com/docs/guides/getting-started/mcp),
[PostgreSQL connections/TLS](https://supabase.com/docs/guides/database/connecting-to-postgres),
[Data API controls](https://supabase.com/docs/guides/api/securing-your-api),
[JWT signing keys](https://supabase.com/docs/guides/auth/signing-keys),
[SMTP restrictions](https://supabase.com/docs/guides/auth/auth-smtp).
Signup labels and behavior: [Auth general configuration](https://supabase.com/docs/guides/auth/general-configuration).
Exact URL matching: [Auth redirect URLs](https://supabase.com/docs/guides/auth/redirect-urls).
