# GL-10 — Completed VideoGame Picker exclusion, implementation review

Date: 2026-11-25. Branch: `feature/exclude-completed-picker`, created from freshly
fetched `origin/dev` at `4c821022b3aeaa19aa56878f25663aacbf18940c` (merged PR #18).
The initial implementation phase was local-only. The owner's subsequent publication
authorization permits a scoped commit/push, a PR to dev and one normal required CI
run. The PR records the published SHA and observed CI results. Merge, deployment,
remote migration and Steam/QA changes remain forbidden.

## Implementation

- Added the VideoGame + Completed restriction to the existing Core candidate query.
  All and VideoGames reject these entries in both legacy and durable protocols,
  before NO_CANDIDATES / ALL_ALREADY_SHOWN classification. BoardGames are unchanged.
- Existing GL-16 recovery/repeated-operation handling already revalidates through
  that candidate query. A now-Completed proposal becomes unavailable/null, while
  newest-first history remains visible/read-only. No round rewrite, history reset,
  older-result promotion or stored revision/filter/receipt mutation was added.
- Selection's existing owner-scoped, post-Library-lock read now includes GameStatus
  and rejects a Completed VideoGame with the existing excluded-game 409 response.
  Foreign/missing IDs remain 404. The slot-write operation and current slots are
  unchanged; a previously selected game is not removed when marked Completed.
- No writes to GL-09's manual flag. Leaving Completed removes only this restriction:
  Owned, manual exclusion, filters, Currently Playing and shown-ID rules still apply.
  Null and all other statuses remain eligible when otherwise allowed; progress 100
  alone does not mean Completed. Explicit Completed filters cannot bypass the rule.
- Status editing/normalization, play logs, Steam and completion workflows remain
  unchanged. Completed Owned games can still be logged, including while active.
- Production changes touch only `RandomPickerService.cs` and
  `CurrentlyPlayingService.cs`. No API contract, Angular production code, UI setting,
  dependency, model, mapping, snapshot, migration, workflow or budget change.
- Updated Product/Domain/Architecture scope notes, added the specification here and
  linked it from GL-09's historical deferral. The prior manual-exclusion tests now
  use Backlog rather than explicitly expecting Completed to be eligible; the new
  GL-10 tests separately cover status behavior, progress and manual-flag independence.

## Verification

Tests used a task-owned pinned PostgreSQL 17 Docker container, exposed only on
loopback, with separate test and guarded E2E databases. Existing committed migrations
were applied only to those disposable databases. No hosted credentials/services or
real Steam/Storage calls. The temporary container/volume were removed afterwards.

| Local check | Result |
| --- | --- |
| Completed exclusion, manual exclusion, legacy Picker, durable rounds, Currently Playing, VideoGame and Play Log PostgreSQL tests | **213 PASS**, including **16 new** GL-10 cases |
| Core RandomPickerRulesTests + VideoGameRulesTests | **122 PASS** |
| Focused Angular Picker, Library, Home and VideoGame form/list tests | **155 PASS**, 7 files |
| New Completed status editing → Picker recovery/draw → reopen E2E scenario | **2 PASS**, chromium-desktop + mobile-chrome, explicit `--retries=0` |
| API build | PASS, zero warnings/errors |
| Angular production build | PASS; unchanged initial **676.56 kB** |
| Angular lint | PASS |
| EF `has-pending-model-changes` | PASS, no model changes |
| `git diff --check` | PASS |

New PostgreSQL cases verify all statuses including null and progress 100, BoardGames
alongside Completed VideoGames in All/BoardGames, both draw protocols, manual/status
independence, matching Completed/multi-value filters, Owned and other filters,
Currently Playing preservation, log preservation/logging permission, same-Library
ordered recovery, repeated acknowledged draws, raw stored-round preservation,
reopening without silently unshowing IDs, stale-selection rejection with unchanged
slots, foreign-ID isolation and fresh type/status checks after actual Library-lock
waits. Normal status edits use the existing API; no new status update path was added.

The E2E uses the existing edit dialog to mark the seeded VideoGame Completed,
checks the manual flag was not set and progress still normalizes to 100, draws the
unaffected BoardGame in All, recovers video history read-only, observes no video
candidates, reopens the status to Playing and verifies ALL_ALREADY_SHOWN until
explicit Start over permits another draw with an enabled Select Game action. No network-body snapshots,
sleeps, added retries or enlarged timeouts.

## Migration / warnings / limitations

- **No new migration required or generated.** Reuses the existing GameType,
  GameStatus, GL-09 flag and GL-16 round schema. Prior migrations on dev remain the
  schema prerequisite; no remote database was inspected or changed.
- Existing Picker SCSS warning remains **4.13 kB versus 4 kB**, with unchanged
  budgets. Existing Vite/Vitest configuration/deprecation warnings remain.
- An initial concurrent local check timed out before returning .NET test results,
  and Vitest failed to start its worker without executing tests. The same focused
  checks passed when run sequentially, with no configuration/timeout/assertion
  changes. All final checks above passed; no product failure was observed.
- Focused local evidence only, not a full-suite/CI/native-runtime/hosted claim.
- No unresolved product decisions. No Steam files or QA files changed. The pending
  `deploy/qa/review/` directory and three untracked Steam QA deployment documents
  remain untouched and outside this delivery.

## Publication review

The final diff against freshly fetched origin/dev contains only GL-10 eligibility
and stale-selection implementation, corresponding specifications/report, focused
PostgreSQL/E2E tests and the GL-09 fixture adaptation necessitated by the newly
approved rule. No Steam/QA, schema/migration/snapshot, API contract, Angular production,
dependency, workflow or budget files changed. Pending QA files stay outside the
commit. No new migration is required; prior dev migrations remain prerequisites.

The unchanged PR workflow runs backend, postgres, frontend, e2e and native-runtime
once. Record its observed results in the PR without an unnecessary follow-up source
commit. Investigate concrete failures only; do not weaken assertions or start an
unrelated diagnostic campaign. Stop for review after reporting PR URL, final SHA
and CI results. No merge, deployment or remote migration authorized.
