# Lane D — AppDelegate lifecycle

Worktree `E:\Droppy\wt\cx\appdelegate`, branch `codex/appdelegate`, based on `origin/impl/b2` `b3709698fe064ac9c615d88abc1d077db5c9fd8a`. Pinned source: `E:\Droppy\reference\droppy-code-v1.7.0\DroppyCode\App\AppDelegate.swift` at B3 `52bc7926097cd622d08d6294e0d8b08a48c7516e` (read in full). No reference, porting, protected, integration, or other lane file was edited.

## Completed item: later activation reaches the update observer

`PR-LAUNCH-046`, one clause only. Source `AppDelegate.swift:28` starts background checks; `UpdateChecker.swift:141-149` observes application activation. `AppShell.FinishLaunching` now wires the main window's later `Activated` event to the existing `UpdateChecker.Shared.HandleActivation()`. The hook is attached after `ApplicationDidFinishLaunching()`; whether the launch activation itself fires the Mac observer remains UNRESOLVED. The 8-second/6-hour schedule and staleness behavior are already in `UpdateChecker` and its tests.

Test first: `AppShellActivationTests.Reactivating_the_main_window_after_launch_checks_when_the_update_observer_is_stale` (Req `PR-LAUNCH-046`) hides and shows a real headless window and calls its `Activate()` method. It reads the production checker's `LastCheckedAt` before the scheduled 8-second tick. Before implementation it failed twice alone, each on a fresh throwaway data directory (1 failed, 0 passed). After implementation it passed (1 passed, 0 failed). Mutation: deleted only the production `main.Activated` hook; the same test failed twice alone (1 failed each). Restored the hook and verified SHA-256 of `AppShell.cs` was `B6FE31BA4F9CFE3BA440493377DE89DFA722177C061A928D0B686DC333EC9A6B`, identical to before mutation. Build and focused tests then passed.

## Fourteen open records: evidence and remaining clauses

| Record | Current test evidence | STOP preventing a whole-record candidate |
| --- | --- | --- |
| `PR-LAUNCH-028` | `AppDelegateThirdTests.Launch_takes_the_window_managers_model_then_shows_the_main_window_before_opening_the_launch_thread`; existing WindowManager tests | `AppDelegate.swift:22`, OD-0018/ED-103 boot stage and ED-180 material/P12 composition evidence absent. |
| `PR-LAUNCH-046` | New `AppShellActivationTests.Reactivating_the_main_window_after_launch_checks_when_the_update_observer_is_stale`; existing `UpdateCheckerSecondTests` exercise schedule/staleness | `AppDelegate.swift:28`, `UpdateChecker.swift:141-149`: whether launch itself counts as didBecomeActive is unresolved. |
| `PR-LAUNCH-050` | `AppDelegateTests` deferred quit/250 ms/240 s tests; `AppShellTests` ordinary close and session-end tests | ED-004 calls for `ShutdownBlockReasonCreate`, but `AppDelegate.swift:41-61` supplies no reason text; AppShell binding awaits a ruling. |
| `PR-LAUNCH-064` | `WindowsToastTests` and `WindowsToastStartupTests` callback/transport tests | `AppDelegate.swift:77-92`: no permitted native toast pointer hit-test fixture; ED-220(7) forbids real toast/registry/URI in tests. |
| `PR-LAUNCH-065` | `WindowsToastTests` and `WindowsToastStartupTests` update callback/transport tests | Same native toast ingress gap at `AppDelegate.swift:83-91`. |
| `PR-LAUNCH-071` | `AppDelegateThirdTests.First_launch_opens_the_chat_then_only_the_delayed_tour_with_no_onboarding_alert` | `AppDelegate.swift:22-33`: full clean-data boot capture waits on ED-103/P12 and ED-180 material composition. |
| `PR-UPDATE-024` | `WindowsToastTests` update routing tests | Native toast hit-test gap at `AppDelegate.swift:77-92`, plus About presentation proof. |
| `PR-WIN-072` | `AppDelegateThirdTests` reopen tests; `AppShellSecondTests` hidden/visible cached-window tests | `AppDelegate.swift:37`, `WindowManager.swift:57-60`: no ruling on `makeKeyAndOrderFront` restoring a minimized window; WindowManager is lane A owned. |
| `PR-WIN-073` | `AppDelegateTests` and `AppShellTests` ordinary quit tests | Same ED-004 session-end reason/binding gap as `PR-LAUNCH-050`. |
| `PR-WIN-076` | `WindowsToastTests` thread/update callback tests | Native toast hit-test gap at `AppDelegate.swift:77-92`. |
| `UI-LAUNCH-001` | Existing launch and WindowManager size/frame tests | Boot stage and material evidence at `AppDelegate.swift:22`, ED-103/P12 and ED-180. |
| `UI-LAUNCH-002` | `AppDelegateThirdTests` 700 ms and hasSeenTour gate tests | `AppDelegate.swift:24-26`, `TourPages.swift:27`, `TourWindowController.swift:103-107,145-148`: native window close versus tour close control has conflicting record wording; whole pointer proof for reopen paths absent. |
| `UI-LAUNCH-003` | `AppDelegateThirdTests` relaunch marker and About page tests | Update notification tap requires native toast hit-testing; full Settings geometry belongs to WindowManager. |
| `UI-LAUNCH-005` | `AppDelegateTests` notification presentation tests; `WindowsToastTests` delivery/routing tests | System-drawn toast pointer hit-test gap at `AppDelegate.swift:63-92`, ED-220(7). |

The above existing tests are partial evidence, not newly mutated whole-record proofs. The source/rulings do not settle the noted STOPs. Guesses made: **zero**. No whole record is claimed proven.

+### PR-LAUNCH-028 — STOP

Source site: `AppDelegate.swift:22` calls `windows.showMain()`; `WindowManager.swift:26-30,57-70` creates/restores the 1320 × 860 main window, activates it and brings it front. Existing `AppDelegateThirdTests` proves that plain launch order. Owner deviation OD-0018 / ED-103 replaces that presentation with a transparent particle stage and delayed window formation. `AppDelegate.cs:56-60` still calls `windows.ShowMain()` directly. `LoadingMark.cs` and `GlassMaterial.cs` exist, but there is no P12 transparent-stage/Acrylic composition probe or stage hosting in this worktree (`rg --files porting/probes` has no P12). ED-103(7) explicitly defers the fallback until that probe. A stage timing assertion against today's direct show would encode the wrong result. STOP at `AppDelegate.ApplicationDidFinishLaunching` before replacing `ShowMain`; P12 and the stage/window composition are required. No Swift failure is asserted: the stage is the owner-approved Windows deviation.

+### PR-LAUNCH-050 — STOP

Source site: `AppDelegate.swift:44-54` returns `.terminateNow` without a merge, otherwise `.terminateLater`, polls every 250 ms to a 4 × 60 second deadline, and unconditionally replies true; `:56-61` orders save, live-spend flush and MCP termination. `AppDelegateTests` and `AppShellTests` cover the ordinary path, including the cap. Windows ED-004(2) additionally requires `ShutdownBlockReasonCreate` while `WM_QUERYENDSESSION` is held; `AppShell.ShutdownRequested` cancels the request but has no reason call. The Swift file contains only a code comment about merging (`:41-43`), no user-facing reason string. Supplying one would invent text. STOP at `AppShell.ShutdownRequested`, the session-end branch; owner-approved reason text and then a test/mutation of that native binding are required. Ordinary quit remains implemented; no source defect is claimed.

+### PR-LAUNCH-064 — STOP

Source site: `AppDelegate.swift:77-92` reads `threadID` as a String, shows main before setting a parseable UUID on `model`, activates, and skips About when `update` is absent. The C# delegate and `WindowsToastStartupTests`/`WindowsToastTests` cover callback ordering and payload transport. The record's action is a user tapping a system toast. ED-220(7) forbids a real toast, registry or URI in tests; the test tree has only recording callbacks, no native toast pointer fixture. A callback invocation cannot prove the tap reaches `UserNotificationCenter(didReceive:)` under the interaction mutation standard. STOP at `WindowsToastPlatform.Show` to toast activation ingress; a safe hit-tested native fixture or an explicit proof-method ruling is required. No source defect is claimed.

+### PR-LAUNCH-065 — STOP

Source site: `AppDelegate.swift:83-91` branches on the mere presence of `update`, skips `showMain`, activates, then opens Settings on About. Existing `WindowsToastTests` and `WindowsToastStartupTests` call the production routing path with update payloads. They do not click the system-drawn toast; ED-220(7) forbids the native notification/registry/URI route in tests. STOP at the same `WindowsToastPlatform.Show` ingress as PR-LAUNCH-064. The remaining clause is a real tap preserving the skip-main and activate-before-About sequence, which needs a safe hit-tested fixture or owner proof-method ruling. No source defect is claimed.

+### PR-LAUNCH-071 — STOP

Source site: `AppDelegate.swift:22-33` shows main, opens the launch thread, schedules the tour at 700 ms when `hasSeenTour` is false, starts bootstrap and update checks, and conditionally opens About. `AppDelegateThirdTests.First_launch_opens_the_chat_then_only_the_delayed_tour_with_no_onboarding_alert` verifies the present plain-window path and no extra onboarding alert. A full clean-data capture cannot match OD-0018/ED-103 while `AppDelegate.cs:60` shows main immediately and there is no P12 stage composition. There is also a timing conflict: ED-103 shows the main window after outline landing around 5 seconds on the normal motion path, while the unchanged Swift 700 ms task would present the first-run tour before that window, contrary to UI-LAUNCH-002's 'over the main window' wording. STOP at `AppDelegate.ApplicationDidFinishLaunching` (`ShowMain` and the tour task); the boot handoff and an explicit ruling on the tour's relation to it are required. No account/sign-in step was added and no Swift defect is claimed.

+### PR-UPDATE-024 — STOP

Source site: `AppDelegate.swift:77-92` sends an `update` response directly through activation to `showSettings(page: .about)` without `showMain`; `WindowManager.swift:32-54` sets the page and centers Settings. Existing `WindowsToastTests` prove the callback branch, and WindowManager owns the centering geometry. The remaining record action is tapping a system banner. ED-220(7) forbids a real toast/registry/URI in tests, and no safe native hit-test ingress is present. STOP at `WindowsToastPlatform.Show` through activation into `AppDelegate.UserNotificationCenter(didReceive:)`; callback-only mutation evidence cannot prove the user tap. No source defect is claimed.

## Commands and observed output

For every `dotnet` build/test command below, PowerShell first removed the listed API-key environment variables and set `MARROW_PORT_DATA_DIR` to a newly created `%TEMP%\marrow-<GUID>` directory. No installed app was launched.

- Initial tests-first attempt used `AppModelHarness.Bound` (120 seconds) as the test wait. `dotnet test tests\Marrow.App.Tests --nologo -v q --filter 'FullyQualifiedName~AppShellActivationTests'` printed only the test-run header and was interrupted with Ctrl+C (exit 1) after it stalled; the wait was tightened to 3 seconds before the two red runs below. This changed only the timeout, not the expected result.
- `dotnet test tests\Marrow.App.Tests --nologo -v q --filter 'FullyQualifiedName~AppShellActivationTests'` before code: `Failed: 1, Passed: 0, Skipped: 0, Total: 1` (twice, second with `--no-build`).
- Same test after code: `Passed: 1, Failed: 0, Skipped: 0, Total: 1`.
- Same test under the hook-removal mutation: `Failed: 1, Passed: 0, Skipped: 0, Total: 1` (twice, second with `--no-build`).
- `dotnet build Marrow.sln -c Debug --nologo -v q`: `Build succeeded. 0 Warning(s), 0 Error(s)`.
- `dotnet test tests\Marrow.App.Tests --no-build --nologo -v q --filter 'FullyQualifiedName~AppShellActivationTests|FullyQualifiedName~AppShellSecondTests|FullyQualifiedName~AppShellTests|FullyQualifiedName~TraceabilityTests'`: `Passed: 16, Failed: 0, Skipped: 0`.
- `dotnet test tests\Marrow.App.Tests --no-build --nologo -v q --filter 'FullyQualifiedName~AppDelegateTests|FullyQualifiedName~AppDelegateSecondTests|FullyQualifiedName~AppDelegateThirdTests|FullyQualifiedName~AppDelegateProofTests'`: `Passed: 71, Failed: 0, Skipped: 0`.
- `node porting/tools/check_protected.mjs`: `protected paths unchanged against HEAD; reference checkout clean`.
- `node porting/tools/verify.mjs --gate A`: `PASS=12`, `RESULT: all implemented checks pass`.
- `node porting/tools/verify.mjs --gate B`: `PASS=8`, `RESULT: all implemented checks pass`.
- `node porting/tools/verify.mjs --gate D`, first run: `2904 test(s) failed, 9235 passed`; requirement verification missing and interaction gate TODO. This first invocation did not set `MARROW_PORT_DATA_DIR` and is not accepted as the isolated gate result.
- `node porting/tools/verify.mjs --gate D` with API keys removed and a fresh `MARROW_PORT_DATA_DIR`, output saved to `_evidence/gateD.log`: exit 1, `4 test(s) failed, 12135 passed`; requirement verification missing and interaction gate TODO. The four failed tests are `DownloadsPopoverSecondTests.Show_in_File_Explorer_opens_the_downloads_folder`, `ProviderRegistryCatalogTests.loadcatalog_is_a_no_op_while_a_load_for_that_provider_is_already_running`, `TerminalStoreTests.A_plain_tab_launches_the_shell_with_no_extra_arguments`, and `TerminalPanelTests.Tab_tooltips_and_accessibility_names_keep_the_distinct_exit_punctuation_and_button_selection_traits`. None is in the edited lifecycle code. These failures have not been reproduced twice alone, so no claim is made that they are stable failures.
- `git diff --check`: exit 0, no output. `git diff --stat`: `AppShell.cs | 8 +++++++-` before the new test/report were added to the index.

records_candidates: none

Files touched: `src/Marrow.App/Port/AppShell.cs`, `tests/Marrow.App.Tests/Port/AppShellActivationTests.cs`, `REPORT.md`. Evidence log `_evidence/gateD.log` remains untracked and must not be committed.
