# Real QA Session Pooler — transport validation

2026-11-07. **IPv4/TCP and client VerifyFull pass. NO-GO for migrations or runtime
startup: private QA password is unavailable, so pooler login and session tests are
blocked.** No migration, DDL, data modification, remote setting, cloud resource or
deployment was performed. The direct-only migration guard remains unchanged.

## Recorded QA target

Owner-supplied, non-secret Session Pooler values are now recorded in
[`deploy/qa/supabase-project-plan.json`](../../deploy/qa/supabase-project-plan.json):

| Field | Value |
| --- | --- |
| QA project | `yqngxhryzklrhhryzzoi` |
| Host | `aws-0-us-east-1.pooler.supabase.com` |
| Port / mode | `5432` / shared Session Pooler |
| Database | `postgres` |
| Pooled username | `postgres.yqngxhryzklrhhryzzoi` |
| Intended database role | `postgres`; not yet authenticated through the pooler |
| TLS policy | VerifyFull, explicit verified Supabase public root CA |

The authenticated QA MCP URL was freshly reconfirmed as
`https://yqngxhryzklrhhryzzoi.supabase.co` before project metadata reads. Protected
development `iramzxpjbnldhebykzhx` was never operated on. A shared pooler hostname
and a password challenge do not establish authenticated tenant identity; the
QA-qualified username and successful later login/read-back remain mandatory.
The cluster hostname is not independent verification of project region/plan/org.

## Actual network and TLS results

| Check | Observed result |
| --- | --- |
| Host IPv4 DNS | **Passed**, three A addresses observed |
| TCP 5432 | **Passed** on both tested addresses: `44.216.29.125` and `44.208.221.186`. The third address was not tested |
| PostgreSQL SSLRequest | Accepted on both tested host connections |
| Python default CA trust | Initially failed with verify code **19**, `self-signed certificate in certificate chain`; verification was not disabled |
| Explicit trusted CA | Public Supabase production root obtained through the official Studio-distributed HTTPS URL; no peer certificate was promoted to a trust anchor |
| Chain + hostname with explicit CA | **Passed on both tested addresses**, TLS **1.3**, validating `aws-0-us-east-1.pooler.supabase.com` against certificate SAN `*.pooler.supabase.com` |
| psql client | Existing local image's **psql 17.11**, non-root, read-only container, no image pull or database server |
| psql VerifyFull | **Passed before authentication**, with `sslmode=verify-full`, explicit root CA and GSS encryption disabled; client reached **`no password supplied`** and exited 2 |
| Pooler database login | **Blocked**, no approved named local QA password input was populated |
| Pooler role/primary/EF state | **Not verified through this pooler**; no password or metadata SELECT was sent successfully |
| Session continuity/prepared statement across transactions | **Not executed**, blocked on authenticated login |
| Pooler → PostgreSQL TLS | Unverified; client-side VerifyFull does not prove this separate hop |

The initial missing-root failure was resolved by adding an explicit, independently
obtained trust anchor to these isolated probes, **not** by downgrading to Require,
turning off hostname checking, accepting an untrusted peer chain or changing a
system/global trust store. Python's first two probes sent no PostgreSQL startup
message/password. The single psql attempt selected only the owner-supplied QA user,
used no password and could not execute its fixed read-only metadata SELECT.
Raw provider output was suppressed; only classified results were retained.

### Public CA provenance and integrity

Official Studio source:
[SSLConfiguration.tsx](https://github.com/supabase/supabase/blob/master/apps/studio/components/interfaces/Settings/Database/SSLConfiguration.tsx)
uses `ssl:certificate_url` through `useCustomContent`, whose
[official production template](https://github.com/supabase/supabase/blob/master/apps/studio/hooks/custom-content/custom-content.json)
resolves to:

```text
https://supabase-downloads.s3-ap-southeast-1.amazonaws.com/prod/ssl/prod-ca-2021.crt
```

Downloaded over validated HTTPS with redirects rejected; public certificate only:

- Subject/issuer: **Supabase Root 2021 CA**; Basic Constraints **CA:TRUE**.
- Validity: 2021-04-28 through 2031-04-26.
- PEM SHA256: `700723581420dd1ac98fd7e9ac529f0ef210eadcaf87fc868a3ad7d114c2f3b7`.
- DER certificate fingerprint SHA256:
  `F8:D7:C4:75:05:3D:1A:D9:40:5C:AB:55:C0:84:E5:12:21:F6:89:9F:26:78:4D:AD:FE:76:0D:8D:91:92:76:97`.
- Verified leaf issuer: **Supabase Intermediate 2021 CA**, leaf expiry 2030-03-11.
- Verified leaf DER SHA256:
  `03709ca44d1b06e4504da0a83b6ca065761edc704cd5634bcc3894fd5c7b3248`.

These are observed public certificate facts, not a perpetual pin or permission to
ignore rotation. Re-obtain/review current public trust material if the provider
rotates it. No private key was requested/downloaded or system trust configuration
edited. The successful hostname validation is for the shared pooler, not a
certificate uniquely identifying the QA PostgreSQL instance.

## Authenticated initial-state evidence — MCP only

Fresh fixed SELECT through the authenticated QA read-only MCP returned:

- Database `postgres`, PostgreSQL **17.11**, role **`supabase_read_only_user`**.
- `transaction_read_only=on`, MCP backend SSL true, **not in recovery**, schema public.
- **Zero public tables/views/sequences/foreign relations** and no
  `public.__EFMigrationsHistory`.
- Security/performance/health advisors each returned zero findings.

This confirms the current QA primary's initial application state through the MCP
network. It **does not substitute** for a login as `postgres` through the supplied
pooler, role/privilege read-back there, backend-hop TLS, or session continuity.
No application/Auth user rows, passwords or server credentials were queried.
The older plan blocks are historical; the new `sessionPoolerValidation` block
separately records these current layer-specific observations and remaining gaps.

## Secure local secret handoff and ready read-only test

No values were populated in the named QA password/password-file inputs checked;
`QA_MIGRATION_CONNECTION` was also absent. No default `.pgpass`, application
development secrets, OS Keychain, OAuth store, environment dump or generic
`ConnectionStrings__Default` was searched as a fallback. No interactive password
prompt was started through a shell tool.

**Required owner action:** create a QA-specific libpq password file through an
approved local editor/password manager, outside the repository, owned by the
operator with **0600** permissions. Its entry must select exactly this host, 5432,
postgres database and `postgres.yqngxhryzklrhhryzzoi` user; no wildcard entries.
Use libpq colon/backslash escaping. Supply **only its absolute path**, or the
`QA_POOLER_PASSWORD_FILE` path variable through the approved local process setup.
Do not paste the password, file content or a complete credential-bearing URL into
chat, CLI arguments, shell history, logs, reports or Git. No password file/value
was created or persisted by this task.

Prepared owner-only artifact directory:

```text
~/Documents/GameLibrary-QA-Artifacts/real-session-pooler-2026-11-07/
  connection-plan.json
  prod-ca-2021.crt
  ca-provenance.json
  run-readonly-preflight.py
  session-pooler-readonly.sql
  test-readonly-preflight.py
  host-network-tls.json
  host-verified-ca-tls.json
  psql17-verified-ca-no-password.json
  verification.json
  SHA256SUMS
```

The existing reviewed read-only runner/SQL were copied, not weakened or changed.
With the privately supplied file path and a fresh authenticated QA identity check,
the actual target command for this approved **read-only** test is:

```sh
artifact="$HOME/Documents/GameLibrary-QA-Artifacts/real-session-pooler-2026-11-07"
python3 "$artifact/run-readonly-preflight.py" \
  --host aws-0-us-east-1.pooler.supabase.com \
  --port 5432 \
  --username postgres.yqngxhryzklrhhryzzoi \
  --password-file "$QA_POOLER_PASSWORD_FILE" \
  --root-ca "$artifact/prod-ca-2021.crt" \
  --confirm-qa-connect-session --connect
```

The runner pins its fixed SELECT/session-probe SQL, requires a local Docker engine,
uses only the existing psql image, mounts the private password file read-only under
the operator's UID/GID, suppresses provider output, and enforces VerifyFull and
`default_transaction_read_only=on`. It has no migration/DDL entry point or fallback.
It will check the database role/primary, metadata/history presence, backend-hop SSL,
then prepared-statement/PID continuity over two read-only transactions. It does not
acquire advisory/table locks. Short-session success alone cannot prove mode under
contention, long-idle behavior, DDL privileges or migration safety.

## Railway ASP.NET Core / Npgsql assessment

**Transport is now a viable IPv4 candidate**, no longer blocked on direct-endpoint
IPv6 for runtime. Actual Npgsql 10.0.3 login, session behavior and the final Railway
network are still untested. Use server-only `ConnectionStrings__Default` with the
recorded host/port/database/pooled user, an approved private password and:

```text
SSL Mode=VerifyFull;Root Certificate=<verified-public-CA-path-in-runtime>;GSS Encryption Mode=Disable;Minimum Pool Size=0;Maximum Pool Size=5;Timeout=15;Command Timeout=30
```

Use Npgsql's builder/escaping; retain normal connection resets and default disabled
multiplexing. Do not assume the .NET/Noble trust store contains the Supabase root.
The public CA must be supplied to the eventual runtime through a separately
reviewed packaging/configuration step; no Dockerfile/service/secret variable was
changed here. Test Npgsql driver-only before API startup: the registered cleanup
service can work even with new uploads disabled. Check provider connection budgets
per process/replica and backend/pooler limits without changing billing/settings.

## Separate migration path — proposal only, not implemented

Session mode's documented backend affinity makes an IPv4 migration runner
**technically plausible**. This task has not established authenticated session
behavior or granted permission to execute migrations. The existing reviewed SQL,
its checksum and the direct-only guard remain unchanged.

A new, separately approved pooler guard would need all of:

1. Pin only this QA pooler host/5432/postgres/QA-qualified user; require fresh exact
   MCP identity and saved private-QA security evidence. Verify client VerifyFull,
   authenticated role/ownership/primary/schema/EF state and exact approved SQL/source
   hashes. A shared hostname or password challenge is insufficient.
2. Keep **one physical session** throughout preflight, serialization and execution,
   no automatic reconnect/replay. Disable local Npgsql pooling for an operator
   profile if using that driver; do not confuse a logical close with session life.
3. **Explicit concurrency control:** retain one authorized runner and require every
   migration entry point to use the same reviewed coordination mechanism. For the
   psql SQL path, a separately approved stable QA-specific session advisory-lock key,
   acquired nonblocking before history initialization and held across all migration
   transactions, is a candidate; fail immediately when busy and release in finally.
   Do not acquire any such lock in this read-only task. A key only protects cooperating
   runners; it does not prevent an unguarded native EF/admin session from bypassing it.
4. If choosing native EF Migrator instead, account for the pinned provider's
   transaction-scoped **ACCESS EXCLUSIVE history-table lock**. The generated psql
   SQL does **not** automatically include that provider lock. Idempotency is not
   serialization, and an absent history table complicates first-run concurrency.
5. Reconcile primary/history after any timeout, connection loss or uncertain COMMIT;
   never infer complete rollback, blindly retry, run Down or reset. Keep bounded
   connection/lock/statement budgets and no concurrent application schema changes.

**Recommendation:** finish the real authenticated read-only pooler/Npgsql tests
first. If they pass, explicitly review/approve a separate session-pooler execution
plan and concurrency guard; do not relax the direct guard. Until then, the only
currently approved migration route remains a direct QA connection from an
owner-existing IPv6-capable environment, which must itself pass its own TLS/login
checks. No paid IPv4 add-on, new runner resource or deployment is required or
authorized by this report. If neither path is verified/approved, stop.

Other pre-migration gates remain: saved Data API OFF evidence, the appropriate
role/DDL ownership review and explicit migration-execution approval. Private QA
origins/redirects, testers and final runtime checks remain separate startup gates.

## Preservation and verification

Only the QA connection plan and this new report were changed/added in the repository.
All other **427 task-entry files/modes**, Git HEAD, raw index and staged diff,
all **407 frozen source hashes**, historical checksum sets and the direct migration
guard are preserved. No stage, commit or push. Relevant JSON/target, offline-probe,
Railway/config, whitespace/link and preservation checks are run; no application
build, live pooler login/session test, migration or Npgsql runtime result is claimed.

Sources: [Supabase psql/VerifyFull](https://supabase.com/docs/guides/database/psql),
[connection modes](https://supabase.com/docs/guides/database/connecting-to-postgres),
[Supavisor session behavior](https://supabase.com/docs/guides/troubleshooting/supavisor-faq-YyP5tI),
[pinned Npgsql EF lock implementation](https://github.com/npgsql/efcore.pg/blob/v10.0.3/src/EFCore.PG/Migrations/Internal/NpgsqlHistoryRepository.cs),
[Npgsql TLS](https://www.npgsql.org/doc/security.html).
