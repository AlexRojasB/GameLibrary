# 004 — VideoGame Management

## Status

`Approved` (final amendment from the second independent review applied)

## Objective

Implement the MVP management of a user's VideoGames.

After this feature is implemented, an authenticated user can:

1. View their VideoGames.
2. Create a VideoGame quickly.
3. Edit a VideoGame.
4. Delete a VideoGame.
5. Associate one or more Platforms.
6. Assign zero or more predefined Genres.
7. Set AcquisitionStatus.
8. Set GameStatus when allowed.
9. Set ProgressPercentage when allowed.
10. Set Rating.
11. Set Notes.
12. Set an optional CoverImageUrl.
13. Preserve strict user isolation.
14. Be prevented from deleting a Platform that is associated with a VideoGame (the `409 Platform in use` state promised by Feature 003 becomes actually reachable).

This feature implements only VideoGame management. BoardGame management remains Feature 005.

## Context

The approved Product, Domain, and Architecture Specifications are authoritative.
This specification is the fourth feature in the approved delivery order:

1. Project foundation and local development. (implemented)
2. Authentication. (implemented)
3. Platform management. (implemented)
4. VideoGame management. (this feature)
5. BoardGame management.
6. Library browse/search/filter/sort.
7. Random Picker.
8. PWA polish and MVP end-to-end verification.

### Actual foundation produced by Features 001–003

This specification builds on the actual repository state:

- Backend: `src/backend/GameLibrary.sln` with exactly two application projects —
  `GameLibrary.Api` (ASP.NET Core Web API, .NET 10) and `GameLibrary.Core`
  (EF Core 10.0.11, Npgsql 10.0.3). `GameLibrary.Api` references `GameLibrary.Core`.
- `GameLibrary.Api/Program.cs` registers controllers, problem details,
  `AddScoped<PlatformService>()`, the `GameLibraryDbContext` with Npgsql (from
  `ConnectionStrings:Default`), a "Frontend" CORS policy, JWT bearer
  authentication (OpenID Connect metadata/JWKS discovery, audience `authenticated`,
  `MapInboundClaims = false`, `ValidAlgorithms = [ES256, RS256]`), and authorization.
  Middleware order is `UseExceptionHandler` → `UseCors` → `UseAuthentication` →
  `UseAuthorization` → `MapControllers`.
- `GameLibrary.Core/Data/GameLibraryDbContext.cs` maps `libraries` and `platforms`
  with explicit snake_case table/column names, a stored generated
  `name_normalized` column (`lower(name)`), a unique `ix_libraries_user_id`, a
  unique `ix_platforms_library_id_name_normalized`, and the
  `platforms.library_id → libraries.id` FK with `DeleteBehavior.Restrict`.
  All mapping is configured inline in `OnModelCreating`.
- Migrations: the intentionally empty `InitialCreate` and `AddPlatformManagement`
  (creates `libraries` and `platforms`).
- `GameLibrary.Core/Platforms/PlatformService.cs` is the authoritative application
  service for Platforms: it derives ownership only from the authenticated
  `userId`, owns name validation (trim → non-empty → ≤ 100), case-insensitive
  duplicate detection (pre-check plus a unique-violation `DbUpdateException`
  backstop), the lazy Library get-or-create (`EnsureLibraryAsync`, unique-violation
  re-query, no locks), and create/update/delete. Feature 003 explicitly deferred
  extraction of the ensure-library helper until a second consumer appears; Feature
  004 is that second consumer (see Backend).
- `GameLibrary.Api/Controllers/PlatformsController.cs` maps `PlatformService`
  failures to problem-details responses; `PlatformContracts.cs` holds the request/
  response records. `PlatformExceptions.cs` defines `InvalidPlatformNameException`,
  `PlatformNameConflictException`, and `PlatformNotFoundException`.
- Tests: `tests/backend/GameLibrary.IntegrationTests/` (xUnit +
  `Microsoft.AspNetCore.Mvc.Testing`). `AuthTestFactory` and `PlatformTestFactory`
  post-configure the bearer `JwtBearerOptions` to validate against a deterministic
  symmetric test key; `TestTokens.CreateToken(sub)` mints tokens for arbitrary
  `sub` values. `PlatformEndpointTests` covers create/list/two-user isolation/
  rename/delete/validation/duplicates/library lifecycle against real PostgreSQL.
  `DatabaseMigrationTests` currently asserts the `public` schema contains exactly
  `__EFMigrationsHistory`, `libraries`, and `platforms` (this assertion must change
  in this feature; see Testing and Risks / Notes). There is no backend unit-test
  project yet (Feature 003 explicitly deferred `GameLibrary.Core.Tests` until a
  feature introduces meaningful framework-independent domain logic — AcquisitionStatus
  transition rules in this feature are that trigger; see Testing).
- Frontend: Angular 22 (`src/frontend/`, standalone components, SCSS, PWA via
  `@angular/service-worker`), `@supabase/supabase-js`, `AuthService` (signal state,
  session restoration, normalized errors), route guards (`authGuard`, `guestGuard`),
  the API token interceptor, and the authenticated home placeholder at `''`
  (`features/auth/home/`). Routes: `''` (home, `authGuard`), `login`/`register`
  (guest-only), `health` (anonymous), `platforms` (protected, Feature 003).
  `features/platforms/` implements the Platform CRUD UI and exposes the reusable
  `PlatformsService` (`platforms.service.ts`, `providedIn: 'root'`). `features/games/`
  exists only as an empty `.gitkeep` skeleton folder. Environment files expose
  `apiBaseUrl` plus `supabaseUrl`/`supabaseKey`. Frontend tests run via Vitest
  through `@angular/build:unit-test` (`ng test`).
- Configuration: committed files contain only non-secret values or placeholders;
  `appsettings.Development.json` is gitignored; `appsettings.Development.example.json`
  is the committed template. `ConnectionStrings:Default` / `ConnectionStrings:Test`
  are supplied via user-secrets or environment variables.
- The current development environment uses the remote Supabase project
  `iramzxpjbnldhebykzhx` for both PostgreSQL and Auth. No Docker is required.
- README documents the Platform endpoints, the lazy Library creation, the naming
  rules, and the fact that the delete-protection `409` becomes reachable only when
  the VideoGame feature introduces the `game_platforms` join table with a
  `platform_id → platforms.id` FK declared `ON DELETE RESTRICT`. Feature 004 now
  implements that join table and the reachable `409`.

### Domain model driving this feature

The approved Domain Specification deliberately separates `Game` from
`LibraryEntry`. Game-level data for a VideoGame is: Name, optional CoverImageUrl,
creation time, zero or more predefined Genres, and the VideoGame type identity.
LibraryEntry data is user-specific: AcquisitionStatus, Rating, Notes, GameStatus,
ProgressPercentage, and Platforms. In the MVP the relationship is 1:1: every
application-created Game has exactly one LibraryEntry and Games are not shared
across users. This specification preserves that separation in the persistence
model (see Domain Decisions).

## In Scope

- The first persistent Game/LibraryEntry data: a `games` table (with a
  `game_type` discriminator), a `library_entries` table (1:1 with `games`), a
  seeded `genres` reference table, a `game_genres` join table, and the
  `game_platforms` join table promised by Feature 003 — all created through one
  EF Core migration.
- `Game`, `LibraryEntry`, `Genre`, `GameGenre`, and `GamePlatform` domain entities
  in `GameLibrary.Core`, plus the `GameType`, `AcquisitionStatus`, and `GameStatus`
  enums.
- A Core application service owning all VideoGame application/domain rules
  (ownership scoping, name validation, acquisition-status rules, GameStatus/
  Progress restrictions, rating/progress ranges, platform and genre reference
  validation) and a framework-independent `VideoGameRules` module holding the pure
  rules so they can be unit-tested without a database.
- The existing `ensure-library` helper extracted into a shared Core mechanism
  (`LibraryService`) now that a second consumer (VideoGame) exists, per the
  deferral note in Feature 003.
- A `VideoGamesController` in `GameLibrary.Api` exposing the CRUD endpoints and a
  `GenresController` exposing the read-only catalog, scoped to the authenticated
  Supabase user ID from the validated JWT `sub`.
- The approved Platform delete-protection rule made reachable: the `game_platforms`
  join table with `platform_id → platforms.id` FK declared `ON DELETE RESTRICT`,
  and the mapping of the resulting PostgreSQL FK violation to the approved
  `409 Platform in use` response without leaking database internals.
- An authenticated Angular VideoGame management UI:
  - List page (loading, empty, error, list states).
  - Create flow.
  - Edit flow.
  - Delete flow with confirmation.
  - A form covering Name, AcquisitionStatus, Platforms, Genres, GameStatus,
    ProgressPercentage, Rating, Notes, and CoverImageUrl with dynamic behavior
    that mirrors the domain rules.
  - Validation errors and API error handling (including `401` session-expired
    handling consistent with the Platforms feature).
- A minimal "Video games" navigation entry from the authenticated home view and a
  way back, without designing the final application shell.
- Backend unit tests (`GameLibrary.Core.Tests`) for the framework-independent
  `VideoGameRules`, backend integration tests against real PostgreSQL (including
  two-user isolation and acquisition transitions), and frontend tests for
  meaningful behavior.
- README updates documenting the new feature (usage, endpoints, schema, unit-test
  project, and the now-reachable delete-protection `409`).

## Out of Scope

Feature 004 must NOT implement or introduce:

- BoardGame or any board-game data: no `BoardGame` entity, no board-game columns,
  no board-game endpoints or UI. Feature 005 owns BoardGame management.
- The main unified Library screen, library search, filters, or sorting for the
  final library view (Feature 006).
- Random Picker (Feature 007).
- Favorites, shared libraries, family libraries, friends.
- External integrations, automatic imports, metadata services, external game APIs.
- Achievements, playtime, gameplay session history.
- Cover image upload, managed storage, resizing, or CDN infrastructure.
- RLS.
- Direct Angular access to application database tables.
- A global/canonical game catalog, admin game management, or genre management
  endpoints (users cannot create/edit/delete Genres).
- Prices, purchase history, collection valuation.
- Wishlist-specific purchasing workflows.
- Production deployment configuration.
- End-to-end (E2E) test suites (manual acceptance verification is specified
  instead).
- New backend application projects (only the `GameLibrary.Core.Tests` verification
  project is added; see Testing).
- NgRx, MediatR, CQRS, generic repositories, a Unit of Work abstraction, event
  sourcing, domain events, message brokers, Redis, GraphQL, or shared-kernel
  abstractions.
- Optimistic-concurrency tokens, version columns, locks, or concurrency frameworks.
- PostgreSQL enum types for the new enum columns (they are mapped to `varchar`;
  see Database / EF Core).

## Domain Decisions

### Game / LibraryEntry Persistence Representation

The approved Domain Specification mandates a deliberate separation between `Game`
(game-level data) and `LibraryEntry` (the user's personal relationship with the
game), with a 1:1 relationship in the MVP. The persistence model preserves that
separation with two tables plus association tables:

- **`games`** holds game-level data: identity, `game_type` discriminator, `name`,
  optional `cover_image_url`, and `created_at`. Genres are game-level data and are
  associated through `game_genres`.
- **`library_entries`** holds user-level data: `library_id` (ownership anchor),
  `game_id` (1:1 reference), `acquisition_status`, `rating`, `notes`, and the
  VideoGame-only `game_status` and `progress_percentage`. Platforms are
  user-level data and are associated through `game_platforms`.

Why a single `games` table with a type discriminator rather than inheritance-heavy
EF mapping:

- A VideoGame adds **no game-level columns** beyond the common Game data (Name,
  CoverImageUrl, CreatedAt). The only "VideoGame type identity" the domain requires
  is a discriminator.
- EF TPH/TPC/TPT inheritance would introduce a base `Game` class, derived
  `VideoGame`/`BoardGame` classes, discriminator configuration, and navigation
  quirks for zero current benefit.
- The simplest representation is a plain `Game` entity with a `GameType` enum
  property mapped to a `varchar` column. Feature 005 will add BoardGame-specific
  storage (player ranges, duration, interaction type) as a future decision on that
  table or a subtype table; this feature adds nothing for BoardGames.
- The same reasoning applies to LibraryEntry: `game_status` and `progress_percentage`
  are LibraryEntry data valid only for VideoGames and are simply nullable columns on
  `library_entries`. BoardGame entries will naturally keep them `null` (Feature 005
  enforces that as part of its own scope).

The `Game`/`LibraryEntry` split is therefore realized as: `games` table (game data)
+ `library_entries` table (entry data) + a unique index on `library_entries.game_id`
+ a required Game ↔ LibraryEntry relationship configured on the LibraryEntry
(dependent) side.

What the unique index guarantees (precise wording): **a Game can be referenced by
at most one LibraryEntry.** It prevents one Game being shared by multiple
LibraryEntries. It does NOT guarantee that every Game has a LibraryEntry; the index
is the database backstop for the entry side only.

The complete MVP lifecycle invariant — **every application-created Game has exactly
one LibraryEntry and orphan Games must not remain** — is enforced by the
`VideoGameService` creation lifecycle (Game + LibraryEntry created together in one
transaction), the delete lifecycle (LibraryEntry + Game removed together), and the
application tests. No triggers, deferred constraints, circular FKs, or database
procedures are added to enforce Game → LibraryEntry existence at the database.

### VideoGame Model

Game-level (`games` table):

| Property | Type | Notes |
| --- | --- | --- |
| `Id` | `Guid` | Primary key, generated by the application on create. |
| `GameType` | enum | Discriminator. Only `VideoGame` is creatable in this feature; `BoardGame` is defined for Feature 005 and must not be creatable here. |
| `Name` | `string` | Required, trimmed, max 100 characters. Game names are **not unique** (approved Domain rule). |
| `CoverImageUrl` | `string?` | Optional manual external URL. No upload/storage/resizing/CDN. |
| `CreatedAt` | `DateTimeOffset` | Server-assigned creation timestamp (supports the future "Recently added" sort). Not editable. |

LibraryEntry-level (`library_entries` table):

| Property | Type | Notes |
| --- | --- | --- |
| `Id` | `Guid` | Primary key, generated by the application on create. |
| `LibraryId` | `Guid` | FK → `libraries.id`. Ownership is always derived from the authenticated user through this Library. |
| `GameId` | `Guid` | FK → `games.id`. Required. Unique index (a Game can be referenced by at most one LibraryEntry). |
| `AcquisitionStatus` | enum | `Owned` / `Wishlist` / `Interested`. Default `Owned` on create. |
| `Rating` | `int?` | Optional, 1–5. |
| `Notes` | `string?` | Optional personal text, max 5000 characters, no rich text. |
| `GameStatus` | enum? | `Backlog` / `Playing` / `Completed` / `Abandoned` / `WantToPlay`. Valid only when `Owned`. |
| `ProgressPercentage` | `int?` | Optional, 0–100. Valid only when `Owned`. |

The API resource identifier is the **Game id** (`games.id`). The route is
`/video-games/{id}`; the `VideoGameResponse.id` is the Game id. Internally every
VideoGame maps to exactly one LibraryEntry (unique `game_id`), so every operation
resolves Game → LibraryEntry → Library and verifies the Library's `user_id` equals
the authenticated `sub`. **Every VideoGame backend operation additionally requires
`Game.GameType == GameType.VideoGame`** (see GameType guards in VideoGameService): a
Game row with any other `game_type` is outside this feature's scope and behaves
exactly like a nonexistent VideoGame (`404 Video game not found`).

### AcquisitionStatus Rules

Supported values: `Owned`, `Wishlist`, `Interested`.

Rules, stated so no agent guesses transition behavior:

- On **create**, when `acquisitionStatus` is omitted or `null`, the default is
  `Owned`.
- On **update**, `acquisitionStatus` is required; a `null`/omitted value is a `400`
  ("Acquisition status is required."). An unknown string value is a `400`
  ("Acquisition status is invalid.").
- **Owned requires at least one Platform.** This single rule is applied to the
  deduplicated resulting platform set and covers all three cases the Product/
  Domain Specifications express separately:
  - Create an Owned VideoGame with no Platforms → invalid.
  - Wishlist/Interested → Owned (update) with no Platforms → invalid.
  - An Owned VideoGame whose update would remove its final Platform while remaining
    Owned → invalid.
  In every case the server returns `400` ("An Owned video game requires at least
  one platform.").
- **Wishlist/Interested may have zero or more Platforms.** No platform requirement.
- **Owned → Wishlist/Interested clears GameStatus and ProgressPercentage.** The
  server forces `gameStatus` and `progressPercentage` to `null` for any resulting
  non-Owned state, ignoring any non-null values the client sent (the client hides
  these fields for non-Owned anyway). The cleared behavior is a normalization rule,
  not a rejection.
- **Owned → Wishlist/Interested preserves Platforms, Rating and Notes — and the
  preservation is authoritative on the backend.** When an update changes the
  persisted AcquisitionStatus from `Owned` to `Wishlist` or `Interested`, the
  backend **must use the currently persisted values** for Platform associations,
  Rating, and Notes. This is a special Domain transition rule and does NOT depend
  on Angular resending the previous values; it takes precedence over normal PUT
  replacement semantics for the preserved fields (see API). During these
  transitions the request fields `platformIds`, `rating`, `notes`, `gameStatus`,
  and `progressPercentage` are **ignored** — neither applied nor validated —
  because they cannot affect the resulting state. The ignored-field semantics are
  intentional and pinned by integration tests; a future agent must not "fix" them
  by validating fields that cannot affect the resulting state. Concretely:
  - **Platforms.** The persisted Platform associations are preserved unchanged. The
    incoming `platformIds` are neither applied nor validated. Therefore even an
    empty Platform list, a nonexistent Platform ID, or a Platform belonging to
    another user is allowed as long as every field that actually applies to the
    transition is valid; the persisted associations remain unchanged. The ignored
    Platform IDs MUST NOT be queried or resolved during the transition, so no
    Platform-existence information is exposed to the client.
  - **Rating.** The persisted Rating is preserved. The incoming `rating` is neither
    applied nor validated, so even `rating: 6` is accepted and ignored; the
    existing valid persisted Rating remains unchanged.
  - **Notes.** The persisted Notes are preserved. The incoming `notes` are neither
    applied nor validated, so an over-length Notes value in the transition request
    is accepted and ignored; the persisted Notes remain unchanged.
  - **GameStatus / ProgressPercentage.** The incoming values are ignored and the
    persisted result is forced to `gameStatus = null` and `progressPercentage =
    null`.
  - **Fields still validated and applied during the transition:** `name`,
    `coverImageUrl`, `genreIds`, and the target `acquisitionStatus`. This asymmetry
    is intentional and documented.
- A resulting state that violates any rule is rejected with `400`. The database
  CHECK constraint `acquisition_status = 'Owned' OR (game_status IS NULL AND
  progress_percentage IS NULL)` (see Database / EF Core) is the integrity backstop
  for the non-Owned-clears rule.

### GameStatus / Progress Rules

- `GameStatus` values: `Backlog`, `Playing`, `Completed`, `Abandoned`, `WantToPlay`.
  No `IsCompleted` field exists; `Completed` is authoritative.
- `GameStatus` is optional and valid only for Owned entries.
- `ProgressPercentage` is optional, an integer from 0 through 100 inclusive, and
  valid only for Owned entries.
- **Progress and GameStatus do not automatically determine each other.** The server
  stores exactly what is sent (subject to the Owned-only rules above). There is no
  logic that sets one from the other.
- Unknown `gameStatus` string values are a `400` ("Game status is invalid.").
  `null` means "not set".
- For any non-Owned resulting state, both are normalized to `null` (see
  AcquisitionStatus Rules).

### Platform Relationship

- A VideoGame LibraryEntry may have zero or more Platforms depending on
  AcquisitionStatus: Owned → at least 1; Wishlist/Interested → 0 or more.
- The relationship is many-to-many: one VideoGame LibraryEntry may use multiple
  Platforms; one Platform may be associated with multiple VideoGame LibraryEntries.
- The association is user-level (it belongs to the LibraryEntry, not the Game),
  consistent with the Domain Specification ("A Platform may be associated with
  multiple VideoGame LibraryEntries").
- **Naming vs. semantics.** The join table is named `game_platforms` and its
  relationship columns are `library_entry_id` and `platform_id`. Despite the table
  name, Platforms are associated with the user's `LibraryEntry`, not directly with
  the conceptual `Game`. This follows the approved Domain model (Platforms are
  LibraryEntry data). The table name and relationship columns are retained as
  documented and are not renamed or changed. The `GamePlatform` entity and/or its
  EF mapping MUST contain a concise code comment stating: "Despite the table name,
  Platform association is LibraryEntry-level/user-specific data.
  `library_entry_id` is intentional." README documentation must reflect the same.
- Persistence: a `game_platforms` join table. This is the exact structural
  mechanism Feature 003 promised. Feature 003 documented the table name
  `game_platforms` and the FK `game_platforms.platform_id → platforms.id` with
  `ON DELETE RESTRICT`; this feature honors both. The join's other FK references
  `library_entries.id` (the domain-correct association target) and is declared
  `ON DELETE CASCADE` so deleting a VideoGame removes its Platform associations
  automatically.
- **Platform delete protection (now reachable):** because
  `game_platforms.platform_id → platforms.id` is `ON DELETE RESTRICT`, PostgreSQL
  rejects any attempt to delete a Platform referenced by one or more
  `game_platforms` rows. `PlatformService.DeleteAsync` maps the resulting
  FK-violation `DbUpdateException` to a `PlatformInUseException`, and
  `PlatformsController.Delete` returns the approved `409 Platform in use`
  ("This platform is in use and cannot be deleted."). The existing frontend delete
  flow already renders this message, so no frontend change is required for it.
  **This protection is independent of the referencing entry's AcquisitionStatus:**
  a Platform referenced by a Wishlist/Interested VideoGame entry is just as
  protected as one referenced by an Owned entry, because the FK does not consider
  AcquisitionStatus. See Technical Requirements / Backend and the relational
  backstop tests.

### Genre Representation

Genres are a fixed, immutable, application-owned catalog (Architecture
Specification section 23). Decision:

- **Persistence:** a `genres` reference table (`id` `uuid` PK, `name` `varchar(50)`
  with a unique index) seeded with exactly the 15 approved catalog values through
  the EF migration's `HasData` configuration (fixed, deterministic GUIDs). A
  `game_genres` join table (`game_id` + `genre_id`) associates Genres with Games
  (Genres are game-level data per the Product/Domain Specifications).
- **Why a reference table + join rather than an array column or a duplicated code
  list:** (1) it gives a database integrity backstop — an unknown `genre_id` is
  rejected by the FK constraint, so no invalid genre value can be persisted;
  (2) Feature 006's Genre filter (multi-select OR) queries cleanly;
  (3) the frontend gets the catalog from one authoritative place — a `GET /genres`
  endpoint — so the approved list is never duplicated in the frontend or
  hard-coded in multiple backend locations.
- **Source of truth.** `GenresCatalog` in `GameLibrary.Core` is the authoritative
  code definition of the 15 approved genres. The EF seed configuration (`HasData`)
  derives from this catalog, and the migration generated from that model contains
  the corresponding persisted seed data. `GenresCatalog` and the migration seed are
  therefore not independent authorities: the code definition drives the seed, and
  the migration is generated from the model that uses it. If `GenresCatalog`
  changes in the future, (1) the model seed changes, (2) a new EF Core migration
  must be generated, and (3) tests must verify the persisted catalog matches the
  approved catalog (see Risks / Notes).
- **No genre-management endpoints.** `GET /genres` is read-only. Users cannot
  create/edit/delete Genres.
- The catalog (exact names, unchanged casing): Action, Adventure, RPG, Strategy,
  Simulation, Sports, Racing, Fighting, Shooter, Platformer, Puzzle, Horror, Rhythm,
  Party, Other.
- A VideoGame may have zero or more Genres.

### Deletion Rules

- `DELETE /video-games/{id}` deletes the caller's VideoGame LibraryEntry **and** its
  associated Game in one transaction (the approved MVP rule: deleting a LibraryEntry
  also deletes its Game; no orphan Games). The service removes (1) the LibraryEntry
  and (2) the Game as one application operation/transaction.
- The Game ↔ LibraryEntry relationship is configured as **required on the
  LibraryEntry (dependent) side**: Game is the principal, LibraryEntry is the
  dependent, `LibraryEntry.GameId` is required, a Game has at most one LibraryEntry
  (unique `ix_library_entries_game_id`), and `ON DELETE RESTRICT` remains on
  LibraryEntry → Game. EF's dependency ordering may issue the SQL in the correct
  order when the required relationship is configured; if necessary the
  implementation may explicitly save/delete the dependent (LibraryEntry) before the
  principal (Game) within the transaction to satisfy the FK. The approved deletion
  result is unchanged.
- Deleting the entry removes its Platform associations automatically via
  `game_platforms.library_entry_id → library_entries.id` `ON DELETE CASCADE`.
- Deleting the Game removes its Genre associations automatically via
  `game_genres.game_id → games.id` `ON DELETE CASCADE`.
- `library_entries.library_id → libraries.id` is `ON DELETE RESTRICT` (never
  silently cascade user data, matching the Platforms FK).
- `library_entries.game_id → games.id` is `ON DELETE RESTRICT`: a Game cannot be
  deleted while its LibraryEntry still exists, enforcing the intended deletion
  order (entry first, then Game) at the database. The service always deletes the
  entry and the Game together, so the RESTRICT FK never blocks a VideoGame
  deletion.
- Deletion never affects another user's data: the entry is loaded scoped to the
  authenticated user's Library, and cross-user Game ids behave as `404`.

### Validation Rules

Validation is authoritative in `GameLibrary.Core`. Order matters (validate before
persistence work):

- **Name:** trim leading/trailing whitespace → non-empty → trimmed length ≤ 100.
  Names are **not** unique; no duplicate detection exists for VideoGame names.
- **CoverImageUrl:** optional. When provided, trim it; if it is empty after trim it
  is stored as `null`. Non-null values must be at most 2048 characters and must
  start with `http://` or `https://`. This is the only URL validation; do not
  overbuild it (no DNS checks, no well-formed-URI libraries beyond a simple scheme
  prefix check).
- **Notes:** optional. Trim it; if empty after trim it is stored as `null`. Non-null
  values must be at most 5000 characters. No rich text.
- **Rating:** optional integer in `[1, 5]`.
- **ProgressPercentage:** optional integer in `[0, 100]`.
- **AcquisitionStatus:** see AcquisitionStatus Rules (default on create, required on
  update, valid enum values).
- **GameStatus:** see GameStatus / Progress Rules.
- **Platform references:** the deduplicated set of requested Platform ids must all
  resolve to Platforms **in the caller's Library**. An id that does not exist, and
  an id that belongs to another user's Library, produce the same `400` ("One or more
  platforms are not available in your library.") so another user's Platform
  existence is never leaked. This applies to create and to normal (non-transition)
  updates only: the Owned → Wishlist/Interested transition preserves the persisted
  Platform associations and does NOT validate, apply, or query/resolve the
  request's `platformIds` — so no Platform-existence information is exposed during
  a transition (see AcquisitionStatus Rules).
- **Genre references:** the deduplicated set of requested Genre ids must all exist in
  the `genres` catalog. Any unknown id → `400` ("One or more genres are invalid.").
  Genre references are always taken from the request, including during the
  Owned → Wishlist/Interested transition.
- Duplicate ids within `platformIds` or `genreIds` are deduplicated (set semantics),
  never rejected.
- **Resulting-state rules:** Owned ⇒ ≥ 1 Platform; non-Owned ⇒ GameStatus and
  ProgressPercentage are `null` (normalized). These are applied to the resulting
  state of every create and update.

## Technical Requirements

### Backend

Structure in `GameLibrary.Core` (domain/application rules + persistence):

```
GameLibrary.Core/
├── Libraries/
│   ├── Library.cs                 (extended: + LibraryEntries collection)
│   └── LibraryService.cs          (new: shared ensure-library helper)
├── Platforms/
│   ├── Platform.cs                (unchanged)
│   ├── PlatformService.cs         (extended: map delete FK violation → PlatformInUseException)
│   └── PlatformExceptions.cs      (+ PlatformInUseException)
├── Games/
│   ├── Game.cs
│   ├── GameType.cs                (enum: VideoGame, BoardGame)
│   ├── LibraryEntry.cs
│   ├── AcquisitionStatus.cs       (enum: Owned, Wishlist, Interested)
│   ├── GameStatus.cs              (enum: Backlog, Playing, Completed, Abandoned, WantToPlay)
│   ├── Genre.cs
│   ├── GameGenre.cs
│   ├── GamePlatform.cs
│   ├── GenresCatalog.cs           (the 15 approved catalog values used for seeding)
│   ├── VideoGameRules.cs          (pure, framework-independent validation/normalization)
│   ├── VideoGameService.cs        (EF-backed application service)
│   └── VideoGameExceptions.cs
└── Data/
    ├── GameLibraryDbContext.cs    (extended)
    └── Migrations/                (+ AddVideoGameManagement)
```

Structure in `GameLibrary.Api` (HTTP boundary only):

```
GameLibrary.Api/
├── Controllers/
│   ├── VideoGamesController.cs
│   └── GenresController.cs
├── VideoGames/
│   └── VideoGameContracts.cs
└── Genres/
    └── GenreContracts.cs
```

#### Shared Library helper

Feature 003 deferred extracting the ensure-library mechanism until a second
consumer appeared. Feature 004 is that consumer, so the helper is extracted into
`LibraryService` (namespace `GameLibrary.Core.Libraries`, concrete class, depends
only on `GameLibraryDbContext`):

- `Task<Library> EnsureAsync(string userId, CancellationToken ct)` — get-or-create
  with the exact unique-violation behavior currently in `PlatformService`
  (re-query on the unique `ix_libraries_user_id` violation, no locks). This is the
  existing `EnsureLibraryAsync` logic relocated verbatim.
- `PlatformService` is refactored to use `LibraryService` for the same behavior
  (a small, justified refactor that Feature 003 anticipated; Platform behavior and
  its tests must remain unchanged).
- Register `AddScoped<LibraryService>()` in `Program.cs` (in addition to the new
  `VideoGameService`).

#### VideoGameRules (pure rules)

`VideoGameRules` is a static class owning all framework-independent rules so they
can be unit-tested without a database (see Testing). The EF-backed service calls it
for every input before doing persistence work. At minimum it exposes:

- `const int MaxNameLength = 100`, `MaxNotesLength = 5000`,
  `MaxCoverImageUrlLength = 2048`.
- `string ValidateAndTrimName(string? name)` — trim → non-empty → ≤ 100, else
  throws `InvalidVideoGameException` with the appropriate message.
- `string? ValidateAndNormalizeCoverImageUrl(string? url)` — trim; empty → `null`;
  length ≤ 2048 and `http://`/`https://` prefix when non-null.
- `string? ValidateAndNormalizeNotes(string? notes)` — trim; empty → `null`; length
  ≤ 5000.
- `AcquisitionStatus ParseAcquisitionStatus(string? value, bool isCreate)` —
  `null` on create → `Owned`; `null` on update → throws
  ("Acquisition status is required."); unknown value → throws
  ("Acquisition status is invalid.").
- `GameStatus? ParseGameStatus(string? value)` — `null` → `null`; unknown → throws
  ("Game status is invalid.").
- `void ValidateRating(int? rating)` — `null` ok; must be 1–5.
- `void ValidateProgressPercentage(int? progress)` — `null` ok; must be 0–100.
- `void ValidatePlatformRequirement(AcquisitionStatus status, int platformCount)` —
  throws when `status == Owned && platformCount == 0`
  ("An Owned video game requires at least one platform.").
- `(GameStatus? GameStatus, int? ProgressPercentage) NormalizeStatusAndProgress(
  AcquisitionStatus status, GameStatus? gameStatus, int? progressPercentage)` —
  returns `(null, null)` when `status != Owned`, otherwise the input values
  unchanged.
- `bool IsOwnedToNonOwnedTransition(AcquisitionStatus currentStatus,
  AcquisitionStatus targetStatus)` — returns `true` exactly when
  `currentStatus == Owned && targetStatus != Owned`. This pure predicate drives the
  backend-authoritative preservation rule: when it is `true`, the service preserves
  the persisted Platforms, Rating, and Notes and forces GameStatus/Progress to
  `null` (see VideoGameService). It is unit-tested without a database.

All exceptions are `InvalidVideoGameException` carrying the exact detail message.

#### VideoGameService

`VideoGameService` is a concrete application service (no interface, no generic
repository, no Unit of Work) depending on `GameLibraryDbContext` and
`LibraryService`, registered with `AddScoped<VideoGameService>()`. It is the
authoritative backend enforcement layer for VideoGame operations. All methods take
the authenticated `userId` (never a client-supplied ID).

- `Task<IReadOnlyList<VideoGameView>> ListAsync(string userId, CancellationToken ct)`
  — returns the caller's VideoGames (Game + its LibraryEntry + Platform ids + Genre
  ids) in a deterministic, case-insensitive order, **filtered to Games owned by the
  authenticated user's Library AND `Game.GameType == GameType.VideoGame`**. Future
  BoardGame rows must never appear in `GET /video-games`:
  1. normalized/case-insensitive Name ascending;
  2. original Name ascending;
  3. `CreatedAt` ascending;
  4. `Id` ascending.
  This is implemented with an EF Core/PostgreSQL-translatable expression (for
  example `OrderBy(g => g.Name.ToLower())` translating to `lower(name)`, followed by
  the original-name, `CreatedAt`, and `Id` tiebreakers). **No generated
  normalized-name column is introduced for Games** because Game names are not
  unique (the Platform `name_normalized` pattern is not reused). Empty when the
  caller has no Library yet; no row is created on read.
- `Task<VideoGameView> CreateAsync(string userId, VideoGameInput input, CancellationToken ct)`
  — validates and normalizes every field via `VideoGameRules` **before** persistence
  work; ensures the user's Library exists; resolves the requested Platform ids
  scoped to the caller's Library and the Genre ids against the catalog; creates the
  `Game` (GameType `VideoGame`, `Id` new, `CreatedAt = UtcNow`) and the
  `LibraryEntry` (Id new, default/parsed AcquisitionStatus, normalized status/
  progress) plus the `GameGenre` and `GamePlatform` rows; saves once; returns the
  created view.
- `Task<VideoGameView> UpdateAsync(string userId, Guid gameId, VideoGameInput input, CancellationToken ct)`
  — loads the Game and its LibraryEntry scoped to the user's Library (see
  `LoadOwnedAsync`); determines the update semantics from the **persisted**
  AcquisitionStatus and the parsed target status; then validates and normalizes
  only the fields it applies:
  - **Owned → Wishlist/Interested (backend-authoritative transition):** when
    `VideoGameRules.IsOwnedToNonOwnedTransition(persistedStatus, targetStatus)` is
    `true`, the service preserves the currently persisted Platform associations,
    Rating, and Notes and forces `GameStatus`/`ProgressPercentage` to `null`. The
    request's `platformIds`, `rating`, and `notes` are ignored for these fields —
    neither applied nor validated, and the request's `platformIds` are NOT queried
    or resolved (no existence information is exposed), because they cannot affect
    the resulting state (an empty `platformIds` array, a foreign/nonexistent
    Platform id, `rating: 6`, or over-length Notes cannot erase or corrupt the
    persisted values). `gameStatus` and `progressPercentage` are ignored and stored
    as `null`. The request's `name`, `coverImageUrl`, `genreIds`, and
    `acquisitionStatus` are validated normally and applied, and the Game's
    `GameGenre` rows are replaced with the resolved request set. The Platform
    associations are left untouched.
  - **All other updates (normal full-replacement PUT):** the request represents the
    complete mutable state and all fields are validated normally. The Game's
    `GameGenre` rows and the entry's `GamePlatform` rows are replaced with the
    resolved request sets; optional metadata (`CoverImageUrl`, `Notes`, `Rating`,
    `GameStatus`, `ProgressPercentage`) follows normal replacement semantics
    (omitted/null clears); the resulting Domain state must be valid.
  applies the changes; saves once; returns the updated view.
- `Task DeleteAsync(string userId, Guid gameId, CancellationToken ct)` — loads the
  Game + entry scoped to the user's Library and removes both in one transaction
  (cascades remove the association rows; see Deletion Rules).

Supporting behavior:

- `LoadOwnedAsync(userId, gameId)`: query `games` where `Id == gameId`,
  `Game.GameType == GameType.VideoGame`, and the Game's LibraryEntry's Library
  `UserId == userId`. Not found → throws `VideoGameNotFoundException`. Cross-user
  Game ids are indistinguishable from nonexistent ids (same exception, same `404`).
  A Game that exists and belongs to the user but has another `GameType` behaves
  exactly like a nonexistent VideoGame (`404 Video game not found`). This prevents
  future `PUT /video-games/{boardGameId}` and `DELETE /video-games/{boardGameId}`
  from operating on a BoardGame.
- `VideoGameView` is a plain Core read-model record (not an EF entity):
  `(Guid Id, string Name, string? CoverImageUrl, DateTimeOffset CreatedAt,
  AcquisitionStatus AcquisitionStatus, GameStatus? GameStatus, int? ProgressPercentage,
  int? Rating, string? Notes, IReadOnlyList<Guid> PlatformIds, IReadOnlyList<Guid> GenreIds)`.
- `VideoGameInput` is a plain Core record carrying the raw create/update fields
  (nullable name, optional metadata, raw enum strings, id lists).
- Platform resolution: query `platforms` where `Id ∈ platformIds` and
  `Library.UserId == userId`. If the resolved count differs from the deduplicated
  requested count, throw `InvalidVideoGameException` ("One or more platforms are not
  available in your library."). This single check covers unknown ids and another
  user's ids identically (no leakage). Platform resolution applies to create and to
  normal (non-transition) updates only; the Owned → Wishlist/Interested transition
  preserves the persisted associations and does not resolve or query the request's
  `platformIds` (so no Platform-existence information is exposed).
- Genre resolution: query `genres` where `Id ∈ genreIds`; a count mismatch → throw
  `InvalidVideoGameException` ("One or more genres are invalid.").
- Concurrent-delete race: a Platform (or Genre — not possible for the immutable
  catalog in practice) deleted between resolution and save surfaces as a
  FK-violation `DbUpdateException`; catch it and map to the same
  `InvalidVideoGameException` messages above. No locks are introduced.
- List queries load Platform/Genre ids through the navigations
  (`entry.GamePlatforms.Select(gp => gp.PlatformId)`, `game.GameGenres.Select(gg => gg.GenreId)`).

#### Platform delete protection (Feature 003 backend change)

- Add `PlatformInUseException` to `PlatformExceptions.cs`
  (message "This platform is in use and cannot be deleted.").
- `PlatformService.DeleteAsync` currently removes the Platform and calls
  `SaveChangesAsync`. Wrap the save: catch `DbUpdateException` whose inner exception
  is a `PostgresException` with `SqlState == PostgresErrorCodes.ForeignKeyViolation`
  and throw `PlatformInUseException`. No constraint name, SQL, or other database
  internal is exposed.
- `PlatformsController.Delete` adds a `catch (PlatformInUseException)` returning
  `Problem(title: "Platform in use", detail: ex.Message, statusCode: 409)`.
- No other Platform behavior changes; existing `PlatformEndpointTests` must pass
  unchanged, plus the new in-use case.

#### Controllers

- `VideoGamesController` (`[ApiController]`, `[Route("video-games")]`,
  `[Authorize]`): `GET` List, `POST` Create, `PUT {id:guid}` Update, `DELETE
  {id:guid}` Delete. Each derives the user from `User.GetSupabaseUserId()` and
  returns `401` when it is `null` (mirroring the existing controllers). It maps
  `InvalidVideoGameException` → `400` (title "Invalid video game", detail = message),
  `VideoGameNotFoundException` → `404` (title "Video game not found"), and maps the
  view to the response contract. It never accepts a user/owner/library ID from the
  body or query string.
- `GenresController` (`[ApiController]`, `[Route("genres")]`, `[Authorize]`):
  `GET` returns the catalog as `GenreResponse[]` ordered by name ascending. It is
  `[Authorize]` like all business endpoints; the catalog is not user-specific but
  no anonymous endpoint is added.

### API

Routes follow the existing no-prefix convention (`auth`, `health`, `platforms`).
All endpoints require authentication. Request/response are JSON.

| Method | Route | Auth | Success | Errors |
| --- | --- | --- | --- | --- |
| `GET` | `/video-games` | required | `200` with a JSON array of `VideoGameResponse` containing only the caller's `VideoGame`-type Games in deterministic case-insensitive order (case-insensitive Name ascending, original Name ascending, `createdAt` ascending, `id` ascending) | `401` |
| `POST` | `/video-games` | required | `201` with the created `VideoGameResponse` (no `Location` header; there is no single-resource `GET /video-games/{id}` to point it at) | `400`, `401` |
| `PUT` | `/video-games/{id}` | required | `200` with the updated `VideoGameResponse` | `400`, `401`, `404` |
| `DELETE` | `/video-games/{id}` | required | `204` (no body) | `401`, `404` |
| `GET` | `/genres` | required | `200` with a JSON array of `GenreResponse` ordered by name ascending | `401` |

- **PUT vs PATCH: `PUT`, kept as full replacement.** Do NOT introduce `PATCH`. For
  updates that are NOT an `Owned → Wishlist` or `Owned → Interested` transition,
  `PUT` represents the complete mutable state:
  - optional values omitted/null may be cleared according to the existing contract;
  - Platform sets are replaced by the submitted set;
  - Genre sets are replaced by the submitted set;
  - normal validation applies to the resulting state.
  The request always carries the complete set of mutable fields; the client always
  sends the complete mutable state for normal updates.
- **Special transition exception (Owned → Wishlist/Interested).** When the update
  is an `Owned → Wishlist` or `Owned → Interested` transition, the backend's
  authoritative preservation rule overrides full replacement for the preserved
  fields: Platforms, Rating, and Notes are taken from the currently persisted state.
  The request's `platformIds`, `rating`, and `notes` are ignored — neither applied
  nor validated — so an empty `platformIds` array, a foreign/nonexistent Platform
  id, `null` or out-of-range Rating, and over-length Notes cannot erase or corrupt
  them, and the ignored Platform ids are not queried. `gameStatus` and
  `progressPercentage` are forced to `null`, and `name`, `coverImageUrl`,
  `genreIds`, and the target `acquisitionStatus` are taken from the request
  normally. See AcquisitionStatus Rules and VideoGameService.
- **Deliberate MVP tradeoffs (documented, do not change):** full-replacement `PUT`
  and last-write-wins concurrency are deliberate MVP tradeoffs. Do NOT add
  optimistic concurrency, `RowVersion`, ETags, locks, merge-patch behavior, or a
  single-resource `GET /video-games/{id}`.
- **No single-resource `GET /video-games/{id}`.** The list returns the complete
  `VideoGameResponse` for every entry, which is everything the edit form needs to
  prefill. Do not add a single-GET endpoint.
- **GameType guards.** `GET /video-games` returns only Games with
  `Game.GameType == GameType.VideoGame`. `PUT`/`DELETE /video-games/{id}` return
  `404 Video game not found` for a Game id whose `GameType` is not `VideoGame`,
  indistinguishable from a nonexistent VideoGame (see VideoGameService
  `LoadOwnedAsync`).
- **`GET /genres` is added** because the frontend form genuinely needs the catalog
  to render the Genre multi-select and the catalog must have a single authoritative
  source (the seeded `genres` table, driven by `GenresCatalog`).

Request contracts (`VideoGameContracts.cs`). Create and update use the same shape;
the service distinguishes the create default (`Owned`) from the update requirement:

- Create: `CreateVideoGameRequest(string? Name, string? CoverImageUrl, string?
  AcquisitionStatus, IReadOnlyList<Guid>? PlatformIds, IReadOnlyList<Guid>? GenreIds,
  string? GameStatus, int? ProgressPercentage, int? Rating, string? Notes)`.
- Update: `UpdateVideoGameRequest` with the same fields (`string?` types at the HTTP
  boundary; the service enforces that `AcquisitionStatus` is required on update).
- Response: `VideoGameResponse(Guid Id, string Name, string? CoverImageUrl, string
  AcquisitionStatus, IReadOnlyList<Guid> PlatformIds, IReadOnlyList<Guid> GenreIds,
  string? GameStatus, int? ProgressPercentage, int? Rating, string? Notes,
  DateTimeOffset CreatedAt)`.

The list response is a plain JSON array of `VideoGameResponse`. No wrapper object,
no pagination. Enum values in JSON use their enum names (`"Owned"`, `"Wishlist"`,
`"Interested"`, `"Backlog"`, `"Playing"`, `"Completed"`, `"Abandoned"`,
`"WantToPlay"`).

Genre contract (`GenreContracts.cs`): `GenreResponse(Guid Id, string Name)`.

EF entities are never exposed by the API.

### Database / EF Core

EF Core migrations remain the sole authority for the application schema. No Supabase
dashboard edits, no second migration workflow, no RLS.

Add a migration named `AddVideoGameManagement`. It creates five tables, seeds the
genres, and adds indexes, FKs, and CHECK constraints.

Table `games`:

| Column | Type | Constraints |
| --- | --- | --- |
| `id` | `uuid` | Primary key. |
| `game_type` | `varchar` | Not null. Enum name string. Feature 004 creates only `VideoGame`. `BoardGame` is a permitted CHECK value for the future Feature 005 but MUST NOT be creatable through this feature's endpoints (see the deliberate CHECK decision below). |
| `name` | `varchar(100)` | Not null. |
| `cover_image_url` | `text` | Nullable. |
| `created_at` | `timestamptz` | Not null. |

Table `library_entries`:

| Column | Type | Constraints |
| --- | --- | --- |
| `id` | `uuid` | Primary key. |
| `library_id` | `uuid` | Not null. FK → `libraries.id`, `ON DELETE RESTRICT`. Indexed. |
| `game_id` | `uuid` | Not null. FK → `games.id`, `ON DELETE RESTRICT`. Required relationship (LibraryEntry is the dependent, Game the principal). Unique index (a Game can be referenced by at most one LibraryEntry). |
| `acquisition_status` | `varchar` | Not null. Enum name string. |
| `rating` | `int` | Nullable. CHECK `rating IS NULL OR (rating BETWEEN 1 AND 5)`. |
| `notes` | `text` | Nullable. |
| `game_status` | `varchar` | Nullable. Enum name string. |
| `progress_percentage` | `int` | Nullable. CHECK `progress_percentage IS NULL OR (progress_percentage BETWEEN 0 AND 100)`. |

Table `genres` (seeded reference data):

| Column | Type | Constraints |
| --- | --- | --- |
| `id` | `uuid` | Primary key. Fixed, deterministic GUIDs supplied by the seed. |
| `name` | `varchar(50)` | Not null. Unique index. |

Table `game_genres` (Game-level Genre association):

| Column | Type | Constraints |
| --- | --- | --- |
| `game_id` | `uuid` | FK → `games.id`, `ON DELETE CASCADE`. Indexed. |
| `genre_id` | `uuid` | FK → `genres.id`, `ON DELETE RESTRICT`. Indexed. |
| — | — | Composite primary key `(game_id, genre_id)`. |

Table `game_platforms` (the Feature 003-promised join; Entry-level Platform
association):

| Column | Type | Constraints |
| --- | --- | --- |
| `library_entry_id` | `uuid` | FK → `library_entries.id`, `ON DELETE CASCADE`. |
| `platform_id` | `uuid` | FK → `platforms.id`, `ON DELETE RESTRICT`. Indexed. |
| — | — | Composite primary key `(library_entry_id, platform_id)`. |

Table-level CHECK constraints:

- `library_entries`: `acquisition_status = 'Owned' OR (game_status IS NULL AND
  progress_percentage IS NULL)` — integrity backstop for the non-Owned-clears rule.
  It is independent of application normalization: the database rejects any persisted
  row that places GameStatus/ProgressPercentage on a non-Owned entry, even when
  written directly. The relational backstop tests attempt the invalid persisted
  state directly (SQL/test DbContext), not through the API, because the service
  correctly normalizes the state before persistence.
- `games`: `game_type IN ('VideoGame', 'BoardGame')` — **intentionally** includes
  `BoardGame`. This is a deliberate schema decision: `GameType` is the approved
  discriminator; BoardGame is the immediately following approved feature (005);
  allowing the discriminator value does not implement BoardGame functionality;
  the API/application services remain responsible for which type each feature can
  create/manage; and the VideoGame `GameType` guards (see VideoGameService) prevent
  cross-type operations. Feature 004 MUST NOT create BoardGames through application
  endpoints.

Mapping notes for `GameLibraryDbContext.OnModelCreating` (configure inline in
`OnModelCreating`; do not introduce `IEntityTypeConfiguration` classes):

- Add `DbSet<Game> Games`, `DbSet<LibraryEntry> LibraryEntries`, `DbSet<Genre> Genres`.
  The join tables are configured as entities (`GameGenre`, `GamePlatform`) with
  composite keys; explicit DbSets for them are optional (migrations model them
  regardless).
- Explicit snake_case table and column names as above.
- Enum properties (`GameType`, `AcquisitionStatus`, `GameStatus`) are mapped to
  `varchar` columns via value conversion storing the enum name strings. **Do not
  create PostgreSQL enum types.** Column sizes: `game_type` `varchar(20)`,
  `acquisition_status` `varchar(20)`, `game_status` `varchar(20)`.
- **Enum persistence contract.** The persisted values are the C# enum names as
  varchar/string values and are part of the database storage contract. The
  application reads and writes only these enum-name strings; no other
  representation (numeric, snake_case, or PostgreSQL enum types) is stored. If a C#
  enum member is renamed in the future, the corresponding persisted value and any
  database CHECK constraints that reference the enum name string must be updated
  through an EF Core migration.
- `Game.Name`: `HasMaxLength(100)`.
- `LibraryEntry.GameId`: unique index `ix_library_entries_game_id` (a Game can be
  referenced by at most one LibraryEntry — the database backstop for the entry side
  of the MVP 1:1 rule).
- `LibraryEntry.LibraryId` → `libraries.id`: `DeleteBehavior.Restrict`, index
  `ix_library_entries_library_id`.
- `LibraryEntry.GameId` → `games.id`: `DeleteBehavior.Restrict`.
- `GameGenre.GameId` → `games.id`: `DeleteBehavior.Cascade`.
- `GameGenre.GenreId` → `genres.id`: `DeleteBehavior.Restrict`.
- `GamePlatform.LibraryEntryId` → `library_entries.id`: `DeleteBehavior.Cascade`.
- `GamePlatform.PlatformId` → `platforms.id`: `DeleteBehavior.Restrict` (this is the
  approved delete-protection FK), index `ix_game_platforms_platform_id`.
- `Library` gains a `LibraryEntries` collection (alongside `Platforms`).
- The `Game` ↔ `LibraryEntry` 1:1 relationship is configured as **required on the
  LibraryEntry (dependent) side**: `Game` is the principal, `LibraryEntry` is the
  dependent, `LibraryEntry.GameId` is required, a Game has at most one LibraryEntry
  through the unique `ix_library_entries_game_id` index, and `ON DELETE RESTRICT`
  remains on LibraryEntry → Game. EF's dependency ordering may issue delete SQL in
  the correct order when the required relationship is configured; if necessary the
  service explicitly deletes/saves the dependent (LibraryEntry) before the
  principal (Game) within the deletion transaction. No triggers, deferred
  constraints, circular FKs, or database procedures are added to enforce
  Game → LibraryEntry existence.
- `Game` has a `LibraryEntry` navigation (1:1); `LibraryEntry` has `Library` and
  `Game` navigations.
- The `GamePlatform` entity and/or its EF mapping carries the semantic comment:
  "Despite the table name, Platform association is LibraryEntry-level/user-specific
  data. `library_entry_id` is intentional." (see Platform Relationship).
- CHECK constraints are added with `ToTable(t => t.HasCheckConstraint(name, sql))`
  or the EF equivalent for the rating, progress, status/progress, and game_type
  rules above.
- `Genre` seeding: `HasData` in `OnModelCreating` inserts the 15 catalog values
  with fixed, deterministic GUIDs (stable across environments and migrations). The
  names must match the approved catalog exactly. `GenresCatalog` in `GameLibrary.Core`
  is the single code definition of the 15 names used by the seed.

Concurrency/integrity behavior (documented so the implementer does not invent
schemes):

- **Library get-or-create race:** unchanged — the unique `ix_libraries_user_id`
  index plus the re-query-on-violation handled by `LibraryService.EnsureAsync`.
  No locks.
- **Concurrent Platform deletion vs. VideoGame creation/update:** the
  `game_platforms.platform_id` FK rejects inserting an association for a Platform
  deleted between resolution and save; map the FK-violation `DbUpdateException` to
  the same invalid-platform `400`.
- **No optimistic concurrency:** concurrent edits of the same VideoGame use
  last-write-wins (no `RowVersion`/version column), acceptable for the MVP. No
  locks, event systems, or concurrency frameworks.

The migration is committed to source control.

### Frontend

Build the feature under `src/app/features/games/` (replacing the `.gitkeep` skeleton
with real code; `features/games/` is the approved location from the Architecture
Specification and the home of both game types — BoardGame will add a sibling area in
Feature 005). No NgRx; use services, signals, and local/component state.

Files:

```
src/app/features/games/
├── genres.ts                      # interface Genre { id: string; name: string }
├── genres.service.ts              # HTTP client for GET /genres
├── video-game.ts                  # VideoGame interface + status string-literal types
├── video-games.service.ts         # HTTP client for the VideoGames API
├── video-games-list/
│   ├── video-games-list.ts
│   ├── video-games-list.html
│   ├── video-games-list.scss
│   └── video-games-list.spec.ts
└── video-game-form/
    ├── video-game-form.ts
    ├── video-game-form.html
    ├── video-game-form.scss
    └── video-game-form.spec.ts
```

- `genres.service.ts` (`@Injectable({ providedIn: 'root' })`): `list()` → `GET
  ${environment.apiBaseUrl}/genres` returning `Genre[]`.
- `video-games.service.ts` (`@Injectable({ providedIn: 'root' })`): wraps the four
  HTTP calls against `${environment.apiBaseUrl}/video-games`, returning
  `VideoGame`/`VideoGame[]`. `create(input)`, `update(id, input)`, `delete(id)`,
  `list()`. The existing API token interceptor attaches the bearer token
  automatically; the service does nothing special for auth.
- `video-game.ts`:
  ```ts
  export type AcquisitionStatus = 'Owned' | 'Wishlist' | 'Interested';
  export type GameStatus = 'Backlog' | 'Playing' | 'Completed' | 'Abandoned' | 'WantToPlay';

  export interface VideoGame {
    id: string;
    name: string;
    coverImageUrl: string | null;
    acquisitionStatus: AcquisitionStatus;
    platformIds: string[];
    genreIds: string[];
    gameStatus: GameStatus | null;
    progressPercentage: number | null;
    rating: number | null;
    notes: string | null;
    createdAt: string;
  }

  export interface VideoGameInput {
    name: string;
    coverImageUrl: string | null;
    acquisitionStatus: AcquisitionStatus;
    platformIds: string[];
    genreIds: string[];
    gameStatus: GameStatus | null;
    progressPercentage: number | null;
    rating: number | null;
    notes: string | null;
  }
  ```

#### Platform source

Platforms come only from the existing Platform API. The video-game form injects the
existing `PlatformsService` (`features/platforms/platforms.service.ts`) and loads
the user's Platforms on open. Platform data is never duplicated or hard-coded.

Quick-add behavior when opening a **new** VideoGame form with
`AcquisitionStatus = Owned` (a frontend convenience only; the backend remains the
authoritative enforcement layer):

- If the authenticated user has **exactly one Platform**, that Platform is
  automatically preselected.
- If the user has **more than one Platform**, the form does not guess: none are
  automatically selected unless existing form state requires otherwise.
- If the user has **zero Platforms**, `Owned` remains selected by default and the
  form shows clear guidance that an Owned game requires a Platform, with a link to
  the `/platforms` page. The form never automatically creates a Platform.

This convenience applies only to the initial state of a new form; edit mode loads
the persisted associations as-is, and the backend-authoritative Owned →
Wishlist/Interested preservation rule (see AcquisitionStatus Rules) governs
transitions regardless of the form's preselection.

#### Genre source

Genres come only from `GenresService.list()` (`GET /genres`). The approved catalog
is never duplicated in the frontend. The form renders the returned genres as a
multi-select.

#### `video-games-list`

- On init, calls `videoGamesService.list()`; exposes signals for `loading`,
  `videoGames`, and `error`. It also loads the Platforms and Genres (reusing
  `PlatformsService` and `GenresService`) so rows and the form can render names.
- Loading state: while the first request is in flight, shows a loading indicator.
- Error state: on a non-`401` failure, shows an error message and a "Try again"
  action that reloads the list.
- Empty state: when the list is empty and not loading, shows "No video games yet"
  plus a prominent create action.
- List state: renders the VideoGames in the API order; each row shows the name,
  AcquisitionStatus, GameStatus (when present), Rating (when present), the
  associated Platform names and Genre names (resolved from the loaded lists), and
  Edit/Delete actions.
- Create: shows the `video-game-form` in create mode; on submit calls the service;
  on success reloads the list; on validation/`400` error keeps the form open and
  shows the message.
- Edit: selecting Edit loads the VideoGame's data into the form in edit mode; on
  submit calls `PUT`; on success reloads the list; on `404` shows "This video game
  no longer exists." and reloads.
- Delete: confirms with the video game name (native `confirm()` is acceptable); on
  `DELETE` success reloads the list; on `404` shows "This video game no longer
  exists." and reloads.
- A `401` from any video-game API call clears the local session
  (`auth.clearLocalSession()`) and navigates to `/login` (session-expired behavior),
  exactly like the Platforms feature.

#### `video-game-form`

- One `FormGroup` with controls for: `name`, `coverImageUrl`, `acquisitionStatus`
  (default `Owned`), `platformIds` (checkbox set), `genreIds` (checkbox set),
  `gameStatus`, `progressPercentage`, `rating`, and `notes`.
- **Frontend validation mirrors backend normalization before length validation**
  (Decision 5). The form must not reject a value solely because its raw untrimmed
  representation exceeds the limit when the normalized value is valid:
  - `name`: 1) trim leading/trailing whitespace; 2) require non-empty; 3) validate
    the normalized length ≤ 100.
  - `notes`: 1) trim leading/trailing whitespace; 2) empty becomes `null`;
    3) validate the normalized non-null length ≤ 5000.
  - `coverImageUrl`: 1) trim; 2) empty becomes `null`; 3) apply the existing
    normalized length (≤ 2048) and `http(s)` validation.
  The backend remains authoritative; these mirror validations provide immediate UX
  feedback only.
- Dynamic domain behavior:
  - When `acquisitionStatus` is `Owned`, `gameStatus` and `progressPercentage`
    controls are shown and usable.
  - When `acquisitionStatus` is `Wishlist` or `Interested`, the `gameStatus` and
    `progressPercentage` controls are hidden/disabled and their form values are
    cleared so the submitted payload sends `null` (changing away from Owned clears
    status/progress in the outgoing request, matching the backend normalization).
  - When `acquisitionStatus` is `Owned`, submitting with zero selected Platforms is
    blocked with the message "An Owned game requires at least one platform." If the
    user has no Platforms at all and `Owned` is selected, the message includes the
    path to `/platforms`; it never auto-creates a Platform. Opening a new form with
    `Owned` preselected auto-selects the single available Platform when the user has
    exactly one (see Platform source); with more than one Platform none are
    preselected.
  - `rating` is an optional 1–5 selector; `progressPercentage` a number input
    validated to 0–100; `coverImageUrl` gets a light client-side
    `http(s)`/normalized-length check; `notes` a textarea with normalized-length
    validation.
  - Genres multi-select comes from `GenresService`; Platform multi-select from
    `PlatformsService`.
- **Transition UX consequence (Owned → Wishlist/Interested).** Because the backend
  preserves the persisted Rating and Notes during an `Owned` → `Wishlist`/`Interested`
  transition, a user cannot change Rating or Notes in the same save that changes
  AcquisitionStatus from Owned to Wishlist/Interested. If a user changes
  `Owned → Wishlist` and simultaneously edits Rating or Notes, the backend
  preservation rule wins and the previously persisted Rating/Notes remain. The UI
  does NOT need a complex warning/modal for the MVP; document that a subsequent
  normal edit (after the status transition) can change Rating or Notes. No extra
  workflow complexity is added.
- Emits submit events with the complete `VideoGameInput`; the parent performs the
  HTTP call so the form stays reusable for create and edit.
- Displays inline error text for client-side validation and for server `400`
  messages supplied by the parent (e.g., "One or more selected platforms are no
  longer available." after reloading Platforms).

### Navigation

- Add a route `{ path: 'video-games', component: VideoGamesList, canActivate:
  [authGuard] }` to `app.routes.ts`.
- Add a minimal "Video games" `routerLink` on the authenticated home view
  (`features/auth/home/home.html`) next to the existing "Platforms" link.
- Add a minimal "Back to home" `routerLink` on the video-games page.
- Do not build a full application shell or shared navigation component.

### Validation and Errors

Predictable behavior, consistent problem-details responses. Expected failures are
raised as typed exceptions in `GameLibrary.Core` (`VideoGameExceptions.cs`:
`InvalidVideoGameException(string message)`, `VideoGameNotFoundException`) and mapped
by the controller, mirroring the Platform pattern.

| Condition | HTTP | Problem details |
| --- | --- | --- |
| Unauthenticated request | `401` | Produced by the JWT bearer handler (with `WWW-Authenticate` challenge). |
| Authenticated principal without a usable `sub` | `401` | Controller returns `Unauthorized()` (mirrors the existing controllers). |
| Name missing / empty / whitespace-only after trim | `400` | Title "Invalid video game", detail "Video game name is required." |
| Name longer than 100 characters | `400` | Title "Invalid video game", detail "Video game name must be at most 100 characters." |
| AcquisitionStatus missing on update | `400` | Title "Invalid video game", detail "Acquisition status is required." |
| AcquisitionStatus invalid value | `400` | Title "Invalid video game", detail "Acquisition status is invalid." |
| GameStatus invalid value | `400` | Title "Invalid video game", detail "Game status is invalid." |
| Rating not in 1–5 | `400` | Title "Invalid video game", detail "Rating must be between 1 and 5." |
| ProgressPercentage not in 0–100 | `400` | Title "Invalid video game", detail "Progress must be between 0 and 100." |
| Resulting state Owned with zero Platforms (create, or update to Owned, or removing the last Platform) | `400` | Title "Invalid video game", detail "An Owned video game requires at least one platform." |
| Platform id unknown or belonging to another user | `400` | Title "Invalid video game", detail "One or more platforms are not available in your library." (identical for both cases; no cross-user leakage). |
| Genre id unknown | `400` | Title "Invalid video game", detail "One or more genres are invalid." |
| CoverImageUrl too long or not `http(s)` | `400` | Title "Invalid video game", detail "Cover image URL is invalid." |
| Notes longer than 5000 characters | `400` | Title "Invalid video game", detail "Notes must be at most 5000 characters." |
| VideoGame does not exist in the caller's Library (update/delete of own or another user's) | `404` | Title "Video game not found". Cross-user identifiers are indistinguishable from nonexistent ones. |
| Platform delete blocked because it is in use | `409` | Title "Platform in use", detail "This platform is in use and cannot be deleted." (now reachable via the `game_platforms` FK). |
| Unexpected exception | `500` | Existing centralized handler (no stack traces, includes `requestId`). |

- The `400` rows for `platformIds`, `rating`, and `notes` above apply to create and
  to normal (non-transition) updates only. During an `Owned` → `Wishlist`/`Interested`
  transition those request fields are ignored (neither validated nor applied), so an
  invalid/foreign/nonexistent Platform id, an out-of-range rating, and over-length
  notes in a transition request do not produce a `400` and do not change the
  persisted values (see AcquisitionStatus Rules).

- The frontend maps these to user-visible messages (not found, required, too long,
  Owned-needs-platform, invalid references, and a generic network/server failure
  message). It never displays raw problem-details internals or stack traces.

### Testing

Proportionate tests. No new test framework (xUnit for backend, Vitest via `ng test`
for frontend).

#### Backend unit tests — `GameLibrary.Core.Tests` (introduced in this feature)

Feature 003 deferred this project "until a feature introduces meaningful pure
domain/application logic that can be tested without a database (for example
AcquisitionStatus transition rules in the VideoGame feature...)". That trigger is
now met: `VideoGameRules` is pure and framework-independent, so it is genuinely
unit-testable without EF or a database.

- New verification project `tests/backend/GameLibrary.Core.Tests/` (xUnit),
  referencing `GameLibrary.Core`. It is a verification project, not one of the two
  application projects; add it to `GameLibrary.sln` under `tests/` alongside the
  integration project.
- **Do not create unit tests that only mirror EF queries.** The rules tested here
  must be pure `VideoGameRules` behavior.
- Required coverage for `VideoGameRules`:
  - Name: `null`/empty/whitespace → error; trims leading/trailing whitespace; 100
    characters accepted; 101 rejected; no duplicate rule exists (names allowed to
    repeat).
  - Rating: `null` ok; 1–5 ok; 0, 6, negative → error.
  - Progress: `null` ok; 0 and 100 ok; −1 and 101 → error.
  - Notes: ≤ 5000 ok; > 5000 → error; whitespace-only → `null`.
  - CoverImageUrl: `null`/empty → `null`; `http(s)` ok; other scheme and > 2048 →
    error.
  - Acquisition: create with `null` → `Owned`; update with `null` → error; valid
    values parse; unknown → error.
  - GameStatus: `null` → `null`; valid values parse; unknown → error.
  - State rules:
    - Owned with 0 Platforms → error (create and update paths are the same rule).
    - Owned with ≥ 1 Platform → ok.
    - Wishlist/Interested with 0 Platforms → ok.
    - Wishlist/Interested with GameStatus/Progress → normalized to `null`
      (the Owned → Wishlist/Interested clears rule, directly tested).
    - Owned with GameStatus/Progress → values preserved.
    - The Wishlist/Interested → Owned requirement is the same Owned-with-platforms
      rule, tested against the "transition to Owned" shape.
  - Transition predicate (`IsOwnedToNonOwnedTransition`):
    - `Owned → Owned` → `false`; `Owned → Wishlist` → `true`; `Owned → Interested`
      → `true`; `Wishlist → Owned` → `false`; `Wishlist → Wishlist` → `false`;
      `Wishlist → Interested` → `false`; `Interested → Owned` → `false`.

#### Backend integration tests — existing `GameLibrary.IntegrationTests` project

Add a `VideoGameTestFactory : WebApplicationFactory<Program>` mirroring
`PlatformTestFactory` exactly: set `ConnectionStrings:Default` to the
`ConnectionStrings__Test` environment value and post-configure the bearer
`JwtBearerOptions` with the deterministic symmetric test key; mint tokens via
`TestTokens.CreateToken(sub)` for distinct test users. Apply migrations idempotently
before each test (the existing `[Collection("Database")]` + `IAsyncLifetime`
`MigrateAsync` pattern).

Required cases (all against real PostgreSQL):

- **Migration/schema:** update `DatabaseMigrationTests` so that after `MigrateAsync`
  the `public` schema contains exactly `__EFMigrationsHistory`, `libraries`,
  `platforms`, `games`, `library_entries`, `genres`, `game_genres`, and
  `game_platforms`, compared as an order-independent set (this test currently
  asserts only `__EFMigrationsHistory`, `libraries`, `platforms` and MUST be
  updated). Optionally assert the unique `game_id` index, the `genres` seed row
  count (15), and the `game_platforms.platform_id` FK exists with `ON DELETE
  RESTRICT`.
- **Create:** `POST /video-games` with a name like `"  Elden Ring  "`,
  `acquisitionStatus` `Owned`, and one owned Platform returns `201` with the
  trimmed name and no `Location` header; the returned and listed `id` is the Game
  id; `GET /video-games` contains it.
- **Create without Library:** a user's first create produces exactly one
  `libraries` row for that user (direct `GameLibraryDbContext` query on the test
  connection), and a second create reuses it (still one row).
- **List:** returns only the calling user's VideoGames, empty for a user with no
  Library (no row created on read).
- **Case-insensitive deterministic ordering:** create VideoGames with names chosen
  so the case-insensitive order differs from the case-sensitive order and/or from
  creation order (for example `"b"`, `"B"`, `"A"`, `"a"`), plus ties on the
  original name; assert `GET /video-games` returns them in the deterministic order
  (case-insensitive Name ascending, original Name ascending, `CreatedAt` ascending,
  `Id` ascending). Repeat against fresh rows to confirm the tiebreakers (`Id`
  ascending) make the order deterministic.
- **Two-user isolation:** user A and user B each create VideoGames (including one
  with the same name in both Libraries — names are not unique); A's list contains
  only A's games and B's only B's; `PUT`/`DELETE` of A's Game id while
  authenticating as B returns `404`.
- **Platform ownership validation:** creating/updating with a Platform id from
  another user's Library returns `400`; creating/updating with a nonexistent
  Platform id returns the same `400` (identical, non-leaking error).
- **Genre persistence:** `GET /genres` returns exactly the 15 catalog genres
  (ordered by name); the persisted `genres` seed names match `GenresCatalog`
  **order-independently** (compare as sets, not by position); create with genre ids
  persists and the response/list include them; an unknown genre id → `400`.
- **Acquisition transitions:**
  - Create `Owned` with no Platforms → `400`.
  - Create `Wishlist` with no Platforms → `201`; with `gameStatus`/`progress` in the
    request → `201` with both stored as `null` (cleared).
  - Update `Wishlist` → `Owned` without adding a Platform → `400`; with a Platform →
    `200`.
  - Update `Owned` removing its only Platform while remaining `Owned` → `400`.
  - Update `Owned` removing one of two Platforms → `200`.
  - **Owned → Wishlist backend-authoritative preservation** (the client cannot
    accidentally erase or corrupt preserved fields):
    1. Preserve Platforms even when the request sends an empty `platformIds` array:
       create an Owned game with a Platform, `PUT` it to `Wishlist` with
       `platformIds: []` → `200`, and assert the response (and a direct query of
       `game_platforms`) still contains the persisted Platform.
    2. Preserve Rating even when the request sends `rating: null`: create an Owned
       game with `rating: 4`, `PUT` it to `Wishlist` with `rating: null` → `200`
       and assert the response still has `rating: 4`.
    3. Preserve Rating even when the request sends an invalid `rating: 6`: `PUT`
       the Owned game (persisted rating `4`) to `Wishlist` with `rating: 6` → `200`
       and assert the response still has `rating: 4` (invalid rating ignored, not
       validated).
    4. Preserve Notes even when the request sends `notes: null`: create an Owned
       game with notes, `PUT` it to `Wishlist` with `notes: null` → `200` and
       assert the response still has the notes.
    5. Preserve Notes even when the request sends Notes longer than 5000 characters:
       `PUT` the Owned game to `Wishlist` with an over-length `notes` value → `200`
       and assert the response still has the persisted Notes (over-length ignored,
       not validated).
    6. Preserve Platforms even when the request sends another user's Platform ID:
       `PUT` the Owned game to `Wishlist` with a `platformIds` entry belonging to a
       different user → `200`, and assert the response (and `game_platforms`)
       contains only the persisted owned Platform association. The foreign ID is
       neither applied nor validated nor queried (no existence leak).
    7. Preserve Platforms even when the request sends a nonexistent Platform ID:
       `PUT` the Owned game to `Wishlist` with a random `platformIds` entry → `200`
       and assert the persisted Platforms are unchanged.
    8. Clear GameStatus: `PUT` an Owned game carrying `gameStatus` to `Wishlist`
       → `200` with `gameStatus` stored `null`.
    9. Clear ProgressPercentage: `PUT` an Owned game carrying `progressPercentage`
       to `Wishlist` → `200` with `progressPercentage` stored `null`.
    10. The same ignored-field preservation behavior for `Owned → Interested`:
       at least the empty-`platformIds`, `rating: 6`, and another user's Platform
       ID cases (representative coverage).
    11. Normal PUT outside those transitions still performs full replacement: for
       example an `Owned → Owned` PUT with `platformIds: []` → `400` (the platform
       set IS replaced by the submitted set), and an `Owned → Owned` PUT with
       `rating: null` and `notes: null` clears them. This proves the special
       preservation rule applies ONLY to the Owned → non-Owned transitions and that
       normal updates remain full replacement.
    12. The SAME invalid values that transition PUT ignores (rating `6`, over-length
       Notes, another user's Platform ID, nonexistent Platform ID) on a NORMAL
       full-replacement update (e.g., `Owned → Owned` or a plain metadata edit)
       return the existing `400` responses. This pins the asymmetry: ignored on
       transitions, validated on normal updates.
  - Update `Owned` → `Wishlist` sends `gameStatus`/`progress` → `200` with both
    `null` and the Platforms/Rating/Notes preserved (assert the response).
- **Validation:** empty / whitespace-only / missing `name` → `400`; 101-character
  name → `400`; `rating` 0 and 6 → `400`; `progressPercentage` −1 and 101 → `400`;
  update with `acquisitionStatus` `null` → `400`.
- **Update:** `PUT /video-games/{id}` renames and updates metadata; `GET
  /video-games` reflects it; `PUT` of an id that does not exist or is another
  user's → `404`.
- **Delete:** `DELETE /video-games/{id}` returns `204`; the Game no longer appears
  in the list; repeating the delete → `404`; direct queries show the `games` row,
  its `library_entries` row, and its `game_genres`/`game_platforms` rows are all
  gone; no FK error surfaces during the deletion (the dependent LibraryEntry is
  removed before/with the principal Game per the required-relationship ordering).
- **Game/LibraryEntry lifecycle — created together, removed together, no orphans:**
  after `POST /video-games`, direct queries show both the `games` row and its
  `library_entries` row exist (entry `game_id` references the created Game); after
  `DELETE /video-games/{id}`, both rows are gone (asserted above); a representative
  normal application sequence (create → update → list → delete) never leaves an
  orphan `games` row without its `library_entries` row.
- **Platform delete protection (now reachable):** create a Platform and an Owned
  VideoGame using it; `DELETE /platforms/{id}` returns `409`; after deleting the
  VideoGame, `DELETE /platforms/{id}` returns `204`. Existing `PlatformEndpointTests`
  must continue to pass unchanged.
- **Platform delete protection is independent of AcquisitionStatus:** create a
  Platform, create a VideoGame initially Owned using it, then transition the
  VideoGame to `Wishlist` while preserving its Platform; `DELETE /platforms/{id}`
  still returns `409 Platform in use` (the `game_platforms.platform_id` FK does not
  consider AcquisitionStatus); after deleting the VideoGame, `DELETE
  /platforms/{id}` returns `204`.
- **GameType guard (non-VideoGame Game rows):** using the test DbContext/SQL, create
  a `games` row with `game_type = 'BoardGame'` and its LibraryEntry belonging to the
  test user (verification infrastructure only — NO BoardGame application
  functionality is introduced). Assert:
  1. the row does NOT appear in `GET /video-games`;
  2. `PUT /video-games/{nonVideoGameGameId}` returns `404`;
  3. `DELETE /video-games/{nonVideoGameGameId}` returns `404`.
- **Non-Owned status/progress CHECK backstop (direct SQL):** using the test
  DbContext/SQL, attempt to persist an invalid state such as `AcquisitionStatus =
  Wishlist` with `GameStatus = Playing`, or `AcquisitionStatus = Interested` with
  `ProgressPercentage = 50`, and assert the database CHECK constraint rejects the
  write. Do NOT test this through the API: the service correctly normalizes the
  state before persistence, so the API would mask the backstop.

Existing tests (`HealthEndpointTests`, `AuthEndpointTests`,
`JwtValidationConfigurationTests`, `PlatformEndpointTests`) must continue to pass.

#### Frontend tests

Vitest via `ng test`. Mock `HttpClient` (`provideHttpClientTesting`) and use the
existing `SUPABASE_CLIENT` mock where needed. Meaningful cases at minimum:

- `video-games.service`: `list`, `create`, `update`, `delete` issue the correct
  method/URL and map responses.
- `genres.service`: `list` issues `GET /genres` and maps the response.
- `video-games-list`:
  - Loading state while the list request is pending.
  - Empty state ("No video games yet").
  - List rendering of returned VideoGames (name, status, platform/genre names).
  - Create success reloads the list and closes the form.
  - Create failure shows the server message and keeps the form usable.
  - Edit success reloads/updates the list; `404` on edit shows the "no longer
    exists" message.
  - Delete confirmation; delete success reloads the list; `404` on delete shows the
    "no longer exists" message.
  - Non-`401` API failure shows the error state with a retry action.
  - `401` clears the local session and navigates to `/login`.
- `video-game-form`:
  - Name required, trim-on-submit, whitespace-only rejection, and normalized-length
    validation: a name whose raw untrimmed length exceeds 100 but whose trimmed
    value is ≤ 100 is accepted (normalize-before-length).
  - Notes and CoverImageUrl normalize-before-length: a value whose raw untrimmed
    length exceeds the limit but whose trimmed value is within it is accepted; empty
    after trim becomes `null`.
  - `Owned` requires at least one selected Platform (submit blocked with the
    message); `Wishlist`/`Interested` submits with zero Platforms.
  - Switching to `Wishlist`/`Interested` clears `gameStatus`/`progressPercentage`
    from the outgoing payload and hides those controls; switching back to `Owned`
    re-enables them.
  - Rating and progress range validation.
  - Platform options come from `PlatformsService.list()`; Genre options come from
    `GenresService.list()`; the "no platforms yet" guidance appears for `Owned` when
    the user has no Platforms.
  - Quick-add auto-selection: opening a new form with `Owned` when the user has
    exactly one Platform preselects it; when the user has more than one Platform,
    none are preselected; when the user has zero Platforms, `Owned` remains the
    default and the `/platforms` guidance/link is shown (no auto-created Platform).

Do not over-test trivial markup.

**Manual acceptance verification** — sign in, manage Platforms as needed, and use
the UI to create/edit/delete VideoGames with all status transitions; confirm list,
error, and delete-protection behavior. See Verification.

## Functional Requirements

`FR-001` — Authenticated list: `GET /video-games` is `[Authorize]` and returns the
calling user's VideoGames (filtered to `Game.GameType == GameType.VideoGame`) as a
JSON array of `VideoGameResponse` in a deterministic, case-insensitive order
(case-insensitive Name ascending, original Name ascending, `createdAt` ascending,
`id` ascending). Unauthenticated → `401`. A user with no Library gets an empty array
(no row created on read). Non-VideoGame Game rows never appear.

`FR-002` — Create: `POST /video-games` creates a VideoGame in the calling user's
Library and returns `201` + the created `VideoGameResponse` (no `Location` header).
The first create for a user lazily creates exactly one `libraries` row for that
user, reused by subsequent creates. The response `id` is the Game id.

`FR-003` — Name rules: names are required (non-empty after trim) and at most 100
characters (trimmed); leading/trailing whitespace is trimmed before storage. Game
names are **not** unique: two VideoGames with the same name in one Library are
allowed, and no duplicate detection exists.

`FR-004` — AcquisitionStatus: values `Owned`/`Wishlist`/`Interested`; create
defaults to `Owned` when omitted; update requires a value. Owned requires at least
one Platform (create, transition to Owned, and removing the final Platform while
remaining Owned are all rejected with `400` when zero Platforms result).
Wishlist/Interested may have zero or more Platforms. An `Owned` → `Wishlist`/
`Interested` transition is the backend-authoritative special rule (see `FR-027`).

`FR-005` — GameStatus: values `Backlog`/`Playing`/`Completed`/`Abandoned`/
`WantToPlay`; optional; valid only for Owned entries. No `IsCompleted` field. Any
non-Owned resulting state stores `null` (cleared server-side).

`FR-006` — ProgressPercentage: optional integer 0–100; valid only for Owned entries;
cleared to `null` for non-Owned resulting states. GameStatus and ProgressPercentage
never change each other automatically.

`FR-007` — Rating: optional integer 1–5; outside the range → `400`.

`FR-008` — Notes: optional personal text, at most 5000 characters, no rich text;
whitespace-only normalized to `null`.

`FR-009` — CoverImageUrl: optional manual external URL at most 2048 characters,
`http(s)` scheme when provided, whitespace-trimmed, empty → `null`. No upload,
storage, resizing, or CDN.

`FR-010` — Genres: the seeded catalog of exactly the 15 approved values; a VideoGame
may have zero or more; `GET /genres` (read-only, `[Authorize]`) returns
`GenreResponse[]` ordered by name; unknown genre ids → `400`; no genre-management
endpoints. `GenresCatalog` in `GameLibrary.Core` is the authoritative code
definition of the catalog; the EF seed derives from it and the migration contains
the persisted seed (see `FR-017` and Risks / Notes).

`FR-011` — Platform associations: many-to-many between VideoGame LibraryEntries and
Platforms via `game_platforms`; requested Platform ids must belong to the caller's
Library (unknown and other-user ids return the identical `400`) on create and on
normal (non-transition) updates; Owned requires ≥ 1 Platform; Wishlist/Interested
allow any count. During an `Owned` → `Wishlist`/`Interested` transition the request's
`platformIds` are ignored (neither applied, validated, nor queried) and the persisted
associations are preserved (see `FR-027`).

`FR-012` — Update: `PUT /video-games/{id}` is full-replacement for all updates other
than an `Owned` → `Wishlist`/`Interested` transition, returns `200` with the updated
`VideoGameResponse`, applies the same validation/state rules, and returns `404` for
ids that do not exist in the caller's Library (including another user's and any Game
whose `GameType` is not `VideoGame`). For the `Owned` → `Wishlist`/`Interested`
exception, see `FR-027`. `PUT` is the only update verb; no `PATCH`, no optimistic
concurrency/`RowVersion`/ETags/locks, and no single-resource
`GET /video-games/{id}` are added (last-write-wins and full-replacement `PUT` are
deliberate MVP tradeoffs).

`FR-013` — Delete: `DELETE /video-games/{id}` returns `204`, deletes the
LibraryEntry (dependent) and its Game (principal) together as one transaction in the
correct FK order, removes Platform and Genre associations, leaves no orphan Game or
orphan LibraryEntry, and returns `404` for cross-user/nonexistent/non-VideoGame ids.

`FR-014` — Platform delete protection: `DELETE /platforms/{id}` returns `409`
"Platform in use" when the Platform is referenced by any `game_platforms` row
(enforced by the `ON DELETE RESTRICT` FK), regardless of the referencing entry's
AcquisitionStatus (Owned, Wishlist, or Interested), and `204` otherwise; the
mapping never leaks database internals.

`FR-015` — User isolation: every VideoGame operation is scoped to the authenticated
user's Library derived from the validated JWT `sub`; the API accepts no user/owner/
library ID from requests; another user's Game ids behave as `404` and another
user's Platform ids behave as an identical `400`.

`FR-016` — Migration and schema: the `AddVideoGameManagement` EF migration creates
`games`, `library_entries`, `genres` (seeded), `game_genres`, and `game_platforms`
with the specified columns, indexes, FKs (including the two `RESTRICT` FKs for
Platform delete protection and Library ownership), CHECK constraints (including the
`game_type IN ('VideoGame', 'BoardGame')` value deliberately permitting the future
BoardGame discriminator), the at-most-one `library_entries.game_id` index, and the
required Game ↔ LibraryEntry relationship configured on the LibraryEntry (dependent)
side; it is committed to source control. Feature 004 creates only `VideoGame` Games.

`FR-017` — Core authority: `VideoGameRules` and `VideoGameService` in
`GameLibrary.Core` implement all validation, normalization, ownership scoping,
platform/genre reference resolution, and create/update/delete; `GameLibrary.Api`
contains only HTTP concerns and contracts. The ensure-library helper is shared via
`LibraryService`.

`FR-018` — Frontend list page: a protected `/video-games` route renders loading,
error (with retry), empty ("No video games yet"), and list states.

`FR-019` — Frontend create/edit form: covers Name, AcquisitionStatus, Platforms,
Genres, GameStatus, ProgressPercentage, Rating, Notes, and CoverImageUrl; Platforms
come from the existing Platform API, Genres from `GET /genres`; dynamic rules mirror
the domain (Owned requires Platform; non-Owned hides/clears GameStatus and Progress;
no auto platform creation). Frontend validation normalizes before length validation
(`FR-028`), and the quick-add single-Platform auto-selection applies on new forms
(`FR-029`).

`FR-020` — Frontend delete flow: confirms, submits `DELETE`, reloads the list on
success, and surfaces not-found/API errors.

`FR-021` — Frontend session expiry: a `401` from any video-game API call clears the
local session and navigates to `/login`.

`FR-022` — Navigation: a "Video games" link on the home view and a "Home" link on
the video-games page connect the two authenticated views.

`FR-023` — Backend unit tests: the `GameLibrary.Core.Tests` project tests
`VideoGameRules` (name/rating/progress/notes/cover validation, acquisition default
and parsing, game-status parsing, Owned-requires-Platform, the non-Owned clears
rule, and the `IsOwnedToNonOwnedTransition` predicate) and passes.

`FR-024` — Backend integration tests: the required real-PostgreSQL cases
(migration/schema, create, library lifecycle, list, two-user isolation, platform
ownership validation, genre persistence, acquisition transitions including the
Owned → Wishlist/Interested backend-authoritative preservation tests, validation,
case-insensitive deterministic ordering, update, delete, and the now-reachable
platform delete-protection `409`) are implemented and pass.

`FR-025` — Frontend tests: the required `video-games.service`, `genres.service`,
`video-games-list`, and `video-game-form` behaviors are tested and pass.

`FR-026` — Foundation preserved: `GET /health` stays anonymous, `GET /auth/me`
behavior is unchanged, Platform CRUD (including the new `409`) works, the backend
has exactly two application projects, and existing backend integration tests still
pass.

`FR-027` — Backend-authoritative Owned → Wishlist/Interested preservation: during an
`Owned` → `Wishlist` or `Owned` → `Interested` transition the backend preserves the
currently persisted Platform associations, Rating, and Notes regardless of the
request's `platformIds`/`rating`/`notes`. These request fields are ignored — neither
applied nor validated — so an empty `platformIds` array, `null`/out-of-range Rating
(e.g., `rating: 6`), over-length Notes, and foreign/nonexistent Platform ids cannot
erase or corrupt them, and the ignored Platform ids are not queried (no existence
leak). The transition forces `gameStatus`/`progressPercentage` to `null`, and applies
`name`/`coverImageUrl`/`genreIds`/target `acquisitionStatus` from the request
normally. This rule is authoritative on the backend and does not depend on Angular
resending the previous values; it takes precedence over normal PUT replacement
semantics for the preserved fields. A normal (non-transition) update rejects the same
invalid values with `400`.

`FR-028` — Frontend normalize-before-length validation: the form trims
`name`/`notes`/`coverImageUrl` before length validation and treats empty-after-trim
`notes`/`coverImageUrl` as `null`, mirroring backend normalization. A value is not
rejected solely because its raw untrimmed representation exceeds the limit when the
normalized value is valid.

`FR-029` — Quick-add single-Platform auto-selection: opening a new VideoGame form
with `AcquisitionStatus = Owned` auto-selects the single available Platform when the
user has exactly one Platform; with more than one Platform, none are auto-selected;
with zero Platforms, `Owned` remains the default and guidance with a `/platforms`
link is shown (no Platform is auto-created). This is a frontend convenience only;
backend validation remains authoritative.

`FR-030` — GameType guards: every VideoGame backend operation requires
`Game.GameType == GameType.VideoGame`. `ListAsync` filters by GameType in addition to
Library ownership, so non-VideoGame Games never appear in `GET /video-games`; a Game
that exists and belongs to the user but has another GameType behaves exactly like a
nonexistent VideoGame (`404 Video game not found`) for `PUT`/`DELETE`. This prevents
future cross-type operations (e.g., `PUT /video-games/{boardGameId}`) and is covered
by integration tests that create a non-VideoGame Game row directly (verification
infrastructure only; no BoardGame application functionality).

`FR-031` — Game/LibraryEntry lifecycle: application-created Games always have exactly
one LibraryEntry and orphan Games must not remain. Creation creates the Game and its
LibraryEntry together in one transaction; deletion removes the LibraryEntry
(dependent) and Game (principal) together in one transaction without an FK error;
normal application operations never leave an orphan `games` row. The database
backstop is the at-most-one `library_entries.game_id` index and the required
relationship; no triggers, deferred constraints, circular FKs, or database procedures
enforce Game → LibraryEntry existence.

`FR-032` — Database CHECK backstop for non-Owned status/progress: the
`library_entries` CHECK `acquisition_status = 'Owned' OR (game_status IS NULL AND
progress_percentage IS NULL)` rejects any persisted row placing GameStatus/
ProgressPercentage on a non-Owned entry, independent of service normalization.
Relational backstop tests attempt the invalid persisted state directly (SQL/test
DbContext) and assert the CHECK rejects it; the API is not used because the service
normalizes first.

`FR-033` — Platform delete protection is independent of AcquisitionStatus: a Platform
referenced by a Wishlist/Interested VideoGame LibraryEntry is as protected as one
referenced by an Owned entry (`DELETE /platforms/{id}` → `409`), because the
`game_platforms.platform_id` FK does not consider AcquisitionStatus. After the
referencing VideoGame is deleted, the Platform is deletable (`204`).

`FR-034` — Transition UX consequence (frontend): because the backend preserves
persisted Rating and Notes on `Owned` → `Wishlist`/`Interested`, a user cannot change
Rating or Notes in the same save that changes AcquisitionStatus away from Owned; if
they do, the backend preservation rule wins and the previously persisted Rating/Notes
remain. No warning/modal is required for the MVP; a subsequent normal edit can change
Rating/Notes after the transition.

`FR-035` — Transition-vs-normal validation asymmetry: the invalid values that a
transition PUT ignores (out-of-range `rating`, over-length `notes`, foreign/
nonexistent `platformIds`) are rejected with the existing `400` responses on a normal
full-replacement update. Integration tests pin both sides of this asymmetry.

## Non-Functional Requirements

`NFR-001` — User isolation: every user-owned query and mutation is scoped to the
authenticated user's Library derived from the validated Supabase `sub`; no
client-supplied user/owner/library ID is trusted; cross-user Game ids return `404`
and cross-user Platform ids return an identical `400` to prevent resource
enumeration and Platform-existence leakage; one user can never read or modify
another user's VideoGames, LibraryEntries, or Platforms.

`NFR-002` — Integrity: database FKs and CHECK constraints act as integrity
backstops (the at-most-one `library_entries.game_id` unique index, the required
Game ↔ LibraryEntry relationship on the dependent side, the
`game_platforms.platform_id → platforms.id` RESTRICT FK, ownership RESTRICT FKs,
cascade FKs for associations, and the rating/progress/status CHECK constraints —
including the non-Owned status/progress CHECK that rejects invalid persisted states
independently of service normalization); transitional/cross-record business rules
live in `VideoGameRules`/`VideoGameService`; no triggers, deferred constraints,
circular FKs, or database procedures enforce Game → LibraryEntry existence; no locks,
optimistic-concurrency tokens, or concurrency frameworks are introduced.

`NFR-003` — Simplicity: the feature adds five tables, six domain entities, three
enums, one pure rules module, two application services (plus the shared
`LibraryService`), two controllers, and one frontend feature folder. No generic
repositories, Unit of Work, MediatR, CQRS, event sourcing, domain events, extra
backend application projects, NgRx, or shared-kernel abstractions are introduced.
EF Core is used directly.

`NFR-004` — Maintainability: domain/application rules live in `GameLibrary.Core`
(with pure rules isolated in `VideoGameRules` for unit testing); HTTP/contract
concerns live in `GameLibrary.Api`; frontend feature code is local to
`features/games/`; no speculative shared abstractions are created before multiple
real usages justify them.

`NFR-005` — Validation consistency: backend validation in `GameLibrary.Core` is
authoritative; the frontend mirrors rules only for immediate UX feedback and never
overrides the backend; every create/update ends in a valid state per the approved
Domain invariants.

`NFR-006` — Security: the validated JWT `sub` is the only trusted identity;
problem-details responses never expose stack traces, constraint names, SQL, or
other database internals; committed configuration remains non-secret; no RLS is
introduced; Angular never accesses application database tables; no new secrets are
committed; Platform existence is never leaked across users — including during an
`Owned` → `Wishlist`/`Interested` transition, where ignored `platformIds` are never
queried or resolved.

## Acceptance Criteria

`AC-001` — Unauthenticated requests to any of `GET/POST/PUT/DELETE /video-games`
and `GET /genres` return `401`.

`AC-002` — An authenticated user creates a VideoGame with `name: "  Elden Ring  "`,
`acquisitionStatus: "Owned"`, and one owned Platform; `POST /video-games` returns
`201` (no `Location` header) with the trimmed name `"Elden Ring"`; `GET
/video-games` lists exactly it.

`AC-003` — `POST` with `acquisitionStatus: "Owned"` and no Platforms returns `400`;
`POST` with `acquisitionStatus: "Wishlist"` and no Platforms returns `201`.

`AC-004` — `POST`/`PUT` with an empty, whitespace-only, or missing `name` returns
`400`; a 101-character name returns `400`; `rating` 0 or 6 returns `400`;
`progressPercentage` −1 or 101 returns `400`; `PUT` with `acquisitionStatus` `null`
returns `400`.

`AC-005` — Acquisition transitions: updating `Wishlist` → `Owned` without adding a
Platform returns `400`; updating `Owned` → `Wishlist` (with `gameStatus`/`progress`
in the request) returns `200` with both `null` and Platforms/Rating/Notes preserved;
updating `Owned` → `Wishlist` with an **empty `platformIds` array and `null`
`rating`/`notes`** still returns `200` and preserves the persisted Platforms,
Rating, and Notes (backend-authoritative; the client cannot erase them); the same
preservation/clearing holds for `Owned` → `Interested`; the ignored-field semantics
are pinned by tests: an `Owned` → `Wishlist`/`Interested` PUT that sends `rating: 6`,
Notes longer than 5000 characters, another user's Platform ID, or a nonexistent
Platform ID returns `200` and preserves the persisted Rating, Notes, and Platform
associations; the SAME invalid values on a normal full-replacement update return the
existing `400`; updating an `Owned` game to remove its only Platform while remaining
`Owned` returns `400` (normal PUT remains full replacement outside the Owned →
non-Owned transitions).

`AC-006` — Two authenticated users A and B: A creates VideoGames and B creates
VideoGames (including one with the same name as A's); `GET /video-games` returns for
A only A's games and for B only B's; `PUT` and `DELETE` of A's Game id while
authenticated as B return `404`; using a Platform id belonging to B in A's
create/update returns `400`.

`AC-007` — `GET /genres` returns exactly the 15 approved catalog genres, matching
`GenresCatalog` (the authoritative code definition; the EF seed derives from it and
the migration persists it) **compared order-independently** against the catalog; a
VideoGame created with Genre ids returns and lists them; an unknown Genre id returns
`400`; no genre-management endpoints exist.

`AC-008` — `DELETE /video-games/{id}` returns `204`; the VideoGame no longer appears
in `GET /video-games`; repeating the delete returns `404`; a direct database query
shows the `games` row, its `library_entries` row, and its `game_genres`/`game_platforms`
rows are all gone, and no FK error surfaces during the deletion (dependent removed
before/with the principal).

`AC-009` — A Platform referenced by a VideoGame cannot be deleted:
`DELETE /platforms/{id}` returns `409` ("Platform in use") whether the referencing
entry is Owned or was later transitioned to Wishlist/Interested (FK protection is
independent of AcquisitionStatus); after deleting the VideoGame, the same
`DELETE /platforms/{id}` returns `204`.

`AC-010` — The `AddVideoGameManagement` migration applies to real PostgreSQL and
creates `games`, `library_entries`, `genres` (seeded with the 15 catalog values),
`game_genres`, and `game_platforms` with the specified columns, indexes, FKs, and
CHECK constraints; the updated `DatabaseMigrationTests` passes and asserts the new
table set.

`AC-011` — A user's first VideoGame create results in exactly one `libraries` row
for that user; a second create keeps it at exactly one (verified by direct database
query).

`AC-012` — `PUT /video-games/{id}` updates the name and metadata and returns `200`
with the updated `VideoGameResponse`; `GET /video-games` reflects it; `PUT`/`DELETE`
of a nonexistent or another user's id returns `404`.

`AC-013` — Backend build (`dotnet build src/backend/GameLibrary.sln`), backend
integration tests (`dotnet test tests/backend/GameLibrary.IntegrationTests`),
backend unit tests (`dotnet test tests/backend/GameLibrary.Core.Tests`), frontend
tests (`ng test --watch=false`), and lint (`ng lint`) all pass with
`ConnectionStrings:Test` configured to real PostgreSQL.

`AC-014` — The Angular `/video-games` route is protected by `authGuard`: signed out
it redirects to `/login`; signed in it loads and displays the VideoGames with
loading, empty, and list states; creating/editing through the UI updates the list
and shows user-visible validation errors; deleting confirms and removes from the
list; a `401` clears the session and navigates to `/login`.

`AC-015` — The form mirrors the domain rules: `Owned` blocks submit without at least
one selected Platform (and shows the no-platforms guidance/link when the user has
none; the single available Platform is auto-selected when the user has exactly one
and `Owned` is selected); `Wishlist`/`Interested` hides `gameStatus`/
`progressPercentage`, clears them from the outgoing payload, and submits without
Platforms; rating/progress/name validation matches the backend and normalizes before
length validation (`name`/`notes`/`coverImageUrl` trim first, empty-after-trim
`notes`/`coverImageUrl` become `null`).

`AC-016` — No out-of-scope artifacts are introduced: no BoardGame entity/table/
columns/endpoints/UI; no `game_type` value other than `VideoGame` is creatable; no
Library search/filter/sort or Random Picker; no RLS; no direct Angular database
access; no genre-management endpoints; no `PATCH`, no single-resource
`GET /video-games/{id}`, and no optimistic-concurrency/`RowVersion`/ETags/locks;
no new NuGet/npm dependencies; no new backend application projects (only the
`GameLibrary.Core.Tests` verification project); no external game APIs.

`AC-017` — VideoGame endpoints reject non-VideoGame Game IDs with `404` and the list
excludes non-VideoGame Games: a Game row with `game_type = 'BoardGame'` (created
directly through the test DbContext/SQL as verification infrastructure only) owned
by the caller does NOT appear in `GET /video-games`, and `PUT`/`DELETE
/video-games/{nonVideoGameId}` each return `404 Video game not found`.

`AC-018` — The Owned → non-Owned ignored-field semantics are pinned by tests: an
`Owned` → `Wishlist`/`Interested` PUT sending `rating: 6`, over-length Notes, another
user's Platform ID, or a nonexistent Platform ID returns `200` and preserves the
persisted Rating, Notes, and Platform associations; the same invalid values on a
normal full-replacement update return the existing `400`; the ignored Platform IDs
are not queried (no existence leak).

`AC-019` — Game/LibraryEntry lifecycle: `POST /video-games` creates the Game and its
LibraryEntry together (both rows exist, entry references the created Game); `DELETE
/video-games/{id}` removes both plus the association rows; a representative normal
application operation sequence leaves no orphan `games` row (no `games` row without
its `library_entries` row).

`AC-020` — The database CHECK rejects non-Owned status/progress invalid persisted
states: a direct SQL/test-DbContext write placing `GameStatus`/`ProgressPercentage`
on a Wishlist/Interested entry (e.g., Wishlist + `GameStatus = Playing`, or Interested
+ `ProgressPercentage = 50`) is rejected by the `library_entries` CHECK constraint.

`AC-021` — Platform deletion is blocked even when the referencing VideoGame is
Wishlist/Interested: after transitioning an Owned VideoGame to Wishlist while
preserving its Platform, `DELETE /platforms/{id}` returns `409 Platform in use`;
after deleting the VideoGame, `DELETE /platforms/{id}` returns `204`.

`AC-022` — Preservation of prior scope: existing user isolation (cross-user Game ids
→ `404`, cross-user Platform ids → identical `400`) and existing Platform management
behavior (including the new in-use `409`) remain intact, and all previous Feature 004
acceptance criteria remain represented and verifiable.

## Verification

For each acceptance criterion, an implementation agent verifies as follows.

`AC-001`:
- Run the API and issue `curl -i http://localhost:5218/video-games` and `curl -i
  http://localhost:5218/genres` with no `Authorization` header: status `401` with a
  `WWW-Authenticate: Bearer` challenge. The same holds for `POST`, `PUT`, and
  `DELETE /video-games` without a token.

`AC-002`:
- Using a valid Supabase access token (or a test minted token against the test
  factory), after creating a Platform:
  `curl -i -X POST -H "Authorization: Bearer <token>" -H "Content-Type:
  application/json" -d '{"name":"  Elden Ring  ","acquisitionStatus":"Owned",
  "platformIds":["<platform-id>"]}' http://localhost:5218/video-games`. Expect
  `201`, body name `"Elden Ring"`, no `Location` header. Then `GET /video-games`
  returns exactly one item with name `"Elden Ring"`.

`AC-003`:
- `POST /video-games` with `{"name":"G","acquisitionStatus":"Owned","platformIds":[]}`
  → `400`. With `{"name":"G","acquisitionStatus":"Wishlist"}` (no Platforms) →
  `201`.

`AC-004`:
- `POST`/`PUT` with `{"name":""}`, `{"name":"   "}`, `{"name":null}`, a body
  without `name`, and a 101-character `name` each return `400`. `rating` 0 and 6,
  `progressPercentage` −1 and 101 each return `400`. `PUT` with
  `acquisitionStatus` omitted/null returns `400`.

`AC-005`:
- Covered by the integration tests: create `Wishlist`; `PUT` it to `Owned` with
  `platformIds: []` → `400`; create `Owned` with a Platform and `gameStatus:
  "Playing"`, `progressPercentage: 50`, `rating: 4`, `notes: "n"`; `PUT` it to
  `Wishlist` (with `gameStatus`/`progress` still in the body) → `200` with both
  `null` and `platformIds`/`rating`/`notes` preserved; `PUT` the same Owned game to
  `Wishlist` with `platformIds: []`, `rating: null`, and `notes: null` → `200` and
  assert the persisted Platform/Rating/Notes are still present in the response and
  in `game_platforms` (direct query); `PUT` the same Owned game to `Wishlist` with
  `rating: 6`, over-length `notes`, another user's Platform ID, and a nonexistent
  Platform ID (each separately) → `200` with the persisted Rating/Notes/Platforms
  preserved; repeat representative ignored-field cases for `Owned` → `Interested`;
  assert the same invalid values on a normal full-replacement update return `400`;
  `PUT` it back to `Owned` with `platformIds: []` → `400` (full replacement outside
  the special transition).

`AC-006`:
- Using tokens for two distinct accounts, create VideoGames for each (including the
  same name in both). `GET /video-games` for A returns only A's; for B only B's.
  Authenticating as B, `PUT /video-games/{aGameId}` and `DELETE
  /video-games/{aGameId}` each return `404`. As A, submitting a request with a
  Platform id created by B returns `400`. Covered by the integration tests.

`AC-007`:
- `GET /genres` returns an array of 15 `{ "id", "name" }` items whose names match
  `GenresCatalog` **order-independently** (compare the persisted/returned names as a
  set against the catalog set, not by position). Create a VideoGame with two genre
  ids; the response and `GET /video-games` include them. `POST` with a random
  `genreIds` value returns `400`. No `POST/PUT/DELETE /genres` routes exist.

`AC-008`:
- `DELETE /video-games/{id}` returns `204`; `GET /video-games` no longer contains it;
  a second `DELETE` returns `404`. Using the test connection, query
  `SELECT COUNT(*) FROM games WHERE id = '<id>'`,
  `SELECT COUNT(*) FROM library_entries WHERE game_id = '<id>'`,
  `SELECT COUNT(*) FROM game_genres WHERE game_id = '<id>'`, and
  `SELECT COUNT(*) FROM game_platforms WHERE library_entry_id = '<entry-id>'` and
  assert `0` each. No FK error surfaces during the deletion.

`AC-009`:
- Covered by the integration tests: create a Platform, create an Owned VideoGame
  using it, `DELETE /platforms/{id}` → `409`; also transition the VideoGame to
  `Wishlist` (preserving its Platform) and confirm `DELETE /platforms/{id}` still →
  `409` (protection is independent of AcquisitionStatus); delete the VideoGame, then
  `DELETE /platforms/{id}` → `204`. Optionally repeat manually.

`AC-010`:
- Set `ConnectionStrings:Test` to a real PostgreSQL database and run
  `dotnet test tests/backend/GameLibrary.IntegrationTests`. The migration test
  passes and asserts the schema contains the new tables. Alternatively run
  `dotnet ef migrations list` to confirm the new migration and `dotnet ef database
  update` against a scratch database to confirm it applies and seeds 15 genres.

`AC-011`:
- Covered by the integration test: after two VideoGame creates for a fresh test
  user, query `SELECT COUNT(*) FROM libraries WHERE user_id = '<sub>'` and assert
  `1`.

`AC-012`:
- `PUT /video-games/{id}` with updated fields returns `200` with the new values;
  `GET /video-games` reflects them. `PUT`/`DELETE` with a random `{id}` returns
  `404`.

`AC-013`:
- Run `dotnet build src/backend/GameLibrary.sln` (succeeds); run
  `dotnet test tests/backend/GameLibrary.IntegrationTests` and
  `dotnet test tests/backend/GameLibrary.Core.Tests` with `ConnectionStrings:Test`
  configured (all pass); from `src/frontend/` run `ng test --watch=false` and
  `ng lint` (both pass). Existing `HealthEndpointTests`, `AuthEndpointTests`,
  `JwtValidationConfigurationTests`, and `PlatformEndpointTests` still pass.

`AC-014`:
- Run `ng serve`; signed out, visiting `http://localhost:4200/video-games` redirects
  to `/login`. Signed in, the page loads the VideoGames (loading indicator while the
  request is in flight, empty state when none exist). Create/edit/delete a VideoGame
  through the UI and confirm list updates and validation messages. Force a `401`
  (or intercept a response as `401`) and confirm the app clears auth state and
  navigates to `/login`.

`AC-015`:
- In the UI, with status `Owned`, confirm saving is blocked with the message when no
  Platform is selected and the no-platforms guidance links to `/platforms` when the
  user has none. Confirm that with exactly one Platform available, a new `Owned`
  form preselects it; with more than one, none are preselected. Switch to `Wishlist`:
  the GameStatus/Progress controls disappear, saving succeeds without Platforms, and
  the submitted payload has `gameStatus: null`/`progressPercentage: null`.
  Rating/progress/name validation blocks invalid values client-side, and
  normalize-before-length validation accepts a `name`/`notes`/`coverImageUrl` whose
  raw untrimmed length exceeds the limit but whose trimmed value is valid. Covered
  by the frontend tests.

`AC-016`:
- Review the repository: no BoardGame entity, table, or `board` game rows exist; no
  `game_type` value other than `VideoGame` is creatable through the API; no search/
  filter/sort or Random Picker endpoints/UI; no RLS policies or Supabase client
  database access; no genre-management endpoints; no `PATCH` route, no single-resource
  `GET /video-games/{id}`, and no version/ETag/lock infrastructure; `package.json`/
  `.csproj` files gain no new dependencies; the backend has exactly two application
  projects plus the test projects under `tests/`; the new frontend code lives under
  `features/games/` and uses services/signals only.

`AC-017`:
- Covered by the integration tests: using the test DbContext/SQL, insert a `games`
  row with `game_type = 'BoardGame'` and a `library_entries` row for the test user
  (verification infrastructure only). Then `GET /video-games` does not contain that
  Game id; `PUT /video-games/{nonVideoGameId}` returns `404`; `DELETE
  /video-games/{nonVideoGameId}` returns `404`. Optionally repeat manually by
  inserting such a row in a scratch database.

`AC-018`:
- Covered by the integration tests (see `AC-005` verification): each ignored-field
  case (rating `6`, over-length Notes, another user's Platform ID, nonexistent
  Platform ID) on `Owned` → `Wishlist`/`Interested` returns `200` and preserves the
  persisted values; the same invalid values on a normal full-replacement update
  return `400`. A direct query confirms the ignored foreign/nonexistent Platform ids
  never produce a `game_platforms` row.

`AC-019`:
- Covered by the integration tests: after `POST /video-games`, query
  `SELECT COUNT(*) FROM games WHERE id = '<id>'` and
  `SELECT COUNT(*) FROM library_entries WHERE game_id = '<id>'` and assert `1` each.
  After `DELETE /video-games/{id}`, assert `0` each plus the association rows.
  Run a representative create → update → list → delete sequence and assert no
  `games` row exists without its `library_entries` row
  (`SELECT COUNT(*) FROM games g LEFT JOIN library_entries le ON le.game_id = g.id
  WHERE le.id IS NULL` = `0`).

`AC-020`:
- Covered by the integration tests: using the test DbContext/SQL, insert or update a
  `library_entries` row with `acquisition_status = 'Wishlist'` and
  `game_status = 'Playing'`, and one with `acquisition_status = 'Interested'` and
  `progress_percentage = 50`; assert both writes are rejected by the CHECK
  constraint (`DbUpdateException`/PostgreSQL check violation).

`AC-021`:
- Covered by the integration tests: create a Platform, create an Owned VideoGame
  using it, transition it to `Wishlist` (preserving the Platform); `DELETE
  /platforms/{id}` returns `409 Platform in use`; after deleting the VideoGame,
  `DELETE /platforms/{id}` returns `204`. Optionally repeat manually.

`AC-022`:
- Confirm the existing integration-test suites (`PlatformEndpointTests`, two-user
  isolation cases in this feature, `HealthEndpointTests`, `AuthEndpointTests`,
  `JwtValidationConfigurationTests`) still pass unchanged, and that every prior
  Feature 004 acceptance criterion above (`AC-001`–`AC-016`) has a corresponding
  implementation and verification entry that remains valid.

## Dependencies

- No new NuGet packages or npm packages are required. The approved stack (EF Core,
  Npgsql, ASP.NET Core, Angular, `@supabase/supabase-js`) covers everything. The new
  `GameLibrary.Core.Tests` verification project uses the existing xUnit test
  framework already in the repository.
- Test tooling: existing xUnit + `Microsoft.AspNetCore.Mvc.Testing` for backend
  integration tests; xUnit for the new unit-test project; existing Angular/Vitest
  tooling for frontend tests.
- Runtime prerequisites are unchanged from Features 001–003: a real PostgreSQL
  database for `ConnectionStrings:Default`/`ConnectionStrings:Test` (the current
  remote Supabase project `iramzxpjbnldhebykzhx`), and the existing JWT
  configuration. No Docker is required in the current environment.

Version-selection policy: unchanged (stable, supported versions recorded in
`README.md`).

## Risks / Notes

- **`DatabaseMigrationTests` must be updated.** Its current assertion expects exactly
  `["__EFMigrationsHistory", "libraries", "platforms"]`. Feature 004 adds five
  tables, so the test must assert the new eight-table set (order-independently).
  Forgetting this will fail the build/test gate.
- **`PlatformService` and `LibraryService` extraction.** The ensure-library helper is
  moved verbatim into `LibraryService` and `PlatformService` uses it. Platform
  behavior must not change; the existing `PlatformEndpointTests` prove it.
- **The `game_platforms` table name and columns.** Feature 003 documented the table
  name `game_platforms` and the FK `game_platforms.platform_id → platforms.id` with
  `ON DELETE RESTRICT`; this feature honors both. Because the Domain Specification
  attaches Platforms to VideoGame LibraryEntries, the join's other column is
  `library_entry_id → library_entries.id` (CASCADE). This keeps Feature 003's
  structural promise intact while preserving the approved domain model. Despite the
  table name, Platforms are associated with the user's `LibraryEntry`, not directly
  with the conceptual `Game`; the name and relationship columns are retained and are
  not renamed. The `GamePlatform` entity and/or its EF mapping MUST carry the
  concise comment: "Despite the table name, Platform association is
  LibraryEntry-level/user-specific data. `library_entry_id` is intentional." README
  text referencing the join should be updated to reflect the actual columns and this
  semantic comment.
- **Owned → Wishlist/Interested preservation is backend-authoritative.** The backend
  preserves the currently persisted Platforms, Rating, and Notes during an
  `Owned` → `Wishlist`/`Interested` transition even when the request sends an empty
  `platformIds` array, `null` Rating/Notes, an out-of-range rating (`rating: 6`),
  over-length Notes, or foreign/nonexistent Platform ids, and forces `gameStatus`/
  `progressPercentage` to `null`. The request's `platformIds`, `rating`, and `notes`
  are ignored — neither applied nor validated — and the ignored Platform ids are not
  queried (no existence leak). This must NOT depend on Angular resending previous
  values. It is a special Domain transition rule that takes precedence over normal
  PUT replacement semantics for the preserved fields. Implementers must add
  integration tests proving the client cannot accidentally erase or corrupt the
  preserved fields, and that the same invalid values on a normal update return the
  existing `400` (see Testing).
- **GameType guards are required NOW.** Every VideoGame backend operation must
  require `Game.GameType == GameType.VideoGame`. `ListAsync` filters by GameType in
  addition to Library ownership; `LoadOwnedAsync` returns `404` for a Game that
  exists, belongs to the user, but has another `GameType`. This is not deferred to
  Feature 005: it prevents future cross-type operations such as
  `PUT /video-games/{boardGameId}` or `DELETE /video-games/{boardGameId}`. The
  integration test for the guard creates a non-VideoGame Game row directly through
  the test DbContext/SQL — verification infrastructure only; no BoardGame
  application functionality is introduced.
- **The `game_type` CHECK intentionally includes `BoardGame`.** `game_type IN
  ('VideoGame', 'BoardGame')` is deliberate: `GameType` is the approved
  discriminator, BoardGame is the immediately following approved feature (005), and
  allowing the discriminator value does not implement BoardGame functionality. The
  API/application services remain responsible for which type each feature can
  create/manage, and the VideoGame `GameType` guards prevent cross-type operations.
  Feature 004 MUST NOT create BoardGames through application endpoints.
- **Scope of the `library_entries.game_id` unique index.** The index guarantees a
  Game can be referenced by at most one LibraryEntry — nothing more. It does not
  guarantee every Game has a LibraryEntry. The complete lifecycle invariant (every
  application-created Game has exactly one LibraryEntry; no orphan Games) is
  enforced by `VideoGameService` creation/delete lifecycles and application tests.
  The Game ↔ LibraryEntry relationship is configured as required on the LibraryEntry
  (dependent) side (Game is principal, `GameId` required, `ON DELETE RESTRICT`
  retained); the deletion service removes the dependent before the principal within
  one transaction. Do NOT add triggers, deferred constraints, circular FKs, or
  database procedures to enforce Game → LibraryEntry existence.
- **Transition UX consequence (Owned → Wishlist/Interested).** Because Rating and
  Notes are preserved during these transitions, a user cannot change Rating or Notes
  in the same save that changes AcquisitionStatus away from Owned; the preservation
  rule wins. No warning/modal is required for the MVP; a subsequent normal edit can
  change Rating/Notes. Document this in the frontend and do not add workflow
  complexity.
- **Relational backstop tests.** The non-Owned status/progress CHECK is verified by
  attempting the invalid persisted state directly (SQL/test DbContext), NOT through
  the API, because the service normalizes first. Platform delete protection is
  verified to hold for a Wishlist/Interested referencing entry as well as an Owned
  one, because the FK does not consider AcquisitionStatus.
- **Delete-protection mapping.** The `409` is produced by catching the PostgreSQL
  FK-violation `DbUpdateException` in `PlatformService.DeleteAsync` and throwing
  `PlatformInUseException`. Do not inspect or expose constraint names or SQL; the
  only FK that can block a Platform deletion is the `game_platforms` join, so any
  FK violation on Platform delete is the in-use case.
- **Enum storage.** The new enum columns are `varchar` storing enum-name strings via
  EF value conversion. Do not create PostgreSQL enum types (they would add Supabase
  schema artifacts for no benefit) and do not reuse the Platform `name_normalized`
  generated-column pattern for games (names are not unique). The persisted enum
  names are part of the database storage contract: if a C# enum member is renamed in
  the future, the corresponding persisted value and any database CHECK constraints
  referencing the enum name string must be updated through an EF Core migration.
- **Non-Owned clears GameStatus/Progress.** The server normalizes `gameStatus` and
  `progressPercentage` to `null` for any resulting `Wishlist`/`Interested` state,
  regardless of the request payload. This is the approved "clears" rule, not a
  rejection; the frontend hides/clears those fields anyway.
- **Full-replacement `PUT` and last-write-wins are deliberate MVP tradeoffs.** The
  request always carries the complete mutable state; omitted optional fields are
  treated as `null` (clearing); Platform and Genre sets are replaced by the
  submitted sets. The client always sends the full set for normal updates. The one
  exception is the `Owned` → `Wishlist`/`Interested` special transition (see the
  preservation note above). There is no single-resource `GET /video-games/{id}`; the
  list is the only read and provides everything the edit form needs. Do NOT add
  `PATCH`, optimistic concurrency, `RowVersion`, ETags, locks, or merge-patch
  behavior.
- **Genres seeding.** The 15 catalog values are seeded via `HasData` with fixed,
  deterministic GUIDs so the seed and the `game_genres` FKs are stable across
  environments and migrations. `GenresCatalog` is the authoritative code definition
  of the names; the EF seed derives from it and the migration contains the persisted
  seed data. If `GenresCatalog` changes in the future: (1) the model seed changes,
  (2) a new EF Core migration must be generated, and (3) tests must verify the
  persisted catalog matches the approved catalog. The integration tests keep
  verifying exactly the 15 approved values. Users cannot mutate Genres and no
  genre-management endpoints exist.
- **Case-insensitive deterministic ordering.** The list order is case-insensitive
  Name ascending, original Name ascending, `CreatedAt` ascending, `Id` ascending,
  implemented with an EF Core/PostgreSQL-translatable expression. No generated
  normalized-name column is added for Games (Game names are not unique, unlike
  Platform names). Integration tests demonstrate the deterministic,
  case-insensitive order.
- **Frontend normalize-before-length.** The form trims `name`/`notes`/
  `coverImageUrl` before length validation and treats empty-after-trim `notes`/
  `coverImageUrl` as `null`, mirroring backend normalization. A value is not
  rejected solely because its raw untrimmed representation exceeds the limit when
  the normalized value is valid.
- **Quick-add auto-selection is a frontend convenience only.** With exactly one
  Platform, a new `Owned` form preselects it; with more than one, none are
  preselected; with zero, `Owned` remains the default with `/platforms` guidance and
  no auto-created Platform. Backend validation remains authoritative.
- **No unit tests that mirror EF queries.** `GameLibrary.Core.Tests` tests only pure
  `VideoGameRules` behavior. All EF/persistence/security behavior is verified by the
  real-PostgreSQL integration tests, per AGENTS.md.
- **No optimistic concurrency.** Concurrent edits of the same VideoGame use
  last-write-wins, which is acceptable for the MVP. No version columns or locks are
  introduced.
- **README update.** Document the new endpoints (`/video-games`, `/genres`), the
  Game/LibraryEntry schema (including the required relationship and the at-most-one
  `game_id` index), the validation and acquisition rules (including the ignored-field
  transition semantics), the `game_type` CHECK decision (BoardGame permitted value,
  not creatable here), the `game_platforms` semantic comment, the unit-test
  project, the seeded genre catalog, and the fact that the Platform delete-protection
  `409` is now reachable.
- **Frontend budget.** The video-games feature adds no large dependencies; if the
  Angular build reports a budget warning, record the adjustment in `README.md` as
  was done for `supabase-js` (no budget change is expected).

## Open Questions

None. The Game/LibraryEntry persistence representation (single `games` table with a
`GameType` discriminator plus a `library_entries` table with the at-most-one
`game_id` index and the required Game ↔ LibraryEntry relationship on the dependent
side), the VideoGame model, the GameType guards (required on every VideoGame backend
operation; non-VideoGame rows behave as `404` and never appear in the list), the
AcquisitionStatus/GameStatus/Progress rules (including the backend-authoritative
Owned → Wishlist/Interested preservation of Platforms/Rating/Notes with ignored and
non-validated transition fields, the clear-on-non-Owned normalization, and the
Owned-requires-Platform rule), the Genre catalog representation (`genres` reference
table + `game_genres` + read-only `GET /genres`, with `GenresCatalog` as the
authoritative code definition verified order-independently), the `game_platforms`
join (Feature 003's promised name and `platform_id` RESTRICT FK joined to
`library_entries`, Platforms associated with the user's LibraryEntry despite the
table name, with the required semantic code comment), the now-reachable `409`
Platform-delete protection (independent of AcquisitionStatus), the deletion/cascade
behavior (dependent-before-principal in one transaction; no orphan Games), the
database CHECK backstops (non-Owned status/progress, and `game_type IN ('VideoGame',
'BoardGame')` as a deliberate future-proof discriminator), the API shape (full-
replacement `PUT` kept; no `PATCH`, no single-GET, no optimistic concurrency, `GET
/genres`), the enum varchar persistence contract, the deterministic case-insensitive
list ordering, the frontend normalize-before-length validation, the quick-add
single-Platform auto-selection, the transition UX consequence (Rating/Notes cannot
change in the same save as an Owned → non-Owned transition), the error semantics, the
migration/schema, the introduction of the `GameLibrary.Core.Tests` unit-test project,
and the test approach are all defined here and by the approved Product, Domain, and
Architecture Specifications.
