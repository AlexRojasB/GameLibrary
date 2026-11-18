# QA application deployment preparation

2026-11-17. **Local candidate prepared and tested. Do not deploy yet.**

This is the current handoff after the successful, separately authorized initial
Neon migration. Earlier creation/cleanup/migration-readiness documents are historical;
their hashes have not been rewritten. Read the approved
[application amendment](../architecture/private-qa-application-amendment.md) and new
[environment plan](../../deploy/qa/application-environment-plan.json).

## Changes and source identity

- `Program.cs` delegates cleanup registration to the new cover-specific
  `CoverCleanupConfiguration` and logs the explicit opt-out. No Core/domain,
  controller, schema, native dependency or JWT allowlist change.
- `PrivateQa__DisableCoverCleanup=true` omits the hosted cleanup worker entirely.
  Missing/false preserves default startup/15-minute behavior. Malformed values,
  uploads enabled or nonempty Storage base/credential fail before hosting.
- QA remains **Production-mode**. No environment/host inference or E2E bypass.
  The switch is startup-only: do not hot-enable uploads or import/restore any
  managed-cover metadata in this mode. Separate fresh evidence must establish
  **zero cover_uploads rows AND zero non-null games.cover_upload_id references**
  immediately before first startup. The earlier migration postchecks found all
  application tables empty except the 15 Genre seeds; no fresh remote query was
  performed in this local task.
- New versioned `deploy/qa/Dockerfile.application` retains the pinned .NET 10
  Noble/glibc linux/amd64 build/runtime, locked restore, non-root execution and
  canonical native assets. Its QA-only immutable OpenSSL default policy restricts
  client TLS to **1.3 only**; Npgsql must still use **VerifyFull**. The policy also
  applies to API outbound HTTPS, not just SQL; unsupported TLS-1.2-only services
  will fail, not cause fallback. Optional provider integrations remain disabled.
- New Production-composition tests cover cleanup on/off, startup validation,
  simulated timer intervals, no DB/Storage work, E2E fencing, real ES256 validation
  with synthetic offline discovery, bad signature/issuer/audience/expiry/sub/HS256,
  and exact-origin CORS. No test validation bypass was added to the application.
- New public environment plan, offline configuration checks, runtime-role plan
  and candidate inventory; **no configuration values were installed remotely**.

The [new candidate manifest](qa-application-source-manifest-2026-11-17.json) binds
the exact current application **including all pre-existing staged/uncommitted and
untracked inputs**. `Program.cs` is the only previously reviewed input changed by
this task. The frozen 407-file manifest/archive and all old inventories are
preserved byte-for-byte. Of those 407 working-tree inputs, 406 remain unchanged;
the approved Program delta is explicitly recorded in the new manifest. Do not
claim the new application still has the old 407-source identity or modify the old
migrator's strict inventory guards to make it accept new application source.

Validate locally:

```sh
python3 deploy/qa/check-application-plan.py
python3 tools/qa-application-inventory.py
node --test src/frontend/scripts/build-qa.test.mjs
git diff --check
```

These are source/configuration checks only, not cloud commands or deployment
authority. Candidate release/publication approval is still required. No staging,
commit or push was performed; the original Git index/HEAD and unrelated files
were preserved.

## Supabase QA Auth review

Public, credential-free discovery/JWKS was checked on 2026-11-17:

| Setting | Exact QA value |
| --- | --- |
| Public project URL | https://yqngxhryzklrhhryzzoi.supabase.co |
| Trusted metadata address | https://yqngxhryzklrhhryzzoi.supabase.co/auth/v1/.well-known/openid-configuration |
| Expected issuer | https://yqngxhryzklrhhryzzoi.supabase.co/auth/v1 |
| Discovered JWKS | https://yqngxhryzklrhhryzzoi.supabase.co/auth/v1/.well-known/jwks.json |
| Expected audience | authenticated |
| Public signing keys observed | ES256 / EC P-256 |

ASP.NET JwtBearer derives signing keys/issuer from that explicitly configured HTTPS
metadata endpoint and validates signature, issuer, audience, lifetime (existing
framework clock skew) and usable `sub`; inbound claim remapping is disabled. Its
existing allowlist is **ES256 and RS256**, not ES256-only. HS256 is not accepted.
There is no fictitious ES256-only environment switch and no shared JWT signing
secret is needed. The offline tests retain the real validation parameters and
replace only remote discovery with a synthetic public key/issuer fixture. A real
QA user's token/login, email confirmation and key rotation have **not** been tested.

Frontend uses supabase-js for email/password sign-in/registration/session handling;
the API Bearer interceptor targets only the configured API origin. External cover
images do not receive application Bearer tokens. No browser application DB/Storage
access is introduced; Supabase QA is Auth only. Browser publishable key must be
supplied later via the public QA build variable, never a service-role/secret key.

Once the stable frontend origin exists, separately approve verification/update of
Supabase QA **Site URL** and exact redirect allowlist (root URL with trailing slash
for the current default flow). Registration currently passes no redirect override,
so email confirmations use Site URL. Do not invent `/auth/callback` or
`/auth/confirm`: neither is implemented. Password-recovery UI is also not implemented;
do not claim that journey is ready. Test actual confirmation landing/session
behavior before inviting testers. No wildcard preview redirects, Auth-user creation,
email/SMTP changes or shared development-project redirects were made here.

API CORS must set **AllowedOrigins__0 to the exact stable frontend HTTPS origin**,
without trailing slash/path. Allow Authorization/Content-Type preflights; existing
configuration exposes Retry-After/Location and does not allow credential cookies.
No wildcard origins or wildcard Vercel previews. CORS is not authorization or a
private-network boundary; authenticated API Library isolation is mandatory.

References reviewed: [Supabase JWTs](https://supabase.com/docs/guides/auth/jwts),
[redirects](https://supabase.com/docs/guides/auth/redirect-urls),
[changelog](https://supabase.com/changelog.md). The relevant TLS CBC deprecation
does not require any downgrade; this candidate requires TLS 1.3. Public keys being
available is not proof that a real user session succeeds. No Supabase resources,
users, Auth settings, grants, Storage or application tables were changed.

## Railway API readiness

Proposed **new isolated** project `game-library-qa`, environment `qa`, service
`game-library-qa-api`, workspace `6636d7dc-798b-4470-8376-b36089020713`.
Reverify account/workspace/budget/region before any future provisioning; do not
reuse unrelated services. Existing historical proposal is one replica, 0.5 GB /
0.5 vCPU, 60-second drain; no additional database, worker, volume or keep-alive job.
No region was moved. Owner must select the actual available Railway region with
the accepted Ohio database and costs in mind before provisioning.

- Repository root build context; **Dockerfile.application**, not automatic buildpack
  detection or the old historical Dockerfile. .NET runtime is 10.0.14 on the pinned
  Noble amd64 base, non-root UID 1654; no SDK in runtime, no AOT/trim/single-file.
- Set both **PORT=10000** and **ASPNETCORE_HTTP_PORTS=10000**; ASP.NET does not
  automatically bind Railway's PORT alone. Domain target port is 10000.
- Install only the reviewed public variables from the environment plan plus exact
  `AllowedOrigins__0`, and one server secret **ConnectionStrings__Default** using
  the proposed separate runtime role. Never install the owner passfile/connection,
  migration inputs, Test/E2E connections, Storage/search/Steam credentials or
  inherited variables from another application.
- Npgsql connection selects the exact direct primary
  `ep-wispy-cherry-beqkq4cc.c-9.us-east-2.aws.neon.tech:5432`, `neondb`.
  Build/escape it with NpgsqlConnectionStringBuilder; require VerifyFull, image
  public CA bundle `/etc/ssl/certs/ca-certificates.crt`, GSS disabled, pool 0–5,
  connection timeout 15s and command timeout 30s. No unverified host, password in
  URI, SSL downgrade, alternate replica, pooler, automatic mutation retry or
  provider-internal end-to-end-encryption claim.
- No startup `Migrate`, `EnsureCreated` or seeding exists in Program. Set **no
  predeploy migration command**. The existing E2E reset action contains test-only
  migrations but remains **404 in Production**, even if the E2E auth flag is
  accidentally true. All fake flags must nevertheless be explicitly false.
- `/health` is anonymous **liveness only**: 200 without DB/Auth/Storage checks.
  It does not prove database privileges, cold-start latency or real login. It
  accepts Railway's `healthcheck.railway.app` hostname with existing AllowedHosts.
  Use `/health`, timeout 300s, target port 10000; no continuous DB keep-alive.
  Railway's deployment healthcheck is not continuous monitoring. See
  [Railway healthchecks](https://docs.railway.com/deployments/healthchecks).

The exact candidate image built locally:

`sha256:bc77495d7e43f3960b4e74e8fe9eeb1367f4eea931a999e2eb37cb27f4e58d5f`.

It was started locally **with network disabled and an unreachable synthetic
loopback DB setting**, never a Neon credential. Health returned 200, unauthenticated
API 401, both E2E routes 404, and cleanup opt-out warning was present. There were
no cleanup-failure warnings. Separate TestServer tests prove no DB/Storage calls
over simulated one-hour timer advancement. Actual Npgsql in the same runtime
passed a trusted loopback TLS1.3 fixture and rejected TLS1.2 and wrong-host
certificates under VerifyFull, without requesting credentials or executing SQL.
**No real runtime-role Neon connection was made**; that remains a next gate.

## Vercel Angular readiness

Proposed **new** `game-library-qa-web`, existing `bar-book` workspace/team
`team_06vOR8VzjmIVJfhtgodwg1wL`. Never reuse BarBook's project/domains/variables.
Recheck current workspace eligibility, access and limits before provisioning.

- Root `src/frontend`, Node **24.x**, `npm ci`, **npm run build:qa**, output
  `dist/game-library/browser`. Pin the existing lockfile; no dependency upgrade.
- Set only public build values **QA_API_BASE_URL** (stable API HTTPS origin),
  **QA_SUPABASE_URL=https://yqngxhryzklrhhryzzoi.supabase.co**, and
  **QA_SUPABASE_PUBLISHABLE_KEY** (QA browser-public publishable key). The build
  allowlist rejects unsafe URLs, development project reuse, legacy JWT/secret
  keys and test mode. Generated config serializes only those public values.
- Both default production and **production,qa** builds passed. The QA build used
  synthetic public input values, not an installed key; built JS contains the QA
  API/Auth URLs and **does not contain the development project ID**. Rebuild with
  approved real public values later; this synthetic local output is not deployable.
- Existing `vercel.json` routes extensionless SPA paths to `/index.html`, preserves
  static asset requests, disables Git auto-deployment and sets noindex/nosniff/
  referrer/cache-revalidation headers. Deep-link `/library`, `/manage` and `/login`
  smoke verification on real hosting is still required. Do not invent callback
  routes or assume noindex makes a public site private.
- Service worker caches the application shell/static assets only, not authenticated
  API or private cover responses. API remains cross-origin and is not proxied through
  a Vercel cache. Browser login plus backend Library authorization protect data;
  platform deployment protection is a separate owner/platform-eligibility decision.

## Verification results

| Fresh local verification | Result |
| --- | --- |
| New cleanup/ES256/CORS/Production-safety tests | **21 passed** |
| Existing image processor tests on host | **94 passed** (115 total with new tests) |
| Core unit suite | **337 passed** |
| Frontend unit suite | **465 passed**, 38 files |
| Frontend lint | Passed |
| Public QA build configuration tests | **15 passed** |
| New offline deployment-plan checks | **6 passed** |
| Angular production / QA production build | Passed; initial 677.02 / 677.12 kB, unchanged budgets |
| Pinned QA Linux Docker locked restore/publish | Passed |
| Actual QA runtime native ABI/ldd and image suite | **94 passed**, non-root Noble amd64, both native libraries resolved |
| Same-runtime resource smoke | 60 operations / two pipelines under 512 MiB; process peak 295,243,776 bytes, cgroup peak 264,835,072 bytes, zero OOM |
| Actual QA runtime Npgsql loopback TLS fixtures | TLS1.3 accepted; TLS1.2 and wrong-host rejected; VerifyFull; no credentials/SQL |
| Actual QA container startup/liveness/E2E fencing | Passed, no network/remote credential |

Warnings: existing Random Picker component stylesheet is 4.13 kB versus a 4 kB
warning budget (130 bytes over); build error budget still passes. Docker warns
about the intentionally fixed amd64 platform and mistakenly classifies the false
E2E auth boolean as a secret ENV; no password/secret is stored there. The SDK
probe reported an informational workload-verification notice; compile had zero
warnings/errors. No budgets, dependencies or platform gates were loosened.

No migration was executed, including in local tests. No database-backed CRUD/
two-user lifecycle suite or real-role grants test was rerun because this task
does not authorize migrations/roles/grants; selected composition/Auth/image tests
are database-free. Historical SQL regression evidence is not presented as a fresh
run. No browser E2E or hosted/real-user smoke was run. These remain explicit
verification limits, not claims of full application/Feature 013 release readiness.

All task containers were removed and Docker Desktop stopped after bounded tests;
existing containers/image tags were preserved. No remote resource/configuration,
database data, role/grant, migration, real credential or Git publication action.

## Short deployment sequence and next approvals

1. **Review/approve this exact candidate source manifest and runtime-role plan.**
   Next narrowly scoped execution approval: validate the proposed privilege matrix
   in isolated local PostgreSQL (explicitly permitting test-only schema/role/grant
   setup), then authorize one `game_library_qa_runtime` role and only reviewed
   exact-target grants/effective-PUBLIC corrections on Neon, private credential
   handoff, and bounded read-only runtime connectivity/privilege checks. See the
   [runtime-role plan](qa-runtime-role-plan.md). No further application migration.
2. Separately approve creation of the **new isolated empty** Railway/Vercel projects/
   service and stable HTTPS origins in the pinned workspaces, selected Railway
   region/resource/spend envelope, no auto-deploy or unrelated resource reuse.
   Do not attach a Git source that auto-deploys before configuration/approval.
3. Approve only the exact runtime secret/public variables and CORS value, QA Auth
   Site URL/exact redirect verification or changes, and two private QA tester
   accounts/email-confirmation policy. Recheck no managed-cover metadata before
   enabling the cleanup opt-out. Keep Storage/Steam/search/fakes disabled.
4. Approve publication of the reviewed source (stage/commit/push) **or explicitly
   approve a verified manual archive-upload route**; the historical Railway plan
   expects an exact commit SHA. Do not silently deploy HEAD: the intended candidate
   includes existing uncommitted changes. Then separately authorize deploying
   that exact API candidate, followed by the frontend rebuilt for the approved
   origins/public QA key. Do not deploy the synthetic local build output.
5. Approve bounded two-real-user QA smoke writes: login/confirmation, platform and
   game CRUD, external-cover keep/replace/remove, Play Log, filters/Picker, deep links,
   logout and cross-user denial. `/health` alone is insufficient. Monitor errors,
   cold-start latency and provider spend without polling Neon to keep it awake.

**Remaining blockers to deployment:** unapproved new source publication/release;
runtime role/effective grants/credential and actual-image connectivity absent;
stable origins/CORS/QA public key not installed; actual Auth URL settings/testers/
login not verified; cloud provisioning/region/cost approvals and hosted smoke pending.
No further infrastructure redesign is required. Stop after this local preparation.
