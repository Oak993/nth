# SK-6 / DL-6 delivery report

Status: implementation delivered; **INCOMPLETE / BLOCKED for Windows and NativeAOT verification**.
Branch: `codex/f-sk-6`. Base: `22902480a51c18e077475b8e2da93f9dc0cd9f34`. Implementation revision: `4c06b34e` (the subsequent evidence/ED commit changes documentation only). Pinned macOS source remains B3 `52bc7926097cd622d08d6294e0d8b08a48c7516e`, Droppy Code 1.7.0 build 21. Worker: Linux x64, .NET SDK 10.0.401 / runtime 10.0.12, 2026-10-04. source-sha256.txt binds the tested sources/fixtures; linux-sk6.trx contains run times and individual outcomes.

## Delivered behavior

BCL-only NativeAOT launcher project in Marrow.sln, real-name executable copies and npm/npx batch dispatch, source-generated sidecars, native inherited streams/working directory/exit codes, explicit target argv prefixes, Windows kill-on-close Job Object behind IShimLifetime, per-call Python ._pth/UTF8 and pip.ini, child-only NODE_PATH, distinct LibreOffice profile URIs and cleanup on target exit, and a Services child-environment PATH helper. Provider integration is owned by SK-11; install locks/seals and version/lease switching are owned by SK-5/SK-8. No changes to those units or to ledger statuses.

## Acceptance tests delivered (all Feature=SK-6)

Linux-runnable, passing:

- `ShimFilesTests.Native_executable_and_sidecar_are_written_under_the_provided_name`
- `ShimLauncherTests.Arguments_with_spaces_quotes_empty_strings_and_Unicode_are_unchanged`
- `ShimLauncherTests.Binary_stdin_stdout_stderr_and_nonzero_exit_are_unchanged`
- `ShimLauncherTests.Python_UTF8_and_package_paths_reach_only_the_child_and_locked_target_is_unchanged`
- `ShimLauncherTests.Node_PATH_is_set_only_for_the_child`
- `ShimLauncherTests.Concurrent_soffice_calls_get_distinct_profiles_and_delete_them_after_exit`
- `ShimLauncherTests.Soffice_profile_is_deleted_even_when_the_target_exits_with_failure`
- `ShimLauncherTests.Child_tree_lifetime_is_armed_before_start_through_the_seam`
- `ShimPathTests.Insert_before_first_WindowsApps_preserves_every_user_entry_and_empty_entry`
- `ShimPathTests.WindowsApps_match_is_case_insensitive_handles_quotes_and_trailing_slash_and_not_similar_names`
- `ShimPathTests.Empty_bin_exists_and_only_the_child_environment_changes`
- `ShimPathTests.Real_python_wins_but_Marrow_shadows_the_Store_alias`
- `ShimPathTests.Running_fake_provider_finds_a_shim_added_after_launch_on_its_next_process`

WINDOWS-ONLY, delivered and explicitly skipped on Linux with `WINDOWS-ONLY: requires Windows shells and Job Objects.`:

- `ShimFilesTests.Npm_and_npx_cmd_wrappers_dispatch_to_Node_with_prefix_arguments_and_preserve_exit`
- `ShimWindowsTests.Cmd_invocation_preserves_arguments_streams_and_exit_code`
- `ShimWindowsTests.PowerShell_invocation_preserves_arguments_streams_and_exit_code`
- `ShimWindowsTests.Git_Bash_invocation_preserves_arguments_streams_and_exit_code`
- `ShimWindowsTests.Killing_the_shim_kills_its_child_and_grandchild_by_Job_Object`

## Authentic validation

All worker test/build commands source `/workspace/.tools/marrow-env.sh`, then override MARROW_PORT_DATA_DIR with a newly created `/tmp/marrow-sk6-*` directory. Child processes get their own disposable fixture root. Test targets are only the locally built Marrow.Shim.FakeTarget program; there are no real python/node/soffice targets, network calls, credentials or real data roots. NuGet restore was constrained to `/workspace/.tools/nuget`.

- `dotnet restore tests/Marrow.Shim.OfflineTests -p:PublishAot=false --source /workspace/.tools/nuget`: passed from the local cache.
- `dotnet test tests/Marrow.Shim.OfflineTests --no-restore -p:PublishAot=false --filter Feature=SK-6 --logger 'trx;LogFileName=/tmp/sk6-final.trx' --logger 'console;verbosity=normal'`: **13 passed, 0 failed, 5 explicitly skipped**. linux-sk6.log and linux-sk6.trx. This compiles the identical Services sources and exact acceptance tests, using a JIT apphost instead of NativeAOT.
- Four subsequent runs of the complete offline acceptance suite with `--no-build --no-restore`: each **13 passed, 0 failed, 5 skipped** (five consecutive successful runs total). linux-sk6-repeat-2.log through -5.log.
- `dotnet test tests/Marrow.Core.Tests --no-restore --filter Feature=SK-1`: **131 passed, 0 failed, 0 skipped**. linux-sk1.log.
- Test-first evidence: tests were authored first; initial Services test build was blocked by missing restore assets/packages, rather than a semantic test result. Two additional real regressions were reproduced before their fixes: missing per-call pip.ini and copied ReadOnly attributes (python-pip-red.log, python-readonly-red.log). The new ShimFiles tests initially failed to compile without the writer (shim-writer-red.log). No existing test was removed, weakened or given a changed expectation. Fixes to new tests strengthened coverage and corrected a nullable compile error/shell-specific invocation construction.
- `dotnet build Marrow.sln -c Debug --no-restore -p:PublishAot=false`: failed, **0 warnings, 9 errors**, due to missing cached Whisper.net / Whisper.net.Runtime / Whisper.net.Runtime.Vulkan / NAudio.Wasapi and un-restored Marrow.Setup assets (solution-build.log). Main Services.Tests cannot currently build on this offline worker for the same dependency reason.
- NativeAOT restore attempted with local cache only: missing Microsoft.DotNet.ILCompiler, Microsoft.NET.ILLink.Tasks and runtime/AOT packs (offline-restore-blockers.log). No NativeAOT publish or Windows binary execution is claimed; nothing was downloaded to work around the owner's no-network condition.
- `node porting/tools/verify.mjs --gate A`: failed, 5 failing / 7 passing checks. The Windows reference checkout is absent on this cloud host, and the existing reading inventory is not fully closed (gate-a.log).
- `node porting/tools/verify.mjs --gate B`: **8 checks passed** (gate-b.log); this is a specification gate, not SK-6 runtime acceptance.
- `node porting/tools/check_protected.mjs 22902480a`: flags Marrow.sln, Services.Tests.csproj and engineering_decisions.md (protected.log). Those changes are specifically needed/authorized for this task: add the SK-6 launcher, build/copy offline fixture outputs, and write the requested ED. Guard configuration is unchanged; its failure is not reclassified as a pass.
- `git -c core.whitespace=cr-at-eol diff --check`: clean. Existing solution CRLF is preserved.

## Independent review and remaining work

An independent read-only reviewer examined the plan and implementation. It found missing pip.ini after Python relocation, missing npm/npx batch dispatch, weak lifetime ordering coverage and PowerShell pipeline/argv assumptions. These were corrected and re-reviewed. The review then requested fuller npm/npx coverage; quote/Unicode/byte-stream/NODE_PATH assertions were added. A separate read-only-copy regression was then reproduced and fixed. Static review is not Windows execution.

Not done, with reasons:

- Five Windows-only acceptance checks: this worker is Linux. Run the delivered tests on Windows with cmd, Windows PowerShell and Git for Windows installed. MARROW_SK6_GIT_BASH can select Git Bash explicitly; the test otherwise requires ProgramFiles/Git/bin/bash.exe, avoiding the WSL alias.
- NativeAOT publish / actual published-launcher execution: AOT compiler/runtime packs are absent locally and network downloads are prohibited. Current process acceptance uses a JIT launcher. Restore/publish and run the Windows tests on the provisioned Windows worker before acceptance.
- Full-solution and main Services.Tests validation: missing unrelated voice package cache and Setup restore assets. No package references/tests were weakened to hide this.
- Actual CPython/pip ._pth/site-prefix behavior and actual LibreOffice behavior: intentionally not exercised; real tools are the plan's separate DL-20 live checks. Fake targets prove generated paths/environment/lifecycle only.
- Forced-kill temp-directory collection: an externally terminated shim cannot execute finally. Its Job Object kills descendants, but python-run-* and lo-profile-* leftovers require store startup/GC integration (SK-5). Normal/nonzero target exit cleanup is implemented/tested. No watchdog was invented.
- Actual provider/native run_command wiring, locks/seals, installed version switching, leases and GC belong to SK-11/SK-5/SK-8 and were not edited.

ED written: the appended `## ED-309` in porting/engineering_decisions.md documents the internal sidecar, project/test setup, Windows lifetime, Python private runtime/config/ReadOnly handling, batch wrapper, PATH rules, test shell construction and remaining verification/cleanup limits. No ledger statuses changed. Push scope is only codex/f-sk-6; no merge, other-branch push or force-push.
