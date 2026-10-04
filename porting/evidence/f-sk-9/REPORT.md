# SK-9 delivery report

Branch: `codex/f-sk-9`. Tested code revision: `a873eb3cc4711573391dd298c1cbc79d984fd3d8`. The final head additionally
records this evidence report; its SHA is in the worker's final response.

Pinned macOS reference: B3 `52bc7926097cd622d08d6294e0d8b08a48c7516e`, Droppy Code 1.7.0.
Base: `origin/codex/f-sk-5` (`4f1ba611c74302e988f179c574e5bf9f1e5e90e7`). Requested terminal
merge: `280d862b3f236b7aecce284f627fdf12fabe3721`, merge commit `94bb5383`.
Both sides of the engineering-decisions conflict were kept. No ledger status was changed.
SK-5 production files and tests were not edited. Existing CRLF files were not rewritten.

SK-9's Services component is implemented: installed/enabled-only progressive disclosure,
reviewed group defaults, provider/source filters, equivalence and reserved-name clashes,
read-only user/project scans, character cap with user entries first, persisted switches,
whole activated plugins for Claude/Grok and skills-only views for other providers,
per-folder plugin activation, stable per-chat paths, spares and listing probes, atomic tmp
publication with SK-5 leases, whole-tree verification and invoked-byte verification.
Downloaded plugin hooks/MCP are off until the plugin is switched on.

## Linux verification

SDK: .NET 10.0.100 installed into `/tmp/sk9-dotnet` because the worker had no .NET SDK.
Linux tests use an explicit ACL/stream test double; it is never selected by production.
Every test command sets `MARROW_PORT_DATA_DIR` to a freshly created `/tmp/marrow-sk9-*` root.
No network connection, real provider CLI or real user-data folder is used by these tests.
Dependency restore and Git setup/push are separate from tests.

- Test-first run: failed with missing generation types, before implementation.
- `dotnet test tests/Marrow.Services.Tests --no-restore --filter 'Feature=SK-5|Feature=SK-9'`:
  **99 passed, 0 failed, 12 WINDOWS-ONLY skips, 111 total**, exit 0.
- SK-9 subset in that TRX: **38 passed, 0 failed, 3 WINDOWS-ONLY skips, 41 total**.
- `dotnet test tests/Marrow.Core.Tests --no-restore --filter
  'Feature=SK-1|Feature=SK-7|FullyQualifiedName~TraceabilityTests|FullyQualifiedName~LayeringTests'`:
  **204 passed, 0 failed, 0 skipped**, exit 0.
- Services and Core compile as part of the final test commands, with warnings treated as errors.
- Plain solution build on Linux rejects Windows targets (NETSDK1100). The cross-targeted build
  with `-p:EnableWindowsTargeting=true` reaches an unchanged existing nullable error:
  `src/Marrow.App/Port/SettingsRise.cs:149`, CS8604 (`Walk` gets a possibly null `node`).
  That file is byte-identical to the SK-5 base; it was not changed to bypass the error.
- `git diff --check`: passed.

Authentic logs and TRX are beside this report. Independent review was performed by the
`review_sk9` subagent, per PORT_CONTRACT section 15. It found and prompted fixes for publication
GC races, skill-creator source selection, plugin-pin identity, explicit-only handling,
damaged-spare repair, background build locking, config comments, plugin-folder switches,
reconciliation overrides and unreturned lease cleanup. Its final focused review found no
remaining material correctness bug. The reviewer did not run tests; the root worker ran them.

## Acceptance tests delivered (names)

- `ClashScannerTests.Grok_scans_its_own_configured_and_Claude_roots_without_leaking_them_to_Claude`
- `ClashScannerTests.Malformed_disclosure_and_unresolved_config_paths_refuse_the_scan`
- `ClashScannerTests.Native_own_skills_switch_off_reads_nothing_from_home`
- `ClashScannerTests.OpenCode_effective_paths_resolve_against_session_and_only_forward_tilde_expands`
- `ClashScannerTests.Provider_specific_user_and_project_folders_are_scanned_without_writes`
- `GenerationCompositionTests.A_downloaded_set_preserves_each_whole_plugin_folder_for_plugin_providers`
- `GenerationCompositionTests.A_restart_reuses_the_chat_path_and_complete_references`
- `GenerationCompositionTests.Budget_uses_smallest_offered_model_and_user_skills_keep_their_descriptions`
- `GenerationCompositionTests.Listing_generation_is_shared_by_probes_and_paths_are_rewritten_for_each_chat`
- `GenerationCompositionTests.Whole_plugin_preserves_assets_but_disabled_skill_is_absent`
- `GenerationLifecycleTests.Damaged_spare_is_rebuilt_from_verified_store_before_launch`
- `GenerationLifecycleTests.Direct_store_GC_during_publication_cannot_delete_the_leased_target`
- `GenerationLifecycleTests.Explicit_only_without_an_upstream_flag_is_left_out_of_file_loading_providers`
- `GenerationLifecycleTests.Missing_clash_snapshot_refuses_to_build`
- `GenerationLifecycleTests.Ready_spare_is_claimed_by_rename_and_key_depends_on_entries_not_chat_or_project`
- `GenerationLifecycleTests.Skill_creator_uses_the_approved_source_for_each_provider`
- `GenerationRegressionTests.A_held_generation_move_is_recorded_and_reused_after_restart`
- `GenerationRegressionTests.A_set_installed_during_chat_is_absent_from_live_snapshot_and_present_on_resume`
- `GenerationRegressionTests.Commented_Grok_section_and_path_arrays_are_read_without_silent_omissions`
- `GenerationRegressionTests.Item_activation_after_reconcile_clears_old_folder_overrides`
- `GenerationRegressionTests.Missing_spare_is_logged_with_reason_without_skill_body_text`
- `GenerationRegressionTests.Path_record_failure_disposes_the_unreturned_generation_lease`
- `GenerationRegressionTests.Planted_chat_path_metadata_is_not_authority_to_read_or_delete_elsewhere`
- `GenerationRegressionTests.Plugin_folders_in_one_set_have_independent_content_switches`
- `GenerationRegressionTests.YAML_inline_comments_do_not_hide_user_skill_name_clashes`
- `GenerationTests.Catalogue_has_only_names_and_descriptions_and_text_requires_invocation`
- `GenerationTests.Claude_and_Grok_get_whole_enabled_plugin_other_providers_get_only_skills`
- `GenerationTests.Damaged_store_and_unavailable_protection_are_never_published`
- `GenerationTests.Defaults_enable_approved_groups_but_not_external_access_or_large_sets`
- `GenerationTests.Disabled_and_not_installed_skills_never_appear`
- `GenerationTests.Plugin_hooks_and_MCP_are_off_until_plugin_is_switched_on`
- `GenerationTests.Provider_filters_and_explicit_only_are_applied_independently`
- `GenerationTests.Published_generations_are_complete_locked_and_lease_protected_from_store_GC`
- `GenerationTests.Running_chat_keeps_its_generation_and_resume_uses_current_switches_at_same_path`
- `GenerationTests.Runtime_plugin_names_and_case_match_equivalence_groups`
- `GenerationTests.Switches_round_trip_carry_over_drop_removed_and_default_new_skills`
- `GenerationTests.Tampered_generation_fails_closed_before_full_text_is_returned`
- `GenerationTests.User_descriptions_stay_whole_and_lowest_priority_is_left_out`

## WINDOWS-ONLY tests

- `WindowsGenerationTests.Claimed_generation_keeps_ACL_and_GC_runs_the_unlock_order`
- `WindowsGenerationTests.Held_old_chat_moves_to_a_new_path_and_GC_retries_after_handle_closes`
- `WindowsGenerationTests.Junction_in_generation_is_rejected_without_reading_its_target`

All three skip explicitly elsewhere: "WINDOWS-ONLY: requires NTFS ACLs, named streams and
Windows delete-sharing semantics; this worker is not Windows." The shared SK-5 fixture unlocks
all trees before deletion and checks that no extant locked fixture remains.

## EDs written

At the end of `porting/engineering_decisions.md`, **ED-324**, numbered choices 1-8:
plugin activation/split; explicit reviewed metadata/defaults/budgets; source-qualified switch
and content identity; publication reservations/spares/probes; unmodified explicit-only
fail-closed handling; read-only scans/closed seams; fixture arithmetic and verification scope;
structured spare-miss diagnostics and item-versus-folder switch semantics. Internal exception
strings are diagnostics, not new UI copy. No new product labels or provider defaults were added.

## Not done and why

- The three native Windows tests were delivered but not executed: this worker is Linux.
- Windows 150 ms spare-claim / first-read timing budgets, real CLI skill discovery/invocation,
  plugin-format compatibility, and live budget measurements were not run: user forbids real
  CLIs here and the plan assigns those checks to WU-11/DL-20.
- Provider argument/protocol wiring, Settings integration, and actual installation-event
  inventory refresh are following SK-11/SK-13 work, outside this Services-only unit. SK-1's
  current schema lacks reviewed disclosure/default/budget fields; these inputs are explicit
  and fail closed. The installation-during-chat fixture writes a new locked set and Installed
  record while the old generation is live, then refreshes the frozen installed inventory at
  session end and proves the resumed chat contains it. It does not claim app-level installation
  event wiring. SK-5 was left unchanged, as requested.
- Unmodified explicit-only skills lacking their upstream disable-model-invocation flag are
  intentionally omitted from file-loading providers and reported in LeftOut, rather than
  patching vendor text or permitting implicit invocation. The ED records this plan conflict.
- Full solution build/test completion is blocked by the pre-existing App nullable error above;
  no existing test was weakened or removed to get a pass.

**Status: Services component tested on Linux; Windows/live/application acceptance remains
INCOMPLETE / NOT RUN. This is not a full-port parity or release acceptance claim.**
