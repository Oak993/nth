# Decisions needed from the owner, 2026-09-22

Every item below is something the Swift source cannot settle and a Windows user would see. Each has
my recommendation. You can accept them all in one answer, or name the numbers you want changed.
Nothing here is implemented until you rule; the rest of the port keeps moving meanwhile.

"Deviation" means the Windows build knowingly differs from Droppy Code. Each accepted one is named in
the final report, never passed off as parity. The glass ruling (OD-0005, Acrylic) already caps the
final status at "VERIFIED WITH USER-APPROVED DEVIATIONS", so these do not lower it further.

## A. Upstream behaviours that can lose data or expose secrets (the 12 quarantined items)

Parity would copy each of these faithfully. I recommend fixing all of them except A7.

| # | What Droppy Code does | Recommendation |
| --- | --- | --- |
| A1 (BLK-0001) | Deleting a custom MCP server: one click, no confirmation even when "Confirm before deleting" is on, and its stored keys are destroyed | Ask for confirmation, as the other six delete actions do |
| A2 (BLK-0002) | One malformed custom-MCP entry hides all custom servers, and the next save overwrites the file with the empty list | Do not overwrite after a failed load; show the error |
| A3 (BLK-0003) | A checkpoint can silently capture only part of the tree; a later revert then deletes files it wrongly thinks are new | Treat a partial capture as "no checkpoint" |
| A4 (BLK-0004) | Revert can act on the wrong paths when the project is a subfolder of a repo with diff.relative set, deleting files it never touched | Derive paths from the repo root correctly |
| A5 (BLK-0005) | Migration from the app's old name "Cody" | Not applicable on Windows: no older install can exist. Do not migrate from your other Marrow app |
| A6 (BLK-0006) | If protected storage refuses an API key, the key is saved in plain text | Fail closed: show the error, store nothing |
| A7 (BLK-0007) | Plan limits are read from each CLI's own credential file and used to query that provider's usage endpoint | **Keep as upstream** (it is how the plan-limit feature works) |
| A8 (BLK-0008) | In Auto mode, `sort --output=`, `find -fprintf`, `git config/tag/remote add` count as read-only and run with no prompt and no checkpoint | Correct those three misclassifications |
| A9 (BLK-0009) | "Remove project" has no confirmation even when "Confirm before deleting" is on | Ask for confirmation |
| A10 (BLK-0010) | An unreadable library.json starts the app empty, and the first save wipes the index of all your projects and threads | Do not overwrite after a failed load; show the error |
| A11 (BLK-0011) | An unreadable "custom title" flag lets the auto-titler overwrite a name you typed | Treat unreadable as "custom", never overwrite |
| A12 (BLK-0012) | Disconnecting an MCP server reports success even if deleting its stored key failed | Report the failure |

## B. Text that names the Mac or Droppy

| # | Source text | Recommendation |
| --- | --- | --- |
| B1 | "Reveal in Finder", "Reveal worktree in Finder", "Show in Finder" (5 places) | "Reveal in File Explorer", "Reveal worktree in File Explorer", "Show in File Explorer" |
| B2 | "Follows your Mac" (System theme) | "Follows Windows" |
| B3 | "Keys stay in your Keychain" and 4 more Keychain strings | Store secrets in Windows Credential Manager and say "Credential Manager" in all five |
| B4 | "this Mac" (5 strings) | "this PC" |
| B5 | "Run `brew install uv` in Terminal" | "Run `winget install astral-sh.uv` in Terminal" (Windows 11's app is also called Terminal) |
| B6 | "Run `cmd login` in Terminal" and the other "in Terminal" strings | Keep verbatim: Windows 11's app is also called Terminal |
| B7 | Shortcut glyphs such as ⌘⌥⇧ and "Add ⌘, ⌃ or ⌥ so typing still works." | Windows notation (Ctrl+Alt+Shift) and "Add Ctrl or Alt so typing still works." |
| B8 | "macOS or Droppy Code already uses this chord." | "Windows or Marrow already uses this chord.", same reserved list |
| B9 | Key labels show the US key position, not the active keyboard layout | Keep upstream: US position labels |
| B10 | Merge requests say "Opened by Droppy Code once its Hydra team finished." | "Opened by Marrow once its Hydra team finished." |
| B11 | Agent CLIs are told the client is name "droppy-code", title "Droppy Code" | "marrow" / "Marrow" |
| B12 | Terminal sets TERM_PROGRAM="Droppy Code" | "Marrow" |
| B13 | Project scripts file is droppy-code.json | Keep reading droppy-code.json, so existing Droppy projects work |
| B14 | 8 strings contain an em dash (—); two of them are parsed back by the merge popover | Keep all 8 verbatim |
| B15 | About and tour text that is untrue on Windows: "built in Swift with Liquid Glass", "for Mac", "Signed and notarized disk image", "One native macOS window", "Move it to your Applications folder", the SwiftTerm credit | Truthful Windows rewrites, keeping each sentence's structure. Credits name the components the Windows build actually ships |

## C. Windows behaviour the source never needed

| # | Question | Recommendation |
| --- | --- | --- |
| C1 | Closing the window: macOS keeps the app alive with no window; on Windows that makes it unreachable | Closing the main window quits the app, keeping the upstream wait while a Hydra merge is running |
| C2 | Quit: no quit label or shortcut exists in source | Window close button and Alt+F4 only; no invented menu item |
| C3 | macOS menu-bar items (About, Edit, Window, and so on) come from the OS, not the source | No menu bar. Every command the source defines stays on its shortcut and in the command palette; text fields keep standard Ctrl+C/V/X/Z/A |
| C4 | Where the close, minimise and maximise buttons sit | Keep them on the LEFT as round buttons at the source's exact positions, matching the Mac look |
| C5 | Which shell a terminal tab and project scripts use | PowerShell 7 if installed, otherwise Windows PowerShell |
| C6 | Updates: Droppy's feed only has Mac .dmg files, and it is Droppy's feed, not yours | Update check shows its "Check failed" state until you have your own Windows release feed; the switch is one setting |
| C7 | Your installed Electron Marrow owns %LOCALAPPDATA%\Marrow, the Start menu name "Marrow" and the Marrow installer ID | The port installs and stores data under "Marrow Code" (folder %LOCALAPPDATA%\Marrow Code, Start menu "Marrow Code"), so the two apps never touch each other |

## D. Icons and fonts (Apple's cannot ship on Windows)

| # | Question | Recommendation |
| --- | --- | --- |
| D1 | SF Symbols (70 symbols, 206 sites) | Microsoft Fluent UI System Icons (MIT licence), mapped one by one in a published table |
| D2 | SF Pro text face | Segoe UI Variable, Windows' own system font. Probe P9 measured it about 3 to 5% narrower than SF Pro, so some lines wrap at different words; that becomes a named deviation. Alternative: bundle Inter (free, OFL), which looks closer to SF Pro; I would measure it the same way first |
| D3 | SF Mono | Cascadia Mono (ships with Windows) |

## E. Live checks against your signed-in CLIs

The contract requires each provider to be checked against the real CLI, not only against recorded
fixtures. That means sending a few tiny prompts ("reply with OK") through each signed-in CLI, inside a
throwaway test repository, using a small amount of your plan quota. Without this, those provider
checks stay BLOCKED, never passed.

| # | Question | Recommendation |
| --- | --- | --- |
| E1 | May the port's verification send a few minimal prompts through your signed-in CLIs (Claude Code, Codex, Copilot, Cursor, OpenCode, Grok, Devin, Pi, Command Code, Antigravity, whichever are installed) in a scratch repo? | Yes, minimal prompts only, and never any credential file read by the test harness itself |
