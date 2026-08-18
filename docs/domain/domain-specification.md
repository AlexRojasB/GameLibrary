Domain Specification v0.6 --- Game Library

Status: Approved
Version: 0.6

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

A Library is private.

5. Game

Game-level information includes:

Name.

Optional CoverImageUrl.

Creation timestamp.

A Game is a VideoGame or BoardGame.

Names are not unique.

A Game is referenced by exactly one LibraryEntry in the MVP and is not
shared across users.

6. VideoGame

A VideoGame may have zero or more predefined Genres.

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

10. AcquisitionStatus

Values:

Owned

Wishlist

Interested

Default:

Owned

Only Owned entries are eligible for the Random Picker.

11. VideoGame LibraryEntry Rules

Platform rules:

Owned -> at least one Platform.

Wishlist -> zero or more Platforms.

Interested -> zero or more Platforms.

GameStatus and ProgressPercentage:

Are permitted only when AcquisitionStatus is Owned.

Must be null for Wishlist and Interested entries.

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

It does not automatically determine GameStatus and vice versa.

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

It is supplied manually as an image URL.

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

The MVP includes a minimum-Rating filter.

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

Changing filters does not reset the session or shown results.

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

The domain must not leave orphan Games.

Deleting or editing one user's Game cannot affect another user.

A Platform in use cannot be deleted until associations are removed.

34. Domain Invariants

A User has exactly one personal Library.

A Library belongs to exactly one User.

A Library is private.

A LibraryEntry belongs to exactly one Library.

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

BoardGame MinimumPlayers is at least 1.

MaximumPlayers is >= MinimumPlayers.

ApproximateDuration is positive when provided.

VideoGames have zero or more Genres from the predefined catalog.

A Platform in use cannot be deleted.

Only Owned entries participate in Random Picker.

Random shown-result history is temporary and not persisted.

Changing filters does not reset shown results.

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

GameSession / PlayTime.

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