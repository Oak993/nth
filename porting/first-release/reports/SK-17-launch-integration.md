# SK-17 shared launch integration follow-up

Current status: Antigravity automatic integration remains complete. The later owner-authorized Cursor MCP route also passed and is implemented; [SK-17-cursor-automatic.md](SK-17-cursor-automatic.md) supersedes the Cursor explicit-only statements and counts in this historical report.

Lead instruction, 2026-10-05: accept the live decision (Antigravity automatic skills; Cursor only when named), finish the shared launch integration, and leave task-queue/marking work to the lead. Worktree `E:\Droppy\wt\cx\sk-17-live`, branch `codex/sk-17-live`, baseline `18bb508c7fe0f17a3682f8a52380d5db54846188`. The original live evidence and configuration restoration are retained in [SK-17-live-test.md](SK-17-live-test.md).

## Final behavior

`ThreadRuntime.EnsureSession` is the shared production launch path for Antigravity (including runtime-created heads). Every new, resumed, restarted, or lost-conversation fallback launch prepares a generation from the one SkillsHost registered for the bound Storage data root and puts it in `SessionConfiguration.SkillGeneration`. The existing Antigravity endpoint verifies it and passes the folder through the second `--add-dir`. Cursor receives no automatic generation; its ACP argv and explicit-only decision are preserved.

The composition uses only the currently selected installed skill/plugin pins from the pipeline's trusted inventory. Full inventory verification and a held source lease precede metadata reads; names and descriptions are read from and hash-checked against pinned SKILL.md bytes. No disk manifest is a trust root. Default-on membership is the exact reviewed set in skills-PLAN 1.2 (Q1); other entries stay off unless enabled. Existing SK-9 provider filters, explicit-only confinement, source-qualified switches and budget admission still apply. Priority ties use SK-9's deterministic source-qualified ordering. Plugin hooks, commands and MCP are excluded from agy's skills-only view.

Every launch reads current enabled.json; absent version uses the host's embedded catalogue read date without rewriting the switches. The read-only clash scanner includes the provider's user skill roots and project roots up to the repository boundary, preserving the user's skill and description. A fresh host with no installed skills still supplies a valid immutable empty generation. An unavailable host refuses the launch with a normal ProviderError before any child starts, rather than reaching an unbound-seam trap.

The lead explicitly approved an **8,192-character admission cap** for SK-17 on 2026-10-05. This is an interim conservative cap, not a measured claim about the CLI's real limit. Existing SK-9 room computation keeps Marrow's descriptions to at most half that cap and reserves the user's entries plus its margin. WU-11 retains responsibility for the actual CLI budget measurement.

Generation copying and hashing during preparation run on the pool. A reused session verifies its generation before the next turn, on the pool, and rejects a changed generation. Stop while preparation or verification is suspended invalidates the session epoch and prevents a late child from starting. The runtime owns leases by session candidate; constructor failures, failed starts, failed resumes and provider exit release their own lease. A Stop request retains the lease until the actual child exits (`ISkillSessionLifetime`, StdioProcess.WaitForExit); a replacement waits for that release before preparing the same chat. The default CLI factory retains the original provider switch, moved behind an async-flow seam for fake-session tests. The original API factory is unchanged.

SOURCE FACT: the pinned `DroppyCode/App/Runtime/ThreadRuntime.swift:1549-1561` has the CLI factory mapping; `:2082-2096` releases the session and invalidates its epoch. SK-17 adds the authorized generation ownership around those boundaries. No owner app, real CLI, account, installer or packaging was run during this follow-up.

## Test-first evidence

The new initial acceptance run compiled with zero warnings/errors, then failed **2/2 Services and 9/9 App** before shared integration existed (test-first commit `a9df4a9ad`). The earlier test build had one CA1307 error, corrected by specifying Ordinal in the fixture assertion; that raw build log is retained. The first implementation run passed 1/2 Services and 8/9 App: the two fixture failures each reproduced twice alone. One fixture omitted addons/routing setup; the retry fixture used IOException instead of the actual localized ProviderError. Both setups were corrected with unchanged acceptance assertions. The next run passed 2/2 and 9/9.

Two additional lifetime tests were observed red (stop releasing early; a stop during preparation allowing a late launch), then passed after ownership followed actual child exit and the epoch was checked after preparation. Two final production-preparation tests were observed red (unbound host seam), then the real default preparation was connected to the bound data-root host and unavailable-host failure became typed. Three final error-boundary assertions were observed red (raw start IO, damaged reuse IO, malformed switches JSON), then passed after those failures were mapped to localized ProviderError without changing the message or weakening verification. All raw red, fixture-failure, four alone-twice, and green logs are in the adjacent evidence directory.

`AntigravitySkillsLaunchTests` has **14 cases**, exercising the real ThreadRuntime factory with in-memory sessions: new/resumed launch, same-session reuse, stop/relaunch, failed start, constructor failure, lost-conversation retry, damage between turns, delayed child exit, stop during preparation, Cursor new/resumed explicit-only paths, the production default's root-host-to-factory connection, unavailable-host refusal, and a malformed switch file returning a localized provider failure before child creation. `AntigravitySkillsComposerTests` has **2 cases**, using fake protection and real isolated hosts: installed pins/current switches/defaults/user clash/body preservation, and a verified empty generation without provider file writes. Existing `SkillOverlay` tests prove the production endpoint argv, resumed-session argv, damaged-tree refusal and layouts with offline stand-ins.

## Validation

All builds/runs hold the unmodified heavy.sh slot, one run at a time. Builds use `dotnet build tests/<project> -v q --nologo`; all test runs use the lead's one-names-long.sh or one-full.sh and exclude Native. No self-mutations or full-suite claim.

| Selection | Result |
| --- | --- |
| Core Skills/Providers/Traceability | 691 passed, 0 failed/skipped |
| Services Skills/Providers/Traceability | 2,912 passed, 0 failed/skipped |
| UI Skills/Providers/Traceability | 9 passed, 0 failed/skipped |
| App Skills/Providers/ThreadRuntime/Traceability | Passed!  - Failed:     0, Passed:   934, Skipped:     0, Total:   934, Duration: 1 m 11 s - Marrow.App.Tests.dll (net10.0) |

Final builds have zero warnings/errors. Exact filters: `(FullyQualifiedName~Skills|FullyQualifiedName~Providers|FullyQualifiedName~Provider|FullyQualifiedName~TraceabilityTests)&FullyQualifiedName!~Native`, with `FullyQualifiedName~ThreadRuntime` added for App. The first broad Core attempt produced no test result because its assembly had not yet been built; it is not counted. Core was explicitly built and passed 691/691. Earlier broad App runs passed 931/931 before the final two tests were added; the final App count above includes them and the final switch-error test. An intermediate App run passed 933/933 before that last test. No remaining failure is concealed by the initial runs.

## Branch isolation and handoff

T13's `codex/trial-h` head `ed6164c5a89368755ec0664e6077907b1c17572f` and the preserved accidental commit `777b2996e4e77b9120109dac4076955d2b53b5cb` are not ancestors of SK-17. The current T13 branch has 70 changed paths. Only the two shared ledger paths overlap; their SK-17 append sections contain no T13 content. No production, test or evidence path overlaps. The preserved accidental SK-17 commit remains on `codex/trial-h-shared-preserved`; neither that branch nor its worktree was modified during the follow-up. Only `codex/sk-17-live` is pushed. The branch-purity artifact records the compared refs and paths, and the final handoff contains the pushed head.

Task queue entries and mark.mjs belong to the lead and are not SK-17 worker blockers under the latest instruction. The earlier launch-integration gap is now closed. Current verdict: **READY**. WU-11's actual CLI-limit measurement remains with that unit; SK-17 uses the approved interim cap. Ledger: ED-379; historical ledger bytes are preserved.
