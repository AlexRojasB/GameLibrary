# PR #1 — local legacy Steam linking rejection

2026-11-21 local work date (test output includes 2026-11-22 UTC).
Branch: `fix/steam-linking-v2-retire-legacy`.
Base: `origin/dev` `503766763fed33a839d37f4107dcd8d1b1a3c59c`, including merged PR #8.

**Implemented and validated locally within the limits below. NOT DEPLOYED.
Global retirement is NOT established. Production remains blocked.**

## Authorization / preserved records

The owner subsequently authorized the local rejection artifact despite the global
cutover dependency. The original
[blocker report](steam-linking-legacy-retirement-blocker.md) remains an unchanged
historical stop receipt, not the current local implementation status. The reviewed
[v2 specification](steam-openid-linking-redesign.md) is also preserved byte-identical
to its original specification worktree. Neither document is rewritten to imply v2
approval. The active bounded acceptance scope is
[linking-retirement.md](../../specs/011-steam-integration/linking-retirement.md).

No push/PR/merge/deployment, cloud settings, real authentication, real data access,
schema/permissions change or migration was performed. All earlier worktrees remain.

## Exact enforcement and guarantees

### New binary

- API `POST /steam/link-requests`, `GET /steam/link/start/{rawState}` and
  `GET /steam/link/callback` are constant **410** tombstones. They take no action
  parameters, do not parse state/assertions/cookies/body, generate no provider/start
  URL, and never call creation or completion. ProblemDetails contains exactly type,
  title, status, detail and `code=STEAM_LINKING_RETIRED`; no request-specific instance,
  account data, state, cookie, URL, signature or provider result is returned.
- Public anonymous 410 is intentional: these are no longer authorization/login
  operations and reveal no private resource. Status, preview/import, unlink and all
  unrelated private endpoints still require their existing validated Bearer JWT.
  Unsupported methods/unknown paths may fail 405/404, never link an account.
- Core `CreateLinkRequestAsync` and `CompleteLinkAsync` return faulted tasks with
  the constant `SteamLinkingRetiredException` immediately. Their old state generation,
  DB queries/locks/writes, provider verification and account-insertion implementation
  is removed. `HasPendingLinkRequestAsync` returns false even for valid stored rows.
  This is unconditional code, not a runtime flag. Direct callers cannot bypass HTTP
  rejection. Old signatures remain explicit rejection tombstones.
- Removed browser-binding issuance/matching class, secret-path StartUrl builder,
  old navigation response contract and credentialed Steam-link CORS policy. Existing
  safe application logging/exception diagnostics stay enabled. Steam responses now
  apply private,no-store / no-referrer / nosniff through the existing early privacy
  middleware, including challenges, errors and unmatched Steam paths.
- Status always reads only the authenticated user's existing link, returns
  linkAvailable=false, and evaluates configured preview/import independently. Unlink,
  imported games, IDs, dates, uniqueness and ownership rules remain unchanged.
  No pending row is consumed/expired/deleted to simulate a shared retirement fence.
- Angular has no linking method, handler, button, credentialed initiation or provider
  navigation, even if stale data says linkAvailable=true. It displays temporary
  unavailability, retains existing-account preview/import/unlink and warns that an
  unlink cannot currently be followed by relinking. Old callback flags never claim
  successful new linking; status remains authoritative.
- No v2 endpoints/secrets/confirmation/schema are implemented. Setting prospective
  enable flags true does not create a missing endpoint or reopen Core operations.
- Existing guarded E2E reset seeds its deterministic existing SteamAccount only when
  the fake Steam flag is enabled, like its existing game fixtures. No new seeding
  endpoint or production bypass was added; it does not call legacy linking. This
  fixture is not an application account-linking path or a real-user operation.

These guarantees concern requests executing this source/binary. They do not prove
every relational acceptance criterion without the pending PostgreSQL run below.

### Not a global guarantee

No accepted legacy operation exists inside the new rejection methods: there is no
await/SQL/provider step at which an earlier accepted write can resume. A synchronized
16-call unit regression demonstrates all completion attempts reject with zero DB
connection/provider work and no tracked changes. It is not a cross-version test.

An old replica may still hold accepted work in verification, SQL or COMMIT. Changing
this binary's code or returning a 410 cannot alter that replica's instructions,
revoke a valid old transaction or prove an uncertain commit rolled back. The original
blocker report's t0–t3 trace remains the proof boundary. No process-local flag or
source-only "global barrier" is introduced or claimed.

## Changed files/components

- Authority: `docs/product/product-specification.md`,
  `docs/domain/domain-specification.md`, `docs/architecture/architecture-specification.md`
  contain narrow authorized-retirement amendments, not approval of full v2.
- Feature: `specs/011-steam-integration/spec.md`, `linking-stabilization.md`, new
  `linking-retirement.md`; historical behavior remains documented but superseded.
- API: `Controllers/SteamController.cs`, `Program.cs`, `Steam/SteamContracts.cs`,
  `Steam/SteamOptions.cs`, `Steam/SteamLogPrivacy.cs`; deleted
  `Steam/SteamLinkBrowserBinding.cs`. Existing test-only reset fixture updated in
  `Controllers/E2eController.cs`.
- Core: `Steam/SteamIntegrationService.cs`, `Steam/SteamExceptions.cs`.
- Frontend: Steam page TS/template/tests and service/types/tests; deterministic
  journeys in `src/frontend/e2e/app.spec.ts`.
- Tests: new `GameLibrary.Core.Tests/SteamLinkRetirementTests.cs` and
  `GameLibrary.IntegrationTests/SteamLinkRetirementEndpointTests.cs`; adapted
  `SteamEndpointTests.cs`, `SteamLogPrivacyEndpointTests.cs`, `SteamLogPrivacyTests.cs`,
  `SteamRuntimePrivilegesTests.cs`.
- Documentation: exact copied reviewed spec/blocker and this implementation report.

No EF model/migration/snapshot, package/dependency/lockfile, GitHub workflow/guard,
cloud configuration or unrelated domain/feature code was changed.

## Executed local validation

| Validation | Actual result |
| --- | --- |
| Locked .NET restore / pinned existing macOS native preparation | PASS |
| Release solution build (all test projects included) | PASS, zero warnings/errors on final build |
| Core suite | **339 PASS**, zero failures/skips; includes two new no-IO rejection/concurrency tests |
| Selected database-free API/client/privacy/composition suite | **62 PASS**, zero failures/skips; includes Production/Development/Test retirement, flags, fixed error shape, no DB/Storage, safe diagnostics and absent v2 routes |
| Angular suite | **470 PASS**, 38 files; includes normal retired status and stale linkAvailable=true |
| QA configuration tests | **15 PASS** |
| Isolation guard unit decisions | **11 PASS** |
| Angular lint | PASS |
| TypeScript `tsconfig.spec.json` noEmit | PASS |
| Angular Production build | PASS with existing Random Picker SCSS budget warning (4.13 kB vs 4 kB); budgets unchanged |
| macOS Release publish | PASS; published appsettings.Production.json verified byte-identical to source |
| Playwright discovery | 24 cases discovered; **not executed** |
| Whitespace, scope, preserved-spec and limited credential-pattern checks | PASS before local commit |

The first new Core test run exposed a fixture error: accessing ChangeTracker without
an EF provider. The fixture now configures Npgsql but intercepts/throws on every
connection opening; no actual DB is contacted. All tracking/no-provider assertions
were retained or strengthened, and the full Core suite reran green. A temporary
using-placement compilation error in that fixture was also corrected before the
final green build. This is not a substituted relational test or EF InMemory use.
The first npm installation hit an existing shared-cache permission error; using a
fresh approved temporary cache succeeded without changing shared cache permissions,
package versions or lockfiles. Existing Vite/Vitest configuration deprecation notices
remain; they were not suppressed or opportunistically refactored.

### Pending — not reported as passing

- **Real PostgreSQL suite and new persistence/privilege/held-lock cases:** Docker
  daemon is unavailable (local Docker socket missing); no local PostgreSQL tools
  were found on PATH. Tests compile but were not run. No hosted DB was substituted.
- **Desktop/mobile E2E execution:** depends on that disposable backend database;
  only discovery/type checking was performed. No mocks replace the persistence gate.
- **Noble linux/amd64 native-runtime/G06 gates:** not run; local macOS publish/native
  preparation is not target-runtime verification.
- **Five GitHub Actions checks:** not run for this implementation. No push/PR or
  workflow dispatch is authorized; baseline PR #8's results are not this SHA's CI.
- **Hosted logging/cookie/replica/drain verification and global retirement:** not
  performed or authorized. An application 410 cannot redact Railway ingress history
  or arbitrary old/manual state-bearing requests already reaching the edge.

## Test-intent preservation / compatibility

All original Steam test methods/cases are retained or renamed for the new authorized
semantics, with no skip attributes added. Former successful legacy linking now must
produce zero link/state writes; invalid, expired, replayed, tampered, transferred,
concurrent, disabled-config and provider-failure cases also reject. OpenID validator
and fake HTTP client rule tests remain unchanged. Existing account/import/unlink
tests use disposable pre-existing account fixtures, not a live linking backdoor.
Future accepted v2 nonce/expiry/browser-race behavior still needs its own T01–T30
verification; a blanket 410 test is not claimed to implement or validate v2.

Old clients/bookmarks lose linking and redirects, including after unlink. Anonymous
creation now returns a public 410 rather than 401, without granting authentication.
Existing credentials/cookies/states are never upgraded to new authority. Manual
catalog use and existing configured linked-account operations remain available.

## Proposed coordinated retirement — future review only

1. Separately authorize an operational cutover and safe rollback artifact. Stop
   admitting legacy linking on every serving path, not merely the new frontend.
2. Remove/drain all old replicas and prevent restart/rollback to legacy-capable
   binaries. If quiescence cannot be proved, request a maintenance window; a rolling
   overlap is still an exposure window.
3. Resolve old in-flight/uncertain SQL transactions on the primary using a bounded
   lock/outcome barrier. HTTP disconnect, log silence, termination signal and COMMIT
   exception are not proof of rollback. Preserve legitimately committed links.
4. Verify only rejection-capable instances serve and no old binary/session can
   still commit a legacy link. Record auditable evidence/time/scope of the global
   barrier. Old pending rows alone grant no authority in the new binary.
5. Keep v2 inaccessible. Any future v2 rollout, schema changes, activation and
   residual provider-log risk acceptance require their own approval and gates.

No migration is required for this local artifact or inherently for a coordinated
PR #1 cutover. A shared DB-enforced fence allowing old binaries to keep running is
a different, unapproved scope. Operational rollback must preserve rejection, not
restore PR #8-only legacy behavior.

## Readiness

Suitable for a **local review commit with explicit pending validation**. Not cleared
for merge/release while PostgreSQL/E2E/target-runtime/required CI evidence is missing.
Push/PR creation requires further owner authorization; deployment/global retirement
requires separate operational approval. No result here authorizes v2 activation.
