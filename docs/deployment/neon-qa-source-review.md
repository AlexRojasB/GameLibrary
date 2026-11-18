# Intended Neon QA deployment source — review record

**Discovery follow-up, 2026-11-15:** owner-created Neon target metadata and
configuration differences are recorded in the [discovery report](neon-qa-project-discovery.md).
The original 436-file inventory is retained byte-for-byte as historical evidence.
The [discovery-revision inventory](neon-qa-source-manifest-discovery.json) covers
updated plan/report/checker companions and the retained original inventory. All
407 frozen application-source hashes remain unchanged. Neither inventory grants
publication, SQL execution or deployment approval.

2026-11-15. **Complete path/hash/provenance review, not publication approval.**
This task changes local documentation/plans only. No application source, dependency,
migration, native binary, historical snapshot or existing staged version is changed.

## What is included

The task-entry tree contains **429 unique Git-eligible files**, not just HEAD:

- **407 frozen paths**: 130 backend, 173 frontend, 48 test paths, 29 specification/
  context, 10 deployment, 16 native/operational-gate and one deferred-documentation
  path. Every byte/hash still matches the retained original manifest.
- **20 existing companions**: local EF 10.0.11 manifest, Supabase/Vercel historical
  plans, security/tooling/connectivity reports and durable snapshot evidence. They
  remain intact; their previous assumptions are not all current deployment authority.
- **Two explicitly excluded local/obsolete inputs**: `opencode.json` (local provider
  configuration; do not publish/read credential values) and `render.yaml` (retired
  deployment blueprint). Neither is deleted or rewritten.

The proposed publication input adds the new approved architecture amendment,
deployment entry point/runbook, creation/environment plans, migration/cleanup/source
reviews and offline checker. The
[generated candidate manifest](neon-qa-source-manifest.json) enumerates each selected
path's bytes/mode/hash and HEAD/index/untracked provenance. It does not include
itself (no self-hash cycle); the final publication envelope must separately identify
that review artifact. This is a candidate input inventory, **not an approved release
tree or a claim that a future cleanup/guard implementation is included**.

HEAD is `1c840c59f1d6470bc2f0b0de59e768c6ee6f699b`, local branch `dev`, two commits
ahead of locally recorded origin/dev. No fetch was performed. Four paths have
staged changes; three also have additional unstaged versions. The candidate uses
the reviewed **working-tree** bytes, not silently the index/HEAD versions; the
original index and those staged versions remain unchanged. Do not deploy HEAD or
run `git add .` to publish the integrated work.

## Deployment-boundary review, including uncommitted source

| Area | Review result / release boundary |
| --- | --- |
| Specifications and 011/012 changes | Picker history/race handling, Steam selection/playtime reference, Family Friendly included. Deferred Steam Play Log lifecycle proposal remains unapproved documentation, not runtime scope. Manual product acceptance remains separate. |
| API/Core changes and new 013 files | Exactly two projects; backend Library/subject scoping, EF relational domain rules and cover lifecycle ownership remain in force. Switching the connection provider does not authorize new domain behavior. |
| Auth composition/interceptor | Real Supabase JWT/discovery validation, usable subject and API-origin-only Bearer attachment retained. ES256 supported; RS256 also allowed in current API. Production mode disables test-Auth activation. |
| E2E/controller/fakes | Reset/auth helpers and fixtures are test-only. Production guard is mandatory; never run reset or enable E2E against hosted QA. No source removal or assertion relaxation proposed. |
| All 11 migrations/Designers/model snapshot | Existing PostgreSQL schema appears Neon-compatible without edits. Conditional cover privacy-role guard skips absent Supabase roles. All migrations, including cover metadata, still required; no managed Auth/Storage schema workflow. |
| CoverUploadOptions/host/Core cleanup | No cleanup flag exists; hosted cleanup immediate/every 15 minutes is unconditional and can mutate. New opt-out is review-only; cover quotas, locks, receipts, normalization and privacy are unchanged. |
| Frontend new/changed cover/form/read paths | Explicit cover intent, local crop/recovery and authenticated Blob display remain integrated but uploaded covers are unavailable in initial QA. No offline queues or persistent private image cache are approved. |
| Docker/native packaging | Existing digest-pinned Noble/glibc linux/amd64 .NET 10, non-root, locked restore, no startup migration. Docker context excludes local outputs/environment/private-key files. Do not remove native checks because uploads are off. |
| Angular QA/Vercel | Allowlisted public config, development-project rejection, E2E=false, SPA/static/PWA outputs, no automatic Git deployment. No backend/database/Storage secret belongs in the build. |
| All existing companion reports/plans | Retained as evidence. New amendment/runbook supersede Supabase application-DB, personal-only Vercel and old authorization/tooling status assumptions for current QA. They do not erase historical failures/limitations. |
| Runtime/publication input selection | Server image only uses allowlisted backend/native paths; Vercel serves built static output, not source/docs/test artifacts. Local provider config, generated outputs, user secrets and historical tar archives are not inputs. |

This is a complete inventory/provenance review plus a focused source/configuration
review of deployment, identity, migration and background-work boundaries. It is not
a new line-by-line audit of every unchanged test/component, nor proof of deployed
behavior. Prior detailed reviews are applicable only because all 407 hashes match.

## Source/security identities retained

- Frozen manifest SHA256:
  `73899e7fd9732884421edefe0dbb0c65edfaa017655b3868d7286d084437e1fb`.
- Frozen USTAR SHA256:
  `b3f2f41a9de588b06f8c28e84398c359522e0ebdc6327f01b1ce1afcdfabb455`.
- Applied frontend manifest/lock are the already-approved security candidate;
  hashes remain `6779fe54babeda3f3da1f8973544369c350658edf4f72bf8d2da5c8ef158cdd2`
  and `cf4e8d5b6f6b3e08b8ca0ec0b4a13f7960c6000085152de0206f6c88c63e1575`.
- Local EF CLI manifest pins 10.0.11. No dependency/tool installation is performed.
- Prior secret scan found public development/synthetic test key strings, not private
  server credentials. Those bytes are preserved, not printed or tested against
  protected services. Do not claim a literal zero-key-strings source archive.

Preceding 2026-11-15 read-only assessment: full npm audit completed with 11 moderate,
zero high/critical affected package records; production-only zero. Same three
Vitest/minimatch advisory URLs as the earlier accepted 17-record result. This task
does not rerun the audit or silently upgrade anything. Future publication needs
fresh source/secret/audit review; previous acceptance is not general release approval.

Historical regression evidence on matching source: 337 Core, 994 PostgreSQL/API,
465 frontend and 22 browser tests; builds/lint/type and earlier native Linux gates.
These are not new runs. A future runtime/guard/cleanup delta needs its own exact
manifest, tests/build/native verification and owner approval, preserving this archive.

## Verification and preservation

The new offline checker validates the creation/variable approval boundaries, frozen
hashes and full candidate inventory. The existing ten Railway-plan and fifteen QA
build-config tests are also rerun; final counts/results are recorded by the task
completion report. No build, integration/browser suite, remote SQL or migration is
claimed. No cloud resource/secret/Auth setting was accessed or changed; only the
public account-level Neon regions listing and tool schema were consulted this task.

All **429 task-entry file contents/modes** must remain identical, along with HEAD,
raw index SHA256 `e369fb71d4dd9dddde1ab2e8d904c5e37d3303d9e4ebf189a8eca85bafbc57eb`
and staged binary-diff SHA256
`62092d1ed1bbe35197e650619a83a5a9eed14a4495a4223a795223ed11372439`.
Only the new explicitly enumerated Neon-plan companions are added. Git status will
therefore show new untracked paths; existing entries/staged/unstaged bytes are not
restaged or replaced. Frozen 407-file and retained metadata checksums stay unchanged.
