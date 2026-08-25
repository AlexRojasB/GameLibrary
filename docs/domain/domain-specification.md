Domain Specification v0.7 --- Game Library

Status: Approved
Version: 0.7

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

CoverImageUrl remains the only persisted cover-image domain data. A user may
enter the URL directly or choose a URL from optional external cover-search
assistance, but search candidates, provider metadata and search history are not
domain entities and are not persisted.

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

A Library is private.

5. Game

Game-level information includes:

Name.

Optional CoverImageUrl.

Optional MinimumPlayers and MaximumPlayers when the Game is a VideoGame.

Creation timestamp.

A Game is a VideoGame or BoardGame.

Names are not unique.

A Game is referenced by exactly one LibraryEntry in the MVP and is not
shared across users.

6. VideoGame

A VideoGame may have zero or more predefined Genres.

A VideoGame may optionally contain player-count metadata:

MinimumPlayers.

MaximumPlayers.

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

16. CoverImageUrl

CoverImageUrl belongs to Game and is optional.

For the MVP:

It is stored as an image URL.

It may be supplied by manual URL entry or by the user selecting a URL from
external cover-search assistance.

Only the selected URL is persisted; cover-search candidates, provider metadata and
search history are not domain concepts.

Image upload is unsupported.

Managed image storage is unsupported.

A null value is valid and the UI uses a placeholder.

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

VideoGame:

Platform.

Genre.

GameStatus.

BoardGame:

Player count.

InteractionType.

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

26. BoardGame Random Picker

Filters:

Player count.

Available duration.

InteractionType as multi-select.

For player count P:

MinimumPlayers <= P <= MaximumPlayers

For available duration D:

ApproximateDuration must exist.

ApproximateDuration <= D

Missing duration does not match an active duration filter.

27. All Random Picker

All combines eligible Owned VideoGames and BoardGames into one candidate
pool.

Type-specific filters are unavailable.

The MVP includes minimum-Rating and player-count filters.

In All mode, player count applies to both VideoGames and BoardGames using the
same range semantics. Candidates missing player-count metadata do not satisfy
an active player-count filter.

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

34. Domain Invariants

A User has exactly one personal Library.

A Library belongs to exactly one User.

A Library is private.

A LibraryEntry belongs to exactly one Library.

Each PlayLogEntry belongs to a Library only through its referenced LibraryEntry.

A LibraryEntry references exactly one Game.

A Game is referenced by exactly one LibraryEntry in the MVP.

A Game is exactly one of VideoGame or BoardGame.

Game records are not shared across users.

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
 +-------- manages --------> Platform
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

Image upload/storage.

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

ExternalAccount.

ExternalLibrarySync.

AIRecommendation.

37. Architecture Readiness

This Domain Specification is approved for architecture design.

The Product Specification and Domain Specification have passed
independent review with no unresolved structural or blocking domain
issues.
