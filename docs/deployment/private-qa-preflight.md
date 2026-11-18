# Private QA preflight — owner decisions pending

**Hosting decision superseded, 2026-11-06:** see [Railway private QA](private-qa-railway.md)
for current authenticated provider/billing evidence and the proposed configuration.
The Render observations below remain historical, not instructions to provision it.

2026-11-06. **NO-GO for cloud creation, migrations or deployment.** This is a
read-only provider preflight plus local diagnostics, not permission to deploy.
The [deployment sequence](private-qa.md#5-safe-migration-and-deployment-sequence--after-approval-only)
and its uploads-disabled scope remain applicable. This report supersedes earlier
provider-access, dependency-candidate and read-only-root readiness statements.

## Decision summary

| Area | Finding | Gate |
| --- | --- | --- |
| Git snapshot | Reviewed local source can be fingerprinted; current HEAD does not contain the prepared work | Owner-reviewed immutable commit/publication needed |
| Dependencies | Existing production audit is clean; existing build graph still has 3 critical / 8 high package records | Review and apply a tested targeted update, or explicitly accept documented risk |
| Render runtime | Four controlled 0.1-CPU / 512-MiB startups passed, including read-only root | Local startup feasible, not hosted/authenticated workload proof |
| Render access | Official CLI 2.35.0 authenticated; `My Workspace` selected; zero services | Account connection resolved; blueprint validation now requires payment information |
| Vercel | Hobby user authenticated; no teams returned; `bar-book` lookup still 403 | Owner must identify and authorize the intended Hobby scope |
| Supabase | MCP timed out; no management CLI/PAT available | Free organization and account-wide free-project slot not verified |
| Configuration | QA safety boundaries retained; unsupported Free shutdown override removed | Real QA origins, issuer, TLS and provider-side access controls require later verification |

No cloud resources, deployments, remote migrations, billing changes, Storage
operations or Git staging/commits/pushes were performed. Provider blueprint
validation was used, not blueprint creation/application. Local diagnostic
containers/network were uniquely named, then removed; existing PostgreSQL
containers/databases were not modified.

## 1. Source snapshot and preservation

- Branch `dev`, two commits ahead of its tracking branch. Starting HEAD:
  `1c840c59f1d6470bc2f0b0de59e768c6ee6f699b`.
- Extensive staged, unstaged and untracked Feature 011–013/deployment work is
  required by the current build. **Do not deploy HEAD alone**, `origin/dev`, or a
  staged-only export and assume it contains this work.
- Before this continuation's narrow configuration/documentation edits, all 403
  original Git-eligible file contents still matched the previous preflight
  baseline. The newly present `opencode.json` is owner/tool connection work; it
  was neither read nor modified and is excluded from the local source archive.
- The source-content archive captured before the shutdown-setting correction has
  SHA256 `e572bbac85638904eb7b98be846497ffb1b1104c34d09f79f3abd0f426fc5958`.
  This is diagnostic evidence, **not an approved final deployment artifact**.
  It intentionally excludes local tool/credential configuration and ignored files.
- Existing index fingerprint:
  `62092d1ed1bbe35197e650619a83a5a9eed14a4495a4223a795223ed11372439`.
  No stage/commit/push/reset/clean operation was performed.
- Limited private-key/provider-token pattern checks found only an explicit local
  Storage test fixture credential, not a live credential. This is not a complete
  secret-scanner certification. Review the final public Git diff before publication.

Safe publication path: owner reviews the complete intended source/specification,
test and lockfile set, excludes local connection/secret configuration, publishes a
dedicated immutable QA commit, records its full SHA, and reruns relevant gates on
that exact revision. Do not rely on a moving `dev` branch for an initial deploy.

## 2. Dependency security review

The existing lock has **32 affected package records: 3 critical, 8 high and 21
moderate**. These are package records, not 32 distinct vulnerabilities. Inherited
Angular build/CLI findings must not be counted as independent vulnerabilities.
All 22 distinct high/critical direct advisory records were retrieved from the
official GitHub Advisory API, alongside additional related moderate advisories.
The latest production-only audit returned **zero**. A subsequent full-audit refresh
timed out after 60 seconds; the existing-lock full count is the previous successful
preflight result on the same unchanged lockfile, not a newly successful refresh.

| Existing package | Severity | Exposure/precondition in this project | Fix for examined advisories |
| --- | --- | --- | --- |
| `@angular/build@22.1.4` | Critical, inherited | Executes build workers/Vite; not shipped as a hosted Node service | Targeted tooling update plus lock review |
| `piscina@5.2.0` | Critical | RCE gadget requires a separate prototype-pollution primitive before worker creation; build execution is the relevant boundary | `5.3.2` |
| `proxy-addr@2.0.7` | Critical | Express IP spoofing requires incorrectly written IPv4-mapped IPv6 trust subnets; inherited through CLI MCP/Express, not ASP.NET authorization | `2.0.8`, or remove the affected dependency path |
| `@angular/cli@22.1.4` | High, inherited | Angular CLI MCP tooling, not the deployed static application | Targeted tooling update plus lock review |
| `@modelcontextprotocol/sdk@1.30.0` | High | HTTP OAuth client credential confusion; shared experimental-task isolation; unbounded HTTP body/batch/validation resource consumption. Conditions differ by client/server use; normal static builds do not expose an MCP HTTP server | SDK `1.32.0` covers examined version fixes; replacement server `2.3.1` selected by candidate CLI |
| `body-parser@2.3.0` | High | An HTTP request with compressed-body trailing bytes can crash an exposed parser; CLI/Express path, not .NET API | `2.4.0`, or remove the affected dependency path |
| `brace-expansion@5.0.9` | High | Untrusted nested/comma/brace patterns can exhaust stack/CPU; ESLint/minimatch tooling | `5.0.12` also covers related moderate rewrite issue |
| `fast-uri@3.1.5` | High | URI normalization/serialization ambiguity can bypass host policy or enable SSRF when an application uses untrusted URLs in that way; CLI/AJV path is not an observed deployed SSRF sink | `3.1.9` covers related normalization findings |
| `source-map-js@1.2.1` | High | Attacker-controlled indexed source-map offsets can block build/test processing | `1.2.2` |
| `undici@7.29.0` | High | jsdom/test HTTP client: hostile HTTP/WebSocket/TLS peers can crash it; specific custom TLS callback and shared-cache/redirect configurations enable bypass/disclosure | `7.30.1` minimum; candidate resolves `7.30.2` |
| `vite@8.1.5` | High | Exposed dev-server editor-launch paths, including Windows-only bypasses; related editor issues depend on editor/path control. Static QA hosting runs no Vite dev server | Examined 8.x fix lines: `8.1.6`, `8.2.4`, `8.3.3`; candidate `8.3.5` |

These packages do not become live server dependencies of the static Vercel
artifact or the separate .NET image. That does **not** make the build pipeline
safe automatically: compromised build inputs/dependencies can affect artifacts
and CI privileges. Keep builds to reviewed source, no untrusted preview builds
with secrets, no exposed Angular/Vite/MCP/test servers, and no backend secrets in
the frontend build environment.

**Important MCP nuance:** GHSA-46jr-4wx8-v453 requires input-element and request-size
limits in addition to upgrading when exposing an HTTP MCP server. An audit with
no high package records does not prove those configuration protections are set.
This QA design does not run the Angular CLI MCP server at all.

### Isolated candidate, not applied to the repository

The older suggested tooling `22.2.1` is insufficient: its Vite `8.3.0` and MCP
server `2.0.0` fall in newer advisory ranges. A new temporary candidate uses:

1. `@angular/build@22.2.6`, `@angular/cli@22.2.6` (exact versions).
2. `npm update brace-expansion fast-uri undici --package-lock-only --ignore-scripts`
   within that temporary directory, preserving compatible parent constraints.
3. Existing Angular framework/compiler packages `22.2.1` and Zod override `4.6.6`
   retained; no new direct dependency or `npm audit fix --force`.

Tooling alone resolves to **0 critical / 3 high / 17 moderate**. The three targeted
transitive refreshes select `brace-expansion@5.0.12`, `fast-uri@3.1.9` and
`undici@7.30.2`, producing **0 critical / 0 high / 17 moderate**. The tooling change
alters 269 version records (including removed/added packages), followed by three
transitive version changes; this is not merely a two-line manifest bump.

Remaining moderate records include minimatch amplification inherited through
ESLint/TypeScript-ESLint/Angular-ESLint and Vitest mock-server issues. Do not accept
npm's suggested major downgrades blindly or silently introduce incompatible
minimatch overrides. These need a separate compatibility review or explicit
limited-QA risk acceptance. None of these dependency changes has been applied to
the repository. Every non-development package version is unchanged in this candidate.

| Isolated candidate verification | Result |
| --- | --- |
| Fresh `npm ci` with separate writable cache | Passed |
| Frontend tests, `--watch=false` | 465 passed across 38 files |
| QA configuration tests | 15 passed |
| Frontend lint | Passed |
| Production build | Passed; initial 678.67 kB |
| QA build, synthetic public inputs only | Passed; initial 678.78 kB |
| Production audit | Zero vulnerabilities |
| Full audit | 0 critical / 0 high / 17 moderate; expected nonzero audit exit |
| PWA output | index, manifest, worker and ngsw.json present |

Candidate lock SHA256:
`cf4e8d5b6f6b3e08b8ca0ec0b4a13f7960c6000085152de0206f6c88c63e1575`.
Existing 4.13-kB Random Picker stylesheet warning and Vitest `poolOptions`
deprecation remain. New tooling also warns about future Vite native config loading
of ESM syntax from `vitest-base.config.ts` as CommonJS. No warning was suppressed,
budget raised or test weakened. Backend/browser E2E suites were not rerun for this
temporary tooling experiment; prior results are recorded separately and final
immutable-source verification is still required.

The first isolated experiment failed on root-owned entries in the normal npm
cache. A separate writable temporary `--cache` resolved that failure; no recursive
`sudo chown`, global npm repair or repository lock mutation was performed.

## 3. Read-only-root investigation and Render feasibility

Same existing image index:
`sha256:ab067feca6450793957b9ca8fbc01f82645adac8cffc9cf3bb8330e551a58b0c`.
Same Production settings, synthetic HTTPS metadata, no test authentication or
Storage secrets, same migrated empty disposable PostgreSQL 16 database, loopback
port publishing and proxy-free HTTP probes. All runs used 0.1 CPU, 512 MiB hard
memory/swap and the same 32-MiB `/tmp` tmpfs. Only root mutability/order changed.
No compilation or frontend test suite ran concurrently with this comparison.

| Run order | Root | First health 200 | Memory peak, bytes | SIGTERM to exit |
| --- | --- | --- | --- | --- |
| 1 | Read-only | 39.57 s | 40,419,328 | 14.06 s |
| 2 | Writable | 20.42 s | 40,218,624 | 11.78 s |
| 3 | Writable | 22.00 s | 41,742,336 | 14.59 s |
| 4 | Read-only | 22.96 s | 39,944,192 | 12.24 s |

All reached health within the **unchanged 90-second budget**, exited 0, and had no
OOM/max-memory events. Retained read-only logs show normal ephemeral/in-memory
Data Protection key warnings, not an I/O startup exception. Writable runs use
ephemeral filesystem keys. No key-persistence infrastructure was introduced.
One initial harness attempt failed to publish a port on an internal-only Docker
network; it produced no startup conclusion. The corrected isolated bridge-network
run above completed and retained logs/results. No existing local database was used.

**Conclusion:** the old read-only-root timeout was not reproduced and its original
cause remains unknown because its logs were not retained. Read-only root itself
is not an established deployment blocker. Render's proposed normal writable
ephemeral filesystem is still the deployment mode; do not add a new hardening or
persistent-disk requirement. Startup/shutdown evidence is local only and does not
prove real JWKS/TLS, authenticated CRUD, load capacity or Render ingress behavior.

### Official Render validation found two separate gates

The authenticated CLI validated `render.yaml` against `My Workspace` without
creating/applying it:

1. Initial response: `max shutdown delay is not supported for free tier services`
   at `services[0].maxShutdownDelaySeconds`. Removed only that optional 60-second
   override locally. Use the platform default 30 seconds; all measured local
   shutdowns above were under 15 seconds. No paid shutdown feature is needed.
2. Revalidation: `valid: false`, **`need_payment_info`** at `services[0]` and the
   document root. This is an observed account/workflow gate, not a syntax failure
   or evidence that Free compute ceased to exist. No payment details were added.

The account still lists zero services after validation. Owner must inspect the
Render dashboard/provider requirement and decide whether a strict-$0 setup is
possible without changing billing. Do not assume a manual dashboard path bypasses
the requirement. Adding payment information is not approved, and Free compute
does not cap bandwidth/build overages when a payment method exists.

CLI access is sufficient; MCP is optional. The owner-added Render MCP currently
reports `needs authentication`; no credentials were extracted or copied from CLI
to MCP. If desired, owner authorizes it separately through `/mcps` and confirms
the workspace. MCP authorization grants potentially mutating capabilities, not an
inherently read-only role.

## 4. Remaining provider and configuration gates

- **Vercel:** authenticated user `proxtos-5361` reports Hobby. `list_teams` returns
  none; known default-team lookup for `bar-book` remains 403. This does not prove
  there are no teams. Owner identifies the intended Hobby team/account first;
  then generate a fresh consent link for exactly that scope. No authorization for
  another team was started, and no unrelated project is a QA target.
- **Supabase:** official policy allows two active Free projects across all
  organizations where the user is Owner/Administrator; paused projects do not
  count. Plans apply per organization, so a new project in a paid organization is
  not a Free project. Tool access cannot currently enumerate organizations,
  account-wide active projects or an available slot. Owner must confirm a Free
  organization, create permission and a free slot through dashboard or authorized
  account-level tooling, **without creating anything yet**. Do not pause, transfer,
  delete or alter `iramzxpjbnldhebykzhx` to manufacture capacity.
- **Secrets/build:** only three public QA build inputs are serialized; current dev
  Supabase project, secret keys and JWT-form keys are rejected. Backend connection
  and optional provider credentials never belong in Vercel. Docker context is
  allowlisted and excludes local secrets/outputs; Dockerfile references no secret
  build arguments. Render can translate runtime variables to build arguments, so
  retain that restriction. Existing default backend metadata still points to dev:
  explicitly override it with the new QA issuer before creating the API service.
- **Auth/access:** asymmetric QA JWT/JWKS, `authenticated` audience, HTTPS metadata,
  approved test accounts and exact frontend Site URL/redirects; disable public and
  anonymous signups/unused providers. Disable **QA Data API before migrations**;
  confirm REST/GraphQL cannot read application data using public keys/browser JWTs.
  Backend-only ownership checks remain authoritative; no parallel RLS workflow.
- **TLS/database:** Npgsql `VerifyFull`, real dashboard session-pooler host/port
  5432 for IPv4 runtime, bounded pool/timeouts. Reviewed EF migrations run only from
  a separate operator's verified QA-primary direct TLS connection, never startup,
  transaction pooling or a Render Free pre-deploy job. EF CLI is still `10.0.7`;
  align it to runtime `10.0.11` before remote migration approval. No tool upgraded.
- **CORS/cache/uploads:** exact stable frontend origin, not wildcards/previews;
  bearer interceptor only targets API origin; external images get no application
  bearer token. PWA has no API data cache group. Uploads and all E2E switches stay
  false, no Storage provisioning/credentials; start with empty managed-cover
  lifecycle metadata because reads/cleanup are not disabled by the upload switch.

## 5. Exact owner decisions/approvals, in order

1. **Connection/eligibility decisions only:** confirm the Vercel Hobby scope and
   authorize that scope; confirm Supabase Free organization and a free slot; review
   Render's `need_payment_info` requirement without changing billing. If $0 cannot
   be enforced under the stated restrictions, stop rather than upgrade.
2. **Source/security:** approve the exact targeted dependency diff after verification
   and any remaining moderate-risk exception; review/publish a complete immutable
   QA commit and record SHA. This task does not stage/commit/push it.
3. **Separate creation approval:** name accounts/regions/resource names and authorize
   only `game-library-qa` (Supabase Free/Auth, Data API disabled),
   `game-library-qa-api` (Render Free), and `game-library-qa-web` (Vercel Hobby).
   Creation may trigger an initial deploy and is not merely configuration.
4. **Separate migration/deployment approval:** verified new QA ref/direct TLS target,
   empty-target or tested-backup evidence, reviewed EF migration list and source SHA;
   private secret injection and coordinated frontend/API initial deployments.
5. **Bounded hosted verification:** two approved synthetic QA users/data, isolation,
   Auth/JWKS/refresh, CRUD/filters/Picker, CORS, Data API denial, PWA deep links,
   disabled uploads, resource limits and cold-start/restart/SIGTERM checks.

No blanket approval is inferred from "continue". Billing/payment changes, paid
plans/trials/overages, Storage, production changes, Git publication by this agent
and resource/data deletion remain unapproved.

## Sources and local evidence

- [Render Free](https://render.com/docs/free), [Docker](https://render.com/docs/docker),
  [deploy/shutdown/filesystem](https://render.com/docs/deploys),
  [official CLI](https://render.com/docs/cli), [MCP](https://render.com/docs/mcp-server).
- [Supabase billing/free-project policy](https://supabase.com/docs/guides/platform/billing-on-supabase),
  [Data API disablement](https://supabase.com/docs/guides/api/securing-your-api),
  [connections](https://supabase.com/docs/guides/database/connecting-to-postgres).
- [GitHub Advisory Database](https://github.com/advisories): GHSA-67c8-pqhq-4rmx,
  GHSA-jqcg-44mw-7w3h, GHSA-27r3-jxmh-p828, GHSA-46jr-4wx8-v453,
  GHSA-6qxp-vccf-f47h, GHSA-22jm-h49p-29qw, GHSA-f897-62hf-6gxq,
  GHSA-qhr7-859c-m2p7, GHSA-6j4f-fj2g-mc7p, GHSA-q2hr-2g5m-vwhr,
  GHSA-68fv-2mgg-jv7q, GHSA-587h-7wmj-h5pm, GHSA-r825-w8mm-5pxw,
  GHSA-2m5v-pch6-74hx and related URI/Undici records retained locally.
- Temporary evidence root:
  `/private/var/folders/fd/h4rhj_wn5_71qy5q79xm8r_m0000gn/T/opencode/qa-preflight/`.
  `baseline.json`, `candidate-source-manifest.json`, `candidate-source.tar`,
  `advisories/`, `security-candidate-22.2.6/`, `render-validation.json`,
  `root-comparison-29b01e57/` and diagnostic scripts. Temporary artifacts are not
  committed or a substitute for final-snapshot gates.
