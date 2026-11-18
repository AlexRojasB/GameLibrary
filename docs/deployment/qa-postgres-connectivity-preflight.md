# QA PostgreSQL connectivity preflight

2026-11-07. **NO-GO for migrations from the current operator environment.**
Authenticated read-only QA access is restored, but the direct database endpoint
is unreachable over IPv6 from both this macOS host and its Docker client network.
TLS validation and direct database login therefore remain unverified, not failed
certificate checks. No migration, DDL, remote setting or billing change occurred.

QA: **`yqngxhryzklrhhryzzoi`**. Protected development:
**`iramzxpjbnldhebykzhx`**, excluded from all API/MCP/database operations.

## Results

| Check | Observed result |
| --- | --- |
| QA MCP restoration | Targeted OpenCode `supabase_qa` reconnect; ten project-scoped read-only tools returned. No interactive shell auth, credential extraction, server reconfiguration or development-server call |
| Authenticated identity | Fresh `supabase_qa.get_project_url` exactly equals `https://yqngxhryzklrhhryzzoi.supabase.co`, checked before project metadata/keys/SQL |
| Direct QA DNS | `db.yqngxhryzklrhhryzzoi.supabase.co`: one IPv6 address; no IPv4 address |
| Host IPv6/TCP 5432 | Python supports IPv6 sockets, but the bounded direct socket attempt failed immediately with **errno 65, No route to host**, before TCP establishment |
| Host PostgreSQL TLS | Not reached: no SSLRequest reply or TLS handshake; no conclusion about the database certificate's validity |
| Native psql | Not found on PATH or inspected standard Homebrew/Postgres.app locations; no package installed or system configuration changed |
| Existing container client | Existing local `postgres:17` image provides **psql 17.11 (Debian 17.11-1.pgdg13+2)**. Version check ran with network disabled, no database server, image pull or persistent volume |
| Container direct preflight | First hostname-based attempt failed at container DNS; a second bounded attempt used the QA AAAA address from the host while retaining the QA TLS hostname and failed **Network is unreachable** |
| Container VerifyFull | Requested `sslmode=verify-full`, `sslrootcert=system`, GSS encryption disabled; no TLS handshake was reached. No password supplied; metadata-only query could not execute |
| Current QA schema/primary | Fresh read-only MCP SQL: database `postgres`, PostgreSQL **17.11**, **not in recovery**, current schema `public`, **zero public relations**, **no `public.__EFMigrationsHistory`** |
| MCP session security | `supabase_read_only_user`, `transaction_read_only=on`, server SSL on, backend session SSL true; search path `"$user", public` |
| Intended migration role metadata | `postgres` has public USAGE and CREATE according to metadata. This does not prove direct-password login or a tested migration-role session |
| Advisors | Security, performance and health each returned **zero findings**; not proof of external connectivity or Data API disablement |
| Logs | Last-hour aggregate only: 2 edge-log and 112 PostgreSQL-log records. No raw messages, user data or credentials retrieved; counts alone are not an error-rate/health assessment |
| Public Auth preservation | Fresh settings HTTP 200: **`disable_signup=true`**, email enabled, `mailer_autoconfirm=false`, anonymous/phone disabled. QA discovery/JWKS HTTP 200, matching issuer and an ES256 public JWK |
| Saved Data API toggle | Still not exposed by this MCP catalog; owner-reported disabled, awaiting saved-setting dashboard evidence. No REST-root response was used as proof |
| Organization/region/plan/formal status | Remain unavailable through the scoped tools and independently unverified; no billing operations |

The MCP round trip confirms current QA identity and an unmigrated application
schema through the MCP service's network. It **does not** demonstrate that this
operator machine, an Npgsql process, Docker or Railway can establish a direct
VerifyFull session. Its read-only role/search path must not be confused with the
future operator's separately verified direct `postgres` session.

## Client available without host installation

The existing local image was resolved and used by immutable ID:

```text
postgres@sha256:e38411452a464af89e5adadb8d223bf53b898d47d6ef918b2d58c08707350449
```

This successful client-only check is repeatable without a package installation:

```sh
docker run --rm --pull=never --network=none --read-only \
  --cap-drop=ALL --security-opt=no-new-privileges \
  --entrypoint=psql \
  sha256:e38411452a464af89e5adadb8d223bf53b898d47d6ef918b2d58c08707350449 \
  --version
```

The transport attempts also used client-only, read-only, capability-dropped,
auto-removed containers, no mounted secret files and no image download. Their
prospective session enforced `default_transaction_read_only=on` and the only SQL
was a fixed metadata SELECT; neither attempt reached it. Suppressed provider
output was classified into transport errors without persisting credentials.

**Conclusion:** a compatible psql can be run locally without modifying unrelated
host configuration, but installing or containerizing a client does not add an IPv6
route. The previously prepared migration guard expects a `psql` executable on its
operator PATH; it does not automatically use Docker. No adapter was introduced or
guard changed to work around this network failure.

## Runtime session-pooler investigation — not a migration fallback

Current official Supabase documentation identifies **Shared Pooler / Session mode**
as the IPv4-compatible option for persistent backend runtime traffic. Actual QA
host and username are not supplied by the ten-tool project MCP or recorded in the
local plan. No hostname was guessed from a region and no guessed pooler was probed.

Obtain **only non-secret host/port/username details** from
[QA Connect → Session pooler](https://supabase.com/dashboard/project/yqngxhryzklrhhryzzoi?showConnect=true&method=session).
Expected port is **5432**; the usual username shape is
`postgres.yqngxhryzklrhhryzzoi`, but the actual selected role and endpoint must be
copied from that QA dialog. Do not paste a complete password-bearing connection
string into chat or a report.

Next runtime-only checks, once those details and private inputs are available:

1. Match dashboard QA reference and QA-qualified username; verify IPv4 DNS/TCP.
2. Validate the actual pooler certificate chain and hostname with Npgsql
   **`SSL Mode=VerifyFull;GSS Encryption Mode=Disable`**, using a verified public CA
   if needed. Never downgrade TLS or infer project identity from a shared host alone.
3. Use the QA password only through a secure process/secret interface and execute
   only read-only database/primary/schema/session metadata checks during preflight.
4. Later validate the same runtime configuration from the actual Railway network
   under its separate provisioning/deployment approval.

Runtime template remains server-only `ConnectionStrings__Default`, actual session
host/user, database postgres, port 5432, minimum pool 0/maximum 5, VerifyFull and
15-second connection/30-second command budgets. No variable was remotely installed.
No port-6543 transaction pooling, session-pooler migration substitution, paid IPv4
add-on, tunnel, proxy, new server or other resource is approved by this investigation.

## Remaining blockers and safest path

1. **Direct operator route:** use an owner-existing, explicitly selected machine or
   network with working native IPv6 and permitted outbound TCP 5432. Current host
   and Docker network cannot reach the QA direct endpoint. No routing/firewall/VPN
   configuration was altered. Do not conclude that other networks can connect
   without testing them.
2. **Direct TLS/login:** repeat QA-only DNS, TCP, certificate hostname/chain and
   read-only login checks in that actual operator environment. No QA root-CA path,
   database password or migration connection was supplied through the named QA
   process variables here; no credential stores were searched or secrets requested
   in chat. Obtain the current public root CA from verified QA database settings
   if the system store is insufficient. `Require`/`Prefer` or trust bypasses are
   not substitutes for VerifyFull.
3. **Execution client:** use an already available supported psql in the selected
   IPv6 environment, or separately review an explicit client-only Docker adapter
   if appropriate. The local Docker client exists, but its current network is
   also blocked and it is not integrated into the migration guard.
4. **Saved Data API setting:** provide reloaded
   [QA Data API Overview](https://supabase.com/dashboard/project/yqngxhryzklrhhryzzoi/integrations/data_api/overview)
   evidence showing **Enable Data API OFF**. Public signup disablement is now
   corroborated by a fresh Auth settings response; Auth remains operational.
5. **Migration authorization:** owner approval currently covers tooling and
   read-only preflight, not applying the SQL. Renew exact authenticated QA identity,
   target-role/primary/schema/history checks and saved-setting evidence immediately
   before a separately approved execution. Unexpected schema/history requires
   review/recovery evidence, not a reset or blind retry.
6. **Runtime pooler:** actual QA endpoint details and Npgsql VerifyFull/login still
   need verification. Auth URLs/redirects, testers and hosted-runtime checks remain
   later private-testing gates, not reasons to provision resources now.

**Safest execution path:** keep the reviewed EF 10.0.11 SQL and fail-closed direct
QA guard unchanged. Move only the operator workflow to an existing IPv6-capable
environment after owner selection, rerun read-only checks there, then seek explicit
migration approval. If no approved direct IPv6 environment exists, **stop** and
request a reviewed plan; do not silently migrate via the runtime pooler.

The [tooling/SQL report](qa-ef-tooling-alignment.md) remains authoritative for the
prepared command and hashes. Its historical unavailable-MCP/client-only blockers
are superseded by this report's narrower findings: MCP restored, initial state
refreshed, container client available, but direct TCP/TLS/login blocked. The SQL,
migrations/model, Feature 013/E2E disabled configuration and operator guard were
not modified or executed by this task.

## Preservation and scope

Only this new report was added in the repository. All **427 task-entry files and
modes**, HEAD, raw index and staged diff were preserved, including all **407** frozen
application source hashes. Historical snapshot and EF-artifact checksum sets remain
unchanged. No stage, commit, push, host package install, image pull, local/global
configuration edit, development operation, DDL, migration, cloud resource,
deployment, billing or Supabase setting change occurred. Only read-only QA metadata,
advisors, aggregated logs, public Auth checks and bounded network/client probes ran.

Sources: [OpenCode V2 MCP](https://opencode.ai/v2/docs/mcp-servers),
[Supabase connections](https://supabase.com/docs/guides/database/connecting-to-postgres),
[Supabase observability](https://supabase.com/docs/guides/monitoring-and-debugging),
[Npgsql TLS](https://www.npgsql.org/doc/security.html).
