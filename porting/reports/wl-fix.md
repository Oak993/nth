# T20 wallpaper branch fidelity

Verdict: **READY for the requested T20 raster fidelity gates**. Native Windows
GPU execution remains unverified; this is not whole-port acceptance.

Base: `fa74d2afce279017e89d9670b61dce17c3765c2c`, held `codex/wl-reconcile`.
Work/push branch: `codex/wl-fix`. No force push or other branch push is authorized.

The pre-fix proof is committed before any production shader change. The locked
GLSL is in `porting/first-release/design-src/wallpaper-library/static/walls-balanced-2.js`
and the embedded `static/wallpapers.json`. The reference generator is
`tools/wallpaper-fidelity/generate.mjs`. Both original shaders use highp, and
the pinned reference Chromium is 151.0.7922.173.

The original maximum-per-frame report concealed additional over-limit pixels.
Scanning all 44 Tunnel/Stairwell frames with the unchanged bridge and metric
finds **39 pixel occurrences in 20 frames**, at six unique coordinates:

| Geometry / coordinate | Cause | Reference value | Port value |
| --- | --- | ---: | ---: |
| Tunnel untilted (65,28), sheet 3 | approximate pow enlarges radius | +0.000025212765 | -0.000024020672 |
| Tunnel untilted (152,67), sheet 0 | approximate atan2 | -0.000001907349 | +0.000022649765 |
| Tunnel tilted (62,45), sheet 3 | approximate pow enlarges radius | +0.000005304813 | -0.000039756298 |
| Tunnel tilted (128,64), sheet 3 | approximate pow enlarges radius | +0.000005125999 | -0.000043272972 |
| Tunnel tilted (129,44), sheet 3 | approximate pow enlarges radius | +0.000042617321 | -0.000006139278 |
| Stairwell tilted (131,25), f | approximate log/atan2 crosses .06 | 0.0598975419998 | 0.0600135326385 |

The CPU emulation reproduces these reference and old port values bit for bit.
It also agrees with all **128000** live branch decisions (four grids, two
backends), then predicts the corrected reference branches for all **64000**
grid pixels and validates the geometry inputs of all **44** affected frames
(704000 frame/pixel decisions). Full pre-fix live float grids are archived in
`wl-fix/branch-probes-before.zip`; all over-limit pixels and per-frame evidence
are in `failing-pixels-before.jsonl` and `branch-proof-before.json`.

Tunnel's Skia radius at k=3 is 0.6334002614021301; SwiftShader's is
0.6333515048027039. The difference is 0.00004875659942626953. The original
loop uses runtime polynomial arithmetic: a literal call produces different
constant folding and is not evidence for the shipped branch. The correct
reference evaluation uses the SwiftShader legacy precision log2/exp2 path
and its atan polynomial, with explicit separate fp32 mul/add.

Perspective wiring is correct. The public selection reaches the static
controller's options["persp"], then .015*depth*persp, then renderer u_tilt.
Stairwell's [0.65,-0.4] becomes [0.006825,0.0042], exactly the manifest's
shader uniform; u_tg=1 adds it to p. Tunnel uses its own per-sheet tilt with
u_tg=0. No wiring repair is necessary.

SDK: installed .NET 10.0.401 under `/workspace/.dotnet`, matching the held
batch's recorded SDK. No global.json exists in this checkout. Tests use
disposable MARROW_PORT_DATA_DIR folders and exclude Native. Native Windows
GPU execution is not implied by the Linux raster checks.

Proof commit: `f5ae4578`. The production change is subsequent: translate
Tunnel's authored `pow(t,.85)` and atan2, and Stairwell's log and atan2 to
the proven reference polynomials. Colour-transfer powers and other wallpapers
retain their existing arithmetic. No epsilon or coordinate-specific correction
is used. ES2-compatible powers-of-two scaling reproduces the reference's
mantissa/exponent decomposition exactly in 262144 independent normal-float
checks; exp2 coefficient decimals round to the exact reference bit constants.

The six added branch regressions are **6 failed before / 6 passed after**,
with actual wrong-branch assertions in the red run. The corrected live float
grids agree with the CPU prediction and the reference for every branch.
The unchanged public-input bridge and metric now pass **44/44** affected
frames, **zero** over-limit pixels, with maximum error **0.010424813332**
(previous worst **0.487809612360**). After float grids and model checks are in
`branch-probes-after.zip` and `branch-proof-after.json`.

The baseline full gate reproduced **5128 passed / 20 failed / 5148 frames**,
and **333 passed / 3 failed / 336 test cases** (`app-before.trx`). The offline
reference integrity tool verifies all **5148** cases and **4428** PNGs, including
original source hashes and the unchanged manifest hash
`fda5a5fb47627cdfc50e6b8d5e6a40d9bef657b3e7413986aa7c6184295b8f64`.

Independent read-only review required by contract section 15 was performed by
`/root/branch_review`. It independently reran the offline CPU proof, checked
the six unique causal values against archived live float outputs, traced
Perspective through Normalize/controller/renderer, and reviewed the ES2
helper code and coefficient bits. It found no blocking issue in the stated
domain. Its attribution suggestion was applied by retaining the SwiftShader
copyright and Apache-2.0 notice. It did not duplicate the expensive test run.

Native Windows GPU precision/contraction remains unverified: the production
visual handler passes a GPU context, while these checks use Linux raster
surfaces. This report concerns the requested T20 arithmetic/fidelity gates;
it does not close the held batch's unrelated WIC/custom-picture checks or
claim whole-port acceptance. Full post-fix regression results follow below.

The serial solution build passed with **0 warnings / 0 errors**. Its command
adds `-m:1` after a parallel build lost a child MSBuild process; both logs are
retained. Gate B passes **8/8**. Byte checks preserve all 4429 reference files,
111 original snapshot files, 13 existing fidelity/metric/bridge files, 23
existing wallpaper UI test files, and the owner-decision ledger verbatim.
Engineering decisions and STATE retain their complete original byte prefixes
and mixed line endings, with only appended ED-NEW-wl-fix entries.

Reproduction commands (use the installed SDK's executable or `dotnet` on PATH):

```sh
dotnet build Marrow.sln -m:1 -p:EnableWindowsTargeting=true -p:TreatWarningsAsErrors=true
MARROW_PORT_DATA_DIR=/tmp/marrow-wl-fix-ui dotnet test tests/Marrow.UI.Tests/Marrow.UI.Tests.csproj --no-build -p:EnableWindowsTargeting=true -p:TreatWarningsAsErrors=true --filter '(FullyQualifiedName~Theme.Wallpapers.|FullyQualifiedName~Traceability)&FullyQualifiedName!~Native'
MARROW_PORT_DATA_DIR=/tmp/marrow-wl-fix-app WALLPAPER_METRICS_PATH=/tmp/marrow-pixels.jsonl dotnet test tests/Marrow.App.Tests/Marrow.App.Tests.csproj --no-build -p:EnableWindowsTargeting=true -p:TreatWarningsAsErrors=true --filter '(FullyQualifiedName~Marrow.App.Tests.Wallpapers.|FullyQualifiedName~Traceability)&FullyQualifiedName!~Native'
node tools/wallpaper-fidelity/check.mjs
python tools/wallpaper-fidelity/emulate-branches.py --probes porting/reports/wl-fix/branch-probes-before.zip --pixel-scan porting/reports/wl-fix/failing-pixels-before.jsonl
python tools/wallpaper-fidelity/emulate-branches.py --probes porting/reports/wl-fix/branch-probes-after.zip --fixed
```

The post-fix UI wallpaper engine/Traceability slice passed **1239/1239** with
zero failures and skips, including the six new branch regressions.

Final production revision: `24706fa725bf8bd198265c4426ba7f229d9cbc12`.
The final evidence-only commit is reported with the push; it does not change
the tested production source.

| Final scope | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Full fidelity frame matrix | 5148 | 0 | 0 |
| App wallpaper fidelity/guards + Traceability | 339 | 0 | 0 |
| UI wallpaper engine + Traceability | 1239 | 0 | 0 |
| Tunnel frames (19 originally failed) | 22 | 0 | 0 |
| Stairwell frames (1 originally failed) | 22 | 0 | 0 |

The final matrix's largest maximum is **0.057506707267**, below the unchanged
**0.080** gate, and its largest mean is **0.001642663317**, below **0.010**.
All **5104** other-wallpaper per-frame measurements are exactly identical to
the baseline. Tunnel's final worst maximum is **0.010424813332**, and
Stairwell's is **0.010063156169**. Measurements, distributions, test counts,
environment and evidence hashes are in `results-final.json`; all individual
frames are in `pixels-after.jsonl`. Full runs: App **11m17s**, UI **4m26s**.

Commit sequence: pre-fix proof `f5ae4578`, proven production arithmetic
`24706fa7`, then final evidence/append-only ledgers. Only `codex/wl-fix` is
pushed, without force. The CPU emulation and all live before/after float grids
remain checked in, independently reproducible offline from the archives.
