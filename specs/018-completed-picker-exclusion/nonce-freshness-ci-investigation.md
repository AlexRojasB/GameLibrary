# PR #19 — nonce freshness CI boundary investigation

Initial head: `fe0dce908cda0e1f0582f42c2cc3b77d70ea1387`. Failed PostgreSQL job:
`139531689406`, run `45707078260`, case
`SteamV2EndpointTests.Nonce_freshness_uses_database_creation_and_now_before_provider(seconds: 61)`.
Expected provider calls 0, observed 1. The original GL-10 diff changed no Steam code.
The owner explicitly authorized this focused follow-up and publication on the same PR.

## Verified condition / clocks

Security design section 8 and both production predicates (`SteamOpenIdProtocol.Fresh`
and `SteamSecureLinkRules.FreshNonce`) require all three inclusive conditions:

```text
nonce >= databaseNow - 10 minutes
nonce >= row.CreatedAt - 60 seconds
nonce <= databaseNow + 60 seconds
```

`CheckCallbackAsync` gets fresh `clock_timestamp()` from PostgreSQL after acquiring
the existing Library/row locks, checks the independent live transaction deadline,
and returns that context. The controller checks freshness before calling the
provider. Core staging checks again after provider/lock waits and after writing.
No .NET TimeProvider/frozen clock controls these checks. PostgreSQL columns are
`timestamp with time zone` (microsecond precision); nonce parsing accepts a UTC
timestamp with whole-second precision. Neither production predicate rounds the
database time down to whole seconds or adds another second of tolerance.

## Deterministic reproduction / root cause

The test used the host's moving UtcNow, added 61 seconds, then formatted away its
fractional second before sending an HTTP request. Its premise that this must still
be more than 60 seconds ahead of the *later* database clock is false.

The new fixed-time regression executes the actual API and Core validators:

```text
generation time:             2026-11-25T12:00:00.999999Z
generation time + 61 s:      2026-11-25T12:01:01.999999Z
serialized/parsed nonce:    2026-11-25T12:01:01Z
initial gap:                60.000001 seconds -> rejected
database time after +1 us:  2026-11-25T12:00:01Z
new gap:                    60 seconds exactly -> accepted by policy
```

This reproduces the boundary-sensitive test setup without sleeps, real-time luck,
or production clock substitution. It is not evidence of acceptance outside the
permitted window. The CI log does not retain its exact nonce/database timestamps;
its exact elapsed interval cannot be reconstructed. The invalid setup and its
acceptance transition are demonstrated deterministically, not merely labelled flaky.

## Exact test-only correction

- In the affected HTTP theory, read the created row and anchor negative offsets to
  its immutable CreatedAt and positive offsets to its immutable ExpiresAt. Keep
  -601/-61/+61 cases, invariant UTC formatting, zero-provider-call assertion and
  Pending-state assertion.
- For a live transaction, `databaseNow < ExpiresAt`. Even after whole-second
  formatting, `floor(ExpiresAt + 61s) > ExpiresAt + 60s > databaseNow + 60s`.
  Thus the positive fixture cannot drift into the allowed window while live.
  Negative fixtures remain strictly before CreatedAt -60s (the -601 case also
  exceeds the ten-minute age bound). No widened production tolerance is involved.
- Add nine fixed-context cases around each precise inclusive boundary, at -1/0/+1
  microsecond, using a whole-second nonce and varying CreatedAt/databaseNow. Each
  checks BOTH actual API and Core predicates and ensures the transaction is live.
  This preserves near-boundary security coverage separately from HTTP scheduling.
- Add the deterministic whole-second-formatting reproduction above.

Production source, security policy, schema, dependencies, clocks, retries, timeouts,
QA and unrelated Steam behavior are unchanged. No new migration. Scope is two test
files plus this report and the feature-spec authorization note.

## Local validation / publication boundary

1. Fixed-time reproduction and exact-boundary class: **10 PASS**.
2. Affected HTTP theory, all -601/-61/+61 cases: **3 PASS** on real PostgreSQL.
3. Focused integration filter `FullyQualifiedName~nonce`: **16 PASS**, including the
   exact cases, before-provider rejection, post-write freshness rollback, provider
   one-use/replay and controlled-provider nonce verification.
4. Existing Core exact deadline/nonce/terminal-boundary test: **1 PASS**.

The test project built successfully. Database tests used a task-owned pinned
PostgreSQL 17 container published on loopback, with the fixture's isolated disposable
databases; no hosted credentials or real Steam/provider calls. Temporary local
container/volume removed after verification. Whitespace checks passed.

Publish the focused fix to existing PR #19, then observe normal automatic CI for its
new SHA. Record final SHA/results in the PR rather than starting another source
commit solely to record CI. No manual workflow rerun, unrelated diagnostic campaign,
merge, deployment, QA change or remote migration is authorized.
