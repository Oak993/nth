# P10 result: PASS (headless CPU-raster lease path only)

Task packet T-M0-009. Run 2026-09-22, 18:29Z to 18:33Z, by a worker subagent.

> P10: Regression harness for the [Unstable] lease API, built BEFORE production code. Capture reference
> screenshots of P1's scene, then bump Avalonia to the next available patch and re-run. PASS: output is
> byte-comparable, or every difference is explainable and approved.

**Verdict.** The harness is built. It is deterministic: two separate processes, and two renders inside one
process, gave byte-identical PNGs. It detects change: a 1% change to one shader constant altered 1,758 pixels,
and removing the lease altered every pixel. The next patch, Avalonia **12.1.3**, gave PNGs byte-identical to
12.1.2 for all three scenes, both as PNG file bytes and as decoded pixels. So the probe's condition, "output is
byte-comparable", is met for the path this harness exercises.

**Scope.** The verdict covers Skia's CPU raster backend and nothing more (`GrContext` was null, measured). The GPU
path the desktop app will use is not exercised. See "What this does NOT establish".

## Numbers

| Comparison | no_glass | tint_only | glass | Harness verdict | Expected |
| --- | --- | --- | --- | --- | --- |
| 12.1.2 run A vs 12.1.2 run B (determinism, separate processes) | identical | identical | identical | IDENTICAL, exit 0 | identical |
| P1's committed `porting/probes/out/p1_*.png` (2026-09-20) vs 12.1.2 run A | identical | identical | identical | IDENTICAL, exit 0 | identical |
| **12.1.2 run A vs 12.1.3 run A (the upgrade)** | **identical** | **identical** | **identical** | **IDENTICAL, exit 0** | the question |
| 12.1.3 run A vs 12.1.3 run B | identical | identical | identical | IDENTICAL, exit 0 | identical |
| CONTROL shader: 12.1.2 vs 12.1.2 with `disp * 1.10` changed to `disp * 1.11` | identical | identical | **1,758 of 1,135,200 px differ**, max channel delta 171, mean 126.57, bbox [166,223]-[1153,643] | DIFFERENT, exit 1 | must differ |
| CONTROL shader, run through the 12.1.3 binary | identical | identical | 1,758 px differ (same figures) | DIFFERENT, exit 1 | must differ |
| CONTROL nolease: 12.1.2 vs the same render with the lease feature treated as absent | 1,135,200 px differ | 1,135,200 px differ | 1,135,200 px differ | DIFFERENT, exit 1; render itself exits 2 | must fail |

"Identical" means both `pngBytesIdentical=True` and `pixelsIdentical=True`. Pixels are compared after a lossless
decode to RGBA8888 unpremultiplied, so a PNG-encoder change alone would appear as `png` false with `pixels` true
and could be told apart from a real rendering change. That case did not occur.

In the shader control, `tint_only` does not change. That is correct: at `strength = 0`, `disp` is zero and the
changed constant multiplies zero. The glass difference is edge-only (`evidence/diff_control_shader_p10_glass.png`):
the top edge where the white bands cross it, the left edge, and the bottom edge under the transcript text. The
bounding box lies inside the panel rect [160,210]-[1160,650].

Reference hashes, identical under 12.1.2 and 12.1.3 (`reference/manifest.txt`, `evidence/manifest_12.1.3.txt`):

| Scene | PNG SHA-256 | Decoded-pixel SHA-256 | Bytes |
| --- | --- | --- | --- |
| p10_no_glass | edcd19cc4a764995c2152bc8075196a625da77e8478ebae3de1d2073361fdb93 | 9dbb396650bcab9b1b4cdb9b52e64e6293358528392768a5548f6926c1b19879 | 260221 |
| p10_tint_only | 12feff068952d7b9b560a6d02bc8ac008b86be43f83dfe3229eec50aa2a7b75e | 9536f7c4baf4f41c6f88be4f7d1fbb57105f58d01498a7b2bb729e27bc264795 | 258146 |
| p10_glass | ec68a64d81177332e9a0812e489f5478596675b7c4766cb18059ce19606fa2cb | d6dd527c61dc9424c7a5a9d403f2296e9652f2b1c97ab5ac0f80f53e4c0c244c | 261263 |

The upgrade really swapped the Avalonia binaries. It did not swap Skia. SHA-256 prefixes of the two build outputs:

| File | 12.1.2 build | 12.1.3 build |
| --- | --- | --- |
| Avalonia.Base.dll | 39f7c973a20346b | 10f1e2633a5776d |
| Avalonia.Skia.dll | 449a83a2d086a1c | 363e38440990804 |
| Avalonia.Headless.dll | 5806ec32049e9bb | bf4b2f867229603 |
| SkiaSharp.dll | aaaaa18c68ba1f3 | aaaaa18c68ba1f3 (same) |
| runtimes/win-x64/native/libSkiaSharp.dll | 7dec3ba900ab353 | 7dec3ba900ab353 (same) |

At runtime the harness also printed the loaded versions: `avalonia 12.1.2+d3c867a9...` against `avalonia 12.1.3+8eeda4f6...`,
with `skiasharp 3.119.4 ... native 119.0` in both. The nuspecs agree: Avalonia.Skia 12.1.2 and 12.1.3 both pin
SkiaSharp 3.119.4 and HarfBuzzSharp 8.3.1.3 for net10.0.

## The capability, the control, and the path exercised

Capability under test: a harness that turns P1's glass path into a byte-exact reference and flags any change, so
that an Avalonia bump can be gated on it.

- Control 1, change detection. One SKSL constant is perturbed (`--perturb shader`, red chromatic offset
  1.10 to 1.11). If the harness were blind, for example if it compared a stale file, hashed the wrong buffer or
  skipped the effect, this would read IDENTICAL. It reads DIFFERENT, exit 1.
- Control 2, lease absent. `--perturb nolease` makes `TryGetFeature<ISkiaSharpApiLeaseFeature>()` return null,
  which is the most likely way an [Unstable] API breaks at runtime. The render's own mechanism check fails
  (`lease=False`, `RENDER: FAIL`, exit 2), and the comparison shows every pixel of every scene differing.
  If the lease interface itself changed shape, the harness would not compile, because it calls the API directly.
- Null control, determinism. Separate processes, and repeated renders inside one process (`repeatIdentical=True`
  on every line), give identical bytes. Without this, "identical after upgrade" would mean nothing.

The path exercised, measured and not assumed:

1. `AppBuilder.Configure<Application>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })`,
   the same bootstrap as P1. This gives Avalonia's real Skia render interface, not the null headless drawing stub.
2. `RenderTargetBitmap(1320x860, 96 dpi).Render(control)` produces an `ImmediateDrawingContext` for the control's
   `ICustomDrawOperation`.
3. `context.TryGetFeature<ISkiaSharpApiLeaseFeature>()` then `feature.Lease()`, giving `lease.SkCanvas` (measured
   `lease=True`). `lease.GrContext` was **null** in every render (measured `grContextNull=True`), so this is a CPU
   raster Skia surface.
4. The app-owned offscreen surface is `SKSurface.Create(info)`, the raster branch of P1's code (measured
   `surface=True`), redrawn with the live scene and snapshotted.
5. `SKRuntimeEffect.CreateShader` over P1's SKSL, with the snapshot bound as the `backdrop` child shader, clipped to
   a rounded rect (measured `effect=True`). The SKSL runs on Skia's CPU raster pipeline.
6. `RenderTargetBitmap.Save(stream, PngBitmapEncoderOptions.Default)` encodes the PNG.

## What this does NOT establish

- **The GPU lease path is untested.** On the desktop app the lease will carry a non-null `GrContext` (Ganesh over
  ANGLE/D3D on Windows). `SKSurface.Create(GrContext, ...)`, SkSL-to-GPU shader code generation and GPU readback
  never ran here. An Avalonia patch that changed how the GPU lease exposes `GrContext`, surface origin or colour
  space would not be caught. A GPU variant needs a real window or an offscreen GL/D3D context. That is a separate
  probe on an unlocked session.
- **Avalonia contributes little to these pixels.** P1's scene draws everything as raw Skia through the lease.
  Avalonia supplies the lease, the RenderTargetBitmap surface and transform, and the PNG encoder. Skia was
  byte-identical across the two versions, so identical output was the likely outcome. The harness would catch an
  Avalonia change to that plumbing: control 2 demonstrates the coarse case. It says nothing about Avalonia-drawn
  controls, text layout or the compositor, because the scene has none.
- **Text uses `SKTypeface.Default`.** It resolved to this machine's system font. The references are therefore
  machine-specific. They are a regression baseline for this PC, not a portable golden image.
- **Only 12.1.3 was tried.** It is the only newer 12.1.x patch on nuget.org. It was published
  2026-09-22T17:42:04Z, about 50 minutes before this run. A future SkiaSharp bump would change the Skia side,
  which this run did not exercise.

## Deviations from the probe text and from p1-glass, with reasons

1. **Central package management.** The repo's `Directory.Build.props` sets `ManagePackageVersionsCentrally=true`
   and `TreatWarningsAsErrors=true`. p1-glass predates both (commit da77ff7, 2026-09-20; the props arrived in
   90bd97d, 2026-09-22) and **no longer restores**. A copy of its unmodified csproj and Program.cs, built inside
   this folder and then deleted, gave:
   `error NU1008: The following PackageReference items cannot define a value for Version: Avalonia, Avalonia.Headless, Avalonia.Skia.`
   `p10.csproj` therefore has no `Version` attributes and takes 12.1.2 from `Directory.Packages.props`. The upgrade
   uses `VersionOverride="$(P10AvaloniaVersion)"`, set per build with `-p:P10AvaloniaVersion=12.1.3`, in this
   csproj only. No repo-level file was edited.
2. **`Bitmap.Save(Stream)` is obsolete in 12.1.2.** It raised
   `error CS0618: 'Bitmap.Save(Stream, int?)' is obsolete: 'Use the overload accepting BitmapEncoderOptions instead.'`
   under TreatWarningsAsErrors. The harness calls `Save(stream, PngBitmapEncoderOptions.Default)` instead. The
   output is byte-identical to P1's committed PNGs, so the change does not alter the encoding.
3. **P1's scene, copied with hooks.** The SKSL, the scene geometry and the draw order are P1's. Four additions:
   the SKSL is read from a field so it can be perturbed, a lease-skip switch, a reset of the probe flags before
   each render, and a record of `GrContext is null`. The files are named `p10_*.png` instead of `p1_*.png`.
4. **Headless CPU raster, not GPU.** The task allowed this. The exact path is listed above.
5. **Release build, with output in `%TEMP%`.** One `obj` folder in this directory served both builds in turn.
   Each `dotnet build` restored the project again for its version, and `obj/project.assets.json` showed
   `Avalonia/12.1.3`, `Avalonia.Skia/12.1.3`, `Avalonia.Headless/12.1.3`, `SkiaSharp/3.119.4` and
   `HarfBuzzSharp/8.3.1.3` after the second build.
6. **NuGet global cache.** Restoring 12.1.3 added `avalonia*`/`12.1.3` packages to the shared user NuGet cache
   (`%USERPROFILE%\.nuget\packages`). Before the run only 12.1.2 was there. The cache is shared rather than a temp
   folder this probe created, so the packages were left in place.

## Environment

- `cmd /c ver`: `Microsoft Windows [Version 10.0.26200.9457]`
- `dotnet --version`: `10.0.401`
- CPU: AMD Ryzen 7 9800X3D 8-Core Processor
- GPUs present: NVIDIA GeForce RTX 4080, AMD Radeon(TM) Graphics, Parsec Virtual Display Adapter. None was used,
  because the lease had no `GrContext`.
- `MARROW_PORT_DATA_DIR=C:\Users\omera\AppData\Local\Temp\p10-data-9684`, created fresh, and deleted at the end.
- Scratch output: `%TEMP%\p10-run`, deleted at the end.

## Commands and their real output (trimmed to the decisive lines)

```
$ curl -s https://api.nuget.org/v3-flatcontainer/avalonia/index.json        (18:29:21Z)
  ... "12.1.0", "12.1.1", "12.1.2", "12.1.3" ]          (same tail for avalonia.skia and avalonia.headless)
$ curl .../avalonia.skia/{12.1.2,12.1.3}/avalonia.skia.nuspec | grep dependency
  both, net10.0: SkiaSharp 3.119.4, HarfBuzzSharp 8.3.1.3; Avalonia 12.1.2 / 12.1.3 respectively
$ curl --compressed https://api.nuget.org/v3/registration5-gz-semver2/avalonia/12.1.3.json | grep published
  "published":"2026-09-22T17:42:04.277+00:00"

# p1-glass as-is under the repo props (copy in p10/_p1check, deleted afterwards)
$ dotnet build p1-glass.csproj -nologo
  error NU1008: The following PackageReference items cannot define a value for Version: Avalonia, Avalonia.Headless, Avalonia.Skia. ...

$ dotnet build p10.csproj -c Release -nologo -o %TEMP%\p10-run\bin-12.1.2        (18:31:38Z)
  Program.cs(312,13): error CS0618: 'Bitmap.Save(Stream, int?)' is obsolete: 'Use the overload accepting BitmapEncoderOptions instead.'
  (after switching to Save(stream, PngBitmapEncoderOptions.Default))
  p10 -> C:\Users\omera\AppData\Local\Temp\p10-run\bin-12.1.2\p10.dll
  Build succeeded.

$ bin-12.1.2\p10.exe render --out ...\base-a        (18:32:00Z; base-b identical, both exit=0)
  avalonia        12.1.2+d3c867a9e2de379249b03dbeb3495bd7f076a81a
  skiasharp       3.119.4+release-3.119.4.f568ac94... native 119.0
  p10_no_glass   png=edcd19cc... pixels=9dbb3966... bytes=260221 repeatIdentical=True lease=True surface=False effect=False grContextNull=True
  p10_tint_only  png=12feff06... pixels=9536f7c4... bytes=258146 repeatIdentical=True lease=True surface=True effect=True grContextNull=True
  p10_glass      png=ec68a64d... pixels=d6dd527c... bytes=261263 repeatIdentical=True lease=True surface=True effect=True grContextNull=True
  RENDER: OK
  exit=0
$ bin-12.1.2\p10.exe render --out ...\ctl-shader --perturb shader
  p10_glass      png=3978f70f... pixels=5ac2f8e5... bytes=261742 repeatIdentical=True lease=True surface=True effect=True grContextNull=True
  RENDER: OK      exit=0
$ bin-12.1.2\p10.exe render --out ...\ctl-nolease --perturb nolease
  p10_*          png=6e0a642b... bytes=4509 lease=False surface=False effect=False grContextNull=False   (all three)
  RENDER: FAIL (mechanism flag false or in-process repeat differed)
  exit=2

$ p10.exe compare base-a base-b            -> all three pngBytesIdentical=True pixelsIdentical=True diffPixels=0/1135200; COMPARE: IDENTICAL exit=0
$ p10.exe compare p1-committed base-a      -> all three identical; COMPARE: IDENTICAL exit=0
$ p10.exe compare base-a ctl-shader
  p10_no_glass   pngBytesIdentical=True pixelsIdentical=True diffPixels=0/1135200 ...
  p10_tint_only  pngBytesIdentical=True pixelsIdentical=True diffPixels=0/1135200 ...
  p10_glass      pngBytesIdentical=False pixelsIdentical=False diffPixels=1758/1135200 maxChannelDelta=171 meanDeltaOfDiffering=126.57 bbox=[166,223]-[1153,643]
  COMPARE: DIFFERENT  exit=1
$ p10.exe compare base-a ctl-nolease
  each scene: pngBytesIdentical=False pixelsIdentical=False diffPixels=1135200/1135200 maxChannelDelta=255 ...
  COMPARE: DIFFERENT  exit=1

$ dotnet build p10.csproj -c Release -nologo -p:P10AvaloniaVersion=12.1.3 -o %TEMP%\p10-run\bin-12.1.3   (18:32:19Z)
  p10 -> C:\Users\omera\AppData\Local\Temp\p10-run\bin-12.1.3\p10.dll
  Build succeeded.
$ grep project.assets.json -> "Avalonia/12.1.3" "Avalonia.Headless/12.1.3" "Avalonia.Skia/12.1.3" "HarfBuzzSharp/8.3.1.3" "SkiaSharp/3.119.4"
$ bin-12.1.3\p10.exe render --out ...\up-a        (up-b identical, both exit=0)
  avalonia        12.1.3+8eeda4f6f546165b3f72e63c9f42247abb306905
  skiasharp       3.119.4+release-3.119.4.f568ac94... native 119.0
  p10_no_glass   png=edcd19cc... pixels=9dbb3966... bytes=260221 repeatIdentical=True lease=True ... grContextNull=True
  p10_tint_only  png=12feff06... pixels=9536f7c4... bytes=258146 repeatIdentical=True lease=True surface=True effect=True grContextNull=True
  p10_glass      png=ec68a64d... pixels=d6dd527c... bytes=261263 repeatIdentical=True lease=True surface=True effect=True grContextNull=True
  RENDER: OK
$ p10.exe compare up-a up-b                -> COMPARE: IDENTICAL exit=0
$ p10.exe compare base-a up-a              (the upgrade)
  p10_no_glass   pngBytesIdentical=True pixelsIdentical=True diffPixels=0/1135200 maxChannelDelta=0 meanDeltaOfDiffering=0.00 bbox=-
  p10_tint_only  pngBytesIdentical=True pixelsIdentical=True diffPixels=0/1135200 maxChannelDelta=0 meanDeltaOfDiffering=0.00 bbox=-
  p10_glass      pngBytesIdentical=True pixelsIdentical=True diffPixels=0/1135200 maxChannelDelta=0 meanDeltaOfDiffering=0.00 bbox=-
  COMPARE: IDENTICAL
  exit=0
$ bin-12.1.3\p10.exe render --perturb shader, then compare up-a up-ctl-shader
  p10_glass      pngBytesIdentical=False pixelsIdentical=False diffPixels=1758/1135200 maxChannelDelta=171 meanDeltaOfDiffering=126.57 bbox=[166,223]-[1153,643]
  COMPARE: DIFFERENT  exit=1        (18:32:29Z)
```

## How to re-run for the next upgrade

```powershell
$env:MARROW_PORT_DATA_DIR = "$env:TEMP\marrow-p10-$(Get-Random)"
dotnet build porting\probes\p10\p10.csproj -c Release -o $env:TEMP\p10\old
dotnet build porting\probes\p10\p10.csproj -c Release -p:P10AvaloniaVersion=<candidate> -o $env:TEMP\p10\new
& $env:TEMP\p10\new\p10.exe render --out $env:TEMP\p10\cand          # must print RENDER: OK, exit 0
& $env:TEMP\p10\new\p10.exe compare porting\probes\p10\reference $env:TEMP\p10\cand --diff-out $env:TEMP\p10\diff
# exit 0 = byte-identical. exit 1 = inspect diff_*.png and explain each difference before approving.
# Also re-run the controls through the candidate binary: --perturb shader must compare DIFFERENT, --perturb nolease must exit 2.
```

## Files in this folder

- `p10.csproj`, `Program.cs`: the harness.
- `reference/p10_{no_glass,tint_only,glass}.png` and `reference/manifest.txt`: the 12.1.2 references, about
  260 KB each, byte-identical to `porting/probes/out/p1_*.png`.
- `evidence/manifest_12.1.3.txt`, `evidence/manifest_control_shader.txt`, `evidence/manifest_control_nolease.txt`.
- `evidence/diff_control_shader_p10_glass.png`: the amplified difference map for the shader control, 7.6 KB.

## Cleanup

- `bin/` and `obj/` in this folder were deleted. The temporary `_p1check/` copy was deleted straight after its
  one build.
- `%TEMP%\p10-run` and `%TEMP%\p10-data-9684` were deleted.
- Every `p10.exe` run was a foreground process that had exited. The shared MSBuild worker nodes and
  `VBCSCompiler` were still running afterwards. They are shared build servers, and other agents' `dotnet test`
  runs were using them at the time, so this probe did not kill them. `dotnet build-server shutdown` stops them
  once nothing else is building.
