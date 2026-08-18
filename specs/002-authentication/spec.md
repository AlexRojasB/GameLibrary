# 002 — Authentication

## Status

`Draft`

## Objective

Establish the minimum authentication foundation required for the Game Library MVP:

- Supabase Auth as the identity provider.
- Angular authentication flows using `supabase-js`.
- Supabase-issued JWT access tokens.
- ASP.NET Core JWT bearer validation of Supabase-issued tokens.

After this feature is implemented, a user can:

1. Register.
2. Log in.
3. Remain authenticated while the Supabase session is valid.
4. Log out.
5. Access authenticated Angular routes.
6. Call a protected ASP.NET Core endpoint using the Supabase access token.
7. Be rejected by protected API endpoints when unauthenticated or when using an invalid token.

This feature establishes **identity only**. It implements no Library or game/business
functionality. No `User` business/domain entity, no `Library`, no application user table, and
no linking of Supabase accounts to application records are introduced.

## Context

The approved Product, Domain, and Architecture Specifications are authoritative. This
specification is the second feature in the approved delivery order:

1. Project foundation and local development. (implemented)
2. Authentication. (this feature)
3. Platform management.
4. VideoGame management.
5. BoardGame management.
6. Library browse/search/filter/sort.
7. Random Picker.
8. PWA polish and MVP end-to-end verification.

The approved authentication architecture:

```text
Supabase Auth
     |
     | issues access JWT
     v
Angular PWA
     |
     | Authorization: Bearer <access-token>
     v
ASP.NET Core API
     |
     | validates Supabase-issued JWT
     v
Authenticated user identity from JWT `sub`
```

Supabase Auth is the identity provider. ASP.NET Core Identity is not introduced. The API does
not issue its own application authentication tokens.

### Actual foundation produced by Feature 001

This specification builds on the actual repository state:

- Backend: `src/backend/GameLibrary.sln` with exactly two application projects —
  `GameLibrary.Api` (ASP.NET Core Web API, .NET 10) and `GameLibrary.Core`
  (EF Core 10.0.11, Npgsql 10.0.3). `GameLibrary.Api` references `GameLibrary.Core`.
- `GameLibrary.Api/Program.cs` currently registers controllers, problem details, the
  `GameLibraryDbContext` with Npgsql, a "Frontend" CORS policy from `AllowedOrigins`, and
  centralized exception handling. Middleware order is currently
  `UseExceptionHandler` → `UseCors` → `MapControllers`. There is no authentication yet.
- `GET /health` (`GameLibrary.Api/Controllers/HealthController.cs`) is anonymous and returns
  HTTP 200. It must remain anonymous and functional after this feature.
- `GameLibrary.Core` holds only the `DbContext` and the intentionally empty `InitialCreate`
  migration. No business tables exist.
- Frontend: Angular 22 (`src/frontend/`, standalone components, SCSS, PWA via
  `@angular/service-worker`), `provideHttpClient()`, `provideRouter()`, and a default route
  `''` rendering the foundation health-check view
  (`src/app/core/health/health-check/`). Environment files
  (`src/environments/environment.ts` and `environment.development.ts`) currently expose only
  `apiBaseUrl` (dev: `http://localhost:5218`). Frontend tests run via Vitest through
  `@angular/build:unit-test` (`ng test`).
- Tests: `tests/backend/GameLibrary.IntegrationTests/` uses xUnit +
  `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory<Program>`) against real
  PostgreSQL. There is no backend unit-test project yet.
- Configuration: committed files contain only non-secret values or placeholders;
  `appsettings.Development.json` is gitignored; `appsettings.Development.example.json` is the
  committed template. `ConnectionStrings:Default` / `ConnectionStrings:Test` are supplied via
  user-secrets or environment variables.

### Supabase development project

The current development environment uses a **remote Supabase development project**, not a local
Docker/Supabase stack. Evidence in the repository: the committed `appsettings.json` connection
string references `db.iramzxpjbnldhebykzhx.supabase.co`, and the gitignored
`appsettings.Development.json` points at the same remote project. Docker is not available in
this environment. This feature therefore uses the same remote project for Auth and does **not**
require Docker.

Confirmed public facts about the current Supabase project (`iramzxpjbnldhebykzhx`):

- Project URL: `https://iramzxpjbnldhebykzhx.supabase.co`
- Auth URL (issuer): `https://iramzxpjbnldhebykzhx.supabase.co/auth/v1`
- OpenID Connect metadata:
  `https://iramzxpjbnldhebykzhx.supabase.co/auth/v1/.well-known/openid-configuration`
- JWKS:
  `https://iramzxpjbnldhebykzhx.supabase.co/auth/v1/.well-known/jwks.json`
  (currently returns an asymmetric ES256/P-256 key, confirming the project uses asymmetric
  signing keys)
- User access tokens carry `aud = "authenticated"`, `sub = <user UUID>`, and
  `iss = <auth URL>`.

These are public, non-secret infrastructure values. Backend JWT validation uses the OpenID
Connect metadata/JWKS, so **the backend requires no Supabase secret** to validate tokens.

## In Scope

- `supabase-js` client configuration and provisioning in Angular.
- Public frontend configuration values required by the SDK (project URL, publishable key,
  API base URL).
- An authentication service/state built on Angular signals and local state.
- Register, login, logout, and session initialization/restoration using Supabase's supported
  client behavior.
- Protected and guest-only Angular routes with guards.
- Attaching the Supabase access token to Game Library API requests via an HTTP interceptor
  scoped to the API base URL.
- ASP.NET Core JWT bearer authentication that validates Supabase-issued access tokens via
  standards-based OpenID Connect metadata/JWKS discovery.
- A small reusable backend mechanism for obtaining the authenticated Supabase user ID from the
  validated JWT `sub` claim.
- One protected verification endpoint: `GET /auth/me`.
- Error behavior for login, registration, provider/network failures, and session
  expiration/unauthorized API responses.
- Local-development documentation for the chosen remote Supabase project and the documented
  Docker/local-Supabase alternative.
- Proportionate backend, frontend, and manual verification as specified under Testing.

## Out of Scope

Feature 002 must NOT implement or introduce:

- Library, `Library`, `LibraryEntry`, `Game`, `VideoGame`, `BoardGame`, `Platform`, `Genre`,
  or any business functionality.
- A `User` business/domain entity, an application user table, profiles, display names,
  avatars, or linking a Supabase account to an application User record.
- RLS.
- Direct Angular access to application database tables.
- ASP.NET Core Identity.
- Custom password storage or custom JWT issuing.
- Social login, MFA, roles, permissions, admin accounts, user profiles, or shared libraries.
- Password-reset or email-verification UI flows (Supabase capabilities may be documented where
  relevant, but no additional UI is built; see Risks / Notes for email-confirmation handling).
- A backend unit-test project (see Testing for rationale).
- End-to-end (E2E) test suites (only manual acceptance verification is required).
- Production deployment configuration.
- New backend projects, NgRx, MediatR, CQRS, custom token-persistence wrappers, or an
  authentication server.

## Technical Requirements

### Supabase Auth

- Supabase Auth handles registration, login, logout, sessions, password reset, and email
  verification where the project has it configured. The application does not re-implement any
  of these.
- The MVP uses email/password only. Social login, MFA, roles, permissions, and admin accounts
  are not added.
- Session persistence and token refresh are Supabase client responsibilities. `supabase-js`
  persists the session in the browser and refreshes access tokens as needed while the session
  is valid. The application must not re-implement token refresh or manually persist access
  tokens in `localStorage`/`sessionStorage`.
- Registration must work per the project's email-confirmation setting:
  - If the project does not require email confirmation, a successful `signUp` produces a
    usable session immediately.
  - If the project requires email confirmation, `signUp` may return a user without a session;
    the register flow must then inform the user to confirm their email, and login is gated on
    confirmation. Both behaviors are defined; no configuration of the Supabase project is
    required by this feature.

### Frontend Authentication

- Add the `@supabase/supabase-js` dependency (stable version, recorded in `README.md`).
- Create the Supabase client from the public environment configuration:
  - `supabaseUrl` — the Supabase project URL.
  - `supabaseKey` — the project's publishable key (or legacy anon key). Both are public.
- Provision the client through an `InjectionToken` (for example `SUPABASE_CLIENT`) whose
  factory calls `createClient(environment.supabaseUrl, environment.supabaseKey)`. Register the
  token in `app.config.ts`. This is the mockable boundary for frontend tests; no large custom
  abstraction over Supabase is created.
- Create an `AuthService` (`core/auth/`) that exposes:
  - Current authentication state as an Angular signal derived from the Supabase session
    (for example a `Session`/`User` signal and an `isAuthenticated` signal).
  - The current access token accessor used by the HTTP interceptor.
  - `signUp(email, password)`, `signIn(email, password)`, and `signOut()` methods that wrap the
    corresponding `supabase.auth` calls.
  - Session initialization/restoration: on application start, call `supabase.auth.getSession()`
    once to restore the persisted session and subscribe to
    `supabase.auth.onAuthStateChange` to keep state in sync (including `TOKEN_REFRESHED`).
  - Normalized errors (see Error Handling).
- Keep the auth state minimal: signals + local state. No NgRx, no complex auth state machine.
- The `AuthService` lives in `core/auth/` (it is app-wide infrastructure consumed by guards and
  the interceptor). Auth UI components live in `features/auth/`.

### Frontend Configuration

- Extend the existing environment files. They may contain only public configuration:
  - `src/environments/environment.ts` (default/production): `apiBaseUrl` (unchanged,
    placeholder/empty), `supabaseUrl` (placeholder/empty), `supabaseKey` (placeholder/empty).
  - `src/environments/environment.development.ts`: `apiBaseUrl` (`http://localhost:5218`),
    and the current dev project's public values `supabaseUrl` and `supabaseKey`.
- `angular.json` `fileReplacements` already map the development environment for `ng serve` and
  the default environment for `ng build`; no change to that mechanism is required.
- The concrete development values are public and committed, consistent with how `apiBaseUrl`
  is already committed. They are obtained from the Supabase dashboard (Settings → API); the
  current values are recorded under Local Development.
- Never expose the service-role key, database credentials, or any server-only secret to
  Angular.
- The API base URL remains `http://localhost:5218` in development.

### Route Protection

- Add functional route guards in `core/auth/`:
  - `authGuard` (`canActivate`): permits navigation only when the user is authenticated;
    otherwise redirects to `/login`.
  - `guestGuard` (`canActivate`): permits navigation to login/register only when the user is
    not authenticated; otherwise redirects to the home route.
- Guards must account for session restoration: they must await the Supabase session state (for
  example via `supabase.auth.getSession()` / the auth service's ready state) so a valid
  persisted session is never treated as "guest" on first load. A simple await of the session
  state is sufficient; do not build a loading-state machine.
- Define the minimum route set in `app.routes.ts`:
  - `''` — authenticated home placeholder (protected by `authGuard`).
  - `login` — login view (protected by `guestGuard`).
  - `register` — register view (protected by `guestGuard`).
  - `health` — the Feature 001 health-check view, retained and anonymous (this keeps the
    foundation health view available and gives the protected `''` route its verification role).
- This is not the final application navigation. Do not design the complete product navigation.

### API Token Attachment

- Create a functional HTTP interceptor (`core/auth/`, registered through
  `provideHttpClient(withInterceptors([...]))` in `app.config.ts`).
- Behavior:
  - If the request URL starts with the configured `environment.apiBaseUrl` **and**
    `apiBaseUrl` is non-empty **and** a Supabase session with an access token exists, clone the
    request with header `Authorization: Bearer <access-token>`.
  - Otherwise pass the request through unchanged (this covers requests to other origins,
    absence of a session, and the production placeholder where `apiBaseUrl` is empty).
- The interceptor must never attach the token to arbitrary third-party URLs.
- Read the current access token from the `AuthService` state, not from manual
  `localStorage`/`sessionStorage` access. Protected routes are reachable only after the guard
  confirms a session, so the token state is populated before protected API calls occur.
- Do not build custom token-refresh logic. Supabase manages refresh.

### Backend JWT Authentication

Use `Microsoft.AspNetCore.Authentication.JwtBearer` and standards-based OpenID Connect
metadata/JWKS discovery. Do not invent a custom JWT implementation.

Configuration (in `GameLibrary.Api`):

- Register `AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(...)` and
  `AddAuthorization()`.
- The JWT bearer handler is configured from the `Authentication:Schemes:Bearer` configuration
  section (the standard binding) plus explicit code-level settings:
  - `MetadataAddress` — the Supabase OpenID Connect metadata URL
    (`https://iramzxpjbnldhebykzhx.supabase.co/auth/v1/.well-known/openid-configuration`).
    The handler discovers the JWKS and issuer from metadata. This validates the token
    signature against Supabase's asymmetric public keys and tolerates key rotation via the
    standard metadata refresh.
  - `RequireHttpsMetadata` — `true` (default) because the metadata address is HTTPS in the
    current setup.
  - `MapInboundClaims = false` — keep standard JWT claim names (notably `sub`) on the
    authenticated principal so the current-user helper reads `sub` directly.
  - `TokenValidationParameters`:
    - `ValidateIssuer = true` (default); the issuer is taken from metadata.
    - `ValidateAudience = true` (default) with `ValidAudiences = ["authenticated"]` (Supabase
      user access tokens carry `aud = "authenticated"`).
    - `ValidateLifetime = true` (default). The default `ClockSkew` of 5 minutes is acceptable
      for Supabase access tokens.
    - `ValidateIssuerSigningKey = true` (default), via the JWKS keys.
    - `ValidAlgorithms` set to the asymmetric algorithms the project JWKS uses — currently
      `ES256`; include `RS256` as well so a key-type change does not break validation.
- Add middleware in the correct order in `Program.cs`:
  `UseExceptionHandler` → `UseCors` → `UseAuthentication` → `UseAuthorization` → `MapControllers`.
- `GET /health` remains anonymous (no `[Authorize]`).
- The committed `appsettings.json` and the `appsettings.Development.example.json` template gain
  a non-secret `Authentication:Schemes:Bearer` section (metadata address and audience values;
  see Local Development). No signing secret is ever configured in the backend because
  validation is asymmetric via JWKS.

### Current User Identity

- Establish a small reusable mechanism in `GameLibrary.Api` (the authentication boundary) for
  obtaining the authenticated Supabase user ID.
- Recommended simplest approach: a small extension/helper around `HttpContext.User`, for
  example `ClaimsPrincipal.GetSupabaseUserId()` returning `string?`, reading the `sub` claim
  (available unchanged because `MapInboundClaims = false`).
- Behavior: returns the `sub` value for an authenticated principal, or `null` when the
  principal is unauthenticated or lacks a usable `sub` claim. Treating a missing `sub` as
  unauthenticated is the required behavior.
- Do not create a large identity/domain framework, a User repository, or a User entity. Future
  features may evolve this into a scoped current-user service when real consumers justify it.
- The implementation must never trust a client-supplied user ID.

### Protected Verification Endpoint

- Add `GET /auth/me` (in `GameLibrary.Api`, for example an `AuthController`):
  - Annotated with `[Authorize]`.
  - Unauthenticated request → `401 Unauthorized` (produced by the JWT bearer handler).
  - Authenticated request → `200 OK` with a minimal JSON body:
    ```json
    {
      "userId": "<supabase-sub>"
    }
    ```
  - If the authenticated principal has no `sub` claim, return `401 Unauthorized`.
- Return only the user ID. Do not return token details, email, roles, or other claims.
- This endpoint is authentication verification only. No `User` domain entity or application
  user table is created.

### Error Handling

Keep error presentation simple and user-visible. Do not expose raw internal/auth-provider
details unnecessarily.

- **Invalid login credentials**: the login view shows a clear message (for example
  "Invalid email or password."). The user remains on the login form.
- **Registration failure** (for example email already registered, weak password): the register
  view shows a clear, non-technical message.
- **Network/auth-provider failure**: both views show a generic message such as "Unable to reach
  the authentication service. Check your connection and try again."
- **Session expiration / unauthorized API response**: a protected API call returning
  `401 Unauthorized` results in a user-visible "session expired / sign in again" state and a
  path back to `/login`. For this feature, the protected home placeholder handles its own
  `/auth/me` `401` response. No global auto-logout infrastructure is required.
- Error normalization lives in the `AuthService` so login/register views render consistent
  messages rather than raw Supabase error strings.

### Local Development

The current environment uses the **remote Supabase project** `iramzxpjbnldhebykzhx` (no Docker
available). Documentation must cover:

- **Public Angular configuration** (`src/environments/environment.development.ts`):
  - `apiBaseUrl`: `http://localhost:5218`
  - `supabaseUrl`: `https://iramzxpjbnldhebykzhx.supabase.co`
  - `supabaseKey`: the project's publishable key (current value:
    `sb_publishable_4JUAkR-M60jBHyXjE5YMyg_3LU-z2xM`; the legacy anon key is also acceptable).
    These values are public and can be re-obtained from Settings → API. They are not secrets.
- **Backend JWT configuration** (committed `appsettings.json` and the
  `appsettings.Development.example.json` template), non-secret:
  ```json
  "Authentication": {
    "Schemes": {
      "Bearer": {
        "MetadataAddress": "https://iramzxpjbnldhebykzhx.supabase.co/auth/v1/.well-known/openid-configuration",
        "TokenValidationParameters": {
          "ValidAudiences": ["authenticated"]
        }
      }
    }
  }
  ```
  `MapInboundClaims = false` and `ValidAlgorithms` are set in code as specified under Backend
  JWT Authentication. No backend secret is required.
- **Test account creation**: register through the Angular register view, or add a user via the
  Supabase dashboard (Authentication → Users → Add user), or create one with a Supabase
  client `signUp` call. The account's UUID (shown in the dashboard) is its `sub`.
- **Verify protected API access manually**:
  1. Run the backend (`dotnet run --project src/backend/GameLibrary.Api`) and the frontend
     (`ng serve` from `src/frontend/`).
  2. Register or log in through the UI.
  3. Navigate to the home route; it renders the result of `GET /auth/me`.
  4. Confirm the displayed `userId` equals the account's UUID shown in the Supabase dashboard.
  5. With no session (signed out), requesting `GET http://localhost:5218/auth/me` with
     `curl` (no `Authorization` header) returns `401`.
- **Docker/local-Supabase alternative** (documented in `README.md` per Feature 001, for
  machines with Docker): if the local Supabase stack is used instead, the Angular
  `supabaseUrl`/`supabaseKey` come from `supabase status` local credentials, the backend
  `MetadataAddress` points at `http://127.0.0.1:54321/auth/v1/.well-known/openid-configuration`
  with `RequireHttpsMetadata = false` for local HTTP, and `ValidAudiences`/issuer semantics
  stay the same. This path is not required for the current environment.

### Testing

Proportionate tests. Do not build a fake authentication architecture that diverges from
production merely for tests, and do not over-test `supabase-js`.

**Backend** — no new backend unit-test project is added in this feature. The authentication
logic is API-boundary behavior with no pure domain/application rules yet; the current-user
mechanism is verified through the API integration tests. (A unit-test project remains deferred
until a feature introduces meaningful non-HTTP domain/application logic.)

Backend integration tests are added to the existing
`tests/backend/GameLibrary.IntegrationTests` project and split into two distinct layers:

1. **Tests of ASP.NET authorization behavior** (an
   `AuthEndpointTests` class using `WebApplicationFactory<Program>`):
   - The test factory post-configures the `Bearer` `JwtBearerOptions` to validate against a
     known symmetric test signing key (disabling metadata discovery, setting a fixed test
     issuer and audience, and the corresponding `ValidAlgorithms`). This is test-only
     configuration in the test project; no test-only branch is added to `Program.cs`. The
     production JWT validation configuration is asserted separately (layer 2).
   - Required cases:
     - `GET /auth/me` without a token returns `401`.
     - `GET /auth/me` with a test-signed token carrying a `sub` returns `200` and
       `body.userId == sub` (verifies the `sub` is read as the authenticated user ID).
     - `GET /auth/me` with a token signed by a different key returns `401`.
     - `GET /auth/me` with an expired token returns `401`.
     - `GET /auth/me` with a valid token lacking a `sub` claim returns `401`.
     - `GET /health` remains `200` without a token (regression).
   - These tests need no database connection; the factory pattern and lazy `DbContext`
     registration make them DB-independent.
2. **Tests of actual JWT validation configuration** (for example a
   `JwtValidationConfigurationTests` class using the unmodified host):
   - Resolve the configured `Bearer` `JwtBearerOptions` and assert that
     `MetadataAddress` points at the Supabase OpenID Connect metadata URL,
     `ValidAudiences` contains `authenticated`, `MapInboundClaims` is `false`, and
     `ValidateIssuer`/`ValidateAudience`/`ValidateLifetime`/`ValidateIssuerSigningKey` are
     enabled. This verifies the production Supabase JWT configuration without a live network
     call.

Existing tests (`HealthEndpointTests`, `DatabaseMigrationTests`) must continue to pass
unchanged.

**Frontend** — Vitest-based tests via `ng test` (the tooling selected by the generated Angular
version). Mock the Supabase client boundary (override the `SUPABASE_CLIENT` token) and the
`HttpClient` as needed. At minimum:

- `AuthService`: `signIn` success sets the authenticated state; `signIn` failure surfaces a
  normalized error and leaves state signed-out; `signOut` clears state; initialization/
  restoration calls `getSession` and syncs state.
- Route guards: `authGuard` redirects an unauthenticated user to `/login` and permits an
  authenticated user; `guestGuard` does the inverse.
- API token interceptor: attaches `Authorization: Bearer` for a request to the API base URL
  when a session token exists; does not attach it for a different origin, when there is no
  session, or when `apiBaseUrl` is empty.
- Login view: successful submit signs in and navigates to home; failed submit shows an error.
- Register view: successful submit signs up per the defined behavior; failure shows an error.
- Home placeholder: calls `GET /auth/me` and displays the returned `userId`; a `401` renders
  the session-expired state.

**Integration / manual verification** — perform the end-to-end manual verification described
under Local Development (register/login → protected home → `GET /auth/me` returns the same
user ID as the Supabase `sub`). This is feature acceptance verification, not a new E2E suite.

## Functional Requirements

`FR-001` — Supabase client dependency and public configuration: `@supabase/supabase-js` is a
frontend dependency; `environment.ts`/`environment.development.ts` expose `supabaseUrl` and
`supabaseKey`; the development values point at the current project's public URL and
publishable key; the default (production) environment uses placeholders/empty values; no secret
value appears in the frontend.

`FR-002` — Supabase client provisioning: a `SUPABASE_CLIENT` injection token creates the
Supabase client from the environment configuration and is registered in the app config,
enabling tests to mock the auth boundary.

`FR-003` — AuthService: exposes authenticated/session state as a signal, the current access
token, `signUp`, `signIn`, `signOut`, and session initialization/restoration via
`getSession()` plus `onAuthStateChange`; errors are normalized to user-visible messages. No
custom token persistence or refresh logic is added.

`FR-004` — Login flow: a `/login` route (guest-only) with an email/password form; successful
login authenticates and navigates to the home route; failed login shows an error and keeps the
user on the form.

`FR-005` — Register flow: a `/register` route (guest-only) with an email/password form;
successful registration authenticates and navigates to home when the session is immediately
usable, or shows an email-confirmation notice when the project requires it; failed registration
shows an error.

`FR-006` — Logout: an explicit logout action in the authenticated home view clears the Supabase
session and redirects to `/login`.

`FR-007` — Session restoration: on application start the Supabase session is restored from
Supabase-managed storage, and auth state stays in sync with `onAuthStateChange` events
(including token refresh). Protected navigation waits for session restoration and never
treats a valid persisted session as guest.

`FR-008` — Route protection: `authGuard` protects the home route (redirect to `/login`);
`guestGuard` protects `login` and `register` (redirect to home when already authenticated).

`FR-009` — Authenticated home placeholder: the `''` route renders a minimal authenticated page
showing the logged-in user's identity and the result of `GET /auth/me`, plus the logout action;
a `401` from `/auth/me` renders a session-expired state with a path back to login.

`FR-010` — Health view retained: the Feature 001 health-check view remains available at the
anonymous `/health` route and continues to work.

`FR-011` — API token interceptor: attaches `Authorization: Bearer <access-token>` to requests
whose URL starts with the non-empty `apiBaseUrl` when a session exists; all other requests pass
through unchanged.

`FR-012` — Backend JWT bearer validation: ASP.NET Core validates Supabase-issued access tokens
using `AddAuthentication(...).AddJwtBearer(...)` with OpenID Connect metadata/JWKS discovery,
audience `authenticated`, `MapInboundClaims = false`, and signature/issuer/audience/lifetime
validation enabled; `AddAuthorization()` and the
`UseAuthentication` → `UseAuthorization` middleware order are applied; no backend signing
secret is configured.

`FR-013` — Current user identity: a small helper reads the authenticated user ID from the
validated JWT `sub` claim (`GetSupabaseUserId()`), returning `null` for unauthenticated
principals or missing `sub`; the mechanism is reusable by future features and never trusts a
client-supplied user ID.

`FR-014` — Protected verification endpoint: `GET /auth/me` requires authentication and returns
`200` with `{ "userId": "<sub>" }`; unauthenticated or `sub`-less requests return `401`; no
unnecessary token details are returned.

## Non-Functional Requirements

`NFR-001` — Security: token validation is standards-based OpenID Connect/JWKS validation with
signature, issuer, audience, and lifetime checks; no custom JWT implementation, no ASP.NET Core
Identity, no custom password storage, and no application-issued tokens. The authenticated
Supabase `sub` is the only trusted identity; client-supplied user IDs are never trusted.

`NFR-002` — Secret handling: no secrets are committed. The backend requires no Supabase secret
(asymmetric JWKS validation). The frontend contains only public configuration. Committed
backend templates contain placeholders for anything environment-specific; the new
`Authentication` settings are non-secret URLs/audience values.

`NFR-003` — Simplicity: no `User` entity/table, no Library, no new backend projects, no
identity framework, no token-persistence wrapper over Supabase, no NgRx, no complex auth state
machine, and no custom token-refresh logic. The approved stack is used directly.

`NFR-004` — User isolation foundation: the validated Supabase `sub` becomes the authoritative
external identity for future backend features; the current-user mechanism is small and
reusable; the backend remains the sole enforcement boundary (no RLS).

`NFR-005` — Maintainability: cross-cutting auth infrastructure lives in `core/auth/`; auth UI
lives in `features/auth/`; error normalization is centralized in the `AuthService`; no
speculative shared abstractions are introduced.

`NFR-006` — Foundation preserved: `GET /health` remains anonymous and functional, the Angular
application builds, and existing backend integration tests continue to pass.

## Acceptance Criteria

`AC-001` — `@supabase/supabase-js` is a frontend dependency; the environment files expose
`supabaseUrl`/`supabaseKey`; development values are the current project's public URL and
publishable key; production uses placeholders; no secret value is present in committed files.

`AC-002` — The API starts and `GET /health` still returns `200` anonymously after the
authentication wiring is added.

`AC-003` — `GET /auth/me` without an `Authorization` header returns `401 Unauthorized`.

`AC-004` — `GET /auth/me` with a valid token (matching configured issuer and audience, signed
by the validating key) returns `200` with `userId` equal to the token's `sub`.

`AC-005` — `GET /auth/me` with a token signed by the wrong key, an expired token, or a valid
token lacking `sub` returns `401 Unauthorized`.

`AC-006` — The production JWT validation configuration is verifiably Supabase's OpenID Connect
metadata/JWKS configuration (metadata address, audience `authenticated`, `MapInboundClaims =
false`, full validation enabled) and is covered by a configuration test.

`AC-007` — Angular login: correct credentials authenticate and navigate to the protected home;
incorrect credentials show an error and remain on `/login`.

`AC-008` — Angular register: successful registration authenticates and navigates to home (or
shows the email-confirmation notice when the project requires confirmation); failure shows an
error.

`AC-009` — Logout clears the session, redirects to `/login`, and the protected home is then
unreachable until sign-in.

`AC-010` — Route guards redirect unauthenticated visitors away from home to `/login` and
redirect authenticated visitors away from `/login`/`/register` to home.

`AC-011` — The interceptor attaches a Bearer token to API-base-URL requests only when a session
exists, and does not attach it to other origins, when signed out, or when `apiBaseUrl` is empty.

`AC-012` — Backend integration tests (authorization behavior and JWT configuration) pass;
frontend tests (`ng test --watch=false`) and lint (`ng lint`) pass; existing backend tests
still pass.

`AC-013` — End-to-end manual verification succeeds: register/login through Angular, reach the
protected home, and confirm `GET /auth/me` returns the same user ID as the authenticated
Supabase account's `sub`.

`AC-014` — No out-of-scope artifacts are introduced: no `User`/`Library` entities or tables, no
new EF Core migration, no ASP.NET Core Identity, no custom JWT issuance, no RLS, and no direct
Angular access to application tables.

## Verification

For each acceptance criterion, an implementation agent verifies as follows.

`AC-001`:
- Inspect `src/frontend/package.json` for `@supabase/supabase-js`.
- Inspect `src/environments/environment.development.ts` and `environment.ts`. Development
  contains `supabaseUrl` = `https://iramzxpjbnldhebykzhx.supabase.co` and `supabaseKey` = the
  publishable key (or legacy anon key); default contains empty/placeholder values.
- Confirm these values are public (Settings → API) and no service-role key, database
  credentials, or other secret appears anywhere in committed files.

`AC-002`:
- Run `dotnet run --project src/backend/GameLibrary.Api`.
- `GET http://localhost:5218/health` returns `200` with the JSON health body and requires no
  token.

`AC-003`:
- Run `curl -i http://localhost:5218/auth/me` (no `Authorization` header). The response status
  is `401` and includes a `WWW-Authenticate: Bearer` challenge.

`AC-004`:
- Using a valid Supabase session, request
  `curl -i -H "Authorization: Bearer <access-token>" http://localhost:5218/auth/me`. The
  response is `200` with a body whose `userId` equals the `sub` of the decoded JWT (the account
  UUID). This is also demonstrated by the integration test layer 1.

`AC-005`:
- Covered by the integration tests: wrong-signature token → `401`, expired token → `401`,
  token without `sub` → `401`. Optionally repeat manually with an expired/tampered token.

`AC-006`:
- The `JwtValidationConfigurationTests` integration test passes: it asserts the configured
  `Bearer` `JwtBearerOptions` `MetadataAddress` is the Supabase OpenID Connect metadata URL,
  `ValidAudiences` contains `authenticated`, `MapInboundClaims` is `false`, and
  `ValidateIssuer`/`ValidateAudience`/`ValidateLifetime`/`ValidateIssuerSigningKey` are true.

`AC-007`:
- From `src/frontend/`, run `ng serve`, open `http://localhost:4200/login`, and sign in with a
  valid test account: the app navigates to home. Sign out, then sign in with an incorrect
  password: an error message appears and the URL remains `/login`.

`AC-008`:
- From the register view, register a new email/password. If the project requires email
  confirmation, the confirmation notice appears and the user is not yet authenticated;
  otherwise the user is authenticated and navigated to home. Register the same email again:
  an error message appears.

`AC-009`:
- From the authenticated home view, click logout: the session is cleared and the app navigates
  to `/login`. Navigating to `http://localhost:4200` redirects to `/login`.

`AC-010`:
- Signed out: visit `http://localhost:4200/` → redirected to `/login`. Signed in: visit
  `/login` or `/register` → redirected to home.

`AC-011`:
- Verified by the interceptor unit tests. Optionally observe in the browser dev tools: while
  signed in, the home page's `GET http://localhost:5218/auth/me` request carries
  `Authorization: Bearer <access-token>`; after sign-out it does not, and the request receives
  `401`.

`AC-012`:
- Run `dotnet test tests/backend/GameLibrary.IntegrationTests` (requires
  `ConnectionStrings:Test` for the migration test; auth tests are DB-independent). All tests
  pass.
- From `src/frontend/`, run `ng test --watch=false` and `ng lint`. Both pass.

`AC-013`:
- Perform the manual end-to-end verification under Local Development: register/login through
  Angular, reach the protected home, and confirm the displayed `userId` from `GET /auth/me`
  equals the account's UUID shown in the Supabase dashboard (Authentication → Users).

`AC-014`:
- Review the repository: no `User`/`Library` entity or table exists; no new migration was added
  (the `InitialCreate` migration is unchanged); no ASP.NET Core Identity packages or code exist;
  no token-issuing endpoint exists; no RLS is introduced; no Supabase client code touches
  application database tables.

## Dependencies

- Frontend: `@supabase/supabase-js` (stable; actual version recorded in `README.md`).
- Backend: `Microsoft.AspNetCore.Authentication.JwtBearer` NuGet package for
  `GameLibrary.Api` (stable, aligned with the selected .NET 10 / EF Core 10 versions; recorded
  in `README.md`). The package brings the `System.IdentityModel.Tokens.*` token libraries
  transitively (also used to mint test tokens in the test project).
- Test tooling: existing xUnit + `Microsoft.AspNetCore.Mvc.Testing` for backend integration
  tests; existing Angular/Vitest tooling for frontend tests. No new test frameworks.
- Public runtime configuration (non-secret): Supabase project URL, publishable/anon key, and
  the Supabase OpenID Connect metadata URL for the current project
  (`iramzxpjbnldhebykzhx`).
- No Docker is required: the current development environment uses the remote Supabase project
  for both PostgreSQL and Auth, matching the repository's committed configuration. The
  Docker/local-Supabase path documented by Feature 001 remains an alternative.

Version-selection policy: stable, supported versions available at implementation time,
consistent with Feature 001. Record actual versions in `README.md`.

## Risks / Notes

- **Remote project is the current dev database/Auth**: the committed `appsettings.json` already
  references `db.iramzxpjbnldhebykzhx.supabase.co`, and the gitignored development config
  supplies the real connection string. This feature uses the same remote project for Auth.
  Docker is not required. If a local Supabase stack is ever used instead, the Angular
  `supabaseUrl`/`supabaseKey`, the backend `MetadataAddress`, and `RequireHttpsMetadata`
  differ (see Local Development); `ValidAudiences` and issuer semantics stay the same.
- **Asymmetric signing keys**: the project signs access tokens with asymmetric keys (currently
  ES256) exposed via JWKS. OpenID Connect metadata discovery handles key rotation through the
  standard metadata refresh (Supabase's edge caches the JWKS for up to ~10 minutes, which can
  briefly delay new-key propagation).
- **Audience value**: user access tokens carry `aud = "authenticated"`. The anon/publishable
  key JWT has a different audience/claims and is not a user session; it must not be sent as a
  bearer token to the API (it would correctly be rejected).
- **Email confirmation**: whether registration yields an immediate session depends on the
  project's "Confirm email" setting. This feature defines behavior for both cases and does not
  reconfigure Supabase. No additional UI flows (password reset, email verification) are built.
- **Test signing strategy**: integration tests for ASP.NET authorization behavior use a
  test-only post-configuration of the bearer options (symmetric test key) so they run
  deterministically offline; the production JWKS/OIDC configuration is asserted separately by a
  configuration test. No test-only branch is added to `Program.cs`.
- **No backend unit-test project**: the current-user mechanism is verified through the API
  integration tests; a unit-test project remains deferred until a feature introduces meaningful
  non-HTTP domain/application logic (consistent with Feature 001's rationale).
- **`/health` is preserved**: the foundation health-check view is retained at the anonymous
  `/health` route; the home route becomes the authenticated placeholder. This keeps the health
  view available while giving the protected-route requirement a concrete page.
- **Committed `appsettings.json`**: it already contains a connection string whose password is a
  placeholder (`[YOUR-PASSWORD]`). This feature only adds the non-secret `Authentication`
  section; it does not alter database configuration.
- **Session refresh is Supabase's responsibility**: supabase-js refreshes access tokens while
  the session is valid; the API interceptor simply uses the current token. No custom refresh or
  persistence logic is introduced.

## Open Questions

None. The identity-only scope, the Supabase OIDC/JWKS validation strategy, the current-user
mechanism, route/guard behavior, the protected `GET /auth/me` endpoint, and the test approach
are all defined here and by the approved Product, Domain, and Architecture Specifications.