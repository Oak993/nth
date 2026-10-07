# MA-4: Marrow's own handoff summary

READY for the requested foundation. Base: `claude/verify-terminal-20261001`, `7a4cfad3ef57459faf2f593368412fd44d54de06`. Branch: `codex/ma-4`. No UI, model call or account-selection policy was added. The decision is recorded under the byte-exact heading `## ED-407`; all earlier bytes of the decision ledger were preserved.

The Core builder preserves all user/assistant text and recorded task/plan/todo state, file edits and the supplied full current diff, command status and exit code, last error, approvals and recent turns. Only older bulky output is shortened; every shortened item points to an immutable full-conversation archive. The report counts the exact UTF-8 bytes of the archive and summary. ThreadRuntime.SaveHandoff uses the complete history, restores original assistant wording and tool output from a durable journal, and refuses a non-quiescent thread. The caller supplies the current diff; the builder does not invoke git or guess worktree contents.

AccountHandoff sends the summary as the first user message of a new destination session for cross-service transfer and unsupported same-service transfer. Explicit matching native capabilities permit manifest-only Claude/Codex transcript copying into a separate account root. Codex path resume is wired to thread/resume; ID, anchor and permissions are preserved. Unsafe paths, credentials/databases, missing artifacts and overwrites are refused. Resume failure stops the destination, rolls back its copies and leaves the source and archive intact. Success is marked pending continuity acknowledgement. No version is enabled by default, and the study's Windows identity/resume experiment remains required before advertising native cross-account continuity.

Tests were written first; initial Core and Services runs failed because the MA-4 APIs did not exist, and the App run likewise failed on those missing dependencies. Offline fixture chats use the existing Marrow ThreadDocument schema, with synthetic representative Claude/Codex conversations; they are not claimed as live CLI captures. Their independent checker walks every original JSON leaf, including file names, decisions, numbers, instructions and buried tool details, and requires it in the summary or restored archive. A negative control detects lost information. New tests use Feature MA-4 and Rule R-TEST-4, accepted by Traceability. The additional Codex parameter test's supervised-mode expectation was corrected against the pre-existing policy source and test: untrusted, read-only, user. No existing expectation or permission policy was changed.

## Linux verification

.NET SDK 10.0.401. Every test invocation sets a disposable MARROW_PORT_DATA_DIR and ends its filter with `&FullyQualifiedName!~Native`. No new test launches a real CLI, model or network. There are no new packages/project references and no changes to Traceability, build settings or test exclusions.

```
MARROW_PORT_DATA_DIR=/tmp/marrow-ma4-data /tmp/marrow-dotnet/dotnet build Marrow.sln -c Debug -p:EnableWindowsTargeting=true -warnaserror
Build succeeded. 0 Warning(s), 0 Error(s).
```

After that build, the slices ran with `dotnet test <project> --no-build --no-restore -p:EnableWindowsTargeting=true`:

| Project | Filter before the Native exclusion | Passed | Failed | Skipped |
| --- | --- | ---: | ---: | ---: |
| Core | HandoffSummary or Traceability | 8 | 0 | 0 |
| Services | AccountHandoff or CodexHandoffParameters or ProviderSessionTests or Traceability | 63 | 0 | 0 |
| App | ThreadRuntimeHandoff or ThreadRuntimeHistoryLoad or ThreadRuntimePart9 or Traceability | 25 | 0 | 0 |
| UI | Traceability | 3 | 0 | 0 |

99 passed in the final slices, including all four Traceability classes. `node porting/tools/verify.mjs --gate B`: 8/8 passed. `git diff --check`: passed. The protected-path checker passed before adding the authorized decision/report entries. Those are the only protected edits, made by the lead for this owner-authorized unit. The Windows reference checkouts are absent; the checker's reference-clean wording is not proof that those absent checkouts were inspected. Gate A and the full Windows/native gates are not claimed, and this new first-release feature does not update a T-* parity packet or requirement ledger.

Windows ICU imports used by existing runtime parsing need a Linux test bridge. `tests/fixtures/handoff/icu-linux-bridge.c` forwards the real unversioned calls to the system ICU 76 exports; it contains no canned Unicode behavior and is not shipped with the Windows application. Reproduce it with:

```
mkdir -p /tmp/marrow-ma4-icu
gcc -shared -fPIC tests/fixtures/handoff/icu-linux-bridge.c -o /tmp/marrow-ma4-icu/icu.dll -ldl
```

Set `LD_LIBRARY_PATH=/tmp/marrow-ma4-icu` for the Linux slices. Before the bridge, one new runtime test was blocked by missing icu.dll; with the bridge, it passes unchanged. This is Linux fixture verification, not verification of the Windows ICU implementation.

A wider optional App probe also included ThreadRuntimeFinishTurn: 37 passed and one unchanged test, `A_command_still_open_when_the_turn_ends_has_its_changes_counted_in_the_turn`, failed in the existing Git harness at ProcessPipe construction because Linux has no kernel32.dll. Its test, Git fixture and process implementation are unchanged from the base; it did not reach a provider or CLI launch. This probe is retained separately in `ma-4/expanded-app-linux-limit.log`, rather than hidden as a skip or relabeled as passing. The final MA-4/history/approval slices above all pass. Real Windows process behavior and native account-resume acknowledgement remain outside this Linux foundation result.

Actual build/test output is saved in `porting/reports/ma-4/`; only trailing whitespace in the expanded failure log was trimmed for git diff --check.
