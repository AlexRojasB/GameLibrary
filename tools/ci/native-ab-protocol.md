# Controlled hosted native-memory A/B (diagnostic branch only)

Owner-authorized experiment, 2026-11-23. This supersedes the previous local-only
execution restriction for this experiment, NOT for PR #9/release/deployment.
No changes to `.github/workflows/ci.yml`, production behavior or PR #9 are included.

## Fixed design (declare before observing results)

- Base application: `503766763fed33a839d37f4107dcd8d1b1a3c59c`.
- Head application: `5483b200a8d960d9c2b99cf15e04448283059466`.
- Full instrumentation: `a7c8947ca6b96e8c5fe1cb6ebce3c2ec4d31f597`.
- Controller: exact diagnostic-branch checkout SHA, recorded in every manifest.
- Three independent Ubuntu 24.04 amd64 runner blocks; each executes four sequential
  trials. No retries, early success stop, or exclusion of OOM results. All 12 results
  count. Three samples/cell are screening evidence, not proof of rare-failure absence.
- Order 1: base-control, base-instrumented, head-instrumented, head-control.
- Order 2: head-instrumented, head-control, base-control, base-instrumented.
- Order 3: base-instrumented, head-control, base-control, head-instrumented.

Within each block both applications/observer conditions share one runner. Host
CPU/kernel/Docker metadata are recorded; cross-block hardware may differ and is a
blocking factor, not assumed identical. Caches warm over time; varied orders reduce
but cannot eliminate order/cache/load effects. Runtime/container/native dependencies
use the existing content-pinned Noble amd64 images/assets. Each trial executes the
ENTIRE existing `tools/ci/native-runtime.sh`, including all 94 image cases, 60 native
operations, both negative-native gates, and all 18 whole-API cases if earlier gates
permit. Early failure is retained as incomplete, never bypassed to run a subset.

Every source checkout is a separate runner-owned disposable worktree. Source `src/`,
tests, codecs and assertions remain untouched. Instrumented trials get the exact same
Program.cs and sampler from a7c8947 on both revisions. The runner/evidence bookkeeping
is identical for all trials and derives from this controller. Overlay file hashes,
application SHA, instrumentation SHA and controller SHA are recorded separately.

## Observer controls

Controls retain the source revision's ORIGINAL Program.cs diagnostics. They disable
only the new external sampler using host-only `NATIVE_AB_SAMPLE=0`; no variable is
forwarded into the API container. They still record final Docker exit/OOM/image/time
metadata using the common runner. Thus "control" means original in-process metrics
plus common final bookkeeping, not literally zero measurement. Their GC timeline
and true exit peak may be unavailable; that is reported, not manufactured.

Instrumented trials enable both a7c8947 in-process diagnostics and its external
observer. This compares the combined observer effect. It does not separately identify
the external versus in-process contribution or prove an allocation-level leak.
Neither condition changes memory=512 MiB, memory+swap=512 MiB, CPU=2, pids=256,
read-only/32 MiB tmpfs, dropped capabilities, no-new-privileges, network-none synthetic
PostgreSQL or the combined one-process API/client/provider workload. The five required
PR checks and their policies are not modified.

## Sanitized evidence / failure behavior

The fixed diagnostic workflow triggers only on pushes to the dedicated diagnostic
branch; there is no pull_request, production environment, secrets binding, deployment,
OIDC or write-scoped repository token. It does not alter default-branch workflows to
enable workflow_dispatch. If repository restrictions reject execution, stop for owner
approval rather than modifying permissions/rulesets or using another service.

Controller output is allowlisted BEFORE storage/console. Artifacts contain only:
fixed PASS/test labels; numeric process/cgroup/GC/observer diagnostics; runtime and
whitelisted host metadata; source/overlay identities; trial timing/results. Unknown
build chatter, arbitrary error bodies, credentials, cookies, URLs and exception
details are counted as unretained lines. Only exception TYPE is retained. This trades
build-error detail for safe artifacts; do not treat a preparation failure as a valid
native trial. No raw log/env/config/source checkout is uploaded.

Manifests are written before execution and after each result. Target 137 is caught
by the OUTSIDE controller; every subsequent planned trial still executes. The block
returns failure if any full workload failed, and upload runs with `always()` for both
success/failure (14-day retention). Runner destruction/hard job timeout can still lose
the active trial's final status. One-second external sampling/cgroup teardown can miss
the fatal peak: distinguish last observed, final cgroup and configured limit. Never
substitute 512 MiB for a measured peak.

Analysis must compare source within matching observer conditions and observer within
matching source/block. Include all failures, missing metrics and self-cost limitations.
Do not merge the diagnostic branch, activate v2, relax gates, implement a speculative
optimization or equate a passing diagnostic trial with all PR checks passing.
