# 009 - Play Log

## Status

`Approved - Manual Review Amendment Pending Implementation`

The base Feature 009 implementation exists. This manual review amendment changes
approved behavior and is ready for targeted implementation, but the amended
behavior is not implemented or done until the implementation and verification work
is completed.

## Objective

Add a manual Play Log so an authenticated user can record that they actually
played an Owned game from their private Library.

After this feature is implemented, an authenticated user can:

1. Log a play for an Owned VideoGame.
2. Log a play for an Owned BoardGame.
3. See a newest-first list of logged plays.
4. Log the same game multiple times.
5. Preserve existing play logs when a game later changes away from Owned.
6. Rely on strict backend user isolation for every play-log read and write.
7. Choose the played date/time before creating a Play Log entry.
8. Optionally record duration in minutes for that play occurrence.
9. Edit an existing Play Log entry to correct PlayedAt or DurationMinutes.

This feature implements only manual user-confirmed play logging.

## Authority

Product Specification -> Domain Specification -> Architecture Specification ->
Feature Specification -> Implementation.

Feature 009 implements the Product and Domain Play Log amendments dated
2026-08-24 as resolved by specification review, with the manual review amendments
dated 2026-08-25 pending implementation.

## Current Repository Assessment

The repository has the base Feature 009 implementation. This amendment documents
required changes discovered during manual product review before Feature 010.

Observed current state:

- Backend has exactly two application projects: `GameLibrary.Api` and
  `GameLibrary.Core`.
- `GameLibraryDbContext` currently maps `libraries`, `platforms`, `games`,
  `library_entries`, `genres`, `game_genres`, `game_platforms`, and
  `play_log_entries`.
- Base Feature 009 added `PlayLogEntry`, `PlayLogService`, `PlayLogController`,
  `GET /play-log`, `POST /play-log`, `/play-log`, Library `Log play`, Random
  Picker `Log play`, and the `AddPlayLogEntries` EF migration.
- Base `play_log_entries` currently has `id`, `library_entry_id`, `played_at`,
  and `created_at`.
- Base `POST /play-log` currently accepts only `gameId` and assigns `playedAt`
  and `createdAt` from server now.
- Current Feature 009 implementation does not yet expose a targeted update
  endpoint or Play Log edit UI. This amendment adds edit requirements pending
  targeted implementation.
- `Game.Id` is the API resource identity exposed by VideoGame, BoardGame,
  Library, and Random Picker result DTOs.
- `LibraryEntry.Id` remains the domain identity for the user's relationship to a
  Game and is already exposed by Random Picker results for shown-history
  exclusion.
- Existing CRUD, Library, and Random Picker DTOs expose enough state for the
  frontend to know whether a game is currently `Owned`.
- The authenticated Angular shell currently exposes Home, Library, Pick, and
  Manage in desktop and mobile navigation. Manual review now requires adding Play
  Log to desktop primary navigation only. Feature 008's mobile bottom navigation
  remains exactly Home, Library, Pick, and Manage.
- Current EF ownership flows through `LibraryEntry.Library`; there is no existing
  second ownership path from gameplay/user-owned child rows directly to
  `libraries`.
- Random Picker visible history is frontend-only volatile state and must remain
  unrelated to Play Log persistence.

## In Scope

- A persisted `PlayLogEntry` domain/entity concept in `GameLibrary.Core`.
- The existing base EF Core migration adding one application table,
  `play_log_entries`.
- Backend API endpoints to create, list, and update play-log entries.
- Backend service behavior that scopes every operation to the authenticated
  Supabase `sub`.
- Manual logging for Owned VideoGames and Owned BoardGames using user-supplied
  `playedAt` and optional `durationMinutes`.
- Multiple log entries per game.
- Newest-first Play Log listing.
- A new EF Core migration adding `duration_minutes integer null` to
  `play_log_entries` with a CHECK constraint requiring positive values when
  present.
- Frontend `/play-log` route protected by the existing `authGuard`.
- Home discoverability for Play Log.
- A `Log play` action from useful game contexts where the frontend already has a
  `gameId`, at minimum Library cards and Random Picker successful results.
- A small Log Play dialog/sheet opened by `Log play`, with played date/time and
  optional duration fields.
- A Play Log card `Edit` action that opens a Play Shelf dialog/sheet for
  correcting PlayedAt and DurationMinutes only.
- Immediate frontend update of the edited Play Log card, including newest-first
  reordering when PlayedAt changes.
- Desktop authenticated top navigation includes Play Log with active state.
- Loading, empty, success, validation/error, and unauthorized states consistent
  with Feature 008 Play Shelf patterns.
- Backend unit/integration tests, frontend tests, and a small E2E addition for the
  core manual logging journey.
- README updates during implementation documenting the route, endpoints, schema,
  and verification commands.

## Out of Scope

Feature 009 must NOT introduce:

- Automatic logging when Random Picker returns a game.
- Persistent Random Picker history, `RandomPickerSession`, `PickerHistory`, or
  `PlaySession` persistence.
- Deleting Play Log entries after creation.
- Changing a PlayLogEntry's associated `LibraryEntryId`, `GameId`, Library,
  owner, or `CreatedAt` after creation.
- Start/end session timestamps, active timers, automatic playtime, pause/resume,
  automatic tracking, platform telemetry, or external platform playtime imports.
- Scores, outcomes, win/loss tracking, player attendance, party composition, or
  board-game session details.
- Platform-specific play sessions for VideoGames.
- Analytics dashboards, streaks, statistics, recommendations, AI, weighting, or
  Random Picker optimization based on play logs.
- Filtering/sorting Play Log beyond newest-first list ordering.
- Pagination unless implementation discovers a concrete need and the spec is
  amended first.
- Direct Angular access to application database tables.
- RLS, Supabase application-table policies, or a second authorization mechanism.
- New backend projects, NgRx, MediatR, CQRS frameworks, generic repositories,
  Unit of Work abstractions, Redis, GraphQL, Elasticsearch/OpenSearch, or other
  architecture changes.
- A fifth mobile bottom-navigation item.

## Domain Decisions

### Manual User Confirmation

A Play Log entry represents an actual user-confirmed play occurrence. The system
does not infer play activity from browsing, editing, filtering, or Random Picker
results.

Random Picker `SUCCESS`, `Another`, visible volatile history, and shown IDs never
create Play Log entries automatically. The user must explicitly choose `Log play`.

### Owned-Only Creation

Only a LibraryEntry with `AcquisitionStatus = Owned` can receive a new
PlayLogEntry.

This rule applies equally to VideoGames and BoardGames.

Wishlist and Interested entries can appear in the Library, but attempting to log
a play for them is rejected by backend validation. The frontend may hide or
disable `Log play` for non-Owned entries, but backend validation is authoritative.

### Identity Boundary

The create endpoint accepts `gameId`, not `libraryEntryId`, because existing game
cards and management routes use `Game.Id` as their public resource identity.

The backend resolves that `gameId` to the caller-owned LibraryEntry through the
authenticated user's Library. The request never includes user ID, owner ID or
library ID.

### Ownership Model

PlayLogEntry has exactly one ownership path:

`PlayLogEntry -> LibraryEntry -> Library`

PlayLogEntry does not store `LibraryId`, does not have a direct FK to
`libraries`, and does not introduce any other ownership field. This normalized
model eliminates the possibility of a PlayLogEntry pointing at one Library while
its LibraryEntry belongs to another Library.

Normalization is not a replacement for authorization. Every Play Log read and
write still scopes by the validated JWT `sub`; ownership must be part of the
database query predicate, for example through
`PlayLogEntry.LibraryEntry.Library.UserId == userId`, not checked after loading
unscoped rows.

Unknown, deleted, cross-type, or another user's well-formed non-empty `gameId`
returns `404` without leaking whether another user's resource exists. Missing,
malformed, or empty `gameId` returns `400`.

When the caller owns the `gameId` but its LibraryEntry is not Owned, the API
returns a validation error because the resource exists for the caller but is not
eligible for logging.

### Timestamps

`PlayedAt` represents when the user says the play occurred.

The client supplies `playedAt` during creation as an ISO 8601 offset-aware or UTC
timestamp, for example `2026-08-24T20:30:00-06:00` or
`2026-08-25T02:30:00Z`. The backend normalizes and persists it in
UTC/offset-aware form according to existing .NET/Npgsql/PostgreSQL conventions.
The frontend presents date/time using browser-local semantics.

Manual Library and general Play Log interactions default `playedAt` to the
current browser-local date/time and allow the user to change it before
submission.

Random Picker logging uses the same Log Play interaction and defaults `playedAt`
to the current browser-local date/time. This reflects that the user is logging
from the current picker flow, while still allowing adjustment before submission.

`PlayedAt` is required for `POST /play-log`. Missing or unparseable `playedAt`
is invalid. The backend rejects `playedAt` values later than server current UTC
time plus five minutes. The five-minute tolerance exists only to account for
small client/server clock differences; it is not a scheduling feature.

`CreatedAt` is also server-assigned and records when the row was created. It is
not user-editable.

`PlayedAt` and `CreatedAt` are distinct fields and may differ substantially.

API responses transport `PlayedAt` and `CreatedAt` as offset-aware timestamps.
The normal frontend Play Log presentation displays `playedAt` using the user's
browser-local date and time rather than rendering raw ISO strings.

### Duration

A PlayLogEntry may optionally record how long that individual play occurrence
lasted.

`DurationMinutes` rules:

- Optional: `null` is valid.
- When present, it must be an integer greater than 0.
- Valid examples: `30`, `90`, `240`.
- Invalid examples: `0`, negative values.
- No arbitrary maximum is imposed in Feature 009.
- Duration belongs to PlayLogEntry, not Game or LibraryEntry.
- Multiple PlayLogEntries for the same game remain allowed regardless of duration.

Backend validation is authoritative. The database also enforces the positive
duration rule with a CHECK constraint for persisted rows.

### Editing Existing Entries

Feature 009 supports correcting an existing PlayLogEntry.

Editable fields are exactly:

- `PlayedAt`.
- `DurationMinutes`.

Non-editable fields are:

- `Id`.
- `LibraryEntryId`.
- `GameId`.
- Library ownership.
- User ownership.
- `CreatedAt`.

Editing does not change ownership or the associated game. If the user wants to
record a play for another game, they create another PlayLogEntry.

The same PlayedAt validation used during creation applies during edit. Historical
timestamps, current timestamps, and timestamps within the approved future
tolerance are allowed. `PlayedAt > server UTC now + 5 minutes` is rejected.
Persist edited PlayedAt values using the existing DateTimeOffset/PostgreSQL
offset-aware conventions.

Duration remains optional during edit. Valid values are `null` and positive
integers. Invalid values are `0` and negative integers. Editing may add a
duration to a log that had none, change an existing duration, or clear an existing
duration back to `null`. No arbitrary maximum is imposed.

`CreatedAt` is never modified by edit behavior.

### Update Ownership Boundary

The update endpoint resolves the PlayLogEntry through the authenticated user:

`JWT sub -> PlayLogEntry -> LibraryEntry -> Library.UserId`

The update query must be caller-scoped. It must not load a global PlayLogEntry by
ID and authorize afterward.

Unknown PlayLogEntry IDs and another user's PlayLogEntry IDs both behave as not
found according to existing API conventions. The API must not leak whether
another user's entry exists.

### Multiple Plays

Multiple PlayLogEntries may reference the same LibraryEntry. There is no unique
constraint on `library_entry_id`.

Valid intentional repeat logging is allowed: if the user logs Game A and, after
the first request completes, explicitly chooses Log play again, the second
PlayLogEntry is valid.

Accidental pending UI duplication is not allowed: if the user double-clicks or
double-taps the same Log play action while the original POST is still pending,
the UI must issue only one request for that pending interaction. Do not add a
backend uniqueness constraint, backend deduplication, or arbitrary time-window
deduplication.

### Acquisition Status Changes

Existing PlayLogEntries remain when an Owned game changes to Wishlist or
Interested.

New PlayLogEntries are blocked while the LibraryEntry is not Owned.

Changing the game back to Owned allows new PlayLogEntries again, subject to all
normal game-type invariants such as the Owned VideoGame Platform requirement.

### Deletion Lifecycle

Deleting a VideoGame or BoardGame deletes its LibraryEntry and associated
PlayLogEntries in the same lifecycle.

The domain must not leave orphan PlayLogEntries.

### Random Picker Independence

Play Log does not change Random Picker candidate eligibility, filter semantics,
shown-history behavior, result states, or random selection weighting.

Only `AcquisitionStatus = Owned` controls picker eligibility. Play history is not
used for recommendations or exclusions in this feature.

Logging from Random Picker must not mutate `shownLibraryEntryIds`, visible picker
history, current mode, current filters, `Another`, Restart/Start over behavior,
GameStatus, or ProgressPercentage.

## Technical Requirements

### Backend Structure

Use the existing two-project architecture.

Expected `GameLibrary.Core` structure:

```text
GameLibrary.Core/
+-- PlayLog/
|   +-- PlayLogEntry.cs
|   +-- PlayLogService.cs
|   +-- PlayLogExceptions.cs
|   +-- PlayLogView.cs
+-- ...
```

Expected `GameLibrary.Api` structure:

```text
GameLibrary.Api/
+-- Controllers/
|   +-- PlayLogController.cs
+-- PlayLog/
    +-- PlayLogContracts.cs
```

Register `PlayLogService` in `Program.cs` with normal scoped lifetime. Do not add
new middleware, projects, infrastructure layers, or generic repository
abstractions.

### Core Entity

```csharp
public class PlayLogEntry
{
    public Guid Id { get; set; }
    public Guid LibraryEntryId { get; set; }
    public DateTimeOffset PlayedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int? DurationMinutes { get; set; }

    public LibraryEntry LibraryEntry { get; set; } = null!;
}
```

Add navigation collections only where useful for EF mapping or queries. Do not
make broad domain-object graph changes solely for convenience.

### Core Views And Requests

Core request:

```csharp
public sealed record CreatePlayLogEntryRequest(
    Guid GameId,
    DateTimeOffset PlayedAt,
    int? DurationMinutes);

public sealed record UpdatePlayLogEntryRequest(
    DateTimeOffset PlayedAt,
    int? DurationMinutes);
```

Core view:

```csharp
public sealed record PlayLogEntryView(
    Guid Id,
    Guid LibraryEntryId,
    Guid GameId,
    GameType GameType,
    string GameName,
    string? CoverImageUrl,
    DateTimeOffset PlayedAt,
    DateTimeOffset CreatedAt,
    int? DurationMinutes);
```

The view intentionally contains only enough game context to identify the logged
play. Do not add ratings, notes, filters, statistics, or full game-management DTO
fields unless this spec is amended.

### PlayLogService

`PlayLogService` depends on `GameLibraryDbContext` and the app's normal clock
source if one already exists. If no clock abstraction exists, use the smallest
local approach consistent with existing services and tests.

Methods:

```csharp
Task<IReadOnlyList<PlayLogEntryView>> ListAsync(string userId, CancellationToken ct);

Task<PlayLogEntryView> CreateAsync(
    string userId,
    CreatePlayLogEntryRequest request,
    CancellationToken ct);

Task<PlayLogEntryView> UpdateAsync(
    string userId,
    Guid id,
    UpdatePlayLogEntryRequest request,
    CancellationToken ct);
```

`ListAsync` behavior:

- Return `[]` for a user with no Library.
- Do not create a Library row.
- Scope the query through `PlayLogEntry.LibraryEntry.Library.UserId == userId` or
  an equivalent join.
- Do not query PlayLogEntries globally and then perform ownership checks after
  materialization.
- Return entries ordered by `played_at` descending, then `created_at` descending,
  then `id` descending for deterministic ties.
- Include game context from the associated LibraryEntry and Game.
- Include `duration_minutes` as `DurationMinutes` in returned views.

`CreateAsync` behavior:

- Validate `GameId` is not empty.
- Validate `PlayedAt` is present and not later than server current UTC time plus
  five minutes.
- Validate `DurationMinutes` is null or greater than 0.
- Resolve the LibraryEntry by joining through Library and Game using the
  authenticated `userId` and request `GameId`.
- Do not query unscoped by user to resolve the game.
- Return not found when no caller-owned LibraryEntry exists for `GameId`.
- Reject the request when the caller-owned LibraryEntry is not `Owned`.
- Create one PlayLogEntry using request `PlayedAt`, request `DurationMinutes`, and
  server-assigned `CreatedAt`.
- Store only the resolved caller-owned `LibraryEntryId`; do not store `LibraryId`.
- Save atomically with normal EF Core transaction behavior.
- Return the created view.
- Never accept or trust client-supplied user, owner, library, library-entry,
  or `createdAt` values. `playedAt` and `durationMinutes` are accepted only as
  Play Log event data and must not affect ownership or authorization decisions.

`UpdateAsync` behavior:

- Validate `id` is not empty.
- Validate `PlayedAt` is present and not later than server current UTC time plus
  five minutes.
- Validate `DurationMinutes` is null or greater than 0.
- Resolve the PlayLogEntry by joining through LibraryEntry and Library using the
  authenticated `userId` and route `id`.
- Do not query PlayLogEntries globally by ID and authorize after materialization.
- Return not found when no caller-owned PlayLogEntry exists for `id`.
- Replace only `PlayedAt` and `DurationMinutes` with submitted values.
- Allow `DurationMinutes` to change from null to a positive integer, from one
  positive integer to another, or from a positive integer to null.
- Do not modify `CreatedAt`.
- Do not modify `LibraryEntryId` or the associated Game.
- Save with normal EF Core update/save behavior.
- Return the updated view.
- Never accept or trust client-supplied user, owner, library, library-entry, game,
  or `createdAt` values. The route ID identifies the PlayLogEntry; `playedAt` and
  `durationMinutes` are accepted only as editable event data.

### Controller

`PlayLogController` is `[ApiController]`, `[Authorize]`, and `[Route("play-log")]`.

It exposes:

```csharp
[HttpGet]
public async Task<IActionResult> List(CancellationToken ct)

[HttpPost]
public async Task<IActionResult> Create(
    [FromBody] CreatePlayLogEntryRequestDto? request,
    CancellationToken ct)

[HttpPut("{id:guid}")]
public async Task<IActionResult> Update(
    Guid id,
    [FromBody] UpdatePlayLogEntryRequestDto? request,
    CancellationToken ct)
```

Controller behavior:

- Derive the user from `User.GetSupabaseUserId()` and return `401` when absent,
  matching existing controllers.
- Map DTOs to Core requests and Core views to API responses.
- Return predictable problem details for expected validation/domain failures.
- Let malformed JSON fail normal `[ApiController]` model binding with `400`; map
  missing or JSON `null` bodies and `{}` to validation problem details rather
  than custom model-binding infrastructure.
- Use the repository's existing full-update route convention for update routes:
  `[HttpPut("{id:guid}")]`. Missing or malformed route IDs therefore follow
  normal ASP.NET Core routing/model-binding conventions for constrained Guid
  routes.
- Expose no EF entities.

### API Contract

`GET /play-log` - `[Authorize]`.

Response `200`:

```json
[
  {
    "id": "00000000-0000-0000-0000-000000000000",
    "libraryEntryId": "11111111-1111-1111-1111-111111111111",
    "gameId": "22222222-2222-2222-2222-222222222222",
    "gameType": "VideoGame",
    "gameName": "Example Game",
    "coverImageUrl": null,
    "playedAt": "2026-08-24T18:30:00Z",
    "createdAt": "2026-08-24T19:05:00Z",
    "durationMinutes": 90
  }
]
```

`POST /play-log` - `[Authorize]`.

Request:

```json
{
  "gameId": "22222222-2222-2222-2222-222222222222",
  "playedAt": "2026-08-24T20:30:00-06:00",
  "durationMinutes": 90
}
```

Request field rules:

- `gameId` is required and must be a non-empty Guid.
- `playedAt` is required and must be an ISO 8601 offset-aware or UTC timestamp.
- `durationMinutes` is optional. Omit it or send `null` when no duration is
  recorded. When present, it must be a positive integer.

The API must not accept user ID, owner ID, `libraryId`, `libraryEntryId`, or
`createdAt` from the client. Extra client fields, if sent, must not affect
authorization, persistence, or timestamps.

Response `201` with the created entry. A `Location` header is not required unless
implementation adds a per-entry GET endpoint through an approved amendment.

`PUT /play-log/{id}` - `[Authorize]`.

Route:

- `id` identifies the PlayLogEntry to update.
- Use the existing repository route style `[HttpPut("{id:guid}")]`.
- `Guid.Empty` is invalid even though it is syntactically a Guid.

Request:

```json
{
  "playedAt": "2026-08-24T20:30:00-06:00",
  "durationMinutes": 90
}
```

Request field rules:

- The request contract contains exactly the editable event fields: `playedAt` and
  `durationMinutes`.
- `playedAt` is required and must be an ISO 8601 offset-aware or UTC timestamp.
- `durationMinutes` is optional. Omit it or send `null` to clear/no-record
  duration. When present, it must be a positive integer.
- The API contract must not include or bind `gameId`, `libraryEntryId`,
  `libraryId`, user ID, owner ID, or `createdAt` for update.
- Extra client fields, if sent, must not affect authorization, persistence,
  timestamps, ownership, or associated game identity.

Response `200` with the updated entry in the same shape as `GET /play-log` items.

Successful update behavior:

- `PlayedAt` is replaced with the submitted value.
- `DurationMinutes` is replaced with the submitted nullable value.
- `CreatedAt` remains unchanged.
- `LibraryEntryId` remains unchanged.
- The associated Game remains unchanged.

Create errors:

| Condition | HTTP | Behavior |
| --- | --- | --- |
| Unauthenticated request | `401` | Produced by JWT bearer handler. |
| Authenticated principal without usable `sub` | `401` | Controller returns `Unauthorized()`. |
| Empty/missing request body | `400` | Validation problem details. |
| JSON `null` request body | `400` | Validation problem details. |
| JSON object `{}` | `400` | Validation problem details because `gameId` is missing/empty. |
| Malformed JSON or malformed Guid in `gameId` | `400` | Normal `[ApiController]` model-binding bad request. |
| Empty `gameId` (`00000000-0000-0000-0000-000000000000`) | `400` | Validation problem details. |
| Missing, empty, or malformed `playedAt` | `400` | Validation/model-binding bad request. |
| `playedAt` later than server current UTC time plus five minutes | `400` | Validation problem details. |
| `durationMinutes` is `null` or omitted | n/a | Valid. |
| `durationMinutes` is positive | n/a | Valid and persisted. |
| `durationMinutes` is zero or negative | `400` | Validation problem details. |
| User has no Library | `404` | No caller-owned game exists to log. |
| Unknown, deleted, cross-type, or another user's `gameId` | `404` | Do not leak resource existence. |
| Caller-owned game is Wishlist or Interested | `400` | Detail: `Only owned games can be logged.` |
| Unexpected exception | `500` | Existing centralized handler. |

Update errors:

| Condition | HTTP | Behavior |
| --- | --- | --- |
| Unauthenticated request | `401` | Produced by JWT bearer handler. |
| Authenticated principal without usable `sub` | `401` | Controller returns `Unauthorized()`. |
| Missing route ID segment, such as `PUT /play-log/` | `405` | Normal ASP.NET Core method-matching behavior because `/play-log` exists for GET/POST but not PUT. |
| Malformed route Guid | `404` | Normal route-constraint behavior for `[HttpPut("{id:guid}")]`. |
| Empty route ID (`00000000-0000-0000-0000-000000000000`) | `400` | Validation problem details. |
| Empty/missing request body | `400` | Validation problem details. |
| JSON `null` request body | `400` | Validation problem details. |
| JSON object `{}` | `400` | Validation problem details because `playedAt` is missing/default. |
| Malformed JSON | `400` | Normal `[ApiController]` model-binding bad request. |
| Missing, empty, null, or malformed `playedAt` | `400` | Validation/model-binding bad request. |
| `playedAt` later than server current UTC time plus five minutes | `400` | Validation problem details. |
| `durationMinutes` is `null` or omitted | n/a | Valid; persisted as null, clearing an existing duration if present. |
| `durationMinutes` is positive | n/a | Valid and persisted. |
| `durationMinutes` is zero or negative | `400` | Validation problem details. |
| Unknown, deleted, or another user's PlayLogEntry ID | `404` | Do not leak resource existence. |
| Request includes `gameId`, `libraryEntryId`, `libraryId`, user ID, owner ID, or `createdAt` | n/a | These are not update contract fields and must not affect authorization or persistence. |
| Unexpected exception | `500` | Existing centralized handler. |

### Database / EF Core

Add one EF Core migration. EF Core migrations remain the single source of truth
for application schema changes.

Base Feature 009 table, with manual review amendment column:

```text
play_log_entries
- id uuid primary key
- library_entry_id uuid not null
- played_at timestamp with time zone not null
- created_at timestamp with time zone not null
- duration_minutes integer null
```

The base Feature 009 migration already created `play_log_entries`. The manual
review amendment requires a new EF Core migration that reuses the existing table
and adds only `duration_minutes` plus its constraint. Do not create a second play
log entity/table.

Relationships:

- `play_log_entries.library_entry_id` references `library_entries.id` with
  `ON DELETE CASCADE` so deleting a LibraryEntry deletes its PlayLogEntries.
- There is no `library_id` column, no direct FK to `libraries`, and no composite
  FK requirement. Ownership derives through `library_entries.library_id`.
- Do not add a uniqueness constraint on `library_entry_id`.

Indexes:

- Required: index `library_entry_id` so joins and delete cascades are efficient,
  for example `ix_play_log_entries_library_entry_id`.
- Do not add a `played_at` index for Feature 009. The caller-scoped newest-first
  query filters ownership through `LibraryEntry -> Library`, so a standalone
  `played_at` index has no proven MVP value. If a future feature demonstrates a
  need for an ordering index, a normal PostgreSQL B-tree can scan in reverse, so
  an explicit descending index is not required merely for newest-first ordering.

Schema rules:

- Use lowercase snake_case table, column, constraint, and index names.
- Add a CHECK constraint equivalent to `duration_minutes IS NULL OR
  duration_minutes > 0`. Use a lowercase snake_case constraint name, for example
  `ck_play_log_entries_duration_minutes_positive`.
- Do not create triggers or database functions for this feature.
- Do not add a duplicated ownership column for query convenience.
- Do not attempt a cross-table CHECK constraint for Owned-only creation;
  PostgreSQL CHECK constraints cannot enforce that cleanly. Enforce Owned-only
  creation in backend domain/application logic.
- Do not add Supabase CLI migrations or manually edit the Supabase dashboard.
- Update `DatabaseMigrationTests` schema assertions to include the new table and
  expected constraints/indexes.

### Frontend

Build the feature under `src/app/features/play-log/`.

Expected files:

```text
src/app/features/play-log/
+-- play-log.ts
+-- play-log.service.ts
+-- play-log.service.spec.ts
+-- play-log-page/
    +-- play-log-page.ts
    +-- play-log-page.html
    +-- play-log-page.scss
    +-- play-log-page.spec.ts
```

`play-log.service.ts` exposes:

```ts
list(): Observable<PlayLogEntry[]>;
logPlay(request: CreatePlayLogEntryRequest): Observable<PlayLogEntry>;
update(id: string, request: UpdatePlayLogEntryRequest): Observable<PlayLogEntry>;
```

The create request contains `gameId`, `playedAt`, and optional `durationMinutes`.
It calls `${environment.apiBaseUrl}/play-log`. The existing auth interceptor
attaches the bearer token.

The update request contains only `playedAt` and optional nullable
`durationMinutes`. It calls `${environment.apiBaseUrl}/play-log/${id}` with
`PUT`. It never sends `gameId`, `libraryEntryId`, `libraryId`, user ID, owner ID,
or `createdAt`.

`play-log-page` behavior:

- Protected route `/play-log` guarded by `authGuard`.
- Load play-log entries on page open.
- Display entries newest first using Play Shelf list/card patterns.
- Show game name, type, cover/placeholder, `playedAt` formatted for the user's
  browser-local date and time, and duration when present. Do not render raw ISO
  strings or `Duration: null` as the normal user-facing presentation.
- Duration presentation should be user-friendly. Examples: `45 min`, `1 h 30
  min`, `2 h`. Exact localization/formatting remains an implementation detail.
- Show an empty state when no plays have been logged.
- Provide retry on load failure.
- On `401`, clear the local session and navigate to `/login`, consistent with
  existing authenticated features.
- Each Play Log card exposes an `Edit` action.
- The card makes the game identity read-only and obvious; edit mode must not show
  game selection.
- `Edit` opens the same Log Play dialog/sheet or a closely related Play Shelf
  dialog/sheet if that is simpler.
- Edit prepopulates the existing `playedAt` and `durationMinutes` values.
- Edit fields are exactly played date/time and optional duration in minutes.
- Edit actions are primary `Save` and secondary `Cancel`.
- Cancel closes the edit dialog and sends no PUT.
- Save sends `PUT /play-log/{id}` with `playedAt` and nullable
  `durationMinutes` only.
- While Save is pending, duplicate update submissions for that edit interaction
  are disabled or ignored and must not issue a second PUT.
- On update failure, keep the dialog open, preserve entered values, show readable
  error feedback, and allow retry.
- On update success, close the dialog or otherwise return to a stable Play Log
  page state and update the corresponding card without a full application reload.
- If edited `playedAt` changes, re-sort the visible Play Log list newest first
  immediately using the same ordering as the page list. For example, an older
  entry edited to today's date moves to the correct newest-first position without
  requiring refresh.
- Edit belongs to the Play Log page only. Do not add Play Log edit actions to
  Library cards or Random Picker result cards.

Shared create/edit interaction:

- Prefer reusing the Log Play dialog/sheet where practical, but keep the solution
  simple and do not build a generic forms framework.
- Create receives `gameId`, defaults PlayedAt to now, defaults duration to empty,
  and submits `POST /play-log`.
- Edit receives an existing PlayLogEntry, prepopulates PlayedAt and
  DurationMinutes, makes the game read-only, and submits `PUT /play-log/{id}`.

Logging behavior from other screens:

- Home links to `/play-log` with a secondary card/action.
- Library cards for Owned games expose `Log play`.
- Random Picker successful current result exposes `Log play`.
- Feature 009 requires Log play on the current successful Random Picker result;
  previous visible history cards are not required to expose Log play.
- Clicking `Log play` opens a lightweight Log Play form/dialog instead of sending
  an immediate POST.
- Use Feature 008 Play Shelf dialog/sheet patterns. Desktop may use an accessible
  modal dialog. Mobile should use the same interaction adapted as a comfortable
  full-screen or near-full-screen sheet if the existing dialog pattern naturally
  behaves that way.
- The Log Play interaction has exactly these MVP fields: played date/time and
  optional duration in minutes.
- The Log Play interaction has these actions: primary `Log play` and secondary
  `Cancel`.
- The dialog is not multi-step and does not introduce timers or automatic
  tracking.
- Opening the dialog defaults `playedAt` to current browser-local date/time and
  duration to empty.
- The user may change `playedAt` and duration before confirming.
- Cancel closes the dialog and sends no POST.
- On confirmation, the frontend sends `gameId`, selected `playedAt`, and
  optional `durationMinutes` to `POST /play-log`.
- The frontend converts the browser-local date/time input into an API timestamp
  with offset or UTC semantics that the backend can parse as `DateTimeOffset`.
- The Random Picker action logs only after the user explicitly confirms the Log
  Play form. Pick and Another never log automatically.
- Each Library card Log Play submission has its own in-flight state after the user
  confirms. While that POST is pending, repeated submission for the same
  interaction is disabled or ignored and must not issue a second POST. Other
  Library cards are not globally disabled.
- Each Random Picker result Log Play submission has its own in-flight state after
  the user confirms. While that POST is pending, repeated submission for that same
  result interaction is disabled or ignored and must not issue a second POST. Do
  not block Another, Restart/Start over, or unrelated history cards unless the
  actual component interaction requires it.
- On successful log, show a small confirmation and do not navigate away unless the
  user chooses to open Play Log. Re-enable the initiating Log play action.
- On failed log, show readable error feedback and re-enable the initiating Log
  play action so the user may retry.
- For non-Owned games, the frontend may hide or disable `Log play`, but backend
  validation remains authoritative.
- A successful log should update the Play Log page if the user is on it. Other
  pages do not need global cache synchronization; if the user later opens Play Log,
  it reloads from the API.
- Library cards and Random Picker results only create new PlayLogEntries. They do
  not edit existing PlayLogEntries.

Navigation requirements:

- Add `/play-log` as a protected route.
- Add Play Log to authenticated desktop top navigation as a normal primary
  destination alongside the existing app areas.
- When the desktop route is `/play-log`, Play Log must show active navigation
  state visually and semantically through the same active-state pattern as other
  desktop nav links.
- Do not add a fifth mobile bottom-navigation item.
- The existing mobile bottom navigation remains Home, Library, Pick, and Manage.
- Home provides Play Log discoverability through a clear Play Log / recent
  activity card or action linking to `/play-log`.
- `/play-log` is not a management destination and must not make Manage active in
  mobile navigation.

## Acceptance Criteria

### Backend

1. `GET /play-log`, `POST /play-log`, and `PUT /play-log/{id}` require
   authentication.
2. All Play Log endpoints derive ownership only from the validated JWT `sub`.
3. `GET /play-log` returns `[]` for a user with no Library and creates no Library
   row.
4. `GET /play-log` returns only the caller's entries.
5. `GET /play-log` orders entries by newest `playedAt` first with deterministic
   tie-breakers.
6. `POST /play-log` accepts `gameId`, `playedAt`, and optional
   `durationMinutes`, and never accepts user ID, library ID, library-entry ID, or
   `createdAt` from the client.
7. `POST /play-log` creates a row for a caller-owned Owned VideoGame.
8. `POST /play-log` creates a row for a caller-owned Owned BoardGame.
9. `POST /play-log` allows multiple rows for the same game.
10. `POST /play-log` returns `404` for unknown or another user's `gameId`.
11. `POST /play-log` rejects caller-owned Wishlist and Interested games with
    `Only owned games can be logged.`.
12. Existing PlayLogEntries remain after a game changes away from Owned.
13. New PlayLogEntries are blocked while that game is not Owned.
14. Deleting a game/LibraryEntry deletes its PlayLogEntries.
15. Random Picker requests and results do not create PlayLogEntries.
16. No Random Picker persistence table or picker-session persistence is added.
17. The EF Core migration adds only the approved `play_log_entries` table,
    foreign keys, and indexes.
18. The EF Core migration does not add `library_id` to `play_log_entries`; Play
    Log ownership derives only through `LibraryEntry -> Library`.
19. `POST /play-log` accepts a valid supplied `playedAt` and persists it.
20. Historical `playedAt` values are accepted.
21. `POST /play-log` rejects `playedAt` later than server current UTC time plus
    five minutes.
22. `CreatedAt` is always server-assigned and cannot be supplied by the client.
23. `PlayedAt` and `CreatedAt` may differ and both round-trip through API
    responses.
24. Null or omitted `durationMinutes` is allowed.
25. Positive `durationMinutes` is accepted and persists.
26. Zero `durationMinutes` is rejected.
27. Negative `durationMinutes` is rejected.
28. Duration round-trips through API responses and database persistence.
29. The amendment EF Core migration reuses `play_log_entries` and adds
    `duration_minutes integer null` with a positive-when-present CHECK
    constraint.
30. `PUT /play-log/{id}` accepts only `playedAt` and nullable `durationMinutes`
    as editable event fields.
31. `PUT /play-log/{id}` lets a caller edit their own PlayLogEntry.
32. `PUT /play-log/{id}` replaces PlayedAt with the submitted valid value.
33. `PUT /play-log/{id}` accepts historical PlayedAt updates.
34. `PUT /play-log/{id}` rejects PlayedAt later than server current UTC time plus
    five minutes.
35. `PUT /play-log/{id}` can set DurationMinutes from null to a positive value.
36. `PUT /play-log/{id}` can change DurationMinutes from one positive value to
    another positive value.
37. `PUT /play-log/{id}` can clear DurationMinutes to null.
38. `PUT /play-log/{id}` rejects zero DurationMinutes.
39. `PUT /play-log/{id}` rejects negative DurationMinutes.
40. `PUT /play-log/{id}` leaves CreatedAt unchanged.
41. `PUT /play-log/{id}` leaves LibraryEntryId unchanged.
42. `PUT /play-log/{id}` leaves the associated Game unchanged.
43. `PUT /play-log/{id}` returns the updated PlayLogEntry response.
44. `PUT /play-log/{id}` resolves the entry through
    `PlayLogEntry -> LibraryEntry -> Library.UserId` in the caller-scoped query.
45. `PUT /play-log/{id}` returns `404` for another user's PlayLogEntry ID without
    leaking existence.
46. `PUT /play-log/{id}` returns `404` for an unknown PlayLogEntry ID.
47. `PUT /play-log/{id}` returns validation/model-binding errors for missing,
    malformed, or empty route/body values according to the API contract.
48. No `DELETE /play-log/{id}` endpoint is added.

### Frontend

1. `/play-log` is protected by `authGuard`.
2. Home provides a discoverable path to Play Log.
3. Desktop top navigation contains Play Log.
4. `/play-log` has active desktop navigation state.
5. Mobile bottom navigation remains exactly Home, Library, Pick, and Manage.
6. `/play-log` does not activate Manage in mobile navigation.
7. Play Log page lists entries newest first with game name, type, cover or
   placeholder, and played timestamp in the user's browser-local date/time.
8. Play Log page shows duration when present using a user-friendly presentation
   and does not show `Duration: null` when absent.
9. Play Log page shows loading, empty, error/retry, and unauthorized states.
10. Owned Library cards expose a `Log play` action.
11. Library `Log play` opens the Log Play form/dialog.
12. Library Log Play defaults `playedAt` to current browser-local date/time and
    duration to empty.
13. Library Log Play allows the user to change `playedAt` and enter optional
    duration before confirming.
14. Library Log Play confirmation creates exactly one entry when the submission
    succeeds.
15. Random Picker successful current result exposes a `Log play` action.
16. Random Picker `Log play` opens the same Log Play interaction.
17. Random Picker Log Play defaults `playedAt` to current browser-local date/time
    and duration to empty.
18. Random Picker Log Play allows optional duration and date/time adjustment
    before confirming.
19. Pick and Another never log a play automatically.
20. Successful `Log play` shows confirmation.
21. Canceling the Log Play interaction sends no POST.
22. While a Library card Log play request is pending, repeated activation of that
    same card/action issues no additional POST; success and failure both
    re-enable that action, and unrelated cards are not globally blocked.
23. While a Random Picker Log play request is pending, repeated activation of
    that same result-card action issues no additional POST; success and failure
    both re-enable that action, and Another, Restart/Start over, and unrelated
    result cards are not unnecessarily blocked.
24. Random Picker logging does not affect `shownLibraryEntryIds`, visible
    history, Another, Restart/Start over, picker eligibility, GameStatus, or
    ProgressPercentage.
25. Non-Owned entries are not presented as eligible for logging in normal UI.
26. Backend validation errors are shown clearly when logging fails.
27. `401` handling matches existing authenticated features.
28. Each Play Log card exposes an `Edit` action.
29. Play Log `Edit` opens a dialog/sheet prepopulated with the entry's existing
    PlayedAt and DurationMinutes.
30. The edit dialog/sheet makes the game identity read-only and does not expose
    game selection.
31. Saving an edit sends `PUT /play-log/{id}` with only `playedAt` and nullable
    `durationMinutes`.
32. Canceling the edit dialog sends no PUT.
33. The edit dialog allows DurationMinutes to be cleared and sends null/omitted
    duration according to the API contract.
34. Edited PlayedAt updates the corresponding card after success.
35. Edited DurationMinutes updates the corresponding card after success.
36. If edited PlayedAt changes ordering, the Play Log list reorders newest-first
    immediately without full application reload.
37. While edit Save is pending, repeated Save activation issues no additional PUT.
38. Edit failure preserves entered values, keeps the dialog usable, and allows
    retry.
39. Edit success closes the dialog or returns to a stable Play Log page state and
    updates the UI.
40. Library cards do not expose Play Log edit actions.
41. Random Picker result cards do not expose Play Log edit actions.
42. Play Log delete UI remains unavailable.

## Testing

### Backend Unit Tests

Add focused tests for any pure Play Log validation/rules code, at minimum:

- Empty `gameId` is invalid.
- Non-Owned status maps to the exact validation detail
  `Only owned games can be logged.`.
- Missing/default `playedAt` is invalid.
- Future `playedAt` later than server current UTC time plus five minutes is
  invalid.
- Historical and current `playedAt` values are valid.
- Null `DurationMinutes` is valid.
- Positive `DurationMinutes` is valid.
- Zero and negative `DurationMinutes` are invalid.
- The same PlayedAt and DurationMinutes validation applies to update requests.
- Empty PlayLogEntry update ID is invalid.
- CreatedAt assignment uses server-side time if a clock abstraction is introduced.

Do not create unit tests that duplicate EF integration behavior without value.

### Backend Integration Tests

Use real PostgreSQL semantics through the existing integration-test setup.

Required cases:

- Unauthenticated `GET /play-log` returns `401`.
- Unauthenticated `POST /play-log` returns `401`.
- Unauthenticated `PUT /play-log/{id}` returns `401`.
- User with no Library receives `GET /play-log` as `[]` and no Library row is
  created.
- User isolation with two users: each user sees only their own play logs.
- User isolation queries scope through `PlayLogEntry -> LibraryEntry -> Library`;
  there is no `play_log_entries.library_id` ownership shortcut.
- Request-body validation covers empty/missing body, JSON `null`, `{}`, malformed
  Guid, `Guid.Empty`, missing/malformed `playedAt`, invalid future `playedAt`,
  and invalid `durationMinutes` as `400` cases.
- `POST /play-log` for another user's game returns `404`.
- `POST /play-log` for an Owned VideoGame returns `201` and persists a row.
- `POST /play-log` for an Owned BoardGame returns `201` and persists a row.
- `POST /play-log` with supplied `playedAt` persists that value and returns it.
- Historical `playedAt` persists and round-trips.
- `CreatedAt` is server assigned and ignores any client-supplied `createdAt`.
- `PlayedAt` and `CreatedAt` may differ.
- Null or omitted `DurationMinutes` persists as null.
- Positive `DurationMinutes` persists and round-trips.
- Zero and negative `DurationMinutes` are rejected.
- Multiple posts for the same game create multiple rows.
- Newest-first ordering is deterministic.
- Wishlist and Interested caller-owned games return `400` with
  `Only owned games can be logged.`.
- Existing play logs remain when an Owned game changes to Wishlist or Interested.
- New play logs are blocked after that status change.
- Deleting a VideoGame deletes its play logs.
- Deleting a BoardGame deletes its play logs.
- Random Picker `POST /random-picker/pick` does not create play logs.
- Schema assertions include `play_log_entries`, its foreign keys, and FK indexes.
- Schema assertions verify `play_log_entries` has no `library_id` column and no
  direct FK to `libraries`.
- Schema assertions verify `duration_minutes integer null` and the positive
  CHECK constraint.
- `PUT /play-log/{id}` updates a caller-owned entry and returns `200` with the
  updated response.
- `PUT /play-log/{id}` accepts a historical PlayedAt update and persists it.
- `PUT /play-log/{id}` rejects PlayedAt later than server current UTC time plus
  five minutes.
- `PUT /play-log/{id}` sets DurationMinutes from null to a positive value.
- `PUT /play-log/{id}` changes DurationMinutes from one positive value to
  another.
- `PUT /play-log/{id}` clears DurationMinutes to null.
- `PUT /play-log/{id}` rejects zero and negative DurationMinutes.
- `PUT /play-log/{id}` leaves CreatedAt unchanged.
- `PUT /play-log/{id}` leaves LibraryEntryId unchanged.
- `PUT /play-log/{id}` leaves the associated Game unchanged.
- `PUT /play-log/{id}` for another user's PlayLogEntry returns `404`.
- `PUT /play-log/{id}` for an unknown PlayLogEntry returns `404`.
- Update validation covers missing route segment, malformed route Guid,
  `Guid.Empty`, empty/missing body, JSON `null`, `{}`, malformed JSON,
  missing/malformed `playedAt`, invalid future `playedAt`, and invalid
  `durationMinutes` according to the API contract.

### Frontend Tests

Add tests for:

- `PlayLogService.list()` calls `GET /play-log`.
- `PlayLogService.logPlay(request)` calls `POST /play-log` with `gameId`,
  `playedAt`, and optional `durationMinutes`.
- `PlayLogService.update(id, request)` calls `PUT /play-log/{id}` with only
  `playedAt` and nullable `durationMinutes`.
- Play Log page renders loading, empty, error/retry, and populated states.
- Entries render newest-first as returned by the API.
- Entries render `playedAt` as a formatted user-facing timestamp without coupling
  tests to a specific OS locale where possible.
- Play Log page formats duration when present and omits duration text when absent.
- Library Owned cards expose `Log play`; non-Owned cards do not expose an enabled
  logging action.
- Library `Log play` opens the Log Play dialog/sheet.
- The Log Play dialog defaults `playedAt` to current local date/time and duration
  to empty.
- The Log Play dialog allows optional duration input.
- Submitting sends selected `playedAt` converted to an API timestamp and selected
  duration when present.
- Cancel closes the dialog and does not POST.
- Library Log play duplicate prevention: while the request for one Owned card is
  pending, repeated click/tap of that same action sends at most one POST; success
  and failure re-enable the action; unrelated card actions are not globally
  blocked.
- Random Picker success result exposes `Log play`, and Pick/Another do not call
  the Play Log API.
- Random Picker `Log play` opens the same Log Play dialog/sheet, defaulted to now
  with empty duration.
- Random Picker Log Play submission includes selected `playedAt` and optional
  duration without changing picker session state.
- Random Picker Log play duplicate prevention: while a result-card Log play
  request is pending, repeated click/tap of that same action sends at most one
  POST; success and failure re-enable the action; Another and unrelated result
  cards are not unnecessarily blocked.
- Successful log shows confirmation.
- Failed log shows readable error.
- `401` clears local auth session and navigates to `/login`.
- Desktop top navigation includes Play Log and marks `/play-log` active.
- Mobile bottom navigation remains Home, Library, Pick, and Manage and does not
  mark Manage active for `/play-log`.
- Play Log card `Edit` action is visible.
- Edit dialog opens prepopulated with the entry's PlayedAt and DurationMinutes.
- Edit Save sends the correct PUT body and does not send game, library-entry,
  library, user, owner, or CreatedAt fields.
- Edit Cancel sends no PUT.
- Edit allows DurationMinutes to be cleared.
- Edited PlayedAt updates the card.
- Edited DurationMinutes updates the card.
- The list reorders newest-first after a PlayedAt change.
- Duplicate Save is prevented while an edit update is pending.
- Edit error preserves form state and allows retry.
- Edit success updates the UI.
- Library cards and Random Picker result cards do not expose Play Log edit
  actions.
- Play Log delete UI is unavailable.

### E2E

Extend the existing Playwright suite with one small journey using deterministic
E2E data:

1. Login in E2E auth mode.
2. Navigate to Library.
3. Open Log play for an Owned game.
4. Submit a deterministic duration.
5. Open Play Log.
6. Verify the logged game and duration appear.
7. Open Edit for the entry.
8. Change duration to a deterministic value.
9. Save.
10. Verify the updated duration appears.

Optionally change PlayedAt only if the datetime control is stable in E2E. Do not
add exhaustive timestamp, styling, or randomness assertions.

## Verification Commands

Run the relevant checks before reporting implementation complete:

```bash
dotnet test tests/backend/GameLibrary.Core.Tests
dotnet test tests/backend/GameLibrary.IntegrationTests
dotnet build src/backend/GameLibrary.sln
```

```bash
cd src/frontend
npm test -- --watch=false
npm run lint
npm run build
npm run e2e
```

Integration and E2E tests require real PostgreSQL semantics. EF Core InMemory is
not an acceptable substitute for Play Log lifecycle, cascade, schema, or user
isolation scenarios.

## Risks / Notes

- The base Feature 009 implementation was now-only. Manual review amendments
  change that: new logs must accept a user-selected `playedAt` before creation,
  and existing logs may be corrected by editing PlayedAt and DurationMinutes.
- Individual PlayLogEntries are no longer append-only for editable event fields,
  but edit scope is intentionally narrow. Delete, associated-game reassignment,
  revision history, archival state, and richer PlaySession behavior remain out of
  scope. Accidental duplicate risk is mitigated by frontend in-flight
  duplicate-submission requirements, not by backend uniqueness/deduplication.
- Manual duration in minutes is now in scope for a PlayLogEntry. Timers, active
  sessions, automatic playtime tracking, start/end timestamps, pause/resume and
  telemetry remain out of scope.
- The create endpoint accepts `gameId` for frontend ergonomics but resolves the
  caller-owned LibraryEntry server-side to preserve authorization boundaries.
- PlayLogEntry stores only `LibraryEntryId`; the sole ownership path is
  `PlayLogEntry -> LibraryEntry -> Library`.
- Play Log persistence must stay separate from Random Picker visible history and
  shown IDs.
- Feature 009 now adds Play Log to desktop primary navigation while preserving the
  Feature 008 mobile bottom navigation exactly as Home, Library, Pick and Manage.
