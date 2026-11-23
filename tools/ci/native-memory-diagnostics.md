# Native memory diagnostics (PR #9)

Local instrumentation authorization, 2026-11-23. No push, PR update, hosted run,
merge, deployment, resource-limit change or production-code optimization is included.

## Instrumentation

- `tools/cover-api/Program.cs`: `NATIVE_MEMORY` snapshots before setup/fixtures and
  each group, during the existing overlap checkpoint, and after every numbered PASS.
  Explicit labels bracket PASS 8 and each of PASS 9's failed read, recovered full
  read and public-bucket denial. UTC, sample IDs, process working/private/peak bytes,
  managed size, cumulative allocation, heap/committed/fragmented bytes, available GC
  budget, memory load, GC mode and collection counts are recorded. GCMemoryInfo heap
  values describe the last completed GC, not a live heap census; allocation counters
  are approximate/nonblocking. Unsupported/missing values say `unavailable`, not 0.
- Fixed test identifiers only; no request/configuration data or exception messages.
  Cgroup events are numeric allowlisted fields. No forced GC, heap dump, sampling
  thread, retained fixture reference or external call is added to the verifier.
- `tools/ci/run-isolated.sh`: starts the SAME container detached, follows its logs,
  obtains the exact exit via `docker wait`, and starts the host-side observer against
  its PID. A stop marker requests a final sample before owned-container cleanup.
  Exit 137/OOM remains a failure. No Docker socket/privilege/network policy change
  is made inside the test container. The observer runs outside its 512 MiB cgroup.
- `tools/ci/native-memory-sampler.py`: one-second cgroup-v2 snapshots, including
  current/peak/max, events and anon/file/kernel/shmem/socket/stat charges. It samples
  numeric `/proc` status/smaps_rollup only for verified members of that exact cgroup,
  capped at the existing 256-process limit with truncation marked. It never reads
  argv, environ, maps, request data or Docker configuration. No host-wide PID scan.
  PID reuse outside the target cgroup is rejected. Root/escaping cgroups are refused.
- The observer is independent of the API and can observe its death. If Docker
  destroys the cgroup immediately on exit, the final counters may be unavailable:
  the last observed peak is not necessarily the fatal peak. A one-second sample
  can miss sub-second spikes/short-lived children. In-process operation markers
  narrow the interval, but still do not identify a fatal allocation/stack.
- `NATIVE_TRIAL` gives checkout SHA, UTC, kernel and architecture; the existing
  TARGET line gives in-container runtime/OS. A comparison using an instrumentation
  overlay must record BOTH the application-source SHA and diagnostic-overlay SHA;
  checkout SHA alone must not be mislabeled the original source revision.

## Observer effect / overhead

These diagnostics are not free. In-process counters allocate temporary strings,
read small proc/cgroup files and synchronously write a log line at boundaries.
They never change the existing assertion time windows to compensate. PASS 8's
new pre-request snapshot is outside its existing Stopwatch window; PASS 9's
snapshots do not change its request/flag/assertion ordering.

Each snapshot reports cumulative **prior** diagnostic thread allocations/elapsed
ticks, and the final `NATIVE_DIAGNOSTIC_COST` includes all completed snapshots.
These counters include snapshot log writing but not the final cost-summary line
or Process.Dispose tail/entry-method JIT before its first timestamp. They are not a measurement of native/kernel memory or
GC/scheduling perturbation. A fatal run might lack the newest cost summary.

The host observer uses one stdlib Python process, sequential bounded reads (64 KiB
per file maximum), no subprocess per sample, and sleeps one second between samples.
Its closing record reports CPU time, elapsed time and host-side peak RSS (Linux KiB).
Its memory is not charged to the target, but CPU, I/O, page-cache/charge behavior
and scheduler disturbance remain possible. Capture both per-process and cgroup
charges; do not subtract unrelated RSS peaks to infer native allocation totals.

Quantify target overhead on Linux with matched **same-source** instrumented/raw
full-workload controls; local snapshot timings cannot quantify the observer's effect
on a 512 MiB Linux failure. No stability/regression claim is supported until that
comparison and the source A/B runs are actually performed.

Supplemental local formatter check (macOS/.NET 10.0.5, no API/native/DB execution):
12 direct diagnostic snapshots allocated **167088 bytes on the calling thread**
and measured **14.197 ms** total inside the writer, including first-use cost/logging.
Cgroup/private/peak counters unavailable on that host were correctly marked.
This is NOT a Linux overhead bound, an OOM reproduction, an application timeline
or a measurement of the host observer's effect. No extra GC was requested.

## Controlled comparison protocol — pending, not executed

Fixed application revisions:

- A/base: `503766763fed33a839d37f4107dcd8d1b1a3c59c`.
- B/published PR head: `5483b200a8d960d9c2b99cf15e04448283059466`.

Use the SAME reviewed instrumentation overlay on BOTH revisions; comparing an
uninstrumented base with an instrumented head confounds attribution. Use disposable
comparison worktrees without modifying existing worktrees. Preserve the original
and overlay SHA/tree identity separately in a trial manifest. Local instrumentation
commits do not mean the published PR head has changed.

On an authorized actual Linux amd64 environment, use the existing pinned Noble
images/codecs/PostgreSQL, 512 MiB memory+swap cap (no extra swap), 2 CPU, 256 PID,
read-only filesystem, bounded tmpfs, dropped capabilities and no external network
during verification. Run `bash tools/ci/native-runtime.sh` for EVERY trial, not only
the problematic subtest. Capture build-time downloads separately; keep host hardware,
Docker/kernel versions, CPU load and restore/build cache conditions equivalent.

Predeclare ten screening trials in order **A B B A A B B A A B** (five per revision),
retaining every result, including failures. Add matched raw/instrumented controls
on each revision to evaluate diagnostic overhead. Five trials per side cannot prove
rare-failure absence/equivalence: report incidence, distribution and uncertainty,
and agree additional counts before extending the experiment, not until it turns green.

For every trial record source and overlay SHA, trial order, environment/runtime,
UTC start/end and monotonic duration, exit/OOM state, last completed PASS and last
operation marker, final or last-observed memory peak with completeness flag, GC
snapshots, process/cgroup timeline, sampler availability/cost and any missing data.
Do not overwrite a failed trial or count historical unmatched CI runs as controlled A/B.

Current execution environment: macOS; Docker daemon unavailable. **Zero controlled
Linux A/B trials executed.** No new hosted run is authorized and macOS is not a
substitute. New PASS 8–9 timelines, fatal allocation attribution, diagnostic observer
effect on Linux, regression attribution and stability therefore remain pending.

## Validation and next decision

Portable tests in `test_native_memory.py` exercise parsing, sensitive-field exclusion,
exact-cgroup membership, missing counters, target death/final samples and stop markers.
Fake Docker tests verify success/137 propagation, cleanup scope and unchanged resource/
isolation flags. Existing isolation tests remain intact. These are NOT kernel/cgroup/
Docker or OOM integration tests. The verifier must also compile in Release.

Local checks for this instrumentation: **21 Python tests passed** (the original
11 isolation decisions plus 10 diagnostic tests), zero skips; shell syntax passed;
Release verifier/API/Core compilation passed with zero warnings/errors. A source
comparison confirmed all original assertions, HTTP calls and ordering, fixture
generation calls and 18 completion checks remain unchanged. Initial mock tests
needed canonical temporary paths on macOS; the fixture paths were corrected without
weakening membership/privacy assertions. Actual Linux Docker lifecycle/sampling,
OOM survival, repeated A/B and overhead controls remain unexecuted.

Do not change application buffering, native codecs, fixtures, concurrency, object
lifecycle or GC policy based only on these instrumentation changes. If both source
revisions exhibit pressure, investigate the shared harness/runtime/application
interaction without attributing it to Steam retirement. Recommend a lifetime/copy
correction only after measured roots/counters identify avoidable consumption, then
preserve all 18 whole-API cases, image/negative-native gates and the unchanged ceiling.
PR #9 remains blocked and native-runtime is NOT yet demonstrated stable.
