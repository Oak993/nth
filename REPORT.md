# Lane C: App.AppModel_HydraMerge — tests-first handoff

Base and current HEAD: `b3709698fe064ac9c615d88abc1d077db5c9fd8a` (`codex/hydramerge`). No commit was made. Pinned Swift: B3 `52bc7926097cd622d08d6294e0d8b08a48c7516e`, `DroppyCode/App/AppModel+HydraMerge.swift`, read in full (948 lines). `Git.swift:485-506` was read for the new sync test. Source ambiguity: none found. Guesses: zero.

## Record inventory and current test evidence

The 17 IDs below are `App.AppModel_HydraMerge.records_todo` in `porting/deps/units.json`. Existing tests precede this lane. They are listed for director review; a tagged test or a passing test is **not** a mutation proof. No record is claimed PROVEN in this handoff.

| Record | Tests on this base |
| --- | --- |
| PR-CHAT-189 | `AppModelHydraMergeCoordinatorTests`, `AppModelHydraMergeTests`, `WindowsToastTests` |
| PR-HYDRAMERGE-010 | `AppModelHydraMergeGuardTests`, `AppModelHydraMergeTests` |
| PR-HYDRAMERGE-017 | `AppModelHydraMergePart1Tests` |
| PR-HYDRAMERGE-030 | `AppModelHydraMergePart1Tests`, `AppModelHydraMergeTests` |
| PR-HYDRAMERGE-033 | `AppModelHydraMergePreparationTests`, `AppModelHydraMergeGuardTests`, `AppModelHydraMergeTests` |
| PR-HYDRAMERGE-034 | `AppModelHydraMergeWorkerTests`, `AppModelHydraMergePart1Tests` |
| PR-HYDRAMERGE-051 | `AppModelHydraMergeRequestTests` |
| PR-HYDRAMERGE-055 | `AppModelHydraMergeRequestTests`, `AppModelHydraMergeTests` |
| PR-HYDRAMERGE-063 | **New** `AppModelHydraMergeSyncTests.A_remote_deletion_fails_after_soft_reset_and_keeps_the_teams_working_file`; existing partial sync tests |
| PR-HYDRAMERGE-075 | `AppModelHydraMergeOwnershipTests`, `AppModelHydraMergeTests` |
| PR-HYDRAMERGE-077 | No Req-tagged proof; `AppModelHydraMergeReviewTests` has a STOP and partial Rule-tagged test |
| PR-HYDRAMERGE-109 | `AppModelHydraMergeRequestTests`, `AppModelHydraMergeTests` |
| PR-HYDRAMERGE-117 | `AppModelHydraMergeOwnershipTests` |
| PR-HYDRAMERGE-122 | `AppModelHydraMergeGuardTests`, `AppModelHydraMergeTests` |
| PR-HYDRAMERGE-124 | `AppModelHydraMergeGuardTests` |
| PR-HYDRAMERGE-125 | `AppModelHydraMergeRequestTests`, `AppModelHydraMergeTests` |
| PR-HYDRAMERGE-129 | `AppModelHydraMergePreparationTests`, `AppModelHydraMergeTests` |

## New test and source conflict

The new PR-HYDRAMERGE-063 test uses a real local bare origin and a clone. It advances the remote with one changed file and one deletion, then calls the production `SyncDefaultBranch`. It checks the call order (`reset --soft`, index reset, checkout, `git rm`), the branch moving to the remote, the retained file changing, the team's working file staying intact, and the deleted file remaining after failure.

**STOP App.AppModel_HydraMerge #1 — spec/source conflict, PR-HYDRAMERGE-063.** `porting/spec/HYDRAMERGE.json` says the sync removes every other path gone from the remote. Swift `AppModel+HydraMerge.swift:803-817` calls `git.removePaths(gone)` and catches a throw as `false` after earlier mutations. Swift `Git.swift:503-506` runs `git rm --quiet -- <path>` without force. In the local Git test this command refused the staged deletion after `reset --soft`, so sync returned `false`, HEAD and the retained file had already changed, and the removed remote file stayed in the working tree. The first version of the new test expected success and failed twice; the expectation was corrected from the cited Swift and Git helper, then passed. The protected spec needs a director ruling/correction before this record can be closed. No production code was changed.

**STOP App.AppModel_HydraMerge #2 — missing dependency, PR-HYDRAMERGE-063 and -077.** Swift `AppModel+HydraMerge.swift:657-663,795-808` uses `Git.presentPaths` for both the final merge path set and sync. Its positional truncation behavior is in Swift `Git.swift:442-459`. The existing test STOPs identify that ED-207 `ShellObserver` observes but cannot return a truncated *successful* cat-file output. The required seam lies in the Git/Shell layer outside lane ownership. The specific survivor is an unanswered tail treated as absent. This lane did not edit that layer or count either record as proven.

No ThreadRuntime dependency was changed. The `isReadyForHydraMerge` predicate remains outside lane ownership and requires its own proof for any record that depends on it.

## Validation and exact command results

Before every build/test command, PowerShell removed the API-key environment variables named in `codex_common.md` and set `MARROW_PORT_DATA_DIR` to a newly created `%TEMP%\marrow-<guid>` directory. No real profile or agent CLI was used. Commands were run from `E:\Droppy\wt\cx\hydramerge` in the ordinary test environment.

1. `dotnet build Marrow.sln -c Debug --nologo -v q` before the edit: `Build succeeded. 0 Warning(s). 0 Error(s).`
2. `dotnet test tests/Marrow.App.Tests --no-build --nologo -v q --filter 'FullyQualifiedName~AppModelHydraMerge'` before the edit: `Passed: 161, Failed: 0, Skipped: 0, Total: 161`, duration `1 m 27 s`.
3. After adding the test, `dotnet build Marrow.sln -c Debug --nologo -v q`: `Build succeeded. 0 Warning(s). 0 Error(s).`
4. `dotnet test tests/Marrow.App.Tests --no-build --nologo -v q --filter 'FullyQualifiedName~Sync_moves_to_the_remote_updates_other_paths_and_keeps_the_teams_working_file'`: `Failed: 1, Passed: 0`; success assertion failed.
5. Same filter with `-v normal`: `Failed: 1, Passed: 0`; `Assert.True()` at test line 41.
6. After adding call diagnostics, same build: `Build succeeded. 0 Warning(s). 0 Error(s).`; same test with `-v normal`: `Failed: 1, Passed: 0`; trace ended at `git rm --quiet -- gone.txt` after reset, checkout and index reset.
7. After correcting the source-derived expected outcome, same build: `Build succeeded. 0 Warning(s). 0 Error(s).`; renamed test with `-v normal` failed on fixture line-ending assumption (`Expected: "a\n", Actual: "a\r\n"`).
8. After removing that fixture assumption, same build: `Build succeeded. 0 Warning(s). 0 Error(s).`; `dotnet test tests/Marrow.App.Tests --no-build --nologo -v normal --filter 'FullyQualifiedName~A_remote_deletion_fails_after_soft_reset_and_keeps_the_teams_working_file'`: `Passed: 1, Failed: 0`, duration `2.3554 Seconds`.
9. `dotnet test tests/Marrow.App.Tests --no-build --nologo -v q --filter 'FullyQualifiedName~AppModelHydraMergeSyncTests'`: `Passed: 15, Failed: 0, Skipped: 0, Total: 15`, duration `28 s`.
10. `dotnet test tests/Marrow.App.Tests --no-build --nologo -v q --filter 'FullyQualifiedName~TraceabilityTests'`: `Passed: 3, Failed: 0, Skipped: 0, Total: 3`, duration `112 ms`.

No solution-wide test, protected-file check, verification gates or production mutation was run in this tests-first phase. No mutation table can truthfully claim red/green evidence yet. `records_candidates: none`.

## Lane tail

Files touched: `tests/Marrow.App.Tests/AppModelHydraMergeSyncTests.cs`; this `REPORT.md`. Production files touched: none. Protected files touched: none. Head commit: `b3709698fe064ac9c615d88abc1d077db5c9fd8a`. Final build: 0 warnings, 0 errors. Latest Hydra merge sync class: 15 passed, 0 failed, 0 skipped. Latest traceability: 3 passed, 0 failed, 0 skipped. Tests-first STOP for director review; lane not DONE.

## Continuation: PR-HYDRAMERGE-109 mutation proof

The director instructed this lane to continue through implementation and mutations without a tests-first pause. The prior sync test was committed at `55317000c` with the required trailer. PR-HYDRAMERGE-063 and -077 remain owner STOPs and are excluded from candidates.

New test: `AppModelHydraMergeUnknownHostTests.A_GitLab_remote_without_a_web_URL_uses_the_literal_unknown_host_in_both_places` reaches the production GitLab call site using a `gitlab:group/repo.git` remote; `Git.Forge` recognizes GitLab while `Git.RemoteWebURL` returns null. It checks the exact fallback text in the auth error. Existing `AppModelHydraMergeRequestTests.Both_GitLab_auth_throw_sites_render_the_exact_host_branch_and_raw_failure_paragraph` and `Auth_error_preserves_an_empty_detail_and_the_unknown_host_literal` check the Swift :943 text at both throw sites. No production edit was needed for this record.

| Clause (Swift `AppModel+HydraMerge.swift:438,454-455,471-472,942-943`) | Production mutation in `AppModel+HydraMerge.cs` | Failing Req-tagged test |
| --- | --- | --- |
| 401 Unauthorized and first paragraph exact | `(401 Unauthorized)` -> `(403 Forbidden)` | `Both_GitLab_auth_throw_sites_render_the_exact_host_branch_and_raw_failure_paragraph` |
| Host interpolated in first paragraph | `for {host}:` -> `for nowhere:` | `Auth_error_preserves_an_empty_detail_and_the_unknown_host_literal` |
| Host interpolated in command | `--hostname {host}` -> `--hostname nowhere` | `Both_GitLab_auth_throw_sites_render_the_exact_host_branch_and_raw_failure_paragraph` |
| Raw detail and paragraph separation | `{detail}\\n\\nSign back` -> `{detail}\\nSign back` | `Auth_error_preserves_an_empty_detail_and_the_unknown_host_literal` |
| Pushed source branch and reassurance sentence exact | `The branch `{branch}` is already pushed` -> `The source branch `{branch}` is already pushed` | `Both_GitLab_auth_throw_sites_render_the_exact_host_branch_and_raw_failure_paragraph` |
| Token name exact | `GITLAB_TOKEN` -> `GITLAB_TOKEN_WRONG` | `Auth_error_preserves_an_empty_detail_and_the_unknown_host_literal` |
| Unknown web host literal at call site | `"your GitLab host"` -> `"unknown host"` | `A_GitLab_remote_without_a_web_URL_uses_the_literal_unknown_host_in_both_places` |

Each of the seven mutants built with 0 warnings and 0 errors, failed its named test in two standalone runs with separate fresh data dirs, and passed the test after restoration. The production file's SHA-256 before and after each restoration was `DA8ADEE5CE2E40D9E2BCEA0BF18DB3A7EA346485414A29500AD70CDC19AC2142`. Full per-mutation output is in untracked `_evidence/mutation-109-*.txt`. The first run of the branch-text mutation did not compile because removing the only use of the `branch` parameter triggers CS9113; the revised, compiling mutation above is the one counted. An initial runner invocation hit the host's PowerShell script policy; subsequent calls used command-scoped `-ExecutionPolicy Bypass`. These are setup attempts, not mutation proof.

`dotnet build Marrow.sln -c Debug --nologo -v q`: Build succeeded; 0 warnings, 0 errors. `dotnet test tests/Marrow.App.Tests --no-build --nologo -v q --filter 'FullyQualifiedName~AppModelHydraMergeUnknownHostTests'`: 1 passed, 0 failed, 0 skipped. The mutation runner ran those same build/test commands with the named filters, fresh data dirs and API-key variables removed. No source ambiguity or guess for PR-HYDRAMERGE-109.

records_candidates: PR-HYDRAMERGE-109

## Continuation: PR-HYDRAMERGE-125 failure helper

Reviewed the interrupted-run `AppModelHydraMergeFailureTests.cs` against B3 `AppModel+HydraMerge.swift:226-237`, `ThreadRuntime.swift:2554-2559`, and the PR-HYDRAMERGE-125 expected text. Kept its four tests unchanged. They exercise the production coordinator with disposable local repositories: immediate one-time nonretryable note, last-record outcome discrimination, newest-20 persisted records, and the retryable two-record threshold. Existing `AppModelHydraMergeRequestTests` and `AppModelHydraMergeTests` cover the several-project prefix, supplied label/URL, repeated-detail retry ordering, and changing retryable details. No production edit was needed.

The interrupted run left 19 untracked `_evidence/mutation-125-*.txt` logs. I inspected every log: all 19 show a successful mutant build, two standalone red runs with fresh data directories, restored build, green run, and matching original/restored SHA-256 `DA8ADEE5CE2E40D9E2BCEA0BF18DB3A7EA346485414A29500AD70CDC19AC2142`. The current production file has that hash. The logs themselves remain untracked. Clause -> production mutation -> failing Req-tagged test:

| Clause | Mutation log | Failing test |
| --- | --- | --- |
| Detail uses project-prefixed title | `125-detail-prefix` | `Each_failure_contract_preserves_project_prefix_metadata_retry_and_note_threshold` |
| Retry scheduled before identical-detail return | `125-schedule` | `The_last_two_records_must_both_be_retryable_failures_and_an_identical_detail_still_schedules_a_new_retry` |
| Only the last record participates in deduplication | `125-last-only` | same last-two-records test |
| Last outcome must be failed | `125-last-outcome` | `An_identical_detail_after_a_nonfailure_record_is_a_new_failure` |
| Full detail uses Swift string equality | `125-dedup` | same last-two-records test |
| Nonretryable failure speaks immediately | `125-immediate` | `A_nonretryable_failure_records_and_announces_once_with_no_retry` |
| Two most recent records determine threshold | `125-last-two` | `AppModelHydraMergeFailureTests` |
| Threshold is two prior retryable failures | `125-threshold` | `AppModelHydraMergeFailureTests` |
| Append record through persistence/trimming path | `125-record-call` | `A_failure_trims_the_record_list_to_twenty_and_saves_the_newest_record` |
| Record time | `125-time` | `AppModelHydraMergeFailureTests` |
| Record failed outcome | `125-outcome` | `AppModelHydraMergeFailureTests` |
| Record project | `125-project` | `AppModelHydraMergeFailureTests` |
| Record label | `125-label` | `Each_failure_contract_preserves_project_prefix_metadata_retry_and_note_threshold` |
| Record URL | `125-url` | same project-prefix test |
| Record files = 0 | `125-files` | `AppModelHydraMergeFailureTests` |
| Record retryable true or null | `125-retryable` | `AppModelHydraMergeFailureTests` |
| Note only when speaking | `125-note` | `AppModelHydraMergeFailureTests` |
| Note title project prefix | `125-note-prefix` | same project-prefix test |
| Return failed | `125-return` | same project-prefix test |

Current verification, after removing the listed API-key environment variables and setting a new `MARROW_PORT_DATA_DIR` for each command: `dotnet build Marrow.sln -c Debug --nologo -v q` -> succeeded, 0 warnings, 0 errors; `dotnet test tests/Marrow.App.Tests --no-build --nologo -v q --filter 'FullyQualifiedName~AppModelHydraMergeFailureTests'` -> 4 passed, 0 failed, 0 skipped; `dotnet test tests/Marrow.App.Tests --no-build --nologo -v q --filter 'FullyQualifiedName~TraceabilityTests'` -> 3 passed, 0 failed, 0 skipped. No source ambiguity or guess for this record. Files touched: `tests/Marrow.App.Tests/AppModelHydraMergeFailureTests.cs`, `REPORT.md`. PR-HYDRAMERGE-063 and -077 remain owner STOPs and are excluded.

records_candidates: PR-HYDRAMERGE-109, PR-HYDRAMERGE-125

## Continuation: PR-HYDRAMERGE-030 in-progress checkout deferral

B3 `AppModel+HydraMerge.swift:244-249` requires a quiet deferred result and a one-minute retry for an existing merge or rebase. Existing Req-tagged `AppModelHydraMergePart1Tests.An_operation_defers_silently_and_attempts_exactly_one_retry_after_a_minute` drives both `MERGE_HEAD` and `rebase-merge` through disposable local repositories and checks the absence of a record, note, merged mark, commit and push, then the retry at exactly 60,000 ms. No new test or production change was needed.

| Clause | Production mutation | Failing Req-tagged test |
| --- | --- | --- |
| Detect the Git operation | `HasOperationInProgress()` guarded by false runtime expression | `An_operation_defers_silently_and_attempts_exactly_one_retry_after_a_minute` |
| Schedule the one-minute retry | `ScheduleHydraMergeRetry` guarded by `leadID == Guid.Empty` | same test |
| Return deferred, without failure record/note/commit/push | return `Failed` | same test |

Each of `mutation-030-guard.txt`, `mutation-030-schedule.txt`, and `mutation-030-return.txt` under untracked `_evidence` records a successful mutant build, two standalone red runs (both parameter cases), restored build, and green run (2 passed), with original/restored SHA-256 `DA8ADEE5CE2E40D9E2BCEA0BF18DB3A7EA346485414A29500AD70CDC19AC2142`. The runner removed the listed API-key variables and used a fresh `MARROW_PORT_DATA_DIR` on every test. An initial schedule mutation used a compile-time `false` guard and failed the mutant build with CS0162; it was restored and replaced with the compiling mutation above. Current production hash matches. No source ambiguity or guess.

Exact mutation command form: `powershell -NoProfile -ExecutionPolicy Bypass -File '_evidence\mutate.ps1' -Name '030-guard|030-schedule|030-return' -Old <logged old text> -New <logged new text> -Filter 'FullyQualifiedName~An_operation_defers_silently_and_attempts_exactly_one_retry_after_a_minute'`; each actual invocation used one Name and the exact Old/New values in its log. The runner ran `dotnet build Marrow.sln -c Debug --nologo -v q` (0 warnings, 0 errors) and `dotnet test tests\Marrow.App.Tests --no-build --nologo -v q` with that filter (red twice; green 2 passed). Files touched for this record: `REPORT.md` only.

records_candidates: PR-HYDRAMERGE-109, PR-HYDRAMERGE-125, PR-HYDRAMERGE-030
