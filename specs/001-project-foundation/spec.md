# 001 — Project Foundation

## Status

`Draft`

## Objective

Create the minimum technical foundation required for future Game Library
features.

After this feature is implemented, a developer or coding agent can:

1. Clone the repository.
2. Install required dependencies.
3. Start the local infrastructure.
4. Start the ASP.NET Core API.
5. Start the Angular application.
6. Run backend tests.
7. Run frontend tests/checks.
8. Verify that Angular can communicate with the API.
9. Understand the basic repository structure and local development commands.

This feature implements no business functionality. No domain entities, domain
tables, business endpoints, or authentication behavior are introduced.

## Context

The approved Product, Domain, and Architecture Specifications are
authoritative. This specification is the first feature specification in the
approved delivery order:

1. Project foundation and local development. (this feature)
2. Authentication.
3. Platform management.
4. VideoGame management.
5. BoardGame management.
6. Library browse/search/filter/sort.
7. Random Picker.
8. PWA polish and MVP end-to-end verification.

The approved high-level architecture is:

```text
Angular PWA
     |
     | HTTPS / JSON + Supabase JWT later
     v
ASP.NET Core API
     |
     | EF Core / Npgsql
     v
Supabase PostgreSQL
```

Supabase Auth, the Supabase access JWT, `supabase-js`, JWT bearer validation,
and all user-identity behavior belong to the authentication feature and are not
implemented here.

The approved backend architecture consists of exactly two application
projects:

```text
GameLibrary.Api
GameLibrary.Core
```

## In Scope

- Repository structure and root-level developer documentation (README,
  `.gitignore`, `.editorconfig`).
- Angular application creation:
  - TypeScript.
  - Angular Router.
  - Angular PWA/service worker foundation.
  - Initial feature-oriented folder skeleton.
  - Development environment configuration including the API base URL.
  - Basic frontend test/build/lint verification using the generated Angular
    tooling.
- ASP.NET Core backend creation:
  - `GameLibrary.Api` (Web API boundary).
  - `GameLibrary.Core` (domain/database infrastructure).
  - Project references and a single solution.
  - Basic dependency injection and configuration structure.
  - Development configuration.
  - API startup.
  - Centralized error-handling foundation.
  - Anonymous `GET /health` endpoint.
- PostgreSQL / Supabase preparation:
  - EF Core.
  - Npgsql.
  - Database connection configuration.
  - EF migration infrastructure (baseline only).
  - Local Supabase/PostgreSQL development approach.
- Minimal testing foundation:
  - Backend integration tests against real PostgreSQL.
  - Frontend tests using the generated Angular default testing setup.
- Local development documentation, including required versions, install steps,
  startup commands, test/build/check commands, and configuration guidance.

## Out of Scope

Feature 001 must NOT implement or introduce:

- Registration, login, logout.
- Supabase Auth integration, `supabase-js`, JWT validation, user identity.
- User/Library domain model, `User`, `Library`, `LibraryEntry`, `Game`,
  `VideoGame`, `BoardGame`, `Platform`, `Genre`.
- Any business database tables or domain entities.
- RLS policies.
- Direct Angular access to application database tables.
- Library CRUD, search, filters, sorting, Random Picker, cover images.
- Platform/Game management endpoints.
- CQRS, MediatR, generic repositories, Unit of Work abstraction,
  microservices.
- NgRx or any global state-management library.
- Full offline CRUD, offline synchronization, client-side database, conflict
  resolution.
- Production deployment configuration.
- CI/CD (not required for the foundation).
- Dockerization of the entire application (only the local Supabase stack runs
  through Docker via Supabase CLI).
- A backend unit-test project (no domain/application rules exist yet to test;
  see Testing).
- End-to-end (E2E) tests (deferred to later features).

## Technical Requirements

### Repository

Follow the approved repository structure from the Architecture Specification,
adapted to the actual repository state:

```text
game-library/
├── AGENTS.md            (located at docs/AGENTS.md; see Risks / Notes)
├── README.md
├── .gitignore
├── .editorconfig        (recommended, small)
├── docs/
│   ├── AGENTS.md
│   ├── product/
│   │   └── product-specification.md
│   ├── domain/
│   │   └── domain-specification.md
│   └── architecture/
│       └── architecture-specification.md
├── specs/
│   └── 001-project-foundation/
│       └── spec.md
├── src/
│   ├── frontend/
│   └── backend/
└── tests/
    └── backend/
```

Rules:

- Do not modify `docs/product`, `docs/domain`, `docs/architecture`, or
  `docs/AGENTS.md`.
- `README.md` at the repository root is written by this feature and documents
  everything required under Local Development.
- `.gitignore` at the repository root must cover the standard .NET artifacts
  (`bin/`, `obj/`, `.vs/`), Node artifacts (`node_modules/`, `dist/`,
  `.angular/`), and secret-bearing local files (see Configuration and
  Secrets).
- No CI/CD configuration is created.

### Frontend

Create the Angular application using the current stable Angular version
available at implementation time via the official Angular CLI, located at
`src/frontend/` with project name `game-library`.

Required setup:

- Routing enabled (Angular Router).
- SCSS for styles.
- Standalone components using the Angular CLI defaults for the selected
  version. Do not introduce NgModules beyond what the generated tooling
  produces.
- Add PWA support with the official schematic (see PWA).
- Add lint support using the standard Angular ESLint tooling if the selected
  Angular version does not generate it by default.
- Do not add `supabase-js` in this feature. Authentication tooling arrives with
  the authentication feature.
- Do not add NgRx or any state-management library.

Folder structure inside `src/frontend/src/app/`, per the Architecture
Specification's proposed organization:

```text
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
```

- The `features/*` subfolders are created empty as the approved skeleton using
  `.gitkeep` files so future features do not invent layouts. No feature code
  or speculative shared abstractions are added.
- `core/` and `shared/` are created empty (or with a `.gitkeep`) and must stay
  small.
- `app.routes.ts` defines routing. A minimal default route renders a small
  foundation "health check" view that displays the result of calling the
  backend `GET /health` endpoint. This is foundation verification, not
  business functionality. Later features replace or extend the default route.

A small `core/` HTTP service calls `GET /health` using the configured API base
URL and an injected `HttpClient` (`provideHttpClient()` in the app config).
The health-check component renders the success or failure result. This
directly verifies Angular-to-API communication with the smallest possible
surface area.

Environment configuration:

- Use Angular environment files (`src/environments/environment.ts` and
  `src/environments/environment.development.ts`) with an `apiBaseUrl` value.
  The development environment points `apiBaseUrl` at
  `http://localhost:5218`. The default (production) environment uses a
  placeholder/empty value; production configuration is out of scope.
- The API base URL is public configuration and is committed.
- `angular.json` uses the standard `fileReplacements` so `ng serve` uses the
  development environment and `ng build` (production) uses the default
  environment.

Verification commands (see Acceptance Criteria and Verification):

- `npm ci` (or the README-documented install command).
- `ng serve` for local development.
- `ng build` for a production build (also verifies PWA artifacts).
- `ng test` (single-run mode: `ng test --watch=false`) for unit tests.
- `ng lint` for lint checks.

### Backend

Create exactly two application projects plus verification test projects. The
backend application consists of exactly:

```text
src/backend/
├── GameLibrary.sln
├── GameLibrary.Api/
└── GameLibrary.Core/
```

Test projects are verification projects and are not part of the two-project
backend architecture; they live under `tests/` (see Testing).

Select the current stable supported/LTS .NET SDK available at implementation
time. Do not use preview versions.

Solution and references:

- One solution file `GameLibrary.sln` at `src/backend/`.
- `GameLibrary.Core` is a class library.
- `GameLibrary.Api` is an ASP.NET Core Web API using controllers (the approved
  stack is "ASP.NET Core Web API"; controllers provide the resource-oriented
  HTTP boundary future features require).
- `GameLibrary.Api` references `GameLibrary.Core`.
- Root namespaces use the `GameLibrary.*` convention.

`GameLibrary.Core` responsibilities:

- Holds the EF Core `DbContext` and migrations infrastructure only. No domain
  entities are defined in this feature.
- Contains the EF Core `DbContext` (`GameLibrary.Core/Data/`).
- Contains EF Core migrations (`GameLibrary.Core/Data/Migrations/`).
- References EF Core and Npgsql packages only.
- No generic repository or Unit of Work abstraction is introduced.

`GameLibrary.Api` responsibilities:

- HTTP endpoint(s) — only the health endpoint in this feature.
- Dependency injection / composition.
- Configuration.
- CORS.
- Centralized error handling.
- References `Microsoft.EntityFrameworkCore.Design` (PrivateAssets) so
  `dotnet ef` tooling can use the API as the startup project.
- Contains `Program.cs`, `appsettings.json`, development configuration, and
  `launchSettings.json`.

`Program.cs` must:

- Register the `DbContext` with Npgsql using the `ConnectionStrings:Default`
  value.
- Register controllers (`AddControllers`) and problem details
  (`AddProblemDetails`).
- Register a CORS policy allowing the configured Angular origin in development
  (see Database / Supabase and Configuration and Secrets).
- Configure centralized exception handling: unexpected exceptions return a
  consistent JSON error response and never expose stack traces or sensitive
  details to clients (Architecture Specification section 17).
- Map controllers.
- Remove the template's sample endpoint/controller (e.g., the generated
  WeatherForecast code).
- Not enable HTTPS redirection in the development profile (local development
  uses HTTP; production HTTPS is a hosting concern outside this feature).

Health endpoint:

- `GET /health` returns HTTP 200 with a small JSON body (for example
  `{ "status": "ok" }`).
- It is anonymous (no authentication exists yet) and intentionally has no
  database dependency. It verifies that the API process started and is
  responding. Database connectivity is verified separately by the integration
  tests and by applying migrations (see Acceptance Criteria and Verification).
- The route is not prefixed with `api/`; it is exactly `/health`.

Development port:

- `launchSettings.json` pins the HTTP profile to `http://localhost:5218` so
  the Angular development environment and CORS configuration have a stable
  target.

Backend boundaries are preserved per AGENTS.md: HTTP concerns stay in
`GameLibrary.Api`; the `DbContext` and migrations live in `GameLibrary.Core`.
Do not create additional
Domain/Application/Infrastructure/Contracts/SharedKernel projects.

### Database / Supabase

- EF Core with the Npgsql PostgreSQL provider is used in `GameLibrary.Core`.
- EF Core migrations are the single source of truth for the application
  schema (Architecture Specification section 13). No other migration workflow
  is introduced. Supabase-managed schemas (`auth.*` and friends) are never
  modified by application migrations.
- The application connects using the `ConnectionStrings:Default` connection
  string configured for the `DbContext`.
- Migrations use a direct PostgreSQL connection, not a transaction-pooling
  connection (Architecture Specification section 13). Locally this means the
  direct Supabase Postgres port, not the pooler port.
- Local development uses the Supabase CLI to start the local Supabase stack
  through Docker, providing PostgreSQL (and, for the future, local Auth).
  Feature 001 uses only the local PostgreSQL service.

Baseline migration:

- Create the `DbContext` and an initial, intentionally empty EF migration
  (`InitialCreate` with empty `Up`/`Down`).
- Rationale: EF migration tooling requires at least one migration and a
  migration history table to operate and to verify end-to-end connectivity.
  An empty baseline establishes that tooling (creating only the
  `__EFMigrationsHistory` infrastructure) without introducing any business
  tables. This is the smallest acceptable technical infrastructure required to
  verify EF Core/PostgreSQL connectivity and to give future features a clean
  migration baseline.
- The migration is committed to source control.
- No business-domain tables are created (no `Game`, `Library`,
  `LibraryEntry`, `Platform`, `Genre`, or user tables).

Dev-port note: the local Supabase stack runs the direct Postgres connection on
port `54322` by default (`postgresql://postgres:postgres@127.0.0.1:54322/postgres`).
These are the well-known local Supabase defaults and are not real secrets, but
they are still kept out of committed configuration via the mechanism in
Configuration and Secrets. The exact local connection string is printed by
`supabase status`.

### Configuration and Secrets

No secrets are committed.

Backend configuration files:

- `appsettings.json` is committed and contains only non-secret shared
  settings.
- `appsettings.Development.example.json` is committed as a template containing
  placeholder values (for example `<connection-string>`) and documents the
  expected keys:
  - `ConnectionStrings:Default` — the application PostgreSQL connection
    string.
  - `AllowedOrigins` — the development CORS origins, default
    `["http://localhost:4200"]`.
- `appsettings.Development.json` is gitignored. A developer copies the example
  file (or relies on environment variables/user-secrets).
- The local connection string is provided through either:
  - `dotnet user-secrets set "ConnectionStrings:Default" "<connection-string>"`
    (run in `src/backend/GameLibrary.Api`), or
  - the environment variable `ConnectionStrings__Default`.

Frontend configuration:

- `src/environments/environment.ts` and
  `src/environments/environment.development.ts` are committed. They contain
  only public configuration (the API base URL). No frontend secret exists in
  Feature 001.

`.gitignore` must exclude at least:

- .NET: `bin/`, `obj/`, `.vs/`.
- Node: `node_modules/`, `dist/`, `.angular/`.
- Secret-bearing local files: `appsettings.Development.json`, `.env`, `.env.*`
  (except `.env.example`), `supabase/.temp/`, local credential dumps.

Supabase CLI:

- `supabase init` produces `supabase/config.toml`, which is committed and
  contains no secrets.
- `supabase start` downloads/starts local services through Docker. Local
  temporary credentials printed by `supabase status` are local-only and are
  never committed.

### Testing

Backend:

- One integration-test project:
  `tests/backend/GameLibrary.IntegrationTests/`.
- Use xUnit and `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory`)
  as the standard test tooling.
- Integration tests run against real PostgreSQL semantics. They must not use
  EF Core InMemory as the authoritative integration database
  (Architecture Specification section 25; AGENTS.md Database Rules).
- The tests use a dedicated test database (for example
  `game_library_test` on the local Supabase Postgres instance, or a standalone
  Docker PostgreSQL) identified by the `ConnectionStrings:Test` configuration
  (user-secret or `ConnectionStrings__Test` environment variable).
- Tests self-provision schema by calling `MigrateAsync` (idempotent), so no
  separate manual migration step is required before running them.
- Required integration coverage for this feature:
  - The API starts through `WebApplicationFactory` and `GET /health` returns
    HTTP 200.
  - The `DbContext` connects to the real PostgreSQL test database and EF
    migrations apply successfully.
- Do NOT create a backend unit-test project in this feature. Rationale: there
  are no domain or application rules yet to test (no entities exist). A
  unit-test project is added by the first feature that introduces testable
  domain/application logic (for example authentication or domain invariants),
  per the approved testing strategy.
- Test command: `dotnet test` at the solution or project level.

Frontend:

- Use the standard Angular testing approach selected by the generated Angular
  version/tooling (for example Jasmine/Karma or the CLI's current default).
  Do not add a separate testing framework beyond what the Angular tooling
  generates.
- Write one small, meaningful test verifying that the foundation health-check
  view/service calls `GET /health` using a mocked HTTP client
  (`provideHttpClientTesting` / `HttpClientTestingModule` as appropriate for
  the selected version).
- Test command: `ng test` (single-run mode `ng test --watch=false`).

End-to-end:

- Do not implement E2E tests in this feature. There is no foundation-only
  reason that justifies them here.

### PWA

- Configure the Angular application as a PWA using the official Angular
  schematic (`ng add @angular/pwa`).
- This adds the service worker package, `ngsw-config.json`, the web app
  manifest, icons, and registers the service worker through the app config.
- Static/application-shell caching is sufficient
  (Architecture Specification section 20; AGENTS.md PWA Rules).
- Do NOT implement offline CRUD, offline synchronization, a client-side
  database, mutation queues, or conflict resolution.
- The service worker is active in production builds. Verification of the PWA
  foundation uses `ng build` output (see Acceptance Criteria and
  Verification).

### Local Development

The implementation must document the complete local workflow in `README.md`
at the repository root, including:

- Required tool versions and the version-selection policy:
  - .NET SDK (current stable LTS at implementation time).
  - EF Core / Npgsql versions compatible with the selected .NET SDK.
  - Node.js (current stable LTS).
  - Angular / Angular CLI (current stable).
  - Supabase CLI (current stable).
  - Docker Desktop (required by the local Supabase stack).
  - The `dotnet-ef` global tool.
  - The actual versions selected during implementation are recorded in
    `README.md` so future agents do not guess.
- Dependency installation:
  - `dotnet` restore/build for the backend.
  - `npm ci` (or `npm install`) in `src/frontend/`.
  - `dotnet tool install --global dotnet-ef` if not already installed.
- Local infrastructure startup:
  - `supabase init` (once) and `supabase start`.
  - `supabase status` to obtain the local connection string.
- Backend startup:
  - Set `ConnectionStrings:Default` via user-secrets or environment variable.
  - `dotnet ef database update` to apply migrations (direct connection).
  - `dotnet run` from `src/backend/GameLibrary.Api`.
- Frontend startup:
  - `ng serve` from `src/frontend/`; open `http://localhost:4200`.
- Tests and checks:
  - Backend tests: `dotnet test`.
  - Frontend tests: `ng test --watch=false`.
  - Frontend lint: `ng lint`.
  - Backend build: `dotnet build src/backend/GameLibrary.sln`.
  - Frontend production build (also verifies PWA artifacts): `ng build`.
- Required environment variables/configuration and the config template
  (`appsettings.Development.example.json`).

## Functional Requirements

`FR-001` — Repository layout: The repository provides the approved top-level
layout including `README.md`, `.gitignore`, `docs/`, `specs/`,
`src/frontend/`, `src/backend/`, and `tests/backend/`. Authoritative documents
under `docs/` are unchanged.

`FR-002` — Backend solution: A single .NET solution exists at
`src/backend/GameLibrary.sln` containing exactly the two application projects
`GameLibrary.Api` and `GameLibrary.Core`, with `GameLibrary.Api` referencing
`GameLibrary.Core`. `dotnet build src/backend/GameLibrary.sln` succeeds.

`FR-003` — Health endpoint: When the API runs with development configuration,
`GET /health` returns HTTP 200 with a small JSON body. It requires no
authentication and has no database dependency.

`FR-004` — PostgreSQL connectivity and migrations: With `ConnectionStrings:Default`
configured, EF Core migrations apply successfully to a real PostgreSQL
database. The only schema artifacts produced are EF migration infrastructure;
no business-domain tables exist.

`FR-005` — Angular foundation: The Angular application starts via `ng serve`,
renders a default route with a foundation health-check view, and builds
successfully via `ng build`. The application is configured as a PWA.

`FR-006` — Angular-to-API communication: The foundation health-check view
calls the API `GET /health` endpoint using the configured `apiBaseUrl` and
displays the result, demonstrating that Angular can communicate with the API.

`FR-007` — Development CORS: In development, the API allows requests from the
Angular dev origin `http://localhost:4200` and does not use an unrestricted
CORS policy.

`FR-008` — Backend integration tests: Backend integration tests execute
against real PostgreSQL and pass, covering API startup/health and database
connectivity/migration application.

`FR-009` — Frontend checks: Frontend tests (`ng test`) and lint (`ng lint`)
run successfully.

`FR-010` — Local development documentation: `README.md` documents required
versions, dependency installation, local Supabase/PostgreSQL startup, backend
startup, frontend startup, tests, build/check commands, and required
configuration. A configuration template (`appsettings.Development.example.json`)
is provided.

## Non-Functional Requirements

`NFR-001` — Simplicity: The implementation introduces no tool, abstraction,
project, or dependency without a concrete current need. Every dependency added
is a framework dependency required by the approved architecture or standard
tooling for the selected framework, and the actual versions are recorded in
`README.md`.

`NFR-002` — No business scope: No business-domain tables, entities, endpoints,
or authentication behavior are introduced. The backend application consists of
exactly `GameLibrary.Api` and `GameLibrary.Core`.

`NFR-003` — Secret safety: No secrets are committed. Secret-bearing local
files are gitignored; committed configuration contains only non-secret values
or placeholders.

`NFR-004` — Reproducibility: A developer can bring a fresh clone to a running
local state using only the commands documented in `README.md`, with no
invented steps. Selected versions are recorded.

`NFR-005` — Developer experience: The local workflow uses a small number of
documented commands. The API dev port and Angular dev origin are stable and
documented.

`NFR-006` — Safe error handling: Unexpected exceptions produce consistent JSON
error responses and never expose stack traces or sensitive details to clients.

`NFR-007` — Maintainability: Code follows the approved repository and project
structure. No speculative shared abstractions or generic infrastructure are
created before real, multiple usages justify them.

## Acceptance Criteria

`AC-001` — A fresh clone of the repository, following `README.md` install
steps, builds the backend solution successfully.

`AC-002` — The Angular application builds successfully, and the production
build output contains the expected PWA artifacts (service worker configuration
and web app manifest).

`AC-003` — The local Supabase/PostgreSQL stack starts via the documented
command, the API connects to it, and `dotnet ef database update` applies
migrations successfully, creating only EF infrastructure (no business-domain
tables).

`AC-004` — The API starts locally and `GET /health` returns HTTP 200.

`AC-005` — The Angular application starts locally and the foundation
health-check view displays the API's success response, proving Angular can
communicate with the API.

`AC-006` — Backend integration tests pass against a real PostgreSQL database.

`AC-007` — Frontend tests and lint checks execute successfully.

`AC-008` — No secrets are committed: the repository contains no real
credentials, secret-bearing local files are gitignored, and only templates or
placeholders are committed.

`AC-009` — No business-domain tables or features are introduced: migrations
contain no business tables, the only HTTP endpoint is `/health`, no
authentication wiring or `supabase-js` dependency exists, and the backend
application consists of exactly two projects.

## Verification

For each acceptance criterion, an implementation agent verifies as follows.

`AC-001`:
- On a fresh clone (or equivalent clean state), run
  `dotnet build src/backend/GameLibrary.sln`. The command reports a successful
  build with no errors. If `dotnet-ef` is required, confirm
  `dotnet tool list --global` shows it installed (or install it first).

`AC-002`:
- From `src/frontend/`, run `npm ci` then `ng build`. The command completes
  successfully. Inspect the `dist/` output and confirm it contains the service
  worker configuration (e.g., `ngsw.json`) and the web app manifest (e.g.,
  `manifest.webmanifest`). The exact file names follow the generated Angular
  version.

`AC-003`:
- Run `supabase start` (after `supabase init` on first use). The CLI reports
  healthy services. Capture the direct Postgres connection string from
  `supabase status` (default `127.0.0.1:54322`).
- Set `ConnectionStrings:Default` to that connection string via user-secrets
  or `ConnectionStrings__Default`.
- Run `dotnet ef database update --project src/backend/GameLibrary.Core
  --startup-project src/backend/GameLibrary.Api`. It succeeds.
- Connect to the database (e.g., `psql` from the Supabase CLI container or a
  client) and confirm the `public` schema contains only EF migration
  infrastructure such as `__EFMigrationsHistory` and no business-domain tables.

`AC-004`:
- With the connection string configured, run `dotnet run` from
  `src/backend/GameLibrary.Api` (or `dotnet run --project
  src/backend/GameLibrary.Api`).
- Request `GET http://localhost:5218/health` (e.g., `curl` or a browser). The
  response status is `200` and the body is the small JSON health response.

`AC-005`:
- With the API still running, run `ng serve` from `src/frontend/` and open
  `http://localhost:4200`.
- The default route renders the foundation health-check view and displays the
  API success result (for example "API healthy" / status `ok`). This confirms
  Angular made an HTTP request to `http://localhost:5218/health` and received
  a `200` response.
- Also confirm the API's development CORS policy permitted the request (the
  browser console shows no CORS error for the health request).

`AC-006`:
- Set `ConnectionStrings:Test` (user-secret or
  `ConnectionStrings__Test` environment variable) to a real PostgreSQL test
  database (for example `game_library_test` on the local Supabase Postgres
  instance, or a Docker PostgreSQL).
- Run `dotnet test tests/backend/GameLibrary.IntegrationTests`. All tests
  pass. The test database is real PostgreSQL; EF Core InMemory is not used.

`AC-007`:
- From `src/frontend/`, run `ng test --watch=false`. Tests pass.
- From `src/frontend/`, run `ng lint`. No errors are reported.

`AC-008`:
- Search the committed files (e.g., `git status`, `git grep` for credential
  patterns) and confirm no real database credentials, connection strings, or
  secrets appear in committed configuration.
- Confirm `.gitignore` excludes `appsettings.Development.json`, `.env`, and
  similar local files, and that the committed backend template
  (`appsettings.Development.example.json`) contains placeholders only.

`AC-009`:
- Review the initial migration file and confirm it contains no business
  tables.
- Review the backend for HTTP endpoints and confirm `/health` is the only one.
- Confirm no authentication configuration, no `supabase-js` package, and no
  business-domain entities exist.
- Confirm `src/backend/GameLibrary.sln` contains exactly the two application
  projects (`GameLibrary.Api`, `GameLibrary.Core`) plus the verification test
  project(s) under `tests/`.

## Dependencies

Prerequisites documented in `README.md` and required at implementation and
development time:

- Git.
- Node.js (current stable LTS) and npm.
- Angular / Angular CLI (current stable).
- .NET SDK (current stable supported/LTS version selected at implementation
  start).
- EF Core and Npgsql packages compatible with the selected .NET SDK.
- `dotnet-ef` global tool (version aligned with the selected EF Core).
- Docker Desktop (required by the local Supabase stack).
- Supabase CLI (current stable).
- PostgreSQL itself is provided by the local Supabase stack through Docker;
  no separate PostgreSQL installation is required for local development.
- Testing tooling: xUnit and `Microsoft.AspNetCore.Mvc.Testing` for backend
  integration tests; the Angular-generated default test tooling for frontend
  tests.

Version-selection policy: use stable, supported versions available at
implementation time. Record the actual selected versions in `README.md`. Do
not introduce preview versions unless explicitly justified.

## Risks / Notes

- **AGENTS.md location**: The Architecture Specification's repository diagram
  shows `AGENTS.md` at the repository root, but the repository currently
  stores it at `docs/AGENTS.md`. Feature 001 does not relocate it; this
  discrepancy is noted and can be resolved separately if desired.
- **Docker dependency**: Local Supabase requires Docker. Machines without
  Docker cannot run `supabase start`; in that case the integration tests can
  use a standalone Docker PostgreSQL or an alternative real PostgreSQL
  instance via `ConnectionStrings:Test`, but the approved local stack remains
  the Supabase CLI.
- **Local Supabase credentials**: The local connection string
  (`postgres:postgres@127.0.0.1:54322`) is a well-known local default, not a
  real secret, but the configuration/secrets mechanism still keeps it out of
  committed files to establish the correct pattern for future real secrets.
- **Pooler vs direct connection**: The direct Postgres port (default `54322`)
  must be used for migrations, not the transaction-pooling port, per the
  Architecture Specification section 13.
- **Service worker only in production builds**: Angular's service worker is
  active in production builds; local `ng serve` verification of the PWA
  foundation relies on the `ng build` output instead.
- **Frontend test tooling**: The concrete frontend test framework is whatever
  the generated Angular version selects. Do not force a specific framework;
  `ng test` is the canonical command.
- **Empty baseline migration**: The initial migration is intentionally empty
  and exists only to establish the EF migration infrastructure and verify
  connectivity. It is not a domain artifact.
- **Port conflicts**: The API (`5218`) and Angular (`4200`) development ports
  are fixed for a stable configuration; conflicts are resolved by documenting
  the override in `README.md`.
- **No JWT configuration now**: Local Supabase Auth services start with
  `supabase start`, but no JWT validation configuration or `supabase-js`
  dependency is added in this feature; that is the authentication feature's
  scope.

## Open Questions

None. The foundation requires no product or architecture decisions beyond
those already fixed by the approved Product, Domain, and Architecture
Specifications and this specification.