Domain Specification v0.9 --- Game Library

Status: Approved
Version: 0.9

Feature 011 PR 4 amendment, 2026-11-24 -- Authorized local Angular/browser:
Trusted UI presents the immutable verified SteamID and current application account;
it never auto-confirms or treats callback flags/tab metadata as authority. Existing
Core/API invariants remain authoritative. No Domain/schema change or activation.
See specs/011-steam-integration/linking-angular-browser.md.

Feature 011 PR 3 amendment, 2026-11-24 -- Authorized local API/protocol:
Integrate the PR 2 private lifecycle with the approved strict Steam verification,
independent cookie proof and JWT/Origin boundary. Unlink cancels live v2 rows under
the same Library→row ordering and preserves consumed receipts/catalog data. No
provider cancellation/error mutates a transaction. No new schema or Domain concept.
Shipped v2 remains inaccessible. See specs/011-steam-integration/linking-api-protocol.md.

Feature 011 PR 2 amendment, 2026-11-24 -- Approved local persistence/Core:
The approved v2 transaction binds immutable T/U/Library/state hash/browser verifier;
independent random browser proof is never persisted raw. A callback may stage one
verified SteamID but never create a SteamAccount. Linking requires the initiating
authenticated owner, matching proof, immutable staged identity and matching explicit
expected-ID precondition. Confirmation consumes once and creates the unique account
atomically, with a write-once receipt surviving unlink. PostgreSQL time/deadlines and
signed JWT expiry are checked after locks and all writes/waits. Cancellation,
replacement and expiry never revive; cleanup is owner-scoped/bounded and cannot
delete accounts. Existing links/import/user data remain unchanged. This supersedes
historical callback-consumes/linking semantics only for private v2 Core under
specs/011-steam-integration/linking-persistence-core.md; legacy remains rejected.

Feature 011 security milestone, 2026-11-21 -- Authorized local implementation:

The new binary must not create or replace SteamAccount through legacy linking,
including direct application-service completion calls and existing pending states.
Existing SteamAccount/Library ownership, uniqueness, imports and unlink invariants
remain unchanged. No state rows are consumed/deleted merely to claim retirement;
no schema change, new identity model or v2 behavior is approved by this milestone.
See specs/011-steam-integration/linking-retirement.md for the binary/global boundary.

Feature 013 amendment, 2026-10-28 -- Approved design:

A Game may have either its existing external CoverImageUrl or one private managed
CoverUploadId, never both. Section 16 defines owner-scoped staging, atomic claim,
explicit cover intent, concurrency, bounded recovery receipts and retirement.
CoverImageUrl remains external-only in persistence AND responses; managed covers
return null for that field and are read through authenticated API byte delivery,
never generated/exposed signed URLs. Existing rows/URLs remain unchanged.

The narrow exception supersedes the former upload/storage deferral in sections
16 and 36. It changes no acquisition, Family Friendly, Steam identity/playtime,
Play Log, filtering, Picker or Game/LibraryEntry lifetime invariant. This approves
the design only, not implementation, applied schema or passed verification.
EF migrations and all lifecycle/isolation/concurrency tests remain future work.

Manual review amendment, 2026-08-19:

VideoGames now support optional player-count metadata using MinimumPlayers and
MaximumPlayers. Owned VideoGames with GameStatus = Completed must persist
ProgressPercentage = 100. RandomPickerSession now includes visible volatile
result history, newest first.

Feature 009 amendment, 2026-08-24:

PlayLogEntry is a manual user-confirmed play activity record. It is distinct from
RandomPickerSession, persistent picker history, automatic playtime, external
gameplay imports and analytics.

Feature 009 manual review amendment, 2026-08-25:

PlayLogEntry now records a user-supplied PlayedAt timestamp and may record an
optional positive DurationMinutes value for the individual play occurrence.
CreatedAt remains server-assigned. Users may correct PlayedAt and
DurationMinutes after creation, but PlayLogEntry ownership and associated game do
not change. The ownership path remains PlayLogEntry -> LibraryEntry -> Library.

Feature 010 amendment, 2026-08-24:

At the time of Feature 010, CoverImageUrl was the only persisted cover-image
domain data. This is historical Feature 010 scope, superseded only for managed
covers by the approved Feature 013 amendment and section 16. A user may still
enter an external URL directly or choose one from optional external cover-search
assistance; search candidates, provider metadata and search history remain
non-domain data and are not persisted.

Feature 011 amendment, 2026-08-25; updated 2026-08-28:

Steam integration introduces one optional SteamAccount link per Library and an
optional SteamAppId reference on imported VideoGame LibraryEntries. A SteamAccount
stores the verified 64-bit SteamID for the user's linked Steam account. It is not
an application login credential and does not store Steam passwords, Steam session
cookies or Steam access tokens. A SteamAppId reference is used only to recognize
and avoid duplicate imports from the same Library; it is not a global shared game
catalog. Steam import candidates and Steam API responses are not domain entities.
An imported Steam game is still a normal Owned VideoGame and must satisfy all
VideoGame LibraryEntry invariants. Steam-reported playtime may be persisted on
the LibraryEntry as the latest known accumulated Steam playtime total in minutes.
It is external reference data only and must not create PlayLogEntries, affect
GameStatus, ProgressPercentage, Random Picker eligibility or weighting, or
become automatic gameplay tracking. Steam-provided cover data may populate the
Game's CoverImageUrl only when BOTH CoverImageUrl and CoverUploadId are null and
the Steam image source is reliable and supported. This empty-cover predicate must
be atomic; an existing external URL or managed cover must not be overwritten by
Steam refresh/import behavior.

Feature 012 amendment, 2026-08-28:

Every Game has a required IsFamilyFriendly boolean classification. It applies to
VideoGames and BoardGames and records whether the user considers the game
appropriate for family play. It is not an age-rating, content-warning,
parental-control or minimum-age model. New games default to false unless the user
explicitly marks them family-friendly. Existing rows introduced before this field
are migrated to false. Steam imports must not infer this value from Steam metadata
and use the same false default unless edited later by the user. Library and Random
Picker filtering may optionally require IsFamilyFriendly = true without changing
existing ownership, mode, shown-history or other filter semantics.

1. Purpose

This document defines the core domain terminology, relationships,
invariants and business rules for the Game Library MVP.

It defines domain behavior, not persistence or framework implementation.

2. Domain Principles

User-scoped MVP

All games and library data created by a user are private to that user's
personal Library.

Two users may independently create games with the same name. They are
independent records and edits by one user must not affect another.

There is no shared global Game catalog in the MVP.

Game vs LibraryEntry is deliberate

Game describes game-level information.

LibraryEntry describes the game's presence and the user's personal
relationship with it.

Although Game and LibraryEntry have a 1:1 relationship in the MVP, this
separation is a deliberate domain decision. It keeps game-level data
separate from user-specific library data and leaves room for future
sharing/catalog scenarios without implementing them now.

Distinct game types

A Game is exactly one of:

VideoGame

BoardGame

Manual-first and simple

The domain does not depend on external game services. Future concepts
are not introduced until required.

3. User

For the MVP:

A User has exactly one personal Library.

The User can access only their own Library.

Sharing is unsupported.

Authentication implementation is outside this specification.

4. Library

Each User has exactly one Library.

Each Library belongs to exactly one User.

A Library contains zero or more LibraryEntries.

A Library may contain zero or more PlayLogEntries through its LibraryEntries.

A Library may have zero or one linked SteamAccount when the optional Steam
integration is used.

A Library is private.

4A. SteamAccount (Feature 011 Amendment)

A SteamAccount records that a Library has been linked to a verified Steam user.

A SteamAccount:

Belongs to exactly one Library.

Stores the verified 64-bit SteamID as SteamID64.

Has a LinkedAt timestamp assigned by the backend.

Rules:

A Library may have zero or one SteamAccount.

A SteamID64 may be linked to at most one Library.

SteamAccount is not a Game Library login credential.

SteamAccount must not store Steam passwords, Steam usernames, Steam session
cookies or Steam access tokens.

Unlinking a SteamAccount does not delete imported VideoGames, PlayLogEntries,
Platforms or SteamAppId references on existing LibraryEntries.

5. Game

Game-level information includes:

Name.

Optional CoverImageUrl.

Optional CoverUploadId, mutually exclusive with CoverImageUrl (section 16).

IsFamilyFriendly.

Optional MinimumPlayers and MaximumPlayers when the Game is a VideoGame.

Creation timestamp.

A Game is a VideoGame or BoardGame.

IsFamilyFriendly is required and defaults to false on creation unless explicitly
set to true by the user.

Names are not unique.

A Game is referenced by exactly one LibraryEntry in the MVP and is not
shared across users.

6. VideoGame

A VideoGame may have zero or more predefined Genres.

A VideoGame may optionally contain player-count metadata:

MinimumPlayers.

MaximumPlayers.

A VideoGame has an IsFamilyFriendly classification inherited from Game.

Rules:

Both values are optional for VideoGames.

If either value is provided, both values must be provided.

When provided, 1 <= MinimumPlayers <= MaximumPlayers.

No separate VideoGame-specific player-count model exists. The domain does not
model local players, online players, co-op ranges, matchmaking, or multiplayer
modes in the MVP.

Status, progress, rating, notes, acquisition status and Platforms are
LibraryEntry data.

7. BoardGame

A BoardGame contains:

MinimumPlayers.

MaximumPlayers.

Optional ApproximateDuration in minutes.

Optional InteractionType.

IsFamilyFriendly classification inherited from Game.

Rules:

MinimumPlayers >= 1

MaximumPlayers >= MinimumPlayers

ApproximateDuration, when provided, is positive.

BoardGames do not use Platforms.

8. InteractionType

Supported values:

Cooperative

Competitive

InteractionType is optional.

9. LibraryEntry

A LibraryEntry:

Belongs to exactly one Library.

References exactly one Game.

Has exactly one AcquisitionStatus.

May contain Rating.

May contain Notes.

For VideoGames it may additionally contain GameStatus,
ProgressPercentage and Platforms subject to the rules below.

For Steam-imported VideoGames it may additionally contain a SteamAppId reference
used to avoid duplicate Steam imports in the same Library and nullable
SteamPlaytimeMinutes reference data representing Steam's latest known
accumulated playtime total in minutes.

9A. PlayLogEntry (Feature 009 Amendment)

A PlayLogEntry records one user-confirmed play occurrence.

A PlayLogEntry:

Has an Id.

References exactly one LibraryEntry.

Belongs to the same Library as that referenced LibraryEntry. This ownership path
is indirect through LibraryEntry; PlayLogEntry does not have a separate Library
ownership relationship.

Has a PlayedAt timestamp.

Has a CreatedAt timestamp.

May have DurationMinutes.

Rules:

Only an Owned LibraryEntry can receive a new PlayLogEntry.

VideoGame and BoardGame LibraryEntries can both receive PlayLogEntries.

Multiple PlayLogEntries may reference the same LibraryEntry.

There is no uniqueness rule on LibraryEntryId.

Multiple intentional plays remain valid. After one PlayLogEntry creation
completes, the user may explicitly create another PlayLogEntry for the same
LibraryEntry. User interfaces must not turn one pending Log play activation into
multiple create requests.

PlayedAt is when the user says the play occurred. It is supplied during creation
as an offset-aware timestamp and persisted in UTC/offset-aware form according to
the application's .NET/PostgreSQL conventions. User interfaces may default
PlayedAt to the user's current browser-local date/time, but backend validation is
authoritative.

PlayedAt is required when creating a PlayLogEntry. It must not be materially in
the future. The approved tolerance is server current UTC time plus five minutes to
allow small client/server clock differences. Values later than that are invalid.

CreatedAt is an audit timestamp for when the PlayLogEntry was created and is not
user-editable. CreatedAt is always assigned by the backend when the row is
created. PlayedAt and CreatedAt may differ substantially.

DurationMinutes belongs to the individual PlayLogEntry, not to Game or
LibraryEntry. DurationMinutes is optional. When present, it must be an integer
greater than 0. Null, 30, 90 and 240 are valid examples. Zero and negative values
are invalid. No arbitrary maximum duration is imposed in the MVP.

After creation, a PlayLogEntry may be corrected by editing only PlayedAt and
DurationMinutes. Editing uses the same PlayedAt and DurationMinutes validation as
creation: PlayedAt must not be later than server current UTC time plus five
minutes, and DurationMinutes must be null or greater than 0. Editing may add a
duration to a log that had none, change an existing duration, or clear an
existing duration back to null.

Editing a PlayLogEntry must not change Id, LibraryEntryId, GameId, ownership or
CreatedAt. Editing does not reassign the PlayLogEntry to another LibraryEntry or
Game. If the user wants to record a play for another game, they create another
PlayLogEntry.

Changing an Owned LibraryEntry to Wishlist or Interested does not delete existing
PlayLogEntries, but new PlayLogEntries cannot be created while the entry is not
Owned.

Deleting a LibraryEntry deletes its PlayLogEntries.

PlayLogEntry is not Random Picker history. Random Picker SUCCESS, Another and
visible volatile result history do not create PlayLogEntries automatically.

PlayLogEntry does not model timers, start/end timestamps, automatic playtime,
external play sessions, pause/resume, platform telemetry, scores, outcomes,
player attendance, platform-specific sessions, recommendations or analytics in
the MVP.

Deleting PlayLogEntries, changing the associated game after creation, and richer
PlaySession behavior are outside the feature scope.

10. AcquisitionStatus

Values:

Owned

Wishlist

Interested

Default:

Owned

Only Owned entries are eligible for the Random Picker.

Only Owned entries can receive new PlayLogEntries.

PlayLogEntry PlayedAt is required, is user-supplied during creation and edit, and
cannot be later than server current UTC time plus five minutes.

PlayLogEntry DurationMinutes is optional during creation and edit and must be
greater than 0 when present.

11. VideoGame LibraryEntry Rules

Platform rules:

Owned -> at least one Platform.

Wishlist -> zero or more Platforms.

Interested -> zero or more Platforms.

GameStatus and ProgressPercentage:

Are permitted only when AcquisitionStatus is Owned.

Must be null for Wishlist and Interested entries.

For an Owned VideoGame, GameStatus = Completed implies ProgressPercentage =
100 after create or update completes.

Editing rules:

Wishlist/Interested -> Owned requires at least one Platform.

An Owned entry cannot remove its last Platform while remaining
Owned.

Owned -> Wishlist/Interested preserves existing Platforms.

Owned -> Wishlist/Interested clears GameStatus and
ProgressPercentage.

Rating and Notes are preserved when changing AcquisitionStatus.

The domain rejects any resulting invalid state.

12. GameStatus

Values:

Backlog

Playing

Completed

Abandoned

WantToPlay

GameStatus is optional, applies only to Owned VideoGames, and
Completed is the authoritative completed state.

No separate IsCompleted property exists.

13. ProgressPercentage

ProgressPercentage:

Applies only to Owned VideoGames.

Is optional.

Must be between 0 and 100 inclusive.

It does not automatically determine GameStatus.

GameStatus generally does not determine ProgressPercentage, except for the
approved one-way Completed rule: if an Owned VideoGame has GameStatus =
Completed, ProgressPercentage must be 100 after the operation completes. If the
incoming progress is null or less than 100, the domain normalizes it to 100
rather than rejecting it. Changing away from Completed does not automatically
lower progress; Completed => ProgressPercentage == 100 does not imply
ProgressPercentage == 100 => Completed.

14. Rating

Rating belongs to LibraryEntry and is optional.

Allowed values:





















These represent one through five stars.

15. Notes

Notes belong to LibraryEntry, are optional and personal to the user.

Notes are not Platform-specific.

16. Cover Selection and Upload Lifecycle

CoverImageUrl and CoverUploadId belong to Game and are nullable. At most one is
non-null. Both null means no cover. CoverImageUrl is exclusively an external
manual/search/supported Steam URL in the database AND API responses. A managed
cover has CoverImageUrl=null and a current CoverUploadId; neither API content
paths, Blob/object URLs nor signed URLs are external cover identity. Existing
URLs are preserved; existing Games acquire a null CoverUploadId.

Only the chosen external URL is persisted for external covers. Search candidates,
provider metadata/history, image bytes, Base64, original photos, EXIF/GPS, crop
history and multiple renditions are not domain data. There is one current cover,
not a gallery, fallback stack or user-visible image history.

Cover upload records

One cover-specific table, cover_uploads, records an opaque server-generated ID,
Library, immutable object_key and storage_namespace, canonical byte_size, state,
created_at, cleanup_after, retired_at and claimed_game_id. The immutable namespace
identifies the trusted Storage API base plus private game-covers bucket; a key
must never be reinterpreted under changed provider configuration. Credentials and
client-supplied paths/ownership are not stored identity. Architecture defines the
EF constraints and operational bounds; this is not a generic asset subsystem.

Uploading reserves quota and a durable locator before provider dispatch. Ready
means canonical upload success is confirmed and the unclaimed handle is live.
Attached means the unique current Game reference and first-claim receipt match.
DeletePending is irreversible retirement; it can never become Ready/Attached or
be reused for another Game. Only a live, same-Library Ready upload may first attach.

Game.CoverUploadId is the current nullable restrictive FK, unique when non-null.
claimed_game_id is an immutable first-successful-claim receipt, written in the
same SQL transaction as Game + LibraryEntry + attachment. It is NOT a second
current relationship or a restrictive Game FK: it survives replacement/removal
and Game deletion until bounded tombstone purge. A previously claimed handle
cannot create a second Game in any lifecycle state or transfer between Libraries.
Owner-only recovery may report the original Game ID and whether that Game still
exists; it must not claim that its current fields still equal an earlier request.

Cover mutation intent and concurrency

Create/update and cover-only contracts accept optional coverUpdate with explicit
keep, external, upload or remove intent. Omitted means keep, NEVER clear. Null,
unknown or malformed coverUpdate is 400. keep preserves the current stored cover
(or none on Create); external supplies one valid non-empty external URL; upload
supplies one owned live Ready handle; remove deliberately chooses neither source.
No implicit equality-based intent is allowed, even if desired and old values match.
Historical URL-only save rules do not authorize inferring a cover change from
legacy fields; explicit changes use coverUpdate.

On an existing Game, external/upload/remove require a non-null expectedCover
snapshot containing both nullable coverImageUrl and coverUploadId with at most
one non-null. A missing/null/malformed required snapshot is 400. Compare the
normalized external URL ordinally and upload ID by value with freshly locked
current references. A mismatch returns 409 without
metadata/claim/retirement changes. No extra Game version column is introduced.
keep/metadata-only saves retain a newer current cover instead of restoring the
form's old snapshot. Cover-only operations change no unrelated Game/LibraryEntry,
Family Friendly, Steam playtime or Play Log data. All usual validation remains
authoritative; confirmed rollback undoes metadata, claim and retirement together.

Unknown outcomes and retirement

An exception acknowledging COMMIT, timeout, cancellation or lost write response
is NOT proof of rollback. Reconcile fresh authoritative primary data using the
SAME upload handle/first-claim receipt before retrying. A confirmed never-claimed
live Ready handle permits deliberate same-handle retry; a committed claim requires
refetch of its original owner-scoped Game, not blind Create or metadata merging.
Never delete a possibly committed object as compensation. Missing/purged receipt,
deleted Game or unavailable primary requires an explicit unknown/deleted outcome
and user resolution, not a fresh automatic Create. Bounded receipt recovery is
not general exactly-once CRUD or durable recovery after browser state is lost.

Replacement, removal and Game deletion retire the old managed cover in the SQL
transaction; physical deletion may occur only after that transaction commits.
Never delete a currently referenced object or an external URL target. SQL failure
must not delete the prior cover; provider deletion failure must not undo an
already committed Game change. Unclaimed expired/canceled uploads retire only
after fresh state/reference checks. Irreversible retirement protects provider
deletion outside SQL; cleanup retries and final metadata purge remain bounded as
specified in Architecture, preserving the receipt until safe purge.

The existing Game/LibraryEntry/PlayLogEntry deletion rules remain unchanged.
Cover records retain locators independently of deleted Games for cleanup/recovery.
Future Library deletion must retire/remove its objects before deleting those
locators; this feature does not add an account/Library-delete product workflow.

Privacy, limits and Steam precedence

Every upload/status/content/cancel/claim operation is scoped to the authenticated
Library; IDs alone grant no access. Ready content must be unexpired and owned;
Attached content additionally requires its current same-Library Game reference.
Uploading/DeletePending bytes are not served. Foreign/missing handles are
indistinguishable. Private content uses per-request JWT + Library-authorized API
bytes, not signed URLs, redirects, token queries or persistent client caching.

At most 250 MiB of canonical objects/reservations and ten unclaimed unexpired
Uploading/Ready records are admitted per Library, serialized under PostgreSQL
locking. DeletePending bytes remain charged until safe purge. These are not a
global storage/spending cap. Upload limits and canonical JPEG rules in Product
and Architecture remain authoritative.

Explicit external/search/upload choices take precedence over Steam. Supported
Steam data may fill CoverImageUrl only with an atomic predicate requiring BOTH
current references null. Removing a cover saves neither reference, with no hidden
fallback or immediate enrichment. A later EXPLICIT Steam operation may fill that
empty cover; permanent suppression is not modeled. No other Feature 011/012 rule
changes. An absent/unavailable cover uses a placeholder without changing identity,
selection or Random Picker history.

17. Platform

Platform is user-configurable and belongs to a Library.

A Platform:

May be associated with multiple VideoGame LibraryEntries.

Cannot be deleted while associated with any VideoGame LibraryEntry.

Users must remove associations before deletion.

BoardGames cannot have Platforms.

18. Genre

The complete predefined MVP catalog is:

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

Users cannot create, edit or delete Genres.

A VideoGame may have zero or more Genres.

19. Quick Creation

VideoGame

Required:

Name.

AcquisitionStatus, default Owned.

At least one Platform when Owned.

Platform is optional for Wishlist/Interested.

Player-count metadata is optional.

Other metadata is optional.

BoardGame

Required:

Name.

MinimumPlayers.

MaximumPlayers.

AcquisitionStatus, default Owned.

Other metadata is optional.

20. Filter Semantics

These rules apply to Library and Random Picker filtering:

Multiple selected values inside one filter type use OR / ANY-match.

Different active filter types combine using AND.

Missing optional metadata does not satisfy an active filter
requiring it.

Family-friendly-only filtering is a boolean filter. When active, it matches only
Games where IsFamilyFriendly = true. When inactive or omitted, it does not filter
by IsFamilyFriendly.

Rating means minimum rating: Rating >= requestedRating.

BoardGame player count P matches when
MinimumPlayers <= P <= MaximumPlayers.

For any approved player-count filter on a game type or picker mode, a candidate
matches only when both player-count values are present and MinimumPlayers <= P
<= MaximumPlayers. Missing player-count metadata does not satisfy an active
player-count filter.

Platform, Genre, GameStatus and InteractionType support multiple
selections.

21. Library Search

Search uses a case-insensitive substring match on Game name.

22. Library Filtering

General:

Game type.

AcquisitionStatus.

Rating.

Family friendly only.

VideoGame:

Platform.

Genre.

GameStatus.

BoardGame:

Player count.

InteractionType.

Family friendly only.

VideoGame player-count metadata may be displayed in Library read models when
present. The approved Feature 006 Library player-count filter remains
BoardGame-specific; VideoGame Library player-count filtering is not added by
this amendment.

23. Library Sorting

Name ascending.

Name descending.

Rating descending.

Rating ascending.

Recently added.

Game creation time supports Recently added.

24. Random Picker Eligibility

Only LibraryEntries with:

AcquisitionStatus = Owned

are eligible.

Modes:

VideoGames.

BoardGames.

All.

25. VideoGame Random Picker

Multi-select filters:

Platform.

Genre.

GameStatus.

Scalar filters:

Player count.

Filter semantics follow Section 20.

Family friendly only is available in VideoGames mode and follows Section 20.

26. BoardGame Random Picker

Filters:

Player count.

Available duration.

InteractionType as multi-select.

Family friendly only.

For player count P:

MinimumPlayers <= P <= MaximumPlayers

For available duration D:

ApproximateDuration must exist.

ApproximateDuration <= D

Missing duration does not match an active duration filter.
Family friendly only is available in BoardGames mode and follows Section 20.

27. All Random Picker

All combines eligible Owned VideoGames and BoardGames into one candidate
pool.

Type-specific filters are unavailable.

The MVP includes minimum-Rating, player-count and family-friendly-only filters.

In All mode, player count applies to both VideoGames and BoardGames using the
same range semantics. Candidates missing player-count metadata do not satisfy
an active player-count filter.
Family friendly only applies to both VideoGames and BoardGames in All mode.

No weighting by game type is applied.

28. Random Selection

Obtain eligible Owned LibraryEntries.

Apply active filters.

Exclude entries already shown in the current session while unshown
matching candidates remain.

Randomly select one remaining candidate.

Add it to temporary shown results.

No recommendation scoring, preference weighting, AI, machine learning or
historical optimization is used.

29. RandomPickerSession

RandomPickerSession is temporary application state, not a persisted
domain entity.

It begins when the Random Picker is opened.

It contains current mode, filters, current result and shown entries as
needed by the UI.

It also contains visible volatile result history for the current picker page
session. The history stores returned result objects only in frontend memory and
is ordered newest first.

Changing filters does not reset the session or shown results.

Changing filters does not reset visible result history.

Changing mode does not reset shown results or visible result history.

A previously shown game remains shown even after filters change.

The session ends when the user leaves the Random Picker or the
application/page session is closed or refreshed.

30. Another Result

Previously shown entries are excluded while unshown candidates matching
the current filters remain.

If all matching candidates have already been shown, report that state.

Do not silently reset shown results.

The user may explicitly make previously shown matching games eligible
again.

The explicit reset clears both shown entry IDs and visible volatile result
history. NO_CANDIDATES and ALL_ALREADY_SHOWN do not clear visible history.

31. Zero Candidates

If no eligible game matches:

Select nothing.

Report no eligible games.

Allow filters to be modified or cleared.

Do not silently relax filters.

32. Duplicate Names

Game name is not unique.

Duplicate detection, remake matching and metadata identity resolution
are outside the MVP.

33. Deletion and Game Lifetime

Deleting a LibraryEntry also deletes its associated Game.

Deleting a LibraryEntry also deletes its PlayLogEntries.

The domain must not leave orphan Games.

Deleting or editing one user's Game cannot affect another user.

A Platform in use cannot be deleted until associations are removed.

Managed covers follow section 16: retirement is journaled with Game deletion,
physical deletion is post-commit, and the first-claim receipt survives until safe
bounded purge without keeping the Game alive.

34. Domain Invariants

A User has exactly one personal Library.

A Library belongs to exactly one User.

A Library is private.

A Library may have zero or one linked SteamAccount.

A LibraryEntry belongs to exactly one Library.

Each PlayLogEntry belongs to a Library only through its referenced LibraryEntry.

A LibraryEntry references exactly one Game.

A Game is referenced by exactly one LibraryEntry in the MVP.

A Game is exactly one of VideoGame or BoardGame.

Game records are not shared across users.

A Game has at most one cover source: external CoverImageUrl or CoverUploadId.
Managed attachments are same-Library and single-consumption; active references
cannot be physically deleted. Explicit cover changes use snapshot comparison;
omitted/keep cover intent preserves the current cover (section 16).

Every LibraryEntry has one AcquisitionStatus; default is Owned.

An Owned VideoGame has at least one Platform.

Wishlist/Interested VideoGames may have zero or more Platforms.

GameStatus and ProgressPercentage must be null unless the VideoGame
is Owned.

Owned VideoGame -> Wishlist/Interested clears GameStatus and
ProgressPercentage.

Owned VideoGame -> Wishlist/Interested preserves Platforms, Rating and Notes.

BoardGames have no Platforms.

Rating is an integer from 1 through 5 when provided.

ProgressPercentage is 0 through 100 when provided.

Owned VideoGame with GameStatus = Completed has ProgressPercentage = 100 after
normalization.

VideoGame player counts are optional, but when present both MinimumPlayers and
MaximumPlayers are present and satisfy 1 <= MinimumPlayers <= MaximumPlayers.

BoardGame MinimumPlayers is at least 1.

MaximumPlayers is >= MinimumPlayers.

ApproximateDuration is positive when provided.

VideoGames have zero or more Genres from the predefined catalog.

A Platform in use cannot be deleted.

Only Owned entries participate in Random Picker.

Only Owned entries can receive new PlayLogEntries.

PlayLogEntries represent user-confirmed play occurrences, not Random Picker
history.

Multiple PlayLogEntries may reference the same LibraryEntry.

PlayLogEntry correction after creation is limited to PlayedAt and
DurationMinutes.

Editing a PlayLogEntry does not change Id, LibraryEntryId, GameId, ownership or
CreatedAt.

Changing an Owned entry to Wishlist/Interested preserves existing
PlayLogEntries but blocks new PlayLogEntries while non-Owned.

Deleting a LibraryEntry deletes its PlayLogEntries.

SteamAppId on a LibraryEntry, when present, is immutable after the imported
VideoGame is created and is cleared only by deleting that LibraryEntry.

Within one Library, at most one LibraryEntry may reference a given SteamAppId.

SteamPlaytimeMinutes, when present, is non-negative external reference data. It
may be refreshed from Steam for the same immutable SteamAppId. It is not Play Log
data and does not imply a play occurrence.

Random shown-result history is temporary and not persisted.

Visible Random Picker result history is temporary, newest first, and not
persisted.

Changing filters does not reset shown results.

Changing filters or mode does not reset visible Random Picker result history.

Missing metadata does not satisfy a filter requiring it.

Multiple values within one filter use OR; different filter types use
AND.

Rating filtering uses minimum-rating semantics.

Player-count filtering uses MinimumPlayers <= P <= MaximumPlayers.

Family-friendly-only filtering uses IsFamilyFriendly = true.

Search is case-insensitive substring matching on Game name.

Deleting a LibraryEntry deletes its associated Game.

Game name is not unique.

35. Conceptual Relationships

User
 |
 | owns
 v
Library
 |
 +-------- may link ------> SteamAccount
 |
 +-------- manages --------> Platform
 |
 +-------- owns -----------> CoverUpload (staging/cleanup and bounded claim receipt)
 |
 | contains
 v
LibraryEntry
  |
  +-- has play log --> PlayLogEntry
  |
  | references (1:1 in MVP)
  v
Game
 | current optional managed cover -> CoverUpload (same Library, at most one Game)
 +----------------+
 |                |
 v                v
VideoGame      BoardGame
 |
 +-- Genres

36. Explicitly Deferred Concepts

Outside MVP:

SharedLibraryMembership.

Family.

Friend.

Global shared Game catalog.

Favorites.

Custom Genres.

Image upload/storage beyond the single private cover lifecycle in section 16:
generic assets, galleries, image history, shared/public uploads and original-photo
retention. Signed-read capabilities and stored/persistent client image caches are
not part of the approved private-cover delivery model.

Automatic GameSession / PlayTime.

Deleting PlayLogEntry after creation.

Changing a PlayLogEntry's associated LibraryEntry or Game after creation.

PlayLogEntry revision history, audit log, event sourcing or archival state.

Timers, active sessions, outcomes, scores or player attendance.

Achievement.

Purchase / Price.

CollectionValue.

BoardGameExpansion.

PersistentRandomHistory.

RecommendationProfile.

ExternalAccount, except for the optional Feature 011 SteamAccount link.

ExternalLibrarySync, except for explicit user-selected Steam imports in Feature
011. Background synchronization remains outside the MVP.

AIRecommendation.

37. Architecture Readiness

This Domain Specification is approved for architecture design.

The Product Specification and Domain Specification have passed
independent review with no unresolved structural or blocking domain
issues.
