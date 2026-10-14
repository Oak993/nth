> Repository copy (2026-10-14) of the owner's master handoff E:/Droppy/work/HANDOFF_NEXT_RELEASE.md. Paths under E:/ and C:/ are on the owner's PC; the rules about heavy slots, wrappers and native locks apply to agents on that PC. On your own PC, follow README.md in this folder.

# nth: handoff for the next release (after 1.0.0)

Date: 2026-10-14. Written by the lead (Claude Opus 5.5) on the owner's order; any LLM may continue from here.

File: E:/Droppy/work/HANDOFF_NEXT_RELEASE.md (UTF-8, LF). It was assembled from six drafts in E:/Droppy/work/handoff/next-release/ (s1 to s6), which were written from files and read-only git. "(unverified)" marks a fact nobody could confirm. Paths use forward slashes. Line numbers in the table of contents are lines of this file. The file is about 243 KB: do not read it whole; read section 1 and 3.6, then read other parts by line range using those numbers.

## Table of contents

- 1. START HERE (line 116)
  - 1.1 Who you work for (line 118)
  - 1.2 What the app is (3 lines) (line 122)
  - 1.3 State in 8 lines (line 128)
  - 1.4 What to read, and in what order (line 140)
  - 1.5 First actions (line 156)
  - 1.6 The 12 most important rules (full list: section 10) (line 164)
  - 1.7 Words used in this file (line 179)
  - 1.8 Artifacts, designs and the cousin's share (line 187)
- 2. The app and its history (line 194)
  - 2.1 Summary (line 198)
  - 2.2 What the app is (line 207)
  - 2.3 How it is built (line 245)
  - 2.4 Timeline (line 281)
  - 2.5 Problems met and how they went (line 315)
  - 2.6 Lessons the next worker must not repeat (line 336)
  - 2.7 File map (line 352)
  - 2.8 Not confirmed (line 371)
- 3. Current state and first-15-minutes checks (line 377)
  - 3.1 Summary (line 381)
  - 3.2 The shipped 1.0.0 (line 391)
  - 3.3 Branches and units for the next release (line 419)
  - 3.4 What ran at the park, and what runs now (line 438)
  - 3.5 Open items not on the owner list (line 450)
  - 3.6 First 15 minutes: read-only checklist (line 469)
  - 3.7 Text that is out of date (read the log instead) (line 524)
- 4. NEXT RELEASE MASTER CHECKLIST (line 531)
  - 4.1 Ordering rules behind the proposal (line 537)
  - 4.2 Phase 0: Approval and setup (owner and lead; no code) (line 547)
  - 4.3 Phase 1: Baseline, blockers, ready units (three lanes in parallel) (line 558)
  - 4.4 Phase 2: Speed phases (section 6.4) (line 577)
  - 4.5 Phase 3: Owner rows by area (section 5) (line 589)
  - 4.6 Phase 4: Integration and the full gate (lead only) (line 609)
  - 4.7 Phase 5: Package (lead only; never run the installer) (line 618)
  - 4.8 Phase 6: The owner's own check and close-out (line 627)
- 5. Owner test list checklist (line 634)
  - 5.1 How to use this file (line 640)
  - 5.2 Words the rows use (as the rows use them) (line 653)
  - 5.3 The checklist (line 661)
    - 5.3.1 Crashes and hangs (blockers; fix first) (line 665)
    - 5.3.2 Speed and smoothness (the speed plan) (line 671)
    - 5.3.3 First-run defaults (line 685)
    - 5.3.4 Glass, theme and look (line 691)
    - 5.3.5 Composer, lights and working line (line 700)
    - 5.3.6 Ignis (line 713)
    - 5.3.7 Dextra (line 721)
    - 5.3.8 Sidebar and navigation (line 734)
    - 5.3.9 Window, panes and tray (line 741)
    - 5.3.10 Popovers, updates and menus (line 753)
    - 5.3.11 Settings pages (line 767)
    - 5.3.12 Providers and accounts (line 778)
    - 5.3.13 Downloads and packs (line 788)
    - 5.3.14 Voice (line 796)
  - 5.4 Where the files named in the rows live (line 801)
  - 5.5 Summary (line 815)
- 6. Speed plan checklist (line 853)
  - 6.1 Decision and goal (line 861)
  - 6.2 The 8 root causes (most felt first) (line 871)
  - 6.3 The Windows-way rules (line 882)
  - 6.4 Phases and checklist (line 893)
    - Phase 0. Speed gate first (M, about 2 days; detail in 6.4.6) (line 897)
    - Phase 1. Stop the waste: idle becomes zero (S, 1-2 days; the appendix sizes P5 as M) (line 904)
    - Phase 2. Background: wallpaper, window glass, lights (M, 3-4 days) (line 917)
    - Phase 3. Typing, hovering, streaming, sidebar (M, 2-3 days) (line 928)
    - Phase 4. Lists and memory (M-L, 3-4 days) (line 940)
    - Phase 5. Start-up (M, about 2 days) (line 949)
    - Phase 6. Avalonia update (S-M, one day) (line 956)
    - 6.4.6 Phase 0 in detail: the speed gate (line 960)
    - 6.4.7 Later, only if the numbers still ask (no ID) (line 1000)
  - 6.5 Owner decisions still needed (picks 1-9) (line 1005)
  - 6.6 Risks and what NOT to do (line 1025)
  - 6.7 What is NOT the problem (do not spend time here) (line 1041)
  - 6.8 Count check (line 1051)
- 7. Smart testing and the build/merge/package method (line 1055)
  - 7.1 Read first (line 1061)
  - 7.2 Smart (lean) testing (line 1073)
  - 7.3 Build and heavy slots (line 1120)
  - 7.4 Code rules (line 1140)
  - 7.5 Review and merge (line 1163)
  - 7.6 Gate and package (line 1184)
  - 7.7 Test plan template (fill in per unit before coding; paste into the report) (line 1219)
- 8. Agents, tokens, cache, context and the owner (line 1235)
  - 8.1 State today (2026-10-14) (line 1241)
  - 8.2 Token saving rules (every agent) (line 1246)
  - 8.3 Cache warming (line 1260)
  - 8.4 Context limits and auto-compact (line 1268)
  - 8.5 Models, concurrency, limits (line 1300)
  - 8.6 How Codex is driven on this PC (line 1338)
  - 8.7 Working with the owner (line 1368)
  - 8.8 Prompt template for a unit agent (line 1383)
  - 8.9 Not confirmed (line 1414)
- 9. Graphify code map (line 1417)
  - 9.1 What it is (line 1421)
  - 9.2 Where (line 1427)
  - 9.3 Example commands (line 1439)
  - 9.4 Rules (line 1454)
  - 9.5 Limits (line 1463)
  - 9.6 Rebuild (line 1471)
- 10. Safety and standing rules (line 1477)
  - 10.1 The full list (line 1481)
  - 10.2 Where the other rules are (line 1502)
- 11. How to keep this file current (line 1509)
  - 11.1 Revision log of this file (line 1528)
- 12. Source index (line 1533)
  - 12.1 Files and what they hold (line 1537)
  - 12.2 What the drafters could not confirm (carried over) (line 1586)

## 1. START HERE

### 1.1 Who you work for

You work for the owner: the one person who gives the orders and makes every design and money decision. The owner writes English, Hindi or Urdu. Answer the owner in short, plain English (section 8.7). The owner's quality bar is Apple-grade: zero stutter, no overlaps, no snaps, never a loosened test. The owner tests the final installer personally and judges UI and motion there. Context: the owner (GitHub Oak993) is not a programmer. Pick generic, industry-top solutions, not niche ones; explain terms in plain words (E:/Droppy/work/HANDOFF_CLAUDE_20X_2026-10-09.md:23 and :294). The dev PC (this machine, 239 Hz screen) is the owner's own PC: the owner installed nth on it and tests it here.

### 1.2 What the app is (3 lines)

1. "nth" is a Windows 11 desktop app in C# on .NET 10 and Avalonia 12.1.2 (pinned, because the code reflects into Avalonia internals). It is a port of the macOS SwiftUI app "Droppy Code" 1.7.0 (a native app for coding agents) plus features the owner designed (Ignis, Dextra, Tactus, Visus, Tabula, Hydra and more; section 2.2.3).
2. A SwiftUI-style layer (Marrow.UI.Port) recreates SwiftUI in C#. There is no XAML. The code is 2,890 .cs files and about 521,000 lines (counted 2026-10-14 without bin/obj) in E:/Droppy/wt/cl/pkg-morning/src. Projects, namespaces and the repo still say "Marrow"; the product was renamed to nth on 2026-10-10 (OD-0135).
3. The Mac source is the read-only authority for ported surfaces: E:/Droppy/reference/droppy-code-v1.7.0. Reference videos of the Mac app: E:/Droppy/work/reference-video/.

### 1.3 State in 8 lines

1. Version 1.0.0 is built, final, and under the owner's test: commit 87eb11490, installer E:/Droppy/Installer/nth-Setup-1.0.0-UNSIGNED.exe. The owner installed it personally on THIS PC (the dev PC, 239 Hz screen) and tests it there; nth.exe may be running. No agent installs, starts, updates or uninstalls it, and nobody touches %LOCALAPPDATA%/nth (rule 3 in 1.6).
2. The owner's test gave 87 findings (rows #1 to #87, section 5). Nothing is fixed. All of them go into the NEXT release.
3. The owner's biggest complaint is speed and smoothness. The owner's decision (2026-10-14 about 14:14, row #79): keep Avalonia, no rewrite, fix the identified causes the Windows way. The plan (section 6) WAITS for the owner's approval and the owner's picks 1 to 9.
4. Work is PARKED since 2026-10-14 03:13 on the owner's order. No loop, workflow or build runs. Claude weekly usage on this account is 98 percent (after this file was written, 2026-10-14 17:02) (resets 2026-10-18 10:00Z).
5. The next release = all 87 rows + the speed plan + ready units (codex/kokoro 714bfbc3a, and the open items in sections 3.3 and 3.5). Newest owner input (2026-10-14 16:11, row #86): what a reference usage panel has that Ignis lacks; the owner picks which of 7 items to build (section 5.3.6, NR0.4). Newest instruction before it (16:02, row #85): the tray menu needs an item named exactly "Reload" (full shutdown, then a fresh start), with "Exit" last. It is recorded in section 5.3.9 and in the checklist item NR3.9.
6. Base for ALL new work: origin/claude/release-integration = claude/final-pre = 9e083470c (same code as the installer commit 87eb11490). The LOCAL branch claude/release-integration (a394de7e4) is 648 commits behind 87eb11490: never branch from it. E:/Droppy/port is only the git hub (old branch port/windows-exact-parity): never work there. Create a unit with: `git -C E:/Droppy/port worktree add E:/Droppy/wt/cl/<unit> -b claude/<unit> origin/claude/release-integration` (Codex: E:/Droppy/wt/cx/<unit>, branch codex/<unit>). Shell: Git Bash on Windows 11 with forward-slash paths; pwsh 7 only for scripts/package-windows.ps1.

7. Shared with the owner's cousin (owner OK 2026-10-14 16:58; repo github.com/Oak993/marrow-port stays PRIVATE): branch release/1.0.0 = 87eb11490 (exactly 1.0.0, never push), branch next-release = 58f1a3360 (9e083470c + docs/next-release/: README guide for the cousin and their AI tools, OWNER-LIST with emails masked, SPEED-PLAN, glass-ui design, HANDOFF copy). The cousin works on cousin/<topic> branches and sends pull requests into next-release; the lead or Codex reviews, tests on this PC and merges. Section 1.8.
8. Every artifact (design page) on this Claude account is saved on disk: E:/Droppy/work/design/published/INDEX.md (54 on account 2, plus the older account pages and canvases). Section 1.8.

### 1.4 What to read, and in what order

| Step | Read | Why |
|---|---|---|
| 1 | This section (1) | Rules and first actions |
| 2 | Section 10 | Safety and standing rules; read them before you touch anything |
| 3 | Section 3 (3.6 for the commands) | Real state of branches, heads, processes; verify it |
| 4 | Section 4 | The proposed order of work with checkboxes |
| 5 | Sections 8.2, 8.3, 8.7 | Token rules, cache, how to talk to the owner (before you start any agent) |
| 6 | Sections 5, 6, 7 as the work needs | Owner rows, speed plan, how to build, test, merge and package |
| 7 | Section 2 | History and why things are as they are (problems, lessons) |
| 8 | Section 9 | Code map: use it before wide searches |
| 9 | Sections 11, 12 | Keep this file current; where every fact came from |

> **ENTRY POINT.** When the owner says "read the handoff file", read THIS file first. Since 2026-10-14 17:05 the first line of E:/Droppy/work/NEXT_SESSION_CLAUDE.md and line 1 of E:/Droppy/work/HANDOFF_PROMPT.md point here. The 2026-10-12 LATEST block in NEXT_SESSION_CLAUDE.md is history: its "restart the cut-off workflows" steps are done or parked; do not follow them.

### 1.5 First actions

1. Run `date`. Never reuse a remembered time.
2. Verify reality with the read-only commands in section 3.6 (about 15 minutes). If a value differs from the expected one, somebody worked since: find out why, then update section 3 (section 11).
3. Check usage: the Claude session tool `get_usage` (expect about 96 percent weekly) and the Codex usage card in the ChatGPT app by eye (sections 8.1, 8.4).
4. Get the owner's approval BEFORE any speed work. Ask once, in one batched message with the recommended option first: (a) approve the speed plan and its order and answer picks 1 to 9 (section 6.5); (b) approve the order in section 4; (c) which open items of section 3.5 join the release; (d) the owner decision that lets closing the window hide nth to the tray (row #85); (e) which of items 1-7 of row #86 (Ignis) to build; (f) whether f3-chats (OD-0131), T43 storage location and f8-contrast join the release (sections 3.3 and 3.5 item 12); (g) the base: confirm origin/claude/release-integration 9e083470c. Never start work the owner has not approved. Nothing in this file authorizes work by itself.
5. After approval, work through section 4 in order. Log every finished step (section 11).

### 1.6 The 12 most important rules (full list: section 10)

1. While parked, nothing new starts without the owner's approval. Speed work waits for the plan's approval and picks 1 to 9.
2. Never run an installer on this PC (msiexec counts). The package script runs only with -SkipUiSmoke, and only when packaging is ordered (without that switch it RUNS nth-Setup.exe --ui-smoke: scripts/package-windows.ps1:312-316).
3. Never change Windows or security settings. Never touch %LOCALAPPDATA%/nth, the installed app or the legacy Marrow app. Never write under E:/Droppy/reference/.
4. Never call model providers from code or tests (they cost money). Tests use fakes that fail closed. A real send happens only on the owner's explicit word.
5. Never force-push, never `git stash`, never `git add -A`. Keep discarded branches. Permanent deletion needs the owner's confirmed list; move instead.
6. Run tests only through the wrappers and heavy slots (section 7). A failure counts only if it fails alone twice. Never loosen, skip or delete a test; a real design change needs an ED plus an equal-or-stricter replacement.
7. Native (real-window) tests run only in the lead's quiet window. Nobody types, clicks or uses a browser or computer-use tool meanwhile.
8. Never enter, print or copy credentials, API keys, tokens or card numbers. Never open the owner's account files in a way that prints tokens.
9. Do not rewrite. Keep Avalonia 12.1.2 pinned (only exception: the A1 12.1.4 trial on a side branch, pick 7, section 6.4). Fix the identified causes the Windows way (row #79; section 6).
10. "Done" means measured on the installed build: maximised, focused, real input, the owner's 239 Hz screen. The owner judges UI and motion on the final installer.
11. Keep 5 to 6 GB RAM free. No auto-kill scripts: find stale processes, stop them by hand. Log each finished task with `python E:/Droppy/work/claude-verify/handoff_log.py "message"`.
12. Decide technical questions yourself and record them (OD or ED). Bring only true design picks, spending, data going out or standing-rule changes to the owner, batched, recommended option first. No unasked progress tables.

### 1.7 Words used in this file

- Owner: the user who decides. Lead: the orchestrating Claude session (writes the handoffs, merges, runs gates). Unit: one small job with a branch, a worktree and a report.
- OD = owner decision (the owner's words, binding; in porting/owner_decisions.md). ED = engineering decision (porting/engineering_decisions.md; a unit adds ED-NEW-<unit>-<n>). AM = contract amendment.
- rel-int = the branch name claude/release-integration. "log:N" = line N of E:/Droppy/work/NEXT_SESSION_CLAUDE.md. "L<n>" in section 5 = a line of the owner's list file.
- Heavy slot = a lock that limits how many builds and test runs run at once (section 7.3). Gate = the full test run on a merged branch (section 7.6.1).
- Native tests = tests that drive real windows; they need the screen to themselves.

### 1.8 Artifacts, designs and the cousin's share

- **Artifacts.** Every design page ever published is saved complete on disk and indexed in E:/Droppy/work/design/published/INDEX.md (status, URL, local folder, master source, which version ships). On 2026-10-14 the 8 account-2 pages that had no local copy were saved (INDEX.md section 9, sha256 in MANIFEST-2026-10-14.sha256). To change a page on the SAME Claude account: Artifact read its URL, edit the saved file, publish with that url (the link stays). From another account or another LLM: edit the folder copy and publish it as a new page, then record the new URL in INDEX.md. Load the artifact-design skill first.
- **Owner reference videos and frames (2026-10-14).** Ignis reference panel: E:/Droppy/work/scratch/lead/owner-testing/ignis-video-2026-10-14/ (row #86). LifeOS glass: E:/Droppy/work/scratch/lead/owner-testing/lifeos-glass-2026-10-14/ (row #87; the 647 MB video stays at C:/Users/omera/Downloads/LIFEOS.mov).
- **The cousin.** Works on UI and glass (rows #87, #32, #84, #83, #58) in their own clone. Start file for them: docs/next-release/README.md on branch next-release. Rules: own cousin/<topic> branches only, pull requests into next-release, never push elsewhere, never change the repo's visibility, no Avalonia upgrade, no new package, smart tests only, frame times before and after. A free private repo cannot lock branches, so check every push to next-release, release/* and claude/* yourself. Merge their pull requests like any unit: adversarial review, lean tests here, then the gate. Glass changes collide with speed Phase 2 (GlassMaterial.cs, Chrome.cs, the wallpaper renderer): agree with the owner who changes what first.
- **Worktree for the shared branch:** E:/Droppy/wt/cl/next-release (branch next-release). Update docs/next-release/HANDOFF.md there when this file changes, mask emails, push.

## 2. The app and its history

From draft s1. Section numbers in this part were renumbered when the file was assembled. Written 2026-10-14 for an LLM agent that has never seen this project. Facts come from files and read-only git. "(unverified)" marks what I could not confirm. OD = owner decision (the owner's words, binding). ED = engineering decision. AM = contract amendment. "rel-int" = branch claude/release-integration. Paths use forward slashes.

### 2.1 Summary

- nth is a Windows 11 desktop app in C# on .NET 10 and Avalonia 12.1.2. It is a port of the macOS SwiftUI app "Droppy Code" 1.7.0, plus many features the owner designed. It was called "Marrow" until 2026-10-10 (OD-0135); projects, namespaces, ledgers and the repo still say Marrow.
- Version 1.0.0 is built: E:/Droppy/Installer/nth-Setup-1.0.0-UNSIGNED.exe (2026-10-14 00:11, commit 87eb11490; never run by an agent; the owner installed it on this PC). The owner is testing it. It is final for 1.0.0.
- The owner's test gave 87 findings (rows #1-#86 in E:/Droppy/work/scratch/lead/owner-test-2026-10-14.md). Nothing is fixed. All of it goes into the NEXT release.
- The biggest open problem is speed and smoothness. The owner chose: keep Avalonia, no rewrite, fix the engine habits the Windows way (row #79). The plan waits for the owner's approval.
- Work has been parked since 2026-10-14 03:13. Nothing runs. Weekly Claude usage is 96 percent.
- Repo github.com/Oak993/marrow-port; 6,561 commits; about 340 worktrees under E:/Droppy/wt/. Owner bar: Apple-grade, zero stutter, no overlaps or snaps, never loosen a test. Read section 2.6 first.

### 2.2 What the app is

#### 2.2.1 Purpose
The Mac original is "a native macOS app for your coding agents" (E:/Droppy/reference/droppy-code-v1.7.0/README.md). It holds no model. It drives the agent command-line tools the user already has signed in (Codex, Claude, Cursor, OpenCode, Grok, Antigravity, Copilot, Command Code, Pi) plus API providers (DeepSeek, Z.ai, Meta). It has a glass sidebar of projects and threads, streamed replies, inline approvals, a diff per turn with revert, a terminal, git worktrees, skills, MCP, voice typing, usage limits, themes and Hydra. nth keeps all of that (4,988 of 5,007 parity records on 2026-10-05) and adds 1.3.

#### 2.2.2 Name history
| When | Name | Notes |
|---|---|---|
| upstream | Droppy Code (Mac) | Pinned baseline B3 = 1.7.0, commit 52bc7926097cd622d08d6294e0d8b08a48c7516e (OD-0024, AM-0003). B1 1.6.0 and B2 1.6.2 are history only. |
| earlier | "Marrow", the owner's Electron app ("Legacy Marrow", C:/Users/omera/Downloads/Claude outputs/agent-workbench) | A DIFFERENT product. Inventory: E:/Droppy/work/legacy-marrow-inventory.md. Some capabilities were picked for carry-over, redesigned in glass style. |
| 09-22 to 10-10 | "Marrow"; data identity "Marrow Code" | The port's working name. |
| 10-10 on | nth | OD-0135 "Lock nth". Data in %LOCALAPPDATA%/nth; first launch copies the old "Marrow Code" folder and leaves it. Wordmark lower-case "nth". Icon B "Exponent" (OD-0136). |

WARNING: CLAUDE.md and AGENTS.md in E:/Droppy/wt/cl/pkg-morning still say "Marrow" and "Marrow Code". Trust OD-0135.

#### 2.2.3 Main features and locked names
The owner picked the names (short Latin-feel words). Do not rename. Code paths are under E:/Droppy/wt/cl/pkg-morning/src.

| Feature | What it is | Decision | Code |
|---|---|---|---|
| Hydra | Upstream: a lead agent briefs helper "heads" in their own project copies; their work lands as one merge. | upstream | App/AppModel+Hydra*.cs, App/Views/Chat/Hydra*.cs |
| Strike slider, Jewel knob | Effort control with liquid-glass knob and lightning effects; Fast is a yellow bolt. | OD-0089..0095 (unverified) | App/Views/Composer |
| Multi-account | Several accounts per provider, handshake, "ask others", usage panel. | OD-0114..0119 | App/Port/MultiAccount, Services/Port/MultiAccount |
| Voice typing, pill | Whisper.net dictation; pill inside the composer. | OD-0121..0132 | App/Port/Voice, UI/Port/Voice, Services/Port/Voice |
| Liquid glass (iOS 27 style) | App-drawn glass; every parameter a user setting (Settings > Customization > Glass). | OD-0147 | UI/Port/GlassMaterial*.cs, GlassLooks.cs |
| 21 composer lights | Animated lights around the composer, each a Settings option. | OD-0150 | UI/Port/ComposerLights, Core/Models/ComposerLights |
| Ignis | Cost saver: hidden keep-warm pings, auto-compact, sidebar ember per chat; design "Kindle". Was Phoenix, then Cruise. | OD-0134, 0138 | Core/Port/Ignis, Services/Port/Ignis, App/Port/Ignis |
| Dextra | Always-on assistant above all chats: missions, decisions, schedule, memory, card purchases behind Windows Hello (off by default). "Armilla" = its ring; "Astur" = area assistants. | design approved 10-12 (E:/Droppy/work/design/assistant/DESIGN.md rev 5) | App/Assistant, Services/Assistant, UI/Port/Armilla |
| Tactus | PC control: any model can see and use apps the user allows. Pill v2, icon "Cuspis". | OD-0140..0146 | App/Port/PcControl, Services/Port/PcControl, UI/Port/PcControl |
| Visus, Tabula | Visus = in-app browser (WebView2). Tabula = artifacts panel. Plus "nth-ui" live answers (built-in MCP server) and a pane workspace. | owner 10-11 | App/Views/Browser, App/Views/Artifacts, App/Views/Panes, UI/NthUi, Core/NthUi, Core/Web, Services/Web |
| 29 downloads | Tool and plugin downloads, each pinned to an exact version from its official source. | OD-0148 | Services/Port/Downloads |
| Calculi | Working animation: three silver beads ride the "n" like an abacus; pair A (Scriptio to Occasus); no fades. | approved 10-11/12 | UI/Port/Nth/Beads, App/Views/Chat/NthWorkingBeads.cs |
| Session hotkeys | Hold Ctrl for badges; Ctrl+1..9 (row and numpad) opens that session; one quick switch motion. | OD-0155 | App/Views/Sidebar/SessionNumbers.cs, App/Window/ThreadWindow.cs |
| Text size | App zoom reaches every surface. BaseScale 1.3 shipped; owner wants 90 percent default (#55). | OD-0154 | UI/Common/AppZoom.cs |
| Other | Nocta switch (OD-0143); Settings inside the main window (OD-0151); sidebar modes (OD-0153); Boot B loaders and the nth boot (OD-0113, 0142); nth Setup installer (Velopack 1.2.161, publisher "oak993", unsigned). | OD as listed | App/Views/Settings/NoctaLook.cs, App/Port/SettingsOverlay.cs, UI/Port/Aurora*, NthMotion.cs, Marrow.Setup |

Also in 1.0.0: skills import, What's new, Updates page, models Muse Wonder and Gemini 4 Argon. Ready but NOT in 1.0.0: Kokoro CPU voices for Dextra (codex/kokoro 714bfbc3a, row #80). NOT shipped: chats without a project and storage location (OD-0131 only on origin/claude/f3-chats 8cdd0e156; 23 uncommitted files in E:/Droppy/wt/cx/f3-chats) and the F8 palette contrast retune (E:/Droppy/wt/cx/f8-contrast, 10 uncommitted files); see sections 3.3 and 3.5 item 12.

### 2.3 How it is built

#### 2.3.1 Projects (E:/Droppy/wt/cl/pkg-morning; .cs counts without bin/obj, 2026-10-14)
| Project | Files | Holds |
|---|---|---|
| src/Marrow.Core | 604 | Records, models, process plumbing, accounts, localisation, nth-ui and web contracts. Depends on nothing in the app. |
| src/Marrow.Services | 742 | Providers, git, MCP, store, assistant, downloads, skills, voice, Ignis, PC-control services. Depends on Core. |
| src/Marrow.UI | 425 | The SwiftUI-style port layer (UI/Port, 110 top-level files), glass, motion, wallpapers, markdown, panes. Depends on Core. |
| src/Marrow.App | 1026 | Composition root and all views (AppModel, Views/*, Window/*, Assistant, Port/* feature folders, Program.cs). Depends on all. |
| src/Marrow.Setup, Marrow.Shim | 92, 1 | Installer UI and flow; NativeAOT launcher copied under each provided tool name (SK-6). |
| tests/ | about 4,990 | Marrow.{App,Core,Services,Setup,UI}.Tests, Marrow.Testing, Marrow.Shim.*, Marrow.Skills.PipelineWorker, fixtures. |

2,890 .cs files (no bin/obj) and about 521,000 lines, counted 2026-10-14. Layering (AGENTS.md): Core depends on nothing; Services and UI only on Core; App on all. Root files: Directory.Packages.props (every NuGet version, pinned here only), global.json (SDK 10.0.401), CHANGELOG.md (short [1.0.0] entry); folders porting/ (ledgers), scripts/, docs/, tools/.

#### 2.3.2 Stack facts
- Avalonia 12.1.2 is PINNED: the glass compositor reflects into Avalonia internals (ISkiaSharpApiLeaseFeature is unstable). Changing it needs the screenshot-regression harness (Directory.Packages.props header; porting/adr/0002).
- SkiaSharp 3.119.4 on ANGLE D3D11. Glass is an app-owned two-pass compositor running SkSL effects; the window edge uses Acrylic. Why not WinUI 3 (adr 0001/0002): Windows gives an app no pixels behind its own window, and the 22 glass sites sit over live app content.
- Release settings (src/Marrow.App/Marrow.App.csproj:21-29): ServerGC false, concurrent GC true, ReadyToRun on, TieredPGO on. Server GC was a launch bug found 10-08.
- Package: `pwsh -NoProfile -File scripts/package-windows.ps1 -SkipUiSmoke` with pwsh 7.6.x (the WindowsApps one, not Windows PowerShell 5.1). vpk refuses an equal version (move old artifacts aside). Restore exit 1 here is only the Node pin (24.19.0).

#### 2.3.3 The port layer (no XAML)
There is no XAML. Every view is C# code imitating SwiftUI: tracked state, bindings, ForEach, modifiers, SwiftColor, SwiftFont, SwiftAnimation springs, SwiftGlass, SwiftShapes (src/Marrow.UI/Port). Files in src/Marrow.App/Views mirror the Swift view files. This gave exact parity and is also the root of the speed problems: the research counted 0 layout files, 2,645 hand-built controls, 132 clear-and-rebuild spots, 0 virtualised lists, 258 private frame requests (owner-test row #79).

#### 2.3.4 Swift to C# mapping, authority, ledgers
- Comments cite the Swift origin as File.swift:line (example "Chrome.swift:80-87" in UI/Port/AccentPalette.cs). Code with no Swift origin starts with "PORT-ONLY:" (about 3,600 lines).
- Requirements: porting/parity_matrix.json (3,992) and ui_contract.json (1,057), ids like PR-SIDEBAR-091; 5,007 applicable. Tests carry [Trait("Req", id)] and are written from the Swift behaviour first. Also read_ledger.jsonl, platform_adaptations.md (macOS API to Windows replacement), IMPLEMENTING.md (conventions).
- Authority order: 1 PORT_CONTRACT.md (never edited), 2 contract_amendments.md (AM-0001..0003), 3 the pinned Swift at E:/Droppy/reference/droppy-code-v1.7.0 (read only), 4 owner_decisions.md, 5 engineering_decisions.md. AGENTS.md lists contract, amendments, source; the 10-03 handoff (NEXT_SESSION_CLAUDE.md line 883) puts ODs above EDs; an OD beats a design page.
- Highest numbers: OD-0156 (153 OD sections); 1,588 ED sections (1,016 of them ED-NEW), last numbered ED-644. Both ledgers mix CRLF and LF.
- "Design wins" (OD-0126): retire an old assertion only with an ED plus an equal-or-stricter replacement. Safety tests (for example R-PROC-1) are never loosened. Parallel branches collided on OD numbers; renumber at merge (OD-0150, 0151 were).

#### 2.3.5 Tests and gates
- Core about 9,733 and Services about 9,308 tests passed at a7659b2af (NEXT_SESSION_CLAUDE.md line 417). UI and App are several thousand each (totals unverified). On 10-08: Core 4,963, Services 5,847, UI 4,752, App 11,052.
- App tests compare real glass renders with the Mac design at several scales and themes. Native tests drive real windows and need the screen alone.
- Accepted voice-pill failures: OD-0128, 0137, 0152, 0156 (215 cases after OD-0156: 183 by OD-0137, +1 by OD-0152, +31 by OD-0156; the JSON in E:/Droppy/work/scratch/codex-merge-train/standing/06-final/OD-0128-known-identities.json lists only the 153 of OD-0128). A failure counts only if it fails ALONE TWICE.
- Gate groups: build, core, svc-prov, svc-rest, ui, app-views, app-rest, setup, shim (gate-seq3.sh) plus views-composer (a gate-par.sh group, not part of gate-seq3.sh). Scripts are in section 2.7 (file map). Never run a bare `dotnet test`.

### 2.4 Timeline
Commits per day in the shipped tree: 09-23 1,025; 09-27 584; 10-04 824; 10-05 526; 10-12 625; 10-13 451.

| Date | What happened |
|---|---|
| 09-19/20 | Gate A (complete reading of upstream: 433 files, 85,392 lines). Gate B (3,827 requirements, 1,029 UI records). ADR-0001 (the "Liquid Glass wall"), then ADR-0002: Avalonia. |
| 09-22 | Claude becomes orchestrator (CLAUDE.md). That day's HANDOFF.md still shows 662 task packets and a Codex orchestrator inside the old Marrow 0.2.1. |
| 09-23 to 09-30 | Bulk build by parallel agents. Re-pin to B3 (1.7.0). Codex "turbo" phase under a ChatGPT orchestrator (J.A.R.V.I.S) delivered unverified branches and an unreliable 88.22 percent completion count. |
| 10-01 | MASTERPLAN v1: truth baseline, state ladder, phases 0-6. Measured 4,417 "implemented", only 3,860 backed by a named test. New $200 plan after the old account hit 100 percent. Staging branch claude/verify-terminal-20261001. |
| 10-03/04 | Account A to B handoff. Owner approved designs (composer lights, Customization, Jewel knob, Strike, voice typing). Test wrappers and flake rule set. |
| 10-05 to 10-07 | Codex upgraded to 5x and did most work: merge train T9 batches 1-9, multi-account, boot loaders, skills, VT-12, wallpapers. 10-05: 99.62 percent parity. 10-06 Codex out of usage until 10-12; a Pro-account lead took over. 10-07: GitHub outage 17:15-18:20; release audit RR-01..RR-50 found merged units that were fail-closed stubs. |
| 10-08 | 01:20 merge train done (staging 79306e519). 01:25 first installer (Marrow-1.7.0-Setup.exe); 01:40 version 1.0.0 (OD-0129). Owner tested and recorded a video ("a broken half-attempt"): launch lag, voice pill, Settings, captions (F1-F8). Codex units T44 speed, T45 Windows, T46 picker/pill, T41 gate, T40 chats, T42 contrast, T43 storage: all NOT READY by night; merged gate RED at 2eecff5ac. |
| 10-09/10 | 10-09 00:30 owner stops Codex; handoff to a Claude 20x session (E:/Droppy/work/HANDOFF_CLAUDE_20X_2026-10-09.md). 10-10: Claude fixes, no Codex. OD-0133 launch fix. Ignis designed and locked (OD-0134, 0138). Rename to nth (OD-0135). 23:35 installer nth-1.0.0-Setup.exe (e4ebf7c31). |
| 10-11 | Release scope widened (PC control, 29 downloads, Visus/Tabula, glass, installer, lean pass, Calculi, Dextra, hotkeys, text size); owner approved many designs. 11 workflows ran; owner said "slow down"; parked at the 5-hour limit. Commit 0e31101f8 reverted. |
| 10-12 | 625 commits. Account 1 hit 99 percent 05:11; new lead session 0d6c2a21 on account 2 from 09:59. State audit (web 13/33, lean 20/23, Tactus 28/46, Dextra 0/23). Merge groups g1-g8 into rel-int. Codex reviews C1-C10; Codex fell to 19 percent, so units moved to Sonnet stand-ins. Several memory emergencies. |
| 10-13 | 451 commits. Owner reset Codex (10:01) and said use it fully. Landed in rel-int: glass d7674bc5d, installer 073bc13bb, perf-ui2 d1a29f21a, motion2 c0e00c000, smooth2 bc9af9efc, gate-fix4 a394de7e4, PlanLimits fix a7659b2af. Dextra and web merged via claude/final-pre. Security gate: 0 blockers, 7 should-fix. |
| 10-14 00:05-00:11 | Owner: stop tuning, merge, build. Composer pill software-glass cache (codex/composer-stack 8987acfe6) merged as 87eb11490; FINAL installer built. A ledger-only merge 9e083470c (plus a web-canary.yml fix) is on origin/claude/release-integration; the code equals 87eb11490. |
| 10-14 00:27-02:34 | Owner tested the installer (video 20261013-2127-13.3265569.mp4); lead inspected via PC control. Rows #1-#76. Notable: first launch 27-34 s, every send crashed (#52), Tactus switch dead (#8, #66), Dextra first run cannot reach Done (#12), idle CPU (#38). |
| 10-14 03:13 on | PARKED on the owner's order after speed research (02:43-03:05). 14:14 owner: keep Avalonia, Windows-first (#79). Speed plan 14:43. Graphify map 14:56. Rows #81-#86 (#85: closing hides to the tray; #86: Ignis gaps against a reference usage panel). Handoff ordered 16:00. |

#### 2.4.1 Installers (E:/Droppy/Installer/; none was run by an agent; the owner installed those builds personally)
- 10-08: Marrow-1.7.0-Setup.exe (01:25), then Marrow-1.0.0-Setup.exe rebuilt five times (110 to 139 MB; ReadyToRun, crash and theme fixes); the 07:49 build from de34c16f1 (139,121,931 B) is what the owner tested first.
- 10-10 23:35 nth-1.0.0-Setup.exe (e4ebf7c31, rename, Ignis cooling); 10-11 13:56 (69d7ae589, warm-up split).
- Name nth-Setup-1.0.0-UNSIGNED.exe from 10-13: 08:31 d7674bc5d (glass); 14:47 fd73b82ee (first gated build); 23:40 a7659b2af (Muse Wonder, Argon, PlanLimits fix); 10-14 00:11 87eb11490 (composer pill glass cache, 180 MB, sha256 384013ba6935b24e..., FINAL). Older ones: C:/nth-archive/Installer-old-2026-10-14 (E: had 2.5 GB free).

#### 2.4.2 Codex units of 2026-10-08
T44 speed (shader warm-up f87ba24c9 made cold init 26.07 s, NOT merged; the rest of T44, T45 Windows 735adbb8a and T46 picker/pill 059e65b42 are in 1.0.0), T41 gate (RED at 2eecff5ac, base of later Claude fixes), T40 chats, T42 contrast, T43 storage (paused, not shipped).

#### 2.4.3 Owner findings of 2026-10-08 (E:/Droppy/work/scratch/lead/owner-testing/findings.md holds F1-F6; F7 and F8 are in HANDOFF_CLAUDE_20X_2026-10-09.md:69-76)
- F1 launch animation lag: still open (see row #1). F2 Claude sign-in did not refresh after the CLI login: fixed. F3 chats without a project: OD-0131, NOT shipped (branch f3-chats; sections 3.3, 3.5 item 12). F4 white box behind tooltips: fixed. F5 voice pill overlap and snap: soft fade OD-0130, then the pill redesign OD-0132; a headless rig showed no overlap and the owner was asked for a screen recording (findings.md line 20). F6 whole app laggy: now row #79.
- F7 caption buttons overlap scrolled Settings content: an owner design call; the screenshot was sent (E:/Droppy/work/scratch/lead/owner-testing/f7/f7-settings-caption-overlap.png) and no answer is on record. Check rows #11, #72 and #46 before changing anything there.
- F8 model-palette light shades equal the dark ones (chip titles about 1.5:1 on light glass; nth-only, not Mac parity): owner design pick; T42 paused (sections 3.3, 3.5 item 12).
- Windows departure that explains rows #46 and #68: Settings is kept above its owner window (T45; an ED and a replacement test record it; HANDOFF_CLAUDE_20X_2026-10-09.md:157).

### 2.5 Problems met and how they went
| # | Problem | What we tried | Outcome | Lesson |
|---|---|---|---|---|
| 1 | Idle CPU: ambient light and orb asked for about 165 fps idle (0.3-1 core, Server GC), 10-08. | T44: 60 Hz cap, stop when unseen, concurrent GC. | 23.96 to 0.57 percent in a controlled pair. On the 1.0.0 install: 0.44 core and 13 percent GPU idle (#38), a stuck caption-button hover redrawing forever (#77), Dextra's ring never settling (#78). | Idle must mean zero frames. Measure the installed app, focused, on the owner's 239 Hz screen. |
| 2 | Launch: first WM_PAINT waited 2.4-3.4 s (ANGLE D3DCompile of glass shaders); 4.1 s main-thread block in the compositor (hang log 10-08). | T44 warm-up of 8 shader variants on 4 workers; OD-0133 (program cache installed before first link; provider checks no longer gate Ready). | Warm-up made cold init worse and was dropped. OD-0133 is in 1.0.0. Owner still saw 27-34 s first launch (#1; PC had 1.2-1.6 GB free RAM, partly inflated, unverified). | No worker warm-up. Try a persistent shader cache or a light first frame. |
| 3 | A new glass piece count links its program on the render thread; a first tap can land during a warm-up part. | 92-step one-draw warm-up (ED-NEW-warmupsplit-1..4) plus glass slots in 0e31101f8. | 0e31101f8 reverted (ED-NEW-warmupsplit-5; it regressed A_warmed_heads_report_opens_within_one_frame). Knob-cache and REVIEW_Escape native tests stay open. A software glass cache for the composer pill (8987acfe6) reuses draws in about 5 ms against 923 ms; in 1.0.0. | Run native frame tests alone before merging. |
| 4 | Speed tests hid lag: the earlier lean pass measured bytes with no window; idle tests ran unfocused (this pauses animation); none of 10 real-screen frame-time checks ran. | none | Found 10-14 (section 6.2, root cause 8). Not fixed. | Measure what the owner feels: maximised, focused, real input, release build, full-rate capture. |
| 5 | The engine copies Mac habits: frame-by-frame animation, full-window copy per frame (about 14.5 MB), glass re-copied and re-blurred per wallpaper frame, GPU cache at default 29 MB, no virtual lists, provider messages on the UI thread. | 8 surveyors and a critic: 111 findings (12 re-checked: 10 true, 2 partly). | Plan only: speed-plan-2026-10-14.md (7 phases, about 20 central places; section 6 of this file). Needs owner approval and picks 1-9. | Not a rewrite. Owner liked "one engine, pieces in harmony". |
| 6 | Voice pill rim specks (VT-12): pixel gate differs between cloud and Windows renders; fixes raised failing identities 138 to 147; owner refuses test changes. | Many Codex and Claude rounds; no matching Mac reference found. | Owner accepted by eye: OD-0128 (153), OD-0137 (183 in all, 6 new from the send button), OD-0152 (+1 = 184), OD-0156 (+31 = 215). Next release must re-anchor V-T133 to the pill's live position (OD-0137). | Ask for the owner's eye approval early; never loosen the test. |
| 7 | Wallpaper fidelity: references from GLSL in Chrome, port runs SkSL; about 20 pixels fail at branch thresholds. | Several rounds. | Held back 10-06; later landed (closing ruling unverified). | Compare render paths first. |
| 8 | Flaky tests (list: E:/Droppy/work/flakes.md; for example ShellTests kill-one-second, GitFifthTests.CaptureTreeOnto, SkillOverlay Cursor_without_a_generation, WallpaperCardTests, PlanLimits Read_executable that can hang a group). | Flake rule: counts only if it fails alone twice. | Works. PlanLimits hang fixed (3a41ee0cb); one Hydra test race was real. | Two alone runs first. |
| 9 | Native window tests fail when someone types, clicks or a toast shows; Codex Computer Use stops on a physical Escape; caption tests failed on input-safety preconditions; a test that spawned a child without -NoNewWindow popped real terminal windows (fixed, ED-NEW-termwin-1). | native.lock, quiet window, no typing meanwhile. | Rule kept. Runner with a total wait cap: E:/Droppy/work/lean-pass/native/run-native.sh. | Fix preconditions, never weaken them; tests must not open real windows. |
| 10 | RAM and slots. 10-05 the heavy gate on 4 GB free stalled every build for hours. 10-12: one test host reached 30.5 GB; 209 PowerShell children took 13 GB; the ChatGPT app respawned about 185 MCP helpers (8 GB). 10-13: hang dumps filled E: (22 GB moved). | heavy.sh (slot 1 always free, slots 2-3 need 3 GB); memguard.ps1 (kills hosts over 10 GB); manual stale checks; bin/obj removed from about 338 worktrees (owner OK). | Works but fragile; builds ran at 0.4-2 GB free many times. | Keep 5-6 GB free; the 2-build-workflow cap was lifted 2026-10-12, but the RAM floor of 5-6 GB and the 4 heavy slots still gate builds; no auto-kill scripts (they killed good tasks). |
| 11 | Codex quality: the warm-up regression; gates left RED; units tuning past their gate while a working cache sat unreported; audit found fail-closed stubs; landings needed fixes (NC6, C10). | Time-box about 30 min per unit; a Sonnet check on every landing; lead review of every merge. | Works. Codex was good on self-contained units, weak on whole-app performance. | Review every Codex unit; read the thread, not only the report. |
| 12 | Usage: weekly caps hit on several Claude accounts and on Codex (19 percent left 10-12). | Handoff files, account switch, Sonnet stand-ins for Codex units, Codex reset 10-13, five-hour guard (claude-verify/limit-park.sh). | Worked, with lost hours. Claude weekly now 96 percent (resets 2026-10-18 10:00Z). | Log every event with claude-verify/handoff_log.py; route work to the cheaper model. |
| 13 | Cross-account: published design pages belong to account 1; account 2 could not open them. Workflow run ids do not resume across sessions; cron-cancel results were cached as done (SPOILED). | Saved every page locally (E:/Droppy/work/design/published/INDEX.md: 50 earlier pages in its section 3, 34 of them republished on account 2 on 10-12; on 10-14 16:06 8 more account-2 pages were saved, so all 54 artifacts of account 2 are on disk, MANIFEST-2026-10-14.sha256); script copies in E:/Droppy/work/handoff-workflows/. | Works. | Keep a local copy of every page and workflow script. |
| 14 | Owner's mid-turn messages reached running agents and were obeyed; SendMessage to a workflow agent started a duplicate editing the same worktree. | AUTHORITY block in every prompt; scan journals for "stale"; never SendMessage workflow agents. | Works. | State in each prompt that the task is the owner's own order. |
| 15 | Ledgers mix CRLF and LF; append mistakes. In bash heredocs a double backslash arrives as one. | Append byte-exact with Python in binary mode; check `git diff --stat`; use the Write tool. | Works. | Never normalise line endings in porting/. |
| 16 | Tests are many and slow; each owner fix changes a Mac-pinned assertion; 10-08 three Codex threads ran full App sweeps and starved each other. | One full gate on the merged branch; per fix only new and changed classes; LEAN-TESTS.md. | A full gate still takes hours (composer views alone about 1 hour). | A change to anything the Mac design pinned needs an ED plus a stricter test; budget for it. |
| 17 | 1.0.0 shipped with bugs the gate missed: every send crashed (#52: NotSupportedException "No Swift text is defined for InvalidDataException" in ThreadRuntime.StartTurn's catch), Tactus switch not redrawn (#66), Dextra bar under caption buttons (#11), first-run Done unreachable (#12), failed Add-account shown connected (#20/#21). | Noted only (owner order). | Open, for the next release. | Nothing tested the installed app end to end. Add install-and-use smoke runs. |

### 2.6 Lessons the next worker must not repeat
1. Do not rewrite. The owner said keep Avalonia (#79). The slow parts sit in about 20 central places.
2. "Done" means measured on the installed app: focused, visible, maximised, real input, 239 Hz.
3. Never loosen a test, tolerance or reference. Use "design wins": ED plus an equal-or-stricter replacement.
4. Run a failing test alone twice before calling it real or flaky. Never run a bare `dotnet test`; use the wrappers and heavy slots.
5. Native tests only in a quiet window under native.lock; no typing in other windows; never press Escape while Computer Use drives.
6. Keep 5-6 GB RAM free (the 2-build-workflow cap was lifted 2026-10-12; the RAM floor and the 4 heavy slots still gate); close the ChatGPT app tree when idle; no auto-kill scripts.
7. Do not warm shaders on workers (T44); do not merge 0e31101f8 or f87ba24c9 as built.
8. Review every Codex unit; time-box it; stop tuning at the gate.
9. Put an AUTHORITY line in every agent prompt. Never SendMessage a running workflow agent.
10. Append to ledgers byte-exact; renumber ODs and EDs at merge.
11. Safety rules: section 10. Also never touch %LOCALAPPDATA%/Marrow Code (the old data folder; nth copies it on first launch and leaves it).
12. Grep the code before asking about or listing a feature (memory dont-reask-existing-features).
13. The owner reviews UI on the final installer; do not block on pre-release screen checks. Motion: continuous, shared poses, no snaps, interruptible springs (memory motion-hub-poses-no-fades).
14. Record every owner decision verbatim as an OD. Reply in English, plain words, even when the owner writes Hindi or Urdu.

### 2.7 File map
| What | Where |
|---|---|
| Main checkout (github.com/Oak993/marrow-port) | E:/Droppy/port, branch port/windows-exact-parity. Its local claude/release-integration ref is stale (a394de7e4); origin/claude/release-integration = 9e083470c. |
| Shipped source | E:/Droppy/wt/cl/pkg-morning (detached 87eb11490: src, tests, porting, scripts, docs) |
| Worktrees (about 340) | E:/Droppy/wt/cl (Claude), cx (Codex), u (unit agents), fl (Fledge), integration (impl/b2). Staging: origin/claude/verify-terminal-20261001 = 79306e519 (unchanged since 10-08). |
| Installer, note | E:/Droppy/Installer/nth-Setup-1.0.0-UNSIGNED.exe, BUILD-NOTE.txt; old ones in C:/nth-archive |
| Mac source (read only), videos | E:/Droppy/reference/droppy-code-v1.7.0; E:/Droppy/work/reference-video/ (INDEX.md, CATALOG.md, study/, frames/, frames.py; a frame is a source fact) |
| Owner test, speed work | E:/Droppy/work/scratch/lead/: owner-test-2026-10-14.md (#1-#86), speed-plan-2026-10-14.md, speed-plan-findings-2026-10-14.md, avalonia-speed-research-2026-10-14.md |
| Handoffs | E:/Droppy/work/NEXT_SESSION_CLAUDE.md (LATEST block = lines 1-476, log from line 48; 1697 lines at 16:34; line numbers drift by one per appended log entry), HANDOFF_LIVE_STATUS.md, HANDOFF_PROMPT.md, HANDOFF_CLAUDE_20X_2026-10-09.md, HANDOFF_CLAUDE_PRO_2026-10-06.md |
| Dossiers, merge plan | E:/Droppy/work/handoff/ (calculi-and-native-runner, dextra-assistant, glass-and-tactus, installer, lean-pass, merge-gate-release, session-hotkeys, web-visus-tabula); E:/Droppy/work/scratch/lead/merge-plan-2026-10-11.md |
| Designs, ideas | E:/Droppy/work/design/ (published/INDEX.md lists every page); E:/Droppy/work/next-version-ideas.md |
| Decisions, contract | <worktree>/porting/: owner_decisions.md, engineering_decisions.md, PORT_CONTRACT.md, contract_amendments.md, STATE.md; MASTERPLAN.md and PROGRESS.md in E:/Droppy/port/porting |
| Verify scripts | E:/Droppy/work/claude-verify/ (one.sh, one-full.sh, gate-seq.sh, verify.sh, handoff_log.py, handoff_status.py, limit-park.sh, memguard.ps1, stale_check.ps1) |
| Lead scripts, briefs | E:/Droppy/work/scratch/lead/ (heavy.sh, gate-par.sh, gated-build.sh, CODEX-LOCAL.md, FEATURE-RULES.md, LEAN-TESTS.md, usage-rules.txt, codex-tasks/) |
| Codex reports, transcripts | E:/Droppy/work/scratch/handoff/codex-*.md; C:/Users/omera/.codex/sessions/2026/10/ |
| Workflow copies, lean pass | E:/Droppy/work/handoff-workflows/; E:/Droppy/work/lean-pass/ (AUDIT.md, RELEASE_PLAN.md, native/) |
| Code map (Graphify), memory | E:/Droppy/work/graphify/ (README.md, rebuild.sh, nth-src/graphify-out/graph.json; query with --graph, never build in a worktree); C:/Users/omera/.claude/projects/E--Droppy/memory/ (MEMORY.md) |

### 2.8 Not confirmed
- Test totals for UI and App at 87eb11490.
- The OD range behind Strike and Jewel (OD-0089..0095 is from a handoff, not re-read).
- How much of the 27-34 s first launch (#1) is the app and how much the busy PC.
- How the wallpaper fidelity hold was finally closed; the cause of row #52.

## 3. Current state and first-15-minutes checks

From draft s2. Written 2026-10-14 about 16:10 (PC local time, UTC+3). Section numbers in this part were renumbered when the file was assembled. Facts come from files and read-only git that the writer read. "(unverified)" marks what could not be confirmed. "log:N" means line N of E:/Droppy/work/NEXT_SESSION_CLAUDE.md. Paths are absolute.

### 3.1 Summary

- Version 1.0.0 of the app "nth" is built and final: commit 87eb11490, installer E:/Droppy/Installer/nth-Setup-1.0.0-UNSIGNED.exe. The owner is testing it. Nothing more goes into 1.0.0.
- The next release starts from origin/claude/release-integration = 9e083470c: the same source as 87eb11490 plus a CI file fix and ledger text (3.2.2).
- The LOCAL branch claude/release-integration in E:/Droppy/port is stale (a394de7e4). Do not branch from it.
- One unit is ready to merge: codex/kokoro 714bfbc3a (section 3.3).
- Earlier work that never landed in 1.0.0 and is not on the owner list: the web C1 robustness fixes, claude/perf-render, one dextra-as9 test commit, the V-T133 re-anchor (section 3.5), and the owner-approved but unshipped OD-0131 branch f3-chats with T43 storage location and the F8 contrast retune (section 3.3, 3.5 item 12).
- Work is parked since 2026-10-14 03:13 on the owner's order. After that the lead only wrote notes, the speed plan and the code map. No build, test or workflow runs now (section 3.4).
- Claude weekly usage is 96% (resets 2026-10-18 10:00Z). Codex had about 24% left at 2026-10-14 00:23 (log:425) and resets about 2026-10-18 08:00 (log:212).

### 3.2 The shipped 1.0.0

#### 3.2.1 Facts

| Item | Value |
|---|---|
| Commit | 87eb11490488e0858fd6fc5852067c76fb3b0104, 2026-10-14 00:09:38 +0300, "Merge codex/composer-stack 8987acfe6: reuse bounded software glass layers for unchanged composer frames (COMPOSER-STACK pill)" |
| Worktree | E:/Droppy/wt/cl/pkg-morning, detached HEAD, clean, bin/ and obj/ present |
| Installer | E:/Droppy/Installer/nth-Setup-1.0.0-UNSIGNED.exe, 188,453,376 bytes (180 MB), built 2026-10-14 00:11, gated, built by the lead, never run by an agent; the owner installed it on this PC (log 2026-10-14 00:25) (E:/Droppy/Installer/BUILD-NOTE.txt) |
| sha256 | 384013ba6935b24eaab74a3783047564404d4af6a8d1c767c14bcd809ee34f77 (computed at write time; first 16 digits equal BUILD-NOTE.txt; the copy in pkg-morning/artifacts/installer matches) |
| Signing, publisher | Unsigned; publisher `oak993`; no clean-VM gate; no Microsoft submissions (E:/Droppy/work/handoff/installer.md:21-24) |
| Build command | `pwsh -NoProfile -File scripts/package-windows.ps1 -SkipUiSmoke` in pkg-morning (lead only, heavy slot; for the next release use a new worktree, section 7.6.3). Its restore step exits 1 here only because this PC's Node differs from the pinned 24.19.0 (log:419, 423). |
| Owner install | The setup said "nth is up to date" on the same version (by design); the owner was told to uninstall the old nth first (log:427). Older setups are in C:/nth-archive/Installer-old-2026-10-14 (log:440). |

#### 3.2.2 Branch situation (easy to get wrong)

- `git branch -a --contains 87eb11490` in E:/Droppy/port lists exactly 5: claude/final-pre, codex/kokoro, remotes/origin/claude/final-pre, remotes/origin/claude/release-integration, remotes/origin/codex/kokoro.
- origin/claude/release-integration = 9e083470c (2026-10-14 00:23). Local claude/final-pre = 9e083470c too. I confirmed the origin value with one `git ls-remote` at write time.
- 9e083470c = 87eb11490 + 3 commits: 5ba9ad955 (fix .github/workflows/web-canary.yml, which failed "No jobs were run" on every push), db67241c7 (ledger text only), and the merge. `git diff --stat 87eb11490 9e083470c` = 2 files (web-canary.yml +4/-2, porting/engineering_decisions.md +18). So the code equals the installer's (log:424, 426).
- Local claude/release-integration = a394de7e4 (2026-10-13 15:11), 648 commits behind 87eb11490, checked out and clean in E:/Droppy/wt/cl/rel-int. The landings of 2026-10-13/14 were pushes of claude/final-pre to origin/claude/release-integration; the local ref was not advanced (inferred).
- E:/Droppy/port is on branch port/windows-exact-parity (head 9f1413954, 2026-10-03): 6442 commits behind 87eb11490, 102 ahead. It is the git hub (origin = https://github.com/Oak993/marrow-port.git), not a place to work.
- Counts: 1200 local branches, 319 remote refs, 342 worktrees; 349 local branches are not merged into 87eb11490, nearly all old.

#### 3.2.3 What is in 1.0.0

Checked: `git rev-list --count 87eb11490..<branch>` is 0 for each. The release scope is in: Tactus PC control (claude/pc-control), 29 downloads (claude/downloads-live), 21 composer lights (claude/composer-light-v2), iOS 27 glass (claude/glass-look), premium installer (claude/installer-build), Visus and Tabula with three web security reviews (claude/web-build, claude/webreview-land 4ddb61cfc), lean pass (claude/lean-r1), Calculi (claude/calculi), Dextra with Armilla (claude/dextra-integration cf7fcd647), session hotkeys, text size, Settings in the main window, sidebar modes, voice UI, soft scroll, the security-gate fixes (claude/sec-final-a/-b/-c), single-card mission PC (MISSION-PC 68d1b0063; owner 2026-10-13: S1 option B - missions keep Tactus and Visus through Dextra approval; the provider prompts stay ordinary auto-accept and the Dextra adapter is the only gate; do not add a second card), Settings reveal, Muse Wonder 1.0 and Gemini 4 Argon rows (codex/muse-wonder), the PlanLimits hang fix, and the composer pill software-glass cache (8987acfe6). The perf ED families idle, memory, motion, startup-structure and ui-thread are in the shipped ledger; perf-render is not (3.5 item 5).
Highest owner decision in the shipped tree: OD-0156 (E:/Droppy/wt/cl/pkg-morning/porting/owner_decisions.md:1362). Next free: OD-0157; check other branches for a collision first. Engineering decisions use ED-NEW-<slug>-N.

### 3.3 Branches and units for the next release

"Local only" = no origin/<branch> ref in E:/Droppy/port, so the only copy is on this PC.

| Branch | Head | What it is | State | Documented in |
|---|---|---|---|---|
| origin/claude/release-integration (= local claude/final-pre, worktree E:/Droppy/wt/cl/final-pre) | 9e083470c | Base of the next release | Pushed. Worktree has one untracked folder (tests/Marrow.Services.Tests/TestResults/). | 3.2.2 |
| codex/kokoro (worktree E:/Droppy/wt/cx/kokoro, clean) | 714bfbc3a | "Add optional CPU Kokoro voices for Dextra". 2 commits over 87eb11490 (5ba9ad955 + 714bfbc3a), 35 files, +1846/-13 (714bfbc3a alone: 34 files, +1842/-11) | READY, not merged, pushed. Owner row #80. 3619 scoped passes, gates A/B pass, 1 quiet Native pass (02:42), review clean. Installer grows 74.7 MB; optional voice download 168.98 MB. Hindi and other languages: quality unverified. | E:/Droppy/work/scratch/handoff/codex-KOKORO-report.md; E:/Droppy/work/scratch/lead/codex-tasks/CU-KOKORO.md; ED-NEW-kokoro-1..3 |
| claude/vt133-reanchor-next | 5f4c2d102 | 1 commit: re-anchor V-T133 to the pill's rendered position | Local only; waits for the next feature release (3.5 item 1) | OD-0137, OD-0156 |
| claude/web-c1-trace (worktree wt/cl/web-c1-trace); also web-c1-fixes a2a931550, web-c1-a/-b/-s | 8e2c7fde8 | Fixes for Codex review C1/C1b of the web surfaces. 20 commits not in 87eb11490, 16 patch-new | NOT in 1.0.0; local only (3.5 item 4) | log:101, 147-148; E:/Droppy/work/scratch/handoff/codex-C1-web-review.md |
| claude/perf-render | 9726fb71a | 4 commits: 60 Hz caret blink, Hydra rings and spinner at 60 Hz slots, input wakes ambient sources after a quiet second, native checks | NOT in 1.0.0; local only (3.5 item 5) | log:255-259 |
| codex/dextra-as9 | 1f80be4e1 | 1 commit: Native toast checks use their own test identity (DecisionToastNativeTests.cs) | Not in 1.0.0; local only; small | log:285, 296 |
| claude/sidebar-slide | 9d0e829a3 | Old sliding sidebar (OD-0139 on that branch only; that number is now the model-line decision; the shipped sidebar decision is OD-0153) | DISCARDED by the owner; branch kept on purpose; pushed | E:/Droppy/work/scratch/lead/merge-plan-2026-10-11.md |
| origin/claude/f3-chats (same head as local claude/f3-chats and codex/f3-chats; worktree E:/Droppy/wt/cx/f3-chats) | 8cdd0e156 | OD-0131 chats without a project, layout A (Chats section above Projects; hidden per-chat folders named after the chat; Open chat folder; storage location separate from a project's access folder) | 3 commits not in 87eb11490 (the branch starts at 9e2cd6d53, 1,379 commits behind 87eb11490: merge the base in first) + 23 uncommitted files in the worktree. PAUSED since 2026-10-08; 25 App acceptance cases pass. The OD-0131 text exists only on this branch (owner_decisions.md:1188 there; 0 matches in the shipped ledger). | E:/Droppy/work/scratch/lead/codex-tasks/T40-f3-chats.md; HANDOFF_CLAUDE_20X_2026-10-09.md:326 |
| T43 storage location (no branch yet; planned codex/storage-location from codex/f3-chats, worktree E:/Droppy/wt/cx/storage-location) | - | Settings: storage location on any drive (the storage half of OD-0131) | NOT STARTED; waits until T40 (f3-chats) is READY | E:/Droppy/work/scratch/lead/codex-tasks/T43-storage-location.md |
| codex/f8-contrast (local only: no origin/codex/f8-contrast; worktree E:/Droppy/wt/cx/f8-contrast) | 9e2cd6d53 (already inside 1.0.0) + 10 uncommitted files | F8 palette chip contrast retune (at least 4.5:1, keep the hue) | PAUSED; one hue check was failing on 2026-10-09 (HANDOFF_CLAUDE_20X_2026-10-09.md:323); owner design pick | E:/Droppy/work/scratch/lead/codex-tasks/T42-f8-chip-contrast.md |

The 10 newest unmerged branches are these rows plus codex/composer-stack (ledger only, already in 9e083470c), claude/perf-motion-f2f3 (0 patch-new commits, no action) and claude/web-c1-fixes/-s/-b. Planned work with no branch yet: the speed plan E:/Droppy/work/scratch/lead/speed-plan-2026-10-14.md (waits for owner approval and picks 1-9) and owner rows #1-#86 in E:/Droppy/work/scratch/lead/owner-test-2026-10-14.md. Neither has started.

### 3.4 What ran at the park, and what runs now

At the park (2026-10-14 03:13, log:463): nothing was mid-merge; no claude/* worktree had changes to commit (limit-park dry run); no Codex thread had written for 60 minutes (Kokoro finished about 02:42). The lead stopped the stale timer, the Codex watcher, the metrics job and the status writer. The bin/obj clean-up of about 338 finished worktrees finished at 01:51 (6376 folders; /c/nth-archive/clear-buildout.log), so a first build in an unused worktree is a full rebuild.

After the park (no build work): owner rows #81-#86 were added (14:08-16:13); a research workflow (wf_701a80d7-85f, 111 findings) ended 14:38 and fed the speed plan; the graphify code map was built 14:56-15:00 in E:/Droppy/work/graphify/ (code only, no model).

Observed 2026-10-14 16:06-16:07:
- No dotnet, testhost, VBCSCompiler or msbuild process. By name: nth.exe 1 (owner test), claude.exe 13, ChatGPT.exe 19, codex.exe 2, node.exe 11 (not inspected, unverified), bash.exe 3, pwsh.exe 1, no python. Port 8765 (graphify preview) is not listening.
- Free RAM 5,730 MB of 31,860 MB. C: 50 GB free. E: 649 GB free.
- No heavy lock: no heavy.<n>.lock folder, heavy-wait is empty, no *lock* entry at depth 1 of E:/Droppy/work/scratch/lead/ (heavy.sh keeps its slots there).
- HANDOFF_LIVE_STATUS.md was last written 15:04 and no python runs, so its writer is stopped. Its STOPPED or RUNNING rows older than 2026-10-13 are superseded by the log; do not redo them.

### 3.5 Open items not on the owner list

1. V-T133 re-anchor. OPEN. OD-0137 schedules it for the next feature release. OD-0156 (owner_decisions.md:1362) accepted 30 speck identities and 1 borderline case until then; line 1231 amends the OD-0128/0137 list. The cases keep failing on record. Start from claude/vt133-reanchor-next 5f4c2d102. Any other new V-T133 failure and any V-T132 failure still block.
2. Accepted, known-red and flaky tests on the 1.0.0 base. OPEN as a list for the next gate.
   - Owner-accepted: ComposerVoiceCleanPillTests (183 cases, OD-0128/0137), the 200% Float 26 px dark pop speck (OD-0152, owner_decisions.md:1272), OD-0156's 31 cases.
   - Base reds the log calls pre-existing: GapModelDownloadAcceptanceTests.GAP_A_voice_model_partial_is_resumed... (fails alone twice on the old base too, log:369), GapCatalogue, PcPauseWait, SidebarRowUpdateDot, VelopackStartupHookTests (log:380; today's state unverified, reworked at log:158-162, 309).
   - Flakes that pass alone: SkillOverlay Cursor_without_a_generation, AntigravitySessionTests interrupt, MCPHubDeadlineTests exit 9, HandshakePainterTests alloc, ACPSessionTests.OpenSession_falls_through..., WallpaperCardTests.Both_row_buttons_disabled, EffortSlider jewel warmup, DextraSmoothSheen Cached_shimmer, ReserveGuard. A failure counts only if it fails alone twice.
   - Slow: ComposerPillClearsTextTests, about 19 minutes; slowest case 119.5 s and 87.8 s against a 150 s gate (log:405, 426).
3. Native tests. OPEN (unverified whether any ran later). The gates excluded Native (`!~Native`). Owed: WebSecReviewNativeTests x2 (log:356); 3 new and 1 changed Native from web-sec-res (log:340); 3 by-eye cases in E:/Droppy/work/scratch/lead/native-smooth-light.md (log:361); DX-OPEN3 Native (log:378). Run only in the lead's quiet window (no typing, no browser). Also owed (not known to have run): Tactus - scripts/pc-control-overlay-spike.ps1 (NP-15/16/18, np11-rdp twice), scripts/pill-native-probes.ps1 (NP-17/19/20/21, -Capture), PillCaptureNativeTests with MARROW_TACTUS_EXE, scripts/pill-canvas-metrics.mjs (Node 22), a full desktop pass, and paste the RESULT lines into ED-22 item 6 (log:188, :78); lean N1-N10 (E:/Droppy/work/lean-pass/native/run-native.sh; none ever produced a result); web spikes W0 a-i and W21; the Nocta revert-sheet Return test; installer --ui-smoke (only with the lead's word; the package script skips it with -SkipUiSmoke) (NEXT_SESSION_CLAUDE.md:39). These need hands and eyes: run only in the quiet window.
4. Web review C1/C1b. NOT LANDED. claude/web-c1-trace 8e2c7fde8 was to be fast-forwarded into web-build (log:148, 162) but is not an ancestor of 87eb11490. Its new test files (e.g. PaneLayoutTinyTests.cs, NthUiEqualityBudgetTests.cs, WebC1LifecycleRegressionTests.cs) are missing from the shipped tree and the shipped ledger has no ED-NEW-web-c1* entry. The security half (ask_user spoof, CSP in a comment, cold model call) was redone as web-sec-review S1-S4 (log:356). Whether the lifecycle and data findings (log:101) were fixed another way is unverified. Land the branch or re-check each finding.
5. claude/perf-render (and codex/dextra-as9 1f80be4e1, one Native test file, low value). NOT LANDED. `git cherry` shows 4 commits not applied and the shipped ledger has no ED-NEW-perf-render-*. The log shows only that it held the landing lock on 2026-10-13 04:34-05:05 and that rel-int then went to 6f7204918 (perf-memory); why is unverified. It paces motion at 60 Hz, the owner's display runs at 239 Hz and the speed plan wants one shared clock, so review it against the plan before merging any of it.
6. Security gate leftovers (E:/Droppy/work/scratch/handoff/security-gate-final.md). 0 blockers; S1-S5, S7 and most notes were fixed (log:383, 388). OPEN: S6 (exported standalone HTML is not contained outside nth; accepted residual; whether its ED exists is unverified), N8 (auto-allowed build-file edit then allowlisted build command; owner design review), N10 (AssistantWebClients has no SSRF guard; add one before wiring it), N1 (verify one real Command Code answer on the installer).
7. Native runner total wait cap. DONE 2026-10-12 10:20 (log:94): E:/Droppy/work/lean-pass/native/run-native.sh has NATIVE_TOTAL_WAIT_MIN=20 (exit 76) and NATIVE_RUN_MAX_MIN=60 (exit 77).
8. Worktrees with unpushed or uncommitted work. OPEN; the owner has not reviewed them. List: E:/Droppy/work/cleanup-2026-10-10.txt. Its "WILL REMOVE" part (176 clean, pushed worktrees) is DONE: 0 of those paths exist. Its "KEEPING" part has 131 paths (70 unpushed, 48 uncommitted, 11 in use, 2 status error); all still exist. At write time 66 had a HEAD on no remote branch and 79 had uncommitted or untracked lines (the older figure "118" is from 2026-10-11 13:56, log line 1697 at 16:34). No permanent deletion without the owner confirming a list.
9. Release feed and publishing. OPEN, owner only. The feed is the public repo https://github.com/Oak993/marrow-releases (OD-0100; created 2026-10-04, README only, no release then; log:703-705). No log line shows a release since. Every publish, signing step or upload needs the owner's word (log:644, 658).
10. Disk. C:/nth-dumps holds 32 GB of old hang dumps moved off E: (log:386, 400); C:/nth-archive holds old installers and the clean-up scripts. Do not delete without the owner's list.
11. Optional: Look-card descriptions stay at 11 pt unless the owner asks for 13 pt. Closed: the public-licence work was dropped (log:421); the web-canary.yml fix is done (5ba9ad955, origin only).
12. Owner-approved work that never shipped (also in section 3.3). (a) OD-0131 chats without a project, layout A: origin/claude/f3-chats 8cdd0e156 (= local claude/f3-chats and codex/f3-chats), worktree E:/Droppy/wt/cx/f3-chats with 23 uncommitted files, PAUSED, 25 App acceptance cases pass; the branch starts at 9e2cd6d53, 1,379 commits behind 87eb11490, so merge the base in first; the OD text exists only on this branch. (b) T43 storage location: not started; brief codex-tasks/T43-storage-location.md; waits for T40. (c) codex/f8-contrast: the commit 9e2cd6d53 is already inside 1.0.0; E:/Droppy/wt/cx/f8-contrast holds 10 uncommitted files of the palette chip contrast retune (at least 4.5:1, keep the hue; one hue check was failing); owner design pick; brief codex-tasks/T42-f8-chip-contrast.md. These overlap rows #13, #44 and #39 (new chat, titles). Memory rule no-deferring-features: build found improvements into the release unless the owner says otherwise. The owner decides in NR0.3(e).

### 3.6 First 15 minutes: read-only checklist

Run in Git Bash. Nothing here writes, builds, tests or starts the app. Expected values are what the writer saw at 16:07; a different value means somebody worked since, so find out why first.

```
# 1. Heads (minutes 0-2)
cd /e/Droppy/port
for r in origin/claude/release-integration claude/final-pre codex/kokoro claude/release-integration; do echo "$r $(git rev-parse --short=9 $r)"; done
#   expect 9e083470c 9e083470c 714bfbc3a a394de7e4, one after each name (the last is stale on purpose)
git -C /e/Droppy/wt/cl/pkg-morning rev-parse HEAD                        # 87eb11490488e0858fd6fc5852067c76fb3b0104
git merge-base --is-ancestor 87eb11490 origin/claude/release-integration && echo ok
git diff --stat 87eb11490 origin/claude/release-integration | tail -3    # 2 files: web-canary.yml, engineering_decisions.md
git rev-list --count 87eb11490..codex/kokoro                             # 2
git branch -a --contains 87eb11490 | wc -l                               # 5
git for-each-ref --no-merged 87eb11490 refs/heads | wc -l                # 349
git worktree list | wc -l                                                # 342

# 2. Worktree status (2-4)
for w in port wt/cl/final-pre wt/cl/rel-int wt/cx/kokoro wt/cl/pkg-morning; do
  echo "$w: $(git -C /e/Droppy/$w status --porcelain | wc -l) lines, head $(git -C /e/Droppy/$w rev-parse --short=9 HEAD)"; done
#   expect port 1 (?? wt/), final-pre 1 (?? TestResults), rel-int 0, kokoro 0, pkg-morning 0

# 3. Local-only branches (4-5): a branch never pushed prints an empty value
for b in claude/web-c1-trace claude/perf-render claude/vt133-reanchor-next codex/dextra-as9; do
  echo "$b: $(git rev-parse -q --verify origin/$b | cut -c1-9)"; done      # expect four empty values

# 4. Installer (5-6)
ls -l /e/Droppy/Installer/nth-Setup-1.0.0-UNSIGNED.exe                   # 188453376 bytes, Oct 14 00:11
sha256sum /e/Droppy/Installer/nth-Setup-1.0.0-UNSIGNED.exe               # 384013ba6935b24e... (under 1 s)
cat /e/Droppy/Installer/BUILD-NOTE.txt

# 5. Processes (6-8)
tasklist | grep -i -E "dotnet|testhost|VBCSCompiler|msbuild"             # expect no output
tasklist | grep -i -c "nth.exe"                                           # 1 while the owner tests
# optional leftovers list (never -Kill): powershell -NoProfile -File E:/Droppy/work/scratch/lead/park-2026-10-11/workflows/stale.ps1

# 6. RAM and disk (8-9)
powershell -NoProfile -Command '$o=Get-CimInstance Win32_OperatingSystem; "{0} MB free of {1}" -f [int]($o.FreePhysicalMemory/1024),[int]($o.TotalVisibleMemorySize/1024)'
df -h /c /e | tail -2                                                     # keep 5-6 GB RAM free before a heavy run

# 7. Heavy locks (9-10)
ls -d /e/Droppy/work/scratch/lead/heavy.*.lock 2>/dev/null                # expect nothing
ls /e/Droppy/work/scratch/lead/heavy-wait                                 # expect empty
find /e/Droppy/work/scratch/lead -maxdepth 1 -iname "*lock*"              # expect nothing (where rel-land, web-land and native locks live is unverified)

# 8. Handoff and owner list (10-12)
head -2 /e/Droppy/work/HANDOFF_LIVE_STATUS.md                             # stamp 2026-10-14 15:04:21 = writer stopped
sed -n 473,475p /e/Droppy/work/NEXT_SESSION_CLAUDE.md | cut -c1-160       # newest log lines; the LATEST block ends at 476 (# PREVIOUS starts at 477), the file has 1697 lines
grep -c "^| [0-9]" /e/Droppy/work/scratch/lead/owner-test-2026-10-14.md   # 86 rows
```

Minute 12-13: call the session tool get_usage (expect Claude weekly about 96%) and look at the Codex app's remaining percent by eye. Minutes 13-15: read the other sections of this handoff, then agree with the owner which items of section 3.5 (1, 4, 5 first) join the next release.

Standing rules: section 10. Log each finished task: section 11.

### 3.7 Text that is out of date (read the log instead)

- NEXT_SESSION_CLAUDE.md top block section 2 ("Work status at 2026-10-12 01:55") and "claude/release-integration (0a946c558)". The log (lines 48-475) wins; lines after 476 are history.
- NEXT_SESSION_CLAUDE.md section 0 ("How to restart work in a NEW session", lines 13-18) tells a new session to reopen old workflow scripts and restart the cut-off work. Do NOT: all that work is merged or parked. The same holds for HANDOFF_PROMPT.md line 1 and the first line of that file (see the ENTRY POINT note in 1.4). Its section 1 ("at most 2 build workflows at once") is also old (7.3).
- NEXT_SESSION_CLAUDE.md section 5 (line 46) says "no Codex work until the owner says so". That is superseded: the owner allowed Codex again on 2026-10-13 (reset 10:01; "use Codex to the full, full speed, save usage" at 10:18; log:298, :300). Still true while parked: no new Codex thread without the owner's approval; check the Codex usage card first; threads must be "This computer".
- E:/Droppy/work/scratch/lead/merge-plan-2026-10-11.md ends on 2026-10-12. The dossiers in E:/Droppy/work/handoff/ date from 2026-10-12 about 02:20, before the builds landed; their scope is in 1.0.0.

## 4. NEXT RELEASE MASTER CHECKLIST

Status: PROPOSED. The owner approves the order. Nothing here has started; work is parked (section 3.4).

This list orders the work. It does not repeat the 86 owner rows (section 5) or the 40 speed items (section 6); it points to them. Items here are numbered NR<phase>.<n>. Tick rule (section 11): change `- [ ]` to `- [x]` and append `Done: <short SHA> <YYYY-MM-DD>`; never tick without a commit SHA (or, for an owner decision, the owner's words and the date).

### 4.1 Ordering rules behind the proposal

1. Approval first (NR0). The speed plan has its own gate: Phase 0 of section 6.4 (the speed gate) runs BEFORE any speed change, so every later change has a before and after number from the owner's PC.
2. Crashes first among the rows: #52 (every send crashes), #36, #7. They block all chat use. They touch different code from the speed gate, so they run in parallel with it (Phase 1).
3. Merge the ready units early (Phase 1) so later work builds on them.
4. Look rows that touch the glass or the composer light (#31, #32, #33, #58, #60, #83, #84) change the same code as speed items W1 to W5. Do them right after speed Phase 2 (section 6.4), with one look check. This is a proposal to avoid conflicts and double checks.
5. Claude's weekly usage is 96 percent until 2026-10-18 10:00Z. Time-boxed build and fix units (about 30 minutes each) go to Codex or another model; Claude reviews, merges and runs gates (sections 8.5.6, 8.6, 7.5). Check the Codex usage card first.
6. Time (section 6.6, last line): speed work about 2.5 weeks (phases 2+3 and 4+5 can run side by side); the whole next release about 3.5 to 4 weeks.
7. Base branch: origin/claude/release-integration = local claude/final-pre = 9e083470c (same code as 87eb11490). The local claude/release-integration ref (a394de7e4) is stale; do not branch from it (section 3.2.2, 7.1).

### 4.2 Phase 0: Approval and setup (owner and lead; no code)

- [ ] NR0.1 Run the read-only checks of section 3.6; correct section 3 wherever a value changed.
- [ ] NR0.2 Owner approves the speed plan and its order, and answers picks 1 to 9 and the open questions in section 6.5 and 6.5.1 (recommended answers for picks 1-9 are in 6.5; 6.5.1 holds open questions with no recommendation yet). The open questions block I6, I9, P3, W1, W6 and G1.
- [ ] NR0.3 Owner approves THIS order, and says which open items of section 3.5 join the release: V-T133 re-anchor (item 1), web C1/C1b (item 4), perf-render (item 5), security leftovers (item 6); and (e) whether f3-chats (OD-0131), T43 storage location and f8-contrast join the release (item 12 and section 3.3; recommended: yes for f3-chats).
- [ ] NR0.4 Record the owner decisions that unlock work, as ODs (next free number OD-0157; check other branches for a number clash first, renumber at merge, section 7.5): (a) row #85, an OD that replaces OD-0008 C1 (close hides to the tray; "Exit" or "Quit" as one word across the app; the item named exactly "Reload" is already decided); (b) row #45, the dim cold Ignis line; (c) every PICK-likely row of section 5.5.3 (#31, #44, #59, #76, #69 item 5); (d) new UI needs the owner's canvas approval before building: #34, #76, #44, #85 menu, and items 2, 3, 4 and 6 of #86; (e) row #86 (Ignis): which of its items 1-7 to build (PICK, stated in the row).
- [ ] NR0.5 Choose the base (section 7.1) and create each unit's branch and worktree from it. Check the base is clean and that no work happened since the park. Command: `git -C E:/Droppy/port worktree add E:/Droppy/wt/cl/<unit> -b claude/<unit> origin/claude/release-integration` (Codex: E:/Droppy/wt/cx/<unit>, branch codex/<unit>). Never branch from the local claude/release-integration (a394de7e4).
- [ ] NR0.6 Check usage and route work: Claude `get_usage` and the Codex usage card (never click "Use reset"; Codex threads must be "This computer"). Pick models by section 8.5.1.
- [ ] NR0.7 Check RAM, disk and locks (section 3.6 steps 5 to 7). Reserve quiet-PC slots for gate runs: about an hour per full run, app maximised and focused, nobody types (pick 9). Prune E:/Droppy/work/scratch/lead/heavy-priority.txt to the paths of the new release work: it still lists six old paths (wt/cl/rel-int, gate-prep, perf-motion2, motion2-tipcheck, hydra-chk, gate-tip) that would get the free slots first.
- [ ] NR0.8 If the base is not the 1.0.0 code, rebuild the graphify map (section 9.6) before relying on it.

### 4.3 Phase 1: Baseline, blockers, ready units (three lanes in parallel)

Lane 1a, the speed gate:

- [ ] NR1.1 Build G1 to G4 (section 6.4, Phase 0). Store a BASELINE of scenarios S0 to S9 on the 1.0.0 payload on the owner's PC before any speed change. Nothing in a background, unfocused or non-maximised window counts. Codex writes the harness code and tests only; baseline and gate runs that launch nth are run by the lead in the quiet window under native.lock (section 7.2.5).

Lane 1b, blockers (area 5.3.1):

- [ ] NR1.2 Fix #52, then #36 (with its test), then #7 (cause unknown). #52: the error mapping must never throw, and the InvalidDataException at turn start must be found and fixed. Add an install-and-use smoke run: nothing tested the installed app end to end (section 2.5, problem 17). A real model send happens only on the owner's explicit word; use a fake provider.

Lane 1c, units that are ready or half-landed (state in section 3.3 and 3.5):

- [ ] NR1.3 Merge codex/kokoro 714bfbc3a (row #80): review, lean tests, ledgers (ED-NEW-kokoro-1..3), check the report. The installer grows 74.7 MB; the optional voice download is 168.98 MB. Hindi and other languages: quality unverified.
- [ ] NR1.4 V-T133 re-anchor: start from claude/vt133-reanchor-next 5f4c2d102. OD-0137 requires it before the next feature release ships (section 7.6.2). Afterwards the gate-seq3 exclusions go. Any other new V-T133 failure and any V-T132 failure still block.
- [ ] NR1.5 Web review C1/C1b (section 3.5 item 4): land claude/web-c1-trace 8e2c7fde8 or re-check each finding another way. Its tests are missing from the shipped tree.
- [ ] NR1.6 claude/perf-render 9726fb71a and codex/dextra-as9 1f80be4e1 (section 3.5 item 5): decide AFTER the speed plan is approved. perf-render paces motion at 60 Hz; the plan wants one shared clock (P3 to P5). Review against the plan; do not merge blindly.
- [ ] NR1.7 Security leftovers (section 3.5 item 6): S6 (accepted residual; confirm its ED exists), N8 (owner design review), N10 (SSRF guard before AssistantWebClients is wired), N1 (verify one real Command Code answer on the installer; a real send needs the owner's explicit word).
- [ ] NR1.8 Resume f3-chats on the new base (only if the owner says yes in NR0.3(e)): merge origin/claude/release-integration into claude/f3-chats (it starts at 9e2cd6d53, 1,379 commits behind), keep the 23 uncommitted files in E:/Droppy/wt/cx/f3-chats, finish T40 (OD-0131), then T43 (storage location). f8-contrast (10 uncommitted files in E:/Droppy/wt/cx/f8-contrast) waits for the owner's design pick. Briefs: E:/Droppy/work/scratch/lead/codex-tasks/T40-f3-chats.md, T43-storage-location.md, T42-f8-chip-contrast.md (section 3.3).

### 4.4 Phase 2: Speed phases (section 6.4)

Each phase ends with before and after numbers from the speed gate. Individual items are ticked in section 6.

- [ ] NR2.1 Speed Phase 1, "Stop the waste": P1 to P8. Solves row #77 (P1) and row #78 (P2); lowers #38.
- [ ] NR2.2 Speed Phase 2, "Background": W1 to W8. Owner picks 3, 6 and 8 apply. The look rows #31 and #32 interact (my reading).
- [ ] NR2.3 Speed Phase 3, "Typing, hovering, streaming, sidebar": I1 to I9.
- [ ] NR2.4 Speed Phase 4, "Lists and memory": L1 to L5 and L1b (galleries and pickers). Rows #25 and #35 are solved by L1b, not by L1; row #29 belongs here and to S3 (the mapping of rows to item IDs is my reading, unverified; L1b follows finding R3.11 and t3-vs-nth-2026-10-14.md).
- [ ] NR2.5 Speed Phase 5, "Start-up": S1 to S4. Rows #1 and #2.
- [ ] NR2.6 Speed Phase 6, "Avalonia update": A1, the 12.1.4 trial on a side branch (pick 7). Stay on 12.1.2 unless every check passes.
- [ ] NR2.7 Re-run the whole speed gate; the targets of section 6.4.6 are met on the installer payload; the owner gets full-frame-rate captures to compare with the Mac videos.

### 4.5 Phase 3: Owner rows by area (section 5)

One box per area. Tick it when every row of that area is ticked in section 5.3. Order inside an area and the dependencies are in section 5.3.

- [ ] NR3.1 5.3.1 Crashes and hangs: #52, #36, #7 (done in NR1.2; tick when all three are ticked).
- [ ] NR3.2 5.3.2 Speed and smoothness: #79 (the umbrella; tick it last), #77, #78, #38, #1, #2, #29, #25, #35. These are solved by the speed plan (NR2.1 to NR2.5); do not work them twice. #25 and #35 are solved by L1b, not by L1.
- [ ] NR3.3 5.3.3 First-run defaults: #55 (BaseScale 1.3 x 0.9 = 1.17), #3 (with #55), #31 (PICK-likely; an animated default wallpaper must obey the idle rules).
- [ ] NR3.4 5.3.4 Glass, theme and look: #47, #58, #32, #59. Do #32 with the glass work of NR2.2.
- [ ] NR3.5 5.3.5 Composer, lights and working line: #74 (with #66), #73, #40, #83 and #84 together, #33, #60, #75, #61, #26.
- [ ] NR3.6 5.3.6 Ignis: #22, #23, #24 (a large data job), #45 (PICK: the owner's call), #86 (PICK: the owner chooses items 1-7; items 2, 3, 4 and 6 need canvas variants first).
- [ ] NR3.7 5.3.7 Dextra: #76 first (PICK-likely; new UI needs the owner's canvas approval). #12, #71, #49 close with it; #11 and #72 are one change; then #48 and #50. The crash #36 is in NR1.2.
- [ ] NR3.8 5.3.8 Sidebar and navigation: #39, #44 (PICK-likely), #13, #30. Check f3-chats (OD-0131: a Chats section above Projects; section 3.3) before building #13, #44 and #39.
- [ ] NR3.9 5.3.9 Window, panes and tray: #9, #10, #54, #37, #67, #46, #53, #34 (new UI) and #85. For #85 the tray menu is: "Show App" first; greyed plan and usage lines (no new provider call); an item named exactly "Reload" (owner order 2026-10-14 16:02: full shutdown, then a fresh start, asking the same running-turn question as Exit); "Exit" last. Needs the OD of NR0.4(a). A hidden nth must draw zero frames.
- [ ] NR3.10 5.3.10 Popovers, updates and menus: #81, #82, #41, #64, #15 as one updates rewrite; #56 and #51 as one menu fix; #68; #43.
- [ ] NR3.11 5.3.11 Settings pages: #66 first (it closes #8), then #57, #65, #69, #70, #42; #16 needs no change.
- [ ] NR3.12 5.3.12 Providers and accounts: #21 first (it may link to #52), then #20, #14, #17, #18, #19, #63.
- [ ] NR3.13 5.3.13 Downloads and packs: #28 (root cause open), #27 (design) before #6, then #5 and #4.
- [ ] NR3.14 5.3.14 Voice: #62 and #80 (#80 is NR1.3).
- [ ] NR3.15 Check the count: `grep -c "^- \[x\] #" E:/Droppy/work/HANDOFF_NEXT_RELEASE.md` prints 86 when all rows are done.

### 4.6 Phase 4: Integration and the full gate (lead only)

- [ ] NR4.1 Merge every unit branch into the base with the landing rules of section 7.5 (ledgers byte-exact, ODs and EDs renumbered at merge).
- [ ] NR4.2 Run the full gate once on the merged branch (section 7.6.1). Classify every red by section 7.6.2; re-run each red alone twice.
- [ ] NR4.3 Native window in quiet time (section 7.2.5). Owed from before: WebSecReviewNativeTests x2, the native tests from web-sec-res, the three by-eye cases in E:/Droppy/work/scratch/lead/native-smooth-light.md, DX-OPEN3 (section 3.5 item 3), plus the new natives of this release. Also owed (section 3.5 item 3; not known to have run): the Tactus scripts and natives (scripts/pc-control-overlay-spike.ps1, scripts/pill-native-probes.ps1, PillCaptureNativeTests with MARROW_TACTUS_EXE, scripts/pill-canvas-metrics.mjs; paste the RESULT lines into ED-22 item 6), lean N1-N10, web spikes W0 a-i and W21, the Nocta revert-sheet Return test, and installer --ui-smoke (only with the lead's word).
- [ ] NR4.4 Security review of the changed surfaces (accounts, tray and Reload, downloads, Kokoro download), with an Opus-tier reviewer (section 8.5.1). Proposed; not required by an owner order.
- [ ] NR4.5 Final speed gate and install-and-use smoke run on the release candidate (fake provider).
- [ ] NR4.6 Rebuild the graphify map (section 9.6). Update STATE.md's incomplete list and the ledgers.

### 4.7 Phase 5: Package (lead only; never run the installer)

- [ ] NR5.1 Preconditions (section 7.6.3): gate green apart from the accepted list, native window done, `df -h /e` fine, nothing else building.
- [ ] NR5.2 Bump the version (src/Marrow.App/AssemblyInfo.cs lines 9 and 10) and add a populated `## [<version>]` entry to CHANGELOG.md; pre-check with `-ValidateReleaseNotesOnly`. The version number is the owner's call (not decided).
- [ ] NR5.3 Run `scripts/package-windows.ps1 -SkipUiSmoke` from a NEW detached worktree in a heavy slot (`git worktree add --detach E:/Droppy/wt/cl/pkg-<version> <sha>`; log to E:/Droppy/work/scratch/lead/pkg-<version>/package.log). Never reuse E:/Droppy/wt/cl/pkg-morning: it is the 1.0.0 reference tree (section 7.6.3).
- [ ] NR5.4 Copy the installer to E:/Droppy/Installer/ with the new version in the name and write BUILD-NOTE-<version>.txt beside it. Do not move or overwrite nth-Setup-1.0.0-UNSIGNED.exe or BUILD-NOTE.txt until the owner has finished testing 1.0.0; only a setup older than 1.0.0 may be moved to Installer/old/ (the old setups are already in C:/nth-archive/Installer-old-2026-10-14; E:/Droppy/Installer/old/ holds only MOVED.txt).
- [ ] NR5.5 Never run the installer. Never upload; the script prints the upload line for the owner. Signing and publishing need the owner's word.
- [ ] NR5.6 Tell the owner: what is in, the accepted reds, that nothing was run or uploaded, and what to check by eye (rows tagged LOOK-V and LOOK-M, section 5.5.4).

### 4.8 Phase 6: The owner's own check and close-out

- [ ] NR6.1 The owner checks UI and motion on the final installer. The owner's new findings go into a new owner list file in E:/Droppy/work/scratch/lead/; fixes ship as a new installer. Do not block any step above on the owner's eye.
- [ ] NR6.2 Release feed, signing, publishing: owner only (section 3.5 item 9).
- [ ] NR6.3 Worktree and disk clean-up (section 3.5 items 8 and 10): only with the owner's confirmation of a list.
- [ ] NR6.4 Update section 3 to the new state, add the release to section 2.4, and append a log line (section 11).

## 5. Owner test list checklist

From draft s3. All 86 rows are kept below. Section numbers in this part were renumbered when the file was assembled (3.x areas are now 5.3.x).

Written 2026-10-14 from the owner's list file only.

### 5.1 How to use this file

- The authority is the list file: E:/Droppy/work/scratch/lead/owner-test-2026-10-14.md (115 lines, about 53 KB; `wc -l` prints 114 because the last line has no newline): a markdown table of 86 rows, #1 to #86 (columns # | Area | What happened | Evidence). This checklist is a short work copy. Read the full row before you work on it. Each item ends with `L<n>`, the row's line in the list file (#1-#19 are on line N+12, #20-#86 on line N+19; L105 is row #86).
- The list is the owner's test of installer 1.0.0 (commit 87eb11490, final for 1.0.0), written from 2026-10-14 00:27 and extended until 16:11 (row #86). Owner order: the rows are only noted; nothing is started; all rows are solved together in the NEXT release (above 1.0.0).
- Not rows, but in the list file: lines 8-9 (the test PC had only 1.2-1.6 GB RAM free; first-launch timings may be inflated, re-measure on a quiet PC), 33-37 (owner ask for #17), 107 (evidence for #1/#2), 109 (lead speed check on a quieter PC), 110 (T3 Code comparison: t3-vs-nth-2026-10-14.md), 112 and 115 (toggle test, parts 1 and 2). Before row #86 was added these were lines 106, 108, 109 and 111.
- Recordings of the test (rows cite "video N s", meaning seconds into the first one): C:/Users/omera/AppData/Local/Packages/Microsoft.ScreenSketch_8wekyb3d8bbwe/TempState/Recordings/20261013-2127-13.3265569.mp4 (7 min 28 s, 2558x1438, 30 fps; copy: C:/Users/omera/Videos/Screen Recordings/Screen Recording 2026-10-14 003444.mp4) and the same folder's 20261013-2200-49.7576022.mp4 (47 s tour of every Settings page, cited by row #29; copy: Screen Recording 2026-10-14 010140.mp4). Row #86 has its own video: E:/Droppy/work/scratch/lead/owner-testing/ignis-video-2026-10-14/ignis-reference.mov (+ sheet1.png to sheet3.png).
- Toggle test (list lines 112 and 115; log 2026-10-14 01:25-02:34): the lead opened the Settings pages and flipped each control and put it back, checking the end state: Ignis, Assistant, General, Hydra, Appearance, Customization, Voice typing, Providers, MCP, Plugins and Skills (one switch each), Tactus, Visus, Tabula, Models, Pills (menu only), Shortcuts, Archive and About. Updates and Source control were looked at only. Not touched on purpose: Open at sign-in, Desktop shortcut, Download/Update/Connect buttons, account switches, Clear browsing data, Import, Update (model points), Save report, Keep warm now, Tidy up now. Its findings are the rows that name it (for example #66, #67, #69, #70, #71-#75).
- To tick an item: change `- [ ]` to `- [x]` at the start and append `Done: <short SHA> <YYYY-MM-DD>` at the end. Never tick without a commit SHA.
- Item form: `#N title — what is wrong — Fix — Code — Notes`. "Fix" is the row's own "Expected". Where the row has none, the item says "as the row's Expected (none stated)": the goal is only that the symptom goes away.
- Tags in Notes: SPEED-PLAN = solved inside the speed plan; PICK = needs an owner decision before work (PICK-likely = my reading, the row does not say so); LOOK-V / LOOK-M = the owner should look at the result (visual / motion).
- "Code" names are as the row gives them (bare names). Section 5.4 maps them to paths in the shipped tree E:/Droppy/wt/cl/pkg-morning/src; row line numbers may have drifted 1-5 lines there.
- Owner rules for every item: section 8.7 (quality bar, decide-don't-drag, review on the final installer, no deferring) and section 10.

### 5.2 Words the rows use (as the rows use them)

- nth: the app (Windows port of the Mac app Droppy Code). Dextra: the in-app assistant (missions, helpers, a first-run setup card; Settings section "Assistant"); Armilla is its ring animation; Kokoro is optional CPU voices for it.
- Ignis: per-chat feature that keeps a chat warm and plans a tidy-up/rebuild point near the end of the model's window (model table, composer state "Cold", sidebar light, Settings page).
- Tactus: PC control (setting pcControl.enabled). Visus: in-app browser (Ctrl+Shift+B). Tabula: artifacts (Ctrl+Shift+A). Side chat: Ctrl+;. Background tasks: Ctrl+Shift+G. Hydra: the Settings > Hydra pair editor.
- Composer light: the glow around the composer (Favilla sparks, Stellaris...; Settings > Customization). Working line: the glass card above the composer with a word ("Combobulating...") and a timer.
- C1 / C2: first and second Claude account. OD: owner decision (porting/owner_decisions.md in a worktree).

### 5.3 The checklist

Areas are in suggested work order (blockers, speed plan, defaults and look, feature areas, settings and accounts). Inside an area, items are in suggested fix order.

#### 5.3.1 Crashes and hangs (blockers; fix first)

- [ ] #52 CRASH: sending any message — clicking Send does nothing (a winget.exe child process appears for a moment and no turn starts); Enter sends ("Noodling..." shows), then nth hangs briefly and closes (owner 01:49; lead repro 01:50 with Haiku 5.5, Low, C1, project Droppy, "Reply with one word: hello"). Same stack twice: NotSupportedException "No Swift text is defined for InvalidDataException" in SwiftErrors.LocalizedDescription, called from the catch in ThreadRuntime.StartTurn. Something at turn start throws InvalidDataException (source not found); the error mapping then throws and kills the app. Draft lost; window reopens non-maximized. — Fix: Send sends; a failed turn shows its real error in the chat and never closes nth (error mapping must never throw); find and fix the InvalidDataException. — Code: ThreadRuntime.StartTurn, SwiftErrors.LocalizedDescription, InstallerTurnLease.RunAsync / SpawnInstallerTurn; candidates AccountStore / AccountKeyStore / AccountDecisionStore reads, SkillSessionFactory, EnsureSessionOwned; dumps %LOCALAPPDATA%\CrashDumps\nth.exe.25232.dmp (01:49), nth.exe.63768.dmp (01:51), nth.exe.53984.dmp (01:07, #36), read with dotnet-dump for the hidden error text (print only the exception type and message; never dump strings or heap objects, they can hold account tokens). — Notes: Blocks all chat use on this PC. The saved multi-account record looks valid; the 159 skill files under ~/.claude/skills pass nth's header rules (not the cause). Check what spawns winget.exe on Send before the dump analysis. See #36, #20, #21. L71.
- [ ] #36 CRASH: opening Dextra from a chat — 01:07: clicking Dextra in the sidebar from a chat closed nth (WER APPCRASH report 477343fc-2615-4b2e-a5c7-791d77e2d06a, minidump in C:\ProgramData\Microsoft\Windows\WER). Unhandled InvalidOperationException "The Control already has a parent." at Decorator.set_Child <- AssistantHomeView.RefreshConversation <- AssistantHomeView.Refresh <- DetailView.Rebuild. Cause (code read): the first-run card from parts.FirstRun is one shared control still parented in the previous Dextra home view's decorator. — Fix: fix the card hand-over; never crash; add a test that opens Dextra, leaves to a chat and opens it again with first run not done. — Code: AssistantHomeView.cs:502-503, AssistantHomeView.Refresh, DetailView.Rebuild, parts.FirstRun. — Notes: See #12, #71, #76. Keep the test even if #76 removes the setup page. L55.
- [ ] #7 Hang after plugin downloads — After the plugin downloads, Settings > Skills showed "nth (Not Responding)": busy cursor, ghost title bar, Settings closed, focus kept being lost; same PID since 00:27:37 (no restart). — Fix: as the row's Expected (none stated). — Code: -. — Notes: Cause unknown. Suspect (t3-vs-nth-2026-10-14.md, "What nth does differently" item 4; unverified): blocking waits or synchronous file reads and writes on the UI thread (11 files with .Wait()/GetResult(), 23 UI files with synchronous file IO) while the plugin downloads write the skills store. Look at the Settings > Skills load path and the plugin download completion handlers first; speed items S2 and L1 touch the same code. See #4, #6, #29, #79. L19.

#### 5.3.2 Speed and smoothness (the speed plan)

Plan: E:/Droppy/work/scratch/lead/speed-plan-2026-10-14.md; it waits for owner approval and owner picks 1-9 (section 6.5). The owner's idea ("one engine, pieces in harmony") and the owner's 239 Hz display are in section 6.1; the plan's change IDs are the checklist in section 6.4.

- [ ] #79 Why nth feels slow: the engine, not small bugs (research + owner direction) — Research 02:36-03:05 against SourceGit, Wasabi, PixiEditor, Stability Matrix and T3 Code: nth is a line-by-line SwiftUI port (0 layout files, 2,645 hand-built controls, 132 clear-and-rebuild spots, 0 virtualised lists); macOS effects redrawn in-app at full resolution (140 shader references, 26 own render-thread drawers, glass that re-copies and re-blurs the window behind it); 258 private frame requests that can run forever; a full-window copy every frame; GPU cache at its 29 MB default; agents in the window's process; no speed gate in tests. At 239 Hz an unpaced animation draws up to 240 frames a second; the window-sized glass layer over the wallpaper is on by default and re-blurs the whole window every wallpaper frame. — Fix: owner direction 03:08: 100% Windows-optimised, highest visual quality, animated wallpapers just as great, fast start, smooth, light on hardware, no quality drop. Owner decision (about 14:14): keep Avalonia, no rewrite, targeted work, the Windows way; stop catering to macOS (a Mac version later is a separate native project). — Code: - (plan with 111 findings with file:line; also speed-plan-findings-2026-10-14.md, avalonia-speed-research-2026-10-14.md, t3-vs-nth-2026-10-14.md, same folder). — Notes: PICK (stated): no work before the owner approves the plan and picks 1-9. SPEED-PLAN umbrella: solves #1, #2, #25, #29, #35, #38, #77, #78. LOOK-M: compare with the Mac reference videos (E:/Droppy/work/reference-video/) at full frame rate. L98.
- [ ] #77 A window button stuck in "hovered" redraws the window forever — Idle, no input (02:47-02:58): render thread 37% of a core, GPU about 5.6%, nonstop. Trace: CaptionButton.OnFrame and CaptionButton.Render every frame; a minimise/restore/close button kept its hover state after the pointer left, so its hover pulse requested frames forever, each copying the whole 2.5K window. Hover is set only from WM_NCMOUSEMOVE / WM_NCMOUSELEAVE, so a missed leave (click on the button, sheet or window change under the pointer) leaves it running. — Fix: hover also ends on any client-area move, focus loss or window change; the pulse stops after a few cycles. — Code: CaptionButton.cs:132-196, CaptionButton.OnFrame, CaptionButton.Render; WindowManager.cs:495-543 (non-client mouse hook: WM_NCMOUSELEAVE at 504-507 clears hover only while PendingHitTest is 0; the button-release path at 516-535 sets hover again but does not call TrackMouseEvent - my reading of the code). — Notes: SPEED-PLAN. Smallest fix for the biggest idle burn (suggested: first). Manual check: moving the mouse over the three caption buttons clears the stuck state and the render thread falls from 37% to 7.7% of a core. See #78, #38. L96.
- [ ] #78 Dextra's ring animates forever — With the Dextra page open and idle, the "Armilla" ring is drawn every frame on the render thread (about 8% of a core, GPU 5-11%, after #77 was cleared), each frame copying the whole window. — Fix: the ring stops (or slows to a rare tick) when Dextra is idle, the window is unfocused or covered; idle means zero frames. — Code: ArmillaHandler.OnRender. — Notes: SPEED-PLAN. LOOK-M (ring must still look right when active). L97.
- [ ] #38 Busy while nothing happens — 01:18, main screen, no input, no chat running: 0.44 of a CPU core, 13% of the GPU 3D engine, 815 MB RAM, 92 threads (minimized: 0.05 core, 0% GPU). While visible it redraws at full rate, most likely the always-on composer light (Favilla sparks) and the glass. — Fix: an idle window near 0% CPU and about 1% GPU (slow the light down when idle, pause it after a while without input). — Code: - (light shaders: see #83). — Notes: SPEED-PLAN. #77, #78 are two measured causes. LOOK-M (the light must still feel alive in use). L57.
- [ ] #1 First launch takes 27-34 s to a usable window — Open nth clicked at ~21 s; splash "n" from 21 s, its dot only at ~34 s; window at ~48 s; sidebar half-drawn until ~55 s. Hang reports hang-2026-10-13T21-27-43Z.txt (4.5 s) and hang-2026-10-13T21-28-07Z.txt (4.1 s) in nth's Logs folder: the main thread waits in MediaContext.SyncWaitCompositorBatch while the render thread runs the start-up glass warm-up, so the boot-time shader warm-up blocks the UI thread. — Fix: as the row's Expected (none stated). — Code: GpuBootPreparation.Prepare -> GlassVariantPreparation.Draw -> GlassMaterial.Draw; MediaContext.SyncWaitCompositorBatch. — Notes: SPEED-PLAN. LOOK-M. Timings may be inflated (low RAM, Defender); lead's quiet-PC check: warm restart to a usable window about 3 s. L13, L106, L108.
- [ ] #2 App laggy right after launch — For the first minutes the app is very laggy; clicks respond late. — Fix: as the row's Expected (none stated). — Code: -. — Notes: SPEED-PLAN. LOOK-M. Same start-up cause as #1; part was memory pressure (quiet PC: Settings about 2 s, Plugins 0.5 s, MCP under 0.3 s, Skills 0.5-1 s, L108). L14.
- [ ] #29 Settings: not snappy — Pages that load data (Plugins, MCP) arrive late; moving between Settings pages feels abrupt instead of smooth and quick. — Fix: every page appears at once (layout first, data filled in after); the change of page is smooth. — Code: -. — Notes: SPEED-PLAN. LOOK-M. Family of #25, #35. Recording 20261013-2200-49.7576022.mp4 (47 s tour of every Settings page). L48.
- [ ] #25 Settings > Customization: composer styles picker takes 5-6 s — Each style's preview animation appears only after the wait. — Fix: the page opens at once; previews fill in without blocking. — Code: -. — Notes: SPEED-PLAN (item L1b in section 6.4, not L1). LOOK-M. Family of #29, #35. L44.
- [ ] #35 Appearance: animated wallpapers "Show all" lags — Settings > Appearance > Animated wallpapers > "Show all" opens the full list after a long lag. — Fix: the list opens at once; previews fill in after. — Code: -. — Notes: SPEED-PLAN (item L1b in section 6.4, not L1). LOOK-M. Family of #25, #29. L54.

#### 5.3.3 First-run defaults

- [ ] #55 Default size too big: today's 90% becomes the new 100% — The shipped default text/UI size is too big; zoomed out it looks perfect. Owner pick 02:04: one step smaller. — Fix: today's 90% is the new 100%: AppZoom.BaseScale 1.3 x 0.9 = 1.17; zoom steps 75-200 stay relative; a saved 100 keeps meaning the new default. Re-check every page at the new size for cut-off text and spacing. — Code: AppZoom.BaseScale. — Notes: Owner pick made. Supersedes #3. Ctrl+- and Ctrl+0 work and save at once, but the owner's earlier Ctrl+- presses were not saved at 02:00 (maybe lost in a crash, #36/#52). LOOK-V (every page). L74.
- [ ] #3 Default text size too big — The owner pressed "minus" until it looked right. — Fix: as the row's Expected (none stated); the owner's pick is in #55. — Code: see #55. — Notes: Do not work separately from #55. L15.
- [ ] #31 Default look is grey and dull — Out of the box the glass looks flat grey; a static wallpaper in Appearance makes it better. — Fix: ship with a wallpaper on by default, preferably animated, so the first look is rich. — Code: -. — Notes: PICK-likely (which wallpaper). LOOK-V, LOOK-M. Interacts with #32 and the speed plan's glass/blur work; an animated default must obey the idle rules of #38/#78 (my reading). L50.

#### 5.3.4 Glass, theme and look

- [ ] #47 On/off switches hard to read — The track stays the same dark grey on and off; only the knob moves and gets brighter, so the state is unclear at a glance. — Fix: an "on" switch clearly reads on (lit or accent-coloured track). — Code: - (lead zoom of the General and Ignis switches). — Notes: Possibly why Tactus looked dead (#8); real cause there is #66. In Light an off switch is a solid black pill that reads as on (#58). LOOK-V. L66.
- [ ] #58 Light theme: weak contrast everywhere — In Light: composer placeholder and icons are pale grey on pale glass; the Appearance buttons "Choose..." and "None" are white pills with pale grey text on a pale card; an off switch is a solid black pill that reads as on; the animated wallpaper behind the glass washes out to near white. — Fix: Light keeps the same legibility as Dark (text, icons, button labels at full contrast; off switches clearly off). — Code: -. — Notes: Together with #60, #84, #83; check every Light screen. LOOK-V. L77.
- [ ] #32 Wallpaper washed out behind the glass — With "Northlight" (Static wallpapers > Vibrant) the window shows only a dark blurred green-grey haze; the aurora and stars of the thumbnail are barely visible. — Fix: the wallpaper reads clearly through the glass (less blur/dimming), as in the thumbnail. — Code: -. — Notes: Do with the speed plan's glass/blur change, which alters the same look (my reading). See #31, #58. LOOK-V. L51.
- [ ] #59 Built-in wallpaper: "None" button reads as the current value — Settings > Appearance > Built-in wallpaper says "Corona . Vibrant . Animated" with one button "None"; it reads as "no wallpaper set" but is the action that turns it off. — Fix: a clear action label (e.g. "Turn off" / "Remove") or a picker showing the current wallpaper. — Code: -. — Notes: PICK-likely (label or picker). See #58. LOOK-V. L78.

- [ ] #87 Glass does not look alive: no visible lensing, no jelly scroll, the live wallpaper is buried — Owner 16:55 (angry; the cousin's LifeOS video, frames in E:/Droppy/work/scratch/lead/owner-testing/lifeos-glass-2026-10-14/): LifeOS shows a vivid moving 3D gradient wallpaper behind clear panels whose rims bend it with colour fringes, and content that bends at the rims while it scrolls. — Fix: nth already ports the same two lens engines with the same defaults (src/Marrow.UI/Port/GlassMaterial.cs:914-920: rizzytoday/liquid-glass displacement, scale -180, aberration 0/10/20; dashersw/liquid-glass-js rim ring; OD-0147). Un-bury it: no window-wide frost washing the wallpaper out (Chrome.cs:484-522 GlassLayer, backdrop opacity 0.5 at AppSettings.cs:347), a more vivid wallpaper, lighter tint, a jelly scroll (content refracted at the rims while scrolling, plus an elastic edge), LifeOS presets as starting points; all inside the speed budget (one shared low-res wallpaper frost, idle 0 fps). — Notes: natural owner is the cousin (section 1.8); coordinate with speed Phase 2 and #32, #58, #83, #84. LOOK-V, LOOK-M.

#### 5.3.5 Composer, lights and working line

- [ ] #74 Composer model chip label goes stale after an effort change — Effort popover Low -> High -> Low (saved effort "low" again): the chip read "Haiku 5.5" without "Low", leaving a gap, through closing the popover and switching threads; "Low" returned by itself after about a minute. — Fix: as the row's Expected (none stated); the row says the state saves but the view does not redraw, as in #66. — Code: -. — Notes: Fix with #66 (one redraw-after-save fix). L93.
- [ ] #73 Composer model picker: search not focused, Escape dead after search — "Select model" opens with the search field unfocused, so typing goes nowhere. After clicking search and typing "haiku", Escape does nothing (list and text stay); without touching search, Escape closes the list. — Fix: the list opens with search focused; Escape clears the search first, then closes the list. — Code: -. — Notes: Escape family of #57, #68. L92.
- [ ] #40 Composer hint cut off — The hint "Ask Claude for anything; big jobs go out to a team of he..." is cut mid-word at every window size; with a side pane open only "Ask Claude for anythi..." shows. — Fix: a hint that fits (shorter text, or one that shortens cleanly). — Code: -. — Notes: LOOK-V. L59.
- [ ] #83 Grey blocky specks around the text of the working line — Owner 15:15/15:19: around the letters of "Combobulating...", the "17s" timer and the white animated dot there are grey pixel-like blocks, a darker halo hugging every glyph (lead zoom 6-8x: square blocks a few pixels wide). Not the issue (owner OK): the stave bar and the light's embers. Likely cause (code read, not confirmed on screen): the "favilla" light's readability guard reads the text under its glow and darkens around it with coarse sampling steps; its band reaches about 378 px around the composer, so the working line is inside it. — Fix: text stays readable over the light with no visible blocks: a smooth, full-resolution guard or a soft text shadow. To confirm, switch the composer light off (or move the line out of the band) and check the halo goes. — Code: ShaderLightShaders.cs:183-262, ShaderLightShaders.V3.cs:77-116. — Notes: Together with #84, #58. LOOK-V. L102.
- [ ] #84 The working line's glass is too transparent (owner design ask) — Owner 15:19: the glass card behind "Combobulating... 17s" is too see-through over the composer and its light, so the line does not stand out. — Fix: more vivid glass, less transparency, so it reads as its own layer above the composer with easy text. Check the other floating cards over the composer for the same. — Code: -. — Notes: Together with #83, #58. LOOK-V. L103.
- [ ] #33 Outline around the composer — The composer looks excellent but a thin outline spoils it (owner 01:24: "looks really bad"). Lead zoom (maximized, Northlight, Favilla): a hard thin light rim round the whole pill with a dark shadow band just outside, so it reads as outlined, not floating glass. — Fix: no visible outline line; a soft shadow at most. Check the border with the default and wallpaper themes. — Code: -. — Notes: LOOK-V. L52.
- [ ] #60 Light theme: the composer light is invisible — In Light the composer light (white/Ignis glow) cannot be seen; lead zoom: only a faint warm tint on near-white glass. — Fix: clearly visible in Light as in Dark (tuned per theme, e.g. a deeper or coloured glow on light glass); every Light screen checked for readability. — Code: -. — Notes: Together with #58. LOOK-V. L79.
- [ ] #75 Composer popovers: small polish — (1) Accounts popover (C1 chip): "Create a pill" sits flush left while header, rows, footer are inset 8 px more (AccountPopover adds the button with no margin); footer wraps with a lone "2". (2) Ignis popover > Details > "Each message": "Carried by every message, before you typ..." is cut by the "not reported" value. (3) Ignis segmented pickers (Savings / History / Each message; Off / Keep warm / Warm + tidy up) pack left in a full-width track. (4) "Also ask, for this message only" menu: letter marks (C, C2, Z, A, O) instead of provider icons; Codex shows raw "GPT-6.1-Sol" where the model picker says "6.1 Sol". (5) Dextra first run: "Summaries of chats on the same provider" reads "Off: Dextra sees titles, ..." while its switch is on (as #69 (1)). — Fix: as the row's Expected (none stated); remove each of the five. — Code: AccountPopover. — Notes: LOOK-V. L94.
- [ ] #61 Composer light preview: wrong provider text, cut text — Settings > Customization > Composer light: after switching the preview to OpenAI (< > arrows) the preview composer still says "Ask Claude for anything..."; in the Typing preview the sample text is cut mid-word ("Add a keyboard shortcut for th") with no fade. — Fix: the preview names the chosen provider; long text fades out like the real composer. — Code: -. — Notes: See #40. LOOK-V. L80.
- [ ] #26 Customization: "Stellaris · Silver · 110 px" — The line under the composer-light preview (light, colour from Look > Colours, Spread from Look > Spread) reads like jargon. — Fix: owner ruling 01:07: keep "Stellaris · Silver", remove "· 110 px" (Spread is shown in Look > Spread). — Code: -. — Notes: Owner pick made. LOOK-V. L45.

#### 5.3.6 Ignis

- [ ] #22 Ignis: Opus 5.5 window is wrong for Claude Code — Ignis lists Opus 5.5 at 750K ("last quarter of its 1M window"); 1M is the API window (Anthropic pricing page, read 2026-10-10). nth runs Claude through Claude Code on the owner's subscription, where Opus 5.5 has a 400K window. IgnisTidyPoint.For uses the catalogue window even when the chat reports its own (IgnisController keeps usage.ContextWindow for the gauge only), so a Claude Code chat is planned to rebuild at 750K, after Claude Code compacted itself near 400K: Ignis never acts. — Fix: the point follows the window the chat really has (about 300K for 400K). Same check for the other 1M Claude rows. — Code: IgnisPointsData.cs, IgnisTidyPoint.For, IgnisController (usage.ContextWindow). — Notes: See #23, #24. L41.
- [ ] #23 Ignis: duplicate rows — "Sonnet 4.6" appears twice (150K for 200K, 750K for 1M) with the same name. — Fix: say which is which (e.g. "Sonnet 4.6 · 1M"). — Code: - (table data: IgnisPointsData.cs). — Notes: With #22. LOOK-V. L42.
- [ ] #24 Ignis: only Claude and GPT have model rows — Rows exist for Claude (12) and Codex/OpenAI (12) only. The other 13 providers (Antigravity incl. Gemini 4 Argon, Z.ai GLM, Meta Muse Wonder, DeepSeek, Grok, Copilot, Cursor, Command Code, OpenCode, OpenRouter, Pi, Devin, local) have a billing rule but no model rows, so their chats get no cheapest point. "Update now" changes nothing: the feed holds the same 2026.10.10 data. — Fix: every provider's models listed from their official pages (window, price steps, cache prices), or for flat-rate subscriptions the last quarter of the window the CLI reports. — Code: IgnisPointsData.cs. — Notes: Large data job. Also explains #45 for those providers. L43.
- [ ] #45 Ignis light in the sidebar looks missing — Owner 01:22: where is the Ignis light at the left of each chat, with its turn-off option? It is built (IgnisSidebarMark.cs, owner's pick B of 2026-10-10): a thin breathing ember line at the row's left edge, a glass "Turn off" / "Turn on" pill on hover at the row's right end, the same item in the right-click menu. By design it shows ONLY on a chat Ignis keeps warm. All the owner's chats are empty "New thread"s with no reply (#39), so no light shows and the composer reads "Cold"; chats on providers without model rows (#24) never light. — Fix: as the row's Expected (none stated): check once a real chat has had a reply. Possible change (owner's call): a dim cold line on every chat so the control can always be found. — Code: IgnisSidebarMark.cs. — Notes: PICK (stated: owner's call). Depends on #39, #24. LOOK-V. L64.
- [ ] #86 Ignis: what a reference usage panel has that Ignis lacks (owner ask) — Owner 16:11: a 34 s video of another Claude Code usage panel docked above the composer (E:/Droppy/work/scratch/lead/owner-testing/ignis-video-2026-10-14/ignis-reference.mov, contact sheets sheet1.png to sheet3.png in the same folder). The owner asked what Ignis lacks. Lead comparison against the 1.0.0 code (Kindle capsule + popover with the tabs Savings, History, Each message). Ignis ALREADY has (do not add again): the quick-memory countdown, keep warm, tidy up, Auto, chat size, reuse rate, money saved and lost, savings by source, the last 7 days, all-chats savings, the savings history by month and day, a saved report, keep-warm ping counts and cost, smart helpers on a cheaper model, advice cost, the week share with a pace forecast, the API-key cost, model points, the "why the cache is not reused" causes, and "carried by every message". MISSING in Ignis: (1) work-quality watch: the reference autopilot shows "Work quality: watching" and its last action; Ignis watches cost only. (2) An autopilot status list: the four things Auto watches, each with its state (cache memory 48m left, chat size 95% and when it will compact, work quality, helpers), plus the last thing Auto did. (3) A plan view: the 5-hour limit as well as the week, each with % used, reset time, "+6% since this chat opened", a pace line, and a plan-terms counterfactual ("No cache: about 97% of the 5-hour limit, not 6%"). (4) A full context breakdown ("Backpack"): one stacked bar of the window (951k of 1.0M) split into messages, MCP tools, system tools, skills, system prompt and MCP server text, plus the heaviest extras per server with token size and used/not used in THIS chat, and about 22k tokens freed by turning off the unused ones. (5) A live Agents view with an empty state and a Check action. (6) A slim always-visible strip: cache bar (48 min of 1h), Context %, Hits %, Saved $, Auto state, one-click Compact, Keep warm, Auto, help and expand (Ignis has only the small Kindle capsule, locked by OD-0134). (7) Small facts: next message about $0.19 warm against $7.61 cold (Ignis shows it only behind an (i)), and absolute sizes (951k / 1.0M, 188M read). — Fix: the owner picks which of items 1-7 to build; each is placed in Kindle's own glass language (no copy of that panel's look); items 2, 3, 4 and 6 change the locked Kindle design, so design variants first (canvas). — Code: KindlePopover.cs, KindleCapsule.cs, IgnisWords.cs:164-166 (paths in section 5.4). — Notes: PICK (stated in the row). LOOK-V. See #22, #23, #24, #45. L105.

#### 5.3.7 Dextra

If #76 (Dextra opens as a chat) is built, the setup-page rows #12, #71, #49 close with it; until then fix them as stated. The crash #36 is in 5.3.1.

- [ ] #76 Dextra should open as a chat, not a setup page (owner direction) — Owner 02:35: Dextra should open as a chat you talk to and do everything through that talk: give it a task, get answers and reports, ask for updates, start new tasks; typing and voice chat both work. Its side work (missions, helpers, finished tasks) can show in the sidebar or a floating panel on the right. Starting Dextra and handing it a first task must be easy; today's setup is "hectic". — Fix: Dextra knows what it needs and asks in the chat; the user just answers. Its helpers or sub-agents are made by Dextra itself, not set up by hand. — Code: -. — Notes: PICK-likely (sidebar or floating panel; new UI needs the owner's canvas approval). LOOK-V. See #12, #71, #72, #36, #49, #80. L95.
- [ ] #12 Dextra first run: cannot finish, options unclear — "Hi, I'm Dextra. Four choices ... no mission starts until you press Done": the page does not scroll, so Done (below "4 A first duty / Morning digest") cannot be reached. The four choices (what it may read; answer questions for me; Answers with; Morning digest) are hard to understand. — Fix: owner: Dextra should be an assistant ready to use; it should make missions itself when you talk to it. — Code: - (see #71). — Notes: Superseded by #76; same defect as #71. LOOK-V. L24.
- [ ] #71 Dextra first run cannot be finished: the page does not scroll — At 90% zoom on a 2558x1438 screen the setup card (Brain, What it may read, Answer questions, A first duty) runs past the window bottom: "Weekly chat tidy-up" is cut in half; Done and the Dextra composer are below the window; the wheel does nothing. Cause: AssistantHomeView puts the card in an Auto row of the conversation grid with no ScrollViewer, pushing the chips and the chat (the * row) out of view. — Fix: the card scrolls inside the column; Done and the composer are always in reach. — Code: AssistantHomeView (Auto row, no ScrollViewer). — Notes: Same as #12. LOOK-V. L90.
- [ ] #49 Assistant page: two different scope pickers — "Answer questions for me" has "This project only | Everywhere"; right below, "Answers with" has "Everywhere | This project only | This chat only" (other options, other order), each on its own line under its row; hard to tell which belongs to which. — Fix: as the row's Expected (none stated). — Code: -. — Notes: Family of #12. LOOK-V. L68.
- [ ] #11 Dextra header runs under the window's caption buttons — The min/max/close buttons are drawn over the Dextra bar, next to "Stop all" and "...". — Fix: as the row's Expected (none stated). — Code: -. — Notes: Duplicate of #72; one change fixes both. LOOK-V. L23.
- [ ] #72 Dextra header buttons sit under the window buttons — On the Dextra page "Stop all" and the "..." menu are drawn under the minimise, restore and close buttons. The thread header leaves room for the caption buttons; the Dextra header does not. — Fix: as the row's Expected (none stated): the Dextra header leaves room as the thread header does. — Code: -. — Notes: Duplicate of #11. LOOK-V. L91.
- [ ] #48 Dextra off: nothing shows it — Settings > Assistant: turning Dextra off leaves every Dextra option active and the sidebar still says "Dextra · Set up in a minute". — Fix: dependent options dimmed; the sidebar shows Dextra is off. — Code: -. — Notes: Same dimming as #69 (2). LOOK-V. L67.
- [ ] #50 Settings: "Assistant" should be "Dextra" — The Settings section is called "Assistant"; the assistant's name is Dextra. — Fix: the section (and its search words) read "Dextra". — Code: -. — Notes: With #48, #49. L69.

#### 5.3.8 Sidebar and navigation

- [ ] #39 Every chat is "New thread" — All 8 sidebar chats (18m to 5d old) and 6 command-palette rows are "New thread"; chats get no title and empty ones are kept. — Fix: a short title from the first message; empty drafts not piling up. — Code: -. — Notes: Also why #45 shows no Ignis light. L58.
- [ ] #44 Correction to #13: New thread exists but is hard to find — The global New thread is the pencil icon (2nd in the top-left toolbar, tooltip "New thread (Ctrl+N)") and Ctrl+N; the owner did not find it (unlabelled icon). — Fix: a clear New chat entry (e.g. at the top of the sidebar). — Code: -. — Notes: PICK-likely (exact place). LOOK-V. L63.
- [ ] #13 New chat hard to find — No visible "New chat" anywhere except inside a project; owner: must be there. (The toolbar compose icon exists; check what it does outside a project.) — Fix: as the row's Expected (a visible New chat); #44 gives the cause and the expected result. — Code: -. — Notes: With #44. LOOK-V. L25.
- [ ] #30 Bottom-left pills pile up — Sidebar-bottom pills stack awkwardly: "Downloading 2 of 3" over "Downloading 11 of 17" (first test); "1 update" over "1 didn't download" (second recording, also at the bottom of the Settings list). — Fix: one tidy status pill (or one combined row) that never stacks. — Code: -. — Notes: See #4, #81, #82. LOOK-V. L49.

#### 5.3.9 Window, panes and tray

- [ ] #9 Caption: Maximize opens Windows 11 Snap Layouts — Hovering Maximize opens the Snap Layouts panel. — Fix: owner wants it gone. — Code: -. — Notes: L21.
- [ ] #10 Caption tooltips — Minimize / Maximize / Close show hover tooltips. — Fix: owner wants none. — Code: -. — Notes: With #9. L22.
- [ ] #54 Title bar: double-click does nothing — Double-clicking the empty title bar neither maximizes nor restores nth; only the maximize button works. — Fix: as the row's Expected (none stated): as in every Windows app, double-click toggles maximize/restore. — Code: -. — Notes: L73.
- [ ] #37 Visus page drawn at the wrong size — Visus (Ctrl+Shift+B) in the 1190-px window with example.com: the page filled only the top-left about 3/4 of the pane, the rest white; corrected only after maximizing. The web view's first size is wrong (likely display-scale factor) until a resize. — Fix: as the row's Expected (none stated). — Code: -. — Notes: LOOK-V. L56.
- [ ] #67 Terminal pane swallows app shortcuts — Ctrl+J opens the terminal pane and focuses it; Ctrl+J again does nothing (the terminal takes it), so the pane cannot be closed by its own shortcut. Ctrl+D (Toggle changes) went to PowerShell as ^D. — Fix: nth's pane shortcuts (Ctrl+J, Ctrl+D, Ctrl+;, Ctrl+Shift+G, Ctrl+\) work while the terminal has focus, like VS Code. — Code: -. — Notes: L86.
- [ ] #46 Settings covers half the composer — Maximized: the Settings sheet's bottom edge runs across the composer, which peeks out half-cut below it. — Fix: Settings fully covers or fully clears the composer. — Code: -. — Notes: LOOK-V. L65.
- [ ] #53 Usage panel: card far too big for its data — The floating usage panel (Settings > General > Show a usage panel) is a big card with little data small at the bottom: "Usage" pill top-left, numbers (Claude 1, 5-hour window 0%, This week 0%, resets, Next if it runs out, All accounts) pushed into the right two-thirds, empty left column and space above. Turning it on shifts the whole chat right, and its last line peeks out under the Settings sheet. — Fix: a compact card sized to its content, content aligned with its header, no dead space. — Code: -. — Notes: LOOK-V. L72.
- [ ] #34 No way to open Visus, Tabula and the other panes — The toolbar's right side has two pills (Open in another app + Run project script; Terminal + Changes). Visus (in-app browser), Tabula (artifacts), Side chat and Background tasks open only by keyboard (Ctrl+Shift+B, Ctrl+Shift+A, Ctrl+;, Ctrl+Shift+G) or by themselves (a chat link, a project preview, the agent); the command palette has no pane entries. — Fix: like Claude desktop's panel menu next to the window buttons, add a third (and fourth) pill in the same glass style for these panes (Visus, Tabula and the rest, by their nth names), so you can open the browser from the top. — Code: -. — Notes: New UI: design in the glass language, canvas-approved. LOOK-V. L53.
- [ ] #85 Closing the window should keep nth running in the tray — Owner 16:00: like other tray apps, closing nth's window must not quit it; the window hides, nth keeps running with its notification-area icon, and exits fully only when the owner right-clicks the icon and picks Exit. Today (lead code read, not changed): the switch "Keep working when the window is closed" exists but is off by default and parks the window instead of quitting (BackgroundSwitches.cs:14, WindowManager.cs:394-412, AS-10); otherwise close quits (OD-0008 C1, PR-LAUNCH-074). The switch is offered only on the Assistant's first-duty card and duty tray menu (FirstDutyCard.cs:35, DutyTray.cs:64), not in Settings > General. The tray icon exists and shows while the window is hidden (AppTray.cs, AvaloniaTrayHost.cs), but its menu has only "Open nth" and the features' items (AppTray.cs:170): no Exit. — Fix: close hides to the tray by default (needs an OD that replaces OD-0008 C1); left or double click on the icon restores the window; the switch moves to Settings > General so it can be turned off; the first close shows a one-time note that nth is still in the tray. Menu (owner 16:00-16:02, shaped like another app's short dark menu): (a) "Show App" (Open nth) first; (b) greyed lines with the active account's plan and usage (like "Max plan", "Usage: 96%"), reusing data nth already reads for Ignis and account routing (AppModelIgnis.cs, AppModelPillRouting.cs), never a new provider call just for the menu; (c) an item named exactly "Reload" that fully shuts nth down and opens it again fresh so anything stuck is cleared, asking the same running-turn question as Exit (AppUpdater.cs:521 WaitExitThenApplyAndRestart is the existing wait-for-exit-then-relaunch step; Reload needs the same without an update); (d) "Exit" last ("Exit nth" or "Quit nth", one word across the app), through today's shell quit path (unsaved-work and running-turn questions included); the features' items in the middle, separators between groups. — Code: BackgroundSwitches.cs:14, WindowManager.cs:394-412, FirstDutyCard.cs:35, DutyTray.cs:64, AppTray.cs:170, AvaloniaTrayHost.cs, AppModelIgnis.cs, AppModelPillRouting.cs, AppUpdater.cs:521, AppShell.ShutdownRequested. — Notes: PICK (stated: an OD replacing OD-0008 C1; the owner already named "Reload" and "Exit"). Check a hidden nth draws nothing (speed plan: idle 0 fps) and sign-out/shutdown still quits cleanly. LOOK-V (tray menu). L104.

#### 5.3.10 Popovers, updates and menus

Rows #81, #82, #41 are one rewrite of the updates popover; #64 is the Settings > Updates page that shares the same rows.

- [ ] #81 Updates popover does not come from its button — The "2 updates" pill sits bottom-left of the sidebar but its popover does not rise from it: it fills almost the whole window height, covers the pill, spills wide, its pointer sits near Settings pointing at nothing, and the last rows ("qpdf" and below) are cut off. Cause (read): the pill opens it with PlacementMode.TopEdgeAlignedLeft (UpdatePill.cs:128 via PlainControls.Popover) but UpdatePopoverContent.cs has no height limit and no scrolling (CardWidth 440, about 30 rows), so the popup is pushed down over the pill. — Fix: the popover grows up out of the pill, bottom edge just above it, pointer on the pill, never covering it; capped to the space above the pill (minus a margin); its list scrolls inside while the header and "Update everything" stay put; open and close motion starts from the pill. — Code: UpdatePill.cs:128, PlainControls.Popover, UpdatePopoverContent.cs (CardWidth 440). — Notes: With #82, #41. LOOK-V, LOOK-M. L100.
- [ ] #82 Updates popover needs a full clean-up (owner direction) — The sidebar says "2 updates" but the popover opens about 30 things, with raw 40-character commit hashes and lock ids that mean nothing. It is one flat list of every item, up to date or not, although Settings > Updates already groups the same rows into "nth", "Provider tools", "Developer tools", "Skills and tools". — Fix: show only what has an update, grouped by section (the app, provider tools, developer tools, plugins and skills...), each with plain name and new version; no hashes or ids anywhere; small for 1-2 updates, may grow when many arrive but stays organised. The full list belongs on the Settings > Updates page, reached from a link in the popover. — Code: UpdatesProjector.cs:187 (the page's grouping). — Notes: Owner direction complete, no pick pending. With #41, #81. LOOK-V. L101.
- [ ] #41 Updates popover unreadable — Names truncate to "I...", "A...", "..." because full 40-character commit hashes take the width; raw hashes shown as versions; Node.js and Python appear twice (the PC's own and nth's) with no label; "Checks when updated" is unclear. — Fix: readable names, short versions (7 characters or a date), labelled duplicates. — Code: -. — Notes: Inside the #82/#81 rewrite. LOOK-V. L60.
- [ ] #64 Updates page: unclear rows and cut columns — Settings > Updates: (1) Developer tools Node.js, Python 3.13, Python 3.14 and Office show Latest "-" and "Checks when updated" yet each has an Update button; Office is listed as a developer tool. (2) Git says latest 2.55.0.5 (from 2.54.0) while nth's own tools list below shows Git for Windows 2.56.0.2 up to date: two "latest" Git versions. (3) The Installed column is too narrow: "lock-3fbbd" and "153.0.8010." are cut and run into Latest. (4) Skills rows show raw commit hashes; Node.js / Python appear twice (as #41). — Fix: Update only where an update is known; one consistent version source; columns sized to their values. — Code: - (page grouping: UpdatesProjector.cs). — Notes: See #41, #82, #15. LOOK-V. L83.
- [ ] #15 Updates: Git did not update while Git Bash runs — "Git didn't update: Git Bash is running (used by Claude Code). Close it and try again." Correct, but worth a note. — Fix: as the row's Expected (none stated); the row asks only for a note. — Code: -. — Notes: See #64 (two Git versions). L27.
- [ ] #68 Popovers: Escape closes everything, effort popover far from its row — Settings > Models: clicking an added model opens the effort popover in the window's top-right corner, outside the Settings sheet, far from the row. Escape there closed the whole Settings sheet (Escape also failed in the Hydra pair editor, #57). — Fix: popovers open next to the row they belong to; Escape closes only the topmost popover or menu. — Code: -. — Notes: Escape family of #57, #73. L87.
- [ ] #56 Dropdown menus not sized or aligned to content — Hydra pair editor: the card is fine but the menu from a picker (Lead provider: Codex, Claude, Cursor, Z.ai, Antigravity, OpenRouter) has empty space left and right and items not lined up; the Effort menu (Chat's effort .. Max) is the same; OpenRouter is the only provider row with no icon, so its text sits out of line. — Fix: every picker menu sized to its longest item with even padding, icons in one column (or none), aligned to its button. — Code: -. — Notes: Same fix as #51; icon gap also in #63. LOOK-V. L75.
- [ ] #51 Source control: dropdown badly sized — The "Automatic" dropdown in Settings > Source control opens a menu much wider than its items (Automatic, Claude, Codex, Off), with a large empty area to the right, sitting off to the left of its button. — Fix: menu sized to its content, aligned to its button, like other dropdowns (e.g. Ignis "Cool idle chats after"). — Code: -. — Notes: Same fix as #56. LOOK-V. L70.
- [ ] #43 Thread right-click menu looks basic — The sidebar thread menu (Rename, Pin, Forward to new thread, Settle, Archive, Delete...) is a small plain default menu with tiny text, not in nth's glass style. — Fix: as the row's Expected (none stated): nth's glass style like the other popovers. — Code: -. — Notes: See #69 (5). LOOK-V. L62.

#### 5.3.11 Settings pages

- [ ] #66 Tactus switches never redraw (likely the real cause of #8) — On Settings > Tactus, clicking the Tactus switch DOES save (pcControl.enabled true -> false -> true in UserDefaults.json) but the switch keeps showing the old state until you leave the page and return. Same for the Safety switches (Sound when Tactus needs a moment). — Fix: every Tactus switch flips on screen at once, like the rest of Settings; add a test that clicks each Tactus switch and checks the drawn state. — Code: PcControlSettingsPage builds each switch as new SettingsSwitch(new Binding<bool>(read, write)) over a plain field; nothing tells the switch to re-read after Change(), unlike other pages. — Notes: Closes #8. Same class as #74. See #47. LOOK-V. L85.
- [ ] #8 Tactus switch cannot be turned on — Click and drag on the knob do nothing. — Fix: as the row's Expected (none stated); cause found in #66 (saves, not redrawn). — Code: see #66. — Notes: Fixed by #66; recheck with #47 in mind. L20.
- [ ] #57 Hydra pair editor: closes on every pick, Escape does nothing — Settings > Hydra > Pairs: picking any value (Lead provider, Lead effort) closes the whole editor, so changing several fields means reopening it. Escape closes neither an open menu nor the editor; clicking the already-chosen item does not close its menu. "Add pair" adds "Any Codex model + itself" at once without asking which provider. The edit button shows only on hover. — Fix: the editor stays open until dismissed; Escape closes the menu, then the editor. — Code: -. — Notes: Escape family of #68, #73; menus as #56. L76.
- [ ] #65 MCP "New server" form: last row hidden under the button — Settings > MCP > Add server: the Environment section is cut off; "Add environment variable" shows only its top half and the "Add server" button sits on top of it. The Kind picker is centred while other fields are left-aligned; the form opens mid-list instead of at the Add server row. — Fix: the form grows (or scrolls) to show every row, buttons below the last field, fields aligned. — Code: -. — Notes: LOOK-V. L84.
- [ ] #69 Small polish found in the toggle test — (1) Tabula > Live answers in chat: subtitle reads "Off: models are not offered live views in chat." while the switch is on. (2) Visus: with "Let models use Visus" off, "Ask before a model uses a new site" stays active (dim it, like #48). (3) Skills > Add menu items are separate pill buttons, unlike every other menu. (4) Models > Z.ai: names mix proper names and raw ids ("GLM 5.3" next to "glm 4.5", "glm 5 turbo"). (5) Ctrl+W archives the thread and Ctrl+S settles it; on Windows those keys mean close and save, so a habit press archives a chat with no confirm. — Fix: as the row's Expected (none stated); remove (1)-(4); for (5) the row states no fix. — Code: -. — Notes: PICK-likely for (5). LOOK-V. L88.
- [ ] #70 Archive empty state has no words; licenses popover hangs off the sheet — Settings > Archive with nothing archived shows only an icon: no "No archived threads" text, no hint. Settings > About > Licenses > Show opens the licence text as a popover that sticks out past the sheet's right edge and covers Check now. — Fix: an empty state with a title and one line of help; licences in a sheet or a popover kept inside the window. — Code: -. — Notes: LOOK-V. L89.
- [ ] #42 Skills: duplicates under "Already available" — Settings > Skills > "Already available through your providers" repeats skills: built-in-browser x4, chrome-browser x4, computer-use x4, cosmic-drift-final-auditor-v7 x3, -v8 x3. — Fix: one row per skill (say where it comes from if it matters). — Code: -. — Notes: L61.
- [ ] #16 MCP import lists servers once per source app — The same servers appear once per source app (Claude Code and Claude Desktop). Owner: fine, since "Already in nth" + Skip / Replace / Keep both prevents duplicates. — Fix: none; resolved, no change asked. — Code: -. — Notes: Nothing to build; tick with "no change asked". L28.

#### 5.3.12 Providers and accounts

- [ ] #21 Add account: "connected" but not signed in — Read-only check 00:55: Claude 2's private folder (%LOCALAPPDATA%\nth\accounts\claude\26fb416d..., made 00:34-00:39) has a .claude.json with NO signed-in account (no email) and no saved credentials, yet Settings lists Claude 2 with its switch on: the add-account flow accepted a sign-in that saved nothing. Also: the default Claude CLI record (~/.claude.json, used by Claude 1) names <owner e-mail, Claude 1 record> while nth's header says "Signed in as <owner e-mail, header>" (the real addresses are in row #21 of the list file) (header stale or read from elsewhere). An empty leftover folder d207895d... (10-11) sits there. Nothing was changed. — Fix: as the row's Expected (none stated): a sign-in that saved nothing must not create an account; the header shows the real signed-in email. — Code: - (account stores: see #52). — Notes: Do first in this area; possible link to #52. Do not open credential files while debugging. L40.
- [ ] #20 Add account: failed sign-in leaves "Signing in..." forever — The second Claude account's browser sign-in FAILED, but nth's card kept showing "Signing in..." with no error and no way out; the owner had to click "Add another account" again to retry. — Fix: nth notices a failed or abandoned sign-in, says so plainly ("Sign-in didn't finish"), offers Try again / Cancel; "Signing in..." never stays forever. — Code: -. — Notes: Same flow as #14, #21. LOOK-V. L39.
- [ ] #14 Providers: Claude "Add another account" shows a plain dialog — It shows a plain grey "Please wait..." system dialog, then a browser sign-in; the card keeps "Signing in..." under it. — Fix: as the row's Expected (none stated). — Code: -. — Notes: With #20. L26.
- [ ] #17 Providers: Claude accounts show no email, plan or usage per row — After adding a second account the rows read only "Claude 1 / Account" and "Claude 2 / Account"; no email. Usage shows only on Claude 1 ("Week 0% · resets Tue 7:00 PM", with a stray thin bar above it). — Fix: owner ask 00:50: with more than one account the header says "Signed in with 2 accounts"; each row shows its email (under "Claude 1" or beside it) and its plan (Pro, Max...). Lead check: nth already reads both for the first account ("Signed in as <email>" from the CLI sign-in status; "Plan usage limits · Pro" from the usage read's subscription type); the same two reads per account's own folder give each row its email and plan automatically. — Code: -. — Notes: Needs #21 first. LOOK-V. List lines 33-37. L29.
- [ ] #18 Account "..." menu: clipped label — One item reads "Handshake at..." with "like all" on the right: clipped or overlapping. — Fix: as the row's Expected (none stated). — Code: -. — Notes: LOOK-V. L30.
- [ ] #19 Stray tooltip — The "Hide the provider settings" tooltip floats mid-page over the Z.ai card, far from any control. — Fix: as the row's Expected (none stated). — Code: -. — Notes: LOOK-V. L31.
- [ ] #63 Small affordance and icon gaps — "Local" is the only provider with no icon; OpenRouter has none in the Hydra menus (#56). Providers > "When an account runs out": the value "Ask me the first time" is a menu (Keep me going / Ask me each time / Just wait) but looks like plain grey text with no chevron, and its menu opens over the text below. — Fix: every provider has an icon; menu values look clickable (chevron, button look) like other pickers. — Code: -. — Notes: See #56. LOOK-V. L82.

#### 5.3.13 Downloads and packs

- [ ] #5 Download failures — "6 didn't download": Playwright Chromium; Python: Office documents, PDF, images and data, browser testing, GIF making (Retry 6). — Fix: as the row's Expected (none stated). — Code: -. — Notes: Reasons are not in the row; check logs on a quiet PC. See #4, #28. L17.
- [ ] #28 Skills: 2 packs never install (checksum) — "Anthropic document skills" (A2) and "OpenAI open document skills" (O3) show "Didn't download · The file didn't match its checksum." even after the terms were accepted. Lead read-only check: all 183 A2 files fetched from raw.githubusercontent.com at pin 8a1541c match their catalogue size and SHA-256, so the download is fine; the failure is nth's own check after install (SkillsPipeline: inventoryVerifier.Verify on the store folder), which reports any mismatch with this same message. No case clashes or long paths. A2 shares repo and pin with A1 (installed fine as A1@8a1541c4). O3 is the only pack whose catalogue entry has no "pin" (null; its licence URL points at 45d05d75). Root cause not found. The other 10 packs are in skills/store. — Fix: as the row's Expected (none stated): both packs install and verify. — Code: SkillsPipeline (inventoryVerifier.Verify on the store folder). — Notes: Root cause open. L47.
- [ ] #4 Downloads speed — "Download all tools" (17 items, 1.22 GB, LibreOffice 379 MB) runs one item at a time: 429 MB / 4 of 17 after about 2.5 min (about 23 Mbps on a line of about 400 Mbps). Plugin packs of a few MB are also slow. — Fix: as the row's Expected (none stated): the symptom is serial, slow downloads. — Code: -. — Notes: See #30, #5. L16.
- [ ] #6 Plugins terms — "Download all plugins" skips Anthropic Claude Code plugins and OpenAI proprietary plugins until "I have read these terms" is ticked on each pack; the tick lagged. The owner expected Download all to just work (or ask once, up front). — Fix: Download all just works, or asks once up front; the design is #27. — Code: -. — Notes: With #27. L18.
- [ ] #27 Terms: ask once, up front (owner design ask for #6) — Applies to skills, plugins and MCP packs that need terms. When "Download all" / "Install all", or a single Download, hits packs that need approval, nth opens a popover right away instead of silently skipping them. — Fix: one card per pack that needs terms (e.g. Anthropic's, OpenAI's): Accept, then Next, Next, until the last. After that every download runs together and nth never asks for those terms again (it remembers). No scattered "I have read these terms" ticks. — Code: -. — Notes: Owner design stated in full, no pick pending. LOOK-V. L46.

#### 5.3.14 Voice

- [ ] #62 Voice typing: "Customization >" link lands at the top — Settings > Voice typing > "Orb and after voice input" > Customization opens the Customization page at its top (Composer light), not at the Voice orb section it names. — Fix: it scrolls to Voice orb. — Code: -. — Notes: L81.
- [ ] #80 Kokoro voice unit ready for the next release — The Codex Kokoro unit finished: commit 714bfbc3a on branch codex/kokoro ("Add optional CPU Kokoro voices for Dextra"); its quiet Native 1 run passed. Not merged into the 1.0.0 installer (final). — Fix: merge, review and gate it together with this list in the next release. — Code: commit 714bfbc3a on codex/kokoro. — Notes: Merge work, not a defect. See #76 (voice chat). Voice rules: Dextra's voice is free and runs on the PC only, no cloud voice (owner 2026-10-11, memory release-scope-2026-10-11). NVIDIA PersonaPlex-7B was discussed on 2026-10-12 14:06: the lead advised against it as Dextra's voice (own 7B brain, no tools, likely English-only, 16-24 GB VRAM) and proposed a GPU-tier local voice (multilingual speech recognition plus better speech output for Hindi/Urdu), PersonaPlex only as an optional English live chat later; the owner's scope pick is not on record (log:129). Kokoro covers English; Hindi/Urdu quality is unverified. L99.

### 5.4 Where the files named in the rows live

Root: E:/Droppy/wt/cl/pkg-morning/src (shipped tree; found by a read-only file search; E:/Droppy/port may differ). Code map: section 9.

- Marrow.App/Runtime/ThreadRuntime.cs:1711 (StartTurn); Marrow.App/Port/InstallerTurnLease.cs; Marrow.Core/Port/SwiftErrors.cs:125 (the throw).
- Marrow.App/Assistant/Views/AssistantHomeView.cs (`firstRun.Child = card` is at :503-504 here, :502-503 in the row). DetailView in the crash stack is likely Marrow.App/Views/RootView.cs:687 (another one: Marrow.App/Port/Skills/Pages/DetailView.cs; unverified which).
- Marrow.UI/Chrome/CaptionButton.cs; Marrow.UI/Port/Armilla/ArmillaMark.cs:188 (ArmillaHandler); Marrow.App/Views/Composer/GpuBootPreparation.cs and GlassVariantPreparation.cs; Marrow.UI/Port/GlassMaterial.cs; Marrow.UI/Common/AppZoom.cs.
- Marrow.Services/Port/Ignis/IgnisPointsData.cs; Marrow.Core/Port/Ignis/Engine/IgnisTidyPoint.cs; Marrow.App/Port/Ignis/Runtime/IgnisController.cs and AppModelIgnis.cs; Marrow.App/Port/Ignis/Views/IgnisSidebarMark.cs.
- Row #86: Marrow.App/Port/Ignis/Views/KindleCapsule.cs and KindlePopover.cs; Marrow.Core/Port/Ignis/IgnisWords.cs:164-166 (the tab names Savings, History, Each message).
- Marrow.UI/Port/ComposerLights/ShaderLightShaders.cs and ShaderLightShaders.V3.cs; Marrow.App/Port/MultiAccount/Popover/AccountPopover.cs; Marrow.App/Port/MultiAccount/AppModelPillRouting.cs.
- Marrow.App/Port/PcControl/PcControlSettingsPage.cs; SettingsSwitch at Marrow.App/Views/Settings/SettingsView.cs:502; Marrow.App/Port/Settings/UpdatesProjector.cs; Marrow.App/Port/Updates/UpdatePill.cs and UpdatePopoverContent.cs; Marrow.UI/Port/PlainControls.cs; Marrow.Services/Port/Skills/Pipelines/SkillsPipeline.cs.
- Marrow.App/Assistant/BackgroundSwitches.cs, FirstDutyCard.cs, DutyTray.cs; Marrow.App/Window/WindowManager.cs; Marrow.App/Shell/AppTray.cs and AvaloniaTrayHost.cs; Marrow.App/AppUpdater.cs; Marrow.App/Port/AppShell.cs.
- Marrow.Core/Port/Accounts/AccountStore.cs and AccountKeyStore.cs; Marrow.Core/Port/MultiAccount/AccountDecisionStore.cs; Marrow.Services/Port/Skills/Providers/SkillSessionFactory.cs.

### 5.5 Summary

#### 5.5.1 Rows per area

| Area | Rows | Count |
|---|---|---|
| 5.3.1 Crashes and hangs | #52, #36, #7 | 3 |
| 5.3.2 Speed and smoothness | #79, #77, #78, #38, #1, #2, #29, #25, #35 | 9 |
| 5.3.3 First-run defaults | #55, #3, #31 | 3 |
| 5.3.4 Glass, theme and look | #47, #58, #32, #59, #87 | 5 |
| 5.3.5 Composer, lights and working line | #74, #73, #40, #83, #84, #33, #60, #75, #61, #26 | 10 |
| 5.3.6 Ignis | #22, #23, #24, #45, #86 | 5 |
| 5.3.7 Dextra | #76, #12, #71, #49, #11, #72, #48, #50 | 8 |
| 5.3.8 Sidebar and navigation | #39, #44, #13, #30 | 4 |
| 5.3.9 Window, panes and tray | #9, #10, #54, #37, #67, #46, #53, #34, #85 | 9 |
| 5.3.10 Popovers, updates and menus | #81, #82, #41, #64, #15, #68, #56, #51, #43 | 9 |
| 5.3.11 Settings pages | #66, #8, #57, #65, #69, #70, #42, #16 | 8 |
| 5.3.12 Providers and accounts | #21, #20, #14, #17, #18, #19, #63 | 7 |
| 5.3.13 Downloads and packs | #5, #28, #4, #6, #27 | 5 |
| 5.3.14 Voice | #62, #80 | 2 |
| Total | | 87 |

#### 5.5.2 Rows the speed plan already solves

Per row #79 of the list: #1, #2, #25, #29, #35, #38, #77, #78 (8 rows; checked, the row names exactly these). #79 itself is the plan (an umbrella row that waits for owner approval and picks 1-9). Related but not named as solved by the plan: #85 (a hidden nth must draw zero frames), #31 (an animated default wallpaper must follow the idle rules), #7 (hang cause unknown).

#### 5.5.3 Rows that need an owner design pick or decision before work

- Stated in the row: #79 (approve the speed plan and picks 1-9), #85 (an OD that replaces OD-0008 C1; "Exit" or "Quit" as one word across the app), #45 (owner's call: a dim cold line on every chat), #86 (the owner picks which of items 1-7 to build; items 2, 3, 4 and 6 need canvas variants first).
- Likely, my reading (the row does not say it): #31 (which default wallpaper), #44 (where New chat goes), #59 (label or picker), #76 (side work in the sidebar or a floating panel; new Dextra UI), #69 item 5 (Ctrl+W / Ctrl+S on Windows).
- Already decided by the owner, no pick pending: #55 (90% becomes the new 100%), #26 (remove "· 110 px"), #27 (terms popover flow), #82 (updates popover content), #85 menu items and the name "Reload", #16 (resolved).

#### 5.5.4 Rows where the owner should look at the result

- Motion (LOOK-M): #1, #2, #25, #29, #31, #35, #38, #78, #79, #81.
- Visual (LOOK-V): #11, #12, #13, #17, #18, #19, #20, #23, #26, #27, #30, #31, #32, #33, #34, #37, #40, #41, #43, #44, #45, #46, #47, #48, #49, #51, #53, #55, #56, #58, #59, #60, #61, #63, #64, #65, #66, #69, #70, #71, #72, #75, #76, #81, #82, #83, #84, #85, #86.
- The owner checks UI and motion on the final installer; do not block a commit on a screen check. Functional rows (for example #52, #36, #73, #67, #28) need tests, not the owner's eyes.

## 6. Speed plan checklist

From draft s4. Section numbers in this part were renumbered when the file was assembled (the draft's 4.x is now 6.4.x, its 5.x is 6.5.x). Inside 6.4 the seven phases are headed "Phase 0" to "Phase 6" without section numbers; 6.4.6 gives the speed gate (Phase 0) in detail and 6.4.7 the "Later" items. The picks 1 to 9 are the owner's; nothing starts before the owner answers.

Status: PLAN ONLY (2026-10-14). Nothing is started. It waits for the owner's approval and picks 1-9 (section 6.5). Sources in `E:/Droppy/work/scratch/lead/`: `speed-plan-2026-10-14.md` (the plan), `speed-plan-findings-2026-10-14.md` (111 findings R1.1-R8.13 with file:line; a critic re-read 12 key claims: 10 true, 2 partly, 0 false), `avalonia-speed-research-2026-10-14.md`, `owner-test-2026-10-14.md` row #79.

Paths: repo root `E:/Droppy/wt/cl/pkg-morning` (1.0.0, commit 87eb11490), SRC = root/src. File names are nearly unique in SRC (tests excluded; exceptions: Program.cs in App, Setup and Shim; NthMotion.cs in UI/Port and UI/Port/Nth): use Glob `**/Name.cs`. `GM` = GlassMaterial.cs. Lines come from the research; about a dozen were re-checked. Merges may move them: grep before editing.

### 6.1 Decision and goal

1. Owner decision (about 14:14, row #79): keep Avalonia (pinned 12.1.2). No rewrite, no restart. Small, targeted work.
2. Fix the Windows way. Stop catering to macOS; a Mac version later is a separate native project.
3. No quality drop: glass, animated wallpapers, composer lights, motion stay.
4. Goal: no hiccups; as smooth and light as a good Windows app on a strong PC.
5. The owner's display runs at 239 Hz (`E:/Droppy/work/lean-pass/native/dryrun.log:10`): an unpaced animation can draw about 240 frames/s, each costing a whole-window copy plus a Windows re-compose.

"One engine, pieces in harmony" (the owner liked it; from the lead's brief, not in the plan file): one shared motion clock, one redraw per beat, capped, stops when nothing moves (P3-P5, W6); one blur, the wallpaper makes one low-res frosted copy that every glass piece reads (W1, W2); redraw only what changed (W3, W7); background work (provider text) batched to the screen once per beat (I4).

### 6.2 The 8 root causes (most felt first)

1. **239 Hz multiplies a fixed price per frame.** Avalonia blits the whole window layer every frame (2576x1408, about 14.5 MB; no option removes it, R1.1) and Windows re-composes the see-through window. Light and orb skip the 60 Hz cap for 0.9 s / 0.7 s after each key (ComposerLightView.cs:114; AmbientMotionController.cs:69-76); every frame request forces a compositor commit (FrameClock.cs:257-268).
2. **Animations that never stop.** Stuck caption hover #77 (WindowManager.cs:495-543). Dextra ring #78 (ArmillaPoses.cs:218; ArmillaEngine.cs:473). About 10 more loops; plain loops are never held back when minimised, covered or idle.
3. **Whole background redrawn per wallpaper frame.** Wallpaper shader at full size, 3.6 Mpx (WallpaperRenderer.cs:225-243). A window-sized glass layer re-copies, blurs and shades the whole window, on by default (Chrome.cs:519-524; GM:1276-1326). One bounding dirty rect (GM:1356-1359). Wakes 20 s after any input (AmbientMotionClock.cs:6).
4. **Windows settings never tuned.** Skia GPU cache left at about 29 MB, what the window glass layer alone needs (MarrowApplication.cs:25-39). Premultiplied Acrylic window (WindowChrome.cs:165-170). No GPU choice (GpuProgramCache.cs:28-32). Avalonia held at 12.1.2 (Directory.Packages.props:4-13).
5. **Typing, hover, streaming, layout do too much.** Composer lays out the draft about 3 times per key (ComposerTextView.cs:664-825). Sidebar hover builds and destroys controls (SidebarView.cs:3644-3739). Streaming fade reshapes paragraphs per tick (StreamVeil.cs:239-262). Provider messages run on the UI thread, no budget (ClaudeSession.cs:518-601). Sidebar slide animates column width (RootView.cs:157-185). About 60 LayoutUpdated listeners.
6. **Lists build everything; Settings never released.** No virtualised list (one ListBox only). Closing Settings only hides it (SettingsOverlay.cs:233-254,403). Memory 652 -> about 1,050 MB, not returned; cause NOT proven (L4). Galleries and pickers: every wallpaper picture compiles its own shader (WallpaperRenderer.cs:77; WallpaperCatalogue.cs:91-119), the wallpaper picker animates up to 12 thumbnails at 60 Hz (WallpaperThumbnailTicker.cs:34-41; WallpaperThumbnail.cs:190-215), the two wallpaper galleries hold 162 live thumbnails, and composer styles start dozens of live renders (finding R3.11; t3-vs-nth-2026-10-14.md, "What nth does differently" item 3). The plan appendix has no ID for this: see L1b in 6.4.
7. **Start-up.** First launch: glass shader link on the render thread freezes the window about 4 s (GM:763,789-1004; GlassVariantPreparation.cs:10-24). UI-thread file reads and probes before the first window (Storage.cs:489-521; VoiceComposition.cs:76-104).
8. **Nobody measured what the owner feels.** The idle probe ran on the boot screen, unfocused, not maximised (DesktopPerformanceProbe.cs:67-84), and ambient effects pause unless Focused and Foreground (WindowVisibilityFacts.cs:6-7). The tour sends TextInput only, never waking the ambient clock (InteractionRecorder.cs:308-321). Native runs N1-N10 never produced a result.

### 6.3 The Windows-way rules

1. Idle means zero frames.
2. Rate by kind: direct touch (scroll, drag, press) at the screen's full rate; decoration (wallpaper, lights, orb, rings) 30-60 Hz and stops when idle; progress (spinners) only while work runs, at most 60 Hz. Decoration never runs at 240 Hz.
3. Move, don't re-layout: animate transforms and opacity, not widths and heights.
4. Draw expensive things small and once: wallpaper and blur at lower resolution; reuse the last blur when nothing behind the glass changed.
5. Lists only build what is on screen.
6. The window's thread only does window work: provider messages in small slices; files and checks in the background.
7. Use Windows' own features where they look the same: opaque window when the glass is solid; the right GPU; a sized GPU cache; the system backdrop.
8. Measure every change on the owner's PC, before and after (6.4.6). Look: pixel tests or similarity score; motion: full-frame-rate capture; the owner's eye on the final installer.

### 6.4 Phases and checklist

Format: `- [ ] ID (size) what — Code — Done when — Owner pick`. Sizes: S hours, M a day or two, L several days; the size tag after each ID is the plan appendix's (speed-plan-2026-10-14.md:344-386). Split any M-L or L item into units of about 30 min. Codex does time-boxed units (about 30 min); Claude reviews, merges, runs gates. Every "Done when" also needs before/after numbers of its scenario (S0-S9, 6.4.6).

#### Phase 0. Speed gate first (M, about 2 days; detail in 6.4.6)

- [ ] G1 (M) Scenario runner: add tour steps (sidebar, timeline-scroll, stream-reply, settings-page x21, resize-live, caption-hover), percentiles, frame counter, thread CPU, maximise + focus assert, real input — Code: InteractionRecorder.cs:244-325,403-463; DesktopPerformanceProbe.cs:36-84 — Done when: one command runs S0-S9 on 1.0.0 into JSON (fps, p50/p95/p99/max, stalls, thread CPU, memory, foreground=true); baseline stored — Owner pick: 9, 2.
- [ ] G2 (M) gate-check.py + gate-thresholds.json (about 150 lines), baseline store, run-gate.sh (one heavy slot) — Code: `lean-pass/native/` native_tools.py:445-520, launch-one.ps1:173,199 — Done when: pass/fail table, non-zero exit on a fail, comparison with the last accepted run; a non-foreground or non-maximised run is rejected — Owner pick: 2.
- [ ] G3 (M) Fake local provider + seeds (harness side only): loopback SSE, OpenAI-compatible, 127.0.0.1:8000; 500-thread and 2,000-entry seeds — Code: LocalDiscovery.cs:17-18; `lean-pass/native/seed-profile/nth/library.json` — Done when: S4 streams a long reply (about 100 msg/s for 60 s) with no real model; S5/S6 load the seeds; set NO_PROXY=127.0.0.1 if the sealed proxy blocks loopback (untested) — Owner pick: none.
- [ ] G4 (S) EventSource "nth-Perf" marks (trace the installed app without a special build); DebugOverlays in the profile build only — Code: new file; InteractionProbe.cs:12-33; InteractionRecorder.cs:56-62 — Done when: `dotnet-trace collect --providers nth-Perf` on the installed app shows marks; zero cost with no listener — Owner pick: none.

#### Phase 1. Stop the waste: idle becomes zero (S, 1-2 days; the appendix sizes P5 as M)

- [ ] P1 (S) Stuck caption hover #77: re-arm TrackMouseEvent on every WM_NCMOUSEMOVE and after release; clear hover on capture change, deactivate, client move; check GetCursorPos every ~10th frame; stop the pulse after a few cycles — Code: WindowManager.cs:495-543; CaptionButton.cs:132-196 — Done when: after a click and a move into the client area IsHovered is false within 100 ms; 25 s idle: render thread at 7.7% of a core or less (stuck: 37%) — Owner pick: none.
- [ ] P2 (S) Dextra ring #78: Armilla under AmbientMotionController; after 20 s without input ease to the still pose, stop frames; wake on input — Code: ArmillaMark.cs:285-430; ArmillaPoses.cs:218; ArmillaEngine.cs:206-218,473 — Done when: Dextra page, 25 s no input: 0 frames/s, drawing thread under 1% of a core; wakes on mouse move; no snap — Owner pick: 4.
- [ ] P3 (S) One loop guard in FrameClock: a callback re-asked from inside itself for over 1 s is held while minimised, covered or idle; debug watchdog names any loop running over 3 s with no input — Code: FrameClock.cs:181-186,224-234,257-268; loops in TabulaVersionStrip.cs:225-266, NthWorkingBeads.cs:75-208, HandshakeCapsuleHost.cs:61-93, EffortSlider.cs:1221-1251, CzStyleGlyph.cs:20-97 — Done when: 60 s with no input per main screen, the watchdog names no loop; short animations unchanged; streaming rows not frozen (6.5.1-2) — Owner pick: none.
- [ ] P4 (S) Frame caps: lights and orb at most 60 Hz, no interaction bypass; other UI motion at most 120 Hz; scroll, drag, press at full rate — Code: AmbientMotionController.cs:62-76; ComposerLightView.cs:35-36,114; VoiceOrbHandler.cs:17-18; FrameClock.cs:257-268 — Done when: typing: light and orb at most 60/s, UI at most 120/s; scroll/drag at display rate; A/B 60 / 120 / unpaced recorded — Owner pick: 5.
- [ ] P5 (M) Shared motion clock: one frame request and one commit per refresh for all tickers (about 35 copies of the tick idiom: AnimatedScalar, AnimatedColor, SidebarView); mutate transforms in place — Code: FrameClock.cs; AnimatedScalar.cs:62-157; PlainControls.cs:296-355; SwiftAnimation.cs:357-380 — Done when: five animations give 1 request and 1 commit per refresh; no per-frame transform allocation; full-rate capture matches — Owner pick: none.
- [ ] P6 (S) GPU cache: `.With(new SkiaOptions { MaxGpuResourceSizeBytes = 256L*1024*1024 })`; A/B default (about 29 MB) / 128 / 256 MB — Code: MarrowApplication.cs:25-39 — Done when: p95/p99 and dedicated GPU memory compared for S2, S3, S6; size recorded; no pixel change — Owner pick: none.
- [ ] P7 (S) Opaque window when solid: TransparencyLevelHint [None] at BackdropOpacity >= 0.98, back below — Code: WindowChrome.cs:147-173; RootView.cs UpdateBackdrop — Done when: pixels identical at 98%+; dwm.exe GPU measured before/after (feeds W8) — Owner pick: none.
- [ ] P8 (S) Log and choose the GPU: log the DXGI adapter list at startup; if adapter 0 is not the discrete GPU on the monitor, set GraphicsAdapterSelectionCallback — Code: MarrowApplication.cs; GpuProgramCache.cs:28-32 — Done when: the owner's PC log shows list and choice; any change has S2/S3 before/after (one re-warm-up) — Owner pick: none.

Expected after Phase 1: Dextra and idle chat near zero; hover and typing lighter. First big felt win: end of week 1.

#### Phase 2. Background: wallpaper, window glass, lights (M, 3-4 days)

- [ ] W1 (M) Wallpaper at about 1 Mpx (F16) + one SkSL upscale with the existing dither at full size; full-size override for hard-edged wallpapers (torus, louver, pleat, fluted); stretch the last image while resizing — Code: WallpaperRenderer.cs:225-297; WallpaperVisualHandler.cs:80-87; WallpaperCatalogue.cs:96-107 — Done when: corona render ms falls (A/B scale 1 / 0.75 / 0.5); similarity score vs scale 1 passes a threshold set before the run; owner's side-by-side shows no difference; decided with W2 — Owner pick: 6.
- [ ] W2 (M-L) Window glass reads the wallpaper's own frosted low-res texture (no full-window capture, blur or two shader passes; keep rim, bezel, dither). First the no-code A/B: Transparency 50% vs 98% — Code: Chrome.cs:484-489,519-541; GM:1276-1326,1698-1729 — Done when: at 50% no full-window Take/blur per wallpaper frame; cost gap to 98% small; GlassMaterial*ReviewTests green — Owner pick: none (owner judges the look).
- [ ] W3 (M) Per-piece damage in Reach.Grow: Take copies only that, skips when empty, no double copy on full cover; hover/press use output-only bounds — Code: GM:1287-1369,1455-1520,645-652 — Done when: a hover no longer redraws about a 250 pt square; GlassMaterialPartialRedrawTests, KeptBackdropReviewTests, GlassMaterialReach*Tests green; no seam — Owner pick: none.
- [ ] W4 (M) Composer glass: flat child buttons as plain translucent fills (same colours and shape) so the composer takes the cheap interior path; keep the merge neck — Code: Chrome.cs:306-314; GM:321-331,832-852 — Done when: composer on the interior path; pixel diff at flat capsule edges within sub-level antialias — Owner pick: none.
- [ ] W5 (M) Composer light: text analysis only when the text or wallpaper under it changes, and only near the text — Code: ShaderLightRenderer.cs:61-176; ShaderLightShaders.cs:183-262 — Done when: favilla band GPU time falls; readability tests (4.6:1 guard) green. Also R3.10: take the wallpaper veil brightness from the wallpaper's own cache or an asynchronous readback, never a synchronous ReadPixels every 3 s (ComposerLightView.cs:197-199,240-262; WallpaperDimVeil.cs:46-65); done when no render-thread stall every 3 s with an animated wallpaper — Owner pick: none.
- [ ] W6 (S) Wallpaper motion policy (pick; C = 30 Hz, holds during input bursts, stops after 2-3 min) + frame-budget governor (two late ticks widen decorative slots to 30 Hz) — Code: AmbientMotionClock.cs:6; AmbientRefreshPacer.cs:15-83; WallpaperVisualHandler.cs:46-67 — Done when: pick made; wallpaper frames in a typing burst match it; cadence judged by eye against the Mac video — Owner pick: 3.
- [ ] W7 (M) Multi-rect tracker in the glass, THEN A/B UseRegionDirtyRectClipping (MaxDirtyRects 8) — Code: GM:1329-1369 (1356-1359 accepts only SingleDirtyRectTracker); MarrowApplication.cs — Done when: reach/take tests pass with the Multi tracker; dirty area and glass draws per frame compared; no seam vs off. Needs W3 first — Owner pick: none.
- [ ] W8 (S) Measure dwm.exe (Acrylic vs Transparent vs None, idle vs animating); Mica only on the owner's pick — Code: WindowChrome.cs:147-148,165-170 — Done when: numbers recorded; small cost: close with no change; large: ask pick 8 — Owner pick: 8.

#### Phase 3. Typing, hovering, streaming, sidebar (M, 2-3 days)

- [ ] I1 (S) One composer layout per key press (shared cached TextLayout; no glyph outlines for multi-line drafts) — Code: ComposerTextView.cs:664-713,776-781,816-825 — Done when: TextLayout creations per key = 1 for a short and a 2,000-char draft (S3); looks unchanged — Owner pick: none.
- [ ] I2 (M) Sidebar hover: build the resting and hover clusters once per row (lazily), cross-fade with the same curve — Code: SidebarView.cs:3524-3739,4072-4117 — Done when: S6 hover sweep over 30 rows builds no controls; p95 under 8 ms; cross-fade matches the Mac frames — Owner pick: none.
- [ ] I3 (M) Streaming fade: mutate one brush's Color per fading run per tick, no new brush — Code: StreamVeil.cs:239-262,641-671; StyledText.cs:116-135 — Done when: TextLayout creations per veil tick fall to about 0; colours byte-identical (fade pixel guards) — Owner pick: none.
- [ ] I4 (S-M) Provider pump: one shared helper replaces `await foreach` in the 6-7 sessions; at most about 4 ms per UI turn; merge adjacent text deltas; strict FIFO — Code: ClaudeSession.cs:364-368,518-601; CommandCodeSession.cs:264; PiSession.cs:224; StdioProcess.cs:206-221 — Done when: in the S4 stress turn no UI freeze over 50 ms; old-vs-new test shows the same events in the same order — Owner pick: none.
- [ ] I5 (S) Narration, plan, thinking rows stream in place via MarkdownView.Stream — Code: TimelineRows.cs:2326-2340,3425 (guard at 1925); ThreadTimeline.cs:1982-1986 — Done when: no MarkdownView recreated per flush; streamed-reply pixel guard passes — Owner pick: none.
- [ ] I6 (M-L) Size animations become move/clip (FLIP): UpdatePill, popover bars, palette, Ignis folds first; sidebar slide and pane width after measuring; same spring curves — Code: UpdatePill.cs:38-136; UpdatePopoverContent.cs:185-220; RootView.cs:157-185; PaneWorkspace.cs:1054-1085 — Done when: UpdatePill reveal runs one layout pass; sidebar layout cost measured before any change — Owner pick: none (6.5.1-1 blocks the sidebar part).
- [ ] I7 (M) One LayoutUpdated hub per window; cache glass ContainerOf — Code: GM:72-77,136-151; ComposerLightSlot.cs:69-71; ComposerView.cs:104-105 — Done when: listeners on the main window drop from about 60 to 1 (counted in a test); glass still re-takes on a real move — Owner pick: none.
- [ ] I8 (M) Shared brush per (colour, theme); intern Opacity() — Code: SwiftColor.cs:268-274,370-470 (1,281 `.Brush(` sites unchanged) — Done when: brush/binding counts after opening Settings fall; theme, accent, high contrast still ease together — Owner pick: none.
- [ ] I9 (S) Corner clip memo: one corner resolve per update; circular rounded rect while resizing/zooming, exact clip at rest — Code: SwiftShapes.cs:194-285,400-448,507-560 — Done when: geometry builds in a 3 s live resize fall; at rest pixel-identical (286 corner tests green) — Owner pick: none (6.5.1-3).

#### Phase 4. Lists and memory (M-L, 3-4 days)

- [ ] L1 (L, unverified: the appendix row has no size cell, the Phase 4 heading says M-L) VirtualRows (grow DeferredRows): first wave of about 8 lists (models, OpenRouter picker, skills/plugins/tools, Archive, Downloads, Diff "Show all"); sidebar only if S6 still misses after L5 — Code: DeferredRows.cs; OpenRouterPicker.cs:117-122; ModelsSettingsPage.cs:556-700; DiffInspector.cs:770,796-805 — Done when: live controls per list bounded by the viewport; Diff on 5,000 lines no UI turn over 50 ms — Owner pick: none.
- [ ] L2 (M) Timeline far rows (over about 2 viewports away) freeze to same-height placeholders, rebuild on approach — Code: ThreadTimeline.cs:195-300,1378-1487 — Done when: S5 control count bounded, p95 under 8 ms; heights, selection, bottom anchor unchanged — Owner pick: none.
- [ ] L3 (S-M) Release Settings on close; pause Track while hidden — Code: SettingsOverlay.cs:233-254,395-405; ViewLifetime.cs:16-22 — Done when: with Settings closed, re-evaluations per minute equal the never-opened case; a change-while-hidden-then-reopen test passes — Owner pick: none.
- [ ] L4 (M) Memory: gcdump after all 21 pages and after a forced GC (managed / GC slack / native GPU); write the cause; then the matching fix (background gen-2 after close, or PurgeUnlockedResources + drain Backdrop.released) — Code: Marrow.App.csproj:21-22; SettingsOverlay.cs; GM:662,1244-1255 — Done when: S7 memory under 600 MB after every page and falls back after close — Owner pick: none.
- [ ] L5 (M) Sidebar change counter + per-project ThreadsIn cache — Code: AppModel.cs:317-345; SidebarView.cs:398-404,611-623,5353-5441 — Done when: a thread event costs the same at 10 and 500 chats (S6); output identical — Owner pick: none.
- [ ] L1b (S-M) Galleries and pickers: cache the translated SkSL string and the compiled SKRuntimeEffect per wallpaper id (per GRContext), shared by the backdrop and all thumbnails (finding R3.11); thumbnails and composer-style previews as still images, live only for the chosen or hovered one, stepped at most 30 Hz at their displayed size; "Show all" and the style picker open at once and fill in lazily — Code: WallpaperRenderer.cs:77; WallpaperCatalogue.cs:91-119; WallpaperThumbnailTicker.cs:34-41; WallpaperThumbnail.cs:190-215; the Customization composer-style picker (find with Glob) — Done when: Settings > Customization composer styles and Appearance > Animated wallpapers > Show all open under 100 ms (S7) and no frame over 16 ms while previews fill in; look unchanged — Owner pick: none. Solves rows #25 and #35. (Size: finding R3.11 says S for the shader cache and the 30 Hz cap; the composer-style previews are not sized, unverified.)

#### Phase 5. Start-up (M, about 2 days)

- [ ] S1 (M) Glass variants prepared in the awaited "Getting ready" pass and the Velopack hook — Code: GlassVariantPreparation.cs:10-66; App Program.cs:110,196; GM:763,789-1004 — Done when: cold first launch (GPU Programs cache deleted in a copied profile only): usable within 4 s, no HangWatchdog report, no UI stall over 50 ms — Owner pick: none.
- [ ] S2 (M) Start-up work off the UI thread: library parse once, hardware probe, IsWarm, boot prep, Hydra sweeps; keep the ordering rules (storage roots first) — Code: Storage.cs:489-521; AppModel.cs:194-237,425-486; VoiceComposition.cs:76-104 — Done when: warm start usable within 1.5 s; per-step timings show each item moved — Owner pick: none.
- [ ] S3 (S) Pre-open common popover windows — Code: PopoverHost.cs; PlainControls.cs (Popover); PopoverGlass.cs — Done when: first open under 100 ms, no frame over 16 ms (S8) — Owner pick: none.
- [ ] S4 (S) A/B TieredPGO off and composite ReadyToRun (csproj rule: keep composite only for a boot gain of 10% or more with at most 30% installer growth), and A/B GCSettings.LatencyMode = SustainedLowLatency and a larger gen0 budget (runtimeconfig), measured with dotnet-counters (speed-plan-findings-2026-10-14.md:894-896) — Code: Marrow.App.csproj:21-29 — Done when: S0/S1 times and GC/JIT counters compared; keep only if equal or better — Owner pick: none.

#### Phase 6. Avalonia update (S-M, one day)

- [ ] A1 (S-M) Try Avalonia 12.1.4 on a side branch (fixes text-layout churn and detached-visual retention) — Code: root Directory.Packages.props:4-13 (only the orchestrator edits it, gated by probe P10 screenshot regression); GM:1337-1349; ResizeStretch.cs:83-103 — Done when: ResizeStretch.MissingMembers, GlassMaterial reflection self-checks and *ReviewTests pass AND S2, S3, S7 are equal or better; else stay on 12.1.2 — Owner pick: 7.

#### 6.4.6 Phase 0 in detail: the speed gate

**Every change is measured before and after on the owner's real display (239 Hz; maximised window about 2576x1408, screen resolution unconfirmed) with the window maximised, focused and in the foreground, using real input. A measurement in a background, unfocused or covered window does not count. That mistake hid the lag before: ambient animations pause unless the window is Focused and Foreground, so old idle numbers were too good.**

Run rules:
- Release build from the installer payload, owner's PC; corona wallpaper, favilla light, Siri orb, Transparency 50% (default). The app sets the state itself (Activate, maximise, assert Focused and Foreground, record display Hz and window size); a non-foreground run is invalid. "The installer payload" means a Release self-contained `dotnet publish` of the same commit, built and launched by lean-pass/native/run-native.sh (worktree E:/Droppy/wt/cl/native-measure, a copied profile through MARROW_PORT_DATA_DIR, flag --performance-profile; launch-one.ps1:16 and :168). Never start the installed nth, never run the setup file, never read or write %LOCALAPPDATA%/nth. Only the lead runs it, inside native.lock and with all four heavy slots.
- Each step first raises what a person raises (KeyDown + TextInput, pointer, wheel; synthetic in-process for repeatability) plus one SendInput pass for the real Win32 path; assert AmbientWindowState LastInput advanced.
- 3-5 repeats, median, compare with the stored baseline; re-run a fail once. No typing or clicking elsewhere during a run (memory note native-runs-focus). About an hour per full run (pick 9).
- First step: a baseline on today's 1.0.0. Expected, unmeasured by this tool: Dextra idle about 7.7% of a core on the render thread; stuck hover about 37%.

| ID | What we measure |
|---|---|
| S0, S1 | Cold start (empty GPU cache) and warm start, 5 reps: time to first non-black frame |
| S2 | 60 s idle on each main screen: chat, Dextra, each Settings page; also unfocused/covered/minimised, after a mouse poke, stuck caption hover (sub-state letters unverified) |
| S3 | Typing a short draft and a 2,000-character draft |
| S4 | Streaming a long reply from the fake local provider |
| S5 | Scrolling a 2,000-entry chat |
| S6 | Sidebar with 500 chats: search, scroll, drag, hover sweep |
| S7 | Opening every Settings page, plus memory (list twice, forced GC and gcdump between) |
| S8 | Popovers |
| S9 | Sidebar open/close and window resize |

How frames are measured:
- Frames/s: count `avalonia.comp.render.time` measurements per second from a passive listener. It is not confirmed that this metric skips empty frames, so calibrate once (30 min) against Blit calls in dotnet-trace, DWM timing, or PresentMon (not installed; the owner must OK a download). Do not use TickProbe for idle: it asks for a frame every tick.
- p50/p95/p99/max of render time and of gaps between drawn frames; UI jobs over 16 ms; dirty area per frame; per-thread CPU every second (name the render thread with SetThreadDescription); GPU engine use and dedicated GPU memory per process (Windows counters, sampled by the driver process); dwm.exe GPU; working set; GC counts and pauses (dotnet-counters, separate pass).
- Start-up: first non-black frame from outside (Windows.Graphics.Capture, ffmpeg `C:/ffmpeg/bin` gdigrab, or ETW). Overlay runs force full redraws: eye only. Record full-rate video of typing, scrolling, streaming to compare with the Mac videos.

Also add normal-lane tests with no wall clock (rule R-TEST-5): 0 frame callbacks after 30 s manual time per screen; control-count budget per Settings page and sidebar; caption hover clears on deactivate/minimise/restore. The fake provider judges UI-side frame behaviour only. A second "owner-copy" pass runs on a copy of the owner's data folder (accounts Off, inside the runner folder).

Targets (proposed; the owner sets the final numbers):

| Scenario | Target |
|---|---|
| Idle, 25 s no input, any screen | 0 frames/s; drawing thread under 1% of a core |
| Typing, scrolling, streaming, hovering | p95 frame under 8 ms; no frame over 16 ms; no freeze over 50 ms |
| Warm start | usable window within 1.5 s |
| First launch after install | within 4 s, no frozen window |
| Settings page or popover | opens in under 100 ms |
| Memory | under 600 MB after every Settings page; falls back after Settings closes |

#### 6.4.7 Later, only if the numbers still ask (no ID)

Provider engine on its own thread (L; R6.9), only if the I4 pump still spikes. Cache static rows as images (BitmapCache; R7.1), only after Phase 2; never over glass, light or shader.
Fonts and scale (critic notes, speed-plan-findings-2026-10-14.md:894-907): add a Windows fallback font list in FontManagerOptions (Segoe UI Variable, Segoe UI Emoji, Segoe UI Symbol) and pre-shape common fallback glyphs on a background thread at start-up; record the owner's Windows display scale first (2576x1408 may be 100% or 150%). Optional: drop the render thread to AboveNormal outside interaction. Check whether MarkdownView.TryDecode runs on the UI thread (MarkdownView.cs:1775; the findings did not trace its caller).

### 6.5 Owner decisions still needed (picks 1-9)

1. **Approve the plan and its order**, or change it. Recommended: approve; Phase 0 first.
2. **Speed targets** (table above): keep or set your own. Recommended: keep.
3. **Wallpaper motion.** (A) today: moves 20 s after any input, still at rest. (B) holds still while you work, moves at rest. (C) moves gently whenever you look, at the cheap rate; holds during typing, scrolling, dragging, resizing (down in about 0.15 s, back in about 1 s); stops after 2-3 min with no input. **Recommended: C.**
4. **Dextra ring** rests in its still pose after 20 s without input and breathes again on movement? The findings recommend yes. If no: 15 fps breathe for about 2 min, then stop.
5. **Frame caps at 240 Hz:** decoration at most 60 Hz, other UI motion at most 120 Hz, scroll/drag/press at full rate. Recommended: yes (the Mac reference runs at most 120 Hz, inferred).
6. **Wallpaper at lower internal resolution**, only if the side-by-side shows no difference. Recommended: yes, with a full-size override for sharp wallpapers.
7. **Avalonia 12.1.4** if our checks pass. Recommended: yes, one-day trial.
8. **Mica instead of Acrylic** for the window base, asked only if W8 shows a big dwm.exe cost. Recommended: do not ask unless measured; Mica shows nothing behind the window, so the look changes.
9. **Quiet PC time** for gate runs: about an hour per full run, app maximised and focused, nobody types. Recommended: reserve slots; baseline first.

#### 6.5.1 Other open questions in the findings (not among the 9)

1. Sidebar/pane slide: keep squeezing the detail every frame, or lay out once at the final width and play a composited slide (RootView.cs:157-161 says the Mac does this)? Owner or canvas decision (I6).
2. May non-decoration loops (Working-row shimmer, Hydra sweeps, Tabula pulse, Settings light tiles) pause after 20 s idle, or must a streaming row always move? (P3)
3. Drop the exact continuous clip while resizing/zooming, and for small radii at rest? (I9)
4. Is a 30 Hz wallpaper acceptable next to the Mac video? Which wallpapers are "sharp"? (W1, W6)
5. May PresentMon be downloaded? May the gate move the real mouse in a reserved slot? (G1)

### 6.6 Risks and what NOT to do

Risks: the look drifts (pixel test or similarity score per visual change; full-rate captures for motion); glass changes are the most delicate (on/off switch for A/B, one at a time); glass reflects into Avalonia internals (keep the self-checks; they also decide A1); noisy measurements (3-5 repeats, stored baseline); memory growth unexplained (dump first, L4).

Do not:
- Rewrite, restart, fork or patch Avalonia. Do not upgrade it except the planned 12.1.4 trial on a side branch.
- Turn on `UseRegionDirtyRectClipping` as a one-liner: the glass only understands SingleDirtyRectTracker (GM:1356-1359) and would get slower and seamed. Do W3 then W7.
- Region-clip, BitmapCache or half-rate the wallpaper before the glass change it depends on (W2, W3, W7): under one dirty rect the whole window is still redrawn and re-blurred. Downscale the wallpaper once, not twice (W1 and W2 share one similarity gate).
- Change CompositionMode, RenderingMode, OverlayPopups, ShouldRenderOnUIThread or DpiAwareness (other modes lose Acrylic, transparent popups, ResizeStretch; R1.12). Switch to Mica without pick 8.
- Measure in a background/unfocused/covered/non-maximised window, or take gate numbers from overlay runs or TickProbe. Put wall-clock asserts in the normal test lane (R-TEST-5).
- Write Foreground, Text or FontSize, or build controls, inside a frame callback or hover handler (add a lint).
- Ship a visual change without a pixel test or similarity score, or a motion change without a full-rate capture.
- Run more than one heavy native run at once. Give Codex units a deadline (about 30 min, BLOCKED as fallback). Claude weekly usage is 96% until 2026-10-18 10:00Z: Codex starts Phases 0-1, Claude reviews lightly. Codex writes the harness code and tests only; baseline and gate runs that launch nth are run by the lead in the quiet window under native.lock (section 7.2.5).

Time: phases add up to 15-16 working days (estimates in the headings). Phases 2+3 and 4+5 can run side by side: about 2.5 weeks of speed work; the whole next release about 3.5-4 weeks.

### 6.7 What is NOT the problem (do not spend time here)

- Avalonia itself (SourceGit, Wasabi, PixiEditor, Stability Matrix are smooth on it) and the Mac look.
- Busy screens already update in place: sidebar keyed reconcile (SidebarView.cs:722-779), timeline rows by block id (ThreadTimeline.cs:195-300), streamed reply keeps settled blocks (MarkdownView.cs:219-259). Of 132 Children.Clear sites about 100 are cold pages, about 25 single-slot swaps, 6-8 warm.
- Blocking waits: nearly all of the 18 are off the UI thread; process spawn, git, attachment import, thread decode, @-mention walk too.
- Ambient effects already stop when minimised or unfocused, and 20.6 s after input. Popovers and tooltips already use Windows' backdrop (PopoverGlass.cs) at no render-thread cost.
- Small or ruled out: nested opacity, shadows, short animations, image decodes, file watchers, DPI manifest, priority settings on nth's own process. Uneven 60 Hz cadence at 144/165 Hz: low priority at 239 Hz.
- The earlier lean pass measured bytes with no window open and idle runs unfocused: its "idle fixed" claims are not frame-time proof.
- Graphify is not needed for the speed work (plan section 7); the owner later approved a code map at `E:/Droppy/work/graphify/` for navigation only (section 9).

### 6.8 Count check

The plan appendix has 39 change IDs: G 4 + P 8 + W 8 + I 9 + L 5 + S 4 + A 1 = 39 (counted with grep). This file adds L1b (galleries and pickers, from finding R3.11), so section 6.4 has 40 `- [ ]` items. Two "Later" items (6.4.7) have no ID. Owner rows solved inside this plan: #1, #2, #25, #29, #35, #38, #77, #78 (row #79); #25 and #35 through L1b.

## 7. Smart testing and the build/merge/package method

From draft s5. Section numbers in this part were renumbered when the file was assembled. Commands are for Git Bash.

Written 2026-10-14 from files and read-only git. "(unverified)" marks what could not be confirmed. Commands are for Git Bash, forward slashes.

### 7.1 Read first

Authority: porting/PORT_CONTRACT.md, contract_amendments.md, owner_decisions.md (ODs), engineering_decisions.md (EDs). Unit rules in E:/Droppy/work/scratch/lead/: FEATURE-RULES.md (Claude units), CODEX-LOCAL.md (Codex), LEAN-TESTS.md (wins over any older "run whole projects" text), usage-rules.txt (first in every agent prompt).
Authority detail (FEATURE-RULES.md:7-9): OD-0103 puts the first-release features in and the ODs decide looks and words; where no reference frame exists the Swift source is the proof (OD-0098); reference videos: E:/Droppy/work/reference-video/ with INDEX.md, CATALOG.md, study/ and frames/ - a frame is a source fact.

Git facts checked 2026-10-14 (read-only); the branch situation is also in section 3.2.2. DECIDE THE BASE BEFORE STARTING (checklist item NR0.5):
- 1.0.0 was packaged from E:/Droppy/wt/cl/pkg-morning at 87eb11490 (detached). That commit is on `claude/final-pre` (tip 9e083470c = 87eb11490 + 3 commits: a workflow-file fix, ledger records).
- `claude/release-integration` (a394de7e4, worktree E:/Droppy/wt/cl/rel-int) is 648 commits BEHIND 87eb11490 (`git rev-list --count claude/release-integration..87eb11490`). The last merges of 2026-10-13 went to `final-pre`. Branch from `final-pre` (= origin/claude/release-integration) or fast-forward the local `release-integration` to it. Below, `$BASE` is your choice. `codex/kokoro` (714bfbc3a) is 2 commits ahead of 87eb11490.
- `origin` = https://github.com/Oak993/marrow-port.git. The lead pushes claude/* (fast-forward only); a Codex unit pushes only its own codex/<unit>.

The never-list is in section 10 (sources: FEATURE-RULES.md:5-6, 39; CODEX-LOCAL.md:9-16). Also: never use another agent's worktree; a NuGet restore in a test run is fine, any other network call is not.

### 7.2 Smart (lean) testing

Owner order 2026-10-12 (LEAN-TESTS.md:3): "test what the function interacts with and finish".

#### 7.2.1 Steps (LEAN-TESTS.md:7-14)
1. While building: only your own new or changed test classes.
2. Before landing or reporting done, ONE run in one heavy slot, native excluded: your unit's classes + classes covering each touched source folder + classes naming a changed type + `TraceabilityTests` of each touched test project.
3. Never an unfiltered Marrow.App/Core/UI/Services/Setup.Tests. Full suites run once per integration gate, by the lead (7.6.1).
4. Unsure? Include the area: a few classes cost minutes, a whole project 20-45 min and a heavy slot.

#### 7.2.2 Map a diff to test classes
`src/Marrow.<P>/<Folder>/` is tested in `tests/Marrow.<P>.Tests/<Folder>/`, namespace `Marrow.<P>.Tests.<Folder>.` (e.g. Marrow.App.Tests.Views.Settings). Exceptions: src/Marrow.UI/NthUi -> tests/Marrow.UI.Tests/Nth and NthUi; src/Marrow.Shim -> tests/Marrow.Shim.OfflineTests; root files (src/Marrow.App/AppModel.cs) -> AppModel*Tests. So always do (c).
```bash
S=E:/Droppy/work/scratch/<unit>; cd <worktree>
git diff --name-only $BASE...HEAD > $S/changed.txt
grep '^src/' $S/changed.txt | awk -F/ 'NF>=4{print $2"|"$3}' | sort -u          # (a) Marrow.App|Views -> ~Marrow.App.Tests.Views.
grep -E '^src/[^/]+/[^/]+\.cs$' $S/changed.txt                                    # (b) root files
grep '^src/.*\.cs$' $S/changed.txt | xargs -n1 basename | sed 's/\.cs$//; s/[+.].*//' | sort -u > $S/types.txt   # (c) types
grep -rlwFf $S/types.txt tests --include='*.cs' | xargs -n1 basename | sed 's/\.cs$//' | sort -u > $S/classes.txt
paste -sd'|' <(sed 's/^/FullyQualifiedName~/' $S/classes.txt)                     # (d) filter text, one run per test project
```
Drop generic type names (Program, Chrome) by hand. Add the folder filters from (a) and `FullyQualifiedName~TraceabilityTests`. Keep a filter under about 8,000 characters (limit unverified); else split the run.

#### 7.2.3 Filters and wrappers
Syntax: `~` contains, `!~` not, `=` equals, `&` and, `|` or. Parenthesise any `|` group and ALWAYS end with `&FullyQualifiedName!~Native`: `"(FullyQualifiedName~A|FullyQualifiedName~B)&FullyQualifiedName!~Native"`.

In E:/Droppy/work/claude-verify/, the first four scripts below take `bash <script> <worktree> <TestProject> "<filter>"`; they unset 18 API-key variables, use a fresh MARROW_PORT_DATA_DIR and `--no-build` (BUILD FIRST):
- one.sh (500 s): prints only FAIL/Passed!/Failed! lines, no zero-match check; not for lean runs.
- one-full.sh (500 s): result line + 3 lines per "Error Message". one-names.sh (900 s): + failing names. one-names-long.sh (3000 s): same, for filters over several App/UI classes.
- rerun-alone.sh is in E:/Droppy/work/scratch/lead/, not in claude-verify: `bash E:/Droppy/work/scratch/lead/rerun-alone.sh <checkout> <logdir> <project> <full test name>...` reruns each red alone up to twice, through the heavy slots.

#### 7.2.4 What counts
- Pass = a `Passed!`/`Failed!` line whose Total covers your new tests. one-full/names/names-long print `NO TEST RESULT LINE (exit N)` and exit 3 when the filter matched nothing or the run died: fix the filter. Silence is never green (LEAN-TESTS.md:21).
- A failure counts only if it fails ALONE twice (`FullyQualifiedName~<Class>.<Method>`, two runs). Report each red as "fails alone x2", "passed alone" or "accepted <OD>".
- Never loosen, skip or delete a test or edit an expectation to match output; a real design change needs an ED plus an equal-or-stricter replacement.
- A class running minutes at full CPU is a hang: stop and bisect. Builders do not mutation-test; the reviewer does.

#### 7.2.5 Native tests
Only the lead, in a quiet window (unless a brief says otherwise). Native = classes with "Native" in App.Tests and UI.Tests.
```bash
until mkdir E:/Droppy/work/scratch/lead/native.lock 2>/dev/null; do sleep 30; done; <run>; rmdir E:/Droppy/work/scratch/lead/native.lock   # release even on failure
```
The window holds all 4 heavy slots + native.lock; no browser, design or computer-use agents; nobody types or clicks (typing elsewhere cost 10 Escape/Return/60 Hz failures on 2026-10-04). Rerun such reds alone twice. A full native pass takes about 1 h. Speed claims need per-frame and allocation numbers, not stills: `bash E:/Droppy/work/lean-pass/native/run-native.sh <ref> <label> [--vs <ref2> <label2>] [--only N1,..] [--reps 5]`.

#### 7.2.6 Typical times (gate logs, shared PC)
Incremental build 26-32 s (cold: unverified). One class alone 0.4 s-2 min. Core ~9.7k tests 1.5 min. Services 1 + 2.4 min. UI ~6.1k 8.7-13.5 min. App Views ~2.6k 7.6 min. App rest 33-39 min (hits the 2400 s cap under load). Setup ~1k 19 s; Shim 5 s. ComposerVoiceCleanPillTests ~24 min. Gate without native about 1-1.5 h plus slot waits. One App.Tests testhost uses 4-4.5 GB RAM.

### 7.3 Build and heavy slots

Run the commands of the first block only inside the heavy_acquire block below (never build or test without a slot).

```bash
dotnet build tests/Marrow.App.Tests -v q --nologo 2>&1 | tail -5     # one test project; expect 0 warnings, 0 errors
dotnet build Marrow.sln -c Debug --nologo -v q                        # whole solution, gate only
dotnet build tests/Marrow.Shim.OfflineTests -c Debug --nologo -v q    # not in the sln
```
SDK pinned in global.json (10.0.401). Worktrees whose bin/obj were cleared (~338 on 2026-10-14) need a build before any `--no-build` wrapper. ONE Bash call per run (shell state does not survive between calls):
```bash
export HEAVY_NO_TRAP=1; source E:/Droppy/work/scratch/lead/heavy.sh; heavy_acquire
dotnet build tests/Marrow.App.Tests -v q --nologo 2>&1 | tail -5
bash E:/Droppy/work/claude-verify/one-names-long.sh <worktree> Marrow.App.Tests "(<filter>)&FullyQualifiedName!~Native"
heavy_release
```
heavy.sh: 4 slots (heavy.N.lock folders in scratch/lead). Slot 1 is always free; slots 2-3 need 3 GB available RAM; slot 4 needs 8 GB. Reclaimed: dead-owner slots, ownerless folders over 1 min, any slot over 60 min; one slot per shell. Wait cap `HEAVY_WAIT_CAP_MIN` (30; 4 on a 5-min prompt cache), then "HEAVY: no slot free" and exit 75: rerun the same command; never build or test without a slot. `HEAVY_NO_TRAP=1` = you call `heavy_release`. Priority lane: dir listed in scratch/lead/heavy-priority.txt, or `HEAVY_PRIO=1`. `MSBUILDDISABLENODEREUSE=1` is exported (no node reuse, no idle MSBuild workers).

Load rules: one build/test run per agent at a time; the 2-build-workflow cap (owner 2026-10-11) was lifted by the owner on 2026-10-12 ("full speed"); today the gates are the 5-6 GB free-RAM floor and the 4 heavy slots; when restarting from the park, start at 2 workflows and add more only while RAM allows; free RAM 5-6 GB or more (2026-10-04 five agents drove it to 0.1 GB and Claude died). Long waits: `ls E:/Droppy/work/scratch/lead/heavy.*.lock` and look for stuck dotnet/testhost. Check `df -h /e` (gate hang dumps filled E: on 2026-10-13). Stale-process checks (by hand, no auto-kill scripts): section 8.5.5. Log events: section 11.

### 7.4 Code rules

- Test-first: write each acceptance test, see it fail once, then implement (FEATURE-RULES.md:12). Never invent a string, control or default; if the spec is silent choose minimally and label it ENGINEERING DECISION. Every visible change is transitioned (OD-0076); prove motion per frame.
- Lean code (LEAN-TESTS.md:17-18): reuse in order: code already in the repo, .NET/Avalonia built-ins, packages already referenced (no new package without the lead). Lean never means less: keep validation, error handling, accessibility, motion polish and the unit's "done when".
- Test files: one type per file, no nested helpers, fixtures in tests/<project>/Fixtures/, single-line attributes.
- Packaging by a unit (FEATURE-RULES.md:39, OD-0105 item 6): a unit never runs package-windows.ps1, vpk, Velopack pack or Setup.exe, even if a record says "run"; test packaging logic only through script-text and seam tests; if a record truly needs a packed build, report it as NOT CLOSED (needs the installer step).

Build floor: grep your ADDED lines before landing (LEAN-TESTS.md:22); remove each hit or justify it in an ED; `async void` only in event handlers; empty output is the goal:
```bash
git diff $BASE...HEAD -U0 -- '*.cs' '*.csproj' '*.props' '*.targets' | grep -E '^\+' | grep -vE '^\+\+\+' | grep -nE '#pragma warning disable|SuppressMessage|<NoWarn>|NotImplementedException|TODO|catch[[:space:]]*(\([^)]*\))?[[:space:]]*\{[[:space:]]*\}|async void|\.Result\b|\.Wait\(\)|GetAwaiter\(\)\.GetResult\(\)|Console\.WriteLine|Debug\.WriteLine'
```
Item-10 checklist (LEAN-TESTS.md:23-31; only what your unit touches; reviewers check it too):
- Paths (from a model, page, download, config): resolve fully (junctions, symlinks, `..`, `\\?\`, case), act only strictly under an allowed root; on refusal log and stop, never fall back to a wider folder.
- Retry/resume side effects: record intent before, outcome after; test a crash between the two; unknown outcome needs a guard or the user, never a silent re-run.
- Caches: key names every input (profile, private tab, origin, thread); say how entries go stale; cap size.
- Thread/ordering/cancel claims: deterministic test (injected clock or scheduler, no Sleep/Delay), else ED marked UNVERIFIED.
- Logs: sentinel secret in the input, drive the failure path, assert the log never holds it; stable event name + run id.
- Formats: versioned; the previous file loads; unknown fields survive load-and-save; a newer file stays untouched.
- Controls: accessible name, Tab order, Escape closes and returns focus, no state by colour alone, loading/empty/error states, one long-text/RTL/large-scale test.
- UI thread: no blocking I/O or long work; loops and retries capped or cancellable.

Traits (tests/Marrow.Testing/Traceability.cs:17-39, 65-76): each test needs `Req` (`(PR|UI)-AREA-NNN`), `Rule` (`R-AREA-N`) or `Decision` (`ED-NNN`, `ED-NEW-<lowercase-words>-<n>`, `OD-NNNN`; `web-` names have narrower shapes); `Oracle` is `O-NN`. Marrow.App.Tests accepts only Req/Rule/Decision/Oracle; Core and Services may add `Feature` and `Spec`. Marrow.Setup.Tests cannot reference Marrow.Core or Marrow.Testing (CS0433; installer dossier 2026-10-12): link Core files as source.

### 7.5 Review and merge

Flow (claude-verify/workflows/feature-unit.js): build test-first, adversarial review, fix, up to 3 rounds; merge only when the reviewer says NOT REFUTED. Models: section 8.5.1. A finding needs a failing test or a measured number to count as confirmed.

Reviewer checklist: (1) `git diff $BASE...<branch> -- tests`: every removed or weakened assertion needs an ED and an equal-or-stricter replacement. (2) Build-floor grep + item-10 on the diff. (3) Mutate the new logic (flip a condition, drop a guard); a test must go red. (4) Re-run the lean set; classify every red (7.6.2). (5) Visual work: compare with E:/Droppy/work/reference-video/ at full frame rate. The owner judges UI and motion on the final installer (memory review-on-final-installer): do not wait for the owner's eye; list what the owner should check.

Landing a unit: merge `$BASE` INTO the unit branch (conflicts so far were additive: keep both), rebuild, re-run the lean set, then fast-forward under a land lock (`<lane>` was `dextra` or `web`; pattern in scratch/lead/dextra-build-2026-10-12.js:31):
```bash
until mkdir E:/Droppy/work/scratch/lead/<lane>-land.lock 2>/dev/null; do sleep 15; done; echo <unit> > E:/Droppy/work/scratch/lead/<lane>-land.lock/owner
git -C <integration worktree> merge --ff-only <unit branch>
rm -f E:/Droppy/work/scratch/lead/<lane>-land.lock/owner; rmdir E:/Droppy/work/scratch/lead/<lane>-land.lock
```
If the base moved while you tested, merge again and re-test first. The lead merges finished branches with `git merge --no-ff --no-commit <branch>`, resolves, and commits with the OD.

- ODs: `## OD-NNNN <title> (date)` in owner_decisions.md, owner words verbatim. Highest at 87eb11490: OD-0156 (line 1362). `grep -oE '^## OD-[0-9]+' porting/owner_decisions.md | sort | uniq -d` must print nothing (run it on the merged file; it prints nothing at 87eb11490). Renumber with a whole-word regex, only in files the branch changed, plus a "Numbering note".
- EDs: a unit appends `## ED-NEW-<unit>` (then `-1`, `-2`) at the END of engineering_decisions.md; the lead numbers at merge. Last numbered: ED-644 (line 5643). Incomplete work (hidden page, flag off) goes in porting/STATE.md's incomplete list.
- Ledger conflicts are append-only: keep HEAD then theirs, byte-exact (`python E:/Droppy/work/scratch/lead/ed_merge.py <repo> <ED-NEW-name> <ED-NNN>`; it also renames, read it first). Afterwards: N added, 0 deleted, nothing missing, no duplicate headings.
- Ledger bytes: at 87eb11490 owner_decisions.md, engineering_decisions.md and STATE.md are 100% CRLF in git; older history mixed CRLF and LF, and a text-mode rewrite once made a 404-line diff. Edit with the Edit tool, `cat >>`, or Python `rb`/`wb`; every new line ends CRLF; never `sed -i` on a ledger; `git diff --stat` must show only the intended lines (e.g. `+28/-0`). A double backslash in a Bash heredoc arrives as ONE: use Write/Edit or `chr(92)`. Workflow scripts must be LF.
- Commits: small steps, files added by name, trailer `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>` (or the exact trailer in the session's attribution reminder). Keep discarded branches (e.g. claude/sidebar-slide 9d0e829a3); deleting needs the owner's OK on a list. Scratch only in E:/Droppy/work/scratch/<unit>/ (Codex: codex-<unit>/). Report: head SHA, done (test names), not done + reason, ED-NEW items, suite counts (Codex adds READY/NOT READY). Units never stop to ask (FEATURE-RULES.md:31). Every agent prompt carries the AUTHORITY line; never SendMessage a running workflow agent (it starts a duplicate). Codex units: ship once the gate passes, ~30 min deadline.
- Git roles. The line "Workers never commit" in the repo's AGENTS.md is old. CODEX-LOCAL.md rule 1 replaces it: a Codex unit commits in its own worktree and pushes only the codex/<slug> branch its brief names. A Claude unit commits in its worktree but never pushes or merges (FEATURE-RULES.md:6); the lead pushes claude/* (fast-forward only). Nobody touches wt/cl/terminal-verify (old staging) or any other agent's worktree.

### 7.6 Gate and package

#### 7.6.1 Full gate (lead only, once per integration)
```bash
bash E:/Droppy/work/claude-verify/gate-seq3.sh <rel-int checkout> E:/Droppy/work/scratch/<gate-log-dir>
# GATE_GROUPS="ui app-views"  run only those groups     GATE_NO_NATIVE=1  skip the native tail (day runs)
```
It builds Marrow.sln and the Shim tests, then runs core, svc-prov, svc-rest, ui, app-views, app-rest, setup, shim (gate-seq3.sh:34-43; each in its own slot, `timeout 2400`, `--blame-hang-timeout 150s`, `!~Native`), then native-app and native-ui under native.lock (44-49). Per group: .log/.trx/.exit, FAILED names printed. Rerun every red alone twice (rerun-alone.sh, section 7.2.3); a Sonnet reviewer reads the result. App groups hit the cap on 2026-10-12/13, so gate-seq3.sh excludes ComposerVoiceCleanPillTests and the two speck-matrix tests (`..._have_no_specks`); scratch/lead/views-r3.sh reruns the views in two halves (3600 s cap).

Faster variant used for the 1.0.0 gate (log 2026-10-13 18:11): `bash E:/Droppy/work/scratch/lead/gate-par.sh <checkout> <log dir>` - one build, then the gate-seq3 groups plus the views-r3 halves (and the views-composer group) in four parallel streams, each stream its own bash process because heavy.sh keys slot ownership on $$; same filters, caps and exclusions, Native excluded. Logs of the last run: C:/nth-gate. Rerun every red alone twice as usual.

#### 7.6.2 Accepted pre-existing failures (no single list file; they live here)
- Owner-accepted, porting/owner_decisions.md at 87eb11490: OD-0128 (line 1173), OD-0137 (1226; 183 of 470 ComposerVoiceCleanPillTests V-T133 speck cases), OD-0152 (1272; one 200% Float 26 px dark pop case), OD-0156 (1362; 30 more identities over the shipped glass + one borderline case). Tests are not changed or skipped. Any other new V-T133 failure and any V-T132 failure still blocks. **OD-0137 requires V-T133 to be re-anchored to the pill's rendered position (same rules, equal-or-stricter, a "design wins" ED) BEFORE the next feature release ships**: make it a unit of the next release; then the gate-seq3 exclusions go.
- Baseline reds the lead named 2026-10-13 (NEXT_SESSION_CLAUDE.md log 17:07, 18:08; re-check at the new base): GapCatalogue, GapModelDownloadAcceptanceTests GAP_A, PcPauseWait, VelopackStartupHookTests (its relay test changed in the dextra land; still red is unverified), SidebarRowUpdateDot.
- Flakes that passed alone twice (same log): SkillOverlaySessionTests.Cursor_without_a_generation, EffortSliderJewelWarmupTests.Warm_async, AntigravitySessionTests interrupt, MCPHubDeadlineTests exit 9.
- Suggestion (not done): put these lists in one porting/ file.

#### 7.6.3 Package (lead only; never a unit agent)
Preconditions: gate green apart from 7.6.2, native window done, `df -h /e` OK, nothing else building.
1. Bump the version: src/Marrow.App/AssemblyInfo.cs:9 `CFBundleShortVersionString` ("1.0.0" now); line 10 `CFBundleVersion` is the build number.
2. CHANGELOG.md needs a `## [<version>]` entry (only `## [1.0.0] - 2026-10-08` exists) with populated `### New features`, `### Bug fixes` or `### Refinements`, or the script fails before doing anything. Pre-check: `pwsh -NoProfile -File scripts/package-windows.ps1 -ValidateReleaseNotesOnly`.
3. Build from a NEW detached worktree in a heavy slot (as scratch/lead/gated-build.sh did; it hard-codes the 1.0.0 file name, so copy and edit it). Do not reuse E:/Droppy/wt/cl/pkg-morning: it holds the shipped 1.0.0 tree, its artifacts/ payload and installer copy, and the 1.0.0 build log pkg-gated/package.log; if artifacts must be cleared, move the folder aside, never rm -rf it:
```bash
git -C E:/Droppy/port worktree add --detach E:/Droppy/wt/cl/pkg-<version> <sha>   # a new tree; pkg-morning stays the 1.0.0 reference
cd E:/Droppy/wt/cl/pkg-<version>      # git status --porcelain must print nothing
mkdir -p E:/Droppy/work/scratch/lead/pkg-<version>
pwsh -NoProfile -File scripts/restore-dependencies.ps1; dotnet tool restore
pwsh -NoProfile -File scripts/package-windows.ps1 -SkipUiSmoke > E:/Droppy/work/scratch/lead/pkg-<version>/package.log 2>&1
```
4. Parameters (package-windows.ps1:63-75): `-Configuration` (Release only), `-PackAuthors oak993` (publisher), `-ReleaseNotesFile`, `-ArtifactsDir`, `-Sign None|SignTool|Azure` (None = unsigned; signing needs the owner's word), `-SkipUiSmoke`, `-ValidateReleaseNotesOnly` (plus `-Runtime`, `-PackId NthApp`, `-PackTitle`, `-MainExe`, defaults fine). `-SkipUiSmoke` omits the one check that RUNS the payload-free `nth-Setup.exe --ui-smoke` (window, shell-link round trip, wallpaper read under Native AOT); 1.0.0 shipped with it and the script ended "NOT FULLY VERIFIED" (line 400). Always pass it: running the UI smoke starts a setup exe, which section 10 forbids unless the owner says so explicitly. Toolchain pins: scripts/release-toolchain.json (SDK 10.0.401, Node 24.19.0, vpk 1.2.161); this PC's Node differs and the build was fine.
5. Result: artifacts/installer/nth-Setup-<version>-UNSIGNED.exe is THE file to share (188,453,376 bytes for 1.0.0). The script's build checks (GUI exes, version resources, icons, no "Marrow" in setup exes, VC++ runtime, size budget) must all pass before it prints "Built"; exit code 0.
6. Copy to E:/Droppy/Installer/ with the new version in the name and write BUILD-NOTE-<version>.txt beside it (time, branch, full SHA, contents, sha256 first 16 hex, size). E:/Droppy/Installer/nth-Setup-1.0.0-UNSIGNED.exe and BUILD-NOTE.txt (built 2026-10-14 00:11 from 87eb11490) are what the owner is testing now: do not move, overwrite or rewrite them until the owner finishes. Only a setup older than 1.0.0 may be moved to Installer/old/.
7. NEVER run Setup.exe, nth-Setup.exe or msiexec on this PC. Never upload: the script prints the `vpk upload github ...` line for the owner.
8. Tell the owner: what is in, the accepted reds, that nothing was run or uploaded, what to check by eye. The owner reviews UI and motion on the final installer; fixes ship as a new installer.

### 7.7 Test plan template (fill in per unit before coding; paste into the report)

```
UNIT: <id>  WORKTREE: <path>  BRANCH: <name>  BASE: <$BASE sha>  Records/ODs/EDs: <ids>  DONE WHEN: <unit's lines>
1. TEST-FIRST: <Class.Method> proves <behaviour>; trait <id>; red seen: yes/no
2. DIFF MAP ($BASE...HEAD): src folders <Project|Folder> -> test folders <..>; changed types -> classes <..>;
   test projects touched <..>; TraceabilityTests included: yes/no
3. FILTER per project (ends &FullyQualifiedName!~Native): <Project>: "<filter>"; one-names-long.sh; expected Total >= <n>
4. NATIVE: none / owed to the lead: <classes> (why)
5. BUILD FLOOR grep empty: yes/no (hits + ED); ITEM-10 areas: <paths|effects|caches|threads|logs|formats|controls|UI thread|none>
6. RESULT (paste wrapper lines): build 0/0; Passed!/Failed! <Total/Passed/Failed>; each red: fails alone x2 / passed alone / accepted <OD>
7. LEDGERS: ED-NEW-<unit>-n added, CRLF kept, `git diff --stat` +N/-0; STATE.md incomplete list: updated yes/no
8. LANDING: base merged at <sha>; lean set re-run after merge; land lock used; head SHA <sha>
9. NOT DONE (exact reason): <..>   VERDICT: READY / NOT READY
```

## 8. Agents, tokens, cache, context and the owner

From draft s6. Section numbers in this part were renumbered when the file was assembled. Recommended settings are NOT applied.

Written 2026-10-14 16:09 (`date`). Sources: the memory files in C:/Users/omera/.claude/projects/E--Droppy/memory/, E:/Droppy/work/scratch/lead/usage-rules.txt, NEXT_SESSION_CLAUDE.md lines 13-19, HANDOFF_PROMPT.md, the Codex rule files in E:/Droppy/work/scratch/lead/, and 3 docs lookups. "(unverified)" = not confirmed. Section 8.4 settings are recommendations only: none is applied, the owner decides. Section 8.8 is a prompt template for a unit agent.

### 8.1 State today (2026-10-14)
- Claude weekly usage is 96%, resets 2026-10-18 10:00Z (lead's figure). Until then give build and fix work to Codex or another model. Claude only reviews, merges, gates (8.5.6).
- Codex weekly was 19% left on 2026-10-12 22:25 (0 resets, 0 credits, reset about 2026-10-18 08:00) and about 24% left at 2026-10-14 00:23 (log:425, section 3.1). Today's figure: (unverified). Read the usage card in the ChatGPT app first.
- When an account hits its weekly limit the owner switches account and says only "read the handoff file". Keep the handoff current (8.4.5).

### 8.2 Token saving rules (every agent)
Source: usage-rules.txt (owner 2026-10-11: no usage may be wasted).
1. Small tool output. Pipe builds and tests through `tail -40` or grep `Passed!|Failed!|error`. Never print a whole log or a 100 KB file.
2. Read by line range or Grep. Never re-read a file already in context.
3. Batch independent tool calls in one message.
4. No idle waits across turns. A long build or test is ONE blocking Bash call (heavy slot acquire, build, test, release), not sleep-and-poll turns.
5. No duplicate work. Check what exists first (git log, worktree list, scratch folder). Never start a second copy of a build that may still run. After a Bash timeout, look for leftover dotnet/testhost first.
6. Cheapest model that fits (8.5.1). Low `effort` for mechanical stages. Verify findings with 2-3 lenses, no more.
7. Compact reports: numbers, hashes, `file:line`; do not restate the brief. Prefix every file you write in the shared scratch folder with your unit id (`AS9-run.sh`); never edit another agent's file.
8. Shared constant FIRST in sibling prompts. The cache matches a byte-identical prefix. Put no unit id and no timestamp before it.
9. Tests only via E:/Droppy/work/claude-verify/ `one.sh`, `one-full.sh` (500 s timeout), `one-names.sh`, `one-names-long.sh`, always with `&FullyQualifiedName!~Native`. Never a bare `dotnet test`. usage-rules.txt "never use longer timeouts" means no hand-made timeouts; one-names-long.sh (3000 s) is the approved wrapper for a filter over several App/UI classes (CODEX-LOCAL.md rule 3). A class running minutes at full CPU is a hang: stop and bisect. A failure counts only if it fails alone twice.
10. Lean tests (owner 2026-10-12, scratch/lead/LEAN-TESTS.md): your unit's classes, the classes of each source folder your diff touches, `TraceabilityTests` of each test project touched. Never a whole project. The lead runs full suites once per integration gate.
11. Code map before wide searches: `E:/Droppy/work/graphify/nth-src/graphify-out/graph.json` with `--graph`. Never run graphify inside a worktree, never with a model pass (memory graphify-map). Confirm answers in code.

### 8.3 Cache warming
- A cache hit reads context at about 0.1x price; a miss re-writes it at about 1.25x (5-min rate; the 1 h write rate is unverified). A cold 225k-token helper costs about 12.5x a warm turn (memory subagent-cache-ttl).
- Default: main conversation 1 h, subagents/workflows/forks 5 min (Claude Code docs, prompt-caching page, checked 2026-10-10). `~/.claude/settings.json` sets `promptCacheTtl = "1h"` and `subagentPromptCacheTtl = "1h"` (env alternative `CLAUDE_CODE_SUBAGENT_PROMPT_CACHE_TTL`, 5m|1h). Keep them.
- Heartbeat rule: a turn that reads the cache refreshes it. Never go about 50 min (4 min on a 5-min cache) without a model turn while work remains. When you end a turn to wait for a job, start a background `sleep 3000` (`sleep 240` on a 5-min cache) and re-arm it each time. The lead's 15-min stale-check timer (`sleep 900`, plain bash) also keeps it warm.
- heavy.sh stops a slot wait after 30 min with exit 75 and "HEAVY: no slot free ...". Re-run the same command at once.
- Long waits belong to the lead. A helper that idles past its TTL re-pays its whole history. The lead runs long tests and gates; helpers get short jobs that do not wait; never message or resume a helper that sat idle.
- Do not restart a running workflow for a small owner clarification (it cost five restarts once). Add it as a constant for later stages, let early agents finish, then stop and resume with the edited script.

### 8.4 Context limits and auto-compact
#### 8.4.1 Today (lead's report; the fix pass of 2026-10-14 read only the model, cache and autoCompact keys of ~/.claude/settings.json)
- Claude Code lead: 400,000-token window, auto-compact at 92% (about 368k). `~/.claude/settings.json`: `model = sonnet`, `autoContinueAtUsageLimit = true`, the two cache TTLs, no `env` block, and `modelSettings.claude-opus-5-5.autoCompactWindow = 400000` (the top-level model is sonnet, which has no entry).
- Codex `~/.codex/config.toml`: `model = "gpt-6.1-sol"`, `model_reasoning_effort = "high"`, no `model_auto_compact_token_limit` (unset = model defaults).

#### 8.4.2 Setting names
- Claude Code: `CLAUDE_AUTOCOMPACT_PCT_OVERRIDE`. Confirmed by search results from code.claude.com (cloud docs; another page says it applies to subagents). Value range: (unverified), expect a percent. Same docs also list env `CLAUDE_CODE_AUTO_COMPACT_WINDOW`, setting `autoCompactWindow` (100000-1000000), `/autocompact <tokens>`, flag `--autocompact`. Interplay with the 400k window: (unverified).
- Codex: `model_auto_compact_token_limit` (number), `model_auto_compact_token_limit_scope` (`total` default | `body_after_prefix`), `model_context_window` (number). Confirmed only in a saved copy of the config reference, E:/Droppy/work/scratch/lead/cli-standby/doc-config-reference.txt (saved 2026-10-06). The live URL developers.openai.com/codex/config-reference redirects (308) to learn.chatgpt.com, off my allowed list. github.com/openai/codex docs/config.md is a stub without the keys. Role layers: `agents.<name>.config_file`.

#### 8.4.3 Recommendations (NOT applied)
Why below 92%: every turn re-reads the whole context, so 368k costs about 30% more per turn than 280k. State lives in files (8.4.5), so a compact loses little.
- Claude Code (lead and helpers share it; the variable is process-wide). Merge into `~/.claude/settings.json`:
```json
{ "env": { "CLAUDE_AUTOCOMPACT_PCT_OVERRIDE": "75" } }
```
About 300k of 400k. The settings file already holds `modelSettings.claude-opus-5-5.autoCompactWindow = 400000`; a top-level `"autoCompactWindow": 300000` may not override that per-model entry (unverified), so the alternative is to change the per-model value to 300000. Never both.
- Helpers: compaction is not their safety net. Keep units small. A helper past about 60% of its window and not done commits, writes a progress note into its report and ends. The lead starts a fresh agent on the same worktree and branch.
- Codex `~/.codex/config.toml`:
```toml
model_auto_compact_token_limit = 280000
model_auto_compact_token_limit_scope = "total"
```
280000 assumes a 400000 window; the real window of gpt-6.1-sol is unknown to me. Use about 70% of it. A lower helper limit can go in an `agents.<name>.config_file`. Whether ChatGPT-app threads read this file: (unverified).

#### 8.4.4 Subagent inheritance
Claude Code workflow agents, helpers and forks run in one process and inherit the environment, so the percent applies to them (cache TTL has its own setting). Codex spawned agents use the same config unless a role layer overrides it. Nobody inherits history: a subagent knows only its prompt and what it reads, so every brief is self-contained.

#### 8.4.5 Keep the handoff current before any compact
- After every finished task, decision, failure or link: `python E:/Droppy/work/claude-verify/handoff_log.py "message"` (dated line in the top block of E:/Droppy/work/NEXT_SESSION_CLAUDE.md).
- `python E:/Droppy/work/claude-verify/handoff_status.py --loop 180` writes E:/Droppy/work/HANDOFF_LIVE_STATUS.md every 3 min. Run it in the background; restart it after a new session or compaction.
- Before a planned compact (context above about 60%) or a limit park, write run ids, branch heads, open decisions and the next step into the handoff. Keep the lead's context small: delegate big reads. After a compact, re-check heads with git.

### 8.5 Models, concurrency, limits
#### 8.5.1 Tiering (owner 2026-10-11, 2026-10-13)
- Opus 5.5: security units and fixes, hard rendering/engine/design bugs, big conflict merges, synthesis, visual/engine builders.
- Sonnet 5.5: normal units, reviews, verify lenses, check-and-land, routine merges, test fixes needing diagnosis, bulk code.
- Haiku 5.5 (owner: a big leap): wait/poll agents, log and test summaries, ledger/ED appends, census rows, traceability label moves, re-record a hash, worktree cleanup, inventories, single-file fixes with a clear recipe.
- Never Fable models (HANDOFF_PROMPT.md). Codex: only GPT-6.1 Sol at High or Extra High.

#### 8.5.2 Concurrency (latest record 2026-10-12)
- RAM is the cap on this 31 GB PC. Keep 5-6 GB free. Check CPU and RAM before a local launch with the read-only RAM command of section 3.6 step 6 (or park-2026-10-11/workflows/stale.ps1 without -Kill). Do not run scratch/lead/cleanup-stale.ps1 or claude-verify/memguard.ps1: they stop processes (cleanup-stale.ps1 uses Stop-Process -Force and also deletes %TEMP%/one-* folders, with no dry run). One full App.Tests testhost takes about 4 GB.
- heavy.sh: 4 slots (slot 1 always, slots 2-3 need 3 GB, slot 4 needs 8 GB; stale slots reclaimed after 60 min; heavy.sh:3, :9). `heavy_acquire` once per run in the same shell, then `heavy_release`. Never inside `( )`, `$( )`, a retry loop or as a probe: a subshell leaves a slot idle.
- 2026-10-04 five local agents drove RAM to 0.1 GB and Claude died. 2026-10-11 the owner said "slow down" (max 2 build workflows); 2026-10-12 "full speed" lifted that. The MEMORY.md index still shows the old line.
- Low free RAM often comes from the kernel non-paged pool (8.3-8.5 GB, cannot be touched) and idle build servers (VBCSCompiler, MSBuild; about 4 GB on 2026-10-13 23:53). When no build or test runs and you need RAM: `dotnet build-server shutdown` (it took available RAM from 1.6 to 4.5 GB then). The App-test shell leak (about 1.3 GB per run) is fixed on claude/test-shell-leak, which is in the base. Check E: and C: space before every gate: hang dumps filled E: on 2026-10-13 (moved to C:/nth-dumps).
- Native real-window tests (`~Native`, native.lock) need focus and a quiet PC: no computer-use or browser typing meanwhile. Rerun native Escape/Return failures alone, twice.

#### 8.5.3 Helpers never idle; time-box Codex units
- Crons fire only when the lead is idle. On each lead turn check the newest commit of every `cloud/*` and `codex/*` branch (`git fetch`, `git log -1`). Refill a free helper at once. If everything looks blocked, split a unit: hand over its service/core part, keep UI/native local.
- Owner 2026-10-14 00:00 ("you've been stalling me"): deadline each Codex unit about 30 min, BLOCKED as fallback. When the gate already passes, steer the unit to commit, run small checks, report; no tuning past the gate. Check the thread in the app (scroll down first), not only the report. "Running" = spinner in the app, never branch times or your own notes. Build an interim installer as soon as the tested head lands.

#### 8.5.4 Usage tracking and limit guards
- Each working lead turn: `mcp__ccd_session_mgmt__get_usage` (Claude desktop tool), log the percent. Format in scratch/lead/usage-log.txt: `HH:MM usage 5h=NN% weekly=NN%`. Lines carry no date: add one. Its last line is old (weekly 24%).
- 5-hour guard (scratch/lead/limit-resume.md, owner 2026-10-12). At 80%: no new launches, helpers commit every step. At 90%, or projected 100% before the next check: PARK. TaskStop every workflow BEFORE the cutoff (an agent killed by the limit can be cached as null and the workflow skips what follows). SendMessage each Agent-tool helper "commit WIP and stop". Run `bash E:/Droppy/work/claude-verify/limit-park.sh` (backup patch + WIP commit per worktree). Log it. Background-sleep until `resetsAt` + 3 min, then resume.
- Weekly: finish in-flight units before new ones. At about 95% helpers commit and stop after the current unit.

#### 8.5.5 Stale check by hand (every 15 min)
The lead looks at CPU, RAM, leftover dotnet/testhost/bash, cut-off workflow steps, free-but-unused heavy slots, hung processes. Stop only what you judged stale, by PID. List with `scratch/lead/park-2026-10-11/workflows/stale.ps1` (no `-Kill`). No auto-killer (the old one stopped real tasks). The ChatGPT app respawns killed Codex MCP helper sets (185 processes, 8 GB on 2026-10-13): when Codex has no live work, close the whole ChatGPT + codex.exe tree after checking no dotnet/testhost/heavy bash runs under it.

#### 8.5.6 Route to Codex at high Claude usage (owner 2026-10-13, at 92%)
"Why are you putting your own usage on the line... why not give it to codex?" When Claude weekly is high, fixes and features go to Codex, even security-side wiring if worded as a plain feature task. Claude does one short review and lands it. Check Codex's card first and keep a reserve for in-flight threads. On 2026-10-12 Codex ran nearly dry: in-flight threads finished, other units went to Sonnet stand-ins with the same brief and report format.

#### 8.5.7 No SendMessage to workflow agents
2026-10-12: a hint to a running workflow agent started a second copy that edited the same worktree. SendMessage is safe only for Agent-tool helpers. To steer a workflow step, change files it reads, or let it finish and rerun that step with an edited prompt. If a duplicate started, tell it to commit its own paths and stop. In a NEW session no old helper can be messaged (NEXT_SESSION_CLAUDE.md:15): start a fresh agent on the same worktree and branch.

#### 8.5.8 Owner messages leak into running agents
Mid-turn owner messages also reach running workflow agents, which obey them (2026-10-11: "stale"/"CronDelete" results were cached as done). A soft "ignore" note failed. What worked is an AUTHORITY block saying: (1) the task is the owner's own order, (2) the named earlier request is already done, (3) call no cron, schedule or SendMessage tools. Relaunch in a turn with no pending owner message. After an owner message mid-turn, scan workflow journals for "stale", "CronDelete" or the owner's words and rerun only those agents with the guard added. Workflow scripts written by Python text mode get CRLF and the launcher refuses them: use binary mode or the Edit tool.

#### 8.5.9 OpenCode Fledge (free side-job model; alpha)
Memory opencode-fledge-free. The OpenCode desktop app bundles a CLI: `%LOCALAPPDATA%/Programs/@opencodedesktop/resources/opencode-cli.exe run -m opencode/fledge-alpha-free "<prompt>"` works with no key or sign-in (tested 2026-10-05, about 3 s). Run it from the scratchpad, not from a repo (it starts a "build" agent in the working directory). Use it for reviews and side jobs only; port code only on trial and never merge its decisions. Free tiers may keep prompts (the owner said OK for private repo code on 2026-10-05). Write work goes in its own worktree under E:/Droppy/wt/fl/*, tests only via one*.sh, it never pushes; the lead reviews and pushes to claude/fledge-*.

### 8.6 How Codex is driven on this PC
#### 8.6.1 Method (memory codex-out-lead-does-all)
- Drive threads in the ChatGPT desktop app by computer use so the owner can watch. Never `codex exec` in the background (Windows sandbox fails: "apply deny-read ACLs").
- New thread: project "Marrow Codex local" (2026-10-07 name, unverified now), Work in "This computer", Full access, gear "New worktree" OFF, model GPT-6.1 Sol High (or Extra High).
- A new chat can default to "Cloud"; cloud threads cannot reach E:/. Click the chip under the composer, pick "This computer", confirm. A cloud thread shows a cloud icon after its title.
- Never click "Use reset" in Settings. "Allow Codex to use resets" is on. Tell each thread it may use an available reset itself when its limit hits (owner 2026-10-08).
- The thread message points at a brief in E:/Droppy/work/scratch/lead/codex-tasks/ (85 files, e.g. `CU-GATE-FIX4.md`). Exact message wording: (unverified). Local Codex jobs count toward the RAM floor; cloud jobs do not.

#### 8.6.2 Rules a Codex thread reads
`codex-tasks/COMMON.md`, `CODEX-LOCAL.md`, `FEATURE-RULES.md`, `LEAN-TESTS.md` (the last three in scratch/lead/). Key points: worktree E:/Droppy/wt/cx/<name> on `codex/<slug>`; commit there; push only that branch; never edit wt/cl/*; earlier work in the worktree is progress to keep; never open the app, run an installer or `package-windows.ps1`, sign, publish, write to reference/, use `git stash` or `git add -A`, or make a real network or CLI call in a test; builds/tests only via a heavy slot; red alone twice before the fix; ED ids `ED-NEW-<unit>-<n>`.

#### 8.6.3 The report contract
A unit ends with a report file (path in the brief, e.g. E:/Droppy/work/scratch/handoff/codex-GATE-FIX4-report.md) and the same text in chat:
1. `New head: <full sha>`.
2. What was fixed (cause, fix, which side was right, ED ids).
3. Counts: run, passed, failed.
4. Each remaining failure and whether it fails alone twice.
5. Verdict READY or NOT READY.
6. Last line `DONE-<id>` or `BLOCKED-<id>` with the exact reason, only in the final report.

The lead accepts a unit from its report. A missing `mark.mjs` queue row for a `CU-*` unit is expected, not a block. A waiter writes `codex-ready/<id>.ready`, then a check-and-land agent verifies and lands the unit.

#### 8.6.4 A non-Claude LLM runs the same method
1. Read E:/Droppy/work/HANDOFF_NEXT_RELEASE.md (this file), then the dossier of your item.
2. Claude-only tools (Workflow, Agent, SendMessage, get_usage) have no twin. Spawn sub-agents or threads of your own platform with the template in section 8.8. Read usage from your own platform.
3. Same worktree layout, heavy-slot scripts, `one-*.sh` scripts and report contract. Invent no other paths.
4. Same handoff log (`handoff_log.py`) after every finished task, failure or decision.
5. Same safety lines: section 10.
6. Reply to the owner in short plain English.

### 8.7 Working with the owner
1. English only, even when the owner writes Hindi or Urdu. Understand it, answer in plain short English.
2. No unasked updates (2026-10-12 23:45). Progress tables only when asked. On notifications and landings: do the work, log it, end with one short line. Still raise real problems. A table compares only with the last table the owner asked for (newest in scratch/lead/progress-snapshots/: 2026-10-13-1745.md), columns "last table / now / change"; save a new snapshot.
3. Decide, don't drag (2026-10-07). Decide technical questions with your recommendation, record it (OD if it touches an owner rule, else ED), go on. Bring only true design/visual picks, or spending money, sending data out, changing a standing rule: one batched question, recommended option first. Never loosen tests on your own.
4. Quality bar: Apple-grade, at least the Mac app. Zero stutter, no overlaps, no static swaps, no movement the Mac lacks. Every change transitioned, never cut (OD-0076). Prove motion per frame, not by still. Compare with E:/Droppy/work/reference-video/ at FULL frame rate. 2026-10-13: smooth like a Mac app, light on hardware, no hangs. 2026-10-14: keep Avalonia, fix the Windows way, no hiccups. Check frames yourself before stating how a reference behaves. Motion: one continuous animation through shared hub poses, no fades between moves, designed start and end. Also (owner 2026-10-11, assistant task rows): no satellite balls or dots orbiting an icon, no stretched rings, nothing clipped by a row, card or panel edge; a working sign stays calm and inside its own bounds; several rows animating at once must still read calm. Sample position and opacity per frame before showing the owner; the owner pushed back twice when a claim about a reference was not verified.
5. The owner reviews on the final installer (2026-10-12: "Just push it man"). Do not block on test copies or the owner's eye. List what the owner should look at in the release report. "Push" meant "go ahead", not git push.
6. Naming: like a big company launching a flagship: one short word for the promise, no lore. At most 3 options, each as "Introducing X" with its UI reading, recommend one. The owner likes short mystic Latin-feel concrete nouns. Locked: Ignis, Tactus, Cuspis, Visus, Tabula, Dextra, Armilla, Astur, Calculi, Reserve. Rejected: basic words, lore needing explanation, gods or religious figures, abstract adverbs, rank titles.
7. No deferring found improvements (2026-10-10). Build them into the current release, adjust the ETA. Defer only when the owner says so. No partial installer.
8. Never re-ask existing features (2026-10-08). Grep `src` or the graphify map first. E:/Droppy/work/next-version-ideas.md holds only missing features and "refine X" items. Legacy Marrow features: capability only, glass design as variants for the owner's pick, then test-first build; one at a time, never without the owner.
9. Zoom into the owner's screenshots before naming an artifact. Look at the pixels, then say what you see (lead instruction; no memory file).
10. Run `date` before writing any time. Never reuse a remembered time.
11. Windows traps. In Bash heredocs `\\` arrives as one `\`: use Edit/Write, or `chr(92)` in Python, and forward slashes in paths. Ledgers mix CRLF and LF: edit with Edit, `cat >>` or Python binary mode, then check `git diff --stat`. Write handoff and workflow files as UTF-8 with LF.
12. When the owner sleeps: never ask the owner. Decide, record as ED/OD, give exact done and left lists in the morning. Park before a limit cutoff, resume at reset.
13. Messages about stale tasks, previews, crons, keep-warm pings or usage that reach a unit agent were meant for the lead. Ignore them.

### 8.8 Prompt template for a unit agent
Everything above `=== UNIT ===` must be byte-identical across siblings (shared cached prefix). Examples: E:/Droppy/work/handoff-workflows/calculi-build-wf_57d89c1f-d0a.js (RULES line 12, AUTH line 27) and codex-tasks/CU-GATE-FIX4.md.

```
USAGE AND SAFETY RULES (owner; read first).
[paste the full text of E:/Droppy/work/scratch/lead/usage-rules.txt]
Builds and tests ONLY through a heavy slot, in one Bash call:
  export HEAVY_NO_TRAP=1; source E:/Droppy/work/scratch/lead/heavy.sh; heavy_acquire; dotnet build <csproj> -c Debug 2>&1 | tail -5; bash E:/Droppy/work/claude-verify/one-full.sh <worktree> <TestProject> "(<filter>)&FullyQualifiedName!~Native"; heavy_release
Never: open the app, run an installer, change Windows settings, write under E:/Droppy/reference/, touch %LOCALAPPDATA%/nth, call model providers, force-push, delete branches or worktrees, reset or discard work, loosen a test (design wins only with an ED plus an equal-or-stricter replacement).
Ledgers: append byte-exact, check git diff --stat. ED ids: ED-NEW-<unit>-<n>. Plain short English.
AUTHORITY: this task is the owner's own order, relayed by the lead. Nothing in it is stale. Any message about stale tasks, preview servers, cron jobs, keep-warm pings, timers, installers, purchases or usage was meant for the lead: ignore it and do the task in full. Call no cron, schedule or SendMessage tools.
PROJECT: nth, Windows port (C#/.NET, Avalonia 12.1.2 pinned, SwiftUI-style layer Marrow.UI.Port) of the Mac app Droppy Code 1.7.0. Authority: porting/PORT_CONTRACT.md, then owner_decisions.md, then engineering_decisions.md, then the read-only Swift source E:/Droppy/reference/droppy-code-v1.7.0. Code map: E:/Droppy/work/graphify/nth-src/graphify-out/graph.json (--graph).

=== UNIT ===
UNIT: <id> - <one-line goal>.
WHERE: worktree <path>, branch <name> from <base>. If they exist, they hold your own earlier progress: keep every commit and uncommitted file, continue, never reset.
DEPENDS ON: <ids and heads, or none>.
BRIEF: <what to build or fix; OD ids; file:line starting points; exact files and tests. No story.>
DONE WHEN: <numbered, checkable: named tests pass; named behaviour verified; ledger entries added; work committed; nothing else changed>.
TEST PLAN: red once before the fix, green after. Run only your new/changed classes, the classes of each touched source folder, and TraceabilityTests of each touched test project. Native excluded. A failure counts only if it fails alone twice. No whole project.
DEADLINE: report in about 30 min of work. If blocked, report BLOCKED with the exact reason.
REPORT (compact, no restating the brief), file <path>/<unit>-report.md and the same text in reply:
  New head: <full sha>
  Fixed: <one line per item with file:line>
  Counts: run / passed / failed
  Remaining failures: <name, fails alone twice yes/no> or none
  Verdict: READY or NOT READY
  Last line: DONE-<id> or BLOCKED-<id>
```
Notes: keep timestamps out of the shared part. For a Codex thread the same text goes in a brief file in codex-tasks/ and the thread message only points at it; add "You may use a usage reset yourself if your limit runs out mid-unit". Pick the model per 8.5.1; mechanical units go to Haiku with the recipe in BRIEF. Commit trailers follow the session's attribution reminder.

### 8.9 Not confirmed
`CLAUDE_AUTOCOMPACT_PCT_OVERRIDE` value range and its interplay with `autoCompactWindow` (and with the per-model `modelSettings.claude-opus-5-5.autoCompactWindow = 400000`); the real window of gpt-6.1-sol; whether ChatGPT-app threads read `~/.codex/config.toml`; today's Codex usage; the 1 h cache write multiplier; current ChatGPT-app names and the exact thread message wording. Codex key names come from the saved 2026-10-06 copy, not a live page.

## 9. Graphify code map

Sources: E:/Droppy/work/graphify/README.md and the memory file C:/Users/omera/.claude/projects/E--Droppy/memory/graphify-map.md (owner approved the map on 2026-10-14).

### 9.1 What it is

A code-only map of the nth source that AI coding tools can query instead of searching files. It is an index, not a diagnostic: it shows where code is and how it links, it does not say what is wrong. It does not change the app. It was built from the shipped 1.0.0 tree E:/Droppy/wt/cl/pkg-morning/src (commit 87eb11490; origin/claude/release-integration 9e083470c has the same code).

Size: 3,038 files, 79,077 nodes (code items), 191,594 edges (links), 1,340 groups (communities). Each group is named after its most connected class, by tools/hub_labels.py with no model.

### 9.2 Where

| What | Path |
|---|---|
| The map (about 137 MB) | E:/Droppy/work/graphify/nth-src/graphify-out/graph.json |
| Network page (groups as dots, 2.6 MB) | E:/Droppy/work/graphify/nth-src/graphify-out/graph.html |
| Tree page (project, folder, class, method; 3.3 MB) | E:/Droppy/work/graphify/nth-src/graphify-out/GRAPH_TREE.html |
| Summary of the most connected parts | E:/Droppy/work/graphify/nth-src/graphify-out/GRAPH_REPORT.md (group names there still read "Community N") |
| Group names | nth-src/graphify-out/hub-labels.json |
| Rebuild script, rebuild log | E:/Droppy/work/graphify/rebuild.sh, rebuild-history.txt (build-log.txt and report-log.txt hold the last build's logs) |
| Preview server | name "graphify-map", port 8765, defined in E:/Droppy/.claude/launch.json (python -m http.server 8765 --bind 127.0.0.1 --directory E:/Droppy/work/graphify/nth-src/graphify-out); start it with the Browser tool preview_start. Start only graphify-map: the older entry tactus-page in the same file also uses port 8765 and serves another folder. The pages load a script library from the internet. At 2026-10-14 16:07 port 8765 was not listening. |

### 9.3 Example commands

Always pass `--graph`. There is no graphify-out folder in any repository. Each lookup takes 2 to 4 seconds. Run them with the model keys removed:

```bash
G=E:/Droppy/work/graphify/nth-src/graphify-out/graph.json
E="env -u GEMINI_API_KEY -u ANTHROPIC_BASE_URL -u ANTHROPIC_API_KEY -u OPENAI_API_KEY GRAPHIFY_QUERY_LOG_DISABLE=1"
$E graphify query "where is the caption hover cleared" --graph $G
$E graphify explain "Marrow.UI/Chrome/CaptionButton.cs::CaptionButton" --graph $G
$E graphify path "ThreadRuntime" "TimelineRows" --graph $G
$E graphify affected "GlassMaterial" --graph $G
```

Use it to find the files behind a row or a speed item before reading code (for example the bare class names in section 5.4). Give a Codex unit the map path when it saves the unit searching.

### 9.4 Rules

1. Never run `/graphify .` or `graphify extract` inside a repository or worktree: it writes graphify-out/ into the tree. A local git exclude (E:/Droppy/port/.git/info/exclude) keeps a stray one from being committed.
2. Never run a model pass: no `--backend`, no non-code mode, no `label`. GEMINI_API_KEY is set on this PC, so graphify could otherwise send text to Gemini by itself. Unset GEMINI_API_KEY, ANTHROPIC_BASE_URL, ANTHROPIC_API_KEY and OPENAI_API_KEY for every run (rebuild.sh does).
3. Never run `cluster-only` without `--no-label`. With no key set, graphify's group naming falls back to the Claude Code command line, which would spend Claude usage and send code names to a model.
4. Rebuild after merges so the map is never stale (section 9.6).
5. Confirm every answer against the code. The map can be older than the branch you are on.
6. Graphify is for navigation only. The speed plan does not need it.

### 9.5 Limits

- 16 files use C# syntax the bundled parser does not know, so they are only partly mapped. The first five: AppModel+HydraMerge.cs, AppSettings.cs, ThreadRuntimeAccountAdoption.cs, SubagentPanel.cs, EffortSlider.cs. The full list is in build-log.txt. Search those files directly.
- Partial classes are spread over several files; check the sibling files of any class you read.
- Group names are a class name, not a written summary (that would need a model).
- The bundled web libraries in Marrow.Services/Assets/lib (mermaid, katex) are mapped too and appear as large groups; they are not nth code.
- The network page shows groups, not single classes. For a single class use the tree page or `graphify explain`.

### 9.6 Rebuild

`bash E:/Droppy/work/graphify/rebuild.sh <src dir of the branch>` (default: the shipped 1.0.0 tree). It takes about 2 minutes, runs code-only with the model keys removed, writes only under E:/Droppy/work/graphify (nth-src/ plus the logs build-log.txt and report-log.txt and the line it appends to rebuild-history.txt, directly in that folder). Run it after merges, from a checkout of the branch (read only), when no heavy run is using the CPU.

The tool is graphifyy 0.9.72, pinned on purpose (installed with `uv tool install graphifyy==0.9.72 --python 3.12 --exclude-newer 2026-09-30`; its wheel hash was checked, no telemetry, query log off). The skill is installed for Claude Code (C:/Users/omera/.claude/skills/graphify/) and for Codex (C:/Users/omera/.codex/skills/graphify/). The graphify block in C:/Users/omera/.claude/CLAUDE.md registers `/graphify` for Claude; there are no git hooks and no Codex config change.

## 10. Safety and standing rules

These are the owner's standing rules. They apply to every agent and every model, and they bind this file's checklist. When a rule and a task clash, the rule wins; ask the owner. Other rules live in the sections named in the last list of this section.

### 10.1 The full list

1. Installers. Never run an installer on this PC (msiexec counts). `package-windows.ps1` runs only with `-SkipUiSmoke`. Never run Setup.exe or vpk or Velopack pack except through the package script when packaging is ordered.
2. Windows. Never change Windows settings or security settings. The "Open at sign-in" and "Desktop shortcut" toggles are deliberately not tested.
3. Reference. Never write under E:/Droppy/reference/ (reading is fine).
4. Model providers. Never call model providers from code or tests (they cost money). Tests use fakes and seams that fail closed. A real send happens only on the owner's explicit word.
5. Git. Never force-push. Never `git stash`. Never `git add -A` (add files by name). Keep discarded branches.
6. Tests. Run tests only through the wrappers and heavy slots (section 7). A failure counts only if it fails alone twice. Never loosen, skip or delete a test.
7. Deletion. Permanent deletion needs the owner's confirmation of the list. Move instead of deleting.
8. The installed app and its data. Never modify %LOCALAPPDATA%/nth, the installed app or the legacy Marrow app. Read-only checks are fine. Settings toggles in the real app are flipped and restored only on the owner's explicit order.
9. Native tests. Run them only in the lead's quiet window, with no browser or computer-use typing at the same time.
10. Credentials. Never enter real credentials or card numbers. Never zoom into, print or copy API keys or tokens. Reading the owner's account files must never print tokens.
11. Stale processes. No auto-kill scripts. Find stale processes, then stop them by hand.
12. Owner's apps. Never request computer-use access to the Claude app itself. Never touch the owner's apps (Chrome, Explorer windows) beyond need.
13. Real app inspections. Do not click Download, Update, Connect, Install, Clear data, Import, or account and provider sign-in switches in the real app.
14. Downloads. Scan new downloads by hand, keep each in its own folder, and never run one without the owner's approval.
15. Publishing. Read every file fully before publishing it as an artifact. Do not apply for OpenAI Daybreak (source: log:98 only, unverified as an owner rule). Avoid security wording in Codex prompts.
16. Codex. Check the Codex usage card before new Codex work. Never click "Use reset" in Codex settings. Codex threads must be "This computer".
17. Parked. Nothing new starts without the owner's approval while parked.
18. Unit agents never open the app (nth / Marrow), never run an installer, sign, package or publish (FEATURE-RULES.md:6; CODEX-LOCAL.md:10). To see UI without the owner's app: a dev build with MARROW_DEV_DATA_ROOT or MARROW_PORT_DATA_DIR on a copied profile, or the native gallery tests (tests/Marrow.App.Tests/Fixtures/GalleryRun.cs, env MARROW_GALLERY). Only the lead drives a real window, in the quiet window, with the owner's grant.

### 10.2 Where the other rules are

- Token and cache rules, models, concurrency, usage guards, how the owner likes to be answered: section 8.
- Lean testing, heavy slots, code rules, ledger bytes, review and merge, gate, packaging: section 7.
- The speed plan's "do not" list: section 6.6. Lessons from the history: section 2.6.
- The text of the lead's own safety prompts for unit agents: the prompt template in section 8.8.

## 11. How to keep this file current

This file is the plan. If it is wrong, the next model starts wrong. Update it in the same turn that the fact changes.

1. Check the clock first. Run `date` before you write any time or date. Never reuse a remembered time.
2. Tick boxes with proof. Change `- [ ]` to `- [x]` and append `Done: <short SHA> <YYYY-MM-DD>` to the same line (for an owner decision, the owner's words and the date). Never tick without a commit SHA. Tick a row in section 5, a speed item in section 6 and an NR item in section 4 separately. An area box in section 4.5 is ticked only when all its rows are ticked.
3. Log every finished task, failure, decision or link with a dated line:
   `python E:/Droppy/work/claude-verify/handoff_log.py "what finished, numbers, commit"`
   It appends to the log of the top block in E:/Droppy/work/NEXT_SESSION_CLAUDE.md. The live status file E:/Droppy/work/HANDOFF_LIVE_STATUS.md is written every 3 minutes by `python E:/Droppy/work/claude-verify/handoff_status.py --loop 180` (stopped now; start it in the background when work resumes, and restart it after a new session or a compact).
4. Update section 3 whenever state changes: a merge, a new head, a new installer, a new worktree, a parked or resumed lane, the usage figures. Re-run the checks of section 3.6 and replace the expected values. Keep the line "Written ... about HH:MM" honest.
5. When a new owner test list arrives, give it a new file in E:/Droppy/work/scratch/lead/ and add its rows to section 5 in the same format. Never edit a row's meaning; add a note.
6. When the plan changes (the owner re-orders, drops or adds work), edit section 4 and say why in one line under the item. Never delete a ticked item.
7. When a release is built, add it to section 2.4 (timeline and installers) and move finished items out of section 3.5.
8. Before a context compact or a usage park, write run ids, branch heads, open decisions and the next step into this file and the handoff log (section 8.4.5, 8.5.4).
9. Files you write: UTF-8, LF line endings, ASCII where possible. Python text mode on Windows writes CRLF; use `newline='\n'` or the Write tool. The porting ledgers are the exception: they are CRLF and are edited byte-exact (section 7.5).
10. Keep one copy of each fact. If a section needs a fact that another section owns, point to it (for example "section 10") instead of copying.
11. If you find a wrong line in this file, fix it and add the fix to the log. Mark what you could not check "(unverified)".
12. Entry point (not done by the writer of this file: only the lead may edit the files below). Put this banner as line 1 of both E:/Droppy/work/HANDOFF_PROMPT.md and E:/Droppy/work/NEXT_SESSION_CLAUDE.md: "READ E:/Droppy/work/HANDOFF_NEXT_RELEASE.md FIRST. Work is parked. Do not restart any workflow listed below." Update the memory file C:/Users/omera/.claude/projects/E--Droppy/memory/handoff-file.md the same way ("2026-10-14: READ E:/Droppy/work/HANDOFF_NEXT_RELEASE.md FIRST. Work is parked. Do not restart any workflow listed below."), so that the owner's words "read the handoff file" land here. Today that memory file and HANDOFF_PROMPT.md line 1 still send a new session to the 2026-10-12 LATEST block and its section 0.

### 11.1 Revision log of this file

- 2026-10-14 (created, 16:29): assembled by a helper agent from drafts s1 to s6 (section 12), on the lead's order. The drafters' notes are in the draft files. Open questions of the drafters are listed in section 12.2.
- 2026-10-14 (fix pass, 17:00): a reviewer's list of 69 issues (4 of them duplicates) was checked against the files and git, then applied by the same helper. Added: row #86 (Ignis gaps; now 86 rows), the ENTRY POINT note (1.4) and the base-branch line (1.3 item 6), the unshipped f3-chats / T43 / f8 work (3.3, 3.5 item 12, NR1.8), item L1b for the wallpaper galleries and composer styles (40 speed items), a size tag on each speed item, the owed native tests, safer package and installer steps (a new worktree, -SkipUiSmoke always, the 1.0.0 installer is not moved), and many smaller corrections of numbers and wording. Corrected while applying: f3-chats is 3 commits not in 87eb11490 but starts 1,379 commits behind it; the list file has 115 lines (wc -l prints 114); the L1 size is unverified; PersonaPlex was advised against, not rejected, and the owner's scope pick is not on record. The table of contents line numbers were regenerated.

## 12. Source index

Every path below was seen to exist on 2026-10-14 (read-only check) unless marked. PK = E:/Droppy/wt/cl/pkg-morning (the shipped 1.0.0 tree). SL = E:/Droppy/work/scratch/lead. CV = E:/Droppy/work/claude-verify. MEM = C:/Users/omera/.claude/projects/E--Droppy/memory.

### 12.1 Files and what they hold

| Path | What it holds | Used in |
|---|---|---|
| E:/Droppy/work/handoff/next-release/s1-app-history.md to s6-agents-tokens.md | The six drafts this file was assembled from | all |
| SL/owner-test-2026-10-14.md | The owner's 87 findings (rows #1 to #87), the owner's words and evidence | 5 |
| SL/speed-plan-2026-10-14.md | The speed plan: phases, change IDs, picks 1 to 9 | 6 |
| SL/speed-plan-findings-2026-10-14.md | 111 findings R1.1 to R8.13 with file:line | 6 |
| SL/avalonia-speed-research-2026-10-14.md | Research into smooth Avalonia apps and the engine | 6 |
| SL/t3-vs-nth-2026-10-14.md | Comparison with T3 Code | 5, 6 |
| SL/native-smooth-light.md | Three by-eye native cases still owed | 3.5, 4 |
| SL/owner-testing/ | 2026-10-08 owner findings F1-F6 (findings.md), morning-report.md, videos, composer centre-line and caption-overlap screenshots (f7/), and the 2026-10-14 Ignis reference video (ignis-video-2026-10-14/) | 2, 5 |
| SL/merge-plan-2026-10-11.md | Merge plan (ends 2026-10-12) | 3, 2 |
| E:/Droppy/work/NEXT_SESSION_CLAUDE.md | Older handoff; LATEST block lines 1 to 476 with the log from line 48 (1697 lines at 16:34); history after "# PREVIOUS" (line 477) | 3 |
| E:/Droppy/work/HANDOFF_LIVE_STATUS.md | Auto status file (writer stopped 15:04) | 3, 11 |
| E:/Droppy/work/HANDOFF_PROMPT.md, HANDOFF_CLAUDE_20X_2026-10-09.md, HANDOFF_CLAUDE_PRO_2026-10-06.md | Earlier handoff prompt and full port histories | 2, 8 |
| E:/Droppy/work/handoff/*.md (calculi-and-native-runner, dextra-assistant, glass-and-tactus, installer, lean-pass, merge-gate-release, session-hotkeys, web-visus-tabula) | Dossiers per feature area (dated 2026-10-12 about 02:20) | 2, 3 |
| E:/Droppy/work/design/published/INDEX.md | Index of the saved design pages: 50 earlier pages (section 3), 34 republished on account 2 (section 7), 8 more saved 2026-10-14 16:06 (section 9), so all 54 artifacts of account 2 are on disk; MANIFEST-2026-10-14.sha256 | 2 |
| E:/Droppy/work/design/assistant/DESIGN.md | Dextra design, rev 5 | 2 |
| E:/Droppy/work/next-version-ideas.md | Missing features and "refine X" ideas only | 8 |
| E:/Droppy/work/legacy-marrow-inventory.md | Inventory of the older Electron "Marrow" app | 2 |
| E:/Droppy/work/flakes.md | Flaky test list | 2 |
| E:/Droppy/work/cleanup-2026-10-10.txt | Worktree clean-up list (WILL REMOVE / KEEPING) | 3.5 |
| PK/porting/owner_decisions.md, engineering_decisions.md | ODs (owner words, binding) and EDs | all |
| PK/porting/PORT_CONTRACT.md, contract_amendments.md, STATE.md | Contract (never edited), amendments, state and incomplete list | 2, 7 |
| PK/porting/parity_matrix.json, ui_contract.json, platform_adaptations.md, IMPLEMENTING.md, adr/ | Requirements, Mac-to-Windows replacements, conventions, decisions 0001 and 0002 | 2 |
| E:/Droppy/port/porting/MASTERPLAN.md, PROGRESS.md | Master plan and progress of the port | 2 |
| PK/AGENTS.md, CLAUDE.md | Repo rules (still say "Marrow"; trust OD-0135) | 2 |
| PK/CHANGELOG.md, Directory.Packages.props, scripts/package-windows.ps1, scripts/release-toolchain.json, src/Marrow.App/AssemblyInfo.cs | Release notes, pinned package versions, package script, toolchain pins, version lines | 2, 7 |
| SL/FEATURE-RULES.md, CODEX-LOCAL.md, LEAN-TESTS.md, usage-rules.txt | Rules for Claude units, Codex units, lean testing, token rules | 7, 8 |
| SL/codex-tasks/ (COMMON.md, CU-KOKORO.md, CU-GATE-FIX4.md, T40-f3-chats.md, T42-f8-chip-contrast.md, T43-storage-location.md; 85 files in all) | Codex unit briefs | 8 |
| SL/heavy.sh, rerun-alone.sh, gate-par.sh, gated-build.sh, views-r3.sh, ed_merge.py, dextra-build-2026-10-12.js | Heavy slots, rerun reds alone, gate scripts, build-from-clean script, ledger merge helper, land-lock pattern | 7 |
| SL/limit-resume.md, usage-log.txt, progress-snapshots/ | 5-hour guard rules, usage log (no dates), owner progress snapshots | 8 |
| SL/park-2026-10-11/workflows/stale.ps1 | Lists stale processes (never use -Kill without reading) | 7, 8 |
| SL/cli-standby/doc-config-reference.txt | Saved Codex config reference (2026-10-06) | 8.4 |
| CV/one.sh, one-full.sh, one-names.sh, one-names-long.sh, gate-seq3.sh, handoff_log.py, handoff_status.py, limit-park.sh, memguard.ps1, workflows/feature-unit.js | Test wrappers, the gate, handoff tools, park script, RAM guard, unit workflow | 7, 8, 11 |
| E:/Droppy/work/lean-pass/native/run-native.sh, dryrun.log | Native runner with total wait cap; log that shows the 239 Hz display | 6, 7 |
| E:/Droppy/work/lean-pass/AUDIT.md, RELEASE_PLAN.md | Lean pass audit and release plan | 2 |
| E:/Droppy/work/scratch/handoff/security-gate-final.md | Final security gate: 0 blockers, leftovers S6, N1, N8, N10 | 3.5 |
| E:/Droppy/work/scratch/handoff/codex-KOKORO-report.md, codex-C1-web-review.md | Kokoro unit report; Codex review C1 of the web surfaces | 3 |
| E:/Droppy/Installer/BUILD-NOTE.txt, nth-Setup-1.0.0-UNSIGNED.exe | The 1.0.0 build note and installer | 3 |
| E:/Droppy/reference/droppy-code-v1.7.0/ | Pinned Mac source (read-only) | 1, 2 |
| E:/Droppy/work/reference-video/ (INDEX.md, CATALOG.md, study/, frames/, frames.py) | Reference videos of the Mac app; a frame is a source fact | 1, 6 |
| E:/Droppy/work/handoff-workflows/ | Copies of workflow scripts | 2, 8 |
| E:/Droppy/work/graphify/ (README.md, rebuild.sh, nth-src/graphify-out/) | Code map | 9 |
| MEM/MEMORY.md and the 32 memory files (for example graphify-map.md, model-tiering.md, codex-out-lead-does-all.md) | The owner's standing preferences, one fact per file | 8, 10 |
| E:/Droppy/.claude/launch.json | Preview server "graphify-map" (port 8765) | 9 |
| C:/Users/omera/.claude/settings.json, C:/Users/omera/.codex/config.toml | Claude Code and Codex settings (not opened by the drafters; the lead reported their content) | 8.4 |

### 12.2 What the drafters could not confirm (carried over)

- History (section 2.8): UI and App test totals at 87eb11490; the OD range behind Strike and Jewel; how much of the 27 to 34 s first launch is the app; how the wallpaper fidelity hold was closed; the cause of row #52.
- State (section 3.5): why perf-render was dropped; whether the C1/C1b findings were fixed another way; whether the S6 ED exists; whether any native test ran after the gates; where the rel-land, web-land and native lock folders live. No release was published to the feed (no log line says so). The drafter of section 3 made one `git ls-remote` call (read-only) to confirm the origin head 9e083470c.
- Owner rows (section 5): row line numbers may differ by 1 to 5 lines in the shipped tree; which DetailView is in the #36 stack; no other row fact was checked against code.
- Speed plan (section 6): the gate sub-state letters of S2; the "one engine" idea comes from the lead's brief and is not in a plan file; about a dozen file:line refs were re-checked, the rest are carried from the research.
- Testing (section 7): cold build time; whether VelopackStartupHookTests still fails at the new base; the command-line length limit for a long filter; one-trace.sh beyond its header. There is no single accepted-failures file (section 7.6.2).
- Agents (section 8): the value range of CLAUDE_AUTOCOMPACT_PCT_OVERRIDE; the real window of gpt-6.1-sol; whether ChatGPT-app threads read ~/.codex/config.toml; today's Codex usage; the 1 hour cache write multiplier; current ChatGPT-app names and thread message wording.
- Assembly: rerun-alone.sh is in SL/, not in CV as draft s5 said; fixed in section 7.2.3. Draft s6's 24 percent Codex figure of 2026-10-14 00:23 was added to section 8.1 from draft s2.
- Fix pass 2026-10-14 (review issues applied after assembly): the size of L1 (the plan appendix row has no size cell) and the extent of the L1b composer-style work; whether a top-level autoCompactWindow overrides the per-model entry; the hue-check state of f8-contrast since 2026-10-09; whether any owed native test (section 3.5 item 3) ran after 2026-10-13.
