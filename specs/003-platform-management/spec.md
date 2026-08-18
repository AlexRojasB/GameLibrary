# 003 — Platform Management

## Status

`Draft`

## Objective

Implement the first real business/domain feature of Game Library: management of a
user's personal video-game platforms.

After this feature is implemented, an authenticated user can:

1. View their Platforms.
2. Create a Platform.
3. Rename a Platform.
4. Delete an unused Platform.
5. Be prevented from deleting a Platform associated with one or more VideoGame
   LibraryEntries.
6. Never see or modify another user's Platforms.

This feature establishes the first persistent user-owned domain data: the
user's Library and the Platforms that belong to it.

It does NOT implement VideoGames, BoardGames, LibraryEntries, or any other game
data.

## Context

The approved Product, Domain, and Architecture Specifications are authoritative.
This specification is the third feature in the approved delivery order:

1. Project foundation and local development. (implemented)
2. Authentication. (implemented)
3. Platform management. (this feature)
4. VideoGame management.
5. BoardGame management.
6. Library browse/search/filter/sort.
7. Random Picker.
8. PWA polish and MVP end-to-end verification.

### Actual foundation produced by Features 001 and 002

This specification builds on the actual repository state:

- Backend: `src/backend/GameLibrary.sln` with exactly two application projects —
  `GameLibrary.Api` (ASP.NET Core Web API, .NET 10) and `GameLibrary.Core`
  (EF Core 10.0.11, Npgsql 10.0.3). `GameLibrary.Api` references
  `GameLibrary.Core`.
- `GameLibrary.Api/Program.cs` registers controllers, problem details, the
  `GameLibraryDbContext` with Npgsql (from `ConnectionStrings:Default`), a
  "Frontend" CORS policy from `AllowedOrigins`, JWT bearer authentication
  (OpenID Connect metadata/JWKS discovery, audience `authenticated`,
  `MapInboundClaims = false`, `ValidAlgorithms = [ES256, RS256]`), and
  authorization. Middleware order is `UseExceptionHandler` → `UseCors` →
  `UseAuthentication` → `UseAuthorization` → `MapControllers`. The centralized
  exception handler returns a consistent 500 JSON problem-details response.
- `GameLibrary.Api/Auth/ClaimsPrincipalExtensions.cs` provides
  `GetSupabaseUserId()` which reads the `sub` claim from the validated principal
  (returns `null` when unauthenticated or when `sub` is missing).
- `GET /auth/me` (`AuthController`) is the protected verification endpoint.
  `GET /health` (`HealthController`) remains anonymous and must remain so.
- `GameLibrary.Core/Data/GameLibraryDbContext.cs` currently has no `DbSet` and
  no `OnModelCreating` configuration. The only migration is the intentionally
  empty `InitialCreate` (baseline only).
- Tests: `tests/backend/GameLibrary.IntegrationTests/` uses xUnit +
  `Microsoft.AspNetCore.Mvc.Testing`. `AuthTestFactory` post-configures the
  bearer options to validate against a deterministic symmetric test key so
  authorization-behavior tests run offline; `TestTokens.CreateToken(sub)` mints
  tokens for arbitrary `sub` values. `DatabaseMigrationTests` currently asserts
  that applying migrations to real PostgreSQL produces exactly
  `["__EFMigrationsHistory"]` (this assertion must change in this feature; see
  Testing and Risks / Notes). There is no backend unit-test project.
- Frontend: Angular 22 (`src/frontend/`, standalone components, SCSS, PWA via
  `@angular/service-worker`), `@supabase/supabase-js`, `AuthService` (signal
  state, session restoration, normalized errors), route guards (`authGuard`,
  `guestGuard`), the API token interceptor (`apiTokenInterceptor`), and the
  authenticated home placeholder at `''` (`features/auth/home/`). Routes:
  `''` (home, `authGuard`), `login`/`register` (guest-only), `health`
  (anonymous). Environment files expose `apiBaseUrl`
  (`http://localhost:5218` in development) plus `supabaseUrl`/`supabaseKey`.
  Frontend tests run via Vitest through `@angular/build:unit-test` (`ng test`).
  `features/platforms/` exists only as an empty `.gitkeep` skeleton folder.
- Configuration: committed files contain only non-secret values or placeholders;
  `appsettings.Development.json` is gitignored;
  `appsettings.Development.example.json` is the committed template.
- The current development environment uses the remote Supabase project
  `iramzxpjbnldhebykzhx` for both PostgreSQL and Auth. No Docker is required.

## In Scope

- The first persistent user-owned domain data: a minimal `Libraries` table and a
  `Platforms` table, created through an EF Core migration.
- `Platform` and `Library` domain entities in `GameLibrary.Core`.
- A Core application service that owns all Platform application/domain rules
  (ownership scoping, name validation, duplicate detection, create/update/delete)
  and is the authoritative backend enforcement layer.
- A `PlatformsController` in `GameLibrary.Api` exposing the four CRUD endpoints
  and their request/response contracts, scoped to the authenticated Supabase
  user ID from the validated JWT `sub`.
- The "Platform in use cannot be deleted" rule's structural mechanism: a
  documented future foreign key with `ON DELETE RESTRICT` from the future
  `game_platforms` join table. Feature 003 implements no VideoGame data and adds
  no speculative infrastructure for the rule.
- An authenticated Angular Platform management UI:
  - List page (loading, empty, error, list states).
  - Create flow.
  - Rename flow.
  - Delete flow with confirmation.
  - Validation errors and API error handling.
- A minimal "Platforms" navigation entry from the authenticated home view (and a
  way back), without designing the final application shell.
- Backend integration tests against real PostgreSQL (including two-user
  isolation) and frontend tests for meaningful behavior.
- README updates documenting the new feature (usage, endpoints, test behavior).

## Out of Scope

Feature 003 must NOT implement or introduce:

- `VideoGame`, `BoardGame`, `Game`, `LibraryEntry`, `Genre`, the
  `game_platforms` join table, or any game data or tables.
- Game/LibraryEntry CRUD, search, filters, sorting for the main library,
  Random Picker.
- A `User` business entity, application user table, profiles, display names, or
  linking a Supabase account to an application `User` record beyond the
  `Libraries.user_id` ownership column.
- Any mechanism that produces the "in use" delete rejection today (no fake
  join tables, no speculative "referencing entries" queries).
- RLS.
- Direct Angular access to application database tables.
- New backend projects (including `GameLibrary.Core.Tests`; see Testing).
- NgRx, MediatR, CQRS, generic repositories, a Unit of Work abstraction, event
  sourcing, or shared-kernel abstractions.
- Platform icons, images, manufacturer, release date, colors, slugs, external
  IDs, or a platform type hierarchy.
- A global/canonical platform catalog, admin platform management, or platform
  integrations.
- Soft-delete.
- Pagination.
- Production deployment configuration.
- End-to-end (E2E) test suites (manual acceptance verification is specified
  instead).

## Domain Decisions

### Ownership / Library Representation

The approved Domain Specification states:

- Each User has exactly one personal Library.
- A Library belongs to exactly one User.
- A Library is private.
- Platform belongs to a Library.
- A Platform may be associated with multiple VideoGame LibraryEntries (future).

There is no persistent `User` or `Library` yet, and no application user table is
approved. The simplest implementation that preserves the approved Domain model
without inventing User/Profile infrastructure is:

**Introduce a minimal persistent `libraries` table now.**

Why this, and not storing `user_id` directly on `Platform`:

- The Domain model is explicit: Platforms belong to a Library, and each User has
  exactly one Library. Collapsing the Library into a `user_id` column on
  `Platform` would silently drop that approved concept and leave the "one
  Library per user" invariant unenforceable.
- The future `LibraryEntry` (VideoGame/BoardGame feature) must belong to a
  Library. Introducing the Library row now gives that feature its natural
  anchor and avoids migrating `platform.user_id` → `platform.library_id` later.
- The "exactly one Library per user" invariant is enforceable at the database
  with a unique constraint on `libraries.user_id`.

The Library is created **lazily on first Platform creation** for a user: the
first `POST /platforms` for a user creates that user's Library row and the
Platform. The database enforces **at most one `libraries` row per user** via the
unique index on `user_id`. Read operations for a user with no Library simply
return an empty list (no row is created on read). A user with no platforms
therefore has no `libraries` row yet; this is consistent with the approved model
(the relationship exists once the user owns library data) and adds no write
traffic to reads.

The ownership column `libraries.user_id` stores the authenticated Supabase `sub`
as an opaque string (`text`), never parsed or interpreted by the backend. It is
populated only from the validated JWT. The API never accepts a user ID or owner
ID from a request body or query string for authorization.

When a second consumer of the Library (for example the VideoGame feature) needs
the same get-or-create behavior, the ensure-library helper becomes a shared Core
mechanism. It is not extracted now (no second consumer exists yet).

### Platform Model

The MVP Platform carries only a configurable name plus persistence identity:

| Property | Type | Notes |
| --- | --- | --- |
| `Id` | `Guid` | Primary key, generated by the application on create. |
| `LibraryId` | `Guid` | Foreign key to `Libraries.Id`. Ownership is always derived from the authenticated user through this Library. |
| `Name` | `string` | Required, trimmed, max 100 characters. The only user-editable field. |
| `NameNormalized` | `string` | Stored generated column `lower(name)`, used only for case-insensitive uniqueness. Not exposed by the API. |

No other fields are added: no manufacturer, release date, icon, color, slug,
external IDs, or platform type hierarchy. BoardGames do not use Platforms (this
feature adds nothing for BoardGames).

### Naming Rules

Decision: **case-insensitive duplicate Platform names are not allowed within a
single user's Library.** The same name is allowed in different users' Libraries
(no global uniqueness, matching the Domain rule "Game name is not unique" in
spirit and avoiding any cross-user coupling).

Rules applied authoritatively in `GameLibrary.Core`:

- Required: the name must be non-empty after trimming leading/trailing
  whitespace.
- Whitespace trimming: leading and trailing whitespace is removed before
  storage and before duplicate comparison.
- Maximum length: 100 characters (enforced both in Core application logic and
  as a `varchar(100)` column). The maximum length applies to the **trimmed**
  name: validation runs trim → non-empty → trimmed-length ≤ 100, in that order.
- Duplicate detection: two names are duplicates when their trimmed lowercase
  forms are equal. The uniqueness constraint is
  `(LibraryId, NameNormalized)` where `NameNormalized = lower(name)`.
- A rename is a duplicate when the new name equals a *different* Platform's name
  in the same Library.
- The same name is always allowed in another user's Library.

This is the simplest user-friendly behavior for a personal catalog: two entries
named "Steam" in one catalog are a user error (rejected), while "Steam" in one
user's library and "Steam" in another's are independent records (allowed).

### Delete Rules

The approved rule is:

> A Platform associated with one or more VideoGame LibraryEntries cannot be
> deleted until those associations are removed.

Feature 003 must handle this without creating fake VideoGame tables or
speculative infrastructure, and must leave the rule enforceable once VideoGames
arrive.

Decision:

- **Today (Feature 003):** no VideoGame/LibraryEntry tables exist, so no
  Platform can be associated with any VideoGame LibraryEntry. `DELETE
  /platforms/{id}` therefore deletes the Platform after verifying ownership.
  Every Platform that exists in the caller's Library is deletable.
- **Structural mechanism for the future:** the VideoGame feature will create the
  `game_platforms` join table with a foreign key
  `game_platforms.platform_id → platforms.id` declared with
  **`ON DELETE RESTRICT`**. PostgreSQL then rejects deletion of any Platform
  referenced by a VideoGame LibraryEntry automatically, enforcing the approved
  invariant at the database. That future feature surfaces the resulting
  rejection (or performs an explicit in-use check) as HTTP `409 Conflict`.
- **No speculative code now:** Feature 003 adds no "in-use check" query, no
  placeholder association table, and no abstraction that fakes the rule. The
  delete path is a plain, ownership-scoped delete. This keeps the feature honest
  and the code simple; the rule emerges naturally through the future FK, which
  is the standard relational enforcement of "cannot delete a referenced row."
- **Frontend posture:** the delete flow still handles a `409` response
  gracefully ("This platform is in use and cannot be deleted.") so the future
  VideoGame feature requires no frontend change. This is error-handling code,
  not speculative domain infrastructure.

## Technical Requirements

### Backend

Structure in `GameLibrary.Core` (domain/application rules + persistence):

```
GameLibrary.Core/
├── Libraries/
│   └── Library.cs
├── Platforms/
│   ├── Platform.cs
│   ├── PlatformService.cs
│   └── PlatformExceptions.cs
└── Data/
    ├── GameLibraryDbContext.cs        (extended)
    └── Migrations/                    (new migration added)
```

- `Library` and `Platform` are plain EF Core entities (see Database / EF Core).
- `PlatformService` is a concrete application service (no interface, no generic
  repository, no Unit of Work). It depends only on `GameLibraryDbContext` and is
  registered with `AddScoped<PlatformService>()` in `Program.cs`.
- `PlatformService` is the authoritative backend enforcement layer. It exposes
  methods that take the authenticated `userId` (never a client-supplied ID):

  - `Task<IReadOnlyList<Platform>> ListAsync(string userId, CancellationToken ct)`
    — returns the caller's Platforms ordered by name (case-insensitive
    ascending); empty when the caller has no Library yet.
  - `Task<Platform> CreateAsync(string userId, string name, CancellationToken ct)`
    — validates the name **before** any persistence work, then ensures the user's
    Library exists (get-or-create), rejects case-insensitive duplicates within
    the Library, inserts, saves, and returns the created Platform.
  - `Task<Platform> UpdateAsync(string userId, Guid platformId, string name, CancellationToken ct)`
    — loads the Platform scoped to the user's Library, validates the name,
    rejects duplicates excluding the Platform being renamed, saves, and returns
    the updated Platform.
  - `Task DeleteAsync(string userId, Guid platformId, CancellationToken ct)`
    — loads the Platform scoped to the user's Library and deletes it. No in-use
    check exists in Feature 003 (see Delete Rules).

- All loads/updates/deletes are scoped by the user's `LibraryId`; a Platform
  that does not exist in the caller's Library is indistinguishable from one that
  does not exist at all (same exception, same 404).
- Name validation (trim, required, max length) lives in `PlatformService` so the
  backend is authoritative and the rule has a single home.

Structure in `GameLibrary.Api` (HTTP boundary only):

```
GameLibrary.Api/
├── Controllers/
│   └── PlatformsController.cs
└── Platforms/
    └── PlatformContracts.cs
```

- `PlatformsController` is `[Authorize]`, derives the user from
  `User.GetSupabaseUserId()`, returns `401` when the ID is `null` (mirroring
  `AuthController`), and maps service failures to HTTP responses (see Validation
  and Errors). It never accepts a user/owner ID from the body or query string.
- `PlatformContracts.cs` holds the request/response records (see API). EF
  entities are never exposed by the API.
- No changes to authentication configuration, CORS, middleware order, or the
  centralized 500 handler are required. The only `Program.cs` change is
  registering `PlatformService`.

### API

Routes follow the existing no-prefix convention (`auth`, `health`): base route
`platforms`. All endpoints require authentication. Request/response are JSON.

| Method | Route | Auth | Success | Errors |
| --- | --- | --- | --- | --- |
| `GET` | `/platforms` | required | `200` with a JSON array of `PlatformResponse` ordered by name (case-insensitive ascending) | `401` |
| `POST` | `/platforms` | required | `201` with the created `PlatformResponse` (no `Location` header; there is no single-resource `GET /platforms/{id}` to point it at) | `400`, `401`, `409` |
| `PUT` | `/platforms/{id}` | required | `200` with the updated `PlatformResponse` | `400`, `401`, `404`, `409` |
| `DELETE` | `/platforms/{id}` | required | `204` (no body) | `401`, `404` (in-use `409` becomes reachable only when the VideoGame feature adds the join table) |

**PUT vs PATCH decision: use `PUT`.** The resource has a single mutable field
(`name`), so the full-replacement semantics of `PUT` are the simplest correct
fit and are idempotent. `PATCH` adds partial-update semantics that nothing
requires.

There is **no single-resource `GET /platforms/{id}`** endpoint: the list is the
only read the UI needs. Do not add it.

Contracts (`PlatformContracts.cs`):

- Request (both create and update): `{ "name": "<string>" }`
  (records `CreatePlatformRequest(string Name)` and `UpdatePlatformRequest(string Name)`).
- Response: `{ "id": "<guid>", "name": "<string>" }`
  (record `PlatformResponse(Guid Id, string Name)`). No ownership or timestamp
  data is exposed.

The list response is a plain JSON array of `PlatformResponse`. No wrapper
object, no pagination.

### Database / EF Core

EF Core migrations remain the sole authority for the application schema. No
Supabase dashboard edits, no second migration workflow, no RLS.

Add a migration named `AddPlatformManagement` (the second migration; the empty
`InitialCreate` is retained). It creates two tables.

Table `libraries`:

| Column | Type | Constraints |
| --- | --- | --- |
| `id` | `uuid` | Primary key. |
| `user_id` | `text` | Not null. Unique index on `user_id` (enforces exactly one Library per user). |

Table `platforms`:

| Column | Type | Constraints |
| --- | --- | --- |
| `id` | `uuid` | Primary key. |
| `library_id` | `uuid` | Not null. Foreign key → `libraries.id`, `ON DELETE RESTRICT` (never silently cascade user data). Indexed. |
| `name` | `varchar(100)` | Not null. |
| `name_normalized` | `text` | Not null. Stored generated column `lower(name)`. Unique index on `(library_id, name_normalized)` (enforces case-insensitive uniqueness within a Library). |

Mapping notes for `GameLibraryDbContext.OnModelCreating` (configure inline in
`OnModelCreating`; do not introduce `IEntityTypeConfiguration` classes until a
second real consumer justifies them):

- `Libraries`/`Platforms` `DbSet`s on the context.
- Explicit snake_case table and column names as above (PostgreSQL convention;
  future features follow the same naming).
- `Platform.Name`: `HasMaxLength(100)`.
- `Platform.NameNormalized`:
  `HasComputedColumnSql("lower(\"name\")", stored: true)` (value generated by the
  database; the application never writes it).
- Unique index on `Libraries.UserId`.
- Unique index on `Platforms (LibraryId, NameNormalized)`.
- FK `Platform.LibraryId → Library.Id` with `DeleteBehavior.Restrict`.
- `Library` has a collection of `Platforms`; `Platform` has a required `Library`
  navigation.
- `DateTimeOffset` properties map to `timestamptz` via Npgsql defaults.
- The application sets `Id` (`Guid.NewGuid()`) on insert. No creation-timestamp
  columns exist on `libraries` or `platforms`.

Concurrency handling (documented so the implementer does not invent a locking
scheme):

- **Library get-or-create race:** two concurrent first-creates for the same user
  could both try to insert a Library. The unique index on `libraries.user_id`
  rejects one. The service catches the unique-violation `DbUpdateException`,
  re-queries the existing Library, and continues with it.
- **Duplicate-name race:** a concurrent insert that violates the
  `(library_id, name_normalized)` unique index surfaces as a
  `DbUpdateException` and is mapped to `409` exactly like the pre-check path.

The migration is committed to source control.

### Frontend

Build the feature under `src/app/features/platforms/` (replacing the `.gitkeep`
with real code). No NgRx; use services, signals, and local/component state.

Files:

```
src/app/features/platforms/
├── platform.ts                       # interface Platform { id: string; name: string }
├── platforms.service.ts              # HTTP client for the Platforms API
├── platforms-list/
│   ├── platforms-list.ts             # list page component
│   ├── platforms-list.html
│   ├── platforms-list.scss
│   └── platforms-list.spec.ts
└── platform-form/
    ├── platform-form.ts              # create/edit name form component
    ├── platform-form.html
    ├── platform-form.scss
    └── platform-form.spec.ts
```

- `platforms.service.ts` (`@Injectable({ providedIn: 'root' })`, feature-local)
  wraps the four HTTP calls against `${environment.apiBaseUrl}/platforms` using
  the injected `HttpClient`, returning the `Platform`/`Platform[]` types. The
  existing API token interceptor attaches the bearer token automatically; the
  service does nothing special for auth.
- `platforms-list`:
  - On init, calls the list; exposes signals for `loading`, `platforms`, and
    `error`.
  - Loading state: while the first request is in flight, shows a loading
    indicator.
  - Error state: on a non-401 failure, shows an error message and a "Try again"
    action that reloads the list.
  - Empty state: when the list is empty and not loading, shows "No platforms
    yet" plus a prominent create action.
  - List state: renders the Platforms in the API order; each row has an Edit and
    a Delete action.
  - Create: shows the `platform-form` in create mode; on submit calls the
    service; on success reloads the list; on validation/duplicate error keeps
    the form open and shows the message.
  - Edit: selecting Edit loads the Platform's current name into the form in edit
    mode; on submit calls `PUT`; on success reloads the list (or updates the
    row locally); on `404` shows "This platform no longer exists." and reloads;
    on `409` shows the duplicate message.
  - Delete: confirms with the platform name (native `confirm()` is acceptable);
    on `DELETE` success reloads the list; on `404` shows "This platform no
    longer exists." and reloads; on `409` shows "This platform is in use and
    cannot be deleted." (reachable only after the VideoGame feature, but
    handled now).
  - A `401` from any platform API call is handled like the home view: clear the
    local session (`auth.clearLocalSession()`) and navigate to `/login` (session
    expired behavior).
- `platform-form`:
  - One `name` field using `@angular/forms` (`FormControl` with
    `Validators.required` and `Validators.maxLength(100)`), trimmed on submit
    and additionally rejected if the trimmed value is empty.
  - Mirrors backend validation for immediate UX feedback; the backend remains
    authoritative.
  - Emits submit events with the trimmed name; the parent performs the HTTP call
    so the form stays reusable for create and edit.
  - Displays inline error text for client-side validation and for a server
    `409`/`400` message supplied by the parent.

Navigation:

- Add a route `{ path: 'platforms', component: PlatformsList, canActivate:
  [authGuard] }` to `app.routes.ts`.
- Add a minimal "Platforms" `routerLink` on the authenticated home view
  (`features/auth/home/home.html`).
- Add a minimal "Back to home" `routerLink` on the platforms page.
- Do not build a full application shell or shared navigation component.

### Navigation

The authenticated UI currently has only the home placeholder. Feature 003 adds
exactly two links ("Platforms" from home, "Home" from the platforms page) plus
the new protected `/platforms` route. No shell layout, no shared nav component,
and no menu design is introduced.

### Validation and Errors

Predictable behavior, consistent problem-details responses. The project already
registers `AddProblemDetails()` and has a centralized 500 handler; controllers
return `Problem(...)` results for expected failures.

| Condition | HTTP | Problem details |
| --- | --- | --- |
| Unauthenticated request | `401` | Produced by the JWT bearer handler (with `WWW-Authenticate` challenge). |
| Authenticated principal without a usable `sub` | `401` | Controller returns `Unauthorized()` (mirrors `AuthController`). |
| Name missing / empty / whitespace-only after trim | `400` | Title "Invalid platform name", detail "Platform name is required." |
| Name longer than 100 characters | `400` | Title "Invalid platform name", detail "Platform name must be at most 100 characters." |
| Case-insensitive duplicate within the same Library (create or rename) | `409` | Title "Duplicate platform name", detail "A platform with this name already exists." |
| Platform does not exist in the caller's Library (update/delete of own or another user's Platform) | `404` | Title "Platform not found". Cross-user identifiers are indistinguishable from nonexistent ones to avoid resource enumeration. |
| Delete blocked because the Platform is in use | `409` (reachable only after the VideoGame feature introduces the join table) | Title "Platform in use", detail "This platform is in use and cannot be deleted." |
| Unexpected exception | `500` | Existing centralized handler (no stack traces, includes `requestId`). |

- Expected failures are raised as typed exceptions in `GameLibrary.Core`
  (`PlatformExceptions.cs`): `InvalidPlatformNameException`, `PlatformNameConflictException`,
  `PlatformNotFoundException`. The controller maps each to its problem response.
  Exceptions are a pragmatic single-consumer mapping mechanism; no result-type
  framework is introduced.
- The frontend maps these to user-visible messages (duplicate, not found, in
  use, required, too long, and a generic network/server failure message). It
  never displays raw problem-details internals or stack traces.

### Testing

Proportionate tests. No new test framework.

**Backend unit tests — not introduced in this feature.** Rationale: the
meaningful Platform rules (ownership scoping, duplicate detection, library
get-or-create, persistence) are inherently database/EF-coupled and are verified
against real PostgreSQL by the integration tests. The only framework-independent
candidate is the trivial name-length check, which is not worth a project. The
`GameLibrary.Core.Tests` project remains deferred until a feature introduces
meaningful pure domain/application logic that can be tested without a database
(for example AcquisitionStatus transition rules in the VideoGame feature, or
Random Picker eligibility/filter logic).

**Backend integration tests** — added to the existing
`tests/backend/GameLibrary.IntegrationTests` project. Use a platform-specific
factory, for example `PlatformTestFactory : WebApplicationFactory<Program>`,
that:

- Post-configures the bearer `JwtBearerOptions` with the deterministic symmetric
  test key, exactly like `AuthTestFactory` (reuse the existing pattern; mint
  tokens via `TestTokens.CreateToken(sub)` for distinct test users).
- Overrides the database connection string so the API-under-test hits the real
  PostgreSQL test database: `builder.UseSetting("ConnectionStrings:Default",
  Environment.GetEnvironmentVariable("ConnectionStrings__Test"))`.
- Applies migrations idempotently before tests run (a `MigrateAsync` call using
  the test connection, mirroring `DatabaseMigrationTests`).

Required cases (all against real PostgreSQL; `ConnectionStrings:Test` is
required as in the existing project):

- Create: `POST /platforms` with a valid name returns `201` with
  the created `PlatformResponse` (no `Location` header); the name is stored
  trimmed.
- List: `GET /platforms` returns only the calling user's Platforms, ordered by
  name.
- Two-user isolation: user A and user B each create Platforms; A's list contains
  only A's Platforms and B's list only B's; `PUT`/`DELETE` of A's Platform id
  while authenticating as B returns `404`; the same name in both Libraries is
  allowed.
- Rename: `PUT /platforms/{id}` returns `200` with the new name reflected in
  `GET /platforms`.
- Delete: `DELETE /platforms/{id}` returns `204`, and the Platform no longer
  appears in the list; deleting the same id again returns `404`.
- Validation: empty / whitespace-only / missing `name` → `400`; a 101-character
  name → `400`.
- Duplicates: case-insensitive duplicate name on create → `409`; rename to a
  duplicate of another Platform in the same Library → `409`; renaming a Platform
  to its own current name (or a differently-cased version of it) succeeds.
- Library lifecycle: a user's first create results in exactly one `libraries`
  row for that user (assert via a direct `GameLibraryDbContext` query on the
  test connection), and a second create reuses it (still exactly one row).
- Migration/schema: update `DatabaseMigrationTests` so that after `MigrateAsync`
  the `public` schema contains exactly `__EFMigrationsHistory`, `libraries`,
  and `platforms`, compared as an order-independent set (this test currently
  asserts only `__EFMigrationsHistory` and MUST be updated). Optionally assert
  the unique indexes exist.
- Delete protection: Feature 003 cannot produce an in-use rejection because no
  join table exists; do not fabricate one. The rule's enforcement is verified
  when the VideoGame feature introduces the `game_platforms` FK with
  `ON DELETE RESTRICT`. Record this explicitly in the feature report.

Existing tests (`HealthEndpointTests`, `AuthEndpointTests`,
`JwtValidationConfigurationTests`) must continue to pass unchanged.

**Frontend tests** — Vitest via `ng test`. Mock `HttpClient`
(`provideHttpClientTesting`) and use the existing `SUPABASE_CLIENT` mock where
needed. Meaningful cases at minimum:

- `platforms.service`: `list`, `create`, `update`, `delete` issue the correct
  method/URL and map responses (mock `HttpClient`).
- `platforms-list`:
  - Loading state while the list request is pending.
  - Empty state ("No platforms yet").
  - List rendering of returned Platforms.
  - Create success reloads the list and clears the form.
  - Create failure shows the server message (e.g., `409` duplicate) and keeps
    the form usable.
  - Edit success updates the row/list; `404` on edit shows the "no longer
    exists" message.
  - Delete confirmation; delete success reloads the list; `409` on delete shows
    the "in use" message.
  - Non-401 API failure shows the error state with a retry action.
  - `401` clears the local session and navigates to `/login`.
- `platform-form`: required and max-length validation, trim-on-submit, and
  whitespace-only rejection.

Do not over-test trivial markup.

**Manual acceptance verification** — register/sign in (per Feature 002), use the
UI to create/rename/delete Platforms, and confirm list and error behavior. See
Verification.

## Functional Requirements

`FR-001` — Authenticated list: `GET /platforms` is `[Authorize]` and returns the
calling user's Platforms as a JSON array of `PlatformResponse` ordered by name
(case-insensitive ascending). Unauthenticated → `401`.

`FR-002` — Create: `POST /platforms` creates a Platform in the calling user's
Library with the trimmed name and returns `201` + the created
`PlatformResponse` (no `Location` header). The first create for a user lazily
creates exactly one `libraries` row for that user, reused by subsequent creates.

`FR-003` — Name validation: names are required (non-empty after trim) and at
most 100 characters; leading/trailing whitespace is trimmed before storage and
comparison.

`FR-004` — Duplicate names: a case-insensitive duplicate name within the same
Library is rejected with `409` on create and on rename (excluding the Platform
being renamed). The same name in a different user's Library is allowed.

`FR-005` — Rename: `PUT /platforms/{id}` updates the Platform's name, applies
the same validation/duplicate rules, and returns `200` with the updated
`PlatformResponse`. A Platform not in the caller's Library → `404`.

`FR-006` — Delete: `DELETE /platforms/{id}` returns `204` and removes the
Platform from the caller's Library. A Platform not in the caller's Library →
`404`. No association check exists in Feature 003 because no VideoGame data
exists; the "in use" rejection is structurally enforced later by a
`game_platforms` FK with `ON DELETE RESTRICT`.

`FR-007` — User isolation: every Platform operation is scoped to the
authenticated user's Library derived from the validated JWT `sub`; the API
accepts no user/owner ID from requests; another user's Platform identifiers
behave as `404`.

`FR-008` — Persistence and migration: an EF Core migration (`AddPlatformManagement`)
creates the `libraries` and `platforms` tables with the specified columns,
unique indexes, and FK, and is committed to source control.

`FR-009` — Core authority: `PlatformService` in `GameLibrary.Core` implements
name validation, duplicate detection, ownership scoping, library get-or-create,
and create/update/delete; `GameLibrary.Api` contains only HTTP concerns and
contracts.

`FR-010` — Frontend list page: a protected `/platforms` route renders loading,
error (with retry), empty ("No platforms yet"), and list states.

`FR-011` — Frontend create flow: creating a Platform submits `POST`, reloads the
list on success, and surfaces validation/duplicate/API errors.

`FR-012` — Frontend rename flow: editing a Platform submits `PUT`, updates the
list on success, and surfaces validation/duplicate/not-found/API errors.

`FR-013` — Frontend delete flow: deleting confirms, submits `DELETE`, reloads
the list on success, and surfaces not-found/in-use/API errors.

`FR-014` — Frontend validation: the form mirrors the backend rules (required,
trim, max 100) for immediate feedback; the backend remains authoritative.

`FR-015` — Frontend session expiry: a `401` from any platform API call clears
the local session and navigates to `/login`.

`FR-016` — Navigation: a "Platforms" link on the home view and a "Home" link on
the platforms page connect the two authenticated views.

`FR-017` — Backend integration tests: the required real-PostgreSQL cases (create,
list, two-user isolation, rename, delete, validation, duplicates, library
lifecycle, migration/schema) are implemented and pass.

`FR-018` — Frontend tests: the required `platforms.service`, `platforms-list`,
and `platform-form` behaviors are tested and pass.

`FR-019` — Foundation preserved: `GET /health` stays anonymous, `GET /auth/me`
behavior is unchanged, and existing backend integration tests still pass.

## Non-Functional Requirements

`NFR-001` — User isolation: every user-owned query and mutation is scoped to the
authenticated user's Library derived from the validated Supabase `sub`; no
client-supplied user/owner ID is trusted; cross-user identifiers return `404`
to avoid resource enumeration; one user can never read or modify another user's
Platforms.

`NFR-002` — Simplicity: the feature adds exactly two database tables, two domain
entities, one application service, one controller, and one frontend feature
folder. No generic repositories, Unit of Work, MediatR, CQRS, event sourcing,
extra backend projects, NgRx, or shared-kernel abstractions are introduced. EF
Core is used directly.

`NFR-003` — Validation consistency: backend validation in `GameLibrary.Core` is
authoritative; the frontend mirrors rules only for immediate UX feedback and
never overrides the backend.

`NFR-004` — Maintainability: domain/application rules live in
`GameLibrary.Core`; HTTP/contract concerns live in `GameLibrary.Api`; frontend
feature code is local to `features/platforms/`; no speculative shared
abstractions are created before multiple real usages justify them.

`NFR-005` — Security: the validated JWT `sub` is the only trusted identity;
problem-details responses never expose stack traces or internal details;
committed configuration remains non-secret; no RLS is introduced; Angular never
accesses application database tables; no new secrets are committed.

`NFR-006` — Error consistency: all expected failures return predictable
problem-details responses with the documented status codes and messages; the
existing centralized 500 handling is retained.

## Acceptance Criteria

`AC-001` — Unauthenticated requests to any of `GET/POST/PUT/DELETE /platforms`
return `401`.

`AC-002` — An authenticated user creates a Platform with the name
`"  Steam  "`; `POST` returns `201` and the stored/returned name is `"Steam"`,
and `GET /platforms` lists exactly it.

`AC-003` — Two authenticated users A and B: after A creates Platforms and B
creates Platforms (including one with the same name as A's), `GET /platforms`
returns for A only A's Platforms and for B only B's; `PUT` and `DELETE` of A's
Platform id while authenticated as B return `404`.

`AC-004` — `POST`/`PUT` with an empty, whitespace-only, or missing `name`
returns `400`; `POST`/`PUT` with a name of 101 characters returns `400`.

`AC-005` — `POST` with a name that is a case-insensitive duplicate of an
existing Platform in the same Library returns `409`; the same name in another
user's Library succeeds. `PUT` renaming a Platform to a case-insensitive
duplicate of a different Platform in the same Library returns `409`, and
renaming it to its own current name (any casing) succeeds.

`AC-006` — `PUT /platforms/{id}` renames the Platform and returns `200` with the
new name; `GET /platforms` reflects it.

`AC-007` — `DELETE /platforms/{id}` returns `204`, the Platform no longer
appears in `GET /platforms`, and repeating the delete returns `404`.

`AC-008` — A user's first Platform create results in exactly one `libraries`
row for that user; a second create keeps it at exactly one (verified by direct
database query).

`AC-009` — The `AddPlatformManagement` migration applies to real PostgreSQL and
produces `libraries` and `platforms` with the specified columns, the unique
`user_id` index, the unique `(library_id, name_normalized)` index, and the FK;
the updated `DatabaseMigrationTests` passes and asserts the new tables.

`AC-010` — The Angular `/platforms` route is protected by `authGuard`: signed
out it redirects to `/login`; signed in it loads and displays the Platforms with
loading and empty states as appropriate.

`AC-011` — Creating and renaming a Platform through the UI updates the list on
success and shows user-visible validation/duplicate messages on failure.

`AC-012` — Deleting a Platform through the UI confirms, removes it from the list
on success, and shows user-visible messages for not-found/in-use/API failures.

`AC-013` — A `401` from a platform API call clears the local session and
redirects to `/login`.

`AC-014` — Backend build (`dotnet build src/backend/GameLibrary.sln`), backend
integration tests (`dotnet test tests/backend/GameLibrary.IntegrationTests`),
frontend tests (`ng test --watch=false`), and lint (`ng lint`) all pass.

`AC-015` — No out-of-scope artifacts are introduced: no VideoGame/BoardGame/
Game/LibraryEntry/Genre entities or tables, no `game_platforms` join table, no
RLS, no direct Angular database access, no new backend projects, no new
dependencies, no NgRx, and no speculative delete-protection infrastructure.

## Verification

For each acceptance criterion, an implementation agent verifies as follows.

`AC-001`:
- Run the API and issue `curl -i http://localhost:5218/platforms` with no
  `Authorization` header: status `401` with a `WWW-Authenticate: Bearer`
  challenge. The same holds for `POST`, `PUT`, and `DELETE` without a token.

`AC-002`:
- Using a valid Supabase access token,
  `curl -i -X POST -H "Authorization: Bearer <token>" -H "Content-Type:
  application/json" -d '{"name":"  Steam  "}' http://localhost:5218/platforms`.
  Expect `201` and body name `"Steam"` (no `Location` header). Then
  `GET /platforms` returns exactly one item with name `"Steam"`.

`AC-003`:
- Using access tokens for two distinct accounts, create Platforms for each
  (including the same name in both). `GET /platforms` for A returns only A's;
  for B only B's. Authenticating as B, `PUT /platforms/{aPlatformId}` and
  `DELETE /platforms/{aPlatformId}` each return `404`. This is also covered by
  the integration tests.

`AC-004`:
- `POST`/`PUT` with `{"name":""}`, `{"name":"   "}`, `{"name":null}`, a body
  without `name`, and a 101-character name each return `400` with problem
  details.

`AC-005`:
- With one Platform named "Steam" in user A's library, `POST {"name":"steam"}`
  as A returns `409`. As user B, `POST {"name":"Steam"}` returns `201`. As A,
  `PUT` another Platform to name `"  STEAM "` returns `409`; `PUT` the Steam
  Platform to `"steam"` (its own name, different casing) returns `200`.

`AC-006`:
- `PUT /platforms/{id}` with `{"name":"Nintendo Switch"}` returns `200` with
  name `"Nintendo Switch"`; `GET /platforms` reflects it.

`AC-007`:
- `DELETE /platforms/{id}` returns `204`; `GET /platforms` no longer contains
  it; a second `DELETE` returns `404`.

`AC-008`:
- Covered by the integration test: after two Platform creates for a fresh test
  user, query `SELECT COUNT(*) FROM libraries WHERE user_id = '<sub>'` on the
  test database and assert `1`.

`AC-009`:
- Set `ConnectionStrings:Test` to a real PostgreSQL database and run
  `dotnet test tests/backend/GameLibrary.IntegrationTests`. The migration test
  passes and asserts the schema contains `libraries` and `platforms`.
  Alternatively run `dotnet ef migrations list` to confirm the new migration is
  present and `dotnet ef database update` against a scratch database to confirm
  it applies.

`AC-010`:
- Run `ng serve`; signed out, visiting `http://localhost:4200/platforms`
  redirects to `/login`. Signed in, the page loads the Platforms, showing the
  loading indicator while the request is in flight and the empty state
  ("No platforms yet") when none exist.

`AC-011`:
- In the UI, create a Platform (list updates) and create/rename to a duplicate
  name (an error message appears and the list is unchanged). Empty and
  over-long names are blocked by the form.

`AC-012`:
- In the UI, delete a Platform after the confirmation prompt (it disappears
  from the list). Force a `404`/`409`/network failure (for example by deleting
  in two tabs, or stopping the API) and confirm a user-visible message appears.

`AC-013`:
- In the UI while signed in, revoke/expire the session (or simulate by
  intercepting the API response as `401`) and confirm the app clears auth state
  and navigates to `/login`. Covered by the frontend test as well.

`AC-014`:
- Run `dotnet build src/backend/GameLibrary.sln` (succeeds); run
  `dotnet test tests/backend/GameLibrary.IntegrationTests` with
  `ConnectionStrings:Test` configured (all pass); from `src/frontend/` run
  `ng test --watch=false` and `ng lint` (both pass). Existing
  `HealthEndpointTests`, `AuthEndpointTests`, and
  `JwtValidationConfigurationTests` still pass.

`AC-015`:
- Review the repository: the only new tables are `libraries` and `platforms`;
  no `game_platforms`, `games`, `library_entries`, or genre tables exist; no
  RLS policies or Supabase client database access exist; no new NuGet/npm
  dependencies were added; the backend still has exactly two application
  projects; the new frontend code lives under `features/platforms/` and uses
  signals/services only.

## Dependencies

- No new NuGet packages or npm packages are required. The approved stack (EF
  Core, Npgsql, ASP.NET Core, Angular, `@supabase/supabase-js`) already covers
  everything.
- Test tooling: existing xUnit + `Microsoft.AspNetCore.Mvc.Testing` for backend
  integration tests; existing Angular/Vitest tooling for frontend tests.
- Runtime prerequisites are unchanged from Features 001/002: a real PostgreSQL
  database for `ConnectionStrings:Default`/`ConnectionStrings:Test` (the current
  remote Supabase project `iramzxpjbnldhebykzhx`), and the existing JWT
  configuration. No Docker is required in the current environment.

Version-selection policy: unchanged (stable, supported versions recorded in
`README.md`).

## Risks / Notes

- **`DatabaseMigrationTests` must be updated.** Its current assertion expects
  exactly `["__EFMigrationsHistory"]` in `public`. Feature 003 changes the
  schema, so this test must assert the new table set (compared order-
  independently). Forgetting this will fail the build/test gate.
- **Delete protection is structurally future-work.** Feature 003 cannot produce
  the "in use" `409` because no VideoGame tables exist. The rule will be
  enforced by the future `game_platforms` FK with `ON DELETE RESTRICT`. The
  implementer must not fabricate a placeholder table or an in-use query now; the
  feature report should state that delete-protection verification is deferred to
  the VideoGame feature.
- **Library get-or-create race.** Two concurrent first-creates for the same user
  are resolved by the unique index on `libraries.user_id` plus a re-query on
  unique violation. Do not introduce locking or advisory locks.
- **Duplicate-name race.** The unique index on `(library_id, name_normalized)`
  is the authoritative backstop; the application pre-check is for a friendly
  `409`. Map the unique-violation `DbUpdateException` to `409` as well.
- **`user_id` stored as `text`.** The Supabase `sub` is treated as an opaque
  string and never parsed, converted, or validated as a UUID. Do not store it in
  a `uuid` column.
- **Case-insensitive uniqueness via a generated column.** `name_normalized` is a
  stored generated column (`lower(name)`); no `citext` extension is used, so no
  Supabase extension enablement is required. The application never writes this
  column.
- **PUT is chosen over PATCH.** The Platform has one mutable field; `PUT` with
  the full `{ "name" }` representation is idempotent and simplest.
- **No single-GET endpoint.** The list is the only read the UI needs; adding
  `GET /platforms/{id}` would be scope creep.
- **No backend unit-test project.** The meaningful rules are DB-coupled and are
  covered by real-PostgreSQL integration tests. A unit-test project is deferred
  until a feature introduces meaningful framework-independent domain logic.
- **No RLS / no client DB access.** User isolation is enforced solely in
  ASP.NET Core by scoping to the authenticated user's Library. RLS remains out
  of the MVP security model.
- **README update.** Document the new endpoints, the lazy Library creation, the
  duplicate/validation rules, and the fact that the delete-protection `409`
  becomes reachable only after the VideoGame feature.
- **Frontend budget.** Adding the platforms feature is small; if the Angular
  build reports a budget warning, record the adjustment in `README.md` as was
  done for `supabase-js` (no budget change is expected).

## Open Questions

None. The Library/ownership representation, Platform model, naming rules,
delete-protection mechanism, API shape (including the PUT-vs-PATCH decision and
the absence of a single-GET endpoint), error semantics, migration/schema, and
test approach are all defined here and by the approved Product, Domain, and
Architecture Specifications.