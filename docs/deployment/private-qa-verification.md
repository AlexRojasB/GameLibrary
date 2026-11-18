# Private QA — local verification

**Railway follow-up, 2026-11-06:** current proposal and new verification are in
[Railway private QA](private-qa-railway.md). Render-specific conclusions below are
historical and are not evidence of an actual Railway deployment.

**2026-11-06 update:** [latest preflight](private-qa-preflight.md) adds authenticated
Render validation and four controlled writable/read-only startup comparisons.
The older failures below remain historical evidence, not current conclusions.

Date: 2026-11-05. **Preparation only; deployment approval pending.**
See [the deployment/approval plan](private-qa.md). No hosted resources, remote
migrations, billing changes, deployment, Storage operation or Git publication.

**Readiness:** configuration and local automated verification prepared; not yet
cleared for external deployment. Resolve build-tool security review, confirm and
authenticate the QA provider scopes/Free project slot, publish an owner-reviewed
snapshot, then obtain explicit resource/migration/deployment approval. Feature 013
release gates are not waived by this limited uploads-disabled QA proposal.

## Checks executed

| Check | Actual result |
| --- | --- |
| Backend solution build, `--no-restore` | Passed, zero warnings/errors |
| Core suite, `--no-build` | 337 passed, zero failed/skipped |
| Complete real-PostgreSQL integration suite | Latest full run: 994 passed, zero failed/skipped |
| Frontend `npm test -- --watch=false` | 465 passed across 38 files |
| QA configuration tests, `npm run test:qa-config` | 15 passed |
| Frontend lint | Passed |
| Ordinary production build | Passed, initial 677.02 kB |
| `npm run build:qa`, synthetic public inputs | Passed, initial 677.13 kB; lazy editor 57.84 kB, budgets unchanged |
| Browser suite, desktop/mobile, no retries | Latest full run: 22 passed, 11 per viewport, zero failed/skipped |
| EF pending-model check | No pending changes |
| EF idempotent migration script generation | Passed locally; output only in session temporary directory, never applied remotely |
| Published Vercel and Render configuration schemas | Both configurations validated locally using existing tools; no provider validation/create API invoked |
| Docker locked restore/framework-dependent linux-x64 publish | Passed after adding RID lock support; no auth/native validation bypass |
| Final-image native packaging | Both expected SHA256 values match; `ldd -r` resolves all dependencies; non-root UID 1654, x86_64, .NET 10.0.14, no SDK, native notices present, no Development settings |
| Local Render-sized basic smoke | 0.1 CPU / 512 MiB, writable ephemeral root: migration, 41 health requests, three authentication denials, two absent test helpers, CORS allow/deny, clean SIGTERM; zero OOM. Not authenticated CRUD/load evidence |
| Production-only npm audit | Zero vulnerabilities; existing Router 22.2.1 and Zod 4.6.6 fixes retained |
| Full npm audit | 32 package records: 3 critical, 8 high, 21 moderate; build/dev-tool review unresolved |
| PWA artifacts | Production manifest, worker and ngsw.json exist; no data cache groups |
| Whitespace / Git staging safety | Tracked/prepared files have no trailing whitespace; staged-diff SHA256 unchanged; generated NuGet locks retain their normal no-final-newline format |

All database-touching runs used newly created, uniquely named disposable databases
on the existing **local PostgreSQL 16** container/port 5433. Those databases were
removed after the runs; existing development/test databases and unrelated Postgres
containers were not reset. Ordinary integration/E2E provider substitution remained
local. Browser E2E deliberately enables the existing guarded test modes locally;
the QA Docker/Render configuration remains Production, without those modes or
Storage credentials. No test assertions, rate limits or timeouts were relaxed.

### Failures and reruns are not erased

- First integration run during parallel builds: **993 passed, 1 failed**. The
  100-ms caller-cancellation test for Storage `read` did not observe cancellation
  inside its waiting stream (`CoverStorageTests.cs:431`). Focused rerun passed all
  four variants; subsequent complete run passed all 994. Scheduling sensitivity is
  suspected, not proven to be the only cause; no test/production code was changed.
- First browser run: **21 passed, 1 failed**, no retries. Desktop delayed-upload/
  Escape journey timed out waiting five seconds for the local-cover editor; the
  captured UI still said **Preparing image...**, before upload. Mobile equivalent
  passed. Builds were running concurrently; no definitive root cause is asserted.
  A fresh complete rerun without concurrent builds passed **all 22**, no retries
  or skips, in 3.9 minutes. The earlier failure remains disclosed, not rewritten
  as a passed run or fixed by weakening an assertion.
- Initial Docker locked restore lacked an API linux-x64 lock target. Explicit
  approved API RID metadata/lock generation corrected it. A later intermediate
  build also exposed an unintended global-RID requirement against Core's generic
  lock; Docker now uses the API-declared RID, and the latest build succeeds. Package
  versions, codec/platform, native hashes and validation rules were not changed.

## Container and resource evidence

Final local image index used by the successful basic smoke:
`sha256:ab067feca6450793957b9ca8fbc01f82645adac8cffc9cf3bb8330e551a58b0c`.
Image config digest:
`sha256:90b5ad323e382b04afb761e91adc57fa0f13eb68d582925177c3cfca0dcd7b28`.
Linux/amd64 manifest:
`sha256:8fdc0ee144ed52d68c767706d21558c5b0dd25796b745f97becf8375fd4353fc`.
These are local artifacts, not a registry upload or approved release snapshot.

Native assets in the ordinary API image:

- Skia: `25b0e8dcaf75444af01e0aa3b29286614ceb56adc80deadb511d3503896fd86b`.
- TurboJPEG: `dffb4dc98ac21a0bdb18ba5987b9ac97fb8ab6da92c9679add294c7cb50caa8f`.

Both stages retain the existing G01 Noble image digests. Docker's constant-platform
lint warnings are intentional because the approved specification requires explicit
amd64 stages. The ENV secret-name warning refers to a **false boolean E2E switch**,
not a credential; no credential build arguments are declared.

The earlier G06 report's 18-scenario resource run used two CPUs, not Render Free's
0.1 CPU. It is historical evidence only, not rerun/claimed as new Render evidence.
A new bounded local 0.1-CPU/512-MiB startup test with a migrated disposable database,
read-only root and 32-MiB tmpfs did **not reach health within its 90-second budget**.
Its logs were not retained, so no definitive root cause is asserted. A diagnostic
follow-up using a normal writable ephemeral root (the proposed Render default),
no loopback HTTP proxy and an empty disposable DB reached health after **59.92
seconds**, without OOM. The changed test conditions do not prove a read-only-root
fix or a CRUD capacity guarantee. The normal-root basic smoke is recorded below;
actual vendor TLS, ingress, cold-start and signal behavior remains unverified.

| Final normal-root basic smoke | Actual result |
| --- | --- |
| Local database migration | All EF migrations applied to a new disposable PostgreSQL database |
| CPU / hard memory and swap limit | 0.1 CPU / 536,870,912 bytes |
| Startup to first successful health response | **73.59 seconds**, within the unchanged 90-second test budget |
| Health | 41 successful responses |
| Unauthenticated protected routes | `/auth/me`, `/library`, `/cover-uploads/options`: 401 |
| Production-only test-helper denial | POST `/e2e/auth/session`, `/e2e/reset`: 404 |
| CORS | Configured origin allowed; hostile origin receives no allow-origin header |
| Cgroup memory.peak | **49,053,696 bytes** (about 46.8 MiB) in this short basic workload |
| Memory max / oom / oom_kill events | 0 / 0 / 0 |
| Local SIGTERM shutdown | **9.6 seconds**, exit 0, OOMKilled=false |

This uses Production mode, no test JWT/fake-provider switches or Storage credentials,
a migrated empty local database and synthetic HTTPS metadata URL. No valid bearer
token was supplied, so **real JWKS/signature validation, authenticated CRUD memory,
Supabase TLS/network behavior and upload availability are not established by this
smoke**. It does not replace the 994 host integration cases or the prior G06 load
run. Do not extrapolate its low peak to a populated Library or native image work.
The initial stricter-root timeout is unresolved, not claimed fixed. The proposed
Render deployment uses a normal writable ephemeral root; no persistent disk or
read-only-root enforcement is configured. Diagnostic startup emitted normal
ephemeral Data Protection-key warnings; no key persistence feature was added.

## Warnings and checks not completed

- Existing stylesheet budget warning: Random Picker 4.13 kB against 4 kB warning
  threshold; existing Vitest removed `poolOptions` warning. Budgets not raised.
- Existing `dotnet-ef` 10.0.7 versus EF runtime 10.0.11 warning; align tools before
  any remote migration. Local successful checks do not waive this procedure.
- No actual Render account/Free-plan limits, deployed image architecture/health,
  request ingress/SIGTERM, memory/CPU workload or billing settings verified.
- No new QA Supabase project, direct TLS migration connectivity, real Auth/JWKS/
  redirects/confirmation, Data API denial, or two-real-account hosted smoke tested.
  Existing connected project's database inspection failed; no unpause/repair attempted.
- No actual Vercel alias/SPA/PWA routing or hosted CORS verified. Local schema/build
  checks are not provider routing proof. Vercel QA scope is still unconfirmed/403.
- Real Storage permissions/adapter/budget checks and physical-device camera/PWA
  release gates remain open and out of this uploads-disabled QA deployment.
- No fresh dependency installation or build-tool security upgrade was performed.
  Existing installed dependencies built/tested; see the targeted candidate versions
  and limitations in the plan rather than treating a runtime-only audit as full safety.

Repository staging fingerprint remains:
`62092d1ed1bbe35197e650619a83a5a9eed14a4495a4223a795223ed11372439`.
