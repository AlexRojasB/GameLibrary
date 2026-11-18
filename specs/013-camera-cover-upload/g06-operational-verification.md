# G06 Operational Verification

Date: 2026-11-02.

**Bounded local whole-API verification: Passed, 18/18 scenarios.**
**G06 as a complete release gate: PARTIAL. Do not deploy or enable uploads.**

The missing local combined API resource evidence is now executed. Actual hosting,
real-provider configuration, monitoring/spend approval and operator sign-off remain
external requirements, not simulated passes. G03 effective real Storage permissions
remain unverified independently. Phase 1/2 reports retain their historical results;
this report is the current operations/regression follow-up.

## Environment And Boundaries

- Native Docker host reports `linux/x86_64`; container reports Ubuntu 24.04.5 LTS,
  .NET 10.0.14, X64, non-root `app` (base-image UID 1654).
- SDK pin: `mcr.microsoft.com/dotnet/sdk:10.0-noble@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b`.
- Runtime pin: `mcr.microsoft.com/dotnet/aspnet:10.0-noble@sha256:de0ee358cdd672f7017a003bed21e9ac9ed48a27c922a59b18175b6e82fa7fee`.
- Final tested verification image:
  `sha256:f9276dccf6d788b47495f8c804b9f0db1d1c08aad076eed3d723f8e063e66335`.
  It is a local test image, not an approved production deployment artifact.
- Normal framework-dependent linux-x64 publish, no trimming/single-file/AOT, no
  SDK or extra native/inspection packages in final runtime. Skia/TurboJPEG assets
  are compared byte-for-byte against a separate normal API publish. G01 source,
  native pins and image regressions were not edited.
- Hard memory/swap limits both 536870912 bytes, CPU quota `200000 100000` (two CPUs),
  PID limit 256, read-only root, 32 MiB tmpfs, no capabilities/no-new-privileges.
- Real Program composition, JWT middleware, authorization, admission/rates, multipart,
  controllers, native decoder, Core, EF/PostgreSQL, byte proxy and cleanup host.
  Test JWT issuer/keys replace discovery; they are NOT real Supabase identities.
- The real typed `SupabaseCoverStorage` performs actual loopback HTTP requests with
  a nonfunctional synthetic server credential. A small Kestrel protocol fixture
  supplies controlled private-bucket JSON, immutable writes, bounded streams and
  failure/cancellation behavior. It is NOT Supabase or an RLS/permission emulator.
- Client, fixture and API share the cgroup, conservatively including their overhead.
  PostgreSQL is the existing separate local Docker container, not inside the API
  memory budget. Only newly generated disposable fixture databases are mutated.
- The runner uses test password authentication with optional GSS negotiation
  disabled; the first trial disclosed missing optional libgssapi. No native package
  was added to mask it, and production Kerberos compatibility is not claimed.

Reproduce with `bash tools/cover-api/run.sh`; see
[tool instructions](../../tools/cover-api/README.md). There is no remote target,
provisioning, production enablement or application deployment in that command.

## Fixed Workload

Three batches each admit two uploads and eight reads simultaneously. Uploads use
baseline JPEG (3020130 bytes), progressive JPEG (2781404 bytes), or PNG (4156060
bytes), padded to exactly **5242880 file bytes**. The JPEG dimensions are 1632x2448
(3995136 pixels); PNG is the existing high-entropy 960x1440 fixture. Real validation
and native decode/normalization execute before the real adapter writes canonical
JPEG. The normal processor discards padding; no original/padded upload is stored.

The eight reads each request **2097152 bytes**. Their fixture is explicitly seeded
from a real uploaded JPEG with valid COM padding and matching test row byte count.
This exercises the maximum transport allocation; it is not a claim that the normal
quality-85 processor naturally produces a metadata-bearing exact-limit output.
The provider sends headers and one byte before blocking, so the API can allocate
its bounded body buffers during upload processing, rather than just wait on headers.
No production validation or quota/admission constant is altered.

The rate scenario is fixed at twenty successful tiny uploads/cancels, four extra
minute-limit probes/waits and one hourly-limit probe. All twenty Ready handles
are canceled through the API, not removed with SQL to bypass live-upload quota.
Canceled rows remain conservatively charged. Four reported 60-second waits plus
one-second margins are honored; there is no mocked rate clock or automatic replay.

The tool has an eight-minute operation cancellation budget and bounded socket/
wait/stop operations. Final execution lasted **381945 ms**. This is short, finite
verification, not an unbounded stress/soak, a capacity guarantee or universal worst
case over every possible native input/hosting configuration.

## Scenario Results

| Case | Observed result |
| --- | --- |
| 1 | Real Kestrel startup with production adapter and trusted local-only settings. |
| 2-4 | Each fixture batch admitted two 5 MiB upload pipelines and eight maximum reads; third upload/ninth read rejected 429. All admitted operations completed with valid bounded responses. |
| 5 | Client read cancellation aborted outbound body read and released proxy admission. |
| 6 | Cancellation after provider write dispatch left durable retirement, no attachment or lost locator. |
| 7 | Stalled provider read body timed out safely after **15022 ms**, API 503. |
| 8 | Stalled provider write timed out after **15600 ms**, API 503 with durable cleanup state. |
| 9 | Storage read failure and simulated public-bucket misconfiguration failed closed; restoring fixture availability preserved the active cover. |
| 10 | Real five/minute policy allowed four groups of five; after minute replenishment, twenty/hour denied the next upload with Retry-After **3600 seconds**, no provider dispatch. |
| 11 | Real Kestrel stalled chunked receive rejected 408 after **5026 ms** under its 240 B/s minimum and five-second grace. |
| 12 | Chunked receive above the minimum rate still hit the API's real 60-second body deadline: 408 after **60135 ms**. |
| 13 | API host restart resumed due cleanup; failed object deletion retained/rescheduled its irreversible tombstone. |
| 14 | Disabled-upload restart retried/purged eligible retired work and still served a current saved cover; new upload returned 503. |
| 15 | Normal `IHost.StopAsync` during a pending provider read completed in **14992 ms**, below configured 30-second shutdown budget; current attachment remained intact. Factory/client disposal was not used to simulate graceful stop. |
| 16 | Invalid trusted Storage URL disabled new upload safely while startup/metadata remained usable. |
| 17 | Production startup rejected the test Storage switch. |
| 18 | Existing real-host PeriodicTimer regression passed with only time accelerated: 15-minute schedule, retained tombstone rescheduling and active-reference protection. |

Case 18 is an existing integration regression invoked by the harness, not a new
distinct xUnit case or a fifteen-minute wall-clock wait. Real receipt/retention,
>100-row fairness/nonoverlap/60-second work budgets, state constraints and other
configuration variants are also covered by the full PostgreSQL suite below.

### Resource Measurements

| Final complete 18-case run | Value |
| --- | ---: |
| Cgroup memory.peak | **479346688 bytes (about 457 MiB)** |
| Enforced memory.max | **536870912 bytes (512 MiB)** |
| Cgroup memory.current at final sample | 478261248 bytes |
| Cgroup memory.events max / oom / oom_kill | **0 / 0 / 0** |
| Process-reported peak working set | 574758912 bytes |
| Process CPU time delta | **71387 ms** |
| Cgroup cumulative CPU usage | 72509118 microseconds |
| CPU quota | **2 CPUs** |
| CPU throttling | 46 periods, 4666258 microseconds throttled |
| Provider writes / reads / deletes / cancellations | **29 / 31 / 2 / 5** |

Process RSS and cgroup charged memory are different measurements, especially for
shared/file-backed mappings. The process peak is disclosed and is NOT described
as below 512 MiB; cgroup memory.max is the actual enforced container charge. No OOM
or memory-limit event occurred. Approximately 55 MiB charged-memory headroom in this
run is not a promise that TLS, different hosts, extra middleware or horizontal
scale fit without further testing. CPU totals include the co-located driver and
fixture and substantial deliberate timeout/rate-window waiting. They are not a
production throughput/SLO benchmark. No performance threshold was invented or
limit increased to make a failure pass.

The linked timer regression emits expected ephemeral Data Protection warnings and
an exception-handler log for an asserted retired-cover 404. These are disclosed
test-host diagnostics, not failed assertions or a persisted production key setup.

## Current Regression Results

All are actual runs after the targeted dependency updates and new security tests:

| Check | Result |
| --- | --- |
| Backend solution build | Passed, zero warnings/errors |
| Core tests | **337 passed**, zero failed/skipped |
| Complete real-PostgreSQL IntegrationTests | **994 passed**, zero failed/skipped |
| New G03 security class, included in 994 | **16 passed** |
| Focused G03/lifecycle/Storage/HTTP/config/transport run, included in 994 | **320 passed**, not an additional 320 distinct cases |
| Bounded whole-API Linux harness | **18 passed**, separate scenario counter, overlaps existing timer regression |
| Frontend tests | **465 passed across 38 files** |
| Frontend lint | Passed |
| Production and E2E builds | Passed |
| Full desktop/mobile Playwright suite | **22 passed**, 11 per viewport, no retries/skips |
| EF pending-model check | No pending model changes |
| Production npm audit | **0 vulnerabilities** |
| Latest full npm audit | **35 package records: 3 critical, 8 high, 24 moderate**, all outside production-only audit |
| Tracked and untracked whitespace / runner shell syntax | Passed |

The 994 includes all 94 image cases and the new 16, plus Steam and previous
regressions. It is not the old 978 count copied forward. The Core and integration
counts are separate; subsets/harness scenarios must not be added as distinct tests.
EF still reports tools/runtime 10.0.7/10.0.11. Frontend retains the known Picker
4.13/4 kB stylesheet warning and Vitest poolOptions deprecation. Production initial
bundle is **677.02 kB**, below unchanged 700 kB warning budget; lazy editor 57.84 kB.

## Operator Runbook And External Requirements

Before G06 can be proposed Passed, record approved, actual environment evidence:

1. **Hosting/ingress:** image digest/RID, non-root/read-only/native provenance,
   configured container memory/CPU/PID limits, replica count and instance-local
   admission/rate semantics. Align proxy body allowance with 5 MiB + 64 KiB
   (5308416 bytes); prove known/chunked oversized requests receive safe errors.
   Verify minimum data rate and proxy timeouts preserve authoritative API behavior
   and allow its bounded receive/processing/provider path. No vendor timeout or
   minimum-rate configuration has been applied/verified here.
2. **Shutdown/restarts:** actual service-manager/SIGTERM termination grace must allow
   the configured host shutdown and bounded in-flight operations, with no false
   rollback/unsafe object compensation. This task tested Host.StopAsync, not a
   vendor's signal delivery, forced-kill behavior or zero-downtime orchestration.
3. **Private Storage:** use the separate [G03 real-provider procedure](g03-storage-verification.md).
   Record real private/JPEG-only/2-MiB settings and effective browser-role denial,
   matching primary DB/Auth issuer/namespace, secret format/rotation and adapter
   responses. Store secrets privately, not in logs, URLs, Angular or this report.
   Do not silently change namespace while old locators exist.
4. **Cleanup alerts:** monitor oldest due work age, retained byte/quota accounting,
   provider/API 429/503/timeouts, deletion failure/backlog, restart recovery and
   namespace mismatch. Retried tombstones retain fixed retirement/receipt and
   remain charged until confirmed purge; downtime can extend nominal retention.
   Verify actual alert delivery and operator response, not merely logger calls.
5. **Budget/egress:** owner approves provider plan/shared quota, API-host egress,
   Storage-to-API plus API-to-browser traffic, supported spend controls/alerts and
   thresholds. Per-Library 250 MiB is not a global spending cap. No monitoring
   destination, paid plan, overage or resource was provisioned by this task.
6. **Reconciliation:** use exact recorded namespace/key and fresh primary state.
   Retry/restore the trusted provider or rotate credentials within that namespace;
   use the immutable same-handle receipt after uncertain Game writes. Never blind
   Create, delete an active reference/external URL, rewrite a locator to another
   bucket, purge ambiguous metadata or age/prefix-sweep Storage. Extremely late
   writes beyond finite retention require separately reviewed provider-API operator
   reconciliation; no zero-orphan or instantaneous erasure claim.
7. **Security tooling:** review the remaining development-tool advisory graph before
   release/CI exposure. Keep dev servers/MCP tooling off untrusted interfaces. The
   targeted Router/Zod fix and clean runtime audit do not clear build-time risks.

## G01-G07 And Release Status

| Gate | Current status |
| --- | --- |
| G01 | **Passed previously**, unchanged bounded-normalization pipeline/pins; current host image regressions and actual Linux native processing pass. |
| G02 | **Partial:** automated cropper/Angular compatibility, lazy bundles and interactions pass after security update; required physical-device touch/accessibility/memory evidence remains absent. |
| G03 | **Partial:** all scoped deterministic local checks pass; isolated real Auth/JWKS/Storage effective permissions/adapter/deployed privacy evidence not executed. |
| G04 | **Passed local persistence/recovery verification:** complete real PostgreSQL suite and EF model check pass; no lifecycle/schema change. |
| G05 | **Partial:** all 22 automated desktop/mobile journeys pass; physical iOS/Android Safari/Chrome/PWA camera/orientation/cancel/accessibility/large-photo review remains pending. |
| G06 | **Partial:** the bounded whole-API resource/operations portion now passes; actual hosting/ingress/signal/provider/monitoring/budget/operator evidence above remains pending. |
| G07 | **Partial closeout:** requested automated regression checks pass, but remaining gate evidence and development-tool security review prevent feature-wide release closeout. |

No release/deployment activity, production upload enablement, remote resource or
policy change, schema migration edit, commit, push or staging occurred. All existing
work was preserved; staged index SHA256 remains
`62092d1ed1bbe35197e650619a83a5a9eed14a4495a4223a795223ed11372439`.
