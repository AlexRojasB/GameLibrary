# QA security and exact snapshot review

2026-11-06. **Preparation only; NO-GO for external changes.** Continues the
[Railway runbook](private-qa-railway.md) and
[dependency approval record](qa-dependency-security-approval.md).

This continuation reproduced the **exact** security candidate in a fresh external
source copy. It did not apply the candidate to this checkout, alter its existing
index/source, stage, commit, push, provision, deploy, change billing or run remote
migrations. New local disposable test databases/build outputs are verification
only, not hosted QA resources. Upload enablement and Feature 013 release remain
unapproved. The snapshot and all account decisions below require owner review.

## 1. Dependency changes proposed for explicit approval

The two-file patch is unchanged:

- `src/frontend/package.json`: only direct development-tool selectors
  `@angular/build` and `@angular/cli` change from `^22.1.4` to exact **22.2.6**.
- `src/frontend/package-lock.json`: **272 version-record changes**, including
  added/removed packages, plus **three metadata-only records** (275 changed records
  total). Metadata-only changes are the root dev-dependency selectors and optional
  flags for `@parcel/watcher` and its nested `node-addon-api`.
- The explicitly targeted compatible transitive refreshes remain
  `brace-expansion` **5.0.12**, `fast-uri` **3.1.9**, `undici` **7.30.2**.
  The tooling graph also resolves Vite **8.3.5**, Piscina **5.3.2**, and MCP server
  **2.3.1** through the selected Angular tools; these are not extra direct dependencies.
- **Zero changed non-development lock records**, including metadata. Framework/
  compiler **22.2.1**, cropper **9.1.7**, Zod override **4.6.6**, Supabase JS,
  TypeScript and the other direct dependency selectors are unchanged. No backend,
  native decoder, migration, application source, test assertion or budget change
  belongs to this security patch. The hundreds of pre-existing Game Library
  feature changes are a separate integrated-source approval, not security changes.

| Exact artifact | SHA256 |
| --- | --- |
| Candidate manifest | `6779fe54babeda3f3da1f8973544369c350658edf4f72bf8d2da5c8ef158cdd2` |
| Candidate lock | `cf4e8d5b6f6b3e08b8ca0ec0b4a13f7960c6000085152de0206f6c88c63e1575` |
| Exact 194372-byte unified diff | `7b1d013fbe8e33df988c5839849129f8150a6e3dae7fd3387463a478ce84c034` |

The diff was recomputed against the current package files and matches the retained
patch byte-for-byte. No `npm update`, `npm audit fix`, force resolution, new override
or unrelated version selection was run. Clean installation used the candidate lock,
not a freshly resolved version graph; the lock hash remained unchanged.

### Fresh full audits succeeded

Earlier 60-second timeouts are historical. This continuation used a clean private
HOME/cache, a 300-second outer budget, bounded 15-second fetches and zero network
retries. No TLS check or audit finding was suppressed:

```sh
npm audit --json --timing --fetch-retries=0 --fetch-timeout=15000
npm audit --json --timing --fetch-retries=0 --fetch-timeout=15000 --omit=dev
```

| Lock / scope | Critical | High | Moderate | Total | Outcome |
| --- | ---: | ---: | ---: | ---: | --- |
| Existing worktree, full | 3 | 8 | 21 | 32 | Completed, exit 1 for findings, ~53.5 s |
| Exact candidate, full | **0** | **0** | **17** | **17** | Completed, exit 1 for findings, ~50.9 s |
| Existing worktree, production-only | 0 | 0 | 0 | 0 | Completed, exit 0 |
| Exact candidate, production-only | 0 | 0 | 0 | 0 | Completed, exit 0 |

No audit returned an audit-service error. Counts are affected **package records**,
not distinct vulnerabilities. The candidate's 17 records propagate **three direct
advisory URLs**, retrieved from the official GitHub Advisory API:

| Advisory | Remaining exposure / fix floor |
| --- | --- |
| [GHSA-82fw-gwwq-j7x9](https://github.com/advisories/GHSA-82fw-gwwq-j7x9) | Vitest/mocker path traversal/file read through a reachable development mock server. 4.x fix floor **4.1.11**; standalone plugin vs authenticated browser-mode paths differ. |
| [GHSA-qxg6-89mg-rqv8](https://github.com/advisories/GHSA-qxg6-89mg-rqv8) | Unauthenticated manual mock registration/code injection in the public interceptor/mocker plugin; 4.x fix **4.1.12**. Maintainer states Node runs do not register these handlers; not a `vite build`/static-runtime handler. |
| [GHSA-g3hv-75wg-m249](https://github.com/advisories/GHSA-g3hv-75wg-m249) | Minimatch globparts amplification from attacker-controlled patterns, documented at optimizationLevel >=2; fix **11.0.1**. Candidate's lint dependency constraints still select affected versions. |

Affected record names: `@angular-eslint/eslint-plugin`,
`@angular-eslint/eslint-plugin-template`, `@angular-eslint/schematics`,
`@angular-eslint/utils`, `@eslint/config-array`, `@eslint/js`,
`@typescript-eslint/eslint-plugin`, `@typescript-eslint/parser`,
`@typescript-eslint/type-utils`, `@typescript-eslint/typescript-estree`,
`@typescript-eslint/utils`, `@vitest/mocker`, `angular-eslint`, `eslint`,
`minimatch`, `typescript-eslint`, `vitest`.

These are build/lint/test dependencies, not deployed Vercel Node functions or
Railway API dependencies. Tests use Node/jsdom; no direct public interceptor-plugin
configuration was identified. Keep all development/test endpoints loopback-only,
reviewed build inputs and no backend credentials in frontend builds. This limits
exposure; it is **not a claim that the build pipeline is vulnerability-free**.

The exact candidate is within the previously proposed security scope but is not
approved or applied. Owner must accept this narrow private-QA residual risk or
authorize a **separate** compatible Vitest/minimatch remediation study. Do not
silently alter this candidate, force minimatch 11 through incompatible parents, or
accept npm's suggested ESLint/Angular-ESLint major downgrades as an automatic fix.

## 2. Verification on the proposed snapshot

The source under test is the 407-file manifest in section 3, exported before this
review document was written. Only the two security-candidate package files differ
from the entry worktree; all other selected bytes/modes are the captured worktree.
The fresh source copy is outside this checkout and has no Git index.

| Fresh check | Result |
| --- | --- |
| Candidate `npm ci`, no force/legacy-peer-deps | Passed, 441 packages installed; new writable cache/private HOME; lock unchanged |
| Installed dependency tree `npm ls --all --json` | Passed, zero reported problems |
| QA configuration tests | **15 passed** |
| Complete frontend unit suite | **465 passed / 38 files**, zero failed/skipped |
| Frontend lint | Passed |
| TypeScript `tsc --noEmit --project tsconfig.spec.json` | Passed |
| Production build | Passed; initial **678.67 kB**, lazy editor **58.02 kB** |
| Synthetic Railway-origin QA build | Passed; initial **678.78 kB**; only synthetic public inputs |
| E2E build | Passed; development/test artifact only, not the deployable QA build |
| Full and production dependency audits | Completed as recorded above |
| Native preparation in fresh source copy | Passed, pinned libjpeg-turbo 3.2.1, host osx-x64; not a new Linux runtime gate |
| .NET locked restore / complete solution build | Passed; build zero warnings/errors |
| NuGet known-vulnerability audit, direct + transitive | Passed, no vulnerable package records in all four solution projects; `dotnet package list --vulnerable --include-transitive --no-restore --format json`, nuget.org source |
| Core unit tests | **337 passed**, zero failed/skipped |
| PostgreSQL/API integration suite, configured rerun | **994 passed**, zero failed/skipped |
| EF pending-model check, existing tool invoked by absolute path | Passed, **no model changes since last migration**; no DB connection/migration applied |
| Railway offline safety/config regression checks | **10 passed** |
| Browser E2E, desktop Chromium + Pixel 7 emulation | **22 passed**, zero failures/skips/retries, ~138 s; unchanged tests/source |

Two harness/setup failures are retained, not hidden: the first integration run
passed 985 and failed nine because the clean private HOME omitted
`ConnectionStrings:Default` required by generic/auth host factories. The complete
994-test rerun supplied explicit **Default/Test** connections to a new disposable
local PostgreSQL database on port **5433**, with no source/assertion changes.
Next, `dotnet ef` could not discover the existing global tool under the isolated
HOME/PATH; this was a tool-discovery failure, not a model-check result. Its original
log remains `ef-model.log`. The already installed **10.0.7** shim was subsequently
located at `/Users/mac/.dotnet/tools/dotnet-ef`; no tool installation/update occurred.
Each launcher's newly created databases were removed even on failure. No existing
database or port-5432 database was used.

The absolute-tool EF rerun passed under the same isolated HOME; its output is in
`ef-model-absolute-tool.log`, separate from the failed discovery attempt. All ten
offline Railway checks and all 22 browser cases then passed. The browser runner
used an external verification-only configuration with frontend **127.0.0.1:4300**,
the unchanged test API **localhost:5218**, matching test CORS/callback values, no
server reuse and **zero retries**. It did not stop/reuse the owner's port-4200
server or edit snapshot source/tests. A new disposable local E2E database was
removed after the run. Tests cover Library/Play Log/manage forms/search covers,
Picker/Family Friendly/history, Steam preview/import, and cover crop/replace/remove,
failed saves/concurrency, cancellation and lost-response receipt recovery on both
browser projects. These simulated provider/test-auth flows are local regression
evidence, not real-provider or physical-device acceptance. Upload/test flags were
enabled **only for local E2E**, never for the proposed hosted QA configuration.

Existing warnings are retained: Picker stylesheet 4.13 kB vs 4 kB, removed Vitest
poolOptions, future Vite native-config-loader warning, and EF **10.0.7** tool vs
**10.0.11** runtime mismatch. Browser dev-server prebundling was unused with caching
disabled. No budget, timeout assertion, decoder check, admission limit or
security boundary was weakened. Physical-device, real Auth/JWKS/Storage and hosted
operational verification remain outside this local test evidence.

## 3. Exact proposed Git snapshot manifest

**Status: PROPOSED, not an approved release/commit.** No Git object, ref, temporary
index, clone, staging operation or commit was created by this task.

| Identity | Value |
| --- | --- |
| Proposed parent commit | `1c840c59f1d6470bc2f0b0de59e768c6ee6f699b` (`dev`) |
| Entry Git-eligible files | **409**, no unmerged index entries or missing files |
| Proposed snapshot | **407 files**, **216** new/changed relative to parent, 191 unchanged |
| Manifest SHA256 | `73899e7fd9732884421edefe0dbb0c65edfaa017655b3868d7286d084437e1fb` |
| Deterministic uncompressed USTAR SHA256 | `b3f2f41a9de588b06f8c28e84398c359522e0ebdc6327f01b1ce1afcdfabb455` |
| Archive size | **3,962,880 bytes** |
| Reproduction | Two exports produced identical archive hashes |
| Original raw index SHA256 | `e369fb71d4dd9dddde1ab2e8d904c5e37d3303d9e4ebf189a8eca85bafbc57eb` |
| Original staged binary-diff SHA256 | `62092d1ed1bbe35197e650619a83a5a9eed14a4495a4223a795223ed11372439` |

The complete machine-readable manifest is **not just directory globs**. Each
selected path records its exact Git mode, size, SHA256, computed Git blob SHA1,
parent/index blob IDs, origin and review area. Exclusions have hashes/reasons;
the four independently staged versions have their own hashes. It is retained at:

```text
/private/var/folders/fd/h4rhj_wn5_71qy5q79xm8r_m0000gn/T/opencode/qa-security-snapshot/
  proposed-snapshot-manifest.json
  proposed-snapshot-summary.json
  proposed-qa-source.tar
  proposed-qa-source-repeat.tar
  proposed-qa-source/
  staged-versions/
  dependency-record-diff.json
```

These are local temporary artifacts, not published or durable release storage.
Owner must retain the approved manifest/archive/patch privately with these hashes
before cleanup of temporary storage. The earlier 406-file exploratory export in
this directory is **not** the selected proposal: it omitted the Steam proposal
document and would lose contextual links. Only the identities above select the
407-file candidate. New post-export review evidence such as this document is
outside the frozen application snapshot; adding it to a commit requires an
explicit, separately enumerated documentation decision/re-manifest.

### Contents and separation

| Review area | Files | Disposition |
| --- | ---: | --- |
| Backend | 130 | Include both production projects, contracts/composition/domain rules, EF metadata/locks and all migrations |
| Frontend | 173 | Include current integrated PWA/source/tests/build configuration, with exact two-file security overlay |
| Backend tests | 48 | Include relational/auth/lifecycle/native/security regressions; do not ship test executables in API image |
| Native/operational gates | 16 | Include provenance, pins/notices/preparation and test harness source; not extra production services |
| Deployment/current and historical evidence | 10 | Include Railway Docker/plan/checks, environment example and QA reports; historical Render conclusions remain labeled superseded |
| Specifications/context | 29 | Include approved authority, feature specs/reports and repository/build context |
| Deferred documentation | 1 | Retain Steam logging lifecycle proposal **as Proposed/unapproved context only**, not implementation approval |

The source origins are 191 HEAD-identical files, 101 newer worktree versions, 112
untracked worktree files, two exact-candidate replacements and one staged-only
version already matching its worktree. **Do not deploy HEAD or the index alone.**

Required integrated work includes the Steam/Picker stabilization and approved
playtime-total behavior, Feature 012 classification, Feature 013 explicit-cover
write contract/current references/EF lifecycle/normalization/UI/privacy and all
deployment inputs. These share files and contracts. Removing only the upload UI,
controllers, EF columns or native dependencies is **not** a safe scope separation.
Keep new uploads false and preserve all existing authorization/codec controls.

All **11 EF migrations**, designers and current snapshot are included, through
`20261029134813_AddCoverUploads`; specifically the untracked Steam playtime,
Family Friendly and cover migrations are required. No schema change was authored.
The proposed future acknowledged Steam baseline/logging field/action was not found
in production code. Its document remains clearly Proposed. Feature 012 manual
acceptance and Feature 013 real-provider/device/release work remain unfinished;
retaining implementation for limited QA does not declare those gates complete.

Excluded, without moving/deleting/editing the originals:

1. `opencode.json`: machine/session provider connection configuration, not app source.
2. `render.yaml`: retired host blueprint; no Render resource should be created.

Ignored build outputs, package caches, real `.env` files, user secrets, credentials,
provider links and Git administration data were never inputs. `.env.example`
templates remain. Final Docker context remains allowlisted and excludes secrets/
Development settings. No generated QA environment, binary or test result is in
the archive.

Four paths have staged changes: `specs/011-steam-integration/spec.md` and Steam
page `.html`, `.scss`, `.ts`. The spec/HTML/TS also have **different unstaged
versions**; the proposal deliberately selects their complete worktree versions,
not their older staged blobs. Separate staged exports preserve the owner's
original staging decisions. No staged hunk was overwritten.

Preservation verification checked all **409 original Git-eligible files**, including
byte hashes/modes, the raw index, staged binary diff and HEAD against the entry
baseline. All matched; the only new checkout file is this review document.
Every selected source byte/mode and every archive member also matched the frozen
407-file manifest after builds/tests; both archive hashes remained identical.
`git diff --check` and this document's whitespace/relative-link checks passed.
Machine-readable results are retained as `preservation-verification.json` beside
the manifest. This review is deliberately not part of the frozen tested source.

A limited content-pattern review flagged only two existing loopback PostgreSQL
examples (README/Foundation spec) and the operational loopback fixture's constant
credential. Values were withheld. No production credential was identified by
this bounded check; `gitleaks`/`detect-secrets` are not installed. This is **not a
comprehensive secret-scan certification**; owner review of a public publication
remains required. No credential stores/ignored secret values were read to build
the manifest.

### Exact commit plan — after separate approval only

1. Approve the exact 407-path manifest/archive, two-file dependency diff and residual
   moderate-risk disposition, including integrated unfinished-feature QA scope.
2. Prepare a separate review clone from the recorded parent. Populate it only from
   this verified archive, keeping the original checkout/index untouched. Compare
   every path/mode/Git blob against the manifest and reject unexpected extras or
   omissions. Neither a moving `dev` branch nor `git add .` in this checkout is a plan.
3. Proposed **one coordinated QA commit**: `Prepare integrated uploads-disabled QA
   snapshot and validated tooling security fixes`. Include exactly the 407 selected
   paths. Avoid a separately published intermediate vulnerable-tooling or partial
   Feature 013 commit. Author/date/commit SHA are intentionally not fabricated now.
4. Only with explicit Git approval stage/commit in that separate review clone;
   verify its tree exactly against the approved manifest. If additional review docs
   are wanted in the commit, enumerate/re-manifest them first rather than silently
   adding untested files. Record the resulting full SHA and rerun final gates there.
5. Obtain separate push and provider/deployment/migration approval. Deploy the same
   immutable SHA/frontend-API pair, not a branch tip. Snapshot approval alone grants
   none of those actions. No credentials or production data may enter the commit.

## 4. Provider findings — read-only

### Vercel

Reproduced `get_team(team_06vOR8VzjmIVJfhtgodwg1wL)` **403**, scope `bar-book`.
The response explicitly identifies missing team authorization; SAML and enforced
SSO are both false. The authenticated user remains `proxtos-5361`, user billing
active Hobby. `list_teams` and Git deployment discovery still return no visible
teams. This is a connection-grant/scoping problem, not evidence that the account
has no team, the project is broken, or the intended team is definitely Hobby.

Owner must identify the intended QA scope. If it is this known team, explicitly
authorize the connection through the provider's `authorize_team_access` consent
flow, then retry on the same connection. Creating a consent flow/grant is not a
read-only operation, so no flow was started here. No project was created as an
access test, no different scope was guessed and no CLI/default-token bypass used.

### Supabase

Project MCP responds for existing development ref `iramzxpjbnldhebykzhx`. The
current catalog exposes **20 project-scoped/docs tools**, no organization listing,
account-wide project inventory or Free create-capacity operation. Read-only docs
search reconfirms the policy, **not account capacity**:

- Two active Free projects across organizations where the user is Owner/Admin;
  paused projects do not count.
- Billing FAQ adds that **another Owner/Admin in the target organization who has
  exhausted their own Free quota can block creation too**. Verifying only this
  owner's project count is insufficient.
- A paid organization cannot contain a Free project.

Owner must verify the intended Free organization, creation rights and capacity for
all relevant owners/admins in dashboard or authorized management tooling. Do not
change roles, pause/delete/transfer projects, provision a project to test capacity,
create a paid branch, extract OAuth credentials or reuse the connected DB. No
application rows/remote schema or secret/public keys were queried in this task.

Sources: [Supabase billing](https://supabase.com/docs/guides/platform/billing-on-supabase),
[billing FAQ](https://supabase.com/docs/guides/platform/billing-faq).

### Railway subscription and resource assumptions

Existing workspace `6636d7dc-798b-4470-8376-b36089020713` remains active **Hobby**,
not a trial, with a $5 subscription item and no scheduled cancellation. The same
two unrelated projects remain; no Game Library project exists. No configuration,
variable, source, limit or deployment was changed.

Fresh CLI usage: **$3.3147 current**, **$3.9326 forecast**, current cycle ends
2026-11-11 18:03:43 UTC; no compute usage limit configured. Additional read-only
billing inspection found all **three returned invoices paid**, each raw total/
amount paid 500 (consistent with $5 invoices in provider minor currency units).
No invoice URL, payment method or billing address was requested. No unpaid invoice
is observed in the returned list; that is not a future charge guarantee.

Previous-cycle CLI usage is **$2.4217533413658634** for
2026-09-11 18:03:43 UTC–2026-10-11 18:03:43 UTC, matching the period of a paid
raw-500 invoice. This is consistent with usage fitting inside a $5 subscription
allowance, not proof of how current/new-project credits will be applied. In
particular, the CLI's `currentBillDollars` usage figure is not the settled invoice.

The API still returns `includedUsageDollars=0`, `remainingUsageCreditBalance=1`,
`creditBalance=0`, `appliedCredits=0`. Historical credit ledger entries do not
establish the current recurring allowance. Official [plan documentation](https://docs.railway.com/pricing/plans)
still promises $5 included usage on Hobby. **The exact current credit treatment
remains unreconciled**; paid invoices strengthen account-standing evidence but do
not resolve this discrepancy. Owner must verify dashboard/upcoming invoice or
provider clarification before relying on free incremental use.

The existing 0.5-GB / 0.5-vCPU / one-replica proposal stays within observed account
ceilings. Prior local basic smoke measured ~47.2 MiB peak under a 500,000,000-byte
limit; it is not average authenticated QA memory. The **128–256 MiB / 0.005–0.05
vCPU** planning allowance remains an estimate, not hosted measurement. At current
published rates, budget **$1.5–$4/month additional resource usage**, <=1 GB egress
assumed; no sleep savings assumed. Actual added invoice cost depends on credits
and unrelated usage. Continuously saturated proposed limits are about **$15/month
compute**, plus egress/transient deploy coexistence; not a hard account spending cap.

## 5. Exact remaining owner decisions

1. **Security:** approve the exact candidate manifest/lock/diff hashes and remaining
   three moderate advisories/17 package records for private uploads-disabled QA,
   or authorize a separate compatible remediation task. Nothing has been applied.
2. **Source:** approve the exact manifest/archive and worktree-over-index choices;
   accept integrated Feature 011/012/013 QA without declaring unfinished gates passed.
   Decide whether to add post-export evidence, then re-manifest any changes.
3. **Git:** separately authorize review-clone creation and exact staging/commit/push
   actions. The current index and all original source/working files must stay intact.
4. **Vercel access:** choose the QA owner/team and authorize only that connection
   scope. Confirm team plan/create permission before any project action.
5. **Supabase access/capacity:** identify a verified Free organization and spare slot,
   including other Owner/Admin quotas. No role/plan/project changes are authorized.
6. **Railway budget:** reconcile credits; approve the proposed new separate QA
   project/region/resource limits and incremental budget. Any workspace-wide limit
   or alert change needs explicit approval and consideration of unrelated projects.
7. **Later external work, independently:** explicit resource creation, fresh QA-only
   secrets, asymmetric Auth/exact redirects/CORS/Data API disabled, reviewed EF
   migrations on verified QA primary, pinned source attachment/deploy and bounded
   hosted verification. Keep uploads/test modes off; no production credentials/DB.

Stop here. This is not permission to make any external change.
