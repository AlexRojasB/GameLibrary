# Approved security candidate applied — publication held

2026-11-06. **Exact dependency application approved and completed.** Owner
provisionally accepts the remaining 17 moderate package findings for private QA
preparation, subject to risk assessment/monitoring. **No Git publication or cloud
write is approved.** Uploads remain disabled for proposed hosted QA.

This status supersedes the earlier dependency record's **unapplied/approval-pending**
status. Earlier reports remain untouched as historical snapshot evidence. The
latest owner selection is a **separate Game Library project in the existing
`bar-book` workspace**; see [current workspace readiness](vercel-workspace-qa-readiness.md).
The personal-only choice was superseded. No BarBook project changes or redeployment
are authorized. At that preparation stage workspace access/plan verification was
blocked by 403; the later read-only recheck below supersedes that access finding.

**Later preparation updates:** the subsequent authenticated Vercel recheck cleared
the workspace access/plan/name preflight (confirmed Hobby and OWNER); creation and
deployment remain unauthorized. The owner also selected the existing Supabase
**`game-library` in Connective**, replacing new `game-library-qa` creation. Its
exact QA reference is now **`yqngxhryzklrhhryzzoi`**, distinct from protected
development. The separate read-only `supabase_qa` MCP is now authenticated and
returns that exact target; read-only database metadata and public QA discovery/
JWKS HTTPS checks passed. Plan/region and deployment prerequisites remain pending;
public signup is enabled and requires an approved private-QA settings change. Use
[existing-project QA readiness](supabase-existing-project-qa-readiness.md)
and `deploy/qa/supabase-project-plan.json`, not earlier project-creation instructions.

## Applied change and exact graph

Only two pre-existing files changed in this task:

- `src/frontend/package.json`: `@angular/build` and `@angular/cli` selectors
  `^22.1.4` → exact **22.2.6**; every other field is unchanged.
- `src/frontend/package-lock.json`: transferred the previously approved generated
  artifact byte-for-byte, without dependency re-resolution. It contains the same
  **272 version-record changes + three metadata-only records**. **Zero changed
  non-development records**, including metadata.

This was checked against the task-entry files, not inferred from the much larger
pre-existing diff against HEAD. Framework/compiler 22.2.1, cropper 9.1.7, Supabase
JS, Zod 4.6.6 override and all other direct selectors remain unchanged. No update,
audit-fix/force, new override, application refactor, backend or migration edit ran.

| Applied artifact | SHA256 |
| --- | --- |
| `package.json` | `6779fe54babeda3f3da1f8973544369c350658edf4f72bf8d2da5c8ef158cdd2` |
| `package-lock.json` | `cf4e8d5b6f6b3e08b8ca0ec0b4a13f7960c6000085152de0206f6c88c63e1575` |
| Exact approved patch | `7b1d013fbe8e33df988c5839849129f8150a6e3dae7fd3387463a478ce84c034` |

All **407 selected checkout paths** now match the original proposed manifest,
including both applied dependency hashes. Fresh `npm ci` from that exact source
installed 441 packages without changing the lock. `npm ls --all --json` exited 0,
with zero reported graph problems. Its normalized recursive dependency tree also
matches the previously validated installed candidate exactly (**1,030 dependency
occurrences**; graph SHA256
`49c3dbf69c5a542eab057c54cf065eb6d0151fa83894860070c05a1b274edf59`).
Verification used a new external extraction,
private HOME/cache, and no force/legacy-peer-deps. Existing checkout `node_modules`
and the owner's running port-4200 dev server were not replaced/restarted. A future
checkout install must use `npm ci` against the approved lock, not unconstrained
resolution; the current running dev process is not claimed to use refreshed tools.

## Fresh security audits and risk acceptance

Both audits ran against the **applied checkout lock**, with a fresh audit cache,
300-second process budget and bounded fetches:

```sh
npm audit --json --timing --fetch-retries=0 --fetch-timeout=15000
npm audit --json --timing --fetch-retries=0 --fetch-timeout=15000 --omit=dev
```

| Scope | Critical | High | Moderate | Total | Result |
| --- | ---: | ---: | ---: | ---: | --- |
| Full | **0** | **0** | **17** | **17** | Completed ~53.7 s, exit 1 for findings; no audit-service error |
| Production-only | **0** | **0** | **0** | **0** | Completed ~1.6 s, exit 0 |

The 17 affected package records still propagate these three direct advisories:

- [GHSA-82fw-gwwq-j7x9](https://github.com/advisories/GHSA-82fw-gwwq-j7x9): Vitest mock-server file read; 4.x fix floor 4.1.11.
- [GHSA-qxg6-89mg-rqv8](https://github.com/advisories/GHSA-qxg6-89mg-rqv8): public mock/interceptor registration injection; 4.x fix 4.1.12.
- [GHSA-g3hv-75wg-m249](https://github.com/advisories/GHSA-g3hv-75wg-m249): minimatch amplification with hostile glob inputs; fix 11.0.1.

These are dev/build/test/lint dependencies, not the static frontend runtime or
the API. Node/jsdom tests do not expose the public mock plugins; there is no direct
public interceptor-plugin setup in selected source. That narrows applicability,
but builds still execute tooling and the acceptance is **not vulnerability-free
certification or permission to expose dev servers**. Keep dev/test servers on
loopback, use reviewed source/build inputs, disable untrusted automatic previews,
and keep all server secrets out of frontend builds.

Acceptance is for **private QA preparation only**, not general/commercial release,
uploads enablement or untrusted CI execution. Re-run audits before future Git
publication/deployment and whenever the lock or relevant advisories change. Record
new counts/advisories and stop for owner reassessment if risk increases; do not
silently update dependencies. Compatible Vitest/minimatch remediation remains a
separate authorization and regression task. The advisory database is time-sensitive.

## Fresh frontend regression checks

| Check | Result |
| --- | --- |
| Clean install / installed graph | Passed; 441 packages, zero graph problems, lock unchanged |
| QA configuration suite | **15 passed**, zero skipped/failed |
| Complete unit suite | **465 passed / 38 files**, zero skipped/failed |
| Lint / TypeScript no-emit | Passed |
| Production build | Passed, initial **678.67 kB** |
| QA build, synthetic HTTPS Railway/QA public inputs only | Passed, initial **678.80 kB**; synthetic key length differs from earlier run |
| E2E configuration build | Passed; test artifact, not hosted QA output |

Existing Picker stylesheet warning (4.13 kB vs 4 kB), Vitest poolOptions deprecation
and future Vite native-config-loader warning remain; no assertion, budget or
security check was weakened. No generated QA config/output was added to the source.

Prior checks on the **same 407-path source hashes** remain valid historical
evidence, not newly executed here: 337 Core tests, 994 PostgreSQL/API tests,
22 desktop/mobile-emulation browser tests with zero retries, ten Railway checks,
no pending EF model changes, complete backend build, and no NuGet direct/transitive
known vulnerabilities. Their setup failures/reruns and real-provider/device/native
limitations remain documented in [the snapshot review](qa-security-snapshot-review.md).

## Durable artifacts and 407 vs 409

The original manifest/summary and exact patch are retained, **byte-for-byte**, with
associated checksums under
[`docs/deployment/snapshots/private-qa-2026-11-06/`](snapshots/private-qa-2026-11-06/README.md).
The non-secret reviewed scan and application results are there too. The exact
3,962,880-byte source archive and its checksum are separately retained in
`~/Documents/GameLibrary-QA-Artifacts/private-qa-2026-11-06/`, with owner-only
permissions. This is durable local storage, not a remote backup or Git publication.
That directory also retains a proposed **418-path publication manifest**, with
per-file modes/hashes, and the preservation-verification result, covered by its
`SHA256SUMS`. This proposed worktree manifest is not a created Git tree/commit;
future approved staging must still be verified against it.

| Frozen source identity | SHA256 |
| --- | --- |
| Manifest | `73899e7fd9732884421edefe0dbb0c65edfaa017655b3868d7286d084437e1fb` |
| USTAR archive | `b3f2f41a9de588b06f8c28e84398c359522e0ebdc6327f01b1ce1afcdfabb455` |

The original review captured **409** Git-eligible files. Only these were excluded:

1. `opencode.json`: local agent/provider configuration, unrelated to app deployment;
   its values are not copied into evidence/source artifacts.
2. `render.yaml`: retired Render-host blueprint; Railway is the selected API host.

Thus **409 − 2 = 407**; no original was deleted/moved. Candidate overlays replaced
two existing paths without adding/removing files. All required integrated source,
tests, EF migrations, native preparation and QA inputs are included. The Steam
logging proposal remains **unapproved documentation context**, not implemented
scope. Feature 012 manual acceptance and Feature 013 real-provider/device/release
gates remain unfinished. No unrelated implementation was introduced by this task.

At this task's entry there were **410** Git-eligible files: the original 409 plus
the prior snapshot-review report. New follow-up reports/evidence and the personal
Vercel plan are outside the frozen 407 and require explicit publication selection.
Do not confuse the snapshot file count with a current worktree count or change count.

## Credential/generated-secret verification

Scanned an isolated extraction containing **only the 407 archive files**, not
ignored `.env`, user secrets, credential stores, binaries, dependencies or build
outputs. **Gitleaks 8.30.1** was downloaded as a temporary standalone scanner, with
release asset SHA256 verified against the release API and checksum file. No project
dependency/global tool installation occurred. It used default rules, no project
allowlist/baseline, ignored inline allow comments, and full output redaction.

The scan exited **1 with three findings**, not a clean exit relabeled as success:

| Location | Reviewed classification |
| --- | --- |
| `scripts/build-qa.test.mjs:40` | Synthetic secret/JWT rejection fixtures, not usable provider credentials |
| `environment.e2e.ts:4` | Constant loopback fake-Auth placeholder |
| `environment.development.ts:4` | Existing **browser-public development Supabase publishable key**, retained unchanged; value withheld |

Additional content/path checks found only documented loopback PostgreSQL examples
and synthetic mocked/loopback Storage-test credentials. No private-key files,
service-role JWTs, provider secret tokens, credential-store files, generated secrets,
generated QA environment, project links or build/cache directories were detected
in the selected source. Per-file hashes/modes and archive-member hashes still match.

**No private/server credentials or generated secrets were detected. This is not
a literal zero-credential-shaped-strings claim:** the source contains the existing
public development key and synthetic test keys above. A publishable key is designed
for browser exposure; its presence does not prove the development project's Data
API/security posture. No network validity probe was made. QA config tests explicitly
reject reusing that development project; hosted QA must use separate QA inputs.
No scanner can guarantee universal absence of secrets. Owner publication review
must acknowledge these findings; any desired source-key removal is a separately
reviewed change, not a silent alteration of this approved dependency candidate.

Values and raw credential-like matches are not copied into durable metadata. See
[`secret-scan-review.json`](snapshots/private-qa-2026-11-06/secret-scan-review.json)
for detector/version/provenance, exact locations, triage and limitations.
The 11 proposed publication companions were also scanned separately with the
same rules, returning **exit 0 / zero findings**. The local personal-Vercel plan's
blocked/null-owner safeguards and frontend command consistency checks passed.

## Preservation

All 410 task-entry files/modes were baselined. The only intentional edits to those
files are the two approved package artifacts; the other **408 files** and the
existing HEAD, raw index and staged binary diff must remain identical. New files
are only the explicitly enumerated review/retention/personal-plan artifacts below.
The four existing staged paths, including the three with newer unstaged versions,
remain unchanged. No stage/reset/checkout/clean, commit, push, provider mutation,
billing action or remote migration ran. Final byte/mode/index, source/archive
identity, durable checksums, companion allowlist and whitespace/relative-link
checks passed. Preservation results are retained outside Git alongside the local
verification logs; no original file was overwritten to repair a test or scan.

## Publication plan — owner review only

**Scope-update note:** the 418-path proposal below and its durable manifest record
the earlier personal-only plan. They remain historical, not current publication
approval. Re-manifest reviewed companion versions and include the new
`deploy/qa/vercel-project-plan.json` and
`docs/deployment/vercel-workspace-qa-readiness.md` before future staging. The frozen
407-path application archive and dependency candidate are unchanged.
The subsequent existing-Supabase-project selection also requires the reviewed
`deploy/qa/supabase-project-plan.json` and
`docs/deployment/supabase-existing-project-qa-readiness.md` in that new manifest.

The security application is approved; the integrated source tree, Git actions and
provider writes are **not**. Proposed final publication is **407 frozen source
paths + these 11 explicit companions = 418 files**:

```text
deploy/qa/vercel-personal-project-plan.json
docs/deployment/qa-security-snapshot-review.md
docs/deployment/qa-security-application.md
docs/deployment/vercel-personal-qa-readiness.md
docs/deployment/snapshots/private-qa-2026-11-06/README.md
docs/deployment/snapshots/private-qa-2026-11-06/proposed-snapshot-manifest.json
docs/deployment/snapshots/private-qa-2026-11-06/proposed-snapshot-summary.json
docs/deployment/snapshots/private-qa-2026-11-06/dependency-candidate.patch
docs/deployment/snapshots/private-qa-2026-11-06/secret-scan-review.json
docs/deployment/snapshots/private-qa-2026-11-06/applied-verification.json
docs/deployment/snapshots/private-qa-2026-11-06/SHA256SUMS
```

1. Owner reviews the original 407-path manifest plus this exact companion allowlist,
   source-key scan findings, unfinished-feature uploads-disabled scope, residual
   moderate risk and **personal-only Vercel** restriction. Do not claim the 407-file
   archive already contains the newer personal-scope plan or reports.
2. After separate authorization, create a review clone from parent
   `1c840c59f1d6470bc2f0b0de59e768c6ee6f699b`. Populate it only from the verified
   durable archive and reviewed companion versions, then generate a final 418-path
   tree manifest/hashes outside that tree. Verify path/mode/blob equality and reject
   extras. Do not use moving branch tips, `git add .`, or the owner's original index.
3. Re-run final checks there, then only with explicit approval stage/commit one
   coordinated snapshot, proposed title `Prepare uploads-disabled QA snapshot with
   approved tooling security fixes`. Record its full immutable commit SHA and tree
   identity. No temporary `.tar`, ignored secrets or local provider config belongs
   in the commit. Do not fabricate a commit SHA before creation.
4. Separately obtain push, project creation, QA-only migration and deployment
   authorization. Automatic Git deployment remains off; attaching a repository or
   calling `create_git_project` can deploy and is not a harmless access check.
   Verify the actual Vercel project owner ID before any authorized project write.

If the owner chooses a different companion set, enumerate it and regenerate the
final publication manifest before staging. The 418 figure is a proposed path set,
not a generated Git tree or a remotely approved release.

## Remaining deployment blockers

- **Vercel:** the subsequent authenticated preflight confirmed `bar-book`, team
  `team_06vOR8VzjmIVJfhtgodwg1wL`, Hobby, confirmed OWNER and one existing project.
  Access/plan/name preflight is GO; the proposed name was absent at that check, not
  reserved. No project creation or deployment was attempted/approved. Keep Game
  Library in a new project; do not modify/redeploy BarBook or change team/default/
  billing settings. Recheck volatile name/quota facts before an approved creation.
- **Supabase:** use the owner's existing `game-library` project in Connective; no
  second project is needed or authorized. Its supplied QA reference is
  `yqngxhryzklrhhryzzoi`, distinct from protected `iramzxpjbnldhebykzhx`, with
  matching public discovery/JWKS HTTPS responses. The separate `supabase_qa`
  connection's authenticated URL and read-only SQL target are verified. Public
  application tables/EF history are absent and advisors returned no findings.
  Organization/region/plan, external-driver PostgreSQL/TLS, Data API disablement
  and full QA Auth readiness remain pending; public signup is currently enabled.
  Never use the original development server for QA or silently change QA settings.
- **Railway:** current included-credit treatment remains unreconciled; approve the
  separate project/region/limits and estimated additional $1.5–$4/month resource
  usage, not guaranteed incremental invoice cost. No billing setting was changed.
- **Publication/release:** owner approval of the final source/companion tree and
  Git actions, separate QA provisioning/secrets/migration/deployment approvals,
  and hosted Auth/CORS/security smoke remain outstanding. Upload enablement,
  physical-device/real-Storage and full Feature 013 release are not approved.

Stop before Git publication or any external change.
