# GL-10 — Exclude Completed VideoGames from Picker

Status: owner-requested local implementation for review. Branch
`feature/exclude-completed-picker`, based on freshly fetched origin/dev
`4c821022b3aeaa19aa56878f25663aacbf18940c` (merged PR #18).
GL-10 is the product task ID; Feature 010 remains Cover Image Search.

## Acceptance / scope

- Add a backend eligibility condition: exclude GameType=VideoGame AND
  LibraryEntry.GameStatus=Completed. Apply before candidate/shown classification
  to All and VideoGames, for both legacy and durable draw protocols. BoardGames
  gain no restriction. Null/Backlog/Playing/Abandoned/WantToPlay remain subject to
  existing eligibility; progress 100 alone does not mean Completed.
- The status condition is mandatory even if a user explicitly filters for Completed.
  Multi-value filters retain OR semantics among otherwise eligible candidates.
  Do not silently change filters or relax Owned/manual/Currently Playing rules.
- Reuse GL-16 common candidate revalidation for recovery and repeated acknowledged
  draws: a now-Completed proposal returns null result/outcome with unavailable
  notice. Retain newest-first same-Library history, shown IDs, revision, receipt
  and filter context. No older history item is promoted or made selectable.
- Selection must freshly check type/status after the existing Library lock. Use
  the existing excluded-game 409 response for a same-Library Completed VideoGame;
  foreign/missing games remain 404. Preserve both slots when rejecting stale input.
- Do not change GL-09's IsExcludedFromPicker value when status changes, or status
  when manual exclusion changes. Leaving Completed permits eligibility only if
  all other rules allow it, including saved shown IDs, active slots and filters.
  An unchanged saved proposal may recover when freshly eligible, as in GL-16;
  new draws do not silently unshow it.
- Use existing status editing and normalization unchanged, including Completed→100
  progress and preservation when leaving Completed. No automatic completion or
  cleanup. A game already Currently Playing remains there after becoming Completed;
  Home/logging remain usable and logs are not added/removed by this rule.
- Preserve authenticated Library isolation. Change no Steam code/synchronization,
  play-log behavior, Currently Playing storage or completed-round lifecycle.
- No migration/model, request contract, UI setting, dependency or architecture change
  is needed: existing type/status data and exclusion response suffice.

## Verification / boundary

Focused real PostgreSQL tests cover all statuses including null and progress 100,
both game types and All mode, status transitions, manual flag independence, Owned/
filter/shown/Currently Playing interactions, ordered saved recovery/repeated draws,
stale selection after status edits and actual lock waits, and owner isolation.
Adapt only prior tests whose explicit Completed-eligible fixture is superseded.
One focused E2E uses existing status editing and verifies Picker exclusion,
read-only recovered history, BoardGame eligibility and eligibility after reopening.
Run local tests/builds/lint and EF pending-model consistency; no migration generated.

The initial implementation phase was local-only. The owner's subsequent publication
authorization permits the scoped commit/push, a PR targeting dev and one normal
required CI run. Investigate concrete failures only, preserve assertions and avoid
unrelated diagnostic campaigns. No merge, deployment, remote migration or Steam/QA
change. Preserve pending QA files untouched/uncommitted. Stop for review after
reporting the PR URL, final SHA and observed CI results.
