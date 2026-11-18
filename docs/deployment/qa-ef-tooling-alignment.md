# QA EF CLI alignment and migration preparation

2026-11-07. **Tooling/SQL preparation complete; NO-GO for hosted migration
execution.** The owner approved EF CLI alignment and offline SQL generation,
not database mutation. No database was contacted or migrated in this task.

QA: **`yqngxhryzklrhhryzzoi`**. Protected development:
**`iramzxpjbnldhebykzhx`**, never contacted or used as a fallback.

## Minimal tooling change

Added [`.config/dotnet-tools.json`](../../.config/dotnet-tools.json), pinning only
`dotnet-ef` **10.0.11**, with tool runtime roll-forward disabled. Restored it and
verified:

```sh
dotnet tool restore --tool-manifest .config/dotnet-tools.json
dotnet tool run dotnet-ef -- --version
```

Result: **10.0.11**, matching EF Core/Design **10.0.11** already in the projects.
Use explicit local `dotnet tool run dotnet-ef -- ...` in the QA workflow. The
unrelated global tool remains **10.0.7**; it was deliberately not updated, avoiding
changes to other projects. No application package, lockfile, migration, model,
factory, runtime configuration, global tool or MCP configuration was edited.

This report supersedes only the outstanding EF-tool-alignment/SQL-regeneration
items in [the earlier pre-migration report](qa-pre-migration-verification.md).
Its remote identity/security/connectivity blockers remain unresolved.

## Fresh offline build, generation and review

Copied the verified **407 frozen source paths** into an isolated temporary
directory. Used a clean allowlisted environment with no QA/development credentials
and an explicit unusable loopback design-time connection, preventing inherited
database settings from selecting a hosted database. No API host was started.
Reused the existing SHA-verified macOS native build asset without changing it;
this is not a new Linux/native-upload release-gate claim.

Results:

- Local tool restore and locked NuGet restore passed.
- Fresh isolated Release build passed: **zero warnings/errors**.
- `migrations has-pending-model-changes` passed: **no model changes**.
- Fresh idempotent SQL generated from `0` through
  `20261029134813_AddCoverUploads` using **EF CLI 10.0.11**.
- All **11 migrations**, their 11 Designers and model snapshot (**23 files**)
  match the reviewed frozen source hashes. The full 407 source hashes still match.
- Fresh SQL is **byte-for-byte identical** to the previously reviewed SQL:
  **`90d01c192dd653205e06a3ef1faa9aa376ab4da1459a53d7d0df34352bf1be27`** (SHA256).

Generation command, run only in the isolated, freshly built source copy:

```sh
dotnet tool run dotnet-ef -- migrations script \
  0 20261029134813_AddCoverUploads \
  --idempotent --no-build --configuration Release \
  --context GameLibraryDbContext \
  --project src/backend/GameLibrary.Core/GameLibrary.Core.csproj \
  --startup-project src/backend/GameLibrary.Api/GameLibrary.Api.csproj \
  --output /absolute/operator-artifact-path/qa-migrations-ef-10.0.11.sql
```

The existing design-time factory was used; no startup migration, cleanup service,
direct DB call, EF update, migration bundle execution or Supabase migration ran.

### Script review outcome

The ordered 11 EF history IDs match the source; there are **11 per-migration
transaction blocks** and **15 deterministic Genre seeds**. `InitialCreate` remains
an intentionally empty history marker. Tables and constraints cover Platforms,
Games/LibraryEntries, player ranges, Play Log, Steam metadata, Family Friendly and
the approved cover lifecycle. Full per-migration review is in the earlier report.

No unexpected operation, forward table/column drop, truncation, delete of existing
rows, role creation/alteration, extension installation, external call, Auth-user
creation or Supabase-managed `auth.*`, `storage.*`, `realtime.*` or
`supabase_migrations.*` change was found. Two deliberate non-additive operations
remain, unchanged from approved source:

1. Replace `ck_games_board_columns_video_null` to allow optional VideoGame player
   ranges while retaining the both-or-neither/range checks.
2. Revoke PUBLIC/known Data API role privileges on **application-owned**
   `cover_uploads`, rejecting inherited checked privileges. Never remove this guard
   to force a migration through.

`Down` paths can destroy data and are not an approved recovery procedure. The
script creates EF history before its per-migration transactions; a failed later
migration can leave earlier ones committed. Idempotency is not drift repair or a
license to retry after an uncertain commit. Never reset, run Down or restore
without a separately reviewed recovery plan.

## Durable local artifacts

Owner-only directory, separate from the unchanged historical snapshot artifacts:

```text
~/Documents/GameLibrary-QA-Artifacts/ef-10.0.11-2026-11-07/
  qa-migrations-ef-10.0.11.sql
  review.json
  apply-reviewed-qa-sql.py
  test-guard.py
  SHA256SUMS
```

`review.json` records all migration file hashes/IDs, CLI/runtime versions and SQL
hash. The script and offline tests are operator artifacts, not another application
project, dependency or database migration authority. SQL remains exclusively
generated by EF; no second Supabase/manual application-schema workflow is added.
These files were not staged, published or sent to any provider. Keep the manifest
and command together; changed source or SQL requires a new review, not editing the
guard's pinned hashes to bypass a failure.

## Safe explicit migration command — PREPARED, NOT EXECUTED

The default operator command is **offline** and has been run safely:

```sh
# Run from the GameLibrary repository root.
artifact="$HOME/Documents/GameLibrary-QA-Artifacts/ef-10.0.11-2026-11-07"
python3 "$artifact/apply-reviewed-qa-sql.py" --source-root "$PWD"
```

The future command below would execute the reviewed EF-generated SQL. **Do not run
it under the current tooling-only approval.** Only after fresh identity/settings
evidence, connection prerequisites and explicit owner migration-execution approval:

```sh
python3 "$artifact/apply-reviewed-qa-sql.py" \
  --source-root "$PWD" \
  --apply-project yqngxhryzklrhhryzzoi \
  --evidence "$QA_MIGRATION_EVIDENCE_FILE"
```

Required private inputs must be supplied by the operator's secure secret mechanism,
not chat, command arguments, a checked-in `.env`, shell-history assignments or logs:

- **`QA_DATABASE_PASSWORD`**: the existing direct-QA `postgres` password.
- **`QA_ROOT_CERTIFICATE`**: absolute path to the verified public PEM root CA file.
- **`QA_MIGRATION_EVIDENCE_FILE`**: absolute path to a recent non-secret operator
  attestation. Do not fabricate evidence or mark this task as execution approval.

The evidence must contain the following fields. This example deliberately has no
usable timestamp or execution approval and **will fail closed**:

```json
{
  "qaReference": "yqngxhryzklrhhryzzoi",
  "authenticatedQaProjectUrl": "https://yqngxhryzklrhhryzzoi.supabase.co",
  "approvedSqlSha256": "90d01c192dd653205e06a3ef1faa9aa376ab4da1459a53d7d0df34352bf1be27",
  "sourceManifestSha256": "73899e7fd9732884421edefe0dbb0c65edfaa017655b3868d7286d084437e1fb",
  "publicSignupDisabled": true,
  "dataApiDisabled": true,
  "authEnabled": true,
  "emailConfirmationRequired": true,
  "ownerApprovedThisSqlExecution": false,
  "verifiedAtUtc": "REPLACE_ONLY_AFTER_ACTUAL_VERIFICATION"
}
```

Renew the authenticated `supabase_qa.get_project_url` exact match and saved dashboard
settings before recording that attestation. It is a manual evidence/approval gate,
**not an automated Management API check**. The command rejects missing/non-boolean
confirmation, wrong target or hashes, future timestamps and evidence older than
one hour. No real affirmative execution-evidence file was created here.

### Fail-closed behavior

- Checks the pinned SQL hash, source-manifest hash, every frozen source path and
  exact migration file set **before DNS/network activity**. Explicit project
  arguments other than QA, including development, are rejected before artifact work.
- Has no configurable host/port/user/database, connection-string, pooler, replica,
  development or localhost fallback: only
  **`db.yqngxhryzklrhhryzzoi.supabase.co:5432`**, database **`postgres`**, direct role
  **`postgres`**. A different dashboard role needs a separately reviewed command.
- Resolves **IPv6 only**, pins one returned address for the session and retains the
  QA hostname for TLS hostname verification. It does not treat database name
  `postgres` alone as project identity; the pinned endpoint, verified TLS hostname,
  fresh authenticated QA evidence and successful login establish the boundary.
- Builds a clean libpq environment, rejecting inherited `PGHOST`, `PGSERVICE`,
  DB URLs and EF fallback settings. Uses **`sslmode=verify-full`**, explicit public
  CA, GSS encryption disabled, no password prompt and no psql startup files. No
  password is put in argv, SQL input or emitted output.
- In **one psql connection**, starts a read-only preflight, verifies TLS,
  database/role, effective `public` schema/search path, primary/not-recovery,
  read-write default, public USAGE/CREATE and **no existing public tables, views,
  sequences or foreign tables**, including EF history. Any failure stops before
  EF-generated DDL; this initial-migration command intentionally refuses partial
  or already-migrated targets rather than silently proceeding.
- Ends the read-only transaction and applies the pinned EF SQL **on that same
  connection**, without reconnecting. Reads back all 11 exact EF history IDs
  afterward. Do not run other schema changes concurrently; this is not a
  distributed deployment lock or a complete grant/trigger audit.
- Uses `ON_ERROR_STOP`, 15-second connection timeout, 5-second lock timeout,
  60-second statement timeout and a 10-minute overall client budget. On failure,
  timeout or lost acknowledgement it emits a generic error, suppresses provider
  output and never retries. Reconcile primary/schema/history before further action;
  a failed return does **not** prove all prior migrations rolled back.

Using psql here applies the **reviewed EF SQL**, not handwritten schema. It avoids
starting the API/cleanup service or checking one connection then migrating through
another. Do not replace it with an unguarded `dotnet ef database update` command.

## Exact TLS VerifyFull and IPv6 prerequisites

1. Confirm QA **Connect → Direct connection**, host/reference, port 5432, database
   postgres and direct postgres role; verify available org/region/plan/status
   metadata. Do not use session/transaction-pooler details for operator migrations.
2. The operator machine needs an IPv6 address, working IPv6 routing/default route,
   resolvable QA AAAA record and outbound TCP **5432** permitted through its
   firewall/VPN and any Supabase network restrictions. AAAA resolution alone proves
   none of TCP, TLS or login. IPv4-only operator networks must stop; do not silently
   buy IPv4, alter billing or substitute a pooler.
3. A supported **psql/libpq 17 or newer** must be installed and trusted on the
   operator machine. It is **currently absent**; none was installed under EF-tooling
   approval. Client setup needs its own applicable approval. The guard's
   conditional/error handling and TLS options require a compatible client.
4. Obtain the current public root CA from the verified QA database settings through
   a trusted channel. Supply it as the explicit readable PEM `sslrootcert` file
   (`QA_ROOT_CERTIFICATE`). The chain must be valid, the client clock correct and
   the server certificate valid for the **QA hostname**, not merely an IP address.
   A private key is never required. Stop on a CA/hostname/expiry/authentication error.
5. For Npgsql, the equivalent is **`SSL Mode=VerifyFull;GSS Encryption Mode=Disable`**
   and `Root Certificate=<verified-CA-path>` when the trust store is insufficient.
   Never downgrade to Require/Prefer, set a trust bypass or install a permissive
   validation callback. Npgsql connection passwords must use Npgsql key/value
   escaping, not URI percent-encoding. The prepared psql command uses libpq rather
   than parsing any Npgsql secret, so `QA_MIGRATION_CONNECTION` is not accepted or
   mapped by this command and the design-time factory's fallbacks cannot select
   its migration destination.
6. Independently verify the future Railway **session-pooler** runtime connection
   with Npgsql VerifyFull from its final runtime network; that remains a separate
   startup gate. A successful libpq operator session is not Npgsql/Railway TLS proof.

## Verification and readiness decision

**Completed:** exact local EF 10.0.11 pin/restore/version check; isolated locked
restore/build; fresh SQL generation and model-drift check; all 11 migrations/23
source files and all 407 frozen source hashes verified; SQL reviewed and unchanged;
**16 offline guard tests**, default offline command, 10 offline Railway checks and
15 QA build-config tests passed. Guard tests mock the client/network boundary;
they do **not** claim live PostgreSQL semantics, TLS success or executed migrations.

**NO-GO for remote migration execution:** fresh authenticated QA identity and saved
signup-OFF/Data-API-OFF evidence are still needed; actual current QA schema/primary
and direct role/permissions, IPv6 TCP/TLS/login and verified CA are not established;
psql is absent; formal metadata remains unverified; and the owner has **not approved
migration execution**. Missing credentials/services are blockers, not permission
to bypass checks. Exact Auth origins/redirects, testers and Railway runtime TLS
remain later private-testing gates documented in the earlier report.

Only the local EF manifest and this new report were added to the repository.
All **425 task-entry files and modes**, raw index, staged diff and HEAD are
preserved. No stage/commit/push, remote setting/billing change, migration, user,
resource or deployment was performed. The historical snapshot/publication manifests
remain unchanged; review these new companion files before any future publication.

Sources: [EF migration SQL scripts](https://learn.microsoft.com/ef/core/managing-schemas/migrations/applying#sql-scripts),
[PostgreSQL psql](https://www.postgresql.org/docs/current/app-psql.html),
[Supabase connections/TLS](https://supabase.com/docs/guides/database/connecting-to-postgres),
[Npgsql TLS](https://www.npgsql.org/doc/security.html).
