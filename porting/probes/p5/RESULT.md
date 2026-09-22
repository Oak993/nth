# P5 RESULT: PASS

Task packet T-M0-004. ConPTY and a Job Object in ONE proc-thread attribute list, from C# P/Invoke.
Run 2026-09-22 (UTC 18:35:56 to 18:36:01 for the recorded run; three repeat runs followed, all PASS).

## Deviation from the probe text (owner's safety rules)

The probe text says to launch `claude.exe --version` / `--help`. **It was not launched.** This PC has
signed-in agents and the owner's safety rules forbid launching claude.exe or any real AI CLI. The
child is instead `fixture/p5-fixture.exe`, a small .NET 10 console app built in this folder that:

- prints its PID and whether its stdout / stdin are redirected,
- writes one marker to stdout and a different one to stderr,
- answers `cols <n>` with `Console.WindowWidth` / `BufferWidth` / `WindowHeight` as its console reports them,
- answers `spawn` by creating a grandchild (itself with `sleep 120`) through a plain `CreateProcessW`,
  creation flags `0`, and prints the grandchild's PID.

Consequence: what is proven is the Windows mechanism (attribute list, pty, job, resize, kill tree)
with a .NET console child. Whether claude.exe specifically (a non-.NET binary) reads its column
count the same way after `ResizePseudoConsole` is **UNKNOWN** from this probe.

Interpretation recorded: "pty output arrives on both channels" is read two ways and both are tested:
(1) the child's stdout AND stderr both arrive on the ConPTY output pipe; (2) both pty pipes carry
data, i.e. commands written to the input pipe are answered on the output pipe. Each pipe has its own
servicing thread (output reader thread, input writer thread), both started before `CreateProcessW`.

## The numbers (recorded run, `evidence/run.txt`)

| Condition | Measured | Result |
| --- | --- | --- |
| One attribute list, `dwAttributeCount` 2 (PSEUDOCONSOLE 0x20016 + JOB_LIST 0x2000D) | `attribute count=2`, CreateProcessW ok | True |
| Blittable `STARTUPINFOEXW` | `Marshal.SizeOf=112` (x64 native size 112), pinnable via `GCHandle.Alloc(Pinned)` = True | True |
| Creation flags | `0x00080000` (EXTENDED_STARTUPINFO_PRESENT only) | True |
| A3 child in job the instant CreateProcessW returns | `IsProcessInJob(child, jobA)` = True, first call after CreateProcessW | True |
| A1 stdout marker on pty output pipe | `OUT-CHANNEL-MARK via-stdout` seen | True |
| A1 stderr marker on pty output pipe | `ERR-CHANNEL-MARK via-stderr` seen | True |
| A1 input pipe to child to output pipe | `cols 1` answered in 31.0 ms | True |
| A2 child is on the pty | child PID 25480 = PROCESS_INFORMATION PID; `outRedirected=False inRedirected=False` | True |
| First output byte | 33.8 ms after the pty threads started (16.0 to 17.7 ms in repeat runs) | |
| A4 columns, no resize between two queries | R1 = 120, R2 = 120 | True |
| A4 after `ResizePseudoConsole(73x25)`, HRESULT 0 | child reports COLS=73 BUF=73 ROWS=25 | True |
| A4 after `ResizePseudoConsole(150x40)`, HRESULT 0 | child reports COLS=150 BUF=150 ROWS=40 | True |
| Grandchild in the job (inherited, no assignment call) | `IsProcessInJob(grandchild, jobA)` = True; job accounting total=2 active=2 | True |
| A5 `TerminateJobObject(jobA, 0x7E57)` | returned True | True |
| A5 child | handle signaled at 2.63 ms, exit code 0x7E57 | True |
| A5 grandchild | handle signaled at 2.63 ms, exit code 0x7E57 | True |
| A5 job accounting after kill | total=2 active=0 | True |
| A5 PIDs after every handle closed | child 25480 and grandchild 53508: `Process.GetProcessById` throws; `tasklist` "No tasks are running" | True |

Kill-to-signal times over the four runs: 2.63, 3.25, 2.54, 2.47 ms.

## Control (case B): JOB_LIST absent, must fail to kill

Same fixture, same pseudoconsole setup, same job created, but the attribute list has count 1 and
carries only PSEUDOCONSOLE.

| Control check | Measured | Behaved as the control must |
| --- | --- | --- |
| B1 child in job | `IsProcessInJob(child, jobB)` = False | Yes |
| grandchild in job | `IsProcessInJob(grandchild, jobB)` = False; accounting total=0 active=0 | Yes |
| B2 `TerminateJobObject(jobB, 0x7E57)` | returned True, yet after 1500 ms child ALIVE and grandchild ALIVE | Yes |
| B3 `TerminateProcess(child)` alone | child exit 0x7E58; grandchild still ALIVE (orphaned) 2000 ms later | Yes |

So the same kill call does nothing without JOB_LIST, and killing the child alone does not take the
grandchild. Case A's grandchild death is attributable to the job, not to the child dying or the
console going away. The orphan was then killed explicitly (exit 0x7E59) and both control PIDs were
confirmed gone.

The control for resize is R1 = R2 = 120 with no resize between them: the column change at R3 and
R4 is caused by `ResizePseudoConsole`, not drift. In the raw transcript (`evidence/pty_transcript_caseA.txt`)
ConPTY also emits `\e[8;25;73t` and repaints the screen after the resize, which is why each query
carries a sequence tag (R1..R4) and only the fresh tag is matched.

## No CREATE_SUSPENDED / ResumeThread anywhere in the path

```
$ grep -rn -i "suspend\|resumethread" --include=*.cs .
grep suspend/resume exit=1          (no match in probe or fixture)
$ grep -n "CreationFlags = \|CreateProcessW(IntPtr.Zero" probe/Program.cs fixture/Program.cs
probe/Program.cs:194:            s.CreationFlags = Native.EXTENDED_STARTUPINFO_PRESENT;
probe/Program.cs:196:            Check(Native.CreateProcessW(IntPtr.Zero, cmd, IntPtr.Zero, IntPtr.Zero, false, s.CreationFlags,
fixture/Program.cs:88:            if (!CreateProcessW(IntPtr.Zero, cmd, IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, IntPtr.Zero, ref si, out var pi))
```

The runtime log also prints `creation flags=0x00080000`. The child is in the job at creation
(A3), so no suspended-then-assign window exists.

## Environment

```
> cmd /c ver
Microsoft Windows [Version 10.0.26200.9457]
> dotnet --version
10.0.401
> (Get-CimInstance Win32_Processor).Name
AMD Ryzen 7 9800X3D 8-Core Processor
```

Runtime reported by the probe: `.NET 10.0.12`, 64-bit process. GPU not relevant.
The probe process itself was already inside a job (`IsProcessInJob(self, NULL)` = True, from the
harness that ran it), so the JOB_LIST path was exercised as a nested job. A launch from a process
in no job at all was not tested separately.
`MARROW_PORT_DATA_DIR` = `C:\Users\omera\AppData\Local\Temp\marrow-p5-652660844` (fresh, deleted afterwards).

## Commands and real output

Build (PowerShell, 2026-09-22T21:35:45+03:00):

```
$env:MARROW_PORT_DATA_DIR = "$env:TEMP\marrow-p5-$(Get-Random)"   -> C:\Users\omera\AppData\Local\Temp\marrow-p5-652660844
dotnet build porting\probes\p5\fixture\p5-fixture.csproj -c Release -nologo
    Build succeeded.  0 Warning(s)  0 Error(s)
dotnet build porting\probes\p5\probe\p5-probe.csproj -c Release -nologo
    Build succeeded.  0 Warning(s)  0 Error(s)
```

Both csproj files carry no PackageReference, so the repo-root Directory.Packages.props has nothing
to resolve, and Directory.Build.props (TreatWarningsAsErrors, Nullable, ImplicitUsings) applies
unchanged; the build had zero warnings under it. (p1-glass's csproj puts Version attributes on its
PackageReferences and predates central package management; that pattern was not copied.)

Run 1 (the recorded run):

```
> p5-probe.exe fixture\bin\Release\net10.0\p5-fixture.exe porting\probes\p5\evidence
P5 probe start 2026-09-22T18:35:56.023Z
OS: Microsoft Windows NT 10.0.26200.0; 64-bit process: True; runtime: .NET 10.0.12
this probe process is itself inside some job: True (nested jobs are supported on Windows 8+)
STARTUPINFOEXW: Marshal.SizeOf=112 (x64 native is 112); pinnable (blittable)=True

=== CASE A: attribute list count 2 (PSEUDOCONSOLE + JOB_LIST) 2026-09-22T18:35:56.025Z ===
CreateProcessW ok: child pid=25480, creation flags=0x00080000, attribute count=2
A3 IsProcessInJob(child, jobA) immediately after CreateProcessW returned: True
first output byte at 33.8 ms after the pty threads started; READY seen=True: 'FIXTURE-READY pid=25480 outRedirected=False inRedirected=False END'
A1 stdout marker arrived on pty output pipe: True; stderr marker arrived on pty output pipe: True
A2 child reports pid=25480 (matches PROCESS_INFORMATION: True); stdout/stdin redirected: False/False
A1/A4 input pipe -> child -> output pipe round trip: R1 'R1 COLS=120 BUF=120 ROWS=30 END' in 31.0 ms; R2 (no resize between) 'R2 COLS=120 BUF=120 ROWS=30 END'
A4 ResizePseudoConsole(73x25) HRESULT=0x00000000; R3 'R3 COLS=73 BUF=73 ROWS=25 END' in 31.0 ms
A4 ResizePseudoConsole(150x40) HRESULT=0x00000000; R4 'R4 COLS=150 BUF=150 ROWS=40 END'
grandchild spawned by the child: 'GRANDCHILD pid=53508 END'
grandchild handle opened=True; IsProcessInJob(grandchild, jobA)=True; job accounting before kill: total=2 active=2
before kill: child alive, grandchild alive
A5 TerminateJobObject(jobA, 0x7E57) returned True
A5 child wait=signaled at 2.63 ms, exit code 0x7E57
A5 grandchild wait=signaled at 2.63 ms, exit code 0x7E57
A5 job accounting after kill: total=2 active=0
A5 after all handles closed: child pid 25480: not present (Process.GetProcessById threw ArgumentException); tasklist: INFO: No tasks are running which match the specified criteria.
A5 after all handles closed: grandchild pid 53508: not present (Process.GetProcessById threw ArgumentException); tasklist: INFO: No tasks are running which match the specified criteria.
threads: output reader managed id 4, input writer managed id 5, main 2
shutdown A: writer joined=True (input queue completed); ClosePseudoConsole took 0.0 ms; reader joined=True (ReadFile returned end, GetLastError=109 (109 = ERROR_BROKEN_PIPE)); bytes read on output pipe=1256

=== CASE B (control): attribute list count 1 (PSEUDOCONSOLE only), same job created 2026-09-22T18:35:56.475Z ===
CreateProcessW ok: child pid=38164, creation flags=0x00080000, attribute count=1
B1 IsProcessInJob(child, jobB) immediately after CreateProcessW returned: False
B READY seen=True: 'FIXTURE-READY pid=38164 outRedirected=False inRedirected=False END'
B grandchild 'GRANDCHILD pid=33476 END'; IsProcessInJob(grandchild, jobB)=False; job accounting: total=0 active=0
B2 TerminateJobObject(jobB) returned True; after 1500 ms each: child ALIVE, grandchild ALIVE
B3 TerminateProcess(child) returned True; child wait=signaled exit 0x7E58; grandchild 2000 ms later: ALIVE (orphaned)
B cleanup: TerminateProcess(grandchild) -> wait signaled, exit 0x7E59
B after cleanup: child pid 38164: not present (...); tasklist: INFO: No tasks are running which match the specified criteria.
B after cleanup: grandchild pid 33476: not present (...); tasklist: INFO: No tasks are running which match the specified criteria.
shutdown B: writer joined=True (input queue completed); ClosePseudoConsole took 0.0 ms; reader joined=True (ReadFile returned end, GetLastError=109 (109 = ERROR_BROKEN_PIPE))

columns reported by child: R1=120 R2=120 (no resize) R3=73 (after 73) R4=150 (after 150)
A1 pty output on both channels ........ True
A2 child is on the pty ................ True
A3 in job at creation, flags 0x80000 .. True
A4 resize changes child's columns ..... True
A5 job kill takes child + grandchild .. True
CONTROL (no JOB_LIST) failed to kill .. True
P5: PASS
P5 probe end 2026-09-22T18:36:01.775Z
EXIT=0
```

Runs 2 to 4 (evidence written to a temp folder, since deleted; decisive lines only):

```
run 2 EXIT=0  first byte 17.7 ms; kill signaled 3.25 ms, both 0x7E57; B2 child ALIVE grandchild ALIVE; B3 grandchild ALIVE (orphaned); R1=120 R2=120 R3=73 R4=150; P5: PASS
run 3 EXIT=0  first byte 16.1 ms; kill signaled 2.54 ms, both 0x7E57; B2 child ALIVE grandchild ALIVE; B3 grandchild ALIVE (orphaned); R1=120 R2=120 R3=73 R4=150; P5: PASS
run 4 EXIT=0  first byte 16.0 ms; kill signaled 2.47 ms, both 0x7E57; B2 child ALIVE grandchild ALIVE; B3 grandchild ALIVE (orphaned); R1=120 R2=120 R3=73 R4=150; P5: PASS
> Get-Process p5-fixture,p5-probe -ErrorAction SilentlyContinue   -> (nothing: no leftover processes)
```

## Design details worth carrying into the supervisor

- `PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE` takes the HPCON value itself as `lpValue`;
  `PROC_THREAD_ATTRIBUTE_JOB_LIST` takes a pointer to an array of job HANDLEs, and that memory must
  stay valid until `CreateProcessW` returns.
- `STARTF_USESTDHANDLES` with null std handles and `bInheritHandles = FALSE` was used so the child
  does not pick up the host's (possibly redirected) std handles; with it, the child saw console
  handles (`outRedirected=False inRedirected=False`). The variant without that flag was not tested.
- The host closes its copies of the pty-side pipe ends right after `CreatePseudoConsole`.
- Shutdown order that worked: complete the input thread, close the input pipe, `ClosePseudoConsole`
  while the output thread is still draining, then the output thread sees ERROR_BROKEN_PIPE (109).
- The jobs also set `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` as a crash safety net. Every kill measured
  above happened while the job handle was still open, so that limit did not contribute to any
  measured kill.

## What P5 does NOT establish

- Behaviour with the real claude.exe (not launched, see the deviation). UNKNOWN.
- `ClosePseudoConsole` with live attached clients: in both cases every attached process was dead
  before the close (0.0 ms). Not measured.
- A host that is in no job at all: this run's host was inside a harness job (nested job case).
- Breakaway (`CREATE_BREAKAWAY_FROM_JOB`) by a child: not attempted; the job does not allow it.

## Files

- `probe/p5-probe.csproj`, `probe/Program.cs`: the probe (case A and control case B, mechanical verdict).
- `fixture/p5-fixture.csproj`, `fixture/Program.cs`: the agent-CLI stand-in.
- `evidence/run.txt`: full log of run 1. `evidence/pty_transcript_caseA.txt`, `evidence/pty_transcript_caseB.txt`:
  raw ConPTY output of run 1 with ESC shown as `\e`.

Cleanup: bin and obj folders of both projects deleted; the temp data folder
`marrow-p5-652660844` and the repeat-run evidence folder deleted; no fixture process left running.
