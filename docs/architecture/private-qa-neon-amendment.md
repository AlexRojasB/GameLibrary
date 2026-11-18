# Private QA architecture amendment — Neon PostgreSQL

Date: 2026-11-15. **Provider architecture approved by the owner. Execution not approved.**

## Authority and scope

This is an explicit, owner-approved **private-QA-only amendment** to
[Architecture v0.4](architecture-specification.md), sections 4, 5, 12, 30 and 34.
For this QA environment it replaces Supabase application PostgreSQL with a new,
dedicated Neon PostgreSQL **17** project. Supabase QA is used exclusively for Auth.
The Product and Domain specifications and all application invariants are unchanged.

The original architecture, `AGENTS.md` and original deployment inputs are in the
407-file frozen source snapshot. They remain byte-for-byte intact as requested;
this additive amendment records the approved exception rather than rewriting their
historical hashes. Read this amendment with the base architecture when planning QA.
Outside this private QA exception, the original production/development choices and
Feature 013 rules remain in force. This is not general multi-provider support.

## Approved topology

```text
Angular PWA — new isolated Vercel project in existing bar-book workspace
  ├─ supabase-js → existing Supabase QA Auth
  └─ HTTPS / Bearer access JWT → isolated Railway .NET 10 API
                                 ├─ QA Auth discovery / JWKS
                                 └─ EF Core / Npgsql / verified TLS
                                       → dedicated Neon PostgreSQL 17
```

- Vercel: new `game-library-qa-web` in `bar-book`, explicit team
  `team_06vOR8VzjmIVJfhtgodwg1wL`. Never reuse/redeploy BarBook or its domains.
- Railway: new private `game-library-qa`, environment `qa`, API
  `game-library-qa-api`, existing Hobby account/workspace
  `6636d7dc-798b-4470-8376-b36089020713`. No unrelated service reuse.
- Neon: **new project**, PostgreSQL 17, no branch/data/role cloning from another
  application. Organization and proposed creation settings are pinned in the
  [creation plan](../../deploy/qa/neon-project-plan.json); IDs remain unknown.
- Supabase QA: existing `yqngxhryzklrhhryzzoi`, **Auth only**. Do not migrate
  application tables there or copy its managed Auth schemas/users into Neon.
- No Neon Auth, Data API, Storage, Functions, Realtime, browser SQL, new ORM/SDK,
  synchronization or distributed transactions are approved.

## Boundaries retained

Exactly `GameLibrary.Api` and `GameLibrary.Core`; EF Core migrations remain the sole
application-schema authority. Use all 11 migrations, including cover metadata even
with uploads disabled. No schema or dependency change is approved by this amendment.

Angular authenticates through the same QA Supabase project and sends the access JWT
only to the configured Game Library API origin. The API validates the exact QA
issuer, signature, audience `authenticated`, expiration and usable `sub`, then scopes
every user-owned operation through Library. Neon receives database credentials,
not user JWTs. `libraries.user_id` remains opaque text; no Auth-table FK is required.

The approved QA token signer is **ES256**. Current API code already validates ES256
and also permits RS256 for its trusted issuer. It rejects HS256; this task neither
changes that production allowlist nor claims an ES256-only override exists. Verify
the actual QA login token uses ES256. Any request to narrow the runtime allowlist to
ES256 only needs a separately reviewed code/configuration change, not a fictitious
environment variable.

Direct Neon read-write primary connection with Npgsql pooling is the initial QA
runtime proposal; migrations use a separate direct, non-pooled operator connection.
Require `SSL Mode=VerifyFull`, real hostname trust and bounded pools/timeouts. Never
use a read replica or transaction-pooler migration connection. Database credentials
are server-only. Keep Neon Data API disabled; no parallel RLS authorization model
is introduced.

## Upload/cleanup boundary

This QA phase is **uploads-disabled**: no bucket, Storage credential, managed-object
import or claim of Feature 013 release readiness. Supabase QA is not used for
Storage. Existing URL covers/manual CRUD remain in scope. Keep native image/runtime
packaging and the Noble/glibc linux/amd64 .NET 10 target unchanged.

The owner authorized **review**, not implementation, of disabling periodic cover
cleanup in this fresh no-Storage QA environment. The
[cleanup proposal](../deployment/neon-qa-cleanup-review.md) is explicitly pending.
The existing startup/15-minute cleanup behavior and all production lifecycle rules
remain unchanged. Do not set an unsupported switch or run without the full schema.

## Approval boundary

Approved now: topology decision, local plans/documentation/source review and
migration/cleanup strategy preparation. Not approved: resource creation/configuration,
billing, credentials installation, Auth-setting changes, migration/DDL execution,
cleanup code changes, Git publication or deployment. See the
[current QA runbook](../deployment/neon-qa-plan.md) for the next narrow gate.
