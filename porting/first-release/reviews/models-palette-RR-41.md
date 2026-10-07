# T37 / RR-41 — whole-record reconciliation

**BLOCKED for complete acceptance.** Decision ED-NEW-models-palette-4.
The nine PR records and six UI records were inspected as complete records,
including source evidence, expected outcomes, failure/cancellation paths,
persistence, visual properties, interactions, notes and deviations. The
append-only `whole-record-evidence.jsonl` preserves each entire original record,
its hash, the source/test hashes and actual per-method test results. It does
not promote a record because a class exists or because Strike has a branch.

The table gives the effective behavior after later owner rulings. Original
source wording is retained in the original ledgers; in particular 42-point
lens / 30 Hz / accent-or-gold fills are not silently presented as current
Windows behavior when a recorded deviation supersedes them.

| Record | Effective whole-record clauses and existing evidence | Residual acceptance need |
| --- | --- | --- |
| PR-COMPOSER-112 | Four solo title combinations; Antigravity sweep / Silver text; pair gradient with gold last; 0.3 s colour change; Oklab accent lift; OD-0094's seamless 7 s resting drift, reduced-motion/visibility behavior. `EffortSliderCardTests`, `EffortSliderSweepTests`, `EffortTitleMorphTests`. | UI colour/residual review cannot run past `$Default` font creation on this Linux fixture. `EffortSliderTitleFramesTests` also fails its 380/400 ms settled-pixel equality on both this branch and the untouched requested base. Native capture comparison still required. Automatic model/maker intensity is RR-20. |
| PR-COMPOSER-117 | Exact single-level line, final full stop, 12 pt callout / secondary foreground, centred reserved 48 px height, same card size with/without levels (OD-0090). `EffortSliderKnobRecordsTests`, `EffortSliderSweepTests`. | Mac/Windows capture comparison is not newly proven here. |
| PR-COMPOSER-123 | 34 px track, 48 px Jewel, inset 24, fill to knob's far edge, passed/unpassed 5 px stops, count<=1 fallback, in-place 1.06 lift, shadows and parent opacity. `EffortSliderKnobRecordsTests`, `EffortSliderJewelKnobTests`, orb/opacity/review suites; silver-stop review suites. | Native capture at three/five stops remains a separate verification step; full orb page-shell proof is UI-035. |
| PR-COMPOSER-125 | Zero-distance press; knob grip offset/in-place lift versus track spring; clamped drag and rounded nearest stop; release/capture-loss paths; 0.22/0.8 and 0.32/0.78 physical springs with 4 ms steps, 50 ms frame clamp and bounded overtravel. Test binding/haptic counts; Windows drops haptics silently. `EffortSliderJewelKnobTests`, knob-record, wiring/loop/review suites. | No new native interaction/capture run; original persistence flows remain the existing binding/preference path. |
| PR-COMPOSER-126 | One labelled/value-bearing adjustable element; both exact labels; effort/Standard value; clamped range adjustment; writes through binding; 48 px reserved height and 0.2/1 keyboard spring (OD-0090). Knob-record and Jewel-knob suites. | Native UI Automation inspection remains unrun. |
| PR-COMPOSER-127 | Exact 2x2 kind table; every kind has liquid (OD-0075); fast/fusion overlay Strike (OD-0073); pair retains its fill; 0.3 s effect/fill transitions. Track/plain-liquid/sweep suites. | RR-20's per-model/per-effort shades remain absent; native all-look capture unrun. |
| PR-COMPOSER-128 | Source brand component values and horizontal gradients; plain also uses maker fill, fast/fusion retain it under Strike (OD-0075/0073); pair's underlay unchanged. Track/plain-liquid/sweep suites cover all known brands/pair kinds. | Fixed known-provider stops do not prove automatic maker mapping/intensity or N-model rendered liquid; those remain RR-20. |
| PR-COMPOSER-132 | Solo identity replacement starts new clock at zero, old effect fades retaining its clock; same-kind changes keep it; pair kind changes retain instance/time; plain has liquid (supersedes original no-effect failure case). Track/plain-liquid/sweep suites. | `EffortSliderNativeFrameTests` capture/clock checks require Windows; T14's integration must be rechecked after merge. |
| PR-COMPOSER-133 | Effective rate is 60 for every liquid kind (OD-0051/0075); reduced motion and not-front/hidden pause; width<=8 draws nothing; no hit tests/accessibility element. Cadence suite simulates ten seconds for every kind/pair. | Real-frame ten-second proof in `EffortSliderNativeFrameTests` requires Windows. Manual-frame evidence is not real GPU cadence. |
| UI-COMPOSER-012 | Mark/name/effort/chevron order and 5 px gaps; 14/15 px marks; 12 pt callout/8 pt bold chevron; exact colours; supported-fast bolt; pair/compact/no-effort states; active/held width; only name truncates; above-chip popup with below-only flip. Chip records/mark/crossfade, components/sweep and placement suites. | Source-runtime visual size remains unverified under OD-0001; native popup/clip/no-overlap verification unrun. RR-19 picker is a separate addition. |
| UI-COMPOSER-030 | Width 330; spacing 16; padding 16 horizontal / 14 top / 16 bottom; chrome; 0.12 s page crossfade; above-chip popup/held chip width. Card/panel/sweep/placement suites. | Native popover/glass/placement proof unrun; RR-19's approved search-card motion is not this legacy model-list test. |
| UI-COMPOSER-031 | Exact resolved effort/Standard labels; title2 medium 17 pt, callout semibold chevron 12; 4 px spacing, 8/2 padding, 44 px inset, 7 px continuous hover plate; exact tooltip/disabled/no-chevron branch; title colour, drift, 0.3 s change; tap switches list. Card/card-parts/title-morph/sweep/title-frame suites. | UI title colour/residual review blocked by Linux `$Default` font; native text/hover capture unrun. |
| UI-COMPOSER-033 | Leading 30 px circular toggle; body semibold 13; bolt/bolt.fill; yellow 0.18 plate or rest/hover overlays; exact tooltip and accessible On/Off; absent when unsupported; 0.3/0.7 spring and bounce. Card-parts/fast-mode-spring/sweep suites. | Native symbol/bounce capture unrun. |
| UI-COMPOSER-035 | Geometry, springs and many orb uniforms/pixels already tested, but this record also includes tube refraction at the shell, red/blue fringe, inner shade, full page layers, model-maker colour and all relevant states/animations. | **Explicitly blocked.** Existing `EffortSliderJewelReviewR8Tests.No_test_credits_UI_COMPOSER_035_whose_page_layers_over_the_orb_have_no_pixel_comparison` passes. Need the full locked page knob-element capture and pixel comparison, plus RR-20 maker/model shades and T14 Strike integration. No Req trait added and no reference/assertion changed. |
| UI-COMPOSER-036 | Exact line/full stop, 48 px slot, 12 pt callout, secondary colour and no interaction; card size retained. Knob-record/sweep suites. | Native visual comparison unrun. |

These are existing implementation proofs with explicitly scoped residuals,
not a full Gate D acceptance. Original status fields are not stamped VERIFIED,
IMPLEMENTED or otherwise rewritten. The complete original ledger bytes remain
unchanged; the reconciliation and ED entries are appended separately.

Linux evidence: the initial App whole-record slice passed 326/326. The expanded
final App slice passed 338 and failed 1: `EffortSliderTitleFramesTests`' 380/400 ms
settled-pixel equality. That test was also run alone in a detached checkout of
the untouched requested base and failed the same assertion. The UI title-review
slice failed all 5 before assertions with `Could not create glyphTypeface. Font
family: $Default ... Weight: Medium`. Core 9/9, Services 64/64, and UI structural
checks 5/5 passed. Final relevant slices therefore total 416 passed / 6 failed /
0 skipped. Original tests were not changed. Exact final counts and commands are
in `porting/evidence/models-palette/rr41/summary.json` and its TRX/log receipts.
Native tests were excluded from this Linux run, not disabled or altered in the
repository.

Gate A: 7 passed / 5 failed because the pinned Windows reference checkout is
unavailable. Gate B: 8 passed. Gate C: quarantine scan passed; existing
`MARROW_STUB` markers fail the stub check and the product-change check remains
unimplemented. `req_verify --no-run`: exit 2, no recorded complete run. No
verification ledger was regenerated or used to assert acceptance.

Windows still needs: pinned-reference Gate A; native slider/popup/automation
interaction and all-look/three-five-stop captures; `EffortSliderNativeFrameTests`
for ten-second cadence and clock identity; complete Jewel page-shell pixel
evidence; title-review font/capture evidence; post-T14 integration tests; complete
solution/requirement Gate D run. OD-0065's real-network palette proof remains
unperformed under the fake-only scope. Verdict **NOT READY**.

EffortSlider seam list: **empty**. All three T14-owned files are byte-identical
to `origin/claude/verify-terminal-20261001`.


## Round 2 — 2026-10-07

**BLOCKED; complete-record reconciliation is updated without granting acceptance.** ED-NEW-models-palette-8. Fifteen new JSONL entries append to `whole-record-evidence.jsonl`; each includes the COMPLETE original record and hash, every inspected field, source/method evidence, refreshed cases where replayed, and explicitly scoped earlier receipts where not replayed. The original fifteen entries and all parity/UI ledger bytes remain intact.

Fresh Linux record replay: App 326 passed; extra title/silver 4 passed/1 failed (the same 380/400 ms equality with the untouched-base reproduction already retained); UI decoder/structure/title-review 7 passed/5 failed before assertions with $Default glyphTypeface. The unchanged UI-COMPOSER-035 R8 guard still refuses credit without full page-shell pixel proof. All three Strike-owned sources are byte-identical, so the new standalone palette/picker/refresh files prove no merged renderer clause by themselves.

RR-19 delivers its approved control and additive seam under the explicit no-EffortSlider ruling. RR-20 retains the strict new-generation/source conflict, incomplete release-pack/authority/Prime/renderer work and closed UI gate. RR-21 now implements OD-0127 option A; its five new state/rendered-dot/motion facts pass, but fourteen original rise guards still assume the pre-new-control second card. Their lifecycle/input/pixel assertions and references are preserved. These changes do not close any clause solely from class existence. No record's status is promoted.

Final focused tests/gates, row outcomes and seam handoff are in `models-palette-round2.md` and `evidence/models-palette/round2/summary.json`. Original-test/ledger/Strike-source/source-copy hash proof is `preservation-final.json`. Native glyph/material/popover geometry, post-Strike model/Hydra binding, complete page orb captures and real 10-second GPU cadence/identity/retarget remain T9 Windows checks. Verdict NOT READY.
