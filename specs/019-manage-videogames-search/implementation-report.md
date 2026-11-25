# GL-15 — Manage VideoGames search, implementation review

Date: 2026-11-25. Branch: `feature/manage-videogames-search`, created from freshly
fetched `origin/dev` at `0a1945b4dbd565dc280e6d0e349c9c5282ebb692` (merged PR #19).
The initial implementation was local and uncommitted. The owner's subsequent
authorization permits scoped commit/push and a PR to dev with one normal required
CI run. The PR will record the published SHA and observed check results.

## Inspection / implementation decisions

- Manage VideoGames previously used `GET /video-games`: full array, no filters,
  sort selector or pagination. Its implicit restrictions are the current Library's
  VideoGames and deterministic case-insensitive name ascending order (including
  original-case, creation timestamp and ID tiebreakers).
- Existing `GET /library` already implements trimmed, case-insensitive literal
  substring search using PostgreSQL ILIKE with escaped %, _ and backslash. It
  combines search and filters using AND, scopes every result to the authenticated
  Library, and returns every matching item without a page window. Its NameAsc
  sort is identical to the management endpoint's order. Its read model contains
  all fields needed by the existing VideoGame edit/cover flows.
- Reuse that endpoint through the existing Angular LibraryService for non-empty
  searches, always passing `gameType=VideoGame` and `sort=NameAsc`. Empty or
  whitespace-only search keeps the original `/video-games` read. No backend
  production change, new endpoint, contract, schema, migration or dependency.
- There is no pagination to reset or existing management filter control to combine
  with. Do not add either speculatively. Tests verify the existing type filter,
  ordering, API search/filter composition and full-collection behavior instead.
  This is an inspected non-applicability, not a client-side page-only search.

## Exact changes

1. `src/frontend/src/app/features/games/video-games.service.ts`:
   optional `list(search)` delegates non-empty searches to LibraryService; no-arg
   list calls remain unchanged, especially cover reconciliation.
2. `src/frontend/src/app/features/games/video-games-list/video-games-list.ts`:
   page-local search signal and input/clear handlers; loads use current search.
   Preserve the existing load-generation/account guard for both results and errors,
   including late 401s. Cancel pending list reads on destruction. Search resets on
   account-generation changes and is retained on CRUD refresh. No storage or
   navigation/query-string persistence. Search runs immediately when the trimmed
   text changes; equivalent surrounding whitespace does not trigger another read.
3. `src/frontend/src/app/features/games/video-games-list/video-games-list.html`:
   visible associated label, native search input, explicit Clear search button
   restoring input focus, live loading/result-count/no-match messages, distinct
   empty-library state and Add action on no matches. Search/clear stay usable
   during loading/errors.
4. `src/frontend/src/app/features/games/video-games-list/video-games-list.scss`:
   small wrapping search row using existing surface/button/input styles; flexible
   input with no fixed mobile width.
5. Existing frontend service/list spec files: **15 added test executions** for
   query transport/normalization/type/sort, no-results/clear/focus, whitespace,
   rendering all 51 supplied matches, stale success/503/401, clearing during reads,
   retry, searched edit/refresh, unfiltered cover reconciliation, account-generation
   reset and destruction cancellation. Existing tests/assertions retained.
6. `tests/backend/GameLibrary.IntegrationTests/LibraryEndpointTests.cs`:
   one additive real-PostgreSQL test of the reused API with 33 VideoGames, matching
   titles after 31 alphabetically earlier fillers, mixed-case partial matching,
   deterministic order, matching BoardGame/foreign-user exclusion, acquisition
   filter AND search, empty results and whitespace/clearing restoration.
   No existing backend test or production behavior changed.
7. `src/frontend/e2e/manage-videogames-search.spec.ts`:
   one scenario on desktop and mobile using real local API/PostgreSQL plus existing
   fake auth: labelled input, width/no-horizontal-overflow, mixed-case partial
   search, full edit/save using Library read data, search retention, no-results/Add,
   clear/focus, whitespace and matching BoardGame exclusion.
8. `specs/019-manage-videogames-search/spec.md` and this report record the authorized
   scope, inspected limitations and verification.

## Validation results

Tests use task-owned pinned PostgreSQL 17 on loopback, with separate
`game_library_test` and guarded `game_library_e2e` databases. Only existing EF
migrations were applied to these disposable databases. No hosted credentials,
remote data, real auth/provider calls or QA changes were required. The temporary
container and its anonymous volume were removed after validation.

| Local check | Result |
| --- | --- |
| Initial focused VideoGames service/list unit suites | **48 PASS** before two additional account/cover regression tests |
| Final publication review, focused VideoGames service/list suites | **50 PASS**, confirming full-list/clear/stale-response cases |
| Final complete frontend suite, `npm test -- --watch=false` | **559 PASS**, 40 files, none skipped/failed |
| LibraryEndpointTests + VideoGameEndpointTests on real PostgreSQL | **89 PASS**, including new full-collection/type/filter/isolation case |
| Core tests | **353 PASS** |
| `dotnet build src/backend/GameLibrary.sln --no-restore -p:UseSharedCompilation=false` | PASS, zero warnings/errors |
| `npm run build` | PASS, initial 676.83 kB; VideoGames lazy chunk 33.25 kB |
| `npm run lint` | PASS |
| `npx tsc --noEmit -p tsconfig.spec.json` | PASS |
| Focused Playwright desktop + mobile, explicit `--retries=0` | **2 PASS** |
| `git diff --check` | PASS |

PostgreSQL regression command:
`dotnet test tests/backend/GameLibrary.IntegrationTests --no-build --no-restore --filter 'FullyQualifiedName~LibraryEndpointTests|FullyQualifiedName~VideoGameEndpointTests'`.
ConnectionStrings__Test pointed only to the disposable local test database.

E2E command from `src/frontend`:
`npm run e2e -- e2e/manage-videogames-search.spec.ts --retries=0`, with
ConnectionStrings__E2E pointing to the dedicated local `game_library_e2e` database.
The first startup attempt supplied Default rather than E2E; the existing guard
correctly refused startup before any browser test. Supplying the required isolated
E2E connection resolved it without source/configuration changes or relaxed guards.

## Warnings / limitations / review boundary

- Existing Picker SCSS budget warning: 4.13 kB versus 4.00 kB (130 bytes over).
  No Picker source or budget changed.
- Existing tooling warnings: Vite's future native config loader does not support
  ESM syntax in the CommonJS-loaded `vitest-base.config.ts`; Vitest 4 deprecates
  `test.poolOptions`. No tooling/dependency changes or warning suppression.
- Desktop/mobile verification used Chromium desktop and emulated Pixel 7, not a
  physical mobile device or a manual screen-reader audit.
- No full integration/native-runtime campaign was run: production backend/native
  code is unchanged; the affected API suites and desktop/mobile scenario passed.
- No unresolved product/architecture decisions. Pagination and additional filter
  controls do not exist on this page and were intentionally not introduced.
- Picker, BoardGame management, Steam, authentication and QA source/configuration
  remain unchanged. Pre-existing untracked QA review/deployment files are untouched.
- Publication follow-up: final diff reviewed against freshly fetched origin/dev;
  scope is GL-15 UI/service, focused tests, specification and this report only.
  Verified full-collection server querying, clearing through the original list
  read, and generation/account guards for stale success/error responses. The owner
  authorizes scoped commit/push, a PR targeting dev and one standard required CI
  run. Record the SHA/check results in the PR without another source commit solely
  for CI evidence. No merge, deployment, remote migration, QA or Steam changes.
  Stop for review; publication is not release approval.
