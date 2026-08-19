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

The backend is authoritative for:

Candidate eligibility.

Filters.

Exclusion of shown IDs.

Random selection.

The frontend owns only volatile session state:

Current mode.

Current filters.

Current result.

Visible volatile result history.

Already-shown IDs.

Do not persist RandomPickerSession.

Changing filters does not clear shown IDs.

Changing filters or mode does not clear visible result history.

A previously shown game remains shown for that session.

Reset shown history clears both shown IDs and visible result history.

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

Cover images are optional external URLs.

Do not implement:

Image uploads.

Object storage.

Image resizing pipelines.

CDN infrastructure.

Use a placeholder when no usable cover URL exists.

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
