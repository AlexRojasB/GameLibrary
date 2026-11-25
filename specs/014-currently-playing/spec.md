# GL-17 / GL-27 — Select Game and Currently Playing, first delivery

Status: owner-requested scope; local implementation for review. Base: origin/dev
`1433cc5990b52839c28856061365b33b38d16419`.
Authority: the corresponding Product, Domain and Architecture amendments plus
the unchanged Picker/session, ownership, cover and Game/LibraryEntry invariants.

## Scope and acceptance

- Select Game appears only on the current Picker proposal, not previous-history
  cards. It persists the selected existing Owned game for the caller's Library.
- Two independent optional slots: VideoGame and BoardGame. Replace only the
  selected game's type; repeating the same selection is a no-op. Concurrent
  selections serialize, with the last committed selection occupying that slot.
- Enforce ownership/type/Owned validation server-side using validated JWT identity.
  Client input supplies Game ID only, not owner, Library or slot type. Revalidate
  after acquiring the Library lock so stale proposals cannot select removed or
  no-longer-Owned games.
- Exclude active games before candidate/shown-ID classification in VideoGames,
  BoardGames and All. Preserve SUCCESS / NO_CANDIDATES / ALL_ALREADY_SHOWN.
- A replaced game loses only its active-slot exclusion. Existing filters,
  Owned-only eligibility and volatile shown IDs still apply; do not silently clear
  shown/history state to force it back into the same session.
- Home displays both slots, covers/placeholders and independent empty states.
  Retrieve persisted state afresh on Home/reload; distinguish loading/failure from
  an empty slot. Respect normal 401 and account-change cancellation/clearing.
- Selection does not change GameStatus (including Playing), progress, acquisition,
  notes, rating, covers or Play Log. BoardGames gain no GameStatus.
- Existing deletion remains usable: deleting the selected Game/LibraryEntry removes
  that slot through cascade; the other slot/data remain intact.
- Defer Skip redesign, persistent Picker history, Log Play Time relocation,
  completion, manual clearing, automatic status/acquisition-driven clearing and
  cross-tab live synchronization. Slots remain stored until explicit replacement
  or existing entry deletion; ordinary game edits gain no new side effects.

## API and persistence

`GET /currently-playing` returns `{ videoGame: item|null, boardGame: item|null }`.
Reads do not create an empty Library. Items contain Game/LibraryEntry IDs, type,
current name and nullable external CoverImageUrl / managed CoverUploadId; never
proxy/signed URLs or private bytes. Existing CoverImage handles authorized bytes.

`PUT /currently-playing` accepts `{ gameId: uuid }`, returning the same two-slot
snapshot. The server derives the slot from the game. Empty/null/malformed ID/body
is 400; foreign/missing game is indistinguishable 404; an own non-Owned game is 409;
unauthenticated is 401. Do not automatically replay mutations after an uncertain
outcome; UI directs the user to check Home before selecting again.

One new `currently_playing` table: `library_id`, `game_type`, `library_entry_id`.
Composite PK `(library_id, game_type)` enforces at most one of each; CHECK permits
only the two existing types. Composite entry FK `(library_entry_id, library_id)`
references additive unique `(id, library_id)` on library_entries, enforcing ownership
and cascading entry deletion. Use existing Library-first lock order plus an atomic
upsert; no in-memory concurrency lock, session/history table or new dependency.

Migration: `20261125053304_AddCurrentlyPlayingSlots`. It adds the table/constraints/
index only; no data backfill, status changes or remote application. Future hosted
rollout must separately review minimum runtime access to the new table. Existing
restricted QA roles are not changed here.

## Focused verification

- Real PostgreSQL integration tests: empty reads, selection, idempotency,
  replacement, type independence, cross-client persistence, active exclusion,
  filters/shown semantics, Owned-only selection, stale proposal validation,
  foreign/spoofed ownership, concurrent replacements, deletion, and relational guards.
- Angular service/Picker/Home tests: payload, both types, independent empty/rendered
  slots, refetch on recreation, pending/repeated clicks, unchanged volatile history,
  errors, 401 and logout cancellation.
- One local Chromium E2E: Select VideoGame and BoardGame → Home → reload, then
  a new Picker session excludes both. Reuse existing isolated fake auth/provider
  setup and literal-loopback disposable PostgreSQL; do not modify auth/Steam gates.
- Relevant backend/frontend builds, focused unit/integration tests and frontend
  lint only. No unrelated CI/native/cover diagnostic campaign.

## Product follow-up, not implemented

Any later acquisition/status-driven clearing, completion action, manual clear,
selection history or cross-tab live update policy needs its own product decision.
This delivery does not infer these workflows from the Currently Playing label.
