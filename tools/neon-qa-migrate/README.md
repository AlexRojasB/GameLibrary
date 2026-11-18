# Neon QA migration operator — local preparation only

**Execution is OFF. No database connection occurs in default mode or offline tests.**
The owner's 2026-11-15 follow-up permits the narrow preflight cast fix and isolated
local PostgreSQL regression/integration tests. It does not permit Neon connections,
remote locks/migrations, deployment or resource changes.

This additive operator tool implements the reviewed route in the
[migration strategy](../../docs/deployment/neon-qa-migration-strategy.md).
That strategy, the [discovery record](../../docs/deployment/neon-qa-project-discovery.md),
[target plan](../../deploy/qa/neon-project-plan.json) and prior inventories are
**historical evidence**, not current approval. This README and the new
[runner inventory](../../docs/deployment/neon-qa-source-manifest-runner-preflight-fix.json)
describe this local implementation. No frozen application/specification file,
old Supabase operator, migration or historical manifest is rewritten.
The [previous runner inventory](../../docs/deployment/neon-qa-source-manifest-runner.json)
is retained unchanged at SHA256
`9ba4d27d2dcd133f231f71e73e5169a6a1e7c94cff7a98382e6d62e0e67f7a89`.

## Scope and pinned target

Only GameLibraryQa: project `super-fire-30188358`, organization
`org-falling-fog-81506005`, branch `production` / `br-muddy-base-be5m73fj`, compute
`ep-wispy-cherry-beqkq4cc`, direct host
`ep-wispy-cherry-beqkq4cc.c-9.us-east-2.aws.neon.tech:5432`, database `neondb`,
operator `neondb_owner`, metadata database ID `612895`, PostgreSQL **18.7 / 180007**,
Ohio `aws-us-east-2`. Exact control-plane fields also pin read_write, 0.25–2 CU,
raw suspend 0, organization `free` and subscription metadata `free_v3`.

Any different value (including a minor upgrade), null field, extra target field,
pooler/replica, Supabase host, BarBook/CRFuelScraper or other target is rejected.
No configurable database/host/role/connection string or alternative provider exists.
Pin changes require code review and new approval, not a flag that bypasses checks.
The earlier PG17/Oregon/fixed-compute architecture proposal remains unchanged:
acceptance of the actual configuration is an **unresolved execution gate**, not
a decision made by this tool. It changes no resources or billing settings.

EF remains the sole schema authority. The tool only dispatches unchanged
substrings from the existing reviewed **24,737-byte** EF 10.0.11 SQL artifact:

```text
SHA256 SQL:    90d01c192dd653205e06a3ef1faa9aa376ab4da1459a53d7d0df34352bf1be27
SHA256 frozen: 73899e7fd9732884421edefe0dbb0c65edfaa017655b3868d7286d084437e1fb
```

All 407 frozen inputs, modes, the exact 23 migration/Designer/snapshot files and
all 11 ordered history IDs through `20261029134813_AddCoverUploads` must match.
The new inventory additionally hashes operator code, templates and all selected
planning companions; its digest must appear in future approval. Neither the SQL
nor any credential is copied into this repository. Historical source inventories
remain intact; `inventory.py --test` replaces only the obsolete historical
"no new current paths" assertion with an exact current-inventory assertion.

## Local prerequisites and safe commands

Unix operator host, Python 3.10+, .NET 10 SDK/shared ASP.NET framework, cached
**Npgsql 10.0.3 net10.0** DLL. The DLL hash is pinned in `run.py`; the assembly version
is checked too. This reuses the project's driver version, with no new NuGet
dependency/download, `psql`, EF CLI execution, API reference/startup or solution change.
`NuGet.Config` clears package sources. Only ignored operator `bin/obj` build output
and temporary synthetic test fixtures are produced. No full API/frontend build
or application integration suite is implied by building this console tool.

From the repository root, these are the only commands authorized by this task:

```sh
python3 tools/neon-qa-migrate/run.py \
  --sql /Users/mac/Documents/GameLibrary-QA-Artifacts/ef-10.0.11-2026-11-07/qa-migrations-ef-10.0.11.sql

python3 tools/neon-qa-migrate/run.py --self-test \
  --sql /Users/mac/Documents/GameLibrary-QA-Artifacts/ef-10.0.11-2026-11-07/qa-migrations-ef-10.0.11.sql

python3 tools/neon-qa-migrate/inventory.py --test

python3 tools/neon-qa-migrate/postgres-regression/run.py \
  --pg-bin /path/to/isolated/postgresql-18.7/bin \
  --test-parent /path/to/external/private/test-parent
```

The last command is **local-only** and requires existing native PostgreSQL 18.7
binaries; it downloads/installs nothing. It creates a fresh owned 0700 test directory,
uses private Unix sockets with TCP disabled, and always attempts shutdown. Synthetic
roles/default ACLs reproduce the non-superuser operator's query context without
credentials. It executes the actual linked `SqlSession.StateSql` in an explicit
read-only transaction, with two nonempty default-ACL records, then proves the old
uncast expression fails with SQLSTATE 42725 as a negative control. Test code is
excluded from the production runner. The test executable has no remote-host input
and never calls production `SqlSession.Open`. Retained evidence includes build,
query and cleanup logs in the disposable directory printed by the command.

Default/offline modes reject private-input flags before touching those paths.
`--execute` without every explicit input fails before build/connection. No environment
variable, application configuration, `.pgpass` fallback, user-secrets or runtime
credential can silently enable execution. Use `run.py`, not a bare DLL/EF command.
The Python entry point checks file ownership and isolates the child environment;
the C# tool independently checks artifacts, modes and authorization before connection.
No shell, inherited PG*/connection/proxy/startup-hook variables or retries are used.

## Future authorization inputs — NOT approved or created by this task

The checked-in example files deliberately contain **false/null gates**. Do not
turn them into permission by merely copying/editing them. They are an operator
record of a **separate actual owner approval**, not a signature/authorization service.
Anyone able to change operator code or forge attestations is outside this tool's
trust boundary. File hashes detect drift; they do not prove who approved them.

After separate permission for a fresh read-only preflight, an authorized operator
must prepare these files **outside Git/chat**, in an owned **0700** directory, each
an owned regular **0600** file, no symlink path components:

1. `metadata.json`: normalize fresh authenticated Neon control-plane evidence into
   `metadata.example.json`'s exact shape. Reconfirm org/project/branch/database/role/
   compute/host/plan/settings; verify Neon Auth, Data API and any other browser-data
   surface are absent/disabled. Do not infer this from absent SQL roles. Set
   `verified` only after actual inspection, with UTC `observedAt` no older than
   **five minutes**. Unknown exposure is rejection, not false. No tokens/passwords
   belong in this file. This runner does **not** fetch Neon API credentials or
   independently authenticate the attestation: PostgreSQL cannot prove cloud org,
   branch or billing identity. The approved operator/control-plane evidence is an
   explicit trust dependency; SQL identity and verified exact-host TLS are additional
   independent checks. Refresh evidence/approval if it expires; never waive the TTL.
2. Authorization JSON: exact shape of `authorization.example.json`, with real owner
   approval, nonempty UUID, UTC `issuedAt`/`expiresAt` (maximum **30 minutes**), exact
   target, SQL/frozen/new-inventory/metadata **file-byte SHA256** values. For preflight
   only, action is `preflight`, approved is true, execution-specific gates remain
   false and `expectedPreflightSha256` remains null. A preflight approval cannot
   authorize `execute` and takes no advisory lock.
3. Operator passfile: one libpq entry selecting precisely the pinned direct host,
   5432, neondb and neondb_owner; no wildcard or second active entry. Password stays
   only in that file, consumed privately by the driver. No connection string,
   password or token in CLI arguments, logs, source, environment or reports. The
   launcher checks its metadata without reading its contents. The driver checks
   the exact entry only after public artifact and authorization gates pass.
4. An explicit **public** CA bundle path. No custom permissive callback or downgrade.

The future read-only command, **also not run/authorized now**, is:

```sh
python3 tools/neon-qa-migrate/run.py --preflight \
  --sql /Users/mac/Documents/GameLibrary-QA-Artifacts/ef-10.0.11-2026-11-07/qa-migrations-ef-10.0.11.sql \
  --authorization-file /Users/mac/.config/game-library-qa/neon-migrations/preflight-authorization.json \
  --metadata-file /Users/mac/.config/game-library-qa/neon-migrations/metadata.json \
  --passfile /Users/mac/.config/game-library-qa/neon-migrations/operator.pgpass \
  --ca-file /etc/ssl/cert.pem
```

Review and retain its structured `preflightSha256` and underlying catalog evidence
through separately authorized inspection. That digest binds identity, version,
empty catalog, effective permissions, default ACLs, current direct role memberships
and backend SSL observation, excluding volatile timestamps/session IDs.

For **execute** authorization, owner must separately approve this exact release,
inventory/SQL, actual PG18/Ohio/compute configuration, successful local PostgreSQL
gates, exposure policy, transport boundary and exclusive maintenance. All six
execution gates must be true, and `expectedPreflightSha256` must be the approved
preflight result. Do not reuse the preflight authorization or old metadata hashes.
Keep a durable external approval/attempt log; no execution approval is bundled here.

### Exact future execution command — DO NOT RUN NOW

Only after the above evidence and separate execution approval exist:

```sh
python3 tools/neon-qa-migrate/run.py --execute \
  --sql /Users/mac/Documents/GameLibrary-QA-Artifacts/ef-10.0.11-2026-11-07/qa-migrations-ef-10.0.11.sql \
  --authorization-file /Users/mac/.config/game-library-qa/neon-migrations/execute-authorization.json \
  --metadata-file /Users/mac/.config/game-library-qa/neon-migrations/metadata.json \
  --passfile /Users/mac/.config/game-library-qa/neon-migrations/operator.pgpass \
  --ca-file /etc/ssl/cert.pem
```

These are future input locations, not assertions that such files exist. This task
does not inspect, retrieve, copy or provision any private credential. `neondb_owner`
is an operator role, **not** the API runtime role. Never install its passfile/value
in Railway/Vercel/API configuration. Runtime login/grants/connectivity need separate
approval; no role/grant provisioning is performed by the runner.

## Execution protocol and rejection behavior

1. Verify all reviewed artifacts offline, before any connection. Revalidate them,
   authorization, evidence hashes and freshness after locking and before history
   initialization/every migration transaction. File-byte SHA256 values are not
   hashes of prettified/normalized JSON. No altered artifacts can be used from disk
   in place of the verified SQL string in memory.
2. One Npgsql physical connection, fixed host, VerifyFull/CA, GSS disabled, pooling/
   multiplexing disabled, no automatic replay/reconnect/failover. Connection budget
   15s, statement/command 30s, SQL lock wait 2s, idle-in-transaction 30s, overall 10min,
   bounded cleanup 5s. No certificate revocation-service result is claimed.
3. Startup `default_transaction_read_only=on`, `search_path=public`; explicit
   `REPEATABLE READ READ ONLY` transaction, then ROLLBACK. Check primary, exact
   version/db/session and current role, owner/schema ownership, CREATE/USAGE/login
   and owner role flags. Reject any user schema other than public, **any user
   relation** (including an empty history table), routines/types, inspected ancillary
   objects/global event/FDW/publication/subscription/large-object metadata, extra
   extensions or Supabase-named roles. Default ACLs must match the earlier two
   provider cloud_admin/neon_superuser records; drift requires investigation, never
   automatic revocation/grants. Catalog queries read no password catalogs/server files.
4. Execute mode also requires the approved fresh preflight fingerprint. Acquire
   **pg_try_advisory_lock(bigint)** nonblockingly, with a deterministic key from the
   first signed big-endian eight bytes of SHA256 of
   `GameLibraryQa/super-fire-30188358/br-muddy-base-be5m73fj/neondb/ef-migrations`.
   Offline mode prints the actual key. Hold this **session** lock from before
   history initialization to after all 11 COMMITs/postchecks. Recheck in a **new**
   read-only transaction/snapshot after locking; reject all schema/fingerprint drift.
   A second cooperating runner exits, never waits/retries. No locks in preflight.
5. Only then permit session writes. The full artifact hash includes EF's UTF-8 BOM;
   the BOM is decoded as an encoding signature, not sent as a SQL token. Dispatch
   the unchanged standalone history CREATE,
   followed by each original START/DO/COMMIT command. No enclosing transaction,
   SQL rewrites, EF-native history lock claim, table-based lock, splitting on inner
   semicolons or suppressing the cover privacy guard. The hash-specific lexical
   splitter respects quoted strings/identifiers and outer dollar bodies (including
   nested `$privacy$` inside `$EF$`); unsupported/new SQL needs review, not fallback.
   One command per round-trip avoids ambiguous multi-command implicit/batch
   transaction boundaries. Stop on the first failure.
6. Minimal read-only postchecks: exact 12 table names/ownership/object kinds, all 11
   ordered history IDs/product versions, exact 15 Genre ID/name pairs, no rows in
   any other application table, unchanged identity/security metadata and no PUBLIC
   cover_uploads privileges. These are **not** complete constraint/index/behavior,
   runtime-role or two-user integration verification. API startup remains forbidden.
7. Finally attempt ROLLBACK and unlock only this session's key, dispose the connection
   even if cleanup fails. A lost connection releases its lock when the server actually
   terminates that session, not necessarily when the client detects a timeout.

Advisory locks coordinate only cooperating operators. They cannot prevent unguarded
DDL/admins, API activity or a client bypassing this tool. Exclusive maintenance,
one approved entry point and no concurrent EF CLI/API/SQL console are mandatory.
This tool assumes no concurrent source edits or privileged local tampering during
the attempt. It is not a sandbox or a global exactly-once authorization service.

## Failure recovery and rollback limits

**Never automatically retry, run Down, delete history, reset/drop schema, restore,
or switch host/credentials.** There is deliberately no resume/force/reset option.

The history-table initialization is its own autocommit. Each EF migration is a
separate transaction; earlier successful migrations survive a later failure.
PostgreSQL transactional DDL can roll back the active transaction, **not** earlier
commits. A cleanup ROLLBACK success does not undo a COMMIT already accepted.
Even first history CREATE can have an unknown committed outcome if its reply is lost.

Structured failures report safe phase, acknowledged commit count and
`reconciliationRequired`. After any migration command is dispatched, failures are
conservatively **possibly partially committed**, regardless of exception, timeout,
cancellation, wrapper termination or failed postcheck. Never equate acknowledged
count with actual committed count. No raw provider errors/SQL/private inputs are
printed. If the process disappears without a result, treat the outcome as unknown.

Recovery procedure (separate read-only inspection approval required):

1. Freeze all other migration/API activity. Retain approval ID/hashes, sanitized
   output, phases/times and transport/attempt evidence outside Git. Do not paste
   passwords or raw connection/exception dumps. Confirm the original session has
   ended and coordinator lock is no longer held before deciding on another attempt.
2. Reconfirm exact Neon control-plane identity and connect to the exact primary
   using a fresh verified read-only driver session. Inspect catalog, history IDs/
   product versions, schema owners, constraints/indexes/generated columns and
   expected seeds. Inspection is a **new operation**, never hidden auto-reconnect.
3. If no objects/history exist: establish that no session can still commit; obtain
   **new** approval/evidence before a deliberate new full attempt. A failed command
   alone does not establish an empty target.
4. If only history exists (even zero rows), a prefix of migrations exists, history
   disagrees with schema, or any foreign object/data appears: runner refuses.
   Record exact state; escalate for an explicit reviewed repair/recovery plan and
   new approval. Do not edit EF files/SQL/hashes to force this runner to resume.
5. If all 11 committed but response/postcheck failed: verify full schema/seeds/
   security separately and record reconciliation. Do **not** rerun this bootstrap
   runner; it intentionally rejects all existing history. No "success" until the
   relevant postconditions are verified.
6. If the primary/evidence/session outcome remains unavailable: leave status UNKNOWN
   and stop. A provider snapshot/PITR/branch/restore/reset is a separately approved
   potentially destructive operation; this task neither provisions a backup nor
   guarantees restoration/retention. The raw suspend/retention plan is not recovery
   proof. Recovery must preserve any unexpected data pending owner decision.

## Verification status and remaining gates

Local build/offline tests cover successful preflight/protocol, rejected identity,
configuration/privilege/catalog drift, changed SQL/source/manifests, added migration,
duplicate execution, modeled competing sessions, post-lock revalidation and lost
history/COMMIT acknowledgements. Fixtures are in memory or UUID-owned temporary
copies only, never mutations of the frozen worktree. **No remote SQL/cloud calls,
credentials or API startup are needed or authorized for these tests.**

### PostgreSQL 18.7 preflight regression

Real local testing found that `pg_default_acl.defaclobjtype` (PostgreSQL internal
`"char"`) made the original `text || "char"` expression ambiguous (42725). The
approved fix is only `defaclobjtype::text` in the actual StateSql concatenation;
EF application SQL and frozen source are unchanged. The checked-in regression
executes this query, rather than validating a fake snapshot or only matching text.

The native PostgreSQL 18.7 environment also permits repeating all twelve real
integration scenarios from the external readiness harness: session-lock persistence
and release, transactional rollback, non-superuser preflight, all 11 reviewed EF
migrations/postchecks, nonblocking concurrent rejection, fresh post-lock snapshots,
terminated-backend rollback, deterministic and actual wire-level lost-COMMIT replies,
history-autocommit loss, repeated execution and partial history. The integration
harness links the actual revised Engine/Policy/SqlSession files, not the earlier
diagnostic candidate. The real wire test withholds the third server COMMIT reply;
fresh primary history confirms three commits while the Engine acknowledges two.
These disposable tests are not permission to run that SQL on Neon.

Verification for this revision: **82 offline runner checks, 25 inventory/historical
checks, two real-query regression checks and twelve real PostgreSQL integration
scenarios passed**. The new inventory records those local results; its
`livePostgresRunnerGatesPassed=true` is test evidence only, not execution approval.
The execution authorization template (including its separate local-test attestation)
remains false/null. Both disposable clusters were stopped with no TCP listener or
system service. The original runner inventory remains unchanged as historical
evidence; use the new preflight-fix inventory digest for a future approval.

**The next gate is a separately authorized read-only Neon preflight, not migration
execution.** The following remain explicit later approval/evidence requirements:

- Docker is unnecessary for these local gates: existing official PostgreSQL 18.7
  binaries can run in a disposable native environment. Neither local regression nor
  integration results certify current Neon settings, grants, hosted networking or
  provider-internal TLS. Keep authorization templates false/null; test evidence is
  not an owner execution attestation.
- Resolve owner acceptance/architecture documentation for actual PG18/Ohio/
  autoscaling versus the prior PG17/Oregon/fixed compute proposal. No silent approval.
- Freeze/review the new inventory and immutable release, confirm fresh control-plane
  identity and actual browser-data exposure policy, obtain fresh read-only preflight
  permission and approve its fingerprint. Earlier inspection is point-in-time only.
- Client VerifyFull succeeded in earlier inspection, but `pg_stat_ssl.ssl=false` was
  observed on the backend. Provider-internal hop protection was not established.
  This does not invalidate client hostname/certificate verification. Internal-hop
  uncertainty may be an explicitly accepted QA operational risk; it is a policy
  blocker if the owner requires independently verified encryption on every hop.
  Resolve that decision explicitly; do not downgrade client TLS or claim end-to-end
  TLS. No new proxy/VPN/infrastructure is required merely to record the decision.
- Obtain exact separate execution approval and maintenance/recovery coordination.
  No runtime credentials, runtime-role grants, deployment, cloud changes or Git
  staging/commit/push are authorized. Those remain separate later operations.
