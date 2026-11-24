# Steam OpenID v2 PR 2 — local persistence/Core report

2026-11-24 UTC. Branch `feat/steam-linking-v2-persistence-core`, based on validated
`origin/dev` `c35ba3bcf6a5e646287c1ac3ef9907670221a0dc` (merged PR #9).

**Local implementation only. Not pushed, deployed, activated or remotely migrated.
Not ready to publish as fully verified: the browser regression suite has one
unresolved failure. Global legacy retirement is NOT established.**

## Implemented boundary

- Adopted the owner-approved Option A/D01–D11 baseline through bounded
  Product/Domain/Architecture/Feature 011 amendments. Active acceptance scope:
  [linking-persistence-core.md](../../specs/011-steam-integration/linking-persistence-core.md).
- Added internal, unregistered `SteamSecureLinkService` and private caller/result
  contracts. No new API/controller/DI, frontend, provider integration or public
  handler. Existing legacy API/Core tombstones remain retired. Build readiness is
  hard false; a runtime flag cannot expose these operations.
- Creation draws independent 32-byte S/B, persists only SHA-256(S) and contextual
  SHA-256 verifier V, rejects noncanonical proof, and binds T/U/Library. Verifier
  uses network-order UUIDs, UTF-8 byte length in uint32 big endian and fixed-time
  comparison. Sensitive contracts have redacted string representations.
- Callback checks B before the future provider boundary; staging rechecks locked
  ownership/proof/deadline and writes identity/time once, never an account. The
  internal staging argument is trusted verified identity/nonce time, not an
  implemented OpenID verifier or a browser-provided verification boolean.
- Confirmation requires signed caller U/exp, B, staged identity and matching
  explicit expected SteamID; account insertion and consumption/receipt commit
  atomically. A prior review call is not a new token or prerequisite. Receipt
  retries require the same current account UUID, never recreate after unlink/relink.
- Fresh contexts, READ COMMITTED, Library→row locks and UUID-ordered replacement/
  cleanup locks; 5s lock, 10s statement, 15s transaction/cancellation budgets.
  PostgreSQL `clock_timestamp()` controls timestamps and eligibility, checked after
  locks and all writes/implicit waits, including signed JWT exp. No provider I/O
  inside SQL and no automatic write/provider retry.
- COMMIT exceptions conservatively yield `OutcomeUnknown` after a bounded new-
  primary lock-barrier observation attempt, even when observation succeeds. Explicit
  reconcile/review can inspect durable state; unlocked old Pending/missing rows do
  not prove rollback. Unknown or late creation never delivers assumed-success proof.
- Cancellation is owner/proof/JWT-bound and idempotent, including proven expired
  rows; it never unlinks. Creation replaces the prior live row. Opportunistic cleanup
  during create/cancel deletes at most 100 expiry+24h same-Library v2 rows, rechecking
  eligibility under locks. It never deletes accounts; inactive retention has no SLA.

## Migration and compatibility

`20261124041656_AddSteamSecureLinkTransactions` is one additive EF migration with
designer/snapshot: seven columns, five versioned/NULL-safe constraints, a partial
unique live-v2-Library index and expiry index. Existing required-column/expiry/hash
constraints, Library cascade and account indexes remain. Legacy rows default to
version 1, with no fake verifier/authority backfill. No account/LibraryEntry schema,
auth/storage schema, RLS, package or dependency change.

Up was exercised on fresh databases and from the previous schema with legacy
pending/consumed rows, an existing link/date, Library, game, external cover and
private notes. Runtime/browser privilege tests use synthetic roles only. V2 Core
guards creation/write-once fields; SQL CHECKs enforce shape, not OLD/NEW transitions.
Legacy tracked-row behavior is preserved. Hosted role/exposure verification is
still a separately authorized gate, not established by these disposable-role tests.
Generated SQL from `20261029134813_AddCoverUploads` to this migration was also
inspected: only ADD columns/constraints, CREATE indexes and migration-history insert;
no application data update/delete/drop. An initial script command named a nonexistent
prior migration and failed without accessing a DB; corrected to the discovered ID.

Down explicitly refuses destructive schema/receipt deletion. Rollback means retain
the additive schema and use compatible rejection-only code, never restore legacy
linking. One existing cover migration test now recreates its exclusively owned,
nonparallel disposable database before a forward historical upgrade instead of
downgrading through newer non-destructive migrations. Its assertions are unchanged.

## Executed verification

All database work used a new loopback-only disposable PostgreSQL 17 container pinned
to `tools/ci/postgres-image.txt`; new fixtures additionally require the explicit
`game_library_test` target before creating their random isolated databases. No
hosted service, real auth, real Steam assertion or user data was accessed.

| Verification | Actual result |
| --- | --- |
| Core unit suite | **353 passed**, zero failures/skips; 14 new cases |
| Reviewed focused PostgreSQL suite | **61 passed**, zero failures/skips: 59 new cases plus the two regression cases |
| Final complete PostgreSQL/API integration suite | **1159 passed**, zero failures/skips, 2m54s |
| Angular unit suite | **470 passed**, 38 files |
| QA configuration tests | **15 passed** |
| Angular lint / TypeScript spec noEmit | PASS |
| Final Release solution build | PASS, zero warnings/errors |
| Angular production build | PASS; unchanged Random Picker SCSS warning (4.13 kB vs 4 kB) |
| EF pending-model check / whitespace check | PASS |
| Desktop/mobile Playwright, 1 worker, `--retries=0` | **23 passed, 1 failed**, no test rerun |

The first focused PostgreSQL invocation timed out at 120s without test results;
cause was not established. It is not counted as passing. Subsequent explicitly
bounded focused runs passed (43, then 50, then reviewed 61 cases after added tests).

The first full integration run reported **1148 passed, 2 failed**. It exposed
unconditional EF creation-field immutability affecting a legacy test fixture and
the old cover test's downgrade assumption. The guard is now v2-only and the upgrade
test starts empty, as described above. Both cases passed the reviewed focused run;
the final complete run is recorded separately above, not substituted by retries.

The single browser failure is Chromium desktop
`covers.spec.ts:199`, “a committed Create with a lost response is recovered through
its receipt without duplicate creation”. Before any upload/Create, `editLocal` timed
out at line 254 waiting 5s for “Edit local cover”; the snapshot still showed
“Preparing image...”. Root cause is **not established**. No claim of a confirmed
flake or baseline defect; no Angular/E2E fix, timeout increase, retry, native-memory
change or diagnostic experiment was made. This run preceded the final v2-only
guard correction; E2E is not claimed green for the final tree.

Existing Vite native-config/Vitest poolOptions deprecation notices remain unsuppressed.
Logs are retained in the session's approved temporary directory as
`steam-pr2-{focused,focused2,focused3,reviewed-focused,full-postgres,full-postgres-final,frontend,e2e}.log`.

## PR 2 threat/acceptance traceability

| Matrix obligation | Local evidence and remaining boundary |
| --- | --- |
| T01–T03, T24 Core subset | Exact contextual verifier/canonical encoding/two RNG draws; leaked S/hash/V/wrong-row B, wrong U/Library/version, malformed proof and expired JWT rejected. Incoming proof is not a creation argument. Cookies/raw duplicate handling and HTTP provider-call ordering remain PR 3/4. This is not MFA: a valid stolen JWT can initiate its own flow. |
| T04, T06 Core subset | Stage-once and parallel callbacks, no account at staging; explicit expected identity and no prior-review-token requirement. Strict assertion fields/signed return_to/provider verification remain PR 3. |
| T05, T25 | Exact rules boundaries, explicit Library/row waits, implicit unique-index waits crossing deadline/JWT exp then blocker rollback, post-write nonce recheck, late creation COMMIT acknowledgment. Real DB clock, no process clock authority. |
| T07, T08 Core subset | Parallel stages/confirms/create, same/different-Library identity uniqueness, cancellation/replacement with no old-proof adoption. Browser reverse delivery/tab/cookie behavior remains PR 3/4. |
| T12 Core subset | Cancel/confirm serialized, terminal cancel no-op, staged replacement, current UUID checks after removal/replacement. Atomic unlink invalidation/provider cancel-error behavior remains PR 3; existing public unlink unchanged. |
| T14, T27 | Before/after COMMIT fault injection, no automatic replay, late proof non-delivery, old-MVCC Pending and missing uncommitted creation lock barriers, commit/rollback reload, actual 5s lock timeout→unknown. |
| T18, T28 | Fresh/previous migration, preserved v1 rows/link/game data, NULL/half-pair/UInt64/date/phase failures, partial unique index, Library cascade, synthetic least-privilege access, all seven creation fields and staged/terminal guards. No trigger-based immutability claim. |
| T20 | Same receipt retries, mismatched expected ID, expiry, removed/replacement account UUID, cleanup of old receipt without deleting current account. |
| T26 PR 2 subset | Private unregistered use cases, hard-false readiness, unchanged rejection-only public API. Full pause/drain/invalidation/mixed-replica and hosted activation evidence remains future work. |

The remaining transport/browser/logging/provider/activation obligations of T01–T30
are not discharged by private Core tests. Existing regression coverage does not
constitute acceptance of public v2.

## Review stop / unresolved gates

Local implementation and commit are authorized; push, PR creation, merge, deploy,
QA/production migration and activation are not. No native CI/workload/runtime files,
768/640 policy or API-only 512 MiB/G06 gate were changed or re-experimented with.
Previous native successes are not validation of this new SHA or proof against OOM.

Publication readiness remains **blocked on the unresolved E2E failure/review**.
Five hosted CI checks, target Noble linux/amd64 native gates, PR 3/4 integration,
hosted exposure/logging/replica checks and operational legacy-retirement/release
approval remain pending. Stop after the local commit; do not infer further authority.
