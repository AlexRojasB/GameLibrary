# GL-17 — Home Log Play Time, implementation review

Date: 2026-11-25. Branch: `feature/home-log-play-time`, created from freshly fetched
`origin/dev` at `bf84cffd7f08586397a12eb3f5c0b4db58bd316a` (merged PR #16).
The initial implementation phase was local-only. Subsequent owner authorization
permits publishing this scoped branch as a PR to dev and one normal required CI
run. Local evidence is recorded below; the PR records the published SHA and CI
results. Merge, deployment, remote migrations and Steam/QA changes remain forbidden.

## Implementation

- Added **Log Play Time** on both populated Currently Playing slots in Home.
  Empty slots retain their selection guidance and expose no logging action.
- Reused the existing `LogPlayDialog` and `PlayLogService.logPlay()` POST `/play-log`.
  The selected game's ID/name is fixed at dialog opening. Date defaults to local
  now, duration remains optional, and users can edit both before explicit save.
  Existing required-date/positive-whole-minute validation, backend future-date
  tolerance/UTC normalization and Owned/Library ownership checks are unchanged.
- Guarded pending submissions, preserved values/readable errors for retry, and kept
  intentional later logging of the same game valid. Cancel sends no POST. There is
  no automatic retry or new deduplication rule.
- Success closes the dialog, confirms the save and refetches Currently Playing,
  the existing relevant Home data. A refresh error is distinguished from a failed
  save: confirmation remains, with the normal Home read-error/retry state.
- Account changes clear dialog/target/pending/feedback and cancel old-account
  requests; destruction cancels the component's subscription. As with existing
  HTTP logging, cancellation does not promise rollback of a server-admitted POST.
  Neither the logging API nor the UI writes slots or Picker rounds.
- Removed Picker logging button, dialog, handlers and logging-only state. Another,
  Select Game, filters, ordered history and GL-16 recovery remain unchanged.
- Library logging and Play Log viewing/editing were not modified. No completion,
  automatic slot cleanup, statistics, new logging semantics or dependencies added.
- Updated Product and historical feature scope notes to record the owner's explicit
  relocation decision; added `spec.md` in this directory. Domain/architecture rules
  remain unchanged. Backend production code is unchanged.

## Verification

All database/browser verification used a task-owned pinned PostgreSQL 17 Docker
container, published only on loopback, with separate `game_library_gl17_tests` and
guarded `game_library_e2e` databases. No hosted credentials, Supabase services or real
Steam calls were used. The container/volume were removed and Playwright's local
web servers stopped after verification.

| Local check | Result |
| --- | --- |
| Angular Home, Picker, Play Log/dialog/service, Library and cover-display integration specs | **104 PASS**, 8 files |
| PostgreSQL PlayLogEndpointTests + CurrentlyPlayingEndpointTests | **63 PASS** |
| Core PlayLogRulesTests | **17 PASS** |
| New Home logging E2E scenario, chromium-desktop + mobile-chrome | **2 PASS** |
| API build `dotnet build src/backend/GameLibrary.Api --no-restore` | PASS, no warnings/errors |
| Angular production `npm run build` | PASS, initial **674.92 kB** below 700 kB warning budget |
| `npm run lint` | PASS |
| EF `has-pending-model-changes` for Core/Api | PASS, no model changes |
| `git diff --check` | PASS |

Home tests exercise both types, empty states, preselected target/defaults, cancel,
date/duration/payload validation, pending duplicate prevention, later intentional
logs, server errors with retained input, refresh and refresh failures, stale slot
replacement without retargeting, 401, logout/account switch and navigation.
Picker logging assertions now verify the action/dialog are absent while selection,
Another and history still work and never log automatically.

The new PostgreSQL regression logs both selected types through the unchanged API,
checks submitted date/duration and rejects invalid duration/future dates/foreign
logging, then verifies both slots, all three round snapshots and video status/
progress remain unchanged. Existing Play Log tests cover Owned validation, missing/
deleted/foreign games, timestamps, duration, repeats, lifecycle and user isolation.

One E2E scenario selects VideoGame and BoardGame in the Picker, logs each from Home,
observes the successful refresh, checks Play Log duration/date persistence, and
compares all three recovered round responses before/after logging. Both selected
games remain on Home. A first local attempt matched the SPA document `/play-log`
instead of the API list response during reload; the response predicate now targets
the exact existing API URL. Final desktop/mobile executions pass without retries.

## Migration / limitations / review

- **No migration required or generated:** reused existing API, validation, table,
  model and relationships. No backend runtime code or EF snapshot/migration changes.
- No unresolved product decisions or failing focused check. Remaining build warning
  is the unchanged Picker SCSS (**4.13 kB / 4 kB**); existing Vite/Vitest configuration
  deprecation warnings remain. No budget increase or unrelated optimization.
- Evidence is focused local verification, not a full-suite/CI/hosted certification.
- Pending `deploy/qa/review/` and the three untracked Steam QA deployment documents
  were left untouched. No Steam/QA/CI/dependency files changed.

Publication review confirmed that the diff contains only this delivery's Home/
Picker implementation, corresponding scope notes, tests and this report. No
backend runtime, migration, dependency, budget, workflow, Steam or QA changes.
The pending QA files remain outside the commit. Normal required CI runs once
through the unchanged PR workflow (backend, postgres, frontend, e2e, native-runtime).
Stop for review after reporting the PR URL, final SHA and observed CI results.
