# GL-15 — Manage VideoGames title search

Status: owner-requested local implementation for review. Branch
`feature/manage-videogames-search`, based on freshly fetched `origin/dev`
`0a1945b4dbd565dc280e6d0e349c9c5282ebb692` (merged PR #19).
GL-15 is the product task ID; Feature 015 remains persistent Picker rounds.

## Acceptance / scope

- Add a visibly labelled, responsive search field on `/video-games`, following
  Library's search input conventions. Match case-insensitive literal substrings
  of Game.Name; trim surrounding whitespace, empty/whitespace means no search.
- Keep an explicit Clear search action available, including while loading or on
  failure. Distinguish no matches from the existing empty-library message and
  explain how to recover. Keep Add available when search has no matches.
- Reuse existing `/library` search with `gameType=VideoGame` and `sort=NameAsc`:
  its read model contains every VideoGame edit field and its ordering exactly
  matches `/video-games`. Preserve the existing unfiltered `/video-games` call
  for empty search and cover-save reconciliation. No backend change is needed.
- Inspection confirms Manage VideoGames has no filter controls, sort selector,
  page state or pagination in either the UI or its list API. Preserve its implicit
  VideoGame filter and deterministic name ordering; search the full collection
  server-side. Pagination reset and other filter-control interactions are not
  applicable; do not invent pagination or copy Library's advanced UI into Manage.
- Use the existing request-generation/account guard for search loads, including
  stale errors. Release pending list reads on destruction. Search is local to
  this page instance and not persisted; CRUD refresh retains the current search.
- Preserve create/edit/delete, cover recovery and account-isolation behavior.
  Do not change BoardGames, Picker, Steam, authentication or QA.

## Verification / boundary

Focused frontend tests cover query normalization/transport/type restriction/sort,
clear/whitespace, no matches, full server-result rendering (not a loaded-page
filter), out-of-order success/error, destruction and CRUD reload with search.
A focused desktop/mobile E2E exercises mixed-case partial search, clearing,
no-results recovery, BoardGame exclusion and edit flow.
Run existing Library PostgreSQL search/filter/sort/isolation regressions as evidence
for the reused unchanged API, affected frontend suites, builds and lint.

No migration, dependency, advanced search syntax or architecture change. The initial
implementation was local and uncommitted. The owner's subsequent publication
authorization permits committing/pushing the scoped GL-15 changes, opening a PR
targeting dev and observing one normal required CI run on the new SHA. Investigate
concrete failures only; preserve assertions and avoid unrelated campaigns. No merge,
deployment, remote migration, QA or Steam changes. Record final SHA/check results in
the PR and stop for review.
