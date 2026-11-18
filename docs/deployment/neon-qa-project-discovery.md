# Owner-created Neon QA project — read-only discovery

2026-11-15. **Discovered and metadata-verified. Do not create another project.**
**NO-GO for migration/deployment: actual settings differ from the approved proposal.**

The owner reported manual project creation. This task used only read-only Neon
organization/project/branch/compute/database/role metadata tools and public docs.
No project creation/update, SQL, migration, credentials tool, Supabase/Railway/Vercel
operation or Git write was performed. Only non-secret metadata is recorded here.

## Exact discovered target

| Field | Verified metadata |
| --- | --- |
| Project name | **GameLibraryQa** |
| Project ID | **`super-fire-30188358`** |
| Organization | Alexander Rojas — `org-falling-fog-81506005` |
| Plan | Organization `free`; project owner subscription metadata `free_v3` |
| Creation source / time | `console` / `2026-11-15T16:38:45Z`; consistent with owner report |
| PostgreSQL major | **18**, from API metadata; running minor not queried |
| Region | **`aws-us-east-2` — Ohio** |
| Default branch | **`production`**, `br-muddy-base-be5m73fj`; default/primary true |
| Branch count / ancestry | One branch; no parent ID reported |
| Database | **`neondb`**, provider metadata ID `612895` |
| Database owner / listed role | **`neondb_owner`**; one listed role, one database |
| Compute endpoint ID | **`ep-wispy-cherry-beqkq4cc`** |
| Compute type / count | `read_write` / one; one backing compute reported |
| Direct host | `ep-wispy-cherry-beqkq4cc.c-9.us-east-2.aws.neon.tech` |
| Min/max compute | **0.25 / 2 CU**, autoscaling, not fixed 0.25 CU |
| Compute state at read | `active`, disabled=false; a point-in-time observation |
| Scale-to-zero raw setting | **`0`**, on both project defaults and endpoint |
| Scale-to-zero interpretation | Plan default: Free enforces **300 seconds / five minutes** |
| History retention | 21,600 seconds / six hours |
| Logical replication | Disabled |

Neon documents that `suspend_timeout_seconds=0` applies the plan default and Free
always scales to zero after five minutes:
<https://neon.com/docs/guides/scale-to-zero-guide>. This is a documented policy
interpretation, **not** evidence of a timed idle/suspend/resume test. The earlier
explicit-300 creation request failed with HTTP 412; no retry was performed by the
agent and this owner-created project was not created by that failed request.

Only the stable direct host is selected for a possible later driver probe. No
connection string, password, token or private key was retrieved, output or saved.
`neondb_owner` is the observed owner, not an approved least-privilege API runtime
role. No separate runtime role is known or created.

## Differences from the approved/historical proposal

| Setting | Proposed/approved | Actual | Consequence |
| --- | --- | --- | --- |
| Project name | `game-library-qa` | `GameLibraryQa` | Pin actual ID; do not rename or create a duplicate |
| PostgreSQL major | **17** | **18** | Approved architecture still selects 17; no silent acceptance of 18 |
| Region | Oregon `aws-us-west-2` | Ohio `aws-us-east-2` | Different locality from proposed Railway sfo pairing; no latency proof |
| Compute | Fixed 0.25 CU | **0.25–2 CU** | Higher ceiling; do not resize or accept the difference implicitly |
| Branch | `qa` | `production` | A default branch label inside the dedicated QA project, not application production |
| Database | `game_library_qa` | `neondb` | Use the actual name for any later approved probe |
| Owner role | `game_library_qa_owner` | `neondb_owner` | Record actual role; never substitute another project's credentials |
| Suspend representation | Explicit 300 seconds | 0 / Free plan default | Same documented five-minute policy, different raw representation |

Free plan, 0.25-CU minimum, one branch/database/owner/endpoint and six-hour history
match the intended bootstrap envelope. No setting was changed to force alignment.
The observed branch's name does not require making another branch. PostgreSQL 18
may be technically compatible with the earlier static migration review, but that
does not amend the owner's approved PostgreSQL 17 architecture or prove execution.

## Isolation evidence and limits

The refreshed organization inventory lists three projects. The discovered ID is
distinct from protected BarBook `nameless-cherry-10121327` and CRFuelScraper
`old-violet-31409208`. Its branch belongs to `super-fire-30188358`; the endpoint
belongs to that same project/branch; database and role metadata name that same
branch. No branch-specific call, credential access or mutation targeted either
protected application. This is a separate project namespace, not their QA branch.

No parent branch ID is reported for the single default branch. Database contents,
effective SQL privileges, empty application schema/EF history, TLS/database login,
and Neon Data API/Auth/other exposure were **not** inspected. Project isolation and
zero usage counters do not establish an empty database or safe API exposure.

## Local planning update

`deploy/qa/neon-project-plan.json` now records actual identifiers, observed settings,
all differences and the previous rejected request. Its historical proposed creation
input remains a comparison record, not an instruction. `applied=false` means the
original configuration was not applied in full; it does **not** mean the project is
absent. All current execution switches remain false; creation is explicitly denied.

The approved architecture and environment plan remain on PostgreSQL 17 until an
explicit decision resolves the mismatch. No application code, dependency, schema,
production cleanup behavior or frozen source is modified. The original candidate
inventory is retained; a new versioned companion inventory records this local delta.

## Next approval — read-only connectivity, not migration

First confirm the exact discovered target and acknowledge the PostgreSQL/region/
compute differences. The owner can authorize **inspection only** of this project
without yet accepting those differences for deployment. If instead the original
configuration is required, stop for a separately approved corrective plan; no
rename/resize/new project/version migration is authorized here.

A narrowly scoped follow-up approval should name:

- Project `super-fire-30188358`, branch `br-muddy-base-be5m73fj`, endpoint
  `ep-wispy-cherry-beqkq4cc`, database `neondb`, role `neondb_owner`, direct host above,
  port 5432; reject protected IDs, read replicas and a transaction pooler.
- A driver-only Npgsql probe using `SSL Mode=VerifyFull`, GSS encryption disabled,
  pooling disabled, bounded connection/statement timeouts, and no automatic replay.
  Do not start the API: its current hosted cleanup can mutate records.
- An owner-supplied private credential input outside Git/chat/CLI arguments, scoped
  exactly to this target (for example an owner-only 0600 password-file path). Agree
  on that secure handoff separately; do not retrieve credentials through MCP or
  search existing secrets/Keychain/development stores. No values installed in app,
  repository, Railway or Vercel. A public CA path is not a private credential.
- An explicitly read-only transaction and fixed metadata SELECTs only: actual
  server/minor version, database/current role/schema/search_path, primary status,
  TLS, public relation/history presence and necessary privilege metadata. Verify
  empty schema rather than assume it; do not read application/Auth user rows.
- Stop after a sanitized connectivity report. No CREATE ROLE, grants, advisory
  migration locks, DDL/DML, migrations, resource settings, deployment, Auth/billing
  change or Git publication. The SQL probe may wake idle compute/use Free quota.

No connectivity probe or credential handoff has been approved/executed by this
metadata-only task. Owner approval of inspection would not itself accept PostgreSQL
18/Ohio/2-CU maximum for deployment or authorize changing them.
