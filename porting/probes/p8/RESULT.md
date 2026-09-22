# P8 result: filesystem watch overflow signal (T-M0-007)

**Result: PASS** (run 3, the decisive run, and run 2). Run 1 printed FAIL; why, and what changed,
is under "Deviations". All three outputs are kept verbatim in this folder.

## The probe text (verbatim, from `porting/tasks/T-M0-007.md`)

> P8 - Filesystem watch overflow signal. Point a System.IO.FileSystemWatcher with InternalBufferSize
> at its 4 KB minimum at a repo-shaped tree and generate a burst (a git checkout across a large branch,
> or a scripted 10,000-file touch). PASS: the Error event fires carrying InternalBufferOverflowException
> at least once, and the app's full-rescan path runs to completion. [...]

## What it maps to upstream (SOURCE FACT)

`DroppyCode/Services/Git/WorkingTreeWatch.swift` at the pinned `dd2b86ef`:

- `:180` the three loss flags `kFSEventStreamEventFlagMustScanSubDirs | KernelDropped | UserDropped`.
- `:186-189` any of them sets `everything = true`; `:211-212` then `state.changed = nil`.
- `:288` / `:308` the capture gets `paths == nil`, "nil means the whole tree".
- `:196` git's own writes are ignored; `:197-205` otherwise the directory holding each changed entry is recorded.

The file is unchanged at live upstream `1fbf81e6`:

```
$ cd /e/Droppy/reference/droppy-code-417ab7d3
$ git diff --stat dd2b86efef9e77168f304a86e8a8411877578bbe 1fbf81e61e28a079508a074c01ee1830a9cc23dc -- DroppyCode/Services/Git/WorkingTreeWatch.swift; echo "exit=$?"
exit=0          (no output: no difference)
```

`Program.cs` mirrors that: an overflow `Error` sets `Everything` (the `changed == nil` state) and bumps
the generation; after the burst, `Everything` routes to the full-rescan path, otherwise the
incremental changed-directory set is used.

## Environment

| Item | Value (command) |
| --- | --- |
| Windows | `Microsoft Windows [Version 10.0.26200.9457]` (`cmd /c ver`) |
| .NET SDK | `10.0.401` (`dotnet --version`) |
| Runtime | `.NET 10.0.12` (printed by the probe) |
| CPU | AMD Ryzen 7 9800X3D 8-Core Processor, 16 logical (`Get-CimInstance Win32_Processor`) |
| Temp volume | `C:` NTFS, Fixed (`Get-Volume`) |
| Git | `git version 2.54.0.windows.1` (not used by the probe) |
| GPU | not relevant |
| Data dir | `MARROW_PORT_DATA_DIR=C:\Users\omera\AppData\Local\Temp\marrow-p8-data-801151989` (fresh, empty, deleted afterwards). The probe refuses to run without it. |

## How it runs

- `p8-fswatch.csproj`: net10.0 console exe, **no PackageReference**. The repo-root
  `Directory.Build.props` (TreatWarningsAsErrors, Nullable, ImplicitUsings) applies unchanged and the
  build has 0 warnings. `Directory.Packages.props` (central versions) has nothing to resolve here.
  p1-glass does not "cope" with central versions at all: it predates them (p1-glass came in commit
  `da77ff7`, the props in `90bd97d`), and it still carries `Version=` on its PackageReferences while
  `dotnet msbuild porting/probes/p1-glass/p1-glass.csproj -getProperty:ManagePackageVersionsCentrally -getProperty:TreatWarningsAsErrors`
  now reports `"ManagePackageVersionsCentrally": "true", "TreatWarningsAsErrors": "true"`. Whether
  p1-glass still builds today was NOT tested (building it would write into its folder): UNKNOWN.
- Each case builds a fresh repo-shaped tree under `%TEMP%\marrow-p8-<guid>\<case>`: `.git\` (HEAD,
  config, refs\heads\main, one loose object), `README.md`, `.gitignore`, and `src\modNN\pkgN\seed.cs`
  for 20 modules x 10 packages = 200 directories, 202 working files. Seeding happens before the
  watcher starts.
- Watcher: `IncludeSubdirectories = true`, `NotifyFilter = FileName | DirectoryName | LastWrite | Size`,
  `InternalBufferSize = 4096`, value read back after setting. Handlers only count and record.
- Burst: 10,000 new files (`FileMode.CreateNew`, 1 byte each) spread over the 200 package directories.
- Quiescence: wait until no event and no error for 2 s (cap 60 s), then stop the watcher.
- Full-rescan path: iterative directory walk of the whole tree, skipping `.git` (as `git add -A` does),
  compared against the exact set of files the probe wrote. Taken **only** if the overflow flag is set.
- Control: 5 files, same watcher configuration. On the incremental path the probe also runs the same
  walk as an oracle (logged as "not the decision path").
- Informational, not a pass condition: the same 10,000 burst at `InternalBufferSize = 65536`.

## Pass conditions checked by the program (run 2 and run 3 version)

- C1 (probe text): at least one `InternalBufferOverflowException` Error across the 4 KB burst trials.
- C2 (probe text): every trial on the full-rescan path completed it with missing 0 and extra 0, and at least one trial took it.
- C3 control: 5-file burst raises no Error, reports a Created event for all 5 files, and the incremental
  set covers every written directory and holds nothing except those directories and their ancestors.
- C4 soundness: no 4 KB trial is missing a Created event for any burst file without an overflow Error
  (loss implies signal), and at least one trial really lost events (so the signal was exercised
  against real loss, not a coincidence).

## Commands and real output

Build (identical output before each run; 0 warnings with TreatWarningsAsErrors on):

```
PS> $env:MARROW_PORT_DATA_DIR = "$env:TEMP\marrow-p8-data-$(Get-Random)"; New-Item -ItemType Directory -Force $env:MARROW_PORT_DATA_DIR
C:\Users\omera\AppData\Local\Temp\marrow-p8-data-801151989
2026-09-22T21:31:32.5761549+03:00
PS> dotnet build "E:\Droppy\port\porting\probes\p8\p8-fswatch.csproj" -c Release
  Restored E:\Droppy\port\porting\probes\p8\p8-fswatch.csproj (in 55 ms).
  p8-fswatch -> E:\Droppy\port\porting\probes\p8\bin\Release\net10.0\p8-fswatch.dll
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

### Run 1: `dotnet ...\p8-fswatch.dll 3` (1 writer thread), 2026-09-22 21:31:38 to 21:32:01 +03:00, exit 1

Full output: `run1-output.txt` (produced by the first version of Program.cs, see Deviations).

```
[18:31:38.646Z] InternalBufferSize default=8192, after setting 1 it reads back 4096
control-5 | 4096 | 5 | 5 | 0 | 0 | incremental | 0 | 207 | 0 | 0
burst-10000-trial1 | 4096 | 10000 | 9975 | 1 | 0 | full-rescan | 24 | 10202 | 0 | 0
burst-10000-trial2 | 4096 | 10000 | 10000 | 0 | 0 | incremental | 0 | 10202 | 0 | 0
burst-10000-trial3 | 4096 | 10000 | 10000 | 0 | 0 | incremental | 0 | 10202 | 0 | 0
info-burst-10000-buffer65536 | 65536 | 10000 | 10000 | 0 | 0 | incremental | 0 | 10202 | 0 | 0
  Error event: System.IO.InternalBufferOverflowException: Too many changes at once in directory:C:\Users\omera\AppData\Local\Temp\marrow-p8-aabd3aad52fa4a70a7d09e92ffd77c8a\burst-10000-trial1.
  Full rescan completed in 24 ms: found 10202, expected 10202, missing 0, extra 0
  Incremental path: changed dirs 6 [src\mod00, src\mod00\pkg0, ... src\mod00\pkg4], expected 5, exact=False
C1 overflow Error in every 4 KB burst trial: False
C2 full rescan completed and found every file: False
C3 control: no Error, all events, exact incremental set: False
C4 burst trials really lost events: False
P8 RESULT: FAIL
```

Serial writes ran at 4,482 to 5,653 files/s. Columns: case | buffer | written | Created events |
overflow errors | other errors | path | rescan ms | on disk | missing | extra.

### Run 2: `dotnet ...\p8-fswatch.dll 5 8` (8 writer threads), 21:33:15 to 21:33:42 +03:00, exit 0

Full output: `run2-output.txt`.

```
control-5 | 4096 | 5 | 5 | 0 | 0 | incremental | 0 | 207 | 0 | 0
burst-10000-trial1 | 4096 | 10000 | 9932 | 2 | 0 | full-rescan | 27 | 10202 | 0 | 0
burst-10000-trial2 | 4096 | 10000 | 10000 | 0 | 0 | incremental | 0 | 10202 | 0 | 0
burst-10000-trial3 | 4096 | 10000 | 9824 | 3 | 0 | full-rescan | 25 | 10202 | 0 | 0
burst-10000-trial4 | 4096 | 10000 | 9977 | 1 | 0 | full-rescan | 15 | 10202 | 0 | 0
burst-10000-trial5 | 4096 | 10000 | 10000 | 0 | 0 | incremental | 0 | 10202 | 0 | 0
info-burst-10000-buffer65536 | 65536 | 10000 | 10000 | 0 | 0 | incremental | 0 | 10202 | 0 | 0
Overflowed trials: 3 of 5; trials that lost Created events: 3
C1 overflow Error at least once across 4 KB burst trials: True
C2 every full rescan completed and found every file (and one ran): True
C3 control: no Error, all events, correct incremental set: True
C4 loss implies signal in every trial, and real loss occurred: True
P8 RESULT: PASS
```

### Run 3 (decisive): `dotnet ...\p8-fswatch.dll 10 8`, 2026-09-22 21:34:16 to 21:35:00 +03:00, exit 0

Full output: `run3-output.txt`. Decisive lines:

```
[18:34:18.857Z] Events: Created=5 (distinct burst files reported 5/5) Changed=10 (on files 5, on dirs 5) Deleted=0 Renamed=0; Errors: overflow=0 other=0; generation=16; everything=False
[18:34:18.860Z] Incremental path: changed dirs 6 (first: src\mod00, src\mod00\pkg0, src\mod00\pkg1, src\mod00\pkg2, src\mod00\pkg3, src\mod00\pkg4), written dirs 5, covers all=True, outside written dirs and ancestors=0, correct=True
[18:34:22.194Z] Events: Created=9916 (distinct burst files reported 9916/10000) Changed=19821 (on files 9920, on dirs 9901) Deleted=0 Renamed=0; Errors: overflow=2 other=0; generation=29740; everything=True
[18:34:22.194Z]   Error event: System.IO.InternalBufferOverflowException: Too many changes at once in directory:C:\Users\omera\AppData\Local\Temp\marrow-p8-48cd2e8df2d44dfb8e18f33a2d7bc5c5\burst-10000-trial1.
[18:34:22.219Z] Full rescan completed in 23 ms: found 10202, expected 10202, missing 0, extra 0
[18:34:29.904Z] Events: Created=9915 (distinct burst files reported 9915/10000) Changed=19813 (on files 9915, on dirs 9898) Deleted=0 Renamed=0; Errors: overflow=1 other=0; generation=29730; everything=True
[18:34:29.920Z] Full rescan completed in 15 ms: found 10202, expected 10202, missing 0, extra 0

case | buffer | written | created events | distinct created | overflow errors | other errors | path | rescan ms | on disk | missing | extra
control-5 | 4096 | 5 | 5 | 5 | 0 | 0 | incremental | 0 | 207 | 0 | 0
burst-10000-trial1 | 4096 | 10000 | 9916 | 9916 | 2 | 0 | full-rescan | 23 | 10202 | 0 | 0
burst-10000-trial2 | 4096 | 10000 | 10000 | 10000 | 0 | 0 | incremental | 0 | 10202 | 0 | 0
burst-10000-trial3 | 4096 | 10000 | 9915 | 9915 | 1 | 0 | full-rescan | 15 | 10202 | 0 | 0
burst-10000-trial4 | 4096 | 10000 | 10000 | 10000 | 0 | 0 | incremental | 0 | 10202 | 0 | 0
burst-10000-trial5 | 4096 | 10000 | 10000 | 10000 | 0 | 0 | incremental | 0 | 10202 | 0 | 0
burst-10000-trial6 | 4096 | 10000 | 10000 | 10000 | 0 | 0 | incremental | 0 | 10202 | 0 | 0
burst-10000-trial7 | 4096 | 10000 | 10000 | 10000 | 0 | 0 | incremental | 0 | 10202 | 0 | 0
burst-10000-trial8 | 4096 | 10000 | 10000 | 10000 | 0 | 0 | incremental | 0 | 10202 | 0 | 0
burst-10000-trial9 | 4096 | 10000 | 10000 | 10000 | 0 | 0 | incremental | 0 | 10202 | 0 | 0
burst-10000-trial10 | 4096 | 10000 | 10000 | 10000 | 0 | 0 | incremental | 0 | 10202 | 0 | 0
info-burst-10000-buffer65536 | 65536 | 10000 | 10000 | 10000 | 0 | 0 | incremental | 0 | 10202 | 0 | 0
Overflowed trials: 2 of 10; trials missing a Created event for some burst file: 2
C1 overflow Error at least once across 4 KB burst trials: True
C2 every full rescan completed and found every file (and one ran): True
C3 control: no Error, all events, correct incremental set: True
C4 loss implies signal in every trial, and real loss occurred: True
P8 RESULT: PASS
```

8-writer bursts ran at 10,406 to 12,837 files/s across runs 2 and 3 (per-case lines in the output files).

## The numbers

- Minimum buffer: default reads 8192; setting 1 reads back 4096. Every 4 KB case read back 4096; the 64 KB case read back 65536.
- Overflow `Error` with `InternalBufferOverflowException`, per 4 KB 10,000-file trial: run 1 (serial) 1 of 3,
  run 2 (8 writers) 3 of 5, run 3 (8 writers) 2 of 10. Total 6 of 18. 1 to 3 Error events per overflowing trial.
  Exception message: `Too many changes at once in directory:<watched root>.`
- Loss when it overflowed: 25, 68, 176, 23, 84, 85 Created events missing out of 10,000.
- **Loss without the signal: 0 of 18 trials.** Every trial without an Error reported a Created event
  for all 10,000 files (runs 1 and 2 by raw count; run 3 by distinct path).
- Other Error types: 0 in all 26 cases.
- Full rescan: completed every time it was taken (6 times), 15 to 27 ms over 10,202 files, missing 0, extra 0.
- 64 KB buffer: no overflow in the 3 informational bursts (one per run). Not a pass condition, and 3 samples say little.
- Control (5 files, 3 runs): 0 Errors, 5 of 5 Created, incremental path; incremental set correct in runs 2 and 3.

## Control

The 5-file burst on the identical 4 KB watcher raised no Error in all three runs and reported every
file. So the Error in the large bursts is not raised by any burst; it appeared only in trials that
also lost Created events, and every trial that lost events raised it (C4). The PASS verdict is gated
by the program on C3; a spurious Error in the control would have printed FAIL.

## Observations the verdict does not cover (UNRESOLVED)

- In bursts WITHOUT an overflow Error, Changed events on directories fell short of one per file
  (run 3: 9979 to 9986 of 10,000), and Changed events on files were sometimes 10,001 or 10,002. The
  probe cannot tell whether the missing directory Changed events were coalesced by Windows or lost
  silently. This did not affect correctness here: every created file still produced a Created event,
  and its holding directory was in the incremental set (covers all = True in every incremental case).
  The port should not treat a directory's own Changed count as a completeness check.
- The overflow is timing dependent: 18 trials of the same burst overflowed 6 times. The probe shows
  the signal fires when Created events are lost; it does not show that no other kind of loss (for
  example a lost Deleted or Renamed event) can happen without it, because the burst only creates files.
- Only NTFS on a local fixed disk was measured. Network shares, ReFS, Dev Drive and WSL paths: UNKNOWN.

## Deviations from the probe text, with reasons

1. **Scripted creates, not a git checkout.** The probe text allows either. 10,000 new files are
   created (a "touch" of files that did not exist). No git process is run. Reason: deterministic,
   needs no repository with a large branch, and the exact expected file set is known for the rescan check.
2. **"The app's full-rescan path" is a probe stand-in.** No production watcher exists yet (gates A and
   B are not met, per CLAUDE.md). The probe's path is a whole-tree walk skipping `.git`, entered only
   through the same flag logic as `WorkingTreeWatch.swift:186-189/:211-212/:308`. Upstream's real
   rescan is `git add -A` into a scratch index; that part is not probed here.
3. **Pass conditions changed between run 1 and run 2, after seeing run 1.** Stated openly:
   - Run 1 required an overflow in *every* trial (stricter than the probe text's "at least once").
     Run 2 onward uses the probe text for C1 and adds C4 (loss implies signal), which is the property
     the port actually needs. Run 1 alone would also satisfy the probe text's C1 and C2 (1 overflow,
     rescan found 10,202 of 10,202), but it failed C3 and was a single serial run, so it is not relied on.
   - Run 1's control check was a probe bug: it required the incremental set to equal the written
     directories exactly, but creating a file also raises a Changed event on its directory, whose
     holding directory is the parent (`src\mod00`). Upstream `:197-205` would record that parent too.
     Run 2 onward requires "covers every written directory, nothing outside them and their ancestors".
   - Run 1 used one writer thread. Run 2 onward uses 8 (`Parallel.For`) to make the burst "as fast as
     possible", roughly doubling the write rate (4,482 to 5,653 files/s serial, 10,406 to 12,837 files/s with 8 writers).
   - Run 3 changed C4 from raw Created counts to distinct created paths, a stricter measure.
   `run1-output.txt` came from the first version of `Program.cs`; the current `Program.cs` produced
   runs 2 and 3 (run 2 before the distinct-path counters were added).
4. **Extra informational case** at the 64 KB maximum buffer, marked in the output as not a pass condition.

## Design consequence

`FileSystemWatcher.Error` with `InternalBufferOverflowException` is a usable Windows equivalent of
the three FSEvents loss flags: map it to `everything = true` / `changed = nil` and a whole-tree capture.
No fallback to ReadDirectoryChangesW by P/Invoke is needed on this evidence. Handle every other
`Error` exception type as "lost" too (upstream treats anything the stream could have missed as a fresh
snapshot); the probe saw none, so that is an ENGINEERING DECISION, not a measured fact.

## Cleanup

- The probe deletes each case tree and its temp parent itself: every case logs `Case tree deleted: True`
  and each run logs `Temp parent deleted: True`.
- `Remove-Item -Recurse -Force ...\p8\bin, ...\p8\obj, C:\Users\omera\AppData\Local\Temp\marrow-p8-data-801151989`,
  then `Get-ChildItem $env:TEMP -Directory -Filter "marrow-p8*"` printed `temp marrow-p8* left: 0`.
- Processes: each probe run exited by itself (exit 1, 0, 0). The MSBuild node processes (started 21:22)
  and VBCSCompiler (started 21:29) that were running afterwards were started before this probe's first
  build (21:31) by other work, so they were left alone.
- Kept in `porting/probes/p8/`: `p8-fswatch.csproj`, `Program.cs`, `RESULT.md`, `run1-output.txt`,
  `run2-output.txt`, `run3-output.txt` (each under 20 KB).
