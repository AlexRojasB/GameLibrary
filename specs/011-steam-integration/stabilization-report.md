# Stabilization Before Feature 013

## Status

Implemented and automatically verified on 2026-10-28. Feature 013 was not started.
All pre-existing staged, unstaged and untracked work was preserved. No Git reset,
revert, clean, staging, commit or push was performed.

This pass fixes existing approved behavior in Features 007, 011 and 012. It does
not approve the separate proposed Steam logging lifecycle or introduce new schema,
API response fields, automatic Play Log entries or historical sessions.

## Root Causes And Fixes

### Random Picker

Every subscription previously applied its result or error unconditionally. A
filter/mode/reset change did not invalidate requests already in flight, and an
older response could overwrite a newer result, stop its loading state, append a
game that was never displayed to history, or trigger obsolete error/401 handling.

The component now uses one local monotonically increasing request generation.
Starting a new pick or changing filters, mode, clear/reset invalidates older
generations. Success and error callbacks check the generation before any side
effect. Invalidating a pick releases loading immediately. Component destruction
unsubscribes outstanding picks using Angular's takeUntilDestroyed.

Only accepted current successes enter shown IDs and newest-first history. All
three result states remain unchanged. Completed history survives filter/mode
changes; only explicit reset clears it. No backend Picker behavior changed.

### Steam Playtime

The existing pure delta helper was tested but never called by refresh. Refresh
replaced the baseline without integrating that calculation. Its int parameters
also did not match the nullable bigint persisted baseline.

Both refresh paths now call the same Core per-entry operation: calculate against
the prior stored total, then update it only when current playtime is present. The
helper accepts long values without narrowing the persisted baseline. Missing or
unchanged values produce zero; a decrease produces zero and establishes the lower
baseline. No manual Game/LibraryEntry metadata or PlayLogEntries are changed.

Preview now uses the same per-Library database lock as import. External Steam calls
remain outside the short transaction. Existing entries are loaded after locking,
so concurrent refresh writers calculate against the serialized persisted baseline.

The per-refresh return value is intentionally transient and unused by the current
HTTP response/UI. It is not persisted as pending playtime. A future deferred
confirmation flow cannot recover an unacknowledged increment from the latest total
alone; see the separate proposal below. No claim is made that the future logging
flow is implemented by calculating a transient delta.

### Steam Selection

hasSelectedSelectable and allSelectableSelected were mutable signals synchronized
only by checkbox toggles. Preview/import/unlink reset selection without updating
those flags. Import also left successful candidates selectable.

The flags are now computed from candidates and selection. Successful imported and
alreadyImported IDs immediately become disabled/labeled candidates while retaining
the summary; unavailable/invalid candidates remain eligible for an explicit retry.
The summary includes invalid counts. Synchronous handler guards and matching
disabled controls prevent duplicate or overlapping preview/import/unlink mutations.
Imported and unknown IDs cannot be toggled through the handler.

### Steam Duplicate Classification

Duplicate lookup had become coupled to the AppIDs returned by the current provider
response. A selected imported AppID omitted by Steam was therefore unavailable,
and invalid provider metadata could put an ID in multiple outcome categories.

After the Library lock, refresh/lookup now checks all selected AppIDs in the
caller's Library, independent of provider availability. Existing import identity
takes precedence over unavailable/invalid classifications. Metadata refresh runs
only when Steam returned that existing AppID. Outcomes are disjoint and no foreign
Library imports are disclosed or modified.

## Regression Coverage

- 24 additional Picker cases: out-of-order successes, both empty states, errors,
  stale 401s, loading ownership, filter/mode changes, clear/reset and destruction.
- 10 additional Steam UI cases: computed selection resets, select all, disabled
  imported candidates, mixed outcomes, re-preview/unlink, overlapping handler
  guards and explicit retries.
- Nine additional Core cases: bigint-compatible arithmetic and the actual shared
  refresh operation with missing, unchanged, increased and decreased totals.
- 16 additional PostgreSQL integration cases: both refresh endpoints and baseline
  combinations, missing/invalid duplicate precedence, cross-user isolation, and a
  shared-lock concurrency test. The concurrency observer uses a separate connection
  and follows queued blockers, rather than relying on a cached transaction activity
  snapshot or treating only direct blockers as waiters.
- Existing E2E journeys strengthened without adding journeys: Picker Another waits
  for the new history card and scopes the current heading; Steam import checks
  disabled imported candidates and select-all selecting only the remaining game.

## Actual Verification

Final runs completed with no failed/skipped tests:

| Check | Result |
| --- | --- |
| Backend Core | 317/317 passed. |
| Full real-PostgreSQL integration | 361/361 passed. |
| SteamEndpointTests subset | 75/75 passed; included in the full integration count. |
| Frontend units | 289/289 passed across 29 files. |
| Frontend lint | Passed. |
| Backend solution build | Passed, zero warnings/errors. |
| Production build | Passed with warning budgets below. |
| E2E build | Passed. |
| Playwright | 14/14 passed, desktop Chromium and mobile Chrome, no retries. |
| EF pending-model check | No changes since the last migration. |
| git diff --check and staged whitespace check | Passed. |

Commands used the existing workflows: dotnet test for the Core and integration
projects, integration --filter FullyQualifiedName~SteamEndpointTests, dotnet build
src/backend/GameLibrary.sln, npm test -- --watch=false, npm run lint, npm run build,
npm run build -- --configuration e2e, npm run e2e, and dotnet ef migrations
has-pending-model-changes with Core as project and Api as startup project.

PostgreSQL ran in the existing game-library-postgres-test container on port 5433,
with separate game_library_test and game_library_e2e databases. Steam provider
behavior was deterministic/fake. No remote service, real Steam credential or remote
schema mutation was needed. Both existing Steam and Family Friendly migrations
remain intact; no new migration was added by stabilization.

## Files Changed In This Pass

```text
src/backend/GameLibrary.Core/Steam/SteamIntegrationRules.cs
src/backend/GameLibrary.Core/Steam/SteamIntegrationService.cs
tests/backend/GameLibrary.Core.Tests/SteamIntegrationRulesTests.cs
tests/backend/GameLibrary.IntegrationTests/SteamEndpointTests.cs
src/frontend/src/app/features/random-picker/random-picker-page/random-picker-page.ts
src/frontend/src/app/features/random-picker/random-picker-page/random-picker-page.spec.ts
src/frontend/src/app/features/manage/steam/steam-page.ts
src/frontend/src/app/features/manage/steam/steam-page.html
src/frontend/src/app/features/manage/steam/steam-page.spec.ts
src/frontend/e2e/app.spec.ts
specs/011-steam-integration/playtime-delta-lifecycle-proposal.md
specs/011-steam-integration/stabilization-report.md
specs/012-family-friendly/spec.md
```

The Feature 012 specification receives only a cross-reference resolving its
historical follow-ups. Other modifications visible in Git status predate this pass.

## Specification Decision And Remaining Risks

No amendment was necessary for stale-response rejection, correct duplicate
classification, selection consistency, or the already-approved delta arithmetic.

[Steam Playtime Delta Lifecycle](playtime-delta-lifecycle-proposal.md) documents
the undefined future lifecycle and proposes one separate acknowledged cumulative
baseline plus atomic explicit confirmation. It remains Proposed, awaiting approval;
no additional persistence or logging action was implemented. Product/Domain and
feature approval must precede that work.

- A superseded Picker request can finish server-side, but its callbacks cannot
  change current client state or count its result as shown. No server session is
  persisted, so discarding that response consumes no candidate.
- Ordinary Steam refreshes advance the observation baseline. Transient increments
  are not retained for later logging; that limitation is explicit, not silently
  solved with speculative persistence.
- The production bundle is 704.66 kB against a 700 kB warning budget. The unchanged
  Picker stylesheet is 4.13 kB against a 4 kB warning budget. Error budgets and
  dependency versions were not changed.
- Existing EF tools/runtime mismatch (10.0.7 / 10.0.11) and Vitest poolOptions
  deprecation warnings remain; verification succeeds despite them.
- Feature 012 manual product review remains pending. Stabilization is ready for
  manual review and Feature 013 planning/specification; implementation of any new
  feature still requires its approved specification. A future Steam logging action
  additionally requires approval of its delta lifecycle.
