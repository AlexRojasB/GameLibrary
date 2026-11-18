# Neon QA migration and verification strategy

2026-11-15. **Preparation only. No migration, DDL or remote SQL executed.**

Read the [approved QA amendment](../architecture/private-qa-neon-amendment.md) and
[target plan](../../deploy/qa/neon-project-plan.json). The owner-created project
is now [discovered](neon-qa-project-discovery.md), but uses PostgreSQL 18/Ohio/
autoscaling instead of the approved PostgreSQL 17/Oregon/fixed compute. Actual IDs
are recorded for inspection, not accepted as a migration target. No runnable
target-specific apply command or approved SQL/connectivity probe exists.

## Source and compatibility

EF Core/Design and local CLI are 10.0.11; Npgsql EF is 10.0.3. Keep exactly the
existing 11 migrations through `20261029134813_AddCoverUploads`; no migration rewrite
or second Neon/Supabase migration workflow is proposed. Their 23 implementation,
Designer and model-snapshot files still match the frozen source.

The previously reviewed EF SQL is 24,737 bytes, SHA256
`90d01c192dd653205e06a3ef1faa9aa376ab4da1459a53d7d0df34352bf1be27`.
Its retained operator location is
`~/Documents/GameLibrary-QA-Artifacts/ef-10.0.11-2026-11-07/qa-migrations-ef-10.0.11.sql`.
It contains 11 ordered transactions/history IDs and 15 fixed Genre seeds.

| Migration suffix | Main effect |
| --- | --- |
| InitialCreate | Empty migration/history marker |
| AddPlatformManagement | Libraries, Platforms, stored lower(name), uniqueness/FK |
| AddVideoGameManagement | Games/entries/joins/catalog, checks and 15 genres |
| AddBoardGameManagement | Board metadata/player constraints |
| RelaxVideoGamePlayerCountConstraints | Optional video player pairs; CHECK replacement |
| AddPlayLogEntries | Owned activity data, FK/cascade/index |
| AddPlayLogDurationMinutes | Nullable positive duration |
| AddSteamIntegration | Accounts/link states/AppID, checks/partial uniqueness |
| AddSteamPlaytimeTracking | Nullable nonnegative bigint total |
| AddFamilyFriendly | Required boolean default false |
| AddCoverUploads | Lifecycle metadata, restrictive current cover FK and privacy guard |

No `auth.*` FK/function, Supabase UUID helper, extension, managed Storage schema or
superuser operation is required. The cover migration enumerates only **existing**
Supabase-named roles before revoking/testing privileges. Absent roles are skipped;
do not create them, strip the guard or replace the migration to force success.

Static review identifies no required Neon migration edit. Successful execution is
still unverified; actual PostgreSQL minor version, collation, role/default grants,
ownership and schema state must pass on the dedicated target.

## Future read-only preflight after exact target confirmation and approval

Use exact returned project/branch/compute/database/role values, never a display-name
match or another project's host. Reject both protected Neon projects, Supabase
database endpoints, read replicas and `-pooler` migration hosts. Credentials must
not appear in arguments/logs/reports. Do not retrieve/install them in this task.

1. Fresh authenticated metadata: expected org, project name/ID, PostgreSQL 17,
   branch/compute mapping and the direct read-write host on port 5432.
2. Confirm no Neon Auth/Data API/other browser data surface is enabled. If a service
   must be disabled, obtain separate configuration approval; do not alter it as a
   side effect of a read-only preflight.
3. Driver-only Npgsql VerifyFull probe with real hostname/CA validation, GSS disabled,
   pooling disabled and no automatic command replay. Never start the API to test
   connectivity: current hosted cleanup is not read-only.
4. In an explicitly read-only transaction, inspect only metadata: server version,
   current database/user/schema/search_path, not-in-recovery, TLS, public relations,
   EF-history presence, migration ownership/CREATE privileges and connection budget.
   Stop on any unexpected application relations/data, ownership or search path.
5. Pin source/migration/SQL hashes and EF tool version to the chosen immutable
   release. Historical empty-schema or source review is not fresh target evidence.
   Check final Railway-network TLS/runtime-role connectivity separately later.

## Proposed execution route — runner not implemented

Prefer applying the reviewed **EF-generated SQL** through a new, narrowly scoped
Neon operator guard. The existing `apply-reviewed-qa-sql.py` is pinned to Supabase
QA and must remain unchanged. No guard, bundle or schema-management dependency is
added here. Its implementation/tests require a separate local-code approval.

Required guard behavior:

- Execution default OFF; fresh owner attestation for exact project/branch/direct
  host/database/role, source manifest and SQL hash. Null target fields fail closed.
- Clean environment/private inputs, no development user-secrets/connection fallback,
  no password echo, no token extraction, direct primary only, verified TLS. The
  existing design-time factory reads `ConnectionStrings__Default`, not
  `QA_MIGRATION_CONNECTION`; map only inside an isolated approved operator process.
- Single physical session for read-only preflight, serialization and the whole
  script; no reconnect, replay or alternative host. Preflight read-only transaction
  ends before mutation. No advisory lock is acquired in a read-only-only task.
- After migration execution is separately approved, acquire a reviewed QA-specific
  **session** advisory lock nonblockingly before EF history initialization and hold
  it through every migration transaction. Derive/pin its key from the exact target;
  no fabricated target/key today. All authorized migration entry points must use
  the same coordinator. An advisory lock cannot stop unrelated unguarded admin SQL.
- Preserve the generated per-migration transactions; do not wrap the entire script
  in another transaction or edit its guards. Stop on first error; bounded connection,
  lock and statement timeouts; release coordinator/connection in finally.
- Native EF's history-table migration lock is distinct from the generated SQL route;
  do not claim the SQL script automatically includes native EF locking. Choose one
  approved execution route, not concurrent CLI/script entry points.
- On timeout, cancellation, disconnect or unknown COMMIT, inspect primary/history
  with a fresh read-only connection before any new approval/retry. Earlier migration
  transactions may already be committed. No blind retry, Down, reset or restore.

No execute command is supplied with guessed host/IDs. Schema application remains
EF-owned even when an operator executes its generated SQL.

## Post-migration evidence before API startup

- All 11 exact history IDs, matching approved tool/source; 11 application tables
  plus `__EFMigrationsHistory`; 15 deterministic genres.
- Relevant FKs, CHECKs, partial/unique indexes, generated Platform normalization,
  reference restrictions and cover privacy guard effective.
- Zero cover lifecycle rows/non-null Game managed-cover references for any future
  cleanup opt-out; no copied business data or Auth/Storage-managed schema changes.
- A separately approved least-privilege runtime login has only required application
  access, not schema ownership/DDL; no browser access and no migrations at startup.
  Runtime role/grant provisioning is **not** included in creation-only approval.
- Direct runtime primary, correct database, VerifyFull and maximum five pooled
  connections; operator owner credentials never installed in Railway/Vercel.
- Live QA Auth ES256 login/JWT flow, two-user authorization/CRUD and hosted resource
  checks require later tester/deployment approval; metadata is not those results.

## Independent approvals

Project creation, driver/private-input setup, guard implementation, role/grants,
exact migration execution, Auth settings/tester changes, Git publication and hosted
deployment are distinct. The current architecture approval grants none of those.
