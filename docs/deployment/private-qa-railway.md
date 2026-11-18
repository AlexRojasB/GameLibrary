# Railway private QA — GO/NO-GO and approval runbook

2026-11-06. **GO for local preparation; NO-GO for provisioning, migrations or
deployment.** Selected topology: Vercel static Angular PWA -> Railway .NET 10 API
-> **separate Supabase QA PostgreSQL/Auth**. This supersedes the earlier Render
hosting proposal and its $0 assumptions, not its security controls or historical
test evidence. Render's payment-info gate is no longer on the selected path.

No cloud resource, deployment, remote migration, billing setting, Git index,
commit or push was changed. The owner still must approve the actions in section 7.
This is limited private QA with uploads **disabled**, not Feature 013 release.
Its real Storage, physical-device and remaining operational gates remain partial.

## 1. Railway authentication, capacity and project choice

| Check | Observed read-only result |
| --- | --- |
| Identity | Official MCP and CLI both authenticate as `alexrojasb` / Alexander Rojas Benavides |
| CLI | Official `railway` **5.62.1**, authenticated; no Railway link in this repository |
| Workspace | `Alexander Rojas Benavides's Projects`, `6636d7dc-798b-4470-8376-b36089020713`, personal |
| Subscription | Workspace plan **HOBBY**; customer `ACTIVE`, subscription `active`, not a trial, no scheduled cancellation; $5 monthly subscription item |
| Project inventory | Two projects; no existing Game Library project |
| Capacity returned by account API | 50 projects, 50 services/project, 6 replicas; container ceiling 8,000,000,000 bytes / 8 CPU; build concurrency 3, soft/hard build budgets 20/40 minutes |
| Requested QA limits | One API, one replica, **0.5 GB RAM / 0.5 vCPU**; well below reported ceilings; actual enforcement remains a hosted verification gate |

Public plan documentation advertises higher aggregate maxima. The table uses this
account's observed `subscriptionPlanLimit`, not a guarantee of schedulable capacity.
Its extra minimum/default-replica fields are not treated as proof of a 0.5-GB
reservation or as proof of a rejected limit. Read back actual service limits after
approved creation; stop rather than deploy uncapped if the request is rejected.

| Existing project | Inventory | Reuse decision |
| --- | --- | --- |
| `El Arbolito POS Staging` (`ccd0ebcc-49de-4a20-895b-da1b8598525a`) | `staging`: live App plus PostgreSQL 18 with a 5-GB volume, region `sfo`; no pending patch | Do not reuse |
| `CR Fuel Service` (`fb9f344f-b10a-46b9-a355-644e629fd8e9`) | `production`: live `CRFuelScraper`, unrelated Git repository, region `sfo`; no pending patch | Do not reuse |

**Prefer a new private `game-library-qa` project, environment `qa`, in the existing
paid workspace.** Neither project is empty/available for repurposing. Reuse would
mix unrelated services, operators, variables and deployment risk. Do not reuse
either database or add Game Library to either project. A separate project does
not add another subscription, but its measured usage shares the workspace bill
and any workspace-wide shutdown limit.

Private project visibility is **not** network access control: the eventual public
API domain still relies on the unchanged Supabase Bearer JWT/Library authorization.
Only the existing liveness endpoint is intentionally anonymous.

Available official MCP reads include `whoami`, `list-workspaces`, `list-projects`,
`list-services`, `describe-environment`, `describe-service`, `get-service-metrics`
and `search-docs`. Future mutation tools include `create-project`, `create-service`,
`update-service`, `set-variables`, `connect-service-source`, domain generation and
`accept-deploy`; **none were called**. The authenticated CLI supplies read-only
`usage`, `usage limit status`, and schema-discovered GraphQL billing queries.
No login/setup, token extraction, agent invocation or tool installation is needed.
Do not use `railway run`/`shell` to import variables from unrelated services.

## 2. Vercel and Supabase access

### Vercel: authenticated, intended scope still blocked

- MCP user **`proxtos-5361`**, user-level billing **active Hobby**, one concurrent build.
- `list_teams(limit=100)` returned an empty list with count 0; Git deployment
  discovery also returned no teams. The visible project list returned one unrelated
  `bar-book-web`; no further pagination cursor was supplied. Do not reuse it.
- Known default/project owner: `team_06vOR8VzjmIVJfhtgodwg1wL`. Explicit `get_team`
  reproduced **403**, scope **`bar-book`**, missing team authorization; response
  reports `saml=false`, `ssoEnforced=false`. Empty discovery is not proof of no teams.
- User Hobby billing does **not** establish this team's plan or project-create
  permission. Owner selects the intended QA account/team. If it is this team, use
  official `authorize_team_access` for the known ID, present its short-lived consent
  link, let the owner approve, then retry **on the same connection**. Do not reconnect
  blindly, authorize another scope, or create a project as an access test. No consent
  grant or scope change was initiated here. Vercel CLI is not installed.

After scope verification, propose dedicated `game-library-qa-web`, root
`src/frontend`, Node 24.x, `npm ci`, `npm run build:qa`, static output
`dist/game-library/browser`. Existing QA `vercel.json` disables Git auto-deploys,
retains SPA routing/PWA headers/no-index and adds no functions/API proxy. Its
stable target named “Production” is **QA-only**, not application production.

### Supabase: project connection works; Free capacity NOT verified

- Official MCP `get_project_url` succeeds, scoped to existing development ref
  `iramzxpjbnldhebykzhx`. No rows, credentials, keys or remote schema were read.
- All 20 advertised tools are project-scoped operations/docs; no organization or
  account-wide project enumeration/create-capacity tool is exposed. No Supabase
  CLI or management-token environment variable is available. A connected project
  MCP is not organization administration access. No OAuth token was extracted.
- Official policy: **two active Free projects across all organizations where the
  user is Owner/Administrator**; paused projects do not count. Plans apply per
  organization: another project in a paid org is not a Free project.
- **Blocker:** owner must confirm a named Free organization, create permission and
  account-wide available slot in the dashboard or authorized management tooling.
  Do not create a project to test capacity, pause/delete/transfer another project,
  use paid branching, or reuse the connected development/production database.

Before eventual migrations, disable the **new QA Data API**, preserve Auth,
configure asymmetric ES256/RS256 JWT/JWKS, exact QA Site URL/redirects and approved
test identities. Disable public signups, anonymous sign-in and unused providers;
retain password authentication/email confirmation. Default Free SMTP recipient
and rate restrictions must be checked for approved QA addresses. No paid SMTP,
IPv4 add-on, Storage bucket or privileged Storage key is proposed. Free database
size is 500 MB/project; inactivity pausing and lack of automatic backup/PITR remain
QA constraints. No application-table RLS or second schema workflow is introduced.

## 3. Deployment configuration readiness

Prepared locally:

- `deploy/qa/railway-service-plan.json`: **review data, not an automatically loaded
  Railway configuration or an executable deployment command**. `settings` maps to
  official `update-service`; IDs/origins/issuer/connection are deliberately absent.
  The `project` object requires the separately recorded workspace ID when submitted
  to `create-project`. `source` is a policy, not a raw API request.
- `deploy/qa/check-railway-plan.py`: offline invariants and existing public-build
  allowlist regression checks. It reads no credentials and contacts no cloud API.
- Existing `deploy/qa/Dockerfile`: same digest-pinned SDK/runtime, explicit
  **linux/amd64**, conventional **.NET 10 Noble/glibc**, locked linux-x64 restore,
  framework-dependent/no AOT/trimming/single-file, non-root UID 1654. Only explicit
  `SteamIntegration__Enabled=false` was added to its runtime safety defaults.
- Existing allowlisted Docker context still excludes secrets/local outputs. No
  credential build arguments, SDK/test harness, startup migration or extra native
  packages enter the final runtime. Skia/TurboJPEG validation/pins are unchanged.
- `deploy/qa/.env.example`: Railway variable names/template only. ASP.NET does not
  automatically load it. Render blueprint retained, marked historical/do not apply.

**Why no `railway.json`/`railway.toml`:** current official docs/MCP say Config as
Code is deprecated and new services cannot opt in; legacy support ends 2026-12-01.
Railway IaC requires a new SDK dependency. Direct, explicit MCP/dashboard settings
are sufficient for one API; no IaC dependency or whole-project apply/delete surface
is justified. The plan is reproducible operator input, not provider-side validation.

| Setting | Proposed safe value / obligation |
| --- | --- |
| Build | Repository-root context `/`, Dockerfile `deploy/qa/Dockerfile`; no Railpack/.NET runtime substitution |
| Port | Explicit `PORT=10000` and `ASPNETCORE_HTTP_PORTS=10000`, public domain target 10000; existing image listens on all interfaces |
| Health | `/health`, 300-second deploy budget; current anonymous dependency-free liveness, not SQL/Auth readiness; Railway checks only during deploy, not continuous monitoring |
| Limits | One replica in proposed `sfo`, 0.5 GB / 0.5 vCPU; owner confirms region near new Supabase QA. No volume, Railway Postgres, worker, cron or cache |
| Lifetime | Sleep disabled initially: predictable QA and existing startup/15-minute cleanup semantics; no keep-alive service. Sleeping/suspension is a separate owner choice, not assumed savings |
| Restart/shutdown | ON_FAILURE, maximum 3 retries; overlap 0; explicit **60-second draining** (Railway default is 0). Docker entrypoint unchanged; no automatic request replay |
| Deployment | Empty service first, configure before attaching source; pinned full owner-reviewed commit via `connect-service-source.commitSha`; pushes must not trigger new deploys. Source attachment builds immediately and requires explicit deployment approval |
| Cache/telemetry | Railway edge CDN and tracing/auto-instrumentation off; never cache API/private image bytes or log Authorization, bodies or provider secrets |
| Migrations | No pre-deploy command or startup `Migrate`; separately approved operator EF workflow only, correct QA primary, exact reviewed schema through `20261029134813_AddCoverUploads` |
| Transport | HTTPS public API, HTTPS metadata and exact audience `authenticated`; no test signing key, auth bypass or production credentials |

Railway container memory is priced by measured usage, not this limit. Do not
equate `memoryGB=0.5` with 512 MiB. Local conservative follow-up uses 500,000,000
bytes; inspect actual deployed cgroup enforcement before accepting hosted readiness.
The per-replica cap does not cap egress or temporary deployment overlap/build
processes. Even with overlap 0, old/new containers coexist during startup/draining.

### Required values and security controls

- **Railway secret:** `ConnectionStrings__Default`, fresh QA-only Npgsql key/value
  connection, never a PostgreSQL URI, build ARG, frontend variable or copied dev secret.
- **Railway public configuration:** actual stable `AllowedOrigins__0` and new QA
  `Authentication__Schemes__Bearer__MetadataAddress`. The checked-in app default
  points to development; it **must** be overridden. Keep HTTPS metadata and
  `authenticated` audience. Never add localhost, wildcards or blanket Vercel previews.
- **Database runtime:** actual dashboard session-pooler host/user on port 5432,
  `SSL Mode=VerifyFull`, `GSS Encryption Mode=Disable`, pool maximum 5/minimum 0,
  initial connection/command budgets 15/30 seconds. Do not purchase IPv4 or weaken
  TLS. Railway-to-Supabase traffic is external networking, not free private DB traffic.
- **Migration operator only:** direct QA-primary TLS connection via privately injected
  `QA_MIGRATION_CONNECTION`. Existing EF CLI 10.0.7/runtime 10.0.11 mismatch must be
  aligned in a separately approved local tool setup before hosted migrations.
  If direct connectivity is unavailable, stop, not transaction pooling or auto-retry.
- **Vercel:** only `QA_API_BASE_URL`, `QA_SUPABASE_URL`,
  `QA_SUPABASE_PUBLISHABLE_KEY`. Existing build allowlist rejects the dev project,
  secret/JWT keys and unsafe origins; optimized PWA build and budgets unchanged.
- **Disabled:** `CoverUploads__Enabled`, `CoverUploads__AllowLocalHttp`, every
  `E2E__*__Enabled` switch and initial Steam integration remain false. No Storage
  base/credential, copied managed-cover rows, real images or production test data.
  Disabling new uploads alone does not stop existing reads/cleanup; start empty.
- API derives caller identity from validated `sub` and enforces Library ownership.
  CORS is not authorization. External images receive no application Bearer token.
  Preserve private/no-store/HEAD/conditional checks and Blob disposal even while
  uploads are off. No direct Angular application-table/Storage access or persistent
  API/private-image service-worker caching is introduced.

## 4. Dependency-security decision

The validated update remains **unapplied**. Review the
[exact candidate/approval record](qa-dependency-security-approval.md): build/CLI
22.2.6 plus three compatible transitive refreshes, same framework/cropper/Supabase
runtime versions, exact lock and diff hashes, 272 changed version records.
The retained patch exactly matches the current two package files; all other 171
frontend source/config files match its previously tested copy.

Historical existing full audit: **3 critical / 8 high / 21 moderate** package
records; candidate **0 critical / 0 high / 17 moderate**, runtime-only zero for
both. Fresh audit and regression results are in section 6. Owner must approve the
exact change and remaining moderate build/test risk, or request separate compatible
remediation. Do not deploy the existing lock as though the candidate were applied.

## 5. Expected memory and incremental billing

Observed current billing period: **2026-10-11 18:03:43 UTC -> 2026-11-11 18:03:43 UTC**.
CLI reports current usage **$3.3133**, forecast **$3.9325**; memory accounts for
about $3.2201 of current usage. These are workspace usage estimates, not a verified
final invoice/net charge. `usage limit status --target workspace` returns **null**:
there is currently **no compute hard limit or soft threshold configured**.

Subscription is confirmed active Hobby. Public docs say $5 includes $5 usage,
but the live account scalar reports **`includedUsageDollars=0`** and customer
**`remainingUsageCreditBalance=1`**. Those fields are not reconciled by CLI usage
output. **Owner must verify dashboard/invoice credit treatment before assuming
free incremental use.** Do not silently choose the favorable value.

Published container rates: **RAM $10/GB-month, CPU $20/vCPU-month, outbound
$0.05/GB**; service builds/image storage are documented free. No Railway database,
volume, VM, cloud agent, paid add-on or second subscription is needed. Ordinary MCP
reads do not invoke the separately metered Railway Agent. Taxes/price changes may apply.

| Workload / evidence | Meaning |
| --- | --- |
| Previous uploads-disabled startup/denial/CORS local smokes | Roughly 38–47 MiB charged peak; no authenticated CRUD, real JWKS/TLS or populated-Library capacity proof |
| Previous G06 native uploads + eight reads, two CPUs | 479,346,688-byte peak (~457 MiB), no OOM under 512 MiB; **not** expected basic-QA use or proof under the new Railway cap; uploads remain off |
| Planning allowance, not measured hosted workload | **128–256 MiB average memory**, average 0.005–0.05 vCPU for light personal QA; initial cap 0.5 GB/0.5 vCPU, not a reservation |

Using decimal GB conservatively for planning, 128–256 MiB average costs about
**$1.34–$2.68/month RAM**; CPU allowance adds **$0.10–$1.00**. At <=1 GB external
egress, allow about **$1.5–$4/month additional resource usage** for a full month
always on. These are assumptions, not a measured hosted forecast/SLO or guarantee.
Supabase runtime pooling/cleanup may keep activity alive; no sleeping discount assumed.

If the standard $5 included allowance applies and other usage stays at the current
$3.93 forecast, illustrative total usage becomes **$5.43–$7.93/month**, roughly
**$0.43–$2.93 added invoice cost** above the existing $5 subscription. If no included
credit applies, conservatively budget the full **$1.5–$4 extra**. First partial cycle
is prorated by actual runtime; future cycles may differ. It is **not guaranteed $0**.

At continuously saturated proposed limits, approximate API compute alone is
**$15/month** (0.5 GB x $10 + 0.5 CPU x $20), **plus uncapped egress and transient
old/new deployment coexistence**. It is not an account spending cap. Region/limits
must be re-read and real memory/current/peak, CPU throttling, restart/OOM and usage
monitored during hosted QA before lowering limits or changing the budget.

Propose an owner-approved **$5/month incremental QA usage allowance** initially,
not an automatically enforced cap. Workspace hard limits affect **both unrelated
projects too**; current docs specify $10 minimum. No billing limit/alert was set.
Owner explicitly selects any workspace alert/hard threshold and accepts shutdown
impact. A separate project does not isolate billing-limit blast radius.

Vercel/Supabase expected increment is $0 **only if** the chosen Vercel Hobby scope
and separate Supabase Free slot/quotas are verified. No plan upgrade, paid org,
branch, IPv4/SMTP add-on, custom domain, Storage or paid monitoring is authorized.

## 6. Verification and Git snapshot

Fresh results from this preparation:

| Check | Actual result |
| --- | --- |
| Backend solution build, `--no-restore` | Passed, zero warnings/errors |
| Core, `--no-build --no-restore` | **337 passed**, zero failed/skipped |
| Complete real-PostgreSQL integration suite | **994 passed**, zero failed/skipped, ~172 seconds |
| Offline Railway plan/safety checks | **10 passed**, including HTTPS Railway-origin acceptance by the existing frontend build allowlist |
| Existing QA build-config tests | **15 passed** |
| Current frontend regression suite | **465 passed / 38 files**, zero failed/skipped |
| Exact candidate frontend regression suite | **465 passed / 38 files**, zero failed/skipped; isolated copy, not applied |
| Current and candidate lint | Both passed |
| Current production / synthetic-Railway QA builds | Both passed; initial 677.02 / 677.13 kB |
| Exact candidate production / synthetic-Railway QA builds | Both passed; initial 678.67 / 678.78 kB; lazy editor 58.02 kB |
| EF model / idempotent SQL generation | No pending model changes; script generated locally, never applied to hosted QA |
| Fresh QA Docker build | Passed locked restore/publish on pinned Noble linux/amd64, non-root 1654, .NET 10.0.14, no SDK |
| Final native packaging | Both approved hashes match, `ldd -r` resolves, notices present; no codec/pin/platform substitution |
| New local Railway-sized basic smoke | **0.5 CPU / 500,000,000-byte hard memory/swap**: health, three 401 denials, two E2E-helper 404s, exact CORS allow/deny and private/no-store challenge passed |
| Basic-smoke resource/lifetime | First health **3.85 s**, charged peak **49,463,296 bytes (~47.2 MiB)**, memory max/oom/oom_kill events **0/0/0**, SIGTERM **0.74 s**, exit 0 |
| Current production npm audit | **0 vulnerabilities** |
| Exact candidate production npm audit | **0 vulnerabilities** |
| Current and candidate full npm audits | Both **timed out at 60 seconds**; historical full counts are not a fresh security clearance |

Backend tests used a newly named disposable database on local PostgreSQL 16,
port **5433**, plus fixtures' own generated databases. Local test connection values
were held in process memory and redacted from retained logs. Existing databases,
the unrelated port-5432 PostgreSQL, hosted Supabase and production were not reset
or migrated. The new regression database was removed after the run. No backend
source, test assertion, schema, auth or native processing control was changed.

Frontend checks used existing installed dependencies; no repository install or lock
mutation. Both QA builds supplied only synthetic public values and a synthetic HTTPS
Railway origin. The candidate's prior clean `npm ci` is historical; it was not
reinstalled in this continuation. Existing 4.13-kB/4-kB Picker stylesheet and Vitest
poolOptions warnings remain; candidate also emits the previously recorded future
Vite native-config-loader warning. No warning was suppressed or budget raised.
No browser E2E rerun is claimed here; the earlier 22/22 desktop/mobile result is
historical and must be rerun on the final approved snapshot. No real-device,
Storage-permission, actual hosted TLS/JWKS or feature-release gate is waived.

Fresh local QA image ID:
`sha256:b62315243c6da12e76b45199678aab0e0bac48cab27b92b48bf203d8ab6d835c`.
It was built locally, **not pushed to a registry or deployed**. The short smoke
used Production mode, the exact plan's false switches, a synthetic HTTPS metadata
URL, no valid JWT and a newly migrated empty local test DB. It is not authenticated
CRUD/load, real JWKS/Supabase TLS, actual Railway signal/ingress or native-codec
execution evidence. Host integration included image regressions; the unchanged
G01 native codec/resource gate was not rerun in full. Final-image hash/loader
checks are not described as a fresh passed G01. The smoke DB/container were removed
without touching existing data. Normal ephemeral Data Protection key warnings remain;
no persistence service was added. Docker's explicit-amd64 and false E2E switch-name
warnings remain intentional, not hidden credentials or a waived runtime target.

Historical test counts in the earlier preflight/G06 reports are not relabeled as
new Railway evidence.
No actual Railway ingress, DNS, QA Supabase TLS/JWKS, Vercel alias or hosted
two-user workload exists yet; all such checks remain **NOT RUN**.

### Reproducible source without touching the existing index

Starting HEAD on `dev`: `1c840c59f1d6470bc2f0b0de59e768c6ee6f699b`.
Raw `.git/index` SHA256 at entry:
`e369fb71d4dd9dddde1ab2e8d904c5e37d3303d9e4ebf189a8eca85bafbc57eb`.
Staged binary-diff SHA256:
`62092d1ed1bbe35197e650619a83a5a9eed14a4495a4223a795223ed11372439`.
An external baseline stores SHA256 for all **405** initially Git-eligible files,
raw status and HEAD. No index update or Git object/ref creation is needed.

Final preservation comparison: raw index, staged binary diff and HEAD unchanged;
all original files outside the seven intended documentation/deployment edits match
the baseline. Four new preparation files were added; none staged. Whitespace and
local documentation links pass. A limited secret-pattern scan flagged only an
unchanged pre-existing README loopback database example, not new credential content;
its value was not printed. This is not a comprehensive secret-scan certification.

Owner snapshot strategy:

1. Review the complete current worktree, not just HEAD, staged changes or `origin/dev`.
   Working bytes include unstaged Feature 011–013 source/migrations and untracked
   deployment/native/test files needed by the build. Preserve the existing staged
   versions separately; do not use `git add`, `stash`, reset, clean or a temporary index.
2. Obtain the read-only path list with
   `git ls-files --cached --others --exclude-standard -z`; deduplicate/sort it.
   Read current file bytes/modes from disk, honoring deletions. Explicitly exclude
   `opencode.json(c)`, `.opencode/`, provider links/caches, secrets, real `.env` files,
   generated/ignored outputs, private evidence and credential stores. Example files
   may remain after review. Run a secret scan; filename exclusions alone are not proof.
3. Produce an external content manifest (path, mode, SHA256) plus deterministic TAR:
   sorted paths, fixed mtime=0/uid=0/gid=0/empty owner names, stable regular-file modes;
   reject symlinks outside the repository. Hash the archive, capture HEAD/status,
   tool versions, native image/artifact pins and manifest hash. Re-hash inputs after
   export; abort on concurrent edits. Raw archive hash is content evidence, not a commit.
4. Retain the exact reviewed dependency candidate patch/hashes **separately** while
   unapplied. Only after its explicit approval apply it to the owner-approved copy;
   re-manifest and retest. Never attach a moving `dev` branch as an implicit snapshot.
5. **After separate Git publication approval**, create/review the QA commit in a
   separate clone/review directory populated from that manifest. Stage/commit/push
   only there if authorized; never modify this checkout's existing index. Check
   final commit-tree contents against the approved manifest and run final gates.
6. Record the full immutable commit SHA for both Vercel and Railway, plus final API
   linux/amd64 image digest, frontend build identity, migration list and configuration
   fingerprints without secrets. Deploy only that reviewed pair. None is published now.

Current temporary evidence lives outside the repository under
`/private/var/folders/fd/h4rhj_wn5_71qy5q79xm8r_m0000gn/T/opencode/railway-qa/`;
the original preservation baseline is `../game-library-railway-baseline.json`.
Temporary logs/artifacts are not durable approval storage. No baseline secret
values or provider variable values are included in this report.

## 7. Remaining blockers and exact owner approvals

**NO-GO until these are resolved, in order:**

1. **Scope/budget decisions:** confirm the existing Railway workspace, new dedicated
   QA project (not either unrelated project), `sfo`/nearby Supabase region, 0.5-GB/
   0.5-vCPU proposal and incremental budget. Reconcile account credit fields in the
   billing dashboard. Any alerts/hard-limit changes require separate approval,
   including their workspace-wide impact; no plan/payment change is needed or authorized.
2. **Access only:** select/authorize the intended Vercel Hobby team/account and
   recheck the `bar-book` 403 in that scope; verify Supabase Free organization,
   create permission and a free slot across all owned/administered organizations.
   No provisioning as an access test. Do not extract credentials from another tool.
3. **Security/source:** explicitly approve the exact dependency diff and moderate
   risk disposition; approve the complete intended source and separate Git publication
   workflow/full SHA. Existing staged/unstaged/untracked content is not blanket approval.
4. **Creation approval, separately:** name the verified accounts/regions and authorize
   only new Supabase `game-library-qa`, Vercel `game-library-qa-web`, and private
   Railway project `game-library-qa` / `qa` with empty `game-library-qa-api`.
   Keep source unattached until deployment approval. No DB/volume/Storage/worker on Railway.
5. **Configuration/secrets approval:** configure **QA only** Data API disabled,
   approved test users/signup policy/asymmetric signing/exact redirects; privately
   supply fresh QA runtime/operator connections; map this plan to actual project/
   environment/service IDs with variables staged or `skipDeploys=true` as appropriate.
   Read back full config, limits, single replica, source, variable **names** and flags
   without printing secrets. Platform staging still counts as an external write and
   is not authorized by this preparation task. Configure actual CORS/issuer, not examples.
6. **Migration approval:** align EF operator tools, review idempotent EF SQL and all
   migrations on the approved snapshot; verify new QA-primary direct TLS and
   empty-target or tested backup evidence; then apply EF migrations **only there**.
   No startup/pre-deploy DB mutation, Supabase schema workflow or blind retry after
   an unknown COMMIT result. Local disposable regression fixtures are not hosted QA.
7. **Deployment/domain approval:** attach the exact repository/full `commitSha`
   (attachment builds immediately), generate the QA API domain/target 10000, set
   exact origins, set only Vercel's three public build inputs and deploy the same
   snapshot to its stable QA alias. Verify state/build logs and actual Noble/amd64/
   non-root/native hashes, health, resource caps and disabled auto-deploys.
8. **Bounded hosted verification approval:** two synthetic QA identities/data only;
   real login/confirmation/JWKS/refresh/logout, foreign/expired/forged token denial,
   cross-user CRUD/filters/Picker/Play Log, exact CORS allow/deny, Data API denial,
   SPA/PWA deep links/control files/update, uploads disabled/authenticated POST 503,
   no Storage calls, absent E2E helpers, memory/CPU/OOM, restart/SIGTERM and spend checks.

Stop on failure; never repair by using production, relaxing TLS/auth/ownership,
enabling test modes/uploads, increasing paid capacity or changing billing implicitly.
No resource deletion, database reset, Git staging/commit/push, payment/plan/paid
add-on or production action is approved by this document.

### Rollback

Record a coordinated last-good API/frontend/schema/config pair. Stop QA writes
and suspend the QA service on failure; never point QA at production. Railway Hobby
documents **72-hour removed-image retention**; outside it, rebuild the reviewed
source rather than assume rollback availability. Provider rollback may restore
deployment settings/**variables**, so check QA issuer/connection/CORS/disabled flags
again before traffic. Vercel/PWA may retain an older shell: verify coordinated
write-contract compatibility and refresh/reinstall only the QA PWA as needed.
App rollback does not undo schema; no EF Down/restore/wipe/delete without a separate
approved recovery plan and backup. The API filesystem is ephemeral and not a backup.

## Sources

- Railway: [pricing](https://docs.railway.com/pricing),
  [plans and image retention](https://docs.railway.com/pricing/plans),
  [cost control](https://docs.railway.com/pricing/cost-control),
  [Dockerfiles](https://docs.railway.com/guides/dockerfiles),
  [healthchecks](https://docs.railway.com/guides/healthchecks),
  [variables/draining](https://docs.railway.com/reference/variables),
  [IaC / Config-as-Code cutoff](https://docs.railway.com/infrastructure-as-code).
- Supabase: [billing and Free project policy](https://supabase.com/docs/guides/platform/billing-on-supabase),
  [connections](https://supabase.com/docs/guides/database/connecting-to-postgres),
  [Data API security](https://supabase.com/docs/guides/api/securing-your-api),
  [SMTP](https://supabase.com/docs/guides/auth/auth-smtp),
  [changelog](https://supabase.com/changelog.md). No Supabase SDK/CLI upgrade is proposed;
  CLI v3 targeting/confirmation changes must be reviewed before future installation.
- Existing [QA preflight](private-qa-preflight.md),
  [historical verification](private-qa-verification.md),
  [G01](../../specs/013-camera-cover-upload/g01-strict-jpeg-implementation-report.md),
  [G03](../../specs/013-camera-cover-upload/g03-storage-verification.md),
  [G06](../../specs/013-camera-cover-upload/g06-operational-verification.md),
  Phase 1/2 reports, approved Product/Domain/Architecture and AGENTS.md were reviewed.
