# SK-5 / DL-5 delivery report

Branch: `codex/f-sk-5`. Implementation and acceptance evidence revision: `14aa3237205e6669e3b5df90b2a19d2875476311`, based on requested staging `22902480a`. The final report/evidence commit follows that revision; the push report supplies its exact HEAD. Only this branch is authorized for push. No ledger statuses were changed.

Implemented in `src/Marrow.Services/Port/Skills/Store/`, one type per file: immutable catalogue-derived inventories; fail-closed full-tree and per-launch binary seal verification; native Windows deny ACL/read-only locking, named-stream and reparse checks; damaged SK-7 state and persistent condemnation; same-installed-pin Repair through narrow downloader/installer interfaces; leases, deferred promotion/deletion, GC, and unlock-all teardown. Plain-file pinned skills have an installer; archive materialization is the SK-4 seam. No production network downloader or CLI was added.

## Requirements and acceptance coverage

| Requirement | Delivered acceptance |
| --- | --- |
| DL-5; PLAN WU-4; ORIGINALS W0-4 lock and tamper checks | StoreVerifierTests and WindowsStoreProtectionTests: complete inventory, hidden/system/bytecode/empty extras, hashes/sizes, named streams, reparse points, deny inheritance/read-only checks. |
| WU-4 full check after install/update and before view building; seal every launch | Full_app_update_verification_detects_text_damage_before_use; Full_store_verification_precedes_every_view_build; Every_launch_rechecks_the_binary_seal_and_reports_damaged_without_a_lease. |
| Damaged items never used; same-pin Repair | SkillsStoreTests and StoreLifecycleReviewTests: damaged state survives restart, no lease, pinned/multi-pin downloads, corrupt/missing seam/cancel refusal, pending recheck, final promotion failure retry. |
| DL-5/WU-4 leases and GC | Active pins/build reservations retained; live probe/chat generation leases; old/tmp/ended generations collected; held handles and repair promotion deferred to next GC. |
| WU-4 teardown/unlock order | All denies removed before readonly clearing and deletion; all managed trees unlocked before any deletion; held residues retried next GC; enabled selections and accepted terms retained. |
| Isolation and fail-closed seams | Disposable data roots, fake downloads/installation; hostile paths and fake-home links refused; malformed condemnation left untouched; unsupported real ACL seam throws. |

## Linux results

.NET SDK 10.0.401 / runtime 10.0.12. Each invocation used `MARROW_PORT_DATA_DIR` set to a fresh `/tmp/marrow-*` directory. Fixtures create independent descendants with spaces; no test uses network, a real CLI, or real user data.

| Check | Result |
| --- | --- |
| Feature=SK-5, five consecutive runs | Each: 61 passed, 0 failed, 9 explicit WINDOWS-ONLY skips (70 cases). |
| Final SK-5 + Services traceability/layering | 67 passed, 0 failed, 9 explicit WINDOWS-ONLY skips (76 cases). |
| Core SK-1/SK-7/SK-10 + layering dependency regression | 346 passed, 0 failed, 0 skipped. |
| Final `dotnet build Marrow.sln -c Debug --no-restore -p:EnableWindowsTargeting=true` | Passed, 0 warnings, 0 errors. The CLI property enables Linux cross-compilation; no project file changed. |
| `git diff --check` | Passed before implementation commit and delivery commit. |
| Protected-path checker against 22902480a | Nonzero: only `porting/engineering_decisions.md` flagged, explicitly authorized by this job. macOS reference folders are unavailable on Linux. No guard was weakened. |

The initial test-first run had 44 failed portable cases and 5 explicit native skips before implementation. Separate adversarial lifecycle/failure passes added tests first and exposed five further defects, fixed without deleting or weakening existing tests. This was a separate self-review pass, not an independent agent review. [Raw logs, final TRX and checksums](../evidence/f-sk-5/) are committed; TRX includes theory case names and native skip reasons.

Final Services command:

```sh
MARROW_PORT_DATA_DIR=$(mktemp -d /tmp/marrow-sk5-trx-XXXXXX) /tmp/marrow-dotnet/dotnet test tests/Marrow.Services.Tests/Marrow.Services.Tests.csproj --no-build --no-restore --filter 'Feature=SK-5|FullyQualifiedName~TraceabilityTests|FullyQualifiedName~LayeringTests' --logger 'trx;LogFileName=sk5.trx' --logger 'console;verbosity=normal' --results-directory /tmp/marrow-sk5-delivery
```

Repeated feature command: the same Services project with `--filter 'Feature=SK-5'`, a fresh data root on each invocation. Core command: the Core project with `--filter 'Feature=SK-1|Feature=SK-7|Feature=SK-10|FullyQualifiedName~LayeringTests'`.

## Engineering decisions and W0-4 closure

[ED-NEW-f-sk-5](../../engineering_decisions.md#ed-new-f-sk-5) contains nine entries: scope/integration ownership; explicit root and native platform boundaries; immutable trusted inventories and multi-pin descriptors; full versus seal checks; downloader/installer and Repair contracts; synchronized leases/GC and atomic condemnation format; ordered unlock/teardown; adversarial lifecycle fixes; final promotion/residual/empty-metadata fixes.

The previously missing ORIGINALS gap is closed. Read `skills-ORIGINALS-PLAN.md` from staging commit `3d3068124` via `git show`: section 3.3, W0-2/W0-4 and their acceptance context. All adapted SK-5 requirements were already covered. No missing-original blocker remains. This feature branch was kept on its requested base; it does not merge unrelated staging work.

## Not executed or integrated, and why

- Native Windows/NTFS acceptance: nine tests delivered, not executed on this Linux worker. Their explicit skip reason is `WINDOWS-ONLY: requires NTFS ACLs, named streams and Windows delete-sharing semantics; this worker is not Windows.` Native ACL inheritance, actual delete sharing/current-directory behavior and Win32 junction handling still require a Windows run.
- SK-3's real downloader is not merged; the narrow `IStoreRepairDownloader` seam and offline fakes are delivered and missing configuration fails closed. SK-4 supplies archive installation and trusted installed-file inventory for archive tools/package sets; plain-file skill repair is implemented here.
- SK-6 owns PATH/shims/environment/tree kill; SK-8 owns shim switching/Reset orchestration; SK-9 owns generation building, filtering, spares and launch budgets; SK-13 owns actual Program uninstall registration/UI. This unit supplies their verification, leases, collection and teardown boundaries. No app composition was added.
- Actual LibreOffice timing and any sampling fallback remain DL-20. ONDEMAND section 8 supersedes bundled payload deletion and marrow-run, so W0-4 did not restore those behaviors.
- Global release gates, macOS reference checks and manual shell ACL commands were not run: this worker is Linux with no reference checkout, and the job prohibits tests invoking real CLIs. The native implementation uses .NET/Win32 APIs directly.

## Acceptance test names delivered

Every acceptance method below has `Trait("Feature", "SK-5")`. Theories account for 61 portable cases across 43 methods; nine native methods account for nine explicit skips.

### StoreVerifierTests.cs

- `The_complete_locked_tree_verifies_in_both_data_layouts`
- `Any_extra_file_is_damage_even_when_hidden_or_bytecode`
- `An_empty_extra_folder_is_damage`
- `A_changed_size_or_same_size_changed_hash_is_damage`
- `A_missing_file_or_missing_root_is_damage`
- `Missing_deny_entries_read_only_attributes_or_stream_inspection_fail_closed`
- `A_named_stream_reported_by_the_platform_is_damage`
- `A_file_or_directory_symlink_is_refused_without_following_it`
- `Unsafe_manifest_paths_are_refused_before_IO`
- `A_manifest_is_an_immutable_case_insensitive_set_and_not_an_on_disk_trust_root`
- `Seal_checks_binaries_on_every_launch_and_full_verify_also_checks_text`

### SkillsStoreTests.cs

- `Every_launch_rechecks_the_binary_seal_and_reports_damaged_without_a_lease`
- `Full_app_update_verification_detects_text_damage_before_use`
- `Repair_downloads_the_installed_pin_and_verifies_before_making_it_usable`
- `Corrupt_repair_and_unconfigured_download_or_installer_seams_never_become_installed`
- `A_live_lease_keeps_a_condemned_tree_until_repair_can_publish_and_restart_keeps_the_mark`
- `GC_preserves_active_items_and_leases_and_deletes_old_tools_stores_and_generations`
- `A_running_probe_or_chat_leases_its_generation_until_session_end`
- `A_damaged_generation_is_condemned_never_released_to_a_second_session_and_survives_restart`
- `A_sharing_failure_is_deferred_to_next_GC_without_waiting_or_losing_condemnation`
- `Teardown_unlocks_everything_before_any_delete_and_keeps_enabled_and_accepted_terms`
- `Teardown_leaves_a_held_tree_unlocked_and_later_GC_removes_it`
- `Paths_and_condemnation_cannot_escape_the_data_root_or_follow_a_link_to_a_fake_home`
- `Invalid_condemnation_file_is_not_silently_replaced_and_outside_paths_are_refused`
- `Two_repairs_share_no_download_and_GC_never_deletes_an_active_build`
- `No_production_ACL_seam_silently_succeeds_on_non_Windows`

### StoreLifecycleReviewTests.cs

- `Full_store_verification_precedes_every_view_build`
- `Teardown_deletes_unleased_trees_now_and_leased_trees_after_session_end_while_retaining_terms`
- `A_cancelled_repair_returns_to_damaged_without_changing_the_old_pin`
- `A_held_old_folder_defers_repair_promotion_until_next_GC`
- `Tampering_with_a_pending_repair_is_detected_before_promotion`
- `Plain_file_set_repair_uses_the_same_catalogue_files_and_keeps_the_licence_beside_them`
- `An_unpinned_set_missing_licence_or_replaced_file_inventory_is_refused`
- `GC_never_deletes_a_descendant_of_an_active_installed_tree`
- `A_symlink_in_the_managed_ancestor_or_root_never_reaches_the_fake_home`
- `Teardown_during_a_download_cannot_resurrect_the_store_or_publish_a_repair`
- `Two_chat_generations_are_copies_and_tampering_one_never_changes_the_other_or_store`
- `Multi_pin_sets_keep_their_per_skill_pins_and_explicit_installed_key_during_Repair`
- `Missing_inherited_deny_on_one_file_is_damage_even_with_a_locked_root`
- `A_restart_loads_real_SK_7_state_and_never_releases_the_condemned_pin`
- `Successful_teardown_removes_condemnation_and_empty_addons_metadata`
- `A_failure_after_repair_rename_is_unusable_and_can_be_retried_without_a_stale_pending_repair`
- `A_held_shim_outside_tools_is_unlocked_and_removed_by_the_next_teardown_GC`

### WindowsStoreProtectionTests.cs — WINDOWS-ONLY

- `The_exact_inherited_deny_refuses_create_write_append_attribute_rename_and_delete`
- `Reset_ACLs_then_clear_read_only_then_delete_removes_the_whole_locked_fixture`
- `A_real_named_stream_is_damage_even_when_the_main_stream_matches`
- `A_real_handle_without_delete_sharing_defers_GC_until_the_next_pass`
- `Removing_the_deny_or_renaming_a_locked_tree_is_checked_on_the_next_use`
- `A_process_working_directory_defers_GC_without_running_a_real_CLI`
- `The_lock_is_one_exact_current_user_deny_with_both_inheritance_flags`
- `Native_hidden_and_system_entries_are_never_excluded_from_verification`
- `A_real_NTFS_junction_is_damage_and_GC_unlinks_it_without_touching_its_target`

