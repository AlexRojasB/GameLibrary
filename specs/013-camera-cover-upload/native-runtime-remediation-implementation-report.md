# Native-runtime remediation — local implementation report

2026-11-23. **Historical local implementation/preparation report.** Statements below
describe that phase only (at its close, no Linux matrix had executed and nothing
was pushed). The subsequent [Linux matrix report](native-runtime-linux-matrix-report.md)
and [revised resource decision](native-runtime-resource-decision.md) supersede its
pending-execution/publication and twelve-confirmation plan, not its recorded results.

## Identity and scope

- Branch: `fix/ci-native-runtime-memory-budget`.
- Worktree: `GameLibrary-Native-Remediation-2026-11-23`.
- Based on freshly fetched current `origin/dev`:
  `503766763fed33a839d37f4107dcd8d1b1a3c59c`.
- Local component commits: `61b1c31` (approved documentation), `b5b600f` (validated
  copy optimization), `af32eed` (resource enforcement), `312443f` (invalid exit-code
  guard), `b10d07b` (prepared matrix). The final follow-up commits this report,
  unknown-event rejection and publication-route clarification. Final full HEAD SHA
  is reported in the task response; nothing is pushed.
- PR #9 remains OPEN/BLOCKED at
  `5483b200a8d960d9c2b99cf15e04448283059466`; it was read, not modified.
- No application, domain, schema, migration, frontend, native codec, G01 workload
  or production resource configuration changes. No deployment, merge or Steam v2.

The complete external decision report was read before implementation. This branch
implements the owner-approved test-only A-minimal + conditional-C plan. Approval
and local results do **not** establish that 768/640 resolves the OOM or pass G06.

## Enforcement design

`NativeResourceBudget` has fixed constants: 805306368 bytes memory and
671088640 bytes inclusive peak threshold. Required CI/local whole-API tooling has
no resource-profile CLI/environment override. Docker memory+swap also equals
805306368; effective cgroup-v2 memory.swap.max and memory.swap.current must be zero.

Startup and final reads require numeric memory.max/peak, memory/swap event counters,
cpu.max = `200000 100000`, pids.max = 256, correct memory cap, no extra/used swap
and no high/max/OOM/swap-limit/failure events. Nonzero unknown event counters also
fail rather than silently ignoring new kernel limit events. Missing, duplicate,
malformed, overflowing, zero peak or decreasing final peak is rejected.

An outer await wraps the complete existing workload method. Its final kernel
high-water-mark read occurs after client, current factory, provider and database
async disposal. A current-factory owner provides exception-path cleanup without
retaining a list of old hosts or changing successful restart/stop boundaries.
Cleanup/assertion failures prevent successful final completion. A synthetic async
disposer crossing the threshold is explicitly tested. Runtime exit after the last
in-process read is not claimed as a certified full-lifetime peak; the hard cap and
Docker OOM/exit checks still cover that tail.

Runners retain their owned API container until Docker wait and allowlisted inspect
finish, then remove only that container. Docker memory/memory+swap/CPU/PID settings,
exit and OOM must agree with acceptance. Malformed wait status or a value outside
0..255 fails, avoiding shell exit-code wraparound. Exit 137 remains a failure.

The host collector requires exact ordered PASS 1–18, 94/0 images, two pipelines/
60 operations, both native negative controls, provider totals 29/31/2/5 and complete
startup/final resource records in order before accepting. It independently reports
functional coverage vs resource acceptance, stores only bounded allowlisted
numeric/fixed-label output and preserves failed/incomplete results. An artifact
upload/cleanup success cannot change a failed verification into a pass.

The body helper is byte-identical to the validated candidate helper from
`1c74f42f02ed848b3072f69e462a61e85456fbfb`: GetAsync still buffers, every byte is
counted through EOF with a 16 KiB buffer and Int64 count; exact-length/privacy/order
checks remain. Cancellation during counting propagates deliberately.

## Complete changed-file list relative to origin/dev

```text
.github/workflows/ci.yml
.github/workflows/native-budget-memory-ab.yml
docs/architecture/architecture-specification.md
specs/013-camera-cover-upload/g06-operational-verification.md
specs/013-camera-cover-upload/native-runtime-remediation-implementation-report.md
specs/013-camera-cover-upload/native-runtime-resource-decision.md
specs/013-camera-cover-upload/spec.md
tools/ci/native-budget-ab-protocol.md
tools/ci/native-budget-ab.py
tools/ci/native-runtime-evidence.py
tools/ci/run-isolated.sh
tools/ci/test_native_budget_ab.py
tools/ci/test_native_resource_budget.py
tools/cover-api-body-tests/BodyTests.csproj
tools/cover-api-body-tests/Program.cs
tools/cover-api-body-tests/README.md
tools/cover-api-body-tests/ResourceBudgetTests.cs
tools/cover-api/NativeResourceBudget.cs
tools/cover-api/Program.cs
tools/cover-api/README.md
tools/cover-api/Verify.csproj
tools/cover-api/run.sh
```

Architecture/Feature 013 distinguish API-only initial/production **512 MiB**,
combined-infrastructure candidate **768/640**, unchanged G01/negative-control
**512 MiB** and comparison-only **1024 MiB**. G06's addendum supersedes stability
interpretations of its historical pass, not the historical evidence itself.
Production/API-only compliance and remaining real-provider/device/hosting/operator
release gates remain unverified. No application dependency is added.

## Actual local validation

| Validation | Actual result |
| --- | --- |
| Native verifier/helper Release build | Passed, zero warnings/errors |
| Backend solution Release build | Passed; two unchanged xUnit2031 warnings in SteamEndpointTests.cs:1413–1414 |
| Body regressions | **12 passed**, no native/API/DB execution |
| Resource helper regressions | **51 passed**, synthetic files/async disposal |
| Python isolation/collector/matrix regressions | **32 passed**, ResourceWarning treated as error |
| Core tests at this dev base | **337 passed**, zero failed/skipped |
| Actual image test class on macOS | **94 passed**, zero failed/skipped; not Linux G01 evidence |
| Angular unit tests | **470 passed across 38 files** |
| Frontend QA configuration tests | **15 passed** |
| Frontend lint | Passed |
| Frontend production and E2E builds | Passed; production initial 680.27 kB, existing 700 kB warning budget unchanged |
| Actionlint 1.7.7 | Both changed workflows passed; optional shellcheck/pyflakes integrations unavailable/disabled |
| Shell syntax/YAML parsing/whitespace | Passed |
| Generated six-profile source compilation | All six minimal/detailed 512/768/1024 overlays compiled locally; shell syntax passed; **no workloads executed** |

The pre-existing Picker stylesheet warning remains 4.13/4.00 kB; Vitest reports
existing poolOptions deprecation and Vite native-loader/CommonJS warning. No warning
was hidden or fixed outside scope. Initial helper compilation correctly stopped
until the already-approved pinned macOS TurboJPEG asset was prepared. Initial npm
ci hit permissions in the user's shared cache; using a new session-temporary cache
resolved it without sudo, forced overwrites, cache deletion or lockfile changes.

The warmed body-allocation check observed old 2097784 vs new 16992 bytes; that
demonstrates the copy mechanism, not Linux peak savings. A normalized source
comparison against dev found identical HTTP/assertion/workload order after removing
only approved copy/budget/teardown edits; normalized SHA-256:
`b13674c5539436f5e7cc0293f0ef66d3cb8d755b1c36a1de2836ac3b2d19b998`.
`src/`, `tests/`, `tools/cover-native/` and `tools/ci/native-runtime.sh` are unchanged.
Historical diagnostic worktree HEAD `859f403c99f488559293e3ffc31831722d883a2d`
remains clean, and all 31 historical artifact file hashes were verified unchanged.

Docker daemon is unavailable on this macOS host. Real PostgreSQL integration, real
Linux cgroup/swap/teardown behavior, the 60-operation Linux resource workload, both
Linux asset controls, the 18-case Linux whole-API workload and desktop/mobile
Playwright journeys were not executed here. No mock/macOS pass substitutes for them.

## Prepared Linux validation, not executed

See `tools/ci/native-budget-ab-protocol.md` for exact order, acceptance, artifacts,
failure handling and approval interface. Three blocks each run six sequential
full-workload attempts: 512/768/1024 x minimal/detailed, totaling **18 attempts**.
Every attempt targets the entire original coverage; early failures remain failures,
not completed workloads. Retain censored peaks/totals and original exit/OOM status.

Only detached diagnostic worktrees receive budget/observer overlays. The required
768/640 acceptance code is never selected through a diagnostic option. The workflow
is manual-only, separate diagnostic check identities, contents:read, no secrets/
environments/OIDC, fail-fast false, failure-safe sanitized artifacts and rejection
of workflow reruns. Controller requires explicit approval text and exact clean SHA;
it refuses changes to protected application/native/test sources relative to base.
The checked-out SHA supplied at a later approved dispatch freezes the protocol.

Local preparation is ready for review; hosted execution still needs owner approval
and a publication/bootstrap route. GitHub must discover the new manual workflow on
its default branch before dispatch; merely pushing this branch may not make it
dispatchable. No merge/bootstrap or alternative push trigger is authorized here.
An approved native Linux runner can also use the frozen controller directly.

## Security, remaining risks and rollback

- Same two CPUs/256 PIDs/non-root/read-only/32 MiB noexec,nosuid tmpfs/cap-drop ALL/
  no-new-privileges; CI keeps external-network denial and isolated PostgreSQL
  loopback. The local named-DB runner retains its existing bridge restriction; it
  is not relabeled as network-none CI. No ports/socket/real credentials are added.
- No split, relaxed decoder, reduced fixtures, changed clock, new whole-API GC,
  provider eviction or automatic escalation. Required check is still `native-runtime`.
- GC/allocator/hardware variability, observer effects, finite corpus/sample sizes,
  the runtime-exit measurement tail and unknown fatal allocation remain risks.
  Neither 768 nor 1024 has been runtime-validated; OOM resolution is unproven.
- If 768/640 fails, stop/review. Do not raise the threshold, retry until green,
  treat 1024 as fallback, close G06 or certify API-only resources.
- Rollback via reviewed reversions: diagnostic preparation first if withdrawing C,
  then enforcement/follow-up guards and matching budget docs. The separate A-minimal
  commit may remain or be reverted independently. Preserve unrelated work and
  historical evidence; never reset other worktrees. Restoring 512 may restore OOM
  and must leave the required check blocking, not bypass branch protection.
- Publication, matrix execution, subsequent twelve confirmations, incorporation/
  PR #9 synchronization, merge and deployment remain separate approval boundaries.

**Stop after these local commits. Await approval before any hosted validation.**
