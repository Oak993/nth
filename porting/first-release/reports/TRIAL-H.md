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

Acceptance tests replay these JSON streams through a strict scripted process seam; there is no
real-process fallback in tests. Additional failure, cancellation and changed-Office cases are
explicitly synthetic and do not masquerade as live observations. Implementation, test-first
results, independent review and final gates are recorded here when complete.

Current verdict: **NOT READY** — Git remains blocked by active processes and Office completion/current
status is unconfirmed. These are captured live limitations, not test failures or a reason to forge success.
