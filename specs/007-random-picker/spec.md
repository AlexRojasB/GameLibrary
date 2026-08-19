# 007 - Random Picker

## Status

`Approved` (implemented and manually verified)

## Manual Review Amendment — 2026-08-19

Feature 007 manual review identified the following amendment requirements, which
are included in the implemented and verified scope:

- VideoGame player-count metadata is now available and Random Picker must support
  player-count filtering in VideoGames mode and All mode.
- Random Picker must show visible volatile result history for the current picker
  page/session, ordered newest first.
- The newest/current result has primary/highlighted visual emphasis; previous
  results remain visible with secondary/muted treatment.
- Visible history is frontend-only volatile state. Do not persist it in
  PostgreSQL, localStorage, sessionStorage, `RandomPickerSession`, `PickerHistory`,
  `PlaySession`, or any other table/entity.
- `shownLibraryEntryIds` remains the only history-like collection sent to the
  backend.

## Closeout — 2026-08-19

Feature 007 implementation and manual verification are complete.

Verified outcomes:

- Random Picker is implemented as a protected Angular route and backend
  `POST /random-picker/pick` endpoint.
- VideoGame optional `minimumPlayers` / `maximumPlayers` round-trip through CRUD
  and are usable by Random Picker player-count filters.
- Owned VideoGames with `GameStatus = Completed` normalize progress to `100`,
  and leaving Completed does not automatically lower progress.
- Random Picker player-count filtering works in VideoGames, BoardGames, and All
  modes; candidates missing required player-count metadata are excluded when the
  filter is active.
- Visible Random Picker history is frontend-only volatile state, newest first,
  and reset only by explicit user action.
- The EF Core migration
  `20260819183609_RelaxVideoGamePlayerCountConstraints` was applied to the local
  application database and the Docker integration-test database.
- Backend integration tests passed against real PostgreSQL: 176 passed, 0
  failed.
- Backend unit tests, backend build, frontend tests, frontend lint, and frontend
  build passed.
- Manual application testing passed for VideoGame player counts and Random Picker
  behavior.

Feature 008 remains separate and was not implemented by this feature.

## Objective

Implement the MVP Random Game Picker so an authenticated user can choose an
eligible owned game at random from their private Library.

After this feature is implemented, an authenticated user can:

1. Open a protected Random Picker screen.
2. Choose VideoGames, BoardGames, or All.
3. Apply the filters available for the selected mode.
4. Request a random eligible result.
5. Request Another without repeating games already shown in the current picker
   session while unshown matching candidates remain.
6. See visible volatile result history for the current picker session, newest
   first.
7. See a clear no-candidates state when filters match no owned games.
8. See a distinct all-already-shown state when all current matching candidates
   have already appeared in the current session.
9. Explicitly reset temporary shown history when they want previously shown games
   to become eligible again.
10. Preserve strict user isolation.

This feature implements only Random Picker behavior.

## Context

The approved Product, Domain, and Architecture Specifications are authoritative.
This specification is the seventh feature in the approved delivery order:

1. Project foundation and local development. (implemented)
2. Authentication. (implemented)
3. Platform management. (implemented)
4. VideoGame management. (implemented)
5. BoardGame management. (implemented)
6. Library browse/search/filter/sort. (implemented)
7. Random Picker. (this feature)
8. PWA polish and MVP end-to-end verification.

### Actual foundation produced by Features 001-006

This specification builds on the actual repository state:

- Backend: `src/backend/GameLibrary.sln` with exactly two application projects -
  `GameLibrary.Api` and `GameLibrary.Core`. `GameLibrary.Api` references
  `GameLibrary.Core`.
- `GameLibrary.Api/Program.cs` registers controllers, problem details,
  `LibraryService`, `PlatformService`, `VideoGameService`, `BoardGameService`,
  EF Core/Npgsql, CORS, Supabase JWT bearer authentication, and authorization.
- `GameLibrary.Core/Data/GameLibraryDbContext.cs` maps the existing MVP schema:
  `libraries`, `platforms`, `games`, `library_entries`, `genres`,
  `game_genres`, and `game_platforms`. The manual-review amendment requires only
  a targeted CHECK-constraint migration; no table or column is added for Random
  Picker.
- `Game` is the API resource identity used by the VideoGame, BoardGame, and
  Library browse contracts. `LibraryEntry` remains the domain identity for a
  user's relationship to a game, and the MVP has a 1:1 Game/LibraryEntry rule.
- `GameLibrary.Core/Libraries/LibraryService.cs` exposes `EnsureAsync` and the
  Feature 006 `BrowseAsync` read query. Reads never create a `libraries` row.
- `GameLibrary.Core/Libraries/LibraryFilterRules.cs` already owns parse and
  validation rules for Library search/filter/sort, including exact enum parsing,
  rating/player-count validation, repeated-list set semantics, and literal
  `ILIKE` search escaping.
- `GET /library` returns a unified `LibraryItemResponse` for the caller's games,
  with type-specific fields set to `null` or empty arrays for the other type.
- Frontend: Angular 22 under `src/frontend/`, standalone components, services,
  signals/local state, route guards, auth interceptor, protected `/library`,
  `/platforms`, `/video-games`, and `/board-games` routes. No NgRx exists.
- Tests: backend unit tests in `tests/backend/GameLibrary.Core.Tests` and backend
  integration tests in `tests/backend/GameLibrary.IntegrationTests` use xUnit;
  frontend tests use Vitest through Angular. Integration tests require real
  PostgreSQL semantics.

### Authoritative Random Picker rules

The Product Specification sections 19-26, Domain Specification sections 24-31,
and Architecture Specification section 24 define this feature:

- Only `AcquisitionStatus = Owned` LibraryEntries are eligible.
- Modes are VideoGames, BoardGames, and All.
- VideoGame Random mode filters: Platform, Genre, GameStatus, and player count.
  Platform, Genre, and GameStatus support multiple selected values.
- BoardGame Random mode filters: player count, available duration, and
  InteractionType. InteractionType supports multiple selected values.
- All Random mode mixes owned VideoGames and owned BoardGames into one candidate
  pool. The MVP filters in All mode are minimum Rating and player count. Other
  type-specific filters are not available in All mode.
- Multiple selected values within the same filter type use OR / ANY-match.
- Different active filter types combine using AND.
- Missing metadata does not satisfy an active filter requiring it.
- Player count `P` matches when both player-count values are present and
  `MinimumPlayers <= P <= MaximumPlayers`.
- Candidates with missing player-count metadata do not satisfy an active
  player-count filter.
- BoardGame available duration `D` matches only when `ApproximateDuration` exists
  and `ApproximateDuration <= D`.
- The backend is authoritative for candidate eligibility, mode-specific filters,
  filter semantics, exclusion of previously shown IDs, and random selection.
- The frontend owns only volatile session state: current mode, current filters,
  current result, visible result history, and already-shown LibraryEntry IDs.
- Random Picker session state is not persisted.
- Changing filters does not clear shown results.
- Changing filters or mode does not clear visible result history.
- The API must distinguish `SUCCESS`, `NO_CANDIDATES`, and
  `ALL_ALREADY_SHOWN`.
- The MVP does not use recommendation scoring, weighting, AI, machine learning,
  persistent history, play sessions, or historical optimization.

## In Scope

- A protected Angular Random Picker page under `/random-picker`.
- A backend Random Picker application service in `GameLibrary.Core` that:
  - parses and validates the request;
  - scopes all candidate queries to the authenticated Supabase `sub`;
  - always filters to `AcquisitionStatus = Owned`;
  - applies only the filters valid for the selected mode;
  - excludes `shownLibraryEntryIds` while unshown matching candidates remain;
  - randomly selects one remaining candidate;
  - returns one of the three conceptual result states.
- A `POST /random-picker/pick` endpoint in `GameLibrary.Api`.
- A Random Picker response DTO containing both `libraryEntryId` for session
  exclusion and `gameId` for navigation to existing game management pages.
- Frontend volatile session state only. The page keeps shown IDs in memory while
  the component is active; leaving, refreshing, or closing the page clears it.
- A visible volatile result-history collection in frontend memory, synchronized
  with successful picks and ordered newest first.
- A visible explicit action to clear the temporary shown-history list and visible
  result history together.
- Backend unit tests for pure parsing/validation rules, backend integration tests
  against real PostgreSQL for candidate eligibility/result states/user isolation,
  and frontend tests for meaningful session and UI behavior.
- README updates during implementation documenting the new route, endpoint,
  request/response contract, and session behavior.

## Out of Scope

Feature 007 must NOT implement or introduce:

- Any new tables, duplicate player-count columns, or persisted Random Picker state.
  The only approved schema change for this amendment is the targeted EF Core
  migration that relaxes/replaces the existing VideoGame player-count CHECK
  constraint.
- `RandomPickerSession`, `PickerHistory`, `PersistentRandomHistory`,
  `PlaySession`, gameplay history, recommendations, AI, scoring, weighting, or
  analytics entities.
- Saved filters, URL/query-string synchronization, or cross-page restored picker
  state.
- Search or sorting in the Random Picker. Random Picker uses mode-specific
  filters only.
- AcquisitionStatus filtering in the Random Picker. Eligibility is always Owned.
- Rating filters in VideoGame or BoardGame mode. The MVP rating filter is only
  available in All mode.
- Type-specific filters in All mode other than the newly shared player-count
  filter. Do not add Platform, Genre, GameStatus, InteractionType, or duration to
  All mode.
- Direct Angular access to application database tables.
- RLS, Supabase application-table policies, or any second authorization system.
- New backend projects or new frontend state-management architecture.
- NgRx, MediatR, CQRS frameworks, generic repositories, Unit of Work
  abstractions, Redis, message brokers, GraphQL, Elasticsearch/OpenSearch, or
  other infrastructure outside the approved Architecture Specification.
- A full application shell or broad navigation redesign. Add only the minimal
  route/home entry needed to reach the Random Picker.
- End-to-end test suites. Manual MVP acceptance verification remains Feature 008.

## Domain Decisions

### Random Picker Session Identity

The Architecture Specification says the frontend session tracks already-shown
`LibraryEntry` IDs. Feature 007 follows that exactly.

The existing VideoGame, BoardGame, and Library browse APIs expose `Game.Id` as
their resource identifier. Random Picker needs a second identifier:

- `libraryEntryId`: the domain/session identity used in
  `shownLibraryEntryIds` and exclusion.
- `gameId`: the existing game-management resource identity used to navigate to
  `/video-games` or `/board-games`.

This does not change the approved MVP 1:1 Game/LibraryEntry rule. It only makes
the Random Picker session boundary explicit and avoids overloading `Game.Id` for
a domain rule that is specified in terms of LibraryEntries.

### Endpoint Shape

Random selection is not a pure read from the caller's perspective: each request
uses caller-provided volatile session state and returns a non-deterministic
choice. The endpoint is therefore:

`POST /random-picker/pick`

The request body carries the current mode, mode-specific filters, and
`shownLibraryEntryIds`. The frontend does not send a user ID, library ID, owner
ID, or candidate IDs. The API derives ownership only from the validated JWT
`sub`.

### Result States

The response state is a string with exactly these values:

| State | Meaning | `result` |
| --- | --- | --- |
| `SUCCESS` | At least one unshown candidate matched; one was randomly selected. | Non-null |
| `NO_CANDIDATES` | The selected mode and filters match no eligible Owned entries. | `null` |
| `ALL_ALREADY_SHOWN` | The selected mode and filters match eligible Owned entries, but every matching candidate is present in `shownLibraryEntryIds`. | `null` |

The API must never collapse `NO_CANDIDATES` and `ALL_ALREADY_SHOWN` into one
generic empty result.

The frontend adds the returned `result.libraryEntryId` to its temporary shown
list and prepends the returned result to visible result history only after a
`SUCCESS` response. `NO_CANDIDATES` and `ALL_ALREADY_SHOWN` do not mutate shown
history and do not erase visible result history.

### Modes And Filters

Modes use these request values:

| Request value | Candidate pool | Available filters |
| --- | --- | --- |
| `VideoGames` | Owned `GameType.VideoGame` entries only | `platformIds`, `genreIds`, `gameStatuses`, `playerCount` |
| `BoardGames` | Owned `GameType.BoardGame` entries only | `playerCount`, `availableDuration`, `interactionTypes` |
| `All` | Owned VideoGame and BoardGame entries together | `ratingMin`, `playerCount` |

Unavailable filters are invalid when they are active for a mode. An unavailable
array filter is active when it is non-empty; an unavailable scalar filter is
active when it is non-null. For example, non-null `ratingMin` in `VideoGames`
mode, non-empty `platformIds` in `All` mode, or non-null `availableDuration` in
`VideoGames` mode produces a `400`. `playerCount` is available in VideoGames,
BoardGames, and All modes. Empty arrays and `null` scalar fields are
treated as absent. This keeps backend behavior aligned with the Product
requirement that type-specific filters other than player count are not available in All mode and
prevents silently ignored active request fields.

### Filter Semantics

Filter semantics follow the Domain Specification exactly:

- OR within a multi-select filter type.
- AND across different active filter types.
- Missing metadata does not satisfy an active filter requiring it.
- `platformIds`: a VideoGame candidate matches when any selected Platform ID is
  associated with its LibraryEntry. BoardGames never match this filter.
- `genreIds`: a VideoGame candidate matches when any selected Genre ID is
  associated with its Game. BoardGames never match this filter.
- `gameStatuses`: a VideoGame candidate matches only when `GameStatus` is non-null
  and one selected status matches.
- `playerCount`: a VideoGame or BoardGame candidate matches when both
  `MinimumPlayers` and `MaximumPlayers` are present and
  `MinimumPlayers <= playerCount <= MaximumPlayers`. A candidate with missing
  player-count metadata does not satisfy an active player-count filter.
- `availableDuration`: a BoardGame candidate matches only when
  `ApproximateDuration` is non-null and `ApproximateDuration <= availableDuration`.
- `interactionTypes`: a BoardGame candidate matches only when `InteractionType` is
  non-null and one selected value matches.
- `ratingMin`: in All mode only, a candidate matches when `Rating` is non-null and
  `Rating >= ratingMin`.

Unknown but well-formed `platformIds`, `genreIds`, and `shownLibraryEntryIds`
are not errors. They naturally match or exclude nothing inside the caller-scoped
query. This avoids leaking whether another user's identifiers exist.

Duplicate values in request arrays are deduplicated with set semantics.

### Random Selection Algorithm

The backend algorithm is:

1. Parse and validate the request before database work.
2. Build a caller-scoped candidate query anchored to
   `g.LibraryEntry.Library.UserId == userId`.
3. Filter to `g.LibraryEntry.AcquisitionStatus == AcquisitionStatus.Owned`.
4. Apply the selected mode's candidate type and allowed filters.
5. Determine whether any candidates exist before shown-ID exclusion.
6. If no candidates exist, return `NO_CANDIDATES`.
7. Exclude candidates whose `LibraryEntry.Id` is in `shownLibraryEntryIds`.
8. If no unshown candidates remain, return `ALL_ALREADY_SHOWN`.
9. Randomly select one unshown candidate.
10. Return `SUCCESS` with the selected result.

The implementation may project the filtered unshown candidate IDs into memory and
choose an index with framework/runtime random facilities. It must not apply
filters in Angular, and it must not use recommendation weighting or historical
optimization.

### Visible Volatile Result History

The Random Picker page maintains a frontend-only result-history array for the
current volatile picker session.

Rules:

- On `SUCCESS`, add `result.libraryEntryId` to `shownLibraryEntryIds`, add the
  returned result object to visible history, and place it first.
- The newest/latest successful result is the current result and receives the
  strongest visual emphasis.
- Older results remain visible underneath with secondary/muted visual treatment.
- `Another` uses the same current mode/filters and updated shown IDs, then
  prepends the newly selected result on `SUCCESS`.
- Changing filters does not clear shown IDs and does not clear visible history.
- Changing mode does not clear shown IDs and does not clear visible history.
- `NO_CANDIDATES` and `ALL_ALREADY_SHOWN` do not erase visible history.
- Leaving, refreshing, or recreating the picker page starts a new empty volatile
  history.
- The history is not sent to the backend; only `shownLibraryEntryIds` is sent.

### Session Reset

The user can explicitly reset temporary shown history from the Random Picker UI.
The MVP reset action clears the entire in-memory `shownLibraryEntryIds` list and
the visible volatile result history for the current picker session. This is
explicit user intent, not a silent reset.

Changing mode or filters does not clear shown history and does not clear visible
history. The current-result emphasis remains on the latest successful result even
when filters or mode later change; previous results are historical outputs for the
same picker session, not claims that they match current filters.

## Technical Requirements

### Backend

Structure in `GameLibrary.Core`:

```
GameLibrary.Core/
+-- RandomPicker/
|   +-- RandomPickerQuery.cs
|   +-- RandomPickerRules.cs
|   +-- RandomPickerService.cs
|   +-- RandomPickerExceptions.cs
+-- ...
```

Structure in `GameLibrary.Api`:

```
GameLibrary.Api/
+-- Controllers/
|   +-- RandomPickerController.cs
+-- RandomPicker/
    +-- RandomPickerContracts.cs
```

`Program.cs` registers `AddScoped<RandomPickerService>()`. No other architecture
or middleware changes are required.

#### Core Types

```csharp
public enum RandomPickerMode
{
    VideoGames,
    BoardGames,
    All,
}

public enum RandomPickerState
{
    Success,
    NoCandidates,
    AllAlreadyShown,
}

public sealed record RandomPickerRequest(
    string? Mode,
    IReadOnlyList<Guid>? ShownLibraryEntryIds,
    IReadOnlyList<Guid>? PlatformIds,
    IReadOnlyList<Guid>? GenreIds,
    IReadOnlyList<string>? GameStatuses,
    int? PlayerCount,
    int? AvailableDuration,
    IReadOnlyList<string>? InteractionTypes,
    int? RatingMin);

public sealed record RandomPickerQuery(
    RandomPickerMode Mode,
    IReadOnlyList<Guid> ShownLibraryEntryIds,
    IReadOnlyList<Guid> PlatformIds,
    IReadOnlyList<Guid> GenreIds,
    IReadOnlyList<GameStatus> GameStatuses,
    int? PlayerCount,
    int? AvailableDuration,
    IReadOnlyList<InteractionType> InteractionTypes,
    int? RatingMin);

public sealed record RandomPickerResult(
    RandomPickerState State,
    RandomPickerItemView? Result);

public sealed record RandomPickerItemView(
    Guid LibraryEntryId,
    Guid GameId,
    GameType GameType,
    string Name,
    string? CoverImageUrl,
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

#### RandomPickerRules

`RandomPickerRules` is a static pure rules module. It must be unit-testable
without a database and at minimum expose:

- `RandomPickerMode ParseMode(string? value)` - exact `VideoGames`, `BoardGames`,
  or `All`; `null`, empty, or unknown values throw `InvalidRandomPickerQueryException`
  with detail `Mode is invalid.`.
- `IReadOnlyList<GameStatus> ParseGameStatuses(IReadOnlyList<string>? values)` -
  empty -> `[]`; exact GameStatus enum-name strings only; unknown values throw
  `Game status is invalid.`; duplicates are deduplicated.
- `IReadOnlyList<InteractionType> ParseInteractionTypes(IReadOnlyList<string>? values)` -
  empty -> `[]`; exact `Cooperative`/`Competitive`; unknown values throw
  `Interaction type is invalid.`; duplicates are deduplicated.
- `int? ValidatePlayerCount(int? value)` - `null` ok; `>= 1` ok; `< 1` throws
  `Player count must be at least 1.`.
- `int? ValidateAvailableDuration(int? value)` - `null` ok; `>= 1` ok; `< 1`
  throws `Available duration must be at least 1.`.
- `int? ValidateRatingMin(int? value)` - `null` ok; `1..5` ok; outside throws
  `Rating must be between 1 and 5.`.
- `RandomPickerQuery Parse(RandomPickerRequest request)` - parses, deduplicates,
  and validates all fields, then validates mode/filter compatibility.

Mode/filter compatibility errors:

| Condition | Detail |
| --- | --- |
| VideoGames mode with active `availableDuration`, `interactionTypes`, or `ratingMin` | `Filter is not available for VideoGames mode.` |
| BoardGames mode with active `platformIds`, `genreIds`, `gameStatuses`, or `ratingMin` | `Filter is not available for BoardGames mode.` |
| All mode with any active type-specific filter other than player count (`platformIds`, `genreIds`, `gameStatuses`, `availableDuration`, `interactionTypes`) | `Filter is not available for All mode.` |

All exceptions are `InvalidRandomPickerQueryException` carrying the exact detail
message.

#### RandomPickerService

`RandomPickerService` is a concrete application service depending only on
`GameLibraryDbContext`. It does not call `LibraryService.EnsureAsync`; Random
Picker is read-only and must not create a Library row.

Method:

```csharp
Task<RandomPickerResult> PickAsync(string userId, RandomPickerRequest request, CancellationToken ct)
```

Required behavior:

- Parse and validate via `RandomPickerRules.Parse` before database work.
- Compose the candidate query server-side with EF Core/Npgsql.
- Scope every query to the authenticated user's Library:
  `g.LibraryEntry.Library.UserId == userId`.
- Filter every mode to `g.LibraryEntry.AcquisitionStatus == AcquisitionStatus.Owned`.
- Apply `GameType.VideoGame`, `GameType.BoardGame`, or no type filter based on
  mode.
- Apply only the validated filters for the selected mode.
- Use `LibraryEntry.Id` for shown exclusion.
- Return `NoCandidates` when the filtered owned candidate query has no rows before
  shown exclusion.
- Return `AllAlreadyShown` when candidates exist before shown exclusion but none
  remain after excluding `shownLibraryEntryIds`.
- Return `Success` with a randomly selected `RandomPickerItemView` when at least
  one unshown candidate remains.
- Never accept or trust a client-supplied user/library/owner ID.
- Never query unscoped by user to resolve foreign IDs.
- Never persist the shown list or any picker session data.

#### Query Composition

The candidate query starts from `Games` and joins through the required navigation
to `LibraryEntry` and `Library`.

Base predicate for all modes:

```csharp
g.LibraryEntry.Library.UserId == userId &&
g.LibraryEntry.AcquisitionStatus == AcquisitionStatus.Owned
```

Mode predicates:

- `VideoGames`: `g.GameType == GameType.VideoGame`
- `BoardGames`: `g.GameType == GameType.BoardGame`
- `All`: no additional `GameType` predicate

Filter predicates:

- `platformIds`: `g.LibraryEntry.GamePlatforms.Any(gp => platformIds.Contains(gp.PlatformId))`
- `genreIds`: `g.GameGenres.Any(gg => genreIds.Contains(gg.GenreId))`
- `gameStatuses`: `g.LibraryEntry.GameStatus != null && gameStatuses.Contains(g.LibraryEntry.GameStatus.Value)`
- `playerCount`: `g.MinimumPlayers <= playerCount && g.MaximumPlayers >= playerCount`
- `availableDuration`: `g.ApproximateDuration != null && g.ApproximateDuration <= availableDuration`
- `interactionTypes`: `g.InteractionType != null && interactionTypes.Contains(g.InteractionType.Value)`
- `ratingMin`: `g.LibraryEntry.Rating >= ratingMin`

The selected item projection reads the Random Picker display fields plus
`LibraryEntry.Id`. It does not include `CreatedAt`; Random Picker has no sorting,
creation-date filter, or result-card creation-date requirement.

### Controller

`RandomPickerController` is `[ApiController]`, `[Route("random-picker")]`, and
`[Authorize]`.

It exposes:

```csharp
[HttpPost("pick")]
public async Task<IActionResult> Pick([FromBody] RandomPickerPickRequest request, CancellationToken ct)
```

Controller behavior:

- Derive the user from `User.GetSupabaseUserId()` and return `401` when absent,
  mirroring existing controllers.
- Map request DTO to `RandomPickerRequest` and call `RandomPickerService.PickAsync`.
- Let syntactically missing/empty request bodies and JSON `null` bodies fail normal
  `[ApiController]` model-binding/validation with `400`. A JSON object `{}` binds
  successfully and then fails Random Picker semantic validation because `mode` is
  missing/null, returning `400` problem details with title
  `Invalid random picker query` and detail `Mode is invalid.`.
- Map `InvalidRandomPickerQueryException` to `400` problem details with title
  `Invalid random picker query` and the exception message as detail.
- Map `RandomPickerState.Success` to JSON state `SUCCESS`, `NoCandidates` to
  `NO_CANDIDATES`, and `AllAlreadyShown` to `ALL_ALREADY_SHOWN`.
- Expose no EF entities.

### API Contract

`POST /random-picker/pick` - `[Authorize]`. Request/response are JSON.
Successful domain outcomes all return HTTP `200`; the `state` field carries the
Random Picker outcome.

Request:

```json
{
  "mode": "VideoGames",
  "shownLibraryEntryIds": ["00000000-0000-0000-0000-000000000000"],
  "platformIds": [],
  "genreIds": [],
  "gameStatuses": ["Backlog"],
  "playerCount": null,
  "availableDuration": null,
  "interactionTypes": [],
  "ratingMin": null
}
```

Request fields:

| Field | Type | Modes | Values |
| --- | --- | --- | --- |
| `mode` | string | all | `VideoGames`, `BoardGames`, `All` |
| `shownLibraryEntryIds` | Guid array | all | current frontend session's shown entry IDs; omitted/null means `[]` |
| `platformIds` | Guid array | VideoGames | optional multi-select filter |
| `genreIds` | Guid array | VideoGames | optional multi-select filter |
| `gameStatuses` | string array | VideoGames | `Backlog`, `Playing`, `Completed`, `Abandoned`, `WantToPlay` |
| `playerCount` | int | VideoGames, BoardGames, All | `>= 1`; candidate player counts must be present and contain this value |
| `availableDuration` | int | BoardGames | `>= 1`; candidate duration must be present and `<=` this value |
| `interactionTypes` | string array | BoardGames | `Cooperative`, `Competitive` |
| `ratingMin` | int | All | `1..5`; minimum-rating semantics |

Response:

```json
{
  "state": "SUCCESS",
  "result": {
    "libraryEntryId": "00000000-0000-0000-0000-000000000000",
    "gameId": "11111111-1111-1111-1111-111111111111",
    "gameType": "VideoGame",
    "name": "Example Game",
    "coverImageUrl": null,
    "rating": 5,
    "notes": null,
    "platformIds": ["22222222-2222-2222-2222-222222222222"],
    "genreIds": [],
    "gameStatus": "Backlog",
    "progressPercentage": null,
    "minimumPlayers": null,
    "maximumPlayers": null,
    "approximateDuration": null,
    "interactionType": null
  }
}
```

For `NO_CANDIDATES` and `ALL_ALREADY_SHOWN`, `result` is `null`.

Errors:

| Condition | HTTP | Problem details |
| --- | --- | --- |
| Unauthenticated request | `401` | Produced by JWT bearer handler. |
| Authenticated principal without usable `sub` | `401` | Controller returns `Unauthorized()`. |
| Empty/missing request body | `400` | Normal `[ApiController]` model-binding/validation bad request. |
| JSON `null` request body | `400` | Normal `[ApiController]` model-binding/validation bad request. |
| JSON object `{}` | `400` | Title `Invalid random picker query`, detail `Mode is invalid.`. |
| Invalid or missing `mode` | `400` | Title `Invalid random picker query`, detail `Mode is invalid.` |
| Invalid GameStatus | `400` | Detail `Game status is invalid.` |
| Invalid InteractionType | `400` | Detail `Interaction type is invalid.` |
| `playerCount < 1` | `400` | Detail `Player count must be at least 1.` |
| `availableDuration < 1` | `400` | Detail `Available duration must be at least 1.` |
| `ratingMin` outside `1..5` | `400` | Detail `Rating must be between 1 and 5.` |
| Filter sent for a mode where it is unavailable | `400` | Mode-specific detail from RandomPickerRules. |
| Malformed Guid in any Guid array | `400` | Automatic model-binding bad request. |
| Unknown/foreign well-formed IDs | `200` | Not an error; scoped predicates match or exclude nothing. |
| Unexpected exception | `500` | Existing centralized handler. |

### Frontend

Build the feature under `src/app/features/random-picker/`. No NgRx; use an
Angular service, signals, and component-local state.

Files:

```
src/app/features/random-picker/
+-- random-picker.ts
+-- random-picker.service.ts
+-- random-picker.service.spec.ts
+-- random-picker-page/
    +-- random-picker-page.ts
    +-- random-picker-page.html
    +-- random-picker-page.scss
    +-- random-picker-page.spec.ts
```

`random-picker.ts` defines the frontend request/response types and reuses shared
game enum types from `features/games/` where they already exist.

`random-picker.service.ts` exposes:

```ts
pick(request: RandomPickerPickRequest): Observable<RandomPickerPickResponse>
```

It posts to `${environment.apiBaseUrl}/random-picker/pick`. The existing auth
interceptor attaches the bearer token.

`random-picker-page` behavior:

- Protected route `/random-picker` guarded by `authGuard`.
- Add a minimal home link to the Random Picker.
- Load Platforms and Genres for VideoGame filter names using existing services.
- Keep local signals/state for mode, filters, current result, current response
  state, visible result history, loading, error, and `shownLibraryEntryIds`.
- Default mode is `All`; all filters start empty.
- A primary `Pick a game` action sends current mode, valid filters for that mode,
  and the current shown list.
- After `SUCCESS`, display the selected game card and add
  `result.libraryEntryId` to `shownLibraryEntryIds` if not already present; prepend
  the returned result to visible result history.
- `Another` sends the same current mode/filters with the updated shown list.
- Changing mode clears filters that are not available in the new mode, but does
  not clear `shownLibraryEntryIds` and does not clear visible result history.
- Changing filters does not clear `shownLibraryEntryIds` and does not clear visible
  result history.
- `NO_CANDIDATES` displays a clear no-results state with guidance to modify or
  clear filters. It does not offer a silent fallback and does not erase visible
  history.
- `ALL_ALREADY_SHOWN` displays a distinct all-shown state and an explicit
  `Reset shown history` action. Reset clears the current session's shown list and
  visible result history; it does not persist anything.
- Card display mirrors Library cards: cover or placeholder, name, type badge,
  rating when present, VideoGame details when present, BoardGame details when
  present, and optional notes.
- Result-history display uses semantic hierarchy, not final brand styling:
  current/newest result is primary/highlighted; previous results are
  secondary/muted. Feature 008 owns the full visual-design system.
- Provide edit/navigation actions to existing management pages:
  `/video-games` for VideoGames and `/board-games` for BoardGames. No deep-link
  edit route is introduced.
- A `401` from the Random Picker, Platforms, or Genres API clears the local
  session and navigates to `/login`, consistent with existing features.

Filter UI by mode:

| Mode | UI controls |
| --- | --- |
| All | Minimum rating: Any / 1+ / 2+ / 3+ / 4+ / 5+; Player count number input |
| VideoGames | Platform multi-select, Genre multi-select, GameStatus multi-select, Player count number input |
| BoardGames | Player count number input, Available duration number input, InteractionType multi-select |

Do not show unavailable controls for the selected mode.

### Navigation

- Add route `{ path: 'random-picker', component: RandomPickerPage, canActivate:
  [authGuard] }` to `app.routes.ts`.
- Add a minimal `Random Picker` `routerLink` on the authenticated home view.
- Add a minimal `Back to home` link on the Random Picker page.
- Do not introduce a full application shell in this feature.

### Database / EF Core

A schema migration is required during the targeted amendment implementation.

The existing schema already contains the preferred storage columns for VideoGame
player counts:

- `games.minimum_players`
- `games.maximum_players`

However, the current Feature 005 CHECK constraint
`ck_games_board_columns_video_null` prohibits any VideoGame row from storing
non-null player-count values. The implementation must add the smallest EF Core
migration that relaxes/replaces that constraint so VideoGame rows may legally use
`minimum_players` and `maximum_players`, while preserving these rules:

- BoardGame rows still require both player counts.
- VideoGame rows may have both player counts null.
- If a VideoGame row has either player-count value, both must be present and
  satisfy `1 <= minimum_players <= maximum_players`.
- VideoGame rows still must keep `approximate_duration` and `interaction_type`
  null.

Do not add duplicate VideoGame player-count columns, a subtype table, triggers,
or a second migration workflow.

The schema already contains all other fields required for candidate eligibility
and filters:

- `library_entries.acquisition_status`
- `library_entries.rating`
- `library_entries.game_status`
- `library_entries.progress_percentage`
- `game_platforms`
- `game_genres`
- `games.game_type`
- `games.minimum_players`
- `games.maximum_players`
- `games.approximate_duration`
- `games.interaction_type`
- `games.cover_image_url`

## Acceptance Criteria

### Backend

1. `POST /random-picker/pick` requires authentication.
2. The endpoint derives the user only from the validated JWT `sub`.
3. A user with no Library receives `NO_CANDIDATES`, and no `libraries` row is
   created by the read.
4. Only Owned entries are eligible in every mode.
5. Wishlist and Interested entries never appear, even when they otherwise match
   filters.
6. VideoGames mode returns only owned VideoGames.
7. BoardGames mode returns only owned BoardGames.
8. All mode mixes owned VideoGames and BoardGames without weighting.
9. VideoGames mode applies Platform, Genre, GameStatus, and player-count filters
   with OR within multi-select filter types and AND across filter types.
10. BoardGames mode applies player count, available duration, and InteractionType
    filters with approved semantics.
11. All mode applies minimum Rating and player count; player count works across
    VideoGames and BoardGames using the same range semantics.
12. Missing optional metadata does not satisfy an active filter requiring it.
13. Previously shown LibraryEntry IDs are excluded while unshown matching
    candidates remain.
14. `NO_CANDIDATES` and `ALL_ALREADY_SHOWN` are distinct responses.
15. Unknown or foreign well-formed IDs do not leak existence and do not produce a
    special error.
16. Invalid mode, invalid enum values, invalid numeric ranges, and unavailable
    mode/filter combinations return predictable `400` problem details.
17. No Random Picker state is persisted.
18. No Random Picker state/history persistence is added.
19. A targeted EF Core migration relaxes/replaces the current VideoGame-player-null
    CHECK constraint without adding duplicate columns or tables.

### Frontend

1. `/random-picker` is protected by `authGuard`.
2. The authenticated home page links to Random Picker.
3. The page supports All, VideoGames, and BoardGames modes.
4. The page shows only the filters available for the selected mode.
5. Pick and Another send the current mode, current filters, and current
   `shownLibraryEntryIds` to the backend.
6. A successful result is displayed as a game card, its `libraryEntryId` is added
   to the shown list, and the result is prepended to visible session history.
7. Visible history is newest-first; Another prepends the newly selected result.
8. The newest/current result has primary/highlighted emphasis and previous results
   are secondary/muted.
9. Changing mode or filters does not clear the shown list or visible history.
10. `NO_CANDIDATES` and `ALL_ALREADY_SHOWN` display distinct messages and preserve
    visible history.
11. The explicit reset action clears shown IDs and visible history only when the
    user asks for it.
12. Session state is volatile and clears when leaving/refreshing/recreating the
    page.
13. `401` handling matches existing authenticated features.

## Testing

### Backend unit tests

Add `RandomPickerRulesTests` to `GameLibrary.Core.Tests` covering:

- Valid and invalid mode parsing.
- GameStatus and InteractionType exact enum parsing and deduplication.
- `playerCount`, `availableDuration`, and `ratingMin` boundary validation.
- Null/empty arrays becoming empty lists.
- Deduplication of Guid arrays.
- Rejection of unavailable filters for each mode, while accepting `playerCount` in
  VideoGames, BoardGames, and All modes.
- Successful parse for each valid mode/filter combination.

### Backend integration tests

Add Random Picker endpoint tests to `GameLibrary.IntegrationTests` using real
PostgreSQL and the existing JWT test-token pattern. Required cases:

- Unauthenticated request returns `401`.
- Empty user returns `NO_CANDIDATES` and read does not create a Library row.
- User isolation with two users: another user's owned matching games are never
  selected, and another user's shown IDs do not affect the caller's results.
- Owned-only eligibility: Wishlist/Interested games are excluded.
- VideoGames mode filters by Platform, Genre, and GameStatus with OR/AND
  semantics, and by player count using `MinimumPlayers <= P <= MaximumPlayers`.
- BoardGames mode filters by player count, available duration, and InteractionType;
  null duration does not match an active duration filter.
- All mode includes both game types and applies minimum-rating semantics and
  cross-type player-count semantics.
- Active player-count filters exclude any VideoGame or BoardGame candidate missing
  player-count metadata.
- Filtered `NO_CANDIDATES`: a user with an existing Library and Owned games sends
  valid active filters that match none of those games and receives
  `NO_CANDIDATES`. This is separate from the empty-user/no-Library case.
- Mode/filter compatibility errors return `400`.
- Representative API-boundary validation returns `400` for missing/invalid mode,
  invalid GameStatus, invalid InteractionType, `playerCount < 1`,
  `availableDuration < 1`, `ratingMin` outside `1..5`, an active filter
  unavailable for the selected mode, malformed Guid values in Guid arrays, empty
  request body, JSON `null` request body, and `{}` with missing mode.
- A single matching unshown candidate returns `SUCCESS` with that candidate.
- Shown candidate exclusion: when two eligible matching games exist and one
  candidate's `LibraryEntry.Id` is supplied in `shownLibraryEntryIds`, the endpoint
  returns `SUCCESS` with the other unshown candidate. Do not rely only on the
  `ALL_ALREADY_SHOWN` case to prove exclusion.
- All matching candidates in `shownLibraryEntryIds` returns `ALL_ALREADY_SHOWN`.
- Unknown or foreign well-formed Platform/Genre/shown IDs do not leak existence.
- The targeted migration preserves the existing table set, reuses
  `games.minimum_players`/`games.maximum_players`, relaxes the VideoGame
  player-null CHECK, preserves BoardGame player requirements, and rejects invalid
  VideoGame player-count persisted states with real PostgreSQL constraints.

The integration tests must distinguish all three empty/exhaustion semantics:
no eligible owned games exist, eligible owned games exist but active filters
produce zero candidates, and candidates exist but all matching candidates have
already been shown.

Tests that involve random selection should assert eligibility and state, not a
specific randomly selected game unless the fixture intentionally leaves exactly
one eligible unshown candidate.

### Frontend tests

Add tests for:

- `RandomPickerService` posts the expected body to `/random-picker/pick`.
- Mode changes show the correct filter controls, clear unavailable filters, and
  preserve accumulated shown IDs and visible history. At minimum, verify that a
  successful result's `libraryEntryId` remains in `shownLibraryEntryIds` after a
  mode change and is still sent on the next Pick request.
- Pick success displays the result, appends `libraryEntryId` to shown IDs, and
  prepends the result to visible history.
- Another sends the accumulated shown history.
- Another prepends the new result above previous results.
- Filter changes do not clear shown IDs or visible history.
- `NO_CANDIDATES` and `ALL_ALREADY_SHOWN` preserve visible history.
- A fresh page/component instance starts a new volatile session: after one
  instance accumulates shown IDs and is destroyed, a newly created Random Picker
  page instance starts with an empty `shownLibraryEntryIds` list. Do not introduce
  persistence to test this.
- `NO_CANDIDATES` and `ALL_ALREADY_SHOWN` render distinct states.
- Reset shown history clears the in-memory shown list and visible history only
  after explicit user action.
- `401` responses clear local auth session and navigate to `/login`.

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
```

Integration tests require `ConnectionStrings:Test` pointing at a real PostgreSQL
database. EF Core InMemory is not an acceptable substitute for the integration
scenarios.

## Risks / Notes

- The result is intentionally random; tests must avoid asserting deterministic
  selection when more than one candidate is eligible.
- The frontend tracks `LibraryEntry` IDs for shown-history exclusion, while
  existing management routes use `Game` IDs. The Random Picker result includes
  both identifiers to keep each boundary explicit.
- Clearing shown history is intentionally explicit. Do not silently clear it when
  filters or mode change.
- If implementation discovers that the approved specs conflict with the current
  code, stop and report the conflict instead of changing the product behavior.
