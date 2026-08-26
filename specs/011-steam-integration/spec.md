# 011 - Steam Integration

## Status

`Approved`

This approved specification defines the MVP behavior for Steam account linking and
explicit Steam-owned game import.

## Feature

Steam Integration.

## Objective

Make it easier for an authenticated user to add Steam-owned VideoGames without
turning Steam into the application's authentication provider or an automatic sync
source.

After this feature is implemented, an authenticated user can:

1. Start a Steam link flow from Game Library.
2. Authenticate/consent on Steam without entering Steam credentials into Game
   Library.
3. Return to Game Library with a verified SteamID64 linked to their private
   Library.
4. Preview visible games returned by Steam for the linked account.
5. Select Steam games to import.
6. Import selected games as normal Owned VideoGames using a user-owned `Steam`
   Platform.
7. Avoid duplicate imports for the same Steam AppID in the same Library.
8. Unlink the Steam account without deleting imported games.

Steam integration is optional assistance. Manual VideoGame creation remains fully
supported and authoritative.

## Authority

Product Specification -> Domain Specification -> Architecture Specification ->
Feature Specification -> Implementation.

Feature 011 uses the Product, Domain and Architecture amendments dated
2026-08-25. Higher-authority behavior remains authoritative if a conflict is found
during implementation.

## Official Steam Sources Reviewed

Official/current Steam documentation used for this specification:

- `IPlayerService/GetOwnedGames`: https://partner.steamgames.com/doc/webapi/IPlayerService#GetOwnedGames
- Steam Web API overview and host/key behavior: https://partner.steamgames.com/doc/webapi_overview
- Steam Web API key authentication: https://partner.steamgames.com/doc/webapi_overview/auth
- Steam Web API error/status behavior: https://partner.steamgames.com/doc/webapi_overview/responses
- Steam user authentication and OpenID: https://partner.steamgames.com/doc/features/auth#website
- Steam Community Web API/OpenID page: https://steamcommunity.com/dev
- Steam library asset documentation: https://partner.steamgames.com/doc/store/assets/libraryassets

Relevant facts from those sources:

- Steam users are identified by a 64-bit numeric Steam ID.
- Steam can act as an OpenID 2.0 provider for browser-based SteamID verification.
- The Steam OpenID claimed identifier has the form
  `https://steamcommunity.com/openid/id/<steamid>`.
- Steam OpenID lets the app verify a user's SteamID without requesting or storing
  the user's Steam username or password.
- Steam Web API methods commonly require a Web API key. Keys are sensitive and must
  stay server-side.
- `IPlayerService` is a Steam Web API Service interface; methods in this
  interface should be called with the `input_json` parameter.
- `IPlayerService/GetOwnedGames` requires `key`, `steamid`, `include_appinfo`,
  `include_played_free_games`, and supports `appids_filter`.
- `include_appinfo=true` requests additional details such as name and icon.
- `include_played_free_games=true` includes free games the user has played; free
  games are excluded by default.
- `GetOwnedGames` returns owned games only if the user's owned games/game details
  are visible to the caller.
- Steam Web API can return standard HTTP errors including `400`, `401`, `403`,
  `429`, `500`, and `503`.
- The partner host `partner.steam-api.com` is intended for secure publisher servers
  with publisher keys; the public host `api.steampowered.com` is the MVP default for
  a normal Steam Web API user key.
- Steam library capsules exist for published store pages, but this specification does not
  rely on undocumented CDN URL patterns for cover images.

## Current Repository Assessment

The repository currently has Feature 010 implemented and follows the approved
two-project backend architecture plus Angular PWA frontend.

Observed backend state:

- Backend projects remain exactly `GameLibrary.Api` and `GameLibrary.Core`.
- `Program.cs` registers controllers, ProblemDetails, CORS, Supabase JWT bearer
  validation, EF Core/Npgsql, Core services, Feature 010 typed `HttpClient`
  provider wiring, and endpoint-specific rate limiting.
- Business endpoints derive the user only from the validated Supabase JWT `sub` via
  `GetSupabaseUserId()` and return `401` when absent.
- `GameLibraryDbContext` maps `libraries`, `platforms`, `games`,
  `library_entries`, `genres`, joins, and `play_log_entries` with explicit
  snake_case names and EF Core migrations as schema authority.
- `VideoGameService` creates/updates/deletes normal VideoGames, enforces user
  isolation, creates Game + LibraryEntry together, and scopes Platform resolution to
  the caller's Library.
- `PlatformService`/`LibraryService` already provide user-owned Platform and lazy
  Library behavior that Steam import can reuse.
- `CoverImageSearchService` and `ICoverImageSearchClient` provide the current
  provider-boundary pattern for deterministic tests and backend-only external HTTP.
- E2E mode already substitutes Feature 010 external behavior without calling Brave.

Observed frontend state:

- Angular standalone components, services, signals/local state and no NgRx.
- `ManageHub` is the natural entry point for Steam integration because it already
  groups catalog-management actions.
- `VideoGamesService` uses `HttpClient` against `${environment.apiBaseUrl}` with
  auth handled by the existing token interceptor.
- VideoGame Add/Edit uses dialog forms. Steam import should create games through a
  separate Manage flow rather than complicating quick-add dialogs.
- Existing cover-search UI can remain the way users add covers after import.

## In Scope

- One linked Steam account per user Library.
- Steam OpenID link flow that verifies the SteamID64 without collecting Steam
  passwords.
- Backend-only Steam Web API client for `IPlayerService/GetOwnedGames`.
- Explicit preview of visible Steam games for the linked account.
- Explicit selected import of previewed Steam games as Owned VideoGames.
- Automatic creation/reuse of the user's `Steam` Platform during import.
- Per-Library duplicate prevention using Steam AppID.
- Unlinking the Steam account without deleting imported games.
- Manage-page Steam UI with link status, link/unlink actions, preview state,
  candidate selection, import result summary, loading, error and privacy/no-visible
  games states.
- EF Core migration for Steam account/link state/import reference persistence.
- Unit, integration, frontend and E2E tests using deterministic Steam substitution.
- README updates during implementation documenting configuration and verification.

## Out of Scope

Feature 011 must not introduce:

- Steam as a Game Library login provider.
- Direct Angular calls to Steam Web API.
- Steam Web API keys in Angular, API responses or logs.
- Asking for or storing Steam passwords, Steam session cookies or Steam access
  tokens.
- Background synchronization, scheduled imports, polling, webhooks or incremental
  sync jobs.
- Automatic import without user review and selection.
- Automatic PlayLogEntries from Steam playtime.
- Automatic gameplay sessions, analytics or Random Picker weighting from Steam.
- Steam achievements.
- Steam wishlist import.
- Steam friends, family sharing or social features.
- Steam store price/purchase history.
- Steam Deck compatibility metadata.
- Steam cover/image CDN URL derivation from undocumented URL patterns.
- A global shared game catalog.
- BoardGame imports.
- Image upload/storage, object storage or CDN infrastructure.
- New backend application projects.
- Redis, queues, distributed locks, workers, CQRS/MediatR, generic repositories,
  GraphQL, Elasticsearch/OpenSearch, NgRx or a new auth system.

## Product Decisions

### Link Model

A Game Library user links Steam to their existing authenticated account. Steam does
not authenticate the user into Game Library. Supabase Auth remains the only Game
Library authentication provider.

Each Library may have zero or one linked Steam account. A SteamID64 may be linked
to at most one Game Library Library at a time. Attempting to link a SteamID64 that
is already linked elsewhere returns `409 Steam account already linked`.

Relinking requires explicit unlink first. If the caller's Library already has a
SteamAccount, `POST /steam/link-requests` must not start a replacement or
reverification flow. It returns `409` with title `Steam account already linked` and
detail `Unlink the current Steam account before linking another account.` This
applies whether the user intends to link the same SteamID64 or a different
SteamID64.

Unlinking deletes only the linked Steam account record. It does not delete imported
VideoGames, their Platform, PlayLogEntries, or `steamAppId` import references.
After unlinking, another Steam account may be linked. Existing imported
`steamAppId` values remain part of the caller's Library history/import identity. If
the newly linked Steam account owns an AppID imported under a previous link, that
candidate appears already imported. Unlinking releases the global SteamID64
uniqueness so that the SteamID64 may later be linked to another Library, but it
does not alter imported data in the previous Library.

### Import Model

Steam preview is read-only. It does not create a Library, Platform, Game or
LibraryEntry.

Steam import is a separate explicit action. The user selects candidates and submits
the selected Steam AppIDs. The backend refetches the selected AppIDs from Steam and
uses Steam-returned data for import; it does not trust names, playtime or ownership
metadata sent by Angular.

Imported games are normal VideoGames with:

- `GameType = VideoGame`.
- `Name = Steam app name`, trimmed and validated by existing VideoGame name rules.
- `CoverImageUrl = null`.
- `MinimumPlayers = null`.
- `MaximumPlayers = null`.
- `AcquisitionStatus = Owned`.
- `PlatformIds = [Steam Platform id]`.
- `GenreIds = []`.
- `GameStatus = null`.
- `ProgressPercentage = null`.
- `Rating = null`.
- `Notes = null`.
- `SteamAppId = imported Steam AppID`.

The import service reuses an existing user-owned Platform named exactly `Steam` by
case-insensitive Platform name semantics, or creates it when importing at least one
new game. This preserves the invariant that Owned VideoGames require at least one
Platform.

### Duplicate Handling

A Steam candidate whose AppID is already referenced by a LibraryEntry in the
caller's Library is shown as already imported and is not selectable for import.

If an import request includes an already-imported AppID, the backend skips it and
reports it in the result summary rather than failing the whole request.

If two import requests race for the same AppID, the database unique index on
`(library_id, steam_app_id)` remains defense in depth, but normal control flow
must not rely on intentionally triggering a unique-index exception. Concurrent
imports for the same Library serialize through the per-Library database lock
described in the Import section.

Manual VideoGames without `steamAppId` are not treated as duplicates by name. Game
names are not unique in the approved domain model, and Feature 011 does not add
name-based matching or merge behavior.

`library_entries.steam_app_id` is a pragmatic Steam-first placement for Feature
011. Do not introduce a generic external integration reference table until a
future approved feature designs a second store integration.

### Steam Playtime

Steam may return playtime fields for owned games. Feature 011 may display
Steam-reported playtime in preview/import results as external reference text.

Feature 011 must not persist Steam playtime in Game, LibraryEntry or PlayLogEntry.
It must not create PlayLogEntries, infer GameStatus or ProgressPercentage, change
Random Picker eligibility, or influence Random Picker selection.

### Covers

Imported Steam games do not automatically set `CoverImageUrl`.

Rationale: Steam docs reviewed for this specification document library assets but do not
provide an official stable Web API field for cover/capsule URLs in
`GetOwnedGames`. Feature 010 cover search remains the supported way to assist cover
selection after import.

## Persistence

Create one EF Core migration, for example `AddSteamIntegration`.

### `steam_accounts`

Columns:

| Column | Type | Rules |
| --- | --- | --- |
| `id` | `uuid` | Primary key, application generated. |
| `library_id` | `uuid` | Required FK to `libraries.id`; unique. |
| `steam_id64` | `varchar(20)` | Required decimal string parsed from verified Steam OpenID claimed id. |
| `linked_at` | `timestamp with time zone` | Server-assigned UTC timestamp. |

Indexes/constraints:

- Unique `ix_steam_accounts_library_id`.
- Unique `ix_steam_accounts_steam_id64`.
- Check `steam_id64` is numeric and at most 20 digits.
- FK `steam_accounts.library_id -> libraries.id` with `ON DELETE CASCADE`.

`SteamAccount` is subordinate integration state. If a Library is ever deleted, the
linked SteamAccount must be deleted automatically and must not block Library
deletion. This does not change unlink semantics: unlink deletes only the
SteamAccount and leaves imported games, `steam_app_id` references, the `Steam`
Platform and PlayLogEntries unchanged.

### `steam_link_requests`

Columns:

| Column | Type | Rules |
| --- | --- | --- |
| `id` | `uuid` | Primary key, application generated. |
| `library_id` | `uuid` | Required FK to `libraries.id`. |
| `state_hash` | `text` | Required hash of the one-time state token. |
| `created_at` | `timestamp with time zone` | Server-assigned UTC timestamp. |
| `expires_at` | `timestamp with time zone` | Required; default lifetime 10 minutes. |
| `consumed_at` | `timestamp with time zone` | Null until a successful callback atomically consumes the request. |

Indexes/constraints:

- Unique `ix_steam_link_requests_state_hash`.
- Index `ix_steam_link_requests_library_id`.
- FK `steam_link_requests.library_id -> libraries.id` with `ON DELETE CASCADE`.
- Check `expires_at > created_at`.

The raw state token is cryptographically random, unguessable, sufficient entropy,
URL-safe, and returned only in browser URLs. It is not stored in plain text.
Expired/consumed states are invalid. State lookup uses the exact hash of the raw
state token. Do not expose raw state tokens or hashes in logs.

Raw state necessarily appears transiently in browser URLs during the OpenID round
trip. Minimize its lifetime and do not persist it outside `state_hash`.

Only a fully verified Steam assertion followed by successful atomic state claim and
link creation sets `consumed_at`. Failed or unverified callbacks do not consume
link state.

No background cleanup job is required. The service may delete expired/consumed rows
opportunistically when creating a new link request.

### `library_entries.steam_app_id`

Add nullable `steam_app_id` to `library_entries`.

Rules:

- Type: `bigint`, because Steam documents AppID as `uint32`.
- Check: `steam_app_id IS NULL OR (steam_app_id >= 1 AND steam_app_id <= 4294967295)`.
- Filtered unique index:
  `ix_library_entries_library_id_steam_app_id` on `(library_id, steam_app_id)` where
  `steam_app_id IS NOT NULL`.
- The column is set only by Steam import in this feature.
- Existing rows have `null`; no backfill is required.
- Deleting a LibraryEntry deletes the imported game as before; no separate Steam
  imported-game entity is left behind.
- `0`, negative values and values above unsigned 32-bit max are invalid.
- Use a C# representation that safely handles the full unsigned 32-bit range and
  serializes cleanly.

## Configuration

Use ASP.NET Core configuration section `SteamIntegration`.

Required for real Steam OpenID linking:

```text
SteamIntegration__Enabled=true
SteamIntegration__PublicApiBaseUrl=http://localhost:5000
SteamIntegration__FrontendBaseUrl=http://localhost:4200
```

Optional configuration with defaults:

```text
SteamIntegration__OwnedGamesUrl=https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/
SteamIntegration__OpenIdProviderUrl=https://steamcommunity.com/openid/login
SteamIntegration__OpenIdVerifyUrl=https://steamcommunity.com/openid/login
SteamIntegration__FrontendSuccessPath=/manage/steam?steamLink=success
SteamIntegration__FrontendFailurePath=/manage/steam?steamLink=failure
SteamIntegration__TimeoutSeconds=5
SteamIntegration__LinkStateLifetimeMinutes=10
```

Required only for Steam library preview/import:

```text
SteamIntegration__WebApiKey=<secret Steam Web API key>
```

Rules:

- Do not commit real Steam Web API keys.
- Steam Web API keys remain backend-only and are configured through user-secrets,
  environment variables or deployment secrets.
- Angular receives no Steam Web API key, publisher key or provider credential.
- Missing Steam configuration must not prevent the API from starting.
- If `Enabled=false`, or required OpenID/public-base configuration is unavailable,
  Steam integration is disabled: status returns clear unavailable feedback,
  link/start/callback cannot proceed, and preview/import are unavailable.
- If OpenID configuration is available but `WebApiKey` is missing, status, link,
  start, callback, unlink and linked-account display remain available; preview and
  import return predictable unavailable feedback.
- The public Steam Web API host is the MVP default. The partner host may be used
  only when the deployment has a valid publisher key and explicit configuration.
- Server-side timeout is five seconds unless implementation findings justify a spec
  amendment.
- `PublicApiBaseUrl` is the trusted public API origin/base used to construct
  OpenID `return_to` and `realm`; do not derive trusted callback origin solely from
  arbitrary request `Host` or `Forwarded` headers unless trusted forwarded-header
  configuration already exists.
- `FrontendBaseUrl` must be a valid `http` or `https` origin. Use
  deployment-appropriate HTTPS in production.
- Success/failure redirect paths must be server-owned relative paths. If paths are
  configurable, they must remain relative and must not specify another origin.
- Callback payloads and Angular requests must not control frontend redirect
  destinations. The backend constructs final redirect URLs from the trusted
  `FrontendBaseUrl` plus server-owned relative paths.

## API

Routes follow the existing no-prefix convention. All JSON application endpoints
require normal Supabase JWT bearer authentication. Redirect endpoints are browser
navigation endpoints protected by one-time link state, not by Supabase bearer auth.

| Method | Route | Auth | Success | Errors |
| --- | --- | --- | --- | --- |
| `GET` | `/steam/status` | Supabase JWT required | `200 SteamStatusResponse` | `401` |
| `POST` | `/steam/link-requests` | Supabase JWT required | `200 SteamLinkRequestResponse` | `401`, `409`, `503` |
| `GET` | `/steam/link/start/{rawState}` | one-time state | `302` to Steam OpenID provider | `302` frontend failure |
| `GET` | `/steam/link/callback` | one-time state + Steam OpenID verification | `302` frontend success/failure | `302` frontend failure |
| `DELETE` | `/steam/link` | Supabase JWT required | `204` | `401` |
| `POST` | `/steam/library/preview` | Supabase JWT required | `200 SteamLibraryPreviewResponse` | `400`, `401`, `404`, `503` |
| `POST` | `/steam/library/import` | Supabase JWT required | `200 SteamImportResponse` | `400`, `401`, `404`, `503` |

### Status

`GET /steam/status` response:

```json
{
  "enabled": true,
  "linkAvailable": true,
  "importAvailable": true,
  "linked": true,
  "steamId64": "76561198000000000",
  "linkedAt": "2026-08-25T12:00:00Z",
  "message": null
}
```

When not linked:

```json
{
  "enabled": true,
  "linkAvailable": true,
  "importAvailable": true,
  "linked": false,
  "steamId64": null,
  "linkedAt": null,
  "message": null
}
```

When Steam integration is disabled or required OpenID configuration is missing:

```json
{
  "enabled": false,
  "linkAvailable": false,
  "importAvailable": false,
  "linked": false,
  "steamId64": null,
  "linkedAt": null,
  "message": "Steam integration is not configured."
}
```

When OpenID linking is configured but Steam Web API access is missing:

```json
{
  "enabled": true,
  "linkAvailable": true,
  "importAvailable": false,
  "linked": true,
  "steamId64": "76561198000000000",
  "linkedAt": "2026-08-25T12:00:00Z",
  "message": "Steam library import is unavailable because Steam Web API access is not configured."
}
```

This endpoint reads only the caller's Library. If no Library exists yet, it returns
`linked=false` and does not create a Library.

### Link Request

`POST /steam/link-requests` creates the caller's Library if needed, creates a
one-time state row, and returns a Game Library API start URL. If the Library is
already linked, it returns `409` and does not create a link request.

Link request flow:

1. Generate a cryptographically secure raw state token.
2. Store only `hash(rawState)` in `steam_link_requests.state_hash`.
3. Generate an API start URL containing the raw state token.

Response:

```json
{
  "startUrl": "https://api.example.test/steam/link/start/<rawState>",
  "expiresAt": "2026-08-25T12:10:00Z"
}
```

The client navigates the browser to `startUrl`. The start endpoint validates the
state and redirects the browser to Steam OpenID. Angular does not construct Steam
OpenID parameters and does not call Steam Web API.

### Link Start

`GET /steam/link/start/{rawState}` hashes `rawState`, resolves an unexpired and
unconsumed SteamLinkRequest by `state_hash`, and constructs the Steam OpenID
request from trusted server-side configuration. It never queries by plaintext state
token and does not log the token.

The OpenID request uses the configured trusted Steam OpenID provider endpoint. Its
`openid.return_to` contains the same raw state as a callback query parameter:

```text
https://api.example.test/steam/link/callback?state=<rawState>
```

The `openid.realm` is generated from trusted server-side API/public-origin
configuration and must be reproducible during callback validation.

### Link Callback

`GET /steam/link/callback` validates:

- The state exists, is unexpired and unconsumed.
- Steam OpenID callback fields are present.
- The callback `state` query parameter is present and came from the server-generated
  callback query parameter, not from OpenID fields.
- `openid.ns` is the expected OpenID 2.0 namespace where applicable:
  `http://specs.openid.net/auth/2.0`.
- `openid.mode` exactly equals `id_res`.
- `openid.op_endpoint` exactly matches the configured trusted Steam OpenID provider
  endpoint.
- `openid.claimed_id` is present.
- `openid.identity` is present.
- `openid.identity` exactly equals `openid.claimed_id`.
- `openid.claimed_id` has exactly the trusted Steam claimed-id prefix
  `https://steamcommunity.com/openid/id/`.
- The SteamID64 parses as an unsigned 64-bit integer.
- `openid.return_to` exactly equals the server-expected callback URL generated for
  this link request, including the raw `state` query parameter.
- `openid.realm` exactly matches the configured Game Library/API realm used when
  the request was initiated.
- Steam confirms the OpenID assertion by the OpenID `check_authentication` request
  to `SteamIntegration:OpenIdVerifyUrl`.

Do not accept callback-provided provider, return or realm URLs as trusted
configuration. Game Library constructs expected values server-side.

On success, the API atomically claims/consumes the SteamLinkRequest and creates the
SteamAccount for the Library referenced by the link state in one database
transaction, then redirects to the server-constructed frontend success URL. Only
after all OpenID validation succeeds may the verified SteamID64 be linked.

On failure, the API redirects to the server-constructed frontend failure URL.
Failure responses must not expose raw OpenID payloads, state tokens, stack traces
or Steam keys. Do not log the raw OpenID payload.

If the verified SteamID64 is linked to another Library, the callback redirects to
failure and does not alter either Library.

If the Library referenced by the link state already has a SteamAccount at callback
time, the callback redirects to failure and does not alter the existing link. This
can happen if the user starts multiple link requests before completing one of them.

Successful callback consumption must be race-safe. Required semantics:

1. Validate state exists, is unexpired and unconsumed.
2. Validate OpenID callback fields.
3. Perform Steam `check_authentication`.
4. Atomically claim/consume the SteamLinkRequest.
5. Recheck that the Library is still unlinked and the SteamID64 is not linked to
   another Library.
6. Only the winner may create the SteamAccount link.

Use the smallest PostgreSQL/EF mechanism consistent with the repository, such as a
row lock inside a transaction or a conditional `UPDATE` where `consumed_at IS NULL`
and `expires_at > now`, verifying affected row count equals `1`. Do not implement
in-memory locking. Multi-instance correctness must come from PostgreSQL state.

Failed or unverified callbacks do not consume link state. Only a fully verified
Steam assertion followed by successful atomic state claim and link creation consumes
it.

### Unlink

`DELETE /steam/link` deletes the caller's SteamAccount row when present and returns
`204` even if no Steam account was linked.

It does not delete imported games, imported PlayLogEntries, Platforms or
`steam_app_id` values on existing entries.

### Preview

`POST /steam/library/preview` has an empty JSON body or no body. It requires a
linked Steam account.

Steam request:

- Endpoint default: `https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/`.
- `key=<server-side key>`.
- `format=json`.
- `input_json=<URL-encoded JSON>`.

Preview `input_json`:

```json
{
  "steamid": "76561198000000000",
  "include_appinfo": true,
  "include_played_free_games": true
}
```

The Web API key and `format` remain outside `input_json` according to Steam Service
API conventions.

Response:

```json
{
  "state": "Success",
  "candidates": [
    {
      "steamAppId": 570,
      "name": "Dota 2",
      "playtimeForeverMinutes": 1234,
      "alreadyImported": false
    }
  ],
  "message": null
}
```

States:

- `Success`: Steam returned visible candidate data. `candidates` may be empty.
- `NoVisibleGames`: Steam returned no usable games. UI text must explain that the
  Steam account may have private game details or no visible owned/played games.

Candidate normalization:

- `steamAppId` is required and must be in the positive unsigned 32-bit range
  `1..4294967295`; AppID `0` is invalid.
- `name` is required, trimmed, non-empty, and must fit existing VideoGame name
  length. Invalid candidates are skipped.
- `playtimeForeverMinutes` is optional external display data only.
- `alreadyImported` is computed by checking `library_entries.steam_app_id` in the
  caller's Library.
- Sort by name case-insensitively, then Steam AppID ascending.

Steam icon hashes may be ignored for MVP because this specification does not rely on
undocumented image URL construction.

### Import

Request:

```json
{
  "steamAppIds": [570, 730]
}
```

Rules:

- `steamAppIds` is required.
- Empty list returns `400` with detail `Select at least one Steam game to import.`
- Maximum 100 AppIDs per request.
- Duplicates are deduplicated preserving first occurrence.
- Every requested AppID must be in the positive unsigned 32-bit range
  `1..4294967295`; invalid requested AppIDs, including `0`, return normal `400`
  validation before any Steam provider call.
- The backend refetches selected AppIDs from Steam using `appids_filter`; it does
  not trust candidate names sent by Angular.
- AppIDs not returned by Steam are reported as `unavailable`.
- Already-imported AppIDs are reported as `alreadyImported`.
- Provider-returned candidates with invalid metadata are reported as `invalid`.
- Valid new AppIDs are imported together in one database transaction.
- If an unexpected database/application failure occurs while writing valid new
  games, the transaction rolls back all new imports from that request and the
  endpoint returns existing generic failure behavior rather than a false partial
  success response.
- Do not implement per-game transactions and do not retry arbitrary application
  failures.

Import maximum: the final selected maximum is `100` AppIDs per request. Current
official docs do not define a smaller hard limit, and URL-encoded `input_json` for
100 uint32 AppIDs remains reasonably sized for normal request limits. Add test
coverage for the maximum count.

Steam request for import:

- Same endpoint/key/steamid/appinfo/free-games behavior as preview.
- `input_json=<URL-encoded JSON>`.
- `appids_filter` contains only the selected AppIDs.

Import/refetch `input_json`:

```json
{
  "steamid": "76561198000000000",
  "include_appinfo": true,
  "include_played_free_games": true,
  "appids_filter": [570, 730]
}
```

Real and fake Steam clients must use the same semantic encoding. Do not switch to
an undocumented provider request form.

Import transaction/concurrency semantics for the caller's Library:

1. Validate requested AppIDs, deduplicate the request, refetch selected AppIDs from
   Steam, and separate unavailable/invalid provider candidates before database
   mutation.
2. Start a database transaction.
3. Acquire a PostgreSQL row-level lock on the caller's Library, or the smallest
   equivalent per-Library database lock using existing EF/PostgreSQL capabilities.
4. After acquiring the lock, re-query existing non-null `steam_app_id` values for
   the selected AppIDs in the caller's Library.
5. Classify rechecked matches as `alreadyImported`.
6. Determine remaining new valid games.
7. Case-insensitively look for the user-owned Platform named exactly `Steam`; reuse
   it if found.
8. Create the `Steam` Platform only if at least one new game will actually be
   imported.
9. Create all new valid Game + LibraryEntry rows.
10. Commit.

Concurrent import requests for the same Library therefore serialize. Example:
request A and request B both import AppID `570`; A obtains the lock, imports and
commits; B waits, obtains the lock, rechecks and reports `570` as
`alreadyImported`. The unique index remains defense in depth. The same per-Library
transaction/lock protects Steam Platform lookup/create and must not create duplicate
Steam Platforms. Preserve current Platform domain constraints.

Response:

```json
{
  "imported": [
    {
      "steamAppId": 570,
      "videoGameId": "00000000-0000-0000-0000-000000000001",
      "name": "Dota 2"
    }
  ],
  "alreadyImported": [730],
  "unavailable": [],
  "invalid": []
}
```

Successful imports return `200` even when some selected AppIDs are skipped as
already imported or unavailable. The UI shows a summary.

### ProblemDetails

Use existing ProblemDetails style.

| Condition | HTTP | ProblemDetails |
| --- | --- | --- |
| Unauthenticated JSON endpoint | `401` | JWT bearer handler challenge or `Unauthorized()`. |
| Authenticated principal without usable `sub` | `401` | `Unauthorized()`. |
| Steam integration disabled or missing OpenID/public-base configuration | `503` | Title `Steam integration unavailable`, detail `Steam integration is not configured.` |
| Missing Steam Web API key for preview/import | `503` | Title `Steam integration unavailable`, detail `Steam library import is unavailable because Steam Web API access is not configured.` |
| No linked Steam account for preview/import | `404` | Title `Steam account not linked`, detail `Link a Steam account before importing Steam games.` |
| Link request when Library is already linked | `409` | Title `Steam account already linked`, detail `Unlink the current Steam account before linking another account.` |
| Steam timeout | `503` | Title `Steam integration unavailable`, detail `Steam did not respond in time. Please try again.` |
| Steam non-success/malformed response | `503` | Title `Steam integration unavailable`, detail `Unable to read your Steam library right now.` |
| Invalid import request | `400` | Title `Invalid Steam import`, detail from validation. |
| Steam account linked elsewhere | `409` | Title `Steam account already linked`, detail `This Steam account is already linked to another Game Library account.` |
| Rate limit exceeded | `429` | Built-in ASP.NET Core rate-limiter response. |

## Backend Technical Requirements

Expected `GameLibrary.Core` files:

```text
GameLibrary.Core/
+-- Steam/
    +-- SteamAccount.cs
    +-- SteamLinkRequest.cs
    +-- SteamIntegrationService.cs
    +-- SteamIntegrationRules.cs
    +-- ISteamClient.cs
    +-- SteamOwnedGame.cs
    +-- SteamExceptions.cs
```

Expected `GameLibrary.Api` files:

```text
GameLibrary.Api/
+-- Controllers/
|   +-- SteamController.cs
+-- Steam/
    +-- SteamContracts.cs
    +-- SteamOptions.cs
    +-- SteamClient.cs
```

This layout is guidance, not permission to create additional projects. Use the
smallest equivalent layout that preserves the contracts and tests.

Service layering:

```text
SteamController
-> SteamIntegrationService
-> ISteamClient
-> Steam OpenID / Steam Web API
```

Core owns:

- SteamAccount and SteamLinkRequest entities.
- Steam link/import application rules.
- Steam AppID and SteamID64 validation.
- Import orchestration and creation of normal VideoGames.
- Provider-neutral Steam client boundary.

API owns:

- HTTP controller and redirect behavior.
- Steam-specific HTTP client implementation.
- Steam OpenID verification request.
- Raw Steam DTO parsing.
- Steam configuration/options.

Rules:

- Controllers remain HTTP/auth/problem-details/redirect boundary code.
- Core derives ownership only from authenticated Supabase user IDs or from a valid
  one-time link state that was created by an authenticated user.
- Core must not depend on `GameLibrary.Api`.
- Use `IHttpClientFactory` / typed HttpClient registration.
- Use cancellation tokens from ASP.NET Core action methods.
- Do not log Steam Web API keys, OpenID payloads, state tokens, Supabase tokens or
  full private library data.
- Do not accept callback-provided provider, return or realm URLs as trusted
  configuration; construct expected OpenID values server-side from trusted
  configuration.
- No retry policy is required for Feature 011 unless implementation findings
  justify a spec amendment.
- Add an ASP.NET Core built-in rate limiter for authenticated Steam JSON endpoints:
  partition by authenticated `sub`, permit 5 preview/import/link-request operations
  per minute, queue size 0.
- Do not add distributed rate limiting.

## Frontend UX

Add a Manage card/link for Steam, route `/manage/steam`.

The Steam page shows:

- Link status.
- Steam integration unavailable state when linking configuration is missing or
  disabled.
- Import unavailable state when OpenID linking is available but Steam Web API
  access is not configured.
- `Link Steam account` action when unlinked.
- Linked SteamID64 and `Unlink` action when linked.
- `Preview Steam library` action when linked.
- Candidate list with checkbox selection.
- Already-imported candidates disabled with visible label.
- Import selected action with pending state and duplicate-click protection.
- Import summary listing imported, already imported and unavailable counts.
- No-visible-games message that mentions Steam privacy/game-details visibility.
- Provider unavailable/timeout/error message with retry.
- Already-linked conflict message explaining that the user must unlink before
  linking another Steam account.

Interaction rules:

- Page load may call only `GET /steam/status`.
- Page load must not call Steam preview/import automatically.
- `Link Steam account` calls `POST /steam/link-requests` and then navigates to the
  returned `startUrl`.
- Returning from Steam uses the configured frontend success/failure URL and shows a
  non-persistent success/failure notice based on query string.
- `Preview Steam library` is explicit and repeatable.
- Changing selection does not call Steam.
- `Import selected` is disabled when no selectable candidates are selected.
- Duplicate click while preview/import is pending issues no duplicate request.
- After successful import, imported candidates become already imported or the page
  refreshes preview/status to reflect the result.
- Existing manual VideoGame Add/Edit flows remain unchanged.
- Imported games are discoverable in Library and VideoGame management through
  existing lists after import.

## Security And Privacy

- Supabase Auth remains the only Game Library login mechanism.
- Steam OpenID is used only to verify a SteamID64 for linking.
- Game Library never asks for Steam username/password and never stores Steam
  passwords, Steam session cookies or Steam access tokens.
- Steam Web API keys are backend-only secrets.
- Angular receives no Steam Web API key and does not call Steam Web API.
- The Steam provider receives only the server Web API key and linked SteamID64
  needed for `GetOwnedGames`.
- User notes, rating, PlayLogEntries, Library ID, Supabase tokens and unrelated
  private library data are not sent to Steam.
- Link state is one-time, unguessable, short-lived, stored hashed at rest, and
  consumed on successful callback.
- Raw state is required in the callback query parameter, hashed for lookup, and
  bound to `openid.return_to` by exact server-side URL comparison.
- Steam OpenID callback validation checks the expected OpenID namespace where
  applicable, `openid.mode`, trusted `openid.op_endpoint`, matching
  `openid.identity`/`openid.claimed_id`, trusted Steam claimed-id prefix, valid
  SteamID64, exact `openid.return_to`, exact `openid.realm`, and Steam
  `check_authentication` success before linking.
- State consumption is atomic and backed by PostgreSQL state; failed verification
  does not link or consume state.
- Frontend redirects are constructed from trusted `FrontendBaseUrl` plus
  server-owned relative paths. Callback payloads and Angular requests cannot supply
  redirect destinations.
- Existing SteamAccount links cannot be silently replaced; users must unlink before
  starting another link flow.
- A SteamID64 already linked to another Library cannot be linked silently to the
  caller.
- Preview and import require normal authenticated app access and a Steam account
  linked to the caller's Library.
- Preview and import always use the caller's linked SteamID64. The caller cannot
  provide an arbitrary SteamID64 for preview or import.
- Imported games are user-owned data and must remain scoped to the caller's Library.

## Testing

### Backend Unit Tests

Add focused unit tests for pure Steam rules/service behavior:

- SteamID64 parsing from claimed id.
- Rejection of malformed claimed id.
- Steam AppID range validation.
- Rejection of AppID `0` and values above unsigned 32-bit max.
- Import request deduplication and max-count validation.
- Preview candidate normalization skips missing/blank/overlong names.
- Preview candidate normalization skips invalid AppID `0`.
- Preview candidate normalization marks already-imported AppIDs.
- Import planning separates new, already-imported, unavailable and invalid AppIDs.
- Steam playtime is not mapped to PlayLogEntry, GameStatus or ProgressPercentage.
- OpenID callback validation rejects wrong `openid.mode`, wrong `openid.op_endpoint`,
  wrong `openid.return_to`, wrong `openid.realm`, `identity != claimed_id`,
  malformed claimed id, invalid SteamID64 and Steam `check_authentication=false`.
- OpenID callback validation rejects missing, unknown, expired and consumed state.
- OpenID callback validation accepts a complete valid fake Steam assertion.

Mock only the `ISteamClient` external provider boundary.

### Backend Integration Tests

Do not call real Steam OpenID or Steam Web API from integration tests.

Use deterministic provider substitution or fake `HttpMessageHandler` behavior.
Required cases:

- `GET /steam/status` unauthenticated returns `401`.
- `GET /steam/status` with no Library returns `linked=false` and creates no Library.
- `GET /steam/status` distinguishes disabled integration from missing Web API import
  availability.
- `POST /steam/link-requests` creates Library/link state and returns API start URL.
- `POST /steam/link-requests` stores only `state_hash`, not raw state.
- `GET /steam/link/start/{rawState}` hashes raw state, resolves state by hash and
  generates `openid.return_to` containing that raw state.
- Link callback with valid fake OpenID verification creates one SteamAccount.
- Link callback validates namespace where applicable, mode, provider endpoint,
  claimed id, identity, return_to, realm and Steam `check_authentication`.
- Link callback rejects expired/consumed state.
- Link callback rejects missing and unknown state.
- Link callback rejects malformed claimed id.
- Link callback rejects wrong mode, wrong provider endpoint, wrong return_to, wrong
  realm, identity mismatch, invalid SteamID64 and false Steam verification.
- Parallel callback attempts with the same state allow only one consume/link.
- Already-linked Library cannot start another link request.
- Linking the same account requires unlink first.
- Linking a different account requires unlink first.
- Unlink allows subsequent linking.
- Unlink leaves imported `steam_app_id` values unchanged.
- After linking another account, matching previously imported AppIDs remain
  alreadyImported.
- Linking a SteamID64 already linked to another Library fails without changing either
  account.
- `DELETE /steam/link` unlinks without deleting imported games.
- Preview without linked account returns `404`.
- Preview/import with OpenID configured but missing Web API key returns `503`.
- Preview with linked account returns normalized candidates and already-imported
  flags.
- Preview no usable games returns `NoVisibleGames`.
- Preview provider timeout maps to `503`.
- Import without selected AppIDs returns `400`.
- Import rejects AppID `0` and values above unsigned 32-bit max with `400` before
  provider call.
- Import rejects more than 100 selected AppIDs.
- Steam client uses URL-encoded `input_json` for preview and import, with Web API
  key and `format=json` outside `input_json`.
- Import creates a `Steam` Platform when missing.
- Import reuses existing case-insensitive `Steam` Platform when present.
- Concurrent imports for the same Library/AppID serialize through PostgreSQL and
  produce one imported game, no duplicate LibraryEntry, and an `alreadyImported`
  outcome for the second request.
- Import creates Owned VideoGames with one Platform and valid default metadata.
- Import sets `library_entries.steam_app_id`.
- Import skips already-imported AppIDs.
- Import does not create PlayLogEntries.
- Two-user isolation: one user's linked Steam account, preview and imports are not
  visible or mutable by another user.
- Database migration includes the new tables/column/indexes/constraints.
- Persistence verifies Library deletion cascades SteamAccount and SteamLinkRequest,
  and SteamAccount does not block deletion. Do not add a new Library delete product
  flow solely for this test; test FK behavior at persistence level as appropriate.
- Rate limit exhaustion returns `429`.
- Missing Steam configuration returns `503` while existing game CRUD still works.

### Frontend Tests

Use existing Vitest/Angular patterns. Required coverage:

- Steam service calls Game Library API routes only.
- No Steam Web API URL or key appears in frontend request construction.
- Steam page loads status on entry.
- Disabled integration and import-unavailable status render clear messages.
- Page load does not preview/import automatically.
- Link action calls `POST /steam/link-requests` and navigates to returned start URL.
- Already-linked conflict renders unlink-first guidance.
- Linked/unlinked states render correctly.
- Unlink action calls `DELETE /steam/link` and updates state.
- Preview action is explicit and has loading/duplicate-click protection.
- Preview candidates render with selectable checkboxes.
- Already-imported candidates are disabled and labeled.
- No-visible-games state renders privacy/help text.
- Import selected is disabled with no selection.
- Import selected calls `POST /steam/library/import` with selected AppIDs only.
- Import loading has duplicate-click protection.
- Import summary renders imported/already-imported/unavailable counts.
- Provider errors preserve current selection where practical and allow retry.
- `401` follows existing session-expired behavior.

### Playwright E2E

Extend the existing Playwright suite with one deterministic journey.

Do not call real Steam.

Journey:

1. Login in deterministic E2E auth mode.
2. Open Manage -> Steam.
3. Link a fake Steam account through deterministic test-only redirect/provider
   substitution.
4. Preview deterministic Steam games.
5. Select two candidates.
6. Import selected.
7. Verify import summary.
8. Open Library or Video games and verify imported games appear as Owned
   VideoGames with Platform `Steam`.
9. Verify no Play Log entry was created solely by import.

Test-provider safety:

- Deterministic Steam behavior is test-only.
- It requires explicit non-Production E2E configuration.
- It is unavailable in Production.
- It does not create public production fake-Steam endpoints.
- It reuses Feature 008/010 E2E safety conventions.

## Acceptance Criteria

### Linking

1. Authenticated users can start a Steam link flow from `/manage/steam`.
2. Game Library never asks for a Steam password.
3. A successful Steam OpenID callback stores the verified SteamID64 for the caller's
   Library.
4. Each Library can have at most one linked Steam account.
5. The same SteamID64 cannot be linked to two Libraries.
6. Expired, consumed or malformed link callbacks do not create or change links.
7. Missing, unknown or concurrently reused state does not create or change links.
8. OpenID callback validation checks trusted namespace/mode/provider/identity,
   claimed-id prefix, return_to, realm and Steam `check_authentication` before
   linking.
9. Callback state is carried only as server-generated raw state in the callback
   query parameter, looked up by hash and bound to exact `openid.return_to`.
10. Successful state consumption is atomic and PostgreSQL-backed.
11. Failed or unverified callbacks do not consume state.
12. Already-linked Libraries cannot start another link flow until they unlink.
13. Users can unlink Steam without deleting imported games.
14. Unlink allows a subsequent link and preserves prior `steam_app_id` import
   identity.

### Preview

1. Preview requires an authenticated user and a linked Steam account.
2. Preview is explicit; it does not run on page load.
3. The backend calls `IPlayerService/GetOwnedGames` with `include_appinfo=true` and
   `include_played_free_games=true`.
4. Steam Web API is called only by the backend.
5. Candidate names and AppIDs are normalized and invalid candidates are skipped.
6. Already-imported candidates are identified by `steam_app_id` in the caller's
   Library.
7. Private/no-visible Steam game details produce clear no-visible-games feedback.
8. OpenID linking can be available while preview/import return Web API
   configuration unavailable.

### Import

1. Import requires explicit selected Steam AppIDs.
2. The backend refetches selected AppIDs from Steam before import.
3. Imported games are Owned VideoGames.
4. Imported games have at least one Platform.
5. The user's `Steam` Platform is reused or created only when needed for import.
6. Imported games have `CoverImageUrl = null`.
7. Imported games do not receive genres, rating, notes, GameStatus, Progress or
   player-count metadata automatically.
8. A Steam AppID can be imported at most once per Library.
9. Manual same-name games without Steam AppID are not merged or overwritten.
10. Steam import does not create PlayLogEntries.
11. Steam playtime does not affect Random Picker behavior.
12. Steam AppIDs must be in the positive unsigned 32-bit range `1..4294967295`.
13. Import requests are limited to 100 AppIDs and reject invalid requested AppIDs
    before provider calls.
14. Steam import/refetch uses URL-encoded `input_json` with `appids_filter` for the
    selected AppIDs.
15. Concurrent imports for the same Library serialize through PostgreSQL locking,
    recheck existing `steam_app_id` values after the lock, and do not create
    duplicate LibraryEntries or duplicate Steam Platforms.
16. Normal per-item outcomes include imported, alreadyImported, unavailable and
    invalid; unexpected write failures roll back new imports from that request.

### Security

1. Supabase Auth remains the only Game Library auth provider.
2. Steam Web API keys are backend-only secrets.
3. Angular never receives Steam Web API credentials.
4. Angular never calls Steam Web API endpoints directly.
5. The backend does not send notes, ratings, Play Log, Library IDs, Supabase tokens
   or unrelated private library data to Steam.
6. Steam link state is one-time, short-lived and stored hashed at rest.
7. User isolation is preserved for status, link, unlink, preview and import.
8. OpenID `return_to` and `realm` are generated from trusted server-side API/public
   origin configuration.
9. Frontend redirects are generated from trusted `FrontendBaseUrl` plus relative
   server-owned paths; no callback payload or Angular request can create an open
   redirect.
10. Preview/import use only the caller's linked SteamID64 and never trust a
    client-supplied SteamID64.

### Persistence

1. EF Core migration adds `steam_accounts`, `steam_link_requests`, and nullable
   `library_entries.steam_app_id`.
2. Existing rows remain valid with `steam_app_id = null`.
3. Unique constraints enforce one Steam account per Library and one Library per
   SteamID64.
4. A filtered unique index enforces one `steam_app_id` per Library.
5. No Steam API response payloads, preview candidates or playtime values are
   persisted.
6. `steam_accounts.library_id` cascades on Library deletion and does not block
   Library deletion.
7. `library_entries.steam_app_id` rejects `0`, negative values and values above
   unsigned 32-bit max.

### Testing

1. Backend unit tests cover SteamID/AppID validation, OpenID validation,
   normalization and import planning.
2. Backend integration tests cover link, status, preview, import, duplicate handling,
   relinking, atomic state consumption, import concurrency, delete lifecycle, user
   isolation and migration constraints without calling real Steam.
3. Frontend tests cover link/status/preview/import UI behavior and no automatic
   preview/import.
4. Playwright adds one deterministic Steam import journey without calling real Steam.
5. Existing backend tests, frontend tests, lint, builds and E2E remain passing after
   implementation.

## Product/Domain/Architecture Amendments

- `docs/product/product-specification.md`
- `docs/domain/domain-specification.md`
- `docs/architecture/architecture-specification.md`

## Open Questions

None.

## Implementation

No Feature 011 implementation code was written.

## Next Step

Implement Feature 011 from this approved specification without changing scope.
