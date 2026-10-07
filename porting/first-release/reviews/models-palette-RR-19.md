# T37 / RR-19 — searchable OpenRouter picker

Base: `285b6b7a99b39e0e517f34c8d34a1860c945f83f` (the requested
`origin/claude/verify-terminal-20261001`). Audit: `origin/codex/release-audit`,
`release-readiness.md` RR-19. Decision: ED-NEW-models-palette-1.

**BLOCKED.** The approved picker is still absent. Existing OpenRouter API/session
plumbing is not the picker. No UI implementation is claimed by this change.

SOURCE FACT: OD-0049 requires search in the composer's model picker. OD-0060's
hold is superseded by OD-0075 and OD-0099 Q-PICKER A. OD-0076 requires folding
rows and easing card height in approximately 0.33 s; minimum chip clearance
10 px; shown count `0 models`, `1 model`, `2 models`, etc.; count crossfade;
top-match highlight during search; arrows move it and Return chooses it.

The approved v4/v5 source is cited at
`E:\Droppy\work\design\openrouter-picker\` and
<https://claude.ai/artifact/BNoUoc4vvCLK5pHzYMmVcA>. It is absent from this
base's tracked design sources. The Windows file connector returned
`tunnel_client_not_seen`, so the external copy could not be read. OD-0069's
exact geometry/type/appearance cannot be reconstructed from the decision's
description. Need the locked source and its transition/placement measurements.
Approval already exists; this is a source-access blocker, not a request to
approve the design again.

Verification on Linux, .NET 10.0.401 / Avalonia 12.1.2, isolated data directory:

- New `OpenRouterPickerCatalogueTests`: 5 passed, 0 failed/skipped. The real API
  parser feeds the existing Settings search with fake models; name, maker,
  complete ID, case-insensitive matching and unsuccessful HTTP status covered.
  Every HTTP exchange uses `InProcessHttpHandler`; no socket/key store.
- This proves a usable existing prerequisite, not approved picker layout,
  keyboard interaction, count transitions or composer wiring.
- Baseline solution build: 0 warnings/errors.

Commands and TRX receipts are stored in `porting/evidence/models-palette/rr19/`.
No existing tests, references or assertions changed. No EffortSlider seam.


## Round 2 — 2026-10-07

**DONE (implementation and additive handoff; native validation is T9's).** ED-NEW-models-palette-5 supersedes the source-access blocker above. The mirror's approved Main.dc.html, capture.js and probe.js are copied byte-for-byte under `design-src/models-palette/openrouter-picker/`; their origin commit and SHA-256 are in `evidence/models-palette/round2/sources.json`. No design branch was merged.

The real standalone control implements the 330 px card, maker grouping and literal source glyphs; invariant all-word search across name/ID/maker; clear/refocus; retained rows with .34 s source folding/opacity/scale/card-height curves; the separate count-out/count-in clocks and singular counts; empty-state words; top-match highlight, arrows and Return. Hover/press and edge masks use the source timings. The source requests smooth nearest keyboard scrolling but gives no numerical browser duration. The port uses the card's .34 s ease on the shared frame clock, retargeting from the displayed offset and landing immediately under Reduce Motion; exact native/browser duration comparison still needs Windows.

Linux: three actual headless picker tests pass in `rr21-app-final3.trx`, including manual-clock source-curve checks, all rows retained while filtered, keyboard selection, zero/one/many counts and clear. Initial missing-type red is `rr19-red.log`. Every HTTP prerequisite uses the existing fake API tests; no models API was contacted.

The lead ruling expressly forbids an EffortSlider edit. Delivered additive seam (in the dependent palette commit): `ModelPickerSeam.Create(entries, selected, hasHistory, choose, back, colour)` and `Place(popup)`. After T14/Strike, bind the model-only list at EffortSlider.cs:304, retaining Choose at :353, Back, history/provider restrictions, pair selection at :305-309 and the outer .12 s page transition. Pass the existing EffortBrand heart; do not enable draft model shades. Place uses Top/-10 px, and the popup must retain that clearance as height changes. This branch changes ZERO bytes in the three Strike-owned files.

Windows needs: native above-chip placement and monitor-edge flip while search/card height changes; glass and Inter/Cascadia/source SVG rendering; hover/press/focus and smooth keyboard scrolling; selection/back/history/Hydra after seam integration. These are explicitly uncredited by the fake/headless proofs. This row's delivered implementation does not open RR-20 or RR-41 acceptance.


### Round 2 lead-ruling follow-up: post-Strike binding

DONE for implementation and fake/Linux proof. T9 merged Strike in standing 666097b04; this branch imports the Strike tip in merge 4f795ba37. The real OpenRouter ModelList now uses the approved searchable picker. New ModelPickerBinding retains Hydra pair rows, Choose, Back, current-provider history restrictions and search. EffortSlider.cs adds only the -10 px OpenRouter popup offset and conditional ModelList delegation for this row; the outer .12 s panel transition is preserved. Locked brand hearts remain authoritative while the separate ModelShades UI gate is false. ED-NEW-models-palette-13.

round2/rr19-rr20-binding-green.trx: five passed, comprising two real picker binding tests, two locked picker tests and one RR-20 adapter test. Native Windows glass/font/smooth-scroll and above-chip captures are still required; no source-side visual acceptance is claimed from headless tests.
