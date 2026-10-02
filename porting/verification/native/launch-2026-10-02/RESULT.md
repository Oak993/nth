# Clean-data launch capture, 2026-10-02 (OD-0055)

Records: PR-LAUNCH-071, UI-LAUNCH-001, UI-LAUNCH-002 (verification method: capture on a clean data directory).

## How it was taken

- The port's own Debug build (`src/Marrow.App/bin/Debug/net10.0-windows10.0.19041.0/Marrow.App.exe`, commit `ab1fab247`),
  never the owner's separate Electron app "Marrow".
- `MARROW_DEV_DATA_ROOT` pointed at a fresh, empty temporary folder (ED-212), every `*_API_KEY` variable removed. The
  owner's `%LOCALAPPDATA%\Marrow Code` was not touched.
- `capture.py` (this folder) starts the app, lists the process's visible top-level windows every 30 ms for 20 s (title,
  class, frame, client size, DPI), grabs the screen at set times, then sends WM_CLOSE to each window and lists what the
  data folder holds. Display: 2560 x 1440 at 100 % (96 DPI), work area 2560 x 1392, 240 Hz.
- Screenshots are cropped to the app's own pixels (the boot mark; the main window's frame), because the clear windows
  show the desktop behind them.

## What it shows (second, warm run; `launch.json`)

| t (ms) | Window | Frame (px) | Client |
| --- | --- | --- | --- |
| 678 | boot stage ("Window", full work area, click-through) | 0,0 - 2560,1392 | 2560 x 1392 |
| 5150 | main window "Marrow Dev" (shown cloaked for the hand-over, ED-236) | 612,164 - 1948,1032 | 1320 x 860 |
| 6694 | welcome tour | 950,402 - 1610,989 | 660 x 587 |

- **UI-LAUNCH-001:** the first-ever main window has a 1320 x 860 content area, centred horizontally (612 + 1336 / 2 =
  1280) with ED-225's vertical bias; it blurs what is behind it under the app's scrim (`main-and-tour-9.0s.png`).
  The first run after a rebuild showed the main window at 13.2 s (cold start); the warm run at 5.2 s.
- **UI-LAUNCH-002:** the tour appears over the main window once, after the main window (1.5 s after its cloaked show,
  which includes ED-103's hand-over and AppDelegate.swift:25's 700 ms); its card is legible over the blurred window.
  Closed natively (WM_CLOSE) it does not write hasSeenTour (OD-0039: the Swift's windowWillClose only clears the
  controller).
- **PR-LAUNCH-071:** no other window appears: no sign-in, account, terms or data-folder step. The data folder afterwards
  holds only `UserDefaults.json`, `library.json`, `mcp/proxy-port` and `notification-icon.png`. The app exited with
  code 0 on the main window's close.

## Earlier run (before ED-236, same day)

The same capture found that the main and tour windows had no blur (OD-0014's Acrylic was not built: WindowManager STOP
#5), so the tour's text ran into whatever was behind it. ED-236 built it; this capture is after that fix.
