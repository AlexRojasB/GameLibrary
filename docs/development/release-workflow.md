# Development, QA and release workflow

## Status and approval boundary

2026-11-19: this is a locally prepared workflow, not an installed CI policy or an
authorization to publish, merge or deploy. The repository currently has **no GitHub
Actions workflows, required checks, branch protections, rulesets or protected
deployment environments**. Those controls need separate owner approval to configure.

Use `feature/*` (or narrowly scoped `fix/*`) -> `dev` -> immutable QA candidate ->
owner-approved promotion to `main`. No permanent QA branch is required.

## Audited baseline

Live remote heads, independently read on 2026-11-19:

| Ref | Commit |
| --- | --- |
| `dev` | `29ba0d97307739484363800450575b2666c37354` |
| `main` | `3be178e6930271c31633dc5a6ab3bf11fc05fe70` |
| `release/private-qa-2026-11-18` | `e2da3b5a73c70f58a940e263ca362618d24146e7` |

`main` is an ancestor of `dev`: 0 main-only commits, 16 dev-only commits. The dated
release is also an ancestor of `dev`: 0 release-only commits, one dev-only commit.
No main/release code needs rescuing or history rewriting. Preserve the dated release
reference. The main-to-dev delta spans 376 files, not just the latest Steam fix.

The current `dev` includes the QA-validated code and is a suitable starting point
for new approved feature work. A local unpublished logging fix does not change that
baseline until it is reviewed, committed and promoted with approval.

## Three different operations

1. **Git alignment:** integrate reviewed commits and retain history. Moving a Git
   ref neither proves QA ran that ref nor authorizes a deployment.
2. **QA alignment:** deploy and validate one exact commit/artifact under the existing
   QA configuration. A later configuration redeploy may have a new deployment ID
   while retaining the same code image.
3. **Production promotion:** separately approve release code, production configuration,
   release gates and rollout/rollback. Merging `main` is not deployment approval.

## Feature development

- Start from a clean, freshly checked `origin/dev`; inspect local work before updating.
  Use a normal fast-forward update where possible, never reset/rebase published history.
- Branch `feature/<approved-spec>-<topic>` or `fix/<topic>` from updated `dev`.
- Follow AGENTS/spec authority. Keep changes and tests feature-local.
- Open a PR to `dev` only after approval of the exact push/PR action. Record the
  source/base SHA and verification. Do not develop directly on `main`.
- Do not disturb other worktrees. In particular, the original detached worktree has
  substantial preserved uncommitted work; use the clean dev or a separate worktree.

## Proposed CI and protection (not configured)

After explicit owner approval, install PR/push checks for `dev` and `main` with
read-only repository permissions, no deploy credentials and no deployment step:

- Backend: .NET 10 locked restore, Release build, Core unit tests and real PostgreSQL
  integration/security tests on a disposable CI-only database.
- Frontend: Node 24, `npm ci`, `npm run test:qa-config`, unit tests, lint and build.
- E2E: the existing small desktop/mobile Playwright suite with local non-Production
  fake auth/Steam/provider boundaries and a separate disposable E2E database.
- Prepare the existing pinned native asset before backend build. Use the approved
  Linux amd64/Noble native verification where that gate is relevant; do not substitute
  a macOS pass for native release evidence.
- Never point reset/migration fixtures at QA or production, or supply real Steam,
  Supabase service-role or application database credentials to ordinary CI.

Choose stable check names before making them required. Protect `dev`/`main` with PRs,
passing checks, resolved review conversations and no force pushes/deletions; require
an owner review for release promotion. Confirm GitHub's reviewer/bypass semantics
for this single-developer repository rather than installing an impossible self-review
rule. Record any explicit owner bypass. Do not claim a prose approval rule is enforced
by GitHub until the approved settings are actually verified.

## QA candidate and traceability

- Select the exact reviewed commit after integration into `dev`. Do not deploy a
  moving branch tip, dirty checkout or another worktree's uncommitted files.
- Prefer an immutable commit/archive and a deployment receipt. A short-lived
  `release/<date>-rcN` reference is optional when a moving integration branch makes
  freezing difficult; creating/pushing it also requires approval. Keep old references.
- Receipt fields: full source SHA/tree, archive checksum/manifest, API image digest,
  frontend deployment ID, backend deployment ID, configuration revision/reference,
  checks/smoke evidence, owner approval and previous rollback targets. No secret values.
- QA currently uses manual CLI uploads, not Git-triggered builds. Keep that unchanged
  unless a separately approved CI/CD change explicitly replaces it.
- Deploy only after approval: API health/auth gates, frontend built-output/origin
  checks, then exact QA alias promotion and a bounded authorized smoke. Treat real
  QA data as durable. Do not reset it or bulk-import to test a logging change.
- Any code change after validation creates a new candidate. Locally tested logging
  changes are not automatically part of the previously validated `29ba0d9` snapshot.

Current QA code alignment:

- Vercel `dpl_3C4sYn6Qh8sJx2ZbRp2EWH54dSFH` has `sourceCommit=29ba0d97307739484363800450575b2666c37354`.
- Railway `71864044-331f-4fb1-90a3-48462f739aac` reuses image
  `sha256:8f242023058c6e4799326c49e821559a938884284595084df9f6708e82a5dbe1` from
  the original `845f122b-3cdc-4eb1-b57c-86e925a6caec` source upload. The original
  archive/manifest and CLI receipt bind that upload to `29ba0d9`; current Railway
  metadata does not independently contain a native Git commit SHA.
- These are QA-only projects even though Vercel calls the stable alias target
  `production` and ASP.NET Core runs with environment `Production`.

## Proposed PR/merge plan — do not execute without approval

1. Review the local `fix/qa-log-volume-release-workflow` delta. After approval, commit,
   push that branch and prepare a PR to `dev`; it adds no CI workflow or cloud setting.
2. Separately approve CI/protection changes; verify the actual required checks.
3. After integration, select the new immutable dev SHA, approve its QA deployment,
   and revalidate the logging policy and existing bounded functional behavior without
   modifying the owner's 131-game library or Steam account without permission.
4. Prepare `dev` -> `main` with the validated SHA pinned in the PR body. Use a merge
   commit to preserve existing meaningful dev history; do not squash/rebase the
   integration/release history. Recheck the head immediately before approved merge.
5. If promoting the existing `29ba0d9` baseline instead, explicitly state that the
   logging fix is not included and that missing checks/release gates remain open.

The existing main/validated-dev ancestry has no merge conflict. This is Git
readiness only, not approval of all 16 commits or Feature 013 release readiness.
Uploads remain gated off. No main promotion is approved by this document.

## Production gate and rollback

Before approving any main merge, inventory provider GitHub App/integration triggers
for the intended production targets. Empty repository webhooks/Actions and disabled
QA Git links do **not** prove that every possible production integration is detached.
If a main merge could auto-deploy, obtain approval to decouple/gate that trigger first.

Production needs a separate owner approval identifying exact targets, source/artifact,
public origins/Auth issuer, runtime secrets, feature gates, tests, costs and rollback.
QA's Neon amendment is private-QA-specific, not blanket approval of a production
database/provider change. Missing production configuration and pending Feature 013
gates must not be inferred from a QA pass.

Promote the exact approved artifact when providers support it; if a rebuild is
necessary, record the new artifact and reverify it. Keep release-code promotion and
deployment approvals distinct. Bring actual main-only hotfixes back to `dev` through
reviewed normal integration; never force-update branches.

Rollback means restore the recorded compatible API artifact/frontend alias and, if
approved, the corresponding configuration revision. It is not a Git reset, database
reset or down-migration. Preserve all user data and sealed secrets. Migrations, if
ever needed, require a separate reviewed compatibility/forward-recovery plan. A code
rollback cannot undo a user's real imports and must not try to delete them.
