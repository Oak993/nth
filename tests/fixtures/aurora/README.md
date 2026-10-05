# AURORA-RENDER reference fixtures and handoff

**NOT READY.** The strict pixel comparison fails. This branch is for review and repair;
it must not be treated as an accepted Aurora port. See `parity-summary.json` and
`validation-tests.txt` for the measured maximum and each reference frame's result.
The tolerance remains **2/255 in each RGBA channel at every pixel**, including
translucent edges; there are no masks, average-score allowances, or skipped frames.

`generate.mjs` executes the locked `iconkit.js` and `icons-a-j/D-aurora.js` verbatim
in a fresh Node VM for each size/theme. The only DOM shim creates real Canvas 2D
canvases from **skia-canvas 3.0.8**, using its CPU engine with filter support enabled.
No reference pixel comes from C#. `manifest.json` binds the locked source hashes,
timings, dimensions, theme, launch progress, idle seconds, and each PNG's SHA-256.
Regenerating all 210 PNGs produced a byte-identical manifest and PNG hashes.

The references cover 16, 24, 32, 48, 64, 256, and 512 pixels, both dark and light,
the still, launch at 0/120/300/540/780/1020/1200 ms, and idle at
0/900/1800/3600/5400/6300/7200 ms. The launch duration is 1200 ms; idle repeats
after 7200 ms. The signed right shift in the module's xorshift generator is retained.

Regenerate on Linux or Windows, from this directory:

```sh
npm ci
npm run generate
```

Validate from the repository root (omit `EnableWindowsTargeting` on Windows if desired):

```sh
dotnet build Marrow.sln -c Debug -warnaserror -p:EnableWindowsTargeting=true
dotnet test Marrow.sln -c Debug --no-build -p:EnableWindowsTargeting=true --filter "(FullyQualifiedName~AuroraMarkTests|FullyQualifiedName~Traceability)&FullyQualifiedName!~Native" --logger "console;verbosity=normal"
```

Set `MARROW_PORT_DATA_DIR` to a disposable directory before validation. Recorded
environment: Debian 13 x64, .NET SDK 10.0.401/runtime 10.0.12, Node 24.19.0,
Avalonia 12.1.2, SkiaSharp 3.119.4. No .NET dependency or project file changed.
`validation-test-first.txt` records the initial failure before AuroraMark/AuroraTime
existed. `validation-build.txt` and `validation-tests.txt` contain the final output.

The renderer exposes `Draw(canvas, size, theme, AuroraTime)` and explicit still,
launch-in-seconds and idle-in-seconds helpers. A renderer instance belongs to one
render thread and must be disposed. Callers with an existing restricted canvas clip
should pass `deviceCanvasSize`, the Canvas 2D canvas.width/height equivalent; the
default uses the canvas's device clip extent. Integer translations on a rectangular
device canvas are covered by a separate regression check.

The managed allocation tests cover changing launch and idle frames at all seven
sizes, both themes, and continuously changing canvas scale. They use
`GC.GetAllocatedBytesForCurrentThread`, the metric used by OrbRendererTests.
Native Skia gradients, pictures, filters and internal layers still allocate; their
references are released. This is a **zero managed bytes** result, not a claim of
zero native heap activity.

`AuroraMark.WarmupPrograms` lists the standard Skia gradient/solid/blur/blend
program families. There is no custom SkSL. App's OD-0108 registry can call
`renderer.Warmup(offscreenGpuCanvas, program, actualDesignSize, deviceCanvasSize)`
with the actual device transform for **each size and DPI it will use**. Blur kernel
selection depends on device sigma. The CPU tests cover 96/256/512 px and
100/125/150/200% scaling. GPU compilation and Windows boot smoothness are unverified;
the App integration and owner motion review belong to the other tasks.

The selected Node backend draws offscreen canvases as nested Skia pictures and
implements its CSS blur with radius/2 sigma. The renderer mirrors that oracle's
picture pipeline, padded transforms, destination clip/composition and solid-alpha
quantization. Browser Canvas normally rasterizes the reduced scratch canvases;
browser parity is an additional unresolved comparison. The remaining measured
differences include path edges and interior color. Their cause is unresolved;
different Skia revisions alone have not been established as the explanation.

An independent source review checked RNG, bands, timing, geometry, ABI signatures,
resource cleanup and warm-up coverage. Its concrete fixes are included: no growing
scale cache, actual device bounds, opaque night-layer elision, exact star arc
construction, and size-aware warm-up. Pixel parity remains a release blocker.
LoadingMark.cs, BootStageWindow.cs, app icon assets and locked design files are untouched.
