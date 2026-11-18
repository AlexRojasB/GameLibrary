# Personal Vercel QA — access check and proposed configuration

> **Superseded owner choice:** the owner now approves a separate Game Library
> project in the existing `bar-book` workspace. A separate personal workspace is
> no longer required. Use [current workspace readiness](vercel-workspace-qa-readiness.md)
> and `deploy/qa/vercel-project-plan.json`; the content below is historical only.

2026-11-06. **Personal account selected; creation/deployment NOT authorized.**
This owner decision supersedes the earlier reports' suggestion to authorize the
`bar-book` team for Game Library. Do not authorize, change, reuse or deploy into
BarBook. No remote write or consent flow was started during this check.

## Identity and actual access

| Check | Read-only observation |
| --- | --- |
| Authenticated personal username / URL namespace | **`proxtos-5361`**, <https://vercel.com/proxtos-5361> |
| Exact authenticated user ID | **`u9w3FMff1gwTyAWS7R2rEShX`** |
| User billing | **Hobby / active**; concurrent build allowance 1 |
| Account default team | `team_06vOR8VzjmIVJfhtgodwg1wL` (`bar-book`), explicitly forbidden for this project |
| Team listing and Git deployment discovery | Both return no visible teams; no next page supplied |
| Projects with scope omitted | Returns the unrelated `bar-book-web`, whose `accountId` is the forbidden team, not the personal user |
| Projects with `slug: proxtos-5361` | Also returns that team-owned project; not proof that the username selected personal ownership |
| Projects with explicit known personal user ID | `GET /v10/projects` **403: Not authorized** |
| CLI fallback | `vercel` not installed; no cached Vercel package found under the npm execution cache |

**Identity is verified; an actionable personal project-owner scope and this
connection's project-create permission are NOT verified.** A successful identity
read, presence of a create tool, or user-level Hobby field does not establish write
authorization. Do not issue a test POST, empty create, deployment or transfer to
probe it. The explicit-ID error has no known automatic remediation; do not retry
unchanged or claim that re-authentication alone is certain to fix it.

Modern personal Vercel resources can belong to a **Hobby team**. Vercel's current
account documentation says unscoped API/CLI calls use the account's default team.
The observed user ID is not a discovered personal Hobby **team ID**; never put it
in `orgId`/`teamId` for a write just because it identifies the logged-in user.
No personal Hobby team ID/slug was returned by the current connection. The
existence/name availability of `game-library-qa-web` in that scope is also unverified.

## Hobby suitability

The official [Hobby plan](https://vercel.com/docs/plans/hobby) supports non-commercial
personal projects, including static Angular/PWA hosting. It currently lists 200
projects and 100 deployments/day; these are plan limits, **not measured free
capacity in the undiscovered personal owner scope**. This proposal uses no Vercel
Functions, paid features, custom domains or plan upgrade. Backend/API remains on
Railway with a separate QA Supabase project.

The authenticated user's active Hobby record supports this plan choice. It does
not verify the actual project-owning Hobby team's billing, current usage/blocks,
remaining quota, or create permission. Those remain pre-create gates. Hobby permits
only personal/non-commercial use; any future commercial use needs a new plan review.

## Proposed local configuration — not applied remotely

The non-executable plan is
[`deploy/qa/vercel-personal-project-plan.json`](../../deploy/qa/vercel-personal-project-plan.json).
Its verified owner fields remain **null** and its create/deploy permissions false
rather than guessing a scope. It is not a Vercel auto-applied configuration file.

| Setting | Proposed value |
| --- | --- |
| New project name | `game-library-qa-web` |
| Owner | Verified personal Hobby owner scope only; unresolved ID/slug |
| Root directory | `src/frontend` |
| Framework / Node | Angular / `24.x` |
| Install / build | `npm ci` / `npm run build:qa` |
| Output | `dist/game-library/browser` |
| Repository connection at creation | None; no deployment-triggering Git import |
| Automatic Git deployments | Disabled, matching current `src/frontend/vercel.json` |
| Source visibility | Private source (`publicSource: false`) |
| Build inputs later | QA API HTTPS origin, separate QA Supabase HTTPS origin and publishable key only |

Existing `src/frontend/vercel.json` already defines the Angular commands/output,
SPA rewrite and noindex/security headers. It contains no account/team/project ID.
It needs no scope edit. Owner selection is separate from framework configuration;
adding an unsupported `scope` field to `vercel.json` would not establish ownership.
No `.vercel/project.json` link was created and no Vercel install/login was run.

## BarBook reference check

Checked root and frontend `vercel.json`/`.vercel/project.json`, local
`opencode.json` by field path only (no credential values), and the non-secret
`VERCEL_ORG_ID`, `VERCEL_TEAM_ID`, `VERCEL_SCOPE`, `VERCEL_PROJECT_ID` environment
selectors. There is no local Vercel project link, root Vercel config, matching
scope environment selector, or BarBook target in the checked executable configs.
The previously existing BarBook text matches are historical deployment reports,
not instructions to deploy there. They remain untouched. The **live account's
default team** is the routing hazard; it was not changed. External/global configs
were not exhaustively inspected, so this is not a claim about every machine config.

## Required next authorization steps

1. In the [Vercel dashboard](https://vercel.com/dashboard), signed in as
   `proxtos-5361`, select the **personal Hobby workspace**, not BarBook. Read its
   **Settings → General → Team ID**, slug, role/ownership, Hobby plan and available
   capacity. Share only those non-secret identifiers. Do not create/upgrade a
   workspace as a workaround without separate approval.
2. If that Hobby team exists but is not visible to this connection, grant this
   connection access **only to the verified personal team**, using the supported
   team-consent flow when applicable. No consent link can safely target an
   undiscovered team. Do not call `authorize_team_access` for BarBook or for the
   user ID masquerading as a team. No credential/token needs to be shared here.
3. Re-read that exact team's identity, membership/owner role, plan and project list
   on the same connection. Check available documented permissions/connection grants
   for project creation; read access alone is insufficient. A separately authorized
   CLI fallback would need its own login and the same explicit verified scope.
4. Keep this plan **blocked** if create permission cannot be established without a
   write. Record the limitation rather than pretending a read preflight proves a
   successful future create. Project creation and deployment each require separate
   owner authorization after scope/permission review and existing QA release gates.

**Readiness: configuration prepared locally; personal create/deploy access remains
blocked.** Nothing was created, linked, deployed, staged, committed or pushed;
BarBook resources, team grants and account-default settings were not modified.

Sources: [Account/default-team behavior](https://vercel.com/docs/accounts#default-team),
[finding a team ID](https://vercel.com/docs/accounts#find-your-team-id),
[Hobby plan](https://vercel.com/docs/plans/hobby).
