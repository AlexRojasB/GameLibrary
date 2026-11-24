# Steam OpenID v2 — PR 5 final local validation and activation gates

2026-11-24. Branch `feat/steam-linking-v2-final-validation`, created from fetched
`origin/dev` **0d96b06cc715d67522f8e0ccb3203845264eca4f**, merge of PR #13. The
merged tree equals PR 4 head `d70751bae1a5105362ee9325e1cd88015ebeb56a`; remote feature
branch deletion was verified. Working tree was clean before creating this branch.

**Local PR 5 safety/verification deliverable; NOT activation readiness. Normal v2
remains inaccessible with hard-false shipped readiness and default-off runtime
gate. Legacy remains rejected. No push/merge/deployment/remote migration, hosted
identity/data/log access, real Steam call or CI-budget change was performed.**

## 1. Review and necessary corrections

Reviewed the approved redesign in full, D01–D11, §§3/6–13 and PR 5, authority
specifications, API admission/envelopes/proof/parser/backchannel/privacy, Core
proof/lifecycle/locking/time/cleanup/reconciliation, EF mapping/migration/grants,
Angular flow/auth/browser/storage/framing/cache and existing regression fixtures.
The Option A protocol, two-project architecture, Domain invariants and prior
retirement remain; no alternate transport/token, package, schema or worker added.

The source review found these bounded PR 5 omissions:

1. **Drain observation:** previously only request-completion/error summaries existed.
   Added process-local `SteamLinkDrainObserver`, registered before v2 admission.
   Event **1102** contains only numeric started/completed/active counts. It tracks
   whole HTTP pipelines (including denials), releases in finally on success/abort/
   exception and stores no request, URL, subject or transaction context. It is not
   a public endpoint, kill switch, distributed fence or database-transaction counter.
   Zero on one process cannot establish global quiescence.
2. **Executable, bounded pause invalidation:** added operator-only
   `tools/steam-linking/invalidate-batch.sql`, not auto-run by API/EF/CI. Explicit
   session acknowledgment is required but is **not proof** of all-replica drain.
   It locks one Library then at most 100 v2 rows in UUID order per transaction,
   READ COMMITTED with 5s/10s/15s limits, fresh post-lock classification, no skip or
   global unbounded write loop. Only unconsumed/uncancelled v2 rows are cancelled;
   expired rows included. Accounts, receipts, legacy rows and initial cancellation
   timestamps survive. SQL is exercised byte-for-byte in disposable PostgreSQL.
3. **Evidence/runbook gap and stale QA advice:** added the
   [operator/QA/rollback runbook](../deployment/steam-linking-v2-runbook.md), complete
   threat/acceptance mapping below and explicit external gate ledger. Historical
   QA grant/CORS/callback guidance and PR #8 privacy notes are marked historical,
   not executable v2 instructions. Added local mixed-host and old-schema HTTP
   safety assertions; retained earlier cleanup/commit/expiry/concurrency regressions.

**Eligibility decision:** PR 5 may finalize build eligibility only when its evidence
gates are satisfied. Hosted browser/provider/collection and global cutover evidence
are not available under this local-only authorization. Therefore `BuildReady=false`
is intentionally retained; no options/test flags can override ordinary readiness.
A future eligibility change requires separate evidence/review, distinct from the
default-off runtime activation decision. This is an explicit remaining gate, not
a claim that documentation completed activation or that a QA bypass is approved.

## 2. Evidence provenance and executed checks

- PR 4 publication [CI 45476609070](https://github.com/AlexRojasB/GameLibrary/actions/runs/45476609070)
  passed backend/postgres/frontend/e2e/native-runtime on exact `d70751b`.
- Current `origin/dev` [CI 45478147551](https://github.com/AlexRojasB/GameLibrary/actions/runs/45478147551)
  passed the same five standard jobs on exact merge `0d96b06`. That merge tree is
  identical to the PR 4 head. Reuse covers unchanged protocol/frontend/native
  components; **neither run is CI evidence for this new PR 5 commit**.
- [PR 2 report](steam-linking-v2-persistence-core-local-report.md),
  [PR 3 report](steam-linking-v2-api-protocol-local-report.md), and
  [PR 4 report](steam-linking-v2-angular-local-report.md) preserve historical results
  and failures. Current final source/test results below supersede neither missing
  hosted gates nor historical cover preparation/export instability. HMR removal
  and a green run do not conclusively establish its cause/resolution.

| Fresh local check | Result |
| --- | --- |
| Focused operational/privacy/admission regressions | **33 passed**, zero failures/skips before final mixed-host/old-schema assertions |
| Core | **353 passed**, zero failures/skips |
| Angular | **516 passed**, 39 files |
| Configuration/header/cache | **18 passed**, zero failures/skips |
| Existing CI guard unit suite | **32 passed**, no new diagnostic workload |
| Release solution build | PASS, zero warnings/errors |
| Angular production build | PASS, **692.98 kB** initial, budgets unchanged |
| Lint / TypeScript spec noEmit / EF pending-model check | PASS |
| Complete PostgreSQL/API suite | **1297 passed**, zero failures/skips; 8m56s |
| Final operational/mixed-host/old-schema assertions | **10 passed**, zero failures/skips after final Release build |
| Complete desktop/mobile E2E | **46/52 passed**, six failures, zero retries/skips; 11.0m — publication blocker |

The full 1297-case run preceded one additional mixed-host test and extension of
the old-schema HTTP assertions (test-only changes). The final 10-case selection
executes every new operational test and the extended old-schema case against the
final build; no subsequent runtime/SQL change was made. Counts are separate runs,
not a claimed single 1307-case suite.

The first build found a ternary-throw syntax error in a newly added test. Corrected
the test expression; subsequent Release build passed. No behavioral assertion,
timeout, isolation guard, rate limit or resource budget was weakened. Existing
Random Picker SCSS budget and Vite/Vitest notices remain unsuppressed.

Local logs are restricted approved temporary storage under `steam-pr5-*` in
`/private/var/folders/fd/h4rhj_wn5_71qy5q79xm8r_m0000gn/T/opencode/`.
No raw browser traces/protocol canaries are committed or published.

### Current E2E failures — not acceptance-complete

One complete run, source frozen, no concurrent builds/test suites,
`--retries=0 --workers=1 --trace=retain-on-failure`. **26/28 Steam cases passed**;
two Steam failures and four cover failures remain:

| Scenario | Project | Concrete failure / inspected evidence |
| --- | --- | --- |
| VideoGame upload/crop/replace/remove | Desktop | Editor visibility at `covers.spec.ts:254`; add dialog remained preparing |
| Escape during delayed upload | Desktop | Editor did not disappear after Use image at `covers.spec.ts:260`; editor showed preparing/disabled Use image, before delayed-upload assertions |
| Committed Create / lost response recovery | Desktop | Same export/editor-removal assertion at line 260, before receipt recovery behavior |
| Escape during delayed upload | Mobile | Same export/editor-removal assertion at line 260 |
| Two real cookie contexts / transferred URL | Desktop | 30s test timeout; snapshot showed `Checking Steam linking…`. Runner also reported truncated/invalid ZIP; no usable trace attachment/chronology survived |
| Account switch with pending create/review | Mobile | 30s timeout in `signIn` at `steam-linking.spec.ts:57`, browser `Runtime.evaluate` session closed. Snapshot remained in checking-review state |

All six available error contexts were inspected. The mobile Steam trace records
one review **200**, then another review and three frontend modules without a
completed response; this does not prove an API authorization failure or identify
the cause of the browser session closure. The desktop trace error limits diagnosis;
it must not be interpreted as evidence that cookie-transfer protection passed.

The complete frontend/E2E tree and cover source are byte-identical to current
`origin/dev`. Prior PR 4 comparisons already demonstrated sampled cover instability
on its own baseline, and the merge's standard CI is green. Those facts do **not**
prove every present failure is pre-existing or unrelated to the new observation
middleware. No security bypass, violated owner invariant or definite root cause
was established by this bounded evidence review. No cover refactor, timeout/rate/
retry increase, skipped test or rerun was used to hide the failed result. Further
targeted remediation/revalidation remains necessary before a fully validated
publication recommendation; no open-ended diagnostic campaign was started.

Artifacts: `steam-pr5-e2e.log` and `steam-pr5-e2e/` in the restricted temporary
directory above. Protocol traces remain private; only sanitized conclusions appear
here. The green API/Core/Angular suites do not replace the failed browser acceptance.

## 3. T01–T30 traceability review

Classes below are in `tests/backend/GameLibrary.IntegrationTests` unless noted.
“Local” means executable source/fixture evidence, not deployed all-replica/browser
certification. Existing passing suite evidence is reused where unchanged; new
operational tests are in `SteamLinkFinalValidationTests` (FV). Full test method
names remain discoverable in those files; no row is waived because of UI checks.

| Threat | Implementation and named regression evidence | Remaining boundary |
| --- | --- | --- |
| T01 leaked S | Core `Matches`/CheckCallback; `SteamV2EndpointTests.Leaked_state_and_forged_or_ambiguous_cookie_cannot_verify_stage_or_confirm` | Knowing S can still influence/deny service in original browser |
| T02 derived/forged B | `SteamSecureLinkRulesTests`, `SteamSecureLinkTests.Proof_state_hash_verifier_wrong_owner_and_legacy_never_substitute_for_B`; endpoint cookie variants | RNG independence checked by separate draws, not statistical proof |
| T03 B alone | Endpoint `Cookie_alone_wrong_JWT_cross_transaction_and_stolen_JWT_existing_flow_fail_but_new_flow_gets_fresh_proof` | B is not application authentication |
| T04 replay | Endpoint `Full_protocol_stages_only_then_explicitly_confirms_current_receipt_without_changing_cookie`, cross-transaction replay and parallel assertions | Real OP one-use nonce behavior requires separate interoperability evidence |
| T05 expiry | Core Library/row/unique waits, nonce post-write and late COMMIT tests; endpoint provider-wait expiry; browser expiry | DB eligibility point, not wall-clock COMMIT acknowledgment |
| T06 wrong identity | `SteamOpenIdProtocol`; endpoint invalid field/signed/nonce/expected-ID and immutable-stage tests; browser full-ID review | Numeric ID cannot attest intended account |
| T07 concurrent completion | Core `Prepared_identity_is_write_once_parallel_callbacks_and_confirmations_have_one_winner` and competing-Library test; endpoint parallel barriers | SQL uniqueness, not UI disabling, is authority |
| T08 initiation/tabs | Core replacement/parallel creations; endpoint provider-wait replacement; browser reversed-response/latest-tab coverage | Out-of-order Set-Cookie may safely force restart |
| T09 browser transfer | Endpoint proof failures and `e2e/steam-linking.spec.ts` real two-context transfer/derived-proof case | Privileged true-B theft is outside physical-browser binding |
| T10 application switch | Core owner recheck; API wrong-JWT/injected-identity tests; Angular U→W→U generation/stale initialization; browser pending-response switches | Previously admitted U request can finish for U |
| T11 Origin/CSRF | Endpoint exact-Origin, body and preflight tests; browser hostile form/GET/query cases | Non-browser Origin spoofing grants nothing without JWT+B |
| T12 cancel/unlink | Core cancel-confirm races; endpoint negative-return nonmutation, unlink-confirm shared barrier and gate-off unlink; browser explicit cancel | Cancel is never unlink/unsigned provider consent |
| T13 outage/partial verification | `SteamV2BackchannelTests`; endpoint nonce-spent/lost-response/provider-wait tests | Provider nonce can be spent externally; no automatic replay recovery |
| T14 unknown COMMIT | Core/endpoint before/after-commit injected faults for create/stage/confirm/cancel; FV interrupted batch | A transport exception does not prove rollback |
| T15 logging | `SteamLogPrivacyTests` with active unsafe-scope control; endpoint application canaries/Activity capture; FV observer error/abort private capture | Hosted edge/internal/export/agent/data collection remains OPEN |
| T16 fixed URL/retirement | `SteamLinkRetirementTests` (Core), `SteamLinkRetirementEndpointTests`, fixed JSON targets/provider navigation in browser | Every deployed old writer must separately be removed/drained |
| T17 cookie policy | Endpoint HTTPS/local cookie/Host/fixation tests; real browser refusal/clear/two contexts | Actual hosted third-party blocking/HTTPS/browser support NOT RUN |
| T18 schema/transition | `SteamSecureMigrationTests`, `SteamSecurePrivilegesTests`, `SteamRuntimePrivilegesTests`; old-schema/source rollback cases | Hosted effective grants/Data API/default privileges uninspected |
| T19 frontend tampering | `steam-link-flow.spec.ts` invalid URL/handle/storage/auth tests; browser fake flags/no metadata/no app Authorization to Steam | No XSS/privileged-extension or all-browser guarantee |
| T20 receipt lifetime | Core removed/replaced UUID and expired/purged-receipt tests; endpoint post-unlink retry; FV preserved receipt | Receipt never authorizes a new account UUID |
| T21 rollback/isolation | Shipped readiness/Production/Staging tests; `SteamV2OldSchemaTests`; schema-mismatch unlink/catalog test; isolated browser guards | Exact operational rollback binary/route inventory still required |
| T22 resources/cleanup | HTTP body/query/rate/concurrency tests, strict streamed limits; Core owner-scoped >100-row cleanup; FV bounded maintenance SQL | Per-process quotas are not distributed budgets; no cleanup SLA |
| T23 injected return | Endpoint `Same_browser_injected_identity_or_user_switch_never_links_until_correct_owner_explicitly_confirms`; browser injected ID and decline | Misleading human confirmation remains possible |
| T24 fixation/stolen JWT | Endpoint chosen/old/duplicate proof tests and stolen-JWT new-flow boundary; Core contextual verifier tests | Not MFA or stolen-session protection |
| T25 DB time/implicit waits | Core `Implicit_unique_index_wait_rechecks_transaction_deadline_and_signed_JWT`, stale-clock/late-ack/nonce tests | Fresh `clock_timestamp()` after writes, not transaction timestamp |
| T26 pause/readiness | API admitted provider pause + observer; FV create/confirm admitted writes, failed batch/resume and mixed-host zero-counter counterexample; normal flags cannot enable build | All-replica legacy/v2 quiescence, primary reconciliation and invalidation receipt remain operational blockers |
| T27 reconciliation snapshot | Core held-writer/missing-row/lock-timeout tests; FV Library wait reload after consumption | Unlocked old/missing MVCC data never settles uncertain writer |
| T28 constraints/immutability | Core NULL/half-pair/UInt64/time and every-creation-field tests; migration legacy-consumed tests; FV receipt preservation | CHECK protects shape; Core protects OLD→NEW transitions |
| T29 backchannel | `SteamV2BackchannelTests` no redirect/cookie/context forwarding, bounded decoded streams, strict UTF-8/header+body deadline/no retry; `SteamClientTests` | Infrastructure instrumentation not certified by HttpClient settings |
| T30 intent/framing | Core/endpoint confirm without review token + wrong expected-ID; Angular explicit full ID/account; browser positive/blocked cross-origin frame and fallback headers | Real hosted headers/browser/PWA behavior and human intent remain unverified |

## 4. Acceptance criteria (§13) disposition

| AC | Local disposition / evidence | Activation gate |
| --- | --- | --- |
| 1 approval/authority | D01–D11 approved baseline retained; bounded PR 5 amendments and feature scope recorded | No protocol choice delegated; deployment/activation permission separate |
| 2 fixed initial targets/retirement | Local fixed-target and unconditional tombstone/Core tests | Global legacy retirement OPEN |
| 3 independent proof | T01–T03/T24 local tests and no raw proof persistence | Hosted log/exposure audit OPEN |
| 4 strict verification | T04/T06/T13/T29 parser/provider tests | Real Steam interoperability OPEN |
| 5 callback stages/explicit confirmation | T06/T10/T23/T30 API/Core/browser tests | Hosted trusted UI evidence OPEN |
| 6 races/expiry/unknown outcomes | T05/T07–T14/T20/T25/T27 | Operational drain/reconciliation OPEN |
| 7 migration/data preservation | PR 2 additive migration unchanged; old/fresh/migrated schema tests and rollback failure-safe source behavior | Remote history/grants/rollback artifact receipts OPEN |
| 8 all threats/builds/checks | Full mapping above, local tests below, five green baseline jobs | Exact PR 5 hosted five checks NOT RUN (no push authorized); external subsets remain OPEN |
| 9 privacy/support/residual risk | Source/canary review and explicit sink limits below | Scoped provider assurance/risk decision and browser matrix OPEN |
| 10 release approval | No activation; separate gates documented | Owner release approval NOT GIVEN |
| 11 hostile direct requests | API tests know state/proof context; direct no-review expected-ID tests, JWT/Origin/waits/reordered-browser tests | No human-consent/MFA claim |
| 12 final clock/constraints/barrier | T25/T27/T28 implemented; metadata observer and exact SQL fault/restart exercise added for T26 | Full global barrier cannot be established by local simulation |
| 13 readiness/default-off | PR 5 preserves stronger hard-false hold and default-off runtime, legacy no re-enable, no new worker | Eligibility deliberately deferred until missing evidence/review |

These are not thirteen unconditional PASS labels: local implementation evidence and
external activation acceptance have different statuses. The final phase cannot
close its operational/hosted acceptance obligations inside a local-only task.

## 5. Application / infrastructure logging privacy review

Source inspection and local captures, **not new infrastructure access**:

| Layer | Observed source protection / evidence | Limit |
| --- | --- | --- |
| ASP.NET hosting | Common `Hosting.Diagnostics=None`; no raw RequestPath scope; negative control reproduces unsafe Warning-only mode | Effective hosted overrides/early parser failures not inspected |
| Steam request/error | Safe fixed-route/status/duration 1100, exception-type-only 1101; normal handled-exception DiagnosticListener suppression | Arbitrary agents/unhandled-after-start transport paths outside capture guarantee |
| Drain observer | Numeric 1102 snapshots only; no request retained/tagged; finally on error/abort | Process lifetime/restart/sampling coverage required; zero is not a barrier |
| MVC/auth | Useful warnings retained; no new body/header middleware or JWT echo | Do not enable verbose request/action diagnostics; hosted filter review pending |
| EF/Npgsql | Sensitive data logging not enabled; Production Command=Warning; no new parameter/state/identity logs | Runtime overrides/audit/slow-query collectors/backups need inspection |
| Outbound Steam | RemoveAllLoggers, no cookies/redirects/Activity propagation; constant warnings and canary tests | External HTTP instrumentation/provider internal stores unverified |
| Angular/PWA | Flow retains only tab T/U/deadline; no feature analytics/console; no private dataGroups/remote SW cache | General bootstrap error console exists; deployed analytics/agents/OS stores not audited |
| Railway/proxy/CDN/exports/support | Historical sanitized report observed legacy state-bearing start-path retention; repository app mitigation retained | No fresh log access, complete inventory, retention assurance or all-store exclusion |
| Browser/Steam URLs | Required protocol transport includes S/return_to/assertion; proof/JWT not forwarded | History/extensions/provider/edge correlation risk remains; no-store is not deletion |

No new logger/exporter, DiagnosticListener capture, broad auth/EF suppression or
infrastructure configuration was introduced. The operator script emits counts
only; its explicit acknowledgement is not security telemetry or a runtime gate.
The [runbook privacy section](../deployment/steam-linking-v2-runbook.md#4-privacyexposure-assessment-before-hosted-qa)
defines a bounded, separately authorized canary/coverage/provider-assurance receipt.
Absent, sampled, filtered or truncated visible logs cannot prove non-collection.
Activation needs supported assurance or explicit approved scoped residual risk;
this task grants neither. Existing historical records are not erased or re-read.

## 6. Activation gate ledger / operational blockers

| Gate | Current state |
| --- | --- |
| Approved protocol/Domain/local architecture | SATISFIED for the bounded local deliverable |
| Local protocol/confirmation/negative/race/owner tests | Backend/unit evidence green; current complete browser run **RED (46/52)** |
| Metadata observation and fault-tested maintenance procedure | Implemented locally; not proof of operational execution |
| Standard CI/native verification | Baseline five green; exact final PR 5 SHA pending publication authorization |
| Every old serving/restart/rollback source retired | **UNVERIFIED / operational authorization needed** |
| All admitted old/v2 writers drained and COMMITs reconciled | **NOT EXECUTED** |
| All outstanding unconsumed v2 rows invalidated under verified pause | **NOT EXECUTED remotely** |
| Hosted additive EF/schema/grants/Data API exposure | **UNINSPECTED / not applied** |
| Exact safe rollback artifact on target old/migrated schemas | Local source tests only; operational artifact receipt **pending** |
| Hosted collection/retention/agent/export/internal-support inventory | **UNVERIFIED**, risk acceptance not granted |
| Real Steam stateless verification/nonce interoperability | **NOT RUN / separately approved test identity needed** |
| Hosted browser/cross-site cookie/framing matrix | **NOT RUN**, no supported-policy bypass approved |
| Physical installed PWA/OS/context handoff | **NOT RUN**, emulation is insufficient |
| Build eligibility change, QA admission, final runtime activation | **NOT AUTHORIZED**, hard-false/default-off retained |

The unchanged combined native verifier remains **768 MiB cap / 640 MiB peak
threshold**; baseline success does not close API-only **512 MiB/G06**, historical
OOM intermittency or runtime-exit-tail limits. No further native diagnostic campaign
was run. Opportunistic retention is expiry+24h eligibility, not guaranteed deletion
for inactive Libraries. Stolen JWT can initiate its own flow; same-browser injected
ID can be mistakenly confirmed; no MFA, human-intent attestation, global revocation
or all-store privacy/global retirement guarantee is claimed.

## 7. QA rollout/rollback and publication verdict

The [runbook](../deployment/steam-linking-v2-runbook.md) supplies the release receipt,
all-replica legacy cutover, primary reconciliation, additive EF/minimum-grant plan,
bounded invalidation/restart procedure, privacy assessment, real-Steam/browser/PWA
matrix, monitoring/stop triggers and compatible gate-off rollback checklist.
Every operational checkbox remains unchecked pending its own evidence/approval.
Safe rollback retains additive schema/accounts/receipts/imports, rejects legacy,
pauses/drains/invalidates and never executes a Down migration or restores old proof.

**NOT ready to publish PR 5 as acceptance-complete:** local safety changes and
operational procedures are implemented/committed for review, but the current complete
E2E suite remains red (four cover cases and two Steam cases). All six failures are
preserved above without a conclusive root-cause claim. A bounded follow-up must
resolve/verify the browser blockers before claiming complete local acceptance.

**Activation remains NO-GO independently of those local failures.** Global retirement,
drain/invalidation, exact final CI/rollback artifact, infrastructure privacy and
hosted/real-provider/PWA evidence still require separately authorized work. A
future green local run would not automatically satisfy those gates or authorize
eligibility/runtime changes. Publication, merge, deployment, real/hosted QA and
activation are distinct decisions. This task stops after local implementation/report
commit; no push is authorized.
