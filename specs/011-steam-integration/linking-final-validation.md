# Steam OpenID v2 — PR 5 local final validation

Owner authorization, 2026-11-24; baseline origin/dev
`0d96b06cc715d67522f8e0ccb3203845264eca4f` (merged PR #13).
Authority: approved redesign sections 3, 6–13 and 14 PR 5; Option A/D01–D11 unchanged.

Review every T01–T30 obligation and acceptance criterion with named evidence and
honest local/hosted/operational boundaries. Reuse valid prior CI/browser/native
evidence; run standard applicable local suites without a new diagnostic campaign.

Necessary final safety work: payload-free per-process in-flight HTTP observation;
an explicitly acknowledged operator-only invalidation script with Library→row
ordering, bounded transactions/batches and real PostgreSQL failure/restart tests.
No public maintenance/diagnostic route or self-asserted distributed barrier. A
process counter/flag, HTTP disconnect or old MVCC snapshot never proves quiescence.
Preserve consumed receipts/accounts/import data; never revive a cancelled row.
Existing owner-scoped opportunistic cleanup remains; no inactivity deletion SLA.

Prepare reviewable migration/grant, global legacy retirement/drain/reconciliation,
rollout/monitoring/rollback and controlled QA procedures. Distinguish real Steam,
hosted cookie/framing/PWA, infrastructure collection/exposure and release approval
as separately authorized evidence/actions. Do not access hosted user data or logs.

Readiness stays hard false in ordinary builds; SecureLinkingEnabled defaults false.
Unverified external gates cannot be turned into a true readiness claim. Any future
eligibility change requires evidence and separate review; this task does not enable
an isolated hosted QA deployment either. Keep existing Production/Staging fake
rejection and normal gate tests. No schema/migration, dependency, CI-budget,
infrastructure or unrelated feature change. Commit locally; no push/merge/deploy/
remote migration/activation; stop for review.
