# Direct private QA deployment — approval handoff

2026-11-18. **Local fix and planning only; no cloud writes or deployment.**
Use the existing QA topology and tooling. No new password operator, support-ticket
gate, database, worker, proxy, registry or CI infrastructure is needed.

## Shortest route

1. Reset the **existing** restricted runtime role's password through Neon Console.
2. Create the isolated empty Railway service and Vercel project; obtain their own
   generated HTTPS origins. No source attached and no automatic deployment yet.
3. Privately install the runtime connection on Railway; set the existing QA
   variables, exact CORS origin and QA Auth URL settings/testers.
4. Direct-upload the corrected, reviewed source snapshot to Railway and Vercel
   using their CLIs; **no Git commit/push is necessary** for this manual route.
5. Verify real login, CRUD, 409 deletion protection and two-user isolation. Return
   the stable frontend URL only after this succeeds.

## A. Neon — exact proposed mutation, awaiting approval

**Prefer Console → GameLibraryQa → production → Roles →
game_library_qa_runtime → Reset password.** This is the provider-supported reset:

```text
POST /api/v2/projects/super-fire-30188358/branches/br-muddy-base-be5m73fj/roles/game_library_qa_runtime/reset_password
MCP equivalent: reset_postgres_role_password(project_id, branch_id, role_name)
```

- Generates a **new** password; does not reuse the uninstalled staged password.
- Returns provider operations; wait for completion before authentication checks.
- Drops compute connections and may wake the endpoint. No application migrations,
  data mutations, repeated grants or role recreation are requested.
- Public provider implementation applies LOGIN/password changes to existing
  SQL-created roles. Verify actual LOGIN and unchanged OID/grants afterward; the
  published reset contract does not spell out every NOLOGIN transition detail.
- If the reset installs the password but leaves NOLOGIN, the only proposed extra
  SQL is **`ALTER ROLE game_library_qa_runtime LOGIN;`**, with separate explicit
  conditional approval. It contains no password. If reset fails/is uncertain,
  reconcile; do not repeat it or create a replacement automatically.

**Credential handling:** owner uses Neon Console privately, saves the generated
credential in their existing password manager, then enters the runtime connection
directly into Railway's service-scoped secret variable after handoff approval.
No password in chat, command arguments, source, logs, image, evidence or Vercel.
Do not invoke the secret-returning MCP reset into this conversation or use CLI
reset with visible stdout. No new secret-management program is proposed. The old
staged file is untouched and must not be mistaken for the new installed credential.

This is a different path from the custom SQL password-setting operator: its
`log_min_messages=fatal/panic` policy is not a provider prerequisite for Console
reset. Use Neon's supported managed workflow and normal documented credential
redaction; do not change logging, prehash passwords or claim an exhaustive guarantee
about inaccessible provider internals. Escalate only a concrete provider failure.

**After reset:** confirm role LOGIN/password-present (boolean only), OID 32768,
unchanged exact reviewed grants/memberships/ownership; authenticate with the runtime
over TLS1.3/VerifyFull, perform permitted reads and read-only privilege/denial checks.
Verify the 11 applied migrations, schema and no-cover state remain unchanged. Owner
credentials remain operator-only, never the API identity. No new custom guard bundle.

### Replacement evaluation (not requested for execution)

MCP `create_postgres_role` exists, but default `admin` joins neon_superuser;
`read_write` grants broad current/future table access, not our validated matrix
(genres/history/cover metadata/Steam permissions differ). Neither is an acceptable
drop-in least-privilege replacement. If the existing role genuinely cannot be
activated, return for approval of a named SQL-created restricted replacement and
**only the already reviewed exact grants**, followed by provider reset and checks.
No blanket grants, owner runtime, admin creation or old-role deletion. The existing
reset is strictly smaller and is the recommended first operation.

## B. Direct deployment settings

The non-secret variable list and TLS/pool options in
[`application-environment-plan.json`](../../deploy/qa/application-environment-plan.json)
remain the configuration source. Its historical `runtimeRoleCreated=false` is
superseded by the existing-role observation below. Do not use old PG17/migration
setup plans or the historical Dockerfile for this deployment.

| Target | Proposed configuration |
| --- | --- |
| Railway | Workspace `6636d7dc-798b-4470-8376-b36089020713`; new private project `game-library-qa`, environment `qa`, empty service `game-library-qa-api` |
| API build | Repository-root snapshot; `deploy/qa/Dockerfile.application`; existing pinned Noble/glibc linux/amd64 .NET 10 recipe, native assets, non-root and TLS1.3 policy unchanged |
| API resources | Propose US East Metal `us-east4-eqdc4a` (Virginia, near Ohio DB), one replica, 0.5 GB / 0.5 vCPU, existing Hobby allowance/usage budget; no plan upgrade without approval |
| Runtime/network | Production mode; `PORT=10000`, `ASPNETCORE_HTTP_PORTS=10000`, generated HTTPS domain to port 10000; `/health`, timeout 300s; no predeploy migration command |
| Railway secret | Only `ConnectionStrings__Default`: exact direct Neon host, neondb, restricted runtime identity; VerifyFull, CA `/etc/ssl/certs/ca-certificates.crt`, GSS disabled, pool 0–5, timeouts 15s/30s, error detail/persist-security-info false |
| Optional features | `PrivateQa__DisableCoverCleanup=true`; uploads, Storage credentials, Steam, search credentials and all E2E/fake flags remain disabled/absent. Confirm zero cover rows/references immediately before first startup |
| API Auth | QA discovery URL `https://yqngxhryzklrhhryzzoi.supabase.co/auth/v1/.well-known/openid-configuration`, HTTPS required, audience `authenticated`; existing ES256/RS256 allowlist unchanged |
| CORS | `AllowedOrigins__0` = **actual stable frontend HTTPS origin**, no trailing slash/path, no wildcard previews; Authorization/Content-Type preflights, no credential cookies |
| Vercel | Team `team_06vOR8VzjmIVJfhtgodwg1wL` (`bar-book`, observed Hobby); **new** `game-library-qa-web`, no Git attachment/automatic deploys, no BarBook reuse |
| Web build | Root `src/frontend`, Node 24.x, `npm ci`, `npm run build:qa`, output `dist/game-library/browser`, existing SPA rewrites/headers |
| Web public variables | `QA_API_BASE_URL` = actual generated API HTTPS origin; `QA_SUPABASE_URL=https://yqngxhryzklrhhryzzoi.supabase.co`; `QA_SUPABASE_PUBLISHABLE_KEY` = QA modern publishable key only |

The **old pre-fix image digest is not the corrected release**. Build the changed
candidate with the same Dockerfile before deployment; do not deploy yesterday's
image or current Git HEAD without the uncommitted fix. `/health` checks liveness,
not database access or Auth. No CDN on the API or sensitive request/body tracing.

For the simplest no-Git route, use a clean directory containing **only** the new
release manifest's selected files, not the entire dirty workspace. Exclude agent
config, retired Render config, user secrets, local env/passfiles, generated QA
configuration, node_modules, bin/obj and previous build output. After approval,
Railway CLI `railway up` uploads/builds the API snapshot to the explicitly selected
new service; Vercel CLI deploys its frontend to the new project's stable production
target (the QA-only project). Neither requires staging/committing/pushing Git.
The provider-generated origins are not known yet; do not invent/reserve aliases.
Railway CLI is installed locally; Vercel CLI is not. Use the official Vercel CLI
via npx/install at deployment time, outside application dependencies, after approval.

## C. Fresh read-only discovery and remaining unknowns

- Neon API at **15:33:11.818Z** confirms the existing role is visible,
  `authentication_method=no_login`, protected=false, on the exact branch, database
  and endpoint. No SQL/catalog/password query or reset was made in this task.
- Railway workspace is accessible, with two unrelated projects; no Game Library
  project exists in its complete listing. No unrelated variables/services inspected.
- Vercel deployment context shows BarBook/Hobby and its existing BarBook project.
  An explicit-team, exact-name query returned no `game-library-qa-web` project.
  Create permission/capacity is not proven by a read; handle any concrete refusal
  without changing plans, billing, team defaults or other projects.
- Supabase QA URL confirmed by QA-scoped MCP. Public discovery/settings at
  **15:38:24.636Z**: issuer/JWKS match the QA project, ES256 EC P-256 keys,
  email sign-in enabled, **signup disabled**, email autoconfirm false. A modern
  active publishable key is available; its value was not printed or installed.
- Site URL/exact redirect allowlist and the intended tester accounts are **not
  verified** by the public endpoint. Once origins exist, review/change only QA
  Site URL and exact root redirect. No invented `/auth/callback` route, wildcard
  preview redirect, SMTP setup or password-recovery feature. Owner must name two
  testers and approve provisioning/invites if they do not already exist; keep
  public signup disabled. Actual email-confirmation/login remains a hosted check.

Private QA here means **approved QA Auth accounts and private API data**, not an
inaccessible login shell. HTTPS URLs are publicly reachable; noindex and CORS are
not access controls. If access to the shell itself must be restricted, approve
existing provider deployment protection separately; do not silently add a proxy
or paid protection product.

## D. Remaining remote approvals — none granted by this document

| Approval | Exact scope and side effects |
| --- | --- |
| 1. Neon activation | One official reset of the existing role on the exact branch; accept generated replacement credential and compute disconnection; optionally authorize the conditional LOGIN-only ALTER above; verify without exposing password/verifier. No grants/recreation/migrations |
| 2. Provisioning and URLs | Create only the empty Railway project/environment/service and separate Vercel project in the stated scopes; approve proposed region/resources/current-plan spend; generate only those projects' HTTPS origins; no source/auto-deploy/other-project changes |
| 3. Configuration and handoff | Owner privately installs only the runtime connection in Railway, reviewed public variables/exact CORS and frontend QA values in the new projects; approve QA Auth Site URL/root redirects if needed and named tester provisioning/invites. No owner/service-role secrets in runtime/frontend, no signup broadening or Supabase DB/Storage changes |
| 4. Release, deploy and smoke | Approve the revised manifest including the 23001 fix and manual **source upload** (not Git publication); rebuild/deploy API, then QA frontend with actual origins/key; allow bounded two-tester application writes/cleanup for smoke: login/confirmation, CRUD, in-use Platform 409, cross-user 404, filters/Picker/Play Log/deep links/logout. No migrations or unrelated writes |

These approvals may be given together with the named testers and accepted resource
budget, or sequentially. Until then all remote actions remain pending.

## Local verification / source identity

- Core now handles both `RestrictViolation` (23001) and `ForeignKeyViolation`
  (23503) in **Platform delete only**, returning the existing safe domain error/409.
- Real isolated **PostgreSQL 18.7** regression asserts the actual 23001 and HTTP
  409 for Owned/Wishlist/Interested, persistence after denial, cross-user 404 and
  successful deletion once unused. It reproduced all three 500s before the fix.
- Final related integration run: **123 passed**; Core: **337 passed**; backend
  solution build: **0 warnings/errors**; public QA build-config tests: **15 passed**.
  An intermediate wider test run lacked the local Default connection for the
  foundation tests; corrected test-only configuration gave the passing final run.
  No remote database or credential was used. Disposable local PostgreSQL was removed.
- Frontend unit suite: **465 passed / 38 files**; lint passed; six existing offline
  deployment-plan checks passed (they describe the historical plan, not live state).
- Corrected QA linux/amd64 image built locally using the unchanged Dockerfile:
  `sha256:370750415b0287f80cdad381174d882ae3c6147dbf30c838f666956a34b967f6`.
  Non-root UID 1654 smoke passed: health 200, unauthenticated platforms 401,
  Production E2E reset/session 404, explicit cleanup-disable warning. Only a
  synthetic unreachable loopback connection was supplied, no real credentials.
  Initial smoke harness attempts hit startup connection closure / an incomplete
  session payload; the final bounded run used a valid synthetic request and passed.
- No frontend behavior/dependency/native/schema changes. Full native decoder/TLS
  fixture evidence remains historical; no fresh hosted/real-user smoke is claimed.
  Existing Vitest poolOptions deprecation and Docker fixed-platform/false-E2E-flag
  lint warnings remain; no settings or budgets were weakened. Docker is stopped.
- Revised release: `qa-application-source-manifest-2026-11-18.json`; includes all
  prior candidate source plus this fix/tests and this handoff. The earlier manifest,
  frozen hashes and inherited Git index remain unchanged. The old dated inventory
  checker intentionally describes the older candidate; it must not be weakened to
  pretend the old identity still covers these new bytes. No new inventory tooling.

References: [Neon reset](https://neon.com/docs/reference/api/branches/reset-project-branch-role-password),
[role access choices](https://neon.com/docs/manage/roles),
[Railway local upload](https://docs.railway.com/cli/up),
[regions](https://docs.railway.com/deployments/regions),
[Vercel CLI deployment](https://vercel.com/docs/cli/deploy).
