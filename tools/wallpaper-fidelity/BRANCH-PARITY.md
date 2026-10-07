The branch probes are developer-only diagnostics for Tunnel and Stairwell. They
read the locked GLSL, compile it in the exact reference Chromium/SwiftShader, and
compare RGBA32F outputs with the production SkSL on Skia's raster backend. They
never write the reference folder, acceptance bounds, metric, or assertions.

Run from the repository root with .NET 10, Python with numpy, Chromium
151.0.7922.173, and `npm ci --prefix tools/wallpaper-fidelity`:

```sh
dotnet build tools/wallpaper-fidelity/branch-probe/BranchProbe.csproj
node tools/wallpaper-fidelity/probe-branches.mjs /tmp/marrow-branch-probes
dotnet run --no-build --project tools/wallpaper-fidelity/branch-probe -- \
  --scan tests/fixtures/wallpapers/reference /tmp/marrow-pixel-scan.jsonl
python tools/wallpaper-fidelity/emulate-branches.py \
  --probes /tmp/marrow-branch-probes --pixel-scan /tmp/marrow-pixel-scan.jsonl
```

After applying the production fix, use `--fixed` with the new live probes to
validate Skia's corrected evaluation against both the prediction and reference.
The archived post-fix verification is:

```sh
python tools/wallpaper-fidelity/emulate-branches.py --fixed \
  --probes porting/reports/wl-fix/branch-probes-after.zip
```

Set `DOTNET` for the probe runner if dotnet is outside PATH, and
`WALLPAPER_CHROMIUM` if the pinned browser is outside `/usr/bin/chromium`.
The offline pre-fix
proof is reproducible without a browser or .NET installation:

```sh
python tools/wallpaper-fidelity/emulate-branches.py \
  --probes porting/reports/wl-fix/branch-probes-before.zip \
  --pixel-scan porting/reports/wl-fix/failing-pixels-before.jsonl
```

The Python model implements ordinary binary32 operations separately, and fused
mul/add only where the backend explicitly contracts. SwiftShader's legacy
precision log2 rational polynomial and exp2 polynomial, its degree-17 atan
polynomial, and Skia's degree-4 atan and approximate log2/pow2 are modeled
independently. Probe outputs confirm the pinned Chrome build uses the legacy
log2/exp2 path with separate polynomial mul/add. Merely substituting libc math
or calling `holeT(p,3.)` with a literal gives different results: ANGLE folds
constant sin/cos/pow, while the original wall evaluates the loop variable.
Diagnostic loops preserve by-value `p` inside holeT.

All 16000 branch decisions in each of the two geometry states, for each
wallpaper and backend, must match live measurements. The model predicts the
proposed fix on all four grids and checks all 44 affected reference frames,
including their exact option and tilt inputs. It does not emulate the other
5104 frames; the unchanged fidelity suite renders those. All 39 over-limit
pixel occurrences in the 20 failing frames must have the reproduced original
wrong branch and the predicted corrected reference branch.

RuntimeEffect exposes ES2 and cannot use the reference implementation's bit
casts. The production helper normalizes/reconstructs through exact
powers-of-two scaling, verified against 262144 independently generated normal
binary32 mantissa/exponent decompositions. Its polynomial operations remain
separate binary32 mul/add. RGBA32F post-fix probes validate the resulting live
branches on the complete grids.

Arithmetic sources (Apache-2.0):

- [SwiftShader ShaderCore.cpp](https://github.com/google/swiftshader/blob/1e80438d2b93ef36a7c05f8d2b81233bac0e3d16/src/Pipeline/ShaderCore.cpp),
  Sin5, Atan_01, Atan2, Log2_legacy and Exp2_legacy. Coefficients and evaluation
  order are confirmed against the pinned runtime, rather than assuming this
  source revision is the Chromium dependency revision.
- [Skia m119 raster math](https://github.com/google/skia/blob/chrome/m119/src/opts/SkRasterPipeline_opts.h),
  approx_log2, approx_pow2, sin5q_, approx_atan_unit, atan2_, and lerp.

The atan polynomial comes from Abramowitz and Stegun, Handbook of Mathematical
Functions, section 4.4.49. The log2/exp2 helpers preserve the reference backend's
finite positive input domain used here; they are not replacements for general
IEEE libm functions. No epsilon, pixel coordinate, theme, frame ID, or palette
appears in the production arithmetic fix.
