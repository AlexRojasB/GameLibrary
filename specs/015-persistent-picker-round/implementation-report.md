# GL-16 — implementation and publication review

Date: 2026-11-25. Branch: `feature/persistent-picker-round`, based on latest fetched
`origin/dev` at `dfc79021bf5ffd096445b88e5955a3c9158802a0` (unchanged after the
publication-review fetch). The initial delivery was local-only. The owner's
subsequent authorization permits committing/pushing this scoped branch, a PR to
`dev` and one normal required CI run. This report records pre-publication local
evidence; the PR records its final SHA and CI results. No merge, deployment, remote
migration, Steam or QA change is authorized.

## Delivered diff

- AGENTS.md and governing Product/Domain/Architecture documents explicitly permit
  only GL-16 current-round recovery. No completed-round archive, statistics,
  completion flow, Skip, Play Log or Steam behavior was introduced.
- Core `PickerRound`/`PickerRoundService` persist independent All, VideoGames and
  BoardGames rows per authenticated Library. All remains the entry/reload default.
  Shared existing Picker filtering/projection is reused rather than duplicated.
- GET/PUT `/random-picker/rounds/{mode}`, POST
  `/random-picker/rounds/{mode}/reset`, and atomic persisted POST
  `/random-picker/pick` with expected revision + operation UUID. The original
  read-only pick protocol remains compatible and never bootstraps a Library.
- Server draws alone append durable history; caller result/history/shown snapshots
  are not authoritative. Library-first locking, optimistic revision checks and a
  single last-operation fingerprint prevent duplicate draws and stale overwrites.
  Immediate identical retries are safe; conflicting/reused/older writes return
  409. There is no automatic retry after an uncertain response/COMMIT.
- Recovery resolves fresh owner-scoped metadata, omits deleted/foreign history,
  retains shown IDs, and rechecks proposal ownership, mode, Owned, saved filters
  and Currently Playing. Unavailable proposals are cleared in the response with
  an explicit notice, never replaced by an older history card. Historical cards
  remain read-only. An idempotent pick retry also revalidates its saved proposal.
- Angular restores the round on entry/mode switches. Filter edits preserve history
  and shown IDs while clearing proposal/outcome; reset touches only its active
  mode and preserves filters. Another and Select Game retain their existing roles.
- Feature-local in-memory online sequencing admits writes independently per mode,
  waits for acknowledgements/revisions and survives component navigation. Account
  changes cancel old requests and discard unsent work. Errors discard unsent work
  and require explicit fresh reload. No browser persistence, offline/reconnect
  replay, client-authored history or new dependency was added.
- Updated obsolete volatile/shared-mode frontend lifecycle assertions; retained
  meaningful selection, Family Friendly, metadata, auth and explicit Log play
  regression coverage. Added focused round endpoint/migration tests and one recovery
  E2E scenario, executed in both existing browser projects.
- Publication review also updates the two older Picker E2E scenarios that still
  expected shared cross-mode history/filters. Assertions now verify per-mode
  independence and recovery, preserving classification, filtering, exhaustion,
  reset and responsive checks. Steam sections of the same test file are untouched.
- A concrete local E2E failure exposed dirty native-checkbox state surviving a
  rapid filter edit + mode switch even though the recovered backend/Angular value
  was false. Keying the native filter form by mode fixes that presentation leak;
  a no-intermediate-render unit regression and the browser assertions cover it.

## Migration requirements

`src/backend/GameLibrary.Core/Data/Migrations/20261125151437_AddPickerRounds.cs`
(plus generated designer/model snapshot) creates only `picker_rounds`.

Composite PK `(library_id, mode)` plus the allowed-mode check permits at most three
current rows per Library. A Library cascade FK and checks constrain mode, outcome,
positive revision, JSON object and last-operation receipt. Arrays store only entry
IDs; no Game/entry FK blocks ordinary deletion. No existing table/data mutation,
backfill, auth/storage schema change, second migration workflow or dependency.

The migration must be applied through the normal reviewed EF migration process
before deploying code that uses the new round endpoints. It has **not** been applied
to any remote database. Rollback drops recovery state only; the isolated local
upgrade/rollback test verifies catalog and Currently Playing preservation.

## Final local validation

All database verification used a task-owned, disposable, loopback-only pinned
PostgreSQL 17 Docker container (`tools/ci/postgres-image.txt`), with separate
`game_library_gl16_tests` and guarded `game_library_e2e` databases. No hosted
credentials or services were used. The container and its disposable volume were
removed after validation; Playwright's local web servers were stopped. Publication
review repeated the affected browser checks in another disposable container using
the same pinned image and loopback guard; it was also removed.

| Check | Result |
| --- | --- |
| `dotnet build src/backend/GameLibrary.Api --no-restore` | PASS, 0 warnings/errors |
| Integration filter: PickerRound, RandomPickerEndpointTests, CurrentlyPlayingEndpointTests, DatabaseMigrationTests | **69 PASS** |
| Core unit filter: RandomPickerRulesTests | **34 PASS** |
| Angular Random Picker, Currently Playing and Home tests, after publication corrections | **56 PASS**, 4 files |
| `npm run lint` | PASS |
| `npm run build` | PASS with warnings below |
| Recovery + Currently Playing + two affected Picker E2E scenarios, chromium-desktop and mobile-chrome | **8 PASS** |
| `dotnet ef migrations has-pending-model-changes` (Core/Api projects) | PASS, no pending changes |
| `git diff --check` | PASS |

Focused PostgreSQL coverage includes all three modes, newest-first order and fresh
metadata across clients/contexts, read-only empty rounds, empty/exhausted outcomes,
independent reset for each mode, filter recovery, forged client history, JWT owner
isolation, foreign references, deleted/non-Owned/active/filtered-out proposals,
repeated saves/resets/draws, concurrent same/different operations, stale revisions,
eligibility after a real lock wait, schema checks, additive upgrade and rollback.
The E2E covers Another → leave/return → reload → sign out/in, retaining proposal,
previous result and player-count filter. Existing Currently Playing E2E also passes.

## Remaining limitations / review notes

- No unresolved product decision or failing local check.
- Bundle comparison using the same local dependencies/configuration confirms the
  warning was introduced by GL-16: an isolated `git archive origin/dev` frontend
  build produced **697.98 kB**, while the original GL-16 build produced **702.07 kB**
  against the unchanged 700 kB warning budget. The small correction lazy-loads only
  `/random-picker`, following the existing catalog-route pattern, with the same
  auth guard and no URL/domain changes. Final initial bundle: **672.40 kB**; Picker
  lazy chunk: **30.30 kB**. The initial-budget warning is resolved without raising
  budgets, changing dependencies or broad optimization.
- Remaining production-build warning is pre-existing Picker SCSS: **4.13 kB versus
  4 kB**. Existing Vitest/Vite configuration deprecation warnings remain unchanged.
- This table is focused local evidence, not hosted release certification. Normal
  required CI (backend, postgres, frontend, e2e, native-runtime) runs once through
  the unchanged PR workflow; see the PR for final results. No production/QA state
  or Steam behavior was changed.
- Pending untracked `deploy/qa/review/` and the three Steam QA deployment documents
  present before this task were left untouched.

Publication is authorized; merge, deployment and remote migration remain prohibited.
Stop for review after reporting the PR, final SHA and CI results.
