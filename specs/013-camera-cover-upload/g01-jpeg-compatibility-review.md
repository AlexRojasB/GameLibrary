# G01 - JPEG Compatibility Review

Reviewed: 2026-10-29. **Historical documentation-only review; G01 was Blocked.**

Subsequent user decision: D3b bounded JPEG normalization is now approved. The
existing structural checks, STOPONWARNING, resource limits and canonical encoding
remain mandatory. Only the AC-run regression's expectation is explicitly changed;
the source stays nonconforming and every other rejection test remains active.
This review's recommendation/pending-approval wording below is historical, not a
new permission to bypass checks. G01 has now Passed fresh verification under D3b;
see the implementation report for actual 94/94 host/Noble results and residual
risks. The input is still nonconforming. Phase 1 remains partial.

## Cause And Validity

The failing regression is `CoverImageProcessorTests.Ac_run_cannot_cross_the_64_coefficient_block_boundary`
at `tests/backend/GameLibrary.IntegrationTests/CoverImageProcessorTests.cs:14`.
Its headers and Huffman tables are legal. Its coefficient sequence is not.

ITU-T T.81 section 4.3 defines an 8x8 block as one DC and 63 AC coefficients.
F.1.2.2.1 defines an AC symbol RS as zero run R followed by the magnitude category S;
F.2.2.2, Figures F.13-F.14, describes advancing the zig-zag index and decoding the
non-zero coefficient. The fixture's RS=0xFA means R=15, S=10, not the special
0xF0 zero-run code. The ten zero amplitude bits sign-extend to -1023, not zero,
so the all-zero payload still encodes non-zero AC values (Table F.2 and Figure
F.14). Its non-zero target positions are:

| Symbol | Next AC index before run | Target after 15 zeros |
| --- | --- | --- |
| First | 1 | 16 |
| Second | 17 | 32 |
| Third | 33 | 48 |
| Fourth | 49 | **64, outside 0..63** |

The 160x240 grayscale frame has 20x30 blocks. Its 600*(1+4*(1+10))=27000 bits
occupy exactly 3375 bytes, with a normal EOI. This is not truncation or an invalid
Huffman-code definition. A positive control with identical headers/tables and
DC=0 followed by EOB passes. Rejecting
the tables, file size or a particular byte pattern would wrongly substitute a
fixture-specific rule for entropy validation.

## Documented Library Behavior

libjpeg-turbo 3.2.1 `src/jdhuff.c` advances k by the run and uses
`jpeg_natural_order[k]` without reporting an out-of-block run. `src/jutils.c`
explicitly documents sixteen extra entries mapping these invalid indexes to
physical coefficient slot 63, avoiding wild writes without an inner-loop check.
The loop then terminates. This intentional error tolerance explains the observed
successful decode; it is not a P/Invoke buffer-sizing defect or an observed crash.

Both sequential fast/slow paths, and related progressive paths, require attention.
Skia decoding/canonical re-encoding does not independently establish conformance:
once a decoder has mapped the bad coefficient into its raster, the original invalid
sequence cannot be inferred reliably from the output pixels.

T.81 describes valid coding/decoding processes. A tolerant decoder accepting an
invalid stream does not make that stream conforming, nor by itself demonstrate
a memory-safety vulnerability. Application rejection policy is a separate choice.

## Supported Options And Upstream

- `TJPARAM_STOPONWARNING` stops when a warning occurs; it does not add missing
  validity checks. An error callback likewise cannot observe an unreported event.
- MAXPIXELS/MAXMEMORY/SCANLIMIT are resource controls, not full format validation.
  DCT/upsampling settings, disabling SIMD or changing output color space are not
  documented conformance modes. Reading coefficients/transcoding shares the entropy
  decoder; a round-trip is not an independent validation strategy.
- Latest public release checked: **3.2.1**, published 2026-10-27. The `jdhuff.c`
  Git blob is identical in 3.2.1, main and dev:
  `cd8c0847a22e1f1dcf4e0ec44e3e3dd859318371`.
- Related upstream issue [933](https://github.com/libjpeg-turbo/libjpeg-turbo/issues/933)
  concerns progressive AC writing beyond its declared spectral band. It was closed
  invalid/not planned with requests for a standalone reproducer, not with a fix.
  This is related evidence, not an accepted fix or a ruling on our exact fixture.
  Header-range fixes such as issue 564 do not validate an entropy-computed index.

No supported option or published correction for this path was identified in the
release/source/issues examined. This is a dated search result, not a claim about
every private branch or a prediction of upstream acceptance. No issue/PR was posted.

## Practical Options

Estimates are engineering ranges, excluding HTTP/UI work and upstream response time.

| Approach | Implications | Indicative effort |
| --- | --- | --- |
| Keep unmodified libraries and current strict promise | Current fixture still passes decoding. Configuration changes cannot close G01. | No viable configuration-only fix identified |
| Upstream-compatible native correction | Validate each non-zero target before use: k+R must stay within the active band. Add a diagnostic/failure that strict callers can act on; preserve safe fallback for tolerant callers if upstream chooses. Cover fast/slow, discarded-coefficient and progressive paths, not only one store. | About 2-5 days for a scoped correction/test proposal; 1-2 weeks for profile-wide review/native validation; upstream schedule unknown |
| Approved downstream patch while awaiting upstream | Same principle, but source builds, patch provenance, new hashes, notices, macOS development assets, perf/fuzz/native CI and security rebases become application responsibilities. Not approved or implemented here. | Additional 2-5 days packaging/CI; recurring review/revalidation on every codec update |
| Independent entropy validator | No IDCT is needed, but Huffman parsing, MCU counts, restart/stuffing rules and progressive coefficient/refinement/EOB-run state still are. This is security-critical codec logic, not a small extension to a marker parser. | Several weeks minimum for baseline+progressive, plus sustained corpus/fuzz maintenance; substantial scope expansion |
| Third decoder/validator | Another dependency does not guarantee stricter behavior. Requires approval, license/resource review and the full adversarial corpus. | Evaluation alone roughly 3-10 days; outcome uncertain |
| Explicit bounded-normalization policy | Retain structural checks, fatal/warning rejection, resource limits and fresh sanitized output, but acknowledge unreported decoder tolerance rather than promise exhaustive input conformance. Requires a real specification/regression-policy decision. | Roughly 1-3 days for policy/test reconciliation after approval; lowest ongoing codec ownership |

A native correction for this particular non-zero-run defect is plausibly narrow.
It is not evidence that a few lines certify all JPEG input. For progressive scans
the bound is Se, not always 63; refinement counts only appropriate zero positions.
ZRL/EOB boundaries and speculative fast-path fallback must not be misclassified.
An added warning is also observable API behavior for callers that inspect status,
so upstream compatibility/performance acceptance cannot be assumed.

## Recommendation And Acceptance Policy

For a personal game-library application, prefer a maintained, bounded raster
normalizer over owning a JPEG conformance engine or native fork. **Request approval
for a bounded compatibility policy before doing further codec work.** This trades
strict input conformance for documented decoder compatibility; it is not a way to
declare the invalid sample valid or to waive the current failing test silently.

The proposed, testable policy is:

1. Accept only the existing supported 8-bit Huffman JPEG cover profile, with explicit
   valid frame/table/scan structure and no implicit abbreviated/MJPEG-table repair.
2. Retain 5 MiB input, 4 million pixels, edge 4096, scan/native-memory bounds and
   the existing two-pipeline/512 MiB verification target. No new platform or decoder.
3. Reject structural incompleteness, failed full decoding and every reported native
   warning/error. Preserve the existing truncation/animation/bomb rejection cases.
4. If the maintained decoder fully returns a bounded raster without diagnostics,
   its unreported entropy tolerance may be accepted. **This explicitly permits the
   currently failing AC-run example; it does not prove ISO/T.81 conformance.**
5. Store only the new Skia JPEG after existing orientation/sRGB/alpha/size/quality/
   metadata normalization. Never retain or serve original upload bytes. Do not
   describe successful decoding or sanitization as proof that native code is bug-free.

This recommendation conflicted with the then-approved malformed-input/no-repair
promise and regression expectation, so it was **not adopted by this review**.
The later explicit D3b approval and verified outcome are recorded above.
If accepting this example is unacceptable, retain strict rejection and pursue a
reviewed upstream/native correction; keep G01 blocked rather than add heuristics.
Independent entropy validation is not necessary to fix this one native diagnostic
gap, but is the substantial alternative if unchanged decoder behavior must be
independently checked across the entire accepted JPEG profile.
If neither a policy change nor native ownership is acceptable, defer the feature
rather than declare its validation gate passed.

## Necessary Decisions And Verification

A compatibility-policy change needs explicit approval and reconciliation of
AGENTS.md's strict-validation/no-repaired-pixels instruction, Architecture's JPEG
boundary, Feature 013 D3a/backend validation/AC07-08/G01, and applicable higher-level
wording. The new policy must distinguish input conformance from bounded sanitized
output. The existing fixture must remain as documented conformance evidence; any
future change to its acceptance assertion/classification requires explicit approval.
Under the instruction in force during this review, the unchanged rejection assertion
still failed and G01 stayed Blocked. The later authorized expectation change does
not delete its fixture or relax other checks.

If strict rejection is retained, approval is instead needed before maintaining a
patched native artifact or adding independent validation. Test the exact defect
and valid coefficient-63 boundary, more runs/categories, fast/slow paths, grayscale/
color, restarts and progressive band/refinement/EOB boundaries. Repeat every
original security case, metadata/canonical-output tests, native ABI/provenance and
bounded concurrent resource checks on the unchanged Noble amd64 target. Do not
infer full conformance from a finite green suite or promise historical integrity
for valid JPEG edits/progressive representations.

The exact failed case is realistically fixable without a third decoder, given
approved native changes. **The existing broad G01 strictness promise is not
achievable with the current unmodified libraries and wrapper configuration alone.**
A small personal-library maintenance footprint and exhaustive format compliance
are different product choices; they should not remain an implicit acceptance rule.

## Evidence And Scope

This review reran the existing failing regression: one failure, same acceptance
defect; its positive control executes first. Full-suite 93/94 and Linux/resource
results remain those in the implementation report, not new runs claimed here.
Only this report and the Phase 1 report were edited. No production/test/dependency/
deployment changes, new decoder, fork, commits, staging or pushes were made.

References checked:

- [ITU-T T.81 record](https://www.itu.int/rec/T-REC-T.81-199209-I/en) and
  [W3C-hosted text](https://www.w3.org/Graphics/JPEG/itu-t81.pdf): 4.3, printed p.15;
  F.1.2.2.1/Table F.2, pp.89-90; F.2.2.2/Figures F.13-F.14, pp.105-107
  (PDF pages 109-111 for the decoder figures).
- [Pinned sequential decoder](https://github.com/libjpeg-turbo/libjpeg-turbo/blob/3.2.1/src/jdhuff.c),
  [documented sentinel table](https://github.com/libjpeg-turbo/libjpeg-turbo/blob/3.2.1/src/jutils.c),
  [progressive decoder](https://github.com/libjpeg-turbo/libjpeg-turbo/blob/3.2.1/src/jdphuff.c).
- [Public TurboJPEG controls](https://github.com/libjpeg-turbo/libjpeg-turbo/blob/3.2.1/src/turbojpeg.h)
  and [library error-handling documentation](https://github.com/libjpeg-turbo/libjpeg-turbo/blob/3.2.1/doc/libjpeg.txt).
- [Release 3.2.1](https://github.com/libjpeg-turbo/libjpeg-turbo/releases/tag/3.2.1),
  [related issue 933](https://github.com/libjpeg-turbo/libjpeg-turbo/issues/933),
  [header-check issue 564](https://github.com/libjpeg-turbo/libjpeg-turbo/issues/564).
