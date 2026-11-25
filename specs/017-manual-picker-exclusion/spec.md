# GL-09 — Manual Picker Exclusion

Status: owner-requested local implementation for review. Branch
`feature/manual-picker-exclusion`, based on latest origin/dev
`a559321ac172f6f00802be579d53dd3cc20f0534`.
GL-09 is the product task ID; Feature 009 remains the existing Play Log feature.

## Acceptance / invariants

- Every Library VideoGame/BoardGame, regardless of acquisition status, has
  `LibraryEntry.IsExcludedFromPicker`, default false. Existing rows remain included.
  Library browsing still lists excluded games. No new Library filter is required.
- Library cards expose Exclude from Picker / Include in Picker and a visible
  excluded indication. Explicit save sends a desired value, not a blind toggle;
  pending duplicates are disabled, errors retain the last confirmed setting, and
  successful saves reload current Library criteria. No automatic retry/queue.
- JWT-derived ownership is authoritative. PUT `/library/{gameId}/picker-exclusion`
  accepts only the required boolean `isExcludedFromPicker`; missing/null/malformed
  values return 400, missing/foreign games 404, unauthenticated calls 401. Returns
  `{ gameId, isExcludedFromPicker }`. Explicit true/false is idempotent. No Library
  is created by a missing-game mutation. Serialized writes use last-committed value.
- Use the existing Library-first transaction lock, then owner-scoped update of only
  the flag. No full-form metadata overwrite. New Angular mutation subscriptions
  cancel on account change/destruction and do not apply old-account feedback.
- Common backend candidates require Owned AND not excluded AND not Currently
  Playing AND applicable mode/filters, before distinguishing NO_CANDIDATES from
  ALL_ALREADY_SHOWN. Both legacy and persistent draw protocols obey this rule.
- Saved rounds store no changed flag/history snapshot. Fresh recovery (including
  repeated acknowledged picks) returns null proposal/outcome and unavailable notice
  for an excluded proposal; preserve ordered same-Library history, shown IDs,
  revision and filters. Never promote an older historical game into a proposal.
- Selection rechecks exclusion after acquiring the Library lock. Stale requests
  for an excluded game return 409, preserving both slots. Excluding a game already
  Currently Playing leaves it there; Home/logging remain usable. Exclusion and
  re-inclusion never reset rounds or change their stored receipts/history.
- Re-inclusion restores only this eligibility condition. Prior shown IDs still
  suppress new draws until explicit reset; an unchanged saved current proposal
  may recover only when freshly eligible, as in GL-16. Other modes remain independent.
- No game deletion, status/progress/acquisition/platform/cover changes, active-slot
  clearing, Play Log mutation, Steam synchronization changes, completion rules,
  automatic Completed exclusion, statistics, dependencies or general framework.
  Normal metadata edits/Steam refresh must not overwrite the separately stored flag.

## Verification and delivery boundary

Focused real PostgreSQL tests: both types, existing/default false, reversible and
idempotent persistence, owner isolation/auth/invalid inputs, all modes and both
protocols, ordered saved history and repeated-pick recovery, stale selection and
Library-lock revalidation, filter/Currently Playing/Owned/shown interactions and
unrelated state preservation. Migration checks cover additive default/backfill and
EF model consistency. Angular tests cover both card actions, reload/pending/error,
service payload and account/navigation lifecycle. One focused E2E scenario covers
Library exclusion → Picker → re-inclusion on desktop and mobile without retries.

The initial implementation phase was local-only. The owner's subsequent publication
authorization permits the scoped commit/push, a PR targeting dev and one normal
required CI run. Apply the prepared EF migration only to disposable local test
databases; hosted application requires a separately authorized remote migration.
No merge, deployment, remote migration, Steam runtime or QA changes. Leave pending
QA files untouched/uncommitted. Stop for review after reporting the PR/SHA/CI results.

The owner subsequently authorized one fixture-only compatibility adjustment in
SteamSecureMigrationTests.cs: seed the pinned historical schema using its existing
columns rather than the newer EF model. Preserve all checks; no Steam runtime,
configuration, synchronization or security behavior change is authorized.
