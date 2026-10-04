# SK-4 worker report

2026-10-04. **SK-4 implementation delivered; Windows-only acceptance remains unverified.**

- Branch: `codex/f-sk-4`, based on `origin/cloud/f-sk-3`, merged with `origin/claude/verify-terminal-20261001` at `3d3068124e44be0557e1efbda84be19b593a5dc6`. The engineering-decision conflict kept both sides.
- Commits before this evidence commit: `80b114ce` setup merge; `702984ca` test-first acceptance; `a47657a5` implementation and review fixes. The head is the commit containing this report (use `git rev-parse codex/f-sk-4`). The final chat report gives the resolved head and push result.
- Implemented in Services, one type per file: zip, python-embed, pyset, nodeset, msi-admin, sfx-7z and skillset; retained pinned licences, catalogue input checks, tmp build/provisional rename/lock/verify/atomic publication, crash rollback, and explicit spare projections.
- SK-3 downloader files are unchanged from `origin/cloud/f-sk-3`. SK-1 catalogue and SK-7 immutable state are used. No existing test was deleted or weakened; fixtures gained explicit catalogue pins for supplemental packages.
- ED written: append-only **ED-322**, items 1-9, in `porting/engineering_decisions.md` (authority, explicit inputs, publication, trust/manifest handoff, seams/ACLs, extractor outcomes, Python/Node, spare ownership/layout, evidence limits).
- No ledger statuses were changed by SK-4 implementation. The requested staging merge carries staging's pre-existing ledger changes. No mark/ledger-apply/req-verify operation was run.

## Linux verification

.NET SDK 10.0.401. Every dotnet invocation used a fresh `/tmp/marrow-sk4-*` data directory in `MARROW_PORT_DATA_DIR`. Inputs and output trees are separate Marrow-made fixture folders under `/tmp`; no real user folder, provider CLI, Python, npm, msiexec or network transport ran in tests.

| Command | Actual result |
| --- | --- |
| `dotnet test tests/Marrow.Services.Tests --no-restore --filter 'Feature=SK-4\|Feature=SK-3\|FullyQualifiedName~TraceabilityTests'` | **189 passed, 0 failed, 5 skipped**: SK-4 90 passed/5 Windows-only skipped, SK-3 96 passed, traceability 3 passed |
| `dotnet test tests/Marrow.Core.Tests --filter 'Feature=SK-1\|Feature=SK-7\|Feature=SK-3\|FullyQualifiedName~TraceabilityTests'` | **202 passed, 0 failed, 0 skipped** |
| `dotnet build Marrow.sln -c Debug --no-restore` | **0 warnings, 0 errors**; first attempt lacked Setup assets, fixed with an offline restore from the local package cache, then rerun successfully |
| `node porting/tools/verify.mjs --gate B` | **8 PASS**, exit 0 |
| `node porting/tools/verify.mjs --gate A` | **7 PASS, 5 FAIL**, exit 1: Windows reference `E:/Droppy/reference/droppy-code-v1.7.0` is unavailable on this worker |
| `node porting/tools/check_protected.mjs` | Exit 0 for clean committed working tree; its reference checks print missing Windows paths, so it does **not** prove reference integrity here |
| `git diff --check` | Pass |

[Evidence directory](evidence/sk4/) contains the test-first compile failure (missing installer APIs), final Services log and TRX, Core log, full build log, gate outputs and protected-check output. Filters are shown without logger arguments above; the saved logs record the actual outcomes. Full solution test execution, Windows acceptance and release-wide parity are not claimed.

## Acceptance test names delivered

All methods below carry `[Trait("Feature", "SK-4")]`. The portable methods expand to 90 passing cases, including the 7-kind x 6-step crash matrix and the 6-step spare crash matrix.

- `InstallerAcceptanceTests.cs`: `Zip_keeps_licence_and_publishes_only_after_rename_lock_verify`
- `InstallerAcceptanceTests.cs`: `Zip_refuses_unsafe_Windows_entries_on_every_host`
- `InstallerAcceptanceTests.cs`: `Python_embed_writes_exact_pth_and_unpacks_pip_wheel`
- `InstallerAcceptanceTests.cs`: `Pyset_requires_local_hashed_wheels_and_precompiles_before_lock`
- `InstallerAcceptanceTests.cs`: `Unbound_process_and_lock_fail_closed`
- `InstallerAcceptanceTests.cs`: `Changed_download_missing_licence_extra_file_and_lost_lock_are_refused`
- `InstallerBoundaryTests.cs`: `SK3_outcome_and_SK7_checking_pin_are_required_without_persisting_state`
- `InstallerBoundaryTests.cs`: `An_extractor_that_returns_zero_without_payload_does_not_install`
- `InstallerBoundaryTests.cs`: `Cancel_before_install_creates_no_final_item_or_process_and_collision_preserves_previous_install`
- `InstallerCrashTests.cs`: `Every_kind_crash_at_every_step_leaves_only_tmp_folders`
- `InstallerKindsTests.cs`: `Nodeset_builds_scoped_and_nested_tree_without_postinstall`
- `InstallerKindsTests.cs`: `Nodeset_refuses_tar_traversal`
- `InstallerKindsTests.cs`: `Msi_admin_argv_and_exit_classification_are_offline`
- `InstallerKindsTests.cs`: `Sfx_uses_documented_argv_and_requires_zero_exit`
- `InstallerKindsTests.cs`: `Skillset_keeps_source_pin8_skill_and_builds_independent_spares`
- `InstallerReviewTests.cs`: `Every_kind_retains_the_pinned_licence`
- `InstallerReviewTests.cs`: `Same_length_artifact_and_wheel_changes_fail_sha256_checks`
- `InstallerReviewTests.cs`: `Full_verify_refuses_extra_directory_missing_or_changed_file`
- `InstallerReviewTests.cs`: `Skillset_does_not_bless_a_file_planted_before_snapshot`
- `InstallerReviewTests.cs`: `Supplemental_packages_must_be_pinned_in_the_catalogue_and_pip_is_required`
- `InstallerReviewTests.cs`: `Crash_during_spare_build_and_publish_quarantines_store_and_every_view`
- `InstallerReviewTests.cs`: `Claude_projection_builds_plugin_layout_and_spares_are_independent_copies`
- `InstallerZipLayoutTests.cs`: `Explicit_multi_archive_layout_and_empty_directory_manifest_are_kept`
- `InstallerZipLayoutTests.cs`: `Zip_refuses_Unix_symlink_metadata_and_case_collisions_on_Linux_too`

## WINDOWS-ONLY test names

These five compile on Linux and each skips with the explicit reason: “WINDOWS-ONLY: requires Windows executable loading, NTFS reparse points/streams, or Windows ACLs; no Linux substitute.” They have not run on Windows.

- `WINDOWS_ONLY_zip_refuses_reparse_entry_and_junction_root`
- `WINDOWS_ONLY_python_embed_runs_tiny_test_built_exe`
- `WINDOWS_ONLY_fake_msiexec_exe_returns_success_and_failure_codes`
- `WINDOWS_ONLY_lock_deny_and_readonly_survive_publish_and_teardown_unlocks`
- `WINDOWS_ONLY_named_data_stream_is_refused`

## Independent review and remaining work

The independent read-only reviewer identified missing Windows tests, spare-crash view leaks not covered by the original matrix, same-length hash mutations, all-kind licence checks, authoritative skillset verification, supplemental package trust, required pip, Claude plugin projection, copy independence, exact ACL ownership and the directory-manifest handoff. These findings were addressed in code and regression tests. The reviewer did not execute Windows tests; the final boundary tests also pass in the Linux run.

Not done, with reason:

- Running the five Windows-only tests: this worker is Linux. No fake ACL or skipped executable test is reported as Windows success.
- Real Python/LibreOffice/MSI/SFX/npm/Playwright or vendor-content installation: explicitly forbidden in this worker job; these belong to DL-20/live checks. LibreOffice elevation and its normal-installer fallback, browser marker filenames/layout acceptance and real-package performance remain the plan's unresolved live checks. Multi-archive browser prefix layout is supported without inventing marker filenames.
- SK-5 GC/leases/per-launch seal/repair, SK-6 launcher/shims, SK-9 provider filtering/key selection, SK-8/SK-16 persistence and orchestration: separate feature units. SK-4 supplies concrete lock/process bindings and verified manifest/spare/result handoffs; missing bindings fail closed. Pip's checked local staging is tmp-prefixed and left for lifecycle cleanup.
- Abrupt termination between several independent spare/store final renames can leave completed verified spares; unfinished work stays tmp-prefixed. Injected faults at every phase quarantine all owned folders. No multi-directory atomicity is claimed (ED item 3).
- Reference Gate A: the configured pristine Windows reference does not exist here; no baseline or gate rule was altered to make it pass.

Only `codex/f-sk-4` is authorized for push, without force.
