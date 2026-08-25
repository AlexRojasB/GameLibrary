Product Specification v0.5 --- Game Library

Status: Approved
Version: 0.5

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

Optional cover image URL.

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

Optional cover image URL.

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

Categories such as Family, Party, Solo and Complexity are outside the
MVP.

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

For the MVP, a cover is supplied only as an optional image URL.

There is no image upload or managed image storage in the MVP.

Games without a cover URL use a UI placeholder.

16. Quick Add --- Video Game

The creation flow must prioritize speed.

Initial fields:

Name.

AcquisitionStatus, defaulting to Owned.

Platform when AcquisitionStatus is Owned.

If AcquisitionStatus is Wishlist or Interested, the game can be
saved without a Platform.

All other metadata, including optional player counts, may be completed later.

17. Quick Add --- Board Game

Initial fields:

Name.

Minimum players.

Maximum players.

AcquisitionStatus, defaulting to Owned.

All other metadata may be completed later.

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

Multiple values within one filter use OR. Different filter types combine
using AND.

Missing metadata does not satisfy an active filter requiring it.

For player count P:

MinimumPlayers <= P <= MaximumPlayers.

A VideoGame without player-count metadata does not satisfy an active player
count filter.

21. Board Game Random Mode

Optional filters:

Number of players.

Available duration.

InteractionType as multi-select.

For player count P:

MinimumPlayers <= P <= MaximumPlayers

For available duration D, the game must have ApproximateDuration and:

ApproximateDuration <= D

Multiple InteractionType values use OR. Different filter types combine
using AND.

22. All Random Mode

All mixes owned VideoGames and owned BoardGames into one candidate
pool.

Type-specific filters are not available.

The MVP includes common minimum-Rating and player-count filters in All mode.

For player count P, both VideoGames and BoardGames match using:

MinimumPlayers <= P <= MaximumPlayers.

Any candidate missing player-count metadata does not satisfy an active player
count filter.

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

Optional cover image URL.

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

Image upload or managed image storage.

External platform integrations.

Automatic imports.

Achievements.

Automatic playtime.

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
