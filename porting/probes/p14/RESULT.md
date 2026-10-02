# P14 result: a window shown cloaked (DWMWA_CLOAK) and then uncloaked has no tint-before-blur gap; Window.Opacity 0 does not hide the Acrylic

Run 2026-10-02 on the owner's PC (Windows 11 26200, NVIDIA RTX 4080, 100% scale, dark theme, transparency effects on),
in a normal desktop session, by a unit agent (branch `unit/build-acrylic`). Question (OD-0041, probe P13 left it open): how
can the ~150-200 ms between an Acrylic window's tint and its blur be kept out of sight at the boot hand-over?

Method: a 1000 x 700 opaque window of black and white vertical stripes (6 px wide, period 12 px) lies topmost on the
desktop. The subject window (600 x 400, AcrylicBlur, transparent background, content = black at alpha 0x61 standing for the
app-drawn scrim, `Topmost`) is shown over it. A background thread reads a 120 x 60 block at the subject's middle from the
real, composed screen (BitBlt with CAPTUREBLT, one read every ~10 ms) and logs its mean luma and "edge" (the mean absolute luma
step between horizontally adjacent pixels): the bare stripes read mean 127.5 and edge 42.9; a blur reads edge 0.0; the
scrim over a blur reads mean 79.0. "Tinted-but-sharp" is a sample with edge at least 3 and a mean shifted by more than 8: the scrim over
stripes that are still sharp. Log: `evidence/p14-log.txt`; source: `Program.cs` (`P14_MODES` picks the modes).

| Mode | What it does | Result |
| --- | --- | --- |
| `visible` | plain `Show()` (the P13 case) | first change +80 ms (tint, stripes still sharp), first blurred frame +313 ms: **15 of 95 samples tinted-but-sharp** (about 230 ms) |
| `content0` | plain `Show()` with the root content at opacity 0 | the window shows as a plain frosted pane from +265 ms (mean 127.0, edge 0.0): the blur is visible without any content |
| `window0` | `Window.Opacity = 0` before `Show()`, set to 1 1.2 s later | **the Acrylic still shows**: while at opacity 0 the block reads mean 127.0, edge 0.0 (blurred) from +267 ms. `Window.Opacity` does not hide the backdrop, so it cannot cover the blur's start-up |
| `window0-borderless` | as `window0` with `WindowDecorations.None` | the same, and the blur arrives at +19 ms: a borderless window has no gap |
| `layered0` | `WS_EX_LAYERED` + `SetLayeredWindowAttributes(alpha 0)` before `Show()` | the material is still displayed (mean 79.0, edge 0.0 while "hidden"); the layered alpha does not hide it (as P3 found for alpha 255) |
| `offscreen` | `Position` (-20000, -20000) before `Show()` | still displayed in part (mean 108.9, edge 34.1); not a hide |
| **`cloak:N`** | `DwmSetWindowAttribute(DWMWA_CLOAK = 13, 1)` before `Show()`, `0` after N ms | **while cloaked the block reads exactly the bare stripes (mean 127.5, edge 42.9 for N = 300 and 1200 ms); on uncloak the first changed sample (+3 to +18 ms) is already blurred and tinted; 0 tinted-but-sharp samples of 47 for N = 0, 300, 1200** |

Also found in the same runs: with `cloak:0` (cloak, `Show()`, uncloak at once) there is no gap either; and a fresh Settings
window of the port's own factory shown plainly has the gap (55 samples, about 225 ms, native test scratch run) while a window shown
a second time has none. So the gap belongs to a window's first appearance; cloaking over it removes it.

`DwmSetWindowAttribute(DWMWA_CLOAK)` returned `S_OK` before the window was shown; the window stays `IsVisible` (Avalonia), is composed by DWM
(the blur is ready when it is uncloaked), is not displayed and is not hit-tested while cloaked. `WS_EX_LAYERED` was false throughout, as in P3.

Design consequence (ED-236): show the main window cloaked while the stage covers it, uncloak it at ED-103 (5)'s hand-over.

Not established: behaviour on another GPU or driver, at another DPI, with "Transparency effects" off, or with "Animation effects"
off (the gap may then not exist, but the cloak does no harm); whether a cloaked, shown window has a taskbar button (not checked: the probe
windows had none).
