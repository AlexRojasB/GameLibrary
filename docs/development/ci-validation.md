# CI validation — PR validation

## Status and scope

2026-11-19. Prepared on `feature/ci-validation`, from
`6698729945b854652ab3825bbcfaa7bb93d1a694` (`origin/dev`, PR #6 merged).
The owner separately authorized committing/publishing this branch, a PR to `dev`,
and the first CI validation cycle, including CI/test-configuration fixes only.
Hosted results are pending and must not be inferred from the local results below.
No merge, branch-protection change, cloud configuration change or deployment is
authorized. Existing worktrees and real user data are untouched. There is no
application/domain/schema change.

The workflow uses the existing tests, fixtures and native harnesses. The only change
to existing test tooling is that Playwright's CI API command uses the pre-restored
Release configuration with `--no-restore`; normal non-CI local startup is unchanged.

## Check names and commands

Workflow: `.github/workflows/ci.yml` (**CI validation**). Stable job/check names:

| Check | Commands/coverage | Timeout |
| --- | --- | --- |
| `backend` | Locked solution restore, Release build, all Core tests, API publish and byte comparison of Production logging policy | 20 min |
| `postgres` | All `GameLibrary.IntegrationTests`, including logging, JWT, isolation, relational/transaction/migration and native processor cases | 35 min |
| `frontend` | `npm ci`, `npm run test:qa-config`, `npm test -- --watch=false`, lint, Production build/budgets | 20 min |
| `e2e` | Existing Playwright `chromium-desktop` and `mobile-chrome` projects, API/Angular local servers, fake Auth/Steam/search/Storage | 35 min |
| `native-runtime` | Existing G01 inspection/smoke/resource/missing/tampered gates and bounded whole-API G06 harness | 45 min |

PRs targeting `dev`/`main` and pushes to these branches run all five jobs. There are
no path filters, silent suite exclusions, deploy steps, remote migration steps or
`pull_request_target` execution. Concurrent obsolete runs of the same ref/PR cancel.
Opening the authorized PR starts these workflows; branch pushes update that PR's run.

The `postgres` job uses one test process, respecting the existing database collections.
`SteamRuntimePrivilegesTests` requires loopback and the exact database name
`game_library_test`; lifecycle fixtures also create/drop isolated databases and roles.
Consequently the synthetic database owner has local admin rights **only in its fresh
container**. It is not a hosted runtime credential or permission recommendation.
The E2E job gets a different container/database, `game_library_e2e`, because its reset
endpoint mutates seeded test data. No shared QA/development database is eligible.

## Pins and supply chain

- Runner family: GitHub-hosted `ubuntu-24.04`, native amd64; the hosted OS image is
  maintained by GitHub, not an immutable OS snapshot. No self-hosted runner is used.
- .NET SDK **10.0.201**, Node **24.15.0**, npm **11.12.1**. Do not silently upgrade
  application package versions. `npm ci` uses the committed lock, including Playwright's
  browser revision. Restore enforces existing application NuGet locks; test/tool
  projects without committed NuGet lock files do not become fully transitive-locked
  merely because `--locked-mode` is present.
- Actions were resolved from official release tags and pinned to full commit SHAs:
  checkout **v7.0.1**, setup-dotnet **v6.1.0**, setup-node **v7.1.0**.
- PostgreSQL 17 is pinned by manifest digest in `tools/ci/postgres-image.txt`, resolved
  from the official `library/postgres:17` image. It never follows a moving tag alone.
- Native Dockerfiles/assets remain unchanged: existing approved .NET Noble SDK/runtime
  digests, SkiaSharp and TurboJPEG artifact/binary hashes. No alternate codec/platform.
  The existing inspection stage uses Ubuntu package repositories for inspection tools;
  this workflow does not claim a completely hermetic/reproducible image build.
- Installs/restores/Docker builds need public package/asset registries. They run before
  runtime isolation, without application secrets. No shared dependency cache or artifact
  upload action is added; Playwright traces/test logs are not published as artifacts.

## Runtime safety boundary

`tools/ci/run-isolated.sh` is Linux-amd64/non-root only and fails closed. It creates
only its own PostgreSQL container from the pinned image, with `--network none`, no
published ports, disposable tmpfs data and a fixed **synthetic CI-only password**.
After TCP readiness, it joins only that container's network namespace using sudo,
then drops root, changes the primary group to `nogroup` (some runners use Docker as
their primary group), clears supplementary groups, all capabilities and privilege
escalation. Database-free commands use a fresh empty network namespace.

`assert-isolated.py` verifies loopback-only networking, kernel privilege flags and
lack of access to known Docker sockets,
and rejects unexpected connectivity using documentation-only IP addresses. The test
process receives an explicit environment allowlist with synthetic loopback connection
strings, not inherited application variables, tokens, proxy settings or real provider
credentials. MSBuild server/node reuse and shared compilation are disabled both during
setup and in the isolated environment, so commands do not delegate work to a previously
started compiler/build server outside the namespace. Native API verification instead
runs the existing non-root read-only
harness container in the isolated PostgreSQL network namespace. Its limits remain
512 MiB/no extra swap, two CPUs, bounded tmpfs and no external network.

Only loopback API, Angular, PostgreSQL and protocol fixtures are reachable during
tests. Live Steam, Auth, Storage, QA and production are unreachable. Provider behavior
must be supplied by the existing deterministic fakes. Checkout does not persist Git
credentials; workflow token permissions are `contents: read`, without OIDC, environments
or secrets references. Cleanup stops only the container ID created by that invocation;
Docker `--rm` removes that disposable container. No existing resource/worktree is deleted.

This is a guard for executing the reviewed suites, **not a sandbox against malicious
workflow changes**: PR code can change the scripts/workflow, and dependency installs
and builds have internet access on an ephemeral hosted runner. Never introduce live
credentials, environment secrets, self-hosted runners or privileged external workflows.
Review workflow changes explicitly and require approval of fork workflows as appropriate.

## Resource requirements (estimates, not measured GitHub timings)

Up to five hosted Linux amd64 jobs may run concurrently, subject to repository quotas.
Plan for at least two CPUs and 7 GiB RAM per job; native builds benefit from four CPUs,
14+ GiB free Docker disk and cgroup v2. Public registries must be reachable during setup.
PostgreSQL is capped at 1 GiB memory/two CPUs/256 PIDs and a 1 GiB tmpfs; it is outside
the API's independent 512 MiB runtime budget. Each E2E project is run by the existing
single Playwright worker, not against a deployed URL. The G06 harness includes real
minute-window waits and an internal eight-minute cancellation budget. Job timeouts
above include cold downloads/builds; confirm actual duration/disk on the first approved
hosted run before reducing budgets or selecting larger/paid runners.

## Actual local verification

Local environment: macOS x64, .NET SDK 10.0.201, Node 24.15.0, npm 11.12.1.
The Docker CLI exists but its daemon socket is unavailable. No Docker service was
started and no database was contacted. Results below are direct local runs, **not**
successful GitHub Actions or Linux-isolation evidence:

- Full-solution locked restore and Release build: PASS. Two pre-existing xUnit2031
  warnings remain in untouched Steam endpoint tests.
- Core unit tests: **337 passed**.
- Focused database-free logging/cleanup-composition/fake-HTTP Steam tests: **52 passed**.
- Angular unit tests: **470 passed across 38 files**. Existing Vitest/Vite config
  deprecation warnings remain; no unrelated configuration refactor was made.
- QA build configuration tests: **15 passed**.
- Angular lint: PASS. Production build: PASS; existing random-picker style warning
  exceeds the warning budget by 130 bytes (4.13 kB), not the error budget.
- Local API publish: PASS; Production policy byte comparison is checked separately.
- Playwright `--list`: **24 cases discovered**, not executed, across desktop/mobile.
- actionlint **1.7.12**: PASS on the workflow (downloaded release asset SHA-256 verified).
- Isolation decision unit tests: **11 passed** (mocked OS/socket inputs, not kernel
  isolation evidence). Six invalid-input/unsupported-platform invocations reject
  without executing the supplied command or contacting Docker.

Final shell/Python syntax, whitespace/link/safety checks and their outcomes are recorded
in the task handoff. The first npm install encountered permissions in the existing global
cache; a separate approved temporary cache was used without changing that cache's
ownership or deleting files. The development-only native asset was copied into the new
worktree's ignored obj directory only after its pinned SHA-256 matched.

### Unexecuted suites and safe next verification

**Not run locally:** complete PostgreSQL integration suite, actual Playwright scenarios,
Linux namespace/privilege execution, G01 native runtime and G06 whole-API verification.
They require a Linux amd64 host with Docker, cgroup v2 and passwordless sudo for
`unshare`, `nsenter` and `setpriv`; macOS without Docker cannot establish these gates.

All suites are wired without skipping them in CI. Their success is **not claimed**.
The safe alternative is an owner-approved Linux disposable VM or the first authorized
GitHub PR run with no secrets. A failed isolation/resource precondition must fail the
check, never fall back to a shared database, external networking or an alternate codec.
Fix CI tooling under review if the hosted runner cannot support a gate; do not waive it.

Passing CI does not prove real hosted Auth/Storage permissions, physical-camera/PWA
review, hosting shutdown/ingress behavior or production readiness. In particular,
**Steam OpenID `state` retention in provider access-log paths remains an OPEN release
blocker** until a separately approved mitigation is implemented and verified. Do not
include raw token/state values in evidence. See the [logging investigation](../deployment/logging-investigation-2026-11-19.md).

## Branch protection — proposal only, not applied

After separately approved publication and actual successful GitHub runs:

1. Verify the exact GitHub check contexts for `backend`, `postgres`, `frontend`, `e2e`
   and `native-runtime`; require all five on `dev` and `main`, from GitHub Actions.
2. Require PRs, resolved conversations and checks against an up-to-date base. No force
   pushes/deletions. Do not require linear history, since approved promotion preserves
   existing merge history. No path skips or success-only aggregation hiding failures.
3. Dismiss stale approvals/require approval of new changes where supported. Agree the
   owner-review/bypass policy explicitly: a single developer cannot approve their own
   PR. Do not silently install an impossible reviewer rule or grant a broad bypass.
4. Recheck permissions/settings with the owner and demonstrate a failing check blocks
   merging before claiming enforcement. Restrict administrative bypass per that approval.
5. Merge and deployment approvals remain distinct. This workflow cannot deploy and
   must not be extended to do so without a separate approval and trigger inventory.

Next approvals after CI validation: PR merge and, separately, branch-protection
configuration. Neither CI success nor this document authorizes merging, deployments
or cloud writes.
