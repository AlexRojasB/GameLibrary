# Frozen native budget matrix — prepared, NOT executed

2026-11-23. Approved for local preparation only. A future explicit authorization
must identify the exact local remediation commit SHA and approve hosted execution.
No push, dispatch, rerun, PR #9 update, merge, deployment or Steam v2 is authorized.

## Fixed source and profiles

Application base is current origin/dev at preparation:
`503766763fed33a839d37f4107dcd8d1b1a3c59c`, not PR #9's application revision.
The future workflow requires the exact 40-character remediation SHA as an input;
its controller rejects a different/dirty checkout and changes to src/, tests/ or
tools/cover-native/ relative to that base. Record source SHA, source/overlay hashes,
image pins, runtime, kernel, CPU/hypervisor, Docker/cgroup version and timings.
The source SHA supplied at approved dispatch freezes all controller/protocol code.

Only disposable detached worktrees receive these allowlisted overlays:

- Combined cgroup memory and memory+swap = 512 / 768 / 1024 MiB, respectively.
- Expected in-process memory.max and numeric host evidence match that trial's cap.
- Mandatory candidate peak threshold remains **640 MiB in every profile**. At 512,
  the lower physical cap still applies. Above 640 at 1024 is a resource failure,
  not permission to make it an acceptance profile.
- Minimal: mandatory startup/final resource metrics and existing bounded checkpoint
  diagnostics only; no detailed GC writer or external sampler.
- Detailed: reviewed writer and host-side one-second sampler from immutable
  `a7c8947ca6b96e8c5fe1cb6ebce3c2ec4d31f597`. The writer's source is transplanted
  without changing its implementation; fixed markers bracket overlap and PASS 8/9.
  No forced GC, heap profiler, native change, extra in-container process, clock
  acceleration or timeout compensation. Report observer overhead and perturbation.

Required CI has a fixed 768/640 profile and no budget/mode CLI/environment override.
It neither imports nor calls this controller. Matrix workflow is manual-only with
a separate diagnostic job name and explicit approval phrase. It never emits a
required acceptance check or promotes a diagnostic result into one.

## Exact execution order

M = minimal, D = detailed. Each row is one Linux runner/paired block, sequential:

| Block | 1 | 2 | 3 | 4 | 5 | 6 |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | 512-M | 768-D | 1024-M | 512-D | 768-M | 1024-D |
| 2 | 1024-D | 768-M | 512-D | 1024-M | 768-D | 512-M |
| 3 | 768-M | 1024-D | 512-M | 768-D | 1024-M | 512-D |

Total **18 full workload attempts**, three per budget/observation cell. Every
attempt runs the unchanged G01 94 cases, 60 operations/two pipelines, both native
negative controls and all 18 API cases in original order. G01/control containers
retain 512 MiB even in 768/1024 combined trials. No split, retry, dropped test or
provider-state eviction. A failure may prevent completion; never call that attempt
a completed workload. Continue all remaining predeclared attempts, preserving it.
There is no automatic escalation from a failed trial to a larger-budget retry.

Same pinned Noble/glibc native amd64 .NET runtime/SDK/native assets, two CPUs,
256 PIDs, non-root, read-only root, 32 MiB noexec/nosuid tmpfs, all caps dropped,
no-new-privileges; isolated PostgreSQL loopback, no external verification network,
published ports, Docker socket, host credentials or inherited application config.
Downloads/builds occur before isolated verification as in the existing gate.

## Acceptance and failure handling

Distinguish functional coverage, resource acceptance and protocol completeness.
Require exact ordered PASS 1–18, 94/0 images, two pipelines/60 operations, both
controls, final result cases=18 and provider totals 29/31/2/5. Require positive
numeric peak <=640 MiB through verifier teardown, startup and final effective
memory.max equal to the chosen trial cap, memory.swap.max/current zero, all memory
high/max/OOM and swap high/max/fail events zero, fixed CPU/PID, exit 0/OOM false.
Missing/duplicate/malformed/incomplete metrics or final window fail closed.

Record every nonzero/137/OOM, last completed PASS, resource records, errors and
coverage_complete independently. Truncated executions are censored observations,
not comparable complete allocation totals. Detailed trials additionally require
GC/phase and external memory observations; an absent observer invalidates protocol
completion, not proof of low consumption. Kernel peak is a high-water mark; final
read follows workload disposal. Runtime exit after that read is not a certified
full-lifetime peak. Cgroup disappearance at OOM must not turn missing counters into
zero; Docker final state remains evidence and the attempt fails.

Artifacts: initial and incrementally flushed block manifest, each attempt's bounded
allowlisted evidence.log, result.json (including original process exit), overlay
SHA-256 map, evidence-log hash, source identity, environment and timing. Never save
raw build/exception/request/configuration output, token, connection string or Docker
Env. Upload even failed/incomplete blocks; if artifact creation/upload fails, report
the data gap and leave the job failed. Output paths must be outside source checkout;
existing output is not overwritten. Preserve historical protocols/branches/artifacts.
Retain artifacts fourteen days; download/archive before expiry if further review needs them.

Matrix runs may be red because 512/1024 fail; that is not a reason to retry them
or a substitute for reviewing each cell. Recommend 768 only if **all six 768 trials**
meet coverage/resources/protocol criteria. A 768 failure requires stop/review, not
threshold increase. Three observations per cell do not establish a rare-failure SLO.
After reviewing this matrix, obtain separate approval for twelve confirmations of
the final profile (eight minimal/four detailed), or for any protocol/source change.

## Prepared execution interface — do not run yet

Safe preparation: `python3 -B tools/ci/native-budget-ab.py --plan`.
The manual workflow additionally requires the approved SHA and exact phrase
`EXECUTE_FROZEN_18_WORKLOAD_MATRIX`. It checks out that SHA and gives it to the
controller with --block/--output/--expected-revision/--approval. This is an accident
guard, not a substitute for owner authorization. Six workloads per block need an
estimated ~40 minutes including cached builds; diagnostic timeout is 90 minutes,
with three blocks and fail-fast disabled. No subsequent trial set is automatic.
Workflow reruns (run_attempt >1) are rejected; any new dispatch needs a new explicit
authorization, and must retain the earlier run rather than replace its failures.

GitHub discovers a new workflow_dispatch definition from the repository's default
branch. This definition is local/unpublished: a branch push alone does not guarantee
manual dispatch availability. Before hosted validation, explicitly approve the
workflow-publication/bootstrap route (or another approved native Linux runner).
This preparation does not authorize merging the definition or adding a push trigger.
