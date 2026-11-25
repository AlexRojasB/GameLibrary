# GL-17 — Log Play Time from Home

Status: owner-requested local implementation for review. Base: freshly fetched
origin/dev `bf84cffd7f08586397a12eb3f5c0b4db58bd316a` (merged GL-16 and its test correction).

## Scope and acceptance

- Show Log Play Time on each populated Home Currently Playing slot, for both
  VideoGame and BoardGame. Empty slots keep their existing selection guidance.
- Reuse LogPlayDialog and POST `/play-log` with only gameId, PlayedAt and optional
  DurationMinutes. Game identity is fixed by the existing dialog at opening; never
  silently retarget an open form if the slot is replaced. No new logging API/model.
- Keep local-now default, editable date, optional positive whole-minute duration,
  backend UTC normalization/five-minute future tolerance, explicit confirmation,
  readable errors, retained form values on failure and pending duplicate prevention.
  Intentional later logs for the same game remain valid. No automatic retries.
- Successful save closes the form, confirms success and refetches Currently Playing
  (the relevant existing Home data). Distinguish a failed refresh from a failed save.
  No optimistic slot clearing/replacement or new Play Log summary/statistics.
- Opening, cancelling and failed logging do not write slots or Picker rounds.
  Successful logging also preserves them and changes no GameStatus/progress. The
  existing Play Log page loads new entries normally when opened.
- Preserve JWT-derived Library ownership and Owned-only validation in the existing
  backend. A stale selected game that became non-Owned/deleted is rejected normally;
  no auto-cleanup or new eligibility rule is inferred from the slot label.
- Cancel requests/clear dialog, private target, pending state and feedback on
  logout/account switch or page destruction using existing Angular lifecycle patterns.
- Once Home works, remove the Picker's logging button, dialog and handlers. Keep
  Another, Select Game and GL-16 independent round/filter/history behavior intact.
- Library logging and Play Log viewing/editing are unchanged. No completion rules,
  automatic cleanup, statistics, dependencies, architecture changes or schema work.

## Verification and boundaries

Focused Angular tests: both populated slots, empty slots, fixed target/defaults,
date/duration validation and payloads, duplicate prevention, later intentional logs,
cancel/failure preservation, successful refresh/failed refresh, 401/account changes,
navigation cancellation and absence of Picker logging. Retain dialog/Library tests.
Real PostgreSQL verification: reuse existing Play Log ownership/validation tests;
add a focused regression that logging selected games preserves slots/Picker rounds.
One E2E scenario: Select Game → Home → explicit successful logging → Play Log,
with both game types, dates/durations and unchanged selections verified.

No migration is required unless inspection demonstrates an actual schema need.
The original implementation phase was local-only. Subsequent owner authorization
permits a scoped commit/push, PR targeting dev and one normal required CI run.
No merge, deployment, hosted migration, Steam changes or QA changes. Preserve
pending QA files untouched and uncommitted. Stop for review after publication/CI.
