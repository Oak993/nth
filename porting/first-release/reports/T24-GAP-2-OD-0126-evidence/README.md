# OD-0126 evidence

The only test edit is the named detail-out per-frame exception. GAP tests still fail, so the verdict is NOT READY.

`gap2-od126.log`/`.trx` record the actual filtered test run. `build.log` and `gate-B.log` record the checks. `commands.json` has exact commands. `scope-check.json` binds the untouched production/oracle/reference files and the changed test to the post-merge baseline.

`rule-replay.json` was produced by the external C# diagnostic `rule-probe.cs.txt`/`rule-probe.csproj.txt`: it calls the unchanged FrameCurves oracle from the newly built App test assembly and checks the exact OD rule against the prior actual-page CSV (production/sampling unchanged). Its exit code is 1, not a replacement-test pass. To reproduce, copy those `.txt` templates to `Program.cs`/`rule-probe.csproj` in a scratch directory, then run:

```bash
dotnet run --project /tmp/probe/rule-probe.csproj -p:MarrowRoot=/path/to/marrow-port -p:EnableWindowsTargeting=true -- /path/to/marrow-port/porting/first-release/reports/T24-GAP-2-motion-evidence/after-page.csv
```

The detail-only exception accepts frames 1/2, but frame 3 fails from nominal-versus-TimeSpan 60 Hz sampling; placeholder remains outside the named exception. No epsilon, rounding, clock or oracle change was made. Windows rendering remains unverified.

Ledger cleanup/appends JSONs prove staging exact prefixes, unique original contributions and binary appends. Independent review findings are included.
