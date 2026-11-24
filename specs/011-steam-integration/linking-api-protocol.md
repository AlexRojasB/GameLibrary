# Steam OpenID v2 — PR 3 API/protocol

Owner-authorized local implementation, tests and commit, 2026-11-24. Base:
origin/dev 81ef59dd6b109d280150e8838e154a397c22399e, merged PR #11.
Follow docs/security/steam-openid-linking-redesign.md fully, especially sections
6–8, 11–13 and section 14 PR 3. Option A only; no alternate protocol/handoff.

Implement fixed authenticated POST creation/review/confirm/cancel, trusted Steam
URL in creation body, independent HttpOnly cookie, anonymous callback that strictly
verifies with the pinned Steam provider and stages immutable identity only. Reuse
PR 2 Core proof/ownership/deadline/receipt/reconciliation; integrate atomic unlink
invalidation. Every current link and catalog/import invariant remains unchanged.

Enforce the exact Origin/CORS/preflight, 4 KiB JSON and 16 KiB query envelopes,
duplicate/unknown-field rejection, five/minute initiation, 30/minute owner operations,
four concurrent callbacks, bounded/no-redirect/no-cookie/no-retry backchannel and
safe private headers/errors/diagnostics specified by the redesign. Use validated
signed JWT sub/exp; never client ownership/destination. No raw secret diagnostics.
No provider cancellation/error mutates state. No additional EF migration.

Acceptance: all applicable backend T01–T30 (Core/PostgreSQL/fake provider/logging)
including direct attacks independent of Angular, schema mismatch, late provider/
SQL waits, unknown commit, cookies/Origin/preflight/throttling, malicious fields,
one-use provider nonce and strict streaming bounds. Keep tests/security assertions
and isolation intact. Shipped Development/Test/Production/Staging remain disabled
regardless of true flags; only explicit integration-host DI test admission in
Development/Test may exercise handlers. Never add a public configuration bypass.

Run normal applicable local Core/integration/frontend/build/lint/EF/E2E checks,
record actual failures/counts/limitations and commit locally. No Angular UI change,
dependency/infrastructure, diagnostic campaign, CI budget change, remote migration,
hosted access, push/merge/deploy/activation or global retirement claim. PR 4 owns
trusted Angular/browser confirmation; PR 5 owns full pause/drain/release evidence.
