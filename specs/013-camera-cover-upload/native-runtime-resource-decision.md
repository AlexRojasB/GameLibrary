# Native-runtime resource decision — 2026-11-23

Status: 768/640 MiB approved for publication and normal required PR validation after
the completed Linux matrix. The twelve additional diagnostic confirmations are
explicitly canceled by the owner. No further diagnostic workloads, PR #9 changes,
merge, deployment or Steam OpenID v2 are authorized.

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
At the initial decision, 768 was a candidate, not demonstrated sufficient.
640 is an engineering threshold,
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
metrics fail. Nonzero unknown memory/swap event counters also fail rather than
silently ignoring a new kernel limit event. An outer verification boundary reads
the kernel high-water mark only after awaited workload disposal, including short
spikes during teardown rather than just one-second samples. Any assertion/cleanup
failure prevents successful final
completion. Docker wait/inspect must also report exit 0/OOM false. Runtime exit
after the last in-process reading is not claimed as a certified full-lifetime peak;
the hard cap and Docker OOM check still apply to that tail.

The host evidence collector requires the complete ordered PASS manifest, 94/0 image
results, 60 operations/two pipelines, both asset controls, final API result and final
resource record before accepting. It stores only bounded allowlisted diagnostics
and numeric status, including failures. Missing telemetry is never zero/success.
The required job remains named `native-runtime`; no skips, retries, continue-on-error,
manual success check or diagnostic result can replace it.

## Completed validation and revised owner approval

The authorized [18-trial Linux matrix](native-runtime-linux-matrix-report.md) ran
once at implementation SHA `9a321e6265b39976d4e37d6dacec88a7910e70c9`:
512 MiB completed 5/6 (one preserved exit-137/OOM), 768 completed 6/6 with final
peaks 399.015625–531.296875 MiB, and diagnostic-only 1024 completed 6/6. The overall
diagnostic run remains FAILURE. All six 768 trials met the unchanged 640 threshold,
effective limits, zero events, no swap, full workload and post-teardown metrics.
Local helper/unit/build/source-equivalence tests preceded Linux execution; portable
tests are not kernel/native/OOM evidence.

The owner explicitly judges this matrix sufficient to proceed and replaces the
previous twelve-confirmation plan (eight minimal/four detailed) with normal PR
validation, balancing confidence, development time and CI costs. Do NOT execute
those twelve or any additional diagnostic matrix. The frozen protocol and previous
implementation report remain historical, not instructions to launch more trials.

Publish an independent remediation PR to current dev. Require all five normal
checks: backend, postgres, frontend, e2e and native-runtime. Native acceptance is
94 image cases, 60 operations, both negative controls and all 18 ordered API cases,
peak <=640 MiB, correct effective 768 MiB/no-extra-swap configuration, zero unexpected
events, complete startup/post-teardown metrics and exit 0/OOM false. No coverage or
isolation regression. Any failure must be reported specifically: no automatic rerun,
memory escalation, threshold relaxation or weakened workload/isolation.

The owner accepts the documented measurement scope for this PR: awaited workload
and teardown, NOT the complete remaining container lifetime. All 17 complete matrix
trials had final peak equal to pre-teardown peak; no observed sample exceeded it.
After that final read, logging/validation/.NET exit still occur; external final
samples found the cgroup gone. Docker exit/OOM covers termination, but a brief
>640 MiB tail below the cap could escape detection. This gap is not closed by
approval or by green CI. No statistical SLO, leak-free claim or definitive
elimination of intermittent OOM follows from the finite sample or normal PR checks.

API-only 512 MiB compliance and G06 release requirements remain unverified by this
combined-infrastructure exception. Do not change production memory policy or close
G06. CI remediation publication is an independent reviewed PR to dev;
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
