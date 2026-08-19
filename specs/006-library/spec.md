# 006 — Library Browse / Search / Filter / Sort

## Status

`Approved` (manual review amendment for Feature 007 applied)

## Manual Review Amendment — 2026-08-19

Feature 007 manual review made VideoGame player-count metadata optional. Feature
006 Library must display `minimumPlayers` / `maximumPlayers` for VideoGames when
present, using the existing unified card/read-model fields.

This amendment does not add a new Library filter. The existing Feature 006
Library `playerCount` filter remains BoardGame-specific until a future approved
Library filtering amendment says otherwise. Implementations after VideoGame
player counts exist must ensure the Library player-count predicate continues to
filter BoardGames only.

## Objective

Implement the unified Library screen: browse, search, filter, and sort all of the
authenticated user's games (VideoGames and BoardGames) from one place.

After this feature is implemented, an authenticated user can:

1. Browse every game in their Library (VideoGames and BoardGames together).
2. Search by Game name (case-insensitive **literal** substring — `%`, `_`, and
   `\` are matched literally, not as PostgreSQL `LIKE` wildcards).
3. Filter by game type (All / Video games / Board games).
4. Filter by AcquisitionStatus.
5. Filter by minimum Rating.
6. Filter VideoGames by Platform, Genre, and GameStatus.
7. Filter BoardGames by player count and InteractionType.
8. Combine multiple active filters with the approved OR-within / AND-across
   semantics.
9. Sort by Name A–Z, Name Z–A, Rating highest first, Rating lowest first, or
   Recently added.
10. Clear all search and filters with one action.
11. Navigate to the existing VideoGame or BoardGame management pages from a
    Library entry.
12. Preserve strict user isolation.

This feature implements only the read-only Library browse experience.

The Random Picker remains Feature 007.

## Context

The approved Product, Domain, and Architecture Specifications are authoritative.
This specification is the sixth feature in the approved delivery order:

1. Project foundation and local development. (implemented)
2. Authentication. (implemented)
3. Platform management. (implemented)
4. VideoGame management. (implemented)
5. BoardGame management. (implemented)
6. Library browse/search/filter/sort. (this feature)
7. Random Picker.
8. PWA polish and MVP end-to-end verification.

### Actual foundation produced by Features 001–005

This specification builds on the actual repository state:

- Backend: `src/backend/GameLibrary.sln` with exactly two application projects —
  `GameLibrary.Api` (ASP.NET Core Web API, .NET 10) and `GameLibrary.Core`
  (EF Core 10.0.11, Npgsql 10.0.3). `GameLibrary.Api` references
  `GameLibrary.Core`.
- `GameLibrary.Api/Program.cs` registers controllers, problem details,
  `AddScoped<LibraryService>()`, `AddScoped<PlatformService>()`,
  `AddScoped<VideoGameService>()`, `AddScoped<BoardGameService>()`, the
  `GameLibraryDbContext` with Npgsql (from `ConnectionStrings:Default`), a
  "Frontend" CORS policy, JWT bearer authentication (OpenID Connect
  metadata/JWKS discovery, audience `authenticated`,
  `MapInboundClaims = false`, `ValidAlgorithms = [ES256, RS256]`), and
  authorization. Middleware order is `UseExceptionHandler` → `UseCors` →
  `UseAuthentication` → `UseAuthorization` → `MapControllers`.
- `GameLibrary.Core/Data/GameLibraryDbContext.cs` maps `libraries`, `platforms`,
  `games`, `library_entries`, `genres`, `game_genres`, and `game_platforms`.
  `games.game_type` is a `varchar(20)` enum-name string with the CHECK
  `game_type IN ('VideoGame', 'BoardGame')`; `games` carries the BoardGame
  columns `minimum_players`, `maximum_players`, `approximate_duration`, and
  `interaction_type` with single-table CHECK constraints enforcing
  cross-type validity. At original Feature 006 implementation time VideoGame rows
  kept all four values null; after the Feature 007 manual-review amendment,
  VideoGames may use `minimum_players` and `maximum_players` while
  `approximate_duration` and `interaction_type` remain BoardGame-only.
  `library_entries` carries `acquisition_status`, `rating`,
  `notes`, `game_status`, and `progress_percentage` with the existing CHECK
  constraints. `Game` is the principal of the required 1:1 Game ↔ LibraryEntry
  relationship; `LibraryEntry.GamePlatforms` and `Game.GameGenres` are the
  Platform and Genre associations.
- `GameLibrary.Core/Libraries/LibraryService.cs` is the shared lazy Library
  get-or-create (`EnsureAsync`). Reads never create a row. This is the exact
  service this feature extends with the browse query (it must NOT be duplicated).
- `GameLibrary.Core/Games/` holds `Game`, `LibraryEntry`, `GameType`
  (`VideoGame`, `BoardGame`), `AcquisitionStatus` (`Owned`, `Wishlist`,
  `Interested`), `GameStatus` (`Backlog`, `Playing`, `Completed`, `Abandoned`,
  `WantToPlay`), `InteractionType` (`Cooperative`, `Competitive`), `Genre`,
  `GenresCatalog`, `VideoGameService`, `BoardGameService`, and the pure rules
  modules. All enums persist as their C# enum-name strings (varchar). Game names
  are **not** unique.
- `GameLibrary.Api/Controllers/` holds `HealthController` (anonymous),
  `AuthController`, `PlatformsController`, `GenresController`,
  `VideoGamesController`, and `BoardGamesController`. Contracts live in
  `GameLibrary.Api/{VideoGames,BoardGames,Genres,Platforms}/*Contracts.cs`. EF
  entities are never exposed. The current-user helper `GetSupabaseUserId()`
  reads the JWT `sub`. Controllers derive the user only from the validated
  token and return `401` when `sub` is absent.
- Tests:
  - `tests/backend/GameLibrary.IntegrationTests/` (xUnit +
    `Microsoft.AspNetCore.Mvc.Testing`). `AuthTestFactory`, `PlatformTestFactory`,
    `VideoGameTestFactory`, and `BoardGameTestFactory` post-configure the Bearer
    JWT options to validate against a deterministic symmetric test key;
    `TestTokens.CreateToken(sub)` mints tokens for arbitrary `sub` values. The
    `[Collection("Database")]` + `IAsyncLifetime` `MigrateAsync` pattern applies
    migrations idempotently before each test. `DatabaseMigrationTests` asserts
    the exact `public` schema table set and the `game_platforms.platform_id` FK
    `ON DELETE RESTRICT`.
  - `tests/backend/GameLibrary.Core.Tests/` (xUnit) unit-tests the pure rules
    modules (`VideoGameRules`, `BoardGameRules`).
- Frontend: Angular 22 (`src/frontend/`, standalone components, SCSS, PWA via
  `@angular/service-worker`), `@supabase/supabase-js`, `AuthService` (signal
  state, session restoration, normalized errors), route guards (`authGuard`,
  `guestGuard`), the API token interceptor, and the authenticated home at `''`
  (`features/auth/home/`) which links to Platforms, Video games, and Board
  games. Routes: `''` (home, `authGuard`), `login`/`register` (guest-only),
  `health` (anonymous), `platforms` (protected), `video-games` (protected),
  `board-games` (protected). `features/games/` implements `video-game.ts`,
  `board-game.ts`, `acquisition-status.ts`, `genres.ts`, `genres.service.ts`,
  `video-games.service.ts`, `board-games.service.ts`, `video-games-list/`,
  `video-game-form/`, `board-games/`. Services use
  `${environment.apiBaseUrl}` + `HttpClient`; the existing `PlatformsService`
  and `GenresService` are reusable for resolving Platform/Genre names on the
  frontend. Frontend tests run via Vitest through `@angular/build:unit-test`
  (`ng test`).
- Configuration: committed files contain only non-secret values or placeholders;
  `appsettings.Development.json` is gitignored; `appsettings.Development.example.json`
  is the committed template. `ConnectionStrings:Default` / `ConnectionStrings:Test`
  are supplied via user-secrets or environment variables.
- The current development environment uses the remote Supabase project
  `iramzxpjbnldhebykzhx` for both PostgreSQL and Auth. No Docker is required.

### Authoritative rules this feature must preserve

The Product Specification section 18 (Library Screen) and Domain Specification
sections 20–23 define the exact behavior:

- Browse: All games, Video games, Board games. Primary visual presentation uses
  game cards.
- Search: case-insensitive substring match on Game name. Feature 006 resolves
  "substring" as **literal substring** (see Search Semantics): `%`, `_`, and `\`
  in a search value are matched literally, never as `LIKE` wildcards.
- Filters:
  - General: game type, acquisition status, rating.
  - VideoGame-specific: Platform, Genre, GameStatus.
  - BoardGame-specific: player count, InteractionType.
  - Platform, Genre, GameStatus, and InteractionType filters support multiple
    selections.
- Filter semantics:
  - Multiple selected values inside one filter type use OR / ANY-match.
  - Different active filter types combine using AND.
  - Rating means minimum rating: `Rating >= requestedRating`.
  - BoardGame player count P matches when
    `MinimumPlayers <= P <= MaximumPlayers`.
  - Missing optional metadata does not satisfy an active filter requiring it.
- Sorting: Name A–Z, Name Z–A, Rating highest first, Rating lowest first,
  Recently added. Game creation time supports Recently added.

## In Scope

- A single unified read endpoint `GET /library` in `GameLibrary.Api` returning
  the caller's VideoGames and BoardGames as one JSON array.
- Extension of the existing `LibraryService` with a browse/query method
  (`BrowseAsync`) in `GameLibrary.Core`, plus a pure `LibraryFilterRules` module
  (parse/validate) that is unit-testable without a database.
- Search, all approved filters (with the approved OR/AND and missing-metadata
  semantics), and all five approved sorts, composed server-side in one EF
  Core/PostgreSQL query.
- An authenticated Angular Library page under `src/app/features/library/`:
  - A protected `/library` route and a "Library" navigation entry on the home
    view.
  - Loading, error (with retry), empty-library, no-results, and list states.
  - Search input, game-type selector, AcquisitionStatus multi-select, minimum
    Rating selector, VideoGame-specific filters (Platform, Genre, GameStatus),
    BoardGame-specific filters (player count, InteractionType), sort selector,
    and a Clear filters action.
  - Game-card presentation resolving Platform/Genre names from the existing
    `PlatformsService` and `GenresService`.
  - Edit navigation from each card to the existing `/video-games` or
    `/board-games` management page.
  - `401` session-expired handling consistent with the existing features.
- Backend unit tests for `LibraryFilterRules`, backend integration tests against
  real PostgreSQL (including user isolation, cross-type filter behavior,
  missing-metadata semantics, sort determinism, and no-row-created-on-read), and
  frontend tests for meaningful behavior.
- README updates documenting the new feature (endpoint, query contract, filter
  semantics).

## Out of Scope

Feature 006 must NOT implement or introduce:

- Random Picker (Feature 007) — including its candidate eligibility, shown/hidden
  state, modes, or "no candidates" reporting.
- Any mutation from the Library screen: no create/edit/delete from `/library`.
  Editing navigates to the existing management pages (see Navigation).
- Pagination or infinite scroll. The MVP returns the complete matching set in one
  response (see Domain Decisions / Unified Library Read Model).
- URL/query-string synchronization of filter state, saved searches, or saved
  filters.
- Global game catalog, admin game management, or genre management.
- External integrations, automatic imports, metadata services, cover-image
  upload/storage/resizing/CDN.
- Favorites, shared libraries, family libraries, friends.
- Prices, purchase history, collection valuation.
- RLS, direct Angular access to application database tables.
- Production deployment configuration.
- End-to-end (E2E) test suites (manual acceptance verification is specified
  instead).
- New backend application projects (the existing two application projects and the
  two verification projects are reused).
- New persistence: no new tables, columns, migrations, or schema changes. Browsing
  is read-only over the existing schema.
- New frontend application architecture: no NgRx, no query-parameter syncing, no
  routing of filter state.
- MediatR, CQRS, generic repositories, a Unit of Work abstraction, event sourcing,
  domain events, message brokers, Redis, GraphQL, or shared-kernel abstractions.
- Generic filter/query frameworks, `Specification` patterns, or LINQ expression
  builders.
- SQL injection vectors or raw SQL strings composed from user input.

## Domain Decisions

### Unified Library Read Model

The Library screen browses both game types together, so the endpoint returns a
single unified JSON array. Two response shapes were compared:

**Option A — one unified `LibraryItemResponse` with type-specific nullable
fields.** Every item carries the common fields plus the VideoGame-only fields
(`platformIds`, `genreIds`, `gameStatus`, `progressPercentage`) and the
game-level player-count fields (`minimumPlayers`, `maximumPlayers`) that may be
present for BoardGames and, after the manual-review amendment, optionally for
VideoGames, plus BoardGame-only fields (`approximateDuration`,
`interactionType`). The frontend keys off `gameType`.
Type-specific fields are empty arrays / `null` for the other type. This is the
same "nullable columns with a discriminator" shape the domain model already
uses.

**Option B — separate response shapes (or two endpoints).** Two endpoints
(`/library/video-games`, `/library/board-games`) or polymorphic DTOs would
duplicate the filter contract, the frontend page, and the sort logic for zero
benefit: the frontend needs one sorted, filtered list regardless of type.

**Decision: Option A — one endpoint, one unified read model.** This matches the
Product requirement (browse All / Video games / Board games from one screen),
keeps filter combination and sorting in one place, and avoids generic
polymorphic DTO frameworks. The unified shape is:

```
LibraryItemResponse:
  id: Guid
  gameType: "VideoGame" | "BoardGame"
  name: string
  coverImageUrl: string | null
  createdAt: DateTimeOffset
  acquisitionStatus: "Owned" | "Wishlist" | "Interested"
  rating: int | null            (shared, 1–5)
  notes: string | null          (shared)
  platformIds: Guid[]            (VideoGame only; always [] for BoardGame items)
  genreIds: Guid[]               (VideoGame only; always [] for BoardGame items)
  gameStatus: string | null      (VideoGame only; null for BoardGame items)
  progressPercentage: int | null (VideoGame only; null for BoardGame items)
  minimumPlayers: int | null     (required for BoardGame; optional for VideoGame)
  maximumPlayers: int | null     (required for BoardGame; optional for VideoGame)
  approximateDuration: int | null (BoardGame only; null for VideoGame items)
  interactionType: string | null  (BoardGame only; null for VideoGame items)
```

**Platform/Genre names are resolved on the frontend**, not embedded in the
response. The item carries `platformIds`/`genreIds`; the Library page resolves
names from the existing `PlatformsService` and `GenresService`, exactly like the
VideoGames list already does. This keeps the catalog authoritative in one place
(`GET /genres` and the Platform API) and keeps the Library contract small. EF
entities are never exposed.

The API resource is read-only: no `GET /library/{id}` single-resource endpoint
is added, and `POST`/`PUT`/`DELETE` are not exposed on `/library`.

### Search Semantics

- `search` is a single-value query parameter (first occurrence is used if
  repeated).
- The value is trimmed (culture-independently); empty or whitespace-only after
  trim means "no search filter".
- When active, it filters by case-insensitive **literal** substring match on
  `games.name`, applied to both game types.
- **Literal substring semantics (approved resolution of the review finding):**
  the user's input is matched literally — the characters `%`, `_`, and `\` in a
  search value are treated as plain text, never as PostgreSQL `LIKE` wildcards or
  escape characters. This is mandatory because PostgreSQL `LIKE`/`ILIKE` treats
  `%` and `_` as wildcards and `\` as the escape character **even when bound as a
  parameter**; a naive parameterized `LIKE` (e.g. the translation of
  `g.Name.ToLower().Contains(value)`) is not literal.
- **Implementation:** compose the predicate with
  `EF.Functions.ILike(g.Name, "%" + escapedSearch + "%", "\\")` where
  `escapedSearch = LibraryFilterRules.EscapeLikePattern(NormalizeSearch(search))`
  (see `LibraryFilterRules`). `ILike` provides the case-insensitive comparison, so
  no culture-sensitive `ToLower()` call is needed; the pattern and the escape
  character (`"\\"`) are bound query parameters, so no raw SQL string is composed
  from user input and there is no SQL-injection surface.
- **Culture independence (approved resolution of the review finding):** search
  semantics must not depend on the server's current culture. `ILIKE` matching is
  case-insensitive at the database level; where any .NET lowercase
  transformation is required (e.g. in `NormalizeSearch`), it must use
  `ToLowerInvariant()`, never culture-sensitive `ToLower()`. No locale or
  collation infrastructure is introduced.
- `citext` is not used and no generated normalized-name column is added.

### Filter Combination Semantics

- **OR within a filter type:** every multi-select filter
  (`acquisitionStatuses`, `platformIds`, `genreIds`, `gameStatuses`,
  `interactionTypes`) matches an item when any selected value matches. Repeated
  query parameters are the **one canonical wire format**:
  `?platformIds=a&platformIds=b`. Comma-separated list syntax is NOT supported and
  is never parsed (see API Query Contract / FR-028).
- **AND across filter types:** every active filter type must be satisfied by the
  item. Search, gameType, ratingMin, and playerCount each count as a filter type.
- **Missing metadata never satisfies an active filter requiring it.** Concretely:
  - An active `ratingMin` requires `Rating != null && Rating >= ratingMin`.
  - An active `playerCount` requires BoardGame player ranges present and
    `MinimumPlayers <= playerCount <= MaximumPlayers` (VideoGame rows have `null`
    player counts and therefore never satisfy it).
  - An active `platformIds`/`genreIds` requires a matching `game_platforms`/
    `game_genres` row (BoardGame rows have neither and therefore never satisfy
    them).
  - An active `gameStatuses` requires `gameStatus` non-null and matching
    (non-Owned entries and BoardGame entries always have `gameStatus = null` and
    therefore never satisfy it).
  - An active `interactionTypes` requires `interactionType` non-null and matching
    (VideoGame rows always have `interactionType = null` and therefore never
    satisfy it).
- The query filters are composed server-side before ordering; no in-memory
  filtering occurs.

### Type-Specific Filters

The general filters (`gameType`, `acquisitionStatuses`, `ratingMin`, `search`,
`sort`) apply regardless of type. The type-specific filter groups are:

- **VideoGame-only:** `platformIds`, `genreIds`, `gameStatuses`. When any of
  these is active, BoardGame items are excluded automatically by the
  missing-metadata rule (see Filter Combination Semantics) — including under
  `gameType=All`. No separate "exclude BoardGames" step is needed; the
  association/column predicates exclude them naturally.
- **BoardGame-only:** `playerCount`, `interactionTypes`. When any of these is
  active, VideoGame items are excluded automatically by the missing-metadata
  rule — including under `gameType=All`.
- The VideoGame-only filter group is shown in the UI for All and Video games
  modes; the BoardGame-only filter group for All and Board games modes. Under
  All, both groups are shown. Type-specific filters stay available in All mode
  (the missing-metadata rule handles exclusion); the UI does not hide a
  type-specific group merely because `gameType=All`.

`gameStatuses` is a multi-select filter (matching the Product/Domain explicit
list of multi-select filters). `acquisitionStatuses` is also multi-select: it is
a filter type under Section 20, and the OR/ANY-match rule governs any multiple
values within it. This is a UI convenience consistent with the approved filter
semantics; it does not contradict the Domain Specification, which lists the four
filters that MUST support multiple selections without forbidding multi-select
elsewhere.

### Rating Filter

- `ratingMin` is a single integer query parameter in `[1, 5]` representing the
  minimum-rating filter (Product: "Rating means minimum rating").
- An item satisfies the filter when `rating != null && rating >= ratingMin`.
- `ratingMin` absent means no rating filter.
- Values outside `[1, 5]` are a `400` (see Validation and Errors).
- The UI presents the approved options as Any / 1+ / 2+ / 3+ / 4+ / 5+, mapping
  to no parameter, `ratingMin=1`, `ratingMin=2`, `ratingMin=3`, `ratingMin=4`,
  `ratingMin=5`.

### Player Count Filter

- `playerCount` is a single integer query parameter `>= 1`.
- An item satisfies the filter when
  `MinimumPlayers <= playerCount <= MaximumPlayers`.
- BoardGame rows always have both player counts present (Feature 005 guarantee).
  VideoGame rows may also have optional player counts after the Feature 007
  manual-review amendment, but the Feature 006 Library `playerCount` filter
  remains BoardGame-specific and excludes VideoGames.
- `playerCount` absent means no player-count filter. Values `< 1` are a `400`.
- Solo eligibility is represented only by `MinimumPlayers = 1`.

### Sorting

Five approved sort options, one value in the `sort` query parameter:

| `sort` value | Primary ordering | Deterministic tiebreakers |
| --- | --- | --- |
| `NameAsc` (default) | case-insensitive Name ascending | original Name ascending, `CreatedAt` ascending, `Id` ascending |
| `NameDesc` | case-insensitive Name descending | original Name ascending, `CreatedAt` ascending, `Id` ascending |
| `RatingDesc` | Rating descending, null ratings last | Name (case-insensitive ascending, original ascending), `CreatedAt` ascending, `Id` ascending |
| `RatingAsc` | Rating ascending, null ratings last | Name (case-insensitive ascending, original ascending), `CreatedAt` ascending, `Id` ascending |
| `RecentlyAdded` | `CreatedAt` descending | `Id` descending |

Rules:

- `NameAsc` is the default when `sort` is absent. The `NameAsc` ordering matches
  the deterministic ordering already used by the VideoGame and BoardGame list
  endpoints.
- **Null ratings always sort last**, for both Rating directions. This is
  implemented with an explicit null-position ordering (e.g.,
  `OrderBy(g => g.LibraryEntry.Rating == null).ThenByDescending(...)` /
  `.ThenBy(...)`) because PostgreSQL's default is `NULLS FIRST` for `DESC`
  ordering; an unguarded `OrderByDescending(rating)` would put null ratings
  first. The explicit ordering translates to `ORDER BY (rating IS NULL), rating
  ...`, which is deterministic.
- Every sort ends with deterministic tiebreakers so equal primary keys never
  produce an unstable order.
- Sorting is applied to the composed filtered query in the same single round
  trip.

### Empty / No-Result Behavior

- The API always returns `200` with a JSON array. When the caller has no Library
  yet, the result is `[]` and **no Library row is created on read** (consistent
  with all existing read endpoints).
- The frontend distinguishes two empty presentations from the local filter
  state only (no extra backend metadata is returned):
  - **Empty library:** no games in the Library at all AND no active search/filter
    → "No games in your library yet" with a path to the management pages
    (`/video-games`, `/board-games`).
  - **No results:** an active search or any active filter with zero matches →
    "No games match your search and filters" with a Clear filters action.
  - The decision is purely: `hasActiveCriteria = search is non-empty OR any
    filter group is non-empty`. When `hasActiveCriteria` is true, an empty list
    is the no-results state; otherwise it is the empty-library state.

### Approved UX Adjustments

Two frontend-only UX behaviors are approved as part of this feature (both are
explicitly recorded here so a future agent does not mistake them for
accidental/undocumented behavior):

- **Edit navigation from Library cards is management-list navigation (D4).**
  Edit on a VideoGame card navigates to the existing `/video-games` management
  page; Edit on a BoardGame card navigates to the existing `/board-games`
  management page. The user then locates/selects the item on that list. Feature
  006 deliberately introduces NO deep-link edit routes and NO single-resource
  `GET /video-games/{id}` / `GET /board-games/{id}` endpoints, and does not
  modify the existing management screens solely for deep linking. This is an
  accepted MVP tradeoff documented in FR-023 and Risks / Notes; a future
  UX/polish feature may allow a selected game id to auto-open the existing edit
  form without necessarily requiring a new API endpoint.
- **BoardGame Quick Add default (frontend-only, does not change Feature 005).**
  Opening a NEW BoardGame create form defaults `InteractionType` to
  `Competitive`; the user may change it to `Cooperative` or "Not set" (`null`).
  This is a frontend default only: it does NOT make `InteractionType` required,
  does not change database nullability, does not change API validation, and does
  not alter any Feature 005 BoardGame domain invariant. The domain keeps
  `InteractionType` optional exactly as approved in Feature 005.

## Technical Requirements

### Backend

Structure in `GameLibrary.Core` (domain/application rules + persistence):

```
GameLibrary.Core/
├── Libraries/
│   ├── LibraryService.cs       (extended: + BrowseAsync)
│   ├── LibraryQuery.cs         (new: LibraryQueryRequest, LibraryQuery,
│   │                            LibrarySort, LibraryItemView)
│   ├── LibraryFilterRules.cs   (new: pure parse/validation)
│   └── LibraryExceptions.cs    (new: InvalidLibraryQueryException)
└── ...                         (no other files change)
```

Structure in `GameLibrary.Api` (HTTP boundary only):

```
GameLibrary.Api/
├── Controllers/
│   └── LibraryController.cs    (new: GET /library)
└── Library/
    └── LibraryContracts.cs     (new: LibraryItemResponse)
```

`Program.cs` needs **no change**: `LibraryService` is already registered and
`LibraryController` is discovered by `MapControllers`.

#### LibraryQuery types

```csharp
// Raw query fields carried from the HTTP boundary; parsed and validated by
// LibraryFilterRules.
public sealed record LibraryQueryRequest(
    string? Search,
    string? GameType,
    IReadOnlyList<string>? AcquisitionStatuses,
    IReadOnlyList<Guid>? PlatformIds,
    IReadOnlyList<Guid>? GenreIds,
    int? RatingMin,
    int? PlayerCount,
    IReadOnlyList<string>? InteractionTypes,
    IReadOnlyList<string>? GameStatuses,
    string? Sort);

public enum LibrarySort
{
    NameAsc,
    NameDesc,
    RatingDesc,
    RatingAsc,
    RecentlyAdded,
}

// Parsed, validated query used to compose the EF query. No invalid state can be
// represented.
public sealed record LibraryQuery(
    string? Search,
    GameType? GameType,
    IReadOnlyList<AcquisitionStatus> AcquisitionStatuses,
    IReadOnlyList<Guid> PlatformIds,
    IReadOnlyList<Guid> GenreIds,
    int? RatingMin,
    int? PlayerCount,
    IReadOnlyList<InteractionType> InteractionTypes,
    IReadOnlyList<GameStatus> GameStatuses,
    LibrarySort Sort);

// Read-model projection of one Library item (Game + its LibraryEntry). Not an
// EF entity. Type-specific fields are null/empty for the other type.
public sealed record LibraryItemView(
    Guid Id,
    GameType GameType,
    string Name,
    string? CoverImageUrl,
    DateTimeOffset CreatedAt,
    AcquisitionStatus AcquisitionStatus,
    int? Rating,
    string? Notes,
    IReadOnlyList<Guid> PlatformIds,
    IReadOnlyList<Guid> GenreIds,
    GameStatus? GameStatus,
    int? ProgressPercentage,
    int? MinimumPlayers,
    int? MaximumPlayers,
    int? ApproximateDuration,
    InteractionType? InteractionType);
```

#### LibraryFilterRules (pure rules)

`LibraryFilterRules` is a static class in `GameLibrary.Core.Libraries` owning all
framework-independent parsing and validation so it can be unit-tested without a
database (mirroring the `VideoGameRules`/`BoardGameRules` pattern). At minimum it
exposes:

- `string? NormalizeSearch(string? search)` — trim using culture-independent
  behavior (`ToLowerInvariant()` only if lowercasing is ever required; no
  culture-sensitive `ToLower()`); empty/whitespace-only → `null`; otherwise
  returns the trimmed value unchanged. Wildcard escaping is NOT done here (it is
  a separate, explicit step in `EscapeLikePattern`).
- `string EscapeLikePattern(string value)` — escapes the user's search text so it
  is matched literally by `ILIKE`. Escapes in this exact order:
  1. backslash: `\` → `\\`;
  2. percent: `%` → `\%`;
  3. underscore: `_` → `\_`.
  All other characters are returned unchanged. This is the only place the
  escape logic lives (a small pure helper in `LibraryFilterRules`); the caller
  composes the final substring pattern as `"%" + escaped + "%"` and passes the
  escape character `"\\"` to `EF.Functions.ILike`. Do not move escaping into
  SQL, the controller, or raw SQL.
- `GameType? ParseGameType(string? value)` — `null` → `null`; exact
  `"VideoGame"`/`"BoardGame"` → the enum; any other value → throws
  `InvalidLibraryQueryException` ("Game type is invalid.").
- `IReadOnlyList<AcquisitionStatus> ParseAcquisitionStatuses(
  IReadOnlyList<string>? values)` — empty → `[]`; each value must be exactly
  `"Owned"`/`"Wishlist"`/`"Interested"`, else throws ("Acquisition status is
  invalid."); duplicates deduplicated (set semantics).
- `int? ValidateRatingMin(int? value)` — `null` → `null`; `1..5` → value; outside
  → throws ("Rating must be between 1 and 5.").
- `int? ValidatePlayerCount(int? value)` — `null` → `null`; `>= 1` → value;
  `< 1` → throws ("Player count must be at least 1.").
- `IReadOnlyList<InteractionType> ParseInteractionTypes(
  IReadOnlyList<string>? values)` — empty → `[]`; each value exactly
  `"Cooperative"`/`"Competitive"`, else throws ("Interaction type is invalid.");
  deduplicated.
- `IReadOnlyList<GameStatus> ParseGameStatuses(IReadOnlyList<string>? values)` —
  empty → `[]`; each value exactly `"Backlog"`/`"Playing"`/`"Completed"`/
  `"Abandoned"`/`"WantToPlay"`, else throws ("Game status is invalid.");
  deduplicated.
- `LibrarySort ParseSort(string? value)` — `null` → `NameAsc`; exact
  `"NameAsc"`/`"NameDesc"`/`"RatingDesc"`/`"RatingAsc"`/`"RecentlyAdded"` → the
  enum; any other value → throws ("Sort is invalid.").
- `LibraryQuery Parse(LibraryQueryRequest request)` — composes the above in order
  and returns a validated `LibraryQuery`. Platform/Genre ids are NOT validated
  here (well-formedness is guaranteed by model binding; unknown/foreign ids
  naturally match nothing — see Query Composition). The parsed `LibraryQuery`
  carries the normalized (trimmed) `Search` value; wildcard escaping is applied
  at query composition time via `EscapeLikePattern`, not stored on the query.

All exceptions are `InvalidLibraryQueryException` carrying the exact detail
message.

#### LibraryService.BrowseAsync

`LibraryService` (the existing shared service, namespace
`GameLibrary.Core.Libraries`) gains one method. It depends only on
`GameLibraryDbContext` (already injected). The existing `EnsureAsync` behavior is
unchanged.

- `Task<IReadOnlyList<LibraryItemView>> BrowseAsync(
  string userId, LibraryQueryRequest request, CancellationToken ct)`
  - Parses the request via `LibraryFilterRules.Parse` **before** any query work;
    invalid input throws `InvalidLibraryQueryException` with no database touch.
  - Composes one IQueryable anchored to the caller's Library:
    `.Where(g => g.LibraryEntry.Library.UserId == userId)`.
  - Applies each active filter as an EF-translatable predicate (see Query
    Composition), then applies the sort (see Sorting), then projects to
    `LibraryItemView` and executes with `ToListAsync`.
  - Returns an empty list when the caller has no Library yet; **no Library row is
    created on read** (BrowseAsync never calls `EnsureAsync`).

### Query Composition

The composed predicates (all EF Core/PostgreSQL-translatable, all parameterized,
no raw SQL):

- **Search:** `query.Search != null` →
  `EF.Functions.ILike(g.Name, "%" + escapedSearch + "%", "\\")`, where
  `escapedSearch = LibraryFilterRules.EscapeLikePattern(query.Search)`.
  `ILike` gives case-insensitive matching (no culture-sensitive .NET `ToLower`
  is used); `EscapeLikePattern` makes `\`, `%`, and `_` literal; the pattern and
  the escape character are bound query parameters (no raw SQL).
- **GameType:** `query.GameType != null` → `g.GameType == query.GameType`.
- **AcquisitionStatuses:** non-empty →
  `query.AcquisitionStatuses.Contains(g.LibraryEntry.AcquisitionStatus)`.
- **RatingMin:** `query.RatingMin != null` → `g.LibraryEntry.Rating >= query.RatingMin`
  (null ratings yield null comparisons and are excluded by the SQL `NULL`
  handling).
- **PlatformIds:** non-empty →
  `g.LibraryEntry.GamePlatforms.Any(gp => query.PlatformIds.Contains(gp.PlatformId))`.
  BoardGame rows have no `game_platforms` rows and are excluded. **Unknown or
  another user's Platform ids match nothing** — the predicate never queries the
  `platforms` table, so no Platform-existence information is exposed and no `400`
  is produced for a well-formed unknown id.
- **GenreIds:** non-empty →
  `g.GameGenres.Any(gg => query.GenreIds.Contains(gg.GenreId))`. BoardGame rows
  have no `game_genres` rows and are excluded.
- **GameStatuses:** non-empty →
  `g.LibraryEntry.GameStatus != null && query.GameStatuses.Contains(g.LibraryEntry.GameStatus.Value)`.
  Non-Owned entries and BoardGame entries have `game_status = null` and are
  excluded.
- **PlayerCount:** `query.PlayerCount != null` →
  `g.GameType == GameType.BoardGame && g.MinimumPlayers <= query.PlayerCount && g.MaximumPlayers >= query.PlayerCount`.
  This preserves the approved Feature 006 Library scope: player-count filtering is
  BoardGame-specific even though VideoGames may now display optional player counts.
- **InteractionTypes:** non-empty →
  `g.InteractionType != null && query.InteractionTypes.Contains(g.InteractionType.Value)`.
  VideoGame rows have `interaction_type = null` and are excluded.

Sort composition follows the Sorting table. The rating sorts use the explicit
null-position ordering described there.

The whole operation is one composed query and one round trip; ordering is
applied before the projection and the projection reads association ids through
the navigations (`g.LibraryEntry.GamePlatforms.Select(gp => gp.PlatformId)`,
`g.GameGenres.Select(gg => gg.GenreId)`), exactly like `VideoGameService.ListAsync`.

#### Controller

- `LibraryController` (`[ApiController]`, `[Route("library")]`, `[Authorize]`)
  exposes a single `GET` action. It derives the user from
  `User.GetSupabaseUserId()` and returns `401` when it is `null` (mirroring the
  existing controllers). It binds the ten query parameters directly:

  ```csharp
  [HttpGet]
  public async Task<IActionResult> Browse(
      [FromQuery] string? search,
      [FromQuery] string? gameType,
      [FromQuery] IReadOnlyList<string>? acquisitionStatuses,
      [FromQuery] IReadOnlyList<Guid>? platformIds,
      [FromQuery] IReadOnlyList<Guid>? genreIds,
      [FromQuery] int? ratingMin,
      [FromQuery] int? playerCount,
      [FromQuery] IReadOnlyList<string>? interactionTypes,
      [FromQuery] IReadOnlyList<string>? gameStatuses,
      [FromQuery] string? sort,
      CancellationToken ct)
  ```

  - Repeated query parameters bind natively into the list parameters
    (`?platformIds=a&platformIds=b`); a single value binds as a one-element list.
    Comma-separated values are NOT supported and are never parsed: a comma-
    containing Guid value (`?platformIds=id1,id2`) fails Guid model binding and
    produces an automatic `400`; comma-separated enum values
    (`?acquisitionStatuses=Owned,Wishlist`) are not split and fail enum/filter
    parsing with `400` (see Validation and Errors).
  - It maps `InvalidLibraryQueryException` → `400` (title "Invalid library
    query", detail = message) and maps each `LibraryItemView` to
    `LibraryItemResponse`. EF entities are never exposed.
  - It never accepts a user/owner/library ID from the request; ownership comes
    only from the validated JWT.

### API Query Contract

`GET /library` — `[Authorize]`. Request/response are JSON.

| Parameter | Type | Repeatable | Values | Absent means |
| --- | --- | --- | --- | --- |
| `search` | `string` | no (first used) | trimmed, case-insensitive **literal** substring on name (`\`, `%`, `_` matched literally) | no search |
| `gameType` | `string` | no | `VideoGame`, `BoardGame` | All (both types) |
| `acquisitionStatuses` | `string` | yes | `Owned`, `Wishlist`, `Interested` | no filter |
| `platformIds` | `Guid` | yes | any well-formed Guid | no filter |
| `genreIds` | `Guid` | yes | any well-formed Guid | no filter |
| `ratingMin` | `int` | no | 1–5 | no filter |
| `playerCount` | `int` | no | >= 1 | no filter |
| `interactionTypes` | `string` | yes | `Cooperative`, `Competitive` | no filter |
| `gameStatuses` | `string` | yes | `Backlog`, `Playing`, `Completed`, `Abandoned`, `WantToPlay` | no filter |
| `sort` | `string` | no | `NameAsc`, `NameDesc`, `RatingDesc`, `RatingAsc`, `RecentlyAdded` | `NameAsc` |

Responses:

| Method | Route | Auth | Success | Errors |
| --- | --- | --- | --- | --- |
| `GET` | `/library` | required | `200` with a JSON array of `LibraryItemResponse` containing the caller's matching games in the requested sort order | `400`, `401` |

- **Multi-select wire format (repeated keys only).** Every repeatable parameter
  (`acquisitionStatuses`, `platformIds`, `genreIds`, `gameStatuses`,
  `interactionTypes`) uses repeated keys — `?platformIds=id1&platformIds=id2`,
  `?acquisitionStatuses=Owned&acquisitionStatuses=Wishlist`, and the equivalent
  repeated-key form for `genreIds`/`gameStatuses`/`interactionTypes`. A single
  occurrence binds as a one-element list. **Comma-separated list syntax is NOT
  supported and is never parsed**: `?platformIds=id1,id2` binds the single value
  `"id1,id2"`, which is not a valid Guid, so ASP.NET Core model binding returns
  `400`; comma-separated enum values (e.g. `?acquisitionStatuses=Owned,Wishlist`)
  are not split into separate values and fail normal enum/filter parsing with
  `400`. No CSV parsing is added and no second format is supported; repeated
  parameters are the one canonical wire format (see FR-028).
- The list response is a plain JSON array. No wrapper object, no pagination, no
  metadata envelope. All matching items are returned.
- Enum values in JSON use their enum names (`"VideoGame"`, `"BoardGame"`,
  `"Owned"`, `"Wishlist"`, `"Interested"`, `"Backlog"`, `"Playing"`,
  `"Completed"`, `"Abandoned"`, `"WantToPlay"`, `"Cooperative"`,
  `"Competitive"`).
- `LibraryItemResponse` in `LibraryContracts.cs`:
  ```csharp
  public sealed record LibraryItemResponse(
      Guid Id,
      string GameType,
      string Name,
      string? CoverImageUrl,
      DateTimeOffset CreatedAt,
      string AcquisitionStatus,
      int? Rating,
      string? Notes,
      IReadOnlyList<Guid> PlatformIds,
      IReadOnlyList<Guid> GenreIds,
      string? GameStatus,
      int? ProgressPercentage,
      int? MinimumPlayers,
      int? MaximumPlayers,
      int? ApproximateDuration,
      string? InteractionType);
  ```

### Frontend

Build the feature under `src/app/features/library/` (a new sibling area;
VideoGame/BoardGame code stays under `features/games/`). No NgRx; use services,
signals, and local/component state.

Files:

```
src/app/features/library/
├── library.ts                  # LibraryItem interface + LibrarySort + filter state types
├── library.service.ts          # HTTP client for GET /library (query-string builder)
├── library.service.spec.ts
└── library-page/
    ├── library-page.ts
    ├── library-page.html
    ├── library-page.scss
    └── library-page.spec.ts
```

- `library.ts`:
  - Reuse existing shared type definitions: `AcquisitionStatus` from
    `features/games/acquisition-status.ts`, `GameStatus` from
    `features/games/video-game.ts`, `InteractionType` from
    `features/games/board-game.ts`. Do not re-declare them.
  - `export type LibraryGameType = 'VideoGame' | 'BoardGame';`
  - `export type LibrarySort = 'NameAsc' | 'NameDesc' | 'RatingDesc' | 'RatingAsc' | 'RecentlyAdded';`
  - `export interface LibraryItem { id: string; gameType: LibraryGameType; name:
    string; coverImageUrl: string | null; createdAt: string; acquisitionStatus:
    AcquisitionStatus; rating: number | null; notes: string | null; platformIds:
    string[]; genreIds: string[]; gameStatus: GameStatus | null;
    progressPercentage: number | null; minimumPlayers: number | null;
    maximumPlayers: number | null; approximateDuration: number | null;
    interactionType: InteractionType | null; }`
  - `export interface LibraryFilterState { search: string; gameType:
    LibraryGameType | null; acquisitionStatuses: AcquisitionStatus[];
    platformIds: string[]; genreIds: string[]; ratingMin: number | null;
    playerCount: number | null; interactionTypes: InteractionType[];
    gameStatuses: GameStatus[]; }` and `sort: LibrarySort`.
- `library.service.ts` (`@Injectable({ providedIn: 'root' })`): `browse(filters:
  LibraryFilterState, sort: LibrarySort): Observable<LibraryItem[]>` issues
  `GET ${environment.apiBaseUrl}/library` and builds the query string from the
  filter state:
  - omits empty/`null` values;
  - repeats multi-select parameters once per value
    (`platformIds=a&platformIds=b`) and never serializes comma-separated values
    (the repeated-key wire format is the only supported format);
  - includes `sort` always (explicit default `NameAsc`), or omits it when
    `NameAsc` (server default).
  - The existing API token interceptor attaches the bearer token automatically;
    the service does nothing special for auth.
- `library-page` component:
  - On init, loads the Platforms (via `PlatformsService.list()`) and Genres (via
    `GenresService.list()`) for name resolution, then calls `libraryService.browse`.
  - Local signals: `loading`, `items`, `error`, plus signals mirroring
    `LibraryFilterState` and `sort`. No query-string sync; filter state is
    component-local and resets on page leave.
  - Re-fetches whenever the search, any filter, or the sort changes (debounced
    briefly for the search input). "Clear filters" resets the search and all
    filter groups to their defaults and keeps the current sort.
  - Loading state: indicator while the first request is in flight. For rapid
    filter changes, stale responses are not shown (each response is checked
    against the latest request; see Risks / Notes).
  - Error state: on a non-`401` failure, shows an error message and a "Try again"
    action that re-fetches with the current filters.
  - Empty states per Empty / No-Result Behavior: "No games in your library yet"
    (with links to `/video-games` and `/board-games`) when there are no games and
    no active criteria; "No games match your search and filters" (with a Clear
    filters action) when active criteria produced zero matches.
  - List state: renders game cards in the API order. Each card shows the cover
    image or a placeholder when `coverImageUrl` is null/empty, the name, a
    type badge (`Video game` / `Board game`), AcquisitionStatus, Rating (when
    present), the VideoGame details (Platform and Genre names resolved from the
    loaded lists, GameStatus and ProgressPercentage when present, and player range
    when both VideoGame player-count values are present) or BoardGame
    details (player range, ApproximateDuration and InteractionType when present),
    and an Edit action.
  - Edit action: navigates to `/video-games` (for `gameType === 'VideoGame'`) or
    `/board-games` (for `gameType === 'BoardGame'`). This is management-list
    navigation: the user lands on the corresponding management page and locates/
    selects the item there. No deep-link edit route or single-resource
    `GET /video-games/{id}` / `GET /board-games/{id}` is introduced, and the
    existing management screens are not modified solely for deep linking (see
    Approved UX Adjustments). No inline editing on the Library page.
  - A `401` from the browse call (or the Platforms/Genres calls) clears the local
    session (`auth.clearLocalSession()`) and navigates to `/login`, exactly like
    the existing features.
  - BoardGame Quick Add default (frontend-only): opening a NEW BoardGame create
    form defaults `InteractionType` to `Competitive`; the user may change it to
    `Cooperative` or "Not set" (`null`). This is a frontend default only — it
    does not make `InteractionType` required, change database nullability or API
    validation, or alter Feature 005 domain invariants (see Approved UX
    Adjustments).

Filter UI on the page:

- Search text input.
- Game-type selector: All / Video games / Board games (`null` / `VideoGame` /
  `BoardGame`).
- AcquisitionStatus multi-select.
- Rating filter: Any / 1+ / 2+ / 3+ / 4+ / 5+ (maps to no parameter /
  `ratingMin=1..5`).
- VideoGame-specific group (shown for All and Video games): Platform multi-select
  (options from `PlatformsService`), Genre multi-select (options from
  `GenresService`), GameStatus multi-select.
- BoardGame-specific group (shown for All and Board games): player count number
  input (min 1), InteractionType multi-select (Cooperative / Competitive).
- Sort selector: Name A–Z (default), Name Z–A, Rating highest first, Rating
  lowest first, Recently added.
- Clear filters action.

### Navigation

- Add a route `{ path: 'library', component: LibraryPage, canActivate:
  [authGuard] }` to `app.routes.ts`.
- Add a "Library" `routerLink` on the authenticated home view
  (`features/auth/home/home.html`). It is the primary entry to the main library
  screen.
- Add a minimal "Back to home" `routerLink` on the Library page.
- Do not build a full application shell or shared navigation component (deferred
  to the PWA polish feature).

### Validation and Errors

Predictable behavior, consistent problem-details responses. Expected failures
are raised as typed exceptions in `GameLibrary.Core`
(`InvalidLibraryQueryException`) and mapped by the controller.

| Condition | HTTP | Problem details |
| --- | --- | --- |
| Unauthenticated request | `401` | Produced by the JWT bearer handler (with `WWW-Authenticate` challenge). |
| Authenticated principal without a usable `sub` | `401` | Controller returns `Unauthorized()` (mirrors the existing controllers). |
| `gameType` value not `VideoGame`/`BoardGame` | `400` | Title "Invalid library query", detail "Game type is invalid." |
| Any `acquisitionStatuses` value not `Owned`/`Wishlist`/`Interested` | `400` | Title "Invalid library query", detail "Acquisition status is invalid." |
| Any `interactionTypes` value not `Cooperative`/`Competitive` | `400` | Title "Invalid library query", detail "Interaction type is invalid." |
| Any `gameStatuses` value not a valid GameStatus | `400` | Title "Invalid library query", detail "Game status is invalid." |
| `ratingMin` outside 1–5 | `400` | Title "Invalid library query", detail "Rating must be between 1 and 5." |
| `playerCount` < 1 | `400` | Title "Invalid library query", detail "Player count must be at least 1." |
| `sort` value unknown | `400` | Title "Invalid library query", detail "Sort is invalid." |
| Malformed Guid in `platformIds`/`genreIds` (or any query param that fails model binding) | `400` | Automatic ASP.NET Core model-binding bad request (no custom detail). |
| Comma-separated multi-select values (`?platformIds=id1,id2`, `?acquisitionStatuses=Owned,Wishlist`) | `400` | NOT supported and never parsed: a comma-containing Guid fails model binding (automatic `400`); a comma-containing enum value is not split and fails enum/filter parsing (problem-details `400`). Repeated-key parameters are the only supported wire format. |
| Well-formed but unknown/foreign `platformIds`/`genreIds` | `200` `[]` | Not an error: the predicate matches nothing (no existence leak, no `400`). |
| Unexpected exception | `500` | Existing centralized handler (no stack traces, includes `requestId`). |

- The frontend maps `400` problem details to a compact "Invalid filters" message
  with the detail and keeps the current filter state; it never displays raw
  problem-details internals or stack traces.

### Testing

Proportionate tests. No new test framework (xUnit for backend, Vitest via `ng test`
for frontend).

#### Backend unit tests — `GameLibrary.Core.Tests`

`LibraryFilterRulesTests` tests the pure `LibraryFilterRules` behavior without a
database:

- `NormalizeSearch`: `null`/empty/whitespace → `null`; trims leading/trailing
  whitespace; preserves inner spaces; behavior is culture-independent.
- `EscapeLikePattern`: normal text is unchanged; `%` → `\%`; `_` → `\_`;
  `\` → `\\`; combinations of all three escape each wildcard exactly once in the
  documented order (e.g. `a_b\c%d` → `a\_b\\c\%d`).
- `ParseGameType`: `null` → `null`; `"VideoGame"`/`"BoardGame"` parse; unknown →
  error.
- `ParseAcquisitionStatuses`: empty/`null` → `[]`; valid values parse;
  duplicates deduplicated; unknown → error.
- `ParseInteractionTypes` / `ParseGameStatuses`: same shape with their value
  sets.
- `ValidateRatingMin`: `null` ok; 1 and 5 ok; 0 and 6 → error.
- `ValidatePlayerCount`: `null` ok; 1 ok; 0 and negative → error.
- `ParseSort`: `null` → `NameAsc`; all five values parse; unknown → error.
- `Parse` composes a valid `LibraryQuery` from a valid request and throws on the
  first invalid member.

#### Backend integration tests — existing `GameLibrary.IntegrationTests` project

Add a `LibraryTestFactory : WebApplicationFactory<Program>` mirroring the
existing factories exactly (set `ConnectionStrings:Default` to the
`ConnectionStrings__Test` environment value, post-configure the bearer
`JwtBearerOptions` with the deterministic symmetric test key, mint tokens via
`TestTokens.CreateToken(sub)` for distinct test users, `[Collection("Database")]`
+ `IAsyncLifetime` `MigrateAsync`). It may reuse the helper methods already
established by `VideoGameTestFactory`/`BoardGameTestFactory` (or small shared
helpers) to create Platforms, VideoGames, and BoardGames over the API.

Required cases (all against real PostgreSQL):

- **Unauthenticated:** `GET /library` without a token → `401`.
- **Empty library / no row on read:** a user with no Library → `200` `[]`; direct
  `GameLibraryDbContext` query shows no `libraries` row was created; after the
  user creates a VideoGame and a BoardGame, `GET /library` returns both.
- **Search:** case-insensitive substring on name (e.g., `"ma"` matches
  `"Mario"` and `"Marvel"` but not `"Zelda"`); a leading/trailing-space search
  value is trimmed before matching; empty/whitespace-only search is ignored;
  results are identical regardless of the server's culture (ILike + escaped
  pattern never depends on .NET culture).
- **Search — literal wildcard semantics:** create fixtures including a game named
  `100% Orange Juice`, a game named `a_b adventure`, and a game named
  `axb adventure`. Verify:
  - `search=100%` matches `100% Orange Juice` (the `%` is literal);
  - `search=a_b` matches `a_b adventure` and does NOT match `axb adventure`
    (the `_` is literal, not "any single character");
  - a search value containing a backslash (`search=a\\b`) is treated literally
    where practical;
  - case-insensitive substring, trimming, and whitespace-only (no filter)
    behavior still hold alongside the literal semantics;
  - the implementation remains fully parameterized (no raw SQL, no
    interpolation of user input).
- **gameType:** `gameType=VideoGame` returns only VideoGames; `gameType=BoardGame`
  only BoardGames; omitted returns both.
- **acquisitionStatuses:** `acquisitionStatuses=Owned&acquisitionStatuses=Wishlist`
  returns Owned and Wishlist entries but not Interested; a single value works.
- **ratingMin (minimum semantics):** `ratingMin=4` returns only entries with
  `rating >= 4` and excludes `rating = 3` and null ratings; `ratingMin=3` includes
  ratings 3, 4, 5.
- **platformIds:** filtering by a Platform id returns only VideoGames associated
  with it; a VideoGame without it and every BoardGame are excluded; a well-formed
  but nonexistent/foreign Platform id returns `200` `[]` (no `400`, no leak).
- **genreIds:** filtering by a Genre id returns only VideoGames carrying it;
  BoardGames excluded.
- **gameStatuses:** `gameStatuses=Playing&gameStatuses=Completed` returns only
  entries whose `gameStatus` is Playing or Completed; Wishlist/Interested entries
  and BoardGames (always `null`) are excluded.
- **playerCount:** `playerCount=4` matches a BoardGame with `minimumPlayers=2,
  maximumPlayers=6` and does not match `minimumPlayers=5, maximumPlayers=8`;
  VideoGames are excluded.
- **interactionTypes:** `interactionTypes=Cooperative` returns only Cooperative
  BoardGames; VideoGames excluded.
- **Combination (AND):** a request combining `gameType=All` (or omitted) with
  `platformIds`, `genreIds`, `ratingMin`, and `gameStatuses` returns only
  VideoGames satisfying all of them.
- **Sorting:** create fixtures with names/ratings/createdAt chosen so each sort's
  primary key and tiebreakers are exercised:
  - default (no `sort`) equals `NameAsc` (case-insensitive name ascending, then
    original name, then `createdAt`, then `id`);
  - `NameDesc`;
  - `RatingDesc` and `RatingAsc` with null ratings last in both directions
    (including at least one null-rating item to pin the explicit null-position
    ordering);
  - `RecentlyAdded` (`createdAt` descending, `id` descending).
- **Invalid query → `400`:** `ratingMin=0` and `ratingMin=6`; `playerCount=0`;
  `gameType=Foo`; `sort=Bad`; `acquisitionStatuses=Foo`;
  `interactionTypes=Foo`; `gameStatuses=Foo`; a malformed Guid in `platformIds`.
- **User isolation:** user A and B each create games (including one with the same
  name in both Libraries); A's browse never contains B's games and vice versa; B
  cannot influence A's results through any filter value. Reading never creates a
  Library row for either user.

Existing tests (`HealthEndpointTests`, `AuthEndpointTests`,
`JwtValidationConfigurationTests`, `PlatformEndpointTests`,
`VideoGameEndpointTests`, `BoardGameEndpointTests`, `DatabaseMigrationTests`)
must continue to pass unchanged. No schema/migration change is introduced, so
`DatabaseMigrationTests` needs no update.

#### Frontend tests

Vitest via `ng test`. Mock `HttpClient` (`provideHttpClientTesting`) and use the
existing `SUPABASE_CLIENT` mock where needed. Meaningful cases at minimum:

- `library.service`:
  - `browse` issues `GET /library` with the correct query string: omitted
    empty/`null` values; repeated parameters for multi-select values (never
    comma-separated); `sort` included (or omitted for the default).
  - Maps the response to `LibraryItem[]`.
- `library-page`:
  - Loading state while the request is pending.
  - List rendering of returned items (name, type badge, acquisition status,
    rating, Platform/Genre names resolved from `PlatformsService`/`GenresService`
    for VideoGames, BoardGame player range/duration/interaction for BoardGames,
    cover placeholder when `coverImageUrl` is null).
  - Empty-library state ("No games in your library yet") with no active criteria
    vs no-results state ("No games match your search and filters") with active
    criteria.
  - Search triggers a refetch; a filter change triggers a refetch; Clear filters
    resets search + filters and refetches, keeping the sort.
  - Sort change triggers a refetch with the new `sort` value.
  - Non-`401` failure shows the error state with a retry action.
  - `401` clears the local session and navigates to `/login`.
  - Edit on a VideoGame card navigates to `/video-games`; Edit on a BoardGame card
    navigates to `/board-games`.
  - Route protection: `/library` is guarded by `authGuard` (covered by the
    existing guard tests or a route-render test).

Do not over-test trivial markup.

**Manual acceptance verification** — sign in, add Platforms/VideoGames/BoardGames,
and confirm browse, search, every filter, sort, empty states, and edit navigation
in the UI. See Verification.

## Functional Requirements

`FR-001` — Unified read endpoint: `GET /library` is `[Authorize]` and returns
`200` with a JSON array of `LibraryItemResponse` containing the calling user's
matching VideoGames and BoardGames in the requested sort order. Unauthenticated →
`401`. A user with no Library gets `[]` (no row created on read).

`FR-002` — Unified read model: one `LibraryItemResponse` shape carries the common
fields plus VideoGame-only fields (empty arrays/`null` for BoardGame items),
BoardGame-only fields, and optional VideoGame player-count fields; `gameType`
discriminates. EF entities are never exposed; no `GET /library/{id}` and no
mutation endpoints on `/library`.

`FR-003` — Search: `search` is trimmed (culture-independently); empty/
whitespace-only after trim = no filter; when active it filters both game types
by case-insensitive **literal** substring on Game name, composed as a
parameterized `EF.Functions.ILike(g.Name, "%" + escapedSearch + "%", "\\")`
predicate where `escapedSearch = LibraryFilterRules.EscapeLikePattern(...)`;
`\`, `%`, and `_` in the search text are matched literally; no raw SQL is
composed and no culture-sensitive lowercasing is used.

`FR-004` — Game type filter: `gameType=VideoGame` / `gameType=BoardGame`; absent
= All (both types).

`FR-005` — AcquisitionStatus filter: `acquisitionStatuses` multi-select (repeated
params), OR within, applies to both types.

`FR-006` — Rating filter: `ratingMin` integer 1–5, minimum-rating semantics
(`rating >= ratingMin`); null ratings never satisfy an active rating filter;
values outside 1–5 → `400`.

`FR-007` — Platform filter: `platformIds` multi-select (repeated params), OR
within, VideoGame-only; BoardGames and VideoGames without a selected Platform are
excluded; well-formed unknown/foreign ids match nothing (`200` `[]`, no `400`, no
existence leak).

`FR-008` — Genre filter: `genreIds` multi-select, OR within, VideoGame-only;
BoardGames excluded when active.

`FR-009` — GameStatus filter: `gameStatuses` multi-select, OR within,
VideoGame-only; requires non-null `gameStatus`, so non-Owned entries and
BoardGames are excluded.

`FR-010` — Player count filter: `playerCount` integer `>= 1`; matches BoardGames
when `MinimumPlayers <= playerCount <= MaximumPlayers`; VideoGames remain excluded
from the Feature 006 Library player-count filter even when they have optional
player-count metadata; values `< 1` → `400`.

`FR-011` — InteractionType filter: `interactionTypes` multi-select, OR within,
BoardGame-only; requires non-null `interactionType`, so VideoGames are excluded.

`FR-012` — Filter combination: OR within each filter type, AND across filter
types; missing optional metadata never satisfies an active filter requiring it
(including type-specific filters under `gameType=All`); the query is composed
server-side in one round trip.

`FR-013` — Sorting: `sort` supports `NameAsc` (default), `NameDesc`, `RatingDesc`,
`RatingAsc`, `RecentlyAdded`; null ratings always sort last (both rating
directions, via explicit null-position ordering); every sort ends in
deterministic tiebreakers; unknown values → `400`.

`FR-014` — Invalid query handling: invalid enum values, `ratingMin` outside 1–5,
`playerCount < 1`, and unknown `sort` → `400` problem details (title "Invalid
library query", detail = message); malformed Guid values → automatic
model-binding `400`.

`FR-015` — User isolation: every browse is scoped to the authenticated user's
Library derived from the validated JWT `sub`; the API accepts no user/owner/
library ID from the request; one user can never see another user's games through
any filter.

`FR-016` — No persistence changes: browsing is read-only over the existing
schema; no tables, columns, migrations, or schema changes are introduced; reads
never create a Library row.

`FR-017` — Core authority: `LibraryFilterRules` (pure parse/validation) and
`LibraryService.BrowseAsync` (query composition) in `GameLibrary.Core` implement
all filter/sort behavior; `GameLibrary.Api` contains only the HTTP endpoint and
the response contract; `Program.cs` requires no change.

`FR-018` — Frontend route and entry: a protected `/library` route renders the
Library page; the home view links to it.

`FR-019` — Frontend states: the Library page renders loading, error (with
retry), empty-library ("No games in your library yet", with links to the
management pages), no-results ("No games match your search and filters", with
Clear filters), and list (game cards) states. Empty-library vs no-results is
decided from whether any search/filter is active.

`FR-020` — Frontend filters and search: the page exposes search, game-type
selector, AcquisitionStatus multi-select, minimum Rating selector, VideoGame-only
filters (Platform, Genre, GameStatus) shown for All/Video games, and
BoardGame-only filters (player count, InteractionType) shown for All/Board games;
changing any of them refetches; Clear filters resets search + filters and keeps
the sort.

`FR-021` — Frontend sorting: the page exposes the five approved sort options and
refetches with the selected `sort`.

`FR-022` — Frontend session expiry: a `401` from any library/Platform/Genre API
call clears the local session and navigates to `/login`.

`FR-023` — Edit navigation: Edit on a VideoGame card navigates to `/video-games`;
Edit on a BoardGame card navigates to `/board-games`; no inline editing on the
Library page. This is management-list navigation (accepted MVP tradeoff): the
user lands on the corresponding management page and locates/selects the item
there. Feature 006 adds no deep-link edit routes and no single-resource
`GET /video-games/{id}` / `GET /board-games/{id}`; the existing management
screens are not modified solely for deep linking (see Approved UX Adjustments).

`FR-024` — Backend unit tests: `LibraryFilterRulesTests` covers the pure rules
(normalize search, `EscapeLikePattern`, parse each enum filter,
rating/player-count validation, sort parsing, `Parse` composition) and passes.

`FR-025` — Backend integration tests: the required real-PostgreSQL cases
(unauthenticated, empty/no-row-on-read, search, literal wildcard search with the
`100% Orange Juice`/`a_b adventure`/`axb adventure` fixtures, comma-separated
rejection, gameType, acquisitionStatuses, ratingMin, platformIds, genreIds,
gameStatuses, playerCount, interactionTypes, AND combination, all sorts with
nulls-last rating, invalid query `400`s, user isolation) are implemented and
pass.

`FR-026` — Frontend tests: the required `library.service` and `library-page`
behaviors are tested and pass.

`FR-027` — Foundation preserved: `GET /health` stays anonymous, auth behavior is
unchanged, Platform/VideoGame/BoardGame CRUD works unchanged, the backend has
exactly two application projects, and all existing backend and frontend tests
still pass.

`FR-028` — Wire format for multi-select: multi-select filters use repeated query
parameters (`?platformIds=a&platformIds=b`), bound natively to
`IReadOnlyList<Guid>`/`IReadOnlyList<string>`; a single value binds as a
one-element list; duplicates are deduplicated. Comma-separated list syntax is
NOT supported and is never parsed: `?platformIds=id1,id2` binds a single value
that is not a valid Guid → automatic `400`; comma-separated enum values (e.g.
`?acquisitionStatuses=Owned,Wishlist`) are not split and fail enum/filter
parsing → `400`. Repeated keys are the one canonical wire format; no CSV parsing
is added.

`FR-029` — Literal search escaping: `LibraryFilterRules.EscapeLikePattern`
escapes `\` (→ `\\`), `%` (→ `\%`), and `_` (→ `\_`) in the exact documented
order so search values are matched literally; the composed ILIKE pattern and
escape character are bound query parameters (no raw SQL, no SQL injection).

`FR-030` — Culture-independent search: search normalization, matching, and
trimming never depend on the server's current culture; case-insensitivity comes
from `ILIKE`; any required .NET lowercase transformation uses
`ToLowerInvariant()`; no locale/collation infrastructure is introduced.

`FR-031` — BoardGame Quick Add default (approved frontend UX adjustment): opening
a NEW BoardGame create form defaults `InteractionType` to `Competitive`; the
user may change it to `Cooperative` or "Not set" (`null`). This is a frontend
default only: it does NOT make `InteractionType` required, change database
nullability or API validation, or alter Feature 005 BoardGame domain invariants
(see Approved UX Adjustments).

## Non-Functional Requirements

`NFR-001` — User isolation: every browse is scoped to the authenticated user's
Library derived from the validated Supabase `sub`; no client-supplied user/owner/
library ID is trusted; filter ids are compared only against the caller's own
data, so Platform/Genre existence is never leaked across users; one user can
never read another user's games.

`NFR-002` — Simplicity: the feature adds one read endpoint, one pure rules
module, one query method on the existing `LibraryService`, one controller, one
contracts file, one frontend feature folder, and no new dependencies, projects,
tables, migrations, or framework abstractions. No NgRx, MediatR, CQRS, generic
repositories, Specification patterns, LINQ expression builders, or query-string
state routing are introduced. No pagination (documented MVP tradeoff).

`NFR-003` — Performance: the browse operation is one composed EF Core/PostgreSQL
query and one round trip; ordering is applied in SQL; no in-memory filtering or
client-side sort of the full set; results are not paginated for the MVP.

`NFR-004` — Maintainability: filter parsing/validation lives in the pure
`LibraryFilterRules` module and query composition in the existing
`LibraryService`; HTTP/contract concerns live in `GameLibrary.Api`; frontend code
is local to `features/library/` and reuses existing shared types and services
(`AcquisitionStatus`, `GameStatus`, `InteractionType`, `PlatformsService`,
`GenresService`) instead of duplicating them.

`NFR-005` — Security: the validated JWT `sub` is the only trusted identity;
query values are bound as parameters (no SQL injection, no raw SQL strings from
user input); problem-details responses never expose stack traces, constraint
names, SQL, or other database internals; committed configuration remains
non-secret; no RLS is introduced; Angular never accesses application database
tables; no new secrets are committed.

`NFR-006` — Deterministic output: every sort option ends in deterministic
tiebreakers (including the explicit nulls-last rating ordering required because
PostgreSQL's default `DESC` ordering places nulls first), so repeated identical
requests return stable orderings.

`NFR-007` — Validation consistency: backend validation in `GameLibrary.Core` is
authoritative; the frontend mirrors filter value sets only for immediate UX
feedback and never overrides the backend; unknown filter values are rejected by
the backend with the same problem-details contract.

`NFR-008` — Culture independence: search normalization, matching, and trimming
never depend on the server's current culture. Case-insensitivity comes from
`ILIKE`; any required .NET lowercase transformation uses `ToLowerInvariant()`,
never culture-sensitive `ToLower()`; no locale/collation infrastructure is
introduced.

## Acceptance Criteria

`AC-001` — An unauthenticated `GET /library` returns `401`.

`AC-002` — A user with no Library gets `200` `[]` from `GET /library` and no
`libraries` row is created; after the user creates a VideoGame and a BoardGame,
`GET /library` returns both in one array.

`AC-003` — Search: `GET /library?search=ma` matches `"Mario"` and `"Marvel"`
(not `"Zelda"`) case-insensitively; `search=%20%20` (whitespace) is ignored;
search values are trimmed.

`AC-004` — `gameType=VideoGame` returns only VideoGames, `gameType=BoardGame`
only BoardGames, and an omitted `gameType` returns both.

`AC-005` — `acquisitionStatuses=Owned&acquisitionStatuses=Wishlist` returns Owned
and Wishlist entries but not Interested entries.

`AC-006` — `ratingMin=4` returns only entries with `rating >= 4` (ratings 3 and
null excluded); `ratingMin=3` includes ratings 3, 4, and 5.

`AC-007` — `platformIds=<id>` returns only VideoGames associated with that
Platform (BoardGames and unassociated VideoGames excluded); a well-formed
nonexistent or another user's Platform id returns `200` `[]` — never a `400` and
never a leak.

`AC-008` — `genreIds=<id>` returns only VideoGames carrying that Genre.

`AC-009` — `gameStatuses=Playing&gameStatuses=Completed` returns only entries
whose `gameStatus` is Playing or Completed (Wishlist/Interested entries and
BoardGames excluded).

`AC-010` — `playerCount=4` matches a BoardGame with
`minimumPlayers=2, maximumPlayers=6` and not `minimumPlayers=5, maximumPlayers=8`;
VideoGames are excluded from the Library filter even if optional player-count
metadata is present.

`AC-011` — `interactionTypes=Cooperative` returns only Cooperative BoardGames.

`AC-012` — A combined request (`platformIds` + `genreIds` + `ratingMin` +
`gameStatuses`, `gameType` omitted) returns only VideoGames satisfying all of
them (AND across filter types).

`AC-013` — Sorting: with no `sort` the result is Name A–Z (case-insensitive, then
original name, then `createdAt`, then `id`); each of `NameDesc`, `RatingDesc`,
`RatingAsc`, and `RecentlyAdded` produces its documented order; null ratings are
last in both rating directions.

`AC-014` — Invalid queries return `400`: `ratingMin=0` and `ratingMin=6`;
`playerCount=0`; `gameType=Foo`; `sort=Bad`; `acquisitionStatuses=Foo`;
`interactionTypes=Foo`; `gameStatuses=Foo`; a malformed Guid in `platformIds`.

`AC-015` — Two authenticated users A and B each create games (including one with
the same name in both Libraries): A's `GET /library` never contains B's games
and B's never contains A's; no filter value lets one user see the other's data.

`AC-016` — Browsing never creates or modifies any row: a read-only user with an
existing Library leaves `libraries`, `games`, `library_entries`, and all
association tables unchanged.

`AC-017` — Frontend: `/library` is protected by `authGuard`; the page shows
loading then a game-card list; with no games and no active criteria it shows the
empty-library state, and with active criteria and zero matches the no-results
state; search, filters, and sort refetch correctly; Clear filters resets search +
filters and keeps the sort; a `401` clears the session and navigates to `/login`;
Edit on a VideoGame card navigates to `/video-games` and on a BoardGame card to
`/board-games`.

`AC-018` — Ordinary case-insensitive substring search still works: with the
`100% Orange Juice`, `a_b adventure`, and `axb adventure` fixtures, `search=ma`
matches `"Mario"` and `"Marvel"` (not `"Zelda"`), case-insensitively, with
trimming and whitespace-only = no filter.

`AC-019` — Literal `%`: with a game named `100% Orange Juice` in the Library,
`GET /library?search=100%` matches `100% Orange Juice` (the `%` is matched
literally, not as a `LIKE` wildcard); a game named `Orange Juice` is NOT matched
by `search=100%`.

`AC-020` — Literal `_`: with games named `a_b adventure` and `axb adventure` in
the Library, `GET /library?search=a_b` matches `a_b adventure` and does NOT match
`axb adventure` (the `_` is literal, not "any single character").

`AC-021` — Literal backslash: a search value containing a backslash (e.g.
`search=a\\b`) is treated literally where practical, and never injects raw SQL
(the implementation stays fully parameterized).

`AC-022` — Culture independence: `GET /library?search=...` returns the same
result regardless of the server's current culture; case-insensitivity and
trimming never depend on culture-sensitive .NET `ToLower()`.

`AC-023` — Comma-separated multi-select values are NOT supported:
`GET /library?platformIds=id1,id2` returns `400` (a comma-containing Guid fails
model binding), and `GET /library?acquisitionStatuses=Owned,Wishlist` returns
`400` (comma-containing enum values are not split).

`AC-024` — Repeated-key multi-select works: `GET /library?platformIds=id1&platformIds=id2`
and `GET /library?acquisitionStatuses=Owned&acquisitionStatuses=Wishlist` match
the OR-within semantics; a single occurrence binds as a one-element list;
duplicates are deduplicated.

`AC-025` — Existing filter semantics are unchanged by the search/wire-format
amendments: OR-within/AND-across, missing-metadata-never-satisfies, rating
minimum, player-count range, type-specific filters under `gameType=All`, and
well-formed-but-unknown Platform ids returning `200` `[]` all behave exactly as
`AC-004`–`AC-016` document.

`AC-026` — Edit navigation remains management-list navigation: Edit on a
VideoGame card navigates to `/video-games` and on a BoardGame card to
`/board-games`; no deep-link edit route and no single-resource
`GET /video-games/{id}` / `GET /board-games/{id}` are introduced by Feature 006.

`AC-027` — BoardGame Quick Add default (frontend regression/UX): opening a NEW
BoardGame create form defaults `InteractionType` to `Competitive`; the user may
change it to `Cooperative` or "Not set" (`null`); `InteractionType` remains
optional in the domain and backend validation/nullability are unchanged (this is
a frontend-only default, not a backend Acceptance Criterion).

## Verification

Each acceptance criterion has verification coverage:

- `AC-001`, `AC-002`, `AC-003`, `AC-004`, `AC-005`, `AC-006`, `AC-007`, `AC-008`,
  `AC-009`, `AC-010`, `AC-011`, `AC-012`, `AC-013`, `AC-014`, `AC-015`, `AC-016`,
  `AC-018`, `AC-019`, `AC-020`, `AC-021`, `AC-022`, `AC-023`, `AC-024`, `AC-025`
  — `LibraryEndpointTests` integration tests against real PostgreSQL (see
  Testing), plus `LibraryFilterRulesTests` for the pure parsing/validation behind
  `AC-014`, `AC-018`–`AC-021` (search normalization + `EscapeLikePattern`), and
  `AC-023`/`AC-024` (wire-format binding).
- `AC-017`, `AC-026` — `library.service.spec.ts` and `library-page.spec.ts` (see
  Testing), plus the existing route-guard coverage.
- `AC-027` — `board-game-form.spec.ts` frontend test plus manual UI verification
  (a new BoardGame create form defaults `InteractionType` to `Competitive`).
- Backend verification commands: build the solution (`dotnet build`), run the
  unit tests (`dotnet test tests/backend/GameLibrary.Core.Tests`), and run the
  integration tests against the configured real PostgreSQL test database
  (`dotnet test tests/backend/GameLibrary.IntegrationTests`). Frontend: `ng test`
  from `src/frontend`.
- Manual acceptance verification (see Testing / Manual acceptance verification):
  sign in against the development Supabase project, add Platforms, VideoGames,
  and BoardGames (including fixtures named `100% Orange Juice`, `a_b adventure`,
  and `axb adventure`), and confirm browse, search (including literal `%`/`_`
  behavior), every filter, every sort, both empty states, Clear filters, and Edit
  navigation in the UI. Confirm the result is exactly the same set the backend
  returns, that other-user data never appears, and that a new BoardGame create
  form defaults `InteractionType` to `Competitive`.
- Definition of Done: acceptance criteria satisfied, Domain invariants preserved
  (no weakening of OR/AND, missing-metadata, rating-minimum, player-count, or
  literal-substring search semantics; repeated-key wire format preserved),
  relevant tests pass, build succeeds, lint/type checks pass, no
  schema/migration change was introduced, no unrelated scope, and no secrets
  committed.

## Dependencies

- Features 001–005 (implemented): authentication, the shared `LibraryService`,
  Platforms, VideoGames, BoardGames, the Genre catalog, and the real-PostgreSQL
  integration test infrastructure.
- The existing frontend `PlatformsService`, `GenresService`, `AuthService`,
  guards, and shared game type definitions.
- The current development environment (remote Supabase project
  `iramzxpjbnldhebykzhx` for PostgreSQL and Auth).
- No new dependencies are introduced.

## Risks / Notes

- **Literal substring search via ILIKE + escaping (resolves the review
  finding).** Parameterization alone does NOT make PostgreSQL `LIKE`/`ILIKE`
  treat `%`, `_`, or backslash literally — they remain wildcard/escape characters
  even inside a bound parameter. The approved implementation therefore escapes
  the normalized search value (`LibraryFilterRules.EscapeLikePattern`:
  `\` → `\\`, `%` → `\%`, `_` → `\_`) and composes
  `EF.Functions.ILike(g.Name, "%" + escapedSearch + "%", "\\")`. The pattern and
  escape character remain bound query parameters; no raw SQL and no interpolation
  of user input. `citext` and generated normalized-name columns are intentionally
  not used. Integration tests pin this behavior with the `100% Orange Juice`,
  `a_b adventure`, and `axb adventure` fixtures.
- **Culture independence (resolves the review finding).** Search semantics must
  not depend on the server's current culture. `ILIKE` provides case-insensitive
  matching, so no culture-sensitive `ToLower()` is used; any required .NET
  lowercase transformation uses `ToLowerInvariant()`. No locale/collation
  infrastructure is introduced. Integration tests verify a search returns the
  same result regardless of server culture.
- **PostgreSQL null ordering for `DESC`:** PostgreSQL orders nulls first for
  `DESC` by default, which would break "null ratings last". The rating sorts use
  an explicit null-position ordering; integration tests pin this with at least
  one null-rating fixture.
- **No pagination:** returning the complete matching set in one response is a
  deliberate MVP tradeoff consistent with the existing list endpoints. A future
  feature may introduce pagination/virtualized lists; nothing in this feature
  assumes a specific result size.
- **Filter state is not persisted or URL-synced:** the Library page keeps filter
  state in component signals only; a page leave/refresh resets it. This is a
  deliberate MVP scope decision.
- **Rapid filter changes:** a briefly debounced search input and stale-response
  guarding (ignore responses that are no longer the latest request) are
  recommended to keep the UI correct; the backend remains the authority on the
  result set.
- **No schema change:** this feature is read-only over the existing schema, so
  `DatabaseMigrationTests` needs no update. Do not introduce one to "improve"
  filtering (e.g., no generated lower(name) column, no index) unless a
  performance issue is demonstrated and approved through an architecture change.
- **Filter ids never resolve against reference tables:** Platform/Genre filter
  ids are compared only against the caller's own `game_platforms`/`game_genres`
  rows. This is what makes unknown/foreign ids safe (empty result, no leak). A
  future agent must not "improve" this by resolving ids against the catalog or
  `platforms` table and returning a `400` for unknown ids.
- **Wire format is repeated keys only:** multi-select filters use repeated query
  parameters and never comma-separated values. Comma-separated input is NOT
  parsed and returns `400` (Guid model binding / enum parsing). A future agent
  must not add CSV parsing or a second wire format.
- **Edit navigation is management-list navigation (accepted MVP tradeoff):**
  Library card Edit navigates to `/video-games` or `/board-games`; the user lands
  on the corresponding management page and locates/selects the item there.
  Feature 006 deliberately adds no deep-link edit routes and no single-resource
  `GET /video-games/{id}` / `GET /board-games/{id}`, and does not modify the
  existing management screens solely for deep linking. A future UX/polish feature
  may allow a selected game id to auto-open the existing edit form without
  necessarily requiring a new API endpoint.
- **BoardGame Quick Add default (approved UX adjustment):** a new BoardGame create
  form defaults `InteractionType` to `Competitive` (user may choose `Cooperative`
  or "Not set"). Frontend-only: it does NOT make `InteractionType` required,
  change database nullability or API validation, or alter Feature 005 domain
  invariants. Recorded in Approved UX Adjustments so future agents treat it as
  intended behavior, not a regression.

## Open Questions

None. All behavior is defined by the Product/Domain Specifications or decided
above.
