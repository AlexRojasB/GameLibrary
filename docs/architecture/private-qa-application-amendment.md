# Private QA application preparation amendment

2026-11-17. Owner-approved **local implementation/preparation only**. This companion
records subsequent decisions without rewriting frozen historical specifications,
deployment plans, source manifests or source archive.

Read with [the private QA provider amendment](private-qa-neon-amendment.md). The owner
subsequently accepted the existing GameLibraryQa PostgreSQL **18.7**, Ohio,
**0.25–2 CU / Free** configuration and the documented provider-internal transport
uncertainty for private QA. The exact 11 EF migrations were separately authorized,
applied once and verified on 2026-11-17. They must not be rerun for application
deployment. Supabase QA `yqngxhryzklrhhryzzoi` remains **Auth only**.

## Approved cleanup exception

The owner now authorizes implementing/testing the previously reviewed
`PrivateQa:DisableCoverCleanup` opt-out for a fresh, uploads-disabled QA database:

- Missing/false leaves existing startup/15-minute cleanup registration, Core
  lifecycle logic, bounds, default options and normal production behavior unchanged.
- Explicit true omits CoverCleanupService registration entirely: no startup pass,
  periodic timer, due-row scan or oldest-deadline query. A startup warning identifies
  this mode. No database access is added to decide whether to register the worker.
- Malformed switch values or true with uploads enabled or any nonempty Storage
  base/credential fail before hosting. This mode is not a Storage outage workaround.
- QA still uses ASPNETCORE_ENVIRONMENT=Production. The opt-out neither enables E2E
  JWTs/providers/endpoints nor changes JWT algorithms or user isolation.
- Separate operator evidence must establish zero cover_uploads rows and zero
  non-null games.cover_upload_id references immediately before first startup. No
  managed-cover data import, restore, Storage installation or hot reconfiguration
  is permitted while opted out. Startup validation cannot prove remote data absence.
- Never use this switch on production/development datasets with managed covers.
  Configuring/enabling managed covers requires a separate review and restart with
  normal cleanup restored and the Feature 013 release gates satisfied.

This narrow composition-only exception supersedes the historical “review only”
cleanup status for this local task, not the default Feature 013 lifecycle policy.
No schema, domain, dependency, native-codec, API contract or worker architecture
change is approved or introduced.

The versioned QA application Dockerfile retains the approved Noble/glibc amd64
.NET 10 images/native assets. It includes the same TLS-1.3-only OpenSSL policy
structure proven by the migration operator, so a future API connection cannot
silently negotiate TLS 1.2. Npgsql VerifyFull remains a separate required setting.
This QA-image-only transport configuration does not modify the host, frozen base
Dockerfile, normal production configuration or trusted-issuer algorithm allowlist.

## Approval boundary

New candidate source hashes are separate from the frozen 407-file and migration-
runner inventories. Existing staged/uncommitted changes remain part of the reviewed
candidate but are not published by this task. New snapshot approval, runtime-role/
effective-grant verification, origins/Auth settings/testers, credentials installation,
resource provisioning, source publication and deployment require separate approval.
No cloud mutations, migrations, database data changes or credential installation
are authorized by this amendment.
