# QA dependency-security candidate — explicit approval pending

2026-11-06. **Not applied. No staging, commit, push or dependency change is authorized.**
This records the previously validated candidate for the Railway/Vercel private QA
proposal. Changing hosts does not resolve frontend build-chain advisories.

## Exact proposed change

- Pin `@angular/build` and `@angular/cli` to **22.2.6**.
- Retain Angular framework/compiler **22.2.1**, Zod override **4.6.6**, cropper
  **9.1.7**, Supabase JS and every non-development resolved package version.
- Compatible transitive refreshes: `brace-expansion` **5.0.12**, `fast-uri`
  **3.1.9**, `undici` **7.30.2**. Candidate also resolves Vite **8.3.5**,
  Piscina **5.3.2**, and the replacement MCP server **2.3.1**.
- Exactly **272 version-record changes** (including additions/removals): 269 from
  tooling plus three transitive refreshes. This is not a two-line-only change.
  No new direct dependency, broad force-fix, major migration or new override.

| Artifact | SHA256 |
| --- | --- |
| Current `src/frontend/package.json` | `a03cec2ca71583bd26d950449321f2f43b7bd40fc821a5d576827f0cce4914c7` |
| Current `src/frontend/package-lock.json` | `daa6288a3e0ebeca5ebc7c9100b6b54f91bec05132f7f80932ff6d9302b428a9` |
| Candidate manifest | `6779fe54babeda3f3da1f8973544369c350658edf4f72bf8d2da5c8ef158cdd2` |
| Candidate lock | `cf4e8d5b6f6b3e08b8ca0ec0b4a13f7960c6000085152de0206f6c88c63e1575` |
| Exact unified diff, 194372 bytes | `7b1d013fbe8e33df988c5839849129f8150a6e3dae7fd3387463a478ce84c034` |

The exact diff is retained **outside Git** at
`/private/var/folders/fd/h4rhj_wn5_71qy5q79xm8r_m0000gn/T/opencode/qa-preflight/dependency-candidate.patch`.
The manifests, installed candidate and verification logs are beside it under
`security-candidate-22.2.6/`. These temporary files are not durable release storage:
before approval/publication, retain the reviewed patch privately with its hashes.
Do not regenerate using an unconstrained `npm update` and assume it is identical.

During this Railway review, the retained diff was recomputed in memory and matched
the current package files -> exact candidate byte-for-byte. All **171** other
Git-eligible frontend files matched the candidate. No candidate file was copied
over the working tree or existing index.

## Security disposition

Previous validated audit: existing lock **3 critical / 8 high / 21 moderate**
package records; candidate **0 critical / 0 high / 17 moderate**. Production-only
audit was **zero for both**. Counts are not distinct advisories and may change
without a lock change. Fresh production-only audits are zero for both locks; both
full-audit attempts timed out after 60 seconds. The historical full counts are
not relabeled as fresh results. See the Railway report for current verification.

The 17 moderate records include minimatch amplification via lint tooling and
Vitest mock-server findings. The static frontend does not expose a Node, Vite,
Vitest or Angular CLI MCP server, but builds execute tooling. Limit builds to
reviewed inputs, keep dev/test servers private, do not supply backend secrets to
Vercel, and do not enable untrusted automatic previews. Static hosting is not a
waiver of build supply-chain risk. No silent ESLint/minimatch overrides or
incompatible major downgrades are proposed.

## Verification and approval

The previous isolated clean-install verification passed: **465 tests / 38 files**,
**15 QA configuration tests**, lint, production build, synthetic-input QA build,
PWA artifacts and production audit. Full audit exited nonzero for the remaining
moderates. Initial output was 678.67 kB production / 678.78 kB QA, within unchanged
budgets. Existing Picker stylesheet, Vitest poolOptions and Vite config-loading
warnings remain disclosed. Full browser/backend runs were not part of that
earlier dependency experiment. See the [preflight](private-qa-preflight.md).

This Railway continuation reran the exact candidate's **465 tests / 38 files**,
lint, production build and QA build with a synthetic HTTPS Railway API origin;
all passed with the same 678.67/678.78-kB initial sizes and a 58.02-kB lazy editor.
Production audit again reports zero; full audit timed out. The clean installation
and 15 QA-config candidate checks above remain historical, not a new install.
The current worktree separately passed its 465 tests, 15 config tests, lint and
both builds. No package file was changed and no candidate was applied.

Owner approval must explicitly identify:

1. These two package-file changes and the exact candidate lock/diff hashes.
2. Acceptance of the remaining moderate build/test-tool risk for **limited private,
   uploads-disabled QA**, or a separate compatible remediation task.
3. Reverification on the final immutable source, including backend and small browser
   regressions. Approval of a dependency diff is not deployment/Git publication approval.

Until approved, builds from the working tree still use its existing vulnerable
development-tool lock. No candidate is silently substituted into a deployment.
