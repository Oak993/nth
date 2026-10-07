# Every full-App result change before/after the approved clock and focus fixture repair

Before: authenticated cdbec8a64 (source/tests/build inputs identical to restored079104326). After: actual TRX E:\Droppy\work\scratch\codex-cz-7b-fix\results\app-full-after-clock\omera_OAK-BLACK_2026-10-07_20_34_02_net10.0.trx

| Test identity | Before | After |
| --- | --- | --- |
| Marrow.App.Tests.App.AppUpdaterRestartTests.The_relaunch_marker_survives_a_restart_and_the_relaunched_build_consumes_it_once | Failed | Passed |
| Marrow.App.Tests.App.AppUpdaterVelopackHandoffTests.An_install_downloads_through_the_engine_then_stages_verifies_and_asks_the_updater_to_restart | Failed | Passed |
| Marrow.App.Tests.App.VelopackLoopbackFeedTests.A_check_a_download_with_percent_and_restart_run_through_velopack_on_a_loopback_github | Failed | Passed |
| Marrow.App.Tests.AppDelegateTests.A_lowercase_threadID_still_parses_and_is_suppressed_for_the_selected_thread_while_active | Failed | Passed |
| Marrow.App.Tests.AppDelegateTests.A_merge_that_ends_before_the_first_poll_is_seen_at_250_ms_and_not_sooner | Failed | Passed |
| Marrow.App.Tests.AppDelegateTests.A_notification_for_a_thread_that_is_not_the_selected_one_shows_a_banner_with_sound_while_active | Failed | Passed |
| Marrow.App.Tests.AppDelegateTests.A_notification_for_the_selected_thread_while_the_app_is_active_is_presented_with_no_banner_and_no_sound | Failed | Passed |
| Marrow.App.Tests.AppDelegateTests.A_notification_for_the_selected_thread_while_the_app_is_not_active_shows_a_banner_with_sound | Failed | Passed |
| Marrow.App.Tests.AppDelegateTests.A_notification_with_no_threadID_shows_a_banner_with_sound_while_the_app_is_active | Failed | Passed |
| Marrow.App.Tests.AppDelegateTests.A_quit_during_a_merge_is_answered_terminate_later_and_replies_true_at_the_first_250_ms_poll_after_the_merge_ends | Failed | Passed |
| Marrow.App.Tests.AppDelegateTests.A_quit_during_a_merge_that_never_ends_replies_true_exactly_when_4_minutes_have_passed | Failed | Passed |
| Marrow.App.Tests.AppDelegateTests.A_quit_waits_while_any_live_chat_is_merging_even_when_the_merging_chat_changes_during_the_wait | Failed | Passed |
| Marrow.App.Tests.AppDelegateTests.A_quit_while_no_live_runtime_is_merging_is_answered_terminate_now_with_no_wait | Failed | Passed |
| Marrow.App.Tests.AppDelegateTests.A_threadID_that_does_not_parse_as_a_UUID_shows_a_banner_with_sound(threadID: "") | Failed | Passed |
| Marrow.App.Tests.AppDelegateTests.A_threadID_that_does_not_parse_as_a_UUID_shows_a_banner_with_sound(threadID: "not-a-uuid") | Failed | Passed |
| Marrow.App.Tests.AppDelegateTests.A_threadID_that_is_not_a_string_is_not_read_even_when_it_names_the_selected_thread | Failed | Passed |
| Marrow.App.Tests.AppDelegateTests.An_update_notification_while_the_app_is_active_shows_a_banner_with_sound | Failed | Passed |
| Marrow.App.Tests.AppDelegateTests.Only_an_active_app_with_the_notified_thread_selected_suppresses_the_banner_and_the_sound(appIsActive: False, notifiedThreadIsSelected: False, expected: Banner \| Sound) | Failed | Passed |
| Marrow.App.Tests.AppDelegateTests.Only_an_active_app_with_the_notified_thread_selected_suppresses_the_banner_and_the_sound(appIsActive: False, notifiedThreadIsSelected: True, expected: Banner \| Sound) | Failed | Passed |
| Marrow.App.Tests.AppDelegateTests.Only_an_active_app_with_the_notified_thread_selected_suppresses_the_banner_and_the_sound(appIsActive: True, notifiedThreadIsSelected: False, expected: Banner \| Sound) | Failed | Passed |
| Marrow.App.Tests.AppDelegateTests.Only_an_active_app_with_the_notified_thread_selected_suppresses_the_banner_and_the_sound(appIsActive: True, notifiedThreadIsSelected: True, expected: None) | Failed | Passed |
| Marrow.App.Tests.AppDelegateTests.Only_the_exact_key_threadID_names_the_thread(key: "ThreadID") | Failed | Passed |
| Marrow.App.Tests.AppDelegateTests.Only_the_exact_key_threadID_names_the_thread(key: "threadId") | Failed | Passed |
| Marrow.App.Tests.AppDelegateTests.Only_the_exact_key_threadID_names_the_thread(key: "threadid") | Failed | Passed |
| Marrow.App.Tests.AppDelegateTests.Termination_saves_the_model_then_flushes_the_live_spend_then_ends_the_MCP_servers | Failed | Passed |
| Marrow.App.Tests.AppDelegateTests.Termination_stops_every_MCP_server_the_hub_started_and_each_one_finds_the_save_and_the_flush_done | Failed | Passed |
| Marrow.App.Tests.AppDelegateTests.The_deferred_quit_replies_from_a_later_main_turn_and_checks_the_merge_before_its_first_sleep | Failed | Passed |
| Marrow.App.Tests.AppDelegateTests.The_four_minute_cap_is_counted_from_the_quit_and_not_from_an_earlier_time | Failed | Passed |
| Marrow.App.Tests.AppDelegateTests.The_presentation_gate_and_the_models_own_gate_read_one_and_the_same_NSApp_isActive(appIsActive: False) | Failed | Passed |
| Marrow.App.Tests.AppDelegateTests.The_presentation_gate_and_the_models_own_gate_read_one_and_the_same_NSApp_isActive(appIsActive: True) | Failed | Passed |
| Marrow.App.Tests.AppDelegateTests.The_presentation_is_nothing_for_the_open_chat_and_exactly_a_banner_with_sound_otherwise(payload: "no threadID", appIsActive: True, suppressed: False) | Failed | Passed |
| Marrow.App.Tests.AppDelegateTests.The_presentation_is_nothing_for_the_open_chat_and_exactly_a_banner_with_sound_otherwise(payload: "other thread", appIsActive: True, suppressed: False) | Failed | Passed |
| Marrow.App.Tests.AppDelegateTests.The_presentation_is_nothing_for_the_open_chat_and_exactly_a_banner_with_sound_otherwise(payload: "selected thread", appIsActive: False, suppressed: False) | Failed | Passed |
| Marrow.App.Tests.AppDelegateTests.The_presentation_is_nothing_for_the_open_chat_and_exactly_a_banner_with_sound_otherwise(payload: "selected thread", appIsActive: True, suppressed: True) | Failed | Passed |
| Marrow.App.Tests.AppDelegateTests.The_presentation_is_nothing_for_the_open_chat_and_exactly_a_banner_with_sound_otherwise(payload: "unparseable threadID", appIsActive: True, suppressed: False) | Failed | Passed |
| Marrow.App.Tests.AppDelegateTests.The_presentation_is_nothing_for_the_open_chat_and_exactly_a_banner_with_sound_otherwise(payload: "update", appIsActive: True, suppressed: False) | Failed | Passed |
| Marrow.App.Tests.AppDelegateTests.With_no_window_of_the_app_active_the_app_is_not_active_and_the_selected_threads_notification_shows | Failed | Passed |
| Marrow.App.Tests.AppSettingsComposerLightTests.A_choice_is_stored_and_read_back_after_a_relaunch | Failed | Passed |
| Marrow.App.Tests.AppSettingsComposerLightTests.Nothing_stored_is_border_beam_with_the_playgrounds_values | Failed | Passed |
| Marrow.App.Tests.AppSettingsComposerLightTests.Stored_text_that_is_not_valid_unicode_is_the_default | Failed | Passed |
| Marrow.App.Tests.AppSettingsComposerLightTests.Unreadable_data_is_the_default | Failed | Passed |
| Marrow.App.Tests.AppSettingsOpenRouterTests.A_key_the_store_accepts_is_kept_there_trimmed_under_its_own_account_and_read_back_after_a_relaunch | Failed | Passed |
| Marrow.App.Tests.AppSettingsOpenRouterTests.A_key_the_store_refuses_is_reported_under_the_field_and_stored_nowhere | Failed | Passed |
| Marrow.App.Tests.AppSettingsOpenRouterTests.A_provider_that_takes_no_key_still_gets_the_empty_string_and_OpenRouter_is_not_one_of_them | Failed | Passed |
| Marrow.App.Tests.AppSettingsOpenRouterTests.A_stored_key_wins_over_the_environment_and_is_sent_trimmed | Failed | Passed |
| Marrow.App.Tests.AppSettingsOpenRouterTests.Assigning_the_key_it_already_holds_touches_neither_store | Failed | Passed |
| Marrow.App.Tests.AppSettingsOpenRouterTests.Emptying_the_field_deletes_the_credential | Failed | Passed |
| Marrow.App.Tests.AppSettingsOpenRouterTests.Filling_clears_a_legacy_entry_only_when_the_store_holds_the_key_and_never_reads_it_as_the_key | Failed | Passed |
| Marrow.App.Tests.AppSettingsOpenRouterTests.SetAPIKeyInput_edits_the_same_field_as_the_Settings_page | Failed | Passed |
| Marrow.App.Tests.AppSettingsOpenRouterTests.The_background_load_fills_the_OpenRouter_field_off_the_main_thread | Failed | Passed |
| Marrow.App.Tests.AppSettingsOpenRouterTests.The_key_is_read_from_the_credential_store_on_the_first_ask_and_the_four_other_providers_stay_empty | Failed | Passed |
| Marrow.App.Tests.AppSettingsOpenRouterTests.With_nothing_stored_the_key_is_the_OPENROUTER_API_KEY_variable_trimmed | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.A_Command_Code_key_the_store_accepts_is_kept_there_and_its_legacy_plaintext_entry_is_removed | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.A_Command_Code_key_the_store_refuses_is_reported_under_the_field_and_stored_nowhere | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.A_DeepSeek_key_the_store_accepts_is_kept_there_and_its_legacy_plaintext_entry_is_removed | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.A_DeepSeek_key_the_store_refuses_is_reported_under_the_field_and_stored_nowhere | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.A_Meta_key_the_store_accepts_is_kept_there_and_its_legacy_plaintext_entry_is_removed | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.A_Meta_key_the_store_refuses_is_reported_under_the_field_and_stored_nowhere | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.A_Zai_key_the_store_accepts_is_kept_there_and_its_legacy_plaintext_entry_is_removed | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.A_Zai_key_the_store_refuses_is_reported_under_the_field_and_stored_nowhere | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.A_binary_path_is_stored_trimmed_under_the_providers_raw_value_one_write_per_change | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.A_disabledProviders_value_that_is_not_a_list_of_strings_disables_nothing | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.A_hydraMaxHeads_of_the_wrong_type_reads_as_no_cap | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.A_later_key_the_store_accepts_takes_the_refusal_line_away | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.A_legacy_plaintext_entry_is_never_sent_as_the_key_the_environment_is_used_instead | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.A_refused_edit_of_an_existing_key_leaves_the_old_key_in_place_and_it_survives_a_relaunch | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.A_refused_key_stays_in_the_field_for_the_session_and_is_gone_after_a_relaunch | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.A_stored_appZoom_outside_the_eight_levels_reads_as_100_and_is_left_unwritten(stored: 0, expected: 100) | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.A_stored_appZoom_outside_the_eight_levels_reads_as_100_and_is_left_unwritten(stored: 100, expected: 100) | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.A_stored_appZoom_outside_the_eight_levels_reads_as_100_and_is_left_unwritten(stored: 110, expected: 110) | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.A_stored_appZoom_outside_the_eight_levels_reads_as_100_and_is_left_unwritten(stored: 125, expected: 125) | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.A_stored_appZoom_outside_the_eight_levels_reads_as_100_and_is_left_unwritten(stored: 130, expected: 100) | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.A_stored_appZoom_outside_the_eight_levels_reads_as_100_and_is_left_unwritten(stored: 150, expected: 150) | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.A_stored_appZoom_outside_the_eight_levels_reads_as_100_and_is_left_unwritten(stored: 175, expected: 175) | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.A_stored_appZoom_outside_the_eight_levels_reads_as_100_and_is_left_unwritten(stored: 200, expected: 200) | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.A_stored_appZoom_outside_the_eight_levels_reads_as_100_and_is_left_unwritten(stored: 75, expected: 75) | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.A_stored_appZoom_outside_the_eight_levels_reads_as_100_and_is_left_unwritten(stored: 90, expected: 90) | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.A_stored_binary_path_map_holding_a_non_string_is_discarded_on_load | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.A_stored_hydraMaxHeads_is_clamped_into_one_to_eight_when_read(stored: -4, expected: 1) | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.A_stored_hydraMaxHeads_is_clamped_into_one_to_eight_when_read(stored: 0, expected: 1) | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.A_stored_hydraMaxHeads_is_clamped_into_one_to_eight_when_read(stored: 1, expected: 1) | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.A_stored_hydraMaxHeads_is_clamped_into_one_to_eight_when_read(stored: 12, expected: 8) | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.A_stored_hydraMaxHeads_is_clamped_into_one_to_eight_when_read(stored: 3, expected: 3) | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.A_stored_hydraMaxHeads_is_clamped_into_one_to_eight_when_read(stored: 8, expected: 8) | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.A_stored_hydraMaxHeads_is_clamped_into_one_to_eight_when_read(stored: 9, expected: 8) | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.A_stored_key_wins_over_the_environment_and_is_sent_trimmed(provider: Commandcode) | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.A_stored_key_wins_over_the_environment_and_is_sent_trimmed(provider: Deepseek) | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.A_stored_key_wins_over_the_environment_and_is_sent_trimmed(provider: Meta) | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.A_stored_key_wins_over_the_environment_and_is_sent_trimmed(provider: Zai) | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.A_stored_model_id_is_returned_as_is_and_reading_it_writes_nothing | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.A_stored_pair_list_that_cannot_be_read_as_a_list_gives_no_pairs(stored: "not json") | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.A_stored_pair_list_that_cannot_be_read_as_a_list_gives_no_pairs(stored: "{\"provider\":\"claude\"}") | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.A_stored_pair_that_fails_to_decode_is_dropped_and_the_others_survive_in_order | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.A_usagePanelHeight_of_the_wrong_type_reads_as_nil_and_is_never_clamped | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.ActivityThreadStyle_is_stored_under_activityThreadStyle_and_defaults_to_icon | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.An_absent_appTheme_falls_back_to_the_legacy_appearance_key_which_stays_unwritten | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.An_added_pair_is_written_under_hydraPairs_and_read_back_after_a_relaunch | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.An_appZoom_of_the_wrong_type_reads_as_100 | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.An_array_of_the_wrong_length_leaves_PanelSize_nil | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.An_array_of_the_wrong_length_leaves_SidebarPanelOrigin_nil | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.An_array_of_the_wrong_length_leaves_SidebarPanelSize_nil | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.An_invalid_stored_lastProjectID_string_yields_nil_without_clearing_the_key | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.An_unrecognized_appTheme_id_falls_through_to_the_legacy_appearance_key | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.AntiSlopEnabled_is_stored_under_antiSlopEnabled_and_defaults_to_true | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.AppZoom_is_stored_under_appZoom_defaults_to_100_and_is_normalized_to_a_known_level_on_read | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.Assigning_the_key_it_already_holds_touches_neither_store | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.AutoContinueAfterLimit_is_stored_under_autoContinueAfterLimit_and_defaults_to_false | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.BackdropOpacity_is_stored_under_backdropOpacity_defaults_to_one_half_and_is_never_clamped | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.BinaryPaths_persists_under_the_exact_key_providerBinaryPaths | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.Both_theme_keys_missing_or_unknown_resolves_to_System | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.Building_the_settings_over_stored_values_writes_nothing_back | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.Building_the_settings_reads_no_credential_and_every_key_field_starts_empty | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.ChimeWhenFinished_is_stored_under_chimeWhenFinished_and_defaults_to_true | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.ChronologicalTimeline_is_stored_under_chronologicalTimeline_and_defaults_to_false | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.Clearing_a_binary_path_removes_its_entry_instead_of_storing_an_empty_string | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.CommitInstructions_persists_free_text_verbatim_with_no_trimming_and_defaults_to_empty | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.ConfirmBeforeDeleting_is_stored_under_confirmBeforeDeleting_and_defaults_to_true | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.DefaultProvider_is_stored_under_defaultProvider_as_its_raw_value_and_defaults_to_codex | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.DefaultRuntimeMode_is_stored_under_defaultRuntimeMode_and_defaults_to_full_access | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.DefaultWorkspaceMode_is_stored_under_defaultWorkspaceMode_and_defaults_to_local | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.DisabledProviders_persists_under_the_exact_key_disabledProviders | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.Disabling_moves_the_provider_to_the_end_and_enabling_removes_every_copy_one_write_each | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.Each_Hydra_switch_is_written_under_its_exact_key_at_once_and_read_back_after_a_relaunch | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.Emptying_a_key_field_deletes_the_credential_and_removes_the_legacy_entry(provider: Commandcode) | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.Emptying_a_key_field_deletes_the_credential_and_removes_the_legacy_entry(provider: Deepseek) | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.Emptying_a_key_field_deletes_the_credential_and_removes_the_legacy_entry(provider: Meta) | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.Emptying_a_key_field_deletes_the_credential_and_removes_the_legacy_entry(provider: Zai) | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.Every_provider_is_enabled_on_a_fresh_store | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.Filling_removes_legacy_entries_only_for_accounts_the_store_holds_and_never_reads_them_as_keys | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.HasSeenHydraIntro_is_stored_under_hasSeenHydraIntro_and_defaults_to_false | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.HasSeenTour_is_stored_under_hasSeenTour_and_defaults_to_false | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.Hydra_settings_start_with_Hydra_on_and_the_upstream_defaults | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.LastEfforts_persists_under_the_exact_key_lastEfforts | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.LastHydraPairID_persists_as_its_uuidString_and_nil_removes_the_key | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.LastModels_persists_under_the_exact_key_lastModels | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.LastProjectID_persists_as_its_uppercase_uuidString_and_nil_removes_the_key | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.ModelList_decode_failure_yields_an_empty_list | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.ModelList_persists_as_JSON_capped_at_15_and_ModelPin_encodes_only_provider_and_modelID | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.ModelPin_is_a_value_with_a_real_nil_in_RowDrag_content_equality_and_a_two_member_encoding | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.ModelPreferences_decode_failure_yields_an_empty_map | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.ModelPreferences_persists_by_ModelPin_id_and_a_default_preference_is_removed_not_stored | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.MoveModel_places_the_pin_before_or_after_the_target_with_the_index_correction | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.NotifyWhenFinished_is_stored_under_notifyWhenFinished_and_an_absent_value_is_true | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.Only_a_raw_value_listed_under_disabledProviders_disables_and_unknown_entries_are_kept | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.PanelSize_persists_as_a_two_element_array_and_nil_removes_the_key | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.ProjectActivationOverrides_decode_failure_yields_an_empty_map | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.ProjectActivationOverrides_follow_the_global_switch_until_explicitly_overridden | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.ProjectsEnabled_is_stored_under_projectsEnabled_and_defaults_to_true | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.Providers_that_take_no_key_get_the_empty_string | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.Providers_without_a_key_field_read_an_empty_field_and_ignore_edits | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.RecentDownloadsPicker_is_stored_under_recentDownloadsPicker_and_defaults_to_true | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.Remember_stores_the_last_model_and_effort_by_raw_value_and_a_relaunch_reads_them_back | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.SetAPIKeyInput_edits_the_same_field_as_the_Settings_page(provider: Commandcode) | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.SetAPIKeyInput_edits_the_same_field_as_the_Settings_page(provider: Deepseek) | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.SetAPIKeyInput_edits_the_same_field_as_the_Settings_page(provider: Meta) | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.SetAPIKeyInput_edits_the_same_field_as_the_Settings_page(provider: Zai) | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.Setting_hydraMaxHeads_writes_the_number_and_setting_nil_removes_the_key | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.Settings_resolve_a_single_store_so_a_write_from_one_construction_is_seen_by_another | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.SettleSound_is_stored_under_settleSound_and_defaults_to_true | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.SettledCollapsed_is_stored_under_settledSectionCollapsed_and_defaults_to_true | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.ShowReasoning_is_stored_under_showReasoning_and_defaults_to_false | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.ShowsUsagePanel_is_stored_under_showsUsagePanel_and_defaults_to_false | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.ShowsWorkingCard_is_stored_under_showsWorkingCard_and_defaults_to_true | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.SidebarActivityView_is_stored_under_sidebarActivityView_and_defaults_to_false | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.SidebarFloats_is_stored_under_sidebarFloats_and_defaults_to_false | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.SidebarMode_is_the_computed_pair_of_the_two_sidebar_switches | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.SidebarOnlyFloats_is_stored_under_sidebarOnlyFloats_and_defaults_to_false | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.SidebarPanelOrigin_persists_as_a_two_element_array_and_nil_removes_the_key | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.SidebarPanelSize_persists_as_a_two_element_array_and_nil_removes_the_key | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.TerminalHeight_is_stored_under_terminalHeight_defaults_to_260_and_is_never_clamped | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.TextGeneration_is_stored_under_textGeneration_and_defaults_to_automatic | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.The_background_load_reads_each_account_once_off_the_main_thread_and_fills_the_fields_without_writing_back | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.The_field_value_is_untrimmed_while_the_key_sent_is_trimmed(provider: Commandcode) | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.The_field_value_is_untrimmed_while_the_key_sent_is_trimmed(provider: Deepseek) | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.The_field_value_is_untrimmed_while_the_key_sent_is_trimmed(provider: Meta) | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.The_field_value_is_untrimmed_while_the_key_sent_is_trimmed(provider: Zai) | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.The_first_ask_reads_the_credentials_itself_exactly_once | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.The_pair_for_a_chat_is_the_exact_lead_model_then_an_any_model_pair_then_the_providers_first | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.The_per_chat_Hydra_default_shares_its_key_with_ChatThread | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.The_refusal_line_is_the_exact_ruled_sentence | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.Theme_is_stored_under_appTheme_defaults_to_system_and_writing_it_updates_ThemeManager | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.ThreadFinishAction_is_stored_under_threadFinishAction_and_defaults_to_settle | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.UpdateHydraPair_clamps_the_cap_and_drops_a_no_op_write_and_remove_drops_by_id | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.UsagePanelDock_is_stored_under_usagePanelDock_and_defaults_to_bottomLeading | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.UsagePanelHeight_persists_as_a_Double_and_nil_removes_the_key | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.With_no_any_model_pair_the_providers_first_pair_is_chosen_and_a_provider_without_pairs_has_none | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.With_nothing_stored_the_key_is_the_providers_environment_variable_trimmed(provider: Commandcode, variable: "COMMAND_CODE_API_KEY") | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.With_nothing_stored_the_key_is_the_providers_environment_variable_trimmed(provider: Deepseek, variable: "DEEPSEEK_API_KEY") | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.With_nothing_stored_the_key_is_the_providers_environment_variable_trimmed(provider: Meta, variable: "MODEL_API_KEY") | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.With_nothing_stored_the_key_is_the_providers_environment_variable_trimmed(provider: Zai, variable: "ZAI_API_KEY") | Failed | Passed |
| Marrow.App.Tests.AppSettingsTests.With_the_store_refusing_everything_every_key_edit_shows_the_same_line_and_nothing_reaches_disk | Failed | Passed |
| Marrow.App.Tests.AppSettingsVoiceKeysTests.A_two_second_composer_light_drag_writes_at_most_9_times_plus_one_and_the_value_is_current_each_frame | Failed | Passed |
| Marrow.App.Tests.AppSettingsVoiceKeysTests.Each_key_stores_its_records_json_under_its_spec_name_and_reads_back_after_a_relaunch | Failed | Passed |
| Marrow.App.Tests.AppSettingsVoiceKeysTests.Every_new_key_is_coalesced_and_the_in_memory_value_is_current | Failed | Passed |
| Marrow.App.Tests.AppSettingsVoiceKeysTests.Held_values_are_written_when_the_app_exits | Failed | Passed |
| Marrow.App.Tests.AppSettingsVoiceKeysTests.Nothing_stored_is_every_keys_default_and_a_launch_writes_nothing | Failed | Passed |
| Marrow.App.Tests.AppSettingsVoiceKeysTests.The_spoken_language_pair_of_keys_reaches_the_store_together | Failed | Passed |
| Marrow.App.Tests.AppSettingsVoiceKeysTests.Unreadable_data_is_the_default_and_a_cleared_hardware_record_removes_its_key | Failed | Passed |
| Marrow.App.Tests.Port.Accounts.AccountRecoveryTests.Unreadable_accounts_keep_legacy_credentials_read_only_without_overwriting_the_users_document | Failed | Passed |
| Marrow.App.Tests.Port.Accounts.AppSettingsAccountsTests.Changing_service_clears_the_previous_services_account_binding | Failed | Passed |
| Marrow.App.Tests.Port.Accounts.AppSettingsAccountsTests.Disabling_every_account_refuses_a_turn_even_with_a_live_session | Failed | Passed |
| Marrow.App.Tests.Port.Accounts.AppSettingsAccountsTests.Production_settings_migrate_current_model_effort_and_fast_preference_and_reopen_stable_ids | Failed | Passed |
| Marrow.App.Tests.Port.Accounts.AppSettingsAccountsTests.Reordered_new_account_cannot_reuse_a_live_session_started_for_the_legacy_account | Failed | Passed |
| Marrow.App.Tests.Port.AppShellTests.A_close_asked_again_while_the_quit_waits_asks_nothing_more_and_the_app_quits_once | Failed | Passed |
| Marrow.App.Tests.Port.AppShellTests.A_merge_that_never_ends_holds_the_close_for_exactly_4_minutes | Failed | Passed |
| Marrow.App.Tests.Port.AppShellTests.A_session_end_during_a_merge_is_refused_and_the_app_quits_when_the_merge_ends | Failed | Passed |
| Marrow.App.Tests.Port.AppShellTests.A_session_end_with_no_merge_running_is_let_through_for_the_platform_to_close_the_app | Failed | Passed |
| Marrow.App.Tests.Port.AppShellTests.A_session_that_ends_runs_the_termination_steps_at_once_during_a_merge_and_only_once | Failed | Passed |
| Marrow.App.Tests.Port.AppShellTests.Closing_the_main_window_during_a_merge_waits_for_it_polling_every_250_ms_then_quits | Failed | Passed |
| Marrow.App.Tests.Port.AppShellTests.Closing_the_main_window_with_no_merge_running_quits_at_once | Failed | Passed |
| Marrow.App.Tests.Port.AppShellTests.Termination_runs_the_three_steps_once_and_then_flushes_the_settings | Failed | Passed |
| Marrow.App.Tests.Port.Settings.ComposerPreviewVoiceTests.Landing_leaves_the_text_or_sends_after_landing_plus_035_and_runs_for_three_seconds(after: Leave) | Failed | Passed |
| Marrow.App.Tests.Port.Settings.ComposerPreviewVoiceTests.Landing_leaves_the_text_or_sends_after_landing_plus_035_and_runs_for_three_seconds(after: Send) | Failed | Passed |
| Marrow.App.Tests.Port.Settings.ComposerPreviewVoiceTests.The_light_gets_the_picked_level_and_the_real_glow_draws_while_listening(setting: Away, level: 0) | Failed | Passed |
| Marrow.App.Tests.Port.Settings.ComposerPreviewVoiceTests.The_light_gets_the_picked_level_and_the_real_glow_draws_while_listening(setting: Dim, level: 0.34999999999999998) | Failed | Passed |
| Marrow.App.Tests.Port.Settings.ComposerPreviewVoiceTests.The_light_gets_the_picked_level_and_the_real_glow_draws_while_listening(setting: Stay, level: 1) | Failed | Passed |
| Marrow.App.Tests.Port.Settings.ComposerPreviewVoiceTests.The_question_uses_the_presenter_preserves_pick_focus_and_keeps_answers_session_local | Failed | Passed |
| Marrow.App.Tests.Port.Settings.ComposerPreviewVoiceTests.Words_relabel_before_replay_and_a_new_pick_replaces_the_pending_start | Failed | Passed |
| Marrow.App.Tests.Port.Voice.AppSettingsVoiceAccessTests.The_record_and_the_typing_settings_read_and_write_through_AppSettings_and_the_orb_is_read | Failed | Passed |
| Marrow.App.Tests.Review.ReviewMa1CorruptSettingsTests.REVIEW_unreadable_or_newer_account_settings_must_not_stop_the_provider_registry_from_starting(stored: "{ not json") | Failed | Passed |
| Marrow.App.Tests.Review.ReviewMa1CorruptSettingsTests.REVIEW_unreadable_or_newer_account_settings_must_not_stop_the_provider_registry_from_starting(stored: "{\"Version\":2,\"Accounts\":[]}") | Failed | Passed |
| Marrow.App.Tests.Views.Composer.EffortSliderJewelFramesTests.Port_frames_render_for_the_side_by_side(appearance: "dark") | Passed | Failed |
| Marrow.App.Tests.Views.Settings.ImportSkillsTests.Choose_a_folder_adds_that_folder_to_the_list_through_the_system_chooser | Failed | Passed |
| Marrow.App.Tests.Views.Settings.ImportSkillsTests.Import_brings_in_only_the_ticked_ones_as_a_copy_by_default_and_the_page_then_lists_them_with_a_quiet_tag | Failed | Passed |
| Marrow.App.Tests.Views.Settings.ImportSkillsTests.What_could_not_be_read_is_said_once_in_plain_words | Passed | Failed |
