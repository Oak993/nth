# WL-RECONCILE

Verdict: **NOT READY**. The reconciliation provides one engine and the review's unchanged tests, but the real static pixel gate and the broader inherited wallpaper slice still have failures. No tolerance was relaxed and no missing dependency or mismatch became a skip.

Branch: `codex/wl-reconcile`, based on WL-1 `51447ee9966c1016714e466d10353b92f2a59968`. The pushed head is reported with the final task response; the evidence files below belong to that commit.

## Implementation and provenance

| Input | Head | Retained contribution |
| --- | --- | --- |
| WL-1 | `51447ee9966c1016714e466d10353b92f2a59968` | Catalogue/resource embedding, custom-picture precedence, compositor ownership, phase/speed integral, cache, shader-switch/clear transitions |
| WL-3 | `f8fbb675d5ed13bbe9a8caacb1bd2da6d9bdb672` | Shared environment/interfaces, Dynamic/Persp/Focus fields, per-wallpaper choices, timer/accent wakes, screen.Bounds position seam, animated options and tested dark_warm falling-edge turn |
| WL-4 | `b95251ee86d7f818179a210172433e0593e1a424` | Faithful static colour/gamut, twelve uniforms, followers, static lighting/Perspective, Gaussian Focus/wash/grain |
| WL-5 | `d0d72fb1cd5e6db8dc2a10067ef245b03ea3f1b7` | Independent original-page SwiftShader oracle, public-input bridge, offline fixtures, perceptual metric and regression guards |
| Review | `4930bebd5c21cf45e0dda4117cd094d2656307a1` | Report, all REVIEW test sources and original retarget generator/fixture, byte-identical |

There is one public renderer, environment, selection/store, view/visual handler and wake source. The collection-specific uniform/follower/Focus calculations remain helpers in that engine. StaticWallpaperInputs and Select adapt the old call sites to the shared environment/store. Both collections write Dynamic/Persp/Focus fields to wallpaperLibraryChoices. Legacy static choices migrate without an active selection, every sibling is normalized before persistence, canonical history wins, and identity-mismatched history entries are ignored.

The locked static/animated snapshot has zero diff against approved `e5b455fe9`. OD-0110's approval is restored from that snapshot's owner-decision record without changing prior bytes. The single ED-397 block explains the provenance, corrections and remaining evidence boundary. The original 1,397,190 engineering-decision bytes remain an exact prefix (SHA256 `befaf1d939e6d75f2c4246c9f598e09c90c0b1ea1e4572442371b7e325859431`). STATE and owner_decisions retain their mixed line endings and original byte prefixes. See [byte-integrity.json](wl-reconcile/byte-integrity.json).

## Findings and gates

| Finding | Change / evidence |
| --- | --- |
| 1 | Shared WL-3 environment/choices/wakes with WL-4 static controller/Focus; no second writer/schema. Migration and shared-history tests cover both collections. |
| 2 | Interrupted static palette fades use .3 seconds and ease-out cubic; normal fades retain .55 seconds and smoothstep. Unchanged page-retarget REVIEW fixture. |
| 3 | Reapply the original static resolvePack ground projection after Dynamic in both dark collections; preserve WL-3's tested falling-edge turn. The locked snapshot stays unchanged. An explicit anchor-checked derived-page policy is hash-bound in the oracle. All-quarter-hour/all-packs/five-accent guards cover both collections. |
| 4 | Correct the stale static idle assertion: .16*ln(1/.002) = .99434 seconds; busy at 59/60, idle at 60/60. |
| 5 | Minute-resolution clock, Feature WL-4 on five static test classes, unchanged 90 RGB picture guards. |
| 6 | Static default and premium bindings open; static accent ID is bound as accent. Missing APIs/data fail, never skip. Real engine pixels now run; their remaining mismatches prevent acceptance. |
| 7 | Non-zero [0.65,-0.4] Perspective through the shared position seam, both collections/themes/phases. |
| 8 | Two accents (#005FB8/#D4501A), 18:00 and between-key 15:15 Dynamic, Focus .37 and authored .05/.1 defaults as well as 0/1. Exact input/coverage checks. |
| 9 | Full real-pixel measurements are recorded below and per frame. Matching-frame error distributions are separated from failures; .010 mean/.080 maximum remain unchanged. The gate cannot be accepted while any frame exceeds either bound. |
| 10 | Entire original full matrix restored and expanded: 5,148 cases, no size-based omissions. |

The explicit no-brown policy uses the existing resolvePack projection for L < .46 and hue strictly between 20 and 120: low chroma goes to 262 degrees, orange to 8+(h-20)*.2, olive to 172. It applies after Dynamic only when dark > .5. Light palettes stay authored. The independently executed page code receives the same narrow policy; references never come from engine output. Policy SHA256: `a1a91f4a795e13f9d85e99497f3ccc8363a3d17de6c31dd1f621132d16c28332`.

Real comparisons also exposed signed-square failures, noise-hash reduction order, approximate constant rotations, constant cell-scale evaluation in Stardust, and the approximate animated Focus pass. The engine now uses signed integer multiplication, native three-component dot with rotated operands to preserve the page's reduction order without exceeding Cloudsea's size limit, constant folding for rot/starN, and the exact original animated 24-tap Gaussian-disc/trilinear-mip pass. A float32 hash probe matched all 16,000 cells exactly. Signed-square/hash tests fail before their fixes. Gaussian shape and 15%-height sigma bounds remain intact.

Precision experiments using custom trigonometric polynomials, reciprocal refinement, equivalent coordinate order, six-sheet unrolling and a float render target did not resolve the remaining Tunnel gate; those changes were removed. Tunnel's hard sheet branch amplifies small backend distance differences into large pixel errors. Stairwell has one remaining Perspective frame above the maximum. The final per-case file is authoritative; these are unresolved engine fidelity failures, not accepted noise.

## Expectations and bounds

Only the review-authorized corrections/expansions changed expectations:

- Static idle: 40 frames becomes 60, with the source follower arithmetic and a 59-frame busy check.
- Brown defect count: [17 authored,59 accent] becomes [0,0], retaining all 5,400 probes under the explicit owner-rule correction.
- Static Dynamic is retained instead of discarded by the obsolete WL-3 assertion.
- Inventory grows from the intermediate 30 static/24 or 72 animated counts to approved 90/72; source-label, palette, option and all loop checks remain.
- The 25 MB compact fixture budget becomes 100 MB to retain full coverage. PNG bytes: 74,361,740; PNGs+manifest: 83,140,092; 4,428 unique PNGs; 1,980 static/3,168 animated cases.

All numeric palette tolerances, follower constants, loop/seam/channel/continuity limits, Gaussian/sigma checks, frame/cache/wake assertions and .010/.080 pixel limits remain. Every original unit test method remains except the two inventory method names updated for the approved expanded counts. REVIEW files remain byte-identical. Shared traceability recognizes class traits, Review/Platform locators, owner Decision IDs, and the existing plan/engineering-contract Spec locators; those locators never replace mandatory Req/Rule/Decision attribution. Existing Services REVIEW files are unchanged.

## Verification

SDK: .NET 10.0.401. Linux x64. Final solution build:

```sh
dotnet build Marrow.sln -p:EnableWindowsTargeting=true -p:TreatWarningsAsErrors=true
```

Result: **0 warnings, 0 errors**; [build-final.log](wl-reconcile/build-final.log).

Final slice commands use `MARROW_PORT_DATA_DIR` pointing at separate throwaway directories. No test uses the owner's data, a provider CLI or network. Paths/filters:

```sh
dotnet test tests/Marrow.UI.Tests/Marrow.UI.Tests.csproj --no-build -p:EnableWindowsTargeting=true -p:TreatWarningsAsErrors=true --filter '(FullyQualifiedName~Theme.Wallpapers.|FullyQualifiedName~Traceability)&FullyQualifiedName!~Native'
WALLPAPER_METRICS_PATH=.../pixels-final.jsonl dotnet test tests/Marrow.App.Tests/Marrow.App.Tests.csproj --no-build -p:EnableWindowsTargeting=true -p:TreatWarningsAsErrors=true --filter '(FullyQualifiedName~Marrow.App.Tests.Wallpapers.|FullyQualifiedName~Traceability)&FullyQualifiedName!~Native'
dotnet test tests/Marrow.Core.Tests/Marrow.Core.Tests.csproj --no-build -p:EnableWindowsTargeting=true -p:TreatWarningsAsErrors=true --filter 'FullyQualifiedName~Traceability&FullyQualifiedName!~Native'
dotnet test tests/Marrow.Services.Tests/Marrow.Services.Tests.csproj --no-build -p:EnableWindowsTargeting=true -p:TreatWarningsAsErrors=true --filter 'FullyQualifiedName~Traceability&FullyQualifiedName!~Native'
```

Final counts and metric distributions are recorded below.

The additional broad wallpaper-name slices (outside the WL-1..WL-5 folders), also ending !~Native, are **UI 36 passed/23 failed/0 skipped (59)** and **App 12 passed/23 failed/0 skipped (35)**. App reproduces the exact 12/23 result on the review baseline. These inherited custom-picture/settings tests call the Windows Imaging Component decoder, whose intentional non-Windows guard returns null; effects include picture-install failures and startup-render timeouts. They were not renamed, skipped or weakened. See ui-existing-wallpaper, app-existing-wallpaper and app-existing-review-baseline logs/TRX. These failures independently prevent an all-wallpaper-slices READY verdict on this host.

Oracle: generate, offline check and independent --verify completed for all 5,148 cases; --verify is byte-identical. Manifest SHA256 `fda5a5fb47627cdfc50e6b8d5e6a40d9bef657b3e7413986aa7c6184295b8f64`. See oracle-generation.log, oracle-check.log and oracle-verify.log. No validator uses port pixels to generate references.

Test-first evidence: review-ui-red-scoped is 90 passed/4 failed (94); review-app-red-scoped is 1 passed/6 failed (7). Separate signed-square, dot-order, migration and matrix-coverage red files precede their fixes. The first complete real run recorded 590 failed frames of 5,148 and 119 failed methods of 339; after the first arithmetic/Focus repairs the next complete run recorded 37 failed frames and five failed methods. Aborted intermediate runs remain labeled as iterations, not final passes.

Repository Gate B passes 8/8. Gate A cannot access the separate pinned Swift checkout at the configured Windows path (5 fail/7 pass); no full-port Gate A claim is made. check_protected reports the explicitly requested orchestrator-level changes (ED, approval/snapshot/report imports and offline-fixture csproj wiring), not a worker-clean result. No protection configuration changed. Native Windows GPU/runtime verification was not run. Only codex/wl-reconcile is authorized for push.

## Final results

| Slice | Passed | Failed | Skipped | Total |
| --- | ---: | ---: | ---: | ---: |
| UI wallpaper engine + UI Traceability | 1,233 | 0 | 0 | 1,233 |
| App wallpaper oracle + App Traceability | 336 | 3 | 0 | 339 |
| Core Traceability | 3 | 0 | 0 | 3 |
| Services Traceability | 3 | 0 | 0 | 3 |
| Inherited UI wallpaper-name slice | 36 | 23 | 0 | 59 |
| Inherited App wallpaper-name slice | 12 | 23 | 0 | 35 |
| **Unique aggregate** | **1,623** | **49** | **0** | **1,672** |

The four final Traceability slices independently pass **12/12**, after preserving the inherited Spec ED-326 test files and recognizing their existing engineering-contract locator grammar. Traceability tests already included in UI/App totals are not double-counted. Every unchanged wallpaper REVIEW test passes: **101/101, 0 failed, 0 skipped**; [review-final.json](wl-reconcile/review-final.json).

The three failing oracle methods are Tunnel's ordinary/premium theories and Stairwell's premium theory. Actual pixel comparisons: **5,128 passed, 20 failed, 0 render errors, 5,148 total**. Animated: all **3,168/3,168** pass. Static: **1,960/1,980** pass. Failures: **19 Tunnel frames and one Stairwell Perspective/light frame**. All means meet .010; the remaining maximum-pixel errors exceed .080. See [pixels-final.jsonl](wl-reconcile/pixels-final.jsonl) and [pixel-summary.json](wl-reconcile/pixel-summary.json).

| Matching-frame distribution (5,128 cases) | Mean OKLab delta E | Maximum pixel delta E |
| --- | ---: | ---: |
| Median | 0.000046563 | 0.003390852 |
| 95th percentile | 0.000763062 | 0.010497568 |
| 99th percentile | 0.001145348 | 0.027320060 |
| Largest matching case | 0.001642663 | 0.076245234 |

These are measured CPU/page differences including grain, dither, quantization, Focus and geometry. They are not a native-GPU calibration or justification to accept failing maxima. The full distribution, including failures and a separate matching sharp-frame subset, is in pixel-summary.json. Neither acceptance bound changed.

| Remaining case | Mean | Maximum | Worst coordinate | Reference PNG |
| --- | ---: | ---: | --- | --- |
| static/stairwell/perspective/Terracotta/light/t=0/clock=18/accent=#005FB8/focus=0/tilt=[0.65,-0.4] | 0.000079938 | 0.085660002 | (131,25) | `3b569de5a7392a48e262921362c00ed1485dcd5d862c38c3c613bf60541d19fb.png` |
| static/tunnel/dynamic-dusk/Coral Reef/dark/t=0/clock=18/accent=#005FB8/focus=0/tilt=[0,0] | 0.000087220 | 0.306187539 | (65,28) | `76e03c7c6cdb7c4af988be4c315bfaee28f1da9631c14269a31ed48ed12e83be.png` |
| static/tunnel/dynamic-dusk/Coral Reef/dark/t=0/clock=15.25/accent=#005FB8/focus=0/tilt=[0,0] | 0.000089761 | 0.300362514 | (65,28) | `bb8146af7cd971c2a02eab6569a2c8adb2648fa8a976116bb114f1cd0dd8820b.png` |
| static/tunnel/dynamic-dusk/Coral Reef/light/t=0/clock=18/accent=#005FB8/focus=0/tilt=[0,0] | 0.000126031 | 0.487809612 | (65,28) | `aa05b3f8a6f63d11c3ec696871f486c495508bdb83670da23e5f393faa6146c2.png` |
| static/tunnel/dynamic-dusk/Coral Reef/light/t=0/clock=15.25/accent=#005FB8/focus=0/tilt=[0,0] | 0.000123521 | 0.470803895 | (65,28) | `3acf29c5e28d49a9cdff226bfb074297ca882f4e3465d350edc6fc7dcc1da3df.png` |
| static/tunnel/match-accent/Match accent/dark/t=0/clock=18/accent=#005FB8/focus=0/tilt=[0,0] | 0.000085114 | 0.298150306 | (65,28) | `f16fae65f3b7247f2d3204a3fd5b4545f95bc6c66ec564c164e8d20ebd3c09fe.png` |
| static/tunnel/match-accent/Match accent/dark/t=0/clock=18/accent=#D4501A/focus=0/tilt=[0,0] | 0.000080558 | 0.290702876 | (65,28) | `429df5893f7b1534601bf94862bb0183cf30cf369e2f60bec82529a6f26f6829.png` |
| static/tunnel/match-accent/Match accent/light/t=0/clock=18/accent=#005FB8/focus=0/tilt=[0,0] | 0.000113915 | 0.450794154 | (65,28) | `b91a33d8f43f92d576694be4ec45ca0524dacb4ac70cec373110321f8346e887.png` |
| static/tunnel/match-accent/Match accent/light/t=0/clock=18/accent=#D4501A/focus=0/tilt=[0,0] | 0.000119120 | 0.454149115 | (65,28) | `7225467349c1c3fa267a9e235f5b54135c3bb9f493063f0c2bfef7b9927f24f5.png` |
| static/tunnel/perspective/Coral Reef/dark/t=0/clock=18/accent=#005FB8/focus=0/tilt=[0.65,-0.4] | 0.000088426 | 0.299458443 | (62,45) | `4ff65e3fea25f15ffd031cd29be6cee393481fa037cbc2d27ecbf194cf916a83.png` |
| static/tunnel/perspective/Coral Reef/light/t=0/clock=18/accent=#005FB8/focus=0/tilt=[0.65,-0.4] | 0.000120408 | 0.455018108 | (62,45) | `9a959b07635c938027ae474d79d966a97873aa006e86f5e014f066d7faffc024.png` |
| static/tunnel/intermediate-focus/Coral Reef/light/t=0/clock=18/accent=#005FB8/focus=0.37/tilt=[0,0] | 0.000120975 | 0.114879663 | (65,28) | `a0604a4500e0b66c138be29b66d3b6ab73cc5518984508c3b90675773308f13f.png` |
| static/tunnel/authored-focus/Coral Reef/dark/t=0/clock=18/accent=#005FB8/focus=0/tilt=[0,0] | 0.000084808 | 0.291368744 | (65,28) | `c479b4a80cf75a251b7c7a21cf7ddc39a807a3e4f654f717b409a152fd1718c9.png` |
| static/tunnel/authored-focus/Coral Reef/light/t=0/clock=18/accent=#005FB8/focus=0/tilt=[0,0] | 0.000113296 | 0.451468347 | (65,28) | `9f047d38c3e501e96bb0bff5ba30e5e72c768c3c067cf70809c5ce7b42554453.png` |
| static/tunnel/default/Coral Reef/dark/t=0/clock=18/accent=#005FB8/focus=0/tilt=[0,0] | 0.000084808 | 0.291368744 | (65,28) | `c479b4a80cf75a251b7c7a21cf7ddc39a807a3e4f654f717b409a152fd1718c9.png` |
| static/tunnel/default/Coral Reef/light/t=0/clock=18/accent=#005FB8/focus=0/tilt=[0,0] | 0.000113296 | 0.451468347 | (65,28) | `9f047d38c3e501e96bb0bff5ba30e5e72c768c3c067cf70809c5ce7b42554453.png` |
| static/tunnel/quiet/Pearl/dark/t=0/clock=18/accent=#005FB8/focus=0/tilt=[0,0] | 0.000089774 | 0.301220138 | (65,28) | `7ca23eccec92e822848cfcb9163f8d22269ee543e22e6326e649b459c7af083d.png` |
| static/tunnel/quiet/Pearl/light/t=0/clock=18/accent=#005FB8/focus=0/tilt=[0,0] | 0.000119649 | 0.455419541 | (65,28) | `7282e3647c93bfb1e6ab16427873ee4afdd76e01c34ef5f88b7122de65e549d4.png` |
| static/tunnel/rich/Coral Reef/dark/t=0/clock=18/accent=#005FB8/focus=0/tilt=[0,0] | 0.000084808 | 0.291368744 | (65,28) | `c479b4a80cf75a251b7c7a21cf7ddc39a807a3e4f654f717b409a152fd1718c9.png` |
| static/tunnel/rich/Coral Reef/light/t=0/clock=18/accent=#005FB8/focus=0/tilt=[0,0] | 0.000113296 | 0.451468347 | (65,28) | `9f047d38c3e501e96bb0bff5ba30e5e72c768c3c067cf70809c5ce7b42554453.png` |

**Acceptance blockers:** the 20 real static pixel mismatches require further faithful backend arithmetic work; the 46 inherited custom-picture/settings failures require their actual Windows decoder/runtime verification. This branch is pushed for lead review as **NOT READY**, not offered for merge. All bounds, every REVIEW artifact and the approved snapshot remain intact.
