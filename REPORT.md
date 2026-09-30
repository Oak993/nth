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
