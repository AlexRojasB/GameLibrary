# Durable private-QA snapshot evidence

2026-11-06. Local files only; **not staged, committed, published or deployed**.
The dependency application is approved; integrated source publication is not.

| Artifact | Purpose |
| --- | --- |
| `proposed-snapshot-manifest.json` | Exact original **407-path** source manifest, copied byte-for-byte; modes, sizes, hashes, origins and staged-version identities |
| `proposed-snapshot-summary.json` | Exact historical summary; its temporary source path is historical, not the durable archive location |
| `dependency-candidate.patch` | Exact approved two-file security diff, not regenerated or broadened |
| `secret-scan-review.json` | Value-free scanner findings/triage, additional checks and limitations |
| `applied-verification.json` | Fresh application/graph/audit/frontend verification and explicitly historical backend/browser checks |
| `SHA256SUMS` | Hashes for the five machine-readable artifacts above; does not hash itself |

From this directory, `shasum -a 256 -c SHA256SUMS` verifies the retained metadata
and patch. None of these files contains credential values or generated secrets.
Public account/project identifiers and source-content hashes are not credentials.

## Source archive, separately retained

The exact source archive is **outside this Git repository and outside temporary
storage**, at:

```text
~/Documents/GameLibrary-QA-Artifacts/private-qa-2026-11-06/proposed-qa-source.tar
~/Documents/GameLibrary-QA-Artifacts/private-qa-2026-11-06/proposed-publication-manifest-418.json
~/Documents/GameLibrary-QA-Artifacts/private-qa-2026-11-06/preservation-verification.json
~/Documents/GameLibrary-QA-Artifacts/private-qa-2026-11-06/SHA256SUMS
```

The directory is owner-only (`0700`); retained files are `0600`. The proposed
418-path manifest hashes the frozen source plus the 11 explicitly enumerated
companions; it is a review artifact outside that tree, not a commit or approval.
It records the earlier personal-only owner choice. The subsequent existing-workspace
selection changes companion metadata and adds a current plan/report; that old
publication manifest is no longer the current worktree identity. Keep it intact
as historical evidence and re-manifest before publication. The 407-path application
manifest/archive and the five checksummed artifacts in this directory are unchanged.
Run `shasum -a 256 -c SHA256SUMS` in that directory to verify it. This is local
durable storage, not an off-machine backup. Do not add the tarball to the source
commit, deploy it as public content, or rely on the previous temporary copy alone.

| Identity | SHA256 |
| --- | --- |
| Original source manifest | `73899e7fd9732884421edefe0dbb0c65edfaa017655b3868d7286d084437e1fb` |
| Original 3,962,880-byte USTAR archive | `b3f2f41a9de588b06f8c28e84398c359522e0ebdc6327f01b1ce1afcdfabb455` |
| Exact approved dependency patch | `7b1d013fbe8e33df988c5839849129f8150a6e3dae7fd3387463a478ce84c034` |

The source archive includes an existing **browser-public development Supabase
publishable key** and explicitly synthetic/test key strings. It is not a
literal zero-key-strings artifact. No private/server credential or generated
secret was detected by the documented scan. Do not publish that conclusion as a
guarantee or test the development key against hosted services. New QA builds
must use separate QA configuration, never that development project.

## Counts and publication boundary

The initial review captured **409** Git-eligible files. Exactly two were omitted:
`opencode.json` (local agent/provider configuration) and `render.yaml` (retired
hosting blueprint). **409 − 2 = 407**. No original was deleted. The two approved
package-file replacements do not change the file count. The unapproved Steam
logging proposal remains labeled documentation in the 407, not runtime scope.

This evidence directory and follow-up reports/configuration plan were written
after export. They are not silently part of the frozen 407-file manifest. The
[publication plan](../../qa-security-application.md#publication-plan--owner-review-only)
enumerates **11** companion paths for a proposed **418-file** publication tree.
Owner approval and final tree verification are required before any Git action.

Current authority: [applied security review](../../qa-security-application.md),
[existing-workspace Vercel readiness](../../vercel-workspace-qa-readiness.md).
The owner now permits a separate Game Library project in `bar-book`, not changes
to BarBook itself. Earlier personal-only plans are superseded; source/dependency
publication, project creation and deployment remain separately gated.
