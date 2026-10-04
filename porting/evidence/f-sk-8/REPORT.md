# SK-8 delivery report

Branch: `codex/f-sk-8`. Tested implementation and test revision: `04d9f8b063be93f0bca57ab2f0eecabb9f14d477`; the following documentation commit changes evidence only. Base: `origin/codex/f-sk-4`, merged `origin/codex/f-sk-5` and `origin/claude/verify-terminal-20261001`. Both engineering-decision conflict sides were retained. No SK-4/SK-5 source/test changes, no ledger status changes, and no other branch pushes.

## Delivered

Update downloads through the SK-3 bridge, installs through unchanged SK-4 in tmp folders beside the old pin, verifies whole, prepares routes, and publishes by one atomic SK-7 state write. The routed SK-6 shim reader and next-generation snapshot select the same installed pins. Failure/cancellation preserves the old selection; true subprocess exits cover all six installer steps, four pipeline steps, and postcommit. Version and generation/source leases retain old versions until GC. Removal cascades Python package sets while skill sets remain with missing needs; reset keeps enabled switches and accepted terms and retries held residue after restart. The UE-2 adapter lists installed/latest pins, progress and outcomes and runs through UpdateOrchestrator. Multi-pin sets and trusted empty directories are covered. Missing download seams and an unconfigured loopback transport fail closed.

The dependency instructions are unchanged: one pipeline owner per root, trusted installed file and directory inventories supplied on restart, and fixed shims bound with PipelineShimFiles.Write. SK-9/SK-19 compose the providers/app around these service APIs; SK-15 owns the terms UI. This unit supplies the terms-required signal and explicit hash-keyed acceptance gate, not the later UI pages.

## Acceptance test names

- `New_pin_switches_shims_and_generations_together_and_old_lease_survives_GC`
- `Failure_at_each_pipeline_step_keeps_previous_pin_usable_after_restart`
- `Installer_crash_at_each_step_preserves_previous_version`
- `Changed_non_open_terms_block_before_download_and_acceptance_allows_update`
- `Update_everything_lists_installed_latest_progress_and_result`
- `Remove_while_leased_removes_route_now_and_deletes_after_lease_ends`
- `Removing_Python_removes_package_sets_and_keeps_skills_with_missing_needs`
- `Reset_preserves_enabled_and_accepted_terms_and_defers_live_tree`
- `Abrupt_process_exit_at_every_step_keeps_old_payload_and_one_complete_routing_snapshot`
- `SK3_bridge_fetches_pinned_fixture_inputs_only_from_loopback_and_installs_offline`
- `SK3_bridge_without_a_bound_fake_fails_closed_and_produces_no_install`
- `Skillset_retry_replaces_unselected_routing_and_publishes_new_spares`
- `A_leased_generation_holds_old_sources_and_GC_removes_unleased_siblings`
- `Multi_pin_set_installs_under_a_safe_key_and_preserves_the_composed_state_pin`
- `Trusted_empty_directories_survive_update_and_extra_directories_fail_verification`
- `A_snapshot_selected_before_an_update_cannot_lease_different_source_pins_after_commit`
- `Actual_shim_reader_selects_the_committed_pin_and_refuses_removed_item`
- `UE2_runs_the_group_and_reports_item_progress_and_updated_versions`
- `Failed_update_retries_without_a_colliding_final_folder`
- `Cancel_before_commit_keeps_previous_pin_and_can_retry`
- `Tampered_routing_cannot_make_GC_delete_the_trusted_active_tree`
- `Missing_pipeline_seams_fail_closed_before_any_download_or_install`

The parameterized rollback tests cover four pipeline and six installer checkpoints. Abrupt_process_exit_at_every_step covers eleven subprocess exit points, bypassing catch/finally. Every test method carries Feature=SK-8 and a valid Rule trait; the traceability checks pass.

## WINDOWS-ONLY tests

- `Locked_update_keeps_old_handle_usable_and_GC_retries_after_close`
- `Reset_retries_held_bin_files_after_restart_and_preserves_enabled_and_terms`

Both explicitly skip on Linux with: "WINDOWS-ONLY: requires NTFS ACLs, named streams and Windows delete-sharing semantics; this worker is not Windows."

## Linux results

- SK-8: 40 passed, 2 WINDOWS-ONLY skipped (42 cases).
- Services regression filter (SK-3/4/5/6/8 plus traceability): 310 passed, 23 platform skips, 0 failed (333 cases), exit 0.
- Core regression filter (SK-1/7/UE-2 plus traceability): 232 passed, 0 skipped/failed, exit 0.
- Existing standalone shim with routed JSON reader: Linux x64 NativeAOT Release publication passed, exit 0. This is a compiler/linker check, not Windows runtime verification.
- git diff --check passed. The SK-4 and SK-5 source/test diff against the setup merge is empty.

Toolchain: isolated Linux .NET SDK 10.0.100. Test processes used MARROW_PORT_DATA_DIR set to fresh /tmp folders; fixture folders were also explicit temporary roots. Network inside tests was either denied or guarded to the loopback fake; no real CLI, user data, or vendor content was used. NuGet restoration was tooling, not application/test traffic.

Commands (with the isolated SDK on PATH and fresh MARROW_PORT_DATA_DIR):

```sh
dotnet test tests/Marrow.Services.Tests --filter 'Feature=SK-8|Feature=SK-3|Feature=SK-4|Feature=SK-5|Feature=SK-6|FullyQualifiedName~TraceabilityTests' -p:EnableWindowsTargeting=true --logger 'trx;LogFileName=sk8-services.trx'
dotnet test tests/Marrow.Core.Tests --filter 'Feature=SK-1|Feature=SK-7|Feature=UE-2|FullyQualifiedName~TraceabilityTests' --logger 'trx;LogFileName=sk8-core.trx'
dotnet publish src/Marrow.Shim/Marrow.Shim.csproj -c Release -r linux-x64 -p:PublishAot=true -o /tmp/sk8-aot
```

## Decisions and remaining checks

`porting/engineering_decisions.md` ends with `ED-NEW-f-sk-8`: atomic pin-selected routing; single root ownership through SK-5 primitives; trusted restart inventories including empty directories; safe hash keys for multi-pin sets; rollback/recovery; durable reset cleanup; and controlled crash/Windows verification boundaries.

Not run: the two Windows runtime tests and Windows NativeAOT runtime, because this worker is Linux. Real vendor downloads, real provider chats, and application/provider/UI composition are the separately planned DL-20/SK-9/SK-15/SK-19 work and are outside this offline service unit. No claim of overall Windows release verification or exact parity is made.

Independent adversarial review found and rechecked fixes for generation GC/leases, reset retry, route/descriptor agreement, skillset retry, multi-pin sets and empty directories. Its final result reported no remaining major blocker, with the documented runtime composition constraints.
