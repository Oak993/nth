# RR-37 native DWM/Acrylic fade-and-scale probe

**Status: needs Windows native check.** This project was compiled on Linux. Its native renderer has not been run here; a successful capture is evidence to review, not a parity approval.

Run from an isolated checkout of `codex/design-gaps` on an interactive Windows 11 x64 desktop (.NET 10, Animation effects enabled, working area fitting 1100 × 760 DIPs):

```powershell
dotnet run --project porting/probes/design-gaps-native/DesignGapsNative.csproj -- --dark
dotnet run --project porting/probes/design-gaps-native/DesignGapsNative.csproj -- --light
```

Each process prints its fresh `%TEMP%\Marrow-design-gaps-<nonce>` artifact directory. The fixture binds application data, settings and credentials to scratch/in-memory stores; it does not launch the installed app, bootstrap providers, read real credentials, change monitor mode, or run headless rendering. It creates an owned stripe backdrop and the production Settings window. Close its windows by letting the probe finish. Keep the complete artifact directories and the command output for review.

It captures the composed screen through `BitBlt(CAPTUREBLT)` and calls `DwmFlush`, rather than treating an Avalonia framebuffer or `Window.Opacity` as DWM evidence. The held poses cover G + 0 … 260 ms and C + 0 … 160 ms in 1/60 s increments; `samples.json` records native blur/root opacity, root scale/centre and stripe contrast/luma. The same process then switches to the real Avalonia frame clock and captures 40 open frames and 12 close frames in real time; compression happens afterwards. A missing pinned WinUI graph or failure to hide after 160 ms fails the probe.

Review against the read-only mirror at `origin/claude/design-sources` commit `26f8fbe89ee8480c9b18b4afe16d6c57bccb3610`:

- `porting/first-release/design-src/work-design/settings-open-rise-2-locked.dc.html`
- `porting/first-release/design-src/work-design/settings-rise/SPEC.md`
- The owner's existing native/reference captures under `E:/Droppy/work/design/settings-rise/` (images are deliberately absent from the mirror).

Required native observations:

1. Before G, nothing of Settings appears. At G, Acrylic and app glass start at zero while captions are whole. Acrylic fades over 80 ms on `cubic-bezier(.2,.8,.2,1)`; it must not appear whole at G or show a tint over sharp stripes. Items rise 10 DIPs over 140 ms with 12 ms stagger, capped at index 10; whole open is 260 ms. No scale or position change on open.
2. Close uses `cubic-bezier(.4,0,1,1)` for 160 ms: one common native root fades 1 → 0 and scales 1 → .985 about the physical client centre. At C + 16.7 … 150 ms alpha is .980, .929, .854, .761, .652, .531, .398, .254, .099; at 160 ms the window is hidden. Inspect the ring, shadow and Acrylic edges as well as content: no stationary opaque native layer, double fade, overlap, jump or snap. Card 1 has the explicitly specified additional fade (effective squared alpha); rows stay settled.
3. Repeat at 100%, 125%, 150% and 200% DPI, on primary and secondary monitors, dark/light, active/inactive and repeated shows. Test a show during close, repeated close, and external hide; no delayed hide after cancellation and no remaining native scale/opacity. Run Reduce Motion separately: complete at G and immediate hide, no wave or scale.
4. Inspect the real-time images together with a live observation/frame-time recording on the owner's renderer. Held-pose images do not establish zero stutter. Record monitor/refresh/GPU/DPI and the frame-time evidence before merge.

Other Windows checks for T40:

```powershell
$env:MARROW_PORT_DATA_DIR = Join-Path $env:TEMP ('Marrow-T40-tests-' + [guid]::NewGuid().ToString('N'))
dotnet test tests/Marrow.Services.Tests --filter "FullyQualifiedName~WindowsGenerationTests|FullyQualifiedName~DesignGapsReplaceFilesystemTests|FullyQualifiedName~DesignGapsManualTests|FullyQualifiedName~W3_ManualPlacementTests|FullyQualifiedName~LibraryControllerManualTests"
dotnet test tests/Marrow.App.Tests --filter "FullyQualifiedName~GettingReady|FullyQualifiedName~SettingsRiseTests|FullyQualifiedName~DesignGaps"
node porting/tools/verify.mjs
```

The unchanged `SettingsRiseTests.Every_later_open_plays_the_same_rise_from_a_hidden_start` asserts immediate hide at line 406; it conflicts with the locked close and currently fails. Do not weaken it during this task. The lead must resolve that acceptance conflict before merge. A separate unchanged Getting Ready scripted-preparation test also fails on this Linux base; the baseline and final logs retain that failure.

Getting Ready still needs a native full-frame comparison against `decisions-2026-10-03/getting-ready/getting-ready.html` and `project/VariantI.dc.html`: exact strings, geometry, live pane blur/saturation/opacity, sheet lift/shadow and removal, all six progress states, normal/reduced motion, DPI/theme variants and frame-time evidence. Linux proves the CSS matrix, live child redraw, single opacity layer, pane-only scope and 21 hand-over samples; it does not prove native GPU cost or full-screen parity.

For RR-48, also exercise a large verified manual file concurrently with a real downloader on the same part-file target, Windows held handles, replacement cleanup and linked slow/removable drives while scrolling the import page and starting independent chats. No owner data should be used.
