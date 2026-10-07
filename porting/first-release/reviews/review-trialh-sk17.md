# Pre-merge review: origin/codex/trial-h (UE-3) and origin/codex/sk-17-live (SK-17)

Base: origin/claude/verify-terminal-20261001. Read-only review; diffs via `git diff origin/claude/verify-terminal-20261001...origin/<branch> -- src tests`, fixtures excluded. No builds/tests run.

## trial-h (UE-3: winget/Office dev-tool updates)

### 1. HIGH - A malformed/duplicate winget table kills the whole update inventory, not just dev tools
`WingetUpdater.ParseList` throws `InvalidDataException` for a truncated table, an unreadable known-package row, or duplicate matches (`src/Marrow.Services/Port/Updates/WingetUpdater.cs:34`, `:41`, `:48`). No handler for that type exists in the composition path:
- `DevToolUpdateCatalog` catch filters at `src/Marrow.Services/Port/Updates/DevToolUpdateCatalog.cs:46`, `:68`, `:89` cover only `InvalidOperationException or IOException or UnauthorizedAccessException`;
- `UpdateEverythingProduction.Real` at `src/Marrow.App/Port/Updates/UpdateEverythingProduction.cs:50` has the same filter;
- `UpdateEverythingProduction.Configure` at `src/Marrow.App/Port/Updates/UpdateEverythingProduction.cs:32` also filters.

So one bad winget row type makes `Discover` throw `InvalidDataException`, which propagates through `Real` (provider items, skills, Marrow already built are discarded), faults the `pending` task in `Configure`, and is rethrown out of `Configure`'s catch filter - `service.Configure` is never called, so even provider-CLI and self-update rows never appear. No test exercises `Discover`/`Real`/`Configure` with a malformed table; `Unparseable_success_output_is_not_an_empty_installed_inventory` only asserts `ParseList` itself throws. Widen the filters to include `InvalidDataException` (and ideally `ArgumentException` from `ValidateId`, WingetUpdater.cs:54).

### 2. MEDIUM - No production warning that Office may close open Office apps
TRIAL-H.md records that the trial was preceded by a chat warning: "open Office apps might close and asked the owner to save work." The production launch path (`src/Marrow.Services/Port/Updates/OfficeC2RUpdater.cs:31-37`, `UpgradeCommand` at `:21`) contains no such warning. The row then says only "Updating in the background". With `forceappshutdown=false` Office waits for open apps, so the user is never told why it is pending. Either surface a one-time warning/confirmation before `run(...)`, or note explicitly in BUILD-PLAN that the port accepts that Office defers.

### 3. LOW (Unsure) - `reachedExpected` marks real upgrades as failures
`src/Marrow.Services/Port/Updates/WingetUpdater.cs:106-111`: `reachedExpected` is `false` whenever `before.Available is null` (i.e. no public listing) unless the exit was `NoApplicableUpdate`. If winget then performs a real upgrade, the outcome is `false` with "The expected installed version was not verified.", a user-visible failure for a successful upgrade. The strictness may be intentional, but `before.Available == null` can occur on a catalogue/listing gap. Confirm intent; consider treating an exit-0 with a moved version as success.

### 4. LOW - Accepted Office request is lost on cancellation during launch
`src/Marrow.Services/Port/Updates/OfficeC2RUpdater.cs:42` (`var result = await run(...)`) lets `OperationCanceledException` escape, so a request cancelled while `OfficeC2RClient.exe` is still starting is reported as no update at all, even though the updater may later install. The "accepted requests survive cancellation" property (lines 51, 549-550) only covers cancellation observed after acceptance. Hard to hit in production; the tests (`Cancellation_after_Office_acceptance_retains...`) cover only the later window.

### 5. LOW - `CDNBaseUrl` is stored where a channel belongs
`src/Marrow.Services/Port/Updates/WindowsOfficeInstallation.cs:21-22` uses `UpdateChannel` or, as fallback, `CDNBaseUrl`, for the `Channel` field; a CDN URL is not a channel name and will read oddly wherever Channel is shown.

### 6. LOW - Tests that are weaker than their names
- `DevToolProcessBoundaryTests.Production_binding_accepts_the_recorded_argv_without_starting_a_process` asserts only `Assert.NotEmpty(WindowsDevToolProcess.Validate(...))` (`tests/Marrow.Services.Tests/Port/Updates/DevToolProcessBoundaryTests.cs`) - it cannot fail on a wrong allowlist, and says nothing about the 30 s read-only timeout vs never-killing-installer policy in `WindowsDevToolProcess.cs:646-652`.
- No test drives `Discover` with a malformed `ParseList` result (see finding 1).
- No test runs a real `winget`/Office/CLI/network call - fixtures are replayed through `TrialHReplay`, and `Default_catalog_cannot_reach_any_real_tool` correctly asserts the fail-closed seam. Good.

### Matches with spec/evidence (verified)
- Upgrade argv matches the trial byte-for-byte: `winget upgrade --id <id> -e --source winget --silent --accept-source-agreements --accept-package-agreements --disable-interactivity` (`WingetUpdater.cs:60`) and Office's once-only argv with `updatepromptuser=false forceappshutdown=false displaylevel=false` (`OfficeC2RUpdater.cs:21`).
- No uninstall recipes, no `upgrade --all`, no source/settings mutation recipes in the Validate allowlist (`WindowsDevToolProcess.cs:10-33`).
- Cancelling during RecheckBackground never relaunches an updater; `UpdatingInBackground` survives Retry/Update everything (`UpdateOrchestrator.cs:58-76`, `:254-280`).
- "Git is in use by another app. Close it and try again." is used only with the witnessed process-use/cancel markers and the installer log is path-validated (`WingetUpdater.cs:92-95`, `GitInstallerLog.cs:37-48`, `WindowsGitInstallerLog.cs:9-14`).

User-visible strings read cleanly except `WingetUpdater.Failure` producing "Updater exit unavailable: ..." for null exits (`WingetUpdater.cs:82`) - acceptable.

## sk-17-live (SK-17: Antigravity/Cursor get Marrow's skills)

### 7. MEDIUM - Composer `InvalidDataException` escapes the provider-error wrapping
`AntigravitySkillsComposer` throws raw `InvalidDataException` for a pinned skill missing its source path or a name/catalogue mismatch (`src/Marrow.Services/Port/Skills/Providers/AntigravitySkillsComposer.cs:30`, `:38`). The wrapping filters in `src/Marrow.App/Port/Skills/AntigravitySkills.cs:30-33` and `CursorSkills.cs:26-28` cover `IOException or UnauthorizedAccessException or JsonException or DecoderFallbackException or ArgumentException` - not `InvalidDataException`. IOException peers are wrapped, so a corrupted pin surfaces as an unlocalized raw error instead of `ProviderError.Case.Failed`. Add `InvalidDataException` to both filters.

### 8. LOW (Unsure) - Skills MCP stdio relay can abandon the last response
`SkillsMcpBridge.RunClient` (`src/Marrow.Services/Port/Skills/Providers/SkillsMcpBridge.cs:87-91`) returns 0 as soon as the *first* of stdin->pipe (`incoming`) or pipe->stdout (`outgoing`) completes. If the client closes stdin while a response is still in flight, `outgoing` is dropped when the pipe is disposed. Common usage (client waits for the response before EOF) makes this rare, but the symmetric fix is `await incoming; close pipe input; await outgoing`.

### 9. LOW - Two SK-17 launch tests assert source text, not behavior
`The_MCP_relay_entry_precedes_all_desktop_and_data_startup_without_running_the_app` and `Main_runs_the_Velopack_hook_before_any_desktop_work_after_the_private_MCP_dispatch` (`tests/Marrow.App.Tests/...`, `VelopackStartupHookTests.cs`) verify ordering by scanning `Program.cs` text. They catch neither a later code motion that re-orders statements at runtime nor a Velopack/single-instance regression; the names are accurate but the verification is brittle.

### Matches with spec/evidence (verified)
- Antigravity launch appends the verified generation folder as a second `--add-dir` on new and resumed sessions (`AntigravitySession.cs:90-92`), matching the live-proven `agy --add-dir` route in SK-17-live-test.md; no plugin install or global settings write.
- Cursor's ACP session merges the owned read-only MCP server into `mcpServers` without touching user servers (`ACPSession.cs:379-381`) and keeps automatic permission allow-once for that server only (`ACPSession.cs:791`, `SkillsMcpBridge.AutomaticOption`).
- Per-candidate ownership: failed resumes release the lease before retry, stop preserves the lease until the child exits (`ThreadRuntime.cs:3003-3016`, `ACPSession.cs:46`, `AntigravitySession.cs:70`), and the shared generator waits for the prior release (`ThreadRuntime.cs:2013`).
- Generations stay readable/verified until the child exited rather than merely `Stop()`-ed (`ISkillSessionLifetime`, `ReleaseSessionSkills`).
- No test opens a real CLI, account, or config file: launch tests use `Sk17LaunchRig`/fake sessions and the ACP stand-in; `Cursor_without_a_generation_does_not_probe_or_change_its_arguments` proves argv is unchanged without a generation.

No user-visible SK-17 strings reviewed badly; the localized failures ("The skills host is unavailable for this run.", "The skill generation failed verification before the next turn.") are acceptable.

## Cross-branch
- Both branches touch `porting/probes/p6/evidence/trials.csv` - note for the merge train.
- trial-h finding 1 is the only release-blocking candidate I found; sk-17's material findings are smaller. Neither branch's tests run real installers/CLIs/network.
