# WL-1 wallpaper library engine

Implemented on `codex/f-wl-1`, starting at requested base `0eb930d1d6d4611ac6654564ea52b3409b465666`. The upstream pin is B3 `52bc7926097cd622d08d6294e0d8b08a48c7516e`. Native Windows verification remains INCOMPLETE / BLOCKED; this is not a whole-port parity claim. The final delivered head SHA is reported by the committing agent after the push. The adjacent evidence JSON binds the test result to SHA-256 hashes of every changed production/test file and both unchanged design JSON resources.

## Delivered behavior and counts

- 30 static wallpapers (10 in each tier) and 24 animated wallpapers (8 in each tier): **54 total**.
- Five packs each: **150 static + 120 animated = 270 packs**, **270 dark + 270 light palettes**, six colours in each palette (**3,240 source colours**).
- Two catalogue controls per wallpaper: **108 entries = 78 wallpaper options + 30 grain controls**. For static wallpapers these are the grain and the wallpaper-specific option. All user-facing names/descriptions/labels are the JSON strings.
- Both original JSON resources embedded directly; typed catalogue, mechanical GLSL-subset translation, compile-error reporting and native SKRuntimeEffect rendering. No design shader is rewritten or replaced.
- OKLab palette/theme interpolation, eased darkness/options, speed follower and phase accumulator, retained-frame crossfades on wallpaper change/clear, freeze under Reduce Motion or hidden/minimized windows, and cached idle static frames.
- Composition backdrop under the existing GlassMaterial; main and Settings windows observe the saved library choice. Upstream default remains None. Successful custom-image installation clears the library selection; library clearing can restore a retained custom picture.
- **307 new test cases**: **54 compile cases**, **240 raw-time loop proofs** (every animated pack, both themes), **13 other cases**, across **15 named test methods**. All 307 pass. The selected existing backdrop/traceability checks add **71 passes**, for **378/378**, zero skipped.
- Additional standalone CPU smoke: all **300 static pack/theme combinations** render opaque, reuse their cache and want no frames at rest. This is a manual smoke, not an extra test-suite count.

## Tests by name

- `Marrow.UI.Tests.Theme.Wallpapers.WallpaperBackdropTests.Builtin_backdrop_is_below_glass_and_uses_the_existing_opacity`
- `Marrow.UI.Tests.Theme.Wallpapers.WallpaperBackdropTests.Clearing_library_restores_the_custom_picture_and_none_retains_upstream_clear_default`
- `Marrow.UI.Tests.Theme.Wallpapers.WallpaperLibraryTests.Catalogue_has_54_wallpapers_270_dual_palettes_and_source_labels`
- `Marrow.UI.Tests.Theme.Wallpapers.WallpaperLibraryTests.Every_animated_pack_loops_and_really_moves`
- `Marrow.UI.Tests.Theme.Wallpapers.WallpaperLibraryTests.Every_shader_compiles_with_error_reported`
- `Marrow.UI.Tests.Theme.Wallpapers.WallpaperLibraryTests.Pack_crossfade_is_continuous_on_every_frame_and_retargets_from_current_colour`
- `Marrow.UI.Tests.Theme.Wallpapers.WallpaperLibraryTests.Reduce_motion_and_hidden_windows_freeze_without_resume_jump`
- `Marrow.UI.Tests.Theme.Wallpapers.WallpaperLibraryTests.Selection_defaults_to_none_and_roundtrips_without_touching_custom_image`
- `Marrow.UI.Tests.Theme.Wallpapers.WallpaperLibraryTests.Speed_change_keeps_picture_and_phase_continuous_and_is_bounded`
- `Marrow.UI.Tests.Theme.Wallpapers.WallpaperLibraryTests.Static_cache_costs_no_frames_at_rest_and_invalidates_on_inputs`
- `Marrow.UI.Tests.Theme.Wallpapers.WallpaperLibraryTests.Successful_custom_image_selection_clears_the_builtin_and_notifies_without_deleting_the_picture`
- `Marrow.UI.Tests.Theme.Wallpapers.WallpaperPlaybackTests.Options_ease_from_current_value_and_are_clamped_to_catalogue_ranges`
- `Marrow.UI.Tests.Theme.Wallpapers.WallpaperPlaybackTests.Playback_withdraws_hidden_reduced_and_disposed_requests_and_resumes_from_current_phase`
- `Marrow.UI.Tests.Theme.Wallpapers.WallpaperPlaybackTests.Static_playback_requests_zero_frames_at_rest_then_stops_after_a_pack_turn`
- `Marrow.UI.Tests.Theme.Wallpapers.WallpaperPlaybackTests.Wallpaper_switch_and_clear_crossfade_from_the_current_picture`

## Commands and authentic outcomes

Environment: Linux x64; .NET SDK 10.0.112; Avalonia 12.1.2; SkiaSharp 3.119.4. The runner executable here is `/workspace/.dotnet/dotnet`; `MARROW_PORT_DATA_DIR` names a disposable scratch directory on every test invocation. No new test uses network or a real provider CLI. No new package/reference version is introduced. All new test types are one per file; all new traits are Rule.

```sh
dotnet test tests/Marrow.UI.Tests/Marrow.UI.Tests.csproj --no-restore   --filter '(FullyQualifiedName~Theme.Wallpapers.|FullyQualifiedName~Chrome.ChromeTests.|FullyQualifiedName~Chrome.ChromeThirdTests.|FullyQualifiedName~TraceabilityTests)&FullyQualifiedName!~Native'
```

**378 passed, 0 failed, 0 skipped**, with the restored final renderer, after the options negative control. CPU raster oracle: RGBA8888, 32x20; raw t=T is compared to t=0 (<=2/255 maximum channel difference), and T/2 must differ by >=3/255. This bypasses phase wrapping. Pack/option/shader changes are sampled every 60 Hz frame, must stay within 20/255 per channel per frame, and land on the exact expected final frame. Static render and pending-frame counts are asserted separately.

```sh
dotnet build src/Marrow.App/Marrow.App.csproj --no-restore -p:EnableWindowsTargeting=true

dotnet test tests/Marrow.App.Tests/Marrow.App.Tests.csproj --no-restore -p:EnableWindowsTargeting=true   --filter '(FullyQualifiedName~WallpaperCardTests|FullyQualifiedName~WallpaperCardAuditTests|FullyQualifiedName~RootViewTests|FullyQualifiedName~SettingsViewTests)&FullyQualifiedName!~Native'
```

The **App build succeeds with 0 warnings and 0 errors**. The App tests produce **80 passed, 95 failed, 0 skipped, 175 total** on Linux. A detached checkout of the requested base (with only the same already-pattern-proven null assertion needed to compile SettingsRise.cs) produces **the exact same per-test outcomes: no outcome changed**. Failure groups include user32.dll, ICU, unavailable default fonts, and WIC-dependent custom-picture assertions/timeouts; some shell/drop/tour assertions remain unverified in this environment. This is regression comparison evidence, not 175 passing tests.

Earlier exploratory UI filters matched inherited `WallpaperStore*` and `WindowChrome*` names as well: one run reports 404 passes/49 failures (including the backdrop clock harness before its binding was corrected), another reports 395 passes/24 user32 failures. Neither is claimed green. The precise final filter above avoids accidental substring matching and proves all new WL-1 cases plus the selected existing checks.

`node porting/tools/verify.mjs --gate B`: **8 passes**. Gate A: **7 passes, 5 failures**, due to unavailable pinned Windows reference paths/evidence in this cloud checkout. The protected-file guard lists the UI csproj embedding edit; this is an intentional lead-owned edit authorized by the owner's explicit embedding instruction and documented in ED item 1. WL-1 is a directly commissioned unit, not a queue packet; no invented queue DONE/VERIFIED status is recorded.

## Test-first evidence and limit

The first 300 cases were observed failing against unimplemented APIs before the engine implementation (`wl-red.trx`: 300 failed, zero passed). The strengthened raw-time loop, scheduler and wallpaper-switch/clear run reports 243 failures before those APIs/behavior were supplied; the backdrop and successful custom-pick tests also have failing-before-implementation TRX. No expectation is lowered and no test is removed. The backdrop fixture subsequently received the real manual frame/monotonic clock setup required by the existing transition system; its assertions stayed intact.

One later options test covered interpolation that already existed for the earlier pack/theme tests, so **strict test-before-all-shared-code ordering is not met for that one method**. A deliberate options-cut regression makes its unchanged-frame assertion fail (`wl-options-negative.trx`: one failed); the mutation is restored and final green tests are rerun. This exception is recorded explicitly instead of presenting that negative control as an original implementation red.

## Engineering decisions

The append-only `## ED-396` section at the end of `porting/engineering_decisions.md` contains:

1. Qualified catalogue identity, verbatim embedded resources, internal static pack/option ids and source option ranges.
2. `wallpaperLibrary` JSON schema, lenient fallback/clamping, None default, custom-picture precedence and observable storage.
3. 0.55 s palette/theme/option and retained-picture fades, 0.18 s analytically integrated speed easing, per-window phase and freeze/resume rules.
4. Composition ownership, window visibility inputs, shared frame scheduler and persistent static/frozen caches.
5. Mechanical shader host translation, y-up coordinates, pixel/DPR/dither rules and CPU continuity/loop oracles.
6. One null-forgiving assertion on the pre-existing non-null Decorator.Child pattern in SettingsRise.cs, to resolve CS8604 and unblock compiling App. No runtime change or general warning suppression.
7. Honest evidence boundaries, the later options-test ordering exception, inherited Linux failures, and no packaging/signing/publication.

All **1,371,252 bytes of the pre-existing engineering-decisions file are retained byte-exact**, including its mixed line endings; only the new section is appended. The evidence JSON records the original prefix hash.

## Not done and why

- The final Settings Static/Animated collections, swatches, speed/options controls and thumbnails: explicitly excluded from WL-1. The engine, catalogue and saved-selection/backdrop seams are ready for that unit.
- Native Windows GPU frame cost, battery cost, simultaneous composer animation, full GlassMaterial fidelity/refraction against the design/video, real minimize/hide/occlusion behavior and WIC/custom-picker live checks: no Windows desktop/GPU in this Linux environment. CPU proofs and logical layering do not verify them.
- Full reference/visual parity and Gate A: the pinned reference lives on unavailable Windows paths. No invented substitutes or rehashed reference evidence are supplied.
- Strict test-first ordering for the later shared-options method: explained above; its failure sensitivity is verified, but the earlier ordering cannot be retroactively claimed.
- No installer, package, signing, publishing, real provider CLI call or live network test was performed. Only the explicitly requested Git branch is pushed.

The sibling `codex-wl-1-test-evidence.zip` retains raw TRX runs (red, negative control, final UI green, App candidate/base comparisons), build logs, gate logs and the standalone static smoke source/output. `codex-wl-1-evidence.json` gives counts, each test outcome and source hashes.
