# 010 - Cover Image Search

## Status

`Approved`

This specification has passed independent review and is approved for
implementation. Approval means ready for implementation; it does not mean the
feature has been implemented.

## Feature

Cover Image Search.

## Objective

Make it easier for an authenticated user to find a cover image URL while adding
or editing a VideoGame or BoardGame.

After this feature is implemented, an authenticated user can:

1. Enter enough game information in the existing Add/Edit dialog.
2. Explicitly activate `Search cover`.
3. Receive up to five visual image candidates.
4. Inspect the candidates as image cards.
5. Select one candidate.
6. Have the selected candidate URL populate the existing `CoverImageUrl` field.
7. Continue editing and save or cancel through the normal game form flow.

Cover Image Search is optional assistance. It does not replace manual URL entry,
does not save the game automatically, and does not download or store images.

## Authority

Product Specification -> Domain Specification -> Architecture Specification ->
Feature Specification -> Implementation.

Feature 010 uses the Product, Domain and Architecture amendments dated
2026-08-24. Higher-authority behavior remains authoritative if a conflict is
found during implementation.

## Current Repository Assessment

The repository currently has Feature 009 implemented and follows the approved
two-project backend architecture plus Angular PWA frontend.

Observed backend state:

- Backend projects remain exactly `GameLibrary.Api` and `GameLibrary.Core` under
  `src/backend/GameLibrary.sln`.
- `Program.cs` registers controllers, ProblemDetails, CORS, Supabase JWT bearer
  validation, EF Core/Npgsql, and scoped Core services. It also contains explicit
  E2E auth-mode selection for Playwright and no general external-HTTP client
  registrations.
- `VideoGamesController`, `BoardGamesController`, `LibraryController`,
  `RandomPickerController` and `PlayLogController` derive the user only from the
  validated JWT `sub` via `GetSupabaseUserId()` and return `401` when absent.
- API expected failures are mapped to `Problem(...)` with stable titles/details.
  Unexpected failures use centralized `UseExceptionHandler` and return generic
  ProblemDetails with `requestId`.
- VideoGame and BoardGame request/response contracts expose `coverImageUrl` as a
  nullable string. EF entities are not exposed.
- `VideoGameRules.ValidateAndNormalizeCoverImageUrl` and
  `BoardGameRules.ValidateAndNormalizeCoverImageUrl` trim the URL, normalize
  blank to `null`, cap it at 2048 characters, and require `http://` or
  `https://`.
- `Game.CoverImageUrl` is the existing persisted cover model. Library,
  Random Picker and Play Log read models reuse that field.
- No current implementation uses `HttpClient`, `IHttpClientFactory`, typed
  options, rate limiting or external image-search services.
- Committed configuration contains placeholders only. Secrets are expected via
  user-secrets or environment variables. `appsettings.Development.example.json`
  is the template; `appsettings.Development.json` is gitignored.
- E2E test infrastructure uses explicit non-Production `E2E:Auth:Enabled` and
  `/e2e/reset`/`/e2e/auth/session` test helpers that are unavailable in
  Production.

Observed frontend state:

- Angular 22 standalone components, services and signals/local state are used.
  There is no NgRx or UI framework.
- `VideoGameForm` and `BoardGameForm` are embedded in Add/Edit `dialog` flows
  from their management list pages.
- Both forms include a normal `Cover image URL` control bound to
  `coverImageUrl`. Manual paste/edit/clear works today and validates `http(s)`
  URLs client-side before submit.
- VideoGame form already loads Platforms and Genres. It keeps selected Platform
  IDs in a `FormControl<string[]>` and auto-selects the single available Platform
  for quick-add when Owned.
- BoardGame form has no Platform concepts.
- Library, Random Picker and Play Log display cover images when
  `coverImageUrl` is present and placeholders when absent. Feature 008 specifies
  fallback to placeholders when image loads fail, but current templates still use
  direct `<img>` elements without explicit `error` handlers in the inspected
  files.
- Play Shelf visual conventions use simple local SCSS, shared button/card
  classes and responsive dialog/sheet behavior.
- Frontend services use `HttpClient` with `${environment.apiBaseUrl}` and rely on
  the existing token interceptor for bearer tokens.
- Current Vitest tests cover form validation, URL trimming, service request
  shapes, list states and Play Log/Random Picker behavior. Playwright E2E tests
  use deterministic E2E auth and reset/seed behavior.

## In Scope

- One authenticated backend endpoint for image search assistance:
  `POST /cover-images/search`.
- A small backend `CoverImageSearchService` that validates input, builds the
  provider query and normalizes provider candidates.
- A narrow Core `ICoverImageSearchClient` provider boundary for deterministic
  tests and provider isolation.
- One concrete provider integration: Brave Search Image API.
- Backend-only provider credentials/configuration.
- Explicit user-triggered `Search cover` UI in VideoGame Add/Edit and BoardGame
  Add/Edit dialogs.
- A compact shared frontend cover-search component/service used by both game
  forms, unless implementation proves an even smaller shared service plus local
  markup is clearer.
- Visual candidate cards with previews, selected state and graceful broken-preview
  behavior.
- Loading, zero-results, provider-error, timeout, missing-configuration and retry
  behavior.
- Unit, integration, Vitest and one small Playwright journey using deterministic
  provider behavior without calling Brave.
- README updates during implementation documenting provider setup and verification
  commands.

## Out of Scope

Feature 010 must not introduce:

- Image uploads.
- Binary image storage.
- Image proxy/CDN infrastructure.
- Image resizing pipelines.
- Cropping or image editing.
- Automatic background cover search.
- Search on every keystroke.
- Search on Name or Platform change.
- Search on page load or while browsing the Library.
- AI image generation.
- OCR.
- Reverse-image search.
- Persistent search history.
- Provider analytics.
- Store-account integration.
- Steam, PlayStation, Xbox, Nintendo, Epic, GOG or BoardGameGeek account/library
  connections.
- Automatic game metadata import.
- Cover deduplication.
- Multiple-provider fallback framework.
- Plugin architecture or provider marketplace.
- Scraping arbitrary search-engine HTML.
- Offline search.
- Social/shared-library behavior.
- New application database tables, columns or migrations.
- New backend application projects.
- Redis, distributed cache, message brokers, CQRS/MediatR, generic repositories,
  GraphQL, Elasticsearch/OpenSearch, NgRx or a new auth system.

## Provider Decision

Selected provider: Brave Search Image API.

Provider endpoint:

```text
GET https://api.search.brave.com/res/v1/images/search
```

Authentication:

```text
X-Subscription-Token: <API key>
```

Why this provider:

- Brave has a documented Image Search API, not HTML scraping.
- It exposes image URL, thumbnail URL, source page URL/source, dimensions when
  available, and publisher/source information that maps cleanly to a small
  candidate DTO.
- It supports result-count control, including requesting a small count.
- It supports strict SafeSearch and documents strict filtering as the default.
- It uses one backend subscription token, which fits the current server-side
  secrets model.
- Pricing is documented as paid Search API usage with monthly free credits and
  stated request rates, making quota/cost understandable.
- It does not require adding SDK dependencies; normal `HttpClient` is sufficient.
- It is a general web image index, so it can handle both VideoGame covers and
  BoardGame box-art queries without adding store/account integrations.

Rejected or not selected for MVP:

- Google Custom Search JSON API: documented image search exists, but the current
  documentation says it is closed to new customers and existing customers must
  transition by January 1, 2027. That is not robust for a new Feature 010.
- SearchApi/SerpApi-style Google Images scraping APIs: viable as paid wrappers,
  but they add an extra scraping provider layer and are less direct than Brave's
  documented image endpoint for this MVP.
- Unsplash/Pexels/Pixabay: documented and often free, but result quality is poor
  for specific game covers and boxed products.
- BoardGameGeek API/account integration: outside Feature 010 because this feature
  is only cover URL search and must not become board-game metadata import.
- Arbitrary Google/Bing/DuckDuckGo HTML scraping: explicitly rejected as brittle
  and terms-sensitive.

Attribution/copyright posture:

- Feature 010 displays public image URLs returned by the provider and stores only
  the URL selected by the user.
- It does not claim ownership of images, copy images into Game Library storage,
  strip source data required by provider terms, or bypass provider controls.
- Candidate responses include `sourcePageUrl` and `sourceName` so the UI can show
  a source indicator/link. If Brave's current terms require more explicit
  attribution at implementation time, the implementation must follow those terms
  without expanding the domain model.
- No provider-specific display attribution requirement was identified in the
  current Brave documentation reviewed during this draft, beyond respecting source
  information and third-party rights.
- The provider does not grant rights to third-party images. This feature only
  assists the user in selecting an external URL.

## Configuration

Use ASP.NET Core configuration with section `CoverImageSearch`.

Required for real provider search:

```text
CoverImageSearch__Provider=Brave
CoverImageSearch__Brave__ApiKey=<secret subscription token>
```

Optional configuration with defaults:

```text
CoverImageSearch__Brave__BaseUrl=https://api.search.brave.com/res/v1/images/search
CoverImageSearch__Brave__Country=US
CoverImageSearch__Brave__SearchLanguage=en
CoverImageSearch__Brave__Count=5
CoverImageSearch__TimeoutSeconds=5
```

Rules:

- Do not commit real API keys.
- Provider credentials remain backend-only and are configured through user-secrets,
  environment variables or deployment secrets.
- Angular receives no Brave API key, subscription token or provider credentials.
- Missing Brave credentials must not prevent the API from starting.
- If configuration is missing, only `POST /cover-images/search` reports image
  search unavailable; VideoGame/BoardGame CRUD and manual CoverImageUrl remain
  functional.
- SafeSearch is a Feature 010 constant. Brave requests must always use
  `SafeSearch=strict` by hard-coding strict provider-request construction.
- Do not expose an effective runtime SafeSearch choice to users or operators. If a
  `SafeSearch` configuration value exists for documentation or diagnostics, any
  value other than `strict` must never result in an unsafe provider request.
- No user-facing SafeSearch setting is allowed.
- `Count` must never result in returning more than five candidates to Angular.
- Server-side timeout is five seconds unless provider guidance or implementation
  findings justify a spec amendment.

## Search Query Rules

The client sends structured fields, not a raw provider query. The backend builds
the final provider query from approved fields only.

General rules:

- `gameType` is required and must be `VideoGame` or `BoardGame`.
- `name` is required, trimmed, and must be non-empty after trimming.
- The backend trims inputs before constructing a query.
- Blank or invalid requests are rejected before calling Brave.
- The provider receives only the constructed query and provider credentials.
- The provider must not receive user ID, Library ID, notes, rating, play history,
  Supabase token or any other private library data.

VideoGame query:

```text
"<trimmed name>" "<selected platform name>" game cover
```

when a platform context is selected.

For multiple currently selected Platforms:

- The UI shows a small `Search platform` select inside the cover-search area.
- Options are derived from the currently displayed Platform list, filtered to the
  Platforms currently selected for that VideoGame form.
- Options preserve the currently displayed Platform list order.
- Default is the first selected Platform according to the currently displayed
  Platform list order.
- Changing `Search platform` affects only the cover-search query.
- It does not add, remove, reorder or persist VideoGame Platforms.
- The request sends the selected Platform name as `platformName`.

For exactly one currently selected Platform:

- The UI uses that Platform automatically.
- No extra select is required.
- The request sends that Platform name as `platformName`.

For zero currently selected Platforms:

- Search is allowed without platform context.
- The VideoGame query becomes:

```text
"<trimmed name>" video game cover
```

Rationale: Wishlist and Interested VideoGames may validly have zero Platforms,
and even an Owned Add form may not yet be ready to save. Cover search is
assistance and should remain usable as long as the name is valid.

BoardGame query:

```text
"<trimmed name>" board game cover
```

BoardGames never require Platform and do not send Platform context.

## API

Endpoint:

```text
POST /cover-images/search
```

Authentication: required through normal Supabase JWT bearer validation.

Request:

```json
{
  "gameType": "VideoGame",
  "name": "Resident Evil 4",
  "platformName": "PlayStation 5"
}
```

BoardGame request:

```json
{
  "gameType": "BoardGame",
  "name": "Catan"
}
```

Request field rules:

- `gameType` is required and must be exactly `VideoGame` or `BoardGame`.
- `name` is required, trimmed, non-empty, and at most 100 characters after
  trimming to match existing game-name rules.
- `platformName` is optional and used only for `VideoGame`.
- `platformName`, when present, is trimmed, must be non-empty after trimming, and
  must be at most 100 characters to match Platform name limits.
- `platformName` on `BoardGame` is rejected with `400` to keep the public
  contract explicit.
- The request does not include user ID, owner ID, Library ID, game ID,
  LibraryEntry ID, raw provider query or provider options.

Response `200`:

```json
{
  "candidates": [
    {
      "imageUrl": "https://example.com/full-cover.jpg",
      "thumbnailUrl": "https://imgs.search.brave.com/example",
      "sourcePageUrl": "https://example.com/page",
      "sourceName": "example.com",
      "width": 600,
      "height": 900
    }
  ]
}
```

Candidate fields:

- `imageUrl`: required, the URL that will populate `CoverImageUrl` if selected.
- `thumbnailUrl`: optional distinct preview URL when provider exposes one.
- `sourcePageUrl`: optional URL of the source page/provider result page.
- `sourceName`: optional source/publisher/domain display text.
- `width`: optional image width in pixels when provider exposes it.
- `height`: optional image height in pixels when provider exposes it.

Normalization rules:

- Return at most five candidates.
- Ask Brave for `count=5` where practical.
- Filter out candidates without a usable `http(s)` `imageUrl`.
- Filter out candidates where `imageUrl` exceeds 2048 characters, because the
  selected URL must be valid for existing `CoverImageUrl` rules.
- Prefer `thumbnailUrl` for preview when present; otherwise the UI may preview
  `imageUrl` directly.
- Do not expose Brave raw DTOs or provider-specific metadata not needed by the UI.
- Deduplicate by `imageUrl` while preserving provider order.
- Successful zero-results search returns `200 { "candidates": [] }`.

Error behavior:

| Condition | HTTP | ProblemDetails |
| --- | --- | --- |
| Unauthenticated request | `401` | JWT bearer handler challenge. |
| Authenticated principal without usable `sub` | `401` | Controller returns `Unauthorized()`. |
| Missing/null request body | `400` | Title `Invalid cover image search`, detail `Game name is required.` or equivalent validation detail. |
| Blank `name` after trimming | `400` | Title `Invalid cover image search`, detail `Game name is required.` |
| `name` longer than 100 after trimming | `400` | Title `Invalid cover image search`, detail `Game name must be at most 100 characters.` |
| Invalid `gameType` | `400` | Title `Invalid cover image search`, detail `Game type is invalid.` |
| `platformName` supplied for BoardGame | `400` | Title `Invalid cover image search`, detail `Platform is only valid for video game cover search.` |
| Blank `platformName` after trimming | `400` | Title `Invalid cover image search`, detail `Platform name is invalid.` |
| `platformName` longer than 100 after trimming | `400` | Title `Invalid cover image search`, detail `Platform name must be at most 100 characters.` |
| Missing provider configuration | `503` | Title `Cover image search unavailable`, detail `Cover image search is not configured.` |
| Provider timeout | `503` | Title `Cover image search unavailable`, detail `Cover image search timed out. Please try again.` |
| Provider non-success or malformed response | `503` | Title `Cover image search unavailable`, detail `Unable to search cover images right now.` |
| Rate limit exceeded | `429` | Built-in ASP.NET Core rate-limiter response. |
| Successful provider response with no usable images | `200` | `{ "candidates": [] }` |
| Unexpected exception | `500` | Existing centralized handler, no stack traces or secrets. |

The endpoint does not mutate the database and does not create a Library row.

## Backend Technical Requirements

Expected `GameLibrary.Core` files:

```text
GameLibrary.Core/
+-- CoverImages/
    +-- CoverImageSearchService.cs
    +-- CoverImageSearchRules.cs
    +-- CoverImageSearchQuery.cs
    +-- ICoverImageSearchClient.cs
    +-- CoverImageSearchResult.cs
    +-- CoverImageSearchExceptions.cs
```

Expected `GameLibrary.Api` files:

```text
GameLibrary.Api/
+-- Controllers/
|   +-- CoverImagesController.cs
+-- CoverImages/
    +-- CoverImageSearchContracts.cs
    +-- BraveCoverImageSearchClient.cs
    +-- CoverImageSearchOptions.cs
```

This layout is guidance, not permission to create additional projects. If
implementation finds a simpler equivalent within the current architecture, it may
use it while preserving the contract and tests.

Service layering:

```text
CoverImagesController
-> CoverImageSearchService
-> ICoverImageSearchClient
-> BraveCoverImageSearchClient
-> Brave Search Image API
```

Dependency direction:

```text
GameLibrary.Api -> GameLibrary.Core
```

Never:

```text
GameLibrary.Core -> GameLibrary.Api
```

Core-side ownership:

- `ICoverImageSearchClient`.
- Provider-neutral request/result DTOs used by Core.
- `CoverImageSearchService`.
- `CoverImageSearchRules`.
- Provider-neutral exceptions/query types.

Api-side ownership:

- `CoverImagesController`.
- `BraveCoverImageSearchClient`.
- Brave-specific HTTP DTOs.
- Brave configuration/options.
- `HttpClient` registration.
- Provider-specific response parsing.

DI in `GameLibrary.Api` wires the Core `ICoverImageSearchClient` interface to the
Api `BraveCoverImageSearchClient` implementation. Do not add an Infrastructure
project.

Rules:

- Controllers remain HTTP/auth/problem-details boundary code.
- Core owns validation/query construction/result normalization behavior that is
  useful to unit-test without HTTP.
- The provider client owns Brave request construction, headers, HTTP timeout and
  raw DTO parsing.
- Use `IHttpClientFactory` / typed HttpClient registration. Do not instantiate an
  unmanaged long-lived `HttpClient` manually.
- Configure base URL, API key/header, count, country, search language and timeout
  through configuration/options.
- Hard-code Brave SafeSearch request construction to `strict` for Feature 010.
- Use cancellation tokens from ASP.NET Core action methods.
- Do not retry provider calls for Feature 010 unless provider documentation or
  observed failures justify a spec amendment.
- Log provider failures, timeout and normalized result counts at normal
  operational levels. Do not log API keys, auth tokens or secrets. Avoid logging
  full user query text at high severity.

Rate/cost protection:

- Explicit user-triggered search is mandatory.
- Authentication is mandatory.
- At most five results are requested/returned.
- Add ASP.NET Core built-in lightweight rate limiting for this endpoint only.
- Policy: partition by authenticated `sub`, permit 10 cover searches per minute,
  and use queue size 0.
- Do not add Redis, distributed rate limiting, cache tables or other
  infrastructure.

Caching:

- Do not add persistent caching.
- Do not add database tables for search history or candidates.
- Do not add Redis/distributed cache.
- A short in-memory cache is not required for MVP and should be omitted unless
  implementation proves duplicate searches are creating practical problems.

E2E provider mode:

- Playwright must not call Brave.
- Deterministic image-search behavior for E2E may be implemented behind explicit
  non-Production `E2E` configuration, following Feature 008 safety conventions.
- Test provider behavior must not be enabled in Production and must not be a
  public fake provider for normal users.
- Prefer provider substitution through DI/configuration over production endpoints
  that return fake image data.

## Frontend UX

Integrate into existing Add/Edit dialogs:

- Add VideoGame.
- Edit VideoGame.
- Add BoardGame.
- Edit BoardGame.

Do not create a separate full-page cover-search feature.

Interaction order within each form should remain close to existing Play Shelf
forms:

1. Name field.
2. Cover image URL field / cover-search area.
3. `Search cover` button.
4. Loading state while search is pending.
5. Results gallery.
6. Candidate selection.
7. Continue normal Create/Save or Cancel.

Manual URL coexistence:

- Keep the existing `Cover image URL` input visible and editable.
- The user can paste, edit or clear the URL without using search.
- Selecting a candidate sets the same `coverImageUrl` form control.
- If the user selects candidate A and then candidate B, B becomes the current
  `CoverImageUrl` value.
- If the user manually modifies `CoverImageUrl` after selecting a candidate,
  clear the selected candidate visual marker when the current `CoverImageUrl` no
  longer exactly equals the selected candidate `imageUrl`.
- Manual URL editing or clearing does not alter the candidate gallery solely
  because of that manual URL edit.
- The `CoverImageUrl` input value is always authoritative for save.
- Search never auto-submits or persists the game.

Search trigger:

- The only search trigger is an explicit `Search cover` action or equivalent.
- Do not search while typing Name.
- Do not search when Platform changes.
- Do not search when a dialog opens.
- Do not search during Library browsing.
- While one search is pending, repeated activation for that same pending request
  must issue no duplicate HTTP request.
- The rest of the form should remain usable where practical.

Search state invalidation:

- Query-driving fields are `Name` for both game types, and for VideoGames the
  selected Platforms when they affect available Platform search context plus the
  `Search platform` selection.
- Changing query-driving fields must not trigger a provider search automatically.
- After a completed search, when `Name` or effective Platform search context
  changes, clear the current candidate gallery.
- Clear zero-results/error state associated with the previous search where
  appropriate.
- Clear the selected candidate visual marker.
- Do not issue another search request solely because the search context changed.
- Do not modify the existing `CoverImageUrl` field solely because the search
  context changed.
- The `CoverImageUrl` field remains authoritative. If candidate A was selected,
  `CoverImageUrl` was populated, and then `Name` or Platform context changes, the
  gallery and selected marker are cleared while `CoverImageUrl` remains unchanged
  until the user manually changes it or selects a candidate from a new explicit
  search.
- If a search request is in flight and a query-driving field changes, the obsolete
  response must not replace results for the new form state. Use cancellation or a
  simple request-generation/version guard; complex state infrastructure is not
  required.

Prerequisites:

- Search is disabled or clearly rejected when trimmed Name is empty.
- VideoGame search does not require Platform, because Wishlist/Interested can
  validly have no Platforms and zero-platform search is explicitly allowed.
- If one or more Platforms are selected for a VideoGame, Platform context follows
  the rules in Search Query Rules.
- Backend validation remains authoritative even when the frontend disables an
  invalid search.

Candidate gallery:

- Display candidates as responsive visual cards/tiles, not raw URLs.
- Maximum five cards are displayed because the API returns at most five.
- Each card shows a preview image using `thumbnailUrl` when present, otherwise
  `imageUrl`.
- Each card has an accessible selection button or button-card semantics.
- Selected candidate state is visible and not indicated by color alone.
- Show source domain/name when available.
- Show dimensions when both width and height are available and it does not crowd
  the UI.
- Use Play Shelf surfaces, spacing and responsive behavior.

Broken preview behavior:

- If a candidate preview image fails to load, replace that card's image area with
  a compact unavailable/placeholder state.
- Do not let one broken preview collapse or destroy the result layout.
- A candidate whose preview failed should be unavailable for selection unless the
  UI is already using a distinct Brave thumbnail and the original `imageUrl` can
  still reasonably be selected. MVP decision: disable selection for the broken
  preview card.
- Do not add server-side downloading or validation of every image solely to detect
  broken candidates.

No-results and error UX:

- `200` with no candidates shows `No cover images found.`.
- The user can modify Name/Platform context and search again.
- Provider failure, timeout, missing configuration or offline/backend failure show
  readable inline error feedback and a retry path.
- Existing form values remain intact after any search failure.
- Manual `CoverImageUrl` entry remains available after failure.
- A new explicit search that fails preserves current game form values and
  `CoverImageUrl`.
- A failed search must not display candidate results from the failed query as
  current.
- If candidates from an older query were already invalidated by changed search
  inputs, they remain cleared; do not silently restore stale results.
- Game creation/update must not depend on the external provider being available.

PWA/offline behavior:

- Cover search requires backend/external connectivity.
- If offline or unavailable, the search action fails gracefully through the same
  inline error pattern.
- Manual URL entry and the rest of the form remain usable.
- Do not cache third-party search results for offline use and do not add offline
  mutation queues.

Shared frontend structure:

- Add a narrow `CoverImageSearchService` that calls
  `${environment.apiBaseUrl}/cover-images/search`; the existing interceptor
  attaches auth.
- A small shared `CoverImageSearch` component is likely justified because
  VideoGame and BoardGame form interaction is almost identical.
- Keep inputs narrow: `gameType`, `gameName`, selected/current Platform names for
  VideoGame context.
- Output only the selected URL to the parent form.
- Do not generalize into an arbitrary image picker, media manager or provider
  framework.

## Persistence

No schema change.

Feature 010 uses the existing `Game.CoverImageUrl` field and existing
VideoGame/BoardGame create/update save paths. It does not add a
`CoverImageCandidate` entity, search history table, provider metadata columns,
image blob columns, migrations or storage buckets.

## Security And Privacy

- `POST /cover-images/search` requires normal authenticated application access.
- The endpoint derives authenticated use from Supabase JWT validation and the
  `sub` claim, matching existing controllers.
- The endpoint does not need Library ownership resolution because it does not read
  or mutate user library data, but authentication protects external API quota.
- The client must not send user ID, owner ID, Library ID, game ID or
  LibraryEntry ID.
- The backend must not send user ID, Library ID, notes, rating, play history,
  Supabase token or other private library data to Brave.
- Brave receives only the constructed search query and the server-side API key.
- Provider API keys/subscription tokens are never sent to Angular, logged, or
  committed.
- Query construction is backend-controlled. The client does not send arbitrary raw
  provider query strings.
- SafeSearch must be strict.

## Testing

### Backend Unit Tests

Add focused unit tests for pure Cover Image Search rules/service behavior:

- VideoGame query construction with name and one Platform.
- VideoGame query construction with multiple selected Platforms using the chosen
  Platform context.
- VideoGame query construction with no Platform using `video game cover` context.
- BoardGame query construction using `board game cover` context.
- Name trimming and blank-name validation.
- Name length validation.
- Invalid `gameType` validation.
- BoardGame rejects Platform context.
- Platform-name trimming/blank validation.
- Platform-name length validation.
- Provider result normalization maps image URL, thumbnail, source and dimensions.
- More than five provider results are capped at five.
- Unusable results are filtered, including missing image URL, non-`http(s)` URL
  and URL longer than 2048 characters.
- Duplicate image URLs are deduplicated while preserving order.
- Provider missing configuration, timeout and failure exceptions map to the
  expected service-level failure categories.

Mock only the `ICoverImageSearchClient` external provider boundary.

### Backend Integration Tests

Do not call Brave from integration tests.

Use deterministic provider substitution or a fake `HttpMessageHandler` at the
external HTTP boundary. Required cases:

- Unauthenticated `POST /cover-images/search` returns `401`.
- Authenticated principal without usable `sub` returns `401`.
- Blank/missing name returns `400`.
- Invalid `gameType` returns `400`.
- VideoGame request with name and Platform returns the expected response contract.
- VideoGame request with no Platform is valid and uses the no-platform query rule.
- BoardGame request returns the expected response contract.
- BoardGame request with `platformName` returns `400`.
- `platformName` longer than 100 characters returns `400` with title `Invalid
  cover image search` and detail `Platform name must be at most 100 characters.`
- Response contains at most five candidates even if provider returns more.
- Provider zero usable results returns `200` with empty candidates.
- Provider non-success/malformed response maps to `503`.
- Provider timeout maps to `503`.
- Rate limit exhaustion returns `429`.
- Missing provider configuration maps to `503` while application startup and
  existing CRUD endpoints remain usable where practical to verify.
- No database mutation occurs and no Library row is created solely by search.

### Frontend Tests

Use Vitest/Angular test patterns already present in the repository. Required
coverage:

- `CoverImageSearchService.search()` calls `POST /cover-images/search` with the
  structured request and no provider credentials.
- Search cover is disabled or clearly rejected when required input is missing.
- Typing/changing Name issues no search request.
- Changing Name after a completed search clears stale candidates, stale selected
  marker and previous search state without clearing `CoverImageUrl`.
- Changing VideoGame Platform selection issues no search request by itself.
- Changing effective VideoGame Platform search context after a completed search
  clears stale candidates, stale selected marker and previous search state without
  clearing `CoverImageUrl`.
- Explicit click issues one search request.
- Duplicate click while pending issues no additional request.
- In-flight stale responses cannot repopulate candidates after query-driving
  fields change.
- Loading state appears and clears.
- Up to five candidate cards render visually.
- Candidate cards are not raw URL lists.
- Selecting a candidate updates `CoverImageUrl`.
- Selected state renders for the chosen candidate.
- Selecting a second candidate replaces the URL and selected state.
- Manual URL paste/edit/clear remains usable after search.
- Manual URL modification clears the selected marker when the current
  `CoverImageUrl` no longer exactly equals the selected candidate `imageUrl`.
- Error state preserves existing form values and allows retry.
- Zero-results state displays `No cover images found.`.
- Broken preview state preserves layout and disables selection for that candidate.
- VideoGame multiple-platform context select appears only when multiple Platforms
  are selected and defaults to the first selected Platform according to the
  currently displayed Platform list order.
- VideoGame one-platform context is used automatically.
- VideoGame zero-platform search sends no `platformName`.
- BoardGame search sends no Platform context.
- Add and Edit workflows for both game types retain the search helper behavior.
- `401` from cover search follows existing session-expired behavior where current
  authenticated services do so.

Avoid brittle image pixel tests.

### Playwright E2E

Extend the existing Playwright suite with one small deterministic journey.

Do not call Brave from Playwright.

Journey:

1. Login in deterministic E2E auth mode.
2. Open Add VideoGame.
3. Enter a game Name.
4. Select a Platform.
5. Click `Search cover`.
6. See up to five deterministic candidate cards.
7. Select one candidate.
8. Verify the `CoverImageUrl` field updates.
9. Save the VideoGame.
10. Verify the selected cover URL is reflected in the saved game/card/preview path
    available in the current UI.

Optional BoardGame check may be added only if inexpensive and not duplicative.

Test-provider safety:

- Deterministic provider behavior is test-only.
- It requires explicit non-Production E2E configuration.
- It is unavailable in Production.
- It does not create a public production fake-image endpoint.
- It reuses Feature 008 E2E safety conventions.

## Acceptance Criteria

### General

1. Authenticated users can explicitly search cover images from game Add/Edit
   dialogs.
2. Unauthenticated requests to `POST /cover-images/search` return `401`.
3. No cover search occurs while typing Name.
4. No cover search occurs when Platform changes.
5. No cover search occurs on page load or dialog open.
6. The API returns at most five candidates.
7. Angular displays at most five visual candidate cards.
8. Selecting a candidate populates the existing `CoverImageUrl` field.
9. Selecting a candidate does not save the game.
10. Manual URL paste/edit/clear remains available and persists through existing
    game save behavior.
11. Changing query-driving fields after a completed search clears stale candidates
    and selected-candidate markers without clearing `CoverImageUrl`.
12. In-flight stale search responses cannot repopulate candidates for obsolete
    form state.

### VideoGame

1. VideoGame search requires non-empty trimmed Name.
2. VideoGame query includes the trimmed game Name.
3. VideoGame query includes cover intent.
4. With exactly one selected Platform, that Platform is used automatically as
   search context.
5. With multiple selected Platforms, the UI exposes `Search platform`, defaults to
   the first selected Platform according to the currently displayed Platform list
   order, and uses only that Platform in the query.
6. Changing `Search platform` does not change persisted/selected VideoGame
   Platforms.
7. With no selected Platforms, search is allowed and uses `"<name>" video game
   cover`.
8. Cover search works in Add VideoGame.
9. Cover search works in Edit VideoGame without auto-searching when Edit opens.

### BoardGame

1. BoardGame search requires non-empty trimmed Name.
2. BoardGame query includes the trimmed game Name.
3. BoardGame query includes board-game context and cover intent.
4. BoardGame search never requires or sends Platform context.
5. Cover search works in Add BoardGame.
6. Cover search works in Edit BoardGame without auto-searching when Edit opens.

### Provider

1. Brave Search Image API is called only by the backend.
2. Brave credentials are configured server-side and are never exposed to Angular.
3. Brave requests use strict SafeSearch.
4. Brave requests ask for a small result count and the backend returns no more
   than five normalized candidates.
5. Missing provider configuration does not prevent app startup.
6. Missing provider configuration returns predictable search-unavailable feedback.
7. Provider failure does not destroy form state.
8. Provider timeout does not hang the form indefinitely and allows retry.
9. SafeSearch cannot become non-strict through user input or runtime
   configuration.

### UX

1. `Search cover` has a visible loading state while pending.
2. Duplicate activation during the same pending search issues no duplicate
   request.
3. Zero usable results show `No cover images found.`.
4. Provider errors show readable inline feedback and allow retry.
5. A failed retry does not restore stale candidate results and preserves
   `CoverImageUrl`.
6. Candidate results are responsive visual cards/tiles.
7. Selected candidate state is visible and accessible.
8. Manual URL edits clear the selected marker when the current `CoverImageUrl` no
   longer exactly equals the selected candidate `imageUrl`.
9. Source indicator is shown when available.
10. Dimensions are shown when available and useful.
11. Broken preview images are handled per-card without breaking layout.
12. Broken-preview candidates are not selectable in the MVP.

### Security

1. The endpoint requires normal Supabase JWT bearer authentication.
2. The request does not accept user ID, owner ID or Library ID.
3. Provider credentials are not exposed in frontend bundles, API responses or
   logs.
4. The provider receives only the constructed search query and required provider
   credentials.
5. User notes, rating, play history, Library ID, user ID and Supabase tokens are
   not sent externally.
6. Backend constructs the provider query from structured fields and known cover
   keywords; raw provider query strings are not accepted from clients.
7. ASP.NET Core built-in per-user rate limiting protects provider quota without
   Redis or distributed infrastructure.
8. `platformName`, when provided, is trimmed, non-empty after trimming and at most
   100 characters; longer values return `400` with title `Invalid cover image
   search` and detail `Platform name must be at most 100 characters.`

### Persistence

1. No EF Core migration is added.
2. No new application table, column, storage bucket or entity is added.
3. Search candidates and search history are not persisted.
4. Only the selected `CoverImageUrl` persists through existing VideoGame/BoardGame
   save flows.
5. Existing Game/LibraryEntry lifecycle and user-isolation behavior remain
   unchanged.

### Testing

1. Backend unit tests cover query construction, validation and normalization.
2. Backend integration tests cover auth, validation, response contract, max five,
   zero results, provider failure, timeout and missing configuration without
   calling Brave.
3. Frontend tests cover explicit triggering, no auto-search, loading, errors,
   zero results, selection, manual URL coexistence, stale-response protection,
   stale-candidate clearing, Platform context and Add/Edit flows.
4. Playwright adds one deterministic cover-search journey without calling Brave.
5. Existing backend tests, frontend tests, lint, builds and E2E remain passing
   after implementation.

## Product/Domain/Architecture Amendments

- `docs/product/product-specification.md`
- `docs/domain/domain-specification.md`
- `docs/architecture/architecture-specification.md`

## Open Questions

None.

## Implementation

No Feature 010 implementation code was written.

## Next Step

Feature 010 is approved and ready for implementation.
