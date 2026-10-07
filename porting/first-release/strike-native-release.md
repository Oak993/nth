# T14 native release gate — needs the lead's quiet window

The owner's ruling defers native quiet-PC measurements to the lead's combined release gate. No quiet run is attempted in this phase. The existing measurement is executable now:

```powershell
& 'C:\Program Files\Git\bin\bash.exe' 'E:/Droppy/work/scratch/codex-strike/strike-quiet-window.sh'
```

This uses unchanged heavy.sh, takes native.lock atomically, builds the App tests, enables server GC in the native child, and runs the official wrapper. It measures 60 seconds after five seconds of warm-up. Build/start/activation are additional. The child verifies server GC and real GPU/compositor execution and records update rate, main callbacks, UI bytes, render-thread quiet/drawn bytes, GC counts and content-update gaps. Artifacts strike-st0-load.json and strike-st0-gate-failures.json are under the wrapper's fresh one-* data directory in the Windows temporary folder (see Marrow.Testing.TestDataDirectory). The historical name is retained; it executes the current Mix production path.

Before each run: screen free of other native/gallery processes, CPU below20%, input idle30seconds. During: CPU must remain below35%. Never count a run under shared load as passed. Native measurements need60seconds each for solo Claude, solo OpenAI, a pair, model change mid-strike, fast toggle and knob drag. N-03 requires both1x and2x. N-05 requires60seconds each Claude/OpenAI. N-06 requires fixed-time strike and afterglow captures against the matching headless frames. The lead's release harness owns these scenario/capture profiles; this command is the ST-0 production probe, not a substitute for the entire PhaseE campaign.

The existing DXGI Desktop Duplication capturer was verified present (read only) at:

```text
C:\Users\omera\AppData\Local\Temp\claude\E--Droppy\c13e7b6a-fc79-4b04-a170-a4b2f43ca682\scratchpad\framecap\bin\Release\net10.0\framecap.exe
framecap.exe <physical-x> <physical-y> <physical-width> <physical-height> 60 <owned-output-directory>
```

Its index.csv records present QPC times, accumulated frames and rectangle changes; frames.raw is BGRA and info.txt records refresh period. Determine the rectangle from the measurement window on the same display; never substitute compositor timestamps for presentation intervals. It must run while the probe is measuring, not after it. Its source/older run2.sh are reference only: the latter targets another worktree and has weaker CPU/idle limits, so do not run it for T14.

Pass criteria, unchanged from TESTS.md PhaseE:

* N-01: ANGLE/D3D11, warmed shader paths, no crash and no draw call over20ms.
* N-02:60.0±0.1 updates/s, present intervals on4refreshes@240Hz (2@120,1@60) at least99%, longest content gap≤21ms, main frames0.
* N-03: full Strike work including recording/submit p99≤1.5ms,max≤3ms at1x; p99≤2ms,max≤4ms at2x.
* N-04: drawn allocation equals measured no-Strike baseline exactly (historical5064B), additional0B, quiet80B, no GC in60s, server GC verified in-process.
* N-05: independent native touched-bubble audit0misses;150–190bursts/minute.
* N-06: same-time DXGI vs headless mean box difference≤3/255 and99th-percentile brightness difference≤0.02.
* N-07: raw knob charge before leader, flare peak within one frame of strike.
* N-08: load results informative only.

StrikeMeasurementGate.Failures fails missing quiet/CPU/idle/presentation evidence. The ST-0 report intentionally identifies itself as informative and carries presentationCapture=not measured. A successful wrapper result never turns that report into a quiet or whole-PhaseE pass. The lead must combine the native probe, actual CPU/input/presentation evidence and the remaining scenario profiles before acceptance. Reviewer owns D-07 mutations and the owner eye check remains unverified.
