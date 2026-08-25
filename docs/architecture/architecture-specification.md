Architecture Specification v0.3 --- Game Library

Status: Approved
Version: 0.3

Manual review amendment, 2026-08-19:

Architecture remains unchanged at the component/project level. The amendment
clarifies that VideoGame player counts reuse existing `games.minimum_players` /
`games.maximum_players`, Completed-progress normalization is backend/domain
behavior, and Random Picker visible history is frontend-only volatile state.

Feature 010 amendment, 2026-08-24:

Cover image search, when implemented, is a backend-mediated external HTTP
integration. Angular calls the Game Library API; the API calls the configured
image-search provider. Provider credentials are server-side secrets and are never
sent to Angular. The persisted model remains the existing optional external
CoverImageUrl, so no application schema change is required.

1. Purpose

This document defines the approved technical architecture for the Game
Library MVP.

It translates the approved Product and Domain Specifications into the
smallest maintainable implementation structure that supports the current
requirements.

2. Authoritative Inputs

This architecture must remain consistent with:

docs/product/product-specification.md

docs/domain/domain-specification.md

Product and Domain rules take precedence over architecture. Conflicts
must be reported, not silently resolved.

3. Architecture Principles

Prefer the simplest implementation that satisfies approved
requirements.

Use one Angular frontend, one ASP.NET Core backend, and one managed
PostgreSQL database.

Use managed authentication instead of building identity
infrastructure.

Keep domain rules authoritative on the backend.

Do not add infrastructure for hypothetical future scale or
integrations.

Prefer framework capabilities over unnecessary third-party
abstractions.

4. Approved High-Level Architecture

                    +----------------------+
                    |    Supabase Auth     |
                    | Registration / Login |
                    | Sessions / JWT       |
                    +----------+-----------+
                               |
                               | Access JWT
                               v
+------------------------------------------------+
| Angular PWA                                    |
|                                                |
| UI / UX                                        |
| Supabase Auth client                           |
| Services / Signals / Local state               |
| Volatile Random Picker session                 |
+----------------------+-------------------------+
                       |
                       | HTTPS / JSON
                       | Authorization: Bearer JWT
                       v
+------------------------------------------------+
| ASP.NET Core Web API                           |
|                                                |
| Supabase JWT validation                        |
| User isolation                                 |
| Domain/application rules                       |
| Search / filtering / sorting                   |
| Random Picker                                  |
| EF Core                                        |
+----------------------+-------------------------+
                       |
                       | Npgsql / TLS
                       v
+------------------------------------------------+
| Supabase PostgreSQL                            |
|                                                |
| Application schema                             |
| Supabase-managed auth schema                   |
+------------------------------------------------+

Supabase is used as:

Managed PostgreSQL hosting.

Authentication provider.

Supabase is not the application backend.

Angular does not directly access application database tables in the MVP.

5. Technology Choices

Frontend

Angular.

TypeScript.

Angular Router.

Angular forms.

Angular PWA/service worker.

supabase-js for authentication flows.

Use Angular services, signals and local/component state where
appropriate.

Do not introduce NgRx unless a future approved requirement demonstrates
a concrete need.

Backend

ASP.NET Core Web API.

Current supported/LTS .NET version selected at implementation start.

EF Core.

Npgsql PostgreSQL provider.

JWT bearer authentication validating Supabase-issued access tokens.

ASP.NET Core Identity is not used.

Database and Auth

Supabase PostgreSQL.

Supabase Auth.

6. Repository Structure

game-library/
├── AGENTS.md
├── README.md
├── docs/
│   ├── product/
│   │   └── product-specification.md
│   ├── domain/
│   │   └── domain-specification.md
│   └── architecture/
│       └── architecture-specification.md
├── specs/
├── src/
│   ├── frontend/
│   └── backend/
└── tests/

Feature specifications live under specs/.

7. Backend Project Structure

Use two .NET projects:

src/backend/
├── GameLibrary.Api/
└── GameLibrary.Core/

Do not create additional Domain, Application, Infrastructure, Contracts
or SharedKernel projects for the MVP.

GameLibrary.Api

Responsibilities:

HTTP endpoints.

Supabase JWT validation.

Authentication/authorization boundary.

CORS.

Request/response contracts.

Dependency injection.

Configuration.

Mapping HTTP concerns to Core operations.

HTTP-specific types should remain out of Core.

GameLibrary.Core

Responsibilities:

Domain entities and concepts.

Domain invariants.

Application services/use cases.

EF Core DbContext.

EF Core mappings.

EF Core migrations.

PostgreSQL persistence behavior.

The Core project may use EF Core directly.

Do not introduce generic repository or Unit of Work abstractions over EF
Core.

8. Logical Backend Areas

Organize code around capabilities where useful:

Library.

Games.

Platforms.

RandomPicker.

Authentication belongs primarily at the API boundary because user
identity is supplied by Supabase.

VideoGame and BoardGame are logical areas within Games, not separate
projects or services.

9. Authentication

Supabase Auth is the approved identity provider.

It handles:

Registration.

Login.

Logout.

Sessions.

Password reset.

Email verification where configured.

Angular uses supabase-js for authentication.

After authentication, Angular sends the Supabase access token to the
API:

Authorization: Bearer <access-token>

The API validates the token and derives the authenticated user identity
from the token's sub claim.

The API must never trust a user ID supplied by the client for
authorization.

Authentication secrets or privileged Supabase credentials must never be
exposed to Angular.

Prefer standards-based JWT/JWKS validation supported by the current
Supabase configuration rather than hard-coding long-lived signing
secrets when possible.

10. Authorization and User Isolation

The ASP.NET Core API is the only application component permitted to
access application database tables.

Every Library, Game, LibraryEntry and user-owned Platform operation must
be scoped to the authenticated Supabase user ID.

User isolation is authoritative on the backend.

Client-side filtering is not authorization.

One user must never be able to read or modify another user's private
library by manipulating identifiers.

11. Row Level Security

RLS is not part of the MVP application security model.

Reason:

Angular does not directly access application tables.

The API is the sole application database accessor.

User isolation is enforced in ASP.NET Core using the authenticated
Supabase user ID.

Adding RLS would introduce a second authorization mechanism that must
remain synchronized with backend rules.

If direct browser-to-Supabase application-data access is introduced in
the future, this decision must be revisited and RLS would become a
required security consideration.

12. Database Access

The API accesses Supabase PostgreSQL using EF Core + Npgsql.

Database credentials are server-side secrets.

Angular must not receive database credentials or privileged service-role
credentials.

Use normal relational constraints where they naturally protect data
integrity, while keeping transitional/cross-record business behavior in
the application/domain layer when that is clearer.

13. Schema and Migration Authority

EF Core migrations are the single source of truth for the
application database schema.

Rules:

Application tables are created/changed through EF Core migrations.

Do not manually edit application tables in the Supabase dashboard.

Do not use Supabase CLI database migrations for the application
schema alongside EF migrations.

Supabase-managed authentication schemas such as auth.* remain
managed by Supabase and are not modified by application EF
migrations.

Migrations are committed to source control.

Schema migrations should use an appropriate direct PostgreSQL connection
rather than relying on a transaction-pooling connection that is
unsuitable for migration tooling.

14. Transactions

The application uses one PostgreSQL database.

Operations that must preserve multiple related invariants execute
atomically using EF Core/PostgreSQL transaction behavior.

No distributed transactions are required.

15. Domain Validation

Backend validation is authoritative.

Examples include:

Owned VideoGame requires at least one Platform.

Wishlist/Interested cannot contain GameStatus or ProgressPercentage.

AcquisitionStatus transition rules.

BoardGame player-count rules.

VideoGame optional player-count rules.

Owned VideoGame Completed => ProgressPercentage = 100 normalization.

Platform deletion protection.

Rating and progress ranges.

Game/LibraryEntry deletion behavior.

Random Picker eligibility and filter semantics.

Angular should mirror useful validation for immediate UX feedback but
cannot be the only enforcement layer.

16. API Principles

Use a JSON HTTP API.

Exact endpoints belong to feature specifications.

General rules:

Resource-oriented HTTP design.

Appropriate HTTP verbs and status codes.

Consistent validation/error response format.

API contracts are separate from EF entities.

Do not add GraphQL.

Do not add pagination until a feature specification demonstrates
that the MVP needs it.

17. Error Handling

Use centralized API error handling.

Expected validation/domain failures return predictable client-readable
responses.

Unexpected exceptions must not expose stack traces or sensitive details
to clients.

18. Frontend Structure

Proposed Angular organization:

src/frontend/src/app/
├── core/
├── shared/
├── features/
│   ├── auth/
│   ├── library/
│   ├── games/
│   ├── platforms/
│   └── random-picker/
└── app.routes.ts

Keep core and shared small.

Do not move code into shared abstractions until it is genuinely shared.

Keep feature-specific code close to its feature.

19. Frontend State

Use:

Component/local state.

Angular services.

Angular signals where useful.

No global state-management library is required for the MVP.

Random Picker session state is frontend-only and volatile.

Refreshing/leaving the picker clears that session according to the
Domain Specification.

Visible Random Picker result history is part of that frontend-only volatile
session state. It is not stored in PostgreSQL, localStorage, or sessionStorage.

20. PWA

Use Angular-supported PWA/service-worker tooling.

MVP goals:

Responsive UI.

Installable PWA where supported.

Appropriate static/application-shell caching.

The MVP does not require:

Offline CRUD.

Offline database.

Offline synchronization.

Conflict resolution.

Authenticated data operations may require network connectivity.

21. Cover Images

Cover images are external URLs.

The application stores the optional URL and displays a placeholder when
absent or unusable.

An approved feature may provide optional cover-image search assistance through
the ASP.NET Core API. This integration follows the normal boundary:

Angular PWA -> Game Library API -> external image-search provider.

Angular must not call the external provider directly and must not receive provider
API keys, subscription tokens or other provider credentials.

The backend uses normal `HttpClient` / `IHttpClientFactory` conventions,
server-side configuration, short timeouts and predictable ProblemDetails error
responses for provider unavailability. Missing provider configuration must not
prevent the rest of the application from starting or using manual CoverImageUrl
entry.

No upload pipeline, object storage, image processing service or CDN is
required.

22. Search, Filtering and Sorting

The backend performs authoritative library search/filter/sort according
to the Domain Specification.

Prefer EF Core/PostgreSQL queries.

Do not load the complete library into Angular merely to implement domain
querying.

Do not introduce Elasticsearch/OpenSearch.

23. Genre Catalog

The approved Genre catalog is deterministic application-owned reference
data.

All environments must expose the same approved catalog.

Users cannot mutate it.

The exact simple representation is selected during persistence/feature
design.

Do not create genre-management endpoints.

24. Random Picker Architecture

Random Picker domain behavior is authoritative on the backend.

The backend handles:

Candidate eligibility.

Mode-specific filters.

Filter semantics.

Exclusion of previously shown IDs.

Random selection.

The frontend handles:

Current picker mode.

Current filters.

Current displayed result.

Temporary list of already-shown LibraryEntry IDs.

Visible temporary result history, newest first.

For a random request, Angular sends the current filters and
already-shown IDs to the API.

Angular does not send the visible result history collection to the API. The
backend continues to require only `shownLibraryEntryIds` for exclusion.

No RandomPickerSession is stored in PostgreSQL.

Conceptual result states

The Random Picker API must distinguish at least:

SUCCESS
NO_CANDIDATES
ALL_ALREADY_SHOWN

NO_CANDIDATES means the current filters match no eligible games.

ALL_ALREADY_SHOWN means eligible games match the current filters, but
every matching candidate has already appeared in the current temporary
session.

The exact DTO and endpoint contract belong to the Random Picker feature
specification.

25. Testing Strategy

Testing should protect important behavior without maximizing test
volume.

Backend unit tests

Focus on:

Domain invariants.

AcquisitionStatus transitions.

Platform rules.

BoardGame rules.

Filter semantics.

Random Picker eligibility.

Backend integration tests

Run against real PostgreSQL semantics.

Use:

Local Supabase/PostgreSQL, or

Docker PostgreSQL.

Do not rely on EF Core InMemory as the authoritative integration-test
database.

Important integration coverage includes:

Persistence.

Transactions.

User isolation with at least two users.

Platform deletion protection.

Game/LibraryEntry lifecycle.

Search/filter behavior.

Random Picker result states.

VideoGame player-count persistence/constraints and Completed-progress
normalization.

Volatile Random Picker visible-history synchronization with shown IDs.

Frontend tests

Test meaningful feature behavior and critical UX.

Do not require exhaustive tests for trivial presentation-only
components.

End-to-end tests

Maintain a small high-value set, such as:

Register/login.

Add Owned VideoGame.

Add BoardGame.

Browse/filter library.

Use Random Picker and Another.

Exact test frameworks are selected during project foundation.

26. Local Development

Preferred local stack:

Angular dev server.

ASP.NET Core API.

Local Supabase stack where practical, providing PostgreSQL + Auth.

Supabase CLI may be used to start the local Supabase services through
Docker.

The API connects to local PostgreSQL and validates local Supabase-issued
JWTs.

The project foundation specification must document the exact startup
commands and required environment variables.

27. CORS

During development and separate-origin deployments, the API must
explicitly allow the configured Angular origin.

Do not use unrestricted production CORS policies.

JWT bearer authentication avoids cookie SameSite coupling between
frontend and API origins.

28. Configuration and Secrets

Secrets are never committed to source control.

Frontend configuration may contain only values intended to be public,
such as the Supabase project URL and publishable/anon key as appropriate
for Supabase client authentication.

Privileged database credentials, service-role credentials and
server-only secrets remain backend/deployment secrets. External provider
credentials, including cover-image search API keys or subscription tokens, are
also server-only secrets.

29. Deployment

The MVP assumes managed Supabase hosting is acceptable.

Runtime components:

Static Angular PWA hosting.

One ASP.NET Core API.

Supabase managed Auth + PostgreSQL.

The frontend and API may be hosted on separate origins.

HTTPS is required in production.

Exact hosting providers for Angular/API are deferred to deployment
decisions.

No Kubernetes or distributed platform is required.

If Supabase managed hosting is later rejected, PostgreSQL/Auth hosting
can be reconsidered without changing the approved domain model.

30. Explicitly Rejected for MVP

Do not introduce without an approved architecture change:

Microservices.

CQRS frameworks.

MediatR solely for layering.

Event sourcing.

Message brokers.

Redis.

Distributed cache.

GraphQL.

Elasticsearch/OpenSearch.

Kubernetes.

Generic repositories over EF Core.

Custom Unit of Work abstraction over EF Core.

Additional backend layering projects.

Separate databases per module.

Direct Angular access to application database tables.

RLS as a parallel MVP authorization system.

Full offline synchronization.

Image-storage infrastructure.

Direct Angular access to external image-search providers or provider credentials.

Generic multi-provider search plugin/fallback frameworks.

Persistent Random Picker sessions.

AI/recommendation infrastructure.

ASP.NET Core Identity.

31. SDD and Agent Workflow

Implementation is driven by feature specifications under specs/.

Agents must:

Read approved Product, Domain and Architecture specifications.

Read the active feature specification.

Implement only approved feature scope.

Preserve existing architecture and domain invariants.

Add/update verification required by acceptance criteria.

Report conflicts or ambiguity instead of inventing behavior.

Architecture changes require an explicit architecture-spec update.

32. Initial Feature Delivery Order

Recommended order:

Project foundation and local development.

Authentication.

Platform management.

VideoGame management.

BoardGame management.

Library browse/search/filter/sort.

Random Picker.

PWA polish and MVP end-to-end verification.

Each step receives its own feature specification before implementation.

33. Architecture Approval

This architecture is approved for MVP feature specification and
implementation.

Approved core decisions:

Angular PWA.

ASP.NET Core API remains the application/domain boundary.

Supabase Auth.

Supabase PostgreSQL.

JWT bearer authentication.

Two .NET backend projects.

EF Core + Npgsql.

EF Core migrations as the sole application-schema authority.

Backend-enforced user isolation.

No RLS in the MVP security model.

Volatile Random Picker session in Angular.

Authoritative Random Picker selection in ASP.NET Core.

Real PostgreSQL integration testing.
