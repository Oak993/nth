# P6 RESULT: PASS (2026-09-22)

Task packet T-M0-005. Throwaway probe, never production code. Sources: `p6.csproj`, `Native.cs`,
`Fixtures.cs`, `Program.cs`. Evidence: `evidence/run_output.txt` (full log of the verdict run) and
`evidence/trials.csv` (one row per trial, 73 rows).

## What was asked

Probe text (verbatim in `porting/critiques/stack_decision.json:65`): graceful-then-forceful
termination with the one-second gap, per transport. Three mechanisms against scripted fixtures:
0x03 into ConPTY input for a pty child; stdin close then CTRL_BREAK through an AttachConsole /
GenerateConsoleCtrlEvent / FreeConsole helper for a CREATE_NEW_PROCESS_GROUP console child; and
TerminateJobObject at the deadline. PASS needs all of these:

1. a cooperating fixture exits before t+1000 ms,
2. a non-cooperating fixture is terminated at t+1000 ms, timestamps within +/- 50 ms,
3. a write to a closed pipe surfaces as IOException with ERROR_BROKEN_PIPE or ERROR_NO_DATA and is
   handled as recoverable.

## Verdict: PASS, with every control behaving as the missing capability predicts

| Case (10 trials each) | Transport, signal | Result | Exit time from t0 (ms) |
| --- | --- | --- | --- |
| M1 cooperating | ConPTY, 0x03 | 10/10 exit code 130, no kill issued | 2.7 to 4.0 |
| M2 non-cooperating + grandchild | ConPTY, 0x03 ignored | 10/10 killed by job, code 137, grandchild dead | 1009.9 to 1011.7 |
| M3 cooperating | pipe, stdin close + CTRL_BREAK helper | 10/10 exit code 131, no kill issued | 40.6 to 55.9 |
| M4 non-cooperating + grandchild | pipe, CTRL_BREAK ignored | 10/10 killed by job, code 137, grandchild dead | 1010.5 to 1012.7 |

t0 is the `Stopwatch` timestamp at the start of `terminate()`. The deadline timer fired between
1004.4 and 1009.4 ms in every 1000 ms case. From kill issued to process-handle signalled took at most
3.7 ms. In the 26 forced exits across M2, M4, N2 and N4, the exit came 9.9 to 12.9 ms after 1000,
well inside +/- 50. In the 26 cooperating exits across M1, M3, N1 and N3, the latest was 55.9 ms,
well before 1000. The `IGNORED` line in every non-cooperating trial shows that the graceful signal
did arrive and was swallowed, so the job kill was needed and was not a coincidence.

Exit codes are probe-only markers that say who ended the process. 130 is the fixture's Ctrl+C
handler, 131 its Ctrl+Break handler, and 137 `TerminateJobObject`. A cooperating trial passes only
if it exited with its own code, was not running when the deadline fired, and had no kill issued. So
the pair was never merged into one kill.

### Controls (3 trials each). Each must fail when the capability under test is missing.

| Control | What is removed | Expected if the capability is real | Measured |
| --- | --- | --- | --- |
| C1 | ConPTY, no 0x03 written | cooperating fixture still alive at deadline | 3/3 alive, killed by job at 1009.5 to 1011.7, code 137 |
| C2 | pipe, stdin closed, no CTRL_BREAK | still alive at deadline | 3/3 alive, killed at 1011.3 to 1012.2, code 137 |
| C3 | pipe, GenerateConsoleCtrlEvent from a process NOT attached to the child's console (helper absent) | still alive at deadline | 3/3 alive, killed at 1011.2 to 1012.0; the call itself failed, helper exit `0x20057` = ERROR_INVALID_PARAMETER (87) |
| C4 | ConPTY, non-cooperating, forceful step disabled | still alive at t0+2000 | 3/3 alive at 2000, `IGNORED` seen |
| C5 | pipe, non-cooperating, forceful step disabled | still alive at t0+2000 | 3/3 alive at 2000, `IGNORED` seen |

C1 shows that the 0x03 byte is what ends M1. C2 shows that closing stdin alone does not end M3
(the fixture does not read stdin, on purpose). C3 shows that AttachConsole is needed: a bare
GenerateConsoleCtrlEvent from outside the child's console fails. C4 and C5 show that the
non-cooperating fixtures really survive the graceful step, so the job is what kills them in M2 and M4.

### Writing to a closed pipe (run first, so every later trial ran after the exceptions)

```
pipe  CONTROL write while the child is alive : write OK (no exception)
pipe  child exited: True code=137
pipe  write after exit, FileStream.Write         : System.IO.IOException HResult=0x800700E8 win32=232 (ERROR_NO_DATA) "The pipe is being closed."
pipe  write after exit, StreamWriter(AutoFlush)  : System.IO.IOException HResult=0x800700E8 win32=232 (ERROR_NO_DATA) "The pipe is being closed."
pipe  write after exit, from a background task   : System.IO.IOException HResult=0x800700E8 win32=232 (ERROR_NO_DATA) "The pipe is being closed."
pty   write after child exit, console still open : write OK (no exception)   (informational)
pty   write after ClosePseudoConsole             : System.IO.IOException HResult=0x800700E8 win32=232 (ERROR_NO_DATA) "The pipe is being closed."  first failure at +59.5 ms
BROKEN PIPE CHECK: PASS
```

Every write-side failure was `IOException` with ERROR_NO_DATA (232). ERROR_BROKEN_PIPE (109) never
appeared on a write. The runner caught each one, the way `try? handle.write` does, and went on to run
all 73 trials.

### Not part of the verdict, but measured

- **S1/S2, 3000 ms gap** (the actual `StdioProcess.swift:120` value, see deviations): cooperating
  runs exited at 41.5 to 46.9 ms. Non-cooperating runs were killed at 3010.7 to 3013.2 ms, with the
  deadline firing at 3006.0 to 3009.8. 6/6 as expected.
- **N1 to N4, node 24.14.1 fixtures** (`node -e` one-liners with `SIGINT` / `SIGBREAK` handlers, not
  an AI CLI): pty cooperating runs exited at 5.0 to 9.6 ms with code 130. Pipe cooperating runs exited
  at 47.4 to 50.2 ms with code 131. Stubborn runs were killed at 1011.1 to 1012.9 ms with code 137.
  12/12 as expected. Node's runtime maps ConPTY 0x03 to SIGINT and the helper's CTRL_BREAK to SIGBREAK.
- `IsProcessInJob` was true for every child in all 73 trials, placed through
  `PROC_THREAD_ATTRIBUTE_JOB_LIST`. On the pty side it sat in the same attribute list as
  `PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE`, and on the pipe side next to `PROC_THREAD_ATTRIBUTE_HANDLE_LIST`.
  This incidentally exercises P5's one-attribute-list claim, but it is **not** a P5 result.
- The helper's misdelivery guard bit (`0x40000000`) was never set. The CTRL_BREAK reached only the
  target group, never the helper.

## Source facts reproduced, and deviations from the probe text

SOURCE FACT, pinned `dd2b86ef`:

- `Shell.swift:110-118`: `guard process.isRunning`, then `process.terminate()` (SIGTERM), then
  `asyncAfter(.now() + 1) { guard process.isRunning; kill(getpgid(pid) == pid ? -pid : pid, SIGKILL) }`.
- `StdioProcess.swift:114-123`: close stdin on the write queue, `guard process.isRunning`,
  `process.terminate()`, then `asyncAfter(.now() + 3) { if process.isRunning { kill(pid, SIGKILL) } }`.
- The live upstream `1fbf81e6` (v1.6.2) leaves both `terminate` functions unchanged
  (`git diff dd2b86ef 1fbf81e6 -- DroppyCode/Core/Support/{Shell,StdioProcess}.swift` touches only
  locking and `.trimmed`).

Deviations, each with its reason:

1. **The stdio gap is 3 s, not 1 s.** The probe text cites `StdioProcess.swift:114-120` under "the
   one-second gap", but line 120 reads `.now() + 3`. The source has three termination shapes, all
   read for this probe:
   - a 1 s gap in `Shell.swift:110-118`, `MCPHub.swift:285-294` and `MCPProbe.swift:226-235`;
   - a 3 s gap in `StdioProcess.swift:114-123`;
   - **no gap** in `MCPHub.terminateAll()` at `MCPHub.swift:544-553`, which sends `terminate()` to
     every process and then SIGKILLs those still running straight away.

   The PASS criterion was run as written, at 1000 ms, for both transports. The stdio shape was also
   run at the source's 3000 ms (S1/S2). The port must reproduce each shape where it occurs, not one
   number for all.
2. **Kill scope.** `Shell.swift:116` kills the process group when the child leads one.
   `StdioProcess.swift:121` kills only the pid, so on macOS a stdio child's grandchildren survive. That
   is why `StdioProcess.didExit` at :173-178 waits 1 s for a grandchild that still holds stdout.
   `TerminateJobObject` kills the whole tree, which matches Shell but is **broader than StdioProcess**.
   This probe used the job for both because the probe text says so. See the design consequences.
3. **The graceful signal is a Windows substitute, not SIGTERM.** Windows has no SIGTERM. The probe
   text chooses 0x03 for pty children and CTRL_BREAK for pipe children (ENGINEERING DECISION,
   ADR-0002 / stack_decision.json). In the source, the terminal panel's pty is ended by SwiftTerm's
   `view.terminate()` (`TerminalStore.swift:59-63`), an external package that is not in the pinned
   tree. What that sends is **UNRESOLVED** by this probe.
4. **The helper is a separate process.** It is this same exe in `ctrlbreak <pid>` mode, created with
   `DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP`, and runs FreeConsole / AttachConsole(pid) /
   GenerateConsoleCtrlEvent(CTRL_BREAK_EVENT, pid) / FreeConsole. Reason: AttachConsole is
   process-wide state, and the runner must not trade its own console away mid-run. The helper's .NET
   start dominates M3's 40 to 56 ms. `helperDone` at 96 to 113 ms includes a deliberate 50 ms wait
   for the misdelivery guard.
5. **When the deadline starts.** The timer starts at t0, alongside the helper launch, not after the
   helper finishes. This mirrors `asyncAfter` being scheduled right after a `terminate()` that does
   not block. The timer is `Task.Delay(gap).ContinueWith(...)`, a timer continuation rather than a
   blocked thread.
6. **The pipe children get their own hidden console** (`CREATE_NO_WINDOW | CREATE_NEW_PROCESS_GROUP`),
   the way a GUI parent with no console would give them. The runner itself had no console window
   (`GetConsoleWindow() = 0`).
7. **Reading the +/- 50 ms rule.** "Both timestamps logged within +/- 50 ms" was read as: the
   non-cooperating exit lands inside [950, 1050] ms after t0, and the cooperating exit lands before
   1000 ms. The timestamp is taken when `WaitForSingleObject` on the process handle returns, on a
   highest-priority watcher thread.
8. **Timer resolution.** The run happened with the global timer resolution at 0.5 ms, set by some
   other process (`NtQueryTimerResolution` current = 5000 x 100 ns). Deadline overshoot was 4.4 to
   9.8 ms. The behaviour at the default 15.625 ms resolution was **not measured** (UNKNOWN). Lowering
   the global resolution would have meant touching other processes. INFERENCE, not measured: even a
   full 15.6 ms tick of added overshoot would stay inside +/- 50.
9. **Only ERROR_NO_DATA was seen.** Either code satisfies the criterion.

## Design consequences

- The mechanism works on this stack, with plain .NET 10 P/Invoke and no packages. Graceful exits in
  3 to 56 ms, a forced kill at the deadline plus about 10 ms, and a tree kill through the job.
- **Use three shapes, not one:** 1 s for `Shell`, `MCPHub.terminate` and `MCPProbe`; 3 s for
  `StdioProcess`; and no gap for `MCPHub.terminateAll`. All three come from source. This probe
  measured the gap mechanism at 1000 ms and 3000 ms. The no-gap shape is the same two calls with a
  zero delay, and was not run separately.
- **Kill scope needs a decision before implementation (orchestrator).** Job-wide termination for
  `StdioProcess` differs from the source's pid-only SIGKILL. Two options: (a) TerminateProcess the
  child only, and keep the 1 s stdout drain at `StdioProcess.swift:173-178` for surviving
  grandchildren, or (b) take the job kill and record it as a platform adaptation.
- **Pty exit cannot be detected from a failed write.** A write to the ConPTY input still succeeds
  after the child has exited, because conhost owns the pipe. It fails only after
  `ClosePseudoConsole`, about 60 ms later. Pty child exit must come from the process handle.
- **Treat a write to a dead child's stdin as recoverable.** Catch `IOException` and check
  `(HResult & 0xFFFF)` for 232 or 109.
- **Keep AttachConsole in a helper.** Calling GenerateConsoleCtrlEvent without attaching to the
  target console fails with ERROR_INVALID_PARAMETER (C3). The call has to run inside the target's
  console, so the helper is needed.

## Environment

- `cmd /c ver`: `Microsoft Windows [Version 10.0.26200.9457]` (Windows 11 Pro).
- `dotnet --list-sdks`: `10.0.401 [C:\Program Files\dotnet\sdk]`. Runtime reported by the probe: `.NET 10.0.12`, X64.
- CPU: AMD Ryzen 7 9800X3D 8-Core Processor, 16 logical processors. GPU is not relevant.
- `Stopwatch.Frequency 10000000  IsHighResolution True`.
- Node for the extra fixtures: `C:\Program Files\nodejs\node.exe`, `v24.14.1`.
- `MARROW_PORT_DATA_DIR=C:\Users\omera\AppData\Local\Temp\marrow-p6-1706920493`, a fresh folder,
  deleted after the run. Nothing in the probe reads it.
- Verdict run started `2026-09-22T21:39:43.8789385+03:00`. The smoke run started `21:38:08`.

## Commands and real output (trimmed to the decisive lines)

```
> cmd /c ver
Microsoft Windows [Version 10.0.26200.9457]

> cd E:\Droppy\reference\droppy-code; git log -1 --format=%H
dd2b86efef9e77168f304a86e8a8411877578bbe
(Shell.swift and StdioProcess.swift read with awk line numbering; the lines are quoted above)

> cd E:\Droppy\reference\droppy-code-417ab7d3; git diff dd2b86ef... 1fbf81e6... --stat -- DroppyCode/Core/Support/Shell.swift DroppyCode/Core/Support/StdioProcess.swift
 DroppyCode/Core/Support/Shell.swift        | 60 ++++++++++++++++--------------
 DroppyCode/Core/Support/StdioProcess.swift | 51 +++++++++++++------------
(hunks touch only Mutex/locking and .trimmed; terminate() at Shell.swift:110-118 is identical in 1fbf81e6)

> $env:MARROW_PORT_DATA_DIR = "$env:TEMP\marrow-p6-$(Get-Random)"   (created fresh)
> dotnet build E:\Droppy\port\porting\probes\p6\p6.csproj -c Release
Program.cs(315,17): error CS4004: Cannot await in an unsafe context          <- first attempt
(fixed by replacing the async lambda with Task.Delay(gap).ContinueWith)
Build succeeded.    0 Warning(s)    0 Error(s)                               <- TreatWarningsAsErrors on

> p6.exe run --out %TEMP%\marrow-p6-smoke --trials 2                         (smoke run)
P6 VERDICT: PASS   exit=0
  (one issue found: C5 #3 printed "[grandchild survived cleanup]". The check waited 0 ms right after
   TerminateJobObject, whose termination of the job's processes is asynchronous. It was changed to a
   bounded 2000 ms wait that records the time. In the verdict run the grandchild was gone 3.4 to
   5.5 ms after cleanup began.)

> dotnet build ... -c Release        -> Build succeeded. 0 Warning(s) 0 Error(s)
> p6.exe run --out E:\Droppy\port\porting\probes\p6\evidence --trials 10     (verdict run)
exit=0
M1-pty-coop          main     10/10 as expected | exit min 2.7 median 3.6 max 4.0 | deadline fired 1004.4..1009.4
M2-pty-stubborn      main     10/10 as expected | exit min 1009.9 median 1010.9 max 1011.7 | deadline fired 1006.9..1008.2 | kill->exit max 3.7
M3-pipe-coop         main     10/10 as expected | exit min 40.6 median 47.1 max 55.9 | deadline fired 1007.0..1008.6
M4-pipe-stubborn     main     10/10 as expected | exit min 1010.5 median 1011.3 max 1012.7 | deadline fired 1007.5..1009.1 | kill->exit max 3.7
C1-pty-no-0x03       control  3/3 as expected | exit min 1009.5 median 1011.3 max 1011.7
C2-pipe-stdin-only   control  3/3 as expected | exit min 1011.3 median 1011.8 max 1012.2
C3-pipe-no-attach    control  3/3 as expected | exit min 1011.2 median 1011.4 max 1012.0
C4-pty-no-force      control  3/3 as expected | exit n/a (alive at t0+2000)
C5-pipe-no-force     control  3/3 as expected | exit n/a (alive at t0+2000)
S1-pipe-coop-3000    source   3/3 as expected | exit min 41.5 median 43.4 max 46.9 | deadline fired 3006.0..3009.8
S2-pipe-stub-3000    source   3/3 as expected | exit min 3010.7 median 3012.2 max 3013.2
N1..N4 (node)        extra    3/3 each as expected
main cases        : all as expected
controls          : all behaved as the absent capability predicts
broken pipe       : PASS
P6 VERDICT: PASS

Sample full trial lines (verdict run):
M2 #1 pass=True inJob=True signal@0.0 deadline@1008.0 aliveAtDeadline=True kill@1008.1 exit@1010.7 code=137 ignoredSeen=True gc=39264 gcDead@1010.7
M4 #1 pass=True inJob=True signal@12.9 helperDone@112.6 helperExit=0x0 deadline@1007.5 aliveAtDeadline=True kill@1007.6 exit@1010.6 code=137 ignoredSeen=True gc=55972 gcDead@1010.6
M3 #1 pass=True inJob=True signal@4.1 helperDone@112.3 helperExit=0x0 deadline@1008.1 aliveAtDeadline=False kill@ exit@52.7 code=131
C3 #1 pass=True inJob=True signal@3.5 helperDone@112.6 helperExit=0x20057 deadline@1008.3 aliveAtDeadline=True kill@1008.5 exit@1011.2 code=137

> Import-Csv evidence\trials.csv  (aggregation)
rows: 73  failures: 0
deadline fired (1000 ms cases): min 1004.4 max 1009.4
forced exit - 1000 (M2,M4,N2,N4): min 9.9 max 12.9 n=26
coop exit (M1,M3,N1,N3): max 55.9 n=26
```

## Clean-up

Every fixture ran in a Job Object with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` and was killed or closed
within its trial. `Get-Process p6` found nothing afterwards. The four `node` processes that were
running had started at 21:19, before this probe's first run at 21:38, so they are not from this probe
and were left alone. `bin\`, `obj\`, the smoke output folder and the `MARROW_PORT_DATA_DIR` folder
were deleted. No file outside `porting/probes/p6/` was written.

## What P6 does NOT establish

- Nothing about real agent CLIs. Only scripted stand-ins and `node -e` one-liners ran.
- Nothing about behaviour at the default 15.625 ms timer resolution (deviation 8).
- Nothing about what SwiftTerm's `terminate()` sends on macOS (deviation 3).
- It is not the P5 probe. Job plus pseudoconsole in one attribute list worked here as a side effect,
  but P5's own criteria were not evaluated.
