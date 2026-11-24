# Steam OpenID v2 — PR 4 Angular/browser

Owner-authorized local implementation/tests/commit, 2026-11-24. Base:
origin/dev d19d96a1145d770cb9659ea57d462bd7757ef1ff, merged PR #12.
Follow the entire approved redesign, especially sections 7/10/12 and section 14 PR 4.

Use fixed v2 API contracts and withCredentials on only four v2 JSON operations.
Navigate same-tab with location.replace only to exact HTTPS Steam host/path, no
credentials/nondefault port/fragment. Retain only T/initiating U/deadline in tab
sessionStorage; no URL/S/B/assertion/new JWT copies. Storage failure requires restart.
No discovery, v1 fallback, automatic retry/confirmation/preview/import or new token.

After auth initialization, reload/review live handle using current JWT + cookie;
callback flag is presentation only and recognized flags alone are removed by
replaceUrl. Display full immutable canonical SteamID and current account label;
explicit Confirm/Cancel only. Handle expiry, missing/blocked proof, account change,
401 reauthentication, rate/errors and unknown response via explicit durable review,
not blind mutation replay. Clear metadata/private state on terminal/expiry/observed
subject change/logout. Same-subject refresh is not an account switch; ignore stale
responses even across U→W→U. Do not claim revocation of admitted backend requests.

Add response-header CSP frame-ancestors 'none' and X-Frame-Options DENY for frontend
routes/SPA fallback in repository host configuration, with local actual-header and
cross-origin frame tests. No hosted deployment or all-browser support claim.

Use existing E2E guards plus explicit test-only linking opt-in, loopback DB/base
validation and isolated provider substitution; Production/Staging must reject test
enablement. No change to hard-false normal readiness, resource budgets or isolation.
Real browser cookies/native navigation must reach real handlers/Core/PostgreSQL;
provider fixture returns realistic signed assertions and one-use verification.

Applicable T08–T11/T17/T19/T23/T24/T30 plus success/cancel/expiry/invalid proof/
account mismatch/replay/concurrent confirmation and interrupted-flow coverage.
Run complete standard local checks and E2E regressions; document remaining real
Steam/hosted browser/PWA/operational activation gates. No remote migration, dependency,
activation, unrelated refactor, push or merge. Commit locally and stop for review.
