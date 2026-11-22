# PR #1 legacy Steam linking retirement — implementation stopped

2026-11-21. **BLOCKED before application changes. NOT IMPLEMENTED / NOT DEPLOYED.**
Production remains blocked. No retirement barrier has been established or verified
for any running service by this task.

## Preparation completed

- Fetched `origin/dev`; base is
  `503766763fed33a839d37f4107dcd8d1b1a3c59c`.
- Confirmed merged PR #8's head
  `5f8d039e33a6d54cf872792436dc6aa073519b72` is an ancestor.
- Created isolated branch `fix/steam-linking-v2-retire-legacy` in
  `/Users/mac/Documents/Projects/GameLibrary-Steam-Retire-Legacy-2026-11-21`.
- Preserved the original specification worktree and its uncommitted file. Copied
  only the reviewed [security specification](steam-openid-linking-redesign.md),
  byte-identical, into the new implementation worktree without overwriting files.
- Read the complete reviewed specification and compared the retirement milestone
  with current API/Core implementation, existing endpoint tests and Angular UI.

## Blocking concurrency fact

The current `SteamController.LinkCallback` invokes
`SteamIntegrationService.CompleteLinkAsync`. That use case:

1. Checks that a pending state exists.
2. Awaits Steam verification **outside** its PostgreSQL transaction.
3. Locks the pending state, then its Library.
4. Checks expiration, consumption and link uniqueness.
5. Inserts `SteamAccount` and consumes state, then commits.

There is no shared retirement marker consulted by the existing deployed code.
A new controller returning 410, a new frontend notice, or an unconditional rejection
inside a **new** service implementation cannot alter instructions already executing
in an **old** process. It cannot revoke another replica's accepted callback or an
already submitted COMMIT. This is a source-derived limitation, not an experiment
against a running service or evidence that such a request currently exists.

### Counterexample to a source-only global barrier

| Time | Old process | New process / proposed retirement |
| --- | --- | --- |
| t0 | Accepts a valid legacy callback, passes pending-state check and waits for Steam. | Not yet serving. |
| t1 | Still has its accepted handler. | Starts rejecting legacy requests with 410; a local flag or new code is called a barrier. |
| t2 | Valid verification completes; state remains live/unconsumed in SQL. | Continues rejecting new requests. |
| t3 | Locks state/Library and atomically creates the account link using old code. | Cannot prevent that old transaction by changing its own code. |

Consequently **t1 is not a global retirement barrier**. Calling it one would violate
the requested guarantee that no previously accepted callback can create a link after
the barrier. A test that only calls the new controller/service cannot establish the
cross-version guarantee. Restarting one replica or changing an environment flag is
not evidence that all old handlers/database transactions are gone.

## Why implementation is paused

The current user authorization expressly requires documenting the limitation and
stopping if robust retirement requires database changes or a coordinated deployment.
The existing source demonstrates that at least coordinated cutover/draining is
necessary for the **global in-flight guarantee**. No such deployment, shared
database fence or access to real pending transactions is authorized here.

Rather than produce a superficial flag or mislabel local endpoint rejection as full
retirement, application implementation is paused pending clarification of this
operational dependency. No application files, tests, EF model/migrations, flags,
database rows/permissions or cloud configuration were changed. V2 is not implemented
or activated. No commit, push or PR was made.

## Recommended path forward — separate approval required

**Preferred: local retirement artifact plus an explicitly coordinated cutover.**
The eventual artifact can reject all three legacy endpoints and all Core creation/
completion entry points unconditionally, preserve existing account reads/import/
unlink, advertise linking unavailable in Angular, retain safe diagnostics, and
contain no v2 functionality or migration. Its static code barrier applies to requests
executed by that artifact only.

To establish the global barrier during a later authorized release:

1. Stop admitting legacy linking requests on every relevant serving path; retire
   every old replica and prevent restart/rollback to a legacy-capable binary.
2. Drain old admitted callback/initiation work and wait for its database transactions
   to finish. If quiescence cannot be demonstrated, request a maintenance window and
   stop/reroute all old instances. Do not infer completion merely from request-log
   silence, an HTTP disconnect or a termination signal.
3. Resolve any uncertain database transaction outcomes with an authorized, bounded
   primary/lock-barrier procedure. An old snapshot or COMMIT exception does not prove
   rollback. Do not delete a legitimate link as compensation or mark pending rows
   consumed merely to simulate a cancellation workflow.
4. Confirm only retirement-capable instances serve, no legacy writer remains, and
   legacy requests cannot call provider verification or perform a link write. Only
   then declare the operational barrier established. Preserve links committed before
   this point; existing pending rows confer no authority in the retired artifact.

This approach does **not inherently require an EF schema migration** for PR #1.
It does require coordination outside the present local-only execution scope. If a
barrier must take effect while unmodified old binaries continue running, a separately
reviewed shared database enforcement/privilege change or equivalent external control
would be necessary; it is not silently introduced here.

## Required verification after implementation is authorized to continue

- New artifact: legacy initiation/start/callback and direct Core completion reject
  without state/cookie/URL echoes, provider calls or database writes, irrespective
  of enable flags, valid assertions, proof cookies or pending rows.
- Real disposable PostgreSQL: existing links and pending rows preserved, no new link
  on concurrent attempts/held locks; existing status, preview/import, unlink and
  two-user isolation remain valid. Preserve unrelated tests with disposable seeded
  account fixtures rather than a legacy linking helper.
- Explicit cross-version cutover verification: pause admission while an old request
  is held at provider/SQL/COMMIT boundaries; do not declare the barrier until it
  resolves. After that point all legacy completion attempts must fail. A local
  single-version concurrency test is not an equivalent substitute.
- Application logging captures cover valid/invalid/anonymous/encoded targets and
  failure paths; frontend tests show unavailable linking without disabling existing
  account operations. Application redaction cannot remove a URL already logged at
  Railway ingress or erase historical records.
- All five required CI checks on the eventual implementation SHA after publication
  is authorized. No hosted checks, real accounts or services used for this stop.

## Current deliverable / decision requested

Only this blocker report and the exact copied specification are present in the new
worktree. Documentation links, whitespace and scope can be verified locally; no
application build/regression or GitHub CI result is claimed.

Please explicitly authorize continuing with the **local retirement artifact only**
while accepting that global retirement remains contingent on a separately authorized
coordinated cutover, or approve a separate shared-barrier design scope. Until then,
PR #1 implementation/commit remains stopped. There is no implementation ready to
merge, and no assertion that the deployed legacy flow is fully retired.
