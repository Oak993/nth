# P3 result: FAIL for the packet's configuration (blurred, not tinted); the ADR's scrim-over-AcrylicBlur route PASSES

Task packet T-M0-002. Run 2026-09-22, 18:52Z to 19:05Z, by a worker subagent on the owner's real, unlocked desktop
(owner decision OD-0007). The session was active (`query user`: console, Active; no LogonUI process), and every
control capture showed the probe's own checkerboard, so the screen was composited throughout.

> P3: Set Window.TransparencyLevelHint = AcrylicBlur on an Avalonia 12 window with ExtendClientAreaToDecorationsHint
> true, over a high-contrast background, with the window keeping focus. PASS: desktop content is visibly blurred
> and tinted through the window, AND the capture's sha256 differs from a bare grab. Assert the window is never
> WS_EX_LAYERED. FAIL: fall back to DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_TRANSIENTWINDOW).

## Verdict

| Question | Result | Evidence |
| --- | --- | --- |
| Avalonia AcrylicBlur window is never `WS_EX_LAYERED` | **True**. `GWL_EXSTYLE = 0x00200108` at every sample (after show, focused, unfocused, refocused), for the bare and the scrim window | `ASSERT ... LAYERED=False` lines |
| `ActualTransparencyLevel` | `AcrylicBlur` (hint honoured) | log |
| Capture sha256 differs from the bare grab | **True**. `73a65904...` against `2af8b2a6...` | log |
| Background visibly **blurred** through the window | **True**. Contrast-normalised sharpness 0.038 (threshold 0.25), block correlation 0.903 (threshold 0.50) | `acrylic_focused.png` |
| Background **tinted** by the material | **False**. Mean RGB moved 1.31 (threshold 8): (128.4, 153.7, 255.0) to (127.4, 152.9, 255.0) | `acrylic_focused.png` |
| **Packet configuration (bare AcrylicBlur)** | **FAIL** on the tint half only | exit code 3 |
| AcrylicBlur + the source's dark scrim drawn by the app (ADR-0002 deviation 1) | **PASS**: sharpness 0.053, block corr 0.903, mean shift 107.1 | `acrylic_scrim_focused.png` |
| Material after the window loses focus | Avalonia AcrylicBlur **does not flatten**: interior pixels byte-identical focused vs unfocused (only the 1 px frame changes) | `interiorSha256` equal |
| Packet fallback: DWMSBT_TRANSIENTWINDOW on the Avalonia window | **PASS while focused** (sharpness 0.051, mean shift 81.4), but **flattens to solid grey (84,84,84) when unfocused** | `fallback_dwm/` |

In one sentence: Avalonia's AcrylicBlur is a real, focus-independent blur of whatever is behind the window and is
not a layered window, but its recipe adds no measurable tint, so the tint has to be the app-drawn scrim ADR-0002
already plans; the DWM system backdrop tints but flattens whenever the window is inactive.

## The capability, the controls, and why the verdict splits

Capability under test: the window's own material shows the content behind it blurred and tinted, and the window
never carries `WS_EX_LAYERED`.

The metrics come from the interior of the window's DWM frame bounds, excluding 48 px on each side and 96 px at the
top: 850 x 489 px. Luma is 0.2126 R + 0.7152 G + 0.0722 B. The thresholds were fixed before the first run and
never changed:

- **sees through**: Pearson correlation of the 16 x 16 block-mean luma with the bare control is at least 0.50.
- **blurred**: the squared-gradient ratio against the control, divided by slope^2, is at most 0.25. Slope is the
  regression gain of the capture's block means on the control's. The division stops a flat tint, which lowers
  contrast, from passing as blur. The tint-only control reads exactly 1.00000.
- **tinted**: the Euclidean distance between the capture's mean RGB and the control's is at least 8.

Controls, each measured against the bare grab `control_hidden`, where no probe window covers the region:

| Capture (default composition, run 3) | block corr | slope | sqGrad ratio | sharpNorm | mean shift | blurred & tinted | Expected |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `control_hidden_repeat` (null control, bare vs bare) | 1.0000 | 1.0000 | 1.00000 | 1.00000 | 0.00 | False, sha identical | fail |
| `ctl_none_opaque` (hint None) | 0.0000 | 0 | 0 | inf | 324.25 (black) | False | fail |
| `ctl_transparent_scrim_no_blur` (hint Transparent + the same scrim, no blur) | 1.0000 | 0.6517 | 0.42471 | 1.00000 | 106.61 | False | fail |
| **`acrylic_focused` (packet configuration)** | 0.9027 | 0.1292 | 0.00064 | 0.03830 | **1.31** | **False** | the question |
| `acrylic_unfocused` | 0.9027 | 0.1292 | 0.00064 | 0.03830 | 1.31 | False | (flattening?) |
| **`acrylic_scrim_focused`** (AcrylicBlur + source scrim) | 0.9034 | 0.0843 | 0.00038 | 0.05348 | **107.06** | **True** | the ADR route |
| `acrylic_scrim_unfocused` | 0.9034 | 0.0843 | 0.00038 | 0.05348 | 107.06 | True | (flattening?) |
| `ctl_layered_acrylic` (AcrylicBlur, then WS_EX_LAYERED + LWA_ALPHA 255 forced) | 0.9027 | 0.1292 | 0.00064 | 0.03830 | 1.31 | False | see note |

- The **tint-only control** discriminates blur from tint. It has the same scrim, the same pixels behind it and the
  same geometry, and differs only in hint Transparent against AcrylicBlur. Its sharpNorm is 1.00000; the acrylic
  scrim window's is 0.05348. Its mean shift, 106.61, matches the acrylic scrim window's 107.06, which confirms that
  almost all of the scrim window's tint is the app-drawn layer. The packet's bare configuration fails the tint test
  in the same way that this control passes it.
- The **opaque control** shows the "sees through" test failing when the window hides the background.
- **Layered note:** forcing `WS_EX_LAYERED` with `SetLayeredWindowAttributes(alpha 255)` onto an Avalonia window
  after creation did **not** disable the material. The capture is byte-identical to `acrylic_focused` (raw sha
  `73a65904...`). The same happened on the DWM path: `ctl_layered_dwmbackdrop` was byte-identical to
  `dwmbackdrop_focused`. So the trap that `acrylic_window.ps1` hit (WinForms `TransparencyKey`, an LWA_COLORKEY
  layered window with a GDI redirection surface) does **not** reproduce by flipping the style bit on these
  `WS_EX_NOREDIRECTIONBITMAP` windows. This control therefore does not discriminate, and the verdict does not rely
  on it. The `WS_EX_LAYERED` assertion is still made and holds.

Visual check, done by opening the PNGs: `control_hidden.png` is a sharp white and blue 96 px checkerboard.
`acrylic_focused.png` is the same pattern, heavily blurred, with no darkening. `acrylic_scrim_focused.png` is the
blurred pattern under a dark grey veil. `ctl_transparent_scrim_no_blur_focused.png` is the sharp pattern under the
same veil. `fallback_dwm/dwmbackdrop_unfocused.png` is flat grey.

Stability: each subject was captured twice while focused, 400 ms apart, and the two were byte-identical every time.
The refocused captures were byte-identical to the first focused ones. Run 2 (18:58Z) and run 3 (19:02Z), in
separate processes, gave the same `acrylic_focused` raw sha, `73a65904...`.

## Focus and flattening

| Subject | Interior focused == unfocused | Unfocused interior |
| --- | --- | --- |
| Avalonia AcrylicBlur (bare) | **True** (full frame differs only in the 1 px border) | still blurred, byte-identical |
| Avalonia AcrylicBlur + scrim | **True** | still blurred and tinted |
| DWMSBT_TRANSIENTWINDOW (bare) | False | solid (84, 84, 84), lumaStd 0.00 |
| DWMSBT_TRANSIENTWINDOW + scrim | False | solid (59, 59, 59), lumaStd 0.00 |

Focus was established with `SetForegroundWindow` on the probe's own HWND and confirmed with
`GetForegroundWindow() == hwnd` before and after every focused capture. Windows never refused it, so no synthetic
click or key was ever sent. Unfocusing gave focus to the probe's own checkerboard window, which owns the subject
window and so stays below it.

Why this matters: the packet warned that "background acrylic is documented to flatten to a solid colour on
deactivation" and asked for behaviour pinned rather than tracked. The DWM system backdrop does flatten, as
measured. Avalonia's AcrylicBlur does not. Its `WS_EX_NOREDIRECTIONBITMAP` style and its independence from
activation are consistent with an in-process composition backdrop brush rather than DWM's system backdrop. That is
an inference, not something this probe inspected.

## Fallback run (packet's FAIL path), `evidence/fallback_dwm/`

Command: `p3.exe --mode dwm-backdrop`. The subject window has hint `Transparent`, and
`DwmSetWindowAttribute(hwnd, 38, DWMSBT_TRANSIENTWINDOW=3)` is called on `TryGetPlatformHandle().Handle`, which
returned `hr=0x00000000`.

| Capture | block corr | sharpNorm | mean RGB | mean shift | blurred & tinted |
| --- | --- | --- | --- | --- | --- |
| `dwmbackdrop_focused` | 0.9087 | 0.05070 | (81.1, 106.6, 208.4) | 81.39 | **True** |
| `dwmbackdrop_unfocused` | 0.0000 | inf | (84.0, 84.0, 84.0) | 189.93 | False (flattened) |
| `dwmbackdrop_scrim_focused` | 0.9101 | 0.06856 | (56.8, 73.4, 139.7) | 157.73 | True |
| `dwmbackdrop_scrim_unfocused` | 0.0000 | inf | (59.0, 59.0, 59.0) | 228.48 | False (flattened) |
| controls (none, tint-only, bare grab) | as in the main table, byte-identical hashes | | | | all False |

The packet's second fallback, forcing `Win32CompositionMode.RedirectionSurface`, was not run. It is conditional on
the DWM fallback "showing nothing", and the DWM fallback did show the material.

## Design consequence

1. **Keep `TransparencyLevelHint = AcrylicBlur` as the window edge material, and draw the tint in the app.** This
   is ADR-0002 deviation 1 as written ("the source's tint and its 0.26 dark / 0.18 light scrim are drawn by the
   app as a layer over the system material"). P3 now shows that this deviation is not optional. Avalonia's recipe
   contributes a blur with a mean shift of 1.3/441, so without the app layer the window is an untinted frosted
   pane. With the source's dark scrim drawn by the app, measured at mean shift 107, the requirement is met.
2. **Prefer Avalonia AcrylicBlur over the DWM system backdrop.** The DWM backdrop tints but flattens to a solid fill
   whenever the window is inactive. Avalonia's material stays identical when inactive, which is the pinned
   behaviour the packet asked for, at no extra cost.
3. **The layered-window trap is guarded but not reproducible here.** Production code should still assert that
   `GWL_EXSTYLE` has no `WS_EX_LAYERED` on every glass window, which is cheap. P3 shows that setting the bit later
   does not break these windows. The historical failure came from WinForms `TransparencyKey`, so the rule for
   production is: never use colour-key transparency or a GDI layered window for glass.
4. **Not established** (open for later probes): behaviour under live resize with ExtendClientArea (P4); DPI other
   than 100%; a second monitor; the light theme (the OS was dark, `AppsUseLightTheme=0`) and its 0.18 white scrim;
   the transparency-effects-off setting (it was on, `EnableTransparency=1`); battery saver; the blur radius and
   exact recipe compared with macOS glass, which OD-0001 leaves unverifiable. What the macOS source's window glass
   does when the window is inactive is also not established here, so "stays blurred when inactive" is recorded as
   Avalonia's behaviour, not as parity.

## Deviations from the packet text, with reasons

1. **Capture method.** Captures use `BitBlt` from the screen DC with `SRCCOPY | CAPTUREBLT` inside the probe
   process, over the window's `DWMWA_EXTENDED_FRAME_BOUNDS`, instead of Alt+PrtScn. The orchestrator's instructions
   required this: all input must be targeted and the owner's clipboard left alone. It returns the DWM-composed
   image, and keeping focus was verified by `GetForegroundWindow` rather than assumed.
2. **Background.** The background is a second window of the probe's own: a 1500 x 1000 opaque checkerboard of
   96 px white `#FFFFFF` and blue `#0033FF` squares. The desktop wallpaper was not used or changed.
3. **Window size.** The windows are 960 x 640 physical px (946 x 633 frame bounds), not the source's 1320 x 860, so
   that the window sits well inside the checkerboard. Size is not under test.
4. **Run 1 (18:57Z) was INCONCLUSIVE and is kept in `evidence/run1/`.** A `Render` override on the `Window`
   subclass drew nothing, and the checkerboard window came out plain white, which the probe's own `screenLive`
   check caught (lumaStd 0.00). The fix, in use since run 2, draws through a child `Control` supplied by
   `FuncControlTemplate`, because no theme is loaded.
5. **Scrim case added after run 2, labelled post hoc.** After run 2 showed the bare material untinted, the source's
   scrim case and a tint-only control matched to that scrim were added, together with an interior-only hash. The
   thresholds were not changed. The scrim is a SOURCE FACT at the "stock" midpoint: black at
   `scrim(for: 0.5, isDark: true) = 0.26`, then `Chrome.glassTint` (#1E1E20) at 0.12 (`Chrome.swift:211-214`,
   `:256-258`, `:264`; `AppTheme.swift:114`, `:116`, `:185`, pinned 1.6.2). Whether 0.5 is the shipped default
   backdrop setting was not checked here (UNRESOLVED for this probe).
6. **Layered control.** The layered control sets `WS_EX_LAYERED` after creation with LWA_ALPHA. It does not
   reproduce the WinForms colour-key trap and does not discriminate; see the layered note above.
7. **No theme loaded.** `AppBuilder.Configure<Application>().UsePlatformDetect()` loads no FluentTheme. The theme
   variant followed the OS: `ActualThemeVariant=Dark`.
8. **Windows used.** All probe windows are `Topmost` and hidden from the taskbar. The subject windows are owned by
   the checkerboard window.
9. **Composition mode.** Avalonia's default composition mode was used, and the code does not record which one
   Avalonia chose. Every probe window had `WS_EX_NOREDIRECTIONBITMAP`.
10. **Cursor.** No synthetic mouse or key input was sent. After each focus step the probe called `SetCursorPos` to
    the cursor position it recorded at start-up, which does nothing unless the cursor moved during the run. The
    start positions differed between runs ((2276,612), (2170,831), (861,1321)), so the pointer was in use between
    runs. Within a run, that call would have put back any pointer movement made during the run. The final position
    matched the recorded start in every run.
11. **Build.** The probe is a console `Exe` (not `WinExe`) so its output could be captured, built in Release in
    `p3/bin`, and deleted at the end. Its csproj follows P10's pattern: no `Version` attributes, versions from
    `Directory.Packages.props`, and `Avalonia.Desktop` added for the real Win32 backend.

## Environment

- `cmd /c ver`: `Microsoft Windows [Version 10.0.26200.9457]`
- `dotnet --version`: `10.0.401`
- Avalonia `12.1.2+d3c867a9e2de379249b03dbeb3495bd7f076a81a`, SkiaSharp `3.119.4+release-3.119.4.f568ac94...` (printed by the probe)
- GPUs (`Win32_VideoController`): NVIDIA GeForce RTX 4080 (driver 32.0.16.1692, driving 2560 x 1440), AMD Radeon(TM)
  Graphics (32.0.21036.18), Parsec Virtual Display Adapter (0.45.0.0)
- Primary work area 2560 x 1392 physical px, DPI 96 (100%) for every probe window
- Theme: dark (`AppsUseLightTheme=0`, `SystemUsesLightTheme=0`), transparency effects on (`EnableTransparency=1`)
- Session: `query user` gave `omera console 3 Active`; no LogonUI process
- `MARROW_PORT_DATA_DIR` was a fresh `%TEMP%\marrow-p3-*` folder for each run (`marrow-p3-1093583852`,
  `marrow-p3-run2` to `marrow-p3-run5`), and all were deleted at the end
- Before each run, `Get-Process` showed no `p2`/`p3`/`p4`/`p7` process

## Commands and their decisive output

```
# pre-flight (18:52:20Z)
PS> Get-Process | ? { $_.ProcessName -match '^(p2|p3|p4|p7)' ... }     -> (none)
PS> query user                                                     -> omera  console  3  Active

# build (after each source change)
PS> dotnet build porting\probes\p3\p3.csproj -c Release -nologo
  first attempt: error CS0246 'ImmutableSolidColorBrush' (missing using Avalonia.Media.Immutable);
                 error CS0618 'Window.SystemDecorations' is obsolete: 'Use WindowDecorations instead.'
  then:          p3 -> ...\p3\bin\Release\net10.0\p3.dll   Build succeeded.  0 Error(s)

# run 1 (18:57:15Z): INCONCLUSIVE, exit 2
PS> p3.exe --out porting\probes\p3\evidence            (moved to evidence\run1)
  CONTROL control_hidden meanRGB=(255.0,255.0,255.0) lumaStd=0.00 twoValuedFraction=1.0000 ...
  screenLive (control shows our checkerboard, not a uniform or foreign image) = False
  RESULT: INCONCLUSIVE

# run 2 (18:58:35Z): exit 1, same acrylic hashes as run 3; log kept in evidence\run2
# run 3 (19:02:04Z): the reported run, exit 3
PS> $env:MARROW_PORT_DATA_DIR = "$env:TEMP\marrow-p3-run3"; p3.exe --out porting\probes\p3\evidence
  acrylic: ... hint=[AcrylicBlur] ActualTransparencyLevel=AcrylicBlur IsExtendedIntoWindowDecorations=True ActualThemeVariant=Dark ex=0x00200108 LAYERED=False NOREDIRECTIONBITMAP=True
  acrylic: focus via SetForegroundWindow; foreground==self True; IsActive=True
  CAPTURE acrylic_focused   rect=[807,376]-[1753,1009] 946x633 rawSha256=73a659040a282100657b3d0d854a6a52df46b30804349be4d7b50bc39890d5ba interiorSha256=18433215c8d0568c...
  CAPTURE acrylic_unfocused rawSha256=2e35f710879415d5... interiorSha256=18433215c8d0568c...   (interior identical)
  CAPTURE control_hidden    rawSha256=2af8b2a6842c1a594ee4727909f6af62fbad436461e481d830a0e4442853a907 (repeat identical)
  CONTROL control_hidden meanRGB=(128.4,153.7,255.0) lumaStd=100.05 twoValuedFraction=1.0000 sqGrad=834.81 repeatIdentical=True metricsInterior=850x489
  screenLive ... = True
  METRIC acrylic_focused       meanRGB=(127.4,152.9,255.0) lumaStd=12.58 pixCorr=0.8044 blockCorr=0.9027 slope=0.1292 sqGradRatio=0.00064 sharpNorm=0.03830 meanDelta=1.31 ... blurred=True tinted=False => blurredAndTinted=False
  METRIC acrylic_scrim_focused meanRGB=(86.9,103.5,170.0) lumaStd=8.20 pixCorr=0.8056 blockCorr=0.9034 slope=0.0843 sqGradRatio=0.00038 sharpNorm=0.05348 meanDelta=107.06 ... => blurredAndTinted=True
  METRIC ctl_none_opaque_focused ... blockCorr=0.0000 ... => blurredAndTinted=False
  METRIC ctl_transparent_scrim_no_blur_focused ... blockCorr=1.0000 slope=0.6517 sqGradRatio=0.42471 sharpNorm=1.00000 meanDelta=106.61 ... => blurredAndTinted=False
  FOCUS acrylic: interior focused==repeat True; interior unfocused==focused True (full frame False); interior refocused==focused True
  LAYERED CONTROL: ctl_layered_acrylic interior == acrylic_focused interior: True
  ASSERT acrylic GWL_EXSTYLE after show|focused|unfocused|refocused: 0x00200108 LAYERED=False ...
  SUMMARY neverLayered=True actualAcrylic=True screenLive=True focusHeld=True negativeControlsFail=True bare: blurred=True tinted=False blurredAndTinted=False ... | scrim: blurredAndTinted=True unfocused=True refocused=True
  RESULT bare acrylic (packet configuration): FAIL
  RESULT acrylic + app-drawn source scrim (ADR-0002 deviation 1): PASS
  windows closed; cursor restored to (861,1321) (was (861,1321))
  exit=3

# run 4 (19:02:48Z) and run 5 (19:03:44Z): packet fallback, exit 0; run 5 is the one kept (adds the layered DWM control)
PS> $env:MARROW_PORT_DATA_DIR = "$env:TEMP\marrow-p3-run5"; p3.exe --out porting\probes\p3\evidence\fallback_dwm --mode dwm-backdrop
  dwmbackdrop: DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_TRANSIENTWINDOW) hr=0x00000000
  METRIC dwmbackdrop_focused   meanRGB=(81.1,106.6,208.4) blockCorr=0.9087 sharpNorm=0.05070 meanDelta=81.39 => blurredAndTinted=True
  METRIC dwmbackdrop_unfocused meanRGB=(84.0,84.0,84.0) lumaStd=0.00 blockCorr=0.0000 => blurredAndTinted=False
  METRIC ctl_layered_dwmbackdrop_focused ... identical to dwmbackdrop_focused (raw sha 6e3183f6...)
  FOCUS dwmbackdrop: interior focused==repeat True; interior unfocused==focused False ...
  RESULT bare dwmbackdrop (packet configuration): PASS
  exit=0
```

The full per-run logs are `evidence/run_log_default_avalonia.txt` (run 3), `evidence/fallback_dwm/run_log_default_dwm-backdrop.txt`
(run 5), `evidence/run1/run_log_default_avalonia.txt` and `evidence/run2/run_log_default_avalonia.txt`.

## Files in this folder

- `p3.csproj`, `Program.cs`: the probe. Usage: `p3 --out <dir> [--mode avalonia|dwm-backdrop] [--composition default|winui|dcomp|redirection]`.
- `evidence/`, run 3: `acrylic_{focused,unfocused}.png`, `acrylic_scrim_{focused,unfocused}.png`, `control_hidden.png`,
  `ctl_none_opaque_focused.png`, `ctl_transparent_scrim_no_blur_focused.png`, and the log.
- `evidence/fallback_dwm/`, run 5: `dwmbackdrop_{focused,unfocused}.png`, `dwmbackdrop_scrim_{focused,unfocused}.png`,
  the controls, and the log.
- `evidence/run1/`: the INCONCLUSIVE first run (white checkerboard). `evidence/run2/`: its log only.
- PNGs that were byte-identical to a kept PNG (`*_repeat`, `*_refocused`, the layered controls) were deleted after a
  `Get-FileHash` comparison. Their hashes remain in the logs. The largest remaining PNG is 227,710 B, and the folder
  totals about 1.07 MB.

## Cleanup

- `p3/bin` and `p3/obj` were deleted.
- `%TEMP%\marrow-p3-*` (5 folders) were deleted.
- Every probe window was closed by the probe's `finally` block, and each `p3.exe` run exited. `Get-Process`
  afterwards showed no `p2`/`p3`/`p4`/`p7` process.
- The cursor ended each run at the position recorded at its start. The wallpaper, theme and system settings were not
  touched. Nothing was committed.
