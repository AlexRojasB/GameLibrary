# Steam OpenID v2 — operator, QA, rollout and rollback runbook

2026-11-24. **Procedure only; no hosted action performed. Activation NO-GO.**
Authority: approved redesign §§7–13/14 PR 5 and
[local final-validation scope](../../specs/011-steam-integration/linking-final-validation.md).
Read the [acceptance/evidence report](../security/steam-linking-v2-final-validation-report.md).
Supersedes the pre-v2 activation/grant proposal in `steam-qa-activation.md`.

## 0. Release receipt and independent permissions

An authorized operator and reviewer must fill in this receipt privately, without
credentials, raw logs, state, proof, JWTs, callback URLs or user data:

- [ ] Approved environment/service/database/primary identity and change window;
  precise boundaries for read access, migration/grants, deployment, QA and activation.
- [ ] Exact backend/frontend commit and immutable artifact digests, target Noble
  linux/amd64 runtime, all five CI check URLs for that SHA, safe rollback artifact.
- [ ] Every serving replica, old deployment/revision, private/public/alias/custom
  domain, direct-origin route, region, restart/scale/rollback source and job/agent.
  Record build/configuration hashes and readiness, not secret values.
- [ ] Primary/failover/pooler topology and all in-doubt/prepared transactions;
  session disappearance alone is not evidence that no writer can later commit.
- [ ] Current EF history + schema/constraint/index receipts, effective runtime and
  browser/Data API grants, backup/restore verification and separate migration role.
- [ ] Effective logging/export/agent/privacy inventory and approved residual risks;
  owner-approved supported-browser/PWA matrix and synthetic-account/data scope.
- [ ] Operator responsible for each step, evidence timestamp/window, stop/rollback
  triggers and approver. A checkbox without evidence is NOT a completed gate.

This artifact deliberately retains `BuildReady=false` and default
`SecureLinkingEnabled=false`. True runtime flags cannot enable normal linking.
Before any hosted linking test, obtain a **separately reviewed eligibility change**
and an explicit QA-only admission/deployment authorization. Never set E2E auth,
provider or linking bypasses in Production/ordinary Staging. Do not turn this
checklist or a session acknowledgment into a configuration readiness bypass.

## 1. Globally retire legacy linking — source rejection is not the barrier

1. Establish the approved inventory above. Stop new legacy initiation/start/callback
   admission on **every** reachable serving path. Keep existing links/manual CRUD
   and explicit preview/import/unlink usable where possible. If this cannot be
   achieved safely, request an explicit wider maintenance window; do not promise
   linking-only availability while stopping the whole API.
2. Remove/reroute all legacy-capable replicas and disable their restart, scale-out
   and rollback sources. Do not allow a mixed rolling overlap to count as retired.
   PR #8's logging-only artifact is **not** a safe rollback binary.
3. Drain **already admitted legacy callbacks/creation**, including provider waits,
   queued/running SQL and uncertain COMMITs. A new 410 response, option flip, quiet
   logs, HTTP disconnect or termination request does not revoke old instructions.
   Old code's row→Library ordering differs from v2: resolve/stop old writers first,
   not by running a competing v2 maintenance transaction and calling it a fence.
4. Authorized primary inspection must reconcile uncertain writers after their locks
   resolve. Identify sessions/transactions privately from the approved process
   inventory; inspect transaction/lock state, not publishable raw query text. If
   outcome cannot be determined, keep admission stopped. Do not assume an unlocked
   Pending/missing row means rollback. Do not delete a legitimately committed link
   as compensation or mark legacy rows consumed to fake invalidation.
5. Verify each surviving route/replica returns constant **410** for the old POST
   `/steam/link-requests`, GET `/steam/link/start/{state}` and old callback, with
   no provider dispatch, linking write, proof issuance or input echo. Only synthetic
   non-live values may be used for endpoint probes; never replay historical state.
   Include direct origins/aliases/old tabs, not only the main frontend.
6. Sign the global retirement receipt only once **no legacy writer can still grant
   authority**. Legacy rows/cookies are deliberately unusable and may expire; never
   derive a v2 verifier from legacy state. Existing accounts/imports remain intact.

## 2. Migration and minimum-grant plan — NOT APPLIED

- Keep linking paused. Inspect actual history; do not assume the historical QA
  migration count. Apply only missing migrations through the existing EF process,
  using a reviewed script and direct primary migration-role connection. Never
  dashboard-edit tables, alter auth/storage schemas or create a second migration flow.
- PR 2's sole v2 addition is `20261124041656_AddSteamSecureLinkTransactions`, after
  `20261029134813_AddCoverUploads`. Generate/review an idempotent EF script for the
  approved actual range; include previous prerequisites if they are missing. Do not
  rerun all migrations by hand or execute a Down migration. PR 5 adds no schema.
- Verify seven v2 columns/types, five validated v2 checks, partial unique live-Library
  index, existing unique state/account indexes and existing Library cascade. Verify
  legacy pending/consumed rows remain version 1; no backfilled verifier or staged ID.
- Compare current grants to local `SteamSecureMigrationTests`/
  `SteamSecurePrivilegesTests` and `SteamRuntimePrivilegesTests`, not old grant notes.
  V2 needs existing Library SELECT/INSERT + row-lock capability; requests need
  SELECT/INSERT/DELETE (bounded owner cleanup) and column UPDATE only for
  `verified_steam_id64`, `assertion_accepted_at`, `cancelled_at`, `consumed_at`,
  `linked_account_id`. Accounts need SELECT/INSERT/DELETE; no account identity UPDATE.
  Preserve separately approved catalog/import grants. The temporary operator role
  needs SELECT and Library row-lock capability plus UPDATE(cancelled_at), not table
  ownership or superuser. Inspect inherited/PUBLIC/default privileges and Data API
  exposure too; browser roles must not gain table/verifier access. No new RLS model.
- Runtime needs no migration-history access, DDL, immutable expiry/context updates,
  blanket schema grants or new credentials in source. A missing permission/schema
  is a linking-unavailable blocker, never permission to fall back to v1.
- Verify existing-account status/import/unlink and manual data on previous and
  migrated schemas using the **exact proposed rollback artifact**. Local fixtures
  are evidence of source compatibility, not execution of every historical binary.

## 3. Pause → drain → reconcile → invalidate → verify

Required before the first eligible rollout and **every** reactivation after pause:

1. Stop admission for create/callback/review/confirm/cancel on every replica; verify
   effective `SecureLinkingEnabled=false`, build inventory and routing coverage.
   Options are sampled on admission: previously admitted writes may still commit.
2. Observe process event **1102 SteamLinkPipelineObserved**: cumulative started,
   completed and active **whole v2 HTTP pipelines**, including rejected requests.
   It starts before admission, releases in finally on success/error/abort, stores
   no request/identity and has no public diagnostics endpoint. Events 1100/1101
   retain safe completion/error diagnostics. Collect per-replica process lifetime
   and restart metadata from the approved platform inventory, not new identity tags.
3. Counters are **observation, not a distributed lock or proof**. They reset on
   restart, can be filtered/dropped, and HTTP completion does not prove all uncertain
   database work resolved. Old binaries lack this observer. Confirm every replica
   stopped admitting, every earlier granting handler finished, and all primary
   transactions/locks/COMMIT outcomes are resolved. Account for bounded provider,
   body and SQL lifetimes; merely waiting 15/30/60 seconds is not a proof. If needed,
   obtain approval to stop/reroute all relevant instances and reconcile connections.
4. Unknown v2 outcomes use a fresh primary READ COMMITTED Library→row lock barrier
   before classification. Do not log T/U/L/proof to find them. Owner-directed live
   review uses JWT+B; operator inspection requires separately authorized private
   context, not a new public receipt/SQL diagnostic route. Preserve consumed receipts
   and links committed before the barrier. An unresolved barrier keeps NO-GO.
5. Only after quiescence is established, use the reviewed
   [`tools/steam-linking/invalidate-batch.sql`](../../tools/steam-linking/invalidate-batch.sql).
   This operator-only script changes **cancelled_at only** for unconsumed,
   uncancelled v2 rows, expired rows included. Each invocation locks one Library
   then at most 100 rows in UUID order; fresh statement after the Library wait;
   READ COMMITTED, 5s lock/10s statement/15s transaction limits, no SKIP LOCKED.
   It never touches accounts, legacy rows, consumed receipts or deletes data.

   Example for a **separately approved** named primary service (NOT run here):

   ```sh
   psql 'service=OWNER_APPROVED_PRIMARY' -X -v ON_ERROR_STOP=1 -v VERBOSITY=terse \
     -c "SET gamelibrary.steam_link_quiesced = 'on'" \
     -f tools/steam-linking/invalidate-batch.sql
   ```

   The session setting is an explicit operator acknowledgment, **not an enforced
   cluster fence or target validator**. Verify the named service/role/primary against
   the signed receipt before execution; do not use an inherited/default target or
   put a connection string/password in shell history. Credentials stay in the
   approved service/secret mechanism. No automated startup or unbounded batch loop.
6. Successful script output is counts only. Database/psql errors are restricted
   operator evidence: use terse client error verbosity, do not publish raw error
   DETAIL/CONTEXT or enable database statement/parameter capture. Server-side audit/
   error collection remains part of the privacy gate; this script cannot sanitize
   a provider's independent logs. Record only affected/outstanding counts and
   success/failure. Repeat bounded
   invocations while paused until the fresh primary outstanding count is zero and
   every COMMIT is known. A failed batch rolls back; earlier committed batches remain.
   Unknown acknowledgment: keep paused, resolve its writer/locks, then safely repeat;
   no automatic account deletion or claim of rollback. Never reactivate before a
   failed/interrupted pass is completed. Preserve initial cancellation timestamps.
7. Verify no unconsumed/uncancelled v2 row remains, no old/mixed writer serves,
   accounts/import/manual data are unchanged, and all schema/configuration/artifact
   receipts match. Ordinary unlink may serialize and remove authority throughout;
   it never permits another link. Sign a dated drain/invalidation receipt.

## 4. Privacy/exposure assessment before hosted QA

- Inventory frontend analytics/console/error reports/service-worker caches; API
  effective logging providers/scopes, exception paths, EF/Npgsql settings, body/header
  middleware, HttpClient handlers; proxy/CDN/WAF, Railway edge/container/tracing,
  exports/drains/APM/auto-instrumentation, support/internal stores and retention/ACLs;
  database audit/slow query/backups/replicas and provider/browser URL records.
- Verify common Hosting.Diagnostics=None and Production EF Command=Warning remain
  effective; no override re-enables raw path scopes. Keep useful auth/provider/EF
  warnings and fixed endpoint/status/timing/error/counter signals. Disable forbidden
  capture by approved scoped controls, not by silently erasing security observability.
- Allowed transport is not telemetry permission: S occurs in the private initiation
  body, Steam URL/return_to and callback query; B only in browser cookies/API headers;
  JWT only in the existing session/API Bearer; T in private JSON/tab metadata; V/hash
  only in SQL. No raw/encoded copies in logs/scopes/traces/body capture/support exports.
- Run a separately approved bounded synthetic canary window across success, 4xx,
  5xx, redirects, encoded/unmatched targets and early parser/ingress rejection. Scan
  locally in restricted memory for raw and encoded representations; publish only
  counts, routes/classes and coverage. Do not enable body logging to test privacy.
  Record pagination, time window, sampling/loss, access scope and every sink result.
- Obtain current provider-supported documentation/assurance for non-visible/internal
  collection and retention; historical Railway UI observations and absent results
  do not prove exclusion. Either verify requirements or obtain an explicit scoped
  residual-risk approval. An absolute all-URL/all-store secrecy requirement is not
  met by standard Steam OpenID; return for design review, not state removal/proxy
  invention/query-token fallback. Historical logs are not erased by v2.

## 5. Controlled QA plan — NOT RUN

Separate authorization must name deployment/origins, identities, devices/browsers,
real-provider permission, data mutations/cleanup, window, privacy collection and
reviewer. Use a dedicated QA environment and at least two **synthetic Game Library
accounts** plus explicitly approved tester Steam identities. Never the owner's
Library, real user's link or E2E reset endpoint. A real Steam identity is not a
synthetic provider; do not claim fake success proves real interoperability.

1. With admission off, verify exact HTTPS frontend/API bases and origin allowlist,
   pinned Steam verifier, normal JWT issuer/audience, real framing/no-referrer and
   private/no-store headers on routes, SPA fallbacks, redirects and all error layers.
   Ship no E2E switches. Static PWA caching must never retain private API/image data.
2. Only after an eligibility review and QA-only opt-in approval, admit the bounded
   window. OpenID requires no Web API key; install a server-only key only if separate
   preview/import testing is authorized. No automatic import/play-log creation.
3. Record a matrix with browser/version, OS, normal/private mode, first/third-party
   cookie policy and desktop/mobile/installed context. Required candidates:
   Chromium desktop/Android, Firefox desktop/Android, WebKit/Safari macOS/iOS,
   and actual installed Android PWA/iOS home-screen app where supported. Fill each
   cell PASS/FAIL/BLOCKED/NOT RUN; emulation is not a physical-device result.
4. For each approved cell, test same-tab navigation to real Steam, exact mandatory
   assertion/signed fields and nonce behavior, return to trusted UI, **no link before
   explicit full SteamID/current-account confirmation**, then one durable link.
   Wrong-ID selection must offer cancel/restart, not auto-confirm. Store private
   evidence without exporting a HAR, callback URL, token, cookie or screenshot ID.
5. Test explicit cancellation vs unsigned provider decline/error; expiry before
   provider return and confirmation; replay/duplicate confirm; transferred URL in
   second browser; derived/missing/cleared/refused/expired proof; logout/account switch U→W→U;
   two tabs/latest-commit and reordered responses; interrupted/lost response and
   durable review. Direct API/security behaviors are already tested locally; do not
   inject faults into a shared production database for browser QA.
6. Test actual `__Host-` Secure/HttpOnly/None/Path=/ host-only deadline behavior,
   API credentialed requests and actual foreign framing protection. Blocked
   third-party cookies must **fail closed with restart guidance**, not a downgraded
   cookie/query credential. Decide supported origins/policies explicitly; if required
   cells cannot work, keep linking unavailable or seek approved same-site hosting
   design, not new infrastructure in this task.
7. On physical installed PWA, exercise OS→browser/Steam→return handoff, cold start,
   background/resume and a return in a different context. Missing tab metadata/proof
   must restart safely. Do not assume installed/browser cookie jars are shared or
   certify PWA behavior from Playwright's Pixel emulation.
8. Recheck two-user isolation and unchanged manual/linked/import data. Preview/import
   only if explicitly in scope; then verify no duplicates/overwrites/Play Log changes.
   Cleanup may unlink only approved test accounts; retain legitimate links otherwise.
   End the window with the full pause/drain/invalidation procedure and gate off.

## 6. Future rollout, monitoring and rollback

**NO-GO until every external gate in the report has evidence/approval.** A successful
CI/local run or this committed runbook is not readiness to activate.

- After separate migration/deployment permission, deploy only complete reviewed
  backend/frontend artifacts with runtime linking **off**. Verify every replica,
  exact schema/grants and rollback artifact; remove old restart sources. Do not use
  a mixed legacy/new rolling window. Existing links/import/unlink remain independent.
- Require the recorded QA/provider/browser/privacy/retirement receipts and owner
  activation approval; separately review any removal of the hard-false eligibility
  hold. The final runtime opt-in is distinct and must never be auto-set in CI/code.
- Monitor payload-free endpoint/status/latency, provider timeout/rejection counts,
  in-flight process observations, 429s, schema/admission failure counts and aggregate
  transaction phases/expiry/cleanup age. Do not label metrics with SteamID/U/L/T/IP,
  proof/state/JWT, raw URL or assertion. Retention eligibility at expiry+24h is **not**
  an inactive-Library deletion SLA; cleanup remains at most 100 owner rows on
  create/cancel. No Steam worker/cron/sync or global provider-spend guarantee.
- Stop admission immediately for privacy canary exposure, unexpected legacy success,
  mixed artifacts, malformed-assertion acceptance, pre-confirm link, cross-user data,
  unsafe cookie fallback, missing framing, persistent unknown commits/schema errors
  or unapproved resource-limit changes. Preserve restricted evidence; no raw payloads
  in incident issues. Request separate scoped incident/data-remediation authorization.
- **Rollback:** pause every v2 endpoint; drain/reconcile; finish bounded invalidation;
  deploy the verified compatible rejection-only artifact with gate off. Keep all
  legacy routes retired, additive schema, accounts, receipts and imported/manual data.
  No Down migration, state-to-proof conversion, deletion of winners or old insecure
  binary. If the safe rollback cannot be verified, stay paused and roll forward or
  request a wider maintenance window. Before any later reactivation, repeat the
  complete barrier and evidence review; never revive outstanding rows.

## Completion receipt

- [ ] Local source/tests and five CI checks on final published SHA (not baseline only).
- [ ] Exact rollback artifact exercised against approved old/migrated schema/runtime.
- [ ] Every legacy replica/writer retired and restart sources fenced operationally.
- [ ] Every earlier writer drained/reconciled; invalidation completed and verified.
- [ ] Hosted grants/exposure/collection and residual-risk decision signed.
- [ ] Real Steam and supported hosted browser/physical PWA cells have scoped evidence.
- [ ] Eligibility change and QA/production runtime opt-in separately approved.

Until then: **normal v2 unavailable, legacy retired in this binary, global retirement
unverified, activation blocked**. Do not mark these boxes from local tests alone.
