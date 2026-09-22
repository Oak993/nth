# P2 result: PASS (on the AMD Radeon integrated GPU and on the RTX 4080), with large headroom

Task packet T-M0-001. Runs 2026-09-22 20:15:50Z to 20:20:19Z, by a worker subagent on the owner's real, unlocked desktop
(owner decision OD-0007). `query user`: `omera console 3 Active`. Every run's screen-live check found the probe's own
marker in a DWM-composed capture of its own window, so the screen was composited throughout.

> P2 - Snapshot budget at production scale. Instrument P1's scene scaled to the real count: 22 glass sites grouped into 3
> shared-snapshot groups (GlassEffectContainer spacings 2, 2, 8), window 1320x860, over a transcript streaming at the
> source's 45 ms flush cadence with .interactive() pointer tracking live. PASS: median frame time <= 16.7 ms and 99th
> percentile <= 33 ms on this AMD integrated GPU, measured by an in-app frame timer with GPU time for the snapshot pass
> reported separately and staying under about 3 ms. FAIL: glass over the live transcript must be cached and invalidated on
> content change - record the fidelity cost before committing.

## Verdict

| Condition (pre-registered in the header of `Program.cs`) | AMD Radeon iGPU, run 1 / run 2 / run 9 | Threshold | Met |
| --- | --- | --- | --- |
| Median frame time (render-op interval) | 4.83 / 4.84 / 6.01 ms | <= 16.7 ms | yes |
| 99th percentile frame time | 9.52 / 8.94 / 9.75 ms | <= 33.0 ms | yes |
| Snapshot pass GPU time, p99 (GL timer queries) | 0.58 / 0.58 / 0.57 ms (max 0.63) | <= 3.0 ms | yes |
| Rendering on a hardware GPU | GrContext non-null, backend OpenGL (ANGLE over D3D11), `GL_RENDERER='ANGLE (AMD, AMD Radeon(TM) Graphics (0x000013C0) Direct3D11 vs_5_0 ps_5_0, D3D11-32.0.21036.18)'`; OS counter: 3D engine of LUID 0x17631 (the AMD adapter) at 31.0% for this pid, no other adapter | precondition | yes |
| 45 ms flushes live | 267 flushes, interval median 45.00 ms, p99 45.27 ms, row < 4 KB throughout (max 3835 chars) | median 44..50 ms | yes |
| .interactive() pointer tracking live | 1374 SendInput moves, 1374 `PointerMoved` events (518 in the 44 px title-bar band), 0 misses, 0 interference | > 100 events, < 5% misses | yes |
| Screen live | our magenta and green markers found (80 px each), 496 distinct colours | precondition | yes |
| GPU timer valid | 100% of frames returned results, 0 disjoint, 0 dropped | >= 90% | yes |

**P2: PASS.** Exit code 0 in runs 1, 2 and 9 (run 9 on the final binary). The packet's FAIL branch (cache the glass and
invalidate on content change) is not needed.

All 22 sites cost the AMD integrated GPU about 1.4 ms per frame in total (content 0.30 + snapshot 0.54 + lens 0.56, medians).
That is 8% of a 60 Hz frame. On the RTX 4080, which Avalonia picks by default, it is 0.11 ms.

## Numbers, every run

Frame time is the interval between consecutive executions of the scene's `ICustomDrawOperation.Render` on Avalonia's
render thread, with the scene invalidated on every UI animation frame, so a frame is produced at every vsync the pipeline
can keep up with. The display runs at 239 Hz, so the floor is 4.17 ms. GPU times are `GL_TIME_ELAPSED_EXT` queries around
each pass, with a Skia flush at each query edge. All figures come from 12.0 s of measurement after a 2.9 s warm-up.

| Run | Adapter | Config | Snapshots / lens draws | Frames | Frame median / p95 / p99 / max (ms) | CPU op median / p99 | GPU content / **snapshot** / lens, median (ms) | **Snapshot p99 / max** | Exit |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| run1_amd_packet | AMD | packet | 3 / 22 | 1959 | 4.83 / 8.42 / 9.52 / 34.23 | 0.72 / 1.30 | 0.30 / **0.54** / 0.56 | **0.58 / 0.63** | 0 PASS |
| run2_amd_packet | AMD | packet | 3 / 22 | 1997 | 4.84 / 8.40 / 8.94 / 31.32 | 0.70 / 1.16 | 0.30 / **0.54** / 0.56 | **0.58 / 0.61** | 0 PASS |
| run9_amd_packet_final | AMD | packet | 3 / 22 | 1934 | 6.01 / 9.36 / 9.75 / 34.86 | 0.70 / 1.14 | 0.30 / **0.54** / 0.56 | **0.57 / 0.60** | 0 PASS |
| run3_amd_source | AMD | source | 11 / 22 | 1904 | 6.30 / 8.56 / 9.76 / 25.38 | 0.84 / 1.39 | 0.30 / 0.45 / 0.55 | 1.27 / 1.30 | 0 PASS |
| run4_amd_persite | AMD | persite | 22 / 22 | 1634 | 8.08 / 9.70 / 10.19 / 31.12 | 1.00 / 1.61 | 0.30 / 0.47 / 0.55 | 1.49 / 1.54 | 0 PASS |
| run5_amd_noglass | AMD | noglass | 0 / 0 | 2842 | 4.17 / 4.60 / 7.42 / 10.36 | 0.36 / 0.65 | 0.30 / - / - | - | 0 baseline |
| run8_amd_packet_notimer | AMD | packet, no queries | 3 / 22 | 2013 | 5.39 / 8.42 / 9.64 / 32.20 | 0.37 / 0.75 | (not measured) | - | 0 characterisation |
| run6_default_packet | RTX 4080 (Avalonia default) | packet | 3 / 22 | 2856 | 4.17 / 4.43 / 4.61 / 30.21 | 0.68 / 1.17 | 0.02 / **0.06** / 0.02 | **0.82 / 26.52** | 0 PASS |
| run7_default_persite | RTX 4080 | persite | 22 / 22 | 2857 | 4.17 / 4.52 / 4.80 / 28.95 | 0.91 / 1.38 | 0.02 / 0.16 / 0.06 | 0.84 / 23.78 | 0 PASS |

Configurations: `packet` is the packet taken literally, all 22 sites in 3 shared-snapshot groups. `source` is the grouping
the source itself has: 14 sites in the 3 containers, and 8 standalone sites with a snapshot each. `persite` gives every site
its own snapshot, which is the worst case. `noglass` is the same scene with no glass. The snapshot area in `packet` is 1.63
Mpx, 1.44 times the window's 1.14 Mpx, because the groups overlap.

Frames over 33 ms: 1 in run 1, 0 in run 2, 1 in run 9. They are 1 in about 2000, so they do not reach the p99. The long frames
on the RTX 4080 (max 26 to 30 ms) come with a snapshot GPU max of 23.8 to 26.5 ms. That points to one-off GPU work inside
the snapshot pass, probably a glyph-atlas upload, since no pass behaves like that at p99. This is an inference; it was not
isolated.

OS cross-check of the GPU timer, from `Get-Counter '\GPU Engine(pid_<pid>_*engtype_3D)\Utilization Percentage'`, 8 samples
at 1 s (`evidence/*/gpu_engine_counters.txt`):

| Run | Adapter LUID the process used | 3D utilisation mean | What the in-app timer predicts |
| --- | --- | --- | --- |
| run1_amd_packet | 0x00017631 (AMD, per Avalonia's adapter list) only | 31.0% | 1.39 ms x 163 fps = 23% for the probe's passes. The remainder is Avalonia's own composition and present work outside the queries |
| ctl2_amd_gpuheavy | 0x00017631 only | 87.7% | 35.4 ms x 24.5 fps = 87% |
| run6_default_packet | 0x00016317 (RTX 4080) only | 2.6% | 0.11 ms x 238 fps = 2.6% |
| ctl3_software | none (no 3D engine instance for the pid) | - | CPU raster, no GrContext |

## The capability, the controls, and what each control proves

Capability under test: 22 glass sites, each a two-pass P1 glass (an app-owned snapshot of the live content, then P1's SKSL
lens sampling it), rendered every frame over a streaming transcript with live pointer response. The question is whether
this fits the frame budget on the AMD integrated GPU, with the snapshot pass under about 3 ms of GPU time.

| Control | What it removes or forces | Must | Measured | Flagged |
| --- | --- | --- | --- | --- |
| `ctl1_amd_stall` (`--ctl stall`) | `Thread.Sleep(30)` inside the render op: a frame that cannot meet the budget | the frame timer reports median > 16.7 | median 37.53, p99 41.19, all 318 frames > 33 ms | **yes** |
| `ctl2_amd_gpuheavy` (`--ctl gpuheavy --heavy 2`) | a GPU-bound shader (256-iteration trig loop, alpha <= 1/255) drawn twice over every snapshot surface, inside the snapshot pass | the GPU timer reports snapshot p99 > 3.0 ms while CPU time stays small | snapshot GPU **34.55** median / 34.57 p99; CPU op median **0.81** ms (unchanged from 0.72); frame median 40.92; OS counter 87.7% on the AMD LUID | **yes** |
| `ctl4_default_gpuheavy` (`--heavy 12`, RTX 4080) | the same on the other adapter | same | snapshot GPU 3.63 median / 5.31 p99; CPU 0.78 ms | **yes** |
| `ctl3_software` (`--render software`) | the GPU: Avalonia's CPU raster backend | the GPU-path check says False | GrContext null, GL_RENDERER empty, no 3D engine counter for the pid; frame median **99.63** ms, p99 121.33 | **yes** |
| `run5_amd_noglass` | the glass | a baseline below the subject | frame median 4.17 (vsync), CPU 0.36 ms, GPU content 0.30 ms | reference |
| `run8_amd_packet_notimer` | the timer's own flushes | frame time not inflated by the instrumentation | median 5.39 (subject runs: 4.83 to 6.01); CPU op 0.37 instead of 0.70 | no inflation beyond run-to-run spread |

The gpuheavy control is the decisive one for the "GPU time reported separately" clause. The GPU timer moved by 34 ms while
the CPU time of the same operation did not move, and the OS counter agreed to within one percentage point. So the timer
measures GPU execution of the pass, not CPU submission. The stall control shows that the frame timer sees slow frames. The
software control shows that the GPU-path detector can say no.

## The scene (what was rendered)

- Window 1320 x 860 client, 96 DPI, `WindowDecorations.Full`, `ExtendClientAreaToDecorationsHint`, title bar height hint
  44, `AcrylicBlur` (actual `AcrylicBlur`, `IsExtendedIntoWindowDecorations=True`). This is the P3/P4/P7 chrome
  configuration. The window is topmost, not shown in the taskbar, and not activated on show.
- Content: the source's dark scrim (black 0.26, Chrome.swift:212) plus a 0.12 theme tint (the P3/P4 recipe), a static diff
  column, and a transcript of prose, header and code rows as cached `SKTextBlob`s. The transcript auto-follows each new row
  with a 60 ms scroll decay, so the content under the glass moves on most frames.
- Streaming: deltas are flushed onto the row every 45 ms from a timer thread, via the UI dispatcher. Each message is capped
  below 4 KB, so the source's 45 ms tier applies throughout.
- 22 glass sites, laid out as in `run_log.txt` (the `site` lines give each rect, radius, tint, interactive flag, container
  and source line). A: the 8 chat-row capsules and circles. B: the 2 diff-inspector header capsules. C: the composer surface,
  the changes tab, the approval card and the effort knob. S: the minimap card, the floating sidebar, the usage panel, the
  hydra panel, the subagent panel, the hydra progress bar, the command palette and the follow-up tab. 12 sites are
  interactive.
- Glass: P1's SKSL unchanged (squircle SDF, lens band, chromatic offsets 1.10/1.00/0.90, tint mix, specular rim), plus a
  pointer-following highlight scaled by a per-site hover amount (80 ms response) and a 4% hover swell. Snapshot surfaces are
  GPU `SKSurface`s created once per group on the lease's `GRContext` and redrawn every frame.
- Pointer: a thread sends absolute `SendInput` moves every 8 ms along a loop over the chat row, the diff header, the changes
  tab and the effort knob. Before every move, `WindowFromPoint` must resolve to our root HWND.

## Source verification of the packet's numbers

| Packet number | Pinned source (1.6.2, `E:\Droppy\reference\droppy-code-v1.6.2\DroppyCode\...`) | Status |
| --- | --- | --- |
| window 1320x860 | `App/Window/WindowManager.swift:19` `static let main = NSSize(width: 1320, height: 860)` (min 860x560 at :20) | SOURCE FACT, matches |
| 45 ms flush cadence | `App/Runtime/ThreadRuntime.swift:3124-3131` `flushDelay`: 45 ms up to 4,096 chars, 90 ms up to 32,768, 150 ms beyond; `:3135` at least 160 ms while the reader scrolls; `:3138` `flushWindow: Duration = .milliseconds(45)`; `:3170-3172` hydra heads wait `max(45, streamingCount * 70)` | SOURCE FACT, matches for rows under 4 KB and no scrolling. The probe held rows under 4 KB. The 90/150/160 ms tiers are slower and so cheaper |
| 3 containers, spacings 2, 2, 8 | `App/Views/Chat/ChatView.swift:1123` `GlassEffectContainer(spacing: 2)` (the chat chrome row); `App/Views/Diff/DiffInspector.swift:25` `GlassEffectContainer(spacing: 2)`; `App/Views/Composer/ComposerView.swift:23` `GlassEffectContainer(spacing: 8)`. There are exactly 3 `GlassEffectContainer(` lines in the tree | SOURCE FACT, matches |
| 22 glass sites | No single source constant. `.glassEffect(` appears on **19** lines and `GlassEffectContainer(` on **3**, and 19 + 3 = 22 grep lines. The 19: Chrome.swift:180, :182, :245, :254; CommandPalette.swift:97; FloatingSidebarPanel.swift:90; ThreadChangesTab.swift:38; UsageFloatingPanel.swift:59; EffortSlider.swift:788; FollowUpQueueTab.swift:154; TourView.swift:82; HydraProgressBar.swift:576; WallpaperCard.swift:42; ShortcutsSettingsPage.swift:477; ComposerView.swift:1304; RequestCards.swift:64; HydraPanel.swift:105; SubagentPanel.swift:339; TimelineMinimap.swift:338. Chrome.swift:180/182 are two branches of one modifier (`ChromeGlassSurface`), and :245/:254 are two branches of `WindowBackdrop`. `ChromeGlassSurface` is instantiated once per chrome capsule or circle, so at run time the chat row alone draws up to 8 glass shapes. A further 62 lines use `.buttonStyle(.glass` / `.glassProminent`. The same 19 / 3 / 62 counts hold in the B1 tree | **UNRESOLVED as a source count.** "22" matches the grep-line count, not the number of glass surfaces on screen. The probe draws 22 simultaneous glass surfaces, more than the app shows at once (see deviations) |
| .interactive() | Chrome.swift:180, ThreadChangesTab.swift:38, EffortSlider.swift:788, ShortcutsSettingsPage.swift:477 | SOURCE FACT. The first three are in the scene |
| Site shapes | radii: composer 22 (ComposerView.swift:1298), request card 20 with `Chrome.warning.opacity(0.1)` (RequestCards.swift:64), tabs 12 top corners (ThreadChangesTab.swift:15, FollowUpQueueTab.swift:47), panels 22 (HydraPanel.swift:33, SubagentPanel.swift:265, used by FloatingSidebarPanel.swift:35 and UsageFloatingPanel.swift:25), palette 24 (CommandPalette.swift:97), cards 16 (Chrome.swift:58, TimelineMinimap.swift:338, HydraProgressBar.swift:572), capsule height 32 (Chrome.swift:33-38), chrome padding 14 / 12 (Chrome.swift:31-32), row spacing 10 / 8 (ChatView.swift:1124, :1152), composer max width 820 (ComposerView.swift:52), knob tint white 0.32 dark (EffortSlider.swift:788), chrome tint `glassTint.opacity(0.3)` (Chrome.swift:180), minimap card width 300 (TimelineMinimap.swift:356), sidebar 300x560 (FloatingSidebarPanel.swift:17) | SOURCE FACT for these values. Positions and the sizes the source takes from content are ENGINEERING |

## GPU in use, and whether rendering ran on the GPU

- GPUs present (`Get-CimInstance Win32_VideoController`): NVIDIA GeForce RTX 4080 (driver 32.0.16.1692; drives the only
  display, 2560 x 1440 at 239 Hz), AMD Radeon(TM) Graphics (the Ryzen 7 9800X3D's integrated GPU, driver 32.0.21036.18; WMI reports no
  active display mode for it), Parsec Virtual Display Adapter (0.45.0.0).
- Avalonia's adapter list, from `Win32PlatformOptions.GraphicsAdapterSelectionCallback`:
  `[0] nvidia geforce rtx 4080 luid_0x00000000_0x00016317 | [1] amd radeon(tm) graphics luid_0x00000000_0x00017631 |
  [2] nvidia geforce rtx 4080 luid_0x00000000_0x0002405A | [3] microsoft basic render driver luid_0x00000000_0x000175E0`.
- **Avalonia's default, with no callback installed, is the RTX 4080** (runs 6 and 7:
  `GL_RENDERER='ANGLE (NVIDIA, NVIDIA GeForce RTX 4080 (0x00002704) Direct3D11 vs_5_0 ps_5_0, D3D11-32.0.16.1692)'`).
  The packet's "this AMD integrated GPU" is therefore not what the app would use on this PC by default. The verdict runs
  forced the AMD iGPU in-process through the callback (index 1). `GL_RENDERER` then named AMD, and the OS counter showed 3D
  work on the AMD LUID only.
- GPU, not CPU raster, in every non-control run: `lease.GrContext` non-null in 100% of measured frames (`frames.All(f =>
  f.Gpu)`), backend `OpenGL` (ANGLE ES 3.0 over Direct3D 11, `ANGLE 2.1.1 git hash: 1c89805903c1`), and a hardware
  `GL_RENDERER`, not SwiftShader, WARP or the Basic Render Driver. The OS counters agree. The CPU raster path was exercised
  only by `ctl3_software`, and it costs 99.6 ms per frame.
- No system or per-app GPU preference was written. The adapter was chosen inside the probe process only.

## Design consequence

1. **The P1 glass design stands at production scale on both GPUs. No caching or invalidate-on-change layer is needed.**
   At 22 sites the whole glass cost on the integrated GPU is about 1.1 ms of GPU time per frame (snapshot 0.54 + lens 0.56).
   The frame stays at the display rate or one vsync behind it, far inside 16.7 ms.
2. **GlassEffectContainer grouping is not needed for performance.** Giving every site its own snapshot costs a snapshot p99
   of 1.49 ms on the iGPU instead of 0.58, still under half the 3 ms budget. The containers can be implemented for what they
   do visually (merging shapes within `spacing`). They do not need to be a performance mechanism.
3. **GPU rendering must be treated as required.** Avalonia's software fallback runs this scene at about 10 fps. ENGINEERING
   DECISION candidate: at startup, detect `GrContext == null` and degrade the glass (for example, tint only), with the
   degradation named, instead of silently rendering 22 CPU lenses.
4. **Leave Avalonia's adapter choice at its default.** Rendering on the adapter that drives the display (the RTX 4080 here)
   holds vsync (4.17 ms median). Forcing the iGPU that drives no display added cross-adapter presentation: the median moved
   to 4.8 to 8.1 ms even though GPU time was only about 1.4 ms. On a laptop whose iGPU drives the panel, the iGPU is the
   display adapter, and these AMD figures are the relevant budget.
5. **What this does NOT establish, and remains a risk:** the snapshot pass here re-draws the probe's own cached Skia text
   blobs (P1's method). In production the content under the glass is Avalonia controls: text layout, a virtualised list and
   images. Re-rendering an Avalonia visual subtree into an app-owned surface, 3 to 22 times per frame, was not measured, and
   neither was the cheaper alternative of copying the already-composed pixels (`lease.SkSurface.Snapshot(rect)`). A
   follow-up measurement belongs with the first real transcript control. Also not established: DPI other than 100%, the
   light theme, a 60 Hz display, 4K windows, user scrolling (the source's 160 ms tier), the container merge effect, and
   glass over glass (every site here samples the content, never another site).

## Owner decision needed

None for P2. One fact for the orchestrator's records: on this PC the display, and Avalonia's default renderer, is the RTX
4080, not the AMD iGPU the packet names. P2 passed on both adapters, so nothing waits on it.

## Deviations from the packet and from P1, with reasons

1. **"22 glass sites" is not a source count** (see the verification table). The probe uses 22 simultaneous surfaces. That
   is more glass than the app shows at once: the palette, four floating panels and both composer tabs are on screen
   together, and the source's `TabSlot` shows one tab at a time (ComposerView.swift:60-75). So the measurement is
   conservative.
2. **Two groupings besides the literal one.** The packet puts all 22 sites in 3 groups. The source's containers hold 14 of
   them, and the other 8 take their own pass. Both were run, and so was the one-snapshot-per-site worst case. The verdict
   rests on the literal (`packet`) configuration. All three pass.
3. **Container merging is not implemented.** SwiftUI morphs shapes that are closer than `spacing` (the changes tab
   overlapping the composer would merge). The probe draws each site separately. The cost of a smooth-union SDF across
   neighbours was not measured.
4. **.interactive() is approximated** with a pointer-following highlight and a 4% hover swell, driven by real
   `PointerMoved` events. No button was pressed, so the press response did not run. SwiftUI's exact response is not
   derivable from source (OD-0001 / UNK-0003).
5. **Snapshot surfaces are reused across frames.** P1 created one per render. A production compositor would reuse them. It
   is the same app-owned `SKSurface` on the same `GRContext`, redrawn every frame.
6. **Frame timer definition.** The in-app timer is the interval between successive render-thread executions of the scene
   under continuous invalidation. Per-pass GPU times come from `EXT_disjoint_timer_query`, which ANGLE exposes. Each query
   edge adds a Skia flush, 7 per frame in total. `run8` without queries shows no frame-time inflation beyond run-to-run
   spread.
7. **Two-adapter machine.** The packet assumed an AMD integrated GPU. The AMD iGPU was selected in-process through
   `GraphicsAdapterSelectionCallback` for the verdict runs. The RTX 4080 was measured as Avalonia's default.
8. **Probe bug fixed mid-session, run discarded:** the first `noglass` run (20:17:02Z, exit 2 INCONCLUSIVE) left a GL
   query begun and never ended (a leftover `Mark(true, 1)` in the no-glass branch), so later queries failed (timer coverage
   0.02). The branch was removed, `noglass` was re-run (run5), and its evidence folder was replaced. Runs 1 to 4 were built
   before the fix, but the removed line was in the no-glass branch only. Run 9 repeats the verdict configuration on the
   final binary, with the same result.
9. **Two smoke runs before the numbered runs** (20:14:28Z, default adapter; 20:15:02Z, AMD; 3 s each). The first showed
   `PointerMoved events=0`: the custom draw operation returned `HitTest = false`, so nothing in the window took the pointer.
   `HitTest` now returns true over the scene's bounds, as app content does. The second passed. The smoke output was deleted.
10. **Build.** Console `Exe`, Release, P10's central-package-management pattern (no Version attributes), plus
    `Avalonia.Desktop`, as in P4. `--api` dumps the Avalonia API surface used for adapter selection and the GL lease
    (`evidence/api_dump.txt`).

## Environment

- `cmd /c ver`: `Microsoft Windows [Version 10.0.26200.9457]`
- `dotnet --version`: `10.0.401` (`dotnet --list-sdks`: `10.0.401 [C:\Program Files\dotnet\sdk]`)
- Avalonia `12.1.2+d3c867a9e2de379249b03dbeb3495bd7f076a81a`; SkiaSharp `3.119.4+release-3.119.4.f568ac94...`;
  ANGLE `OpenGL ES 3.0 (ANGLE 2.1.1 git hash: 1c89805903c1)` (printed by the probe)
- CPU AMD Ryzen 7 9800X3D. GPUs as listed above. Display 2560 x 1440 at 239 Hz on the RTX 4080, primary work area
  2560 x 1392, DPI 96 for the probe window.
- Session `omera console 3 Active` throughout. `MARROW_PORT_DATA_DIR` was a fresh `%TEMP%\marrow-p2-<run>-<n>` folder for
  every run, deleted after each run.
- Before every run, no `p2`/`p3`/`p4`/`p7` process was running (checked by `run_probe.ps1`; at 20:06:50Z also by `tasklist`).

## Commands and their decisive output

```
# pre-flight (20:06:50Z)
PS> Get-Process | ? { $_.ProcessName -match '^(p2|p3|p4|p7|p\d+)$' }; tasklist /FI "IMAGENAME eq p2.exe" (p3, p4, p7 likewise)
    (none) / INFO: No tasks are running which match the specified criteria.   (x4)
PS> query user            -> omera  console  3  Active
PS> cmd /c ver; dotnet --version  -> Microsoft Windows [Version 10.0.26200.9457]; 10.0.401
PS> Get-CimInstance Win32_VideoController   -> RTX 4080 2560x1440@239; AMD Radeon(TM) Graphics; Parsec Virtual Display Adapter

# source verification
PS> Select-String -Pattern '\.glassEffect\('          (every *.swift in both pinned trees)  -> 19 lines each
PS> Select-String -Pattern 'GlassEffectContainer\('   -> 3 lines each (ChatView.swift:1123, DiffInspector.swift:25, ComposerView.swift:23)
PS> Select-String -Pattern 'buttonStyle\(\.glass'     -> 62 lines each
PS> Select-String -Pattern '1320|0\.045|flushDelay|flushWindow' -> WindowManager.swift:19; ThreadRuntime.swift:3092,3124,3126,3138,3170

# build
PS> dotnet build porting\probes\p2\p2.csproj -c Release -nologo
    first attempts: CS8602 (nullable Description in the adapter callback), fixed with `?? ""`. Then: Build succeeded. 0 Warning(s) 0 Error(s)

# runs (porting\probes\p2\run_probe.ps1 -Name <run> -ProbeArgs "<args>" [-Counters]; each run ~16 s)
run1_amd_packet        --config packet --adapter amd -Counters   20:15:50Z  exit 0  RESULT PASS   (lines quoted in the verdict table)
   GPU    pass1 SNAPSHOT:     n=1960 median=0.54 p95=0.57 p99=0.58 max=0.63
   FRAMES render-op interval (frame time): n=1959 median=4.83 p95=8.42 p99=9.52 max=34.23
   counters: luid_0x00000000_0x00017631 eng_0  samples=8 mean=31.0% max=31.9%
run2_amd_packet        --config packet --adapter amd             20:16:14Z  exit 0  RESULT PASS   median 4.84 p99 8.94; snapshot p99 0.58
run3_amd_source        --config source --adapter amd             20:16:30Z  exit 0  RESULT PASS   median 6.30 p99 9.76; snapshot p99 1.27
run4_amd_persite       --config persite --adapter amd            20:16:46Z  exit 0  RESULT PASS   median 8.08 p99 10.19; snapshot p99 1.49
(noglass attempt 1     --config noglass --adapter amd            20:17:02Z  exit 2  INCONCLUSIVE, timerCoverage=0.02: probe bug, deviation 8)
run5_amd_noglass       --config noglass --adapter amd            20:17:29Z  exit 0  baseline      median 4.17 p99 7.42; GPU content 0.30
ctl1_amd_stall         --ctl stall --adapter amd                 20:17:53Z  exit 3  CONTROL ... frameMedian 37.53 > 16.7 => flagged=True
ctl2_amd_gpuheavy      --ctl gpuheavy --heavy 2 --adapter amd -Counters  20:18:09Z  exit 3
   CONTROL gpuheavy control must be flagged by the GPU timer: snapshotGpuP99 34.57 > 3.0 => flagged=True; CPU op median 0.81 ms
   counters: luid_0x00000000_0x00017631 eng_0  samples=8 mean=87.7% max=90.2%
ctl3_software          --render software -Counters               20:18:32Z  exit 3
   render path: lease=True GrContext=null (CPU raster) effect=True
   FRAMES ... median=99.63 p95=108.62 p99=121.33
   CONTROL software control must fail the GPU-path check: gpuPath=False => flagged=True;  counters: (no instance for the pid)
run6_default_packet    --config packet --adapter default -Counters  20:18:47Z  exit 0  RESULT PASS (RTX 4080)  median 4.17 p99 4.61; snapshot p99 0.82
   counters: luid_0x00000000_0x00016317 eng_0  samples=8 mean=2.6% max=2.8%
run7_default_persite   --config persite --adapter default        20:19:16Z  exit 0  RESULT PASS (RTX 4080)  median 4.17 p99 4.80; snapshot p99 0.84
ctl4_default_gpuheavy  --ctl gpuheavy --heavy 12 --adapter default  20:19:32Z  exit 3  snapshotGpuP99 5.31 > 3.0 => flagged=True; CPU 0.78
run8_amd_packet_notimer --config packet --adapter amd --notimer  20:19:47Z  exit 0  characterisation: median 5.39 p99 9.64
run9_amd_packet_final  --config packet --adapter amd             20:20:03Z  exit 0  RESULT PASS   median 6.01 p99 9.75; snapshot p99 0.57
   CHECKS screenLive=True gpuPath=True timerCoverage=1.00 timerOk=True flushOk=True pointerOk=True | frameMedian=6.01<=16.7 frameP99=9.75<=33 -> True | snapshotGpuP99=0.57<=3.0 -> True
every run: "window closed; cursor restored to (1765,516) (was (1765,516))"
```

Full logs: `evidence/<run>/run_log.txt`. Per-frame data: `evidence/<run>/frames.csv` (frame, time, interval, CPU op, GPU
content, GPU snapshot, GPU lens). The OS counters are in `evidence/<run>/gpu_engine_counters.txt`, the environment in
`evidence/environment.txt`, and the API dump in `evidence/api_dump.txt`.

## Files in this folder

- `p2.csproj`, `Program.cs`: the probe. Usage: `p2 --out <dir> [--config packet|source|persite|noglass] [--adapter
  default|amd|nvidia] [--render gpu|software] [--ctl none|stall|gpuheavy] [--heavy N] [--notimer] [--seconds N]`, or
  `p2 --api`. Exit 0 PASS, 1 FAIL, 2 INCONCLUSIVE, 3 control run, 4 crash.
- `run_probe.ps1`: the driver used for every numbered run. It checks for other probes, creates a fresh
  `MARROW_PORT_DATA_DIR`, optionally samples the GPU engine counters, and deletes the data folder afterwards.
- `evidence/<run>/window_mid_run.png`: a half-scale DWM-composed capture of the probe window taken mid-measurement. The
  largest is 150,550 B. The folder totals about 2.8 MB.

## Cleanup

- `p2/bin` and `p2/obj` deleted.
- Every `%TEMP%\marrow-p2-*` folder deleted (count 0 afterwards). The smoke run's folder was deleted by hand. The temporary
  stdout files were deleted.
- Every probe window was closed by the probe's `finally` block. Each `p2.exe` exited, and afterwards `Get-Process` showed no
  `p2`/`p3`/`p4`/`p7` process.
- The cursor ended every run at its recorded start (1765,516). No button or key was ever sent. Wallpaper, theme, GPU
  preferences and system settings were not touched. Nothing was committed. The shared MSBuild and VBCSCompiler build servers
  were left running, as in P10.
