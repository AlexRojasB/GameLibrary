Product Specification v0.7 --- Game Library

Status: Approved
Version: 0.7

GL-16 approved exception — current Picker round recovery only:
Persist three independent current rounds per authenticated Library: All (default),
VideoGames and BoardGames. Recover the proposal, newest-first history, shown IDs,
last outcome and filter context across reload/navigation/sign-in. Switching modes
loads that mode's own round. Start over clears only the active round, keeping its
filters; filter edits keep its history/shown IDs and clear its displayed proposal
as before. Another and Select Game retain their meaning; eligibility and ownership
remain backend-authoritative, including stale recovery. This supersedes the volatile
session/no-persistence rules below ONLY for recovering current rounds. Completed-
round archives, statistics, completion and Log Play Time changes remain prohibited
in this delivery. See specs/015-persistent-picker-round/spec.md.

GL-17 / GL-27 first-delivery amendment — owner-requested local implementation:
The Picker's proposed game has an explicit Select Game action. Persist one
Currently Playing VideoGame and one Currently Playing BoardGame independently for
the authenticated Library, replacing only the same-type selection. Home shows both
slots and empty states across reloads/sessions. Active games are excluded from the
Picker; replaced games are again subject to the existing eligibility/filter/shown-ID
rules. This is separate Library state, not persisted Picker history, GameStatus,
automatic Play Log or a completion workflow. Skip/history redesign, Log Play Time
relocation and completion remain deferred. See specs/014-currently-playing/spec.md.
Implementation/tests/migration are local only; no hosted changes or release approval.

Feature 011 PR 5 amendment, 2026-11-24 -- Authorized local final validation:
Review the approved v2 threat matrix, add only necessary verification/operational
safety corrections and prepare privacy, retirement, migration, QA and rollback
procedures. Keep normal linking unavailable and legacy rejected. Hosted evidence,
global retirement and activation require separate authorization. No push/deployment
or remote migration. See specs/011-steam-integration/linking-final-validation.md.

Feature 011 PR 4 amendment, 2026-11-24 -- Authorized local Angular/browser:
Implement approved redesign section 10 and section 14 PR 4: same-tab trusted Steam
navigation, per-tab non-authorizing metadata, full verified SteamID/current-account
review and explicit confirmation/cancel/recovery. Keep normal linking unavailable
under hard-false shipped readiness. No activation/deployment/remote migration/push
or merge. Active scope: specs/011-steam-integration/linking-angular-browser.md.

Feature 011 PR 3 amendment, 2026-11-24 -- Authorized local API/protocol:
Implement the approved Option A API/OpenID boundary in section 14 PR 3 of
docs/security/steam-openid-linking-redesign.md. Callback stages only; explicit owner
confirmation creates the link. Keep shipped readiness false, legacy retired and
the existing Angular unavailable experience unchanged. No activation, hosted work,
remote migration, push or merge is authorized. See linking-api-protocol.md.

Feature 011 PR 2 amendment, 2026-11-24 -- Approved local persistence/Core:
Approve the recommended v2 baseline in docs/security/steam-openid-linking-redesign.md:
independent browser proof, callback staging without linking, explicit authenticated
owner confirmation of the full immutable SteamID, one live transaction per Library,
no extra confirmation token, and expiry+24h opportunistic retention with no inactive
Library deletion SLA. Numeric-ID review is not human-intent attestation, MFA or
protection against a stolen JWT initiating its own flow. PR 2 implements private
persistence/Core only; legacy remains retired and v2 publicly unavailable. No
Angular, deployment, activation or global-retirement claim. See
specs/011-steam-integration/linking-persistence-core.md for the bounded scope.

Feature 011 security milestone, 2026-11-21 -- Authorized local implementation:

Temporarily reject legacy Steam account linking unconditionally in the new binary.
Show linking unavailable and remove initiation actions; preserve existing links,
configured explicit preview/import, unlink, manual catalog and Supabase authentication.
This narrow exception supersedes historical linking-availability requirements below.
It does not approve v2 activation or claim old replicas/in-flight requests are revoked.
See specs/011-steam-integration/linking-retirement.md. Deployment requires separate
coordinated-retirement approval; production remains blocked.

Feature 013 amendment, 2026-10-28 -- Approved design:

One optional private uploaded cover per VideoGame or BoardGame is approved, with
device selection/mobile capture, local preview/crop/zoom/reposition, 90-degree
rotation, replacement and removal. Manual external URLs and explicit Cover Image
Search remain available in the same selector. Section 15 defines the approved
privacy, limits, save/recovery and Steam-precedence behavior; section 31 retains
the exclusions outside this narrow exception.

Approval is of the design, not implementation or release readiness. No code,
migrations, dependencies, bucket or other resources are created by this amendment.
Implementation tests, real Storage permission checks, native-runtime codec smoke
tests, mobile/accessibility review and bundle-budget verification remain future
gates. The Feature 013 specification and agent instructions must match these
approved higher-authority decisions before implementation begins.

Manual review amendment, 2026-08-19:

Feature 007 manual product testing introduced three approved product changes
before Feature 007 can be marked DONE:

VideoGames support optional player-count metadata using the existing
MinimumPlayers and MaximumPlayers concepts.

Owned VideoGames with GameStatus = Completed must persist ProgressPercentage =
100.

The Random Picker must show visible volatile session result history, newest
first.

Feature 009 amendment, 2026-08-24:

Play Log is a manual user-confirmed activity feature. It is distinct
from Random Picker visible history, persistent picker history, automatic playtime,
external gameplay imports and analytics.

Feature 009 manual review amendment, 2026-08-25:

Play Log remains manual. A new PlayLogEntry records the user-selected played
date/time and may optionally record the duration of that play in minutes. Users
may correct an existing PlayLogEntry's PlayedAt and DurationMinutes after
creation, but may not change the associated game, ownership or CreatedAt. Play
Log is promoted to authenticated desktop primary navigation while mobile bottom
navigation remains Home, Library, Pick and Manage.

Feature 010 amendment, 2026-08-24:

Historically, Feature 010 used CoverImageUrl as the persisted cover model; its
URL-only scope is not a prohibition on the later Feature 013 amendment. Users may
continue to manually paste, edit and clear cover image URLs. The product may also
offer optional, explicitly user-triggered external cover-image search assistance that returns
visual candidates for the user to inspect and select. Selecting a candidate only
fills the existing CoverImageUrl field; normal game saving remains separate.
Under Feature 013, that explicit choice is saved using external/remove coverUpdate
intent, not by inferring intent from the historical URL field alone.

Feature 011 amendment, 2026-08-25; updated 2026-08-28:

The product may offer an optional Steam integration for authenticated users. A
user may link one Steam account, review games returned by Steam for that linked
account, and explicitly import selected Steam-owned games as Owned VideoGames in
their private Library. Steam integration is optional assistance and must not
replace manual entry. It must not request or store Steam passwords, must not
grant Game Library authentication, and must not run background synchronization.
Steam imports must preserve all VideoGame invariants, including Owned
VideoGames requiring at least one Platform. Imported games use the user's Steam
Platform, creating it only when needed. Steam playtime returned by Steam may be
persisted on the imported LibraryEntry as the latest known accumulated Steam
playtime total in minutes. This value is external reference data only: it is not
Play Log data, must not create PlayLogEntries, must not alter GameStatus,
ProgressPercentage or Random Picker weighting, and must not become automatic
gameplay tracking. Steam-provided cover data may populate CoverImageUrl only
when a reliable supported Steam image source is available and BOTH CoverImageUrl
and CoverUploadId are null, using an atomic empty-cover check. Neither an external
URL nor a managed cover may be overwritten by Steam refresh/import behavior.
Feature 010 cover search remains available as the fallback or replacement path.

Feature 012 amendment, 2026-08-28:

Games may be explicitly classified as Family Friendly. This applies equally to
VideoGames and BoardGames and is a simple yes/no indication of whether the user
considers the game appropriate for family play. It is not an age-rating system and
does not model content warnings, parental controls, minimum age or ratings such as
ESRB/PEGI. New manually created games and Steam-imported games default to not
family-friendly unless the user explicitly classifies them otherwise. The Library
and Random Picker may filter to family-friendly games only without changing
existing ownership, mode, shown-history or other filter semantics.

1. Product Vision

Game Library is a responsive Progressive Web App (PWA) for managing a
personal library of games and helping users decide what to play.

The MVP supports two distinct game types:

Video games.

Board games.

The application must be useful without external integrations. Manual
entry is the primary MVP workflow.

A central product feature is the Random Game Picker, which selects a
game from the user's owned collection according to optional filters.

A secondary product feature is the Play Log, which lets the user manually record
that they played an Owned game.

2. Product Principles

Simplicity

Adding a game must be fast. Secondary metadata can be completed later.

Manual-first

The MVP must work without Steam, PlayStation, Xbox, Nintendo, Epic, GOG,
BoardGameGeek, or other integrations.

Random Picker is a core feature

The Random Picker is a primary use case, not an auxiliary feature.

Avoid overengineering

The MVP should solve current requirements with the simplest reasonable
architecture and domain model.

3. Users and Libraries

The application supports multiple registered users.

For the MVP:

Each user has one private personal library.

Games and library data created by one user do not affect another
user's data.

Shared libraries are not implemented.

Future versions may support shared or family libraries.

4. Game Types

The MVP supports:

VideoGame

BoardGame

A video game and a board game with the same name are distinct games.

Every Game has an explicit IsFamilyFriendly classification indicating whether the
user considers it appropriate for family play.

5. Acquisition Status

Every LibraryEntry has one acquisition status:

Owned

Wishlist

Interested

When the user does not explicitly choose a value during creation, the
default is Owned.

Only Owned games participate in the Random Game Picker in the MVP.

6. Video Games

A VideoGame contains game-level information:

Name.

Optional cover: external image URL or private uploaded image (section 15).

Family Friendly classification.

Optional minimum number of players.

Optional maximum number of players.

Zero or more predefined genres.

Its LibraryEntry contains the user's personal information:

Acquisition status.

Zero or more platforms depending on acquisition status.

Optional game status.

Optional progress percentage.

Optional rating.

Optional personal notes.

Platform requirement

Owned -> at least one Platform is required.

Wishlist -> Platform is optional.

Interested -> Platform is optional.

A user may associate multiple platforms with the same video game.

Status, progress, rating and notes apply to the user's experience with
the video game as a whole, not independently per platform.

Player-count metadata, when present for a VideoGame, uses the same
MinimumPlayers and MaximumPlayers concepts as BoardGames. Both values are
optional for VideoGames, but if either is supplied then both are required and
must satisfy 1 <= MinimumPlayers <= MaximumPlayers.

Editing acquisition status and platforms

The LibraryEntry must always end an edit in a valid state.

Changing Wishlist or Interested to Owned requires at least one
Platform.

If no Platform is selected, the change cannot be saved.

An Owned VideoGame cannot lose its last Platform.

Changing Owned to Wishlist or Interested preserves existing
Platform associations.

Changing Owned to Wishlist or Interested clears GameStatus and
ProgressPercentage.

Wishlist and Interested entries cannot have GameStatus or
ProgressPercentage.

Rating and Notes may remain when changing away from Owned.

7. Video Game Status

Supported values:

Backlog

Playing

Completed

Abandoned

WantToPlay

GameStatus is optional and is valid only for Owned VideoGame
LibraryEntries.

Completed is the authoritative indication that the user considers the
game completed. No separate IsCompleted field is required.

8. Video Game Progress

ProgressPercentage is optional and represented from 0 through 100.

It is valid only for Owned VideoGame LibraryEntries.

ProgressPercentage does not automatically determine GameStatus.

GameStatus generally does not determine progress, except for the approved
Completed rule: for an Owned VideoGame, GameStatus = Completed implies
ProgressPercentage = 100 after create or update completes. A lower or null
incoming progress value is normalized to 100. Changing away from Completed does
not automatically lower progress.

9. Platforms

Users can manage their own platform catalog.

Examples include Steam, PC, PlayStation 5, Xbox, Nintendo Switch, Retro
and Emulation.

Users can create, edit and delete unused platforms.

A Platform associated with one or more VideoGames cannot be deleted
until those associations are removed.

BoardGames do not use Platforms.

10. Genres

A VideoGame may have zero or more genres.

The complete MVP catalog is:

Action.

Adventure.

RPG.

Strategy.

Simulation.

Sports.

Racing.

Fighting.

Shooter.

Platformer.

Puzzle.

Horror.

Rhythm.

Party.

Other.

Users cannot create, edit or delete genres in the MVP.

11. Board Games

A BoardGame contains game-level information:

Name.

Minimum number of players.

Maximum number of players.

Optional approximate duration in minutes.

Optional interaction type.

Optional cover: external image URL or private uploaded image (section 15).

Family Friendly classification.

Its LibraryEntry contains:

Acquisition status.

Optional rating.

Optional notes.

Minimum and maximum player counts are required for quick creation.

Approximate duration is a single positive number of minutes.

12. Board Game Interaction Type

Supported values:

Cooperative

Competitive

InteractionType is optional.

Categories such as Party, Solo and Complexity are outside the MVP. Family Friendly
is a separate yes/no classification, not a BoardGame category taxonomy.

Solo eligibility is represented by MinimumPlayers = 1.

13. Rating

Ratings are optional and use a five-star scale:

1 star.

2 stars.

3 stars.

4 stars.

5 stars.

14. Notes

Each LibraryEntry may contain optional personal notes.

Notes apply to the game as a whole and are not platform-specific.

15. Cover Images

Cover images are optional.

Each Game has at most one current cover: an external CoverImageUrl, a managed
CoverUploadId, or neither. CoverImageUrl remains external-only in the database
AND API responses; it is null for a managed cover. Existing external URLs remain
unchanged. Managed covers use private Supabase Storage bucket game-covers, not
image bytes/Base64 in PostgreSQL or a public image URL.

The user may manually paste, edit or explicitly clear an external URL. The unified
selector keeps the external URL input visible/editable, offers explicit cover
search, device upload and a mobile rear-camera capture hint with file-selection
fallback, and supports replacing one source with another. Camera/chooser/editor
cancellation preserves the prior draft and saved cover. No live camera stream is
required, and a Game is not silently created merely to stage a cover.

Cover search is assistance only. It must not automatically run while browsing,
typing or opening forms, and it must not automatically save a game.

JPEG, PNG and static WebP may be selected locally. Local editing supports fixed
2:3 crop, zoom, reposition, reset and 90-degree rotation. BoardGames default to
Fit whole image with white padding so square artwork is preserved; VideoGames
default to Crop. Both modes produce 2:3 output without upscaling image content.
No HEIC conversion, AI editing, remote-image copying, gallery or image history is
included. Selection/cropping remains local until final Create/Save.

The API accepts one multipart image of at most 5 MiB, independently validates and
sanitizes it, strips EXIF/GPS and other metadata, and stores only canonical JPEG
of at most 2 MiB and 960 x 1440 pixels. Client cropping is not trusted validation.
Approved JPEG compatibility policy, 2026-10-29: validation is bounded normalization,
not exhaustive standards conformance. Decoder-tolerated coefficient anomalies
without diagnostics may be normalized only after the existing structural/resource
checks pass. Truncation, invalid headers/tables, decoder warnings/errors and other
rejected categories remain rejected; original uploaded bytes are never the stored cover.
Durable admission is limited to 250 MiB of objects/reservations per Library and
ten unclaimed unexpired uploads. Pending deletion remains conservatively charged
until safe purge. These limits are NOT a global provider/spending cap; enabling
uploads requires provider quota/egress monitoring and an owner-approved budget.

Private bytes are delivered only by the authenticated ASP.NET Core endpoint
GET /cover-uploads/{id}/content, with JWT and Library authorization on every
request. No signed URLs are generated/exposed, no Storage redirects are returned,
and no tokens are placed in query strings. Angular fetches a Blob using
HttpClient with Bearer authentication and displays a component-local object URL,
revoked on logout/account change, source change and component destruction.
Responses, including errors, use Cache-Control: private, no-store, without
validators or 304 responses. No CDN delivery, service-worker image cache or
persistent client cache is permitted. This prevents application cache reuse, not
recall of bytes already downloaded by an authorized user; logout does not revoke
an otherwise valid JWT at the identity provider.

Create/Save stages a new upload, then claims it atomically with Game and
LibraryEntry persistence. Upload alone does not save the cover. Cover changes use
explicit optional coverUpdate intents: keep, external, upload or remove. Omission
means keep, never clear; null/malformed intent is rejected with 400. On Create,
keep leaves no cover. Explicit changes on existing games require an expectedCover
snapshot of the nullable external URL/upload ID; stale comparison returns 409
without partial mutation. Metadata-only saves preserve the current cover, even
if it changed in another tab. Intent must not be inferred from value equality.
Cover-only changes do not alter unrelated metadata.

Known upload/save failures preserve the saved cover and other form edits where
possible. A lost response or COMMIT acknowledgement exception is NOT proof of
rollback: reconcile fresh primary data using the same upload handle, never blind
Create or delete a possibly committed attachment. A bounded immutable first-claim
receipt survives cover retirement/Game deletion until safe purge; missing/purged
evidence requires explicit Library inspection/user resolution, not a claim of
general exactly-once game creation.

Explicit external/search/upload choices take precedence over Steam. Steam may fill
only when both current references are null, checked atomically. Remove saves no
cover and no old fallback or immediate enrichment; a later EXPLICIT Steam
operation may fill an otherwise empty cover. Replacement/removal/Game deletion
journals managed retirement in the SQL transaction, with physical deletion only
after commit and bounded retries in the existing API process. Never delete an
active referenced cover or an external URL target. Cleanup may be delayed while
the API/provider is unavailable; no immediate-erasure guarantee is made.

Games without a usable external or managed cover use a UI placeholder. Rendering
failure does not erase the saved selection or alter Random Picker history.

16. Quick Add --- Video Game

The creation flow must prioritize speed.

Initial fields:

Name.

AcquisitionStatus, defaulting to Owned.

Platform when AcquisitionStatus is Owned.

If AcquisitionStatus is Wishlist or Interested, the game can be
saved without a Platform.

All other metadata, including optional player counts, may be completed later.
Family Friendly is shown in the Add/Edit flow and defaults to false unless the
user explicitly marks the game as family-friendly.

17. Quick Add --- Board Game

Initial fields:

Name.

Minimum players.

Maximum players.

AcquisitionStatus, defaulting to Owned.

All other metadata may be completed later.
Family Friendly is shown in the Add/Edit flow and defaults to false unless the
user explicitly marks the game as family-friendly.

18. Library Screen

The main library allows the user to browse:

All games.

Video games.

Board games.

The primary visual presentation uses game cards.

Search

Search uses a case-insensitive substring match on Game name.

Filters

General:

Game type.

Acquisition status.

Rating.

Family friendly only.

VideoGame-specific:

Platform.

Genre.

GameStatus.

BoardGame-specific:

Player count.

InteractionType.

Platform, Genre, GameStatus and InteractionType filters support multiple
selections.

Filter semantics

Multiple selected values inside the same filter use OR / ANY-match.

Different active filter types combine using AND.

Rating means minimum rating.

BoardGame Player count matches when
MinimumPlayers <= requestedPlayers <= MaximumPlayers.

VideoGame player-count metadata may be displayed on Library cards when present.
This amendment does not add VideoGame player-count filtering to the Library
screen; the existing Library player-count filter remains BoardGame-specific
until a future approved Library filtering amendment says otherwise.

Missing optional metadata does not satisfy an active filter
requiring that metadata.

Family friendly only matches games with IsFamilyFriendly = true. When it is not
active, both family-friendly and non-family-friendly games are included.

Sorting

Name A-Z.

Name Z-A.

Rating highest first.

Rating lowest first.

Recently added.

Games have a creation timestamp sufficient to support Recently added.

19. Random Game Picker

The Random Game Picker operates only on games with
AcquisitionStatus = Owned.

The user chooses:

Video games.

Board games.

All.

20. Video Game Random Mode

Optional multi-select filters:

Platform.

Genre.

GameStatus.

Optional scalar filters:

Number of players.

Family friendly only.

Multiple values within one filter use OR. Different filter types combine
using AND.

Missing metadata does not satisfy an active filter requiring it.

For player count P:

MinimumPlayers <= P <= MaximumPlayers.

A VideoGame without player-count metadata does not satisfy an active player
count filter.
Family friendly only matches VideoGames with IsFamilyFriendly = true.

21. Board Game Random Mode

Optional filters:

Number of players.

Available duration.

InteractionType as multi-select.

Family friendly only.

For player count P:

MinimumPlayers <= P <= MaximumPlayers

For available duration D, the game must have ApproximateDuration and:

ApproximateDuration <= D

Multiple InteractionType values use OR. Different filter types combine
using AND.
Family friendly only matches BoardGames with IsFamilyFriendly = true.

22. All Random Mode

All mixes owned VideoGames and owned BoardGames into one candidate
pool.

Type-specific filters are not available.

The MVP includes common minimum-Rating, player-count and family-friendly-only
filters in All mode.

For player count P, both VideoGames and BoardGames match using:

MinimumPlayers <= P <= MaximumPlayers.

Any candidate missing player-count metadata does not satisfy an active player
count filter.
Family friendly only matches candidates with IsFamilyFriendly = true.

No weighting by game type is applied.

23. Random Selection

Obtain Owned games for the selected mode.

Apply active filters.

Exclude games already shown during the current Random Picker session
when unshown matching candidates remain.

Randomly select one remaining candidate.

Display the result.

On SUCCESS, add the selected result to the visible volatile result history for
the current picker session.

The MVP does not use recommendation scoring, weighting, AI, machine
learning or historical optimization.

24. Another and Temporary History

The user can request Another.

Games already shown during the current Random Picker session do not
appear again while unshown eligible candidates remain.

The application must visually display results already shown during the current
session.

Visible history is ordered newest first. Another prepends the newly selected
result above previous results.

The newest/current result receives primary/highlighted visual emphasis.
Previous results remain visible with secondary/muted visual treatment.

This history is temporary and is not persisted.

25. Random Picker Session

A session begins when the user opens the Random Picker.

Changing filters does not create a new session and does not clear
previously shown results.

Changing filters does not clear the visible volatile result history.

Changing mode does not clear previously shown results and does not clear the
visible volatile result history.

A game that appeared earlier remains considered shown even if filters
later change.

The session ends when the user leaves the Random Picker or the
application/page session is closed or refreshed.

26. No Random Candidates

If current filters produce zero eligible candidates, show a clear
no-results state and allow the user to modify or clear filters.

Do not silently relax filters.

If every eligible candidate for the current filters has already been
shown, inform the user and offer an explicit action to make previously
shown matching games eligible again.

Do not silently reset the shown-results pool.

NO_CANDIDATES and ALL_ALREADY_SHOWN states must not erase visible volatile
history. The explicit reset action clears both the shown-results pool and the
visible volatile history.

26A. Play Log (Feature 009 Amendment)

The Play Log records actual user-confirmed play activity.

It is not Random Picker history. Showing a game in Random Picker must never
automatically create a Play Log entry.

The user may manually log a play for an Owned VideoGame or Owned BoardGame in
their private Library.

Only games with AcquisitionStatus = Owned can receive new Play Log entries.

Each Play Log entry records one play occurrence for one LibraryEntry. Multiple
plays for the same LibraryEntry are allowed.

A Play Log entry belongs to the user's Library through its referenced
LibraryEntry. It does not introduce a second stored Library ownership path.

Multiple intentional plays remain valid: after one Log play request completes,
the user may explicitly log the same game again. The UI must prevent accidental
duplicate logging from repeated activation while the original Log play request is
still pending.

When creating a Play Log entry, the user can choose when the play actually
happened. Manual logging interactions default the played date/time to the current
browser-local date/time, and the user may change it before submitting. Random
Picker logging uses the same interaction and defaults the played date/time to now.

PlayedAt represents when the user says the play occurred. It is persisted and
transported as an offset-aware/UTC-compatible timestamp, and normal UI displays it
using browser-local date/time semantics.

CreatedAt represents when the PlayLogEntry row was created. It is always assigned
by the backend and is not user-editable. PlayedAt and CreatedAt may differ
substantially.

A Play Log entry may optionally record how long that individual play occurrence
lasted. Duration is a positive integer number of minutes when present. Duration
belongs to the PlayLogEntry, not to Game or LibraryEntry. Null duration is valid;
zero or negative duration is invalid. No arbitrary maximum duration is imposed in
the MVP.

Feature 009 supports correcting an existing Play Log entry after creation.
Editable fields are limited to PlayedAt and DurationMinutes. The associated
LibraryEntry/Game, ownership and CreatedAt are not editable. If the user wants to
record a play for a different game, they create another Play Log entry.

The same PlayedAt and Duration validation used during creation applies during
edit. PlayedAt may be historical, current or within the approved future tolerance;
PlayedAt later than server current UTC time plus five minutes is invalid.
DurationMinutes may be added, changed or cleared to null. When present, duration
must be a positive integer; zero and negative values are invalid. No arbitrary
maximum duration is imposed in the MVP.

The Play Log page exposes an Edit action on each Play Log card. Edit uses the
Play Shelf dialog/sheet pattern, prepopulates PlayedAt and DurationMinutes, and
makes the game identity read-only and obvious. Saving updates the card without a
full application reload. If PlayedAt changes, the Play Log list re-sorts newest
first immediately.

Edit actions belong only to the Play Log page. Library cards and Random Picker
result cards may create new logs but must not expose Play Log edit actions.

Deleting Play Log entries, changing the associated game, and richer session
details remain outside this feature unless a future approved specification adds
them.

Changing an Owned game to Wishlist or Interested does not remove existing Play
Log entries, but it prevents new entries from being logged while the game is not
Owned.

Deleting a LibraryEntry also deletes its Play Log entries.

The Play Log screen lists entries newest first and shows enough game context to
identify what was played. It shows PlayedAt in the user's browser-local date/time
format and shows duration when present using a user-friendly minutes/hours
presentation.

The Play Log does not change Random Picker eligibility, filtering, shown-history
behavior or selection weighting.

The Play Log does not include timers, active sessions, automatic playtime
tracking, outcomes, scores, party/player attendance, platform-specific sessions,
analytics, recommendations or external integrations in the MVP.

Desktop authenticated primary navigation includes Play Log as a normal destination
alongside Home, Library, Pick and Manage. Home must still provide a discoverable
secondary path to Play Log. Mobile bottom navigation remains exactly Home,
Library, Pick and Manage; Play Log must not become a fifth mobile bottom-nav item
and must not make Manage active on `/play-log`.

27. Feature 008 UX Input

Manual Feature 007 product testing also identified a management-page UX issue
for Feature 008. Adding a new VideoGame or BoardGame must remain easy and
immediately accessible regardless of collection size.

The current create form can become inconveniently positioned as the list grows.
Feature 008 must evaluate the create/manage workflow and choose an appropriate
responsive pattern, such as placing creation above the list, a modal/dialog,
drawer, dedicated creation view, sticky/floating add action, or another design.

This product requirement is recorded for Feature 008 only. This amendment does
not choose the final interaction pattern and does not require management-page UX
refactoring before Feature 008.

28. Deletion

Deleting a LibraryEntry in the MVP also deletes its associated Game
because Game records are not shared between users or LibraryEntries.

Deleting one user's game must never affect another user's library.

29. PWA

The application is a responsive PWA for desktop, tablet and mobile
browsers.

A native mobile application is outside the MVP.

30. MVP Scope

The MVP includes:

Registration.

Login/logout.

One private library per user.

VideoGame CRUD.

BoardGame CRUD.

Platform CRUD with safe deletion.

Multiple Platforms per VideoGame.

Predefined Genres.

GameStatus.

Optional progress.

Five-star ratings.

Notes.

Owned / Wishlist / Interested.

Optional external cover image URL or single private uploaded cover.

Local cover selection/capture, crop/Fit, 90-degree rotation and explicit removal.

Family Friendly classification.

Optional cover image search assistance for choosing a CoverImageUrl.

Optional explicitly user-triggered Steam account linking and selected Steam
owned-game import for VideoGames.

Library search/filter/sort.

Random Picker for VideoGames.

Random Picker for BoardGames.

Mixed All Random Picker.

Another.

Temporary Random Picker history.

Visible volatile Random Picker result history.

Manual Play Log entries for Owned games, with user-selected PlayedAt, optional
duration in minutes, and correction of existing entries' PlayedAt and
DurationMinutes.

Responsive PWA.

31. Explicitly Outside the MVP

Shared libraries.

Family libraries.

Friends.

Favorites.

Custom genres.

Image upload or managed storage beyond the single private game-cover flow in
section 15: galleries, image history, arbitrary media, public sharing or uploads.

Automatic cover search, copying/downloading external search covers into managed
storage, image generation, reverse-image search, HEIC conversion or stored
cover-search history. Authenticated delivery of the user's own managed cover is
permitted by section 15; CDN delivery and persistent/offline private-image caching
are not.

External platform integrations other than the optional Feature 011 Steam import
assistance.

Automatic imports or background synchronization. Feature 011 permits only
explicit user-selected Steam imports after the user reviews candidates.

Achievements.

Automatic playtime, except that Feature 011 may persist Steam-reported
accumulated playtime as external reference data on Steam-imported LibraryEntries
without creating PlayLogEntries or gameplay sessions.

Automatic gameplay session tracking.

Deleted Play Log entries, associated-game reassignment for Play Log entries, and
richer Play Log session details beyond PlayedAt and DurationMinutes.

Timers, active sessions, outcomes, scores or player attendance.

Persistent Random Picker history.

RandomPickerSession or PickerHistory persistence.

PlaySession for picker history.

Local or online multiplayer modeling.

Co-op-specific player ranges.

AI recommendations.

Prices and purchase history.

Collection valuation.

BoardGame expansions.

Native mobile applications.

32. Initial Technical Direction

Subject to the Architecture Specification:

Angular PWA.

ASP.NET Core Web API.

PostgreSQL.

Simple monolithic/modular organization.

33. MVP Success Criteria

The MVP is useful when a user can:

Create an account and sign in.

Manage personal Platforms.

Quickly add VideoGames.

Quickly add BoardGames.

Track Owned, Wishlist and Interested games.

Browse, search, filter and sort the library.

Record status, progress, ratings and notes where applicable.

Open the Random Picker.

Choose VideoGames, BoardGames or All.

Apply the filters available for that mode.

Receive an eligible random result.

Request another result without repeating previously shown games
during the same session when alternatives remain.

See visible volatile result history for the current Random Picker session.

Manually record, browse and correct Play Log entries for Owned games, including
when the play happened and optional duration when useful.
