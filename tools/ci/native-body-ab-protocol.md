# Buffered response body-count correction — frozen A/B protocol

User-authorized isolated implementation and hosted diagnostic experiment, 2026-11-23.
This is NOT authorization to update PR #9, merge, deploy, change required checks,
raise limits, force GC, reduce work/isolation or activate Steam v2.

## Fixed inputs and minimal correction

- Application in EVERY trial: `5483b200a8d960d9c2b99cf15e04448283059466`.
- Original instrumentation: `a7c8947ca6b96e8c5fe1cb6ebce3c2ec4d31f597`.
- Candidate harness/test commit: `1c74f42f02ed848b3072f69e462a61e85456fbfb`.
- Identical outside-controller/evidence runner: `2a8b0841abee0f2c54ae9f6c775491048123270b`.
- Controller: exact workflow checkout SHA in manifests. All overlay file SHA-256s recorded.

The only measured candidate change is one response assertion plus its 16 KiB-buffer
helper in Program.cs. It keeps GetAsync ResponseContentRead buffering, reads EVERY
byte through EOF, asserts exact length and disposes its stream; the existing response
using, privacy checks, eight readers/two uploads, gates, HTTP/assertion order, fixtures,
timeouts, native processors and Storage implementation are unchanged. Exceptions and
cancellation are not caught/suppressed. The original compares Int32 length; the helper
uses an Int64 count and the same xunit equality assertion. A cancelled buffered read
now observes its cancellation token through Stream.ReadAsync; unlike an immediate
byte-array copy, it can throw while counting. This is intentional cancellation
propagation, not a successful validation after cancellation.

The standalone regression executable references the actual internal compiled helper,
with one test-only friend-assembly declaration in the tool project. No new dependency
or application project is added. Neither the friend declaration nor test runner enters
the measured trial images: both use the original Verify.csproj and application tree.

## Predeclared trials (12 total, no retries/extensions until green)

Three fresh Ubuntu 24.04 amd64 blocks, four sequential full workloads each. Application
revision is constant. Each block has one original/candidate pair with instrumentation
and one pair with original diagnostics only. Three observations/cell, six per harness.

1. original-instrumented, candidate-instrumented, original-control, candidate-control.
2. candidate-control, original-control, candidate-instrumented, original-instrumented.
3. original-control, candidate-control, original-instrumented, candidate-instrumented.

Each adjacent pair shares its host and observer condition; variants alternate across
the entire block. Both instrumented variants receive identical phase/GC/sampler code
from a7c8947. Raw candidate is the exact same minimal patch applied to original raw
Program.cs, never instrumentation on only one side. Controls retain original metrics
and common final Docker bookkeeping; host-only NATIVE_AB_SAMPLE=0 disables sampling
without forwarding an extra environment variable into the container.

Measure total managed allocations/GC/PASS 8–9 on instrumented pairs; raw controls do
not have those metrics and must say unavailable. Quantify in-process direct diagnostic
self-cost and external sampler CPU/RSS on all instrumented trials. Observer effect on
peaks is assessed by within-source/host controls; no constant overhead is assumed.

Host hardware/kernel/cache/load may vary across blocks. Record them and prioritize
within-host paired comparisons; cache warming and GC/scheduling effects remain. Three
observations/cell do not establish rare-failure absence/equivalence. All failures count;
an inconclusive result is reported as inconclusive, not automatically extended.

## Workload, safety and evidence

Every trial runs the ENTIRE existing tools/ci/native-runtime.sh: 94 image cases,
60 native operations, missing/tampered-native gates and 18 whole-API cases if previous
gates permit. Preserve exact Noble amd64 image/native pins, 512 MiB memory AND swap,
2 CPUs, 256 PIDs, read-only filesystem, bounded tmpfs, dropped capabilities and owned
network-none PostgreSQL namespace. No application/Storage/image/required-ci changes.

First run all 12 focused body regressions through the existing network/privilege
isolation guard, after build/restore. They cover partial reads/exact length, truncation,
extra/empty body, pre/during cancellation, stream/acquisition/final-EOF failures,
misleading Content-Length, default GetAsync buffering, disposal and bounded allocation.
They perform no API/DB/provider/native work or external HTTP; an in-memory handler is
used for GetAsync. Privacy/concurrency remain dynamically checked by the full workload
and statically protected by a byte-identical Gate comparison apart from the assertion.

Use only the dedicated branch's push-triggered workflow, read-only repo permissions,
synthetic data and sanitized artifacts. Reuse native-ab.py's strict allowlisting;
unknown chatter/error bodies are counted but withheld. Regression output adds only
fixed labels/numeric cost/results. Do not upload source trees, configuration, raw logs,
environment dumps, URLs with tokens or credentials. Preserve all manifests and per-trial
logs/results with always-upload and 14-day retention; continue after target 137.
Hard runner timeout/destruction can still lose active-trial final metadata. Cgroup
teardown may precede the observer's final sample: last observed != exit-complete peak.

Primary hypothesis: eliminating 24 extra 2 MiB body copies reduces cumulative managed
allocation by roughly 48 MiB minus small fixed-buffer/helper overhead. This does NOT
predict an equal resident-peak reduction or identify the earlier fatal allocation.
Report matched allocation deltas, peaks, GC, timelines, exits/OOM and overhead for ALL
trials before deciding whether incorporation into PR #9 should be separately approved.
