# Supervised trial H fixtures

44 real command captures on the owner's Windows PC on 2026-10-05. Updater invocations were
sequential; read-only Office inspection overlapped Python updater waits. That scheduling
deviation from the task's stricter one-tool-at-a-time requirement is recorded in TRIAL-H.md.
See `porting/first-release/reports/TRIAL-H.md` for interpretation, documentation and limitations.

- `executable` and `arguments`: exact argument boundaries; `commandLine`: Windows-quoted display.
- `exitCode`: signed Win32 process exit, or null if process creation failed. stderr retains that failure.
- `stdout` / `stderr`: original decoded UTF-8 streams except profile/username redaction.
- `durationMs`, `timestamp`, `finishedAt`: measured Stopwatch duration and UTC capture instants.
  A metadata serialization pass converted UTC instants to local offsets; they were canonicalized
  back to UTC without changing those instants, durations or captured streams.
- `versionsBefore` / `versionsAfter`: verified **whole-trial** versions of the associated tool;
  these are metadata attached after the sequential trial, not claims about command-time state.
  Python metadata names both installed CPython minor lines. Absent gh has null versions.

The acceptance tests load these files from the checkout and replay streams and argv in memory.
Unknown calls throw. No winget, Office, real CLI or network runs in these tests. Synthetic outcomes
are constructed in test source and explicitly identified; no synthetic command was saved here.

Installer provenance is limited to current registry installer type/package-manager matching;
winget matching does not prove which installer launcher was used historically.
