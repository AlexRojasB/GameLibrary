# Steam Playtime Delta Lifecycle

## Status

Proposed, awaiting approval. This is a clarification proposal for the future explicit
logging flow, not an approved persistence change or Feature 013 implementation.

## Current Approved Behavior

The approved Product and Domain amendments permit only the latest cumulative Steam
total on LibraryEntry. Feature 011, Steam Playtime and Import criterion 14, defines
the arithmetic for a transient increment on each preview/re-import:

- Read the previously persisted total before replacing it.
- Missing current data preserves the previous total and produces zero increment.
- A missing baseline produces zero increment; the current total establishes it.
- Otherwise calculate `max(0, current - previous)`.
- Unchanged totals produce zero; decreased totals produce zero and replace the
  stored baseline with the lower reported total.
- Never create PlayLogEntries, assign a played date, or infer historical sessions.

The stabilization implementation applies that arithmetic through Core's shared
refresh operation. Preview and import serialize their short database refreshes on
the same Library row, after the external Steam call, so the previous total is read
after the lock. The return value from the per-entry refresh is transient; neither
existing API contracts nor the database expose/store a pending increment.

## Gap

The approved specification does not define how a future explicit "Log Steam
playtime" action retains, acknowledges, consumes, or deduplicates increments.

For example, a refresh from 100 to 150 minutes computes 50, but an immediate second
preview sees 150 to 150 and computes zero. Exposing only that operation's delta
would not preserve an unlogged increment across refreshes, navigation or retries.
Persisting only the last delta would also lose or double-count increments and would
not define user confirmation or corrections. Latest cumulative total and
acknowledged-for-logging total are different concepts.

No additional persistence, API delta field or logging action is implemented here.

## Smallest Proposed Amendment

Before an explicit Steam logging action is implemented, amend Product, Domain and
the relevant feature specification to approve the following lifecycle:

1. Keep SteamPlaytimeMinutes as the latest observed cumulative total. Never reinterpret
   it as manually logged duration or a persistent pending delta.
2. Add one separate nullable per-LibraryEntry acknowledged cumulative baseline, such
   as SteamAcknowledgedPlaytimeMinutes, through an EF migration. This avoids storing
   every refresh delta, provider payload, session or timestamped gameplay history.
3. On first observation, initialize the acknowledged baseline to the current total
   without creating a log. For preexisting imports, initialize from the known total
   when enabling this future flow. Do not offer lifetime totals as inferred sessions.
4. Compute pending minutes as `max(0, latest - acknowledged)`. Ordinary refreshes
   update latest but do not consume pending minutes. Missing data preserves known
   values. A decrease below the acknowledged baseline explicitly rebases it to the
   corrected total, producing no negative pending minutes or new log.
5. Offer a single explicit confirmation for the known pending increment. The user
   supplies/confirms PlayedAt under normal Play Log validation; a Steam total does
   not tell the application when any individual session occurred. The UI must not
   present an invented historical date or split the increment into sessions.
6. On confirmation, atomically recheck ownership, Owned eligibility and the pending
   snapshot, create at most one user-confirmed PlayLogEntry, and advance the
   acknowledged baseline through exactly the confirmed total. Concurrent/repeated
   confirmations must not log the same increment twice; newer observed increments
   must remain pending. A changed snapshot requires confirmation again, not an
   automatic retry with a different duration.
7. Expose latest/pending totals and the confirmation snapshot only in the future
   explicit flow. Keep background sync and automatic logging prohibited. Manual
   Play Log entries remain independent unless a separate approved rule says otherwise.

This proposal requires approval, including the chosen field name, correction and
confirmation semantics, before any additional schema/API/logging implementation.
