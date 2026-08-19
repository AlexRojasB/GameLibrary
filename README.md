# Game Library

Personal game library web application. Angular PWA frontend, ASP.NET Core Web API backend, Supabase PostgreSQL database and authentication.

## Status

Feature 007 (Random Picker) is implemented. Authenticated users can open the protected Angular `/random-picker` view and request an owned game from the backend-authoritative `POST /random-picker/pick` API. The picker supports VideoGames, BoardGames, and All modes, temporary shown-history exclusion, and distinct `SUCCESS`, `NO_CANDIDATES`, and `ALL_ALREADY_SHOWN` outcomes. No migration or persisted picker state was introduced. Features 001–006 are preserved, including platform management, VideoGame/BoardGame CRUD, Library browse/search/filter/sort, domain invariants, and cross-user isolation.

## Documentation

Authoritative specifications (read before implementing a feature):

- `docs/product/product-specification.md`
- `docs/domain/domain-specification.md`
- `docs/architecture/architecture-specification.md`
- `specs/` — approved feature specifications
- `AGENTS.md` — coding-agent instructions

## Repository Layout

```
├── AGENTS.md
├── README.md
├── docs/                 # authoritative specifications (product/domain/architecture)
├── specs/                # feature specifications
├── src/
│   ├── backend/
│   │   ├── GameLibrary.sln
│   │   ├── GameLibrary.Api/    # ASP.NET Core Web API (HTTP, config, auth boundary)
│   │   └── GameLibrary.Core/   # domain, application logic, EF Core DbContext/migrations
│   └── frontend/               # Angular PWA
└── tests/
    ├── backend/GameLibrary.Core.Tests/      # domain/application unit tests
    └── backend/GameLibrary.IntegrationTests/
```

## Required Versions

Version-selection policy: stable, supported versions available at implementation time. Preview versions are not used unless explicitly justified. Actual versions used in this repository:

| Tool | Version | Notes |
| --- | --- | --- |
| .NET SDK | 10.0.400 (LTS) | `dotnet --version` |
| EF Core | 10.0.11 | `GameLibrary.Core.csproj` |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.3 | `GameLibrary.Core.csproj` |
| `dotnet-ef` global tool | 10.0.11 | aligned with EF Core; `dotnet tool update -g dotnet-ef` |
| Node.js | 24.15.0 (LTS) | `node --version` |
| npm | 11.12.1 | `npm --version` |
| Angular | 22.1.0 | `@angular/cli` ^22.1.4, `src/frontend/package.json` |
| TypeScript | ~6.0.2 | `src/frontend/package.json` |
| xUnit | 2.9.3 | backend integration tests |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.11 | backend integration tests |
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.11 | backend JWT bearer validation (`GameLibrary.Api.csproj`) |
| @supabase/supabase-js | 2.112.3 | frontend Supabase auth SDK (`src/frontend/package.json`) |
| Supabase CLI | current stable | install: `winget install supabase.cli` (or `npm i -g supabase`) |
| Docker Desktop | current stable | required by the local Supabase stack |

## Prerequisites

- Git
- Node.js (LTS) and npm
- Angular CLI (installed per-project via npm; no global install required)
- .NET SDK (LTS)
- `dotnet-ef` global tool: `dotnet tool install --global dotnet-ef`
- Docker Desktop (for the local Supabase stack)
- Supabase CLI

PostgreSQL itself is provided by the local Supabase stack through Docker; no separate PostgreSQL installation is required for local development.

## Install

Backend:

```
dotnet restore src/backend/GameLibrary.sln
```

Frontend:

```
cd src/frontend
npm ci
```

## Local Infrastructure (Supabase / PostgreSQL)

First use:

```
supabase init
supabase start
```

Obtain the local direct Postgres connection string:

```
supabase status
```

Use the direct Postgres port (default `127.0.0.1:54322`) for migrations — not the transaction-pooling port. The local default is `postgresql://postgres:postgres@127.0.0.1:54322/postgres`.

> Machines without Docker cannot run `supabase start`. In that case use a standalone Docker PostgreSQL or another real PostgreSQL instance and point `ConnectionStrings:Default` / `ConnectionStrings:Test` at it.

## Configuration

Set `ConnectionStrings:Default` for the application database via user-secrets or an environment variable:

```
dotnet user-secrets set "ConnectionStrings:Default" "<connection-string>" --project src/backend/GameLibrary.Api
# or
$env:ConnectionStrings__Default = "<connection-string>"
```

Set `ConnectionStrings:Test` for the integration test database (for example `game_library_test` on the local Supabase instance, or a Docker PostgreSQL):

```
dotnet user-secrets set "ConnectionStrings:Test" "<connection-string>" --project src/backend/GameLibrary.Api
# or
$env:ConnectionStrings__Test = "<connection-string>"
```

Committed configuration is templates/placeholders only. Copy the template for local development:

```
Copy-Item src/backend/GameLibrary.Api/appsettings.Development.example.json src/backend/GameLibrary.Api/appsettings.Development.json
```

`appsettings.Development.json` is gitignored. The example template:

```json
{
  "ConnectionStrings": {
    "Default": "<connection-string>"
  },
  "AllowedOrigins": [
    "http://localhost:4200"
  ]
}
```

`AllowedOrigins` is the CORS allow-list (dev default: the Angular dev origin `http://localhost:4200`). The CORS policy is never unrestricted.

## Authentication (Feature 002)

Supabase Auth is the identity provider. `supabase-js` handles registration, login, logout, session persistence, and token refresh in the browser; the application does not re-implement any of those. The backend validates Supabase-issued access tokens with ASP.NET Core JWT bearer authentication using standards-based OpenID Connect metadata/JWKS discovery, so **no backend Supabase secret is required**.

### Supabase "Confirm email" setting

The current Supabase project (`iramzxpjbnldhebykzhx`) has **email confirmation required** (`mailer_autoconfirm: false`). Consequences:

- `signUp` returns a user **without a session**; the register view shows a "check your email to confirm your address" notice.
- Login is gated on email confirmation.
- This feature defines both behaviors (immediate session vs. confirmation notice) and does not reconfigure the Supabase project.

### Frontend configuration

Public values in `src/environments/environment.development.ts`:

| Key | Value |
| --- | --- |
| `apiBaseUrl` | `http://localhost:5218` |
| `supabaseUrl` | `https://iramzxpjbnldhebykzhx.supabase.co` |
| `supabaseKey` | `sb_publishable_4JUAkR-M60jBHyXjE5YMyg_3LU-z2xM` (project publishable key; public) |

The default `src/environments/environment.ts` uses empty placeholders for `supabaseUrl`/`supabaseKey` (production). No secret value appears in committed files.

The `SUPABASE_CLIENT` injection token (registered in `app.config.ts`) creates the client from these values. `core/auth/` holds the auth infrastructure: `AuthService` (signal-based state, session restoration via `getSession()` + `onAuthStateChange`, normalized errors), the route guards, and the API token interceptor. The interceptor attaches `Authorization: Bearer <access-token>` only to requests whose origin matches the configured API base URL (an `API_BASE_URL` injection token defaulting to `environment.apiBaseUrl`), and only when a session exists.

Routes: `''` (protected home — shows the signed-in identity and the `GET /auth/me` result, plus logout), `login` and `register` (guest-only), `health` (anonymous, retained from Feature 001), `library` (protected, Feature 006), `random-picker` (protected, Feature 007), `platforms` (protected, Feature 003), `video-games` (protected, Feature 004), and `board-games` (protected, Feature 005).

### Backend JWT configuration

The committed `Authentication:Schemes:Bearer` section (`appsettings.json` and the `appsettings.Development.example.json` template) is non-secret:

```json
"Authentication": {
  "Schemes": {
    "Bearer": {
      "MetadataAddress": "https://iramzxpjbnldhebykzhx.supabase.co/auth/v1/.well-known/openid-configuration",
      "TokenValidationParameters": {
        "ValidAudiences": ["authenticated"]
      }
    }
  }
}
```

`Program.cs` additionally sets `MapInboundClaims = false` (so `sub` is read unchanged), applies `ValidAudiences` in code from the config section (the framework's options config reads `ValidAudiences` at the scheme level, so the value is bound explicitly in code), and sets `ValidAlgorithms = [ES256, RS256]`. Signature validation uses the Supabase JWKS discovered from metadata; `GET /health` remains anonymous.

**Production deployment must override `Authentication:Schemes:Bearer`** (the `MetadataAddress` and any environment-specific issuer/audience values) with the production Supabase project's OpenID Connect metadata URL before deploying. The development values target the current `iramzxpjbnldhebykzhx` project only.

### Verification

- `GET /auth/me` without an `Authorization` header → `401` with a `WWW-Authenticate: Bearer` challenge.
- `GET /auth/me` with a valid Supabase access token → `200 {"userId":"<sub>"}`.
- Backend integration tests cover authorization behavior (offline, using a test-only symmetric signing key) and the production JWT configuration; the auth tests need no database.
- Frontend tests cover the `AuthService`, guards, interceptor, and login/register/home views (`ng test --watch=false`).

**End-to-end manual register/login verification is deferred to Feature 8 (PWA polish and MVP end-to-end verification)**, consistent with the approved delivery order. Automated backend and frontend tests cover the authentication behavior.

### Docker/local-Supabase alternative

If a local Supabase stack is used instead (machines with Docker), the Angular `supabaseUrl`/`supabaseKey` come from `supabase status` local credentials, the backend `MetadataAddress` points at `http://127.0.0.1:54321/auth/v1/.well-known/openid-configuration` with `RequireHttpsMetadata = false` for local HTTP, and the audience/issuer semantics stay the same. Not required for the current environment.

## Platform Management (Feature 003)

Authenticated users manage their personal video-game platforms (the only platform data for now: a required name, trimmed, at most 100 characters). Every operation is scoped to the authenticated Supabase `sub` (stored as opaque `text` on `libraries.user_id`); the API never accepts a user/owner ID from the request. All rules live in `GameLibrary.Core` (`PlatformService`) and are authoritative; the API maps failures to problem-details responses.

Endpoints (all `[Authorize]`):

| Method | Route | Success | Errors |
| --- | --- | --- | --- |
| `GET` | `/platforms` | `200` with a JSON array of `{ "id", "name" }` ordered by name (case-insensitive ascending) | `401` |
| `POST` | `/platforms` | `201` with the created `{ "id", "name" }` (no `Location` header) | `400`, `401`, `409` |
| `PUT` | `/platforms/{id}` | `200` with the updated `{ "id", "name" }` | `400`, `401`, `404`, `409` |
| `DELETE` | `/platforms/{id}` | `204` | `401`, `404` |

Behavior:

- **Lazy Library creation.** The first `POST /platforms` for a user creates that user's `libraries` row (exactly one per user, enforced by a unique index on `user_id`) and the Platform; reads for a user with no Library return an empty list and create nothing.
- **Naming rules.** Names are required (non-empty after trimming leading/trailing whitespace) and at most 100 characters (trimmed). Duplicates are case-insensitive within a single user's Library and rejected with `409` on create and rename (excluding the Platform being renamed); the same name in another user's Library is allowed. `name_normalized` is a stored generated column (`lower(name)`); uniqueness is enforced by the unique index on `(library_id, name_normalized)`.
- **User isolation.** Platforms belonging to another user behave as `404` (no resource enumeration). A missing/empty `sub` on an authenticated principal returns `401`.
- **Delete protection.** Feature 003 implements a plain, ownership-scoped delete. The "platform in use cannot be deleted" rule is enforced structurally by the `game_platforms` join table (Feature 004) with a foreign key `ON DELETE RESTRICT`; the frontend delete flow handles the resulting `409` ("This platform is in use and cannot be deleted.").

Frontend: a protected `/platforms` route (`features/platforms/`) lists platforms with loading/error (retry)/empty/list states, and provides create and rename forms plus delete-with-confirmation. Backend validation is mirrored in the form for immediate feedback but never overridden. A `401` from any platform API call clears the local session and navigates to `/login`.

## VideoGame Management (Feature 004)

Authenticated users manage personal video games (`VideoGame` type only for the MVP): name, optional cover image URL, acquisition status, Platforms, Genres, game status, progress, rating, and notes. Every operation is scoped to the authenticated Supabase `sub`; the API never accepts a user/owner ID from the request. Domain rules live in `GameLibrary.Core` (`VideoGameRules`/`VideoGameService`) and are authoritative; the API maps failures to problem-details responses.

Endpoints (all `[Authorize]`):

| Method | Route | Success | Errors |
| --- | --- | --- | --- |
| `GET` | `/video-games` | `200` with a JSON array ordered by name (case-insensitive ascending, then name, then created date, then id) | `401` |
| `POST` | `/video-games` | `201` with the created video game | `400`, `401` |
| `PUT` | `/video-games/{id}` | `200` with the updated video game | `400`, `401`, `404` |
| `DELETE` | `/video-games/{id}` | `204` | `401`, `404` |
| `GET` | `/genres` | `200` with the seeded genre catalog (read-only, ordered by name) | `401` |

Behavior:

- **Enums.** `GameType` (`VideoGame`/`BoardGame`), `AcquisitionStatus` (`Owned`/`Wishlist`/`Interested`, default `Owned` on create), `GameStatus` (`Backlog`/`Playing`/`Completed`/`Abandoned`/`WantToPlay`). Only `VideoGame` rows are exposed by this feature's endpoints.
- **Owned platform requirement.** `Owned` requires at least one Platform (`400` otherwise); `Wishlist`/`Interested` allow any count. `GameStatus` and `ProgressPercentage` (0–100) are valid only when `Owned` and are forced to `null` otherwise.
- **Owned → Wishlist/Interested transition.** Backend-authoritative: the request's `platformIds`, `rating`, `notes`, `gameStatus`, and `progressPercentage` are ignored; persisted Platforms, Rating, and Notes are preserved, and `GameStatus`/`ProgressPercentage` are cleared. A later normal edit can change Rating/Notes.
- **Full replacement.** All other updates replace the whole VideoGame; there is no PATCH and no row versioning.
- **Platforms/Genres.** Multi-select; unknown or foreign ids return `400` with a single message. The 15-seed genre catalog (`GenresCatalog`) is authoritative and read-only.
- **Platform delete protection.** The `game_platforms` join table references `platforms` with `ON DELETE RESTRICT`; deleting a referenced Platform returns `409` "Platform in use" (Feature 003's deferred rule is now structurally enforced).
- **Delete lifecycle.** Deleting a VideoGame removes its `library_entries` row (dependent) then the `games` row (principal) in one transaction, leaving no orphans.

Schema: the `AddVideoGameManagement` migration adds `games`, `library_entries`, `genres`, `game_genres`, and `game_platforms`. Enums are stored as enum-name strings (`varchar(20)`); check constraints enforce rating 1–5, progress 0–100, the Owned-only status/progress rule, and `game_type IN ('VideoGame','BoardGame')`. `Game` ↔ `LibraryEntry` is a required 1:1 with `ON DELETE RESTRICT` on both sides; `game_platforms` stores the `library_entry_id` (Platform association is LibraryEntry-level data).

Frontend: a protected `/video-games` route (`features/games/`) lists video games with loading/error (retry)/empty/list states and a create/edit form covering Name, CoverImageUrl, AcquisitionStatus, Platforms, Genres, GameStatus, Progress, Rating, and Notes. Quick Add defaults to `Owned` and auto-selects the single available Platform only when the user has exactly one. Switching to `Wishlist`/`Interested` hides and clears `GameStatus`/`Progress`; `Owned` with zero selected Platforms is blocked with guidance (linking to `/platforms` when none exist). Client validation (trim-before-length, `http(s)` cover URLs, rating/progress ranges) mirrors the backend but never overrides it. A `401` from any video-game/genre API call clears the local session and navigates to `/login`.

## BoardGame Management (Feature 005)

Authenticated users manage personal board games (`BoardGame` type only): name, required player counts (`minimumPlayers` ≥ 1, `maximumPlayers` ≥ `minimumPlayers`), optional approximate duration (positive integer minutes), optional interaction type (`Cooperative`/`Competitive`), acquisition status, rating, notes, and cover image URL. Every operation is scoped to the authenticated Supabase `sub`; the API never accepts a user/owner ID from the request. Domain rules live in `GameLibrary.Core` (`BoardGameRules`/`BoardGameService`) and are authoritative; the API maps failures to problem-details responses.

Endpoints (all `[Authorize]`):

| Method | Route | Success | Errors |
| --- | --- | --- | --- |
| `GET` | `/board-games` | `200` with a JSON array ordered by name (case-insensitive ascending, then name, then created date, then id) | `401` |
| `POST` | `/board-games` | `201` with the created board game (no `Location` header) | `400`, `401` |
| `PUT` | `/board-games/{id}` | `200` with the updated board game | `400`, `401`, `404` |
| `DELETE` | `/board-games/{id}` | `204` | `401`, `404` |

Behavior:

- **No Platforms, Genres, GameStatus, or Progress.** BoardGames never use Platforms, Genres, `GameStatus`, or `ProgressPercentage` (domain-invariant for the MVP). BoardGame create/update never writes those columns and never creates `game_platforms`/`game_genres` rows.
- **Player counts required.** `minimumPlayers` ≥ 1 and `maximumPlayers` ≥ `minimumPlayers`; both are required (`400` otherwise). `approximateDuration` is an optional positive integer. `interactionType` is optional and must be exactly `Cooperative` or `Competitive` (stored as the enum name string).
- **Full replacement.** `PUT /board-games/{id}` replaces the whole BoardGame uniformly; unlike VideoGame, an acquisition-status change does not preserve/clear any subset of fields (there are no Owned-only fields for BoardGames).
- **AcquisitionStatus.** `Owned` (default on create) / `Wishlist` / `Interested`; no Platform requirement exists for `Owned`.
- **Cross-type isolation.** `GET /board-games` returns only `GameType.BoardGame` rows; `PUT`/`DELETE /board-games/{id}` return `404` for a VideoGame row, another user's BoardGame, or a nonexistent id. VideoGame endpoints likewise reject BoardGame ids. Game names are not unique, including across types.
- **Delete lifecycle.** Deleting a BoardGame removes its `library_entries` row (dependent) then the `games` row (principal) in one transaction, leaving no orphans.

Schema: the `AddBoardGameManagement` migration adds nullable `minimum_players`, `maximum_players`, `approximate_duration`, and `interaction_type` columns to the existing `games` table (no new tables, no EF inheritance). Single-table CHECK constraints enforce the player-count rules, duration positivity, interaction-type whitelist, BoardGame-requires-players, and VideoGame-null-board-columns invariants. `InteractionType` is mapped via value conversion storing the enum name (`varchar(20)`).

Frontend: a protected `/board-games` route (`features/games/board-games/`) lists board games with loading/error (retry)/empty/list states and a create/edit form covering Name, MinimumPlayers, MaximumPlayers, ApproximateDuration, InteractionType (None/Cooperative/Competitive), AcquisitionStatus, Rating, Notes, and CoverImageUrl. Quick Add defaults to `Owned`, defaults new forms to `Competitive` as a frontend-only UX default, and requires only name and player counts. Client validation (trim-before-length, player-count rules, duration positivity, `http(s)` cover URLs, rating range) mirrors the backend but never overrides it. A `401` from any board-game API call clears the local session and navigates to `/login`. The shared `AcquisitionStatus` type is extracted into `features/games/acquisition-status.ts` and re-exported by `video-game.ts`.

## Library Browse / Search / Filter / Sort (Feature 006)

`GET /library` is a read-only, authenticated endpoint returning one JSON array of unified library items. The API accepts no user, owner, or library ID; ownership is always derived from the validated Supabase `sub`. A user with no Library receives `200 []`, and reads never create a `libraries` row.

Query parameters:

| Parameter | Values | Behavior |
| --- | --- | --- |
| `search` | string | Trimmed, case-insensitive literal substring on game name. `%`, `_`, and `\` are escaped and matched literally via parameterized PostgreSQL `ILIKE`. |
| `gameType` | `VideoGame`, `BoardGame` | Omitted means both types. |
| `acquisitionStatuses` | repeated `Owned`, `Wishlist`, `Interested` | OR within selected statuses. |
| `platformIds` | repeated GUID | VideoGame-only; unknown/foreign well-formed IDs match nothing, not `400`. |
| `genreIds` | repeated GUID | VideoGame-only. |
| `gameStatuses` | repeated `Backlog`, `Playing`, `Completed`, `Abandoned`, `WantToPlay` | VideoGame-only; requires non-null status. |
| `ratingMin` | `1`-`5` | Minimum-rating semantics; null ratings do not match. |
| `playerCount` | integer `>= 1` | BoardGame-only range match: `minimumPlayers <= playerCount <= maximumPlayers`. |
| `interactionTypes` | repeated `Cooperative`, `Competitive` | BoardGame-only; requires non-null interaction type. |
| `sort` | `NameAsc`, `NameDesc`, `RatingDesc`, `RatingAsc`, `RecentlyAdded` | Default is `NameAsc`; rating sorts put null ratings last. |

Filter semantics are OR within a repeated filter type and AND across different filter types. Missing optional metadata never satisfies an active filter. Multi-select wire format is repeated keys only (`?platformIds=a&platformIds=b`); comma-separated values are not parsed and return `400` when invalid for the parameter type.

Frontend: the protected `/library` route renders the unified card list, loading/error retry states, empty-library and no-results states, search and all approved filters, sorting, and Clear filters. Platform and Genre names are resolved from the existing `/platforms` and `/genres` APIs. Edit actions navigate to the existing management lists (`/video-games` or `/board-games`); Feature 006 adds no deep-link edit routes and no mutation endpoints under `/library`.

## Random Picker (Feature 007)

`POST /random-picker/pick` is an authenticated endpoint that returns one Random Picker outcome for the caller's private Library. The API accepts no user, owner, or library ID; ownership is always derived from the validated Supabase `sub`. Reads do not create a Library row.

Request body fields include `mode`, `shownLibraryEntryIds`, and the filters available for the selected mode. Modes and filters:

| Mode | Candidate pool | Filters |
| --- | --- | --- |
| `VideoGames` | Owned VideoGames only | `platformIds`, `genreIds`, `gameStatuses` |
| `BoardGames` | Owned BoardGames only | `playerCount`, `availableDuration`, `interactionTypes` |
| `All` | Owned VideoGames and BoardGames | `ratingMin` |

Response states are `SUCCESS` with a result, `NO_CANDIDATES` with `result: null`, or `ALL_ALREADY_SHOWN` with `result: null`. The result includes both `libraryEntryId` for temporary shown-history exclusion and `gameId` for navigation to the existing management pages. It does not include `createdAt`.

The protected `/random-picker` Angular route keeps shown history only in component memory. Pick and Another send the accumulated `shownLibraryEntryIds`; changing filters or mode clears the displayed result but not shown history; Reset shown history explicitly clears it. Leaving or recreating the page starts a fresh volatile picker session. No `RandomPickerSession`, picker history table, localStorage/sessionStorage persistence, scoring, weighting, AI, or recommendation infrastructure is used.

## Backend

Apply migrations (direct connection):

```
dotnet ef database update --project src/backend/GameLibrary.Core --startup-project src/backend/GameLibrary.Api
```

Build:

```
dotnet build src/backend/GameLibrary.sln
```

Run:

```
dotnet run --project src/backend/GameLibrary.Api
```

The API listens on `http://localhost:5218` (fixed dev port; override in `src/backend/GameLibrary.Api/Properties/launchSettings.json` if a conflict occurs).

Liveness check:

```
GET http://localhost:5218/health
```

Returns HTTP 200 with a small JSON body (e.g. `{"status":"ok","timestamp":"..."}`). No authentication; no database dependency.

## Frontend

Run:

```
cd src/frontend
ng serve
```

Open `http://localhost:4200`. The home route is authenticated: it redirects to `/login` when signed out and otherwise shows the signed-in identity and the result of `GET /auth/me` (dev `apiBaseUrl` is `http://localhost:5218` via `src/environments/environment.development.ts`). "Library", "Random Picker", "Platforms", "Video games", and "Board games" links on home open the protected `/library` (Feature 006), `/random-picker` (Feature 007), `/platforms` (Feature 003), `/video-games` (Feature 004), and `/board-games` (Feature 005) features. `/login` and `/register` are guest-only; `/health` renders the Feature 001 health-check view anonymously.

Production build (also verifies PWA artifacts — service worker configuration `ngsw.json` and web app manifest `manifest.webmanifest` in the `dist/` output):

```
cd src/frontend
ng build
```

## Tests and Checks

Backend tests (integration tests require `ConnectionStrings:Test` pointing at a real PostgreSQL database; unit tests need no database):

```
dotnet test tests/backend/GameLibrary.Core.Tests
dotnet test tests/backend/GameLibrary.IntegrationTests
```

Frontend tests:

```
cd src/frontend
ng test --watch=false
```

Frontend lint:

```
cd src/frontend
ng lint
```

Backend build:

```
dotnet build src/backend/GameLibrary.sln
```

Frontend production build:

```
cd src/frontend
ng build
```

## Notes

- The initial migration is intentionally empty: it establishes the EF migration infrastructure and verifies connectivity, not a domain artifact. The `AddPlatformManagement` migration (Feature 003) creates the `libraries` and `platforms` tables with the unique `user_id` index, the unique `(library_id, name_normalized)` index, and the `platforms.library_id → libraries.id` foreign key (`ON DELETE RESTRICT`). The `AddVideoGameManagement` migration (Feature 004) adds `games`, `library_entries`, `genres`, `game_genres`, and `game_platforms`. The `AddBoardGameManagement` migration (Feature 005) adds the four BoardGame columns and their CHECK constraints to the existing `games` table.
- Backend unit tests (`tests/backend/GameLibrary.Core.Tests`) cover the domain/application rules (`VideoGameRules`, `BoardGameRules`, `LibraryFilterRules`, and `RandomPickerRules`). Platform, VideoGame, BoardGame, Library, and Random Picker integration tests run against real PostgreSQL via `ConnectionStrings:Test` (in the current environment, the remote Supabase project's `postgres` database) and include two-user isolation, duplicate/lifecycle behavior, acquisition transitions, cross-type safety, relational CHECK backstops, literal search semantics, library filter/sort behavior, Random Picker eligibility/result states/shown-history exclusion, and schema assertions. `DatabaseMigrationTests` asserts the `public` schema contains exactly `__EFMigrationsHistory`, `libraries`, `platforms`, `games`, `library_entries`, `genres`, `game_genres`, and `game_platforms` (order-independent).
- Angular's service worker is active in production builds only; local `ng serve` verification of the PWA relies on `ng build` output.
- No secrets are committed. Local secret-bearing files (`appsettings.Development.json`, `.env`, `.env.*`) are gitignored; committed configuration contains placeholders only.
- Authentication adds `@supabase/supabase-js` and `Microsoft.AspNetCore.Authentication.JwtBearer`; the Angular initial-bundle budget warning was raised from 500 kB to 700 kB to accommodate `supabase-js` (the error budget stays 1 MB).
