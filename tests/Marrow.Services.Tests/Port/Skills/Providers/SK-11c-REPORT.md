# SK-11c delivery report

Branch: `codex/f-sk-11c`. Base: `7993466ecf09c94f326ca2d6cd58e21518f3d1c8` (`origin/claude/verify-terminal-20261001`). The delivery commit contains this report; use `git rev-parse HEAD` for its full head. No other branch was pushed.

Copilot accepts a caller-owned, verified SK-9 generation root in `skillDirectories` on create and resume. The two specified rejection forms retry once without that key and cache rejection by executable path/size/mtime. Missing identities and damaged/empty generations fail closed. Copilot and ACP emit normalized permission and completion events from their existing structured protocol fields. Synthetic fixture transcripts are committed; no live CLI transcript, network request or real user folder was used.

## Byte identity

`Fixtures/BaselineWire.cs` freezes the existing provider builders from the base commit, independently of the new implementation. Portable tests compare UTF-8 serialized RPC bytes for Copilot create/resume and ACP initialize/new/load/failed-load, argv bytes, and every inherited child-environment entry. With no generation or all skills off, no skills field or capability-probe RPC is added. The sole environment exception is the explicitly required DL-6 `ShimPath.ForChild` insertion of addons\bin into PATH; tests compare that exact insertion and preservation of all other entries. Two additional Windows cases compare every actual Copilot startup RPC body and argv using the existing compiled offline stand-in, but were not executed on this Linux worker.

## Linux verification

Reproduce with the installed .NET 10 SDK on PATH and the existing filesystem `NUGET_PACKAGES` cache:

```sh
python3 tests/Marrow.Services.Tests/Port/Skills/Providers/run-linux.py
```

The runner creates fresh projects/data under `/tmp`, sets `MARROW_PORT_DATA_DIR`, restores only from that filesystem cache, and starts no provider process. Final artifacts were `/tmp/marrow-sk11c-22myi767`; the worker log was `/tmp/sk11c-runner-verified.log`.

| Selection | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Portable SK-11c | 32 | 0 | 0 |
| Portable Services regressions, including SK-9, SK-11d and traceability | 204 | 0 | 0 |
| Core skills/detector/catalog/state and traceability | 347 | 0 | 0 |

Test-first evidence: initial 17 cases had 15 failures before implementation; expansion to 30 had five failures before fixes; explicit unbound-root coverage failed before the root seam was added. These logs are recorded in the engineering decision.

The extended Services selection ran 208 cases: 204 passed and four failed because unchanged Windows code loads `icu.dll`. All four reproduced against the original providers at the base commit (`/tmp/sk11c-original-icu.log`):

- `CopilotSessionThirdTests.Kinds_without_paths_are_described_with_their_exact_titles_details_and_reasons`
- `CopilotSessionThirdTests.The_bracket_holds_one_Character_tried_before_none`
- `CopilotSessionThirdTests.Todos_parse_bullets_and_numbers_with_x_done_blank_pending_and_anything_else_active`
- `CopilotSessionFourthTests.The_approval_card_titles_fall_back_to_their_literals`

Those tests remain unchanged; the portable runner explicitly excludes them. The ordinary Services/solution build is unverified: the local cache lacks voice NAudio/Whisper dependencies, so the temporary Services project excludes Port/Voice. This is a bounded Linux verification, not a successful Windows build.

## Delivered test names

All new tests carry `Feature=SK-11c` and an existing `R-TEST-4`, `R-TEST-8` or `R-TEST-9` Rule trait. Theory rows account for 32 portable cases and two Windows cases.

### ByteIdentityTests.cs

- `Empty_Copilot_session_parameters_are_byte_identical_to_the_existing_provider_code`
- `Empty_ACP_argv_and_initialize_are_byte_identical_to_the_existing_provider_code`
- `Copilot_argv_is_byte_identical_and_PATH_is_the_only_child_environment_change`
- `Empty_Copilot_create_and_resume_RPC_bytes_equal_the_existing_code`
- `Empty_ACP_new_load_and_failed_load_RPC_bytes_equal_the_existing_code`
- `An_unbound_storage_seam_uses_only_the_explicit_throwaway_test_root_for_child_shims`
- `An_unbound_storage_seam_cannot_fall_back_to_a_real_data_folder_or_an_unusable_test_root`

### CopilotSkillWiringTests.cs

- `Create_and_resume_send_the_verified_generation_root_without_loading_instruction_text`
- `Rejected_key_retries_once_and_is_omitted_on_the_next_session_for_the_same_executable`
- `Unrelated_errors_are_never_retried`
- `An_invalid_params_error_without_any_installed_skills_is_not_retried`
- `A_damaged_generation_is_not_sent_to_the_provider`
- `A_failed_retry_is_propagated_without_a_third_request_and_the_executable_is_still_remembered`
- `A_changed_modified_time_retries_the_key_even_with_the_same_path_and_size`
- `A_missing_executable_identity_fails_closed_without_a_capability_probe_or_added_key`
- `A_generation_with_all_skills_off_has_the_existing_provider_parameters_byte_for_byte`

### PermissionTapTests.cs

- `Copilot_fixture_emits_held_failed_and_skill_events_without_changing_timeline_events`
- `ACP_fixture_correlates_sparse_completion_with_the_original_command`
- `Automatically_approved_commands_never_emit_held_events`
- `Prose_unknown_results_and_replayed_ACP_updates_are_not_signals`
- `A_repeated_full_ACP_completion_emits_one_finished_event_with_the_existing_metadata_exit`
- `Hook_resolved_Copilot_permissions_and_declined_tool_results_are_not_missing_tool_signals`
- `Closing_or_stopping_a_provider_discards_command_correlation`

### WindowsByteIdentityTests.cs

- `Empty_Copilot_Start_sends_the_existing_argv_and_every_startup_RPC_body_byte_for_byte`

## Decisions and remaining work

Decisions are appended under `## ED-363` in `porting/engineering_decisions.md` (items 1–10); the original prefix and existing line endings are preserved. No ledger status, existing test, other provider, generation store, installer or shim implementation changed.

ACP receives no invented `skillDirectories` or generic `SkillUsed` field: the plan specifies the former only for Copilot and supplies no documented ACP skill-invocation field. Its documented command permission/completion taps are delivered. App generation acquisition, lease teardown until the connection closes, per-turn verification/notices and SK-12 offer/resume orchestration belong to the later app-wiring units. The Windows startup tests and full solution validation remain for a worker with Windows and the required cached dependencies.
