# GL-13 — PWA Back navigation

Status: owner-requested local implementation for review. Branch
`feature/pwa-back-navigation`, from freshly fetched origin/dev
`85af245`. GL-13 is the task ID, not Feature 013 (cover uploads).

## Scope and screen selection

- Reuse a small shared native button and the existing secondary-button styling.
  Replace (do not duplicate) Video games/Board games' static Back to Manage and
  Platforms' Home link. Their direct-entry parent is `/manage`.
- Add a mobile-only Back on Play Log (Home fallback): desktop already has primary
  Play Log navigation; mobile deliberately has only four primary destinations.
- Health has no shell or exit: add Back with guarded Home fallback.
- Home, Library, Pick and Manage already have primary navigation: no Back added.
  Existing auth screens have explicit Sign in/new-email/account paths; retain
  those deterministic controls, not history-driven recovery navigation.
- Steam remains unchanged, with its existing shell/Manage exit. Do not alter
  Steam UI, linking, callbacks, storage or protocol for this task.
- Dialogs/sheets already have Cancel/Close/Escape; no route or Back control added.

## Navigation and security

1. Observe successful Angular navigations from startup. Keep only a volatile,
   current-runtime ledger of known browser entries and a Back/Forward cursor.
   Use public Router navigation/restoration IDs and Location, not history.length,
   referrer, caller-supplied return URLs or persistent navigation storage.
2. Reuse browser history to return to the closest known **different** meaningful
   internal screen. A Back traversal must not append a new entry or destroy Forward.
   Account for replaceUrl, cancelled/failed/skipped navigations and Forward followed
   by a new branch. Unknown/unsuccessful browser traversal or skipLocationChange
   discards certainty of offsets; use a fixed fallback rather than guessing.
3. Only canonical known non-auth application paths serialized by Angular may be
   destinations. Query strings, fragments, matrix parameters, auth/recovery
   (including aliases) and Steam routes are barriers, not values to store or replay.
   Do not traverse across a barrier.
   Reset the ledger on normal account/logout changes. Never capture credentials,
   callback parameters or navigation extras. Existing callback scrubbing and the
   single stable outlet remain unchanged.
4. Reload, direct entry, unknown/old/external history or no eligible predecessor:
   navigate to the fixed screen-specific parent using replaceUrl. No arbitrary
   external fallback or new return-to mechanism. All navigations still run guards.
   Do not rewrite or globally intercept ordinary browser Back/Forward.
5. Prevent repeated activation while navigation is pending. Native button semantics,
   descriptive visible Back-to-destination label, focus styles and >=44px touch
   target; responsive control without changing primary-navigation layout.
6. Preserve only state that existing screens already recover. Library/search/forms
   are component-local and are not newly persisted or route-reused. Picker's existing
   server-recovered rounds remain untouched. No new state-retention promise.

## Acceptance / verification

- Focused real Angular-router tests: normal return, direct/reload fallback,
  replace/back/forward/branch/cancellation, guarded targets, auth/callback barriers,
  unknown history and account invalidation; meaningful component/control checks.
- Affected frontend tests plus build, lint, TypeScript and focused desktop/mobile
  Playwright. Retain recovery/guard/stable-outlet regressions.
- Report changed/unchanged screens, commands/results/warnings and limitations.
  Browser emulation is not installed-PWA, OS gesture, physical-device or offline
  verification. Manifest/service-worker/static-shell policy stays unchanged.

No dependency, native wrapper, backend, schema/migration, Picker-rule, Steam or QA
changes. The initial implementation was local/uncommitted only. The owner's
subsequent publication authorization permits scoped review/corrections, commit,
push, a PR targeting dev and one standard required CI run on the final SHA.
Investigate concrete failures only; preserve assertions and avoid unrelated
validation campaigns. No merge, deployment, QA/remote configuration change or
migration. Record final SHA/check results in the PR and stop for review.
