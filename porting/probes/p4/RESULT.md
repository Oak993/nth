# P4 result: FAIL on the edge half (a one-frame edge lag while the pointer moves); the animation half PASSES

Task packet T-M0-003. Runs 2026-09-22 19:54:47Z to 20:00:26Z, by a worker subagent on the owner's real, unlocked
desktop (owner decision OD-0007). `query user`: `omera console 3 Active`. Every run's screen-live check read the
probe's own checkerboard (two-valued fraction 1.0000 from run 2 on), so the screen was composited throughout.

> P4 - Acrylic plus extended client area plus live resize, all three at once. Drag-resize a 1320x860 frameless acrylic
> window running P1's glass surface and a spring animation. PASS: no margin artefact at any edge (WindowImpl.cs:1183 /
> issue #8316), and the spring shows no visible stall, snap or discontinuity across WM_ENTERSIZEMOVE and
> WM_EXITSIZEMOVE, with per-frame times logged. FAIL: record 'animation not held during live resize' as a named
> deviation and decide the reduced-fidelity resize behaviour BEFORE the chrome is designed.

## Verdict

| Half | Result | Evidence |
| --- | --- | --- |
| Spring and glass keep animating through the real modal size loop | **PASS in all 12 runs.** UI-thread animation frames and render-thread frames continue at the display rate (median 4.1 to 4.2 ms, 239 Hz) during WM_ENTERSIZEMOVE..WM_EXITSIZEMOVE. Max interval in the modal loop is 22.3 ms at worst (run 1), 13.4 ms in run 2. Intervals across ENTER and EXIT: 4.5 to 4.9 ms. The visible bar never stalled for more than 31.5 ms, which is the sampler's resolution, and never snapped | `FRAMES`, `BOUNDARY`, `VISIBLE` lines; `ui_frames.csv`, `render_frames.csv`, `row_samples.csv` |
| No margin artefact at any edge | **FAIL.** While the pointer moves, the content lags the window size by about one frame. On the growing (right and bottom) edges a strip 3 to 5 px past the content shows the window's backdrop without the app layer. On the shrinking edge the outermost content is clipped. This happens in 4 to 12% of dense samples while moving and 0% while holding or after release. Left and top are never affected. The strip is also present with no acrylic, with no extended client area, and under DirectComposition | `EDGE`/`DENSE` lines; `drag_worst_*_corner_br.png`; `dense_edges.csv` |
| Packet verdict (both halves required) | **FAIL**, on the edge half only | run 2 exit 1; run 5 exit 1 |

In one sentence: Avalonia 12.1.2 keeps the UI thread, the compositor and P1's glass shader running at full display rate
inside Windows' modal size loop, so the packet's named failure ("animation not held during live resize") did not occur.
But every configuration measured shows a transient one-frame lag between window size and content at the moving edges,
and the pre-registered margin criterion counts that as an artefact.

## The capability, the controls, and the thresholds

Capability under test: (1) no strip or clipping at any edge while and after the window is live-resized; (2) the spring
keeps moving smoothly through the modal loop.

What the subject window contained, drawn in one `ICustomDrawOperation` through `ISkiaSharpApiLeaseFeature`, with a
1320 x 860 client area at 96 DPI:

- the source's dark scrim over the acrylic (black at 0.26, then #1E1E20 at 0.12, as in P3)
- a card of P1's live content (bands, gradient, transcript text)
- P1's glass panel, 440 x 220, running P1's SKSL unchanged over an app-owned `SKSurface` re-render of that content. Its
  x position is driven by the spring, so the shader re-runs every frame.
- an opaque pure-green 6 px bar at the panel centre. This is the spring's visible readout.
- a 3 px pure-magenta marker on the outermost pixels the app draws. This is the edge readout.

Window: `WindowDecorations.Full`, `ExtendClientAreaToDecorationsHint = true`, `ExtendClientAreaTitleBarHeightHint = 44`
(P7's measured chrome configuration), `TransparencyLevelHint = [AcrylicBlur]`, `Background = Transparent`. Actual:
`ActualTransparencyLevel=AcrylicBlur IsExtendedIntoWindowDecorations=True`, style `0x16CF0000`, ex `0x00200108`.

Spring (ENGINEERING choice for the probe, not a source constant): response 0.8 s, damping fraction 0.5, integrated in
1 ms steps against wall-clock time. Its target flips between x=120 and x=700 every 700 ms, so the panel is always moving.
Because the spring runs on wall-clock time, a stalled frame loop resumes with a visible jump.

Thresholds, fixed in the file header before the first run and never changed:

- **margin artefact**: on a capture whose DWM frame bounds were identical immediately before and after the BitBlt, the
  magenta marker on any side is missing within 64 px of the frame edge, or sits more than 2 px from its at-rest
  calibrated offset (calibration: 1 px on every side, all 5 sample lines agreeing, every run).
- **visible stall**: the green bar's screen x is unchanged, or not found, for more than 100 ms.
- **visible snap**: a step between consecutive screen samples larger than 1.5x the baseline's largest step, or a speed
  above 1.5x the baseline's largest speed.
- **frame stall**: a UI-thread animation-frame interval or a render-thread frame interval above 50 ms, counting from the
  last frame before WM_ENTERSIZEMOVE to the first frame after WM_EXITSIZEMOVE.

Controls, both measured in every run and both flagged by the same detectors every time:

| Control | What it removes | Must | Run 2 result |
| --- | --- | --- | --- |
| `ctl_stall`: `Thread.Sleep(500)` on the UI thread at rest, before the drag | the animation (the capability's absence, forced) | be flagged as a stall | UI max interval **512.9 ms**, render **508.4 ms**, visible bar unchanged **496.7 ms**, then a 728 px snap: **flagged** (runs 1 to 12: 496 to 527 ms) |
| `ctl_inset`: the same window with its content stopping 24 px short of the right and bottom edges | edge-to-edge content (a margin by construction) | be flagged on right and bottom only | right and bottom offset 25 (dev **24**), left and top dev 0: **flagged** (identical in every run), `ctl_inset_corner_br.png` |

## Numbers

### Pre-registered runs (full captures every 120 ms, same cadence in every phase)

| Run | Configuration | Screen live | UI max in modal loop | Render max in modal loop | Visible longest unchanged | Stable drag/after captures with artefact | Exit |
| --- | --- | --- | --- | --- | --- | --- | --- |
| run1 | acrylic, extend, default composition | **False** (probe bug, see deviations) | 22.3 ms | 12.7 ms | 31.0 ms | 1/40 (grow: right dev 5, bottom dev 3) | 2 INCONCLUSIVE |
| **run2** | acrylic, extend, default composition (**decisive**) | True | 13.4 ms | 9.3 ms | 31.5 ms | **1/38** (shrink: right and bottom marker clipped) | **1 FAIL** |
| run3 | same as run2 | True | 17.8 ms | 10.9 ms | 31.5 ms | 0/35 | 0 PASS |
| run4_level_none | no acrylic (hint None), extend (comparison) | True | 14.2 ms | 8.0 ms | 31.5 ms | 0/39 | 0 PASS |
| run5_dcomp | `Win32CompositionMode.DirectComposition` (actual level Transparent, not acrylic) | True | 13.4 ms | 8.2 ms | 31.0 ms | 2/40 (grow: right dev 5, bottom dev 3) | 1 FAIL |

At a 120 ms cadence the artefact is caught in some runs and missed in others: 1/38 in run 2 and 0/35 in run 3 come from the
same configuration. So the dense runs below measure its rate.

Run 2 detail, per drag phase (UI / render frames, then visible):

| Phase | UI frames, median, max | Render frames, median, max | Visible samples, longest unchanged, max step |
| --- | --- | --- | --- |
| baseline (at rest) | 512, 4.22 ms, 8.6 ms | 512, 4.21 ms, 8.6 ms | 133, 31.0 ms, 77 px |
| grow (1.2 s, 60 steps, +280,+180 px) | 263, 4.13 ms, 10.5 ms | 279, 4.19 ms, 9.3 ms | 74, 31.0 ms, 72 px |
| hold (1.0 s, button down, pointer still) | 236, 4.23 ms, 7.6 ms | 236, 4.24 ms, 7.6 ms | 59, 31.0 ms, 74 px |
| shrink (0.8 s, 40 steps, -150,-90 px) | 181, 4.15 ms, 13.4 ms | 190, 4.16 ms, 8.1 ms | 50, 31.0 ms, 74 px |
| hold2 (0.3 s) | 76, 4.19 ms, 7.4 ms | 76, 4.20 ms, 7.5 ms | 21, 31.5 ms, 22 px |
| post (1.4 s after release) | | | 86, 31.0 ms, 74 px |

Modal loop, run 2: `WM_NCLBUTTONDOWN ht=17` at 7002.2 ms, `WM_SYSCOMMAND 0xF000` (SC_SIZE) 7005.3, `WM_ENTERSIZEMOVE`
7005.8, 100 `WM_SIZING`, 102 `WM_SIZE`, `WM_CAPTURECHANGED` 10549.2, `WM_EXITSIZEMOVE` 10550.2. The UI-thread frame
interval containing ENTER was 4.8 ms, and the one containing EXIT 4.9 ms (render: 4.8 / 4.8). Settled after release:
frame 1452 x 951, all four markers at offset 1 (dev 0) in every run.

### Dense edge runs (added post hoc, see deviations): one screen row across the right edge and one column across the bottom edge, about every 8.4 ms

| Run | Configuration | grow: artefact / stable samples | shrink | hold, hold2, post | Strip pixel 2 px inside the right edge, in artefact samples |
| --- | --- | --- | --- | --- | --- |
| run6_dense | acrylic, extend | 4/76 (5.3%): right dev 4 to 5, one with bottom dev 3 | 0/52 | 0 | (not recorded yet) |
| run7_dense | acrylic, extend | 4/78 (5.1%) | 2/51 (3.9%), right clipped | 0 | (not recorded yet) |
| run12_dense | acrylic, extend | 6/77 (7.8%) | 6/52 (11.5%), right clipped | 0 | grow: (83..176, 118..191, 255), blurred checker **without** the app scrim; shrink: scrim-darkened content |
| run8_dense_level_none | no acrylic | 4/78 (5.1%) | 2/52 (3.8%) | 0 | (not recorded yet) |
| run11_dense_level_none | no acrylic | 5/78 (6.4%) | 5/50 (10.0%) | 0 | grow: **(255,255,255) or (0,51,255)**, the sharp checkerboard behind the window; shrink: (4,4,4) |
| run9_dense_dcomp | DirectComposition (Transparent) | 6/78 (7.7%) | 3/52 (5.8%) | 0 | (not recorded yet) |
| run10_dense_noextend | acrylic, **no** extended client area (OS title bar) | 5/77 (6.5%) | 3/52 (5.8%) | 0 | (not recorded yet) |

In every dense run the animation half passed again: UI max in the modal loop 11.1 to 13.8 ms, render max 8.0 to 12.3 ms,
visible longest unchanged 31.0 to 31.5 ms.

What the strip is, from the captures: when the window grows by one step (about 4.7 px horizontally and 3 px vertically per
60 Hz input step), DWM shows the new frame before Avalonia has presented content at the new size. For about one frame,
the uncovered band shows whatever the window's backdrop is. With AcrylicBlur that is the blurred material without the
app-drawn scrim, so it is lighter than the neighbouring pixels (`drag_worst_grow_7611_corner_br.png`, run 1). With no
acrylic it is the desktop behind the window, sharp: here white or blue checker squares. When the window shrinks, the
previous, larger content is clipped for about one frame, and the right marker disappears (`drag_worst_shrink_9571_corner_br.png`,
run 2). The left and top edges never moved and never showed it.

About #8316: its symptom is white strips during resize on a black-background window. This probe did not see white
strips as such. It saw a strip showing the window's backdrop. It is an **inference, not measured**, that the same lag
over a white desktop behind a non-acrylic window produces #8316's white strips. The comparison runs show the lag does
not need acrylic, extended client area, or WinUI composition, so it is not specific to the WindowImpl.cs:1183 path.

## Design consequence

1. **No reduced-fidelity animation mode is needed for live resize.** The packet's FAIL branch was written for
   "animation not held during live resize". That did not happen in 12 of 12 runs. The UI thread keeps receiving
   `RequestAnimationFrame` callbacks, and the compositor keeps rendering, at the display rate inside Windows' modal
   size loop, including while the button is held and the pointer is still. P1's glass shader re-ran every frame. The
   stack-decision critique's concern ("installs no timer to keep UI-thread animation ticking inside Windows' modal
   size/move loop") does not bite on this build: dispatcher work posted to the thread is dispatched by the modal loop's
   own message pump. That mechanism is an inference; the frame logs are the measurement. Springs, glass and live
   content can be designed as if resize were not happening.
2. **Record a named deviation: "one-frame edge lag during live resize".** While the pointer is moving, the growing edges
   show a strip of the window's own backdrop, 3 to 5 px wide, for about one frame per resize step (4 to 12% of samples
   while moving, never at rest). The shrinking edges clip for about one frame. The macOS source presumably resizes
   without it, but that was not checked here and is UNRESOLVED. The lag is independent of acrylic, extended client
   area and composition mode, so no choice made so far causes it, and no chrome design choice measured here removes it.
3. **Chrome implication, ENGINEERING DECISION candidate:** make the lag strip look like the window rather than like a
   hole. The strip shows the backdrop without the app layer. If the outermost few px of the design are the acrylic
   material anyway, the strip differs only by the missing scrim. That is the case with a plain edge. ADR-0002
   deviation 5's app-drawn shadow in transparent margins would not be: the new margin would lack its shadow for a
   frame. With AcrylicBlur on (P3's recommendation) the strip is always blurred material, never raw desktop. Without
   it, the strip is the raw desktop, sharp.
4. **Not established:** a remedy that removes the lag. Candidates such as synchronous rendering in `WM_SIZING`,
   `DwmFlush`, or presenting at the new size before `WM_WINDOWPOSCHANGED` completes were not measured. Also not
   established: DPI other than 100%, the AMD integrated GPU the critique names (the measured display is driven by the
   RTX 4080 at 239 Hz; which adapter rendered was not recorded), a 60 Hz display (the lag is measured in frames, so it
   would last about 16.7 ms instead of 4.2 ms, which is more visible), the light theme and its 0.18 white scrim, drags
   on the left and top edges, and a maximised window.

## Owner decision needed

Yes, one: **is the one-frame edge lag during live resize (deviation above) acceptable as a named deviation, or must a
remedy be probed before the chrome is designed?** The animation half needs no decision. If the owner wants the lag
removed, the next step is a follow-up probe of synchronous resize presentation (candidates in item 4), with this
probe's dense edge detector as the measure.

## Deviations from the packet and from the plan, with reasons

1. **Run 1 was INCONCLUSIVE because of two probe bugs, fixed before run 2. No threshold changed.** (a) The screen-live
   check sampled an 8 px strip just left of the subject window, which lies inside the subject's DWM drop shadow, so
   the checker squares read darkened (two-valued fraction 0.0385). It now samples an 80 x 80 patch of the checker far
   from the subject: 1.0000 in every later run. (b) The baseline started immediately after the UI thread had encoded
   the at-rest PNGs, so its first frame interval was 58.6 ms. A 400 ms settle now precedes the baseline. Run 1's
   measurements are kept in `evidence/run1/`; its animation and edge numbers agree with the later runs.
2. **Comparison modes added after run 2, labelled post hoc:** `--level none` (no acrylic), `--composition dcomp`,
   `--noextend` (OS title bar), and `--dense`. The dense mode replaces the 120 ms full captures during the drag with a
   1-px row and column across the right and bottom edges, about every 8.4 ms, because at 120 ms the artefact was hit or
   missed by chance (1/38 against 0/35 in the same configuration). Dense runs use the same marker detector and the same
   2 px tolerance. They are characterisation only. The pre-registered verdict rests on runs 2, 3 and 5. The strip pixel
   colour was added to the dense CSV only from run 11 on.
3. **DirectComposition does not give acrylic.** With `--composition dcomp`, `ActualTransparencyLevel` was `Transparent`,
   so runs 5 and 9 compare composition modes without acrylic.
4. **Capture method.** In-process `BitBlt` from the screen DC with `SRCCOPY | CAPTUREBLT` (DWM-composed), over our own
   windows only. A capture counts only when `DWMWA_EXTENDED_FRAME_BOUNDS` was identical immediately before and after it
   ("stable"; 0 to 4 unstable captures per run were skipped). The screen-row spring samples take 3.6 ms median, and one
   comes about every 16 ms, so visible stalls shorter than about 31 ms cannot be resolved on screen. The 4.2 ms frame
   logs cover that range.
5. **Background and window placement.** The background is a probe-owned 2000 x 1312 checkerboard window (96 px white
   #FFFFFF and blue #0033FF), topmost, covering the whole drag extent. The wallpaper was not used or changed. The
   subject is topmost, hidden from the taskbar, and owned by the checker.
6. **Input.** The only input was SendInput: absolute moves (`MOUSEEVENTF_MOVE | ABSOLUTE | VIRTUALDESK`) and one
   left-button down/up pair per run. The start point came from a WM_NCHITTEST diagonal scan of our own HWND.
   `(1528,960)` returned `HTBOTTOMRIGHT` (17), and `WindowFromPoint` returned our HWND. The four points 1 to 4 px further
   in returned HTCLIENT. In the `--noextend` run it was `(1529,992)`. Every intermediate point lies on our window's
   moving border over our own checker. `GetCursorPos` was checked after every move: 0 misses in every run. The cursor
   was restored to its recorded start position at the end of each run. The start position changed between run 10
   (2182,855) and run 11 (2559,1439), so the pointer was moved by someone between those runs, not during a drag.
7. **Spring** is a probe choice (response 0.8 s, damping 0.5), not a source constant. The packet names no spring.
8. **No theme loaded** (`AppBuilder.Configure<Application>().UsePlatformDetect()`, as in P3 and P7); `ActualThemeVariant=Dark`.
9. **Build.** Console `Exe`, Release, P10's central-package-management pattern (no Version attributes), plus
   `Avalonia.Desktop`. `bin/` and `obj/` deleted at the end.

## Environment

- `cmd /c ver`: `Microsoft Windows [Version 10.0.26200.9457]`
- `dotnet --version`: `10.0.401`
- Avalonia `12.1.2+d3c867a9e2de379249b03dbeb3495bd7f076a81a`; SkiaSharp `3.119.4+release-3.119.4.f568ac94...` (printed by the probe)
- GPUs (`Win32_VideoController`): NVIDIA GeForce RTX 4080 (driver 32.0.16.1692, 2560 x 1440 at **239 Hz**), AMD Radeon(TM)
  Graphics (32.0.21036.18), Parsec Virtual Display Adapter (0.45.0.0)
- Virtual screen (0,0) 2560 x 1440; primary work area 2560 x 1392; DPI 96 (100%) for the subject window
- Theme dark (`AppsUseLightTheme=0`), transparency effects on (`EnableTransparency=1`)
- Session: `query user` gave `omera console 3 Active`, idle 11:52 at the first run
- `MARROW_PORT_DATA_DIR` was a fresh `%TEMP%\marrow-p4-<run>` folder for every run; all were deleted at the end
- Before every run no `p2`/`p3`/`p4`/`p7` process was running (`Get-Process`, `tasklist`)

## Commands and their decisive output

```
# pre-flight (19:50:50Z)
PS> Get-Process | ? { $_.ProcessName -match '^(p2|p3|p4|p7)$' }        -> (none)
PS> query user                                                          -> omera  console  3  Active  11:47
PS> dotnet --version; cmd /c ver                                        -> 10.0.401; Microsoft Windows [Version 10.0.26200.9457]

# build (after each source change)
PS> dotnet build porting\probes\p4\p4.csproj -c Release -nologo
  first attempt: CS0119 'Path(...)' is a method (a local function shadowed System.IO.Path); CS1061 tuple element names
                 lost through a conditional expression. Fixed by renaming and explicit tuple types.
  then:          p4 -> ...\p4\bin\Release\net10.0\p4.dll   Build succeeded.  0 Warning(s) 0 Error(s)

# run 1 (19:54:47Z): exit 2 INCONCLUSIVE (probe bugs, deviation 1)
PS> p4.exe --out porting\probes\p4\evidence\run1
  screenLive: checker strip two-valued fraction=0.0385 ... => False
  FRAMES ui baseline: ... maxInterval=58.6ms (starting at 2706.3)

# run 2 (19:55:50Z): the decisive pre-registered run, exit 1
PS> $env:MARROW_PORT_DATA_DIR = "$env:TEMP\marrow-p4-run2"; p4.exe --out porting\probes\p4\evidence\run2
  subject ... frame=[207,100]-[1529,961] 1322x861 client=1320x860 ... ActualTransparencyLevel=AcrylicBlur IsExtendedIntoWindowDecorations=True
  glass: lease=True surface=True effect=True
  CALIB left=1 top=1 right=1 bottom=1 consistent=True
  screenLive: checker strip two-valued fraction=1.0000 markers found on every side=True => True
  drag start point (1528,960) HTBOTTOMRIGHT on our HWND
  drag times: down 6991.7 grow 7208.7 hold 8420.0 shrink 9429.8 hold2 10238.0 up 10548.6; pointerMisses=0
  modal loop: WM_ENTERSIZEMOVE x1 WM_EXITSIZEMOVE x1 WM_SIZING x100 WM_SIZE x102
  FRAMES ui modal [ENTER 7005.8 .. EXIT 10550.2] incl. boundary frames: frames=799 medianInterval=4.18ms maxInterval=13.4ms
  FRAMES render modal incl. boundary frames: frames=822 medianInterval=4.20ms maxInterval=9.3ms
  BOUNDARY interval containing WM_ENTERSIZEMOVE: ui 4.8ms render 4.8ms; containing WM_EXITSIZEMOVE: ui 4.9ms render 4.8ms
  VISIBLE baseline: samples=133 notFound=0 longestUnchanged=31.0ms maxStep=77px maxSpeed=3.35px/ms
  VISIBLE ctl_stall: samples=37 notFound=0 longestUnchanged=496.7ms maxStep=728px maxSpeed=46.96px/ms
  VISIBLE modal (ENTER-100 .. EXIT+100): samples=226 notFound=0 longestUnchanged=31.5ms maxStep=74px maxSpeed=2.84px/ms
  EDGE 9571.88.. shrink stable=True frame=[207,100]-[1779,1123] right=[none x5] bottom=[none x5]   (content clipped)
  EDGE SUMMARY stable captures during/after drag=38 (unstable skipped=2) with artefact=1; grow:0/9 hold:0/8 shrink:1/6 hold2:0/2 post:0/13
  AFTER frame=[207,100]-[1659,1051] 1452x951 edges all dev0 artefact=False
  CTL_INSET ... right=[25,25,25,25,25]dev24 bottom=[25,25,25,25,25]dev24 left/top dev0 artefact=True
  CONTROLS stall control flagged=True (visible longest 496.7ms, ui max 512.9ms, render max 508.4ms); inset control flagged=True
  SUMMARY screenLive=True modalLoop=True pointerMisses=0 baselineClean=True | marginArtefact=True (1/38 ...) | visibleStall=False visibleSnap=False frameStall=False
  RESULT margin: FAIL; animation held across the modal size loop: PASS; P4: FAIL
  windows closed; cursor restored to (2182,855) (was (2182,855))

# runs 3 to 5 (19:56:20Z to 19:57:06Z)
PS> p4.exe --out ...\run3                                   -> margin 0/35, animation PASS, exit 0
PS> p4.exe --out ...\run4_level_none --level none            -> ActualTransparencyLevel=None; margin 0/39, animation PASS, exit 0
PS> p4.exe --out ...\run5_dcomp --composition dcomp          -> ActualTransparencyLevel=Transparent; margin 2/40 (grow, right dev 5, bottom dev 3), exit 1

# dense runs 6 to 12 (19:57:54Z to 20:00:26Z); "RESULT ... PASS" in these runs is not a verdict (no full drag captures)
PS> p4.exe --out ...\run6_dense --dense                      -> DENSE grow 4/76 (5.3%) shrink 0/52; hold/hold2/post 0
PS> p4.exe --out ...\run7_dense --dense                      -> DENSE grow 4/78 (5.1%) shrink 2/51 (3.9%)
PS> p4.exe --out ...\run8_dense_level_none --dense --level none      -> DENSE grow 4/78 (5.1%) shrink 2/52 (3.8%)
PS> p4.exe --out ...\run9_dense_dcomp --dense --composition dcomp    -> DENSE grow 6/78 (7.7%) shrink 3/52 (5.8%)
PS> p4.exe --out ...\run10_dense_noextend --dense --noextend         -> IsExtendedIntoWindowDecorations=False; CALIB top=31; DENSE grow 5/77 (6.5%) shrink 3/52 (5.8%)
PS> p4.exe --out ...\run11_dense_level_none --dense --level none     -> DENSE grow 5/78 (6.4%) shrink 5/50 (10.0%); strip pixels (255,255,255)/(0,51,255)
PS> p4.exe --out ...\run12_dense --dense                     -> DENSE grow 6/77 (7.8%) shrink 6/52 (11.5%); strip pixels blurred checker without scrim
  every dense run: animation frames and visible bar as in run 2 (UI max 11.1..13.8 ms, render max 8.0..12.3 ms, visible 31.0..31.5 ms)
```

Full logs: `evidence/<run>/run_log.txt`. Per-frame data: `ui_frames.csv` (ms, spring x, client size),
`render_frames.csv` (ms, spring x drawn, size), `row_samples.csv` (screen-row bar x), `messages.csv` (WM_* with times),
`edge_captures.csv` (every full capture's per-side offsets), `dense_edges.csv` (dense runs).

## Files in this folder

- `p4.csproj`, `Program.cs`: the probe. Usage: `p4 --out <dir> [--composition default|winui|dcomp] [--deco full|borderonly]
  [--level acrylic|none] [--dense] [--noextend]`. Exit 0 PASS, 1 FAIL, 2 INCONCLUSIVE, 4 crash. `--deco borderonly` was
  not run (P7 ruled BorderOnly out).
- `evidence/run1` to `run5_dcomp`: `rest_window_half.png`, `rest_corner_br.png`, `drag_<phase>_<ms>_{window_half,corner_br}.png`
  (a mid-phase capture of grow, hold and shrink), `drag_worst_<phase>_<ms>_*` (the worst-deviation capture, where there
  was one), `after_*`, `ctl_inset_corner_br.png`, the CSVs and the log.
- `evidence/run6_dense` to `run12_dense`: the same, minus the half-scale window PNGs (deleted as duplicates of runs 1 to 5).
- The largest PNG is 149,310 B. The folder totals about 6.4 MB, mostly frame CSVs.

## Cleanup

- `p4/bin` and `p4/obj` deleted.
- `%TEMP%\marrow-p4-*` (12 folders) deleted and confirmed gone (`Get-ChildItem` count 0).
- Every probe window was closed by the probe's `finally` block, which also releases the button if a drag was interrupted.
  Each `p4.exe` exited, and `tasklist` afterwards showed no `pN.exe` process.
- The cursor ended each run at its recorded start. Wallpaper, theme and system settings were not touched. Nothing was
  committed.
