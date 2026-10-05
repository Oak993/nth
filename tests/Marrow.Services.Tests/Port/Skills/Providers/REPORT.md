SK-11a delivery report, 2026-10-05

Branch: codex/f-sk-11a. Staging merge: bc20daab (origin/claude/verify-terminal-20261001 at 7993466e).
Implemented: gated --plugin-dir for verified per-chat/listing generations and installed plugin folders;
DL-6 PATH on all Claude children; correlated CommandHeld/CommandFinished/SkillUsed taps;
actual-exit lease retention, stable resume and exact moved-root prompt; isolated observer diagnostics.

Tests delivered: 20 methods, 24 xUnit cases, all Feature=SK-11a plus Rule. Exact method names:

- Empty_install_matches_frozen_existing_provider_argv_environment_and_RPC_bytes
- Empty_handshake_matches_existing_provider_without_an_extra_help_or_RPC_call
- Start_passes_generation_and_each_installed_plugin_as_single_arguments_and_keeps_user_command_first
- Missing_plugin_flag_never_acquires_a_generation_and_keeps_existing_argv
- Fixture_tool_use_result_and_can_use_tool_emit_only_correlated_normalized_events
- Taps_leave_existing_provider_events_and_permission_responses_unchanged
- Handshake_passes_verified_listing_and_plugin_folders_and_releases_its_lease
- Resume_after_GC_and_coordinator_restart_keeps_argv_root_and_transcript_skill_directory_verified
- Held_old_generation_uses_new_root_and_exact_plan_moved_line_in_existing_prompt
- Tampered_generation_fails_before_any_provider_process_and_releases_lease
- Edge_fixture_isolates_head_commands_and_ignores_unknown_malformed_noncommand_and_prose_events
- Observer_failure_is_reported_without_changing_approval_or_provider_events
- Tap_keeps_real_permission_response_RPC_bytes_identical
- Session_generation_survives_stdout_EOF_and_Stop_until_child_exit
- Handshake_listing_remains_leased_until_the_terminated_child_actually_exits
- Failed_help_even_with_flag_text_never_adds_plugin_arguments
- Changed_generation_warns_once_before_turn_and_remains_leased_until_exit
- Held_observer_runs_after_the_existing_permission_request_is_registered
- Failed_launch_or_initialization_releases_generation_and_never_uses_a_live_CLI
- Tampered_listing_is_refused_before_provider_spawn_and_probe_lease_is_released

Byte-identity proof: tests run frozen pre-change ClaudeSession and current provider through the same
in-memory help/transport seams. Exact NUL-delimited argv bytes, JSON-lines RPC bytes, and sorted
NUL-delimited environment bytes compare; only the expressly required SK-6 PATH envelope is applied
to the legacy expected environment. Empty Handshake adds no help or RPC. Allow/deny wire bytes compare.
Frozen-copy transformation matches mechanically. Runtime proof NOT RUN: no dotnet executable.

ED written: ED-NEW-f-sk-11a (porting/engineering_decisions.md, appended only).
Independent static review: no remaining definite compile/API issue or required SK-11a discrepancy;
optional launch/initialize/listing failure tests subsequently added. No reviewer files were modified.

Linux results: build and Feature=SK-11a test each exit 127 (dotnet absent). See Linux-validation.txt.
Static fixture parsing, original CRLF checks, frozen-copy comparison and git diff --check PASS.
No passing C# build or xUnit execution claimed. No network/real CLI/user data used by tests.

Not done: runtime Linux tests/build (missing offline SDK/dependencies); live Windows CLI/WU-11/DL-20
checks (outside this unit and forbidden by brief); Read rule/--add-dir (not selected by live evidence);
application factory binding/offer UI (SK-16/DL-19, SK-12, outside permitted files). Ledger statuses unchanged.
Status: IMPLEMENTED; runtime verification BLOCKED. Not a release or exact-parity acceptance claim.
