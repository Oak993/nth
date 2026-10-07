# T21 — authorized Aurora artwork expectations

The lead's 2026-10-07 instruction supersedes the stale-baseline blocker in
T21-REPORT.md and ED-401. OD-0112 replaces the artwork itself; the
updated expectations encode its locked Aurora outputs. Remaining Windows
coverage and two unresolved toast cases are listed exactly below. The full
Windows suite is not claimed green; overall verification remains **NOT READY**.

First merged `origin/claude/verify-terminal-20261001` at
`285b6b7a`; the merge added only the lead's owner-decision entry. Work remains
on `codex/app-icon-d`. No shipping asset or locked design source byte changed.

Only five test files changed:

- `AboutIdentityIconTests.cs`: `ApprovedSha256` for the App PNG.
- `MarrowLogoCheck.cs`: literal App PNG hash and four artwork sample locations/values.
- `SwiftImageTests.cs`: the two UI PNG hash arguments.
- `AssetCatalogueManifest.json`: only the two shipped UI PNG hashes; original
  deviation ID, upstream hashes and all other entries are unchanged.
- `MarrowLogoArtworkTests.cs`: the same four artwork samples as the shared App helper.

Related comments identify OD-0112 and the generator. All assertions, comparison
expressions, pixel decoding, dimensions, interpolation, accessibility, original
upstream exclusions and numeric bounds/tolerances remain intact. In particular,
the brightness thresholds remain A>=250/R>=214/G>=220/B>=240; the sample moves
to Aurora's actual saturated curtain glint at (0.88,0.67). The three `Near`
calls use the same 6/10 tolerances. Corner, edge and dark-tile samples are unchanged.
Tests pin checked-in literal expectations and do not run or require the generator.

**ED-402** lists every changed hash expectation and every changed pixel
sample with old/new source PNG hashes. It also records old/new SHA-256 hashes of
the canonical sample expectation JSON, with its exact encoding. Its 6,544 bytes
are a pure append; the ledger's prior 1,788,729 bytes are untouched.

The generator now pins **Chromium 151.0.7922.173** by name and exact version,
records Linux/x86_64 and Playwright 1.62.0, and rejects other browser names or
versions. The manifest retains the source/tooling/output hashes and seven
accepted still oracle hashes. `--check` compares provenance as well as outputs.
All ten size PNGs and four shipping assets remain byte-identical to the prior
T21 commit. The manifest is not a runtime test dependency.

## Results

- Generator regeneration and `--check`: **PASS**, 15 output files; seven still
  references byte-identical. No renderer reference was updated.
- UI artwork/catalogue/SwiftImage run: **39 passed, 0 failed, 0 skipped**.
- Exact App artwork consumers plus WindowsToastStartupTests: **17 passed,
  0 failed, 0 skipped**, with isolated scratch data.
- App test assembly build: **PASS**, zero warnings/errors. The first two-minute
  attempt expired during compilation; a separate build completed in 54.35 s.
- Final `dotnet build Marrow.sln -p:EnableWindowsTargeting=true`: **PASS**, zero
  warnings/errors, 27.19 s.
- Wider App icon-adjacent run: **156 passed, 27 failed, 0 skipped**. Twenty-five
  failures explicitly load unavailable `user32.dll` or `icu.dll`; two unchanged
  background-toast assertions fail separately. No affected artwork assertion fails.
- Mechanical source check: assertion statements in both pixel helpers are
  identical after normalizing the one replaced App SHA literal. All numeric
  bounds/tolerances match. Hash arguments and sample data are the only semantic
  test changes. Mixed line endings are preserved in the manifest and ledgers.
  `git -c core.whitespace=cr-at-eol diff --check` passes; the default whitespace
  check treats the preserved CR on the two changed JSON lines as whitespace.

The wider filter was the union of MarrowLogo, AboutIdentityIconTests,
NewThreadPrompt, RootViewWelcomeTests, SettingsRestAboutTests, SettingsViewTests
and WindowsToast, each excluding Native. Both runs set MARROW_PORT_DATA_DIR to
a disposable `/tmp` directory and used .NET SDK 10.0.401 with
`-p:EnableWindowsTargeting=true`.

Every changed-artwork consumer passed: AboutIdentityIconTests; both
MarrowLogoPlacesTests; MarrowLogoSettingsRowTests; RootViewWelcomeTests' app-icon
case; NewThreadPromptTests' accessibility/app-icon case; the icon/geometry case
in NewThreadPromptGeometryTests; SettingsRestAboutTests' attribution case; and
SettingsViewTests' product/attribution case. None is left untested because it
requires Windows.

## Exact remaining Windows runs

The following is the exact case list from the wider run's TRX, not a speculative
list of all Windows-dependent tests in the repository. Re-run these with an
isolated Windows MARROW_PORT_DATA_DIR. The prior full solution Linux run remains
incomplete; separately run `dotnet test Marrow.sln --filter "FullyQualifiedName!~Native"`
on Windows for full suite acceptance.

### Cases blocked by Windows DLLs

- `Marrow.App.Tests.Views.Chat.NewThreadPromptProjectPickerTests.Choosing_a_folder_adds_the_project_and_moves_the_thread_into_it`
- `Marrow.App.Tests.Views.Chat.NewThreadPromptProjectPickerTests.Choosing_several_folders_adds_them_all_and_moves_the_thread_into_the_last`
- `Marrow.App.Tests.Views.Chat.NewThreadPromptProjectPickerTests.Clicking_another_project_closes_the_popover_and_moves_the_empty_thread_there`
- `Marrow.App.Tests.Views.Chat.NewThreadPromptTests.Choosing_a_different_project_moves_the_thread_and_closes_the_popover`
- `Marrow.App.Tests.Views.Chat.NewThreadPromptTests.The_prompt_fades_in_on_the_frame_source_in_force_when_it_attaches_and_a_new_name_does_not_fade_it_again`
- `Marrow.App.Tests.Views.Settings.SettingsViewTests.About_Show_replays_all_eight_tour_pages_even_when_the_tour_was_already_seen`
- `Marrow.App.Tests.Views.Settings.SettingsViewTests.About_update_dot_is_conditionally_attached_to_the_icon_with_the_source_circle_and_offset`
- `Marrow.App.Tests.Views.Settings.SettingsViewTests.Droppy_attribution_link_and_legal_document_titles_are_exact`
- `Marrow.App.Tests.Views.Settings.SettingsViewTests.Finish_chime_previews_only_off_to_on_including_external_changes`
- `Marrow.App.Tests.Views.Settings.SettingsViewTests.General_preview_transitions_are_not_replayed_on_equal_writes_and_usage_switch_is_last`
- `Marrow.App.Tests.Views.Settings.SettingsViewTests.General_settings_persist_before_closing_without_commit_or_cancel_buttons`
- `Marrow.App.Tests.Views.Settings.SettingsViewTests.Header_override_wins_over_auth_default_and_collapsed_content_stays_mounted`
- `Marrow.App.Tests.Views.Settings.SettingsViewTests.Legal_popover_inherits_presented_chrome_with_all_missing_resource_names`
- `Marrow.App.Tests.Views.Settings.SettingsViewTests.Licences_are_read_once_and_the_three_markdown_blocks_use_fixed_popover_geometry`
- `Marrow.App.Tests.Views.Settings.SettingsViewTests.Marks_edit_is_disabled_without_projects_and_opens_the_400_wide_list_in_library_order`
- `Marrow.App.Tests.Views.Settings.SettingsViewTests.Models_page_has_60_top_inset_and_sticky_chrome_and_search_changes_preserve_page_identity`
- `Marrow.App.Tests.Views.Settings.SettingsViewTests.Providers_keep_all_sections_and_status_header_metrics_and_animate_reveal_without_unmounting`
- `Marrow.App.Tests.Views.Settings.SettingsViewTests.Settle_preview_only_plays_on_enable_and_archive_disables_the_whole_row_without_changing_value`
- `Marrow.App.Tests.Views.Settings.SettingsViewTests.Signed_in_providers_lead_in_stable_declaration_order`
- `Marrow.App.Tests.Views.Settings.SettingsViewTests.The_three_licence_documents_ship_beside_the_executable_upstream_first_and_the_popover_shows_them`
- `Marrow.App.Tests.WindowsToastRoutingTests.A_thread_notification_with_an_unparsable_id_still_shows_the_main_window_and_keeps_the_selection`
- `Marrow.App.Tests.WindowsToastRoutingTests.Clicking_a_thread_notification_shows_the_main_window_and_selects_the_thread_and_opens_no_Settings`
- `Marrow.App.Tests.WindowsToastRoutingTests.Clicking_an_update_notification_opens_Settings_on_About_and_never_the_main_window(version: "")`
- `Marrow.App.Tests.WindowsToastRoutingTests.Clicking_an_update_notification_opens_Settings_on_About_and_never_the_main_window(version: "1.7.1")`
- `Marrow.App.Tests.WindowsToastTests.Launch_installs_the_delegate_after_cache_and_markdown_before_windows`

### Unresolved toast cases

These fail Assert.True on Linux rather than reporting a missing DLL. Their
source and exercised production notification code are unchanged. They require a
Windows rerun to distinguish a host limitation from a product regression; neither
is declared fixed or waived.

- `Marrow.App.Tests.WindowsToastTests.Background_update_posts_through_the_same_poster_after_persisting_once_even_if_delivery_fails(denied: False)`
- `Marrow.App.Tests.WindowsToastTests.Background_update_posts_through_the_same_poster_after_persisting_once_even_if_delivery_fails(denied: True)`

### Native icon and boot regression cases

These were not run on Linux and remain explicit Windows checks:

- `Marrow.App.Tests.Port.WindowsTaskbarOverlayIconTests.The_system_icon_made_from_the_badge_has_its_size_and_its_pixels(size: 16)`
- `Marrow.App.Tests.Port.WindowsTaskbarOverlayIconTests.The_system_icon_made_from_the_badge_has_its_size_and_its_pixels(size: 32)`
- `Marrow.App.Tests.Window.Native.AuroraBootNativeTests.B_warms_behind_the_cloaked_main_window_grows_to_its_DWM_frame_and_records_at_60_fps`

The overlay cases read back actual GDI HICON pixels; the boot case requires an
unlocked Windows desktop and DWM. Neither is an automated check of Explorer,
the base taskbar/Alt-Tab icon, Start shortcuts, shell toast visuals or installer
icons. Those remain manual Windows checks at 100/125/150/200% DPI from T21-REPORT.md.
There is no existing packaged-icon test here that asserts those shell surfaces.

For the three native cases, run the App test project with filter
`FullyQualifiedName~WindowsTaskbarOverlayIconTests|FullyQualifiedName~AuroraBootNativeTests`.
The two toast cases have the single-method filter
`FullyQualifiedName~WindowsToastTests.Background_update_posts_through_the_same_poster_after_persisting_once_even_if_delivery_fails`.
