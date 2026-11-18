# Private QA deployment plan — approval required

**Hosting decision superseded, 2026-11-06:** the owner selected Railway, not Render.
Use [the Railway GO/NO-GO and runbook](private-qa-railway.md). This document retains
historical Render evidence; its Render/$0 assumptions are not the selected plan.

**2026-11-06 update:** see the [latest preflight](private-qa-preflight.md) for
authenticated Render access, the observed `need_payment_info` gate, the corrected
Free shutdown setting, current dependency candidate and controlled root tests.
It supersedes the older status/candidate statements below; deployment is not approved.

Inspected 2026-11-05. **Local preparation only; nothing deployed or provisioned.**
Target: Vercel Hobby static Angular PWA → Render Free .NET 10 Docker API → a
**separate Supabase Free PostgreSQL/Auth project**. Expected $0/month only within
the providers' shared free quotas. No paid trial, billing change or overage approved.

Local results and unresolved checks: [verification report](private-qa-verification.md).

This is a proposal for personal, authenticated QA with uploads **disabled**, not a
Feature 013 release or a declaration that its G01–G07 release gates are complete.
The existing README/Phase 1/Phase 2/G06 release warnings remain in force. Explicit
approval of this limited QA deployment is required before any external mutation.

## 1. Connections and repository snapshot

| Provider/tool | Observed access | Remaining requirement |
| --- | --- | --- |
| GitHub | Official `gh` CLI authenticated as `AlexRojasB`, repository ADMIN; no GitHub MCP | Owner must publish an approved, reviewed snapshot; this task never stages/commits/pushes |
| Vercel | Official MCP connected as `proxtos-5361`; account reports active Hobby. One unrelated `bar-book-web` project visible; Git deployment discovery returned no teams | Default team lookup (`bar-book`) returned 403. Confirm the correct QA Hobby owner/team and authorize that scope; don't deploy into the unrelated project |
| Render | No configured MCP, installed CLI, or `RENDER_API_KEY` environment variable | Account/workspace not verified; authenticate official MCP/CLI or use dashboard |
| Supabase | Official MCP connected, scoped to existing `iramzxpjbnldhebykzhx` project; no installed CLI/PAT environment variable | QA project/Free organization/available project slot not discovered. Existing table inspection failed: database possibly inaccessible. Empty security-advisor output is not proof the DB is secure or online |
| Other MCPs | Playwright connected; Neon awaits authentication; Railway not logged in | Neither Neon nor Railway is part of this deployment |
| Local tools | Docker host `linux/x86_64`, .NET SDK 10.0.201, Node 24.15.0, npm 11.12.1 | `dotnet-ef` 10.0.7 differs from EF runtime 10.0.11; align before remote migrations |
| Skills | OpenCode, Supabase, Supabase Postgres best practices loaded; Report also available | No Render/Vercel/GitHub deployment skill installed by this task |

Vercel/Render/Supabase CLIs and standalone `psql` are absent. OAuth/CLI credential
stores are separate: connected MCP does not imply authenticated CLI. No secrets
were requested or printed. Existing user-secret **names** were inspected only;
development database/Brave/Steam secrets must not be copied to QA automatically.

The repository is public: `AlexRojasB/GameLibrary`, default branch `main`. Current
branch `dev` is two commits ahead of its tracking branch and has extensive staged,
unstaged and untracked Feature 011–013 work. Git hosting cannot deploy this dirty
worktree as an immutable commit. Nothing was reset, cleaned, staged, committed or
pushed. Staged-diff SHA256 before preparation:
`62092d1ed1bbe35197e650619a83a5a9eed14a4495a4223a795223ed11372439`.

### Official connection paths (not executed)

- Render hosted MCP: `https://mcp.render.com/mcp`, supports OAuth. In OpenCode V2,
  owner can run `opencode mcp add render --url https://mcp.render.com/mcp`, then
  `/mcps` to sign in. Select the approved workspace and rediscover actual tools.
- Render CLI fallback: official `brew install render`, then interactive
  `render login`; inspect `render help` before use. REST API fallback is
  `https://api.render.com/v1/`, requiring private authentication.
- Vercel: keep official MCP; authorize only the selected QA scope. Official CLI is
  a fallback, not currently installed/authenticated. Do not reuse a CLI default team.
- Supabase: current MCP is project-scoped, not proof of organization/project-create
  access. Provision via the dashboard or authorized Management API after approval,
  then connect MCP to QA. Do **not** use a paid Supabase development branch.
- Supabase CLI v3 shipped November 2: hosted targets/confirmation behavior changed.
  Inspect its installed `--help` if later needed. Do not introduce Supabase schema
  migrations; EF remains the sole application schema authority.

## 2. Proposed resources and costs

Names/domains are proposals, not reserved or confirmed available.

| Resource | Proposed name | Plan/settings |
| --- | --- | --- |
| Frontend project | `game-library-qa-web` | Vercel Hobby, root `src/frontend`, Node 24.x, static files only |
| API web service | `game-library-qa-api` | Render Hobby workspace + **Free compute**, Docker, one instance, Oregon proposed |
| Database/Auth project | `game-library-qa` | Separate Supabase **Free organization/project**, region near API, fresh test data/identities |
| Storage | None now | Reserve `game-covers` for a later, separately approved verification; no bucket/credential/policy work |

Select regions before creation. Supabase region/organization quota availability is
not yet known. Never create the QA project inside a paid organization while
assuming its compute will be free. No Redis, Render Postgres, disks, workers,
cron, monitoring subscription, paid SMTP, custom domain or image CDN is proposed.

Published limits checked November 5 (recheck account-specific quotas at approval):

- **Vercel Hobby:** personal/non-commercial use; 100 GB Fast Data Transfer,
  1 million CDN requests/month, 100 deployments/day, one concurrent build. Usage
  exhaustion generally pauses the feature until its window resets. No SSR/functions
  are needed. Dashboard-only free deployment protection may be used if compatible
  with personal mobile/PWA testing; no paid password-protection add-on.
- **Render Free:** 512 MB RAM, 0.1 CPU, 750 shared instance-hours/month; sleeps after
  15 minutes idle, roughly one-minute cold start, arbitrary restarts and ephemeral
  disk. Hobby currently includes 5 GB outbound bandwidth and 500 pipeline minutes.
  External DB/API traffic can trigger discretionary suspension. Free has no SSH,
  one-off jobs or persistent disks; paid pre-deploy infrastructure is not required.
  Keep no payment method/paid overages for the strict $0 path where supported;
  verify billing controls with the owner. With a payment method, bandwidth can be
  billed ($0.15/GB) and extra build minutes can be billed. Free compute alone is
  **not** a universal spending cap. No keep-alive service to defeat sleeping.
- **Supabase Free:** two active Free projects, 500 MB database/project, 50,000 MAU,
  5 GB origin egress, 5 GB cached egress and 1 GB Storage; shared quotas still apply.
  Inactive projects pause after a week; automatic backups/PITR are not included.
  Default Auth SMTP sends only to pre-authorized organization-member addresses,
  currently two messages/hour. Personal QA must account for this without disabling
  email confirmation. No paid IPv4 add-on or Supabase branching.

## 3. Feasibility and security boundaries

| Check | Assessment |
| --- | --- |
| .NET 10 on Render Free | Supported through Docker, not a native .NET runtime. Bind `0.0.0.0:10000` using `ASPNETCORE_HTTP_PORTS`; TLS terminates at Render |
| Native image packages | Deployment Dockerfile uses the approved digest-pinned Noble/glibc linux/amd64 SDK/runtime, linux-x64 publish, non-root UID, pinned Skia/TurboJPEG and license notices; no codec/platform substitution |
| Memory/CPU | Prior G06 load: 479,346,688 charged bytes under 512 MiB, **two CPUs**. New local normal-root **0.1 CPU / 512 MiB basic smoke passes**, 49,053,696-byte peak, no OOM, 73.59-second startup. Health/denials/CORS only, not authenticated CRUD capacity or deployed Render proof; initial read-only-root attempt timed out. Uploads remain disabled; deployed resource monitoring required |
| PostgreSQL | Npgsql over TLS. Use the QA dashboard's **session pooler on port 5432** for Render IPv4, not transaction port 6543. Keep pool small (initial maximum 5, minimum 0); no automatic startup migrations |
| Migrations | Direct QA-primary PostgreSQL connection from an IPv6-capable operator machine/runner, separate from Render runtime. If unavailable, stop; don't buy IPv4 or silently substitute transaction pooling |
| Auth/JWT | QA Supabase URL, exact Site URL/redirect URL, matching metadata issuer/JWKS, `authenticated` audience, ES256/RS256 signing key. API derives sub and Library ownership; never client owner IDs. No HS256/test-key deployment |
| Frontend separate API origin | `build:qa` generates only three explicitly allowlisted public values; production optimization/PWA/budgets retained. Missing/invalid values and existing dev Supabase project are rejected. E2E always false |
| CORS | Existing API supports exact origins. `AllowedOrigins__0` must be the **actual stable QA frontend origin**, no path/trailing slash; no localhost, wildcard or blanket Vercel previews. Other API access still requires JWT; CORS is not authorization |
| SPA/PWA | Static output `dist/game-library/browser`; extensionless deep links rewrite to index, files with extensions do not. Control files revalidate, no API rewrite/proxy/cache. Existing worker has no authenticated data cache |
| Isolation | Separate Supabase project, credentials, issuer, users and synthetic data; separate Vercel/Render resources. Vercel's “Production” target is only the QA project's stable alias, **not application production** |
| Feature 013 | `CoverUploads__Enabled=false`, `AllowLocalHttp=false`, no Storage base/credential, all E2E switches false. Disabling uploads alone would not disable existing reads/cleanup; therefore start with an empty QA cover lifecycle, no copied managed records |

**Supabase Data API is a security prerequisite:** this app's approved security
boundary is ASP.NET, not application-table RLS. Disable the QA project's **Data
API** in its dashboard before migrating application tables; keep Auth enabled.
Verify REST/GraphQL cannot read application tables with either the publishable key
or a browser JWT. Do not infer safety from missing Angular calls or new default
grant changes. No application RLS/schema redesign or manual table edits are added.
Storage access is separate and remains out of scope.

**Personal access:** configure only approved QA accounts; disable public signups,
anonymous sign-in and unused social providers after establishing the approved
accounts via the Supabase dashboard. Keep password authentication, email
confirmation and backend JWT validation. A register page may remain visible but
the provider must deny further public registrations. No auth bypass or internal
test endpoints. `/health` remains the existing anonymous, dependency-free liveness
endpoint, not a database-readiness guarantee. `/auth/me` tests real JWT validation.

### Dependency advisories

The previous high Router/Zod findings are **already fixed in the pre-existing
worktree**, not fixed anew by deployment preparation:

- All framework packages are 22.2.1. Router GHSA-ff3f-86qr-9cv3 fixes start at
  22.2.0; GHSA-62vg-58rm-qff7 and GHSA-57xq-rjx2-v5xh require 22.2.1. These exploit
  SSR/Node URL/rendering paths, absent from this browser-only static deployment.
- Zod GHSA-mrhx-c7xc-m3w5 affects `<4.6.6`; existing override locks 4.6.6. Do not
  remove it: even CLI 22.2.1's manifest requests 4.6.5.
- Fresh production-only npm audit: **zero vulnerabilities**. Full audit: **32
  package records (3 critical, 8 high, 21 moderate)** in development/build tooling.
  Static hosting does not expose a Vite/CLI/MCP dev server, but hosted builds still
  execute tooling. This is an unresolved release-security review, not a clean audit.
- Manifest-compatible follow-up: `@angular/build`/CLI **22.2.1** support current
  Node 24.15/TS 6.0/framework peers. Build 22.2.1 pins Piscina 5.3.2 and Vite 8.3.0,
  outside the reported critical Piscina/high Vite ranges. CLI changes its MCP
  dependency graph. This has **not** been installed/tested or proven to clear the
  entire remaining graph; regenerate/review the lock and rerun audit/build/tests in
  a separately reviewed targeted update. No broad `npm audit fix --force`.

See [the existing security review](../../specs/013-camera-cover-upload/dependency-security-review.md).

## 4. Prepared configuration

- `render.yaml`: manual Git-backed Free Docker service, `/health`, default 30-second
  shutdown allowance (Free rejects a custom override), no automatic deploys or additional resources. Creation still
  triggers an initial deployment; do not apply until approved and migration-ready.
- `deploy/qa/Dockerfile` and its allowlisted context: application-only final image,
  port 10000, no SDK/test harness/startup migration/credentials in build arguments.
- API `RuntimeIdentifiers`/NuGet lock: adds the required linux-x64 restore target;
  the generated Core lock records the library graph. Docker restores the API's
  declared RID without imposing it on Core. No dependency version upgrade.
- `deploy/qa/.env.example`: backend/operator variable-name template, no secret values.
- `src/frontend/vercel.json`: QA build/output/SPA routing, no-index headers, Git
  auto-deploys disabled. **QA-specific file: do not import unchanged into production.**
- `src/frontend/scripts/build-qa.mjs` and tests, Angular `qa` replacement layered
  over `production`, Node 24.x engine metadata, `.env.example`, and generated-file/
  Vercel-link ignores. Ordinary
  `npm run build` and development configuration are unchanged. No runtime config API.

Required deployment variable **names only**:

| Location | Names |
| --- | --- |
| Vercel build (public) | `QA_API_BASE_URL`, `QA_SUPABASE_URL`, `QA_SUPABASE_PUBLISHABLE_KEY` |
| Render secret | `ConnectionStrings__Default` |
| Render configuration | `ASPNETCORE_ENVIRONMENT`, `ASPNETCORE_HTTP_PORTS`, `AllowedOrigins__0`, `Authentication__Schemes__Bearer__MetadataAddress`, `Authentication__Schemes__Bearer__RequireHttpsMetadata`, `Authentication__Schemes__Bearer__TokenValidationParameters__ValidAudiences__0` |
| Render safety switches | `CoverUploads__Enabled`, `CoverUploads__AllowLocalHttp`, `E2E__Auth__Enabled`, `E2E__CoverUploads__Enabled`, `E2E__CoverImageSearch__Enabled`, `E2E__SteamIntegration__Enabled`, `SteamIntegration__Enabled` |
| Operator only | `QA_MIGRATION_CONNECTION` (temporary `ConnectionStrings__Default` for EF) |

Connection string must be **Npgsql key/value format**, not a `postgresql://` URI.
Use the dashboard's actual host/user (don't construct the pooler hostname), TLS
`SSL Mode=VerifyFull` with the appropriate trust chain, `GSS Encryption Mode=Disable`
for password auth on this image, `Maximum Pool Size=5;Minimum Pool Size=0` and bounded
connection/command timeouts (initially 15/30 seconds). Do not disable certificate
verification. No database password, JWT signing secret, service-role/Storage secret,
Brave or Steam key belongs in Vercel. Initial QA search/Steam remain unconfigured;
manual cover URLs/CRUD still work without optional provider costs.

## 5. Safe migration and deployment sequence — after approval only

1. Owner confirms accounts/Free plans/available slots, regions, exact resource names,
   and a reviewed immutable source snapshot. Owner publishes necessary Git changes
   separately; this agent does not stage/commit/push. Record source SHA, native
   pins, migration list and image digest. Re-run relevant checks on that snapshot.
2. Provision **only** the separate QA Supabase Free project. Disable Data API,
   establish approved QA users and signup policy, select asymmetric JWT signing,
   and obtain public URL/key plus separate private runtime/migration connections.
   Do not inspect/copy production rows or apply changes to the existing project.
3. Prepare the migration locally with EF tools **10.0.11**, matching runtime:

   ```sh
   bash tools/cover-native/prepare.sh
   dotnet build src/backend/GameLibrary.sln
   dotnet ef migrations has-pending-model-changes --project src/backend/GameLibrary.Core --startup-project src/backend/GameLibrary.Api
   dotnet ef migrations script --idempotent --project src/backend/GameLibrary.Core --startup-project src/backend/GameLibrary.Api --output "$QA_SQL_REVIEW_PATH"
   ```

   Review every migration through `20261029134813_AddCoverUploads` (including
   earlier Feature 011/012 migrations), even with uploads disabled: current code
   expects these columns. EF does not manage `auth.*`/`storage.*`. Test clean and
   upgrade paths on disposable PostgreSQL. No Supabase `db push`/parallel migration
   history, Render pre-deploy job or startup `Migrate()`.
4. Confirm the private direct connection targets **QA primary**, port 5432, correct
   database and TLS. Inspect its migration history/schema read-only. For a nonempty
   QA database, obtain a private logical backup and verify restore on an isolated
   database before mutation; Free does not supply recoverable backups. Stop testers
   and API writers during migration. Never migrate a production/shared database.
5. With explicit migration approval, inject the secret into the operator's process
   environment privately (no shell tracing, history, command-line secret or logs):

   ```sh
   export ConnectionStrings__Default="$QA_MIGRATION_CONNECTION"
   dotnet ef database update --no-build --project src/backend/GameLibrary.Core --startup-project src/backend/GameLibrary.Api
   unset ConnectionStrings__Default QA_MIGRATION_CONNECTION
   ```

   Verify migration history and constraints. On timeout/unknown commit outcome,
   inspect authoritative history/state before any retry. Test databases/E2E reset
   helpers must **never** point at this hosted QA database.
6. Create the dedicated Vercel project without importing/deploying unreviewed code;
   confirm its actual stable alias and set only its three public build inputs.
   Create the Render service from the approved commit using the blueprint (initial
   deployment is an approved action), injecting QA connection, metadata URL and
   exact Vercel origin. Keep all safety switches and absent Storage secrets as above.
   If names receive suffixes, use actual assigned origins, not proposed ones.
7. Set Supabase Auth Site URL and exact allowed redirect to the stable QA frontend
   URL. This app's signup doesn't specify redirectTo, so Site URL is authoritative.
   Do not add wildcard Vercel previews or change the existing project's redirects.
   Manually deploy the coordinated frontend build to **the QA project's** stable
   target. Verify Render build/state logs and actual Vercel output before declaring live.
8. Execute the deployed smoke checks below; record actual CPU/memory, ingress,
   SIGTERM/restart/cold-start results and quota/billing settings. No automated
   mutation retries or uptime pings. Stop and report failure rather than enabling
   paid capacity, relaxing filters/auth or turning uploads on.

Cold starts can be slow: the local 0.1-CPU basic smoke took roughly 74 seconds to
health, before any real hosting cold-start overhead. For personal testing, open
health and wait for readiness before CRUD; retry failed reads explicitly. Do not
automatically replay a write or use an uptime service to defeat Free sleeping.

### Required hosted smoke checks

- HTTPS `/health` returns 200; `/auth/me`, `/library`, `/cover-uploads/options`
  without JWT return 401. `/e2e/auth/session` and `/e2e/reset` are absent (404).
- Actual Supabase sign-in/confirmation/session refresh/logout works; a QA token
  succeeds at `/auth/me`, foreign-issuer/expired/forged tokens fail. Two QA users
  cannot read/edit/delete each other's games, platforms or notes.
- Known frontend origin passes CORS preflight; a hostile origin has no allow-origin
  header. Use the stable alias, not a transient deployment URL.
- Cold-load/refresh `/library`, `/manage/steam`, `/random-picker`, `/login`; actual
  JS/CSS/icons/manifest/`ngsw.json`/`ngsw-worker.js` have correct content types, not
  fallback HTML. Install/reopen PWA; authentication redirect remains on QA.
- Create valid VideoGame/BoardGame, CRUD/filters/Picker/Play Log persist across API
  restart. `/cover-uploads/options` reports false; authenticated upload returns 503;
  manual external covers remain usable. No Storage traffic/managed attachments.
- Direct Supabase REST/GraphQL application data access is disabled for anonymous
  and authenticated browser credentials. Public signup/anonymous sign-in rejected.
- Inspect deployed secrets/config without printing values; verify actual instance
  architecture/native hashes/non-root image, memory/CPU/cold-start behavior and
  no OOM. Native uploads/real Storage and physical-camera review are separate gates.

## 6. Rollback and stop conditions

Record the last **coordinated frontend/API pair**, schema version, configuration and
image digest before changes. Disable automatic builds/deploys (already proposed).
If QA fails, suspend the QA API and stop testing; do not redirect QA to production.
Rollback Vercel to the compatible previous QA deployment and Render to a retained
compatible build (Free documents only two previous deploys). If none exists,
leave suspended until a reviewed rebuild is available.

Application rollback does **not** roll back PostgreSQL or configuration. Prefer
forward-compatible schema fixes; do not automatically run EF Down, restore an old
schema under a newer API, drop tables, wipe a database or delete provider resources.
A backup restore or QA re-creation/data loss requires separate explicit approval.
Account/secret changes must also be reviewed independently. PWA tabs can retain
older app shells: refresh/update or remove/reinstall the QA PWA after rollback and
verify API write-contract compatibility. This does not authorize clearing users'
unrelated browser data. Never relax the explicit cover intent contract for old tabs.

## 7. Approval checklist

**STOP before external actions.** Approval must identify the exact accounts,
regions and source SHA and explicitly cover:

1. Creating `game-library-qa` in a verified Free organization with a free slot,
   disabling its Data API, configuring QA-only Auth redirects/signups/accounts and
   supplying secrets privately. Existing project changes are **not** authorized.
2. Applying reviewed EF migrations **only to that new QA primary** after backup/
   empty-target verification. No remote migration has run during preparation.
3. Creating `game-library-qa-api` on Render **Free**, setting its environment/CORS,
   initial deployment and manual future deploys of the approved snapshot.
4. Creating `game-library-qa-web` in the confirmed Vercel **Hobby** scope, setting
   public build configuration, and deploying the coordinated static QA artifact.
5. Running bounded deployed smoke tests that create **synthetic QA data only**,
   including two approved test users and restart/cold-start checks.

Still **not approved**: billing/payment changes, trials/paid compute/overages,
Storage provisioning/enablement, production deployment/data/schema/configuration,
Git staging/commits/pushes, or resource deletion. If account or free-tier constraints
cannot meet $0, stop and ask instead of upgrading.

## Sources

- [Render Free limitations](https://render.com/docs/free), [Docker](https://render.com/docs/docker), [Blueprint specification](https://render.com/docs/blueprint-spec), [pricing](https://render.com/pricing), [official MCP](https://render.com/docs/mcp-server), [CLI](https://render.com/docs/cli).
- [Vercel Hobby](https://vercel.com/docs/plans/hobby), [project configuration](https://vercel.com/docs/project-configuration/vercel-json), [published schema](https://openapi.vercel.sh/vercel.json).
- [Supabase pricing](https://supabase.com/pricing), [PostgreSQL connections](https://supabase.com/docs/guides/database/connecting-to-postgres), [redirect URLs](https://supabase.com/docs/guides/auth/redirect-urls), [SMTP limits](https://supabase.com/docs/guides/auth/auth-smtp), [Data API security/disablement](https://supabase.com/docs/guides/api/securing-your-api), [CLI v3 breaking change](https://supabase.com/changelog/supabase-cli-v3).
- [OpenCode V2 MCP setup](https://opencode.ai/v2/docs/mcp-servers).
