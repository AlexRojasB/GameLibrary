# Neon QA runtime role — plan only

2026-11-17. **No SQL, role creation, grants or credential installation performed.**
Target only GameLibraryQa / `super-fire-30188358`, branch `br-muddy-base-be5m73fj`,
database `neondb`. The branch name `production` does not authorize production use.

Propose one login `game_library_qa_runtime`, distinct from `neondb_owner`. Create it
only after a separate exact-target approval. Supply its generated password privately
through the approved operator/deployment secret UI, never SQL checked into Git,
chat, command arguments, frontend or image layers. Prefer SCRAM authentication.

## Minimum capability plan for current application paths

| Object | Proposed privileges / reason |
| --- | --- |
| Database neondb | CONNECT only; no CREATE or TEMPORARY |
| Schema public | USAGE only; no CREATE or ownership |
| libraries | SELECT, INSERT, UPDATE(id) for SELECT FOR UPDATE row locking used by current cover/Game saves; no library delete workflow |
| games, library_entries, platforms | SELECT, INSERT, UPDATE, DELETE for owned CRUD |
| game_genres, game_platforms | SELECT, INSERT, DELETE for associations; no arbitrary UPDATE |
| genres | SELECT only; deterministic seeds are migration-owned |
| play_log_entries | SELECT, INSERT, UPDATE; deletion follows library-entry FK cascade, no direct delete endpoint |
| cover_uploads | SELECT, UPDATE(id) for the existing lock/query path; no INSERT/DELETE or lifecycle-column writes in this no-managed-covers phase |
| steam_accounts | SELECT, DELETE: the existing authenticated unlink endpoint remains callable even when linking/import is disabled; no INSERT/UPDATE |
| steam_link_requests | No grants initially; linking/import is explicitly disabled; enable only under a separately reviewed privilege/provider plan |
| __EFMigrationsHistory | No privileges; application startup must not query/apply migrations |
| Sequences / routines | None required by this UUID-based schema; no blanket grants |

PostgreSQL row-locking SELECT requires UPDATE privilege on at least one selected
column, even if no rows match. Column-level UPDATE(id) is sufficient for permission
checking and does not authorize updates to other columns. **Validate this proposed
matrix with the exact EF-generated statements and a real isolated PostgreSQL test
before granting it remotely.** If an existing operation requires more privileges,
review the precise operation rather than granting ALL. Disabled cover endpoints
must not be treated as authorization to insert managed metadata.

Application authorization remains backend Library/user scoping, not per-user DB
roles or new application RLS. All requests share this server-only runtime login.
No role or grant exists yet; this matrix is not a passed runtime smoke test.

## Required role and effective-privilege checks at the separate provisioning gate

- LOGIN, NOSUPERUSER, NOCREATEDB, NOCREATEROLE, NOREPLICATION, NOBYPASSRLS;
  no memberships (especially not neondb_owner/neon_superuser/pg_database_owner).
  NOINHERIT alone is not enough: membership may still permit SET ROLE.
- Own no schema/table/index/database; migration owner retains all schema ownership.
- Do not use an API that silently gives newly created roles provider-admin membership.
  Inspect the resulting role/membership graph before accepting it.
- Explicit grants are limited to this database/schema/object list, without grant
  options, REFERENCES, TRIGGER, TRUNCATE, MAINTAIN or blanket ALL.
- Inspect PUBLIC/database/schema/default ACLs first. PostgreSQL privileges are
  additive: a REVOKE from the runtime role cannot cancel PUBLIC or inherited access.
  Database TEMP and schema CREATE may require a separately approved scoped PUBLIC
  revoke on this dedicated database. Do not silently change unrelated ACLs or
  assume NOINHERIT removes PUBLIC privileges.
- No broad automatic future-table grants. After future migrations, review the
  new objects and extend only the explicit runtime set. Do not grant runtime
  default ownership or migration privileges.
- Confirm SELECT/CRUD and FK cascades work for two users through the API, while
  DDL, migration-history reads/writes, seed mutations, role changes, SET ROLE owner,
  schema creation and temporary-table creation fail. Use rollback-safe negative
  probes on an isolated test database first; never attempt destructive production
  probes or real migrations as a privilege test.
- Set bounded connection/pool/timeouts per the application environment plan;
  optional role connection limit 5 must be reviewed with rollout overlap/draining.

Keep migration credentials operator-only. Railway receives only the runtime
connection, built with NpgsqlConnectionStringBuilder escaping and verified TLS.
Vercel receives neither credential. Do not reuse the owner passfile or previously
expired execution authorization for runtime provisioning.

## Immediate next approval

Approve the exact privilege matrix after isolated validation, then authorize
**one runtime role plus only its reviewed grants/effective-PUBLIC corrections**,
private password handoff, and bounded read-only runtime connectivity/privilege
verification. This local preparation does not authorize those operations.
