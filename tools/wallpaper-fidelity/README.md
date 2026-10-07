# Wallpaper fidelity (WL-RECONCILE)

OD-0110 approves all 90 static and 72 animated wallpapers. This tool renders an independent WebGL2 oracle from the locked snapshot in `porting/first-release/design-src/wallpaper-library/`. Offline tests consume checked-in PNGs and JSON; they do not launch Chromium, regenerate references or use the network.

## Regeneration

```sh
npm ci --ignore-scripts --prefix tools/wallpaper-fidelity
WALLPAPER_CHROMIUM=/usr/bin/chromium node tools/wallpaper-fidelity/generate.mjs
node tools/wallpaper-fidelity/check.mjs
WALLPAPER_CHROMIUM=/usr/bin/chromium node tools/wallpaper-fidelity/generate.mjs --verify
```

Use the versions bound in the manifest. The generator requires ANGLE/SwiftShader, a fixed sRGB colour profile, device scale 1 and a 160 x 100 framebuffer. Browser requests are aborted. An inert local Kit shell supplies DOM/storage/theme plumbing and suspends timers. The generator assembles the hosts as the snapshot's Python builders do, including glass code and notices, then calls the hosts' public probes. Original-byte SHA-256 provenance uses the Python builders' universal-newline convention only when assembling text.

The locked snapshot is unchanged. `no-brown-policy.cjs` applies two narrow, anchor-checked colour adaptations to the derived page code, bound by its own manifest hash: the existing static `resolvePack` ground projection is reapplied after Dynamic in both collections, and WL-3's tested long-way `dark_warm` falling-edge interpolation is preserved for animated Match accent. This makes the owner's no-brown rule consistent without replacing any shader, rendering method or independent page colour calculation with port output. See the single `ED-397` decision.

Static shaders retain the approved JSON's `contract.head/lib/main` and wallpaper `src`. Animated `common.glsl` and matching standalone shader/catalogue twins are checked. Do not run the snapshot catalogue builders: their earlier inventory would overwrite the approved catalogue.

## Complete coverage

There is no size-based pruning. All looks run in both themes; animated looks run at both 0 and T/2.

| Look | Inputs |
| --- | --- |
| Default pack | Source option defaults, Focus 0 |
| Quiet pack | Source quiet tag, or lowest mean pack chroma for animated |
| Rich pack | Highest mean pack chroma for animated; source rich tag for static |
| Dynamic | 18:00 and 15:15, including interpolation between key looks |
| Match accent | `#005FB8` and warm `#D4501A` |
| Full Focus | 1 |
| Perspective | Non-zero tilt `[0.65, -0.4]` |
| Intermediate Focus | 0.37 |
| Authored Focus | Source defaults, including 0.05 and 0.1 |

The matrix contains 5,148 cases: 1,980 static and 3,168 animated, represented by 4,428 content-addressed PNGs. PNG bytes total 74,361,740; PNGs plus manifest total 83,140,092. The explicit fixture budget is 100,000,000 bytes. Both validators require the complete matrix and an empty omissions list. Grain/dither remain enabled at source defaults. PNGs are lossless RGB8 with top-left rows, without resizing, cropping or colour fitting. Every case records user inputs, resolved source uniforms, pixel/encoded hashes and source provenance.

## Real engine comparisons

```sh
dotnet test tests/Marrow.App.Tests/Marrow.App.Tests.csproj -p:EnableWindowsTargeting=true -p:TreatWarningsAsErrors=true --filter '(FullyQualifiedName~Marrow.App.Tests.Wallpapers.|FullyQualifiedName~Traceability)&FullyQualifiedName!~Native'
```

The bridge binds the shared public `WallpaperSelection` and `WallpaperEnvironment(TimeProvider, IWallpaperAccentSource, IWallpaperWindowPosition)` seam for both collections. Static Match accent's public ID is `accent`; animated's is `match`. Fixed clock/accent/position inputs enter the real renderer, which settles through its public follower API and draws with `DrawFrameAt`. The bridge never assigns oracle palettes or reads private shader state. Missing public APIs/data fail instead of skipping. Each theory collects every failed frame for its wallpaper.

## Acceptance and evidence

Decode sRGB bytes to linear RGB, convert with Ottosson's OKLab matrices, and measure Euclidean distance. Every opaque, identically sized frame must meet the existing WL-5 bounds: **mean delta E <= 0.010 and maximum pixel delta E <= 0.080**. Neither bound was relaxed. Synthetic tests reject both a wrong pixel and a broad three-level colour shift. `--verify` independently rerenders every reference and requires byte-identical PNG and manifest hashes; `check.mjs` checks fixtures offline and detects source drift.

Set `WALLPAPER_METRICS_PATH` to collect per-frame JSONL evidence, including mean, maximum, worst coordinate, reference hash and outcome. Measured CPU Skia/SwiftShader distributions and integration results are recorded in `porting/reports/wl-reconcile.md`. The full page matrix exposed signed-power, noise-hash reduction-order and animated Focus differences; the reconciled engine corrects those differences. This is Linux raster evidence. Native Windows GPU fidelity remains outside the requested `!~Native` slice.
