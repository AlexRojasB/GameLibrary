# Supabase QA — pre-migration verification

2026-11-07. **NO-GO for QA migrations.** Read-only review only; no SQL was
executed, no database was mutated and no remote configuration was changed.

Target: **`yqngxhryzklrhhryzzoi`**. Protected development:
**`iramzxpjbnldhebykzhx`**, excluded from all operations.

## Evidence and limitations

| Check | Current result |
| --- | --- |
| Authenticated QA identity | **Blocked:** discovery of `supabase_qa` now returns zero tools in this session. The 2026-11-06 exact authenticated URL check is historical, not renewed evidence |
| Public signup disabled | **Owner reports manually completed; independently unverified.** Prior `disable_signup=false` observation predates that report and must not be treated as current |
| Data API disabled | **Owner reports manually completed; independently unverified.** No authoritative configuration interface is available; no REST-root status is accepted as proof |
| Auth discovery | **Fresh HTTP 200** from `https://yqngxhryzklrhhryzzoi.supabase.co/auth/v1/.well-known/openid-configuration`; issuer exactly matches QA and advertised JWKS URI matches QA |
| Auth JWKS | **Fresh HTTP 200**, one public EC/ES256 key, no private-key material; requests used HTTPS certificate/hostname validation and rejected redirects |
| Active user JWT/login | Not tested; public key advertisement does not establish the active login signer. Existing API ES256/RS256 validation must remain unchanged |
| Organization, region, plan, formal project status | Unverified through current tools. Connective / game-library remain owner-reported; successful HTTPS is not formal project-status evidence |
| Current QA application schema/history | Cannot refresh without authenticated access. Historical MCP check found zero public tables, no EF history, read-only PostgreSQL 17.11/SSL and zero advisor findings; none is relabeled as a fresh check |
| Direct endpoint DNS | **Fresh:** `db.yqngxhryzklrhhryzzoi.supabase.co` resolves IPv6, not IPv4. DNS does not prove TCP, TLS or database authentication |
| Session-pooler connection | Actual dashboard host/user not available; no runtime-driver login or TLS test |
| Direct migration connection | Proposed direct QA primary, port 5432/database postgres; actual dashboard details, certificate validation, permissions and current target state not verified |
| Feature 013 / E2E flags | Disabled in local QA deployment plan, Dockerfile, backend defaults and frontend QA generator as described below; no hosted effective configuration exists to verify here |

No QA publishable key was retrieved in this task because authenticated QA identity
could not be refreshed. No development MCP/API/database operation, credential
extraction, management-token workaround or MCP configuration change was attempted.
Public HTTPS evidence confirms the named QA issuer, not authenticated account access.

### Dashboard confirmation requested — read only

Open the exact QA reference, reload each page and provide only the saved setting
and project reference (or cropped, credential-free evidence):

1. [QA Authentication → Sign In / Providers](https://supabase.com/dashboard/project/yqngxhryzklrhhryzzoi/auth/providers):
   **Allow new users to sign up = OFF**, email/password **ON**, **Confirm Email ON**,
   anonymous sign-ins **OFF**, unused providers **OFF**.
2. [QA Integrations → Data API → Overview](https://supabase.com/dashboard/project/yqngxhryzklrhhryzzoi/integrations/data_api/overview):
   **Enable Data API = OFF**. This is not the separate automatic-table-exposure
   switch. Do not toggle anything or use REST-root 401 as evidence.
3. [QA General](https://supabase.com/dashboard/project/yqngxhryzklrhhryzzoi/settings/general)
   and [Infrastructure](https://supabase.com/dashboard/project/yqngxhryzklrhhryzzoi/settings/infrastructure):
   confirm name, reference, organization, region and available project status;
   inspect the selected organization's Billing / Subscription for plan only.
   Do not change billing, restart/pause, transfer or buy an add-on.
4. Restore the existing read-only QA MCP connection's availability in this session;
   then require a fresh exact QA `get_project_url` assertion before authenticated
   metadata/settings reads. Never call the development server as a fallback.

No new setting changes are requested or authorized by this review. Dashboard
confirmation would be owner-provided evidence, labeled separately from an automated
read-back. Current authoritative post-change read-back remains unavailable.

## Secure connection configuration — prepared, not installed

### Future Railway runtime

Only the future dedicated `game-library-qa` / `qa` / `game-library-qa-api` service
may receive the server-side secret **`ConnectionStrings__Default`**. Obtain the
actual **Session pooler** details from
[QA Connect](https://supabase.com/dashboard/project/yqngxhryzklrhhryzzoi?showConnect=true&method=session).
Copy the host and username exactly; a pooler host cannot be derived from a region.
The standard pooled username has form `postgres.yqngxhryzklrhhryzzoi`, but that is
an expected pattern, not an observed account/endpoint. Confirm the selected role.

Npgsql key/value template, **not an executable credential**:

```text
Host=<actual-QA-session-pooler-host>;Port=5432;Database=postgres;Username=<actual-QA-session-user>;Password=<private-QA-value>;SSL Mode=VerifyFull;GSS Encryption Mode=Disable;Minimum Pool Size=0;Maximum Pool Size=5;Timeout=15;Command Timeout=30
```

- Supply the complete value through a secure secret interface, never chat,
  frontend variables, command arguments, shell history, build arguments or Git.
  Use `NpgsqlConnectionStringBuilder` or correct Npgsql quoting for passwords
  containing delimiters; URI percent-encoding is not a substitute for Npgsql
  key/value escaping. No password was requested or handled here.
- Keep `SSL Mode=VerifyFull`; no `Require`/`Prefer` downgrade, trust bypass or
  permissive certificate callback. If the CA is not trusted by the final Noble
  runtime, obtain the verified public root certificate and configure Npgsql
  `Root Certificate=<verified-CA-path>` without disabling hostname checks.
- Verify DNS, TCP, certificate chain/hostname and login using Npgsql from the
  intended runtime network. A shared-pooler host alone is not project identity:
  also match the QA-qualified username and dashboard connection details.
- Maximum five pooled connections is per process, not a project-wide cap.
  Inspect actual provider limits; do not change plan or pooler settings here.
- The existing `.env.example` is documentation only; ASP.NET does not load it
  automatically. No Railway variable/service was created or updated.

### Separate operator migration session

Expected direct primary: **`db.yqngxhryzklrhhryzzoi.supabase.co:5432`**, database
**`postgres`**, dashboard-confirmed migration role (normally direct `postgres`).
Require `SSL Mode=VerifyFull` and direct IPv6 reachability from the operator.
No port-6543 transaction pooler, read replica, IPv4 purchase or copied development
connection is permitted. Stop if the approved direct connection is unavailable.

Privately load **`QA_MIGRATION_CONNECTION`** only in an isolated operator process.
`GameLibraryDesignTimeFactory` actually reads **`ConnectionStrings__Default`**, then
`ConnectionStrings__Test`, then a localhost design fallback. Therefore explicitly
map the approved direct value to Default, remove unrelated Test/default fallbacks,
and assert target before any eventual EF execution. Do not put the migration secret
in Railway/Vercel or make it a visible `--connection` argument.

Before migration approval, authenticated **read-only** metadata must confirm:
database/role, `current_schema()` and effective search path resolving to `public`,
primary status (not recovery), session SSL, expected application tables/history
and the migration role's necessary public-schema DDL privileges. Managed
`auth.*`/`storage.*` remain untouched. If unexpected application state exists,
stop for review and appropriate backup/recovery evidence. Do not blindly reset it.
Do not start the API for this check: hosted services can do work at startup.

## All 11 EF migrations reviewed

Source: [`GameLibrary.Core/Data/Migrations`](../../src/backend/GameLibrary.Core/Data/Migrations).
Read every `Up` and `Down`; all **23** migration/Designer/model-snapshot files match
their frozen 407-path source hashes. The previously generated idempotent SQL was
also read; its ordered 11 history IDs match source, with 11 transaction blocks and
15 deterministic Genre seeds. SQL SHA256:
`90d01c192dd653205e06a3ef1faa9aa376ab4da1459a53d7d0df34352bf1be27`.
That SQL was **not regenerated or applied** in this task. This is source/static-SQL
review, not a fresh execution or model-drift test against QA.

| Migration | Reviewed forward effect |
| --- | --- |
| `20260816220833_InitialCreate` | Empty Up/Down; EF history marker only, not a missing-table bug |
| `20260817034441_AddPlatformManagement` | libraries/platforms; unique user and per-Library normalized platform name; restrictive Library FK |
| `20260818012723_AddVideoGameManagement` | games/genres/library_entries/join tables; 1:1 unique Game reference, rating/progress/ownership-state checks, restrictive FKs, 15 Genre seeds |
| `20260818035247_AddBoardGameManagement` | Shared player-range/duration/interaction columns and checks; BoardGame ranges required |
| `20260819183609_RelaxVideoGamePlayerCountConstraints` | Replaces one CHECK to allow optional VideoGame player pairs; both-or-neither and range rules retained; Down can reject existing VideoGame ranges |
| `20260825025118_AddPlayLogEntries` | play_log_entries with cascade to LibraryEntry and ownership-query FK index |
| `20260825034331_AddPlayLogDurationMinutes` | Nullable positive duration_minutes CHECK |
| `20260826034524_AddSteamIntegration` | Nullable Steam AppID and per-Library filtered uniqueness; steam_accounts/link_requests with uniqueness, hash and expiry checks |
| `20260827154518_AddSteamPlaytimeTracking` | Nullable non-negative bigint playtime; no automatic Play Log records |
| `20261028185426_AddFamilyFriendly` | Required boolean default false, including existing rows |
| `20261029134813_AddCoverUploads` | Nullable unique restrictive cover reference, cover_uploads lifecycle/locator/date/size constraints, external/upload exclusivity, due index and explicit Data API privilege revocation |

Review conclusions and execution caveats:

- No forward table/column deletion, external calls, user creation, auth/storage
  managed-schema mutation, role creation/alteration or Storage provisioning.
  One existing CHECK is deliberately replaced. Down paths are destructive and
  **not an approved recovery procedure**.
- The cover migration revokes PUBLIC and known Data API role privileges on
  `cover_uploads` and fails if checked privileges remain inherited. Do not remove
  the guard to make deployment pass. The actual Supabase-role/default-privilege
  result still needs verification after a separately approved migration.
- Application authorization and cross-record Domain invariants remain enforced
  by Core/API; these migrations do not establish a second RLS security model.
  **Saved Data API disablement must be verified before any application DDL.**
- Migrations create the integrated 011–013 schema even with those integrations
  disabled. Do not omit migrations or introduce a second Supabase schema workflow.
- Idempotency checks EF history, not arbitrary schema drift. History table creation
  precedes the per-migration transactions, and a failure can leave earlier
  migrations committed. Reconcile actual primary/schema/history after an uncertain
  outcome; no blind retries, Down, reset or restore.
- Fresh local tool check: **EF CLI 10.0.7**, project EF runtime/Design **10.0.11**,
  Npgsql EF provider **10.0.3**. Align the operator EF CLI with 10.0.11 under separate
  tool-setup approval, then regenerate/review the exact release SQL and pin the
  selected source identity before hosted execution. No tool upgrade was performed.

## Exact known Auth values and unresolved browser URLs

| Setting | Prepared value / status |
| --- | --- |
| Frontend `QA_SUPABASE_URL` | `https://yqngxhryzklrhhryzzoi.supabase.co` |
| Expected issuer | `https://yqngxhryzklrhhryzzoi.supabase.co/auth/v1` |
| API `Authentication__Schemes__Bearer__MetadataAddress` | `https://yqngxhryzklrhhryzzoi.supabase.co/auth/v1/.well-known/openid-configuration` |
| JWKS | `https://yqngxhryzklrhhryzzoi.supabase.co/auth/v1/.well-known/jwks.json` |
| Audience / HTTPS metadata | `authenticated` / `RequireHttpsMetadata=true`; retain ES256/RS256, issuer/signature/lifetime/usable-sub validation |
| Site URL | **Unknown** until owner confirms stable HTTPS QA frontend URL; current saved dashboard value not read |
| Allowed redirect URLs | **Unknown**, must be exact required QA destinations including path/trailing slash, no wildcard previews/localhost/development/BarBook |
| API `AllowedOrigins__0` | Exact QA frontend origin, not yet assigned; no path/trailing-slash CORS mismatch |
| Frontend `QA_API_BASE_URL` | Exact QA API origin, not yet assigned; no fabricated Vercel/Railway hostname |
| Frontend publishable key | QA-only modern publishable key via future secure build interface; no server key/DB string in browser |

Reviewed client currently signs in with email/password, supplies no `redirectTo`
or `emailRedirectTo`, and has **no dedicated auth callback or password-recovery
route**. Do not invent `/auth/callback` or claim invitation/recovery support.
Without an explicit redirect, email flows use the configured Site URL; the guarded
root route and actual confirmation/invitation session behavior need later testing.
Read back [QA URL Configuration](https://supabase.com/dashboard/project/yqngxhryzklrhhryzzoi/auth/url-configuration)
once exact origins and the tester flow are agreed; **do not save changes now**.
The checked-in API default still points at development metadata, so a verified QA
override is mandatory before eventual startup; no development setting was edited.

Tester preparation: two distinct, owner-approved **QA-only** accounts for isolation
checks; no copied development users/data and no credentials in Git/chat. Determine
their administrative creation/invitation and confirmation/password flow before
separate user-creation approval. With default SMTP, recipients must be existing
organization-team addresses; official docs currently describe a two-message/hour
limit and best-effort delivery. Actual SMTP/recipient eligibility is unknown.
Do not grant organization membership merely to get email delivered, disable email
confirmation, change SMTP, send invites/recovery mail or create users in this task.

## Feature 013 and E2E remain disabled

Verified local plan/Dockerfile values: `ASPNETCORE_ENVIRONMENT=Production`,
`CoverUploads__Enabled=false`, `CoverUploads__AllowLocalHttp=false`,
`E2E__Auth__Enabled=false`, `E2E__CoverUploads__Enabled=false`,
`E2E__CoverImageSearch__Enabled=false`, `E2E__SteamIntegration__Enabled=false`,
and `SteamIntegration__Enabled=false`. The frontend QA generator always emits
`e2e.enabled=false` and permits only public configuration; backend defaults also
disable uploads/E2E. Production rejects fake cover Storage outside its safety gate.
No Storage credentials/bucket are needed or provisioned for this path.

**Important:** disabling new uploads does not disable the registered cleanup
BackgroundService or remove schema requirements. It queries `cover_uploads` at
startup and periodically, and can mutate existing due rows. Do not start the API
against an unverified/unmigrated QA database as a connectivity probe. This is an
operational caveat, not an implementation change made by this review.

## Decision and exact next gates

**NO-GO for migrations**, despite completed local review. Hard migration blockers:

1. Renew authenticated exact-QA identity and current read-only primary/schema/EF
   history checks; historical empty-target evidence is insufficient.
2. Obtain reloaded saved-setting evidence for signup OFF/Data API OFF and Auth
   preservation. The owner's completion report is recorded, not assumed verified.
3. Confirm available org/region/plan/status metadata and actual QA Connect values;
   verify direct operator Npgsql TLS/hostname/login/DDL readiness, effective public
   schema and current target state. Resolve unexpected data with reviewed recovery
   evidence, not a reset. No credentials should be pasted into chat.
4. Approve/perform isolated operator EF tool alignment, then regenerate and review
   SQL for the exact selected immutable source; preserve all source/index changes.
5. Obtain **explicit QA migration-execution approval** after these checks. The
   present task explicitly forbids database mutation.

Separate **pre-startup/private-testing gates**, not a reason to create resources
now: verified session-pooler connectivity in the final Railway runtime; exact QA
frontend/API origins, saved URL/redirect/CORS settings and QA issuer override;
approved testers/confirmation/email delivery; actual user-token/JWT flow and later
two-user isolation plus REST/GraphQL application-data denial checks. URL changes,
secret placement, user creation/SMTP work and provisioning/deployment each remain
outside this read-only task and require their applicable explicit approvals.

Verification: 10 offline Railway checks and 15 QA build-configuration tests passed;
JSON, migration-source/history-ID consistency, whitespace and relative links were
checked. No application build, EF generation/model-drift test, live PostgreSQL
driver login or integration suite was rerun. The migration/SQL review is not an
assertion that hosted permissions or schema application will succeed. Repository
preservation is checked against the task-entry file/mode, HEAD, raw index and staged
diff baseline, including the unchanged frozen 407 source paths.

Sources: [Supabase connections/TLS](https://supabase.com/docs/guides/database/connecting-to-postgres),
[Npgsql TLS](https://www.npgsql.org/doc/security.html),
[Auth configuration](https://supabase.com/docs/guides/auth/general-configuration),
[Data API toggle](https://supabase.com/docs/guides/api/securing-your-api),
[SMTP restrictions](https://supabase.com/docs/guides/auth/auth-smtp),
[Auth redirects](https://supabase.com/docs/guides/auth/redirect-urls).
