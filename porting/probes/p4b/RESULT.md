# P4b result: no remedy removes every edge strip. `stretch` removes the common strip and every shrink clip; a rarer DWM lag remains, so OD-0013 goes back to the owner

Follow-up to P4 (task packet T-M0-003) under owner ruling OD-0013 ("the live-resize edge strip must be fixed"). Ten
recorded sessions, 2026-09-22 21:30:31Z to 23:17:35Z, on the owner's real, unlocked desktop (OD-0007). They were run by
two invocations of a Claude subagent: the first built the probe and ran sessions 1 and 2, and the second (this one)
resumed, extended the probe and ran sessions 3 to 10. `query user` showed `omera console 3 Active` and no LogonUI
process was running. Every session's screen-live check read the probe's own checkerboard with a two-valued fraction of
1.0000.

Configuration, as in P4: Avalonia 12.1.2, 1320 x 860 client, `WindowDecorations.Full`,
`ExtendClientAreaToDecorationsHint` with a 44 px title bar, `TransparencyLevelHint [AcrylicBlur]`, P1's glass surface
and the app-drawn scrim, default (WinUI) composition. Actual values: level `AcrylicBlur`, extended `True`, style
`0x16CF0000`, ex style `0x00200108`.

## Verdict

| Question | Answer | Evidence |
| --- | --- | --- |
| Does any remedy give "no strip while moving" (P4's dense detector and thresholds, 0 artefacts)? | **No, not reliably.** The best is `stretch`: Avalonia's content visual is sized with the window and its surface brush set to Fill. It passes P4's criterion in 18 of 28 instances of the `stretch` family. At normal render speed and pooled over every session, it lowers the moving grow-sample artefact rate from **2.54 % (146/5755, unremedied) to 0.16 % (10/6377)** and the shrinking-edge clip from **2.36 % (93/3934) to 0 % (0/4365)**. All 10 remaining artefacts are a different lag that comes from DWM (next row). | pooled table |
| What is left | The **type B frame**. For about one display frame, DWM draws the window's frame (its dark border) at the new size while still showing the content only up to the previous frame. The growing edge then shows a 3 to 9 px hole of whatever is behind the window: the raw checkerboard, not even acrylic. The app's edge is cut off. This frame also occurs in the unremedied window (2 of 146 strips at normal speed, 5 under stress). No tested remedy removes it, including extending the client area 56 px past the window during the loop (`over2`, `overstretch`, `over2sync` still show it). | pixel rows in `artefacts.csv` (sessions 4 to 10), `kymo_right.png`; section "Two kinds of lag frame" |
| Side effect of `stretch` | When a frame lags, DWM shows the previous frame **scaled** to the new window instead of a strip. At normal speed this affects 1.5 to 2.5 % of moving samples, by up to one resize step (5 px at the moving edge, 2 px mid-window at P4's drag speed), for about one frame. With a 16 ms render it affects 98 % of moving samples, up to 9 px. It is not a strip, but it is not invisible, so it needs the owner's acceptance. | scaled-frame columns (static tick at client x 1260) |
| Timing remedies (task items 1, 2, 3) | **They do not remove the strip.** Avalonia already renders every new size synchronously: WM_PAINT follows each WM_SIZE after a median 0.13 ms (in 407 of 408 steps), and its WM_PAINT path waits for the render thread. Results: forcing the render inside WM_SIZE (`sync`) 1.27 %; adding DwmFlush (`syncflush`) 0.63 %; DwmFlush alone (`flush`) **73.8 %**, which is worse; DwmFlush before the resize (`align`) 4.0 %, with the edge trailing the pointer by 4 px; WVR_VALIDRECTS 1.25 %. With a 16 ms render, all of them are at 96 to 99 %. | sessions 1, 4, 9 |
| Over-render (task item 4) | As first built (`overrender`), it moved the visible frame out by the 7 px invisible resize border for the whole drag (P4 detector 100 %). `over2` compensates for that and keeps the edge under the pointer. It **hides** the strip: each lag band shows the app's own scrim, and 0 of 1277 lagging grow samples were visible. But the app's edge still lags a frame, so P4's detector counts it (0.95 % at normal speed), the shrink clip stays (3.65 %), and type B holes still appear. `overstretch` (`over2` + `stretch`) had 0 grow and 0 shrink artefacts in 952 / 656 samples at normal speed. However, it corrupts the frame when the loop starts and ends: at the end, the content is shown squeezed by 4 %, so its right edge jumps up to 56 px inward (5 at-rest artefacts in 12 drags). Under stress the start shows up to 655 px displacements, and it still had a type B hole. **Rejected.** | sessions 1, 7 to 10 |
| Colour match (task item 5) | `hide` **hides the strip but does not remove it**: P4's detector counts grow strips at 0.93 %, none of them visible against the window, and the shrink clip stays (1.37 %). | session 1 |
| DWM animations and Snap | **Unchanged in every case measured.** Maximise, restore, minimise and restore-from-minimise are all animated, the Snap Layouts flyout appears, and dragging the caption to the top edge snaps. The control (DWMWA_TRANSITIONS_FORCEDISABLED) reads not animated in sessions 1, 3, 8 and 10. | sessions 1, 3, 8, 10 |
| At rest | Every hold, hold2 and post sample has 0 artefacts in every case, except the first `overrender` (constant offset) and `overstretch` (exit glitch). `stretch` scales nothing at rest: marker offsets and static ticks match `base` when restored, maximised and restored again. | GEOM lines, sessions 3, 8, 10 |

In short: sizing Avalonia's content visual with the window (with a Fill surface brush) is the only change that keeps the
app's edge on the window's edge while it moves, and it costs no frame time. But Windows sometimes composes a frame whose
content is still clipped to the previous size, and nothing the app draws can reach that band. The owner has to decide
whether to accept `stretch` with that residual and its one-frame scaled frames, or to keep the chrome blocked.

## Two kinds of lag frame

From session 4 on, the dense sampler records pixel rows for each artefact: the 18 px outside P4's scan window (columns
`outsideRGB...`) and the first 17 px (`sliceRGB...`), with a scan that starts 24 px outside the frame. Before session 4
only the kymographs (`kymo_right.png`, reps 1 and 2) hold pixels. These rows show two different frames.

- **Type A, the strip P4 measured** (144 of the 146 unremedied grow artefacts at normal speed):
  - Frame and composition root are at the new size; Avalonia's absolutely sized content is still the old size.
  - The growing edge shows the relatively sized acrylic without the scrim, for example `(80,115,255)` to `(176,191,255)`,
    beside the scrim over acrylic at `(56..118, 80..128, 170)`.
  - On a shrink, the old, larger content is cut off and the marker vanishes.
  - `stretch` removes this type entirely: 0 in 6377 moving grow samples at normal speed, 0 in 962 under the 16 ms stress,
    and 0 shrink clips anywhere.
- **Type B, the residual**:
  - The window's frame, its dark 1 to 2 px border, is at the new size. With `stretch` the content has already been
    scaled to the new size: the static tick at client x 1260 sits 4 to 9 px to the right.
  - Everything is clipped at the previous edge. Between that edge and the new frame, the raw checkerboard behind the
    window shows.
  - Example from session 3, `stretchsync`, rep 1, t = 90583.70 ms, frame 1448 px wide after 1443: d0..d1 `(0,0,0)`,
    d2..d5 `(255,255,255)` (the white checker square behind the window), then the scrim from d6. The magenta marker,
    drawn at the content's edge, is cut off.
  - Session 9, `overstretch`, whose client area reaches 56 px past the window: d0..d1 `(0,0,0)`, d2..d4 `(0,51,255)`
    (blue checker), then the scrim. The clip is therefore the previous window frame, not the client rectangle.
  - Every `stretch`-family artefact is a sample where the marker is missing on the growing right edge. For all 13 that
    have pixel rows (sessions 4, 5, 9, 10, and session 3 rep 1 from the kymograph), the frame is of this type: the wide
    scan finds no marker between 24 px outside and 64 px inside. Sessions 2 and 3 (reps 3 and 4) have no pixel rows for
    their 6 others.
  - In the unremedied window, the same frame shows as a strip whose band is raw checker instead of acrylic.

Artefact rows of type B:

| Case | Normal speed | 16 ms stress |
| --- | --- | --- |
| `base` | 2 in 5755 grow samples (sessions 3 and 10) | 5 in 1916 |
| `stretch` family | 10 in 6377 | `stretch` 2 in 962, `stretchrdw` 4 in 307, `overstretch` 1 in 315 |
| `sync` | none measured | 16 in 937 |
| `over2sync` | none measured | 7 in 621 |

At normal speed, `stretch` shows them more often than the unremedied window. A plausible reason is that there the same
frame is sometimes covered by an ordinary strip, but that is not established. Under stress, blocking the UI thread inside
WM_SIZE makes them more frequent (`sync`, `stretchrdw`, `over2sync`). That suggests DWM updates the content clip only
after the application returns from its resize handling. This is an INFERENCE, not measured.

A per-step estimate, also an INFERENCE: a sample lasts about 8 ms and a sample is taken about every 15 ms, so about
1 resize step in 150 to 250 shows a type B frame at P4's drag speed on this 239 Hz display. On a 60 Hz display each
would last about 16.7 ms instead of 4.2 ms. That was not measured.

## What the strip is: how Avalonia 12.1.2 presents a resize

These points were read from the shipped binaries (Avalonia.Win32.dll 12.1.2, `lib/net10.0`, NuGet cache; the packages
carry no source) with this folder's `ildump`. Excerpts are in `evidence/avalonia_il_excerpts.txt`. They are facts about
the dependency, not about Droppy's source.

1. **Visual tree.** The window has no redirection bitmap: its ex style includes WS_EX_NOREDIRECTIONBITMAP. Its content is
   a Windows.UI.Composition tree built by `WinUiCompositedWindow..ctor`:
   - `CreateDesktopWindowTarget(hwnd)`;
   - a root ContainerVisual with `RelativeSizeAdjustment (1,1)`;
   - the acrylic SpriteVisual `_blur`, also relative (1,1);
   - Avalonia's content SpriteVisual `_visual`, painted by the CompositionSurfaceBrush `_surfaceBrush` over a
     CompositionDrawingSurface.
2. **The acrylic follows the window; the content does not.** `WinUiCompositedWindowRenderTarget.BeginDraw(sceneInfo)`:
   - calls `WinUiCompositedWindow.ResizeIfNeeded(sceneInfo.Size)`, which does `_visual.SetSize(size)`;
   - then calls `ICompositionDrawingSurfaceInterop.Resize(size)`, then BeginDraw ... EndDraw;
   - does all of this inside `BeginTransaction()`, which is `Monitor.Enter(_shared.SyncRoot)`. This was read in session
     3 with `ildump ... "WinUiCompositedWindow$" "BeginTransaction|Dispose|SetBlur"`.
3. **Render thread.** Frames are rendered on the "DwmRenderTimerLoop" thread, which creates a DispatcherQueue and the
   Compositor. It is ticked by `ICompositor5.RequestCommitAsync` completions: `RunLoopHandler.OnCommitCompleted` calls
   `_tick`, then `ScheduleNextCommit`.
4. **Resize and paint.**
   - DefWindowProc sends WM_SIZE from WM_WINDOWPOSCHANGED inside the modal loop. WM_SIZE runs `Resized`, then
     `WindowBase.HandleResized`: the new client size and a synchronous layout pass.
   - WM_PAINT runs `CompositingRenderer.Paint`, then `MediaContext.ImmediateRenderRequested`, then
     `SyncCommit(waitFullRender: true)`, which waits for the batch to be rendered.
   - Measured: the size loop dispatches WM_PAINT a median 0.13 ms after WM_SIZE (p95 0.25 ms, in 407 of 408 steps,
     `messages.csv` of sessions 1 and 2). Each new size is therefore already rendered synchronously.
5. **Earliest possible render.** The size loop never gives Avalonia the new size before the window has it: the render
   target's size comes from the client size set on WM_SIZE. So rendering the new size in WM_SIZING or
   WM_WINDOWPOSCHANGING (task item 1 as worded) is not possible through Avalonia. The earliest point is inside WM_SIZE,
   which is the `sync` case.

## The cases

Each case runs on a fresh subject window of P4's configuration, in the same process as `base`, the unremedied control.
The remedy code runs in a window-procedure subclass wrapped around Avalonia's own (SetWindowLongPtr GWLP_WNDPROC with
CallWindowProc), so it can act before and after Avalonia's handling. The composition cases also use reflection on
Avalonia.Win32 internals (probe only).

| Case | What it does | Task item |
| --- | --- | --- |
| `base` | nothing (the control) | |
| `sync` | In WM_SIZE, after Avalonia: `RedrawWindow(RDW_INVALIDATE \| RDW_UPDATENOW)`, so Avalonia's WM_PAINT renders and waits before WM_SIZE returns. | 1 |
| `flush` | In WM_SIZE, after Avalonia: `DwmFlush()`. | 2 |
| `syncflush` | `sync`, then `DwmFlush()`. | 1 + 2 |
| `align` | `DwmFlush()` in WM_SIZING (before the system resizes), then `sync`, then `DwmFlush()`. | 1 + 2 |
| `validrects` | In WM_NCCALCSIZE, after Avalonia: return WVR_VALIDRECTS with 1 x 1 source and destination rectangles; plus `syncflush`. | 3 |
| `overrender` | In an SC_SIZE loop, the client rectangle reaches 64 px past the window's right and bottom edges (WM_NCCALCSIZE). The scene lays out the visible part and fills the rest with its scrim. Trimmed at WM_EXITSIZEMOVE. | 4 |
| `over2` (session 3) | As `overrender`, plus: at WM_ENTERSIZEMOVE the window rectangle is shrunk by the invisible resize border in the same SetWindowPos (7 px right and 7 px bottom, measured as window rect minus DWMWA_EXTENDED_FRAME_BOUNDS), and every WM_SIZING rectangle is kept that much smaller. The visible frame and the pointer stay together. Restored in one SetWindowPos at WM_EXITSIZEMOVE. | 4 |
| `hide` | The scrim moves into the composition tree: a SpriteVisual with the scrim colour ARGB(89,10,10,11), relative (1,1), inserted above the acrylic. The app stops drawing its own scrim. | 5 |
| `stretch` | Avalonia's content visual gets Size (0,0) and `RelativeSizeAdjustment (1,1)`; the surface brush gets `Stretch = Fill`. `WinUiCompositedWindow._visual` is pointed at a detached SpriteVisual, so `ResizeIfNeeded` no longer sets an absolute size. | 6 |
| `stretchsync` | `stretch` + `syncflush` | 6 + 1 + 2 |
| `stretchrdw` | `stretch` + `sync` | 6 + 1 |
| `overstretch` (session 3) | `over2` + `stretch`. While `over2` is on, the stretched visual's Size (added to its relative size) is set to the client area beyond the window (56 x 56 px), so it is not squeezed into the root. | 4 + 6 |
| `over2sync` (session 3) | `over2` + `sync`: a diagnostic of whether the extended client area prevents type B. | |

Not built, with the reason:
- Rendering in WM_SIZING or WM_WINDOWPOSCHANGING: see item 5 of the mechanism.
- WM_ERASEBKGND, a class brush and DWMWA_CAPTION_COLOR for item 5: the window has no redirection surface for GDI
  painting to reach, and the extended client area covers the caption. This is an INFERENCE from the ex style and P7's
  configuration, not measured. `hide` is the composition-tree equivalent that does reach the screen.
- `overstretchrdw` is defined in the probe but was not run.

## Thresholds and controls

The thresholds were pre-registered in the file header before session 1 and not changed.

- **Artefact** (P4's dense criterion): a stable dense sample (DWM frame bounds identical before and after the two line
  captures) in which the magenta marker on the right row or the bottom column is missing within 64 px of the frame edge,
  or sits more than 2 px from its at-rest calibrated offset. The calibrated offset was 1 px on every side in every case.
- **Phase of a sample**: the phase in which its capture ended. A moving phase keeps samples until 50 ms after its last
  pointer step.
- **A remedy passes in a session** when all of these hold:
  - 0 artefacts in grow and in shrink, over at least 200 and 150 stable samples;
  - 0 in hold, hold2 and post;
  - no pointer miss;
  - every `base` case of that session shows at least one grow artefact.
- **Visible strip** (for the hiding remedies): a band pixel differs by more than T_nb from the scrim just inside the
  displaced marker. T_nb = max(12, 3 x rest noise), which was 12 in every case. A secondary test compares against a
  scrim-only reference image, with threshold T_app.
- **Scaled frame** (side effect of `stretch`): the static cyan tick drawn at client x 1260 on the dense row is 1 px or
  more from its most common at-rest position.
- **DWM animation and Snap**: P7's detectors, unchanged.
  - Animated: at least 2 distinct intermediate magenta bounding-box areas in 1/4-scale captures over 800 ms after
    SC_MAXIMIZE, SC_RESTORE or SC_MINIMIZE.
  - Snap Layouts flyout: more than 5 % of the zone changes after a 2 s hover on the declared maximise button.
  - Snap by drag: dragging the caption to the top edge gives IsZoomed.
- **Rest geometry** (added in session 3; a check, not a P4 threshold): the marker offsets from the client edges and the
  two static ticks, when restored, maximised and restored again, must be identical to those of the session's first
  `base`.

Controls:
- `ctl_inset` (content stopping 24 px short) was flagged on right and bottom only (dev 24; left and top dev 0) in every
  session.
- Every `base` case showed grow strips. The lowest was 1/321, in session 1.
- The animation control (transitions force-disabled) read not animated in sessions 1, 3, 8 and 10.

## Sessions

All times are UTC on 2026-09-22.

| # | Folder | Time | Cases (4 drags each) | Options | Log's verdict |
| --- | --- | --- | --- | --- | --- |
| 1 | `evidence/run1` | 21:30:31 to 21:39:28 | base, sync, syncflush, flush, align, validrects, base, overrender, hide, stretch, stretchsync, base | Snap on every case | a remedy passes (stretch, stretchsync) |
| 2 | `evidence/run2_confirm` | 21:40:19 to 21:45:14 | base, (stretch, stretchsync, base) x 3 | no Snap | INCONCLUSIVE. Case 07 saw 3 modal loops for 4 drags: the owner moved the pointer during a drag and the probe still pressed the button. Session 3 onward adds an owner guard. |
| 3 | `evidence/run3_final` | 22:22:29 to 22:27:55 | base, stretch, stretchsync, stretchrdw, base, stretch, stretchsync, stretchrdw, base | Snap on base and stretchrdw; rest geometry | a remedy passes |
| 4 | `evidence/run4_stress16` | 22:33:08 to 22:36:37 | base, stretch, stretchsync, stretchrdw, sync, syncflush, base | 16 ms added to every render inside a size loop | a remedy passes (stretch) |
| 5 | `evidence/run5_wide` | 22:37:46 to 22:41:42 | base, stretch, stretchrdw, stretchsync, stretch, stretchrdw, stretchsync, base | | a remedy passes |
| 6 | `evidence/run6_topleft` | 22:43:30 to 22:45:59 | base, stretch, stretchsync, stretchrdw, base | top-left corner dragged | a remedy passes |
| 7 | `evidence/run7_over2_stress16` | 22:49:58 to 22:52:27 | base, over2, stretch, over2, base | 16 ms stress | a remedy passes (stretch) |
| 8 | `evidence/run8_over2` | 23:03:45 to 23:07:03 | base, over2, base, over2, base | Snap on base and over2; rest geometry | no remedy passes |
| 9 | `evidence/run9_over2sync_stress16` | 23:08:07 to 23:12:04 | base, sync, over2sync, overstretch, stretch, sync, over2sync, base | 16 ms stress | no remedy passes |
| 10 | `evidence/run10_overstretch` | 23:13:13 to 23:17:35 | base, overstretch, stretch, overstretch, base, overstretch, base | Snap on base and overstretch; rest geometry | a remedy passes (overstretch, 1 of 3) |

### Pooled per remedy

These rows count every instance of every session. "Normal" means no added render time and a bottom-right drag.

| Condition | Case | Sessions | Instances (passing) | Grow artefacts | Of which marker missing | Shrink artefacts | At rest (hold, hold2, post) | Grow scaled frames (max px) | Shrink scaled (max px) |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| normal | `base` | 1, 2, 3, 5, 8, 10 | 18 | 146/5755 (2.54 %) | 0 | 93/3934 (2.36 %) | 0/12517 | 0 | 0 |
| normal | `stretch` | 1, 2, 3, 5, 10 | 8 (3) + 1 inconclusive | 5/2558 (0.20 %) | 5 | 0/1740 | 0/5554 | 59/2558 (2.31 %, 5) | 39/1740 (2.24 %, 3) |
| normal | `stretchrdw` | 3, 5 | 4 (3) | 2/1267 (0.16 %) | 2 | 0/874 | 0/2779 | 31/1267 (2.45 %, 5) | 13/874 (1.49 %, 3) |
| normal | `stretchsync` | 1, 2, 3, 5 | 8 (7) | 3/2552 (0.12 %) | 3 | 0/1751 | 0/5563 | 48/2552 (1.88 %, 5) | 31/1751 (1.77 %, 3) |
| normal | `overstretch` | 10 | 3 (1) | 0/952 | 0 | 0/656 | **5/2083** (exit glitch) | 31/952 (3.26 %, 5) | 26/656 (3.96 %, 3) |
| normal | `over2` | 8 | 2 (0) | 6/634 (0.95 %), 0 visible | 0 | 16/438 (3.65 %) | 0/1388 | 0 | 0 |
| normal | `hide` | 1 | 1 (0) | 3/321 (0.93 %), 0 visible | 0 | 3/219 (1.37 %) | 0/695 | 0 | 0 |
| normal | `sync` | 1 | 1 (0) | 4/316 (1.27 %) | 0 | 1/220 (0.45 %) | 0/695 | 0 | 0 |
| normal | `syncflush` | 1 | 1 (0) | 2/319 (0.63 %) | 0 | 1/220 (0.45 %) | 0/697 | 0 | 0 |
| normal | `validrects` | 1 | 1 (0) | 4/319 (1.25 %) | 0 | 2/220 (0.91 %) | 0/695 | 0 | 0 |
| normal | `align` | 1 | 1 (0) | 8/198 (4.04 %) | 0 | 2/134 (1.49 %) | 0/696 | 0 | 0 |
| normal | `flush` | 1 | 1 (0) | 236/320 (73.75 %) | 0 | 159/219 (72.60 %) | 0/697 | 0 | 0 |
| normal | `overrender` | 1 | 1 (0) | 321/321 (100 %), 0 visible | 0 | 218/218 (100 %) | 315/694 | 0 | 0 |
| 16 ms stress | `base` | 4, 7, 9 | 6 | 1888/1916 (98.54 %) | 0 | 1257/1303 (96.47 %) | 0/4167 | 0 | 0 |
| 16 ms stress | `stretch` | 4, 7, 9 | 3 (2) | 2/962 (0.21 %) | 2 | 0/650 | 0/2089 | 948/962 (98.54 %, 9) | 626/650 (96.31 %, 7) |
| 16 ms stress | `stretchrdw` | 4 | 1 (0) | 4/307 (1.30 %) | 4 | 0/215 | 0/694 | 303/307 (98.70 %, 9) | 207/215 (96.28 %, 7) |
| 16 ms stress | `stretchsync` | 4 | 1 (0, n < 200) | 0/167 | 0 | 0/119 | 0/695 | 160/167 (95.81 %, 9) | 112/119 (94.12 %, 9) |
| 16 ms stress | `overstretch` | 9 | 1 (0) | 1/315 (0.32 %) | 1 | 0/218 | 0/698 | 310/315 (98.41 %, 9) | 211/218 (96.79 %, 6) |
| 16 ms stress | `over2` | 7 | 2 (0) | 629/637 (98.74 %), 0 visible | 0 | 420/436 (96.33 %) | 0/1392 | 0 | 0 |
| 16 ms stress | `over2sync` | 9 | 2 (0) | 613/621 (98.71 %), 7 visible | 0 | 412/428 (96.26 %) | 0/1384 | 0 | 0 |
| 16 ms stress | `sync` | 4, 9 | 3 (0) | 923/937 (98.51 %) | 0 | 615/639 (96.24 %) | 0/2083 | 0 | 0 |
| 16 ms stress | `syncflush` | 4 | 1 (0) | 160/166 (96.39 %) | 0 | 111/117 (94.87 %) | 0/696 | 0 | 0 |
| top-left drag | `base` | 6 | 2 | 22/644 (3.42 %) | 0 | 14/438 (3.20 %) | 0/1387 | 0 | 0 |
| top-left drag | `stretch`, `stretchsync`, `stretchrdw` | 6 | 3 (3) | 0/961 | 0 | 0/653 | 0/2083 | 24/961 (5) | 13/653 (3) |

### Per session

**Session 1** (`evidence/run1`)

| # | Case | Verdict | Grow artefacts (visible) | Shrink artefacts | Scaled grow / shrink (max px) | At rest | UI p95 / max ms (frames) |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 00 | `base` | CONTROL | 8/320 (2.5%) (8) | 6/216 (2.8%) | 0 / 0 (0) | 0/695 | 5.7 / 16.4 (3319) |
| 01 | `sync` | FAIL | 4/316 (1.3%) (4) | 1/220 (0.5%) | 0 / 0 (0) | 0/695 | 5.8 / 16.3 (3323) |
| 02 | `syncflush` | FAIL | 2/319 (0.6%) (2) | 1/220 (0.5%) | 0 / 0 (0) | 0/697 | 13.4 / 21.8 (2534) |
| 03 | `flush` | FAIL | 236/320 (73.8%) (236) | 159/219 (72.6%) | 0 / 0 (0) | 0/697 | 14.2 / 19.4 (2528) |
| 04 | `align` | FAIL | 8/198 (4.0%) (8) | 2/134 (1.5%) | 0 / 0 (0) | 0/696 | 21.1 / 29.5 (1990) |
| 05 | `validrects` | FAIL | 4/319 (1.3%) (4) | 2/220 (0.9%) | 0 / 0 (0) | 0/695 | 13.4 / 20.7 (2532) |
| 06 | `base` | CONTROL | 1/321 (0.3%) (1) | 3/216 (1.4%) | 0 / 0 (0) | 0/695 | 5.7 / 12.7 (3323) |
| 07 | `overrender` | FAIL | 321/321 (100.0%) (0) | 218/218 (100.0%) | 0 / 0 (0) | 315/694 | 5.7 / 19.5 (3315) |
| 08 | `hide` | FAIL | 3/321 (0.9%) (0) | 3/219 (1.4%) | 0 / 0 (0) | 0/695 | 5.7 / 13.1 (3316) |
| 09 | `stretch` | PASS | 0/320 (0.0%) (0) | 0/218 (0.0%) | 2 / 1 (4) | 0/693 | 5.6 / 18.1 (3324) |
| 10 | `stretchsync` | PASS | 0/319 (0.0%) (0) | 0/220 (0.0%) | 6 / 4 (5) | 0/696 | 13.2 / 17.3 (2542) |
| 11 | `base` | CONTROL | 1/319 (0.3%) (1) | 2/219 (0.9%) | 0 / 0 (0) | 0/697 | 5.7 / 18.1 (3323) |

**Session 2** (`evidence/run2_confirm`)

| # | Case | Verdict | Grow artefacts (visible) | Shrink artefacts | Scaled grow / shrink (max px) | At rest | UI p95 / max ms (frames) |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 00 | `base` | CONTROL | 7/320 (2.2%) (7) | 3/220 (1.4%) | 0 / 0 (0) | 0/694 | 5.7 / 16.3 (3313) |
| 01 | `stretch` | FAIL | 1/320 (0.3%) (0) | 0/215 (0.0%) | 11 / 7 (5) | 0/695 | 6.0 / 14.0 (3310) |
| 02 | `stretchsync` | PASS | 0/321 (0.0%) (0) | 0/219 (0.0%) | 4 / 7 (5) | 0/695 | 13.5 / 24.2 (2519) |
| 03 | `base` | CONTROL | 11/316 (3.5%) (11) | 2/219 (0.9%) | 0 / 0 (0) | 0/695 | 5.8 / 18.7 (3301) |
| 04 | `stretch` | FAIL | 1/319 (0.3%) (0) | 0/217 (0.0%) | 5 / 6 (5) | 0/696 | 5.8 / 14.8 (3308) |
| 05 | `stretchsync` | PASS | 0/317 (0.0%) (0) | 0/218 (0.0%) | 6 / 5 (5) | 0/695 | 13.5 / 25.3 (2532) |
| 06 | `base` | CONTROL | 7/317 (2.2%) (7) | 2/219 (0.9%) | 0 / 0 (0) | 0/694 | 5.8 / 13.1 (3304) |
| 07 | `stretch` | INCONCLUSIVE: modal loops 3 != drags 4 | | | | | |
| 08 | `stretchsync` | PASS | 0/317 (0.0%) (0) | 0/217 (0.0%) | 5 / 6 (5) | 0/693 | 13.5 / 21.0 (2536) |
| 09 | `base` | CONTROL | 4/323 (1.2%) (4) | 9/217 (4.1%) | 0 / 0 (0) | 0/694 | 5.8 / 14.5 (3309) |

**Session 3** (`evidence/run3_final`)

| # | Case | Verdict | Grow artefacts (visible) | Shrink artefacts | Scaled grow / shrink (max px) | At rest | UI p95 / max ms (frames) |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 00 | `base` | CONTROL | 5/321 (1.6%) (5) | 1/217 (0.5%) | 0 / 0 (0) | 0/695 | 5.8 / 12.7 (3299) |
| 01 | `stretch` | PASS | 0/318 (0.0%) (0) | 0/217 (0.0%) | 5 / 5 (5) | 0/694 | 5.7 / 17.9 (3304) |
| 02 | `stretchsync` | FAIL | 3/318 (0.9%) (0) | 0/219 (0.0%) | 11 / 1 (5) | 0/698 | 13.6 / 24.1 (2525) |
| 03 | `stretchrdw` | PASS | 0/318 (0.0%) (0) | 0/220 (0.0%) | 6 / 5 (5) | 0/693 | 6.0 / 12.4 (3301) |
| 04 | `base` | CONTROL | 5/315 (1.6%) (5) | 2/220 (0.9%) | 0 / 0 (0) | 0/697 | 5.7 / 18.7 (3313) |
| 05 | `stretch` | FAIL | 1/319 (0.3%) (0) | 0/219 (0.0%) | 3 / 2 (5) | 0/693 | 5.7 / 17.7 (3315) |
| 06 | `stretchsync` | PASS | 0/321 (0.0%) (0) | 0/219 (0.0%) | 6 / 6 (5) | 0/694 | 13.5 / 25.7 (2527) |
| 07 | `stretchrdw` | PASS | 0/319 (0.0%) (0) | 0/214 (0.0%) | 8 / 4 (5) | 0/696 | 5.9 / 17.9 (3305) |
| 08 | `base` | CONTROL | 6/321 (1.9%) (6) | 2/220 (0.9%) | 0 / 0 (0) | 0/696 | 5.7 / 17.6 (3323) |

**Session 4, 16 ms added to every render in the size loop** (`evidence/run4_stress16`)

| # | Case | Verdict | Grow artefacts (visible) | Shrink artefacts | Scaled grow / shrink (max px) | At rest | UI p95 / max ms (frames) |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 00 | `base` | CONTROL | 312/317 (98.4%) (312) | 213/220 (96.8%) | 0 / 0 (0) | 0/694 | 33.8 / 62.6 (464) |
| 01 | `stretch` | PASS | 0/323 (0.0%) (0) | 0/215 (0.0%) | 319 / 207 (9) | 0/696 | 33.8 / 60.4 (464) |
| 02 | `stretchsync` | FAIL | 0/167 (0.0%) (0) | 0/119 (0.0%) | 160 / 112 (9) | 0/695 | 34.0 / 67.7 (459) |
| 03 | `stretchrdw` | FAIL | 4/307 (1.3%) (0) | 0/215 (0.0%) | 303 / 207 (9) | 0/694 | 33.7 / 60.9 (464) |
| 04 | `sync` | FAIL | 300/306 (98.0%) (300) | 203/211 (96.2%) | 0 / 0 (0) | 0/695 | 33.7 / 61.8 (463) |
| 05 | `syncflush` | FAIL | 160/166 (96.4%) (160) | 111/117 (94.9%) | 0 / 0 (0) | 0/696 | 33.9 / 67.3 (460) |
| 06 | `base` | CONTROL | 311/315 (98.7%) (311) | 204/212 (96.2%) | 0 / 0 (0) | 0/693 | 33.6 / 59.9 (464) |

**Session 5** (`evidence/run5_wide`)

| # | Case | Verdict | Grow artefacts (visible) | Shrink artefacts | Scaled grow / shrink (max px) | At rest | UI p95 / max ms (frames) |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 00 | `base` | CONTROL | 7/321 (2.2%) (7) | 5/220 (2.3%) | 0 / 0 (0) | 0/694 | 5.8 / 17.0 (3318) |
| 01 | `stretch` | FAIL | 1/321 (0.3%) (0) | 0/219 (0.0%) | 6 / 2 (5) | 0/696 | 5.7 / 13.2 (3316) |
| 02 | `stretchrdw` | PASS | 0/314 (0.0%) (0) | 0/220 (0.0%) | 4 / 2 (5) | 0/696 | 5.8 / 17.8 (3312) |
| 03 | `stretchsync` | PASS | 0/322 (0.0%) (0) | 0/219 (0.0%) | 3 / 2 (4) | 0/697 | 13.4 / 21.3 (2523) |
| 04 | `stretch` | PASS | 0/320 (0.0%) (0) | 0/217 (0.0%) | 6 / 7 (5) | 0/692 | 5.8 / 15.6 (3295) |
| 05 | `stretchrdw` | FAIL | 2/316 (0.6%) (0) | 0/220 (0.0%) | 13 / 2 (5) | 0/694 | 5.9 / 15.0 (3302) |
| 06 | `stretchsync` | PASS | 0/317 (0.0%) (0) | 0/220 (0.0%) | 7 / 0 (5) | 0/695 | 13.4 / 20.6 (2526) |
| 07 | `base` | CONTROL | 6/322 (1.9%) (6) | 4/218 (1.8%) | 0 / 0 (0) | 0/695 | 5.7 / 18.1 (3321) |

**Session 6, top-left corner dragged** (`evidence/run6_topleft`). P4's lines stay on the right and bottom edges, which do
not move in this drag.

| # | Case | Verdict | Grow artefacts (visible) | Shrink artefacts | Scaled grow / shrink (max px) | At rest | UI p95 / max ms (frames) |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 00 | `base` | CONTROL | 11/323 (3.4%) (11) | 7/219 (3.2%) | 0 / 0 (0) | 0/692 | 5.7 / 14.7 (3293) |
| 01 | `stretch` | PASS | 0/321 (0.0%) (0) | 0/216 (0.0%) | 6 / 4 (5) | 0/694 | 5.7 / 14.2 (3301) |
| 02 | `stretchsync` | PASS | 0/318 (0.0%) (0) | 0/218 (0.0%) | 7 / 8 (5) | 0/696 | 13.6 / 21.7 (2515) |
| 03 | `stretchrdw` | PASS | 0/322 (0.0%) (0) | 0/219 (0.0%) | 11 / 1 (5) | 0/693 | 6.0 / 16.0 (3296) |
| 04 | `base` | CONTROL | 11/321 (3.4%) (11) | 7/219 (3.2%) | 0 / 0 (0) | 0/695 | 5.7 / 12.6 (3300) |

**Session 7, 16 ms render** (`evidence/run7_over2_stress16`)

| # | Case | Verdict | Grow artefacts (visible) | Shrink artefacts | Scaled grow / shrink (max px) | At rest | UI p95 / max ms (frames) |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 00 | `base` | CONTROL | 316/322 (98.1%) (316) | 210/218 (96.3%) | 0 / 0 (0) | 0/697 | 33.6 / 60.5 (464) |
| 01 | `over2` | FAIL | 316/320 (98.8%) (0) | 210/218 (96.3%) | 0 / 0 (0) | 0/697 | 33.6 / 62.3 (463) |
| 02 | `stretch` | PASS | 0/318 (0.0%) (0) | 0/218 (0.0%) | 312 / 210 (9) | 0/697 | 33.7 / 60.2 (464) |
| 03 | `over2` | FAIL | 313/317 (98.7%) (0) | 210/218 (96.3%) | 0 / 0 (0) | 0/695 | 33.7 / 59.7 (464) |
| 04 | `base` | CONTROL | 318/322 (98.8%) (318) | 210/218 (96.3%) | 0 / 0 (0) | 0/693 | 33.8 / 61.0 (464) |

**Session 8** (`evidence/run8_over2`)

| # | Case | Verdict | Grow artefacts (visible) | Shrink artefacts | Scaled grow / shrink (max px) | At rest | UI p95 / max ms (frames) |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 00 | `base` | CONTROL | 17/320 (5.3%) (17) | 11/219 (5.0%) | 0 / 0 (0) | 0/696 | 6.2 / 16.8 (3282) |
| 01 | `over2` | FAIL | 4/315 (1.3%) (0) | 9/219 (4.1%) | 0 / 0 (0) | 0/694 | 5.9 / 16.1 (3294) |
| 02 | `base` | CONTROL | 17/322 (5.3%) (17) | 12/219 (5.5%) | 0 / 0 (0) | 0/695 | 6.1 / 14.9 (3297) |
| 03 | `over2` | FAIL | 2/319 (0.6%) (0) | 7/219 (3.2%) | 0 / 0 (0) | 0/694 | 5.8 / 18.2 (3318) |
| 04 | `base` | CONTROL | 9/320 (2.8%) (9) | 10/217 (4.6%) | 0 / 0 (0) | 0/697 | 6.1 / 21.6 (3288) |

**Session 9, 16 ms render** (`evidence/run9_over2sync_stress16`)

| # | Case | Verdict | Grow artefacts (visible) | Shrink artefacts | Scaled grow / shrink (max px) | At rest | UI p95 / max ms (frames) |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 00 | `base` | CONTROL | 314/319 (98.4%) (314) | 211/218 (96.8%) | 0 / 0 (0) | 0/696 | 33.7 / 61.0 (463) |
| 01 | `sync` | FAIL | 314/318 (98.7%) (314) | 206/214 (96.3%) | 0 / 0 (0) | 0/694 | 33.7 / 62.1 (463) |
| 02 | `over2sync` | FAIL | 308/312 (98.7%) (5) | 206/214 (96.3%) | 0 / 0 (0) | 0/693 | 33.8 / 60.6 (464) |
| 03 | `overstretch` | FAIL | 1/315 (0.3%) (0) | 0/218 (0.0%) | 310 / 211 (9) | 0/698 | 33.8 / 60.5 (463) |
| 04 | `stretch` | FAIL | 2/321 (0.6%) (0) | 0/217 (0.0%) | 317 / 209 (9) | 0/696 | 33.7 / 60.2 (464) |
| 05 | `sync` | FAIL | 309/313 (98.7%) (309) | 206/214 (96.3%) | 0 / 0 (0) | 0/694 | 33.7 / 61.0 (464) |
| 06 | `over2sync` | FAIL | 305/309 (98.7%) (2) | 206/214 (96.3%) | 0 / 0 (0) | 0/691 | 33.8 / 60.5 (464) |
| 07 | `base` | CONTROL | 317/321 (98.8%) (317) | 209/217 (96.3%) | 0 / 0 (0) | 0/694 | 33.7 / 60.7 (464) |

**Session 10** (`evidence/run10_overstretch`)

| # | Case | Verdict | Grow artefacts (visible) | Shrink artefacts | Scaled grow / shrink (max px) | At rest | UI p95 / max ms (frames) |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 00 | `base` | CONTROL | 12/320 (3.8%) (12) | 5/219 (2.3%) | 0 / 0 (0) | 0/695 | 5.8 / 17.0 (3296) |
| 01 | `overstretch` | PASS | 0/317 (0.0%) (0) | 0/218 (0.0%) | 13 / 10 (5) | 0/696 | 6.0 / 14.0 (3286) |
| 02 | `stretch` | FAIL | 1/321 (0.3%) (0) | 0/218 (0.0%) | 21 / 9 (5) | 0/695 | 6.2 / 18.2 (3281) |
| 03 | `overstretch` | FAIL | 0/314 (0.0%) (0) | 0/219 (0.0%) | 12 / 10 (5) | 2/694 | 6.0 / 14.0 (3292) |
| 04 | `base` | CONTROL | 8/317 (2.5%) (8) | 5/219 (2.3%) | 0 / 0 (0) | 0/697 | 6.0 / 19.8 (3295) |
| 05 | `overstretch` | FAIL | 0/321 (0.0%) (0) | 0/219 (0.0%) | 6 / 6 (5) | 3/693 | 6.0 / 11.9 (3311) |
| 06 | `base` | CONTROL | 15/320 (4.7%) (15) | 9/220 (4.1%) | 0 / 0 (0) | 0/696 | 6.0 / 18.3 (3295) |

About `overstretch`'s transitions:
- At normal speed, the 5 at-rest artefacts (session 10) are all post-phase samples taken right after WM_EXITSIZEMOVE. The
  right marker is 55 px inside the edge and the static tick is 24 px out of place: for a frame, the content visual has
  lost its 56 px offset while the surface is still the extended one.
- Under stress (session 9), 7 of 87 rows before the grow phase show the tick up to 655 px out of place. That is the
  WM_ENTERSIZEMOVE change. `row_samples.csv` keeps only the moving phases, so those rows are counted in the log's
  STRETCH-TICK line.

## Frame-time impact

Measured inside the modal size loops over all 4 drags: UI-thread animation frames, and render-thread frames.

| Case | UI frame interval median / p95 / max | UI frames | Added per resize step |
| --- | --- | --- | --- |
| `base` | 4.1 / 5.7 to 6.2 / 12.7 to 21.6 ms | about 3300 | none |
| `stretch` | 4.1 / 5.6 to 6.2 / 13.2 to 18.2 ms | about 3300 | none |
| `stretchrdw`, `sync` | 4.1 / 5.8 to 6.0 / 12.4 to 17.9 ms | about 3300 | RedrawWindow, 3.5 ms median in WM_SIZE (it replaces the WM_PAINT that follows anyway) |
| `stretchsync`, `syncflush`, `validrects` | 4.2 / 13.2 to 13.6 / 17.3 to 25.7 ms | about 2530 (-24 %) | RedrawWindow 3.5 ms + DwmFlush 7.4 ms median |
| `flush` | 4.2 / 14.2 / 19.4 ms | 2528 | DwmFlush, 7.1 ms |
| `align` | 4.3 / 21.1 / 29.5 ms | 1990 (-40 %) | three hooks, 19 ms; the window edge trails the pointer by 4 px (median) |
| `hide`, `overrender`, `over2`, `overstretch` | as `base` | as `base` | none per step; `over2` adds 0.7 to 1.0 ms at WM_ENTERSIZEMOVE and 0.5 to 1.0 ms at exit |

Under the 16 ms stress:
- Every case gets about 66 WM_SIZING per drag instead of 100.
- In the `sync` family, WM_SIZE blocks for 23 to 30 ms per step.
- With DwmFlush, the edge trails the pointer by 5 px (median).
- `stretchsync` collects only half the samples (167 grow).

## Rest geometry, DWM animations and Snap

**Rest geometry.** Sessions 3, 8 and 10, every case "SAME as base":

| State | Client | Markers | Ticks |
| --- | --- | --- | --- |
| Restored | 1320 x 860 | L0 T1 R0 B0 | 660 and 1260 |
| Maximised | 2560 x 1392 at (0,0) | L0 T0 R0 B0 | 660 and 1260 |
| Restored again | as restored | as restored | as restored |

This covers `stretch`, `stretchsync`, `stretchrdw`, `over2` and `overstretch`. None of them scales or shifts the content
at rest, in either state.

**DWM animations and Snap.** These were checked with the remedy installed, on a static all-magenta window, using P7's
detectors, for:
- every case of session 1 (base x 3, sync, syncflush, flush, align, validrects, overrender, hide, stretch, stretchsync);
- `base` and `stretchrdw` in session 3;
- `base` and `over2` in session 8;
- `base` and `overstretch` in session 10.

Results in every one of them:
- Maximise, restore, minimise and restore-from-minimise each showed 7 to 9 distinct intermediate areas over 62 to 65
  captures (animated).
- IsZoomed and IsIconic were as commanded.
- The Snap Layouts flyout changed 40.09 % of the zone after the hover (0.00 % parked against parked).
- Dragging the caption to the top edge gave IsZoomed = True, frame [0,0]-[2560,1392].
- Animation control: 0 intermediate areas with DWMWA_TRANSITIONS_FORCEDISABLED.

## Top-left drags and the slow renderer

- **Top-left corner** (session 6):
  - The right and bottom edges do not move. The unremedied window still shows a strip there in 3.4 % of grow samples,
    because the stale, smaller content is drawn from the new origin.
  - `stretch`, `stretchsync` and `stretchrdw`: 0 of 961 grow and 0 of 653 shrink samples, with scaled frames as in
    bottom-right drags.
  - The moving left and top edges were not measured, because P4's lines are on the right and bottom. Type B would show
    there.
- **16 ms render** (sessions 4, 7 and 9):
  - `base`: 98.5 % of grow and 96.5 % of shrink samples.
  - Every timing remedy: 96 to 99 %.
  - `stretch`: no type A frame in 962 grow and 650 shrink samples. It had 2 type B frames, and 98.5 % of its samples show
    a scaled frame (up to 9 px at x 1260).
  - `over2`: 98.7 % by P4's detector, none visible.

## How the production chrome should implement it

This is an ENGINEERING DECISION candidate for the orchestrator.

Adopt `stretch` without the sync or flush hooks:
- The hooks do not lower the residual at normal speed: `stretch` 5/2558, `stretchrdw` 2/1267, `stretchsync` 3/2552.
- DwmFlush costs 24 % of UI frames during a drag.
- Under load, blocking inside WM_SIZE makes type B more frequent.

Apply it to every resizable top-level window of Marrow (Avalonia 12.1.2, WinUI composition, the P7 / ED-063 chrome):

1. **Wait for the first frame.** The platform surface exists only after the renderer's first `CreateRenderTarget`. After
   that, on the UI thread, resolve:
   - `window.PlatformImpl` (Avalonia.Win32 `WindowImpl`);
   - its field `_glSurface` (`WinUiCompositedWindowSurface`);
   - that surface's field `_window` (`WinUiCompositedWindow`; null before the first frame);
   - the window's fields `_shared` (`WinUiCompositionShared`, with properties `SyncRoot` and `Compositor`), `_visual`
     (`IVisual`) and `_surfaceBrush` (`ICompositionSurfaceBrush`).
2. **Change the composition under `lock (SyncRoot)`.** The render thread takes the same lock for every frame, so no
   frame is drawn half-changed.
   - `_visual.SetSize(Vector2.Zero)`, then `QueryInterface<IVisual2>(_visual).SetRelativeSizeAdjustment(Vector2.One)`.
   - `_surfaceBrush.SetStretch(CompositionStretch.Fill)` (the value that reads back as `Fill`; it was `Uniform`).
   - Create `ICompositor.CreateSpriteVisual()`, query `IVisual`, and store it in `WinUiCompositedWindow._visual`.
     `ResizeIfNeeded`, called with the scene size by every `BeginDraw`, then sizes this detached visual instead. Keep the
     real visual in the chrome's own field.
3. **Verify.** Read back `RelativeSizeAdjustment` (1,1) and `Stretch` Fill. If a member is missing or a read-back
   differs (for example after an Avalonia upgrade), log it once and leave the window as Avalonia made it. A test pinned
   to Avalonia 12.1.2 must fail in that case, so the regression is visible.
4. **On window close**, before Avalonia disposes the surface: under the lock, put the real visual back into
   `WinUiCompositedWindow._visual` (its `Dispose` releases `_visual`), and dispose the detached one.
5. **No Win32 message handling.** `stretch` needs none; the ED-063 WndProc hook stays as it is. Nothing changes at rest
   (rest geometry above), so maximise, Snap and DPI keep Avalonia's own paths.

Alternative without reflection: carry a patch of Avalonia.Win32's `WinUiCompositedWindow`. Create `_visual` with
`RelativeSizeAdjustment (1,1)` and the brush with `Stretch = Fill`, and in `ResizeIfNeeded` size only the rounded-corner
clip geometry, not `_visual`. Either route is an internal engineering choice; the port has no rule on it yet.

What `stretch` does not do:
- It does not remove type B frames: 0.16 % of moving samples at normal speed, one frame each, 3 to 9 px of whatever is
  behind the window.
- About 2 % of moving samples show a one-frame scaled frame instead.
- `over2`, `overstretch` and `over2sync` show that extending the client area does not reach the type B band.
- An idea not tested here: DWM's own system backdrop (DWMWA_SYSTEMBACKDROP_TYPE) might paint the hole with the
  material, because DWM draws it with the frame. It would still hide the band, not remove it.

## Owner decision needed

Yes. OD-0013 says the chrome is not accepted until the strip is gone, and that a failure returns to the owner. The best
remedy measured removes the ordinary strip and every shrink clip. It does not remove the rare type B frame, which comes
from DWM and also exists without the remedy. And it turns each remaining lag frame into a one-frame scaled frame.

Options:
- (a) Adopt `stretch` and accept the type B frames and the scaled frames as a named deviation.
- (b) Adopt `stretch` now and keep the chrome open until a remedy outside the app removes type B, for example a change in
  Avalonia or in how DWM is told the new size.
- (c) Keep the chrome blocked.

## Deviations from the packet and from the plan, with reasons

1. **Task item 1** asked for rendering at the new size in WM_SIZING or WM_WINDOWPOSCHANGING. That is not possible
   through Avalonia (mechanism item 5). It was built in WM_SIZE (`sync`) instead, and the unremedied window was shown to
   render synchronously already.
2. **Task item 5**: the WM_ERASEBKGND, class brush and DWMWA_CAPTION_COLOR variants were not built. The reason is an
   INFERENCE (no redirection surface; the caption is covered). `hide` was built instead and is reported as hiding.
3. **Session 2 is INCONCLUSIVE.** The owner moved the pointer during case 07, and the probe of that time still pressed
   its button. From session 3 on, the probe has:
   - an owner guard: it remembers where its own last input left the cursor, and any other movement stops all input for
     the session;
   - an idle gate: it starts only after 60 s without input.

   The probe's own injected input counts toward the idle gate, so sessions started at least 70 s apart. The owner was
   active between sessions several times, which delayed starts by up to 11 minutes. No session from 3 on was interrupted.
4. **Post hoc additions, labelled as such:**
   - rest geometry (session 3);
   - the wide diagnostic scan with pixel rows (sessions 4 to 10);
   - the cases `over2`, `overstretch`, `over2sync` and `overstretchrdw`;
   - stress sessions: the `--slowrender` knob existed as the first invocation's diagnostic, and its first recorded runs
     are sessions 4, 7 and 9;
   - the "pre" phase in the static-tick readout (sessions 9 and 10);
   - `--snapfirst` and `--snapcases`.

   The pooled table is also post hoc. The per-instance verdicts follow the pre-registered rule.
5. **Base rates are lower than P4's.** The unremedied rates here (2.54 % pooled grow; 0.3 to 5.3 % per instance) are
   below P4's dense runs (4 to 12 %). The probe differs from P4 in these ways:
   - a sample is labelled by the phase in which its capture ended, with a 50 ms settle;
   - there are 4 drags per case;
   - the window carries P7's chrome declarations.

   The control condition held in every session.
6. **Animation PNGs are cropped** to the probe's own checker window, because a monitor capture also holds whatever else
   is on screen. Session 1's six existing PNGs were cropped in place (320 x 180 to 249 x 164). Only numbers are kept from
   the maximised rest-geometry lines, which cross the whole monitor.
7. **Program.cs header corrected.** It said WM_PAINT is dispatched only when the loop goes idle. It now notes that this
   happens after every step (measured).
8. **Builds.** Each build is of this folder's Program.cs with `dotnet build porting\probes\p4b\p4b.csproj -c Release
   -nologo`, 0 warnings, 0 errors.

   | Sessions | Build (UTC) | What it added |
   | --- | --- | --- |
   | 1 and 2 | first invocation's builds of earlier states of the file (header notes mark the later additions) | |
   | 3 | 22:19:54, from the file of 22:19:49 | |
   | 4 to 6 | 22:26:55 | the wide scan |
   | 7 and 8 | 22:46:12 | `over2` |
   | 9 and 10 | 23:07:06, from the final file of 22:57:14 | the `overstretch` offset, `over2sync` and the "pre" readout |

   The detector, the thresholds and the phase rule were not edited in this invocation.

**Not established:**
- DPI other than 100 %;
- a 60 Hz display (this one runs at 239 Hz on the RTX 4080);
- the light theme;
- the moving left and top edges;
- DirectComposition or redirection-surface composition modes, which P4 showed do not give acrylic or remove the strip;
- a real Marrow scene: the probe's render takes about 3.5 ms, and the 16 ms stress stands in for a heavier one.

## Environment

- `cmd /c ver`: `Microsoft Windows [Version 10.0.26200.9457]`; `dotnet --version`: `10.0.401`
- Avalonia `12.1.2+d3c867a9e2de379249b03dbeb3495bd7f076a81a`, Avalonia.Win32 the same; SkiaSharp
  `3.119.4+release-3.119.4.f568ac94...` (printed by the probe)
- GPUs:
  - NVIDIA GeForce RTX 4080 (driver 32.0.16.1692, 2560 x 1440 at 239 Hz)
  - AMD Radeon(TM) Graphics (32.0.21036.18)
  - Parsec Virtual Display Adapter (0.45.0.0)
- Primary work area 2560 x 1392; DPI 96
- Dark theme (`AppsUseLightTheme=0`), transparency effects on
- SPI_GETANIMATION iMinAnimate=1; client-area animation 1; window arranging 1; dock moving 1; snap sizing 1
- `MARROW_PORT_DATA_DIR`: a fresh `%TEMP%\marrow-p4b-<run>[-<id>]` folder per session, printed in each log

## Commands and their decisive output

```
# pre-flight (this invocation, 22:10Z)
PS> query user                                   -> omera  console  3  Active
PS> Get-Process LogonUI                          -> (none)
PS> Get-Process p4b                              -> (none)

# build (after each source change)
PS> dotnet build porting\probes\p4b\p4b.csproj -c Release -nologo    -> Build succeeded. 0 Warning(s) 0 Error(s)

# session 1 (first invocation; command reconstructed from the log header), exit 0
p4b --out evidence\run1                          (default 12 cases, reps 4, Snap on)
  CONTROLS ... flagged=True; every base case showed a grow strip=True (#0: 8/320, #6: 1/321, #11: 1/319); animation control True
  REMEDIES passing ...: [stretch, stretchsync]   RESULT: a remedy passes

# session 2 (first invocation), exit 2
p4b --out evidence\run2_confirm --cases base,stretch,stretchsync,base,stretch,stretchsync,base,stretch,stretchsync,base --nosnap
  CASE 07 stretch INCONCLUSIVE: modal loops 3 != drags 4        RESULT: INCONCLUSIVE

# session 3, exit 0 (MARROW_PORT_DATA_DIR=%TEMP%\marrow-p4b-run3-241885de)
p4b --out evidence\run3_final --cases base,stretch,stretchsync,stretchrdw,base,stretch,stretchsync,stretchrdw,base --reps 4 --snapcases base,stretchrdw --idlegate 60
  input idle 70.4 s; every GEOM CASE ... SAME as base; animation control True; RESULT: a remedy passes

# session 4, exit 0
p4b --out evidence\run4_stress16 --cases base,stretch,stretchsync,stretchrdw,sync,syncflush,base --reps 4 --nosnap --nogeom --slowrender 16 --idlegate 60
# session 5, exit 0
p4b --out evidence\run5_wide --cases base,stretch,stretchrdw,stretchsync,stretch,stretchrdw,stretchsync,base --reps 4 --nosnap --nogeom --idlegate 60
  stretch: WIDE marker-missing samples=1: [grow rnone->widenonex0 b1 tick2 1265]
# session 6, exit 0
p4b --out evidence\run6_topleft --cases base,stretch,stretchsync,stretchrdw,base --reps 4 --nosnap --nogeom --edge tl --idlegate 60
# session 7, exit 0
p4b --out evidence\run7_over2_stress16 --cases base,over2,stretch,over2,base --reps 4 --nosnap --nogeom --slowrender 16 --idlegate 60
# session 8, exit 1
p4b --out evidence\run8_over2 --cases base,over2,base,over2,base --reps 4 --snapcases base,over2 --idlegate 60
# session 9, exit 1
p4b --out evidence\run9_over2sync_stress16 --cases base,sync,over2sync,overstretch,stretch,sync,over2sync,base --reps 4 --nosnap --nogeom --slowrender 16 --idlegate 60
  overstretch: STRETCH-TICK ... pre rows=87 tickDisplaced=7 (max 655px ...) ... post rows=387 tickDisplaced=1 (max 24px ...)
# session 10, exit 0
p4b --out evidence\run10_overstretch --cases base,overstretch,stretch,overstretch,base,overstretch,base --reps 4 --snapcases base,overstretch --idlegate 60
  CASE 03 overstretch ... post 2/378;  CASE 05 overstretch ... post 3/378; RESULT: a remedy passes

# smoke runs of this invocation (output in %TEMP%, deleted; not evidence)
22:15:40Z  --cases base,stretch --reps 1 --nosnap --idlegate 30   -> input idle 0.4 s: refused, no window, exit 2
22:20:52Z  --cases base,stretchrdw --reps 1 --nosnap              -> geometry check works; exit 1 (n < 200)
22:21:33Z  first start of session 3                               -> input idle 14.8 s (the smoke run's own input): refused, exit 2
22:47:05Z  case list split by PowerShell                          -> "unknown case", before any window
22:47:17Z  --cases base,overstretch,over2 --reps 1 --nosnap       -> overstretch squeezed (tick at 634, not 661); led to the Size offset
```

Full logs: `evidence/<run>/run_log.txt`. Every session ended with `windows closed; cursor restored to (x,y) (was (x,y))`.

## Files in this folder

- `Program.cs`, `p4b.csproj`: the probe. Usage: `p4b --out <dir> [--cases a,b,c] [--reps 4] [--nosnap] [--snapfirst]
  [--snapcases a,b] [--nogeom] [--edge br|tl] [--slowrender <ms>] [--idlegate <s>] [--cornercap]`. Exit codes: 0 a
  remedy passes, 1 none passes, 2 INCONCLUSIVE, 4 crash. `--cornercap` is a diagnostic that was not used in any
  recorded session.
- `ildump/Program.cs`, `ildump/ildump.csproj`: the IL lister used to read Avalonia 12.1.2. It is a reading aid, not part
  of the probe build.
- `evidence/avalonia_il_excerpts.txt`: the IL excerpts cited above.
- `evidence/<run>/run_log.txt` and `ctl_inset_corner_br.png`.
- `evidence/<run>/<NN>_<case>/` holds:
  - `dense_edges.csv`: every dense sample; wide-scan columns from session 4 on;
  - `artefacts.csv`: every artefact side, with band and neighbour colours; pixel rows from session 4 on;
  - `row_samples.csv`, `messages.csv`, `hooks.csv`, `ui_frames.csv` and `render_frames.csv` (first drag),
    `appearance_lines.csv`;
  - `kymo_right.png` and `kymo_bottom.png`: one image row per stable sample of reps 1 and 2, with red margin marks on
    artefacts;
  - `rest_corner_br.png`;
  - `anim_*_mid_*.png` and `snap_flyout_hover.png` where Snap ran.
- The largest PNG is under 20 KB. The folder totals about 30 MB, mostly CSVs.

## Cleanup

{{CLEANUP}}
