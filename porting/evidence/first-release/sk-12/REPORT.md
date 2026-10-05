# SK-12: offer flow and resume

Implemented on `codex/f-sk-12`, based on `origin/claude/verify-terminal-20261001` (797867fb), with all five requested SK-11 branches merged first. Only this feature branch is pushed. The final head SHA is reported in the delivery message and can be read with `git rev-parse codex/f-sk-12`; this report is committed with the tested source.

DL-13 / R1-R4 implementation is complete in source; acceptance remains partially verified because the available worker is Linux and Windows execution is unavailable. No parity or release gate is promoted. No package, signing, publishing or installer was run. The official .NET 10 SDK archive was extracted to a temporary folder to run builds/tests; no SDK installer was run.

The offer flow uses the merged SK-10 detector and provider taps, requires verified install paths and routing publication before resuming, preserves held-approval epochs, queues/sends the exact S1 continuation as a timeline Notice/provider User message, and completes request_tool in the same turn. Downloader HTTP, provider transport, install protection and incidental Git process calls in new tests are bound to offline fixtures. Missing/unknown seams fail closed. No test assertion was weakened or edited to match observed output.

Raw TRX/log files are preserved as emitted, including diagnostic trailing spaces. The source diff passes the whitespace check; generated evidence retains its original formatting.

## Commands and counts

SDK 10.0.401, runtime 10.0.12. Every test invocation used `MARROW_PORT_DATA_DIR=/tmp/sk12-data`, `/tmp/sk12-dotnet/dotnet test`, `--no-restore`, `-p:EnableWindowsTargeting=true`, a TRX logger, and a filter ending `&FullyQualifiedName!~Native`. These are selected-suite counts, not a claim that the entire repository suite was run.

| Project | Final filter | Passed | Failed | Skipped | Total |
|---|---|---:|---:|---:|---:|
| Core | `(FullyQualifiedName~Port.Skills.Detect&#124;FullyQualifiedName~TraceabilityTests)&FullyQualifiedName!~Native` | 148 | 0 | 0 | 148 |
| Services | `(FullyQualifiedName~SkillsOfferDownloadTests&#124;FullyQualifiedName~ClaudeSkillsOfferDenialTests&#124;FullyQualifiedName~PipelineDownloadTests&#124;FullyQualifiedName~TraceabilityTests&#124;FullyQualifiedName~.Store.TokenLedger)&FullyQualifiedName!~Native` | 37 | 27 | 0 | 64 |
| App | `(FullyQualifiedName~SkillsOfferFlowTests&#124;FullyQualifiedName~TraceabilityTests&#124;FullyQualifiedName~SpecTests&#124;FullyQualifiedName~SpecCoverageTests)&FullyQualifiedName!~Native` | 78 | 28 | 0 | 106 |
| Ui | `FullyQualifiedName~TraceabilityTests&FullyQualifiedName!~Native` | 3 | 0 | 0 | 3 |

Combined: **321 cases, 266 passed, 55 failed, 0 skipped**. New SK-12 coverage: **28 cases, 24 passed, 4 failed**. All new cases were observed failing before their implementation or regression repair. Traceability: all 12 cases pass across Core, Services, App and UI. Ledger: 54 cases, 27 pass and 27 fail. Spec: 80 cases, 56 pass and 24 fail.

## What is not verified, and why

- Four SK-12 App cases: `R2_queues_S1_once_and_sends_after_turn_ends(status: Completed)`, `R3_sends_S1_immediately_and_preserves_composer_draft`, `Progress_exposes_bytes_and_checking_before_done`, and `Failure_keeps_S1_reason_and_Retry_runs_again`. Existing draft/queue Foundation text handling calls the Windows `icu.dll`; it is unavailable on Linux. The first two show DllNotFoundException directly; the latter two retain the same DLL exception as the offer failure. Their exact expectations remain intact. R2 after Failed, both cancellation stages, R1, R4 and the attachment/regression cases pass.
- 27 unchanged TokenLedger/TokenLedgerSecond cases: Windows SwiftPath/backslash normalization makes fixture write/read locations disagree on Linux, causing empty scans and missing files. Names and exact exceptions are below and in TRX. This unit does not alter Ledger or its expectations.
- 24 unchanged App Spec cases: 23 cannot create the headless `$Default` glyphTypeface; `HydraSettingsPageRowSpecTests.The_minus_control_is_24_square_and_removes_with_no_confirmation` calls unavailable `user32.dll`. These are environment/platform gaps, not green or skipped cases.
- An earlier broad `~Spec` Core filter also matched `ThumbnailCacheTests.Square_fill_multiplies_by_aspect_ratio_and_caps_the_long_edge_at_2048`; it failed because the WIC Windows decoder yields no image on Linux. That run was 166 pass / 1 fail / 167 total. The final Core filter selects the detector and Traceability classes explicitly; the earlier failure remains in `core-sk12-checks.trx`.
- An earlier broad Services `~Ledger` filter also matched two CodexSession fixture tests that fail locating the Windows-style `tests\fixtures\codex-cli\CodexStandIn.cs` path before any process starts. Its initial bridge fixture lacked required `Spares=[]`; that input was corrected without changing either test's expectation. The final filter selects actual Ledger classes. Intermediate runs are retained, not hidden.
- Windows-only Native tests were excluded by the owner-required filter. The read-only Windows tool check failed: “Tunnel-client has not been seen for 300 seconds. Ensure tunnel-client is running and connected.” Windows ICU, fonts, user32, WIC, ACL/installer/real provider acceptance therefore still require Windows execution. No real provider, artifact download or CLI was used to stand in for tests.
- Gate A: 5 failures / 7 passes because the pinned `E:/Droppy/reference/droppy-code-v1.7.0` checkout is absent. Gate B: 8 passes. Gate C: 1 pass, 1 failure (26 pre-existing stub markers elsewhere) and 1 NOT_IMPLEMENTED check. Those repository gates cannot be claimed complete by SK-12.
- SK-13 host/terms binding, SK-14 card UI, SK-16 shared Settings/offer service registration and restart/removal invalidation, and DL-20 live checks remain the explicitly assigned later units. This unit exposes their injected boundaries; it does not register an implicit real downloader. The embedded catalogue remains subject to SK-2 pin/input completeness and is refused when incomplete.

## Engineering decisions

`porting/engineering_decisions.md` ends with `ED-NEW-codex-sk-12`: (1) one main-actor chat flow and persisted decline; (2) epoch-checked R1 and catalogue-only S1 slots; (3) existing-queue R2/R3 with task-local Notice marking and same-turn R4; (4) combined checked download/install/publication and shared transfer lifetime; (5) revision-based cancellation and subsequent confirmation of late needs; (6) fail-closed offline seams and inherited compile/metadata repairs; (7) byte-preserved ledger merge repair; (8) platform and later-unit boundaries; (9) approval title/description regression repair.

The original base lines of both append-only ledgers are preserved in order byte-exact. All merge-side added lines are present byte-exact. STATE retains its 419 original CRLF endings; engineering decisions retains LF. The displaced cloud SK-5 entry is restored as an exact 7,603-byte block. The new checkpoint/decision are appended with LF without normalizing old bytes. See `sk12-ledger-bytes.json` for merge head SHAs and checks.

## Test-first evidence by new test name

- **Passed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.Api_request_tool_receives_exact_installed_at_result_without_a_new_turn` — observed red in `sk12-api-red.trx`.
- **Passed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.Cancel_returns_to_asked_and_late_completion_cannot_resume(stage: Checking)` — observed red in `sk12-red.trx`.
- **Passed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.Cancel_returns_to_asked_and_late_completion_cannot_resume(stage: Downloading)` — observed red in `sk12-red.trx`.
- **Passed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.Cancelled_model_request_does_not_later_resume_it` — observed red in `sk12-red.trx`.
- **Passed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.Existing_download_attaches_without_starting_a_second_download` — observed red in `sk12-attach-red.trx`.
- **Failed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.Failure_keeps_S1_reason_and_Retry_runs_again` — observed red in `sk12-red.trx`.
- **Passed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.Missing_offline_download_seam_fails_closed` — observed red in `sk12-red.trx`.
- **Passed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.One_offer_per_item_per_chat_and_second_item_joins_card` — observed red in `sk12-red.trx`.
- **Failed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.Progress_exposes_bytes_and_checking_before_done` — observed red in `sk12-red.trx`.
- **Passed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.R1_approves_held_command_only_after_verified_done` — observed red in `sk12-red.trx`.
- **Passed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.R1_correlates_command_title_when_approval_detail_is_a_description` — observed red in `sk12-approval-description-red.trx`.
- **Passed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.R1_declines_with_S1_reason_and_never_downloads` — observed red in `sk12-red.trx`.
- **Passed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.R2_and_R3_decline_send_nothing(running: False)` — observed red in `sk12-red.trx`.
- **Passed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.R2_and_R3_decline_send_nothing(running: True)` — observed red in `sk12-red.trx`.
- **Failed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.R2_queues_S1_once_and_sends_after_turn_ends(status: Completed)` — observed red in `sk12-red.trx`.
- **Passed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.R2_queues_S1_once_and_sends_after_turn_ends(status: Failed)` — observed red in `sk12-red.trx`.
- **Failed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.R3_sends_S1_immediately_and_preserves_composer_draft` — observed red in `sk12-red.trx`.
- **Passed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.R4_decline_returns_declined_without_continuation` — observed red in `sk12-red.trx`.
- **Passed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.R4_returns_installed_path_in_same_turn_without_continuation` — observed red in `sk12-red.trx`.
- **Passed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.Releasing_session_drops_stale_held_approval` — observed red in `sk12-red.trx`.
- **Passed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.Repeated_accept_during_download_does_not_start_another_download` — observed red in `sk12-red.trx`.
- **Passed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.S1_declared_skill_need_uses_same_offer_and_executable_message` — observed red in `sk12-red.trx`.
- **Passed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.Verified_result_must_include_every_requested_install_path` — observed red in `sk12-red.trx`.
- **Passed** `Marrow.Services.Tests.Port.Skills.Offers.SkillsOfferDownloadTests.Cancelled_offer_cannot_reach_a_download_plan_or_installer` — observed red in `offer-download-red-all.trx`.
- **Passed** `Marrow.Services.Tests.Port.Skills.Offers.SkillsOfferDownloadTests.Checked_download_is_installed_and_published_before_the_offer_can_resume` — observed red in `offer-download-red-all.trx`.
- **Passed** `Marrow.Services.Tests.Port.Skills.Offers.SkillsOfferDownloadTests.Failed_routing_publication_never_returns_an_installed_path` — observed red in `offer-download-red-all.trx`.
- **Passed** `Marrow.Services.Tests.Port.Skills.Offers.SkillsOfferDownloadTests.Unpinned_item_cannot_reach_a_download_plan_or_installer` — observed red in `offer-download-red-all.trx`.
- **Passed** `Marrow.Services.Tests.Port.Skills.Providers.ClaudeSkillsOfferDenialTests.S1_decline_reason_reaches_Claudes_control_response_byte_equal` — observed red in `claude-denial-red.trx`.

## Final class counts

| Test class | Passed | Failed | Total |
|---|---:|---:|---:|
| `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests` | 19 | 4 | 23 |
| `Marrow.App.Tests.TraceabilityTests` | 3 | 0 | 3 |
| `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests` | 44 | 0 | 44 |
| `Marrow.App.Tests.Views.Settings.HydraSettingsPageRowSpecTests` | 11 | 5 | 16 |
| `Marrow.App.Tests.Views.Settings.HydraSettingsPageSpecTests` | 0 | 19 | 19 |
| `Marrow.App.Tests.Views.Settings.SettingsViewCoreSpecCoverageTests` | 1 | 0 | 1 |
| `Marrow.Core.Tests.Port.Skills.Detect.CommandTokenizerTests` | 36 | 0 | 36 |
| `Marrow.Core.Tests.Port.Skills.Detect.DetectorGuardsTests` | 16 | 0 | 16 |
| `Marrow.Core.Tests.Port.Skills.Detect.HeldCommandTests` | 25 | 0 | 25 |
| `Marrow.Core.Tests.Port.Skills.Detect.MissingModuleTests` | 22 | 0 | 22 |
| `Marrow.Core.Tests.Port.Skills.Detect.MissingProgramTests` | 11 | 0 | 11 |
| `Marrow.Core.Tests.Port.Skills.Detect.ModelRequestTests` | 17 | 0 | 17 |
| `Marrow.Core.Tests.Port.Skills.Detect.NoProseSignalTests` | 5 | 0 | 5 |
| `Marrow.Core.Tests.Port.Skills.Detect.OfferCardTests` | 8 | 0 | 8 |
| `Marrow.Core.Tests.Port.Skills.Detect.SkillNeedTests` | 5 | 0 | 5 |
| `Marrow.Core.Tests.TraceabilityTests` | 3 | 0 | 3 |
| `Marrow.Services.Tests.Port.Skills.Offers.SkillsOfferDownloadTests` | 4 | 0 | 4 |
| `Marrow.Services.Tests.Port.Skills.Pipelines.PipelineDownloadTests` | 2 | 0 | 2 |
| `Marrow.Services.Tests.Port.Skills.Providers.ClaudeSkillsOfferDenialTests` | 1 | 0 | 1 |
| `Marrow.Services.Tests.Store.TokenLedgerSecondTests` | 4 | 13 | 17 |
| `Marrow.Services.Tests.Store.TokenLedgerTests` | 23 | 14 | 37 |
| `Marrow.Services.Tests.TraceabilityTests` | 3 | 0 | 3 |
| `Marrow.UI.Tests.TraceabilityTests` | 3 | 0 | 3 |

## Every final case by name

- **Passed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.Api_request_tool_receives_exact_installed_at_result_without_a_new_turn`
- **Passed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.Cancel_returns_to_asked_and_late_completion_cannot_resume(stage: Checking)`
- **Passed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.Cancel_returns_to_asked_and_late_completion_cannot_resume(stage: Downloading)`
- **Passed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.Cancelled_model_request_does_not_later_resume_it`
- **Passed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.Existing_download_attaches_without_starting_a_second_download`
- **Failed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.Failure_keeps_S1_reason_and_Retry_runs_again`
- **Passed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.Missing_offline_download_seam_fails_closed`
- **Passed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.One_offer_per_item_per_chat_and_second_item_joins_card`
- **Failed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.Progress_exposes_bytes_and_checking_before_done`
- **Passed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.R1_approves_held_command_only_after_verified_done`
- **Passed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.R1_correlates_command_title_when_approval_detail_is_a_description`
- **Passed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.R1_declines_with_S1_reason_and_never_downloads`
- **Passed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.R2_and_R3_decline_send_nothing(running: False)`
- **Passed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.R2_and_R3_decline_send_nothing(running: True)`
- **Failed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.R2_queues_S1_once_and_sends_after_turn_ends(status: Completed)`
- **Passed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.R2_queues_S1_once_and_sends_after_turn_ends(status: Failed)`
- **Failed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.R3_sends_S1_immediately_and_preserves_composer_draft`
- **Passed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.R4_decline_returns_declined_without_continuation`
- **Passed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.R4_returns_installed_path_in_same_turn_without_continuation`
- **Passed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.Releasing_session_drops_stale_held_approval`
- **Passed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.Repeated_accept_during_download_does_not_start_another_download`
- **Passed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.S1_declared_skill_need_uses_same_offer_and_executable_message`
- **Passed** `Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.Verified_result_must_include_every_requested_install_path`
- **Passed** `Marrow.App.Tests.TraceabilityTests.Every_test_names_a_requirement_a_rule_or_a_decision_and_every_id_is_well_formed`
- **Passed** `Marrow.App.Tests.TraceabilityTests.Test_classes_and_Req_tests_have_the_shape_req_verify_can_read`
- **Passed** `Marrow.App.Tests.TraceabilityTests.Test_source_has_one_class_per_file_single_line_attributes_and_no_stray_type_keywords`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.A_catalog_being_loaded_says_so_with_one_ellipsis_character`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.A_catalog_line_has_the_sources_type_and_insets`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.A_chosen_heads_model_elsewhere_offers_its_scale_from_the_heads_provider`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.A_chosen_heads_model_offers_its_own_scale`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.A_chosen_lead_model_offers_its_own_scale`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.A_chosen_lead_model_with_no_declared_scale_falls_back_to_the_providers_efforts`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.A_loaded_catalog_draws_no_line`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.A_section_label_is_a_hairline_over_a_quiet_word_with_the_sources_insets`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.A_section_labels_hairline_is_the_ports_divider_wash`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.An_editor_row_has_the_sources_padding_spacing_and_one_line_title_and_never_a_detail`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.An_effort_row_is_absent_not_empty_when_its_side_has_nothing_to_offer`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.An_empty_catalog_says_no_models_are_loaded`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.Changing_the_lead_provider_clears_the_lead_and_the_heads_that_were_on_the_old_provider`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.Choosing_Any_model_leaves_the_chosen_effort_alone`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.Choosing_Same_as_the_lead_removes_the_override_and_clears_the_heads_choices`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.Choosing_Same_as_the_lead_when_there_is_no_override_changes_nothing`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.Choosing_a_cap_writes_it_to_the_pair_and_the_first_option_clears_it`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.Choosing_a_heads_model_that_has_the_chosen_effort_keeps_it`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.Choosing_a_heads_model_without_the_chosen_effort_clears_it_using_the_heads_provider`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.Choosing_a_lead_model_that_has_the_chosen_effort_keeps_it`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.Choosing_a_lead_model_without_the_chosen_effort_clears_the_effort`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.Choosing_another_heads_provider_stores_it_and_clears_the_heads_model_and_effort`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.Choosing_the_leads_own_provider_for_the_heads_stores_nil`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.Choosing_the_same_lead_provider_is_a_no_op`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.Effort_options_read_through_the_effort_title_rule`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.Heads_elsewhere_get_their_own_line_after_the_leads`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.Heads_elsewhere_whose_default_model_has_no_scale_offer_every_effort_of_their_provider`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.Heads_elsewhere_with_no_model_offer_the_default_models_efforts_and_inherit_the_providers_default`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.Heads_on_the_leads_provider_offer_the_leads_efforts_and_inherit_the_chat`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.Moving_the_lead_elsewhere_leaves_heads_on_their_own_provider_untouched`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.Moving_the_lead_onto_the_heads_provider_drops_the_override_and_keeps_the_heads_choices`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.The_at_once_picker_lists_As_many_as_it_takes_then_one_to_eight`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.The_current_heads_provider_stays_on_offer_even_when_it_is_not_installed`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.The_editor_draws_Lead_then_Heads_with_the_sources_rows_in_order`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.The_heads_model_picker_inherits_the_chat_while_heads_run_on_the_leads_provider`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.The_heads_model_picker_inherits_the_heads_providers_default_while_heads_run_elsewhere`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.The_heads_provider_picker_offers_Same_as_the_lead_then_the_other_providers`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.The_heads_provider_picker_shows_the_leads_icon_until_heads_go_elsewhere`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.The_lead_model_picker_lists_Any_model_then_the_catalog_in_order`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.The_lead_provider_picker_offers_installed_enabled_providers_and_the_pairs_own`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.The_two_model_pickers_are_capped_at_190_and_the_others_are_not`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.When_the_editor_is_narrow_the_control_keeps_its_width_and_the_title_gives_way`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.With_no_lead_model_the_effort_picker_offers_every_effort_the_provider_knows`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPagePairSpecTests.Without_any_effort_anywhere_neither_effort_row_is_drawn`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPageRowSpecTests.A_pair_row_has_the_sources_padding_text_block_and_typography`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPageRowSpecTests.A_pair_rows_icons_sit_in_a_leading_frame_of_at_least_22`
- **Failed** `Marrow.App.Tests.Views.Settings.HydraSettingsPageRowSpecTests.Add_pair_is_enabled_with_no_provider_installed_and_adds_one_on_the_default_provider`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPageRowSpecTests.An_unnamed_pair_shows_its_models_title_as_the_placeholder`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPageRowSpecTests.Clicking_a_pair_row_opens_a_trailing_popover_with_a_360_wide_editor`
- **Failed** `Marrow.App.Tests.Views.Settings.HydraSettingsPageRowSpecTests.The_cookbook_button_is_the_last_thing_in_the_Pairs_header`
- **Failed** `Marrow.App.Tests.Views.Settings.HydraSettingsPageRowSpecTests.The_cookbook_button_opens_a_bottom_popover_holding_the_460_by_540_panel`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPageRowSpecTests.The_editor_draws_nothing_once_its_pair_is_removed`
- **Failed** `Marrow.App.Tests.Views.Settings.HydraSettingsPageRowSpecTests.The_minus_control_is_24_square_and_removes_with_no_confirmation`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPageRowSpecTests.The_name_block_has_the_sources_insets_and_caption`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPageRowSpecTests.The_name_field_is_not_focused_when_the_editor_opens`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPageRowSpecTests.The_name_field_takes_focus_when_it_is_clicked`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPageRowSpecTests.The_second_line_joins_providers_cap_and_warning_with_a_middle_dot`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPageRowSpecTests.The_slider_hint_fades_in_on_hover_and_out_on_leaving`
- **Passed** `Marrow.App.Tests.Views.Settings.HydraSettingsPageRowSpecTests.Typing_writes_the_name_at_once_and_emptying_it_stores_none`
- **Failed** `Marrow.App.Tests.Views.Settings.HydraSettingsPageRowSpecTests.With_pairs_Add_pair_sits_in_the_Another_pair_row_and_appends`
- **Failed** `Marrow.App.Tests.Views.Settings.HydraSettingsPageSpecTests.Auto_hide_heads_in_the_sidebar_is_a_switch_off_by_default_with_its_exact_detail`
- **Failed** `Marrow.App.Tests.Views.Settings.HydraSettingsPageSpecTests.Checkout_lists_Own_copy_before_Shared_and_its_detail_follows_the_choice`
- **Failed** `Marrow.App.Tests.Views.Settings.HydraSettingsPageSpecTests.Clear_finished_heads_is_a_switch_off_by_default_with_its_exact_detail`
- **Failed** `Marrow.App.Tests.Views.Settings.HydraSettingsPageSpecTests.Head_panel_lists_Progress_before_Every_step_and_keeps_one_detail`
- **Failed** `Marrow.App.Tests.Views.Settings.HydraSettingsPageSpecTests.Heads_at_once_hovered_segment_takes_the_eight_percent_overlay`
- **Failed** `Marrow.App.Tests.Views.Settings.HydraSettingsPageSpecTests.Heads_at_once_is_nine_segments_and_its_detail_is_not_pluralised`
- **Failed** `Marrow.App.Tests.Views.Settings.HydraSettingsPageSpecTests.Heads_at_once_segments_have_the_sources_geometry`
- **Failed** `Marrow.App.Tests.Views.Settings.HydraSettingsPageSpecTests.Heads_at_once_selected_segment_is_semibold_on_an_accent_capsule_and_the_rest_are_plain`
- **Failed** `Marrow.App.Tests.Views.Settings.HydraSettingsPageSpecTests.Heads_panels_lists_One_panel_before_A_panel_each_and_its_detail_follows_the_choice`
- **Failed** `Marrow.App.Tests.Views.Settings.HydraSettingsPageSpecTests.Heads_work_at_a_working_effort_is_a_switch_on_by_default_with_its_exact_detail`
- **Failed** `Marrow.App.Tests.Views.Settings.HydraSettingsPageSpecTests.Messages_holds_the_two_toggles_in_order_with_one_divider`
- **Failed** `Marrow.App.Tests.Views.Settings.HydraSettingsPageSpecTests.Messages_toggles_default_on_and_off_and_write_their_settings`
- **Failed** `Marrow.App.Tests.Views.Settings.HydraSettingsPageSpecTests.The_Heads_section_headers_info_mark_is_How_heads_work_and_opens_the_heads_explainer`
- **Failed** `Marrow.App.Tests.Views.Settings.HydraSettingsPageSpecTests.The_master_card_has_the_sources_geometry`
- **Failed** `Marrow.App.Tests.Views.Settings.HydraSettingsPageSpecTests.The_master_cards_info_mark_is_How_Hydra_works_and_opens_the_Hydra_explainer`
- **Failed** `Marrow.App.Tests.Views.Settings.HydraSettingsPageSpecTests.The_working_effort_detail_holds_one_em_dash_between_single_spaces`
- **Failed** `Marrow.App.Tests.Views.Settings.HydraSettingsPageSpecTests.When_the_team_is_done_holds_the_two_toggles_in_order_with_one_divider`
- **Failed** `Marrow.App.Tests.Views.Settings.HydraSettingsPageSpecTests.When_the_team_is_done_toggles_default_off_and_write_their_settings`
- **Failed** `Marrow.App.Tests.Views.Settings.HydraSettingsPageSpecTests.With_Hydra_off_the_note_sits_under_the_card_and_the_sections_dim`
- **Passed** `Marrow.App.Tests.Views.Settings.SettingsViewCoreSpecCoverageTests.Every_MCP_interactive_construction_has_an_owner_and_literal_in_the_pinned_MCP_spec`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.CommandTokenizerTests.A_backslash_is_a_plain_character_so_a_Windows_path_survives_in_every_shell`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.CommandTokenizerTests.A_plain_command_is_one_segment_of_words_cut_at_blanks_and_tabs`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.CommandTokenizerTests.A_python_launcher_with_dash_m_names_the_module_as_well(line: "C:\\Python314\\python.exe -m pip list", program: "python", module: "pip")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.CommandTokenizerTests.A_python_launcher_with_dash_m_names_the_module_as_well(line: "PY -3 -M pip", program: "py", module: null)`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.CommandTokenizerTests.A_python_launcher_with_dash_m_names_the_module_as_well(line: "py -3 -m pip install x", program: "py", module: "pip")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.CommandTokenizerTests.A_python_launcher_with_dash_m_names_the_module_as_well(line: "py -3.12 -m venv .venv", program: "py", module: "venv")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.CommandTokenizerTests.A_python_launcher_with_dash_m_names_the_module_as_well(line: "py -m playwright install chromium", program: "py", module: "playwright")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.CommandTokenizerTests.A_python_launcher_with_dash_m_names_the_module_as_well(line: "python -m playwright install chromium", program: "python", module: "playwright")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.CommandTokenizerTests.A_python_launcher_with_dash_m_names_the_module_as_well(line: "python3 -u -m pytest -q", program: "python3", module: "pytest")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.CommandTokenizerTests.A_segment_with_nothing_to_run_has_no_target_and_a_start_with_only_a_title_has_none_either`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.CommandTokenizerTests.An_empty_quoted_word_is_kept_and_an_unterminated_quote_runs_to_the_end_without_failing`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.CommandTokenizerTests.Dash_m_counts_only_for_a_python_launcher_before_its_first_script_word(line: "node -m pip")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.CommandTokenizerTests.Dash_m_counts_only_for_a_python_launcher_before_its_first_script_word(line: "python -c \"print(1)\" -m pip")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.CommandTokenizerTests.Dash_m_counts_only_for_a_python_launcher_before_its_first_script_word(line: "python -m")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.CommandTokenizerTests.Dash_m_counts_only_for_a_python_launcher_before_its_first_script_word(line: "python script.py -m x")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.CommandTokenizerTests.Dash_m_counts_only_for_a_python_launcher_before_its_first_script_word(line: "python script.py")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.CommandTokenizerTests.Every_pipeline_segment_has_its_own_first_program`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.CommandTokenizerTests.Leading_cd_ampersand_call_start_and_environment_assignments_are_skipped`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.CommandTokenizerTests.Only_executable_extensions_are_stripped_so_a_file_name_is_not_mistaken_for_a_program`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.CommandTokenizerTests.Pipelines_and_the_sequence_operators_of_cmd_PowerShell_and_bash_split_segments`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.CommandTokenizerTests.Quotes_keep_separators_and_blanks_inside_a_word_in_the_three_shells`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.CommandTokenizerTests.Redirections_that_contain_an_ampersand_do_not_split`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.CommandTokenizerTests.The_program_is_the_first_word_without_its_path_and_executable_extension_in_lower_case(line: "& 'C:\\Program Files\\LibreOffice\\program\\soffic"···, program: "soffice")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.CommandTokenizerTests.The_program_is_the_first_word_without_its_path_and_executable_extension_in_lower_case(line: "'/c/Program Files/LibreOffice/program/soffice' x", program: "soffice")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.CommandTokenizerTests.The_program_is_the_first_word_without_its_path_and_executable_extension_in_lower_case(line: "..\\bin\\pdftoppm.exe -png a b", program: "pdftoppm")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.CommandTokenizerTests.The_program_is_the_first_word_without_its_path_and_executable_extension_in_lower_case(line: "./soffice x", program: "soffice")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.CommandTokenizerTests.The_program_is_the_first_word_without_its_path_and_executable_extension_in_lower_case(line: "C:\\x\\soffice.exe a", program: "soffice")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.CommandTokenizerTests.The_program_is_the_first_word_without_its_path_and_executable_extension_in_lower_case(line: "SOFFICE.EXE a.docx", program: "soffice")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.CommandTokenizerTests.The_program_is_the_first_word_without_its_path_and_executable_extension_in_lower_case(line: "\"C:\\Program Files\\LibreOffice\\program\\soffice"···, program: "soffice")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.CommandTokenizerTests.The_program_is_the_first_word_without_its_path_and_executable_extension_in_lower_case(line: "npm.cmd install x", program: "npm")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.CommandTokenizerTests.The_program_is_the_first_word_without_its_path_and_executable_extension_in_lower_case(line: "python3 script.py", program: "python3")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.CommandTokenizerTests.The_program_is_the_first_word_without_its_path_and_executable_extension_in_lower_case(line: "qpdf.com --help", program: "qpdf")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.CommandTokenizerTests.The_program_is_the_first_word_without_its_path_and_executable_extension_in_lower_case(line: "soffice a.docx", program: "soffice")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.CommandTokenizerTests.The_program_is_the_first_word_without_its_path_and_executable_extension_in_lower_case(line: "tool.bat", program: "tool")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.CommandTokenizerTests.The_token_set_holds_every_word_of_the_command_and_the_words_of_a_quoted_inner_command`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.CommandTokenizerTests.The_tokenizer_never_throws_on_odd_input`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.DetectorGuardsTests.A_context_that_cannot_be_trusted_is_refused_not_guessed_at`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.DetectorGuardsTests.A_damaged_or_failed_item_is_not_there_so_it_is_offered_for_repair_or_retry_unless_already_offered`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.DetectorGuardsTests.A_decline_holds_for_the_chat_that_made_it_only`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.DetectorGuardsTests.A_download_running_for_an_offer_made_in_this_chat_still_attaches_it_instead_of_staying_silent`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.DetectorGuardsTests.A_tool_found_on_this_PC_is_not_offered_and_a_found_answer_for_another_tool_is_no_help`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.DetectorGuardsTests.An_installed_and_healthy_item_is_not_offered(state: Installed)`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.DetectorGuardsTests.An_installed_and_healthy_item_is_not_offered(state: UpdateAvailable)`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.DetectorGuardsTests.An_item_already_offered_in_this_chat_is_not_offered_again_and_another_item_still_is`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.DetectorGuardsTests.An_item_being_removed_is_not_offered`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.DetectorGuardsTests.An_item_whose_first_download_is_running_attaches_the_offer_to_that_download(state: Checking)`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.DetectorGuardsTests.An_item_whose_first_download_is_running_attaches_the_offer_to_that_download(state: Downloading)`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.DetectorGuardsTests.An_item_whose_first_download_is_running_attaches_the_offer_to_that_download(state: Queued)`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.DetectorGuardsTests.An_update_in_flight_or_a_failed_update_leaves_the_old_version_healthy_and_a_repair_does_not`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.DetectorGuardsTests.The_guards_apply_to_every_signal_alike`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.DetectorGuardsTests.When_several_guards_apply_the_first_in_this_order_is_the_reason_installed_found_removing_attach_declined_offered`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.DetectorGuardsTests.With_no_guard_in_the_way_the_missing_tool_is_offered`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.HeldCommandTests.A_program_that_is_only_an_argument_or_sits_after_the_first_word_does_not_count`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.HeldCommandTests.A_python_launcher_with_dash_m_counts_the_module_too_a_python_module_first_then_a_program`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.HeldCommandTests.An_unknown_program_or_an_empty_command_gets_no_hit`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.HeldCommandTests.Every_segment_of_a_pipeline_counts_and_the_card_lists_them_in_command_order`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.HeldCommandTests.The_first_program_of_a_held_command_is_looked_up_in_the_provides_index(command: "& 'C:\\Program Files\\LibreOffice\\program\\soffic"···, item: "T3")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.HeldCommandTests.The_first_program_of_a_held_command_is_looked_up_in_the_provides_index(command: "'/c/Program Files/LibreOffice/program/soffice' a.d"···, item: "T3")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.HeldCommandTests.The_first_program_of_a_held_command_is_looked_up_in_the_provides_index(command: "\"C:\\Program Files\\LibreOffice\\program\\soffice"···, item: "T3")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.HeldCommandTests.The_first_program_of_a_held_command_is_looked_up_in_the_provides_index(command: "bash script.sh", item: "T7")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.HeldCommandTests.The_first_program_of_a_held_command_is_looked_up_in_the_provides_index(command: "cd C:\\work && soffice a.docx", item: "T3")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.HeldCommandTests.The_first_program_of_a_held_command_is_looked_up_in_the_provides_index(command: "gh pr list", item: "T8")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.HeldCommandTests.The_first_program_of_a_held_command_is_looked_up_in_the_provides_index(command: "node make.js", item: "T2")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.HeldCommandTests.The_first_program_of_a_held_command_is_looked_up_in_the_provides_index(command: "npm install docx", item: "T2")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.HeldCommandTests.The_first_program_of_a_held_command_is_looked_up_in_the_provides_index(command: "npx tsx x.ts", item: "T2")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.HeldCommandTests.The_first_program_of_a_held_command_is_looked_up_in_the_provides_index(command: "pandoc a.docx -t plain", item: "T6")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.HeldCommandTests.The_first_program_of_a_held_command_is_looked_up_in_the_provides_index(command: "pdftoppm -png a.pdf out", item: "T4")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.HeldCommandTests.The_first_program_of_a_held_command_is_looked_up_in_the_provides_index(command: "pdftotext a.pdf -", item: "T4")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.HeldCommandTests.The_first_program_of_a_held_command_is_looked_up_in_the_provides_index(command: "pip install x", item: "T1p")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.HeldCommandTests.The_first_program_of_a_held_command_is_looked_up_in_the_provides_index(command: "py -3 script.py", item: "T1")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.HeldCommandTests.The_first_program_of_a_held_command_is_looked_up_in_the_provides_index(command: "python script.py", item: "T1")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.HeldCommandTests.The_first_program_of_a_held_command_is_looked_up_in_the_provides_index(command: "python3 script.py", item: "T1")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.HeldCommandTests.The_first_program_of_a_held_command_is_looked_up_in_the_provides_index(command: "qpdf --check a.pdf", item: "T5")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.HeldCommandTests.The_first_program_of_a_held_command_is_looked_up_in_the_provides_index(command: "sh -c 'echo hi'", item: "T7")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.HeldCommandTests.The_first_program_of_a_held_command_is_looked_up_in_the_provides_index(command: "soffice --headless --convert-to pdf a.docx", item: "T3")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.HeldCommandTests.The_first_program_of_a_held_command_is_looked_up_in_the_provides_index(command: "uv run x.py", item: "T9")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.HeldCommandTests.The_same_item_named_twice_is_one_finding`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.MissingModuleTests.A_dotted_python_module_is_looked_up_by_its_top_package_and_quotes_may_be_either_kind_or_absent`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.MissingModuleTests.A_node_specifier_is_reduced_to_its_package_name(specifier: "pptxgenjs", set: "P6")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.MissingModuleTests.A_node_specifier_is_reduced_to_its_package_name(specifier: "react-dom/server", set: "P6")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.MissingModuleTests.A_node_specifier_is_reduced_to_its_package_name(specifier: "react-icons", set: "P6")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.MissingModuleTests.A_node_specifier_is_reduced_to_its_package_name(specifier: "react-icons/fa", set: "P6")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.MissingModuleTests.A_node_specifier_is_reduced_to_its_package_name(specifier: "sharp", set: "P6")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.MissingModuleTests.A_python_module_error_on_a_command_that_succeeded_is_no_signal`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.MissingModuleTests.A_python_module_the_plan_names_resolves_to_its_package_set(module: "PIL", set: "P3")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.MissingModuleTests.A_python_module_the_plan_names_resolves_to_its_package_set(module: "docx", set: "P1")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.MissingModuleTests.A_python_module_the_plan_names_resolves_to_its_package_set(module: "imageio", set: "P5")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.MissingModuleTests.A_python_module_the_plan_names_resolves_to_its_package_set(module: "openpyxl", set: "P1")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.MissingModuleTests.A_python_module_the_plan_names_resolves_to_its_package_set(module: "pandas", set: "P3")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.MissingModuleTests.A_python_module_the_plan_names_resolves_to_its_package_set(module: "pdf2image", set: "P2")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.MissingModuleTests.A_python_module_the_plan_names_resolves_to_its_package_set(module: "pdfplumber", set: "P2")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.MissingModuleTests.A_python_module_the_plan_names_resolves_to_its_package_set(module: "playwright", set: "P4")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.MissingModuleTests.A_python_module_the_plan_names_resolves_to_its_package_set(module: "pptx", set: "P1")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.MissingModuleTests.A_python_module_the_plan_names_resolves_to_its_package_set(module: "pypdf", set: "P2")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.MissingModuleTests.A_python_module_the_plan_names_resolves_to_its_package_set(module: "reportlab", set: "P2")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.MissingModuleTests.A_relative_absolute_or_built_in_node_specifier_is_not_a_catalogue_name_and_python_modules_are_not_node_modules`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.MissingModuleTests.Node_needs_its_error_code_and_the_message_and_names_a_module_or_a_package`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.MissingModuleTests.One_failure_that_shows_a_missing_module_and_a_missing_browser_gives_both_items`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.MissingModuleTests.Playwright_s_missing_browser_message_offers_Chromium`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.MissingProgramTests.A_finished_command_that_succeeded_never_signals_whatever_its_output_says`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.MissingProgramTests.An_unknown_program_gets_no_hit_and_a_catalogue_name_in_prose_around_it_is_not_enough`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.MissingProgramTests.Cmd_reports_a_missing_program_with_process_exit_code_1_and_the_English_sentence_names_it`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.MissingProgramTests.CommandNotFoundException_is_the_PowerShell_signal_whatever_the_exit_code_and_the_language`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.MissingProgramTests.Exit_127_needs_the_not_found_phrase_and_without_an_exit_code_the_error_flag_and_the_phrase_do`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.MissingProgramTests.Exit_code_9009_is_the_cmd_signal_in_any_language_and_needs_the_program_named_in_the_output`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.MissingProgramTests.PowerShell_names_only_the_program_of_its_CategoryInfo_not_the_programs_of_the_echoed_line`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.MissingProgramTests.The_Store_alias_for_python_and_python3_is_a_missing_Python_through_exit_9009`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.MissingProgramTests.The_program_named_in_the_output_must_be_a_token_of_the_command_that_ran`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.MissingProgramTests.The_same_missing_soffice_is_found_in_every_provider_shape_in_English_and_German`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.MissingProgramTests.Two_missing_programs_in_one_command_are_both_found_in_the_order_of_the_command_line`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.ModelRequestTests.A_catalogue_id_or_a_program_name_the_catalogue_provides_is_an_offer(name: "A2", item: "A2")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.ModelRequestTests.A_catalogue_id_or_a_program_name_the_catalogue_provides_is_an_offer(name: "P1", item: "P1")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.ModelRequestTests.A_catalogue_id_or_a_program_name_the_catalogue_provides_is_an_offer(name: "SOFFICE", item: "T3")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.ModelRequestTests.A_catalogue_id_or_a_program_name_the_catalogue_provides_is_an_offer(name: "T3", item: "T3")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.ModelRequestTests.A_catalogue_id_or_a_program_name_the_catalogue_provides_is_an_offer(name: "pdftoppm", item: "T4")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.ModelRequestTests.A_catalogue_id_or_a_program_name_the_catalogue_provides_is_an_offer(name: "pip", item: "T1p")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.ModelRequestTests.A_catalogue_id_or_a_program_name_the_catalogue_provides_is_an_offer(name: "python", item: "T1")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.ModelRequestTests.A_catalogue_id_or_a_program_name_the_catalogue_provides_is_an_offer(name: "soffice", item: "T3")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.ModelRequestTests.A_name_outside_the_catalogue_gets_no_hit(name: "  ")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.ModelRequestTests.A_name_outside_the_catalogue_gets_no_hit(name: "")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.ModelRequestTests.A_name_outside_the_catalogue_gets_no_hit(name: "LibreOffice")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.ModelRequestTests.A_name_outside_the_catalogue_gets_no_hit(name: "T99")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.ModelRequestTests.A_name_outside_the_catalogue_gets_no_hit(name: "docx")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.ModelRequestTests.A_name_outside_the_catalogue_gets_no_hit(name: "rm")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.ModelRequestTests.A_name_outside_the_catalogue_gets_no_hit(name: "soffice.exe")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.ModelRequestTests.A_name_outside_the_catalogue_gets_no_hit(name: "t3")`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.ModelRequestTests.An_item_that_is_there_or_declined_or_already_offered_is_not_offered_again`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.NoProseSignalTests.A_command_that_only_talks_about_a_missing_tool_is_not_a_missing_tool`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.NoProseSignalTests.A_skill_or_tool_request_with_empty_or_odd_text_gives_nothing_and_never_throws`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.NoProseSignalTests.An_event_the_detector_does_not_know_is_ignored_whatever_it_says`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.NoProseSignalTests.Prose_between_real_events_does_not_change_what_the_real_events_give`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.NoProseSignalTests.The_detector_has_no_seam_to_the_machine_it_decides_from_the_facts_it_is_given`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.OfferCardTests.A_need_shared_by_two_requested_items_is_in_the_card_once`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.OfferCardTests.A_skill_set_requested_by_the_model_brings_its_missing_needs_and_a_size_the_plan_does_not_know_is_unknown_not_zero`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.OfferCardTests.A_suppressed_or_attached_item_is_not_part_of_the_card_but_its_neighbour_still_is`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.OfferCardTests.An_item_that_needs_others_brings_the_missing_ones_first_in_dependency_order_and_all_sizes_are_added`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.OfferCardTests.Items_found_by_different_events_of_one_batch_still_make_one_card`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.OfferCardTests.Needs_that_are_installed_found_or_already_on_their_way_are_left_out_of_the_card`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.OfferCardTests.No_missing_item_means_no_card_and_the_result_is_the_same_whatever_the_event_list_is_called_with`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.OfferCardTests.Two_missing_items_found_together_share_one_card_with_the_first_found_first_and_a_combined_size`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.SkillNeedTests.A_named_set_decides_even_when_two_are_installed_and_a_wrong_name_gives_nothing`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.SkillNeedTests.A_need_that_is_installed_or_found_on_this_PC_is_not_offered`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.SkillNeedTests.A_skill_with_no_listed_needs_or_not_in_the_catalogue_gives_nothing`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.SkillNeedTests.A_used_skill_offers_each_per_skill_need_the_catalogue_lists_that_is_missing`
- **Passed** `Marrow.Core.Tests.Port.Skills.Detect.SkillNeedTests.An_unnamed_set_is_taken_from_the_installed_sets_and_an_ambiguous_or_uninstalled_one_gives_nothing`
- **Passed** `Marrow.Core.Tests.TraceabilityTests.Every_test_names_a_requirement_a_rule_or_a_decision_and_every_id_is_well_formed`
- **Passed** `Marrow.Core.Tests.TraceabilityTests.Test_classes_and_Req_tests_have_the_shape_req_verify_can_read`
- **Passed** `Marrow.Core.Tests.TraceabilityTests.Test_source_has_one_class_per_file_single_line_attributes_and_no_stray_type_keywords`
- **Passed** `Marrow.Services.Tests.Port.Skills.Offers.SkillsOfferDownloadTests.Cancelled_offer_cannot_reach_a_download_plan_or_installer`
- **Passed** `Marrow.Services.Tests.Port.Skills.Offers.SkillsOfferDownloadTests.Checked_download_is_installed_and_published_before_the_offer_can_resume`
- **Passed** `Marrow.Services.Tests.Port.Skills.Offers.SkillsOfferDownloadTests.Failed_routing_publication_never_returns_an_installed_path`
- **Passed** `Marrow.Services.Tests.Port.Skills.Offers.SkillsOfferDownloadTests.Unpinned_item_cannot_reach_a_download_plan_or_installer`
- **Passed** `Marrow.Services.Tests.Port.Skills.Pipelines.PipelineDownloadTests.SK3_bridge_fetches_pinned_fixture_inputs_only_from_loopback_and_installs_offline`
- **Passed** `Marrow.Services.Tests.Port.Skills.Pipelines.PipelineDownloadTests.SK3_bridge_without_a_bound_fake_fails_closed_and_produces_no_install`
- **Passed** `Marrow.Services.Tests.Port.Skills.Providers.ClaudeSkillsOfferDenialTests.S1_decline_reason_reaches_Claudes_control_response_byte_equal`
- **Failed** `Marrow.Services.Tests.Store.TokenLedgerSecondTests.A_launch_after_a_run_that_used_codex_moves_that_runs_spend_into_settled_provenance`
- **Passed** `Marrow.Services.Tests.Store.TokenLedgerSecondTests.A_legacy_preference_value_that_is_not_a_dictionary_of_dictionaries_is_left_alone_and_nothing_is_saved`
- **Passed** `Marrow.Services.Tests.Store.TokenLedgerSecondTests.A_save_that_fails_is_silent_and_the_migrated_map_is_still_returned`
- **Failed** `Marrow.Services.Tests.Store.TokenLedgerSecondTests.A_second_call_made_while_the_first_scan_is_in_flight_returns_at_once_and_the_scan_leaves_the_main_actor`
- **Failed** `Marrow.Services.Tests.Store.TokenLedgerSecondTests.A_v1_file_alone_is_removed_and_yields_an_empty_map_with_no_v3_file_written`
- **Failed** `Marrow.Services.Tests.Store.TokenLedgerSecondTests.A_v1_file_is_deleted_without_being_migrated_even_when_the_v3_file_is_present`
- **Failed** `Marrow.Services.Tests.Store.TokenLedgerSecondTests.A_v2_file_is_migrated_forward_filling_empty_identities_from_the_path_then_deleted_and_saved_as_v3`
- **Failed** `Marrow.Services.Tests.Store.TokenLedgerSecondTests.A_v3_file_is_used_when_it_decodes_and_fills_missing_tokens_day_and_identity_with_defaults`
- **Failed** `Marrow.Services.Tests.Store.TokenLedgerSecondTests.A_v3_file_that_does_not_decode_falls_through_to_the_v2_file`
- **Passed** `Marrow.Services.Tests.Store.TokenLedgerSecondTests.Building_the_ledger_scans_nothing`
- **Failed** `Marrow.Services.Tests.Store.TokenLedgerSecondTests.Later_refreshes_in_the_same_process_scan_nothing_again`
- **Failed** `Marrow.Services.Tests.Store.TokenLedgerSecondTests.Live_codex_spend_recorded_by_an_earlier_process_is_added_into_settled_provenance_on_load`
- **Failed** `Marrow.Services.Tests.Store.TokenLedgerSecondTests.Spend_rolled_forward_on_load_is_subtracted_from_that_rollouts_growth_by_the_first_scan`
- **Failed** `Marrow.Services.Tests.Store.TokenLedgerSecondTests.The_first_refresh_scans_rollouts_older_than_the_launch_moment_and_merges_history_live_and_legacy_totals`
- **Failed** `Marrow.Services.Tests.Store.TokenLedgerSecondTests.The_legacy_preference_dictionary_is_removed_converted_and_saved_as_v3_dropping_records_it_cannot_read`
- **Failed** `Marrow.Services.Tests.Store.TokenLedgerSecondTests.The_scan_saves_one_fingerprint_for_each_rollout_it_counted`
- **Passed** `Marrow.Services.Tests.Store.TokenLedgerSecondTests.With_no_live_codex_spend_the_roll_forward_leaves_settled_provenance_as_it_was`
- **Passed** `Marrow.Services.Tests.Store.TokenLedgerTests.Build_CoversTheWholeYearInSundayFirstColumnsWithFutureDaysAsCells`
- **Passed** `Marrow.Services.Tests.Store.TokenLedgerTests.Build_CumulativeModeWritesARunningTotalIntoEveryCellIncludingOutOfYearOnes`
- **Passed** `Marrow.Services.Tests.Store.TokenLedgerTests.Build_DailyModePutsEachDaysOwnTotalInItsCellAndZeroElsewhere`
- **Passed** `Marrow.Services.Tests.Store.TokenLedgerTests.Build_MonthTicksAreTwelveFixedEnglishAbbreviationsAtEachMonthsFirstColumn`
- **Passed** `Marrow.Services.Tests.Store.TokenLedgerTests.Build_TheThreeModesScaleTheSameDataDifferentlyAndMaxValueIsThePostScaleMaximum`
- **Passed** `Marrow.Services.Tests.Store.TokenLedgerTests.Build_WeeklyModeSumsTheColumnIntoEveryRealCellAndLeavesOutOfYearCellsAtZero`
- **Passed** `Marrow.Services.Tests.Store.TokenLedgerTests.EventTimestamps_ParseFractionalSecondsFirstThenWholeSeconds`
- **Passed** `Marrow.Services.Tests.Store.TokenLedgerTests.FlushLiveSpend_IsANoOpWithNothingPending`
- **Failed** `Marrow.Services.Tests.Store.TokenLedgerTests.FlushLiveSpend_WritesImmediatelyAndCancelsThePendingTimer`
- **Passed** `Marrow.Services.Tests.Store.TokenLedgerTests.LiveSpendStateStore_ReadsTheV1KeyOnceWhenTheV2FileIsAbsentAndNeverRewritesIt`
- **Failed** `Marrow.Services.Tests.Store.TokenLedgerTests.LiveSpendState_PersistsExactlyFourNamedMapsAtomically`
- **Failed** `Marrow.Services.Tests.Store.TokenLedgerTests.MergedTotals_AddsLegacyLiveAndHistorySourcesRatherThanAccumulating`
- **Failed** `Marrow.Services.Tests.Store.TokenLedgerTests.Reconcile_ClearsSettledLiveSpendFromTheNewestDayFirstAndNeverGoesNegative`
- **Passed** `Marrow.Services.Tests.Store.TokenLedgerTests.RecordCodexSpend_DropsAnEmptyRolloutIDAndLowercasesANonEmptyOne`
- **Failed** `Marrow.Services.Tests.Store.TokenLedgerTests.Record_DebouncesTheLiveSaveByTwoSecondsAndLandsOneWrite`
- **Passed** `Marrow.Services.Tests.Store.TokenLedgerTests.Record_IgnoresNonPositiveSpendAndKeysByLocalDay`
- **Passed** `Marrow.Services.Tests.Store.TokenLedgerTests.RolloutIdentity_MatchesTheLastUuidInTheBaseNameCaseInsensitivelyAndLowercasesIt(path: "C:\\home\\.codex\\sessions\\no-uuid-here.jsonl", expected: "C:\\home\\.codex\\sessions\\no-uuid-here.jsonl")`
- **Passed** `Marrow.Services.Tests.Store.TokenLedgerTests.RolloutIdentity_MatchesTheLastUuidInTheBaseNameCaseInsensitivelyAndLowercasesIt(path: "C:\\home\\.codex\\sessions\\rollout-2026-01-01T00-"···, expected: "abcdef01-1111-2222-3333-444444444444")`
- **Passed** `Marrow.Services.Tests.Store.TokenLedgerTests.RolloutIdentity_TakesTheLaterUuidWhenTwoAreInTheName`
- **Failed** `Marrow.Services.Tests.Store.TokenLedgerTests.ScanCodexHistory_AcceptsATailTotalTokensGivenAsAFractionalNumberTruncatedTowardZero`
- **Failed** `Marrow.Services.Tests.Store.TokenLedgerTests.ScanCodexHistory_AcceptsATailTotalTokensGivenAsANumericJsonString`
- **Failed** `Marrow.Services.Tests.Store.TokenLedgerTests.ScanCodexHistory_ContributesOnlyTheFilesOwnGrowthOverItsInheritedHistory`
- **Failed** `Marrow.Services.Tests.Store.TokenLedgerTests.ScanCodexHistory_DoesNotSubtractAnUnterminatedInheritedRecordAtEndOfFile`
- **Failed** `Marrow.Services.Tests.Store.TokenLedgerTests.ScanCodexHistory_OnlyCountsJsonlFilesWithinTheWindowAndBeforeLiveCutoff`
- **Failed** `Marrow.Services.Tests.Store.TokenLedgerTests.ScanCodexHistory_OnlySubtractsAnInheritedRecordWhoseNewlineFitsInsideTheHeadBudget(newlineInsideBudget: False, expectedTokens: 260)`
- **Failed** `Marrow.Services.Tests.Store.TokenLedgerTests.ScanCodexHistory_OnlySubtractsAnInheritedRecordWhoseNewlineFitsInsideTheHeadBudget(newlineInsideBudget: True, expectedTokens: 60)`
- **Passed** `Marrow.Services.Tests.Store.TokenLedgerTests.ScanCodexHistory_PrunesFingerprintsForFilesNoLongerSeen`
- **Failed** `Marrow.Services.Tests.Store.TokenLedgerTests.ScanCodexHistory_RejectsATotalTokensGivenAsABooleanAndContributesNothing`
- **Failed** `Marrow.Services.Tests.Store.TokenLedgerTests.ScanCodexHistory_ReusesACachedFingerprintWhenMtimeAndSizeMatch`
- **Passed** `Marrow.Services.Tests.Store.TokenLedgerTests.SeedBaseline_IsANoOpOnceAlreadySeeded`
- **Passed** `Marrow.Services.Tests.Store.TokenLedgerTests.SeedBaseline_SetsTheBaselineWithoutRecordingWhenUnseeded`
- **Passed** `Marrow.Services.Tests.Store.TokenLedgerTests.Spend_ADropReseedsSilentlyAndReturnsZero`
- **Passed** `Marrow.Services.Tests.Store.TokenLedgerTests.Spend_FirstCallSeedsFromMinOfTotalAndBefore_ThenReturnsPositiveDelta`
- **Passed** `Marrow.Services.Tests.Store.TokenLedgerTests.Spend_NegativeTotalsClampToZeroAtBaselineAndAssignment`
- **Passed** `Marrow.Services.Tests.Store.TokenLedgerTests.TokenActivityMode_HasThreeCasesInDeclarationOrderWithFixedTitles`
- **Passed** `Marrow.Services.Tests.Store.TokenLedgerTests.UncertainTokensThisYear_IsZeroWithNoLegacyValues`
- **Passed** `Marrow.Services.Tests.Store.TokenLedgerTests.UncertainTokensThisYear_SumsOnlyLegacyDaysInTheCurrentYear`
- **Passed** `Marrow.Services.Tests.TraceabilityTests.Every_test_names_a_requirement_a_rule_or_a_decision_and_every_id_is_well_formed`
- **Passed** `Marrow.Services.Tests.TraceabilityTests.Test_classes_and_Req_tests_have_the_shape_req_verify_can_read`
- **Passed** `Marrow.Services.Tests.TraceabilityTests.Test_source_has_one_class_per_file_single_line_attributes_and_no_stray_type_keywords`
- **Passed** `Marrow.UI.Tests.TraceabilityTests.Every_test_names_a_requirement_a_rule_or_a_decision_and_every_id_is_well_formed`
- **Passed** `Marrow.UI.Tests.TraceabilityTests.Test_classes_and_Req_tests_have_the_shape_req_verify_can_read`
- **Passed** `Marrow.UI.Tests.TraceabilityTests.Test_source_has_one_class_per_file_single_line_attributes_and_no_stray_type_keywords`

## Exact failure messages


### Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.Failure_keeps_S1_reason_and_Retry_runs_again

```text
Assert.Null() Failure: Value is not null
Expected: null
Actual:   "Unable to load shared library 'icu.dll' or one of "···
```

### Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.Progress_exposes_bytes_and_checking_before_done

```text
Assert.Equal() Failure: Values differ
Expected: Continuing
Actual:   Failed
```

### Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.R2_queues_S1_once_and_sends_after_turn_ends(status: Completed)

```text
System.DllNotFoundException : Unable to load shared library 'icu.dll' or one of its dependencies. In order to help diagnose loading problems, consider using a tool like strace. If you're using glibc, consider setting the LD_DEBUG environment variable:
/workspace/marrow-port/tests/Marrow.App.Tests/bin/Debug/net10.0-windows10.0.19041.0/runtimes/linux-x64/native/icu.dll.so: cannot open shared object file: No such file or directory
/tmp/sk12-dotnet/shared/Microsoft.NETCore.App/10.0.12/icu.dll.so: cannot open shared object file: No such file or directory
/workspace/marrow-port/tests/Marrow.App.Tests/bin/Debug/net10.0-windows10.0.19041.0/runtimes/linux-x64/native/libicu.dll.so: cannot open shared object file: No such file or directory
/tmp/sk12-dotnet/shared/Microsoft.NETCore.App/10.0.12/libicu.dll.so: cannot open shared object file: No such file or directory
/workspace/marrow-port/tests/Marrow.App.Tests/bin/Debug/net10.0-windows10.0.19041.0/runtimes/linux-x64/native/icu.dll: cannot open shared object file: No such file or directory
/tmp/sk12-dotnet/shared/Microsoft.NETCore.App/10.0.12/icu.dll: cannot open shared object file: No such file or directory
/workspace/marrow-port/tests/Marrow.App.Tests/bin/Debug/net10.0-windows10.0.19041.0/runtimes/linux-x64/native/libicu.dll: cannot open shared object file: No such file or directory
/tmp/sk12-dotnet/shared/Microsoft.NETCore.App/10.0.12/libicu.dll: cannot open shared object file: No such file or directory

```

### Marrow.App.Tests.Port.Skills.SkillsOfferFlowTests.R3_sends_S1_immediately_and_preserves_composer_draft

```text
System.DllNotFoundException : Unable to load shared library 'icu.dll' or one of its dependencies. In order to help diagnose loading problems, consider using a tool like strace. If you're using glibc, consider setting the LD_DEBUG environment variable:
/workspace/marrow-port/tests/Marrow.App.Tests/bin/Debug/net10.0-windows10.0.19041.0/runtimes/linux-x64/native/icu.dll.so: cannot open shared object file: No such file or directory
/tmp/sk12-dotnet/shared/Microsoft.NETCore.App/10.0.12/icu.dll.so: cannot open shared object file: No such file or directory
/workspace/marrow-port/tests/Marrow.App.Tests/bin/Debug/net10.0-windows10.0.19041.0/runtimes/linux-x64/native/libicu.dll.so: cannot open shared object file: No such file or directory
/tmp/sk12-dotnet/shared/Microsoft.NETCore.App/10.0.12/libicu.dll.so: cannot open shared object file: No such file or directory
/workspace/marrow-port/tests/Marrow.App.Tests/bin/Debug/net10.0-windows10.0.19041.0/runtimes/linux-x64/native/icu.dll: cannot open shared object file: No such file or directory
/tmp/sk12-dotnet/shared/Microsoft.NETCore.App/10.0.12/icu.dll: cannot open shared object file: No such file or directory
/workspace/marrow-port/tests/Marrow.App.Tests/bin/Debug/net10.0-windows10.0.19041.0/runtimes/linux-x64/native/libicu.dll: cannot open shared object file: No such file or directory
/tmp/sk12-dotnet/shared/Microsoft.NETCore.App/10.0.12/libicu.dll: cannot open shared object file: No such file or directory

```

### Marrow.App.Tests.Views.Settings.HydraSettingsPageRowSpecTests.Add_pair_is_enabled_with_no_provider_installed_and_adds_one_on_the_default_provider

```text
System.InvalidOperationException : Could not create glyphTypeface. Font family: $Default (key: ). Style: Normal. Weight: Normal. Stretch: Normal
```

### Marrow.App.Tests.Views.Settings.HydraSettingsPageRowSpecTests.The_cookbook_button_is_the_last_thing_in_the_Pairs_header

```text
System.InvalidOperationException : Could not create glyphTypeface. Font family: $Default (key: ). Style: Normal. Weight: Normal. Stretch: Normal
```

### Marrow.App.Tests.Views.Settings.HydraSettingsPageRowSpecTests.The_cookbook_button_opens_a_bottom_popover_holding_the_460_by_540_panel

```text
System.InvalidOperationException : Could not create glyphTypeface. Font family: $Default (key: ). Style: Normal. Weight: Normal. Stretch: Normal
```

### Marrow.App.Tests.Views.Settings.HydraSettingsPageRowSpecTests.The_minus_control_is_24_square_and_removes_with_no_confirmation

```text
System.DllNotFoundException : Unable to load shared library 'user32.dll' or one of its dependencies. In order to help diagnose loading problems, consider using a tool like strace. If you're using glibc, consider setting the LD_DEBUG environment variable:
/workspace/marrow-port/tests/Marrow.App.Tests/bin/Debug/net10.0-windows10.0.19041.0/runtimes/linux-x64/native/user32.dll.so: cannot open shared object file: No such file or directory
/tmp/sk12-dotnet/shared/Microsoft.NETCore.App/10.0.12/user32.dll.so: cannot open shared object file: No such file or directory
/workspace/marrow-port/tests/Marrow.App.Tests/bin/Debug/net10.0-windows10.0.19041.0/user32.dll.so: cannot open shared object file: No such file or directory
/workspace/marrow-port/tests/Marrow.App.Tests/bin/Debug/net10.0-windows10.0.19041.0/runtimes/linux-x64/native/libuser32.dll.so: cannot open shared object file: No such file or directory
/tmp/sk12-dotnet/shared/Microsoft.NETCore.App/10.0.12/libuser32.dll.so: cannot open shared object file: No such file or directory
/workspace/marrow-port/tests/Marrow.App.Tests/bin/Debug/net10.0-windows10.0.19041.0/libuser32.dll.so: cannot open shared object file: No such file or directory
/workspace/marrow-port/tests/Marrow.App.Tests/bin/Debug/net10.0-windows10.0.19041.0/runtimes/linux-x64/native/user32.dll: cannot open shared object file: No such file or directory
/tmp/sk12-dotnet/shared/Microsoft.NETCore.App/10.0.12/user32.dll: cannot open shared object file: No such file or directory
/workspace/marrow-port/tests/Marrow.App.Tests/bin/Debug/net10.0-windows10.0.19041.0/user32.dll: cannot open shared object file: No such file or directory
/workspace/marrow-port/tests/Marrow.App.Tests/bin/Debug/net10.0-windows10.0.19041.0/runtimes/linux-x64/native/libuser32.dll: cannot open shared object file: No such file or directory
/tmp/sk12-dotnet/shared/Microsoft.NETCore.App/10.0.12/libuser32.dll: cannot open shared object file: No such file or directory
/workspace/marrow-port/tests/Marrow.App.Tests/bin/Debug/net10.0-windows10.0.19041.0/libuser32.dll: cannot open shared object file: No such file or directory

```

### Marrow.App.Tests.Views.Settings.HydraSettingsPageRowSpecTests.With_pairs_Add_pair_sits_in_the_Another_pair_row_and_appends

```text
System.InvalidOperationException : Could not create glyphTypeface. Font family: $Default (key: ). Style: Normal. Weight: Normal. Stretch: Normal
```

### Marrow.App.Tests.Views.Settings.HydraSettingsPageSpecTests.Auto_hide_heads_in_the_sidebar_is_a_switch_off_by_default_with_its_exact_detail

```text
System.InvalidOperationException : Could not create glyphTypeface. Font family: $Default (key: ). Style: Normal. Weight: Normal. Stretch: Normal
```

### Marrow.App.Tests.Views.Settings.HydraSettingsPageSpecTests.Checkout_lists_Own_copy_before_Shared_and_its_detail_follows_the_choice

```text
System.InvalidOperationException : Could not create glyphTypeface. Font family: $Default (key: ). Style: Normal. Weight: Normal. Stretch: Normal
```

### Marrow.App.Tests.Views.Settings.HydraSettingsPageSpecTests.Clear_finished_heads_is_a_switch_off_by_default_with_its_exact_detail

```text
System.InvalidOperationException : Could not create glyphTypeface. Font family: $Default (key: ). Style: Normal. Weight: Normal. Stretch: Normal
```

### Marrow.App.Tests.Views.Settings.HydraSettingsPageSpecTests.Head_panel_lists_Progress_before_Every_step_and_keeps_one_detail

```text
System.InvalidOperationException : Could not create glyphTypeface. Font family: $Default (key: ). Style: Normal. Weight: Normal. Stretch: Normal
```

### Marrow.App.Tests.Views.Settings.HydraSettingsPageSpecTests.Heads_at_once_hovered_segment_takes_the_eight_percent_overlay

```text
System.InvalidOperationException : Could not create glyphTypeface. Font family: $Default (key: ). Style: Normal. Weight: Normal. Stretch: Normal
```

### Marrow.App.Tests.Views.Settings.HydraSettingsPageSpecTests.Heads_at_once_is_nine_segments_and_its_detail_is_not_pluralised

```text
System.InvalidOperationException : Could not create glyphTypeface. Font family: $Default (key: ). Style: Normal. Weight: Normal. Stretch: Normal
```

### Marrow.App.Tests.Views.Settings.HydraSettingsPageSpecTests.Heads_at_once_segments_have_the_sources_geometry

```text
System.InvalidOperationException : Could not create glyphTypeface. Font family: $Default (key: ). Style: Normal. Weight: Normal. Stretch: Normal
```

### Marrow.App.Tests.Views.Settings.HydraSettingsPageSpecTests.Heads_at_once_selected_segment_is_semibold_on_an_accent_capsule_and_the_rest_are_plain

```text
System.InvalidOperationException : Could not create glyphTypeface. Font family: $Default (key: ). Style: Normal. Weight: Normal. Stretch: Normal
```

### Marrow.App.Tests.Views.Settings.HydraSettingsPageSpecTests.Heads_panels_lists_One_panel_before_A_panel_each_and_its_detail_follows_the_choice

```text
System.InvalidOperationException : Could not create glyphTypeface. Font family: $Default (key: ). Style: Normal. Weight: Normal. Stretch: Normal
```

### Marrow.App.Tests.Views.Settings.HydraSettingsPageSpecTests.Heads_work_at_a_working_effort_is_a_switch_on_by_default_with_its_exact_detail

```text
System.InvalidOperationException : Could not create glyphTypeface. Font family: $Default (key: ). Style: Normal. Weight: Normal. Stretch: Normal
```

### Marrow.App.Tests.Views.Settings.HydraSettingsPageSpecTests.Messages_holds_the_two_toggles_in_order_with_one_divider

```text
System.InvalidOperationException : Could not create glyphTypeface. Font family: $Default (key: ). Style: Normal. Weight: Normal. Stretch: Normal
```

### Marrow.App.Tests.Views.Settings.HydraSettingsPageSpecTests.Messages_toggles_default_on_and_off_and_write_their_settings

```text
System.InvalidOperationException : Could not create glyphTypeface. Font family: $Default (key: ). Style: Normal. Weight: Normal. Stretch: Normal
```

### Marrow.App.Tests.Views.Settings.HydraSettingsPageSpecTests.The_Heads_section_headers_info_mark_is_How_heads_work_and_opens_the_heads_explainer

```text
System.InvalidOperationException : Could not create glyphTypeface. Font family: $Default (key: ). Style: Normal. Weight: Normal. Stretch: Normal
```

### Marrow.App.Tests.Views.Settings.HydraSettingsPageSpecTests.The_master_card_has_the_sources_geometry

```text
System.InvalidOperationException : Could not create glyphTypeface. Font family: $Default (key: ). Style: Normal. Weight: Normal. Stretch: Normal
```

### Marrow.App.Tests.Views.Settings.HydraSettingsPageSpecTests.The_master_cards_info_mark_is_How_Hydra_works_and_opens_the_Hydra_explainer

```text
System.InvalidOperationException : Could not create glyphTypeface. Font family: $Default (key: ). Style: Normal. Weight: Normal. Stretch: Normal
```

### Marrow.App.Tests.Views.Settings.HydraSettingsPageSpecTests.The_working_effort_detail_holds_one_em_dash_between_single_spaces

```text
System.InvalidOperationException : Could not create glyphTypeface. Font family: $Default (key: ). Style: Normal. Weight: Normal. Stretch: Normal
```

### Marrow.App.Tests.Views.Settings.HydraSettingsPageSpecTests.When_the_team_is_done_holds_the_two_toggles_in_order_with_one_divider

```text
System.InvalidOperationException : Could not create glyphTypeface. Font family: $Default (key: ). Style: Normal. Weight: Normal. Stretch: Normal
```

### Marrow.App.Tests.Views.Settings.HydraSettingsPageSpecTests.When_the_team_is_done_toggles_default_off_and_write_their_settings

```text
System.InvalidOperationException : Could not create glyphTypeface. Font family: $Default (key: ). Style: Normal. Weight: Normal. Stretch: Normal
```

### Marrow.App.Tests.Views.Settings.HydraSettingsPageSpecTests.With_Hydra_off_the_note_sits_under_the_card_and_the_sections_dim

```text
System.InvalidOperationException : Could not create glyphTypeface. Font family: $Default (key: ). Style: Normal. Weight: Normal. Stretch: Normal
```

### Marrow.Services.Tests.Store.TokenLedgerSecondTests.A_launch_after_a_run_that_used_codex_moves_that_runs_spend_into_settled_provenance

```text
System.IO.FileNotFoundException : Could not find file '/workspace/marrow-port/tests/Marrow.Services.Tests/bin/Debug/net10.0-windows/\tmp\sk12-data/StorageTests/1cebac6779694bbaa6b1225fc570e908/root/token-activity-live-v2.json'.
```

### Marrow.Services.Tests.Store.TokenLedgerSecondTests.A_second_call_made_while_the_first_scan_is_in_flight_returns_at_once_and_the_scan_leaves_the_main_actor

```text
System.Collections.Generic.KeyNotFoundException : The given key '2026-05-01' was not present in the dictionary.
```

### Marrow.Services.Tests.Store.TokenLedgerSecondTests.A_v1_file_alone_is_removed_and_yields_an_empty_map_with_no_v3_file_written

```text
Assert.False() Failure
Expected: False
Actual:   True
```

### Marrow.Services.Tests.Store.TokenLedgerSecondTests.A_v1_file_is_deleted_without_being_migrated_even_when_the_v3_file_is_present

```text
Assert.False() Failure
Expected: False
Actual:   True
```

### Marrow.Services.Tests.Store.TokenLedgerSecondTests.A_v2_file_is_migrated_forward_filling_empty_identities_from_the_path_then_deleted_and_saved_as_v3

```text
System.Collections.Generic.KeyNotFoundException : The given key 'C:\sessions\rollout-2026-03-04T10-00-00-AAAAAAAA-1111-2222-3333-444444444444.jsonl' was not present in the dictionary.
```

### Marrow.Services.Tests.Store.TokenLedgerSecondTests.A_v3_file_is_used_when_it_decodes_and_fills_missing_tokens_day_and_identity_with_defaults

```text
Assert.Single() Failure: The collection was empty
```

### Marrow.Services.Tests.Store.TokenLedgerSecondTests.A_v3_file_that_does_not_decode_falls_through_to_the_v2_file

```text
Assert.Equal() Failure: Collections differ
           ↓ (pos 0)
Expected: ["C:\\v2\\b.jsonl"]
Actual:   []
```

### Marrow.Services.Tests.Store.TokenLedgerSecondTests.Later_refreshes_in_the_same_process_scan_nothing_again

```text
System.Collections.Generic.KeyNotFoundException : The given key '2026-05-01' was not present in the dictionary.
```

### Marrow.Services.Tests.Store.TokenLedgerSecondTests.Live_codex_spend_recorded_by_an_earlier_process_is_added_into_settled_provenance_on_load

```text
Assert.Equal() Failure: Values differ
Expected: 3
Actual:   2
```

### Marrow.Services.Tests.Store.TokenLedgerSecondTests.Spend_rolled_forward_on_load_is_subtracted_from_that_rollouts_growth_by_the_first_scan

```text
System.Collections.Generic.KeyNotFoundException : The given key '2026-05-02' was not present in the dictionary.
```

### Marrow.Services.Tests.Store.TokenLedgerSecondTests.The_first_refresh_scans_rollouts_older_than_the_launch_moment_and_merges_history_live_and_legacy_totals

```text
Assert.Equal() Failure: Values differ
Expected: 112
Actual:   12
```

### Marrow.Services.Tests.Store.TokenLedgerSecondTests.The_legacy_preference_dictionary_is_removed_converted_and_saved_as_v3_dropping_records_it_cannot_read

```text
System.IO.FileNotFoundException : Could not find file '/workspace/marrow-port/tests/Marrow.Services.Tests/bin/Debug/net10.0-windows/\tmp\sk12-data/StorageTests/dce2e7a1e2be484993c37a59aa495f6f/root/token-activity-codex-v3.json'.
```

### Marrow.Services.Tests.Store.TokenLedgerSecondTests.The_scan_saves_one_fingerprint_for_each_rollout_it_counted

```text
System.IO.FileNotFoundException : Could not find file '/workspace/marrow-port/tests/Marrow.Services.Tests/bin/Debug/net10.0-windows/\tmp\sk12-data/StorageTests/285091752c954ba3a328dc868668a16d/root/token-activity-codex-v3.json'.
```

### Marrow.Services.Tests.Store.TokenLedgerTests.FlushLiveSpend_WritesImmediatelyAndCancelsThePendingTimer

```text
Assert.True() Failure
Expected: True
Actual:   False
```

### Marrow.Services.Tests.Store.TokenLedgerTests.LiveSpendState_PersistsExactlyFourNamedMapsAtomically

```text
System.IO.FileNotFoundException : Could not find file '/workspace/marrow-port/tests/Marrow.Services.Tests/bin/Debug/net10.0-windows/\tmp\sk12-data/StorageTests/6430996478c9419ea49b28813e238f79/root/token-activity-live-v2.json'.
```

### Marrow.Services.Tests.Store.TokenLedgerTests.MergedTotals_AddsLegacyLiveAndHistorySourcesRatherThanAccumulating

```text
Assert.Equal() Failure: Values differ
Expected: 8
Actual:   5
```

### Marrow.Services.Tests.Store.TokenLedgerTests.Reconcile_ClearsSettledLiveSpendFromTheNewestDayFirstAndNeverGoesNegative

```text
System.Collections.Generic.KeyNotFoundException : The given key '2026-01-05' was not present in the dictionary.
```

### Marrow.Services.Tests.Store.TokenLedgerTests.Record_DebouncesTheLiveSaveByTwoSecondsAndLandsOneWrite

```text
Assert.True() Failure
Expected: True
Actual:   False
```

### Marrow.Services.Tests.Store.TokenLedgerTests.ScanCodexHistory_AcceptsATailTotalTokensGivenAsAFractionalNumberTruncatedTowardZero

```text
System.Collections.Generic.KeyNotFoundException : The given key '2026-01-05' was not present in the dictionary.
```

### Marrow.Services.Tests.Store.TokenLedgerTests.ScanCodexHistory_AcceptsATailTotalTokensGivenAsANumericJsonString

```text
System.Collections.Generic.KeyNotFoundException : The given key '2026-01-05' was not present in the dictionary.
```

### Marrow.Services.Tests.Store.TokenLedgerTests.ScanCodexHistory_ContributesOnlyTheFilesOwnGrowthOverItsInheritedHistory

```text
System.Collections.Generic.KeyNotFoundException : The given key '2026-01-05' was not present in the dictionary.
```

### Marrow.Services.Tests.Store.TokenLedgerTests.ScanCodexHistory_DoesNotSubtractAnUnterminatedInheritedRecordAtEndOfFile

```text
System.Collections.Generic.KeyNotFoundException : The given key '2026-01-05' was not present in the dictionary.
```

### Marrow.Services.Tests.Store.TokenLedgerTests.ScanCodexHistory_OnlyCountsJsonlFilesWithinTheWindowAndBeforeLiveCutoff

```text
Assert.Single() Failure: The collection was empty
```

### Marrow.Services.Tests.Store.TokenLedgerTests.ScanCodexHistory_OnlySubtractsAnInheritedRecordWhoseNewlineFitsInsideTheHeadBudget(newlineInsideBudget: False, expectedTokens: 260)

```text
System.Collections.Generic.KeyNotFoundException : The given key '2026-01-05' was not present in the dictionary.
```

### Marrow.Services.Tests.Store.TokenLedgerTests.ScanCodexHistory_OnlySubtractsAnInheritedRecordWhoseNewlineFitsInsideTheHeadBudget(newlineInsideBudget: True, expectedTokens: 60)

```text
System.Collections.Generic.KeyNotFoundException : The given key '2026-01-05' was not present in the dictionary.
```

### Marrow.Services.Tests.Store.TokenLedgerTests.ScanCodexHistory_RejectsATotalTokensGivenAsABooleanAndContributesNothing

```text
System.Collections.Generic.KeyNotFoundException : The given key '/workspace/marrow-port/tests/Marrow.Services.Tests/bin/Debug/net10.0-windows/\tmp\sk12-data/StorageTests/eea58fc068fe407fb22607105663471e/home/home-boolean-total/.codex/sessions/boolean-total-a1b2c3d4-1111-2222-3333-cccccccccccc.jsonl' was not present in the dictionary.
```

### Marrow.Services.Tests.Store.TokenLedgerTests.ScanCodexHistory_ReusesACachedFingerprintWhenMtimeAndSizeMatch

```text
System.Collections.Generic.KeyNotFoundException : The given key '2026-02-01' was not present in the dictionary.
```
