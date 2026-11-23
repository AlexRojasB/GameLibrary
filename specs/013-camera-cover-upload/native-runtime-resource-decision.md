# Native-runtime resource decision — 2026-11-23

Status: architecture approved for isolated local implementation. Hosted validation,
publication, PR #9 changes, merge, deployment and Steam OpenID v2 are NOT authorized.

## Separate contracts

| Target | Memory | Memory+swap | Peak acceptance |
| --- | ---: | ---: | ---: |
| API-only initial/production resource verification | 512 MiB | Unchanged production policy | Requires separate application/hosting evidence |
| G01: 94 image cases and 60 two-pipeline operations | 512 MiB | 512 MiB | Existing gate unchanged |
| Missing/tampered native controls (both) | 512 MiB | 512 MiB | Existing fail-unavailable controls unchanged |
| Combined verifier: API + driver + fixtures + simulated Storage | 768 MiB | 768 MiB | **<=640 MiB**, including verifier teardown |
| Diagnostic comparison only | 512 / 768 / 1024 MiB | Equal to each cap | Same 640 MiB candidate threshold; never automatic escalation |

MiB is 1048576 bytes. A Docker memory+swap cap equal to memory means no additional
swap: cgroup-v2 memory.swap.max and memory.swap.current must both be zero. Validate
effective memory.max, swap limits/events, cpu.max and pids.max inside the container,
and Docker resource settings/exit/OOM state outside it. Do not trust flags alone.
Application admission, input/output bounds, codec pins and production config do not change.

## Evidence and rationale

Required run 45222361398/job 137859720672: exit 137, OOM true under 512 MiB.
The first controlled application/observer A/B (45230278339) passed 12 workloads;
maximum recorded cgroup peak 501.504 MiB. That did not prove rare-failure absence.
The corrected-harness/observer A/B (45244794203) passed 9/12; three OOMs included
one candidate without detailed observers. Two complete instrumented pairs saved
46.4–49.8 MiB cumulative allocation; peak differences were not uniform. No fatal
allocation attribution, application leak or consistent PR #9 regression is proven.

Use A-minimal (remove only the second buffered response copy) plus conditional C.
768 is a candidate, not demonstrated sufficient. 640 is an engineering threshold,
not a measured percentile: it provides 128 MiB to the hard cap, approximately 25%
over the largest recorded 509.707 MiB lower bound before an OOM. Changing the cap
may change GC behavior and peak memory; allocations are not resident bytes.
1024 is a diagnostic comparator only. Failure means stop/review, not raise a limit.

No workload split: all 94 images, 60 operations, both controls and all 18 ordered
API scenarios remain. Preserve two uploads of 5 MiB/eight reads of 2 MiB, all three
formats, exact-length/EOF/privacy assertions, real rate clocks, cancellation,
timeouts, durable Storage state, restart/disabled/shutdown/timer behavior. Do not
clear provider objects, force GC in whole-API, reduce fixtures or alter codecs.

## Fail-closed acceptance

Required acceptance has a fixed 768/640 profile, no environment/CLI resource override.
Check startup and final effective cgroup-v2 memory/swap/CPU/PID limits; require a
positive numeric peak <=640 MiB. Require max/high/oom/oom_kill/oom_group_kill and
swap high/max/fail events zero. Missing, duplicate, malformed or incomplete required
metrics fail. An outer verification boundary reads the kernel high-water mark only
after awaited workload disposal; that includes short peaks during teardown, not
just one-second samples. Any assertion/cleanup failure prevents successful final
completion. Docker wait/inspect must also report exit 0/OOM false. Runtime exit
after the last in-process reading is not claimed as a certified full-lifetime peak;
the hard cap and Docker OOM check still apply to that tail.

The host evidence collector requires the complete ordered PASS manifest, 94/0 image
results, 60 operations/two pipelines, both asset controls, final API result and final
resource record before accepting. It stores only bounded allowlisted diagnostics
and numeric status, including failures. Missing telemetry is never zero/success.
The required job remains named `native-runtime`; no skips, retries, continue-on-error,
manual success check or diagnostic result can replace it.

## Validation and approval boundaries

Local helper/unit/build/source-equivalence tests precede Linux execution. Portable
tests are not kernel/native/OOM evidence. The frozen matrix protocol defines 18
complete Linux workloads with all failures preserved. Execution needs new approval.
Review the matrix before authorizing twelve additional final-profile confirmations
(eight minimal/four detailed), publication or incorporation. No statistical SLO or
leak-free claim follows from a small finite sample or green CI.

API-only 512 MiB compliance and G06 release requirements remain unverified by this
combined-infrastructure exception. Do not change production memory policy or close
G06. Future CI remediation publication should be an independent reviewed PR to dev;
sync PR #9 only with separate approval and require normal checks on its new SHA.
Historical red checks remain recorded; branch protection is not bypassed.

## Rollback and residual risks

Use reviewed local commits, not resets of unrelated work. Revert the resource-policy
implementation and matching docs together; the response-copy optimization can be
reverted independently. Restoring 512 may restore the known OOM and is not a fix.
Keep the check blocking; never use 1024 or disable the threshold as silent rollback.
Historical diagnostic branches/protocols/artifacts remain untouched. Hardware/GC/
native allocator variation, missing fatal allocation attribution, finite coverage,
measurement overhead and the runtime-exit tail remain risks. Failed 768/640 needs
new analysis/approval, not post-hoc threshold adjustment.
