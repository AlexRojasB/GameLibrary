# Frontend Dependency Security Review

Updated: 2026-11-02

## Disposition

The reported Angular Router and Zod findings are removed from the current locked
frontend dependency graph. Production-only audit now reports zero vulnerabilities.
This is not a clean full audit or approval to release Feature 013: development-tool
advisories and the existing physical-device/G03/G06 release requirements remain.
After the dependency-review handoff, the full browser suite passed **22/22**;
current backend/whole-API and gate evidence is in the
[G06 follow-up](g06-operational-verification.md).

Reviewed AGENTS.md, the approved Product/Domain/Architecture specifications,
Feature 013 and its phase-2 implementation report. Changes are limited to
`src/frontend/package.json`, its generated `package-lock.json`, and this report.
The pre-existing pinned cropper change and all other staged/unstaged/untracked
work were preserved. No application source, test, budget, backend, migration,
Supabase resource or production-upload setting was edited.

## Versions And Selection

| Package(s) | Initial manifest / installed lock | Result |
| --- | --- | --- |
| `@angular/common`, `compiler`, `core`, `forms`, `platform-browser`, `router`, `service-worker` | Each `^22.1.0` / **22.1.2** | Each pinned and locked to **22.2.1** |
| `@angular/compiler-cli` | `^22.1.0` / **22.1.2** | Pinned and locked to **22.2.1** |
| Zod, transitive | **4.4.3** | One deduplicated **4.6.6**, enforced by `overrides.zod` |
| `@angular/build`, `@angular/cli` | Each `^22.1.4` / **22.1.4** | Unchanged |
| `@supabase/supabase-js` and its five installed `@supabase/*` dependencies | **2.112.3** | Unchanged |
| `ngx-image-cropper` | **9.1.7**, exact | Preserved unchanged |
| TypeScript / RxJS / tslib / Vitest | **6.0.3 / 7.8.2 / 2.8.1 / 4.1.10** | Unchanged |

These are exact security-fix versions within the existing Angular 22 and Zod 4
majors, not a claim that 22.1.2 -> 22.2.1 or 4.4.3 -> 4.6.6 is a semver patch-only
change. The fixes require minor-version movement: no published Angular 22.1 release
is outside the advisory ranges, and no Zod 4.4 patch satisfies `<4.6.6`. The selected
Angular version is already admitted by every original `^22.1.0` range. 22.2.1 is the
minimum release fixing all three Router advisories; newer 22.2 patches were not
pulled into this narrow update.

Router, Common, Forms, Platform Browser and Service Worker require matching
framework peers. Compiler CLI requires its matching Compiler. All eight framework
packages were therefore aligned, rather than updating Router alone. The unchanged
22.1.4 builder admits these peers via `^22.0.0`; its TypeScript range and Compiler
CLI's range are both `>=6.0 <6.1`. Node **24.15.0** and npm **11.12.1** were used.
Node satisfies Angular's `^22.22.3 || ^24.15.0 || >=26.0.0` requirement. The pinned
cropper's Common/Core peers are `>=17.3.0` and remain satisfied.

The Zod override is necessary while retaining CLI 22.1.4: CLI pins `zod: "4.4.3"`
exactly. A lock-only update without an override could leave that vulnerable copy
nested under CLI. Forms accepts `^4.0.10`, MCP accepts `^3.25 || ^4.0`, and
zod-to-json-schema accepts `^3.25.28 || ^4`; these accept 4.6.6. Only CLI's exact
pin is intentionally overridden. No Zod 3 consumer is replaced, and no new direct
runtime dependency was added. Reassess/remove the override when a separately
reviewed CLI update no longer pins a vulnerable version.

Relative to the dirty starting lockfile, exactly nine package versions changed:
the eight framework packages above and Zod. No unrelated locked package or package
set was updated; the existing cropper entry remains intact.

## Advisories And Applicability

Audit counts below count vulnerable package records, not unique advisories: Router
was one high package record with three separate advisories.

| Advisory | Angular 22 / Zod affected range | First fixed version | Actual application exposure |
| --- | --- | --- | --- |
| [GHSA-ff3f-86qr-9cv3](https://github.com/angular/angular/security/advisories/GHSA-ff3f-86qr-9cv3), CVE-2026-101896: numeric URL matrix parameters amplify V8 allocation | Router `>=22.0.0 <22.2.0` | **22.2.0** | Requires on-demand Node/V8 SSR parsing attacker request URLs. Current CSR deployment does not have that server boundary. |
| [GHSA-62vg-58rm-qff7](https://github.com/angular/angular/security/advisories/GHSA-62vg-58rm-qff7), CVE-2026-111752: unmatched empty-path outlets amplify matching/guards/resolvers | Router `>=22.0.0 <22.2.1` | **22.2.1** | Requires SSR and susceptible route configuration. The app has an empty-path route, but no SSR runtime. |
| [GHSA-57xq-rjx2-v5xh](https://github.com/angular/angular/security/advisories/GHSA-57xq-rjx2-v5xh), CVE-2026-111751: RouterLink retains copied query-parameter UrlTrees | Router `>=22.0.0 <22.2.1` | **22.2.1** | Requires SSR rendering links with merged/preserved query parameters. Current browser-only imperative Steam navigation using `merge` is not that SSR/RouterLink path. |
| [GHSA-mrhx-c7xc-m3w5](https://github.com/advisories/GHSA-mrhx-c7xc-m3w5), CVE-2023-54404: oversized constrained collections cause excessive validation work/memory | Zod `<4.6.6` | **4.6.6** | Installed in production through Forms and in development through CLI/MCP. No application Zod validation entry point was identified. |

### Router Runtime

`angular.json` uses `@angular/build:application` with `browser: src/main.ts`, and
has no server entry point, SSR or prerender configuration. `src/main.ts` calls
`bootstrapApplication` from Platform Browser; `app.config.ts` installs
`provideRouter(routes)` and the ordinary browser service worker. Neither
`@angular/ssr` nor `@angular/platform-server` is installed. The approved architecture
specifies static Angular PWA hosting and a separate ASP.NET Core API, not Node SSR.

The Angular maintainers explicitly state that pure client-side SPAs are unaffected
by these SSR advisories because local browser resource use does not cross the SSR
service security boundary. This is an applicability finding, not a claim that URL
processing can never consume browser resources. The dependency update removes the
vulnerable versions anyway; any future SSR proposal requires a new exposure review.

### Zod Paths And Supabase

Initial and final `npm ls` / `npm explain zod` establish these paths:

```text
game-library -> @angular/forms -> zod
game-library -> @angular/cli -> zod
game-library -> @angular/cli -> @modelcontextprotocol/sdk -> zod
game-library -> @angular/cli -> @modelcontextprotocol/sdk
             -> zod-to-json-schema -> zod (peer)
```

All paths initially deduplicated to 4.4.3 and now deduplicate to 4.6.6. The direct
Forms dependency explains why Zod appeared in `npm audit --omit=dev`; this was not
merely a dev-only CLI finding. Application source uses ReactiveFormsModule,
FormControl/FormGroup and Angular Validators, with no `zod` imports, Signal Forms
imports or Standard Schema validation calls. CLI's MCP tool implementations do
use Zod schemas, so the override also affects real developer tooling. CLI MCP is
not part of the deployed PWA or ASP.NET API; broader MCP security is not cleared
by this Zod fix.

Supabase is **not** an installed dependency path to Zod. The aggregate SDK's five
runtime dependencies are Auth, PostgREST, Functions, Realtime and Storage JS, all
2.112.3; their installed runtime manifests do not depend on Zod. PostgREST's own
published `devDependencies` mention Zod 3, but dependency packages' development
dependencies are not installed for this application and are not an application
runtime path. The application uses the aggregate client for Auth/session methods,
not Zod validation or direct application-table/managed-Storage operations. No
Supabase upgrade or remote Supabase call was made.

The reviewed GitHub advisory description discusses unbounded arrays, while the
[maintainer fix](https://github.com/colinhacks/zod/pull/6652) specifically performs
leading upper-bound checks before walking arrays, maps and sets. Its explicit
limitation is that **unbounded collections remain unchanged**. Therefore 4.6.6 is
the registry/advisory fix floor, not a promise that arbitrary unbounded schemas
are resource-safe. A future server-side Zod use still needs input/body/collection
bounds. Current application code exposes no such schema-processing endpoint.

## Audit Results

Fresh npm registry audits before and after this update:

| Scope | Before | After |
| --- | --- | --- |
| `npm audit --omit=dev --json` | **2 high**, zero moderate/critical | **0 total** |
| Full `npm audit --json` | **19 total: 3 critical, 10 high, 6 moderate** | **17 total: 3 critical, 8 high, 6 moderate** |

The remaining full-audit package records are outside this targeted update:

Final 2026-11-02 registry recheck: production audit is still **0**. Full audit now
reports **35 records: 3 critical, 8 high, 24 moderate**, not the earlier 17-record
snapshot in the table above. Eighteen additional moderate records involve the
ESLint/angular-eslint/typescript-eslint/minimatch graph. No lockfile changed during
these audit rechecks; advisory metadata can evolve independently. The earlier
before/after snapshot is retained as historical evidence, not the final current
audit count. Review the entire current development-tool graph separately.

| Highest severity | Remaining package records |
| --- | --- |
| Critical (3) | `@angular/build` (via Piscina/Vite), `piscina`, `proxy-addr` |
| High (8) | `@angular/cli` (via MCP SDK), `@modelcontextprotocol/sdk`, `body-parser`, `brace-expansion`, `fast-uri`, `source-map-js`, `undici`, `vite` |
| Moderate (6) | `@hono/node-server`, `@vitest/mocker`, `hono`, `ip-address`, `qs`, `vitest` |

The full audit remains nonzero and requires a separate development-tool dependency
and exposure review. In particular, do not expose dev servers/MCP tooling to
untrusted networks or treat the production-only audit as clearing build-time risk.
No `npm audit fix`, bulk update, Angular major upgrade, framework migration,
additional tooling override or change to the cropper was used to hide those findings.

## Installation And Verification

The first normal installs, including an explicit coordinated package list and
lock-only attempt, failed with ERESOLVE referring to the old locked 22.1.2 peer
set. A candidate lock was generated with
`npm install --package-lock-only --ignore-scripts --no-audit --no-fund --force`.
The force flag was confined to resolution of the candidate lock, not used as
evidence of compatibility. No legacy-peer-deps flag or persistent npm configuration
was introduced.

A subsequent **normal, non-forced** `npm ci --ignore-scripts --no-audit --no-fund`
passed using the approved temporary npm cache. The default user cache first failed
with EACCES; its permissions/content were not altered. Cache used:
`/var/folders/fd/h4rhj_wn5_71qy5q79xm8r_m0000gn/T/opencode/npm-cache`.
The reviewed lock includes registry URLs and SHA512 integrity for every changed
package. No lifecycle install scripts were needed for the successful builds/tests.

| Fresh check | Result |
| --- | --- |
| Normal clean `npm ci`, flags/cache above | **Passed**, 503 packages installed |
| Targeted `npm ls` for all Angular framework/build/CLI peers, cropper and Zod | **Passed**; matching framework peers and one overridden Zod 4.6.6 |
| Full installed-tree inspection | **Zero invalid/missing nodes**; five extraneous optional WASM-helper nodes noted below |
| `npm test -- --watch=false` | **465 passed, 38 files**, zero failures/skips |
| Focused cover selector/editor/preparation/display/coordinator/save/session suites | **158 passed, 11 files**, zero failures/skips |
| `npm run lint` | **Passed** |
| `npm exec -- tsc --noEmit --project tsconfig.spec.json` | **Passed** |
| `npm run build -- --configuration e2e` | **Passed** |
| `npm run build` | **Passed**; budgets unchanged |
| `npm run e2e -- --list` | **22 cases discovered** across desktop/mobile; no browser or web server launched |
| `npm exec -- ng mcp --help` | **Passed**; no MCP session/remote call opened |
| Local Zod/MCP compatibility and bounded-collection smoke | **Passed**, details below |
| Package-file whitespace check | **Passed** |

Focused test command:

```sh
npm test -- --watch=false \
  --include='src/app/features/games/cover-image-search/**/*.spec.ts' \
  --include='src/app/features/games/cover-image/**/*.spec.ts' \
  --include='src/app/features/games/covers/**/*.spec.ts'
```

The local Node smoke verified classic and mini Zod, sync/async rejection of a
100,000-element array bounded to two, and oversized maps/sets. Each rejected with
one `too_big` issue; the bounded-array element refinement ran zero times before
rejection. A valid bounded array parsed normally. The smoke also exercised the
shipped `zod/v3` entry point with zod-to-json-schema and registered a Zod 4 input
schema on an MCP SDK 1.30.0 McpServer, then closed it without connecting a transport.
This supplements application regressions; it is not a full CLI/MCP protocol suite.

`npm ls --all` reports five extraneous optional helpers after normal `npm ci`:
`@emnapi/core`, `@emnapi/runtime`, `@emnapi/wasi-threads`,
`@napi-rs/wasm-runtime`, and `@tybys/wasm-util`. A normal `npm prune` does not remove
the diagnostics or change the lock. There are no invalid/missing nodes, and the
targeted framework/Zod tree passes. These installation diagnostics are disclosed,
not represented as a completely clean full-tree check.

Production initial output is **677.02 kB raw / 160.98 kB estimated transfer**,
versus the phase-2 report's 676.31 kB / 160.71 kB. It remains below the unchanged
700 kB initial warning budget. The editor remains lazy at **57.84 kB raw / 12.90 kB
estimated transfer**. The existing Random Picker stylesheet warning (4.13 kB versus
4 kB) and removed Vitest poolOptions warning remain unchanged in scope.

## Limits And Handoff

At dependency-agent handoff no browser had been launched; the subsequent main-agent
full desktop/mobile execution passed **22 cases**, with no retries/skips. Discovery
alone was not used as browser evidence. No physical-device or remote Auth/Storage
gate has passed. Subsequent backend/Linux evidence and remaining limits are in
the G06 report; production uploads and release activities remain unauthorized.

Only the two package files and this new report are owned by this task. Generated
ignored node_modules/build/cache output is not an application-source edit. Nothing
was staged, reset, committed, deployed or provisioned. The staged-diff SHA256 before
and after dependency verification remained
`62092d1ed1bbe35197e650619a83a5a9eed14a4495a4223a795223ed11372439`.

## Sources

Official upstream advisories and maintainer fix are linked above. npm metadata was
queried with `npm view` and compared with installed manifests/`npm ls`:

- [Router 22.2.1 manifest, matching peers and package group](https://registry.npmjs.org/@angular/router/22.2.1)
- [Core 22.2.1 manifest and engine requirements](https://registry.npmjs.org/@angular/core/22.2.1)
- [Forms 22.2.1 manifest and Zod range](https://registry.npmjs.org/@angular/forms/22.2.1)
- [Compiler CLI 22.2.1 manifest and TypeScript range](https://registry.npmjs.org/@angular/compiler-cli/22.2.1)
- [Build 22.1.4 manifest and framework peer ranges](https://registry.npmjs.org/@angular/build/22.1.4)
- [CLI 22.1.4 manifest with exact Zod/MCP pins](https://registry.npmjs.org/@angular/cli/22.1.4)
- [Zod 4.6.6 manifest and integrity](https://registry.npmjs.org/zod/4.6.6)
- [GitHub reviewed Zod advisory data](https://api.github.com/advisories/GHSA-mrhx-c7xc-m3w5)

The Zod repository-specific advisory URL and queried Angular 22.2.1 GitHub release/
raw changelog URLs returned 404. No release notes were invented from those failed
requests; version selection uses the published advisories/npm artifacts and the
actual install, compatibility, test and build results above.
