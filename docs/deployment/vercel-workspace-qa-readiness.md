# Existing Vercel workspace — separate Game Library QA project

2026-11-06. **Owner-approved workspace choice; no project creation/deployment yet.**
This supersedes the personal-only proposal. Use the existing workspace that hosts
BarBook, but never reuse or modify the BarBook project, secrets, domains,
deployments or settings. The old personal plan/report remain historical.

## Exact workspace and authorization evidence

| Check | Observed result |
| --- | --- |
| Workspace slug | **`bar-book`** |
| Exact team ID | **`team_06vOR8VzjmIVJfhtgodwg1wL`** |
| Workspace dashboard | <https://vercel.com/bar-book> |
| Authenticated user | `proxtos-5361`, ID `u9w3FMff1gwTyAWS7R2rEShX` |
| User-level billing | Active Hobby; **not verification of the team's plan** |
| Workspace identity cross-check | Current authenticated `defaultTeamId` and explicit-scope 403 metadata agree with the previously returned BarBook project `accountId` |
| `get_team`, explicit team ID | **403**, missing authorization for scope `bar-book` |
| `list_projects`, explicit team ID | **403**, same scope authorization error |
| `list_team_members`, explicit team ID and authenticated-user search | **403**, same scope authorization error |
| SAML / enforced SSO flags in error | Both false; the provider still requests team consent |
| CLI fallback | `vercel` not installed; no alternate CLI credentials were read or installed |

The previously observed protected project is `bar-book-web`, ID
`prj_OTIBpJxMSv6l6ktqk1iN9BQ3k0Qi`. It is identity evidence only: no BarBook
settings, variables, domains, deployment or source was read or changed in this task.
No unscoped listing or different scope was used to bypass the explicit 403.

**Project-create permission is NOT verified.** A successful user identity read,
old project-list visibility or existence of a create tool is not proof of current
team write permission. No create/deploy/test POST was used as a permission probe.

## Authorization action pending

The provider explicitly requested `authorize_team_access` for the exact selected
team. A **single-use, ten-minute consent link** was generated and returned to the
owner in the conversation. Status: **`requires_user_action`**. Generating that link
does not grant access, create a project or change BarBook/team settings; approval
has not been observed. Its expiring URL is intentionally not stored in the repo.

Next steps:

1. Open the conversation's consent link while signed in to the intended account;
   review that it names **`bar-book`** and review the requested app permissions.
   Approve only if acceptable; complete SSO if the provider prompts. If expired,
   request a new link for this same team. No token or credential needs to be shared.
2. Retry **the same explicit team** reads on the **same connection** after approval:
   team identity/billing, member role/grants, and complete paginated project list.
   Do not reconnect as a first step or change the account's default team.
3. Verify the authenticated member's project-creation role/permissions and this
   connection's write grants from available permission metadata/owner confirmation.
   Read access alone is insufficient. If a read-only interface cannot conclusively
   establish create permission, report that remaining limitation; do not test it
   with a write. Confirm no existing `game-library-qa-web` project before creation.
4. Verify actual team plan, active/unblocked status, usage and spare project quota.
   Keep creation blocked if any of those remain unknown or require payment.
5. Return the findings for owner review. Creation and deployment still require
   separate explicit approval; workspace selection and team consent are not either.

A workspace consent grant may cover other workspace resources as well; it is not
a new project-specific credential. The owner must review its breadth. Task-level
authorization remains limited to Game Library preparation, not BarBook mutations.

## Plan support — conditional, not falsely confirmed

The current [official Hobby plan](https://vercel.com/docs/plans/hobby) lists **200
projects** and supports non-commercial personal static Angular/PWA hosting. A
second separate project does **not inherently require a paid upgrade**. This plan
requests no paid features, extra team members, Functions, custom domains or plan
change. An already-active eligible plan with available quota can support it.

However, explicit team billing/capacity reads are blocked. The authenticated
**user's** Hobby field does not establish the **workspace's** current plan, quota,
account restrictions or policy eligibility. Therefore **no-upgrade readiness for
this particular workspace is not yet verified**. Do not upgrade, start a trial,
add paid seats/features or change billing to make the check succeed. Hobby remains
restricted to personal/non-commercial use; commercial use needs separate review.

## Updated Game Library configuration and strict project separation

Current local non-executable plan:
[`deploy/qa/vercel-project-plan.json`](../../deploy/qa/vercel-project-plan.json).
It pins the explicit selected team ID/slug, while all access/plan/capacity/create
verification flags remain false and all execution permissions remain disabled.
The old `vercel-personal-project-plan.json` is marked **superseded / historical**.

| Setting | Proposed value |
| --- | --- |
| Project | New **`game-library-qa-web`**, project ID unknown until separately authorized creation |
| Owner | `team_06vOR8VzjmIVJfhtgodwg1wL` / `bar-book`, explicitly supplied on future operations |
| Root / framework / Node | `src/frontend` / Angular / `24.x` |
| Install / build / output | `npm ci` / `npm run build:qa` / `dist/game-library/browser` |
| Git connection / automatic deployments | None at creation / disabled |
| Source visibility | `publicSource: false` |
| Remote execution now | Creation, linking, deployment, billing and settings writes **disabled** |

No change to `src/frontend/vercel.json` is required: its existing commands/output,
SPA routing/security headers and disabled automatic Git deployment remain correct.
It is account-neutral; do not add unsupported account/scope fields or fabricate a
`.vercel/project.json` project ID. No link file was created.

Separation requirements for any later authorized operation:

- **Projects:** create a new project, never update/reuse `bar-book-web`. Verify the
  returned Game Library project name, new ID and exact `accountId` before further
  writes. Reject the known protected BarBook ID or an unexpected owner/project.
- **Environment:** only Game Library project-scoped `QA_API_BASE_URL`,
  `QA_SUPABASE_URL`, `QA_SUPABASE_PUBLISHABLE_KEY`, from the separate QA backend/Auth.
  Do not read/copy/attach/change BarBook or shared workspace variables. No server,
  database, Storage or provider secret enters the frontend build.
- **Domains:** initially only the new project's own generated Vercel hostname.
  No BarBook domain, alias, DNS, custom-domain attachment or transfer.
- **Deployments:** Game Library project ID and approved immutable Game Library
  source only; no BarBook redeploy, promotion, rollback, alias or source connection.
- **Settings:** Game Library project-level settings only. No workspace defaults,
  shared integrations, plan, billing, access-role, team setting or BarBook changes.
  Do not call `create_git_project` as a harmless link step: it can deploy by default.

These are distinct project resources in a shared workspace, **not independent
workspace tenancy**: workspace membership, billing/plan limits and build/usage
quotas remain shared. Future Game Library usage could consume that shared quota;
measure capacity before an approved deployment rather than promising no operational
impact on BarBook. No shared setting was changed to enforce isolation here.

## Local verification and publication boundary

Verify JSON parsing, explicit exact team scope, protected BarBook identifiers,
false execution/access-verification flags, no shared-variable/domain operations,
and agreement with existing frontend build settings. Check prior file hashes,
HEAD, raw index and staged diff against the task-entry baseline, plus whitespace
and relative links. Application code/dependencies/build inputs are not changed;
prior regression results are not relabeled as fresh runs.

Those local checks passed: only the four supersession/current-scope metadata files
changed among 420 task-entry files; the other **416** files, HEAD, raw index and
staged diff remained identical. Exactly the new current plan/report were added.
All 407 frozen source hashes and both durable checksum sets still verified.

The frozen **407-path** source manifest/archive and approved dependency hashes
remain unchanged. The earlier **418-path** publication proposal contains the old
personal-only companion versions; preserve it as historical evidence, not a current
publication tree. Future publication must include the new workspace plan/report
and re-manifest all reviewed companion versions before staging. No staging,
commit, push, project creation or deployment is authorized by this update.

**Outcome:** existing workspace identified and local configuration updated;
workspace authorization, current-plan capacity and create permission remain blocked
pending owner consent and the read-only rechecks above. BarBook is untouched.
