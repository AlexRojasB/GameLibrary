# Railway log-rate investigation

## Scope and result

2026-11-19. Read-only investigation of existing QA evidence; no new Steam imports,
account operations, user-data reads/mutations, database access, provider setting
changes or deployments. The local candidate is based on `dev` commit `29ba0d9`.

**Strongest supported cause:** successful EF Core SQL command text emitted at
Information, expanded into hundreds of console lines for batched imports. Railway
limits stdout/stderr ingestion to **500 log lines/second per replica across plans**,
then drops additional lines. This is a rate/volume problem, not log retention or
the log-query result limit.

## Evidence

Existing QA deployment: `71864044-331f-4fb1-90a3-48462f739aac`, SUCCESS, one replica.
The owner confirmed the later imports as their own activity, not Playwright imports.

| Observation | Sanitized evidence |
| --- | --- |
| Owner import responses | POST `/steam/library/import` 200 at 17:25:34.349 and 17:25:45.892 UTC |
| First import sample | 17:25:34.3–34.8: 487 captured lines, below the 500-result tool cap |
| EF category | Five `Microsoft.EntityFrameworkCore.Database.Command[20101]` headers / Executed DbCommand events |
| SQL within sample | 182 INSERT statements, four SELECTs; one command event can contain many SQL lines |
| Parameters | Five parameter headers; 2,205 `='?'` redaction markers; no enabled-sensitive-data warning observed |
| Platform drop event | 17:25:51.415: explicit Railway 500 logs/sec warning and dropped messages |
| Retention | Platform warning was not about retention; Railway documents Hobby retention separately as seven days |

This sample is not a count of the full import's commands or a claim that every
line was per-game application logging. The second short sample had no retained
rows; the drop event means full source coverage cannot be reconstructed. A broader
context query hit its result cap and was narrowed before making sample claims.

Source matches the evidence:

- `appsettings.json` defaults to Information and suppresses only the
  `Microsoft.AspNetCore` family to Warning. EF command logging inherits Information.
- There is no production-specific EF override in the deployed baseline and no
  service-level `Logging*`/`Serilog*` variable name in the inspected QA service.
- `SteamIntegrationService.ImportAsync` stages Game, LibraryEntry and Platform
  associations, then EF emits batched INSERT SQL on SaveChanges. It does not log a
  success message for each game.
- Preview/re-import may also execute one empty-cover-guarded update per matching
  entry with usable Steam cover data. This is another SQL-volume contributor,
  not a request to change approved cover/transaction behavior in this task.
- No `UseHttpLogging`, explicit `LogTo` or `EnableSensitiveDataLogging` call was found
  in application source. ASP.NET request Information logging is already filtered.
- The real Steam and Storage typed HttpClients use `RemoveAllLoggers`; Steam's own
  provider warnings omit raw URLs/keys/payloads. They are not the observed flood.

No application execution failure is established by the platform drop warning:
the two imports returned 200 and subsequent QA checks found the owner's imported
games and a healthy API. Logging still consumes resources, but no measured CPU,
memory or database slowdown is claimed. The proven impact is missing observability;
missing lines prevent asserting that no other event occurred during that window.

## Small local correction (not deployed)

Add only this provider-independent runtime policy in `appsettings.Production.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Microsoft.EntityFrameworkCore.Database.Command": "Warning"
    }
  }
}
```

QA's ASP.NET environment is Production, so the same policy applies to this hosted
QA topology after a separately approved candidate deployment. This file contains no
environment address, credential or project binding. Development/Test retain their
existing SQL Information diagnostics; normal configuration overrides still apply.

The change filters successful command text, not important failures. SQL warnings,
failed-command errors, critical events, EF query warnings, auth/provider warnings
and application Information summaries remain available. It does not disable all
Microsoft logs, change console format, alter queries/transactions, reduce Railway
security telemetry or require a new logging dependency.

No new per-game or success log was added. Existing provider HTTP logs already give
bounded per-request status/timing evidence. If aggregate import metrics are later
needed, use one structured completion event per operation containing only bounded
counts/duration/outcome, never AppID lists, identities, game names, URLs, payloads,
keys or tokens. That enhancement is not necessary to remove the measured flood.

## Privacy findings and remaining gate

**Release blocker — OPEN:** Steam OpenID `state` is retained in provider-managed
access-log paths. Production release/promotion is blocked until an owner-approved
mitigation is implemented and verified. This logging-volume fix does not resolve
that finding. Never include raw state or token values in review evidence.

Local follow-up: [Steam OpenID log privacy](steam-openid-log-privacy.md) documents
application-scope hardening and a separate edge mitigation/verification plan. It
does not change the start/callback contract or close this provider-side blocker.

Observed EF parameter values were redacted and sensitive-data logging is not enabled
by source. Do not turn it on to investigate real users. This observation is not an
exhaustive secret scan of every possible failure or inaccessible provider log.

An independent provider-access-log check found **two start URLs retaining the opaque
state segment in the path**. Callback access records did not include their query
strings in the inspected sample. No raw state was printed or persisted in this
report. These historical flows are completed; the state is browser-bound, short-lived
and one-time, not an application login token, but this is still a privacy gap against
the no-raw-state logging intent.

The EF filter cannot redact Railway-managed HTTP paths. Before production, review
supported provider path-redaction/access controls and retention with the owner.
Do not simply disable important failures. If provider controls cannot meet the
requirement, separately review/specify a minimal redirect-contract change that
keeps all cookie/state/expiry/ownership checks. No route, cookie policy, infrastructure,
provider setting or specification was silently changed in this patch.

Other typed clients (for example cover search) and exception paths deserve normal
privacy review before production; this task does not certify all logs globally or
expand into unrelated provider changes. No evidence of a Steam API key in the
inspected application log sample was found, and no secret value was retrieved.

## Local verification and remaining hosted verification

`ProductionLoggingTests` starts the actual application composition with a synthetic
unreachable loopback connection and a guard that records/rejects any database or
Storage access. It tests production/development/test filters and sends 1,000
synthetic success events through the real ILogger provider/filter chain, without SQL
or imports. Production captures zero success events and retains Warning/Error/Critical;
Development/Test retain all 1,000. Separate checks retain application summary,
security, provider and EF query warnings. All guards remain at zero IO.

The focused new tests pass (4 cases). A wider relevant, database-free run of logging,
cleanup-composition and fake-HTTP Steam-client/privacy tests passes (52 cases).
Full-solution locked restore and Release build pass with .NET SDK 10.0.201. The
initial no-restore solution build needed the fresh worktree's missing Core.Tests
assets; restoring the solution resolved it without dependency changes. The final
incremental build reports zero warnings/errors; the earlier test compilation emitted
two pre-existing xUnit2031 warnings in untouched Steam endpoint tests.

A local macOS API publish also passes: the output includes
`appsettings.Production.json`, byte-identical to the targeted source policy. This
is packaging/composition evidence, not a deployment or fresh Linux/native-runtime
verification. No frontend source changed, so frontend build/lint/E2E and real-database
integration suites were not rerun for this patch. Markdown link/whitespace checks and
`git diff --check` pass. A focused current-tree static secret-pattern scan found no
high-confidence matches; it is not a full-history or global-secret certification.

Remaining after owner approval: publish/review/integrate the candidate, deploy a
known new QA commit, verify the actual Production policy and retained failures
under a separately approved bounded workload. Do not bulk-import the real 131-game
library merely to generate logs, and do not claim the local synthetic check proves
hosted throughput/latency. Production promotion remains separately gated.

References: [Railway logs/throughput](https://docs.railway.com/guides/logs),
[release workflow](../development/release-workflow.md).
