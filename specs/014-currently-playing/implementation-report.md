# GL-17 / GL-27 — local implementation report

2026-11-25. Branch `feature/random-picker-currently-playing`, created from freshly
fetched origin/dev `1433cc5990b52839c28856061365b33b38d16419`. **Ready for review,
not pushed, merged or deployed.** Pre-existing untracked QA preparation files were
preserved, not incorporated into this feature.

## Delivered

- Explicit Select Game on the current Picker card, pending/repeated-click guard,
  success/error feedback and account-change cancellation. Existing Another,
  filters, volatile shown/history and Log play actions remain otherwise unchanged.
- Authenticated GET/PUT `/currently-playing`, using server-derived Library and
  actual game type. Own non-Owned games fail 409; foreign/missing games share 404;
  missing/invalid IDs fail 400. No client owner identity controls authorization.
- One persistent slot per Library/type with atomic idempotent upsert, Library-first
  locking and same-Library composite FK. Concurrent replacements cannot create
  multiple active games; stale proposals are revalidated after lock waits.
- Active-game exclusion in all Picker modes. Replacement removes only that active
  exclusion, not existing filters or volatile shown-ID exclusion. Picker reads use
  a repeatable-read, read-only snapshot so a concurrent replacement cannot remove
  a sampled candidate halfway through the existing multi-query algorithm.
- Both slots on Home, independent empty states, fresh backend loading after
  navigation/reload, retry/error distinction and private cover/placeholder reuse.
- Selection does not alter GameStatus, progress, acquisition, metadata or Play Log.
  Existing game deletion cascades removal of its slot without affecting the other.

## Local verification

| Check | Result |
| --- | --- |
| Currently Playing + existing Random Picker PostgreSQL integration tests | 41 PASS |
| Updated application schema-inventory integration test | 1 PASS |
| Existing RandomPickerRules unit tests | 34 PASS |
| Picker, Home and CurrentlyPlaying Angular tests | 68 PASS |
| Focused Chromium E2E: two Select Game actions → Home → reload → active exclusion | 1 PASS |
| Backend Release solution build | PASS, 0 warnings/errors |
| Frontend production build | PASS, initial bundle 697.98 kB |
| Frontend lint | PASS |
| EF pending-model-change check | No changes since migration |
| `git diff --check` | PASS |

Integration tests used a task-owned disposable PostgreSQL 18.7 container bound only
to loopback. E2E used a separate database named `game_library_e2e` within that
container, existing isolated Test fake auth/provider guards and one desktop test
scenario only. Its first startup rejected a different local database name; the
name was corrected, with no guard/auth/Steam configuration source changes.
Final E2E passed against the final backend. No unrelated CI, cover/native suite,
remote database/service or real Steam provider was exercised.

Frontend build retains the warning for the unchanged Picker SCSS: 4.13 kB versus
the 4 kB warning budget (8 kB error budget). No budget was raised. The existing
Vitest/Vite configuration deprecation warnings remain; tests pass.

## Migration and release boundary

Additive EF migration:
`src/backend/GameLibrary.Core/Data/Migrations/20261125053304_AddCurrentlyPlayingSlots.cs`
(plus generated designer/snapshot). Three Up operations: unique entry owner key,
new slot table/constraints, and composite FK index. No existing-data rewrite.
Applied only to disposable local test databases, **never remotely**.

A future authorized release needs this migration and minimum runtime access on
`currently_playing` (SELECT/INSERT and UPDATE of `library_entry_id`; FK cascades
handle entry deletion). Existing restricted QA grants were not changed. No general
DELETE, broader account privileges or new authentication configuration is needed.

## Product follow-up

Currently Playing is distinct from the pre-existing VideoGame `Playing` status.
No completion/manual-clear/status-driven/acquisition-driven clearing workflow was
added. Slots persist until replacement or existing entry deletion; any future
automatic clearing policy needs a separate product decision. Skip redesign,
persistent Picker history and Log Play Time relocation remain deferred.

No required acceptance decision is left implicit in the implementation contract;
the above future workflows are explicitly outside this delivery. QA/production,
Steam linking, shipped BuildReady and authentication configuration were untouched.
