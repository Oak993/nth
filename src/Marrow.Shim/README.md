# SK-6 launcher

Publish on Windows x64 with the .NET 10 NativeAOT toolchain:

```powershell
dotnet publish src/Marrow.Shim/Marrow.Shim.csproj -c Release -r win-x64
```

The output is a standalone launcher. `ShimFiles.Write` copies it under the provided executable name and writes its `.shim` sidecar. The installer supplies all pinned paths and child environment values; locking, leases and version switching are the other build units. `ShimPath.ForChild` takes a resolved data root and an environment snapshot for SK-11 provider/native-command wiring.

Sidecars use source-generated JSON. `Target` is required; other fields are explicit child overrides (`Environment`, `PrefixArguments`, `PythonPathFile`, `PackagePaths`, `NodePath`, `LibreOffice`, `WorkDirectory`). Python/python3 use the installed interpreter plus its installed `python314._pth`, installed set paths and writable agent path. Pip uses the same definition with `PrefixArguments` set to `-m`, `pip`. Node sets `NodePath` to the installed node_modules tree. npm/npx remain `.cmd`: supply Node as `Target` and the upstream npm/npx CLI JavaScript path as the prefix; the writer creates an internal native launcher under `.launchers`. LibreOffice sets `LibreOffice` and its explicit work root. Other names need only their target and any explicit environment.

Native standard handles and working directory are inherited. Per-call Python/profile directories are removed after the target exits, including nonzero exit. Forced shim termination ends its Windows child tree through the Job Object; temporary-directory collection after forced termination needs the store GC integration.

All acceptance tests live in `tests/Marrow.Services.Tests/Port/Skills/Shims/` with `Feature=SK-6`. The standalone `tests/Marrow.Shim.OfflineTests` project links those same tests and sources, allowing offline Linux acceptance without unrelated voice packages. With a populated local package cache:

```sh
dotnet restore tests/Marrow.Shim.OfflineTests -p:PublishAot=false --source <local-package-cache>
dotnet test tests/Marrow.Shim.OfflineTests --no-restore -p:PublishAot=false --filter Feature=SK-6
```

That command tests a JIT launcher; it does not verify NativeAOT publish. Five Windows-only checks report explicit skips on Linux. Windows Git Bash acceptance requires Git for Windows (or its path in `MARROW_SK6_GIT_BASH`). See the engineering decision and evidence report for exact limits.
