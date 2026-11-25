# GL-09 — Manual Picker Exclusion, implementation review

Date: 2026-11-25. Branch: `feature/manual-picker-exclusion`, created from freshly
fetched `origin/dev` at `a559321ac172f6f00802be579d53dd3cc20f0534` (merged PR #17).
The initial implementation phase was local-only. The owner's subsequent publication
authorization permits the scoped commit/push, a PR to dev and one normal required
CI run. The PR records the published SHA and observed CI results. Merge, deployment,
remote migration, Steam runtime and QA changes remain forbidden.

## Delivered behavior

- Both Library game types offer **Exclude from Picker** / **Include in Picker**,
  with visible excluded state. The setting is available for all acquisition statuses;
  excluded games remain in Library. Pending duplicate saves are guarded per card,
  failure retains the last confirmed setting and prompts a fresh read, and success
  reloads current Library criteria. No automatic retry or optimistic full-form edit.
- Added `LibraryEntry.IsExcludedFromPicker`, required/default false, and exposed it
  in the Library read model. A narrow authenticated desired-value PUT at
  `/library/{gameId}/picker-exclusion` changes only that column. Required boolean
  validation, JWT-derived Library ownership and indistinguishable foreign/missing
  404s are authoritative in the API/Core. No missing-game Library bootstrap.
- The Core operation uses the existing Library-first PostgreSQL lock order, shared
  with selection and persistent draws. Stale selection rechecks the flag after its
  lock and returns 409 without slot replacement. New mutation and browse/refetch
  subscriptions cancel on account changes/destruction.
- Common server candidates reject excluded games in All, VideoGames and BoardGames,
  for legacy and persistent draws, before candidate/shown-result classification.
  Existing round recovery and repeated-operation recovery use those same candidates:
  excluded proposals are unavailable, not selectable; ordered same-Library history,
  shown IDs, filters, revisions and receipts remain stored unchanged. No promotion
  of a historical game into the current proposal.
- Re-inclusion removes only this eligibility condition. Existing Owned, filters,
  Currently Playing and shown-ID rules still apply. A previously saved proposal
  can recover again only if freshly eligible; new draws never silently unshow it.
- No deletion, acquisition/status/progress/platform/cover change, active-slot
  clearing, play-log mutation, automatic Completed exclusion or Steam runtime change.
  Excluded Owned games, including active games on Home, may still log plays.
- Updated authoritative Product/Domain/Architecture scope notes and the GL-09 spec
  in this directory. No dependency, CI workflow, budget or QA changes.

## Migration

Prepared EF migration `20261125190122_AddManualPickerExclusion`, its designer and
model snapshot. The generated upgrade SQL changes exactly one application column:

```sql
ALTER TABLE library_entries ADD is_excluded_from_picker boolean NOT NULL DEFAULT FALSE;
```

No data deletion, game/status backfill, round rewrite, index, auth/storage change
or second migration workflow. Existing entries are included by default. It must
be reviewed/applied before running this binary against a hosted database; **no
hosted database was changed**. Local PostgreSQL tests exercise actual old-schema
upgrade, both game types, DB default/non-null constraints and rollback preserving
the catalog, slots and saved round IDs. Rollback drops the new preference column.

Two historical migration fixtures needed compatibility fixes because they query/
seed old schemas using today's EF model. The Picker rollback check now projects
only its historical GameId column. With the owner's explicit fixture-only approval,
`SteamSecureMigrationTests.cs` seeds the pinned historical columns using parameterized
SQL instead of current-model inserts. All its original security checks remain.
No Steam production/configuration/synchronization code was changed.

## Final local validation

All database/browser tests used one task-owned pinned PostgreSQL 17 Docker container,
published on loopback only, with separate application/E2E databases and isolated
upgrade-test schemas/databases. No hosted credentials or real Steam/Storage calls.
The temporary container/volume were removed after verification.

| Check | Result |
| --- | --- |
| Focused Angular Library, Picker, Home and cover-display specs (6 files) | **97 PASS** |
| PostgreSQL exclusion, Picker/durable rounds, Currently Playing, Library, Play Log and migration tests | **151 PASS** |
| Authorized historical Steam migration fixture regression | **1 PASS** |
| Core Picker/filter/VideoGame/BoardGame rule tests | **226 PASS** |
| One new Library exclusion/re-inclusion E2E scenario | **2 PASS**, desktop + mobile, explicit `--retries=0` |
| API build | PASS, zero warnings/errors |
| Angular production build | PASS; initial **676.56 kB**, below unchanged 700 kB warning budget |
| Angular lint | PASS |
| EF `has-pending-model-changes` and bounded upgrade-script generation/review | PASS |
| `git diff --check` | PASS |

New PostgreSQL coverage includes reversible/idempotent exclusion for both types,
all three modes and both draw protocols, default inclusion, Completed remaining
eligible, ownership/auth/invalid input, ordered recovery, repeated-operation/stale
selection rejection, actual Library-lock wait revalidation, filters/Owned/Currently
Playing/shown-ID interaction, metadata/log/slot/Steam-reference preservation and
normal metadata edits preserving the separate flag. The new migration upgrade test
covers existing rows and default insertion. Angular tests cover both card actions,
payloads, pending/error behavior, current-criteria reload, account/navigation
cancellation and cancellation of the post-save read. The E2E checks persisted
exclusion for both types, no candidates in all modes, re-inclusion, unavailable
saved proposal with read-only history, and preserved already-shown behavior.

## Warnings / limitations / review boundary

- Existing Picker SCSS warning remains **4.13 kB versus 4 kB**. No style/budget changes.
- Existing Vite/Vitest configuration/deprecation warnings remain.
- Focused local verification only; no full-suite, hosted/native-runtime or CI claim.
  One local test-build attempt timed out before executing tests; the targeted
  fixture was subsequently rebuilt without shared compilation and passed.
- No unresolved product decisions or failing final focused check.
- Pending `deploy/qa/review/` and the three untracked Steam QA deployment documents
  were left untouched. No Steam change beyond the expressly authorized test fixture.

## Publication review

The diff against freshly fetched origin/dev contains only GL-09 implementation,
specification notes, tests, report, additive EF migration/designer/snapshot and
historical migration-fixture compatibility corrections. The Steam fixture's entire
post-seed body and every assertion were compared with origin/dev and are identical,
including no authority backfill, immutable legacy rows, pinned security boundary,
partial uniqueness, receipt survival and cascade checks. No Steam runtime area,
dependency, workflow, bundle budget or QA file changed. Pending QA files remain
outside the commit. Hosted use of the new binary still requires the separately
authorized migration described above; publication does not apply it.

The unchanged PR workflow runs backend, postgres, frontend, e2e and native-runtime
once. Investigate concrete failures only; do not weaken assertions or start an
unrelated diagnostic campaign. Stop for review after reporting the PR URL, final
SHA and observed CI results. No merge, deployment or remote migration authorized.
