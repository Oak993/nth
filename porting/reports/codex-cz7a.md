# CZ-7a delivery — 2026-10-05

Branch: `codex/f-cz-7a`. Frozen/final checked CZ-5 head: `476fd501bfb038ac2dd4a105a1747610e9fb270e` (started at `4e87ef7f`, then merged its newer head). Implementation SHA is the commit containing this report, supplied in the delivery reply.

Implemented: the nine Voice orb card rows (eight choices and the exact note), 24 options, seven requested spec test IDs, the settings writes/reset, real orb tiles and transitions. CZ-7b's live stage and demo are outside this unit; `PreviewSession.VoicePreviewChangeRequested` is the documented seam. No changes to settings defaults or wire schema. No packaging, signing, publishing or installer execution.

## Test results

.NET SDK 10.0.100, Linux cloud executor. Every `dotnet test` filter ends in `&FullyQualifiedName!~Native`. Test data uses a throwaway `MARROW_PORT_DATA_DIR`. No test invokes a network, CLI, microphone or download. In the new App tests only Decision traits are used (ED-251 and ED-237); each new type has its own file and fixtures are under the project's Fixtures directory.

| Run | Passed | Failed | Skipped | Total |
|---|---:|---:|---:|---:|
| red | 0 | 7 | 0 | 7 |
| focused-final | 12 | 0 | 0 | 12 |
| app-final | 137 | 30 | 0 | 167 |
| base-app | 130 | 30 | 0 | 160 |
| core | 579 | 0 | 0 | 579 |
| ui-broad | 337 | 0 | 0 | 337 |
| ui-final | 40 | 0 | 0 | 40 |

The red run preceded production implementation: all seven methods failed because the Voice orb section was absent. Tests were not weakened. The unchanged language persistence assertion subsequently caught a pending coalesced override; the discrete language pick now commits both keys. The two inherited section-order tests were expanded to require Voice orb as well as every previous section. Three nullable annotations were repaired so final builds retain warnings-as-errors; the untouched base run needed `-p:TreatWarningsAsErrors=false` to get past its inherited CS8604/CS8619 warnings.

The focused final run includes the seven new tests, two expanded section-order tests and all three App TraceabilityTests. All pass. Core's selected voice/text tests include TraceabilityTests and all pass. All 337 selected UI voice tests passed; after the final tile render-bounds adjustment the 40 directly affected scene/view/renderer and Traceability tests were rebuilt and passed again. The broad App run has exactly the same 30 failed names as the untouched base; there are no additional failed names. Font disposal/layout failures are 26 of the base failures. They remain outside this unit. The new tests use a scoped fixture to prime faces, drain queued layout before font disposal, and script the existing legacy switch's platform reduce-motion read; voice-row motion is still checked on its manual frame clock.

Full test names and outcomes by run: [codex-cz7a-tests.tsv](codex-cz7a-tests.tsv). These counts are test cases, not port-wide requirement verification or an overall completion percentage.

### New test methods (all pass)

- `CZ_T70_T90_Exact_row_order_options_details_and_note`
- `CZ_T71_All_picks_write_the_documented_voiceOrb_JSON`
- `CZ_T72_Tiles_use_real_52px_views_30px_balls_selected_glow_and_wrapping_arrows`
- `CZ_T91_Every_language_pick_including_current_clears_override_and_keeps_other_settings`
- `CZ_T93_Dimmed_rows_are_inert_unfocusable_keep_values_and_return`
- `CZ_T97_Listed_unlisted_Windows_language_and_explicit_override_details`
- `Row_detail_and_dimming_crossfade_on_the_frame_clock`

### Windows-only failures also present at CZ-5

- `Marrow.App.Tests.Port.Settings.ComposerPreviewPixelsTests.The_restated_ring_chip_end_and_send_ink_draw_as_the_real_controls_do` — missing `icu.dll` on Linux; also failed on base.
- `Marrow.App.Tests.Port.SettingsEscapeTests.A_paragraph_text_menu_closed_otherwise_leaves_the_next_escape_to_the_window` — missing `icu.dll` on Linux; also failed on base.
- `Marrow.App.Tests.Port.SettingsEscapeTests.Escape_while_a_paragraph_text_menu_is_open_closes_the_menu_and_the_window_stays` — missing `icu.dll` on Linux; also failed on base.
- `Marrow.App.Tests.Port.SettingsRiseTests.Input_reaches_the_window_while_the_wave_runs` — missing `user32.dll` on Linux; also failed on base.

### Other unchanged base failures

- `Marrow.App.Tests.Port.Settings.CustomizationAuraRowsTests.A_colours_pick_writes_the_scheme_and_Custom_opens_the_wells` — headless font initialization/disposal.
- `Marrow.App.Tests.Port.Settings.CustomizationAuraRowsTests.A_well_opens_the_glass_picker_and_a_pick_is_stored_with_its_alpha` — headless font initialization/disposal.
- `Marrow.App.Tests.Port.Settings.CustomizationAuraRowsTests.Every_uiverse_row_says_what_the_design_says_in_every_fixture_case` — headless font initialization/disposal.
- `Marrow.App.Tests.Port.Settings.CustomizationAuraRowsTests.The_colours_detail_follows_the_preview_maker_and_the_accent` — headless font initialization/disposal.
- `Marrow.App.Tests.Port.Settings.CustomizationAuraRowsTests.The_rim_and_dot_rows_dim_with_their_switches` — headless font initialization/disposal.
- `Marrow.App.Tests.Port.Settings.CustomizationAuraRowsTests.The_uiverse_sliders_have_the_designs_ranges_and_write_what_they_show` — headless font initialization/disposal.
- `Marrow.App.Tests.Port.Settings.CustomizationBeamRowsTests.A_stored_corner_above_the_sliders_end_shows_at_the_end_and_is_not_rewritten` — headless font initialization/disposal.
- `Marrow.App.Tests.Port.Settings.CustomizationBeamRowsTests.Auto_shows_the_resolved_value_moving_stores_and_the_capsule_toggles` — headless font initialization/disposal.
- `Marrow.App.Tests.Port.Settings.CustomizationBeamRowsTests.Colour_drift_is_dimmed_and_inert_where_it_does_nothing` — headless font initialization/disposal.
- `Marrow.App.Tests.Port.Settings.CustomizationBeamRowsTests.Every_beam_row_says_what_the_design_says_in_every_fixture_case` — headless font initialization/disposal.
- `Marrow.App.Tests.Port.Settings.CustomizationBeamRowsTests.Hold_colours_still_is_forced_on_and_inert_under_mono` — headless font initialization/disposal.
- `Marrow.App.Tests.Port.Settings.CustomizationBeamRowsTests.Motion_sets_the_families_first_shape_and_the_shape_sets_swap` — headless font initialization/disposal.
- `Marrow.App.Tests.Port.Settings.CustomizationBeamRowsTests.The_colours_row_has_the_eight_palettes_and_a_pick_writes_one` — headless font initialization/disposal.
- `Marrow.App.Tests.Port.Settings.CustomizationBeamRowsTests.The_speed_slider_stores_the_values_it_shows` — headless font initialization/disposal.
- `Marrow.App.Tests.Port.Settings.CustomizationMotionTests.The_pages_row_transitions_follow_their_curves_on_the_frame_clock` — headless font initialization/disposal.
- `Marrow.App.Tests.Port.Settings.CustomizationMotionTests.Under_reduce_motion_every_transition_lands_at_once` — headless font initialization/disposal.
- `Marrow.App.Tests.Port.Settings.CustomizationPageTests.The_stage_follows_the_apps_settings` — headless font initialization/disposal.
- `Marrow.App.Tests.Port.Settings.CustomizationPersistenceTests.A_drag_does_not_write_the_store_every_step_and_its_end_writes_the_last_value` — headless font initialization/disposal.
- `Marrow.App.Tests.Port.Settings.CustomizationPersistenceTests.Every_page_change_reaches_a_second_composer_and_the_store` — headless font initialization/disposal.
- `Marrow.App.Tests.Port.Settings.CustomizationSectionsTests.Border_beam_reset_is_idle_at_the_defaults_and_resets_everything` — headless font initialization/disposal.
- `Marrow.App.Tests.Port.Settings.CustomizationSectionsTests.Off_shows_the_note_alone_and_both_styles_keep_their_settings` — headless font initialization/disposal.
- `Marrow.App.Tests.Port.Settings.CustomizationSectionsTests.The_sections_rows_folds_and_dividers_are_the_designs_in_every_state` — headless font initialization/disposal.
- `Marrow.App.Tests.Port.Settings.CustomizationSectionsTests.The_style_cards_are_a_radio_group_and_a_pick_writes_the_style_keeping_the_others_settings` — headless font initialization/disposal.
- `Marrow.App.Tests.Port.Settings.CustomizationSectionsTests.Uiverse_reset_returns_every_setting_and_the_hidden_wells_follow` — headless font initialization/disposal.
- `Marrow.App.Tests.Port.SettingsEscapeTests.Escape_with_a_hydra_pair_editor_open_closes_it_and_the_window_stays` — headless font initialization/disposal.
- `Marrow.App.Tests.Port.SettingsEscapeTests.Escape_with_the_hydra_cookbook_popover_open_dismisses_it_and_the_window_stays` — headless font initialization/disposal.

## Commands

All commands below run from the repository with `/workspace/dotnet` on PATH and `MARROW_PORT_DATA_DIR` pointing to a throwaway directory. `EnableWindowsTargeting` enables compilation on Linux; it does not emulate Windows APIs.

```sh
dotnet test tests/Marrow.App.Tests/Marrow.App.Tests.csproj -p:EnableWindowsTargeting=true --filter '(FullyQualifiedName~CustomizationVoiceRowsTests|FullyQualifiedName~TraceabilityTests|FullyQualifiedName~The_page_holds_the_stage_then_Style_then_the_style_sections_in_order|FullyQualifiedName~The_section_sits_at_the_end_of_the_sticky_container_after_the_style_sections)&FullyQualifiedName!~Native'
dotnet test tests/Marrow.App.Tests/Marrow.App.Tests.csproj --no-build -p:EnableWindowsTargeting=true --filter '(FullyQualifiedName~Marrow.App.Tests.Port.Settings|FullyQualifiedName~TraceabilityTests)&FullyQualifiedName!~Native'
dotnet test tests/Marrow.Core.Tests/Marrow.Core.Tests.csproj --filter '(FullyQualifiedName~Marrow.Core.Tests.Port.Voice|FullyQualifiedName~CzTextTests|FullyQualifiedName~TraceabilityTests)&FullyQualifiedName!~Native'
dotnet test tests/Marrow.UI.Tests/Marrow.UI.Tests.csproj -p:EnableWindowsTargeting=true --filter '(FullyQualifiedName~Marrow.UI.Tests.Port.Voice|FullyQualifiedName~TraceabilityTests)&FullyQualifiedName!~Native'
dotnet test tests/Marrow.UI.Tests/Marrow.UI.Tests.csproj -p:EnableWindowsTargeting=true --filter '(FullyQualifiedName~OrbSceneTests|FullyQualifiedName~VoiceOrbViewTests|FullyQualifiedName~OrbRendererTests|FullyQualifiedName~TraceabilityTests)&FullyQualifiedName!~Native'
```

The base App command was the broad App filter, without `--no-build` and with `-p:TreatWarningsAsErrors=false`, in a detached worktree at 476fd501. Raw TRX and console logs are retained in `/workspace/cz7a-evidence`; the committed TSV records every named result. TRX SHA-256 values:

- red: `fbfe6b42bf3abb8a181f9e752481999d8fe9480e067863cfdf9a7c1085ae42a3`
- focused-final: `0ec285355c9462efa821a2481c4eb285fd6592062d3e51c706018986c9f54b4a`
- app-final: `207c940554884eec266345d4b159bc35e5563955e03bef4e2829f4b74c164293`
- base-app: `98bb109472a71116be1e4d2db72234f9446550f8ca510956af8f77ac04d660cd`
- core: `ba1d3b6c439516a2f5d6d2bc9b9589446495a6974c2379c0bdfb953ac1655267`
- ui-broad: `381034ec66002821421814c00aa4c74cdaad6dea13911daaefb793a3002f696b`
- ui-final: `3ee293122ddac36ecfaa4e8cab328359fac5668e66a52ac8db1c8960c3aafa7b`

## Decisions and remaining work

Eight decisions are appended under [ED-347](../engineering_decisions.md#ed-new-codex-cz-7a): (1) card/place, (2) copy/defaults/language detail, (3) opt-in row layout and silent menu echo, (4) real tile frame/glow, (5) CZ-7b event protocol, (6) observation and paired language commit, (7) inherited annotation repairs and strengthened section-order checks, (8) test fixtures/evidence/limits. The entire pre-existing engineering_decisions.md byte prefix is preserved; append uses the existing LF endings. No owner decision, specification or design source was edited.

A separate adversarial pass checked all voice copy (88 literal occurrences found in the locked spec), row order/layout/dimming, echo suppression, same-option language reset, frame dimensions and seam timing. No second reviewer is claimed.

`node porting/tools/verify.mjs --gate B`: 8 pass. Gate A: 7 pass, 5 fail because this executor lacks `E:/Droppy/reference/droppy-code-v1.7.0` (pin/tree/clean-checkout/inventory/hash checks); not claimed passed. `check_protected.mjs`: before commit it flagged the user-requested engineering-decisions append and the two delivery-report files as orchestrator-owned protected paths. These are intentional lead-authored documentation changes, with that reason recorded in the commit; no protected contract, owner decision, specification, design, project or gate policy was changed. After commit it reports protected paths unchanged against HEAD. Its attempts to inspect the absent Windows reference checkouts print missing-path errors, so reference cleanliness is unverified. CZ-7a is a first-release build unit, not a T-* queue packet accepted by `mark.mjs`; no legacy packet status or parity matrix is fabricated.

**Not done:** CZ-7b live preview, by assignment; CZ-8 Speech, by assignment; Windows desktop/native/render comparison, this executor is Linux; Gate A reference checks, absent pinned Windows reference directories; the 30 inherited App failures. No installer or release artifact was requested or produced. Port-wide status remains INCOMPLETE / BLOCKED; this delivery does not claim verified exact parity of the whole app.
