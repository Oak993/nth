# T24 / GAP-2 motion investigation

**Verdict: NOT READY.** The immutable source-timing oracle and the immutable 15% frame-step test contradict each other. The port already follows the source curve. There is no compliant production fix within this task's absolute rules, so no motion code, threshold, assertion, fixture or design reference was changed by this investigation. The requested merge was completed, and the evidence-only result is delivered on `codex/gap-2-fix`.

Date: 2026-10-07 (Asia/Riyadh). Baseline: `4f0f2ac07eb135609c2751c356c3c1355b9c964a`. Requested incoming branch: `1291606cbac734da0b2afe125ad9c01227eed83b`. Tested merge: `6621f2c794a5ee36daff1349e42a5e6a49390989`. Executor: Linux x64, .NET SDK 10.0.100 / runtime 10.0.0, Avalonia 12.1.2. No Windows desktop/rendering run was available.

## Root cause and source authority

`customization-SPEC.md` CZ-T40 (line 906) requires the named duration and curve, monotonic sampling, and no frame step above 15%. CZ-14.1 sends Detail text to CZ-12.9 (lines 691–692): outgoing 0.1 s, incoming 0.2 s after 0.08 s. The locked design `design-src/customization/gen.py:231–232` supplies the curve: `.xf>span ... transition:opacity .1s ease` and `.xf>span.on ... transition:opacity .2s ease .08s`. CSS `ease` is cubic-bezier(.25,.1,.25,1).

This particular detail fade is an owner-approved addition beyond the Mac baseline, as the SPEC introduction and Sources/authority section explicitly state (OD-0085/0089/0091/0097/0076). It is not a port of a Mac Swift detail-out animation. The record cites `ComposerView.swift:672` for the separate working-dots .easeInOut(duration: 0.4) transition; that is not authority for detail-out. The pinned ComposerView source was retrieved read-only from GitLab; its Git blob `8908f160078c06dcb182548b7fb6a30663a3b24b` matches the inventory, and converting its LF download to the inventory's CRLF produces the recorded SHA-256. No Mac source was modified. Details and hashes are in [tested-source-sha256.json](T24-GAP-2-motion-evidence/tested-source-sha256.json).

`CzCrossfadeText.cs:48–57` fades outgoing opacity with `CzMotion.Ease(.1)`, and `CzMotion.cs:22` supplies exactly those four CSS control points. `CzNumber.cs:18,28–31` starts at the action's monotonic `SwiftClock.Now` and evaluates that curve at actual elapsed seconds. Its +1 ms endpoint tolerance cannot explain the large first or second step. The duration and curve are source-correct; the source timing itself exceeds the strict smoothness cap.

The first reported violation is 22.0672% at frame 1. It is **not the maximum**: frame 2 moves 35.5188% of the full travel. Placeholder-out's source 0.15 s CSS ease also violates the cap (23.3404% at frame 2).

## Why no allowed fix exists

The unchanged `CustomizationMotionTests.The_pages_row_transitions_follow_their_curves_on_the_frame_clock` calls `FrameCurves.Check` for a 100 ms CSS ease. At ideal 60 Hz, its independent oracle gives outgoing opacity 0.779326255418 at frame 1. Including the existing 0.02 travel tolerance and 1e-6 allowance, that assertion requires opacity **<= 0.799327255418**.

The unchanged GAP test `GapCustomizationAcceptanceTests.GAP_Each_transition_frame_is_monotonic_and_moves_at_most_fifteen_percent_of_its_travel("detail out")` requires frame 0 = 1 and the first step <= .15 + 1e-9, hence frame 1 opacity **>= 0.849999999**. The allowed intervals do not overlap. Even a linear 100 ms fade cannot cover a unit of travel in six 60 Hz steps bounded by .15 (six times .15 = .90).

Lengthening the fade, changing its curve, clamping per-frame movement, delaying its start, rescaling its clock or inserting/skipping samples would violate the frozen timing oracle or the user's explicit rules. There is no proposed or secretly applied duration adjustment. Continuing requires an explicit reconciliation of the locked timing and its immutable curve assertions with the owner's 15% bar; this report does not amend either one.

## Excluded implementation causes

- Missing first sample: `FrameCurves.Sample:27–35` explicitly records frame 0 immediately after the action. Both production dumps begin at opacity 1.
- Skipping time base or delayed first tick: `CustomizationPageRig.Advance:110–123` advances its runtime and frame clock together in <=1/60 s steps. Every dump contains every frame, with 16.6666 ms increments (TimeSpan's 100 ns quantization). The direct baseline control and isolated scalar have the same steps as the merged page.
- Mid-animation layout jump: the baseline direct-control capture never measures or arranges text, yet its opacity is identical to the actual page. `CzCrossfadeText`'s height animation is separate from opacity.
- Discrete flip: `SetText` synchronizes the outgoing scalar to its visible opacity before fading; its new text starts at opacity zero. The first two violations occur inside the continuous Bézier calculation, far before its endpoint completion.

## Frame-by-frame before and after

[before.csv](T24-GAP-2-motion-evidence/before.csv) is a genuine CPU run against a detached checkout at `4f0f2ac07eb135609c2751c356c3c1355b9c964a`, using that branch's actual `CzCrossfadeText`, `CzNumber`, `CzMotion`, `Animation` and `SwiftClock`. [after.csv](T24-GAP-2-motion-evidence/after.csv) runs the same probe against `6621f2c794a5ee36daff1349e42a5e6a49390989`. **After means after the requested merge and investigation, not after a successful motion repair.** They are identical apart from the stage label; no improvement is claimed.

[after-page.csv](T24-GAP-2-motion-evidence/after-page.csv) additionally captures the actual merged `CustomizationPageRig` detail and preview-placeholder properties, plus their isolated scalars, on the rig's own unchanged clock. It uses one headless application lifetime to avoid the Linux unit-test dispatch font-teardown bug, an existing fake platform seam reporting animations enabled for the unrelated legacy switch's Windows query, and the same real ICU 76 ABI bridge already documented by GAP-2. None alters the fade, clock, expected samples, assertions or fonts. The sampler sources and project templates are checked in beside the CSVs as `.txt` evidence, not added to production/test compilation.

Every CSV covers frame 0 and all 24 following frames. [check-frames.py](T24-GAP-2-motion-evidence/check-frames.py) independently solves the CSS Bézier with 80 bisections, verifies exact cadence, all samples, monotonicity, endpoints, baseline/merged equality and page/scalar equality, and checks the unchanged .15 + 1e-9 bound. [frame-check.json](T24-GAP-2-motion-evidence/frame-check.json) records the violations. Its exit code is **1 / NOT READY**, not a fabricated acceptance pass.

The direct detail-out capture is reproduced below (before and after columns are equal):

| Frame | Actual elapsed ms | Before opacity | After opacity | Travel step % |
|---:|---:|---:|---:|---:|
| 0 | 0.0000 | 1.000000000 | 1.000000000 | 0.000000 |
| 1 | 16.6666 | 0.779327700 | 0.779327700 | 22.067230 |
| 2 | 33.3332 | 0.424140059 | 0.424140059 | 35.518764 |
| 3 | 49.9998 | 0.197598601 | 0.197598601 | 22.654146 |
| 4 | 66.6664 | 0.075177360 | 0.075177360 | 12.242124 |
| 5 | 83.3330 | 0.016526552 | 0.016526552 | 5.865081 |
| 6 | 99.9996 | 0.000000000 | 0.000000000 | 1.652655 |
| 7 | 116.6662 | 0.000000000 | 0.000000000 | 0.000000 |
| 8 | 133.3328 | 0.000000000 | 0.000000000 | 0.000000 |
| 9 | 149.9994 | 0.000000000 | 0.000000000 | 0.000000 |
| 10 | 166.6660 | 0.000000000 | 0.000000000 | 0.000000 |
| 11 | 183.3326 | 0.000000000 | 0.000000000 | 0.000000 |
| 12 | 199.9992 | 0.000000000 | 0.000000000 | 0.000000 |
| 13 | 216.6658 | 0.000000000 | 0.000000000 | 0.000000 |
| 14 | 233.3324 | 0.000000000 | 0.000000000 | 0.000000 |
| 15 | 249.9990 | 0.000000000 | 0.000000000 | 0.000000 |
| 16 | 266.6656 | 0.000000000 | 0.000000000 | 0.000000 |
| 17 | 283.3322 | 0.000000000 | 0.000000000 | 0.000000 |
| 18 | 299.9988 | 0.000000000 | 0.000000000 | 0.000000 |
| 19 | 316.6654 | 0.000000000 | 0.000000000 | 0.000000 |
| 20 | 333.3320 | 0.000000000 | 0.000000000 | 0.000000 |
| 21 | 349.9986 | 0.000000000 | 0.000000000 | 0.000000 |
| 22 | 366.6652 | 0.000000000 | 0.000000000 | 0.000000 |
| 23 | 383.3318 | 0.000000000 | 0.000000000 | 0.000000 |
| 24 | 399.9984 | 0.000000000 | 0.000000000 | 0.000000 |

## What ran

Every .NET test filter ends in `&FullyQualifiedName!~Native`. Logs, TRX files and the exact GAP/curve commands are in [T24-GAP-2-motion-evidence](T24-GAP-2-motion-evidence).

| Check | Actual result |
|---|---|
| `dotnet build tests/Marrow.App.Tests/Marrow.App.Tests.csproj -p:EnableWindowsTargeting=true` | PASS: 0 warnings, 0 errors |
| UI: `(FullyQualifiedName~SwiftAnimation\|FullyQualifiedName~FrameClock\|FullyQualifiedName~CzControls)&FullyQualifiedName!~Native` | PASS: 101 passed, 0 failed, 0 skipped |
| Core: `(FullyQualifiedName~Clock\|FullyQualifiedName~ContinuousInstant\|FullyQualifiedName~TestRuntime)&FullyQualifiedName!~Native` | 21 passed, 1 failed, 0 skipped. The broad name filter also selected `FileOpsTests.An_atomic_write_blocked_by_a_held_handle_retries_on_the_manual_clock_and_lands_once_released`; Linux does not reproduce its Windows held-handle IOException. No file-lock implementation or assertion was changed. |
| `GapCustomizationAcceptanceTests` | 1 passed, 3 failed, 0 skipped: preview isolation passes; the already recorded palette discrepancy fails; placeholder-out fails at .233404; detail-out is blocked by `Could not create glyphTypeface ... Inter ... Medium` during headless teardown. |
| Isolated `...GAP_Each_transition&DisplayName~detail&FullyQualifiedName!~Native` | 1 failed due to the same glyphTypeface teardown; not a Windows assertion pass or a claimed stutter fix. |
| `CustomizationMotionTests` | 3 passed, 2 failed, 0 skipped. Maker/state curve cases and glyph motion pass; page-row curve and Reduce Motion cases are blocked by the same font teardown. |
| Baseline App-dependent dump build | Blocked by existing CS8604 in baseline `SettingsRise.cs:148` under this SDK. No warning suppression or baseline edit. The separate actual baseline UI control/scalar probe builds and runs successfully. |
| Baseline CPU control/scalar, merged CPU control/scalar, merged actual-page captures | All run successfully; identical source-correct curves; the cap remains FAILED. |
| Independent frame checker | Exit 1, NOT READY; detail max .35518764072716558, placeholder max .23340432065090078. |
| `node porting/tools/verify.mjs --gate B` | PASS: 8/8 |
| `node porting/tools/verify.mjs --gate A` | 7 pass, 5 fail: configured `E:/Droppy/reference/droppy-code-v1.7.0` checkout is unavailable on Linux. The verified single Swift file is not a replacement claim for Gate A. |

Windows rendering / real display cadence remains NOT RUN. It is needed for the native acceptance run, but cannot make the two contradictory frame-1 intervals overlap. No DONE/VERIFIED status was asserted and `mark.mjs DONE` was not invoked for a held GAP finding.

The first filtered invocation overlapped completion of the initial build and discovered zero matching cases. It is not counted as a pass; the subsequent completed-build run above discovered and executed all four GAP cases.

## Merge and ledger integrity

The requested branch was created from the exact supplied head; only that branch is pushed. The origin fetch configuration initially covered only `impl/b2`, so both named branches were fetched explicitly. The no-fast-forward merge's only text conflict was a comment/trait in `ComposerPreviewTests.cs`; the incoming comment and existing `Spec=CZ-T45` trait were both retained. No assertion was changed in the resolution.

Incoming ledgers had one historical cross-reference replacement each, plus additions (including a middle insertion in STATE). The merge preserves the entire original GAP-2 ledger as a byte-exact prefix and appends each incoming replacement/insertion chunk byte-exact with provenance delimiters. No CRLF/LF normalization. [merge-ledger-bytes.json](T24-GAP-2-motion-evidence/merge-ledger-bytes.json) records offsets, lengths and hashes; independent review verified every chunk. The merge ledger numstat was engineering decisions **756 additions / 0 deletions**, STATE **131 additions / 0 deletions**. The investigation appends `ED-591` and a truthful STATE handoff in binary append mode.

The GAP test, source-curve test, FrameCurves fixture and locked gen.py are byte-identical to the supplied GAP-2 head. Selected CZ-12.9, CZ-14.1 and CZ-T40 clauses are also byte-identical; the incoming SPEC changes only two unrelated ED-number references. This investigation adds no tests/reference edits or production changes beyond the separately authorized merge.

## Independent review and handoff

A read-only subagent, required by PORT_CONTRACT section 15, independently checked the sources, clocks, contradictory assertions and merged ledger bytes. It found no compliant code fix, independently calculated the 22.0674% / 35.5189% ideal steps, and returned NOT READY. Its source/byte review is in [independent-review.md](T24-GAP-2-motion-evidence/independent-review.md); it did not independently execute .NET tests.

Next work: reconcile the frozen source duration/curve oracle and strict smoothness bar, then implement the approved timing and repeat the unchanged-cap motion audit plus the Windows render run. This branch contains the requested merge, a source-bound diagnosis and reproducible evidence, not a passing GAP-2 fix.
