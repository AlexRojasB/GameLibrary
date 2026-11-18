# 012 - Family Friendly

## Status

`Approved`

Implementation and automated verification completed on 2026-10-28. Manual product
review remains pending; the specification remains Approved, not manually accepted.

## Feature

Family Friendly classification.

## Objective

Let an authenticated user explicitly mark VideoGames and BoardGames as appropriate
for family play, display that classification without visual noise, and use it as
an optional Library and Random Picker filter.

After this feature is implemented, an authenticated user can:

1. Create a VideoGame or BoardGame with Family Friendly explicitly on or off.
2. Edit an existing VideoGame or BoardGame and change Family Friendly.
3. See a compact Family Friendly indicator on game cards when useful.
4. Filter Library browse results to family-friendly games only.
5. Ask Random Picker to pick only family-friendly games in VideoGames, BoardGames
   or All mode.

Family Friendly is simple manual classification. It is not an age-rating or
content-safety system.

## Authority

Product Specification -> Domain Specification -> Architecture Specification ->
Feature Specification -> Implementation.

Feature 012 uses the Product and Domain amendments dated 2026-08-28. The approved
Architecture Specification remains unchanged. Higher-authority behavior remains
authoritative if a conflict is found during implementation.

## Current Repository Assessment

Observed backend state:

- Backend projects remain exactly `GameLibrary.Api` and `GameLibrary.Core`.
- `Game` is the persisted game-level entity shared by VideoGame and BoardGame rows.
- VideoGame and BoardGame create/update/read contracts already expose game-level
  fields such as `name`, `coverImageUrl`, player counts and created timestamp.
- Library browse and Random Picker read models are projected from `Game` plus the
  associated `LibraryEntry` and already include shared fields.
- Library filtering and Random Picker filtering are backend-authoritative and use
  typed query/request objects in `GameLibrary.Core`.
- EF Core migrations are the application schema source of truth.
- Steam import creates normal Owned VideoGames through Core service logic and can
  set the same default value as manual creation.

Observed frontend state:

- VideoGame and BoardGame Add/Edit use Angular dialog forms with existing form
  controls and validation messages.
- Library, VideoGame and BoardGame list cards already use compact chips/badges for
  concise metadata such as game type and Steam already-imported state.
- Random Picker owns volatile UI state and sends current filters plus
  `shownLibraryEntryIds` to the backend.
- The Library page has a filter panel with general filters and type-specific
  filters.

## In Scope

- Add a required boolean `IsFamilyFriendly` game-level classification.
- Apply it to both VideoGames and BoardGames.
- Default new manual games to `false` unless the user explicitly marks the game as
  family-friendly.
- Default existing rows to `false` during migration.
- Default Steam-imported games to `false` and do not infer from Steam metadata.
- Expose Family Friendly in VideoGame and BoardGame create/update/read API
  contracts.
- Expose Family Friendly in Library and Random Picker read models.
- Add a Library browse filter: family friendly only.
- Add a Random Picker filter: family friendly only.
- Display a compact positive Family Friendly chip on relevant cards.
- Add backend, frontend and proportionate E2E coverage.

## Out of Scope

Feature 012 must not introduce:

- ESRB ratings.
- PEGI ratings.
- Minimum age.
- Parental controls.
- Content-warning categories.
- Alcohol, drug, violence, language or sexual-content classifications.
- Automatic classification.
- AI classification.
- Steam age-rating/content inference.
- Child profiles.
- Shared libraries or family libraries.
- New backend projects.
- New frontend state-management libraries.
- New database tables for classification.

## Future Backlog Context

Do not implement these as part of Feature 012:

- Camera / Upload Cover: allow users, especially from mobile/PWA, to take or upload
  their own game cover photo and use it as `CoverImageUrl` through proper image
  storage rather than database blobs.
- Production Deployment: deploy Game Library so it can be securely used from mobile
  devices.
- Later / post-MVP: Shared Library.
- Later / post-MVP: additional store integrations.

## Domain Decisions

### Classification Model

`IsFamilyFriendly` is a required boolean on `Game`.

It answers only:

"Is this game considered appropriate for family play?"

It applies to:

- VideoGame.
- BoardGame.

It does not imply that `false` means adults-only. `false` means the user has not
classified the game as appropriate for family play, or has classified it as not
appropriate for family play.

No additional domain entity, enum or taxonomy is introduced.

### Default Behavior

New games default to `IsFamilyFriendly = false`.

Reason: a game should not be assumed to be appropriate for family play unless the
user explicitly says so.

This default is consistent with existing Add/Edit conventions because optional or
secondary metadata can be completed later, while the backend still normalizes a
valid persisted value. The UI should show the control in Add/Edit, default it to
off, and submit the explicit boolean value.

Existing rows are migrated to `false`.

Steam-imported games use `false` unless edited later by the user.

### Relationship To Existing Concepts

Family Friendly is game-level metadata, not LibraryEntry metadata.

It does not affect:

- AcquisitionStatus.
- GameStatus.
- ProgressPercentage.
- Rating.
- Notes.
- Platforms.
- Genres.
- InteractionType.
- Player-count ranges.
- PlayLogEntry semantics.
- SteamPlaytimeMinutes.
- Random Picker eligibility beyond the explicit filter.

## API Contract

### VideoGames

Add `isFamilyFriendly` to:

- `CreateVideoGameRequest`.
- `UpdateVideoGameRequest`.
- `VideoGameResponse`.

Request behavior:

- Type: boolean at the JSON boundary.
- Missing or `null` on create is normalized to `false`.
- Missing or `null` on update is normalized to `false`, matching the current
  full-form update style where the frontend sends the whole edited shape.
- The backend remains authoritative for normalization.

Response behavior:

- Always returns non-null boolean `isFamilyFriendly`.

### BoardGames

Add `isFamilyFriendly` to:

- `CreateBoardGameRequest`.
- `UpdateBoardGameRequest`.
- `BoardGameResponse`.

Request and response behavior is the same as VideoGames.

### Library Browse

Add `isFamilyFriendly` to `LibraryItemResponse`.

Add optional query parameter:

- `familyFriendlyOnly`: boolean.

Semantics:

- Omitted, `false` or `null`: do not filter by Family Friendly.
- `true`: return only games where `IsFamilyFriendly = true`.
- Applies across `gameType = null`, `VideoGame` and `BoardGame` results.
- Combines with search and all existing filters using AND.

Invalid boolean query values should be rejected with the existing invalid-filter
ProblemDetails pattern.

### Random Picker

Add `familyFriendlyOnly` to `RandomPickerPickRequest`.

Add `isFamilyFriendly` to `RandomPickerItemResponse`.

Request semantics:

- Type: nullable boolean at the API boundary.
- Omitted, `false` or `null`: do not filter by Family Friendly.
- `true`: only candidates with `IsFamilyFriendly = true` match.
- Applies in `VideoGames`, `BoardGames` and `All` modes.
- It is valid with every picker mode and therefore must not trigger mode/filter
  compatibility errors.
- Combines with all active filters using AND.

Existing Random Picker behavior remains unchanged:

- Only Owned entries are eligible.
- Mode selection still determines VideoGames, BoardGames or All.
- Type-specific filters remain unavailable in All mode.
- Player-count, rating, platform, genre, GameStatus, duration and InteractionType
  filters keep their existing semantics.
- `shownLibraryEntryIds` exclusion is unchanged.
- Changing filters or mode does not clear shown IDs or visible history.
- `SUCCESS`, `NO_CANDIDATES` and `ALL_ALREADY_SHOWN` remain distinct.
- No weighting, scoring or automatic recommendations are added.

## UI/UX Decisions

### Add/Edit Forms

VideoGame and BoardGame Add/Edit forms must include a Family Friendly control.

Use existing form conventions, with one improvement over a bare ambiguous
checkbox:

- Label: `Family friendly`.
- Default: off.
- Include concise helper text such as `Mark this when the game is appropriate for
  family play.`
- The control must be easy to operate on mobile.

The exact visual control can be a checkbox, switch or segmented yes/no control as
long as it is accessible, sends a boolean, defaults to false and avoids implying an
age-rating system.

### Display

Cards should show a compact positive chip only when `isFamilyFriendly = true`.

Chosen behavior:

- Display `Family friendly` chip for true values.
- Do not display a negative `Not family friendly` chip for false values.

Reason: false can mean either not suitable or not yet classified, and showing the
negative state on every card would add visual noise.

Apply the positive chip consistently where game cards already present compact
metadata:

- Library cards.
- VideoGame management cards.
- BoardGame management cards.
- Random Picker current-result and history cards when space permits without
  crowding.

Play Log cards are not required to display Family Friendly because Play Log
semantics are unchanged.

### Library Filter

Library browse should include a `Family friendly only` filter.

Decision: include it.

Reason: the classification is directly useful when deciding what to play with the
family, it applies to both game types, and it does not conflict with Feature 006
filter architecture because it is a simple common boolean filter using existing AND
combination semantics.

The Library filter must not replace the existing Game type, AcquisitionStatus,
Rating, Platform, Genre, GameStatus, player-count or InteractionType filters.

## Persistence

Smallest EF Core schema change:

- Add `games.is_family_friendly boolean NOT NULL DEFAULT false`.

Existing rows:

- Existing `games` rows are explicitly/backfilled to `false` by the migration.
- The final column is non-null.

Constraints/indexes:

- No separate check constraint is necessary because PostgreSQL boolean already
  constrains the representation.
- No new table is necessary.
- No index is required for MVP unless implementation measurements show a concrete
  need. A standalone boolean index is not added speculatively.

EF model:

- Add `bool IsFamilyFriendly` to `Game`.
- Configure column name `is_family_friendly` and default value `false`.
- Include it in the EF model snapshot through the generated migration.

## Steam

Steam imports must not infer Family Friendly from Steam metadata.

Imported Steam games are created with:

- `IsFamilyFriendly = false`.

Steam refresh/import behavior must not change `IsFamilyFriendly` for already
imported games. The user may edit the game later through the normal VideoGame edit
flow.

Do not add Steam age-rating, content descriptor or store-page inference.

## Play Log

No Play Log semantics change.

Play Log creation, editing, browsing and deletion behavior must not depend on
Family Friendly.

No PlayLogEntry field is added.

## Testing

### Backend Unit Tests

Required coverage:

- VideoGame create defaults missing/null Family Friendly to false.
- VideoGame create preserves explicit true and explicit false.
- VideoGame update can change false -> true and true -> false.
- BoardGame create defaults missing/null Family Friendly to false.
- BoardGame create preserves explicit true and explicit false.
- BoardGame update can change false -> true and true -> false.
- Library filter parsing accepts omitted/false/true values and rejects invalid
  boolean values according to existing query-validation conventions.
- Random Picker parsing accepts omitted/false/true Family Friendly values in all
  modes.
- Random Picker Family Friendly filter combines with existing filters using AND.

### Backend Integration Tests

Required coverage using real PostgreSQL semantics:

- EF migration adds `games.is_family_friendly` as non-null with default false.
- Existing-row migration behavior results in false for preexisting games.
- VideoGame create/read/update API returns `isFamilyFriendly` correctly.
- BoardGame create/read/update API returns `isFamilyFriendly` correctly.
- Library browse responses include `isFamilyFriendly`.
- Library `familyFriendlyOnly=true` returns only true games for all game types.
- Library Family Friendly filtering combines with search, game type,
  AcquisitionStatus and Rating.
- Random Picker returns only family-friendly candidates when requested in
  VideoGames mode.
- Random Picker returns only family-friendly candidates when requested in
  BoardGames mode.
- Random Picker returns only family-friendly candidates when requested in All mode.
- Random Picker Family Friendly filtering combines with existing filters such as
  Rating, player count, Platform/Genre/GameStatus or InteractionType as applicable.
- Random Picker still distinguishes `NO_CANDIDATES` from `ALL_ALREADY_SHOWN` with
  Family Friendly active.
- Steam import creates games with `is_family_friendly = false`.
- Steam refresh/re-import does not overwrite a user-edited Family Friendly value.
- User isolation remains intact for create/update/read/filter/pick behavior.

### Frontend Tests

Required coverage:

- VideoGame Add form renders Family Friendly default off and submits false unless
  selected.
- VideoGame Add form submits true when selected.
- VideoGame Edit form initializes from the existing value and can toggle it.
- BoardGame Add form renders Family Friendly default off and submits false unless
  selected.
- BoardGame Add form submits true when selected.
- BoardGame Edit form initializes from the existing value and can toggle it.
- Library service serializes `familyFriendlyOnly=true` and omits it when inactive.
- Library page exposes the filter and refetches when it changes.
- Library cards render the positive chip only for true values.
- VideoGame and BoardGame management cards render the positive chip only for true
  values.
- Random Picker service sends `familyFriendlyOnly=true` and omits or sends false
  consistently when inactive according to the chosen implementation style.
- Random Picker page preserves existing mode/filter/session behavior when the new
  filter changes.

### Playwright E2E

Add proportionate E2E coverage without expanding the suite unnecessarily.

Recommended journey:

1. Login in deterministic E2E auth mode.
2. Create one family-friendly game and one non-family-friendly game.
3. Verify the positive chip appears only on the family-friendly card.
4. Use Library `Family friendly only` and verify only the family-friendly game is
   shown.
5. Use Random Picker with `Family friendly only` and verify the selected result is
   family-friendly.

This may be added to an existing high-value flow if it remains readable and stable.

## Acceptance Criteria

### Creation And Editing

1. VideoGame Add/Edit allows the user to set Family Friendly.
2. BoardGame Add/Edit allows the user to set Family Friendly.
3. New manually created games default to `isFamilyFriendly = false` unless the user
   explicitly marks them true.
4. Edit can change the value in both directions.
5. Backend responses for VideoGame and BoardGame include non-null
   `isFamilyFriendly`.

### Display

1. Library cards show a compact `Family friendly` chip only for true values.
2. VideoGame management cards show the same positive chip only for true values.
3. BoardGame management cards show the same positive chip only for true values.
4. False values do not produce a negative chip.

### Library Filtering

1. Library browse supports `familyFriendlyOnly=true`.
2. The filter applies to VideoGames and BoardGames.
3. The filter combines with search and all existing filters using AND.
4. Omitted or false values do not filter by Family Friendly.
5. Invalid boolean query values are rejected with the existing invalid-filter
   response pattern.

### Random Picker

1. Random Picker supports `familyFriendlyOnly=true` in VideoGames, BoardGames and
   All modes.
2. It is an additional filter and does not change Owned-only eligibility.
3. It combines with all other active filters using AND.
4. It does not clear shown IDs or visible result history when toggled.
5. It does not change reset behavior.
6. It preserves `SUCCESS`, `NO_CANDIDATES` and `ALL_ALREADY_SHOWN` distinctions.
7. It does not introduce weighting or recommendation scoring.

### Steam

1. Steam imports create VideoGames with `isFamilyFriendly = false`.
2. Steam import/refresh does not infer Family Friendly from Steam metadata.
3. Steam refresh/re-import does not overwrite a user's edited Family Friendly value.

### Persistence

1. EF Core migration adds `games.is_family_friendly boolean NOT NULL DEFAULT false`.
2. Existing rows are migrated/backfilled to false.
3. No new table is added.
4. No speculative index is added.

### Testing

1. Backend unit tests cover defaults, update behavior and filter parsing.
2. Backend integration tests cover CRUD, migration, Library filtering, Random Picker
   filtering, Steam default behavior and user isolation.
3. Frontend tests cover Add/Edit controls, display chips, Library filter and Random
   Picker filter state.
4. Playwright coverage includes at least one high-value path for create/display,
   Library filtering and Random Picker filtering.
5. Existing backend tests, frontend tests, lint, builds and E2E remain passing after
   implementation.

## Product/Domain/Architecture Amendments

- `docs/product/product-specification.md`
- `docs/domain/domain-specification.md`

No Architecture Specification change is required. Feature 012 uses the existing
Angular -> ASP.NET Core API -> EF Core/PostgreSQL architecture and existing
backend-authoritative filtering approach.

## Open Questions

None.

## Implementation

Implemented and automatically verified on 2026-10-28, preserving the existing staged,
unstaged and untracked Feature 011 work. No Git reset, revert, clean, staging,
commit or push was performed. Feature 013 was not started.

The resumed implementation completed nullable request normalization, the missing EF
migration/snapshot, form helper text, Library and Picker UI/filter wiring, positive
chips, and the required regression coverage. Existing partial domain, API, form and
query changes were retained.

### Acceptance Verification

| Acceptance criteria | Implementation and verification |
| --- | --- |
| Creation And Editing 1-5 | Both forms default off, submit explicit booleans, initialize existing values and toggle in both directions. Core normalizes missing/null create and full-form update values to false; responses return non-null booleans. Form units and PostgreSQL CRUD tests cover both types. |
| Display 1-4 | Positive-only chips on Library and both management lists; also on Picker current/history cards. True/false rendering is covered by frontend tests and the browser journey. |
| Library Filtering 1-5 | Backend common predicate applies across both types, combined using AND. Tests cover search/type/acquisition/rating and applicable platform/genre/status/player-count/interaction filters, inactive values, and invalid-filter ProblemDetails. Frontend tests cover serialization, refetch, clearing and empty results. |
| Random Picker 1-7 | All modes accept the common boolean filter. Core/PostgreSQL tests retain Owned-only eligibility, AND composition, shown exclusions and all three result states. UI tests and E2E retain shown IDs/history through toggles/mode changes and verify explicit reset. Uniform selection is unchanged. |
| Steam 1-3 | Imports explicitly default false; no metadata inference. PostgreSQL tests verify user-edited true/false values survive preview refresh and re-import while playtime refresh continues. |
| Persistence 1-4 | AddFamilyFriendly adds only the required default-false boolean column. An isolated-schema PostgreSQL upgrade test verifies existing rows of both types become false, database defaults, NOT NULL enforcement, no boolean index, and preservation of prior Steam AppID/playtime/cover data. Existing table-set assertions still pass. |
| Testing 1-5 | Complete Core, PostgreSQL integration, frontend unit, lint, production/E2E build and desktop/mobile Playwright verification passed. Results below. |

### Migrations

- Preserved unchanged: `20260827154518_AddSteamPlaytimeTracking`, including its
  designer, nullable bigint playtime column and non-negative check constraint.
- Added: `20261028185426_AddFamilyFriendly`, including its designer and updated
  `GameLibraryDbContextModelSnapshot`.
- The new migration adds `games.is_family_friendly boolean NOT NULL DEFAULT false`.
  PostgreSQL supplies false for existing rows without a separate data UPDATE.
- Real upgrades were exercised in the existing Docker PostgreSQL 16 test container
  `game-library-postgres-test` on port 5433. Integration and E2E used separate local
  test databases; no remote database migration was applied.
- `dotnet ef migrations has-pending-model-changes --project
  src/backend/GameLibrary.Core --startup-project src/backend/GameLibrary.Api
  --no-build` reported no pending changes.

### Executed Verification

| Command / suite | Result |
| --- | --- |
| `dotnet build src/backend/GameLibrary.sln --no-restore` | Passed, zero warnings/errors. |
| `dotnet test tests/backend/GameLibrary.Core.Tests/GameLibrary.Core.Tests.csproj --no-build --no-restore` | 308 passed, zero failed/skipped. |
| `dotnet test tests/backend/GameLibrary.IntegrationTests/GameLibrary.IntegrationTests.csproj --no-build --no-restore` | 345 passed, zero failed/skipped, real PostgreSQL. |
| Integration suite with `--filter FullyQualifiedName~SteamEndpointTests` | 59 passed, zero failed/skipped; included in the full integration count, not additional tests. |
| `npm test -- --watch=false` | 255 passed across 29 files. |
| `npm run lint` | Passed. |
| `npm run build` | Passed with the budget warnings below. |
| `npm run build -- --configuration e2e` | Passed. |
| `npm run e2e` | 14 passed: seven journeys each on desktop Chromium and mobile Chrome; no retries needed. |
| `git diff --check` | Passed. |

Frontend commands ran from `src/frontend`. Backend tests received the existing
container's local test connection through `ConnectionStrings__Test`; Playwright
received the isolated E2E connection through `ConnectionStrings__E2E`. Secrets were
not written to the repository. Playwright now explicitly configures the fake Steam
integration, including a dummy provider key, without requiring local user secrets.
The Family Friendly browser journey covers default-off creation, a positive card
chip, Library filtering, Picker filtering in every mode, retained history and reset;
it also checks horizontal fit at desktop/mobile viewport sizes.

### Feature Files

This inventory includes Feature 012's retained partial implementation as well as
the completion changes, not unrelated pre-existing Feature 011 or cover-search work.

Backend API, relative to `src/backend/GameLibrary.Api/`:

```text
BoardGames/BoardGameContracts.cs
VideoGames/VideoGameContracts.cs
Library/LibraryContracts.cs
RandomPicker/RandomPickerContracts.cs
Controllers/BoardGamesController.cs
Controllers/VideoGamesController.cs
Controllers/LibraryController.cs
Controllers/RandomPickerController.cs
```

Backend Core, relative to `src/backend/GameLibrary.Core/`:

```text
GameLibrary.Core.csproj
Games/Game.cs
Games/GameRules.cs
Games/BoardGameService.cs
Games/VideoGameService.cs
Data/GameLibraryDbContext.cs
Data/Migrations/20261028185426_AddFamilyFriendly.cs
Data/Migrations/20261028185426_AddFamilyFriendly.Designer.cs
Data/Migrations/GameLibraryDbContextModelSnapshot.cs
Libraries/LibraryQuery.cs
Libraries/LibraryFilterRules.cs
Libraries/LibraryService.cs
RandomPicker/RandomPickerQuery.cs
RandomPicker/RandomPickerRules.cs
RandomPicker/RandomPickerService.cs
Steam/SteamIntegrationService.cs
```

Tests, relative to `tests/backend/`:

```text
GameLibrary.Core.Tests/FamilyFriendlyRulesTests.cs
GameLibrary.Core.Tests/LibraryFilterRulesTests.cs
GameLibrary.IntegrationTests/FamilyFriendlyEndpointTests.cs
GameLibrary.IntegrationTests/DatabaseMigrationTests.cs
GameLibrary.IntegrationTests/SteamEndpointTests.cs
GameLibrary.IntegrationTests/RandomPickerEndpointTests.cs
```

Frontend, relative to `src/frontend/`:

```text
src/app/features/games/video-game.ts
src/app/features/games/board-game.ts
src/app/features/games/video-games.service.spec.ts
src/app/features/games/board-games.service.spec.ts
src/app/features/games/video-game-form/video-game-form.{ts,html,scss,spec.ts}
src/app/features/games/video-games-list/video-games-list.{html,spec.ts}
src/app/features/games/board-games/board-game-form/board-game-form.{ts,html,scss,spec.ts}
src/app/features/games/board-games/board-games-list/board-games-list.{html,spec.ts}
src/app/features/library/library.ts
src/app/features/library/library.service.{ts,spec.ts}
src/app/features/library/library-page/library-page.{ts,html,scss,spec.ts}
src/app/features/random-picker/random-picker.ts
src/app/features/random-picker/random-picker.service.spec.ts
src/app/features/random-picker/random-picker-page/random-picker-page.{ts,html,scss,spec.ts}
e2e/app.spec.ts
playwright.config.ts
```

The approved Product/Domain amendments remain in place. This specification's status
and implementation notes were updated to record actual verification.

### Remaining Issues And Scope Boundaries

The historical Picker/Steam follow-ups listed below were subsequently resolved by
the [stabilization pass](../011-steam-integration/stabilization-report.md). That
report contains the newer verification counts and the separately proposed, still
unapproved future Steam logging lifecycle.

No Feature 012-specific test failures or blocking implementation gaps remain.
Manual product review is still required. Passing regression suites do not establish
that every pre-existing Feature 011 edge case is correct.

- Production build warnings: initial bundle 703.63 kB against the 700 kB warning
  budget; Picker stylesheet 4.13 kB against the 4 kB warning budget. Error budgets
  were not exceeded or changed.
- Tooling warnings: installed EF tools 10.0.7 are older than runtime 10.0.11;
  Vitest reports the existing removed `test.poolOptions` configuration. No dependency
  upgrades or configuration refactors were introduced.
- Pre-existing Picker behavior: changing filters/mode during an outstanding pick
  can let the older response become current. The new backend filter and completed-
  request history semantics are covered; generic request lifecycle hardening is a
  separate follow-up (`random-picker-page.ts`, `sendPick` / `patchFilters`).
- Pre-existing Steam follow-ups observed by inspection: selection-derived signals
  are not refreshed on every preview/import reset, and imported candidates are not
  marked disabled until a fresh preview; duplicate imports omitted from Steam's
  latest response can be classified as unavailable rather than already imported;
  the tested playtime-delta helper is not invoked by the refresh service. These were
  not introduced by Family Friendly and were not changed in this pass
  (`manage/steam/steam-page.ts`, `SteamIntegrationService.cs`).

## Next Step

Manual review of Feature 012. Do not begin Feature 013 as part of this pass.
