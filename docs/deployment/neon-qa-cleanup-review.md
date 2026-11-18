# QA-only periodic cover cleanup — review, not implementation

2026-11-15. **Proposal only. No code, default, Docker image or environment changed.**
The owner approved reviewing the option, not disabling cleanup. The approved Neon
provider amendment does not waive production cover lifecycle rules.

## Current source

- `GameLibrary.Api/Program.cs:37` registers `CoverCleanupService` unconditionally.
- `CoverCleanupService` runs immediately, then every 15 minutes. It calls Core
  `CleanupAsync` and separately queries the oldest cleanup deadline.
- `CoverUploadOptions` has no cleanup switch. `CoverUploads__Enabled=false` disables
  new uploads, **not** periodic queries/retirement/deletion/reconciliation.
- Core cleanup scans up to 100 due rows, bounds work to 60 seconds, locks/rechecks
  references, retains receipts and uses Storage only outside the retirement commit.
  The hosted service is the only production call site for `CleanupAsync` found.

On Neon Free, these queries can repeatedly wake otherwise idle compute. A simple
planning model at 0.25 CU and five idle minutes after each 15-minute pass is about
60 CU-hours per 30 days before user traffic, not a measured benchmark. This is a
cost/idle-behavior concern, not permission to skip required cleanup with real covers.

## Smallest proposed change for a later approval

Propose a new API configuration key **`PrivateQa:DisableCoverCleanup`**, environment
name **`PrivateQa__DisableCoverCleanup`**. It does not exist today. Never install it
and claim cleanup is disabled on the frozen binary.

If approved and implemented:

1. Absent/false preserves exactly the existing hosted service, startup pass,
   cadence, bounds, Core lifecycle behavior and production defaults. Do not alter
   `CoverUploads.Enabled`, Core cleanup logic or native/dependency choices.
2. Explicit true is a narrow no-managed-covers private-QA opt-out. Conditionally omit
   **hosted service registration**, not merely skip Storage deletion after a query.
   No startup pass, timer, cleanup scan or oldest-deadline query should run.
3. Fail startup for invalid/malformed opt-out configuration, or true combined with
   uploads enabled or configured Storage base/credential. Keep these checks before
   hosting; do not query the database simply to decide whether to register cleanup.
4. Production-mode hosting remains necessary for QA JWT/E2E safety. Do not switch
   ASP.NET to Development/Test, infer QA from the environment name, infer it from a
   Neon hostname or make cleanup default false globally.
5. Operator evidence must establish a fresh dedicated QA database with **zero
   `cover_uploads` rows and zero non-null `games.cover_upload_id` references** after
   migration/before startup. No import/restore of managed-cover data is allowed in
   this mode. If any metadata exists, stop; do not purge it or enable the opt-out.
6. Never use this flag on existing production/development/managed-cover datasets.
   Retain explicit monitoring and documentation of the disabled QA worker. This is
   not a scheduler framework, an alternate cleanup job or permission to abandon
   objects. No cron, polling service, keep-alive or direct Storage mutation is added.

Production safeguards are default-on behavior plus regression tests and separately
scoped QA deployment configuration. A configuration flag cannot prove that an
operator never mislabels production; do not promise such an absolute guarantee.

## Required verification before using the opt-out

- In Production with flag absent/false, service registration, immediate execution,
  15-minute cadence, 100-row/60-second bounds and existing lifecycle tests unchanged.
- In Production private-QA mode with true and no Storage/uploads, no hosted cleanup
  service/DB factory/Storage operation is invoked at startup or across simulated
  timer intervals. Include the separate oldest-deadline query in the assertion.
- True + uploads, Storage base/credential, or malformed value fails before startup.
- Authentication still validates real QA tokens; `/e2e/auth/session` and `/e2e/reset`
  remain unavailable in Production. No test JWT/provider bypass is introduced.
- Manual URL covers, CRUD/isolation, keep/remove/concurrency contracts and the full
  existing Core/HTTP lifecycle suite retain their meaningful tests.
- Full schema is still required. No new EF migration is expected for an API
  composition-only flag. Re-run Linux build, native packaging and relevant suites
  after any approved code change; document actual results, not just test plans.

## Frozen-source gate

Implementation would change frozen source (`Program.cs` and tests, possibly a small
configuration default) and must be separately authorized. Preserve the historical
407-file manifest/archive; create a **new versioned candidate manifest** covering
the exact approved delta instead of overwriting its hashes. This task does not
authorize that change or treat the reviewed source as already supporting the flag.

The owner can later choose to keep cleanup enabled and accept/monitor its Neon
usage, or approve this limited opt-out implementation. Until that choice and any
implementation verification are complete, deployment remains held.
