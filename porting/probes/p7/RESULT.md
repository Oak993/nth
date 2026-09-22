# P7 result: FAIL (packet configuration, verbatim). Remedy measured: PASS on `WindowDecorations.Full` only

Task packet T-M0-006. Decisive run: `evidence/run4/`, 2026-09-22 19:38:23Z to 19:42:56Z, on the owner's
unlocked desktop (OD-0007), by a worker subagent resuming an earlier session of this task. Earlier runs
`evidence/run2/` (19:19Z to 19:22Z) and `evidence/run3/` (19:26:14Z to 19:30:26Z) were made by that earlier
session with previous revisions of `Program.cs`; run3 reproduces every verbatim result of run4.

> P7: ExtendClientAreaToDecorationsHint plus ExtendClientAreaTitleBarHeightHint plus WindowDecorations; app-drawn
> caption buttons on the LEADING edge at the derived traffic-light constants; Win32Properties.SetNonClientHitTestResult
> declaring those controls Caption / Close / MinButton / MaxButton; and a second window with WS_EX_NOACTIVATE
> (AddWindowStylesCallback) plus a WM_MOUSEACTIVATE / MA_NOACTIVATE hook (AddWndProcHookCallback) holding a password
> field. PASS: drag, double-click-to-maximise and the system context menu all work from the app-drawn region; the
> app-drawn buttons minimise, maximise and close; AND the user can type into the non-activating panel's field.
> Record, per WindowDecorations value, the Snap Layouts flyout and the DWM min/max/restore animations (#21212).

## Verdict

**FAIL, measured.** With exactly the packet's mechanism (hit-test declarations only), on every WindowDecorations
value:

1. Drag and double-click maximise / restore **work** from the app-drawn caption.
2. The app-drawn **buttons do nothing**. The click arrives as `WM_NCLBUTTONDOWN HTMINBUTTON` (or HTMAXBUTTON,
   HTCLOSE) and no `WM_SYSCOMMAND` ever follows. IsIconic, IsZoomed and IsWindow are unchanged.
3. **Right-click on the caption shows no system menu.** `WM_NCRBUTTONDOWN HTCAPTION` and `WM_CONTEXTMENU` arrive;
   no `#32768` menu opens. The OS-framed control on the same desktop opens it.
4. **The panel does not stay non-activating.** Windows honours `MA_NOACTIVATE`: the click does not activate the
   panel. But Avalonia's own pointer handling then calls Win32 `SetFocus` on the panel HWND, and that activates it.
   Recorded stack of the activation: `WindowImpl.AppWndProc > UnmanagedMethods.SetFocus > WM_NCACTIVATE 1 /
   WM_ACTIVATE 1 / WM_SETFOCUS`. The foreground moved host to panel in all six unhooked panel cases, including the
   one where the field never asks for focus. The typed text does arrive, but only because the panel became the
   active window, which is exactly what a non-activating panel must not do.

**Remedy, measured in the same run (ENGINEERING DECISION candidates, not the packet's configuration):**

- `WindowDecorations.Full` + extend + the same declarations + a WndProc hook that handles the caption buttons, the
  caption context menu and the keyboard window menu itself: **everything passes**, including DWM
  minimise / maximise / restore animations and the Snap Layouts flyout over the leading-edge maximise button.
- Panel: a thread-local `WH_CBT` hook that refuses `HCBT_ACTIVATE` / `HCBT_SETFOCUS` for the panel HWND keeps the
  host in the foreground **and** the typed text arrives in the panel's field. Its control (same hook, field that
  neither focuses nor receives diverted text) sends the text to the host's field instead, so the mechanism is what
  routes it.

## Controls

| Control | Must | Result |
| --- | --- | --- |
| Positive: `os_standard`, OS-drawn caption (`Full`, no extend), same injected input | every chrome action works | drag, dbl-max, dbl-restore, right-click menu, min, max, restore, close, snap flyout, all 3 animations: **True** |
| Negative: `*_undeclared`, extend with no hit-test declarations, all three decorations | every chrome action fails | NCHITTEST all HTCLIENT; drag moved (0,0); min/max/close False; flyout False (zone change 0.0000): **fail as required** |
| Panel: `control_plain` (neither style nor hook) | activates | foreground host to panel: **activates** |
| Raw Win32: a window whose procedure is `user32!DefWindowProcW` (no Avalonia) | isolates the keyboard-menu path | right-click menu True; **Alt+Space False**; posted `SC_KEYMENU ' '` **True** |
| Panel remedy: `cbt_nofocus` (WH_CBT, no diversion) | text must NOT reach the panel | panel text "" , host field "Pw7!xyz": **fails as required** |

The screen was live: every chrome window's content interior read magenta fraction 1.0000 before any step.

## Numbers, run4

Constants rendered (SOURCE FACT, see "Constants"): measured geometry of the app-drawn buttons at 96 DPI, scale 1.00,
identical for every extended variant: close / min / max at DIP left **18.0 / 41.0 / 64.0**, top **16.0**, size
**14.0**; title bar height **44.0** DIP. NCHITTEST returned HTCAPTION / HTCLOSE / HTMINBUTTON / HTMAXBUTTON / HTCLIENT
at the sidebar placement (18,16) and at the collapsed placement (14,21) for every declared variant.

Chrome, per variant (`drag` = window moved exactly (+150,+90); `anim` = at least 2 distinct intermediate bounding boxes
of the window's magenta in a 1/4-scale monitor capture sampled every ~14 ms for 800 ms):

| Variant | Style | drag | dbl max / restore | right-click menu | keyboard menu | min | max | restore | close | Snap flyout (zone px changed) | anim min / max / restore |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| os_standard (control) | 0x16CF0000 | True | True / True | True | False (1) | True | True | True | True | **True** (0.1984) | True / True / True (6 / 7 / 7) |
| full_declared | 0x16CF0000 | True | True / True | **False** | False | **False** | **False** | n/a | **False** | **True** (0.4009) | no action to animate |
| borderonly_declared | 0x16870000 | True | True / True | **False** | False | **False** | **False** | n/a | **False** | **True** (0.4004) | no action to animate |
| none_declared | 0x16030000 | True | True / True | **False** | False | **False** | **False** | n/a | **False** | **False** (0.0000) | no action to animate |
| full_undeclared | 0x16CF0000 | False | False / n/a | False | False | False | False | n/a | False | False (0.0000) | none |
| borderonly_undeclared | 0x16870000 | False | False / n/a | False | False | False | False | n/a | False | False (0.0000) | none |
| none_undeclared | 0x16030000 | False | False / n/a | False | False | False | False | n/a | False | False (0.0000) | none |
| **full_hooked** | 0x16CF0000 | True | True / True | **True** | **True** | **True** | **True** | **True** | **True** | **True** (0.4009) | **True / True / True** (7 / 8 / 8) |
| borderonly_hooked | 0x16870000 | True | True / True | False (2) | False (2) | True | True | True | True | **True** (0.4007) | **False / False / False** (0 / 0 / 0) |
| none_hooked | 0x16030000 | True | True / True | False (2) | False (2) | True | True | True | True | **False** (0.0000) | **False / False / False** (0 / 0 / 0) |

(1) The Space of an injected Alt+Space never reached any window of this process, including the raw DefWindowProc
window, so a system-wide hotkey on this desktop consumes it (which process owns it: UNRESOLVED). Posting
`WM_SYSCOMMAND SC_KEYMENU, ' '` instead opens the menu on the raw window but **not** on the Avalonia
`os_standard` window (`WM_SYSCOMMAND 0xF100` arrives, no menu): Avalonia does not pass SC_KEYMENU to
DefWindowProc. The hooked variant opens it itself.
(2) `GetSystemMenu` returns NULL (logged `GetSystemMenu=0x0`): BorderOnly and None windows have no `WS_SYSMENU`.

Style bits: Full `WS_CAPTION|WS_SYSMENU|WS_THICKFRAME|MIN|MAX`; BorderOnly drops `WS_CAPTION` and `WS_SYSMENU`
(keeps THICKFRAME, MIN, MAX); None keeps only MIN and MAX. This matches AvaloniaUI/Avalonia#21212 for the animations:
they run only where `WS_CAPTION` survives. On this build (26200) the Snap Layouts flyout **does** survive BorderOnly;
it is lost on None.

Non-activating panel (secret typed with `KEYEVENTF_UNICODE`: `Pw7!xyz`; the host's own field first proved the host
keyboard path by receiving the same text in every case):

| Panel | ex style | WM_MOUSEACTIVATE | foreground before / after click | panel field | host field | panel activations |
| --- | --- | --- | --- | --- | --- | --- |
| **noactivate (packet config)** | 0x08200008 NOACTIVATE | MA_NOACTIVATE (hook) | host / **panel** | "Pw7!xyz" | "" | 1, via Avalonia `SetFocus` |
| control_plain | 0x00200008 | default | host / panel | "Pw7!xyz" | "" | 1, WA_CLICKACTIVE |
| style_only | 0x08200008 NOACTIVATE | default | host / panel | "Pw7!xyz" | "" | 1, WA_CLICKACTIVE |
| hook_only | 0x00200008 | MA_NOACTIVATE (hook) | host / panel | "Pw7!xyz" | "" | 1, via Avalonia `SetFocus` |
| noactivate_nofocus | 0x08200008 NOACTIVATE | MA_NOACTIVATE (hook) | host / panel | "" | "" | 1, via Avalonia `SetFocus` |
| noactivate_forward | 0x08200008 NOACTIVATE | MA_NOACTIVATE (hook) | host / panel | "" | "" | 1, via Avalonia `SetFocus` |
| **cbt_focus** | 0x08200008 NOACTIVATE | MA_NOACTIVATE + WH_CBT | host / **host** | **"Pw7!xyz"** | "" | 0 (CBT refused 1 HCBT_SETFOCUS) |
| cbt_nofocus (control) | 0x08200008 NOACTIVATE | MA_NOACTIVATE + WH_CBT | host / host | "" | "Pw7!xyz" | 0 |
| **cbt_forward** | 0x08200008 NOACTIVATE | MA_NOACTIVATE + WH_CBT | host / **host** | **"Pw7!xyz"** | "" | 0 |

In `cbt_focus` the panel field holds Avalonia keyboard focus (`panelField.IsFocused=True`, `panel.IsActive=False`,
`host.IsActive=True`) while Win32 focus stays in the host, and Avalonia delivers the host's key and text input to the
focused element: 7 KeyDowns and 7 TextInputs reached the panel field. In `cbt_forward` the field is never focused;
the host's tunnelling TextInput handler diverts the text to it (0 KeyDowns, 7 TextInputs). Only the WH_CBT refusal
differs between `noactivate_forward` (fails) and `cbt_forward` (passes).

`style_only` shows that `WS_EX_NOACTIVATE` alone did not stop click activation here (WA_CLICKACTIVE from the system):
Avalonia's own WM_MOUSEACTIVATE handling evidently answers first; with the hook's MA_NOACTIVATE the system no longer
activates, and the remaining activation is Avalonia's SetFocus.

## Constants (SOURCE FACT, pinned 1.6.2, `E:\Droppy\reference\droppy-code-v1.6.2`)

- `DroppyCode/UI/Chrome/Chrome.swift:11` sheetInset = 0; `:15` trafficLightDiameter = 14; `:16` trafficLightSpacing = 9;
  `:17` trafficLightPitch = diameter + spacing = 23; `:18` trafficLightLeading = 18; `:19` trafficLightTop = 16;
  `:31` chromeHorizontalPadding = 14; `:32` chromeTopPadding = 12; `:33-34, :38` capsuleHeight = 28 + 2 * 2 = 32.
- `DroppyCode/UI/Chrome/WindowChrome.swift:136-138` collapsedTrafficLightTop = 0 + 12 + (32 - 14) / 2 = 21;
  `:148-150` titlebarHeight = sheetInset + chromeTopPadding + capsuleHeight = 44; `:178-179` leading / top =
  sidebarVisible ? (18, 16) : (sheetInset + chromeHorizontalPadding = 14, 21); `:183-187` order close, miniaturize,
  zoom at x = leading + index * pitch; `:197-198` width = height = trafficLightDiameter.
- Derived spec: `porting/spec/WIN.json` lines 664-667, 687-688, 709-711 cite the same lines.
- The panels the non-activating case stands for: `DroppyCode/App/Views/Settings/MCPKeyPanel.swift:41` and
  `MCPSetupPanel.swift:66` (`styleMask: [.nonactivatingPanel, .fullSizeContentView, .titled, .closable]`), with
  `SecureField` at `MCPKeyPanel.swift:192`.

Note (SOURCE FACT): the source moves AppKit's own standard window buttons (`standardWindowButton`), so on macOS the
buttons, hover glyphs and window menu are the system's. On Windows they are app-drawn in every variant here.

## What was done, and how input was kept on our windows

Probe: `Program.cs` (one file, Avalonia 12.1.2, no theme loaded) and `p7.csproj`, built the way P10 is built: under the
repo root, so `Directory.Build.props` and `Directory.Packages.props` (central package management, Avalonia 12.1.2)
apply and no PackageReference carries a version.

- Every mouse event: `SetCursorPos` + `SendInput`, only after `GetAncestor(WindowFromPoint(p), GA_ROOT)` equalled the
  intended HWND of this process (checked before and after the move). No `GUARD refused` line occurred in run4.
- Every key: `SendInput`, only when the foreground window belonged to this process.
- `SetForegroundWindow` and `SetWindowPos` only on this process's HWNDs; `PostMessage` only to this process's HWNDs.
- Evidence: `BitBlt` / `StretchBlt` of the screen DC (`CAPTUREBLT`), which returns the DWM-composed image, cropped to
  our window's frame; PNGs encoded with SkiaSharp.
- All windows closed at the end (`windows closed (0)` open at cleanup, raw window `IsWindow=False`), cursor restored to
  where it was found: `cursor restored to (970,1401) (was (970,1401))`.
- The WH_CBT hook is thread-local (`SetWindowsHookExW(WH_CBT, proc, 0, GetCurrentThreadId())` on the UI thread) and
  removed before each panel closes.

Variants: `os_standard` (positive control), `{full,borderonly,none}_declared` (the packet), `*_undeclared` (negative
controls), `*_hooked` (remedy: declared plus a WndProc hook that swallows `WM_NCLBUTTONDOWN` on HTMIN/MAX/CLOSE and
posts `SC_MINIMIZE` / `SC_MAXIMIZE` / `SC_RESTORE` / `SC_CLOSE` on the matching `WM_NCLBUTTONUP`; answers
`WM_CONTEXTMENU` on HTCAPTION and `SC_KEYMENU ' '` with `TrackPopupMenu(GetSystemMenu)` and posts the chosen command).
Per variant, in order: NCHITTEST map at both placements, drag (+150,+90), double-click caption twice, right-click
caption, Alt+Space, posted `SC_KEYMENU`, min button with animation sampling, max and restore with sampling, 2 s hover
on the max button for Snap Layouts (decided by pixels changed below the caption inside our window, with the cursor
parked in the content as the before image; flyout dismissed afterwards, change back to 0.0000 in every case), close.

## Commands and decisive output

```
tasklist /FO CSV /NH | Select-String '"(p1|p2|...|p11|p1-glass)\.exe"'   -> no match; LogonUI not running
cd E:\Droppy\port\porting\probes\p7; dotnet build -c Debug               -> Build succeeded. 0 Warning(s) 0 Error(s)
$env:MARROW_PORT_DATA_DIR = "$env:TEMP\marrow-p7-7e6fa315"  (fresh, empty; deleted afterwards)
bin\Debug\net10.0\p7.exe --out evidence\run4                              -> exit 1 (FAIL), 19:38:23Z to 19:42:56Z
```

Decisive lines of `evidence/run4/run_log.txt`:

```
VERDICT osStandardPositiveControl=True undeclaredNegativeControlsFail=True verbatimChromePassingDecorations=[] verbatimPanelPass=False panelControlActivates=True
VERDICT remedy: hookedChromePassingDecorations=[full_hooked] forwardPanelPass=False forwardControl(nofocus)TextMissing=True
VERDICT raw DefWindowProc control: rightClickMenu=True altSpaceMenu=False postedKeyMenu=True
VERDICT remedy WH_CBT: cbtForwardPass=True (noActivate=True text="Pw7!xyz" sink="") control cbt_nofocus: noActivate=True panelTextMissing=True sink="Pw7!xyz"; cbt_focus: noActivate=True text="Pw7!xyz" sink=""
RESULT (packet configuration, verbatim): FAIL
MSGS full_declared min: WM_NCLBUTTONDOWN HTMINBUTTON                    (nothing follows)
MSGS full_declared rightclick: WM_NCRBUTTONDOWN HTCAPTION | WM_CONTEXTMENU   (no menu)
PANEL noactivate EVENT WM_MOUSEACTIVATE -> MA_NOACTIVATE (hook)
PANEL noactivate EVENT WM_ACTIVATE 1 stack: WindowImpl.WndProcMessageHandler < UnmanagedMethods.SetFocus < UnmanagedMethods.SetFocus < WindowImpl.AppWndProc < ...
MSGS full_hooked min: WM_NCLBUTTONDOWN HTMINBUTTON | HOOK swallowed ... | WM_NCLBUTTONUP HTMINBUTTON | HOOK ... -> PostMessage WM_SYSCOMMAND 0xF020 | WM_SYSCOMMAND 0xF020
```

## Environment

- Windows 11 Pro 25H2, build 26200.9457 (registry ProductName reads "Windows 10 Pro"); one monitor 2560x1440 at
  96 DPI (scale 1.00), work area 2560x1392; `SPI_GETANIMATION iMinAnimate=1`, `SPI_GETCLIENTAREAANIMATION=1`,
  double-click time 500 ms.
- GPUs: NVIDIA GeForce RTX 4080 (driver 32.0.16.1692, drives the 2560x1440 display), AMD Radeon(TM) Graphics
  (32.0.21036.18), Parsec Virtual Display Adapter (0.45.0.0).
- .NET SDK 10.0.401, runtime Microsoft.NETCore.App 10.0.12; Avalonia `12.1.2+d3c867a9e2de379249b03dbeb3495bd7f076a81a`.

## Deviations from the task text

1. **Alt+Space could not be measured on this desktop.** The Space never reached our windows or the raw
   DefWindowProc window. Substituted: posted `WM_SYSCOMMAND SC_KEYMENU, ' '` (what DefWindowProc generates for
   Alt+Space) to our own windows, with the raw window as its control. The system context menu was also driven by
   right-click, which the task allows.
2. For `os_standard` the button points come from `DWMWA_CAPTION_BUTTON_BOUNDS`, because `WM_NCHITTEST` sent by
   `SendMessage` returns DefWindowProc's classic, narrower button layout (run2 clicked the scanned "min" centre and
   hit the max button). So `os_standard map=False` in the log is expected and not a failure.
3. The hooked system menu shows **stale item states**: in `full_hooked_sysmenu_rightclick.png` and
   `full_hooked_sysmenu_keyboard.png` a restored window offers Restore enabled and Move, Size, Maximize, Close
   greyed. Choosing a menu command was not exercised (the probe closes the menu with Escape).
4. Snap-flyout PNGs show the Windows flyout's icons of other running apps; nothing else outside our window is saved.
5. Why the declared-only buttons do nothing (DefWindowProc's classic button tracking versus Avalonia's capture) was
   not isolated: UNRESOLVED. The measured fact is that no WM_SYSCOMMAND follows the WM_NCLBUTTONDOWN.

## What this does NOT establish

- Anything above 96 DPI or on a second monitor; the maximised-window geometry of a hooked `Full` window
  (GetWindowRect [-8,-8]-[2568,1400], frame = work area) is recorded but not checked against a design.
- Acrylic (`TransparencyLevelHint`), `WS_EX_NOREDIRECTIONBITMAP` and app-drawn shadow margins together with this
  chrome (P4 territory): the probe window is opaque.
- Behaviour with an Avalonia theme loaded: no theme was loaded, so no framework caption buttons existed. With `Full`
  + extend and no theme, **no OS caption glyphs were drawn** (every `*_titlebar.png`; the NCHITTEST row of every
  declared `Full` window has no trailing button zones). The ADR-0002 deviation 4 clause "'Full' draws OS caption
  glyphs over the app's own controls" is therefore not reproduced in this configuration.
- Hover and pressed glyphs on the app-drawn buttons, keyboard access to them, accessibility.
- Whether the WH_CBT remedy survives IME composition, dead keys, or a panel shown while another app is foreground
  (the macOS panel's real case: floating over another app without activating Droppy).

## Design consequence

1. **Chrome: `WindowDecorations.Full`, `ExtendClientAreaToDecorationsHint = true`, `TitleBarHeightHint = 44`**, the
   leading-edge buttons declared through `SetNonClientHitTestResult`, **plus an app WndProc hook** (via
   `AddWndProcHookCallback`) that performs the button actions, the caption context menu and the keyboard window menu,
   and that sets the system menu's item states itself before `TrackPopupMenu`. Hit-test declarations alone are not
   enough on any decoration value. This is the only measured configuration that keeps DWM animations and the Snap
   Layouts flyout over the leading-edge maximise button.
2. **BorderOnly and None are ruled out** for the main window: they lose DWM animations (#21212 confirmed), have no
   system menu, and None also loses Snap Layouts. ADR-0002 deviation 4 should be amended to these measured facts:
   the animation loss is real for BorderOnly and None; Snap Layouts survives BorderOnly on build 26200; and `Full` with
   no theme draws no OS glyphs, so the caption-glyph half of the deviation does not apply provided no theme caption
   buttons are included.
3. **Non-activating panels (MCPKeyPanel, MCPSetupPanel): WS_EX_NOACTIVATE plus MA_NOACTIVATE is not sufficient on
   Avalonia**, because Avalonia's pointer handling calls `SetFocus` on the panel HWND. The measured working mechanism
   is a thread-local WH_CBT hook refusing activation and focus for the panel HWND, after which Avalonia routes the
   active window's keyboard input to the panel's focused field. The keyboard lands in the panel; the host keeps the
   foreground.
4. The keyboard window menu needs the hook on every Avalonia window, because Avalonia does not pass SC_KEYMENU to
   DefWindowProc (raw control: it opens; Avalonia `os_standard`: it does not).

No owner decision is needed. The remedies are engineering decisions for the orchestrator to accept into ADR-0002 and
the chrome deviation list.

## Files

- `Program.cs`, `p7.csproj`: the probe. `bin/` and `obj/` deleted.
- `evidence/run4/`: decisive run. `run_log.txt` (full message traces, animation series, verdict), per-variant
  `*_titlebar.png` and `*_snap_hover.png`, `os_standard_sysmenu_rightclick.png`, `full_hooked_sysmenu_*.png`,
  `raw_sysmenu_keyboard.png`, `anim_*_mid_mask.png` (mid-transition frames of the animated cases), `panel_*.png`.
  All PNGs are under 60 KB.
- `evidence/run3/`: earlier revision, same verbatim results. `evidence/run2/`: first run, whose OS-button targeting
  error led to deviation 2.
