# Trial H / UE-3 — READY for the merge train

## Lead-accepted follow-up, 2026-10-05

**READY**, branch `codex/trial-h`. The lead accepted trial H, including the recorded Office/Python inspection overlap, and owns `mark.mjs`. This section supersedes the initial NOT READY verdict below. The final head SHA and remote equality proof are in `E:\Droppy\work\scratch\handoff\codex-T13-trial-h.md`; this commit contains the final implementation, acceptance tests and report. Packet readiness is not a claim that the global port/native/package gates are complete.

Git's recorded installer log now gives the exact page/pill message **"Git is in use by another app. Close it and try again."** with **Retry**, including when the post-update inventory is unreadable or missing. Only a failed Git upgrade with the recorded process-use/cancel/abort markers is classified this way. Generic errors stay generic; reported-log reads are restricted to the witnessed diagnostic directory/name. Developer-tool bindings contain no process-termination commands or APIs: timed-out/cancelled probes remain observed until natural exit. No live retry was run.

Office's accepted asynchronous request shows **"Updating in the background"**, never Done/current solely because the updater returned zero. The shared service rechecks only pending versions every ten seconds, including both page and pill entrypoints; it defers during foreground runs and stops on completion/reconfiguration. Unchanged/null/unreadable versions retain pending, and only a later verified change becomes Updated. Accepted requests also survive verification cancellation, so Retry/Update everything cannot relaunch them. Later-change test versions are synthetic; the actual live Office version remains 16.0.20430.20092.

Absent GitHub CLI already had no row, proven by `Recorded_inventory_yields_only_Git_Node_two_Pythons_and_Office_rows`; this behavior remains covered. The 44 captured JSON fixtures and all live before/after facts are unchanged.

| Test-first / final evidence | Result | Logs under E:/Droppy/work/scratch/codex-ue-3 |
|---|---|---|
| Lead policy acceptance red | Services 17 failed, 2 passed /19; App 6 failed, 1 passed /7 | lead-red-services-tests.log; lead-red-app-tests.log |
| Independent-review edge-case red | 5 failed /5: two Git post-inventory cases, two UE-2 cancellation timings, direct Office cancellation | lead-edge-red-tests.log |
| Final Services (95 UE-3 +45 provider regressions +6 metadata) | **146/146, twice; 0 failed/skipped** | lead-final-Services-{2,3}-tests.log |
| Final App (page/pill/composition/lifecycle and metadata) | **117/117, twice; 0 failed/skipped** | lead-final-App-{2,3}-tests.log |
| Final Core (UE-2/model/listing and metadata) | **56/56, twice; 0 failed/skipped** | lead-final-Core-{2,3}-tests.log |
| All six final builds | **0 warnings, 0 errors** | lead-final-{Services,App,Core}-{2,3}-build.log |

Every build/test held the unmodified heavy.sh slot; tests ran through one*.sh with Native excluded, fresh data roots and no real process/network/Office fallback. The earlier fixture nullable compilation repair is not counted as acceptance red. Independent read-only static review identified the Git-post-inventory and accepted-Office cancellation regressions; both have red witnesses and fixes, and final static review has no remaining blocker. No mutation acceptance is claimed. Tested source/test hashes are frozen in `lead-final-source-hashes.txt` and checked after commit/push.

ED-NEW-ue-3 items 8-13 record the lead ruling, diagnostic scope, pending state, ten-second monitor, no-termination policy and cancellation preservation. The initial wider coverage notes below remain recorded: untrialled installed recipes, Store/portable variants, unmatched snapshots, inventory banner, installer provenance, and native/full/mutation acceptance. They are not remaining failed scoped tests or reasons to reclassify the lead-accepted packet. No live updater, Marrow launch, native test, packaging, process killing or mark.mjs call was made during this follow-up.

## Archived initial report — superseded by the lead ruling above

The original report follows byte-for-byte as historical evidence; its initial bounded-poll Office behavior and NOT READY interpretation were replaced by this follow-up.

# Trial H / UE-3 — supervised Windows run, 2026-10-05

Branch: `codex/trial-h`, created from `origin/claude/verify-terminal-20261001` at
`18bb508c7fe0f17a3682f8a52380d5db54846188`. Live-trial worktree: `E:\Droppy\wt\cx\v-cz7a`.
Implementation worktree: `E:\Droppy\wt\cx\trial-h-isolated`, from the same requested base.
An active SK-17 session started editing/committing in the original worktree during this task.
Its commits and uncommitted files were preserved there (`codex/trial-h-shared-preserved`).
Only this task's 60 untracked files were copied, hash-verified and removed from the shared
checkout; no SK-17 file was moved, committed, discarded or pushed by this task.
Authority: lead's `COMMON.md`, `CODEX-LOCAL.md`, `FEATURE-RULES.md`, `T13-trial-h.md`,
BUILD-PLAN section 4 H / UE-3, OD-0100. The task explicitly authorizes live upgrades,
overriding the standing prohibition on running installers for this supervised trial only.

## Live results (SOURCE FACT: captured commands)

| Tool | Detection and installation evidence | Before | After | One upgrade attempt / result |
|---|---|---|---|---|
| Git | `where git`, `git --version`; HKLM `Git_is1` (Inno); winget matches `Git.Git` | 2.54.0.windows.1 | 2.54.0.windows.1 | winget exit -1978335226 (0x8A150106), installer exit 1. Installer log explicitly cancels because other agents' Git Bash/tail/grep processes hold Git files. No retry or process termination. |
| GitHub CLI | `where gh` exit 1; `gh --version` cannot start (exit null); exact winget list exit -1978335212 (0x8A150014); filtered uninstall registry empty | absent | absent | No updater run; no row and no installation. |
| Node.js | `where node`, `node --version`; Windows Installer ARP version 24.14.1; winget matches **OpenJS.NodeJS.22**, despite actual v24 | 24.14.1 | 24.14.1 | exit -1978335189 (0x8A15002B): **no applicable update**. Exact stdout: "No available upgrade found.\r\nNo newer package versions are available from the configured sources.\r\n". Catalogue for this match is 22.23.2. No force, downgrade, match repair, or major-version migration. |
| Python 3.14 | `python --version`, `py -0p`, `py -3.14 --version`; registered CPython installer at `C:\Python314`; winget `Python.Python.3.14` | 3.14.4 | 3.14.7 | winget exit 0, "Successfully installed"; verified by `py -3.14 --version` and default `python --version`. |
| Python 3.13 | `py -0p`, `py -3.13 --version`; per-user CPython installer; winget `Python.Python.3.13` | 3.13.14 | 3.13.15 | winget exit 0, "Successfully installed"; verified by `py -3.13 --version`. |
| Office | HKLM ClickToRun Configuration; client file exists; x64 ProPlus2024Retail; Current Channel, audience `492350f6-3a01-4f97-b9c0-c7c6ddf67d60` | 16.0.20430.20092 | 16.0.20430.20092 | OfficeC2RClient exit 0, empty stdout/stderr. Immediate and delayed registry reads unchanged. **UNRESOLVED:** no supported completion/current-status output; do not claim updated or up to date. |

The live command fixtures are `tests/fixtures/dev-tools/trial-h/00-*.json` through `43-*.json`.
All updater invocations were sequential. **Trial scheduling deviation:** Office read-only
inspection (34-36) overlapped the Python updater waits. This does not meet the task's stricter
one-tool-at-a-time instruction; the real chronology is retained and this deviation is not
erased or represented as a compliant full run. No two upgrade processes overlapped.
Each contains the executable, argv, command line, actual exit code (null when spawn fails),
stdout, stderr, measured duration, before/after versions, UTC timestamp and completion timestamp.
Metadata serialization briefly converted timestamp offsets to local +03:00; the same instants
were canonicalized back to UTC. Streams, measured durations and actual command chronology stayed intact.
Streams are preserved except personal path/name redaction (`%USERPROFILE%`, `%USERNAME%`).
The argv array is the executable replay authority (display command lines use Windows quoting).

Registry/package-manager matching proves installer type and current matching, **not the original
historical launch mechanism** (winget vs a manually run copy of the same installer). That history is
UNRESOLVED. Python Install Manager is separately detected as a Store MSIX, version 26.3.240.0;
Python Launcher is a CPython component and ended at 3.14.7 with the CPython update. Miniconda,
venv runtimes, the SVP embedded Python, and the Codex bundled Git/Node runtimes are inventory
observations, not the standalone tools selected for this trial; their packages were not upgraded.

## Documentation and exact commands

Local `winget upgrade --help` is fixture 01. `winget show` for each matched ID is captured in
05, 13, 17, 25 and 31. Documentation read before execution:

- [Microsoft winget upgrade command](https://learn.microsoft.com/en-us/windows/package-manager/winget/upgrade): exact ID, source, silent install, agreement and interactivity flags.
- [Microsoft Q&A Office update examples](https://learn.microsoft.com/en-us/answers/questions/902651/office-not-updating-automatically) (fixture 35).
- [Microsoft Q&A on OfficeC2RClient switches](https://learn.microsoft.com/en-us/answers/questions/5104823/microsoft-365-apps-for-enterprise-officec2rclient): Microsoft explains that it is an internal executable and direct automation is unsupported; its options can change without notice. Examples are evidence of available switches, not a supported updater API contract.
- [Microsoft Q&A silent Office update example](https://learn.microsoft.com/en-us/answers/questions/5030303/is-there-any-way-to-update-office-365-silently): includes `updatepromptuser=False forceappshutdown=False displaylevel=False`.
- [Microsoft channel mapping](https://learn.microsoft.com/en-us/intune/configmgr/sum/deploy-use/manage-office-365-proplus-updates): the captured audience/CDN URL is Current Channel. The trial did not change it.

For Git, the actual Node match, and each CPython minor:

```text
winget upgrade --id <recorded-id> -e --source winget --silent --accept-source-agreements --accept-package-agreements --disable-interactivity
```

Office, once (fixture 37):

```text
"C:\Program Files\Common Files\Microsoft Shared\ClickToRun\OfficeC2RClient.exe" /update user updatepromptuser=false forceappshutdown=false displaylevel=false
```

The chat warning preceded Office by more than 60 seconds while the sequential Python updates
finished. It said open Office apps might close and asked the owner to save work. No force shutdown,
reboot, uninstall, source/settings changes, or `upgrade --all` was used.

## UE-3 implementation and verification

Implementation commit: `8cf4ca0032bea2a3d3c4a4942e17090e5bd46564`; test-first capture/stub commit:
`6ec433c06`. Final branch head and remote verification are in the external task handoff.

- `WingetUpdater`: installed-package parser, exact recorded upgrade argv, known target/version
  verification, signed no-installed/no-applicable exit handling, preserved error streams and cancellation.
  Missing Available stays unknown; malformed supported rows and duplicate matches fail.
- `OfficeC2RUpdater`: the one recorded request, immediate plus bounded delayed postchecks; zero exit
  and unchanged version remain failed/unconfirmed. Latest listing is unknown, not installed-as-latest.
- `DevToolUpdateCatalog`: only installed standalone tools; both CPython minors; manual/unmatched
  snapshots without install commands; explicit per-tool recovery keeps independent probes/Office.
  Strict default callers propagate inventory faults. Untrialled gh/other Node/Python upgrades refuse.
- `WindowsDevToolProcess` / `WindowsOfficeInstallation`: explicit production bindings; exact argv
  allowlist, ArgumentList without shell commands, canonical Office path and normalized Python paths,
  no selectors/WindowsApps Python probes, filtered read-only registry values. Read-only probes have
  thirty-second deadlines; installer waits are never killed/cancelled halfway through.
- `UpdateEverythingProduction` asynchronously composes developer items with provider items, the
  existing skills hook and Marrow in the shared UE-2 service. Completed fake sources still configure
  synchronously for launch tests. Configuration emits a row-change notification for an attached page.

Tests replay JSON streams through a strict scripted seam with no real-process fallback. Additional
failure, cancellation and changed-Office cases are explicitly synthetic. Process-boundary tests
validate argv without starting processes. App tests use fake items/deferred sources; existing
provider, UE-2, page/pill and launch composition regressions retain their expectations.

| Evidence | Result | Scratch log(s), under E:/Droppy/work/scratch/codex-ue-3 |
|---|---|---|
| Original Services test-first run | 53 failed, 1 passed / 54 | red-tests.log |
| Process-binding test-first run | 14 failed / 14; earlier 8-case run excluded six methods whose old names contained Native, corrected and rerun | boundary-red-tests-2.log; boundary-red-tests.log |
| App composition test-first run | 3 failed / 3 | app-red-tests.log |
| Review regressions before fixes | Services 4 failed / 4; attached-page App 1 failed / 1 | review-red-services-tests.log; review-red-app-tests.log |
| Intermediate review slice | Services 77 passed, 2 failed / 79; App 33 passed / 33 | review-green-services-tests.log; review-green-app-tests.log |
| Four remaining cases, isolated before fixes | Each failed alone twice: known target erased, Node v-prefix recovery, rejected registered Python, forward-slash alias | red-Losing_the_available_column-{1,2}.log; red-Explicit_inventory_recovery-{1,2}.log; red-A_rejected_registered_Python_probe-{1,2}.log; red-A_Store_alias_using_forward_slashes-{1,2}.log |
| Final Services (75 UE-3 + 45 provider regression + 6 traceability/layering cases) | **126 passed / 126, twice; zero failed/skipped** | final-services-tests-{1,2}.log |
| Final App (4 new composition cases + 106 update/page/pill/launch/metadata regression cases) | **110 passed / 110, twice; zero failed/skipped** | final-app-tests-{1,2}.log |
| Final Core UE-2/listing/model/metadata slice | **56 passed / 56; zero failed/skipped** | final-core-tests.log |
| Final Services, App and Core builds | **0 warnings, 0 errors**, all three | final-services-build.log; final-app-build.log; final-core-build.log |

All builds/tests held the lead's unmodified heavy.sh slot and tests used one*.sh with Native
excluded. The wrappers create fresh MARROW_PORT_DATA_DIR roots and clear API-key variables.
Wrapper exit codes alone are not acceptance: the recorded Passed!/Failed! counts above are the
raw results. There is no remaining scoped failure. No real CLI/network updater/Office request,
Marrow app launch, native test, installer retry, packaging or release step occurred in tests.
The initial two failed builds were overload/analyzer compilation repairs, not acceptance red.

An independent subagent reviewed tests and implementation read-only. Its concrete findings
(notification, inventory loss, invented latest, malformed-row omission, direct-probe recovery)
have red witnesses and fixes; final static review found no further concrete defect within the
implemented scope. It ran no tests, mutations or live process, so it does not independently attest
runtime green or full parity. No self-mutation or full independent mutation pass is claimed.

Verification entrypoint results: Gate A 12 PASS, Gate B 8 PASS. Gate C has 1 PASS, 1 FAIL (26
inherited stub markers) and 1 NOT_IMPLEMENTED; the baseline has exactly the same 26 markers and
none of their files changed (inherited-stubs.json). Gates E and F are NOT_IMPLEMENTED. Gate D
was not invoked: it directly runs bare solution dotnet test, forbidden by the lead's current
one*.sh-only rule, and would not substitute for a source-bound full acceptance gate. No global
port gate is called green. `node porting/tools/mark.mjs T13-trial-h DONE` exited 2, **no task
T13-trial-h in the queue**; the queue/spec/parity status was not edited and DONE was not accepted.

The protected-file checker against the requested base reports one authorized ledger change,
porting/engineering_decisions.md. T13 explicitly requires ED-NEW-ue-3; its prior 1,598,840 bytes
were verified unchanged. STATE's prior 60,188 bytes were also preserved. No guard/spec/build
configuration, pinned reference, or other worktree's content was changed by implementation.

## Remaining limits and verdict

**NOT READY.** Python 3.14 and 3.13 were updated and verified. Node recorded no applicable update;
gh was absent. Git's once-only installer attempt failed, and Office's zero exit does not prove
completion or current status. Read-only Office inspection overlapped Python waits, so this is not
a compliant one-tool-at-a-time trial closure. No failed upgrader was retried.

Unclosed implementation/product coverage: unsupported internal Office automation/completion;
untrialled installed gh/other Node/Python recipes; Store/portable Python variants without an
accepted registration/direct probe; immutable unmatched-version snapshots; logged inventory
errors without a dedicated page banner; running-app/native binding validation and full/mutation
acceptance. Original installer launch provenance is unresolved. These are explicit limitations,
not forged success, skipped tests, or reasons to modify the gate. See ED-NEW-ue-3 and STATE.
