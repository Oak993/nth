# T37 / RR-21 — Settings palette refresh

**BLOCKED.** Decision ED-NEW-models-palette-3. The existing Settings provider
refresh checks provider availability/catalogues; it does not research maker
logos, recompute family shades or report new makers. RR-21 is not already done.

SOURCE FACT: OD-0067 requires the same engine for automatic and requested
refreshes, checking all model sources, discovering new makers, deriving their
palettes, re-spacing existing families, showing progress and a short changed/
new-maker summary, with changed shades gliding to their new values. Personal
provider lists cannot determine shades (OD-0065/0066).

Evidence inspected:

- `SettingsView.cs:732-739`: `ProvidersRefreshButton` calls
  `Providers.RefreshEverything()` with `Checking providers…` /
  `Check providers again`. This is the source's provider-check control, not the
  owner-added palette refresh control; it must keep its existing behavior.
- No Settings control binding to palette research/refresh exists in this base.
- The new `PublicModelCatalogueStore.Refresh()` is a validated keyless catalogue
  prerequisite only. Its ten-root/restart/failure tests are in RR-20's receipt;
  it neither extracts logo colours nor produces a palette/new-makers summary.

The cited palette/refresh design is outside the repository. OD-0067's
`Settings > Appearance`, `Colours`, `Refresh`, and `last checked` description
is explicitly a **proposal**, not a licence to choose final geometry, text or
states. Need the locked refresh design plus RR-20's actual palette engine.
The missing external copies were requested during this task; the Windows file
connector is disconnected. Existing owner approvals are not being reopened.

No substitute UI or fabricated success/progress was added. No existing tests or
Settings behavior changed. When sources are available, test first with fake
catalogue/logo sources: progress through every maker; new-maker summary;
same result as the automatic path; offline failures that retain known palettes;
repeat refresh; and animated re-spacing with reduced motion. Run final Settings
layout/scroll/no-overlap checks on Windows. No EffortSlider seam.

Inspection receipt: `porting/evidence/models-palette/rr21/inspection.json`.


## Round 2 — OD-0127 visible option A

**BLOCKED for release; the approved visible control is implemented.** ED-NEW-models-palette-7. OD-0127 on origin/claude/verify-terminal-20261001 supersedes the earlier Round-2 no-control instruction. Its verbatim decision is `evidence/models-palette/round2/OD-0127.md`; the complete locked HTML is copied byte-exact under `design-src/models-palette/palette-refresh/`, with SHA/origin in sources.json. Options B and C are not implemented.

Appearance now contains Colours after the Look card and before the old transparency/sidebar card. The one Model colours row reuses ChromeRow/ChromeCard and the existing Updates glass capsule; the under-row progress control IS UpdatesProgressBar. Refresh displays Refreshing… and disables while running. The exact locked Checked/Checking/summary/no-new/offline words come from the actual engine's progress and snapshot diff. Already-loaded native/provider models are observed without a reload or changing global lineup facts. Five representative dots maximum matches the source's "a few real makers" strip; changed colours use the literal 1.1 s (.4,0,.2,1) curve, arrivals .6 s CSS ease, detail crossfade .25 s, all retargetable and reduced-motion aware. No unknown/pending model is reported as successfully coloured. The summary clears only on leaving the page. Offline catalogue failures return without publishing or saving different colours. Initial {when} is never if no catalogue check has occurred.

The four new real-control tests over the fake engine pass: exact running/result words, busy/disabled button and actual Updates bar; summary retained over clock advances then cleared on close; no-new result and offline unchanged digest/dots; displayed-colour retarget/reduced motion; detail opacity curve/reduced motion. The shared engine's two-server-day seed-change diff is tested as well. The single-flight/cooldown path uses no real network. Test-first receipts include missing-control/detail API reds and the real seed re-spacing red. The initial new dot fixture omitted a frame source, so its unchanged assertions correctly saw immediate still motion; the newly authored fixture now binds/advances ManualFrameSource. No pre-existing test changed.

New control/related App slice `rr21-app-final3.trx`: 18 passed / 0 failed / 0 skipped. Existing Appearance/rise expansion `rr21-appearance-final.trx`: 34 passed / 14 failed / 0 skipped. All 14 failures occur in unchanged SettingsRiseTests.Parts at line 85: the exact source-derived old row list is asserted for ChromeCard index 1. Option A inserts the new Colours card there. Its motion, pixel, lifecycle and input assertions must survive reconciliation; these are NOT temporary "must throw" guards and cannot be retired under the general ruling. No original reference/tolerance/assertion was edited, and the control is not hidden or moved away from the locked option-A position to evade them. Final post-order-correction receipt is reported in the overall Round-2 summary. RR-20 pack/Prime/authority completion is also a prerequisite; this row does not manufacture it.

Windows/T9: full real Settings page in both themes and Reduce Motion; actual glass/font metrics and progressive dot arrival/retarget; long maker/model names, width/scroll/no-overlap, keyboard refresh/repeated clicks, away/return lifecycle, offline/retry, progress line and real snapshot diff. The production startup/resume-wide engine lifecycle remains RR-20, not silently credited by this page's shared-engine factory. Verdict: NOT READY until the retained guard conflict and engine prerequisites are resolved.

Final control follow-up: a fifth fake-pipeline test asserts the actual 9 px maker dot beside Acme's re-spacing summary. It failed with no matching rendered dot; production suffix extraction is repaired, with the assertion untouched. `rr21-final.trx`: 35 pass/14 fail/0 skip, all five new control facts and three picker facts pass. The 14 preserved rise guards now correctly report Model colours at the conflicting card position. Build final2: zero warnings/errors.


### Round 2 - lead placement ruling applied (2026-10-07)

DONE for T9 Windows verification. OD-0127 Colours is now the LAST Appearance card, after App Zoom. ED-NEW-models-palette-9 records the lead placement ruling. SettingsRiseTests is byte-identical. Original second ChromeCard is still the four-row card. New PaletteColoursRiseTests proves Colours card index 10 hidden state, exact shared rise curve/stagger and restored transform; the five fake-pipeline option-A tests remain green. `rr21-ruling-renderer-verified.trx`: 38 passed, one Windows-only failure, zero skipped. All 17 Linux-compatible original rise tests pass; Input_reaches_the_window_while_the_wave_runs now reaches user32.dll after its preserved guards. T9 must run that input test and native option-A geometry/dot/progress comparisons. No original test/tolerance/reference was edited.


### Round 2 final refresh lifetime

DONE. OD-0127 option A remains the last Appearance card, following the existing rise rule (ED-9 and the lead ruling). The controller now borrows the one process ModelShades facade; leaving the page clears its summary and removes its listener while the engine/facade stay alive. The new fake pipeline lifetime proof passes 1/1 in round2/rr21-shared-refresh.trx, and all palette tests pass 27/27 in the post-Strike replay. ED-NEW-models-palette-15. Exact text, Refresh/Refreshing… state, UpdatesProgressBar, offline preservation and dot/Reduce Motion guards continue to pass. All fourteen locked rise guards are unchanged and pass; the original native input fact requires user32/Windows.


### Round 2 final resting-line clock correction

ED-NEW-models-palette-16 (OD-0127) fixes "Checked {when}" to use the engine's actual successful-check time, including an automatic check that republishes no palette. The two fake-pipeline cases failed before the fix and now pass. Leaving the page releases the transient no-new-models outcome; returning shows the normal age/count line. The trailing card, styles, progress, dot motion and all original rise guards remain unchanged. Latest App scope: 49/50 with only the original user32 input failure, including all 29 palette cases; Core 21/21, Services 66/66; full build zero warnings/errors. RR-21 remains DONE; native visual/Windows acceptance remains listed in the final report.
