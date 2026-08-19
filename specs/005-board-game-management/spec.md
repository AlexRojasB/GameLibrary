# 005 — BoardGame Management

## Status

`Approved` (manual review amendment for Feature 007 applied)

## Manual Review Amendment — 2026-08-19

Feature 007 manual review changed the approved meaning of
`games.minimum_players` and `games.maximum_players`: those two columns are now
shared game-level player-count metadata used by BoardGames and optionally by
VideoGames. BoardGame behavior is unchanged: BoardGames still require both
values and must satisfy `1 <= MinimumPlayers <= MaximumPlayers`.

This amendment supersedes earlier Feature 005 wording that VideoGame rows must
keep all four BoardGame columns null. After the targeted implementation,
VideoGame rows may use `minimum_players` and `maximum_players`; only
`approximate_duration` and `interaction_type` remain BoardGame-only.

## Objective

Implement the MVP management of a user's BoardGames.

After this feature is implemented, an authenticated user can:

1. View their BoardGames.
2. Create a BoardGame quickly.
3. Edit a BoardGame.
4. Delete a BoardGame.
5. Set MinimumPlayers.
6. Set MaximumPlayers.
7. Set optional ApproximateDuration in minutes.
8. Set optional InteractionType.
9. Set AcquisitionStatus.
10. Set Rating.
11. Set Notes.
12. Set an optional CoverImageUrl.
13. Preserve strict user isolation.
14. Reuse the approved `Game` / `LibraryEntry` domain model introduced by Feature 004.
15. Keep BoardGames completely separate from Platforms.

This feature implements only BoardGame management.

The unified Library screen remains Feature 006.

The Random Picker remains Feature 007.

## Context

The approved Product, Domain, and Architecture Specifications are authoritative.
This specification is the fifth feature in the approved delivery order:

1. Project foundation and local development. (implemented)
2. Authentication. (implemented)
3. Platform management. (implemented)
4. VideoGame management. (implemented)
5. BoardGame management. (this feature)
6. Library browse/search/filter/sort.
7. Random Picker.
8. PWA polish and MVP end-to-end verification.

### Actual foundation produced by Features 001–004

This specification builds on the actual repository state:

- Backend: `src/backend/GameLibrary.sln` with exactly two application projects —
  `GameLibrary.Api` (ASP.NET Core Web API, .NET 10) and `GameLibrary.Core`
  (EF Core 10.0.11, Npgsql 10.0.3). `GameLibrary.Api` references
  `GameLibrary.Core`.
- `GameLibrary.Api/Program.cs` registers controllers, problem details,
  `AddScoped<LibraryService>()`, `AddScoped<PlatformService>()`,
  `AddScoped<VideoGameService>()`, the `GameLibraryDbContext` with Npgsql (from
  `ConnectionStrings:Default`), a "Frontend" CORS policy, JWT bearer
  authentication (OpenID Connect metadata/JWKS discovery, audience
  `authenticated`, `MapInboundClaims = false`, `ValidAlgorithms = [ES256, RS256]`),
  and authorization. Middleware order is `UseExceptionHandler` → `UseCors` →
  `UseAuthentication` → `UseAuthorization` → `MapControllers`.
- `GameLibrary.Core/Data/GameLibraryDbContext.cs` maps `libraries`, `platforms`,
  `games`, `library_entries`, `genres`, `game_genres`, and `game_platforms` with
  explicit snake_case table/column names. `games.game_type` is mapped to a
  `varchar(20)` enum-name string with the CHECK
  `ck_games_game_type` = `game_type IN ('VideoGame', 'BoardGame')`. All mapping is
  configured inline in `OnModelCreating`. No `IEntityTypeConfiguration` classes
  exist.
- Migrations: the empty `InitialCreate`, `AddPlatformManagement` (creates
  `libraries`, `platforms`), and `AddVideoGameManagement` (creates `games`,
  `library_entries`, `genres` seeded with the 15 approved catalog values,
  `game_genres`, and `game_platforms`; adds the `ix_library_entries_game_id`
  unique index and the CHECK constraints `ck_library_entries_rating`,
  `ck_library_entries_progress_percentage`, and
  `ck_library_entries_owned_status_progress`).
- `GameLibrary.Core/Games/` holds the shared game model:
  - `Game` (Id, GameType, Name, CoverImageUrl, CreatedAt, LibraryEntry,
    GameGenres) — the principal of the 1:1 Game ↔ LibraryEntry relationship.
  - `GameType` (`VideoGame`, `BoardGame`), `AcquisitionStatus` (`Owned`,
    `Wishlist`, `Interested`), `GameStatus` (VideoGame-only).
  - `LibraryEntry` (LibraryId, GameId, AcquisitionStatus, Rating, Notes,
    GameStatus, ProgressPercentage, GamePlatforms).
  - `Genre`, `GameGenre`, `GamePlatform`, `GenresCatalog`.
  - `VideoGameRules` (pure rules), `VideoGameService`, `VideoGameExceptions`
    (`InvalidVideoGameException`, `VideoGameNotFoundException`).
  - The VideoGame feature established the required `Game.GameType ==
    GameType.VideoGame` guard on every VideoGame operation: `ListAsync` filters
    by GameType, and `LoadOwnedAsync` returns `404 Video game not found` for any
    Game that exists, belongs to the user, but has another GameType.
- `GameLibrary.Core/Libraries/LibraryService.cs` is the shared lazy Library
  get-or-create (`EnsureAsync`), used by `PlatformService` and `VideoGameService`.
- `GameLibrary.Api/Controllers/` holds `HealthController` (anonymous),
  `AuthController`, `PlatformsController`, `GenresController` (read-only catalog),
  and `VideoGamesController`. Contracts live in
  `GameLibrary.Api/VideoGames/VideoGameContracts.cs`,
  `GameLibrary.Api/Genres/GenreContracts.cs`, and
  `GameLibrary.Api/Platforms/PlatformContracts.cs`. EF entities are never exposed.
  The current-user helper `GetSupabaseUserId()` reads the JWT `sub`.
- Tests:
  - `tests/backend/GameLibrary.IntegrationTests/` (xUnit +
    `Microsoft.AspNetCore.Mvc.Testing`). `AuthTestFactory`,
    `PlatformTestFactory`, and `VideoGameTestFactory` post-configure the Bearer
    JWT options to validate against a deterministic symmetric test key;
    `TestTokens.CreateToken(sub)` mints tokens for arbitrary `sub` values.
    `DatabaseMigrationTests` asserts the `public` schema contains exactly
    `__EFMigrationsHistory`, `libraries`, `platforms`, `games`, `library_entries`,
    `genres`, `game_genres`, and `game_platforms` (order-independent), and that the
    `game_platforms.platform_id` FK is `ON DELETE RESTRICT`.
  - `tests/backend/GameLibrary.Core.Tests/` (xUnit) unit-tests `VideoGameRules`.
- Frontend: Angular 22 (`src/frontend/`, standalone components, SCSS, PWA via
  `@angular/service-worker`), `@supabase/supabase-js`, `AuthService` (signal
  state, session restoration, normalized errors), route guards (`authGuard`,
  `guestGuard`), the API token interceptor, and the authenticated home at `''`
  (`features/auth/home/`) which links to Platforms and Video games. Routes:
  `''` (home, `authGuard`), `login`/`register` (guest-only), `health`
  (anonymous), `platforms` (protected), `video-games` (protected).
  `features/games/` implements `genres.ts`/`genres.service.ts`,
  `video-game.ts`, `video-games.service.ts`, `video-game-form/`, and
  `video-games-list/`. Frontend tests run via Vitest through
  `@angular/build:unit-test` (`ng test`).
- Configuration: committed files contain only non-secret values or placeholders;
  `appsettings.Development.json` is gitignored; `appsettings.Development.example.json`
  is the committed template. `ConnectionStrings:Default` / `ConnectionStrings:Test`
  are supplied via user-secrets or environment variables.
- The current development environment uses the remote Supabase project
  `iramzxpjbnldhebykzhx` for both PostgreSQL and Auth. No Docker is required.

### Domain model driving this feature

The approved Domain Specification defines BoardGame as a `Game` (game-level data)
with BoardGame-specific structural information:

- Game-level (`games`): Name, optional CoverImageUrl, creation time, plus
  BoardGame-specific MinimumPlayers, MaximumPlayers, optional
  ApproximateDuration, and optional InteractionType.
- LibraryEntry-level (`library_entries`): AcquisitionStatus, optional Rating,
  optional Notes.

BoardGames do NOT use Platforms, Genres, GameStatus, or ProgressPercentage. The
BoardGame quick-create requires Name, MinimumPlayers, MaximumPlayers, and
AcquisitionStatus (default Owned); all other metadata is optional.

## In Scope

- BoardGame-specific persistence on the existing `games` table (see Domain
  Decisions), created through one EF Core migration.
- A `BoardGameRules` pure rules module and a `BoardGameService` application
  service in `GameLibrary.Core`, plus the `InteractionType` enum.
- A `BoardGamesController` in `GameLibrary.Api` exposing the CRUD endpoints and
  the BoardGame request/response contracts, scoped to the authenticated Supabase
  user ID from the validated JWT `sub`.
- Symmetric `GameType == GameType.BoardGame` guards on every BoardGame backend
  operation so BoardGames and VideoGames never leak into each other's endpoints.
- An authenticated Angular BoardGame management UI under
  `src/app/features/games/board-games/`:
  - List page (loading, empty, error, list states).
  - Create flow.
  - Edit flow.
  - Delete flow with confirmation.
  - A form covering Name, MinimumPlayers, MaximumPlayers, ApproximateDuration,
    InteractionType, AcquisitionStatus, Rating, Notes, and CoverImageUrl, with
    client validation mirroring the backend rules.
  - Validation errors and API error handling (including `401` session-expired
    handling consistent with the VideoGames and Platforms features).
- A minimal "Board games" navigation entry from the authenticated home view and a
  way back, without designing the final application shell.
- Backend unit tests (`BoardGameRulesTests` in the existing
  `GameLibrary.Core.Tests` project), backend integration tests against real
  PostgreSQL (including two-user isolation and cross-type safety), and frontend
  tests for meaningful behavior.
- README updates documenting the new feature (usage, endpoints, schema).

## Out of Scope

Feature 005 must NOT implement or introduce:

- The unified Library screen, Library search, filters, or sorting (Feature 006).
- Random Picker (Feature 007).
- BoardGame Family/Party/Solo categories, complexity, or any board-game category
  outside the approved Domain.
- BoardGame genres, expansions, persistent play sessions, or notifications.
- Any change to Platform behavior, Platform deletion protection, or the
  `game_platforms` join (BoardGames never touch Platforms).
- Any change to VideoGame behavior or its endpoints beyond what Feature 005
  requires for cross-type safety (which Feature 004 already guards).
- Genres on BoardGames: no `game_genres` rows are ever created for BoardGames and
  no `genreIds` field appears in BoardGame contracts.
- GameStatus / ProgressPercentage on BoardGames: no such fields appear in
  BoardGame contracts, and the backend guarantees the existing
  `library_entries` columns remain `null` for BoardGame entries.
- External metadata APIs, BoardGameGeek integration, cover image upload, object
  storage, resizing, or CDN infrastructure.
- RLS, direct Angular access to application database tables.
- Favorites, shared libraries, family libraries, friends.
- Prices, purchase history, collection valuation.
- Production deployment configuration.
- End-to-end (E2E) test suites (manual acceptance verification is specified
  instead).
- New backend application projects (the existing two application projects remain;
  the existing `GameLibrary.Core.Tests` verification project is reused).
- NgRx, MediatR, CQRS, generic repositories, a Unit of Work abstraction, event
  sourcing, domain events, message brokers, Redis, GraphQL, or shared-kernel
  abstractions.
- EF inheritance (no TPH/TPT/TPC), polymorphic DTO frameworks, generic game
  controllers, `IGameService<T>`, or generic CRUD base services.
- PostgreSQL enum types for the new enum column (it is mapped to `varchar`; see
  Database / EF Core).
- Optimistic-concurrency tokens, version columns, locks, or concurrency
  frameworks.

## Domain Decisions

### BoardGame Persistence Representation

BoardGame structural fields (MinimumPlayers, MaximumPlayers, ApproximateDuration,
InteractionType) are **Game-level data** per the approved Domain Specification,
so they must not be stored in `library_entries`. The decision is where to
persist them. Two options were compared:

**Option A — add nullable BoardGame/player-count columns to `games`.**
`games` gains `minimum_players`, `maximum_players`, `approximate_duration`, and
`interaction_type`, all nullable. After the manual-review amendment,
`minimum_players` and `maximum_players` are shared player-count metadata;
`approximate_duration` and `interaction_type` remain BoardGame-only. Single-table
CHECK constraints enforce validity. No new table, no new FK, no extra join.

**Option B — a `board_games` subtype table keyed 1:1 to `games.id`.**
A separate table would strictly separate VideoGame and BoardGame schema, but
introduces a new table, a 1:1 FK, delete-ordering concerns (the subtype row must
be removed with the Game), an extra navigation, and an eager-load/join on every
list query — all for a single subtype with only four columns.

**Decision: Option A — add nullable BoardGame-specific columns to `games`.**

Rationale:

- The four fields are game-level data and `games` is the natural home of
  game-level data. Adding four nullable columns does not overload the table
  irresponsibly.
- Cross-type validity is enforceable with **single-table** CHECK constraints, so
  no complicated cross-table constraints or triggers are needed.
- This is the same pattern Feature 004 established for `library_entries`, where
  VideoGame-only `game_status` and `progress_percentage` are nullable columns
  with a CHECK backstop.
- Option B's benefits (structural separation) buy nothing here: the Application
  layer's `GameType` guard is authoritative, and Option B would add a table, a
  FK, delete ordering, and a join for zero current benefit.
- No EF inheritance is introduced under either option.

The complete GameType-specific validity rule after the manual-review amendment is
therefore:

- `Game.GameType == GameType.VideoGame` ⇒ `MinimumPlayers` and `MaximumPlayers`
  are optional but both-or-neither; when present they satisfy
  `1 <= MinimumPlayers <= MaximumPlayers`; `ApproximateDuration` and
  `InteractionType` are `NULL`.
- `Game.GameType == GameType.BoardGame` ⇒ `MinimumPlayers` and `MaximumPlayers`
  are present; `ApproximateDuration` and `InteractionType` are optional.

The application layer (BoardGameService, see BoardGame Service) is authoritative
for which operations may create/modify these columns; the single-table CHECK
constraints are the integrity backstop (see Database / EF Core).

### BoardGame Model

Game-level (`games` table) additions:

| Property | Type | Notes |
| --- | --- | --- |
| `Id` | `Guid` | Primary key, generated by the application on create. (existing) |
| `GameType` | enum | Discriminator. BoardGame operations require `GameType.BoardGame`. |
| `Name` | `string` | Required, trimmed, max 100 characters. Game names are **not** unique. |
| `CoverImageUrl` | `string?` | Optional manual external URL. No upload/storage/resizing/CDN. |
| `CreatedAt` | `DateTimeOffset` | Server-assigned creation timestamp. Not editable. |
| `MinimumPlayers` | `int?` | Required for BoardGame rows; optional for VideoGame rows. When present, must be `>= 1`. |
| `MaximumPlayers` | `int?` | Required for BoardGame rows; optional for VideoGame rows. When present, must be `>= MinimumPlayers`. |
| `ApproximateDuration` | `int?` | Optional single value in minutes; `> 0` when provided. Null for VideoGame rows. |
| `InteractionType` | `InteractionType?` | Optional enum; `Cooperative` / `Competitive`. Null for VideoGame rows. |

LibraryEntry-level (`library_entries` table) — unchanged from Feature 004:

| Property | Type | Notes |
| --- | --- | --- |
| `Id` | `Guid` | Primary key, generated by the application on create. |
| `LibraryId` | `Guid` | FK → `libraries.id`. Ownership is always derived from the authenticated user through this Library. |
| `GameId` | `Guid` | FK → `games.id`. Required. Unique index (a Game can be referenced by at most one LibraryEntry). |
| `AcquisitionStatus` | enum | `Owned` / `Wishlist` / `Interested`. Default `Owned` on create. |
| `Rating` | `int?` | Optional, 1–5. |
| `Notes` | `string?` | Optional personal text, max 5000 characters, no rich text. |
| `GameStatus` | enum? | **Unused by BoardGames.** Must remain `null` for BoardGame entries. |
| `ProgressPercentage` | `int?` | **Unused by BoardGames.** Must remain `null` for BoardGame entries. |

The API resource identifier is the **Game id** (`games.id`). The route is
`/board-games/{id}`; the `BoardGameResponse.id` is the Game id. Internally every
BoardGame maps to exactly one LibraryEntry (unique `game_id`), so every operation
resolves Game → LibraryEntry → Library and verifies the Library's `user_id` equals
the authenticated `sub`. **Every BoardGame backend operation additionally requires
`Game.GameType == GameType.BoardGame`** (see Cross-Type Safety): a Game row with
any other `game_type` is outside this feature's scope and behaves exactly like a
nonexistent BoardGame (`404 Board game not found`).

### Player Count Rules

- `MinimumPlayers >= 1`.
- `MaximumPlayers >= MinimumPlayers`.
- Both are required for every BoardGame create and update.
- The core invariant is `1 <= MinimumPlayers <= MaximumPlayers`.
- **No artificial business maximums are invented.** The PostgreSQL `integer` type
  and the .NET `int` type provide the technical ceiling; a value outside the
  .NET `int` range is rejected by JSON model binding as `400`. A documented
  non-goal: no player-count catalog, no "party/solo" semantics, no maximum based
  on game design. Solo eligibility is represented only by
  `MinimumPlayers = 1`.

### Duration Rules

- `ApproximateDuration` is optional and represents **one** approximate duration
  in minutes.
- When provided: `ApproximateDuration > 0`.
- No duration ranges, min/max duration, sessions, or hours/minutes composite
  models are introduced. The single integer minute value is the whole model.
- No artificial business maximum is defined beyond the `integer`/`int` type
  ceiling described under Player Count Rules.

### InteractionType Rules

- Supported values exactly: `Cooperative`, `Competitive`.
- Optional. `null` means "not set".
- No additional values are supported in this feature: no `SemiCooperative`,
  `TeamBased`, `Party`, `Family`, `Solo`, or `Complexity`.
- Solo eligibility is represented only by `MinimumPlayers = 1`.
- Parsing accepts only the exact enum-name strings (`"Cooperative"`,
  `"Competitive"`); any other non-null string is a `400`.
- The frontend must never infer InteractionType automatically (see Frontend).

### AcquisitionStatus Rules

Supported values: `Owned`, `Wishlist`, `Interested`.

Rules, stated so no agent guesses transition behavior:

- On **create**, when `acquisitionStatus` is omitted or `null`, the default is
  `Owned`.
- On **update**, `acquisitionStatus` is required; a `null`/omitted value is a
  `400` ("Acquisition status is required."). An unknown string value is a `400`
  ("Acquisition status is invalid.").
- **Unlike VideoGame, BoardGame acquisition status has no Platform requirement.**
  Owned, Wishlist, and Interested may all exist without any Platform, because
  BoardGames do not use Platforms.
- **There is no GameStatus or ProgressPercentage on BoardGame entries**, and
  changing AcquisitionStatus does not require any special transition clearing or
  preservation: Rating and Notes remain normal mutable LibraryEntry fields and
  always follow normal PUT full-replacement semantics.
- **Feature 004's Owned → Wishlist/Interested backend-authoritative preservation
  rule does NOT apply to BoardGames.** Do not copy VideoGame-specific transition
  behavior where it does not apply. A BoardGame update always fully replaces
  Rating and Notes regardless of the resulting AcquisitionStatus.
- The resulting state never violates any Domain invariant: the existing
  `library_entries` CHECK
  `acquisition_status = 'Owned' OR (game_status IS NULL AND progress_percentage IS NULL)`
  is always satisfied because BoardGame entries always keep both columns `null`.

### Game / LibraryEntry Reuse

- BoardGames reuse the existing `games` and `library_entries` tables and the
  existing `Game`, `LibraryEntry`, `AcquisitionStatus`, and `GameType` concepts.
- **No second ownership model is created.** Ownership continues to flow through
  `libraries.user_id` → `library_entries.library_id` → `games.id`.
- The lazy Library get-or-create continues through the shared
  `LibraryService.EnsureAsync` (no change to its behavior).
- BoardGame-specific structural fields are stored on `games` (Option A) and are
  never stored in `library_entries`. LibraryEntry remains personal/user-specific:
  AcquisitionStatus, Rating, Notes.
- No second Game or LibraryEntry entity, no subtype entity, and no EF inheritance
  are introduced.

### Cross-Type Safety

This is important and is enforced symmetrically.

- Every BoardGame backend operation requires `Game.GameType == GameType.BoardGame`:
  - `GET /board-games` returns only Games with `GameType.BoardGame` in the
    caller's Library.
  - `PUT`/`DELETE /board-games/{id}` return `404 Board game not found` for a Game
    id whose `GameType` is not `BoardGame`, indistinguishable from a nonexistent
    BoardGame (see `LoadOwnedAsync`).
- Feature 004 already enforces the symmetric VideoGame guard:
  - `GET /video-games` returns only Games with `GameType.VideoGame`.
  - `PUT`/`DELETE /video-games/{id}` return `404` for a BoardGame id.
- Required behavior (pinned by integration tests):
  1. BoardGames never appear in `GET /video-games`.
  2. VideoGames never appear in `GET /board-games`.
  3. `PUT /video-games/{boardGameId}` → `404`.
  4. `DELETE /video-games/{boardGameId}` → `404`.
  5. `PUT /board-games/{videoGameId}` → `404`.
  6. `DELETE /board-games/{videoGameId}` → `404`.
- **Do not leak cross-type existence.** A Game that exists, belongs to the caller,
  but has the other `GameType` behaves exactly like a nonexistent resource of the
  requested type (the same `404`).

### Deletion Rules

- `DELETE /board-games/{id}` deletes the caller's BoardGame LibraryEntry **and**
  its associated Game in one transaction (the approved MVP rule: deleting a
  LibraryEntry also deletes its Game; no orphan Games). The service removes (1)
  the LibraryEntry and (2) the Game as one application operation/transaction, in
  the same order and using the same approach as `VideoGameService.DeleteAsync`
  (dependent LibraryEntry removed before the principal Game to satisfy the
  `library_entries.game_id → games.id` `ON DELETE RESTRICT` FK; no FK error
  surfaces).
- Because Option A stores BoardGame fields on `games`, deleting the Game removes
  them with it. There is no BoardGame-specific subtype row to delete.
- BoardGame entries have no `game_platforms` or `game_genres` rows (see Platforms
  and Genres below), so deletion removes no association rows.
- Deletion never affects another user's data: the entry is loaded scoped to the
  authenticated user's Library, and cross-user Game ids behave as `404`.

### Platforms

- BoardGames must never have Platforms.
- The implementation must ensure:
  - BoardGame create/update requests do not accept `platformIds` (the contract
    has no such field; any `platformIds` property sent in a BoardGame request
    body is ignored by model binding and no `game_platforms` row is created).
  - no `game_platforms` rows are created for BoardGame entries;
  - the BoardGame response does not expose Platform IDs;
  - Platform delete behavior is unaffected by BoardGames (BoardGames never
    reference Platforms, so they never block a Platform deletion).
- VideoGame request contracts are **not** reused by BoardGame contracts, so no
  Platform semantics leak into BoardGame operations.

### Genres

- Genres are not part of the approved BoardGame MVP model.
- BoardGames never create `game_genres` rows; the BoardGame request/response
  contracts expose no `genreIds`.
- `GET /genres` and the genre catalog are untouched.

### Validation Rules

Validation is authoritative in `GameLibrary.Core`, in a dedicated `BoardGameRules`
module (see BoardGame Rules). Order matters (validate before persistence work):

- **Name:** trim leading/trailing whitespace → non-empty → trimmed length ≤ 100.
  Names are **not** unique; no duplicate detection exists for BoardGame names, and
  a BoardGame may have the same name as another BoardGame or as a VideoGame.
- **MinimumPlayers:** required; must be `>= 1`.
- **MaximumPlayers:** required; must be `>= MinimumPlayers`.
- **ApproximateDuration:** optional; when provided must be `> 0`.
- **InteractionType:** optional; when provided must be exactly `Cooperative` or
  `Competitive`.
- **AcquisitionStatus:** default `Owned` on create, required on update, valid enum
  values (see AcquisitionStatus Rules).
- **Rating:** optional integer in `[1, 5]`.
- **Notes:** optional. Trim; if empty after trim it is stored as `null`. Non-null
  values must be at most 5000 characters.
- **CoverImageUrl:** optional. When provided, trim; if empty after trim it is
  stored as `null`. Non-null values must be at most 2048 characters and must
  start with `http://` or `https://` (the same lightweight validation as
  Feature 004; no DNS checks, no URI library beyond the scheme prefix check).
- **No platform, genre, GameStatus, or ProgressPercentage rules exist for
  BoardGames** because those concepts do not apply.
- **GameStatus / ProgressPercentage null guarantee:** `BoardGameService` never
  writes them. Create stores both as `null`; update never modifies them; the
  BoardGame request/response contracts expose no such fields. Enforcement is at
  the application layer (see Database / EF Core for why no cross-table CHECK is
  added).

## Technical Requirements

### Backend

Structure in `GameLibrary.Core` (domain/application rules + persistence):

```
GameLibrary.Core/
├── Games/
│   ├── Game.cs                      (extended: + 4 BoardGame nullable properties)
│   ├── InteractionType.cs           (new enum: Cooperative, Competitive)
│   ├── BoardGameRules.cs            (new: pure, framework-independent rules)
│   ├── BoardGameService.cs          (new: EF-backed application service + view/input records)
│   ├── BoardGameExceptions.cs       (new: InvalidBoardGameException, BoardGameNotFoundException)
│   ├── VideoGameRules.cs            (unchanged)
│   └── ...
└── Data/
    ├── GameLibraryDbContext.cs      (extended: map the new columns + CHECK constraints)
    └── Migrations/                  (+ AddBoardGameManagement)
```

Structure in `GameLibrary.Api` (HTTP boundary only):

```
GameLibrary.Api/
├── Controllers/
│   └── BoardGamesController.cs
└── BoardGames/
    └── BoardGameContracts.cs
```

#### InteractionType

Add `InteractionType.cs` to `GameLibrary.Core.Games`:

```csharp
public enum InteractionType
{
    Cooperative,
    Competitive,
}
```

No additional members are added in this feature.

#### BoardGame Rules

`BoardGameRules` is a static class in `GameLibrary.Core.Games` owning all pure,
framework-independent BoardGame rules so they can be unit-tested without a
database. It is self-contained: it does NOT call into `VideoGameRules`, because
the shared field validations carry type-specific messages and exception types.
Duplication is limited to small, type-specific methods and is the approved
simplicity tradeoff (reuse only when genuinely simpler). The limits below match
the Feature 004 limits exactly and must never diverge. No shared `GameRules`
abstraction or generic validation framework is introduced.

**Cross-reference requirement (drift protection):** because `BoardGameRules`
deliberately duplicates the shared Game-level field limits and
trim-before-length normalization behavior from `VideoGameRules`, `BoardGameRules`
MUST carry a concise code comment/cross-reference — and preferably the matching
relevant section of `VideoGameRules` SHOULD carry the same — stating that these
shared field limits intentionally match each other:

- Name: 100
- Notes: 5000
- CoverImageUrl: 2048

and that changing one game type's common Game-level field limit in the future
requires reviewing and aligning the other. This is a documentation/cross-reference
requirement only; the type-specific validation methods are kept separate because
they produce BoardGame-specific exception types and validation messages.

At minimum it exposes:

- `const int MaxNameLength = 100; const int MaxNotesLength = 5000; const int
  MaxCoverImageUrlLength = 2048;`
- `string ValidateAndTrimName(string? name)` — trim → non-empty → ≤ 100, else
  throws `InvalidBoardGameException` ("Board game name is required." /
  "Board game name must be at most 100 characters.").
- `string? ValidateAndNormalizeCoverImageUrl(string? url)` — trim; empty → `null`;
  length ≤ 2048 and `http://`/`https://` prefix when non-null, else throws
  ("Cover image URL is invalid.").
- `string? ValidateAndNormalizeNotes(string? notes)` — trim; empty → `null`;
  length ≤ 5000, else throws ("Notes must be at most 5000 characters.").
- `int ValidateMinimumPlayers(int? value)` — required (`null` → throws
  "Minimum players is required."); `>= 1` (`value < 1` → throws
  "Minimum players must be at least 1."). Returns the value.
- `int ValidateMaximumPlayers(int? value, int minimumPlayers)` — required
  (`null` → throws "Maximum players is required."); `>= minimumPlayers`
  (`value < minimumPlayers` → throws
  "Maximum players must be at least the minimum players."). Returns the value.
- `void ValidateApproximateDuration(int? value)` — `null` ok; `value <= 0` →
  throws ("Approximate duration must be greater than 0.").
- `InteractionType? ParseInteractionType(string? value)` — `null` → `null`;
  exact enum-name strings (`"Cooperative"`, `"Competitive"`) parse; any other
  value throws ("Interaction type is invalid.").
- `AcquisitionStatus ParseAcquisitionStatus(string? value, bool isCreate)` —
  `null` on create → `Owned`; `null` on update → throws
  ("Acquisition status is required."); unknown value → throws
  ("Acquisition status is invalid.").
- `void ValidateRating(int? rating)` — `null` ok; must be 1–5, else throws
  ("Rating must be between 1 and 5.").

All exceptions are `InvalidBoardGameException` carrying the exact detail message.

#### BoardGame Service

`BoardGameService` is a concrete application service (no interface, no generic
repository, no Unit of Work) depending on `GameLibraryDbContext` and
`LibraryService`, registered with `AddScoped<BoardGameService>()` in `Program.cs`.
It is the authoritative backend enforcement layer for BoardGame operations. All
methods take the authenticated `userId` (never a client-supplied ID).

Records:

```csharp
public sealed record BoardGameInput(
    string? Name,
    int? MinimumPlayers,
    int? MaximumPlayers,
    int? ApproximateDuration,
    string? InteractionType,
    string? AcquisitionStatus,
    int? Rating,
    string? Notes,
    string? CoverImageUrl);

public sealed record BoardGameView(
    Guid Id,
    string Name,
    string? CoverImageUrl,
    int MinimumPlayers,
    int MaximumPlayers,
    int? ApproximateDuration,
    InteractionType? InteractionType,
    AcquisitionStatus AcquisitionStatus,
    int? Rating,
    string? Notes,
    DateTimeOffset CreatedAt);
```

Methods:

- `Task<IReadOnlyList<BoardGameView>> ListAsync(string userId, CancellationToken ct)`
  — returns the caller's BoardGames (Game + its LibraryEntry), **filtered to
  Games owned by the authenticated user's Library AND
  `Game.GameType == GameType.BoardGame`**. Future/other-type Games must never
  appear in `GET /board-games`. Ordering is deterministic and matches Feature
  004:
  1. normalized/case-insensitive Name ascending;
  2. original Name ascending;
  3. `CreatedAt` ascending;
  4. `Id` ascending.
  Implemented with an EF Core/PostgreSQL-translatable expression. **No generated
  normalized-name column is introduced** (Game names are not unique). Empty when
  the caller has no Library yet; no row is created on read.
- `Task<BoardGameView> CreateAsync(string userId, BoardGameInput input, CancellationToken ct)`
  — validates and normalizes every field via `BoardGameRules` **before**
  persistence work; ensures the user's Library exists
  (`LibraryService.EnsureAsync`); creates the `Game` (GameType `BoardGame`,
  `Id` new, `CreatedAt = UtcNow`, with `MinimumPlayers`, `MaximumPlayers`,
  `ApproximateDuration`, `InteractionType`) and the `LibraryEntry` (Id new,
  default/parsed AcquisitionStatus, `Rating`, `Notes`, `GameStatus = null`,
  `ProgressPercentage = null`); saves once; returns the created view.
- `Task<BoardGameView> UpdateAsync(string userId, Guid gameId, BoardGameInput input, CancellationToken ct)`
  — loads the Game and its LibraryEntry scoped to the user's Library (see
  `LoadOwnedAsync`); validates and applies every field normally — this is a plain
  **full-replacement PUT** with no transition special-casing: the resulting
  `AcquisitionStatus`, `Rating`, `Notes`, `CoverImageUrl`, `Name`, and BoardGame
  structural fields are all taken from the request; omitted/null optional values
  clear (Rating, Notes, CoverImageUrl, ApproximateDuration, InteractionType);
  `GameStatus`/`ProgressPercentage` are never modified (they remain `null`);
  saves once; returns the updated view. No platform or genre references are
  resolved because BoardGames use neither.
- `Task DeleteAsync(string userId, Guid gameId, CancellationToken ct)` — loads the
  Game + entry scoped to the user's Library and removes both in one transaction
  (LibraryEntry first, then Game), mirroring `VideoGameService.DeleteAsync`. No
  orphan Game remains.

Supporting behavior:

- `LoadOwnedAsync(userId, gameId)`: query `games` where `Id == gameId`,
  `Game.GameType == GameType.BoardGame`, and the Game's LibraryEntry's Library
  `UserId == userId`. Not found → throws `BoardGameNotFoundException`. Cross-user
  Game ids are indistinguishable from nonexistent ids (same exception, same
  `404`). A Game that exists and belongs to the user but has another `GameType`
  behaves exactly like a nonexistent BoardGame (`404 Board game not found`). This
  prevents `PUT /board-games/{videoGameId}` and `DELETE
  /board-games/{videoGameId}` from operating on a VideoGame.
- The `BoardGameView` projection reads the four structural columns from `games`.
  BoardGame rows are guaranteed to have both `MinimumPlayers` and
  `MaximumPlayers` present through `BoardGameService` validation and the
  database CHECK constraints. The projection MUST NOT use `0` or any other
  fallback value (for example `g.MinimumPlayers ?? 0` is not permitted). It must
  read the non-null value directly (conceptually `g.MinimumPlayers!.Value` /
  `g.MaximumPlayers!.Value`, or the actual EF-translatable equivalent selected
  by the implementation). A null player count on a BoardGame row is an invariant
  violation and must surface loudly during development/testing rather than being
  silently converted into a valid-looking response.
- No optimistic concurrency: concurrent edits use last-write-wins, consistent
  with Feature 004.

#### Controllers

- `BoardGamesController` (`[ApiController]`, `[Route("board-games")]`,
  `[Authorize]`): `GET` List, `POST` Create, `PUT {id:guid}` Update,
  `DELETE {id:guid}` Delete. Each derives the user from
  `User.GetSupabaseUserId()` and returns `401` when it is `null` (mirroring the
  existing controllers). It maps `InvalidBoardGameException` → `400` (title
  "Invalid board game", detail = message), `BoardGameNotFoundException` → `404`
  (title "Board game not found"), and maps the view to the response contract. It
  never accepts a user/owner/library ID from the body or query string.
- `Program.cs` change: `AddScoped<BoardGameService>()`. No other `Program.cs`
  changes (authentication, CORS, middleware order, and the centralized 500
  handler are unchanged).

### API

Routes follow the existing no-prefix convention (`auth`, `health`, `platforms`,
`video-games`, `genres`). All endpoints require authentication. Request/response
are JSON.

| Method | Route | Auth | Success | Errors |
| --- | --- | --- | --- | --- |
| `GET` | `/board-games` | required | `200` with a JSON array of `BoardGameResponse` containing only the caller's `BoardGame`-type Games in deterministic case-insensitive order (case-insensitive Name ascending, original Name ascending, `createdAt` ascending, `id` ascending) | `401` |
| `POST` | `/board-games` | required | `201` with the created `BoardGameResponse` (no `Location` header; there is no single-resource `GET /board-games/{id}` to point it at) | `400`, `401` |
| `PUT` | `/board-games/{id}` | required | `200` with the updated `BoardGameResponse` | `400`, `401`, `404` |
| `DELETE` | `/board-games/{id}` | required | `204` (no body) | `401`, `404` |

- **PUT vs PATCH: `PUT`, kept as full replacement.** `PUT` represents the complete
  mutable state for every update: `name`, `minimumPlayers`, `maximumPlayers`,
  `approximateDuration`, `interactionType`, `acquisitionStatus`, `rating`,
  `notes`, and `coverImageUrl`. Optional values omitted/null clear; there is NO
  transition exception (unlike VideoGame), so full replacement applies uniformly,
  including to Rating and Notes. Do NOT introduce `PATCH`.
- **No single-resource `GET /board-games/{id}`.** The list returns the complete
  `BoardGameResponse` for every entry, which is everything the edit form needs to
  prefill. Do not add a single-GET endpoint.
- **GameType guards.** `GET /board-games` returns only Games with
  `Game.GameType == GameType.BoardGame`. `PUT`/`DELETE /board-games/{id}` return
  `404 Board game not found` for a Game id whose `GameType` is not `BoardGame`,
  indistinguishable from a nonexistent BoardGame (see Cross-Type Safety).
- **No `GET /genres` dependency** and no Platform/Genre data are involved in
  BoardGame contracts.

Request contracts (`BoardGameContracts.cs`). Create and update use the same
shape; the service distinguishes the create default (`Owned`) from the update
requirement:

- Create: `CreateBoardGameRequest(string? Name, int? MinimumPlayers, int?
  MaximumPlayers, int? ApproximateDuration, string? InteractionType, string?
  AcquisitionStatus, int? Rating, string? Notes, string? CoverImageUrl)`.
- Update: `UpdateBoardGameRequest` with the same fields (`string?`/`int?` types at
  the HTTP boundary; the service enforces the required fields and the
  update-required `AcquisitionStatus`).
- Response: `BoardGameResponse(Guid Id, string Name, string? CoverImageUrl, int
  MinimumPlayers, int MaximumPlayers, int? ApproximateDuration, string?
  InteractionType, string AcquisitionStatus, int? Rating, string? Notes,
  DateTimeOffset CreatedAt)`.

The list response is a plain JSON array of `BoardGameResponse`. No wrapper
object, no pagination. Enum values in JSON use their enum names (`"Owned"`,
`"Wishlist"`, `"Interested"`, `"Cooperative"`, `"Competitive"`).

The contracts intentionally omit `platformIds`, `genreIds`, `gameStatus`, and
`progressPercentage`. Extra properties sent in a request body are ignored by the
default JSON model binding and create no association rows.

EF entities are never exposed by the API.

### Database / EF Core

EF Core migrations remain the sole authority for the application schema. No
Supabase dashboard edits, no second migration workflow, no RLS.

Add a migration named `AddBoardGameManagement`. It alters the existing `games`
table: no new tables are created.

New nullable columns on `games`:

| Column | Type | Constraints |
| --- | --- | --- |
| `minimum_players` | `int` | Nullable. CHECK `minimum_players IS NULL OR minimum_players >= 1`. |
| `maximum_players` | `int` | Nullable. CHECK `minimum_players IS NULL OR maximum_players IS NULL OR maximum_players >= minimum_players`. |
| `approximate_duration` | `int` | Nullable. CHECK `approximate_duration IS NULL OR approximate_duration > 0`. |
| `interaction_type` | `varchar(20)` | Nullable. Enum name string. CHECK `interaction_type IS NULL OR interaction_type IN ('Cooperative', 'Competitive')`. |

New table-level CHECK constraints on `games` (the GameType-specific integrity
backstop; all are single-table, so no cross-table constraints or triggers are
needed):

- `ck_games_board_players_required` —
  `game_type = 'VideoGame' OR (minimum_players IS NOT NULL AND maximum_players IS NOT NULL)`
  (BoardGame rows must have both player counts present).
- `ck_games_board_columns_video_null` —
  superseded by the manual-review amendment. The targeted Feature 007 amendment
  implementation must replace this constraint so VideoGame rows may use
  `minimum_players` and `maximum_players`, while still requiring
  `approximate_duration IS NULL` and `interaction_type IS NULL` for VideoGame rows.

Mapping notes for `GameLibraryDbContext.OnModelCreating` (configure inline in
`OnModelCreating`; do not introduce `IEntityTypeConfiguration` classes):

- `Game.MinimumPlayers` → `minimum_players`, `Game.MaximumPlayers` →
  `maximum_players`, `Game.ApproximateDuration` → `approximate_duration`,
  `Game.InteractionType` → `interaction_type`.
- `InteractionType` is mapped to a `varchar` column via value conversion storing
  the enum name string (`HasConversion<string>()`, `HasMaxLength(20)`).
  **Do not create a PostgreSQL enum type.**
- **Enum persistence contract.** The persisted value is the C# enum name as a
  varchar/string value and is part of the database storage contract. The
  application reads and writes only these enum-name strings; no other
  representation (numeric, snake_case, or PostgreSQL enum types) is stored. If a
  C# enum member is renamed in the future, the corresponding persisted value and
  any database CHECK constraints referencing the enum name string must be updated
  through an EF Core migration (consistent with Feature 004).
- Add the new CHECK constraints with `ToTable(t =>
  t.HasCheckConstraint(name, sql))` or the EF equivalent, alongside the existing
  `ck_games_game_type`.
- **No schema changes to `library_entries`, `platforms`, `genres`,
  `game_genres`, or `game_platforms`.** The existing VideoGame-only nullable
  columns `game_status` and `progress_percentage` on `library_entries` remain and
  stay `null` for BoardGame entries.
- **GameStatus/Progress null enforcement is application-level — an accepted
  residual integrity tradeoff for the MVP.** Enforcing "BoardGame entries have
  `game_status`/`progress_percentage` null" at the database would require a
  cross-table constraint joining `library_entries` to `games.game_type`, which
  is awkward. The approved approach is application enforcement:
  `BoardGameService` never writes those columns (create stores `null`; update
  never modifies them) and the BoardGame contracts expose no such fields. No
  cross-table CHECK, PostgreSQL trigger, stored procedure, or new denormalized
  `game_type` column on `library_entries` is added solely to enforce this rule.
  This application-authoritative enforcement is accepted for the MVP and is
  documented as a residual integrity tradeoff: the guarantee rests on
  `BoardGameService` never writing the columns, the contracts never exposing
  them, and integration coverage proving BoardGame-created LibraryEntries keep
  both values null (see Testing). The requirement itself is not weakened. The
  existing `ck_library_entries_owned_status_progress` CHECK is always satisfied
  by BoardGame entries.

The migration is committed to source control.

### Frontend

Build the feature under `src/app/features/games/board-games/` (a sibling area of
`video-game-form/` and `video-games-list/`, keeping VideoGame and BoardGame code
distinguishable without a new application architecture). No NgRx; use services,
signals, and local/component state.

Files:

```
src/app/features/games/
├── acquisition-status.ts            # shared type (extracted; see below)
├── board-game.ts                    # BoardGame interface + InteractionType + input types
├── board-games.service.ts           # HTTP client for the BoardGames API
├── board-games/
│   ├── board-games-list/
│   │   ├── board-games-list.ts
│   │   ├── board-games-list.html
│   │   ├── board-games-list.scss
│   │   └── board-games-list.spec.ts
│   └── board-game-form/
│       ├── board-game-form.ts
│       ├── board-game-form.html
│       ├── board-game-form.scss
│       └── board-game-form.spec.ts
```

- **Shared `AcquisitionStatus` type (small justified extraction).** `AcquisitionStatus`
  (`'Owned' | 'Wishlist' | 'Interested'`) is now genuinely shared by VideoGame and
  BoardGame, so it is extracted into `features/games/acquisition-status.ts`.
  `video-game.ts` re-exports it (`export type { AcquisitionStatus } from
  './acquisition-status';`) so existing imports continue to work unchanged.
  `GameStatus` remains in `video-game.ts` (VideoGame-only). This is the same
  "extract when a second consumer appears" pattern the backend used for
  `LibraryService`.
- `board-game.ts`:
  ```ts
  import type { AcquisitionStatus } from './acquisition-status';

  export type InteractionType = 'Cooperative' | 'Competitive';

  export interface BoardGame {
    id: string;
    name: string;
    coverImageUrl: string | null;
    minimumPlayers: number;
    maximumPlayers: number;
    approximateDuration: number | null;
    interactionType: InteractionType | null;
    acquisitionStatus: AcquisitionStatus;
    rating: number | null;
    notes: string | null;
    createdAt: string;
  }

  export interface BoardGameInput {
    name: string;
    minimumPlayers: number | null;
    maximumPlayers: number | null;
    approximateDuration: number | null;
    interactionType: InteractionType | null;
    acquisitionStatus: AcquisitionStatus;
    rating: number | null;
    notes: string | null;
    coverImageUrl: string | null;
  }
  ```
- `board-games.service.ts` (`@Injectable({ providedIn: 'root' })`): wraps the four
  HTTP calls against `${environment.apiBaseUrl}/board-games`, returning
  `BoardGame`/`BoardGame[]`. `list()`, `create(input)`, `update(id, input)`,
  `delete(id)`. The existing API token interceptor attaches the bearer token
  automatically; the service does nothing special for auth.

#### `board-games-list`

- On init, calls `boardGamesService.list()`; exposes signals for `loading`,
  `boardGames`, and `error`.
- Loading state: while the first request is in flight, shows a loading indicator.
- Error state: on a non-`401` failure, shows an error message and a "Try again"
  action that reloads the list.
- Empty state: when the list is empty and not loading, shows "No board games yet"
  plus a prominent create action.
- List state: renders the BoardGames in the API order; each row shows the name,
  player range (`{{ minimumPlayers }}–{{ maximumPlayers }}` players, or a suitable
  single-player label when both are 1), ApproximateDuration (when present, e.g.
  "60 min"), InteractionType (when present), AcquisitionStatus, Rating (when
  present), and Edit/Delete actions.
- Create: shows the `board-game-form` in create mode; on submit calls the service;
  on success reloads the list; on validation/`400` error keeps the form open and
  shows the message.
- Edit: selecting Edit loads the BoardGame's data into the form in edit mode; on
  submit calls `PUT`; on success reloads the list; on `404` shows "This board game
  no longer exists." and reloads.
- Delete: confirms with the board game name (native `confirm()` is acceptable); on
  `DELETE` success reloads the list; on `404` shows "This board game no longer
  exists." and reloads.
- A `401` from any board-game API call clears the local session
  (`auth.clearLocalSession()`) and navigates to `/login` (session-expired
  behavior), exactly like the VideoGames and Platforms features.

#### `board-game-form`

- One `FormGroup` with controls for: `name`, `minimumPlayers`, `maximumPlayers`,
  `approximateDuration`, `interactionType`, `acquisitionStatus` (default `Owned`),
  `rating`, `notes`, and `coverImageUrl`. **No Platform, Genre, GameStatus, or
  ProgressPercentage controls exist.**
- **Quick add:** a new form prioritizes Name, MinimumPlayers, MaximumPlayers, and
  AcquisitionStatus (default `Owned`). Everything else is optional. No
  InteractionType or duration is required to save.
- **Player count UX:** `minimumPlayers` and `maximumPlayers` are easy number
  inputs (e.g., `type="number"`, `min="1"`). Client validation mirrors the
  backend: `minimumPlayers >= 1` and `maximumPlayers >= minimumPlayers`.
- **Duration UX:** `approximateDuration` is an optional integer minutes number
  input (e.g., `type="number"`, `min="1"`), with placeholder examples such as 30,
  60, 120. No complex duration widget.
- **InteractionType UX:** an optional selector with exactly three options:
  Cooperative, Competitive, None (None → `null`). The form never infers
  InteractionType automatically.
- **Frontend validation mirrors backend normalization before length validation**
  (consistent with Feature 004's Decision):
  - `name`: 1) trim; 2) require non-empty; 3) normalized length ≤ 100.
  - `minimumPlayers`: required, integer `>= 1`.
  - `maximumPlayers`: required, integer `>= minimumPlayers`.
  - `approximateDuration`: optional, positive integer.
  - `notes`: 1) trim; 2) empty → `null`; 3) normalized length ≤ 5000.
  - `coverImageUrl`: 1) trim; 2) empty → `null`; 3) normalized length ≤ 2048 and
    `http(s)` scheme check.
  - `rating`: optional 1–5.
  The backend remains authoritative; these mirror validations provide immediate
  UX feedback only.
- **AcquisitionStatus UX:** a selector of Owned/Wishlist/Interested. Changing it
  has no dynamic field-hiding consequence: BoardGames have no GameStatus or
  ProgressPercentage to show or clear, and no Platform requirement, so no other
  field depends on the selected status. Unlike the VideoGame form, no "Owned
  requires at least one platform" block exists.
- Emits submit events with the complete `BoardGameInput`; the parent performs the
  HTTP call so the form stays reusable for create and edit.
- Displays inline error text for client-side validation and for server `400`
  messages supplied by the parent.

### Navigation

- Add a route `{ path: 'board-games', component: BoardGamesList, canActivate:
  [authGuard] }` to `app.routes.ts`.
- Add a minimal "Board games" `routerLink` on the authenticated home view
  (`features/auth/home/home.html`) next to the existing "Video games" and
  "Platforms" links.
- Add a minimal "Back to home" `routerLink` on the board-games page.
- Do not build a full application shell or shared navigation component.

### Validation and Errors

Predictable behavior, consistent problem-details responses. Expected failures are
raised as typed exceptions in `GameLibrary.Core` (`BoardGameExceptions.cs`:
`InvalidBoardGameException(string message)`, `BoardGameNotFoundException`) and
mapped by the controller, mirroring the Platform/VideoGame pattern.

| Condition | HTTP | Problem details |
| --- | --- | --- |
| Unauthenticated request | `401` | Produced by the JWT bearer handler (with `WWW-Authenticate` challenge). |
| Authenticated principal without a usable `sub` | `401` | Controller returns `Unauthorized()` (mirrors the existing controllers). |
| Name missing / empty / whitespace-only after trim | `400` | Title "Invalid board game", detail "Board game name is required." |
| Name longer than 100 characters | `400` | Title "Invalid board game", detail "Board game name must be at most 100 characters." |
| MinimumPlayers missing | `400` | Title "Invalid board game", detail "Minimum players is required." |
| MinimumPlayers < 1 | `400` | Title "Invalid board game", detail "Minimum players must be at least 1." |
| MaximumPlayers missing | `400` | Title "Invalid board game", detail "Maximum players is required." |
| MaximumPlayers < MinimumPlayers | `400` | Title "Invalid board game", detail "Maximum players must be at least the minimum players." |
| ApproximateDuration ≤ 0 | `400` | Title "Invalid board game", detail "Approximate duration must be greater than 0." |
| InteractionType not `Cooperative`/`Competitive`/`null` | `400` | Title "Invalid board game", detail "Interaction type is invalid." |
| Rating not in 1–5 | `400` | Title "Invalid board game", detail "Rating must be between 1 and 5." |
| Notes longer than 5000 characters | `400` | Title "Invalid board game", detail "Notes must be at most 5000 characters." |
| CoverImageUrl too long or not `http(s)` | `400` | Title "Invalid board game", detail "Cover image URL is invalid." |
| AcquisitionStatus missing on update | `400` | Title "Invalid board game", detail "Acquisition status is required." |
| AcquisitionStatus invalid value | `400` | Title "Invalid board game", detail "Acquisition status is invalid." |
| BoardGame does not exist in the caller's Library (update/delete of own, another user's, or cross-type) | `404` | Title "Board game not found". Cross-user and cross-type identifiers are indistinguishable from nonexistent ones. |
| Unexpected exception | `500` | Existing centralized handler (no stack traces, includes `requestId`). |

- A value outside the .NET `int` range (e.g., a player count above
  `int.MaxValue`) is rejected by JSON model binding as a `400` before any Core
  rule runs; no custom handling is required.
- The frontend maps these to user-visible messages (not found, required, player
  range, invalid duration/interaction/rating, etc., and a generic network/server
  failure message). It never displays raw problem-details internals or stack
  traces.

### Testing

Proportionate tests. No new test framework (xUnit for backend, Vitest via `ng test`
for frontend). Reuse the existing `GameLibrary.Core.Tests` project; do NOT create
another Core test project.

#### Backend unit tests — existing `GameLibrary.Core.Tests` project

Add `BoardGameRulesTests` covering the pure `BoardGameRules` behavior. Do not
create unit tests that only mirror EF queries.

Required coverage:

- Name: `null`/empty/whitespace → error; trims leading/trailing whitespace; 100
  characters accepted; 101 rejected; no duplicate rule exists (names allowed to
  repeat).
- MinimumPlayers: `null` → error; 0 → error; 1 accepted; a large positive value
  accepted.
- MaximumPlayers: `null` → error; equal to MinimumPlayers accepted; greater than
  MinimumPlayers accepted; less than MinimumPlayers → error.
- ApproximateDuration: `null` ok; 0 → error; negative → error; 1 and 60 ok.
- InteractionType: `null` → `null`; `"Cooperative"` → Cooperative;
  `"Competitive"` → Competitive; `""`, `"Solo"`, and any other value → error.
- AcquisitionStatus: create with `null` → `Owned`; update with `null` → error;
  valid values parse; unknown → error.
- Rating: `null` ok; 1–5 ok; 0, 6, negative → error.
- Notes: ≤ 5000 ok; > 5000 → error; whitespace-only → `null`.
- CoverImageUrl: `null`/empty → `null`; `http(s)` ok; other scheme and > 2048 →
  error.

#### Backend integration tests — existing `GameLibrary.IntegrationTests` project

Add a `BoardGameTestFactory : WebApplicationFactory<Program>` mirroring
`VideoGameTestFactory` exactly: set `ConnectionStrings:Default` to the
`ConnectionStrings__Test` environment value and post-configure the bearer
`JwtBearerOptions` with the deterministic symmetric test key; mint tokens via
`TestTokens.CreateToken(sub)` for distinct test users. Apply migrations idempotently
before each test (the existing `[Collection("Database")]` + `IAsyncLifetime`
`MigrateAsync` pattern).

Add a `BoardGameEndpointTests` class. Required cases (all against real
PostgreSQL; use at least two users):

- **Migration/schema:** extend `DatabaseMigrationTests` (or add a migration
  assertion) so the four new `games` columns (`minimum_players`,
  `maximum_players`, `approximate_duration`, `interaction_type`) exist and the
  new CHECK constraints are present. The existing 8-table `public` schema
  assertion remains unchanged and must continue to pass (Feature 005 adds no
  tables). Existing `DatabaseMigrationTests` assertions must keep passing.
- **Unauthorized:** `GET/POST/PUT/DELETE /board-games` without a token → `401`.
- **Create:** `POST /board-games` with a name like `"  Catan  "`, `minimumPlayers`
  3, `maximumPlayers` 4, no AcquisitionStatus (defaults to Owned) returns `201`
  with the trimmed name, no `Location` header, `acquisitionStatus` `"Owned"`,
  `minimumPlayers` 3, `maximumPlayers` 4; `GET /board-games` lists exactly it.
- **Create without Library:** a user's first create produces exactly one
  `libraries` row for that user (direct `GameLibraryDbContext` query), and a
  second create reuses it (still one row).
- **List:** returns only the calling user's BoardGames, empty for a user with no
  Library (no row created on read).
- **Case-insensitive deterministic ordering:** create BoardGames with names chosen
  so the case-insensitive order differs from the case-sensitive order and/or from
  creation order (for example `"b"`, `"B"`, `"A"`, `"a"`); assert `GET
  /board-games` returns them in the deterministic order, and that two calls return
  the same order.
- **Two-user isolation:** user A and user B each create BoardGames (including one
  with the same name in both Libraries); A's list contains only A's BoardGames and
  B's only B's; `PUT`/`DELETE` of A's Game id while authenticating as B returns
  `404`.
- **Same name as a VideoGame allowed:** create a VideoGame "Catan" (Owned with a
  Platform) and a BoardGame "Catan" for the same user; `GET /board-games`
  contains the BoardGame, `GET /video-games` contains the VideoGame, and neither
  list leaks the other.
- **Required player counts:** create without `minimumPlayers` → `400`;
  without `maximumPlayers` → `400`; `minimumPlayers: 0` → `400`; `maximumPlayers:
  2, minimumPlayers: 3` → `400`; `minimumPlayers: 1, maximumPlayers: 1` → `201`;
  `minimumPlayers: 2, maximumPlayers: 5` → `201`.
- **Optional duration:** with `approximateDuration: 60` → `201` and stored; with
  `null`/omitted → stored `null`; with `0` → `400`; with `-5` → `400`.
- **Optional InteractionType:** with `"Cooperative"` → stored; with
  `"Competitive"` → stored; with `null`/omitted → `null`; with `"Solo"` → `400`;
  with an unknown string → `400`.
- **AcquisitionStatus:** create defaults to `Owned`; create with `"Wishlist"` or
  `"Interested"` (no Platforms) → `201` (BoardGame has no Platform requirement);
  update with `acquisitionStatus: null` → `400`; update to a different status →
  `200`.
- **Rating:** `1`–`5` ok; `0`, `6` → `400`; `null` ok.
- **Notes/Cover behavior:** whitespace-only `notes` → `null`; `notes` longer than
  5000 → `400`; whitespace-only `coverImageUrl` → `null`; invalid scheme or
  over-length `coverImageUrl` → `400`; valid values stored trimmed.
- **Update:** `PUT /board-games/{id}` renames and updates metadata; full
  replacement clears omitted/null optional fields (Rating, Notes, CoverImageUrl,
  ApproximateDuration, InteractionType) even across an AcquisitionStatus change
  (no transition preservation — e.g., `PUT` an Owned BoardGame with Rating 4 and
  Notes "n" to `Wishlist` with `rating: null`, `notes: null` → `200` and both are
  cleared); `PUT` of a nonexistent or another user's id → `404`.
- **Delete:** `DELETE /board-games/{id}` returns `204`; the Game no longer appears
  in the list; repeating the delete → `404`; direct queries show the `games` row
  and its `library_entries` row are both gone; no FK error surfaces during the
  deletion; a representative create → update → list → delete sequence leaves no
  orphan `games` row (the existing orphan SQL check pattern).
- **BoardGame never creates `game_platforms`:** create a BoardGame and a Platform
  for the same user; direct query shows zero `game_platforms` rows for the
  BoardGame's LibraryEntry; the Platform remains deletable (`204`).
- **BoardGame never creates `game_genres`:** direct query shows zero `game_genres`
  rows for the BoardGame's Game.
- **GameStatus/Progress remain null:** create a BoardGame; direct query confirms
  `game_status` and `progress_percentage` are `null`; the response omits them.
- **Cross-type safety (the six required cases):**
  1. A BoardGame never appears in `GET /video-games`.
  2. A VideoGame never appears in `GET /board-games`.
  3. `PUT /video-games/{boardGameId}` → `404`.
  4. `DELETE /video-games/{boardGameId}` → `404`.
  5. `PUT /board-games/{videoGameId}` → `404`.
  6. `DELETE /board-games/{videoGameId}` → `404`.
  Create a VideoGame through the VideoGame endpoints (Owned with a Platform) and a
  BoardGame through the BoardGame endpoints for the same user; assert all six
  behaviors.
- **Relational backstop (direct SQL / test DbContext):** using the test
  DbContext/SQL, attempt each invalid persisted state and assert the database
  CHECK rejects it:
  - a VideoGame row with exactly one player-count value present;
  - a VideoGame row with a non-null `approximate_duration` or `interaction_type`;
  - a BoardGame row with a null `minimum_players` or `maximum_players`;
  - a row with `minimum_players < 1` or `maximum_players < minimum_players`;
  - a row with `approximate_duration <= 0`;
  - a row with an `interaction_type` outside `('Cooperative', 'Competitive')`.
  Do NOT test these through the API, because the service validates first. This
  backstop also guarantees the `BoardGameView` projection can never observe a
  null player count, making its non-null read (no `?? 0` fallback) safe; the
  rejected null-player-count state is the loud failure path for the projection
  invariant.
- **GameType guard regression:** the existing `VideoGameEndpointTests`
  non-VideoGame guard test (BoardGame row → not listed, `PUT`/`DELETE`
  `/video-games/{boardGameId}` → `404`) must continue to pass.

Existing tests (`HealthEndpointTests`, `AuthEndpointTests`,
`JwtValidationConfigurationTests`, `PlatformEndpointTests`,
`VideoGameEndpointTests`, `VideoGameRulesTests`) must continue to pass unchanged.

#### Frontend tests

Vitest via `ng test`. Mock `HttpClient` (`provideHttpClientTesting`) and use the
existing `SUPABASE_CLIENT` mock where needed. Meaningful cases at minimum:

- `board-games.service`: `list`, `create`, `update`, `delete` issue the correct
  method/URL and map responses.
- `board-games-list`:
  - Loading state while the list request is pending.
  - Empty state ("No board games yet").
  - List rendering of returned BoardGames (name, player range, duration,
    interaction type, status, rating).
  - Create success reloads the list and closes the form.
  - Create failure shows the server message and keeps the form usable.
  - Edit success reloads/updates the list; `404` on edit shows the "no longer
    exists" message.
  - Delete confirmation; delete success reloads the list; `404` on delete shows
    the "no longer exists" message.
  - Non-`401` API failure shows the error state with a retry action.
  - `401` clears the local session and navigates to `/login`.
- `board-game-form`:
  - Name required, trim-on-submit, whitespace-only rejection, and normalized-length
    validation.
  - Player-count validation: `minimumPlayers >= 1`; `maximumPlayers >=
    minimumPlayers`; both required to submit.
  - Optional duration: empty → `null` in the outgoing payload; `0`/negative →
    invalid client-side.
  - InteractionType selector with Cooperative / Competitive / None; None →
    `null`; no automatic inference.
  - AcquisitionStatus default `Owned`; `Wishlist`/`Interested` submit fine without
    any Platform; changing status has no effect on any other field.
  - Rating range validation; Notes/CoverImageUrl normalize-before-length.
  - Submit emits the correct `BoardGameInput` (trims, null-normalization).
- Route protection: the `/board-games` route is registered behind `authGuard`
  (signed-out visits redirect to `/login`).

Do not over-test trivial markup.

**Manual acceptance verification** — sign in, and use the UI to create/edit/delete
BoardGames across all statuses and optional fields; confirm list, validation, and
session-expiry behavior. See Verification.

## Functional Requirements

`FR-001` — Authenticated list: `GET /board-games` is `[Authorize]` and returns the
calling user's BoardGames (filtered to `Game.GameType == GameType.BoardGame`) as a
JSON array of `BoardGameResponse` in a deterministic, case-insensitive order
(case-insensitive Name ascending, original Name ascending, `createdAt` ascending,
`id` ascending). Unauthenticated → `401`. A user with no Library gets an empty
array (no row created on read). Non-BoardGame Game rows never appear.

`FR-002` — Create: `POST /board-games` creates a BoardGame in the calling user's
Library and returns `201` + the created `BoardGameResponse` (no `Location` header).
The first create for a user lazily creates exactly one `libraries` row for that
user, reused by subsequent creates. The response `id` is the Game id.

`FR-003` — Name rules: names are required (non-empty after trim) and at most 100
characters (trimmed); leading/trailing whitespace is trimmed before storage. Game
names are **not** unique: a BoardGame may share a name with another BoardGame or
with a VideoGame, and no duplicate detection exists.

`FR-004` — Player counts: `MinimumPlayers` and `MaximumPlayers` are required on
every create and update. `MinimumPlayers >= 1` and `MaximumPlayers >=
MinimumPlayers`; violations and missing values return `400`. No artificial
business maximum is defined beyond the `integer`/`int` type ceiling.

`FR-005` — ApproximateDuration: optional single integer minutes value; must be
`> 0` when provided; `null` means "not set"; `0`/negative → `400`. No ranges,
sessions, or composite duration models are introduced.

`FR-006` — InteractionType: optional; exactly `Cooperative` or `Competitive`;
any other non-null value → `400`; `null` means "not set". No additional values are
supported.

`FR-007` — AcquisitionStatus: values `Owned`/`Wishlist`/`Interested`; create
defaults to `Owned` when omitted; update requires a value. No Platform requirement
exists for any status. No GameStatus/Progress transition clearing or preservation
applies; Rating and Notes always follow normal full-replacement PUT semantics.

`FR-008` — Rating: optional integer 1–5; outside the range → `400`.

`FR-009` — Notes: optional personal text, at most 5000 characters, no rich text;
whitespace-only normalized to `null`.

`FR-010` — CoverImageUrl: optional manual external URL at most 2048 characters,
`http(s)` scheme when provided, whitespace-trimmed, empty → `null`. No upload,
storage, resizing, or CDN.

`FR-011` — Platforms: BoardGames never use Platforms. BoardGame contracts accept
no `platformIds`, BoardGame responses expose no Platform IDs, no `game_platforms`
rows are created for BoardGame entries, and Platform deletion is unaffected by
BoardGames.

`FR-012` — Genres: BoardGames never use Genres. BoardGame contracts expose no
`genreIds` and no `game_genres` rows are created for BoardGame entries. The genre
catalog and `GET /genres` are unchanged.

`FR-013` — GameStatus/Progress absent: BoardGame contracts expose no `gameStatus`
or `progressPercentage`; `BoardGameService` stores both as `null` on create and
never modifies them on update; direct queries confirm they remain `null` for
BoardGame entries.

`FR-014` — Update: `PUT /board-games/{id}` is uniformly full replacement (no
transition exception), returns `200` with the updated `BoardGameResponse`, applies
the same validation rules, and returns `404` for ids that do not exist in the
caller's Library (including another user's and any Game whose `GameType` is not
`BoardGame`). `PUT` is the only update verb; no `PATCH`, no optimistic
concurrency/`RowVersion`/ETags/locks, and no single-resource `GET /board-games/{id}`
are added.

`FR-015` — Delete: `DELETE /board-games/{id}` returns `204`, deletes the
LibraryEntry (dependent) and its Game (principal) together as one transaction in
the correct FK order, leaves no orphan Game or orphan LibraryEntry, and returns
`404` for cross-user/nonexistent/non-BoardGame ids.

`FR-016` — User isolation: every BoardGame operation is scoped to the authenticated
user's Library derived from the validated JWT `sub`; the API accepts no
user/owner/library ID from requests; another user's Game ids behave as `404`.

`FR-017` — Migration and schema: the `AddBoardGameManagement` EF migration added the
four nullable game-level columns to `games` with the specified single-table CHECK
constraints (`minimum_players >= 1`, `maximum_players >= minimum_players`,
`approximate_duration > 0`, `interaction_type IN ('Cooperative','Competitive')`,
BoardGame-rows-have-both-players, and the original VideoGame-rows-have-all-null
constraint), maps `InteractionType` as a `varchar` enum-name string (no PostgreSQL
enum type), and is committed to source control. The Feature 007 manual-review
amendment requires a later targeted EF migration to relax/replace the VideoGame
all-null constraint so VideoGames may use `minimum_players`/`maximum_players` while
duration and interaction remain BoardGame-only. No tables are added or removed.

`FR-018` — Core authority: `BoardGameRules` and `BoardGameService` in
`GameLibrary.Core` implement all validation, normalization, ownership scoping, and
create/update/delete; `GameLibrary.Api` contains only HTTP concerns and contracts.
The shared `LibraryService` is reused; `VideoGameRules`/`VideoGameService` are
unchanged.

`FR-019` — Frontend list page: a protected `/board-games` route renders loading,
error (with retry), empty ("No board games yet"), and list states.

`FR-020` — Frontend create/edit form: covers Name, MinimumPlayers, MaximumPlayers,
ApproximateDuration, InteractionType, AcquisitionStatus, Rating, Notes, and
CoverImageUrl; client validation mirrors the backend (name, player counts,
duration, interaction type, rating, notes/cover normalization); quick add
prioritizes Name, player counts, and AcquisitionStatus default Owned; no Platform,
Genre, GameStatus, or Progress fields exist.

`FR-021` — Frontend delete flow: confirms, submits `DELETE`, reloads the list on
success, and surfaces not-found/API errors.

`FR-022` — Frontend session expiry: a `401` from any board-game API call clears the
local session and navigates to `/login`.

`FR-023` — Navigation: a "Board games" link on the home view and a "Home" link on
the board-games page connect the two authenticated views.

`FR-024` — Backend unit tests: `BoardGameRulesTests` (name, player counts,
duration, interaction type, acquisition, rating, notes, cover) passes in the
existing `GameLibrary.Core.Tests` project.

`FR-025` — Backend integration tests: the required real-PostgreSQL cases
(migration/schema, create, library lifecycle, list, ordering, two-user isolation,
same-name-as-VideoGame, player counts, duration, interaction type, acquisition,
rating, notes/cover, update, delete, no platform/genre rows, GameStatus/Progress
null, cross-type safety, relational backstops) are implemented and pass.

`FR-026` — Frontend tests: the required `board-games.service`, `board-games-list`,
and `board-game-form` behaviors are tested and pass.

`FR-027` — Cross-type safety: BoardGame endpoints require
`Game.GameType == GameType.BoardGame` on every operation. `GET /board-games` never
includes VideoGames; `PUT`/`DELETE /board-games/{videoGameId}` return `404`;
`GET /video-games` never includes BoardGames (Feature 004 guard); `PUT`/`DELETE
/video-games/{boardGameId}` return `404`. No cross-type existence is leaked.

`FR-028` — Foundation preserved: `GET /health` stays anonymous, `GET /auth/me`
behavior is unchanged, Platform and VideoGame behavior (including VideoGame's
acquisition transitions and Platform delete protection) is unchanged, the backend
has exactly two application projects, and existing backend integration and unit
tests still pass.

## Non-Functional Requirements

`NFR-001` — User isolation: every user-owned query and mutation is scoped to the
authenticated user's Library derived from the validated Supabase `sub`; no
client-supplied user/owner/library ID is trusted; cross-user BoardGame ids return
`404` to prevent resource enumeration; one user can never read or modify another
user's BoardGames, LibraryEntries, or Games.

`NFR-002` — Cross-type integrity: BoardGame and VideoGame operations are guarded by
their respective `GameType` filters and never operate on each other's rows; no
cross-type existence is leaked; the six cross-type cases (Feature 004 symmetric
guards plus this feature's) are pinned by integration tests.

`NFR-003` — Integrity: the single-table CHECK constraints on `games` act as the
database integrity backstop for BoardGame structural fields and GameType-specific
validity (VideoGame rows may have valid optional player counts but keep duration
and interaction null; BoardGame rows have valid required player counts); the
existing `library_entries` FKs, unique `game_id` index, and
CHECK constraints are unchanged; GameStatus/Progress null for BoardGame entries is
application-enforced (no awkward cross-table constraints/triggers).

`NFR-004` — Simplicity: the feature adds one migration altering one table, one new
enum, one pure rules module, one application service, one controller, and one
frontend feature folder. No generic repositories, Unit of Work, MediatR, CQRS,
event sourcing, domain events, extra backend application projects, NgRx,
inheritance-based EF mapping, generic game abstractions (`IGameService<T>`, generic
CRUD bases, polymorphic DTO frameworks), or shared-kernel abstractions are
introduced. EF Core is used directly.

`NFR-005` — Maintainability: BoardGame domain/application rules live in
`GameLibrary.Core` (pure rules isolated in `BoardGameRules` for unit testing);
HTTP/contract concerns live in `GameLibrary.Api`; frontend feature code is local to
`features/games/board-games/`; the shared `AcquisitionStatus` frontend type is the
only cross-game-type extraction, justified by the second consumer; `VideoGameRules`
and `VideoGameService` are not modified.

`NFR-006` — Validation consistency: backend validation in `GameLibrary.Core` is
authoritative; the frontend mirrors rules only for immediate UX feedback and never
overrides the backend; every create/update ends in a valid state per the approved
Domain invariants (`1 <= MinimumPlayers <= MaximumPlayers`,
`ApproximateDuration > 0` when provided, valid InteractionType, Rating 1–5,
AcquisitionStatus valid).

`NFR-007` — Security: the validated JWT `sub` is the only trusted identity;
problem-details responses never expose stack traces, constraint names, SQL, or
other database internals; committed configuration remains non-secret; no RLS is
introduced; Angular never accesses application database tables; no new secrets are
committed.

## Acceptance Criteria

`AC-001` — Unauthenticated requests to any of `GET/POST/PUT/DELETE /board-games`
return `401`.

`AC-002` — An authenticated user creates a BoardGame with `name: "  Catan  "`,
`minimumPlayers: 3`, `maximumPlayers: 4`, and no AcquisitionStatus; `POST
/board-games` returns `201` (no `Location` header) with the trimmed name
`"Catan"`, `minimumPlayers` 3, `maximumPlayers` 4, and `acquisitionStatus`
`"Owned"`; `GET /board-games` lists exactly it.

`AC-003` — `POST` with `minimumPlayers` missing → `400`; with `maximumPlayers`
missing → `400`; with `minimumPlayers: 0` → `400`; with `maximumPlayers: 2,
minimumPlayers: 3` → `400`; with `minimumPlayers: 1, maximumPlayers: 1` and with
`minimumPlayers: 2, maximumPlayers: 5` → `201`.

`AC-004` — `POST` with `approximateDuration: 60` → `201` and stored; with
`approximateDuration: 0` or `-5` → `400`; `POST` with `interactionType:
"Cooperative"` → stored, `"Competitive"` → stored, `"Solo"` → `400`; `POST`/`PUT`
with an empty, whitespace-only, or missing `name` → `400`; a 101-character `name`
→ `400`; `rating` 0 or 6 → `400`; `PUT` with `acquisitionStatus` `null` → `400`.

`AC-005` — Two authenticated users A and B: A creates BoardGames and B creates
BoardGames (including one with the same name as A's); `GET /board-games` returns
for A only A's BoardGames and for B only B's; `PUT` and `DELETE` of A's Game id
while authenticated as B return `404`.

`AC-006` — A BoardGame and a VideoGame with the same name coexist in one Library:
`GET /board-games` contains the BoardGame and not the VideoGame; `GET /video-games`
contains the VideoGame and not the BoardGame.

`AC-007` — `PUT /board-games/{id}` updates the name, player counts, duration,
interaction type, acquisition status, rating, notes, and cover URL and returns
`200` with the updated `BoardGameResponse`; `GET /board-games` reflects it;
omitted/null optional values clear (including across an AcquisitionStatus change);
`PUT`/`DELETE` of a nonexistent or another user's id returns `404`.

`AC-008` — `DELETE /board-games/{id}` returns `204`; the BoardGame no longer
appears in `GET /board-games`; repeating the delete returns `404`; a direct
database query shows the `games` row and its `library_entries` row are both gone
and no FK error surfaces; a representative create → update → list → delete
sequence leaves no orphan `games` row.

`AC-009` — A BoardGame never creates `game_platforms` or `game_genres` rows (direct
database query), and a Platform created alongside a BoardGame remains deletable
(`DELETE /platforms/{id}` → `204`).

`AC-010` — A BoardGame entry's `game_status` and `progress_percentage` remain
`null` (direct database query); the BoardGame request/response contracts expose no
such fields.

`AC-011` — Cross-type safety: a BoardGame does not appear in `GET /video-games`;
a VideoGame does not appear in `GET /board-games`; `PUT`/`DELETE
/video-games/{boardGameId}` each return `404`; `PUT`/`DELETE /board-games/{videoGameId}`
each return `404`.

`AC-012` — The `AddBoardGameManagement` migration applies to real PostgreSQL and
adds the four `games` columns and the six new CHECK constraints (player min,
player range, duration, interaction type, BoardGame-players-required,
VideoGame-columns-null); the updated migration tests pass and the existing
8-table schema assertion remains valid.

`AC-013` — A user's first BoardGame create results in exactly one `libraries` row
for that user; a second create keeps it at exactly one (verified by direct database
query).

`AC-014` — The relational backstops reject invalid persisted states: direct
SQL/test-DbContext writes of a VideoGame row with exactly one player-count value or
non-null duration/interaction, a BoardGame row with a null player count,
`minimum_players < 1`,
`maximum_players < minimum_players`, `approximate_duration <= 0`, and an
`interaction_type` outside `('Cooperative', 'Competitive')` are each rejected by a
CHECK constraint.

`AC-015` — Backend build (`dotnet build src/backend/GameLibrary.sln`), backend
integration tests (`dotnet test tests/backend/GameLibrary.IntegrationTests`),
backend unit tests (`dotnet test tests/backend/GameLibrary.Core.Tests`), frontend
tests (`ng test --watch=false`), and lint (`ng lint`) all pass with
`ConnectionStrings:Test` configured to real PostgreSQL. Existing
`HealthEndpointTests`, `AuthEndpointTests`, `JwtValidationConfigurationTests`,
`PlatformEndpointTests`, `VideoGameEndpointTests`, and `VideoGameRulesTests` still
pass unchanged.

`AC-016` — The Angular `/board-games` route is protected by `authGuard`: signed out
it redirects to `/login`; signed in it loads and displays the BoardGames with
loading, empty, and list states; creating/editing through the UI updates the list
and shows user-visible validation errors; deleting confirms and removes from the
list; a `401` clears the session and navigates to `/login`.

`AC-017` — The form mirrors the domain rules: player-count validation
(`minimumPlayers >= 1`, `maximumPlayers >= minimumPlayers`) blocks invalid
submits; duration is an optional positive integer; InteractionType is an optional
Cooperative/Competitive/None selector with no automatic inference; AcquisitionStatus
defaults to Owned and `Wishlist`/`Interested` submit without any Platform; Rating,
Notes, and CoverImageUrl validation matches the backend and normalizes before
length validation.

`AC-018` — No out-of-scope artifacts are introduced: no new database tables, no
BoardGame genres/expansions/categories, no platform/genre/status/progress fields in
BoardGame contracts, no changes to Platform or VideoGame behavior, no RLS, no
direct Angular database access, no new backend application projects, no new
NuGet/npm dependencies, no `PATCH`, no single-resource `GET /board-games/{id}`, no
optimistic-concurrency/`RowVersion`/ETags/locks, and no generic game abstractions
or EF inheritance.

## Verification

For each acceptance criterion, an implementation agent verifies as follows.

`AC-001`:
- Run the API and issue `curl -i http://localhost:5218/board-games` with no
  `Authorization` header: status `401` with a `WWW-Authenticate: Bearer` challenge.
  The same holds for `POST`, `PUT`, and `DELETE` without a token.

`AC-002`:
- Using a valid Supabase access token (or a test minted token against the test
  factory):
  `curl -i -X POST -H "Authorization: Bearer <token>" -H "Content-Type:
  application/json" -d '{"name":"  Catan  ","minimumPlayers":3,"maximumPlayers":4}'
  http://localhost:5218/board-games`. Expect `201`, body name `"Catan"`,
  `minimumPlayers` 3, `maximumPlayers` 4, `acquisitionStatus` `"Owned"`, no
  `Location` header. Then `GET /board-games` returns exactly one item with name
  `"Catan"`.

`AC-003`:
- `POST /board-games` with `{"name":"G"}` (no player counts) → `400`; with
  `{"name":"G","minimumPlayers":3}` (no maximum) → `400`; with
  `{"name":"G","minimumPlayers":0,"maximumPlayers":4}` → `400`; with
  `{"name":"G","minimumPlayers":3,"maximumPlayers":2}` → `400`; with
  `{"name":"G","minimumPlayers":1,"maximumPlayers":1}` → `201`; with
  `{"name":"G","minimumPlayers":2,"maximumPlayers":5}` → `201`.

`AC-004`:
- `POST /board-games` with `approximateDuration` 60 → `201`; with 0 and −5 → `400`.
  With `interactionType` `"Cooperative"`/`"Competitive"` → `201`; `"Solo"` → `400`.
  `name` empty/whitespace/missing and 101 characters each → `400`. `rating` 0 and 6
  → `400`. `PUT` with `acquisitionStatus` omitted/null → `400`.

`AC-005`:
- Using tokens for two distinct accounts, create BoardGames for each (including the
  same name in both). `GET /board-games` for A returns only A's; for B only B's.
  Authenticating as B, `PUT /board-games/{aGameId}` and `DELETE
  /board-games/{aGameId}` each return `404`. Covered by the integration tests.

`AC-006`:
- Covered by the integration tests: create a VideoGame "Catan" (Owned with a
  Platform) and a BoardGame "Catan" for the same user; assert the BoardGame id is
  in `GET /board-games` and not in `GET /video-games`, and the VideoGame id is in
  `GET /video-games` and not in `GET /board-games`.

`AC-007`:
- `PUT /board-games/{id}` with updated fields returns `200` with the new values;
  `GET /board-games` reflects them. `PUT` a BoardGame owned with Rating 4 and Notes
  "n" to `Wishlist` with `rating: null` and `notes: null` → `200` with both cleared
  (no transition preservation). `PUT`/`DELETE` with a random `{id}` → `404`.

`AC-008`:
- `DELETE /board-games/{id}` returns `204`; `GET /board-games` no longer contains
  it; a second `DELETE` returns `404`. Using the test connection, query
  `SELECT COUNT(*) FROM games WHERE id = '<id>'` and
  `SELECT COUNT(*) FROM library_entries WHERE game_id = '<id>'` and assert `0`
  each. No FK error surfaces. Run a create → update → list → delete sequence and
  assert the orphan query
  `SELECT COUNT(*) FROM games g LEFT JOIN library_entries le ON le.game_id = g.id
  WHERE le.id IS NULL` equals `0`.

`AC-009`:
- Covered by the integration tests: create a BoardGame and a Platform for the same
  user; query `game_platforms` for the BoardGame's LibraryEntry → `0` rows and
  `game_genres` for its Game → `0` rows; `DELETE /platforms/{platformId}` → `204`.

`AC-010`:
- Covered by the integration tests: after creating a BoardGame, query
  `SELECT game_status, progress_percentage FROM library_entries WHERE game_id =
  '<id>'` and assert both are `NULL`; the BoardGame request/response contracts
  contain no such fields.

`AC-011`:
- Covered by the integration tests: create a VideoGame and a BoardGame for the same
  user; assert all six cross-type behaviors (Feature 004's guard tests cover the
  VideoGame side; this feature's tests cover the BoardGame side).

`AC-012`:
- Set `ConnectionStrings:Test` to a real PostgreSQL database and run
  `dotnet test tests/backend/GameLibrary.IntegrationTests`. The migration test
  passes and asserts the new `games` columns and CHECK constraints. Alternatively
  run `dotnet ef migrations list` to confirm `AddBoardGameManagement` and `dotnet
  ef database update` against a scratch database to confirm it applies.

`AC-013`:
- Covered by the integration test: after two BoardGame creates for a fresh test
  user, query `SELECT COUNT(*) FROM libraries WHERE user_id = '<sub>'` and assert
  `1`.

`AC-014`:
- Covered by the integration tests: using the test DbContext/SQL, attempt each
  invalid persisted state and assert a CHECK-violation `DbUpdateException`/Postgres
  check violation is thrown. Do not test these through the API (the service
  validates first).

`AC-015`:
- Run `dotnet build src/backend/GameLibrary.sln` (succeeds); run
  `dotnet test tests/backend/GameLibrary.IntegrationTests` and
  `dotnet test tests/backend/GameLibrary.Core.Tests` with `ConnectionStrings:Test`
  configured (all pass); from `src/frontend/` run `ng test --watch=false` and
  `ng lint` (both pass). Existing `HealthEndpointTests`, `AuthEndpointTests`,
  `JwtValidationConfigurationTests`, `PlatformEndpointTests`, `VideoGameEndpointTests`,
  and `VideoGameRulesTests` still pass.

`AC-016`:
- Run `ng serve`; signed out, visiting `http://localhost:4200/board-games` redirects
  to `/login`. Signed in, the page loads the BoardGames (loading indicator while the
  request is in flight, empty state when none exist). Create/edit/delete a BoardGame
  through the UI and confirm list updates and validation messages. Force a `401`
  (or intercept a response as `401`) and confirm the app clears auth state and
  navigates to `/login`.

`AC-017`:
- In the UI, confirm the player-count validation blocks `minimumPlayers` 0 and
  `maximumPlayers < minimumPlayers`, and that a valid quick-add (name + min + max +
  default Owned) saves without requiring duration or interaction type. Confirm the
  InteractionType selector offers only Cooperative/Competitive/None and never infers
  a value; `Wishlist`/`Interested` save without any Platform; duration 0 is blocked;
  Rating/Notes/CoverImageUrl validation matches the backend. Covered by the
  frontend tests.

`AC-018`:
- Review the repository: no new database tables were added by the migration (only
  columns and CHECK constraints on `games`); no BoardGame genre/expansion/category
  data, endpoints, or UI exist; BoardGame contracts contain no
  platform/genre/status/progress fields; Platform and VideoGame code and behavior
  are unchanged; no RLS policies or Supabase client database access exist; no new
  NuGet/npm dependencies were added; the backend has exactly two application
  projects; the new frontend code lives under `features/games/board-games/` and
  uses services/signals only.

## Dependencies

- No new NuGet packages or npm packages are required. The approved stack (EF Core,
  Npgsql, ASP.NET Core, Angular, `@supabase/supabase-js`) covers everything.
- Test tooling: existing xUnit + `Microsoft.AspNetCore.Mvc.Testing` for backend
  integration tests; existing xUnit in the existing `GameLibrary.Core.Tests`
  project for unit tests; existing Angular/Vitest tooling for frontend tests.
- Runtime prerequisites are unchanged from Features 001–004: a real PostgreSQL
  database for `ConnectionStrings:Default`/`ConnectionStrings:Test` (the current
  remote Supabase project `iramzxpjbnldhebykzhx`), and the existing JWT
  configuration. No Docker is required in the current environment.

Version-selection policy: unchanged (stable, supported versions recorded in
`README.md`).

## Risks / Notes

- **Persistence choice (Option A) is deliberate.** BoardGame structural fields are
  game-level data stored as nullable columns on `games`, with single-table CHECK
  constraints enforcing GameType-specific validity. After the manual-review
  amendment, the two player-count columns are shared with VideoGames as optional
  metadata; duration and interaction remain BoardGame-only. A `board_games` subtype table
  was considered and rejected: it adds a table, a 1:1 FK, delete-ordering
  concerns, and a join for a single four-column subtype with no current benefit.
  The application `GameType` guard remains authoritative; the CHECK constraints are
  the integrity backstop.
- **Do not weaken VideoGame behavior.** Feature 004's acquisition-transition
  preservation rule, GameType guards, Platform rules, and genre handling are
  unchanged. Feature 005 adds symmetric BoardGame guards only. Existing tests must
  remain green.
- **BoardGame update is uniform full replacement.** Unlike VideoGame, there is NO
  `Owned → Wishlist/Interested` preservation rule for BoardGame Rating/Notes. An
  integration test must pin that a BoardGame `PUT` clearing Rating/Notes (even
  across an AcquisitionStatus change) clears them. Do not "helpfully" copy
  Feature 004's transition behavior.
- **No null-fallback in the BoardGameView projection.** `MinimumPlayers` and
  `MaximumPlayers` on a BoardGame row are guaranteed present by service
  validation and the `games` CHECK constraints, so the projection MUST read the
  non-null value directly and must not fall back to `0` or any other value. A
  null player count on a BoardGame row is an invariant violation and must
  surface loudly during development/testing rather than being masked. No new
  error-handling infrastructure is required for this impossible-valid-state
  case.
- **Enum storage.** `InteractionType` is persisted as a `varchar` enum-name string
  via EF value conversion, consistent with Feature 004. Do not create a PostgreSQL
  enum type. The persisted names are part of the database storage contract.
- **GameStatus/Progress null is application-enforced (accepted residual
  integrity tradeoff).** The database cannot cheaply enforce "BoardGame entries
  have null `game_status`/`progress_percentage`" without a cross-table
  constraint; the approved approach is that `BoardGameService` never writes them
  and BoardGame contracts expose no such fields. Do not add a cross-table
  constraint, PostgreSQL trigger, stored procedure, or a denormalized `game_type`
  column on `library_entries` solely to enforce this rule. This application
  enforcement is accepted for the MVP; integration coverage proves created
  BoardGame entries keep both columns null.
- **No artificial numeric maximums.** Player counts and duration are unbounded
  beyond the PostgreSQL `integer`/. NET `int` type ceiling; out-of-range values are
  rejected as `400` by JSON model binding. Do not invent business limits.
- **`DatabaseMigrationTests` schema assertion.** Feature 005 adds no tables, so the
  existing 8-table assertion remains valid. Extend the migration test to assert the
  new columns and CHECK constraints. Do not accidentally drop or weaken the
  existing assertion.
- **BoardGame request bodies may carry extra properties.** `platformIds`,
  `genreIds`, `gameStatus`, and `progressPercentage` are not part of the BoardGame
  contracts; default model binding ignores them and creates no association rows.
  This is the "does not accept" behavior — do not add explicit rejection logic.
- **Frontend shared type extraction.** `AcquisitionStatus` is extracted to
  `features/games/acquisition-status.ts` because a second consumer (BoardGame) now
  exists; `video-game.ts` re-exports it so existing imports keep working. This is
  the only cross-game-type frontend extraction and is justified by real reuse.
- **Frontend budget.** The board-games feature adds no large dependencies; if the
  Angular build reports a budget warning, record the adjustment in `README.md` as
  was done previously (no budget change is expected).
- **README update.** Document the new endpoints (`/board-games`), the BoardGame
  schema additions (the four `games` columns and their CHECK constraints), the
  player-count/duration/interaction rules, the uniform full-replacement `PUT`, the
  GameStatus/Progress null guarantee, and the cross-type safety guarantees.

## Open Questions

None. The persistence representation (nullable BoardGame columns on `games` with
single-table CHECK backstops), the BoardGame model, the player-count rules
(`1 <= MinimumPlayers <= MaximumPlayers`, no artificial maximums), the duration
rules (single positive integer minutes), the InteractionType rules (exactly
Cooperative/Competitive, optional), the AcquisitionStatus rules (no Platform
requirement, no transition preservation, uniform full-replacement `PUT`), the
Game / LibraryEntry reuse (no second ownership model), the symmetric GameType
cross-type guards, the deletion rules (Game + LibraryEntry together, no orphan
Games, no subtype row), the validation rules, the API shape (no single-GET, no
`PATCH`, no optimistic concurrency), the migration/schema, the
application-enforced GameStatus/Progress null guarantee, the frontend structure
(`features/games/board-games/` plus the small `AcquisitionStatus` extraction), the
navigation, the error semantics, and the test approach are all defined here and by
the approved Product, Domain, and Architecture Specifications.
