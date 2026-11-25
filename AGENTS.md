AGENTS.md --- Game Library

Purpose

This file contains durable instructions for coding agents working on
Game Library.

The project uses Specification-Driven Development (SDD).

Agents must implement approved specifications, not reinterpret the
product.

Source of Truth

Read these before implementing a feature:

docs/product/product-specification.md

docs/domain/domain-specification.md

docs/architecture/architecture-specification.md

The active feature specification under specs/

Authority order:

Product Specification
        ↓
Domain Specification
        ↓
Architecture Specification
        ↓
Feature Specification
        ↓
Implementation

A lower-level document must not contradict a higher-level approved
specification.

If specifications conflict or a required behavior is ambiguous:

STOP and report the conflict. Do not invent a decision.

General Rules

Prefer the simplest implementation that satisfies the specification.

Implement only the active feature scope.

Do not add speculative functionality.

Do not modify unrelated features.

Do not introduce architecture changes silently.

Do not add dependencies without a concrete current need.

Preserve all approved Domain invariants.

Backend validation is authoritative.

Keep code understandable for a single developer and future coding
agents.

Approved Stack

Frontend:

Angular.

TypeScript.

Angular PWA.

supabase-js for authentication.

Angular services/signals/local state.

Backend:

ASP.NET Core Web API.

Two projects only:

GameLibrary.Api

GameLibrary.Core

EF Core.

Npgsql.

Supabase JWT bearer validation.

Data/Auth:

Supabase PostgreSQL.

Supabase Auth.

Feature 013 permits a private Supabase Storage bucket for managed game covers only.
Its approved specification defines the narrow upload/proxy/cleanup boundary and
conditional image-processing dependencies; this is not approval of general media
storage or direct browser Storage access.

Backend Boundaries

GameLibrary.Api

May contain:

HTTP endpoints.

API request/response contracts.

JWT authentication configuration.

Authorization boundary.

CORS.

Dependency injection/composition.

API error handling.

Do not put core domain rules only in controllers/endpoints.

GameLibrary.Core

Contains:

Domain concepts.

Domain rules/invariants.

Application/use-case logic.

EF Core DbContext.

EF mappings.

EF migrations.

Persistence behavior.

Do not create extra
Domain/Application/Infrastructure/Contracts/SharedKernel projects unless
the Architecture Specification is explicitly changed first.

Authentication and Security

Supabase Auth is the identity provider.

Angular authenticates using supabase-js.

Angular sends the Supabase access token to ASP.NET Core as a Bearer JWT.

The API derives the authenticated user from the validated token.

Never trust a client-supplied user/owner ID for authorization.

Every user-owned query or mutation must be scoped to the authenticated
user's Library.

One user must never access another user's:

Library.

Games.

LibraryEntries.

Platforms.

Notes or other private library data.

Do not expose:

Database credentials.

Supabase service-role credentials.

Server-only secrets.

Do not implement direct Angular access to application database tables.

RLS is not part of the MVP application security model.

Storage default-deny/access configuration is separate. For Feature 013, only the
backend may read/write/delete private cover objects. Managed image bytes are served
by an authenticated API proxy with a fresh Library/state check on every request,
including conditional/HEAD handling; do not expose signed links or JWT query tokens.
Use private,no-store for content and errors, and no persistent/shared image cache.

Database Rules

EF Core migrations are the single source of truth for the application
schema.

Do:

Create schema changes through EF migrations.

Commit migrations.

Use PostgreSQL relational constraints where appropriate.

Do not:

Manually create/edit application tables in the Supabase dashboard.

Add a second Supabase migration workflow for the application schema.

Modify Supabase-managed auth.* schema.

Manage Supabase storage.* tables through EF or direct SQL. Use supported Storage
APIs for bucket/object operations; application cover metadata remains EF-owned.

Use EF Core InMemory as a substitute for important PostgreSQL
integration tests.

Domain Invariants

Always preserve the approved Domain Specification.

Especially:

Owned VideoGame requires at least one Platform.

Wishlist/Interested may have zero Platforms.

GameStatus and ProgressPercentage are allowed only for Owned
VideoGames.

Owned -> Wishlist/Interested clears GameStatus and
ProgressPercentage.

Owned -> Wishlist/Interested preserves Platforms, Rating and Notes.

BoardGames have no Platforms.

Platform in use cannot be deleted.

Rating is 1--5 when provided.

Progress is 0--100 when provided.

Owned VideoGame with GameStatus Completed normalizes ProgressPercentage to 100.

VideoGame player counts are optional, but when present both MinimumPlayers and
MaximumPlayers are required and must be valid.

BoardGame player ranges must be valid.

Only Owned entries participate in Random Picker.

Game/LibraryEntry lifecycle follows the approved 1:1 MVP rule.

User data isolation is mandatory.

Do not weaken an invariant for UI convenience.

Search and Filter Semantics

Follow Domain Specification exactly.

Search is case-insensitive substring matching on Game name.

Multiple values within the same filter use OR.

Different filter types use AND.

Missing metadata does not satisfy an active filter requiring it.

Rating filters use minimum-rating semantics.

Player-count filtering uses:
MinimumPlayers <= requestedPlayers <= MaximumPlayers.

Missing player-count metadata does not satisfy an active player-count filter.

Random Picker

GL-16 exception: persist only the current round for recovery, independently for
All, VideoGames and BoardGames, scoped to the authenticated Library. Recover its
proposal, newest-first history, shown IDs, outcome and current filter context.
All remains the default. Switching modes recovers that mode's own round; Start
over clears only the active mode's proposal/history/shown IDs/outcome and keeps
its filters. Filter changes preserve that round's history/shown IDs and clear its
displayed proposal/outcome as before. The backend remains authoritative for draws,
ownership and fresh eligibility. Stale/deleted proposals cannot be selected on
recovery. This narrow exception does not authorize completed-round archives,
statistics, play sessions, completion workflows or general history persistence.
See specs/015-persistent-picker-round/spec.md.

The backend is authoritative for:

Candidate eligibility.

Filters.

Exclusion of shown IDs.

Random selection.

Outside the GL-16 current-round recovery exception, the frontend owns only volatile session state:

Current mode.

Current filters.

Current result.

Visible volatile result history.

Already-shown IDs.

Do not persist RandomPickerSession or Picker history beyond the GL-16 current-round exception.

Changing filters does not clear shown IDs.

Changing filters does not clear visible result history. GL-16 mode changes switch
between independent saved rounds without clearing either mode's history.

A previously shown game remains shown for that session.

Reset shown history clears both shown IDs and visible result history of the active
mode's round only under GL-16; other modes remain untouched.

The API must preserve the conceptual distinction between:

SUCCESS

NO_CANDIDATES

ALL_ALREADY_SHOWN

Do not silently relax filters or silently reset shown results.

Frontend Rules

Prefer:

Feature-local components.

Angular services.

Signals.

Local/component state.

Do not add NgRx for the MVP without an approved architecture change.

Keep core/ and shared/ small.

Do not create generic abstractions before multiple real usages justify
them.

Quick-add flows must remain quick and respect Product requirements.

PWA Rules

The application is a PWA, but the MVP does not require offline CRUD or
synchronization.

Do not add:

Client-side database synchronization.

Conflict resolution.

Offline mutation queues.

Static/application-shell caching is sufficient unless a future
specification says otherwise.

Cover Images

Cover images are optional. Game.CoverImageUrl stores external manual/search/Steam
URLs only. Approved Feature 013 adds nullable Game.CoverUploadId and the narrow
cover_uploads lifecycle table for one private uploaded cover per Game. At most one
reference may be set. Never store image bytes, Base64, Blob/proxy or signed URLs in
CoverImageUrl or expose server Storage credentials to the browser.

Feature 013 permits device selection/capture, local crop/adjustment, backend-only
5 MiB uploads, bounded validation/JPEG re-encoding, authenticated API byte delivery,
and bounded cleanup inside the existing API. Browser HttpClient obtains authorized
Blobs; revoke local object URLs on source/account changes and destruction. External
images retain their existing behavior and must not receive application Bearer tokens.

Use explicit coverUpdate intent. Omission means keep, never clear. Explicit changes
require the approved optimistic precondition. User-selected covers beat Steam;
atomic Steam fill is allowed only when both cover references are null. Retire objects
with the SQL mutation, delete only after commit, and never delete a referenced
object or infer ownership from a URL. Preserve immutable namespace/claimed_game_id
and reconcile unknown COMMIT outcomes before retries; exceptions do not prove rollback.

SkiaSharp/native Linux assets, libjpeg-turbo 3.2.1 exclusively for JPEG
prevalidation, and ngx-image-cropper are permitted only within Feature 013, subject
to its native-runtime and Angular integration gates. JPEG uses bounded structural
checks, full TurboJPEG STOPONWARNING validation, then existing Skia normalization.
The approved bounded-normalization policy does not promise exhaustive JPEG
conformance. Unreported decoder-tolerated coefficient anomalies may be normalized
only after all existing structural, diagnostic and resource checks pass. This does
not relax rejection of truncation, malformed headers/tables, animation or limit
violations. Never suppress warnings, bypass validation, or store original upload
bytes; only fresh canonical output may reach Storage. No entropy engine/native
fork is approved. Do not substitute marker checks for the pipeline. Target
the specified Noble/glibc linux/amd64 .NET 10 image family; do not substitute a
different codec/platform or claim compatibility without executing the gates.

Do not add galleries, HEIC conversion, AI image processing, generic media frameworks,
on-demand transformation/CDN infrastructure, direct Storage mutations from Angular,
or offline upload queues. Use a placeholder when no usable cover is available.

Testing Rules

Tests should verify meaningful behavior.

Backend unit tests should cover important domain/application rules.

Backend integration tests for relational/security behavior must use real
PostgreSQL semantics, such as Docker PostgreSQL or the local Supabase
PostgreSQL instance.

Important integration scenarios include:

User isolation with two users.

CRUD persistence.

Platform deletion protection.

Acquisition transitions.

Game/LibraryEntry lifecycle.

Search/filter behavior.

Random Picker states.

Frontend tests should focus on meaningful feature behavior.

Keep end-to-end tests small and high value.

Every feature's acceptance criteria must have appropriate verification.

Dependency Rules

Before adding a dependency, verify:

The framework does not already solve the problem adequately.

The dependency solves a current approved requirement.

Its complexity is justified.

Do not add the following without an explicit Architecture Specification
change:

MediatR.

CQRS frameworks.

Generic repository frameworks/patterns over EF Core.

Custom Unit of Work abstraction.

NgRx.

Redis.

Message brokers.

GraphQL.

Elasticsearch/OpenSearch.

Event sourcing.

Microservices infrastructure.

Kubernetes.

Scope Discipline

When implementing a feature:

Read the feature specification fully.

Identify its acceptance criteria.

Inspect existing code before changing architecture.

Implement the smallest complete solution.

Add/update required tests.

Run relevant tests/build/lint.

Report what changed and any unresolved issue.

Do not opportunistically refactor unrelated code.

Small local refactors required to implement the feature safely are
acceptable.

When to Stop

Stop implementation and report the issue when:

Specifications contradict each other.

Required behavior is genuinely undefined.

A requested implementation would violate Domain invariants.

The feature appears to require an Architecture change.

A new dependency or infrastructure component appears necessary but
is not approved.

Required credentials/services are unavailable and prevent meaningful
verification.

Do not solve specification problems by silently choosing behavior.

Definition of Done for an Agent Task

A task is not complete merely because code was generated.

Before reporting completion:

Acceptance criteria are satisfied.

Domain invariants remain valid.

Relevant tests pass.

Build succeeds.

Lint/type checks pass where configured.

Database migrations are included when schema changes require them.

No unrelated scope was introduced.

No secrets were committed.

Any limitation or unverified behavior is explicitly reported.
