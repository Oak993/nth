# Independent review: WL-4 (static wallpaper engine) and WL-5 (fidelity oracle)

Reviewer: Claude (independent; no production fixes made). Review branch `cloud/review-wl-4-5`.
Reviewed: `codex/f-wl-4` head `b95251ee8`, `codex/f-wl-5` head `d0d72fb1c`, against `cloud/f-wl-3` head `f8fbb675d`, the locked snapshot
`porting/first-release/design-src/wallpaper-library/{static,animated}`, OD-0110 and the ED-NEW blocks of each branch.

## Verdicts

| Branch | Verdict |
| --- | --- |
| `codex/f-wl-4` | **FIX FIRST** (the engine's colour and focus math is faithful; items 1 to 5 below must be settled before it merges) |
| `codex/f-wl-5` | **FIX FIRST** (the oracle is genuine, but it compares zero static pixels against WL-4 and its coverage is narrow; items 6 to 10) |

## How this was checked (what actually ran)

- SDK: `dotnet-sdk-10.0` 10.0.112 from apt (the Microsoft download host is denied by policy here); Linux x64. Builds used
  `-p:TreatWarningsAsErrors=false -p:EnableWindowsTargeting=true`; every filter ended `&FullyQualifiedName!~Native`.
- WL-4 head, `Marrow.UI.Tests`, filter `Theme.Wallpapers.`: **506 passed, 268 failed, 0 skipped, 774 total**: the report's own numbers, reproduced.
  Failures: 240 `Every_animated_pack_loops_and_really_moves`, 24 `Every_shader_compiles_with_error_reported`, and one each of
  `Static_cache_costs_no_frames_at_rest_and_invalidates_on_inputs`, `Speed_change_keeps_picture_and_phase_continuous_and_is_bounded`,
  `Playback_withdraws_hidden_reduced_and_disposed_requests_and_resumes_from_current_phase`, `Reduce_motion_and_hidden_windows_freeze_without_resume_jump`.
  All but the static one are the animated shaders' undeclared `u_tilt`/`u_focus` (WL-3's work). UI and App `TraceabilityTests`: 3/3 each.
- Oracle regeneration: `node tests/Marrow.UI.Tests/Fixtures/StaticWallpaperOracle.cjs` rewrites `static-wallpaper-page.json` **byte-identical**
  (SHA256 `7491981d...408c`). The design snapshot is byte-identical to `e5b455fe9` (`git diff` over `design-src` is empty); `static/wallpapers.json`
  SHA256 `55aa56f1...5803` as WL-4 states. `StaticWallpaperOracle.cjs` executes the page's own `sw-color.js` and `sw-gl.body.js` (`vm.runInNewContext`) and the
  page's own `Viewer.prototype.state/tick`: it is not a re-derived copy. Not run here: `tools/wallpaper-fidelity/generate.mjs` (needs Playwright, pngjs, a Chromium and an npm install).
- **WL-4 + WL-5 merged** (scratch merge; only `porting/STATE.md` and `engineering_decisions.md` conflict, text only): `Marrow.App.Tests` filter `Marrow.App.Tests.Wallpapers`:
  **5 passed, 0 failed, 4 skipped**. All four fidelity theories (2 static, 2 animated) are `NotExecuted`. Zero pixels were compared.
- **WL-3 + WL-4 merged**: `git merge origin/codex/f-wl-4` into `cloud/f-wl-3` stops with **9 content conflicts**: `WallpaperCatalogue.cs`,
  `WallpaperDefinition.cs`, `WallpaperLibraryStore.cs`, `WallpaperRenderer.cs`, `WallpaperSnapshot.cs`, `WallpaperView.cs`, `WallpaperVisualHandler.cs`,
  `WallpaperLibraryTests.cs`, `engineering_decisions.md`.

## What is correct (read against the page, line by line)

- `StaticWallpaperPalette.LookAt/HomeLook/Dynamic/MatchAccent/ResolvePack/LchToLab/LchToHex` equal `sw-color.js` (`lookAt`, `homeLook`, `dynDelta`, `shiftPal`,
  `accentSpec`, `resolvePack`, `lch2lab` 22 steps, `lch2hex` 24 steps, hex rounding, both warm-dark branches). `StaticWallpaperController` followers: tau .16/.09/.09/.09/.16/.16/.16/.18/.18, snap .002/.002/.002/.002/.002/.004/.002/.0015/.0015,
  tilt re-bake at 1/16 px, `AMP * depth * persp`, y negated, pointer length capped at 1, `FOCUS_SIG` .022 times pixel height, `WASH` .028: all match `sw-gl.body.js`.
  The static header declares exactly the page contract's twelve uniforms; `contract.lib + src + contract.main` is embedded verbatim; `gl_FragCoord` is mapped as `(coord.x, u_res.y - coord.y)`.
  (`u_sun`, `u_px`, `u_pan` belong to the animated host, not to this one.)
- Static at rest: no frames at rest, none for a stable window position, none for disabled options (their seams are not read), Reduce Motion hard-zeroes the tilt; the minute timer exists only while Dynamic is on and the window is visible.

## Findings, WL-4 (`codex/f-wl-4`)

1. **[blocking, integration] WL-3 and WL-4 are two incompatible implementations of the same seams; they cannot both merge.** 9 conflicts (above). The designs differ in kind:
   WL-3 injects `WallpaperEnvironment(TimeProvider, IWallpaperAccentSource, IWallpaperWindowPosition)` and persists `Dynamic`/`Persp`/`Focus` as fields of `WallpaperSelection`
   (`wallpaperLibraryChoices` key, `Recall`); WL-4 injects `StaticWallpaperInputs(TimeProvider, Func<string>, Func<Vector>)`, keeps the same three choices in `Options["dynamic"|"persp"|"focus"]`
   (`wallpaperLibraryStatic` key, `Select`). Two wake mechanisms (`WallpaperWakeSource` for animated only, `WallpaperView` minute timer for static only); two window-position normalisations
   (WL-3 `screen.Bounds`, WL-4 `screen.WorkingArea`: `WallpaperView.cs:72`); two persistence keys, so WL-2's settings page needs two stores. WL-3's ED item 11 deliberately departs from the page to avoid brown
   on the `dark_warm` falling edge; WL-4 keeps the page's brown (finding 3), so the two collections will disagree on the owner's own "no brown" rule. Failure scenario: merge either first and the other cannot rebase without choosing a schema;
   WL-5's bridge (below) is written for WL-3's schema, so choosing WL-4's silently skips the oracle. Needs one lead decision on the shared seam and the persistence schema before either lands.
2. **[medium] An interrupted palette fade does not follow the page.** `StaticWallpaperController.cs:60` divides every fade by .55 with smoothstep. `sw-gl.body.js` `target()` sets `palD = palK < 1 ? 0.3 : 0.55` and eases a retarget with
   `1 - (1 - k)^3`. ED item 3 records this as "the explicit WL-4 instruction", but it is not an owner decision (no OD cites it), the page is the locked authority, and an accent drag (the case the page names) is exactly the retarget case.
   Test: `REVIEW_a_palette_retarget_during_a_running_fade_follows_the_pages_point_three_second_ease_out` (fixture `REVIEW_static-retarget.json`, produced by running the page's own Viewer): fails at the first step after the retarget (palette[0] page .299915, port .300297).
3. **[medium, source conflict carried verbatim] Dynamic produces brown dark grounds.** `shiftPal` does not reapply `resolvePack`'s warm rule; WL-4 measured 76 strict-band ground states of 5400 (17 authored plus 59 accent) and
   **asserts the count as the expected result** (`StaticWallpaperTests.cs:138`, `Assert.Equal(new[] { 17, 59 }, warm)`), so the defect is pinned green. OPTIONS-FORMAT says "never muddy: no brown" and Match accent keeps "the dark-mode brown rules".
   `REVIEW_no_dynamic_dark_ground_is_brown_at_any_key_hour` samples 90 wallpapers x 5 packs x 9 hours on authored packs only: **27 brown grounds**, e.g. `corolla/Blush@3: L .223 C .027 h 20.8`, `satin/Rose Gold@12: L .201 C .049 h 21.7`.
   The fix belongs in the page's `shiftPal` (design source, owner/lead), as WL-3's ED item 11 says for its own twin; WL-4 must not diverge silently. Until then this is an open, owner-visible defect.
4. **[medium] A known-red test is left in the tree.** `WallpaperLibraryTests.Static_cache_costs_no_frames_at_rest_and_invalidates_on_inputs` (line 154; the 40/60 s idle assertion at ~163) fails.
   WL-4's reasoning is right (page dark follower .16 s, snap .002: idle at ~.994 s, not .667 s). Leaving it red is not "no weakening", it is a stale expectation: amend it to the page's number under a recorded ED (a correction to the page truth, with the arithmetic) rather than carry a permanent failure into a merge.
5. **[low] Dynamic clock has second resolution; the page has minute resolution.** `StaticWallpaperController.cs:36` uses `TimeOfDay.TotalHours`; `sw-page.js:26` uses `getHours() + getMinutes()/60`.
   At 15:00:59 the palette differs from the page's 15:00 by up to 1.9e-4 per OKLab component (17 of 18 values exceed 1e-5). `REVIEW_the_dynamic_clock_has_the_pages_minute_resolution_not_seconds`.
   (Low: the minute timer fires on the boundary, so the steady state agrees; only a mid-minute start or an unrelated `Set` shows it.)

   Also (note, no test): the static test classes carry only `Rule R-TEST-1`; none carries a `Feature` trait for the build unit (`REVIEW_every_static_wallpaper_test_class_names_its_build_unit` fails for the 5 classes), so a unit's tests cannot be located.
   And `StaticWallpaperTests.Every_approved_static_shader_compiles_and_rasters` asserts `Contains(frame, b => b != 0)` over RGBA bytes, whose alpha is always 255: it cannot fail on a black picture.
   The review added `REVIEW_every_static_wallpaper_renders_a_picture_that_is_neither_black_nor_flat_...` (90 cases, dark/light, plain and every option on) as a real check; **all 90 pass today**, so this is a weak assertion, not a rendering defect.

## Findings, WL-5 (`codex/f-wl-5`)

6. **[blocking] The static fidelity gate is shut against WL-4; the "oracle" executes nothing.** `WallpaperFidelityTheoryAttribute` converts a missing binding into an xUnit `Skip` at discovery, so the run is green ("Passed! 8, Skipped 4"; the report's own `metricComparisons: 0`).
   With WL-4 merged, `WallpaperRendererBridge.SkipReason` returns "WL-1 has not landed..." because `RendererConstructor` (`WallpaperRendererBridge.cs:18`) accepts only 2 or 3 parameters whose third is `WallpaperEnvironment`, while WL-4's constructor is `(WallpaperSelection, bool, StaticWallpaperInputs?)`;
   the premium gate additionally needs selection parameters named `dynamic`/`focus` (WL-4 keeps them in `Options`) and a `Premium`/`StaticPremium` definition property (WL-4 exposes `Static`) (`WallpaperRendererBridge.cs:35, 45`).
   ED item 4 says a future WL-4 contract "must be bound explicitly in this test-only bridge before its gate can open"; that binding was not done, and a skip is not fidelity. A gate that can only skip should fail instead (the report's own wording: "without accepting skips").
   Tests: `REVIEW_the_static_default_fidelity_gate_is_open_against_the_WL_4_renderer`, `REVIEW_the_static_premium_fidelity_gate_is_open_against_the_WL_4_renderer` (both fail with the skip reason).
7. **[medium] Perspective is never compared pixel-wise.** Every case is generated with `tilt: [0, 0]` (`generate.mjs:155`) and the bridge feeds position (0, 0); `WallpaperReferenceIntegrityTests.cs:50-52` asserts that constant. `u_tilt * u_tg`, the only static Perspective path, has no reference.
   `REVIEW_some_static_reference_is_rendered_with_a_non_zero_tilt` fails.
8. **[medium] Match accent, Dynamic and Focus are each compared at one point.** One accent (`#005FB8`, `generate.mjs:153`), one clock hour (18, a key hour where the smoothstep legs are flat, so inter-key interpolation is never compared), Focus 0 and 1 only (ED item 2 concedes the 0.1 and 0.05 defaults and intermediate sigmas are uncovered).
   The integrity test pins all three constants (`WallpaperReferenceIntegrityTests.cs:50-53`), so widening the matrix will first fail that test; that is an expansion, not a weakening. Tests: `REVIEW_match_accent_is_compared_at_more_than_one_accent`,
   `REVIEW_dynamic_is_compared_at_an_hour_between_two_key_looks`, `REVIEW_focus_is_compared_at_an_intermediate_value`.
9. **[low] Tolerances are a priori, not measured.** Mean OKLab dE <= .010, maximum <= .080 were "chosen before app comparisons" (ED item 3) and described as not calibrated. Review result: a flat 3-level error over a mid-grey picture (dE .0126) is rejected, so the mean limit is not provably loose
   (`REVIEW_a_uniform_three_level_colour_error...` is a passing guard); but the .080 maximum is about twenty times the dither amplitude and nothing records a measured noise floor between CPU Skia and SwiftShader. No comparison has ever run, so no floor has been measured. Measure it on the first real run and record it before accepting a pass.
10. **[low] The compact subset drops cases by size, not by risk.** The full matrix (2808 cases, 42 MB) was reduced to 1278 to fit a 25 MB target; omitted: quiet light, rich dark, all premium light. Light-theme Dynamic and Match accent (where light-hue arrays and the light chroma path run) are therefore never compared. Reasonable as a first cut, but it should be stated against the gate's verdict.

   Evidence on the WL-3 side (animated), for the lead: WL-5's own compatibility run recorded 623 matching and 113 mismatching of 736 comparisons against WL-3 `510919ce`, and 272 render errors; `reports/wl5-fidelity-evidence/wl3-*.trx`. Fresh run (WL-5 harness copied onto `cloud/f-wl-3` f8fbb675d, `Marrow.App.Tests`, fidelity theories): **131 passed, 103 failed, 1 skipped, 235 total**; of 271 printed comparisons 157 are within the mean limit and 114 are over it
(worst mean dE .300), and the static theories fail (WL-3 has no static catalogue). The animated half of the oracle therefore does run, and finds real WL-3 mismatches (for example `animated/kiln/rich/jun/light/t=45` mean .088, max .325); that is a WL-3 FIX FIRST item for its own review, not scored here.
Measured noise floor: matching animated renders show mean dE about .0006 to .0007 and max .03 to .06 (e.g. `nocturne`), so the a-priori .010 mean limit is roughly 15 times the floor (finding 9: measure, then tighten).

## REVIEW tests added (no production code changed)

| Test | Project | Result at the review base |
| --- | --- | --- |
| `REVIEW_StaticWallpaperPageParityTests.REVIEW_a_palette_retarget_during_a_running_fade_follows_the_pages_point_three_second_ease_out` | UI.Tests | FAIL (finding 2) |
| `...REVIEW_no_dynamic_dark_ground_is_brown_at_any_key_hour` | UI.Tests | FAIL, 27 grounds (finding 3) |
| `...REVIEW_the_dynamic_clock_has_the_pages_minute_resolution_not_seconds` | UI.Tests | FAIL (finding 5) |
| `...REVIEW_every_static_wallpaper_test_class_names_its_build_unit` | UI.Tests | FAIL, 5 classes |
| `REVIEW_StaticWallpaperRenderTests.REVIEW_every_static_wallpaper_renders_a_picture_that_is_neither_black_nor_flat_...` | UI.Tests | PASS x90 (guard) |
| `REVIEW_WallpaperFidelityGateTests.REVIEW_the_static_default_fidelity_gate_is_open_against_the_WL_4_renderer` | App.Tests | FAIL (finding 6) |
| `...REVIEW_the_static_premium_fidelity_gate_is_open_against_the_WL_4_renderer` | App.Tests | FAIL (finding 6) |
| `...REVIEW_some_static_reference_is_rendered_with_a_non_zero_tilt` | App.Tests | FAIL (finding 7) |
| `...REVIEW_match_accent_is_compared_at_more_than_one_accent` | App.Tests | FAIL, 1 accent (finding 8) |
| `...REVIEW_dynamic_is_compared_at_an_hour_between_two_key_looks` | App.Tests | FAIL (finding 8) |
| `...REVIEW_focus_is_compared_at_an_intermediate_value` | App.Tests | FAIL (finding 8) |
| `...REVIEW_a_uniform_three_level_colour_error_over_the_whole_picture_is_not_within_tolerance` | App.Tests | PASS (guard) |

All carry `Rule R-TEST-1`, `Feature` and `Review` traits (WL-4 or WL-5), one type per file; UI and App `TraceabilityTests` pass with them. Fixture generator: `tests/Marrow.UI.Tests/Fixtures/REVIEW_StaticWallpaperRetarget.cjs` (runs the page's own code).

## Base of this branch

The tests for the two units need both units' code, so the review branch's base is a scratch merge of `codex/f-wl-4` and `codex/f-wl-5` (text-only conflicts in `STATE.md` and `engineering_decisions.md` resolved to WL-4's side; nothing else touched). The single review commit on top contains only the REVIEW files and this report.
