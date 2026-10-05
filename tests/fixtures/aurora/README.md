# AURORA-RENDER reference fixtures and handoff

**NOT READY.** The primary oracle is now `chrome/`, imported unchanged from
`origin/codex/icon-aurora` at `eb2d30e43`. It contains 210 direct Canvas 2D PNGs
from Google Chrome 154.0.8037.93, at DPR 1 and one canvas pixel per output pixel.
`chrome/manifest.json` binds the browser, locked source hashes, inputs and every PNG.
The tests verify these hashes before comparing every unpremultiplied RGBA channel.
The limit is **2/255 at every pixel**, including transparent and translucent edges;
there are no masks, averages, skipped frames or tolerance exceptions.

`parity-summary.json` records all 210 measurements, including each frame's worst
pixel, channel, expected/actual ARGB values and number of pixels exceeding 2.
`validation-tests.txt` contains the full final test output. The engineering ledger
records the delivery as NOT READY until every frame passes. The final build has
zero warnings/errors; 34/35 filtered tests pass, including all 12 Traceability
checks. The sole failed test aggregates the 202 parity failures.

| Size | Maximum channel difference | Worst frame |
|---:|---:|---|
| 16 | 29/255 | `png/16-dark-idle-900.png` |
| 24 | 31/255 | `png/24-dark-still-0.png` |
| 32 | 85/255 | `png/32-dark-launch-120.png` |
| 48 | 128/255 | `png/48-light-launch-120.png` |
| 64 | 153/255 | `png/64-light-launch-120.png` |
| 256 | 255/255 | `png/256-light-still-0.png` |
| 512 | 255/255 | `png/512-light-still-0.png` |

**202/210 frames fail.** Only 16/24/32 px dark/light launch at 0 ms and 256 px
dark/light launch at 0 ms pass. Every still, idle frame and nonzero launch frame
fails. All exact per-frame values are in `parity-summary.json`.

Coverage: 16/24/32/48/64/256/512 px, dark/light, still, launch at
0/120/300/540/780/1020/1200 ms, idle at 0/900/1800/3600/5400/6300/7200 ms.
Launch duration is 1200 ms; idle period is 7200 ms. See `chrome/README.md` for
browser provenance and the lead's Windows regeneration commands. The locked
module and IconKit were not changed. The renderer retains the module's signed
right shift in its seeded xorshift generator.

The root `manifest.json`, `png/`, `generate.mjs`, package files and
`parity-summary-node.json` are the original **secondary Node diagnostic** set.
They are not the acceptance oracle. Node's skia-canvas 3.0.8 records its offscreen
canvases as pictures and halves the CSS blur sigma, unlike the browser.
`validation-node-*.txt` preserves that earlier delivery's logs. Its original
red test is `validation-test-first.txt`; `validation-chrome-test-first.txt`
records the failing Chrome comparison before the browser pipeline changes.
To regenerate only the secondary Node set: `npm ci` then `npm run generate` here.

The C# renderer now rasterizes both pooled scratch canvases, starting at the
browser's 300 x 150 default and growing them as needed. It preserves the padded
reduction transform, reverses the scratch roles after each blur, filters the
raster snapshot, then samples the requested source rectangle using Catmull–Rom
with the fast source constraint. Destination clip, blend and image alpha apply
when the filtered image is drawn back. The static night layer is cached as a
raster at IconKit's scale steps. Gradient stops retain float CSS alpha, interpolate
unpremultiplied sRGB and enable dithering; drawImage global alpha is quantized to
8 bits as Chromium does.

**Blur clarification:** the locked module uses CSS `filter: blur(length)`.
That length specifies Gaussian **sigma directly**. Radius/2 belongs to
`shadowBlur`, which this module does not use. Chromium's
`core/paint/filter_effect_builder.cc` passes the length to `FEGaussianBlur`,
which passes the same standard deviation to `BlurPaintFilter`. This is why the
renderer uses rounded `rd/k`, without division by 2. The independent review
confirmed the distinction. A direct Chromium 151 impulse probe matched SkiaSharp
at sigma .29/.38/.96/1.28/1.44/1.54/2.5, including the browser's box approximation;
see `diagnostics/blur-impulse-comparison.json`. An exact Gaussian replacement was
experimentally checked and removed because it disagreed with that browser.

The remaining failures include native path coverage, stroke coverage, and
partially transparent composition/resampling. The isolated 16 px night fill in
`diagnostics/` uses the locked cubic geometry: at (1,2), Chromium alpha is 100,
while SkiaSharp alpha is 80. This stage has no blur, clipping or prior drawing.
The full 16 px still also differs at an opaque green-band pixel (5,6): browser
G=247 versus C# G=222. These are material failures in the small-size art, independent
of the low-alpha RGB amplification seen in larger frames. Supersampling and
curve/stroke refinement experiments also failed the strict bound and were not
retained. Native path/raster behavior must be repaired before this is accepted;
the cause of every remaining composition difference is not yet isolated.
The report names every affected frame rather than attributing all errors to a
single unproved cause.

Validate from the repository root, with a disposable `MARROW_PORT_DATA_DIR`:

```sh
dotnet build Marrow.sln -c Debug -warnaserror -p:EnableWindowsTargeting=true
dotnet test Marrow.sln -c Debug --no-build -p:EnableWindowsTargeting=true --filter "(FullyQualifiedName~AuroraMarkTests|FullyQualifiedName~Traceability)&FullyQualifiedName!~Native" --logger "console;verbosity=normal"
```

Set `AURORA_PARITY_REPORT` to an output JSON path to capture the same detailed
measurements from the test. Recorded environment: Debian 13 x64, .NET SDK
10.0.401/runtime 10.0.12, Avalonia 12.1.2, SkiaSharp 3.119.4.
No .NET package, project file or app integration changed.

`Draw(canvas, size, theme, AuroraTime)` has explicit still, launch-in-seconds and
idle-in-seconds helpers. Own one instance per render thread and dispose it there.
If a caller has already restricted its clip, pass `deviceCanvasSize`, the Canvas
2D canvas.width/height equivalent. The default uses the device clip extent.
Tests cover canvas state restoration, prior-frame independence, rectangular
canvases and integer offsets.

The allocation tests cover changing launch/idle frames in both themes at all
seven sizes and continuously changing canvas scale, using the OrbRendererTests
metric, `GC.GetAllocatedBytesForCurrentThread`. Their result is zero **managed**
bytes. Native surfaces, snapshots, filters and shaders still allocate and their
references are released. Raster scratch surfaces are CPU-backed even when the
destination is a GPU canvas.

`AuroraMark.WarmupPrograms` lists destination gradient, solid, image-sampling and
blend families; no custom SkSL is needed. OD-0108's registry can call
`renderer.Warmup(offscreenGpuCanvas, program, actualDesignSize, deviceCanvasSize)`
with its actual transform at every required design size/DPI. The tests cover
96/256/512 px and 100/125/150/200% scaling. GPU compilation and Windows launch
smoothness remain unverified. LoadingMark.cs, BootStageWindow.cs, icon assets,
and the locked design files are untouched.
