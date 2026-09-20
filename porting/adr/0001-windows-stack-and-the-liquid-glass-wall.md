# ADR-0001: The Windows stack, and the Liquid Glass wall

Status: **BLOCKED ON OWNER DECISION.** No stack chosen. No Windows code written.
Date: 2026-09-20. Authority: PORT_CONTRACT section 9 and section 10; owner ruling OD-0003.

## What was evaluated

Eight candidate stacks, one researcher each against the 12 constraints derived from the pinned
source, then an adversary pass whose job was to break the leaders rather than confirm them.
Full evidence: `porting/critiques/stack_eval.json`.

| Stack | Verdict | Why |
| --- | --- | --- |
| WinUI 3 / Windows App SDK 2.0 | DISQUALIFIED | the backdrop API and the shader API cannot be bridged |
| Native Win32 + D3D11/12 + DirectComposition | VIABLE, then BROKEN | only route to desktop pixels, but the route is privacy-gated |
| Avalonia 12.1.2 | UNRESOLVED | can lens over app-drawn content, cannot reach the desktop |
| Qt 6.11 | VIABLE (conditional), then BROKEN | same wall, plus QProcess cannot express the process model |
| Electron 44.x | VIABLE, then BROKEN | one window-wide enum cannot express 22 per-site materials |
| Tauri 2 | DISQUALIFIED | WebView2 content can never obtain desktop pixels |
| Flutter 3.47.2 | DISQUALIFIED | can author the material, cannot reach the desktop |
| WPF | DISQUALIFIED | no pixel access, and the one backdrop API is documented unreadable |

## The finding that decides everything

**No Windows API hands an application the desktop pixels behind its own window in a form a custom
shader can read.** This is a platform fact, not a property of any stack. Every candidate converged
on it independently.

Two families of API exist and neither does the job:

1. **Backdrop APIs are closed.** DWM's system backdrop is a five-value enum
   (`DWMSBT_AUTO/NONE/MAINWINDOW/TRANSIENTWINDOW/TABBEDWINDOW`) with no parameters.
   `Windows.UI.Composition.Compositor.CreateHostBackdropBrush()` does sample behind the window,
   which is the macOS semantic, but its own Remarks state: *"The app cannot read the pixel data
   back."* It may only be fed into WinComp's closed list of 14 effect types. WinUI 3's lifted
   compositor does not expose `CreateHostBackdropBrush` at all, and Microsoft's own documentation
   states the UWP `BackgroundSource` host-backdrop mode *"is not available in WinUI 3."*
   `DesktopAcrylicController` exposes four knobs: `TintColor`, `TintOpacity`, `LuminosityOpacity`,
   `FallbackColor`. Win2D's `PixelShaderEffect` is marked `[NoComposition]` and cannot consume a
   `CompositionBackdropBrush`. The two halves are mutually exclusive by design.

2. **Screen capture reaches the pixels, but is not a material API.** `Windows.Graphics.Capture`
   via `IGraphicsCaptureItemInterop::CreateForMonitor` genuinely delivers the composed desktop as
   an `ID3D11Texture2D`. The adversary pass established why this is not a fidelity solution:
   - **The capture indicator is not the app's to remove.** `GraphicsCaptureSession.IsBorderRequired`
     needs user consent; the documentation states that if access is denied, *"setting this property
     to false will succeed, but the value will be ignored and the border will be displayed during
     subsequent captures."* A permanent coloured border around the window is a larger visual
     deviation than the one it was meant to avoid.
   - **Unpackaged apps are consent-store gated** at
     `HKCU\...\CapabilityAccessManager\ConsentStore\graphicsCaptureProgrammatic\NonPackaged`, and
     the permission is revocable at any time with no fallback that preserves the look.
   - **Self-capture has no acceptable branch.** A monitor capture includes our own window, so
     feeding it into our own glass is an infinite mirror. The only documented suppression is
     `SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)`, after which the window **does not
     appear in the user's own screenshots, screen recordings or screen shares at all.** For an app
     whose users demo and screen-share their coding sessions, that is a severe functional cost.
   - **The backdrop is architecturally one to two frames late**, because we render from an
     already-composed frame and are then composed ourselves in the next one. Dragging the window
     would smear the backdrop.
   - **The capture item dies on any display topology change** and must be rebuilt. This very machine
     carries a Parsec virtual display adapter alongside the Radeon, so that path is live here.
   - **Multi-window makes it worse**: every floating panel is its own HWND needing its own capture
     session and its own exclusion.

## Why this matters so much here

`UI/Chrome/WindowChrome.swift:116-117` sets `window.isOpaque = false` and
`window.backgroundColor = .clear`. The app's own wallpaper layer is **optional**
(`UI/Theme/Wallpaper.swift`: `hasWallpaper` is nil until the user picks a file, and the stored file
name defaults to empty). So in the **default configuration the glass sits directly over the
desktop**, which is exactly the case Windows cannot reproduce. The owner-supplied field capture
`field-02-translucent-system-theme.webp` shows precisely this: the window nearly transparent over a
Golden Gate desktop with desktop widgets legible through it.

Where the user *has* set a wallpaper, the glass sits over app-drawn content, and every candidate
that can run a custom shader can reproduce the material there faithfully.

## Independent corroboration from the owner's own work

The Electron adversary found that the owner's separate Marrow project has failed to settle this
question across two verification blocks: `PROJECT_STATE.md:88` records *"The glass looked flat in
the screenshot; whether transparency effects..."*, and that project's `CLAUDE.md` still carries it
as the standing open question: *"whether the acrylic backdrop shows the desktop through the window
on the owner's PC."* That is field evidence from a shipping Windows build, not speculation.

## What this means for OD-0003

OD-0003 is: *"I want the exact same look as the droppy operating on MacOS, zero guesswork."*

**As stated, that is not achievable on Windows for the desktop-backed case.** Not by effort, not by
budget, and not by picking a different stack. Contract section 10 anticipates exactly this: *"Any
visible compromise needs my explicit acceptance and must remain identified as a deviation."*

This ADR therefore stops rather than choosing. Choosing a stack now would silently pick one of the
compromises below on the owner's behalf, which sections 1, 2 and 10 all forbid.

## The three real options

### Option A: capture-based lensing over the true desktop
True refraction and chromatic fringing over desktop content, closest to the macOS pixels.
Costs, all documented above and none removable: a consent prompt; a coloured capture border unless
consent is granted and honoured; the app absent from the user's own screenshots and screen shares;
one to two frames of backdrop lag; continuous full-screen capture cost; rebuild on every display
change; per-panel capture sessions. Realistically requires the native Win32 + D3D stack.

### Option B: exact lensing over an app-drawn backdrop, system material over the desktop
The app always draws its own backdrop layer, and the glass lenses over it exactly, with the full
shader treatment the source specifies, at every one of the 22 sites. Where the user has set no
wallpaper, the window uses a Windows system material (Acrylic) instead of showing the true desktop.
Cost: **the default configuration changes**, because upstream defaults to a clear window over the
desktop. That is a visible product deviation, and it is the honest one. No capture, no consent, no
border, no lag, and the app behaves normally in screenshots and screen sharing.

### Option C: Option B by default, Option A as an opt-in setting
Ships B as the default and offers capture-backed glass as a setting the user turns on, accepting the
border and screenshot consequences knowingly. Costs: both code paths, plus a setting that does not
exist upstream, which is itself an invented feature under section 2 unless the owner authorises it.

## Not yet decided, and deliberately so

Stack selection is downstream of this ruling, because the options do not all admit the same stacks.
The adversary pass also broke the non-glass criteria differently per stack: Qt's `QProcess` cannot
express SIGTERM-then-SIGKILL or process-group kill and cannot carry a ConPTY
`PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE`; Electron cannot express 22 per-site materials through one
window-wide `backgroundMaterial` enum, cannot build the non-activating `NSPanel`-equivalent panels,
and its `titleBarOverlay` has no side option for leading-edge caption buttons; native Win32 pays for
everything by hand-building every control, the UI Automation tree, IME and the terminal emulator.
Those trade-offs are recorded in `porting/critiques/stack_eval.json` and will be re-weighed against
whichever option the owner accepts.
