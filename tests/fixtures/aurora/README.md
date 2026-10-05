# AURORA-RENDER reference fixtures and handoff

**READY: 210/210 Chrome frames pass the lead's perceptual bound.** The oracle is
`chrome/`, imported byte-for-byte from `origin/codex/icon-aurora` at `eb2d30e43`:
Google Chrome 154.0.8037.93, DPR 1, one canvas pixel per output pixel, and a fresh
browser document for each frame. The tests verify the locked source, input,
manifest and PNG hashes before comparison. Node is only a secondary diagnostic.

The lead's acceptance rule, recorded under `ED-NEW-aurora-render`, applies to
**each frame independently**: premultiplied RGBA peak <= 24/255, mean <= 0.5/255,
and at least 99.5% of pixels within 4/255 in every channel. All pixels and all
four channels count; there are no comparison masks, skipped frames or exceptions.
Both PNG exports are decoded to straight RGBA, then RGB is premultiplied using
nearest-rounded 8-bit `(channel*alpha+127)/255`; alpha remains unchanged. The mean
is the absolute channel difference summed over all four channels and all pixels,
divided by four times the pixel count.

CPU rendering uses premultiplied **BGRA8888/N32**, matching Chromium's raster
canvas storage on Windows/Linux. Logical RGBA is the comparison representation,
independent of that storage order. Skia 119's RGBA raster blitters use different
rounding and are not interchangeable with Chrome's N32 blitters. The scratch
and night surfaces and the parity-test destination now use N32. GPU/other-format
readback is not covered by this CPU comparison; T10 owns native boot validation.

The table gives the maximum peak, maximum frame mean and minimum fraction of
pixels within 4 across the 30 frames at each size. The strict column is the
maximum **unpremultiplied PNG RGBA** difference, retained as the 2/255 diagnostic.

| Size | Premultiplied peak /255 | Worst mean /255 | Minimum pixels within 4/255 | Strict peak /255 |
|---:|---:|---:|---:|---:|
| 16 | 7 | 0.253906 | 99.609375% | 7 |
| 24 | 5 | 0.256944 | 99.652778% | 5 |
| 32 | 6 | 0.169434 | 99.609375% | 85 |
| 48 | 4 | 0.173720 | 100.000000% | 64 |
| 64 | 3 | 0.155579 | 100.000000% | 64 |
| 256 | 4 | 0.121281 | 100.000000% | 128 |
| 512 | 5 | 0.118959 | 99.999619% | 128 |

`parity-summary.json` records every frame, both representations and their
per-size aggregates. Strict 2/255 passes 31/210 frames; it is a diagnostic, as
explicitly decided by the lead. No remaining frame fails perceptual acceptance.
The final warnings-as-errors solution build reports zero warnings/errors; all
35 filtered tests pass, including all 12 Traceability checks.

Coverage: 16/24/32/48/64/256/512 px, dark/light, still, launch at
0/120/300/540/780/1020/1200 ms, idle at 0/900/1800/3600/5400/6300/7200 ms.
Launch duration is 1200 ms; idle period is 7200 ms. `chrome/README.md` contains
browser provenance and the lead's Windows regeneration commands. The renderer
retains IconKit's signed right shift in its deterministic seeded xorshift RNG.

Skia 119 normally selects legacy 4x4 antialiasing for point-dense small paths;
Chrome's newer Skia selects analytic antialiasing. The renderer bypasses that
heuristic with a zero-edge move or an offscreen zero-length stroke contour,
then confines rasterisation to the original path's device bounds rounded outward,
as Chrome's small mask blitter does. These bounds come from transformed source
controls, including rotation/skew. No visible source geometry is added and no
comparison pixel is removed. Wider scratch strokes use SkPaint's own filled
outline before analytic scan conversion; its native two-axis hairline rule is
preserved. This repairs the small bands, stars, tile clips and 64 px glass rim.
`diagnostics/review-perceptual.md` records the independent source/API review.

The two pooled scratch canvases start at 300 x 150, grow as needed and reverse
roles after each blur. They retain IconKit's padded reduction transform, filter
a raster snapshot and sample the requested source rectangle using Catmull-Rom
with the fast source constraint. Destination clip, blend and image alpha apply
on the return draw. The static night layer is cached at IconKit's scale steps.
Gradients retain float CSS alpha, interpolate unpremultiplied sRGB and dither;
image global alpha is quantized to 8 bits, as Chromium does.

**Blur clarification:** D uses CSS `filter: blur(length)`, whose length specifies
Gaussian sigma directly. Radius/2 belongs to `shadowBlur`, which D does not use.
Chromium's `filter_effect_builder.cc` passes the CSS length through FEGaussianBlur
to BlurPaintFilter unchanged. The renderer uses rounded `rd/k` without halving it.
Direct Chromium 151 impulse probes matched SkiaSharp's box approximation at
sigma .29/.38/.96/1.28/1.44/1.54/2.5; see `diagnostics/blur-impulse-comparison.json`.

Validate from the repository root, with disposable application data:

```sh
MARROW_PORT_DATA_DIR=/tmp/marrow-aurora-data dotnet build Marrow.sln -c Debug -warnaserror -p:EnableWindowsTargeting=true -m:1 -nodeReuse:false
MARROW_PORT_DATA_DIR=/tmp/marrow-aurora-data AURORA_PARITY_REPORT=tests/fixtures/aurora/parity-summary.json dotnet test Marrow.sln -c Debug --no-build -p:EnableWindowsTargeting=true -m:1 -nodeReuse:false --filter "(FullyQualifiedName~AuroraMarkTests|FullyQualifiedName~Traceability)&FullyQualifiedName!~Native" --logger "console;verbosity=normal"
```

The Windows lead can omit `EnableWindowsTargeting` and choose a Windows scratch
profile. `validation-build.txt` and `validation-tests.txt` are the final output:
Debian 13 x64, .NET SDK 10.0.401/runtime 10.0.12, Avalonia 12.1.2, SkiaSharp 3.119.4.
No package, project file, loading control, boot window, app icon asset or locked
design source changed.

`Draw(canvas,size,theme,AuroraTime)` has explicit still, launch-in-seconds and
idle-in-seconds helpers. Own one instance per render thread and dispose it there.
For an already restricted clip, pass `deviceCanvasSize`, the Canvas 2D
canvas.width/height equivalent. Tests cover restored caller state, prior-frame
independence, rectangular canvases and integer offsets.

Seven allocation checks measure 0 **managed** bytes for 120 changing draws each,
in both themes, after warm-up. The continuously changing-scale check also
passes. This is OrbRendererTests' `GC.GetAllocatedBytesForCurrentThread` metric;
native surfaces, shaders, filters and snapshots still allocate and are released.

`AuroraMark.WarmupPrograms` lists the 9 destination program families; no custom
SkSL is needed. OD-0108's registry can call `Warmup(offscreenGpuCanvas,program,
actualDesignSize,deviceCanvasSize)` at each required size/DPI. Each call draws a
still and an early launch frame with 249/255 tile opacity, covering both opaque
and opacity-modulated programs. Tests exercise 96/256/512 px and 100/125/150/200%
scales. Windows GPU compilation and launch smoothness remain T10's runtime checks.
The renderer API is unchanged from the head T10 is integrating. The lead will
communicate that loaders use the same premultiplied acceptance representation.

Historical evidence is retained: `parity-summary-chrome-strict.json` and
`validation-chrome-strict-tests.txt` describe d42ac543's prior strict delivery.
`validation-chrome-test-first.txt` is the initial Chrome red run.
`validation-perceptual-test-first.txt` and `parity-baseline-rgba.json` preserve the
measurement before the format/coverage fixes; the lead's alpha representation
was still pending in that run, so its original assertion/report used straight
RGBA, while recording both sets of metrics. Applying the final premultiplied
rule to those recorded frames gives 147 failures. The Node root `manifest.json`,
`png/`, `generate.mjs`, package files, `parity-summary-node.json` and
`validation-node-*.txt` remain secondary diagnostics. Regenerate only Node with
`npm ci` and `npm run generate`; its picture canvases/blur behavior differ from
browser raster canvases.
