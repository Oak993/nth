# T35 skills backend — NOT READY

Branch: `codex/skills-backend`, based on `origin/claude/verify-terminal-20261001` at `285b6b7a99b39e0e517f34c8d34a1860c945f83f`. Read audit from `origin/codex/release-audit` at `6b2e699407c9a6ef596887ca9dd335b56b2744f5`. Read CLAUDE.md, IMPLEMENTING.md, owner decisions OD-0099/amendments and OD-0118, skills specifications and BUILD-PLAN 1.5. No row was already done; each missing behaviour is cited in its report.

| Row | Result | Evidence / remaining reason |
| --- | --- | --- |
| RR-13 | BLOCKED | [Catalogue pin report](skills-backend-t35/RR-13.md): all 29 items remain incomplete. Existing assertions demand empty inventories/null trusted hashes; required upstream tree/artifact hosts are denied. No facts were invented. |
| RR-14 | BLOCKED | [Installer report](skills-backend-t35/RR-14.md): production tool/package plans and trusted restart descriptors remain absent; existing assertions require per-skill rejection and dropped tool state after reopen. |
| RR-15 | DONE (backend) | [Provider report](skills-backend-t35/RR-15.md): managed/imported generation composition, SK-11a..f launch/native tools, resume/reuse and child-exit leases connected. Live Windows acceptance remains. |
| RR-16 | DONE (backend) | [Provider-origin report](skills-backend-t35/RR-16.md): actual observations, current readable sync, distinct origins, equivalence and default library binding. |
| RR-45 | DONE | [Import report](skills-backend-t35/RR-45.md): bounded suffix-preserving names; Copy/Link maximum-length loop regression passes. |

Core/Services/runtime sources only. No shared UI file was edited; RR-17/18/44 remains with codex/skills-ui. No pre-existing test, tolerance, reference or assertion was changed. Engineering ledger entries ED-NEW-skills-backend-1 through -5 were appended in binary append mode; the original mixed-ending bytes remain an exact prefix. Owner ledger is unchanged. One commit is assigned to each requested row.

## Verification

Linux, official .NET SDK 10.0.401; C#/.NET 10 and Avalonia 12.1.2 unchanged. All added tests use local fixture trees, fake protection/locks/processes/credentials/offer flows; native sessions send no turn. No real download, install or provider request runs in the new tests. Read-only upstream provenance probes run outside tests, with URLs and received-byte hashes in the evidence folder.

| Check | Result |
| --- | --- |
| Core skills | 479 passed, 0 failed/skipped |
| Added Services regressions + Services traceability | 34 passed (31 additive cases + 3 existing meta-tests), 0 failed/skipped |
| Selected App runtime regressions + traceability | 63 passed (includes 12 additive runtime cases), 0 failed/skipped |
| Core traceability | 3 passed |
| Entire Services skills namespace | 891 passed, 16 failed, 48 skipped, 955 total |
| Untouched detached-base Services skills namespace | 860 passed, same 16 failures and 48 skips, 924 total; zero newly failing cases |
| Entire Core attempt | Aborted from unavailable Windows kernel32.dll; 2,128 passed/281 failed before the crash. Untouched base also aborts; differing asynchronous crash timing prevents an overall equivalence claim. |
| Entire Services attempt | Interrupted after about eight minutes of unsupported native Windows fixtures and repeated fake-HTTP/FileWatcher timeouts. No full-run success or completed total is claimed. |
| Builds / diff / immutable tests | App/Services/Core compiled with warnings as errors; git diff --check clean. Every pre-existing test file and catalogue file remains byte-identical to the base. |

Evidence: [verification summary](skills-backend-t35-evidence/verification-summary.json), [Core skills log](skills-backend-t35-evidence/core-skills-final.log), [final Services log](skills-backend-t35-evidence/services-trace-final.log), [App log](skills-backend-t35-evidence/app-final.log), baseline/final skills logs and explicitly labelled broad-run excerpts. Behavioural red witnesses and pinned upstream probes are in the same folder. Traceability's initial failures in the new fixtures were repaired by moving a helper to a separate file and adding rule metadata; all assertions stayed intact.

Commands used with MARROW_PORT_DATA_DIR set to a fresh test root:

```text
dotnet test tests/Marrow.Core.Tests --filter FullyQualifiedName~Port.Skills
dotnet test tests/Marrow.Services.Tests --filter FullyQualifiedName~Port.Skills
dotnet test tests/Marrow.App.Tests -p:EnableWindowsTargeting=true --filter "FullyQualifiedName~BackendProviderLaunchTests|FullyQualifiedName~CursorSkillsLaunchTests|FullyQualifiedName~AntigravitySkillsLaunchTests|FullyQualifiedName~ThreadRuntimeEnsureSessionTests|FullyQualifiedName~ThreadRuntimeSessionFactoryTests|FullyQualifiedName~ThreadRuntimeOpenRouterSessionTests|FullyQualifiedName~TraceabilityTests"
```

All three projects' TraceabilityTests were run; full Core and Services commands were attempted separately. Full Services skills failures were compared by exact failing case names against the untouched detached base, not dismissed from memory.

## Windows-run needs

1. Run full Core and Services on Windows 11 x64/.NET 10; repeat App runtime/traceability checks. The 16 Services skills failures involve Windows stand-ins/pipes/drive paths; 48 tests explicitly skip Linux. Native ProcessLauncher, kernel32/BCrypt, FileWatcher and fake TLS/HTTP fixtures need the intended platform.
2. Run real ACL/reparse/junction/held-handle tests for managed views, imported Copy/Link, maximum-length duplicate names, GC, uninstall/repair and restart. Verify original provider folders remain untouched.
3. Complete SK-11a..f WU-6..12 manual acceptance against pinned supported CLI versions: new/resumed Claude/Codex/Copilot/OpenCode/Go/Pi/Command Code/Grok, discovery/config precedence, namespaces/plugins, argv/env/shims, EOF versus exit and failed-start lifetime; native names/descriptions/use_skill/request_tool and invocation budget. Automated tests stay fake; live manual acceptance is separate.
4. After RR-13/14 contract and upstream-access blockers are resolved, verify all 29 real pinned catalogue entries and Windows installer/restart routes, including Python embed/pip, wheels, Node packages, MSI/SFX layouts, cancellation, damage/repair and updates. No such production installation is claimed here.

Release verdict: **NOT READY**. RR-13 and RR-14 remain mandatory release blockers, and Linux verification does not replace Windows acceptance.
