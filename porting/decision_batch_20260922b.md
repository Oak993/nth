# Decisions needed from the owner, batch 2, 2026-09-22

The spec refresh for 1.6.2 applied batch 1. It left these points that no ruling or source line settles.
Each has my recommendation. New wording copies the style upstream already uses (for example the
thread delete confirmation: a question, one note, and a red action button). Accepting them all is one
answer; or name the numbers to change.

## E. Wording for the prompts and errors your safety fixes create

Batch 1 said "ask for confirmation" or "show the error", but upstream has no text for these, because
upstream never shows them.

| # | Where | Recommended wording |
| --- | --- | --- |
| E1 (A1) | Deleting a custom MCP server | Question "Delete this server?", note "“<name>” and the values it keeps in Credential Manager will be removed.", red button "Delete server" |
| E2 (A9) | Remove project (it also deletes the project's threads) | Question "Remove this project?", note "“<name>” and its threads will be removed. Files in your project stay as they are.", red button "Remove project" |
| E3 (A2) | custom-mcp.json cannot be read | Shown in place of the custom server list: "custom-mcp.json could not be read, so your custom servers are hidden. The file will not be changed until it can be read." |
| E4 (A10) | library.json cannot be read | Shown in the main window at launch: "Marrow could not read library.json, so your projects and threads are hidden. The file will not be changed until it can be read." |
| E5 (A6) | Credential Manager refuses an API key | Under the key field: "Credential Manager did not accept this key, so it was not saved." |
| E6 (A12) | Disconnect could not delete stored values | "Disconnected, but Credential Manager did not remove this server’s stored values." |

## F. Remaining "Droppy Code" text

One rule instead of forty: every remaining "Droppy Code" / "droppy-code" the app shows or sends out
becomes "Marrow" / "marrow", EXCEPT the two machine contracts existing users' files depend on
(droppy-code.json and the DROPPY_CODE_PROJECT_ROOT variable, already kept by B12/B13). This covers:
the pull request body "Opened from Marrow."; the git checkpoint author "Marrow" <marrow@localhost>
and message "Marrow checkpoint"; "Marrow: a Hydra head's starting point"; the "[Marrow]" notes sent to
models; the system prompt "You are Marrow's <identity>, working inside <folder> on Windows."; the
client name sent to MCP servers, OAuth and Copilot; the network User-Agent "Marrow/<version>"; the temp
folder marrow-catalog; "Show Marrow"; the About card; "Marrow <version> is the latest version"; and
"Marrow also picks up scripts from droppy-code.json."

| # | Item | Recommendation |
| --- | --- | --- |
| F1 | The rule above | Accept |
| F2 | Welcome tagline "The coding app by Droppy. A calm, native home for your coding agents." | "A calm, native home for your coding agents." (Droppy's trademark terms forbid implying affiliation) |
| F3 | About line "Droppy Code is a coding app by Droppy for Mac." | "Based on Droppy Code by Jordy Spruit (Droppy), https://getdroppycode.app" (the attribution Droppy's licence asks for) |

## G. Remaining Mac-specific text and menus

| # | Item | Recommendation |
| --- | --- | --- |
| G1 | "is not installed on this Mac" (a sixth "this Mac") | "is not installed on this PC", like B4 |
| G2 | Provider key rows "Stored in your Keychain. Get one at …" and "… stored in your Keychain and handed to the CLI." | "Keychain" becomes "Credential Manager", like B3 |
| G3 | Tooltip "Thread title — click to copy or rename" (a ninth em dash) | Keep verbatim, like B14 |
| G4 | "Open in" menu items Finder, Terminal, Xcode, iTerm | "File Explorer" and "Terminal" (Windows Terminal); Xcode and iTerm are left out because they do not exist on Windows |
| G5 | "Welcome tour" lived only in the Mac Help menu, which C3 removes | Add "Welcome tour" to the command palette so it stays reachable |
| G6 | Ctrl+0 is both "Actual Size" and "Show Marrow" (the window can no longer hide, per C1) | Ctrl+0 is Actual Size; "Show Marrow" keeps no default chord |

## H. Exact truthful rewrites for B15 (About and tour)

| Source | Recommended Windows text |
| --- | --- |
| "The coding app by Droppy: a native home for your coding agents, built in Swift with Liquid Glass." | "A native home for your coding agents, built in C# with Avalonia." |
| "Signed and notarized disk image" | "Unsigned Windows installer" |
| "One native macOS window for every coding agent you use. Claude, Codex, Gemini, Copilot and more, each in its own thread, on the same Liquid Glass." | "One native Windows window for every coding agent you use. Claude, Codex, Gemini, Copilot and more, each in its own thread, on the same glass." |
| "... in real Liquid Glass. Built in Swift, so it scrolls at 120 frames a second and starts in a blink." | "... in real glass. Built in C#, so it scrolls smoothly and starts in a blink." |
| "Droppy Code cannot replace itself in <path>. Move it to your Applications folder and try again." | "Marrow cannot replace itself in <path>. Reinstall it with the installer and try again." |
| SwiftTerm credit row and licence clause | Name the terminal component the Windows build actually ships, with its licence |

## I. Update feed (C6)

| # | Item | Recommendation |
| --- | --- | --- |
| I1 | The feed "setting" batch 1 created | An internal value, empty by default, with no settings UI until you have a feed. While empty, no network request is made and the About pill shows upstream's "Check failed" with the note "No update feed is set." |
