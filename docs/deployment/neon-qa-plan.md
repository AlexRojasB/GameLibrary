# Approved Neon private QA — local plan and next gate

2026-11-15. **Owner-created project discovered; configuration review and connectivity
pending. Do not create another project. Migration/deployment remain NO-GO.**

## Latest discovery — actual target, not an approved configuration replacement

The owner manually created **`GameLibraryQa`**, ID **`super-fire-30188358`**, in the
verified **Alexander Rojas / Free** organization. Read-only metadata identifies
PostgreSQL **18**, **Ohio (`aws-us-east-2`)**, **0.25–2 CU autoscaling**, default branch
`production`, database `neondb` and owner role `neondb_owner`. These differ from the
approved PostgreSQL 17/Oregon/fixed-0.25-CU proposal. No setting was corrected or
silently accepted, and no second project was created.

Scale-to-zero is reported as `0` (plan default), which on Free means five minutes;
this is not a claim of a timed suspension test. See the
[discovery report](neon-qa-project-discovery.md) and the actual `returnedTarget` /
`discovery` sections of the [local plan](../../deploy/qa/neon-project-plan.json).
The original `proposedCreateProjectInput` below/in JSON is retained as **historical
comparison only**, not a pending command. The existing approved architecture still
specifies PostgreSQL 17; discovery alone does not approve PostgreSQL 18, a different
region or a higher compute ceiling for deployment.

The owner approved a dedicated Neon PostgreSQL **17** application database while
retaining existing Supabase QA **exclusively for authentication**. The approval also
allows local deployment documentation/source review and preparation. Nothing in
this task authorizes cloud writes, DDL, credentials installation or Git publication.

## Current documents and authority

- [Approved architecture amendment](../architecture/private-qa-neon-amendment.md)
  explicitly scopes the exception to private QA; production/development behavior
  remains governed by the original architecture.
- [Target plan](../../deploy/qa/neon-project-plan.json): verified actual IDs/settings,
  historical proposed input, differences, isolation rules and all execution OFF.
- [Environment plan](../../deploy/qa/neon-qa-environment-plan.json): exact public
  values/names, unassigned origins, server-only input names and unsupported switches.
- [Migration strategy](neon-qa-migration-strategy.md),
  [cleanup review](neon-qa-cleanup-review.md), and
  [complete source review](neon-qa-source-review.md).

These are new companions, not a modified frozen source snapshot. The original
Supabase database/Render/personal-Vercel plans remain historical; they must not be
automatically applied. The current Vercel target is the existing `bar-book` workspace
with a **new Game Library project**, not BarBook itself. Supabase application-table
migration instructions are superseded for Neon QA; no Supabase settings are changed.

## Historical proposed creation envelope — do not execute

One `game-library-qa` Neon project in **Alexander Rojas** / Free organization
`org-falling-fog-81506005`, PostgreSQL 17. Proposed region **AWS us-west-2 (Oregon)**,
listed by the read-only regions tool on 2026-11-15; it is near the existing Railway
`sfo` proposal, not proof of measured latency. Region selection needs explicit
creation approval; do not silently move the frozen Railway region.

Initial automatic resources only: root branch `qa`, database `game_library_qa`, owner
role `game_library_qa_owner`, one read-write compute. Request fixed **0.25 CU**,
five-minute scale-to-zero and six-hour history. No extra branch/database/role,
cloned data, paid plan, extensions, Auth/Data API/Realtime/Storage/Functions or
application schema is requested. Complete input acceptance has not been tested.
Stop if the provider requires changed defaults, paid features or another region.

Do not operate on protected Neon projects `nameless-cherry-10121327` (BarBook) or
`old-violet-31409208` (CRFuelScraper), any of their branches/data/credentials, the
protected Supabase development project, or unrelated Railway/Vercel resources.

Actual IDs/direct host are now recorded from metadata, not invented. A separate
runtime role, credentials and deployed origins remain unknown/unconfigured. No
application DB data will be placed in Supabase QA; Auth retains its own managed store.

## Environment handoff — no values installed

Railway Production-mode API uses the existing pinned Noble/glibc linux/amd64 .NET 10
Docker image/build design, one replica, 0.5 GB/0.5 vCPU proposal, port 10000, health
`/health`, 60-second draining, no predeploy/startup migrations or auxiliary services.
`ConnectionStrings__Default` will select the Neon runtime role/direct primary using
VerifyFull and a maximum five-connection client pool. No owner migration credential
or Storage credential belongs in runtime.

JWT metadata is explicitly QA:
`https://yqngxhryzklrhhryzzoi.supabase.co/auth/v1/.well-known/openid-configuration`.
Require HTTPS, issuer match, audience `authenticated`, expiration/signature/usable
subject; verify a real ES256 token later. Current API also permits RS256 for its
trusted issuer; there is no supported environment-only ES256-exclusive switch.
No Auth setting/key/user or signing algorithm was changed.

Vercel gets only `QA_API_BASE_URL`, `QA_SUPABASE_URL` and the browser-public
`QA_SUPABASE_PUBLISHABLE_KEY`; build `npm run build:qa`, root `src/frontend`, Node
24.x, static output `dist/game-library/browser`. Do not supply any Neon connection,
server key, operator input or unrelated application's variables. Exact frontend/API
origins and the QA public key value remain unset. No fabricated callback route,
wildcard redirect, shared variable, BarBook alias or source auto-deploy is allowed.

Uploads/Storage, Steam integration and all E2E/fake switches remain disabled.
Optional cover search remains unconfigured initially; manual/external URL covers
continue to work. No native dependency/runtime changes are proposed.

## Cleanup choice remains open

The frozen API always starts periodic cleanup despite uploads being disabled.
`PrivateQa__DisableCoverCleanup=true` is a **proposed unsupported** opt-out, not a
working setting. See the review for default-on production behavior, fail-fast
incompatible configuration, zero-managed-cover operator evidence and required tests.

Before deploying, either explicitly retain cleanup with its monitored Neon usage,
or separately approve the small local opt-out implementation and a new versioned
source manifest. Do not mutate the historical 407 hashes to make that delta appear
already reviewed. No managed-cover data or Storage namespace may be introduced in
the opt-out mode.

## Cost/readiness constraints

- Neon Free is freshly confirmed for the discovered target organization. Current public allowance
  is 100 CU-hours/project/month, 1 GB PostgreSQL/project, 5 GB egress/project. No
  paid upgrade or quota expansion is approved. The actual 2-CU maximum is higher
  than the approved fixed-0.25-CU ceiling and requires owner review, not an automatic
  settings change or deployment acceptance.
- Periodic cleanup could consume roughly 60 CU-hours/30 days in a simple otherwise
  idle 0.25-CU/five-minute-wakeup model. Continuous 0.25 CU uses 180 CU-hours/30 days,
  beyond Free's 100. There is no measured target usage yet.
- Vercel `bar-book` workspace was confirmed Hobby/OWNER in the preceding assessment;
  only a separate eligible personal/noncommercial project is proposed. Shared
  build/traffic quotas remain shared. No private-project/no-index claim substitutes
  for authentication and server authorization.
- Railway Hobby was active in the preceding assessment. Existing $5 subscription
  remains; estimate $2–5/month additional small-API resource usage, not a guarantee.
  Credit treatment/spend limits need owner review; no billing setting is changed.
- Supabase QA Auth organization/plan, inactivity behavior, final URL settings,
  tester creation/confirmation/email delivery and real login still need verification.
  Keeping Auth preserves Supabase dependency/cost, even with Neon application data.
- Docker was unavailable during the preceding assessment. No new Linux build,
  integration/browser run, Neon login/migration or hosted smoke is claimed here.

## Next narrowly scoped approval

**Confirm that `super-fire-30188358` is the intended project despite the documented
differences, then approve a bounded driver-only read-only connectivity/metadata
verification on its exact branch/database/owner role.** No such query/probe is
authorized or executed by this discovery task.

The future probe needs an explicitly approved private credential handoff supplied
by the owner outside the repository, verified TLS/hostname, pooling disabled and a
read-only transaction with fixed metadata SELECTs. Do not call a credential getter,
create a read-only role, start the API, install secrets or run migrations as part of
discovery. See the report for exact scope and remaining checks. Inspection approval
is not deployment acceptance of PostgreSQL 18/Ohio/autoscaling; resolve those
architecture/cost/region differences separately before migration or deployment.

No resource rename/resize, PostgreSQL replacement, additional project/branch/role,
grant, billing/Auth change, Git publication or deployment is authorized. If the
owner wants the original configuration instead, return for a separately scoped
decision; do not fix it by creating or modifying resources automatically.

Later gates remain separate: local guard/cleanup implementation as selected; exact
source approval; runtime-role/private-input setup and verified TLS preflight; exact
migration approval; origin/Auth/tester changes; service provisioning/deployment and
two-real-user hosted smoke. Architecture approval is not a release approval.

## Local verification

Run `python3 deploy/qa/check-neon-plan.py` for offline plan/source-manifest safeguards,
`python3 deploy/qa/check-railway-plan.py` for the frozen base plan and
`node --test src/frontend/scripts/build-qa.test.mjs` for the public-build allowlist.
No command above contacts a cloud provider, starts the API or migrates a database.
The [source report](neon-qa-source-review.md) separates fresh checks from historical
regression/security evidence and records final preservation results.
