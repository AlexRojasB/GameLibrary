# 008 - UX / PWA Polish / E2E

## Status

`Approved`

This specification is approved and ready for implementation. Approval means ready
to implement, not implemented or done.

## Selected Direction

`Play Shelf`

Game Library should feel warm, personal, collection-oriented, playful without
becoming visually noisy, modern, responsive, and suitable for an installed PWA.

The product is a personal collection of VideoGames and BoardGames. The Random
Picker is the central differentiated experience and should answer:

> What should I play?

## Objective

Make the implemented MVP feel like a coherent product rather than a set of
functional CRUD screens, while preserving all approved Product, Domain, and
Architecture behavior from Features 001-007.

After Feature 008, an authenticated user can:

1. Navigate the app through a coherent desktop and mobile/PWA shell.
2. Use a warm Play Shelf visual system consistently across authenticated and auth
   pages.
3. Add and edit VideoGames and BoardGames from immediately accessible dialogs so
   creation is never pushed below long lists.
4. Browse the Library through responsive, scan-friendly collection cards.
5. Use Random Picker as a prominent centerpiece with a highlighted current result
   and readable volatile history.
6. See consistent loading, empty, error, validation, and success feedback.
7. Install/use the app as a polished MVP PWA with honest limited offline behavior.
8. Run a small Playwright E2E suite covering the highest-value MVP journeys.

## Authority

Product Specification -> Domain Specification -> Architecture Specification ->
Feature Specification -> Implementation.

Feature 008 is a UX, visual polish, PWA polish, and E2E verification feature. It
must not change domain semantics, production backend authorization boundaries,
Random Picker domain/session behavior, or the approved application architecture.

## Current Repository Assessment

The current Angular application is functionally complete through Feature 007 but
visually and structurally minimal.

Observed current state:

- `app.html` only renders `<router-outlet />`; there is no global application
  shell.
- Authenticated Home is a plain link list and exposes raw User ID as normal page
  content.
- Page navigation is local and inconsistent: individual pages include simple
  Home/Back links.
- Login and Register are functional plain forms with little product identity.
- VideoGame and BoardGame management pages append the create/edit form after the
  collection list.
- With a long VideoGame or BoardGame list, opening Add/Edit places the form far
  from the page heading and action context.
- Management rows are dense text stacks with weak metadata hierarchy.
- Library already uses cards and placeholders but presents most metadata with
  similar visual weight.
- Library filters and Random Picker filters use dense native controls including
  multi-select boxes.
- Random Picker implements Feature 007 behavior but visually reads like another
  form plus one card; history is a simple ordered text list.
- Loading, empty, error, success, and no-result states exist but are inconsistent.
- `src/styles.scss` has no meaningful global foundation; component SCSS repeats
  small local colors and spacing.
- Angular PWA configuration exists through `@angular/service-worker`,
  `ngsw-config.json`, and `manifest.webmanifest`, but manifest names are generic
  (`game-library`) and no theme/background colors are present.
- No Playwright, Cypress, or E2E suite currently exists.

## In Scope

- A small shared visual foundation using CSS custom properties and limited shared
  pattern classes.
- A coherent authenticated app shell with desktop top app bar and mobile/PWA
  bottom navigation.
- A protected `/manage` route used as the mobile management hub.
- A redesigned authenticated Home page based on Play Shelf.
- Visual polish for Login and Register without changing Supabase Auth behavior.
- Dialog/sheet-based VideoGame and BoardGame Add/Edit workflows.
- Management list visual hierarchy improvements for VideoGames and BoardGames.
- Library card and filter presentation polish while preserving Feature 006
  semantics.
- Random Picker visual polish while preserving Feature 007 backend/session
  semantics.
- Consistent cover image and placeholder presentation.
- Consistent loading, empty, no-result, error, validation, and operation feedback.
- Pragmatic accessibility and responsive behavior improvements.
- PWA manifest/presentation polish and graceful limited offline messaging.
- A small Playwright E2E suite for critical MVP journeys.
- Existing Angular/Vitest tests updated or added where behavior changes.
- Existing backend unit/integration test suites run as regressions; no business
  rules are moved into E2E tests.

## Non-Goals

Feature 008 must not introduce:

- Domain redesign or domain semantic changes.
- Production backend authorization/security model changes. The only authentication
  change allowed by this feature is the explicit local/test E2E authentication mode
  defined under E2E Authentication, and it must never be active in production.
- New backend projects.
- New application persistence, schema changes, or EF Core migrations unless an
  unexpected implementation detail is discovered and independently approved.
- Direct Angular access to application database tables.
- NgRx, Redux-style architecture, Angular Material, Tailwind, Bootstrap, PrimeNG,
  or another UI framework.
- SSR.
- Dark mode or a theme switcher.
- Persistent Random Picker history, `PlaySession`, analytics, AI, recommendations,
  or notifications.
- Image upload, object storage, image resizing, or CDN infrastructure.
- Full offline CRUD, client-side database synchronization, conflict resolution, or
  offline mutation queues.
- Bulk editing/deleting.
- Shared/social libraries or external game-platform integrations.
- Exhaustive E2E automation.
- Production E2E-only authentication, seed, reset, or database-control endpoints or
  mechanisms.

## Visual System - Play Shelf

### Theme

The app is light-first. Feature 008 does not require dark mode.

Use CSS custom properties in `src/styles.scss` for a small token set. Exact final
values are chosen during implementation, but roles must be explicit:

- `--color-bg`: warm off-white application background.
- `--color-surface`: main card/dialog surface.
- `--color-surface-raised`: subtly elevated surface for featured cards.
- `--color-text`: primary readable text.
- `--color-text-muted`: secondary metadata/helper text.
- `--color-border`: soft border color.
- `--color-primary`: playful primary accent used for main actions and Pick.
- `--color-primary-contrast`: text/icon color on primary accent.
- `--color-video`: restrained VideoGame type accent.
- `--color-board`: restrained BoardGame type accent.
- `--color-success`: restrained success/notice color.
- `--color-warning`: restrained warning/offline color.
- `--color-danger`: destructive/error color.
- `--focus-ring`: visible focus outline color/treatment.

Semantic colors must be accessible and restrained. Do not use many saturated
colors simultaneously.

### Typography

Use a system/native font stack unless a font is already available locally. Do not
add external font downloads solely for polish.

Hierarchy:

- Page titles: large, friendly, high contrast, short line length.
- Section titles: medium weight, clear spacing above related content.
- Card titles: strong but compact, with wrapping that does not break card layout.
- Body text: readable line-height, normal weight.
- Metadata: smaller or muted, never competing with the game name.
- Helper/error text: compact, readable, associated with the relevant control or
  state.

### Spacing, Radius, Elevation

Define reusable spacing tokens rather than arbitrary per-component values:

- `--space-1`: 0.25rem.
- `--space-2`: 0.5rem.
- `--space-3`: 0.75rem.
- `--space-4`: 1rem.
- `--space-6`: 1.5rem.
- `--space-8`: 2rem.
- `--space-10`: 2.5rem.

Define radius tokens:

- `--radius-sm`: small controls/chips.
- `--radius-md`: inputs and small cards.
- `--radius-lg`: primary cards/dialogs.
- `--radius-xl`: hero/current-result surfaces.

Define elevation tokens:

- `--shadow-sm`: subtle card lift.
- `--shadow-md`: raised dialog/featured card.
- `--shadow-focus`: focus ring where useful.

Elevation must remain soft. Avoid heavy shadows and nested-card clutter.

### Shared Patterns

Feature 008 may add a small set of global pattern classes if they reduce real
duplication:

- Page container / page header.
- Button variants: primary, secondary, ghost/text, danger.
- Card/surface base.
- Badge/chip base.
- Form field stack.
- Inline alert/notice.
- Empty-state panel.

Keep component-specific layout in component SCSS. Do not build a generic CSS
framework.

### Controls

Consistent treatments are required for:

- Primary button: used for Pick, Create, Save, and the primary home action.
- Secondary button: used for cancel, clear, retry, and non-primary navigation.
- Destructive action: used for Delete and destructive confirmations.
- Text inputs, numeric inputs, selects, and textareas.
- Multi-select/filter controls.
- Chips/badges for type/status/selected filters.
- Disabled and loading states.
- Inline validation and API errors.

Controls must have at least touch-friendly target sizing on mobile and visible
focus states across keyboard navigation.

## Application Shell And Navigation

### Authenticated Shell

Introduce a coherent authenticated shell around protected app pages.

The shell should apply to protected routes:

- Home (`/`).
- Library (`/library`).
- Random Picker (`/random-picker`).
- Manage (`/manage`).
- VideoGames (`/video-games`).
- BoardGames (`/board-games`).
- Platforms (`/platforms`).

Login, Register, and Health do not need the authenticated shell.

Implementation may use a shell component wrapping authenticated child routes or a
small root-level shell that conditionally renders authenticated navigation based on
auth state. Keep route guards authoritative; do not weaken auth behavior.

### Desktop Navigation

Desktop uses a top application bar.

Required destinations:

- Home.
- Library.
- Random Picker.
- VideoGames.
- BoardGames.
- Platforms.

Random Picker must have greater visual emphasis than ordinary management
destinations, for example as a primary `Pick`/`Random Picker` nav button in the
top bar.

Account/logout behavior remains available but must not dominate navigation. Show
the signed-in email when useful. Do not show raw User ID as normal home or shell
content.

### Mobile/PWA Navigation

Mobile must not simply squeeze the desktop top nav into a narrow row.

Use bottom navigation for primary destinations:

- Home.
- Library.
- Pick (Random Picker, visually emphasized).
- Manage.

The mobile bottom navigation contains exactly those four primary destinations. Do
not add VideoGames, BoardGames, Platforms, account, or logout as separate bottom-nav
items.

`Manage` navigates to a protected route:

`/manage`

Do not implement Manage as an ambiguous popup/menu. The `/manage` page is a
lightweight management hub that provides clear navigation cards/actions to:

- VideoGames.
- BoardGames.
- Platforms.

Do not add analytics, collection statistics, or management metrics to `/manage`.

The bottom bar must avoid overcrowding and keep Pick easy to reach. Navigation
must work in standalone installed mode and not rely on hover.

Bottom navigation must expose route-aware active state visually and semantically.
Do not depend only on color for active indication.

Active mapping:

- `/` -> Home active.
- `/library` -> Library active.
- `/random-picker` -> Pick active.
- `/manage` -> Manage active.
- `/video-games` -> Manage active.
- `/board-games` -> Manage active.
- `/platforms` -> Manage active.

Mobile account/logout access belongs to the application shell outside the four
primary bottom-nav destinations. Use a compact account/menu control in the mobile
shell/header. It must provide at minimum Logout. Do not add profile-management
functionality unless it already exists.

Login and Register must not display the authenticated shell or bottom navigation.

### Manage Page

The `/manage` route is protected by the existing auth guard and is primarily for
compact mobile navigation.

Requirements:

- Show a simple Play Shelf management hub with three clear destinations:
  VideoGames, BoardGames, and Platforms.
- Each destination navigates to the existing protected management route:
  `/video-games`, `/board-games`, or `/platforms`.
- Keep it lightweight: no analytics, no fake counts, no management statistics, and
  no new backend read model.
- Desktop may expose `/manage`, but desktop top navigation still directly exposes
  Home, Library, Random Picker, VideoGames, BoardGames, and Platforms.

## Home Page

Replace the current plain authenticated link list with a lightweight Play Shelf
home.

Required behavior:

- Lead with a prominent `What should I play?` panel.
- Provide a primary action to Random Picker.
- Provide secondary cards/links for Library, VideoGames, BoardGames, and
  Platforms/Manage.
- Show signed-in identity in a friendly way when useful, such as email in the
  shell/account area.
- Remove raw User ID from the normal visual experience.
- Keep existing session-expired/error handling semantics.

Do not invent backend analytics, fake counts, fake recent activity, or dashboard
metrics solely to fill the layout.

## Authentication Pages

Polish Login and Register with the Play Shelf identity.

Requirements:

- Centered responsive auth surface with warm background context.
- Clear page title and short helper copy.
- Email/password fields remain labelled and use existing autocomplete behavior.
- Existing Supabase Auth behavior remains unchanged.
- Existing confirmation/error states remain semantically distinct.
- Submit actions show disabled/loading state to prevent duplicate submissions.
- Links between Login and Register remain clear.
- Touch targets and focus states are accessible.

Do not add social providers, new auth flows, password reset flows, or email-flow
assumptions.

## Cover Images And Placeholders

Covers are optional external URLs.

Feature 008 must define one consistent cover treatment:

- Preferred aspect ratio: `2 / 3` for game covers in cards and picker result
  surfaces.
- Library cards may use cover-forward top/left placement depending on breakpoint.
- Management rows may use smaller thumbnails with the same aspect ratio.
- Random Picker current result uses the largest cover treatment.
- Placeholders must be purposeful Play Shelf placeholders, not blank boxes.
- Placeholder should work for both VideoGame and BoardGame and may include subtle
  type styling/text.
- If an image fails to load, the UI must fall back to the same placeholder for the
  current rendering session.

Do not implement uploads, storage, resizing, proxying, or CDN behavior.

## Management Workflows

### Confirmed Problem

The current VideoGame and BoardGame create/edit forms participate in the page/list
flow and render after the list. As collections grow, forms can be pushed far down
the page.

Feature 008 must solve this. Adding a new VideoGame or BoardGame must remain easy
and immediately accessible regardless of collection size.

### Desktop Add/Edit Dialogs

Use an accessible modal/dialog for:

- Add VideoGame.
- Edit VideoGame.
- Add BoardGame.
- Edit BoardGame.

Requirements:

- The management page remains the browse/manage context when no dialog is open.
- Add action is near the page heading/management controls and remains immediately
  visible near the top of the page.
- Create and Edit reuse the same interaction pattern for VideoGames and
  BoardGames.
- Dialog has a clear title, such as `Add video game`, `Edit video game`, `Add
  board game`, or `Edit board game`.
- Dialog has clear Create/Save and Cancel/Close actions.
- Dialog is sized for the relatively long forms.
- Dialog content may scroll internally when form content exceeds available height.
- Background page must not become the primary scrolling context while modal is
  active.
- Background interaction is blocked while modal is active.
- Escape closes the dialog when it is safe to cancel without data loss; if the form
  has unsaved changes, implementation may require an explicit confirmation or keep
  Escape disabled for that state.
- Focus moves into the dialog when it opens and returns to the Add/Edit trigger
  when it closes.
- Validation and server errors remain visible inside the dialog.
- A successful create/update closes the dialog, reloads/updates the list, and
  shows simple confirmation feedback.
- A failed create/update keeps the dialog open.

Prefer the platform-native `<dialog>` element or a small local/shared Angular
dialog component with no external UI framework. If a shared dialog component is
created, keep it narrowly scoped to current real usages.

### Mobile Add/Edit Sheet

On phone-sized screens, the same create/edit interaction becomes a full-screen or
near-full-screen dialog/sheet.

Requirements:

- The form is not squeezed into a small centered desktop modal.
- Title and primary action remain visible near the top or bottom in a predictable
  way.
- Form body can scroll comfortably.
- Cancel/Close is always reachable.
- Touch targets are large enough for phone use.
- Field grouping remains sensible and follows existing domain concepts.

### Management Forms

Do not redesign the underlying fields or domain behavior.

Preserve:

- VideoGame required/optional fields and dynamic Owned/non-Owned behavior.
- Owned VideoGame Platform requirement.
- VideoGame GameStatus/Progress rules, including Completed normalization handled
  by backend/domain.
- VideoGame optional player-count pair.
- BoardGame required player range.
- BoardGame optional duration/interaction/rating/notes/cover fields.
- BoardGame default `InteractionType` behavior from Feature 006 where implemented.
- Existing backend validation as authoritative.

### Management Lists

The collection list remains visible on the management page when no dialog is
active.

Improve hierarchy so users can quickly identify:

- Cover/placeholder thumbnail.
- Name.
- Relevant status/type metadata.
- Rating when present.
- Player count when useful.
- Edit action.
- Delete action.

Do not display every field with equal prominence. Do not introduce bulk editing or
bulk deleting.

Delete behavior remains deliberate and understandable. The existing confirmation
pattern may be polished, but deleting must remain explicit and must not become a
one-click accidental action.

## Library

The unified Library should visually embody the Play Shelf direction.

### Collection Cards

Use responsive collection cards with this hierarchy:

1. Cover/placeholder.
2. Game name.
3. GameType.
4. Important current state.
5. Concise relevant metadata.

VideoGames and BoardGames should feel like members of the same collection.

Required card treatments:

- Use a consistent card surface, radius, and soft elevation.
- Use type chips/badges for VideoGame and BoardGame.
- Use status chips/badges for AcquisitionStatus.
- Use GameStatus compactly when relevant.
- Display rating in a scan-friendly compact form when present.
- Display player-count information naturally for BoardGames and for VideoGames
  when present.
- Display BoardGame duration and InteractionType only when useful and present.
- Display VideoGame Platforms/Genres compactly; avoid long chip clouds that
  dominate the card.
- Keep Notes secondary and avoid rendering long notes as primary card content.
- Keep Edit navigation/action available without making it the visual focus.

Do not give every field a chip. Avoid metadata walls.

### Library Filters

Preserve all Feature 006 search/filter/sort semantics exactly.

Requirements:

- Search remains case-insensitive literal substring matching by Game name.
- General filters remain: game type, acquisition status, minimum rating.
- VideoGame filters remain: Platform, Genre, GameStatus.
- BoardGame filters remain: player count, InteractionType.
- Sort options remain: Name A-Z, Name Z-A, Rating highest first, Rating lowest
  first, Recently added.
- Multiple values within one filter type use OR.
- Different active filter types use AND.
- Missing metadata does not satisfy an active filter requiring it.
- BoardGame player-count filter remains BoardGame-specific in Library.

Improve presentation:

- Desktop may use a compact filter toolbar plus expandable filter panel.
- On mobile, search remains readily accessible and sort remains accessible near the
  Library controls.
- On mobile, advanced filters live in a collapsible filter panel positioned with
  the Library controls. The panel starts collapsed on initial mobile render and
  must not permanently consume large vertical space above the card list.
- Filter changes apply immediately using the existing Feature 006 browse/filter
  behavior. Do not add an Apply/Submit step and do not change backend query
  semantics.
- When one or more filters are active, show a compact active-filter summary outside
  the collapsed panel so it is obvious the collection is filtered. Chips/badges or
  equivalent concise summary text are acceptable. Long multi-value selections may
  be grouped or counted rather than rendering an unusable chip wall.
- Provide `Clear filters` inside the expanded filter panel. When filters are
  active, a clear/reset affordance must also remain readily discoverable from the
  collapsed/summary state.
- The mobile filter disclosure control must expose expanded/collapsed state, be
  keyboard/touch accessible, and have an accessible label.
- Do not introduce a backend generic filter framework.
- Do not add URL/query-string synchronization or saved filters.

## Random Picker

Random Picker should receive the strongest product identity in the application.
It should not look like a CRUD form.

### Picker Layout

Requirements:

- Page heading/copy should directly frame the product question: `What should I
  play?`.
- Provide a prominent picker area with mode selection, relevant filters, a strong
  Pick action, and current result.
- The initial state before the first pick should feel intentional and invite the
  user to pick a game.
- Pick and Another must be visually clear and close to the result context.
- Clear filters remains available but secondary.
- Reset shown history remains explicit and only appears where appropriate.
- Preserve all Feature 007 mode/filter availability and backend request semantics.

### Current Result

The newest successful result is visually dominant.

Use a larger/highlighted Play Shelf card treatment emphasizing:

- Cover/placeholder.
- Game name.
- GameType.
- Rating when present.
- Useful type-specific information.
- Player count when present.
- Notes when useful, without allowing long notes to dominate.

`Another` should be immediately understandable as requesting another eligible
result with the current mode/filters and current shown list.

Avoid excessive animation. A modest reveal/transition is acceptable if it respects
reduced-motion preferences and does not make tests brittle.

### Visible Volatile History

Feature 007 history semantics remain unchanged.

Display history newest first.

Requirements:

- The current/newest result receives the primary/highlighted treatment.
- Previous results appear underneath in compact muted cards.
- Previous result cards remain readable but visually secondary.
- Do not simply apply progressively darker colors indefinitely.
- Use one consistent secondary-history treatment.
- History survives filter changes, mode changes, `NO_CANDIDATES`, and
  `ALL_ALREADY_SHOWN` within the current page session.
- Reset clears both shown IDs and visible history.
- Leaving/reloading starts a fresh volatile session.
- Do not persist history in PostgreSQL, localStorage, sessionStorage, or any other
  persistence mechanism.

### Picker States

The UI must preserve the conceptual distinction between:

- Initial empty state.
- `SUCCESS` current result.
- `NO_CANDIDATES`.
- `ALL_ALREADY_SHOWN`.
- API/client error.
- Loading/picking.

`NO_CANDIDATES` should invite filter changes or clearing filters. It must not
offer or perform silent fallback selection.

`ALL_ALREADY_SHOWN` should explain that every matching game has appeared and offer
explicit reset. It must not silently reset.

### Mobile Random Picker

On phones, filters must not overwhelm the Pick experience.

Requirements:

- Mode selection remains directly visible.
- Mode-specific filters live inside a collapsible filter panel.
- The main Pick action remains outside the collapsed filter panel and prominently
  accessible.
- Pick action and current result remain prominent.
- Current result and visible history remain visually more important than filter
  controls after picking.
- Filter changes continue to use Feature 007 behavior. Do not add an Apply step.
- Changing filters preserves shown history, visible result history, mode/filter
  semantics, and reset semantics exactly as Feature 007 specifies.
- The filter disclosure is keyboard/touch accessible and exposes its state to
  assistive technology.
- History remains visible below current result but does not displace primary
  controls before the user has picked.
- No critical interaction depends on hover.

## Loading, Empty, Error, And Feedback States

### Loading

Create consistent lightweight loading behavior:

- Initial page/data loading should show a visible inline loading state, not a blank
  page.
- Button/action loading should disable the initiating button where duplicate
  submissions would be harmful.
- Keep loading indicators lightweight; do not build a skeleton framework unless a
  small local skeleton is clearly simpler.

### Empty States

Use a consistent Play Shelf empty-state panel with short explanation and next
useful action.

Required distinct empty/no-result states:

- No VideoGames.
- No BoardGames.
- Empty Library.
- Library filters produce no results.
- Random Picker initial empty state.
- Random Picker `NO_CANDIDATES`.
- Random Picker `ALL_ALREADY_SHOWN`.

Do not collapse semantically different states.

### Errors And Validation

Use a consistent inline alert/notice pattern.

Requirements:

- API errors are readable and do not expose stack traces or raw internals.
- Validation errors are associated with relevant fields where practical.
- Form-level errors remain visible inside dialogs.
- `401` handling remains consistent with existing features: clear local session
  and navigate to Login where current implementation does so.
- Offline/unavailable network-backed action errors use the offline/unavailable
  message pattern described under PWA.

Do not introduce global toast infrastructure unless implementation proves a small
shared feedback component is simpler than existing inline notices. If added, it
must be narrowly scoped and not replace accessible inline validation.

### Success Feedback

Successful create/update/delete should provide understandable feedback through:

- Dialog closure plus updated list.
- A small inline confirmation/notice in the management page.
- Or another simple consistent pattern.

Avoid excessive notifications.

## Accessibility Requirements

Feature 008 must include pragmatic accessibility improvements.

Required:

- Semantic buttons for actions and links for navigation.
- Associated form labels for every input/select/textarea.
- Keyboard-accessible navigation and controls.
- Visible focus states for all interactive elements.
- Sufficient contrast for text, controls, chips, and alerts.
- Sensible heading hierarchy per page and dialog.
- Touch-friendly targets on mobile.
- Accessible validation and status messages using appropriate `role`/ARIA where
  practical.
- Dialog focus management.
- Escape-to-close where appropriate.
- Focus restoration to the triggering Add/Edit control after dialog closes.
- Background interaction blocked while modal is active.
- Reduced-motion preference respected for any transition/animation.

Do not claim formal WCAG certification.

## Responsive Behavior

Use a small breakpoint strategy rather than many device-specific layouts.

Recommended breakpoints:

- Phone: below 40rem.
- Tablet: 40rem to below 64rem.
- Desktop: 64rem and above.

Expected behavior:

- Desktop uses top app bar; mobile uses bottom navigation.
- Page content has comfortable max widths on desktop and safe side padding on
  phone.
- Library cards use a responsive grid on tablet/desktop and single-column or
  compact cards on phone.
- Management Add/Edit dialogs are centered/raised on desktop and full-screen or
  near-full-screen sheets on phone.
- Library mobile filters use the concrete collapsible-panel behavior defined in
  Library Filters.
- Random Picker mobile filters use the concrete collapsible-panel behavior defined
  in Mobile Random Picker.
- Random Picker keeps Pick/current result prominent at all widths.
- Auth pages remain centered and usable on phone.
- No critical interaction depends solely on hover.

## PWA Polish

The app already uses Angular service worker and a web manifest. Feature 008
polishes MVP install/presentation without adding offline synchronization.

Requirements:

- Manifest `name` should be meaningful, for example `Game Library`.
- Manifest `short_name` should be meaningful and concise, for example `Games` or
  `Game Library` if supported by install surfaces.
- Add `theme_color` aligned with the Play Shelf primary/shell color.
- Add `background_color` aligned with the warm app background.
- Preserve `display: standalone`, `scope`, and `start_url` unless implementation
  discovers a concrete issue.
- Verify all referenced icons exist and load.
- If current icons are generic generated assets, replace them with a lightweight
  app-appropriate icon set only if this can be done without large assets or a new
  pipeline.
- Ensure `index.html` title and metadata align with the app name.
- Maintain mobile viewport correctness.
- Keep Angular service-worker static/application-shell caching.
- Do not add API data caching that could misrepresent authenticated data or write
  state.

### Offline / Unavailable Network Behavior

The app requires authenticated backend access for data operations.

Feature 008 must provide graceful limited offline/unavailable behavior:

- If the app shell loads from cache but network-backed data cannot load, show a
  clear unavailable/offline state and a retry action where applicable.
- Mutations must not pretend to succeed while offline.
- Do not queue writes.
- Do not add client-side sync.
- Existing static app-shell caching may continue through Angular service worker.
- The UI may use `navigator.onLine` as a hint, but must still handle failed HTTP
  requests because online status is not authoritative.

## E2E Decision

No E2E framework currently exists. Feature 008 should introduce one small E2E
suite.

Selected framework: `Playwright`.

Rationale:

- It is appropriate for small cross-browser smoke/user journey coverage.
- It does not require adding a UI component framework.
- It can run against the Angular frontend, ASP.NET Core API, and PostgreSQL test
  database without production architecture changes.
- Do not introduce Cypress in the same feature.

## E2E Authentication

Normal real-world authentication remains Supabase Auth:

- Angular uses `supabase-js` for production/development login, logout, session
  restoration, and access-token handling.
- The API validates Supabase-issued JWTs and derives the authenticated user from
  the validated token's `sub` claim.
- Production and normal development authentication behavior must remain unchanged.

Playwright must not depend on an uncontrolled remote Supabase user, shared mutable
credentials, manual login setup, email confirmation, or a hidden production auth
bypass. Feature 008 therefore defines a deterministic test-only E2E authentication
mode.

### Backend E2E Auth Mode

When the API is run in an explicitly designated E2E/test environment for
Playwright, it uses a deterministic test authentication scheme equivalent in spirit
to the existing integration-test JWT configuration (`TestTokens` plus test-time
bearer validation).

Requirements:

- Activation requires explicit server-side E2E/test configuration or environment.
- Production and normal development continue using Supabase JWT validation.
- E2E authentication must never be enabled merely because a request sends a special
  header, token, or magic value.
- The authenticated principal contains a deterministic Supabase-style `sub` user
  identity usable by the existing ownership and Library scoping logic.
- After authentication succeeds, authorization and user-isolation behavior follows
  the normal application path. Controllers/services still derive ownership only
  from the authenticated principal and never from client-supplied user IDs.
- Prefer reusing the existing integration-test authentication concepts/code where
  practical rather than inventing a second independent auth architecture.
- Do not introduce ASP.NET Core Identity and do not replace Supabase Auth for normal
  application behavior.

### Frontend E2E Auth Mode

The Playwright browser needs deterministic authenticated application state without
calling remote Supabase Auth.

Requirements:

- An E2E-specific frontend environment/configuration enables deterministic test
  login/session bootstrap.
- That configuration is included/used only for E2E execution.
- Normal production and development builds continue using Supabase
  `signInWithPassword` through the existing `AuthService` behavior.
- Browser API requests still pass through the normal auth interceptor and carry a
  bearer token to the API.
- The test mode must not leak into production bundles/configuration in an enabled
  state.
- The implementation may extend/reuse existing auth abstractions, but must not
  bypass route guards or make protected routes public.

The Playwright authentication journey verifies the application's login flow within
this deterministic E2E auth environment. Logout must still clear frontend
auth/session state, return the app to unauthenticated behavior, and keep protected
routes inaccessible afterward.

## E2E Scope

Keep E2E intentionally small. Use semantic locators and user interactions, not
styling snapshots.

### Authentication Journey

Cover:

- Login in deterministic E2E auth mode.
- Authenticated shell appears.
- Logout returns user to unauthenticated flow.
- Protected routes are inaccessible after logout.

Registration may be covered only if the test environment can safely create users
without brittle external email dependencies. Do not make the entire suite depend
on uncontrolled email confirmation flows.

### VideoGame Management Journey

Cover:

- Navigate through shell to VideoGames.
- Open Add VideoGame dialog.
- Create an Owned VideoGame with required Platform.
- Verify it appears in the management list.
- Open Edit dialog.
- Update at least the name or one meaningful metadata field.
- Verify update appears.
- Verify cancel/close behavior does not submit changes.

### BoardGame Management Journey

Cover at minimum:

- Navigate to BoardGames.
- Open Add BoardGame dialog.
- Create a BoardGame with required player range.
- Verify it appears.

Do not duplicate every VideoGame CRUD assertion for BoardGames.

### Library Journey

Cover:

- Navigate to Library.
- Verify created VideoGame and BoardGame content can be browsed.
- Verify at least one important search/filter interaction works.
- Verify clearing filters restores expected content.

### Random Picker Journey

Use controlled seeded/test data so randomness is not brittle.

Cover:

- Navigate to Random Picker.
- Pick.
- Successful current result appears with highlighted treatment.
- Another.
- Previous result remains in visible history.
- Newest-first ordering.
- Reset shown history clears visible history and shown IDs for subsequent request.

Do not assert a specific random candidate when multiple candidates are valid unless
the fixture deliberately makes only one candidate eligible for that step.

## E2E Infrastructure

E2E setup is development/test-only and must not change production architecture.

No E2E-only authentication, seed, reset, or database-control endpoint/mechanism may
be active in production. Prefer test process/setup code over HTTP endpoints. If an
HTTP test helper is unavoidable, it must only be registered under explicit E2E/test
server configuration, must not exist in production, and must not rely only on
obscurity or a secret header.

Requirements:

- Add Playwright as a dev dependency only.
- Add scripts such as `e2e` / `e2e:headed` if useful.
- Credentials and secrets must not be committed.
- Test configuration may rely on environment variables and documented local setup.
- Do not create an entirely separate infrastructure ecosystem if existing backend
  integration-test infrastructure can support E2E setup.

### E2E Runtime Environment

Playwright runs against separately running services:

1. PostgreSQL test database.
2. ASP.NET Core API process.
3. Angular frontend dev/test server.
4. Playwright browser runner.

Do not use `WebApplicationFactory` as the actual browser-facing API host. The API
under test must be a real running process. Existing `WebApplicationFactory` auth and
database patterns may inform reusable test setup code, but Playwright interacts with
the running API through HTTP.

### E2E PostgreSQL

Reuse the project's existing real PostgreSQL/Docker testing approach where
practical. The E2E database must be:

- Test-only.
- Isolated from production data.
- Isolated from normal development data where practical.
- Disposable/resettable.

Do not use EF InMemory or SQLite for E2E. E2E must exercise real PostgreSQL
semantics and EF Core migrations.

### E2E Configuration

Document repository commands and logical environment/configuration values for at
least:

- E2E database connection string, such as a dedicated equivalent of
  `ConnectionStrings__Test` or an explicitly documented E2E variant.
- API base URL used by the frontend and Playwright.
- Frontend base URL used by Playwright.
- Explicit E2E authentication enabled flag/environment for the API.
- Deterministic test-user identity/configuration, including the Supabase-style
  `sub` value used for ownership.
- E2E frontend environment/configuration that enables deterministic test login.
- E2E frontend origin allowed by the API CORS policy under test configuration.

Do not commit credentials. Use repository/environment conventions rather than
inventing secret files unnecessarily. Actual port numbers may follow existing
development conventions, but E2E scripts/configuration must use stable known URLs.

### E2E Database Lifecycle

Before Playwright test execution:

1. Ensure PostgreSQL is available.
2. Ensure the E2E database is reachable.
3. Apply current EF Core migrations.
4. Reset/clean application test data.
5. Seed deterministic E2E fixtures.

After or between tests, restore deterministic state using the smallest reliable
cleanup/reset mechanism. Tests must not depend on execution order. Avoid rebuilding
the entire PostgreSQL container for every individual browser test unless the
existing infrastructure makes that inexpensive and simple.

### E2E Seed Data

Create deterministic fixtures appropriate to the critical journeys. The seed must
include enough known state for:

- Authenticated deterministic test user.
- Platforms where needed.
- VideoGame workflow.
- BoardGame workflow.
- Library browse/search/filter.
- Random Picker deterministic candidate scenarios.

Random Picker fixtures must avoid brittle random assertions. Candidate sets may
intentionally leave only one eligible unshown result at specific points. Do not seed
through production-only shortcuts. Use application/database test infrastructure
appropriate to the repository.

### E2E CORS

The E2E API must explicitly allow the configured local E2E frontend origin under
test configuration. Do not broaden production CORS merely to make E2E pass.

### E2E Startup And Cleanup

The implementation must provide documented repository scripts/commands that can:

- Prepare/start PostgreSQL if necessary.
- Configure/start the API in explicit E2E/test mode.
- Configure/start the Angular frontend with E2E frontend configuration.
- Run Playwright.

Prefer a small repository script or Playwright `webServer` orchestration where
practical. Do not require developers to remember several undocumented terminal
steps. The implementation agent may choose exact script names based on current
repository conventions.

E2E execution must not leave shared mutable data that causes the next run to fail.
Do not create elaborate database snapshot infrastructure unless necessary.

## Existing Tests Remain Authoritative

Feature 008 does not replace:

- Backend Core tests.
- Backend PostgreSQL integration tests.
- Angular/Vitest unit and component tests.
- Lint and build checks.

E2E complements these tests. Do not move business-rule verification into browser
tests.

## Architecture Guardrails

- Keep Angular standalone components, services, signals/local state.
- Small reusable presentational components are acceptable when repetition exists
  across current screens.
- Avoid abstracting every card/input/button for theoretical reuse.
- Keep `core/` and `shared/` small.
- Do not introduce NgRx, Angular Material, Tailwind, Bootstrap, PrimeNG, SSR, new
  backend projects, MediatR/CQRS, generic repositories, new persistence, new
  production auth, or generic component frameworks.
- The explicit E2E/test authentication mode is test infrastructure only and is not
  new production application authentication architecture.

## Acceptance Criteria

### Shell / Navigation

1. Protected pages render inside a coherent authenticated shell.
2. Desktop navigation uses a top app bar with Home, Library, Random Picker,
   VideoGames, BoardGames, and Platforms reachable.
3. Random Picker receives stronger visual emphasis than ordinary management links.
4. Account/logout remains accessible without dominating navigation.
5. Raw authenticated User ID is not shown as normal Home/shell content.
6. Mobile/PWA navigation uses a bottom navigation pattern with Home, Library, Pick,
   and Manage as the exact primary destinations.
7. `/manage` is a protected lightweight management hub with clear destinations to
   VideoGames, BoardGames, and Platforms.
8. Mobile management destinations remain accessible without overcrowding the
   bottom bar.
9. Bottom navigation exposes route-aware active state for `/`, `/library`,
   `/random-picker`, `/manage`, `/video-games`, `/board-games`, and `/platforms`.
10. `/video-games`, `/board-games`, and `/platforms` keep Manage active in mobile
    bottom navigation.
11. Active navigation state is exposed visually and semantically and does not depend
    only on color.
12. Mobile account/logout is available from the shell/header outside the four
    bottom-nav destinations.
13. Login and Register do not show the authenticated shell or bottom navigation.

### Visual Foundation

14. `src/styles.scss` or equivalent global styling defines the Play Shelf tokens for
   colors, spacing, radius, focus, and elevation.
15. Buttons, inputs, selects, textareas, chips/badges, cards, alerts, and empty
   states have consistent visible treatments.
16. The app uses a light-first warm background and distinct card/surface styling.
17. No new UI framework is introduced.
18. Typography hierarchy distinguishes page titles, section titles, card titles,
   body, metadata, and helper/error text.

### Home / Auth

19. Home has a prominent `What should I play?` entry point to Random Picker.
20. Home provides secondary access to Library and management areas without fake
   analytics.
21. Login and Register visually match the Play Shelf identity while preserving
   Supabase Auth behavior.

### Management

22. Add VideoGame and Add BoardGame actions are immediately accessible near page
   headings/management controls.
23. Add/Edit VideoGame and Add/Edit BoardGame use accessible desktop dialogs.
24. On mobile, Add/Edit forms use full-screen or near-full-screen dialog/sheet
   treatment.
25. Create/edit forms are no longer pushed below collection lists.
26. Dialogs have clear titles, Create/Save, Cancel/Close, visible validation, and
   internal scrolling when needed.
27. Dialog focus moves in on open and returns to the trigger on close.
28. Background interaction is blocked while dialogs are active.
29. Successful create/update closes the dialog and updates/reloads the list.
30. Failed create/update keeps the dialog open and shows the error.
31. Management lists show improved hierarchy for cover/placeholder, name,
   important metadata, Edit, and Delete.
32. Delete remains explicit and deliberate.

### Library

33. Library uses responsive Play Shelf collection cards.
34. Card hierarchy prioritizes cover/placeholder, game name, game type, important
   state, and concise metadata.
35. VideoGames and BoardGames feel visually related while retaining type-specific
   information.
36. Rating and player-count information are scannable when present.
37. Library filters are grouped and easier to scan than the current wall of native
   controls.
38. On mobile, search and sort remain accessible while advanced filters start in a
    collapsible panel positioned with the Library controls.
39. Mobile Library filter changes apply immediately; no Apply/Submit step is added.
40. Active Library filters show a compact summary outside the collapsed panel.
41. `Clear filters` is available inside the expanded panel and remains readily
    discoverable from the collapsed/summary state when filters are active.
42. The mobile filter disclosure exposes expanded/collapsed state, keyboard
    interaction, and an accessible label.
43. Feature 006 search/filter/sort semantics remain unchanged.

### Covers

44. Cover images use a consistent aspect ratio/presentation.
45. Null covers render purposeful placeholders.
46. Failed cover image loads fall back to the placeholder.
47. No upload/storage/image-processing behavior is added.

### Random Picker

48. Random Picker page visually centers the `What should I play?` experience.
49. Initial Random Picker state invites the user to pick a game.
50. Pick action is visually prominent.
51. Current/newest successful result uses the strongest result-card emphasis.
52. Another action is immediately understandable and available after a current
   result.
53. Previous visible history is shown newest first in compact muted cards.
54. History persists through filter changes, mode changes, `NO_CANDIDATES`, and
   `ALL_ALREADY_SHOWN` within the page session.
55. Reset clears shown IDs and visible history only after explicit action.
56. No Random Picker history is persisted.
57. `NO_CANDIDATES` and `ALL_ALREADY_SHOWN` remain distinct visual states.
58. Random Picker filters remain semantically identical to Feature 007.
59. On mobile, mode selection remains directly visible, mode-specific filters live
    inside a collapsible panel, and the main Pick action remains outside the panel.
60. Mobile Random Picker result/history presentation remains visually more important
    than filter controls after picking.
61. The mobile Random Picker filter disclosure is keyboard/touch accessible.

### States / Feedback / Accessibility

62. Initial loading, action loading, empty states, no-results, API errors,
   validation errors, and success feedback follow consistent visual patterns.
63. Empty Library, no filtered Library results, no VideoGames, no BoardGames,
   Random Picker initial, `NO_CANDIDATES`, and `ALL_ALREADY_SHOWN` remain distinct.
64. Keyboard navigation works for shell, dialogs, filters, forms, and cards.
65. Focus states are visible.
66. Form controls have associated labels.
67. Dialog accessibility requirements are satisfied.
68. Touch targets are usable on phones.
69. Reduced-motion preference is respected for any transitions.

### PWA

70. Manifest app name and short name are meaningful.
71. Manifest theme/background colors align with Play Shelf.
72. Existing icon references are valid; default/generated icons are replaced only if
   implementation can do so simply and safely.
73. Standalone installed-app presentation remains supported.
74. Service worker static/app-shell caching remains in place.
75. Offline/unavailable network-backed actions show honest failure/unavailable
   feedback and do not pretend writes succeeded.
76. No offline CRUD, sync, or mutation queue is added.

### E2E / Regression

77. Playwright is added as the only E2E framework.
78. E2E covers login in deterministic E2E auth mode, authenticated shell, logout,
    and protected routes inaccessible afterward.
79. Production and normal development continue to use Supabase authentication.
80. E2E test authentication cannot be enabled without explicit test-only server
    configuration/environment.
81. No production authentication bypass is introduced.
82. The deterministic E2E user identity uses a Supabase-style `sub` and still
    exercises normal ownership/user-isolation logic.
83. E2E runs against separately running PostgreSQL test database, ASP.NET Core API,
    Angular frontend, and Playwright browser runner.
84. E2E does not use `WebApplicationFactory` as the browser-facing API host.
85. E2E database setup uses real PostgreSQL, applies EF Core migrations, resets
    application test data, and seeds deterministic fixtures before tests.
86. E2E configuration documents database connection, API base URL, frontend base
    URL, E2E auth enabled flag/environment, deterministic test-user identity, and
    E2E CORS origin without committing credentials.
87. E2E startup is available through documented repository commands/scripts or
    Playwright orchestration and does not require undocumented manual terminal
    steps.
88. E2E cleanup/reset prevents shared mutable data from causing later runs to fail.
89. A developer with repository checkout, supported .NET/Node tooling,
    Docker/PostgreSQL capability, and documented local environment values can run
    E2E without manual database editing, production credentials, a mutable shared
    Supabase test user, or undocumented setup.
90. E2E covers VideoGame Add dialog, create, verify, edit, verify, and cancel/close
   behavior.
91. E2E covers BoardGame creation through the new dialog interaction.
92. E2E covers Library browse plus at least one search/filter interaction.
93. E2E covers Random Picker Pick, Another, visible history newest-first, and Reset.
94. E2E uses deterministic setup so randomness does not make tests brittle.
95. No E2E-only authentication, seed, reset, or database-control endpoint/mechanism
    is active in production.
96. Existing backend Core tests, backend PostgreSQL integration tests,
   Angular/Vitest tests, lint, and production build still pass.
97. No domain-semantic regressions are introduced.

## Testing Expectations

Implementation must run, at minimum:

```bash
dotnet test tests/backend/GameLibrary.Core.Tests
dotnet test tests/backend/GameLibrary.IntegrationTests
dotnet build src/backend/GameLibrary.sln
```

```bash
cd src/frontend
npm test -- --watch=false
npm run lint
npm run build
npm run e2e
```

If the final Playwright script name differs, document and run the equivalent E2E
command. The E2E command must either orchestrate or clearly invoke the documented
E2E preparation/startup steps for PostgreSQL, EF Core migrations, API E2E/test
configuration, Angular E2E frontend configuration, deterministic seed/reset, and
Playwright execution.

Frontend tests should use semantic DOM and interaction assertions. Do not add
meaningless styling snapshots.

## Recommended Implementation Sequence

Implement in coherent reviewable increments:

1. Shared Play Shelf tokens/foundation in global styles and any minimal shared
   pattern classes.
2. Authenticated app shell, `/manage` hub, and desktop/mobile navigation.
3. Home and auth page polish using the new foundation.
4. Shared accessible dialog/sheet behavior needed by management forms.
5. VideoGame and BoardGame management dialog workflows and management list polish.
6. Library card/filter polish.
7. Random Picker centerpiece/result/history polish.
8. Loading, empty, error, success, offline/unavailable, and cover-placeholder
   consistency pass across pages.
9. PWA manifest/icon/theme polish.
10. Playwright E2E setup, deterministic test-only auth, reproducible runtime
    environment, and critical journeys.
11. Full regression test/build/lint/E2E run plus manual responsive/accessibility
   verification.

Do not turn this into an uncontrolled CSS rewrite. Keep changes scoped to Feature
008 and preserve previous feature behavior.

## Risks / Notes

- Dialog accessibility is the highest interaction-risk area; focus management and
  mobile sheet behavior must be verified manually and by tests where practical.
- Random Picker tests must not assert deterministic random output unless fixtures
  intentionally leave one eligible candidate.
- Mobile bottom navigation must not hide access to management pages; the Manage
  destination must be obvious.
- Optional and broken cover images require strong placeholder behavior to keep the
  Play Shelf design attractive.
- E2E infrastructure must avoid committed secrets and avoid dependence on mutable
  shared user data.
- E2E-only authentication, seed, reset, and database-control mechanisms must never
  be active in production.

## Implementation Status

No Feature 008 implementation code has been written.
