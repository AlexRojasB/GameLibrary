# GL-16 — persistent current Picker rounds

Status: owner-approved decisions and local implementation scope. Base origin/dev:
`dfc79021bf5ffd096445b88e5955a3c9158802a0`.

## Narrow exception and behavior

This supersedes Feature 007's volatile-only lifecycle and shared-mode history only
for recovery of the current rounds. Persist exactly three independent rounds per
authenticated Library: All (default), VideoGames, BoardGames. There is no archive
of prior rounds, statistics, completion workflow, Skip button or Log Play change.

- Re-enter/reload/sign-in starts on default All; selecting another mode recovers
  its own saved proposal, ordered newest-first history, shown IDs, outcome and
  filters. Activity/reset in another mode never overwrites that round.
- Another still draws an eligible unshown game. Select Game still sets its
  Currently Playing slot; it does not reset rounds or log a play.
- Filter edits, including Clear filters, preserve the active round's history and
  shown IDs, clear its proposal/outcome, and persist the current filter context.
  As before, history records prior outputs, not a claim that each past item matches
  the latest filters. Recovery restores those latest filters, not another mode's.
- Start over clears proposal/outcome/history/shown IDs only for the active mode;
  filters remain. No archived copy of the old round is made.
- Reads never create an empty Library. Explicit mutation of an empty round may
  bootstrap the caller's Library using the existing clean-context pattern.
- Restore resolves current same-Library game metadata. Omit deleted/foreign items
  without removing their stored shown IDs. Never automatically promote an older
  history item. A deleted, non-Owned, wrong-mode, filtered-out or Currently Playing
  proposal returns null with an unavailable notice; remaining history is read-only.
  Select Game retains server ownership/Owned validation; recovery is no authority
  to bypass current eligibility. No image bytes, signed links or copied private
  metadata snapshots are stored in round state.

## API / consistency

- GET `/random-picker/rounds/{mode}` reads that current round (empty revision 0 if
  absent) and fresh eligible proposal/history.
- PUT `/random-picker/rounds/{mode}` saves validated filters, preserving server
  history/shown IDs and clearing proposal/outcome. Client cannot supply history.
- POST `/random-picker/rounds/{mode}/reset` performs the explicit active-mode reset.
- Existing POST `/random-picker/pick` accepts optional GL-16 `roundRevision` and
  `operationId`. When supplied together, pick + state/history append commit
  atomically, using server-stored shown IDs and validated request filters. Its
  response includes the acknowledged round. The original read-only protocol stays
  available to existing clients/tests without GL-16 mutation metadata.
  A repeated durable pick also revalidates its saved proposal: if it became stale,
  return null proposal/outcome with `proposalUnavailable` on the round, not a new
  draw or the old selectable game. Legacy reads still return the three outcomes.
- Mutations require an expected revision and unique operation UUID. Library-first
  lock + revision check prevents stale overwrites (409). Repeating the immediately
  acknowledged operation with identical input returns the saved state, not another
  draw; reusing that ID with different input or retrying an older operation after
  newer work conflicts. No automatic retry after unknown COMMIT outcome.
- Per-mode online write serialization in the feature service prevents its own
  rapid filter edits from racing. Requests already admitted may finish across
  navigation; errors/account changes discard unsent work. No offline persistence,
  reconnect replay or general synchronization framework. A conflict requires an
  explicit reload of the affected round, not silently overwriting another tab.
- All owner identities come from validated JWT, never request owner/Library IDs.

## Persistence and verification

One additive EF table: `picker_rounds`, PK (library_id, mode), Library FK, validated
mode/state/revision, filter JSON, current entry ID, ordered history/shown UUID arrays,
and last operation ID/fingerprint. At most one current row per mode; no previous
round rows. Entry IDs are opaque references rehydrated only through an owner-scoped
query, not FKs that would block normal Game/LibraryEntry deletion.

Focused real PostgreSQL and Angular tests must cover all three modes, ordered
recovery, empty state, independent reset, filter recovery, server-authoritative
draw/history, repeated/conflicting writes, ownership, deleted/ineligible/active
proposals, account changes and navigation during writes. One local recovery E2E:
Another → leave Picker → return/reload → recover proposal/history/filter context.
The original implementation phase was local-only. The owner's subsequent GL-16
publication authorization permits the scoped commit/push, PR to dev and one normal
required CI run. Review the final diff and address only an obvious small bundle
correction or document its warning; never raise budgets. Pending QA files stay
untouched/uncommitted. No merge, deployment, remote migration or Steam change.
