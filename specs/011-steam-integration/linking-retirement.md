# Feature 011 — local legacy linking rejection (PR #1)

2026-11-21. Owner-authorized **local implementation, tests and commit only**.
No push, PR, merge, deployment, cloud change, real Steam/user data, migration or v2.
Product/Domain/Architecture security-milestone amendments govern this narrow scope.
The reviewed redesign and original blocker report are preserved unchanged under
docs/security/; this milestone does not approve all future v2 decisions.

## Application-level contract

- POST /steam/link-requests, GET /steam/link/start/{rawState} and GET
  /steam/link/callback return constant 410 ProblemDetails, type about:blank,
  code STEAM_LINKING_RETIRED, fixed unavailable title/detail. They disclose no
  state, cookie, return URL, provider result, account or request-specific instance.
  These public tombstones do not establish authentication and require no JWT.
- No redirect, Set-Cookie, pending-state lookup, state generation, provider
  verification, create/replace/consume write or cookie-based authorization there.
  Unsupported methods/unknown paths may return 405/404, never perform linking.
- Core CreateLinkRequestAsync and CompleteLinkAsync unconditionally fail with
  SteamLinkingRetiredException before any DB/provider operation. The former pending
  eligibility probe always returns false. Existing method signatures remain
  rejection tombstones, not an unchecked completion implementation.
- Enable/configuration/E2E flags cannot reopen those paths. No v2 endpoint is
  registered or callable. No global flag is presented as a revocation mechanism.
- All handled Steam responses, including errors, use private,no-store,
  no-referrer and nosniff. Preserve PR #8 safe logs/scopes/handled-exception behavior;
  remove obsolete proof issuance and credentialed linking CORS. Ingress collection
  and historical logs remain outside the application's redaction authority.
- Authenticated status always reads the caller's existing account, returns
  linkAvailable=false and fixed temporary-unavailable guidance. Import availability
  is evaluated independently using the existing provider/Web API configuration and
  production HTTPS rules. Status/preview/import/unlink retain validated JWT scoping.
- Angular has no initiation method/action/button or Steam navigation, even if a
  stale status says linkAvailable=true. Explain unlink cannot currently be followed
  by relinking. Old success/failure flags remain presentation only and cannot claim
  linking succeeded; authoritative status still determines an existing link.
- Existing links and legacy state rows are left unchanged. No migration or schema,
  database privilege, pending-row consumption/expiration/purge operation is added.

## Concurrency proof boundary

In this binary there is no await, SQL, provider verification or account insertion
in the legacy Core operations. Concurrent calls all reject; there is no accepted
link write queued behind a Library/state lock. Earlier provider results cannot
enable these tombstones. This covers execution of this code, not another assembly.

An old replica can have passed its preliminary state check and still commit after
a new replica begins rejecting requests. New code cannot revoke that old execution
or a COMMIT already in flight. The original blocker report's cross-version trace
remains valid. Do not label local tests an established global retirement barrier.

Future coordinated procedure (not executed/authorized by this milestone): stop
legacy admission everywhere; remove/drain every old replica and prevent old-binary
rollback/restart; reconcile uncertain writers/commits on the primary; verify no old
writer can commit; then establish an auditable barrier. Preserve legitimate links
already committed. If drain cannot be proven, request a maintenance window rather
than claiming a flag/restart of one instance is sufficient. V2 remains disabled.

## Acceptance / verification

1. Valid/invalid/missing/expired/consumed/derived-cookie legacy requests reject with
   no provider/DB work; direct Core calls and synchronized concurrent calls reject.
2. Real disposable PostgreSQL tests show pending rows unconsumed and existing links
   not replaced, including held locks, replay, different states/users and historical
   extra runtime grants. Preview/import/unlink/isolation regressions remain covered.
3. Existing account setup for these tests uses disposable fixture rows, not the
   retired protocol or a new production seeding endpoint. Existing guarded E2E reset
   may seed its deterministic existing account only when its fake Steam mode is set.
4. Database-free hosts assert fixed errors/headers, no cookie/redirect, safe logging,
   no credentialed CORS or test/config bypass, and absent v2 routes.
5. Preserve OpenID validation rule/client unit tests as historical/future security
   intent. Old success expectations change to unconditional rejection, not skipped
   assertions. Pending v2-specific concurrency/nonce/browser behavior stays future
   work, not falsely claimed covered by a 410 test.
6. Angular tests/build/lint, Core/build and available API privacy/composition tests
   pass. PostgreSQL/Docker/E2E/native Linux and five hosted CI gates must be reported
   pending when unavailable/not authorized, never inferred from local green results.

Global retirement and production release remain blocked independently of a local
commit. See the local implementation report for actual executed results/limitations.
