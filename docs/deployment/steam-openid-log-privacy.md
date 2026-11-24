# Steam OpenID log privacy — local mitigation, release blocker OPEN

> Historical PR #8 mitigation/evidence. The legacy flow described below is retired
> in current source, not a current activation contract. V2 review and outstanding
> infrastructure privacy evidence are tracked in the
> [final-validation report](../security/steam-linking-v2-final-validation-report.md)
> and [operator runbook](steam-linking-v2-runbook.md). Historical hosted observations
> have not been re-queried or promoted into all-store assurance.

Work started 2026-11-19; local verification completed 2026-11-21.
Base: `origin/dev` `ce7247250b2e0af7909f487dc00860d8f5787400`.
Branch: `fix/steam-openid-log-privacy`, isolated worktree. No push, PR, merge,
deployment, cloud setting change, real database access or real Steam request.

## Confirmed source and scope of evidence

The [previous sanitized investigation](logging-investigation-2026-11-19.md#privacy-findings-and-remaining-gate)
records **two Railway HTTP access records containing the opaque state in the
start URL path**, `/steam/link/start/{state}`. Callback records did not contain
query strings in that sample. The confirmed finding is therefore **start-path
retention**, not a demonstrated callback-query leak. No historical sensitive logs
were retrieved again or copied into this report. The prior EF command-volume fix
does not address this issue.

Railway's current [logs guide](https://docs.railway.com/guides/logs) and
[CLI HTTP-log documentation](https://docs.railway.com/cli/logs#http-logs) distinguish
container stdout/stderr from edge-managed HTTP records. The latter record request
paths independently of application log filters. An ASP.NET middleware/filter
cannot rewrite an already observed edge request target or delete provider logs.
Querying/filtering/hiding a column in the log viewer is not collection-time redaction.

No supported per-route path/query redaction setting was found in the documentation
reviewed. This is **not** proof that Railway support cannot supply an account/plan-
specific control. No provider configuration or fresh hosted behavior was tested.

## Complete linking flow (unchanged)

1. Supabase Auth establishes the application session in Angular. The API validates
   its Bearer JWT and derives the Library owner from `sub`. Steam is **not** an
   application login provider and this flow establishes no new application session.
2. Explicit `POST /steam/link-requests` requires that JWT, exact-origin credentialed
   CORS and the existing rate limit. Already-linked Libraries cannot start another
   link. Core generates 32 cryptographically random bytes, URL-safe encodes them,
   stores only the SHA-256 state hash, and assigns an expiry (default ten minutes).
3. Only this POST issues the existing host-only, HttpOnly correlation cookie with
   the state hash. HTTPS uses Secure, SameSite=None and the `__Host-` name. The
   response is no-store and supplies the existing API start URL to Angular.
4. Browser navigation to `/steam/link/start/{state}` must match the cookie and an
   unexpired, unconsumed SQL state. The API redirects to the trusted Steam endpoint,
   with exact server-generated realm and `return_to` containing callback query state.
   No-referrer/no-store remain in force. This start URL is the observed edge leak.
5. Steam authenticates its own user and returns an OpenID assertion to
   `/steam/link/callback`. Game Library never receives a Steam password/session.
6. Callback cookie/state matching precedes provider work. Core checks pending state,
   asks the trusted verifier for `check_authentication`, and validates namespace,
   mode, endpoint, claimed identity/SteamID, exact return URL and realm. Only
   `openid.*` fields reach the verifier; application Authorization is not forwarded.
7. PostgreSQL locks the state and then its Library; expiry, consumption and link
   uniqueness are rechecked after waits. One atomic commit consumes state and
   creates the account link. Failed validation does not consume state. A transferred
   URL, expired state, replay or conflicting account link cannot gain authority.
8. Success/failure redirects use configured server-owned frontend destinations.
   Callback responses do not overwrite/delete correlation cookies, preserving the
   newer-flow/race behavior. The frontend removes the recognized notice flag from
   browser history; normal JWT authentication remains required for private API data.

No state generation, lifetime, hashing, browser proof, callback validation, SQL
transaction, replay protection, account uniqueness, auth or frontend code changed.

## Logging-layer inventory

| Layer | Finding / boundary |
| --- | --- |
| Railway edge / reverse proxy | Confirmed historical start-path retention; outside this patch. No new proxy is introduced and no global production proxy inventory is claimed. |
| ASP.NET Hosting.Diagnostics | Information start/end events can contain URL/query. Even Warning-only filtering still creates a shared `RequestPath` scope containing path-state, visible to scope-capable providers. A synthetic negative control reproduces this. |
| Routing/MVC | Existing `Microsoft.AspNetCore=Warning` filters data-rich routing, action-argument and redirect Information/Debug events. Do not enable verbose request/action/response logging for this flow. |
| Application middleware | No pre-existing custom HTTP request/body logger was found. New Steam summaries use only constant endpoint templates, status and elapsed time. Unknown Steam paths are collapsed, never echoed. |
| Steam outbound HTTP | The typed client already uses `RemoveAllLoggers`; existing warnings contain fixed text/status only. Fake-HTTP tests verify exceptions containing private URLs/signatures do not reach its logger. |
| EF/Npgsql | Only state hashes reach SQL. Sensitive-data logging remains off; no SQL/mapping/connection policy changed. The earlier Production command filter remains intact. |
| Central exception handler | Previously could emit original exception/inner exception data and publish a handled-exception diagnostic event. A provider exception may embed a private URL even if the fixed top-level message does not. Locally mitigated as below. |
| Authentication | JWT validation and warning/error categories remain enabled and unchanged; query tokens are not application authentication. No raw token logging is added. |
| Telemetry / EventSource | No exporter/SDK is configured in repository code. An attached agent, DiagnosticListener, EventPipe or external auto-instrumentation can observe HTTP data independently of ILogger. Such collection is not certified safe by this patch. |
| Browser / Steam | The approved OpenID redirect requires transient state in URLs. No-referrer/no-store reduce propagation but do not erase browser history, network tooling or Steam-side records. There is no frontend console/telemetry logging in the Steam feature. |

## Minimal compatible application mitigation

- Set **only** `Microsoft.AspNetCore.Hosting.Diagnostics=None` in common settings.
  `Warning` was insufficient because the framework starts its raw path scope when
  any level, including Critical, is enabled. This removes the unsafe hosting URL
  event/scope source across environments, not authentication/provider/EF warnings.
  Other endpoints' pre-existing raw hosting scopes are removed too; their ordinary
  request Information events were already filtered by the existing ASP.NET policy.
- Add one bounded `SteamRequestCompleted` Information event (1100) per completed
  Steam request: a fixed endpoint template, status code and elapsed milliseconds.
  It does not record the Host, actual path/query, method supplied by the client,
  route values, state/hash, Cookie, Authorization, Location or response/body.
- Use .NET 10's `SuppressDiagnosticsCallback` **only for handled Steam exceptions**.
  Emit `SteamRequestFailed` Error (1101), with fixed endpoint template and exception
  type, without passing the original exception, its message, Data or inner objects.
  This replaces the sensitive framework error/handled-exception event; it does not
  conceal that the operation failed. Generic HTTP 500 behavior is unchanged.
- Other endpoints retain their existing exception handler diagnostics. Auth, Steam
  provider and EF Warning/Error/Critical remain available. No new package is used.

This is explicit diagnostic minimization, **not a general-purpose string/regex
redactor**. Logging only `Request.Path` would still leak the existing start token.
Stripping only a top-level `state` field misses encoded `openid.return_to`, scopes,
redirect URLs and exceptions. An allowlisted event avoids those representations.

### Limits of the application claim

Tests cover the checked-in configuration and the normal handled-exception path
for these non-streaming endpoints. Environment/provider-specific logging overrides,
verbose MVC/HTTP logging, third-party providers/agents, telemetry exporters, failures
before the application, exceptions after response start or error-handler failure
require separate review. The .NET callback does not run on every unhandled/aborted
transport path. Do not describe this as a guarantee for arbitrary instrumentation.

No global log category was set to None; only the named raw hosting category is
disabled. Failure diagnostics are retained in a safe representation. Production
operators must verify effective filters and scopes after deployment; a provider-
specific override can undo a configuration-level policy.

## Tests and actual results

All new inputs are synthetic. Assertions inspecting captured data report a generic
failure, not its payload. The synthetic unsafe-scope negative control uses only an
in-memory capture provider, never console or a hosted log service.

| Verification | Result |
| --- | --- |
| Locked solution restore; Release build | PASS; only two existing xUnit2031 warnings in unchanged Steam endpoint tests |
| Local macOS API publish | PASS; common/Production settings byte-identical in output; not a deployment or Noble runtime verification |
| Complete Core suite | **337 passed, zero failed/skipped**; includes existing OpenID/rule regressions |
| DB-free logging/client/composition selection | **63 passed, zero failed/skipped** |
| New privacy tests within that selection | Ten host tests + one fake-HTTP transport test passed |
| Isolation-decision unit suite | **11 passed**; existing guards and CI workflow unchanged |
| New real-PostgreSQL privacy/link tests | Five cases compile and are discovered; **NOT RUN**, Docker daemon and local PostgreSQL binaries unavailable |
| Existing full Steam account-linking/import/relational suite | **NOT RUN locally** for this candidate; needs the disposable PostgreSQL CI harness |
| Frontend/E2E/native-runtime hosted CI | **NOT RUN**; no frontend/native code changed and no push/CI trigger authorized |

The ten host cases exercise Production, Development and Test: rejected start and
callback paths, full-URL exceptions with inner exceptions/Data, preserved non-Steam
failure diagnostics and security warnings, suppression of the handled-exception
DiagnosticListener event, ignored query tokens, unknown paths, and the negative
scope control. Captures inspect formatted text, structured state, scopes and
exception objects, not just message templates. DB/Storage guards assert zero IO.

The five PostgreSQL cases exercise real authorized initiation, the browser cookie
jar, actual start/callback endpoints and fake Steam verification: success, invalid
state, expired state, replay and provider exception. They assert authoritative
link/consumption state, no new application login, no provider call for invalid or
expired state, no replayed provider verification, exact persisted consumption-time
immutability, and absence of state/hash/signature/identity/JWT from captured logs.
They create only synthetic test-owned Library data under the existing harness.
These compiled cases are **not evidence of passing persistence behavior yet**.

DB-free tests ran with a clean process environment, no inherited app credentials,
fake HTTP transports and the existing fail-closed DB/Storage interceptors. An
attempted additional macOS sandbox run failed/timed out in VSTest IPC and is **not
claimed as passed network isolation**. No timeout was raised or CI guard weakened.
The successful local runs are not a substitute for the existing Linux namespace,
privilege and network-isolation gates.

Reproduction after native preparation/locked restore and Release build:

```sh
dotnet test tests/backend/GameLibrary.Core.Tests -c Release --no-build --no-restore
dotnet test tests/backend/GameLibrary.IntegrationTests -c Release --no-build --no-restore --filter 'FullyQualifiedName~SteamLogPrivacyTests|FullyQualifiedName~SteamClientTests|FullyQualifiedName~ProductionLoggingTests|FullyQualifiedName~PrivateQaCleanupConfigurationTests'
python3 -B -m unittest discover -s tools/ci -p 'test_*.py' -v
```

When a Linux disposable runner is available, use the existing guard, not a real
connection string or an EF InMemory substitute:

```sh
bash tools/ci/run-isolated.sh postgres dotnet test tests/backend/GameLibrary.IntegrationTests -c Release --no-build --no-restore --filter 'FullyQualifiedName~SteamLogPrivacyEndpointTests|FullyQualifiedName~SteamEndpointTests'
```

## Separate edge mitigation / approval and verification plan

**Required before closing the release blocker; nothing below was applied.**

1. Confirm with Railway's supported documentation/support whether collection-time
   redaction/exclusion of raw request targets, path segments, query strings,
   headers and redirect/error details is available for this service/plan. Review
   export destinations, retention and access too. Retention/access restrictions
   reduce exposure; alone they do not meet the no-raw-state collection requirement.
2. If a supported control meets the requirement, obtain explicit owner approval for
   that exact provider change and a bounded QA deployment/test. Preserve method,
   safe route, status, timing and failure diagnostics. Do not blanket-disable
   security telemetry or treat a UI search filter as redaction.
3. If path redaction is unavailable, propose a **separately approved Feature 011
   contract amendment**, minimally replacing generated path-state start URLs with
   a fixed start path carrying state in a query (or an explicitly reviewed POST
   handoff). Keep the current cookie/hash, expiry, single-use SQL claim and signed
   exact `return_to` binding. Query relocation addresses the observed path-only
   sink only after evidence proves queries are excluded from all relevant sinks.
   Update producer/consumer tests and address outstanding old start URLs; a
   compatibility alias does not erase previously logged paths.
4. Do not remove callback state or use the correlation cookie alone as a shortcut.
   A fixed cookie-only callback changes the cross-flow assertion binding and needs
   a protocol/replay threat-model review. Replacing state with another opaque URL
   identifier merely renames the sensitive correlation value. A new proxy would
   be an infrastructure/architecture change, not an automatic fallback.
5. After explicit authorization, deploy the exact reviewed artifact to QA and use
   only a synthetic test identity/provider and isolated test data. Verify success,
   invalid/expired/replayed state, mismatch/transferred-browser rejection, provider
   errors and unchanged app authentication. Do not exercise the owner's Steam link
   or 131-game Library, import games, reset QA data or call real Steam accounts.
6. Inspect container logs **and** edge HTTP logs, configured proxy/agent/export sinks
   and scopes for that bounded window. Scan raw/URL-encoded representations in
   process memory; report only counts/outcomes, not tokens or full URLs. Include
   redirects and failures. Record artifact/configuration receipts, time window,
   queries, coverage/pagination and test evidence. Missing/truncated/dropped logs
   cannot establish absence. A future real-Steam protocol smoke needs its own
   explicit approved account and scope; fake-provider testing is not that evidence.
7. Close the blocker only after every identified deployed collection layer has
   verified mitigation and the normal linking/CSRF/replay gates pass. Existing logs
   are not deleted by this patch; historical retention/access disposition is a
   separate owner decision, not an implied credential-rotation authorization.

**Railway configuration required?** With the unchanged URL contract, effective
provider-side collection redaction/exclusion (if supported) is needed. Otherwise
an approved URL-contract change plus evidence of provider query handling is needed.
This patch alone cannot resolve the confirmed source. **Release blocker: OPEN.**

## Proposed PR (not opened)

**Title:** `fix: harden Steam application log privacy; retain edge release blocker`

**Description:**

- Replace raw hosting URL scopes and handled Steam exception payloads with
  allowlisted endpoint/status/timing and payload-free Error diagnostics.
- Preserve the existing OpenID redirect contract, secure state/hash/cookie binding,
  expiry, atomic consumption, account-linking and Supabase JWT behavior.
- Add synthetic logging/exception/telemetry regressions and real-PostgreSQL flow
  coverage; record local passes and unexecuted relational/CI gates explicitly.
- Document the confirmed Railway start-path source and a separate approved
  provider/contract mitigation and verification plan. **Does not close the release
  blocker; not production-ready and not deployed.**

Owner approval is still required before push, PR creation, merge, provider changes
or any deployment. Review the diagnostic tradeoffs and finish relational/CI checks
before treating the application candidate as validated for integration.
