# Steam OpenID v2 — PR 2 persistence and private Core

Owner-approved local implementation, tests and commit, 2026-11-24. Base dev
c35ba3bcf6a5e646287c1ac3ef9907670221a0dc; PR 1 retirement is merged as PR #9.
The recommended Option A/D01–D11 baseline in the approved
[redesign](../../docs/security/steam-openid-linking-redesign.md) is authoritative,
particularly sections 7–9, T01–T08/T12/T14/T18/T20/T24–T28 and PR 2 in section 14.
No alternative handoff, token or retention worker is selected.

Implement the seven additive columns, versioned NULL-safe state/UInt64/date
constraints and partial unique Library/expiry indexes specified in section 9.
Preserve legacy pending/consumed rows, accounts, links and unrelated data. No raw
state/browser secret/assertion persistence, fake backfill, triggers or account FK
that erases receipts on unlink. EF migrations remain the only schema authority.

Core creates fresh independent 32-byte S/B and contextual binary V; proof/owner
binding is revalidated on every operation. One live v2 row per Library, replacement
under its lock. Stage once, no account until explicit authenticated confirmation of
the staged SteamID. No mandatory review-call or extra token. Receipt retries require
live U/JWT/B/deadline, matching expected ID and the same current account UUID.
Cancel is idempotent for proven terminal/expired rows, never unlink. Cancel/confirm,
replacement/staging and distinct-Library identity races use SQL, not process locks.

Use fresh contexts/READ COMMITTED and Library→row locks in stable UUID order, 5s
lock timeout, 10s statement timeout and 15s total SQL budget. PostgreSQL
clock_timestamp() supplies recorded times and live eligibility after locks and
all writes/implicit waits. Reconciliation acquires the same lock barrier on a new
primary context and never calls a mutation/provider automatically. COMMIT errors
are not rollback; uncertain creation must never issue assumed-success proof.

Cleanup during creation/cancel deletes at most 100 same-Library v2 rows whose
expires_at+24h has elapsed, rechecked under locks. No inactive-retention SLA, worker,
provider call, cross-owner scan or deletion of accounts. Unlink invalidation/API
transport/provider verification are PR 3 integration work; PR 2 operations stay
internal and unregistered, and legacy public handlers remain unchanged/retired.

Real PostgreSQL 17 tests must cover previous/fresh migration, NULL/phase/format/date
constraints, preservation, proof context/canonical encoding, owner/JWT/expiry,
immutability, replay/receipts, replacement/cancel/identity races, implicit unique
wait expiry and unknown-COMMIT lock-barrier recovery. Unit tests cover exact verifier
encoding and boundary rules. No EF InMemory substitute or silent security skips.
No new public endpoints, Angular edits, v2 activation, native-memory changes,
QA/production migration, push/PR/merge, deployment or global retirement claim.
