# PR #7 — PostgreSQL and test correctness investigation

## Evidence and failure grouping

Initial candidate: `83dba3a3f5b9cb669d65d36ff37c6dbba2bab8f6`.
[Run 44620703791](https://github.com/AlexRojasB/GameLibrary/actions/runs/44620703791).
The complete failed PostgreSQL job log was inspected (833 lines, no result truncation).
It reports **1026 passed, 43 failed, 0 skipped, 1069 total**.

All 43 failures are accounted for:

| Group | Failed cases | Cause |
| --- | ---: | --- |
| Cover HTTP metadata preservation | 31 | POST's tracked `createdAt` compared with a later database read |
| Family Friendly classification update | 8 | POST's tracked `createdAt` compared with PUT's database-loaded `createdAt` |
| Play Log editable-field replacement | 1 | POST's tracked `createdAt` compared with database-loaded immutable `createdAt` |
| Platform referenced by Owned/Wishlist/Interested VideoGame | 3 | Test expected SQLSTATE `23001`; PostgreSQL 17's FK trigger reports `23503` |

The 31 cover failures comprise two CAS cases, eleven malformed-intent cases, two
stale combined updates, one precommit failure, six provider failures, five malformed
body cases, two valid cover-only mutation cases and two invalid metadata cases.
Other cases could pass by chance when the clock's final tick digit was zero.

The failing timestamp is **`createdAt`, not `updatedAt`**. Neither the relevant
models nor the current backend schema/contracts contain an `updatedAt` field.
No actual persisted timestamp mutation is established by the initial assertions:
they compared values from different representation boundaries, not database snapshots
before/after the operation. Corrected exact comparisons must still pass before claiming
the covered rejected operations preserve persisted state.

## Timestamp trace

1. `BoardGameService`, `VideoGameService` and `PlayLogService` assign
   `DateTimeOffset.UtcNow`. DateTime/DateTimeOffset have 100 ns ticks; platform clock
   resolution may make submicrosecond digits visible or mask the test error.
2. EF maps `CreatedAt` to PostgreSQL `timestamp with time zone` (microsecond precision).
   These client-assigned properties are not configured as database-generated values.
3. The pinned **Npgsql 10.0.3** DateTimeOffset converter requires UTC and passes
   `value.DateTime` to `PgTimestamp.Encode`. That encoder computes
   `(ticks - postgresEpochTicks) / 10`: it **drops**, rather than rounds, extra precision.
   The PostgreSQL binary parameter is already expressed in whole microseconds.
4. SaveChanges retains the original property in the tracked .NET entity. Create's
   response projects that entity without a reload. Database reads decode whole
   microseconds back to DateTime/DateTimeOffset ticks (`microseconds * 10 + epoch`).
5. System.Text.Json preserves the supplied BCL value (up to seven fractional digits)
   and omits trailing fractional zeros. JSON deserialization is not the precision-loss
   source. Raw JSON comparison exposes the representation difference as well.

For the modern positive-epoch timestamps in these tests, a seven-tick remainder is
stored with that remainder removed; this is not seven ticks of audit mutation.
No production timestamp normalization, mapping, schema or serializer change is made.

Sources: [Npgsql precision documentation](https://www.npgsql.org/doc/types/datetime.html),
[10.0.3 DateTime converters](https://github.com/npgsql/npgsql/blob/v10.0.3/src/Npgsql/Internal/Converters/Temporal/DateTimeConverters.cs),
[10.0.3 timestamp encoding](https://github.com/npgsql/npgsql/blob/v10.0.3/src/Npgsql/Internal/Converters/Temporal/PgTimestamp.cs).

## Minimal corrections and strength

- Cover tests retain the POST response checks but take an additional fresh GET snapshot
  **before** the operation. `UnchangedMetadata` still compares every non-cover JSON
  property exactly after the operation; cover reference/status/ownership assertions
  remain. The Play Log snapshot in the cover-only scenario is also persisted before
  cover changes. There is no normalization/tolerance applied to the observed result.
- Family Friendly and Play Log immutable-field tests capture `CreatedAt` with a fresh
  no-tracking database query **before** PUT. Exact response and fresh persisted-value
  assertions remain (and Family Friendly gains a direct post-update database check).
- `TimestampPersistenceTests` adds ten deterministic cases, with remainders 0–9 ticks
  above a fixed, microsecond-aligned expected value. It exercises the real typed Npgsql
  binary parameter, EF persistence/tracking, HTTP JSON read and rejected cover update.
  Expected values are fixed beforehand, not copied from observed output. Fresh persisted
  before/after timestamps and the complete HTTP read model are compared exactly after
  rejection. These cases require real isolated PostgreSQL, not an in-memory substitute.
- Platform deletion tests require the correct exact `ForeignKeyViolation` (`23503`)
  and additionally the specific `FK_game_platforms_platforms_platform_id` constraint.
  Direct deletion must fail; user-scoped HTTP 404/409 and eventual 204 checks remain.
  This is not accepting arbitrary integrity errors. The existing application already
  handles both FK/restrict codes and is unchanged.
- The PostgreSQL job first runs the affected classes and precision regressions, then
  the complete integration suite on a separate fresh disposable container. No tests,
  assertions, isolation checks or failure exit codes are removed or bypassed.

Any mismatch between the corrected persisted snapshots is an application defect to
investigate separately, not a reason to add tolerances or rewrite expected values.

## Separate E2E test-infrastructure findings in the same cycle

Initial E2E result: **22 passed, 2 failed**, both on mobile; backend, frontend and
native-runtime jobs passed. The first failure reads a browser response body after
navigation invalidated its CDP resource. The test now buffers the **real** server
JSON through `route.fetch()` before delivering it to the browser, preserving all
status, request-contract and response assertions. It does not fabricate a response.

The second failure receives a real 429 in a cancellation scenario. Desktop and mobile
previously shared one API process, the deterministic E2E user and its real upload rate
limiter. Resetting seeded database data does not reset that in-memory limiter, and
retries amplify the interference. Each project now runs with its own fresh API,
Angular and PostgreSQL namespace/container. Both projects and the production limiter
remain enabled and unchanged. This is test isolation, not relaxed limits or extra sleeps.

## Validation and safety boundary

Local Release build and existing Core/no-database regressions are available on macOS.
Docker's daemon is unavailable, so PostgreSQL/actual browser/native execution must be
validated by the authorized GitHub PR run. New focused cases and the complete rerun
are **pending**, not claimed passed by this investigation document.

Correction preparation actually ran: Release solution build (PASS, two unchanged
xUnit2031 warnings), Core **337 passed**, selected no-database regressions **52 passed**,
guard decision units **11 passed**, actionlint (PASS), and Playwright discovery
**24 cases**, not browser execution. A supplemental standalone E2E `tsc` invocation
could not start type checking because the existing dependencies do not provide a
resolvable `@types/node`; no dependency or type stub was added to hide that limitation.
This is not a configured workflow check. Actual Playwright execution remains required.

All five workflow jobs remain. Runtime namespace/privilege/environment guards, pinned
images/actions, fake providers and read-only token permissions are unchanged. Only
synthetic data in invocation-owned disposable containers may be migrated/reset by
existing test fixtures. No QA/production database or real Steam account is eligible.
No cloud configuration, branch protection, merge or deployment is authorized.

Steam OpenID state-path logging remains an **OPEN release blocker**, independent of
these test fixes. No raw state/token values are included in this evidence.
