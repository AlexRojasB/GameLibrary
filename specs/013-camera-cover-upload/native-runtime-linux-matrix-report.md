# Native-runtime Linux matrix — completed evidence and publication decision

Execution: 2026-11-23 23:37:03 to 2026-11-24 00:16:05 UTC.
This is a durable repository summary of the independently checked matrix evidence,
not a new diagnostic protocol or permission to execute workloads.

## Current owner decision (supersedes the confirmation plan)

Approve the combined verifier's **768 MiB memory/memory+swap, no additional swap,
mandatory measured peak <=640 MiB** for an independent remediation PR to dev.
The completed matrix is sufficient to proceed with the five normal required checks,
balancing engineering confidence, development time and CI costs. **Do not execute
the twelve additional confirmations (previously proposed as eight minimal/four
detailed) or another diagnostic matrix.** The earlier GO recommendation meant
request approval, not execute; the owner's later decision replaces that next step.

Require backend, postgres, frontend, e2e and native-runtime to pass normally. A
failure must be reported specifically without automatic rerun, limit escalation,
threshold relaxation or reduced coverage/isolation. PR #9 remains untouched.
No merge, deployment, upload enablement or Steam OpenID v2 is authorized.
API-only initial/production verification remains **512 MiB**; **G06 remains PARTIAL**.
This finite evidence does not definitively eliminate intermittent OOM.

## Exact execution identity

- Repository: `AlexRojasB/GameLibrary`.
- [Run 45290366910](https://github.com/AlexRojasB/GameLibrary/actions/runs/45290366910):
  push, run_number=1, run_attempt=1, overall **FAILURE**, preserving the 512 MiB OOM.
- All 18 trials used implementation
  `9a321e6265b39976d4e37d6dacec88a7910e70c9`.
- Application base: dev `503766763fed33a839d37f4107dcd8d1b1a3c59c`,
  **not** PR #9 head `5483b200a8d960d9c2b99cf15e04448283059466`.
- Diagnostic branch: `diagnostics/native-budget-9a321e6-2026-11-23-once`.
- Launcher `e1cfc59f78dedf99a2dfcaf732d535f2f9948e38` has parent exactly 9a321e6
  and adds only `.github/workflows/native-budget-authorized-push.yml`.
  That launcher is **not** part of the remediation branch/PR.
- Detailed observation source: `a7c8947ca6b96e8c5fe1cb6ebce3c2ec4d31f597`.

The narrow first-created-push workflow checked the exact source/paternity/diff and
clean checkout, rejected attempts >1, used contents:read and no persisted checkout
credentials, secrets, environments or OIDC. All three revision guards and portable
test steps passed. There was one run only, no retries or replacement trials.
The [frozen protocol](../../tools/ci/native-budget-ab-protocol.md) remains historical
and unchanged; its original authorization status is not current launch permission.

| Block | Job | Result | Duration |
| --- | --- | --- | --- |
| 1 | [138097237007](https://github.com/AlexRojasB/GameLibrary/actions/runs/45290366910/job/138097237007) | SUCCESS | 38m58s |
| 2 | [138097237413](https://github.com/AlexRojasB/GameLibrary/actions/runs/45290366910/job/138097237413) | FAILURE | 38m39s |
| 3 | [138097237602](https://github.com/AlexRojasB/GameLibrary/actions/runs/45290366910/job/138097237602) | SUCCESS | 38m57s |

All three artifact uploads succeeded. Each block attempted its six conditions in
the frozen order below; total controller time including builds was 6940.655 s.

## All 18 trials

M=minimal, D=detailed observation. Each row uses the exact implementation SHA above.
Coverage is images/operations/negative controls/API cases completed. Exit is both
process and Docker exit (identical in every row). Events=0 means all required final
memory high/max/oom/oom_kill/oom_group_kill and swap high/max/fail counters zero,
with swap.max/current zero. ND is unknown, **not zero**. Duration includes builds.
Peaks below are rounded; aggregate maxima/ranges below retain exact MiB values.

| Block.order | Cap MiB | Mode | Exit | OOM | Final peak MiB | Coverage | Final events | Seconds | Artifact |
| --- | ---: | --- | ---: | --- | ---: | --- | --- | ---: | --- |
| 1.1 | 512 | M | 0 | false | 489.238 | 94/60/2/18 | 0 | 415.536 | A1 |
| 1.2 | 768 | D | 0 | false | 399.016 | 94/60/2/18 | 0 | 380.598 | A1 |
| 1.3 | 1024 | M | 0 | false | 546.301 | 94/60/2/18 | 0 | 380.307 | A1 |
| 1.4 | 512 | D | 0 | false | 405.887 | 94/60/2/18 | 0 | 380.761 | A1 |
| 1.5 | 768 | M | 0 | false | 531.297 | 94/60/2/18 | 0 | 380.291 | A1 |
| 1.6 | 1024 | D | 0 | false | 513.531 | 94/60/2/18 | 0 | 380.839 | A1 |
| 2.1 | 1024 | D | 0 | false | 476.422 | 94/60/2/18 | 0 | 415.392 | A2 |
| 2.2 | 768 | M | 0 | false | 500.312 | 94/60/2/18 | 0 | 380.490 | A2 |
| 2.3 | 512 | D | 0 | false | 440.012 | 94/60/2/18 | 0 | 381.293 | A2 |
| 2.4 | 1024 | M | 0 | false | 452.043 | 94/60/2/18 | 0 | 379.867 | A2 |
| 2.5 | 768 | D | 0 | false | 482.898 | 94/60/2/18 | 0 | 380.559 | A2 |
| **2.6** | **512** | **M** | **137** | **true** | **ND** | **94/60/2/12** | **ND** | **364.991** | A2 |
| 3.1 | 768 | M | 0 | false | 468.781 | 94/60/2/18 | 0 | 416.153 | A3 |
| 3.2 | 1024 | D | 0 | false | 456.730 | 94/60/2/18 | 0 | 380.731 | A3 |
| 3.3 | 512 | M | 0 | false | 458.176 | 94/60/2/18 | 0 | 380.619 | A3 |
| 3.4 | 768 | D | 0 | false | 476.566 | 94/60/2/18 | 0 | 380.770 | A3 |
| 3.5 | 1024 | M | 0 | false | 468.637 | 94/60/2/18 | 0 | 380.835 | A3 |
| 3.6 | 512 | D | 0 | false | 365.234 | 94/60/2/18 | 0 | 380.623 | A3 |

| Cap MiB | Complete | Minimal / detailed | OOM | Exact final peak range MiB |
| ---: | --- | --- | ---: | --- |
| 512 | 5/6 | 2/3 / 3/3 | 1 | 365.234375–489.23828125 (survivors only) |
| **768** | **6/6** | **3/3 / 3/3** | **0** | **399.015625–531.296875** |
| 1024 | 6/6 | 3/3 / 3/3 | 0 | 452.04296875–546.30078125 |

768's maximum leaves 108.703125 MiB to the threshold and 236.703125 MiB to the cap
in this sample. 1024 remains diagnostic-only, not a fallback or alternate required
acceptance profile. Its passing results do not justify automatic escalation.

Trial `block-2-6-512-minimal` died after PASS 12, before PASS 13/RESULT/final metrics.
Docker reported exit 137 and OOM true with memory=memorySwap=536870912, two CPUs
and 256 PIDs. The last checkpoint peak **357.636719 MiB** is an old lower bound,
not the fatal peak. Fatal allocation/stack and final event counts are unavailable.
The interval includes state/restart preparation; no specific allocation, leak or
consistent PR #9 regression is proven. Do not discard the censored failed trial
when describing 512 stability or treat its missing metrics as successful zeros.

## Resource detector and measurement boundary

- 18/18 startup records verified effective profile memory.max, swap.max/current=0
  and complete numeric fields/zero initial events. 18/18 Docker inspections retained
  exact memory/memory+swap, CPU/PID, exit and OOM status before container removal.
- 17/18 had complete post-teardown records: positive monotonic kernel peak,
  threshold=671088640, correct limits, no swap and all required events zero.
  All six 768 trials satisfied the unchanged fail-closed contract.
- The actual OOM was rejected for incomplete API cases/result, invalid final
  resources, container exit/OOM/limits and nonzero exit. Missing telemetry stayed red.
- The 32 portable tests passed in all three Linux jobs. Local-phase 12 body and
  51 resource helper regressions are separate evidence, not extra Linux trials.
  No complete trial exceeded 640: threshold-overrun rejection is covered by synthetic
  regressions, not a real overrun in this matrix.

`await workload()` completes its clients/factories/provider/database disposal
before the final kernel high-water-mark read; short spikes before that read are
included, not merely one-second samples. The runner then waits for Docker exit,
inspects state and only then removes its container. In all 17 complete trials,
post-teardown peak equaled pre-teardown peak; no observed sample exceeded that final
peak. This does not prove teardown allocated nothing or released all resident pages.

**Remaining gap:** logging/formatting, validation and .NET exit still occur after
the final read. Retaining the stopped container for inspect did not retain a
readable cgroup: all nine external final samples found counters unavailable.
**0/18 trials certify peak through complete container exit.** Docker exit/OOM
covers termination, but a brief late rise above 640 and below the hard cap could
escape the final reading and one-second sampling. Its duration is not measured.
The owner accepts this workload-plus-awaited-teardown scope for normal PR
validation. Approval and passing CI do not close the gap or certify a stronger
complete-container-lifetime contract; changing that contract needs separate review.

## Coverage, runtime and isolation

18/18 completed 94 image cases, 60 operations and both missing/tampered-asset controls;
17 completed all 18 API cases, preserving provider totals **29/31/2/5**.
Case order, all three image formats, simultaneous two 5 MiB uploads/eight 2 MiB reads,
EOF/privacy assertions, real rate clocks, cancellation/timeouts and durable
restart/disabled/shutdown/timer scenarios were unchanged. No splitting, forced GC
in whole-API, fixture reduction, object clearing or codec changes were introduced.

Observed runtime: .NET 10.0.14, Ubuntu 24.04.5 LTS, X64, non-root app. All three
hosts reported Xeon 6975P-C/KVM, kernel 7.0.0-1014-aws, Docker 28.0.4/cgroup v2,
four host CPUs. This limits hardware/kernel diversity despite three separate jobs.
GC available budget in detailed trials changed with cap: 384/576/768 MiB. Each of
nine detailed trials retained 38 snapshots, workstation GC; allocation/GC scheduling
varied. They are not heap-root/leak attribution. Detailed observers produced 3222
valid external peak samples, observed at most one member and VmSwap=0. Observer
cost/GC perturbation remains a limitation; minimal trials do not report detailed GC.

Source, overlay and Docker/native/PostgreSQL pin hashes were independently checked.
`src/`, `tests/`, `tools/cover-native/` and `tools/ci/native-runtime.sh` are unchanged
from application base. The response optimization retains GetAsync buffering and
the validated 16 KiB EOF counter, removing only the second full-body copy.
API/G01 positive retain two CPUs/256 PIDs. The API retains non-root/read-only root,
32 MiB noexec/nosuid tmpfs, cap-drop ALL/no-new-privileges, no external networking,
published ports, host Docker socket or real credentials. PostgreSQL remains a
separate disposable container with NetworkMode none checked before network sharing.

The negative controls retain their historical 512 MiB memory/memory+swap,
network-none/read-only/non-root/cap-drop/no-new-privileges commands; unlike the
positive commands they do not explicitly fix CPU/PID/tmpfs. Do not claim otherwise
or silently change their isolation. The frozen collector archives API effective
metrics but not G01's printed counters or final Docker image IDs; source/pin review
is not independently archived effective runtime counter evidence for those items.
Synthetic Auth/Storage does not establish real provider permissions or hosting fit.

## Artifact provenance and retention

Three original ZIPs and their 39 extracted files (three manifests and 18 log/result
pairs) were downloaded and checked against GitHub digests and byte-identical
extractions. Offline analysis regenerated all 18 identities/order/overlay hashes,
recomputed each saved result and retained the failure. The analyzer was also checked
against synthetic censored-OOM/overrun/post-final-spike fixtures, not extra workloads.

| Ref | Artifact | ZIP bytes | SHA-256 |
| --- | --- | ---: | --- |
| A1 | [14876905826](https://github.com/AlexRojasB/GameLibrary/actions/runs/45290366910/artifacts/14876905826) | 85725 | `359a263bc91da20c9a1fa7ebe694fe60c5ccbdfa0d127fe19a9a1d75d95c7811` |
| A2 | [14875962559](https://github.com/AlexRojasB/GameLibrary/actions/runs/45290366910/artifacts/14875962559) | 86918 | `dbc10eb028e4a425da4ecc6c80266c9763b2331f0877f72bbbb873ba2575b733` |
| A3 | [14875404372](https://github.com/AlexRojasB/GameLibrary/actions/runs/45290366910/artifacts/14875404372) | 85477 | `25afe98f35637b39a39a984c0e3a5f3378dbc4dc6f22c9c4a782469a770719a7` |

Names: `authorized-native-budget-9a321e6-block-{1,2,3}-45290366910`.
GitHub retention expires around **2026-12-08 00:16 UTC**. Local evidence directory:
`/private/var/folders/fd/h4rhj_wn5_71qy5q79xm8r_m0000gn/T/opencode/native-budget-linux-2026-11-23/`.
It contains original `archives/`, intact `artifacts/`, report.md, analysis.json,
trial-details.csv (18 rows/71 fields), artifact hashes/inventories, run identities
and reproducible offline analyze.py/test_analyzer.py/export-details.py. The historical
report's confirmation recommendation is superseded by the current decision above.
Temporary local copies and expiring GitHub artifacts are not permanent raw archives;
this repository report preserves the results/provenance, not all original bytes.

## Publication preparation

Freshly fetched origin/dev remained `503766763fed33a839d37f4107dcd8d1b1a3c59c`
and is an ancestor of the remediation branch. Publication-phase changes after
9a321e6 are documentation only; all executable implementation/workflow files retain
the matrix-tested revision. Application/native/workload paths listed above remain
byte-identical to dev. No diagnostic launcher was imported.

Publication checks passed: 32 portable Python tests, 12 body/51 resource regressions
from the existing local Release binary, actionlint for both workflows, shell syntax,
whitespace, relative document links and independent comparison of all 18 report
rows against the saved verified analysis. The earlier full local build/lint/tests
remain recorded in the historical implementation report. Local Docker is unavailable;
fresh PostgreSQL/Linux native/browser verification is deferred to the normal hosted
PR checks, not claimed from these portable checks. Required check results must be
inspected on the published SHA before acceptance; this report does not predeclare
their outcome.

## Remaining risks and release boundaries

Six passing 768 trials are finite evidence, not a future stability guarantee.
Host/cache/order correlation, native allocator/GC variation, finite adversarial
corpus, observer overhead, unknown fatal allocation and runtime-exit measurement
tail remain. No API-only 512 MiB, real Auth/JWKS/Storage, ingress/signal, monitoring,
spend/operator or physical-device release gate is certified by this combined check.
Normal required PR validation must run on the published revision; the historical
diagnostic result cannot replace required check statuses or bypass protections.
PR #9's historical failed native-runtime check remains recorded and unchanged.
