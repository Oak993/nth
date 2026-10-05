# Aurora browser Canvas references

These 210 PNGs execute the unmodified locked `iconkit.js` and `D-aurora.js`
in local headless Google Chrome 154.0.8037.93. Each frame gets a fresh document,
a canvas of exactly the output dimensions, and DPR 1. They are direct canvas
exports, with no screenshot scaling, resampling, cropping, or C# rendering.

`inputs.json` preserves the exact frame schedule extracted from
`tests/fixtures/aurora/manifest.json` at cloud commit
`20e15c8ffdd838665ed120f66ea2bc49928a8361`, including that manifest's SHA-256.
Every size/theme in the cloud manifest was checked to have that same schedule.
`manifest.json` records browser name/version, locked source and tooling hashes,
the input provenance, browser flags, exact time arguments, dimensions, and every
PNG's SHA-256. `file` paths are relative to this directory, matching the cloud
fixture naming scheme.

Coverage: 16, 24, 32, 48, 64, 256, 512 px; dark and light; one still, seven launch
frames, and seven idle frames each. Launch milliseconds are
0/120/300/540/780/1020/1200, passed as normalized progress
0/.1/.25/.45/.65/.85/1. Idle milliseconds are
0/900/1800/3600/5400/6300/7200, passed as seconds
0/.9/1.8/3.6/5.4/6.3/7.2. Still uses `{launch:1,idle:0,still:true}`;
animation frames use `still:false`.

From the repository root on Windows:

```sh
node porting/first-release/design-src/app-icon/render/render-aurora-chrome.mjs
node porting/first-release/design-src/app-icon/render/render-aurora-chrome.mjs --check
```

`--check` regenerates every frame and byte-compares PNGs and manifest without
rewriting them. Both commands also require all 14 stills to match the accepted
T8 browser references byte for byte. `AURORA_BROWSER` selects a local Chrome/Edge
executable; `AURORA_SCRATCH` selects disposable profile storage (default
`E:/Droppy/work/scratch/codex-icon-aurora`). A different browser/version is a new
reference baseline and will fail manifest comparison with `--check`.

The page loads through `file://` only, with CSP denying network resources. There
is no server, remote debugging socket, dependency installation, or network
download. Browser background networking is disabled and DNS maps all hosts to
not found. The temporary profile has no signed-in browser state.

This delivery supplies the browser oracle. Comparing or repairing the cloud
SkiaSharp renderer against it belongs to AURORA-RENDER; no C# code changes here.
