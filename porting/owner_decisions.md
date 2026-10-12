# Owner decisions

Decisions the commissioning owner made that could not be resolved from the pinned source.
Each is binding, dated, and linked to the unknown it closes. Only a later explicit owner
instruction may change one.

---

## OD-0001 macOS reference access: none

- **Asked:** 2026-09-19. Can reference captures or a build of pinned commit `dd2b86e` be provided?
- **Answer:** "No macOS access at all."
- **Closes:** UNK-0003 as BLOCKED_ON_ACCESS, permanently for this engagement.
- **Binding consequence, stated plainly:** per contract section 10, macOS-delegated rendering cannot
  be verified from here. Every such property ships as **UNVERIFIED** and is named individually in the
  final report. No screenshot comparison against a manufactured reference will be produced, and the
  words "pixel-perfect" will not be used.
- **Mitigation found after the answer (see OD-0003):** the pinned tree itself contains real macOS
  renderings of the app, which materially improves what can be checked even without a Mac.

## OD-0002 Product name: Marrow

- **Asked:** 2026-09-19. What name and marks does the Windows build ship under?
- **Answer:** "We'll name it Marrow."
- **Closes:** UNK-0002.
- **What this means, precisely:**
  - The Windows build ships as **Marrow**, not as Droppy Code. Product name, bundle/app identity,
    installer identity and app icon follow the Marrow identity.
  - This is an **owner-approved deviation** from exact parity, limited to product identity. Per
    contract section 18 the final acceptance statement is therefore at best
    "VERIFIED WITH USER-APPROVED DEVIATIONS", never "VERIFIED EXACT PARITY", and this deviation is
    listed explicitly. It is not mislabeled as parity (contract section 6).
  - Everything that is not identity stays exact-parity to the pinned source. Renaming the product
    does **not** authorize adopting the separate existing Marrow product's feature set. That project
    is pinned to an older upstream commit and carries many invented features; contract section 2
    still forbids all of them here. See `porting/instruction_conflicts.md` C-05 and C-06.
  - AGPLv3 section 7 obligations are unaffected and stay intact: the attribution to Droppy Code by
    Jordy Spruit, the origin notice, `LICENSE`, `THIRD_PARTY_NOTICES.md` and `TRADEMARK.md` continue
    to ship, because `project.yml` puts all three in the app bundle's resources.
  - Every user-visible occurrence of the product name inside the app becomes a rename point and must
    be enumerated in the UI contract, so that none is missed and none is changed beyond the name.
- **Still open under this decision:** the app icon artwork for Marrow, and whether the in-repo
  Droppy marks (`droppy-logo`, `AppIcon.icon` assets, the `drop`/`drop.fill` symbol usage) are
  replaced or removed. Raised as UNK-0008.

## OD-0003 Liquid Glass: exact look, zero guesswork

- **Asked:** 2026-09-19. How should Liquid Glass be weighed in the Windows stack choice?
- **Answer:** "I want the exact same look as the droppy operating on MacOS, zero guesswork."
- **Effect on the stack evaluation:** visual fidelity is now the **primary** weighted criterion in the
  architecture decision record, above familiarity, developer convenience and delivery speed. A stack
  that cannot render a custom material with backdrop sampling, blur, specular edge treatment and
  per-frame interactive response is disqualified regardless of its other merits.
- **The honest tension, recorded rather than smoothed over:** "zero guesswork" and "no macOS access"
  pull against each other. Liquid Glass is rendered by macOS; its appearance is not a value in this
  repository. Contract section 10 is explicit that without a matching reference, source-based UI work
  may proceed but visual parity stays UNVERIFIED, and that a pixel-perfect claim from source
  inspection alone is forbidden. This port will therefore not claim the material is exact. It will
  make it as close as the available evidence supports, and say exactly what the evidence was.
- **What removes guesswork, and what does not:**
  - SOURCE FACT, zero guesswork, fully reproducible: the shape, corner radius, style
    (`.continuous`), tint color, tint opacity, `.interactive()` flag and `GlassEffectContainer`
    spacing at all 22 glass sites; every theme hex; every spacing, padding, size and duration in the
    Swift source. These are extracted verbatim and are not approximations.
  - REAL REFERENCE EVIDENCE, newly found: eight full-window macOS screenshots of the running app are
    tracked in the pinned tree (see below). They show Liquid Glass as actually rendered, over the
    app's own wallpaper, in four themes.
  - IRREDUCIBLE: the exact per-pixel behavior of the macOS compositor under arbitrary content. No
    amount of source reading yields it, and no Mac is available to measure it.

## OD-0004 Visual reference material found inside the pinned tree

- **Not a question asked of the owner; a finding recorded here because it changes what OD-0001 costs.**
- **What exists:** eight `tour-*@2x.png` assets under
  `DroppyCode/Resources/Assets.xcassets`, each 1320x824 pixels, shipped in the app bundle:
  `tour-welcome`, `tour-threads`, `tour-panels`, `tour-slider`, `tour-themes`, `tour-hydra`,
  `tour-pairs`, `tour-recipes`. Inspected directly: they are real screenshots of the running macOS
  app, not illustrations. `tour-themes@2x.png` shows one chat in four themes at once, including the
  title bar, toolbar, branch picker, message bubbles, a collapsed "Worked for 58s" row, markdown with
  inline code, an "Edited files, read files" row, a "2 of 3 done" task card, a tool row running
  `xcodebuild -scheme DroppyCode build`, the scroll-to-bottom button, an attachment pill reading
  "1 file +3", and the composer with placeholder text
  "Ask Claude for anything; big jobs go out to a team of heads", a model chip reading "Opus High",
  and the send/stop control.
- **Why this matters:** it is genuine macOS-rendered evidence of Liquid Glass, the wallpaper, the
  type, the icon set and the layout, produced by the app's own author, tracked at the pinned commit.
  It is the closest thing to a reference available under OD-0001.
- **The caveat that must travel with every use of it (contract sections 4 and 6):** these are
  **assets**, regenerated by hand at unknown times. Nothing proves they were captured from commit
  `dd2b86e`. They are an **approximate oracle**, not an exact one. They may be used to check
  proportion, color, ordering, iconography and material character; they may **not** be used to claim
  pixel parity, and any measurement taken from them is recorded as approximate with this caveat
  attached.
- **The capture harness also exists in source:** `App/Captures/WebsiteCaptures.swift` (1122 lines),
  `App/Captures/TourCaptures.swift` (313 lines) and `App/Captures/TourCaptures+Hydra.swift`
  (305 lines), reachable at launch through `WebsiteCaptures.isEnabled` in
  `App/AppDelegate.swift:12-15`, with `WindowManager.makeCaptureWindow(size:)` providing a
  fixed-size window that deliberately does not read or write the real window's saved frame.
  This is exactly the "deterministic fixture data" capture mechanism contract section 10 asks for.
  Reading it yields the exact list of screens and states the author considers canonical, and the
  exact fixture content behind each one. The Windows port will mirror this harness so that Windows
  captures are produced in the same states, at the same sizes, with the same fixtures, making the
  comparison meaningful rather than ad hoc.

## OD-0005 Liquid Glass: Acrylic, no screen capture

- **Asked:** 2026-09-20, after the owner requested a side-by-side probe.
- **Answer, verbatim:** "I choose acrylic edge . Option A"
- **Naming collision, recorded because it was the lead's error:** ADR-0001 labels the
  CAPTURE-BASED option "Option A", while the probe image labels the ACRYLIC panel "A". They are
  opposites. The owner's words name the material, "acrylic edge", which is unambiguous, so the
  ruling is read as **Acrylic, no screen capture**, which is ADR-0001's Option B. Surfaced to the
  owner in the same message so it could be corrected in one word.
- **What is now settled:**
  - `Windows.Graphics.Capture` is **off the table**. No consent prompt, no capture indicator, no
    `WDA_EXCLUDEFROMCAPTURE`, so the app behaves normally in the user's own screenshots, screen
    recordings and screen shares. No frame-late backdrop, no rebuild on display topology change,
    no per-panel capture session.
  - Where the window shows the **desktop**, it uses a Windows system material of the Acrylic
    class. Lensing, refraction and chromatic fringing are **not** reproduced there.
  - Where the app draws its **own** backdrop, the full source-defined material is reproduced:
    shape, corner radius, tint colour and opacity, `.interactive()` and container spacing at all
    22 sites.
- **This is an owner-approved deviation, not parity.** Contract section 18 therefore caps the final
  statement at "VERIFIED WITH USER-APPROVED DEVIATIONS". UNK-0004 moves from BLOCKED_ON_OWNER to
  RESOLVED-as-accepted-deviation, and the deviation stays named in the final report.
- **Consequence for the stack choice:** the one capability that only a native Win32 + Direct3D
  stack could deliver is no longer required. Stack selection now turns on the remaining eleven
  constraints, and WinUI 3 and Avalonia, previously disqualified or unresolved solely on the
  desktop-sampling question, re-enter the comparison.
- **Still open under this ruling:** whether the app should always draw a backdrop so that the
  richer material applies more often, which would change an upstream default (upstream ships a
  clear window over the desktop with the wallpaper unset). Raised as UNK-0023.

## OD-0006 Target upstream: live 1.6.2, not the 1.6.0 pin

- **Asked:** 2026-09-22, when the owner handed the whole port to a new orchestrator and linked
  https://gitlab.com/droppyformac1/droppy-code asking for "a complete 100% port". Upstream `main`
  had moved from the pinned `dd2b86ef` (1.6.0 build 19) to `1fbf81e6` (1.6.2 build 20): 120 files,
  +2,645 / -1,621 lines, 195 Swift files instead of 192. An intermediate snapshot `417ab7d3` (a
  design refresh) had already been reverted upstream by `1fbf81e6`.
- **Options put to the owner:** switch to live 1.6.2 now (recommended); keep 1.6.0; port 1.6.0
  first and catch up at the end.
- **Answer, verbatim (selected option):** "Switch to live 1.6.2 (Recommended)"
- **What is now settled:**
  - The pinned baseline is `1fbf81e61e28a079508a074c01ee1830a9cc23dc`, tree
    `a583cec47ea4e97dbdb81349fa0cb45499b88fe6`, cloned fresh from GitLab into
    `E:\Droppy\reference\droppy-code-v1.6.2`. `porting/baseline.json` records it as B2 and keeps B1.
  - It is frozen again. If upstream moves before the end, one catch-up pass at the end, put to the
    owner as its own decision; nothing drifts in silently.
  - Reading evidence carries over only for the 317 byte-identical files. The 115 changed and 4 added
    files are UNREAD until re-read with evidence; Gate A and Gate B are re-earned for the delta.
  - The 1.6.0 checkout `E:\Droppy\reference\droppy-code` stays pristine as B1 history.

## OD-0007 Desktop probes may run without asking

- **Asked:** 2026-09-22. Probes P2, P3, P4 and P7 must open real windows on the owner's unlocked
  screen for a few minutes each.
- **Answer, verbatim (selected option):** "Anytime, no need to ask"
- **Effect:** they run when the plan reaches them. The owner leaves the mouse alone while they run.


## OD-0008 Decision batch: every recommendation accepted

- **Asked:** 2026-09-22, as one sheet (`porting/decision_batch_20260922.md`) covering the 12 quarantined
  behaviours (A1-A12), the platform and product-name strings (B1-B15) and the Windows-only behaviours
  (C1-C7), each with a recommendation.
- **Answer, verbatim (selected option):** "Accept all (Recommended)"
- **Effect:** every BLK entry in `porting/blocked_requirements.json` is RESOLVED with its ruling (11
  fixes as named deviations, BLK-0007 reproduced as upstream, BLK-0005 not applicable); all 27
  OD-PENDING items are resolved; C7 gives the port the install and data identity "Marrow Code"
  (`%LOCALAPPDATA%\Marrow Code`, Start menu "Marrow Code") so it never touches the owner's Electron
  Marrow, closing UNK-0024. Every fix is a named deviation in the final report, never parity.

## OD-0009 Text face: Inter if it measures closer than Segoe UI Variable

- **Asked:** 2026-09-22, after probe P9 measured Segoe UI Variable 3-5% narrower than SF Pro.
- **Answer, verbatim (selected option):** "Inter, if it measures closer"
- **Effect:** Inter (OFL) is downloaded from its official GitHub release and measured with the P9
  harness against the tour oracle; the face that matches more wrap points becomes the port's text
  face. Either way wrap points are a named deviation. Monospace: Cascadia Mono, as recommended in
  the sheet (D3). Closes the typography half of UNK-0007.
- **Measured result, 2026-09-22 (porting/probes/p9/INTER.md):** Inter matched 6/10 oracle fixtures (3/7
  multi-line) against Segoe UI Variable's 3/10 (0/7); mean width ratio 1.024 against 0.957. The rule
  gives **Inter**, shipped from the Avalonia.Fonts.Inter 12.1.2 package (Inter 3.019), added to
  Directory.Packages.props. Wrap points still differ on 4 fixtures: a named deviation.

## OD-0010 Icons: Lucide

- **Answer, verbatim (selected option):** "Lucide"
- **Effect:** SF Symbols map to Lucide icons (ISC licence) through a published symbol-by-symbol table.
  Closes the icon half of UNK-0007.

## OD-0011 Live checks through the owner's signed-in CLIs

- **Answer, verbatim (selected option):** "Yes, minimal prompts (Recommended)"
- **Effect:** provider verification may send minimal prompts ("reply with OK") through the installed,
  signed-in CLIs in a throwaway repository, using a little plan quota. The test harness itself never
  reads a credential file. This authorizes the chargeable calls contract section 12 requires.


## OD-0012 Decision batch 2: every recommendation accepted

- **Asked:** 2026-09-22, as `porting/decision_batch_20260922b.md`, after the 1.6.2 spec refresh left
  wording and identity points no ruling or source line settled.
- **Answer, verbatim (selected option):** "Accept all (Recommended)"
- **Effect, exactly as the sheet words it:**
  - E1-E6: the wording of the prompts and errors the batch-1 safety fixes create, in upstream's own
    confirmation style (question, note, red action button).
  - F1: every remaining "Droppy Code" / "droppy-code" the app shows or sends out becomes "Marrow" /
    "marrow", except droppy-code.json and DROPPY_CODE_PROJECT_ROOT. F2: welcome tagline "A calm, native
    home for your coding agents." F3: About line "Based on Droppy Code by Jordy Spruit (Droppy),
    https://getdroppycode.app".
  - G1 "this PC"; G2 "Credential Manager" in the provider key rows; G3 the ninth em dash kept; G4 "Open
    in": Finder becomes File Explorer, Terminal is Windows Terminal, Xcode and iTerm are left out, the
    other editors stay (the sheet's wording; an earlier summary here said "only", corrected by ED-087); G5 "Welcome tour" in the command palette; G6 Ctrl+0 is
    Actual Size and "Show Marrow" has no default chord.
  - H: the exact About and tour rewrites listed in the sheet; the terminal credit names the component the
    Windows build ships.
  - I1: the update feed is an internal value, empty by default, no settings UI; while empty no request is
    made and the pill shows "Check failed" with the note "No update feed is set."
  Every item is a named deviation in the final report.


## OD-0013 The live-resize edge strip must be fixed

- **Asked:** 2026-09-22, after probe P4 measured a one-frame edge lag during live resize: while the
  pointer moves, the growing edges show a 3-5 px strip of the window's own backdrop without the app
  layer in 4-12% of samples, and the shrinking edges clip for about a frame; never at rest.
- **Options put to the owner:** try a fix and otherwise accept it as a named deviation (recommended);
  accept it now; treat it as a blocker.
- **Answer, verbatim (selected option):** "Must be fixed"
- **Effect:** the main window chrome is not accepted until the strip is gone. Probe P4b is measuring
  remedies (synchronous present in the size loop, DwmFlush, WVR_VALIDRECTS, over-rendering). If none
  removes it, this returns to the owner; it is not quietly downgraded to a deviation.

## OD-0014 The window stays clear by default, as upstream

- **Asked:** 2026-09-22 (UNK-0023): keep upstream's clear window, where the glass sits over the desktop
  until the user picks a wallpaper, or always draw a backdrop.
- **Answer, verbatim (selected option):** "Keep upstream default (Recommended)"
- **Effect:** exact parity. Over the desktop the material is Acrylic plus the app-drawn scrim (OD-0005,
  probe P3); the richer drawn material appears once the user sets a wallpaper. UNK-0023 is resolved.

## OD-0015 App icon: the Marrow icon the owner already finalized

- **Asked:** 2026-09-22, in decision batch 2 (UNK-0008, the app icon under the Marrow name). The answer then was
  "Design a new icon"; three drafts and a Stitch board followed, and a round-3 board on 2026-09-23.
- **Answer, verbatim (2026-09-23):** "As for the icon, I don't like any of stitch's generated icons. LEt's stick to
  our original finalized icon."
- **Effect:** the port ships the icon of the owner's own Marrow app: `resources/icon.svg` (three arcs round a white
  core on a dark navy tile), `resources/icon-small.svg` for 24 px and below, `resources/icon-mono.svg`, and the
  rendered `build/icon.ico` and `resources/icon.png`, copied unchanged into `src/Marrow.App/Assets/`. The files say
  they are original artwork, not derived from Droppy or Droppy Code artwork. The executable, installer, taskbar and
  window use it, and so does every place upstream shows its own app icon or logo. It supersedes the batch-2 answer
  "Design a new icon". This is part of the identity deviation of OD-0002 and is listed with it in the final report.

## OD-0016 The Marrow icon, animated, on loading screens

- **Asked by the owner, 2026-09-23, verbatim:** "I need that icon animated in the app for loading screens"
- **What upstream does:** it has no loading screen. A thread whose history is still being read shows an empty
  canvas on purpose, so the new-thread prompt does not flash (ThreadTimeline.swift:132-135); launch shows the main
  window once the prologue has run (PR-LAUNCH-001, PR-LAUNCH-028); area-sized waits show the system spinner
  (DiffInspector.swift:76 "Reading the diff...", AttachmentPreview.swift:299 and :319); small inline waits show
  small or mini system spinners.
- **Effect:** an owner-requested addition, listed as a named deviation in the final report. The animated Marrow
  icon (the loading mark) appears where the app waits with a screen or a pane to fill: the thread history load, the
  diff pane while it reads the diff (its label kept), the large attachment preview while its image or video frame
  loads, and a launch splash while the prologue runs. Small and mini inline spinners stay the system spinner, as
  upstream. Where, when and how it moves is ED-097. The motion is option A "Orbit" from
  `E:/Droppy/work/icon/loading_preview.html` unless the owner picks B "Ratchet" or C "Bloom".

## OD-0017 Live resize: adopt `stretch`, accept the Windows residual

- **Asked:** 2026-09-23, after probe P4b (porting/probes/p4b/RESULT.md) found no remedy that removes every edge strip,
  as OD-0013 required it to come back.
- **Put to the owner:** `stretch` lowers the moving grow-edge strip from 2.54 % to 0.16 % of samples and removes every
  shrink clip; the rest is a one-frame lag composed by DWM that also occurs without the remedy and that no app code
  reaches, and about 2 % of moving samples show the previous frame scaled by up to 5 px for one frame instead of a
  strip; nothing changes at rest. Options: adopt and accept the rest as a named deviation (recommended); adopt and keep
  the chrome open; keep the chrome blocked.
- **Answer, verbatim (selected option):** "Adopt fix, accept rest (Recommended)"
- **Effect:** every resizable top-level window uses `stretch` as ED-101 builds it, and the window chrome is accepted
  once that is in and verified. The DWM type B frame (one frame, 3 to 9 px of what is behind the window, about 1 resize
  step in 150 to 250 at P4's drag speed) and the one-frame scaled frames are a named deviation in the final report.
  OD-0013 is closed by this decision.

## OD-0018 Loading mark: "Comet", a large boot animation, one pulse per turn

- **Asked:** 2026-09-23, after the owner found the first three motions not premium enough and was shown round 2
  (`porting/design/loading_mark/loading_premium.html`: A Tide, B Comet, C Glass).
- **Answers, verbatim:** "I like B. Ship it! I also want the animation to show up large when starting the app, but the
  animation must come to life in a highly premium way at boot up, like particles flying to form the animation and the
  animation must smoothly initiate a running state." Then: "although the pulse the central white dot releases must be
  controlled to trigger once when the spiral comes to a stop. To make it seem that the pulse initiates the spiral."
- **Effect:** the loading mark of OD-0016 moves as "Comet" (ED-102): one eased turn from rest to rest, trails only at
  speed, and the core pulses once per turn, exactly when the turn comes to rest, releasing one ring, so the pulse starts
  the next turn. Every launch opens with a large boot splash: particles in the arms' colours spiral in and form the
  mark, the core's first pulse sets the first turn going, and the mark runs until the app is ready, then hands over to
  the main window. The boot splash replaces the 400 ms launch-splash rule of ED-097 (5); the other loading sites keep
  that rule. An owner-requested deviation, listed with OD-0016 in the final report. Reference previews:
  `porting/design/loading_mark/boot_sequence.html` and `loading_premium.html` (card B).

- **Amended the same day, verbatim:** "boot animation = particles form the spiral, pulse happens and spiral happens,
  spiral ends motion, logo disintegrates into particles and forms the app complete window."
- **Effect of the amendment:** the boot splash no longer fades out. After the first turn comes to rest (more turns,
  each started by a pulse, while the app is not ready), the mark breaks back into its particles, and they fly out and
  build the main window: its edge first, then its contents, which appear as the particles land. ED-103 gives the
  numbers; the reference preview is `porting/design/loading_mark/boot_sequence.html`.

## OD-0019 Agent models for the implementation waves

- **Asked:** 2026-09-23, after measuring that wave A spent 834 million context tokens for 2 merged units, when the owner
  asked how to cut usage and speed the port up.
- **Answer, verbatim (selected option):** "Sonnet 5 for bulk (Recommended)"
- **Effect:** from implementation engine v3 on, Sonnet 5 at medium effort writes the tests and the implementations of
  ordinary (STANDARD-risk) units; Opus 5.5 writes the tests and implementations of HIGH-risk and foundation units and
  does every review, every merge and every ruling. Agents work from compact per-part work packets
  (`porting/tools/make_packets.mjs`) instead of the whole guide and spec. This is a process decision: it does not change
  what the port must match or how it is verified.

## OD-0020 Decision batch 3: every recommendation accepted (wave A rulings)

- **Asked:** 2026-09-23, as the nine questions in `E:/Droppy/work/rulings/owner_questions.md`, raised by the rulings for
  the units that stopped in implementation wave A (ED-104 to ED-150).
- **Answer, verbatim (selected option):** "Accept all (Recommended)"
- **Effect:** (1) Cascadia Mono ships inside the app; the owner approved downloading CascadiaCode-2407.24.zip (150,454,761
  bytes) from Microsoft's release (done; porting/probes/p9/CASCADIA.md). (2) Italic and weight-800 Inter come from the
  official rsms/inter release; the exact file and size are confirmed with the owner before it is downloaded. (3) Cookies
  and the URL cache persist across restarts as on the Mac, the cookie store encrypted with DPAPI under
  %LOCALAPPDATA%\Marrow Code. (4) An npm `.cmd` shim is resolved to the program it starts, which is launched directly with
  no command interpreter; any other `.cmd` or `.bat` target is refused; proven first on this PC with a fake npm tool.
  (5) A forced stop reports termination by signal 9 and a polite stop signal 15, as on macOS. (6) Command Code is
  looked up in the Mac order, "cmd" first, with the PA-0016 check that skips the Windows command interpreter.
  (7) Frame-timed waits may pause while the window is minimized; the effect is measured on the owner's PC.
  (8) Dates follow the Windows custom short and long date formats and first day of the week, as the Mac follows its
  own settings. (9) The agent file-access containment check compares case-sensitively (drive letter aside), as on the
  Mac. Where a ruling differs from the Mac this is a named deviation.

## OD-0021 Build first, verify later

- **Asked by the owner, 2026-09-23, verbatim:** "We can create the app and get it running first, then anything missed or a
  proper review can be scheduled later?" (after OD-0019 and the lean-mode answer "Lean for ordinary units
  (Recommended)", given when the projection showed the full pipeline would not fit the week's remaining limit).
- **Effect:** implementation runs in build mode: each unit part is written tests-first by one agent (Sonnet 5 for
  ordinary units, Opus 5.5 for high-risk and foundation units) and merged when the full build and every test pass;
  foundation units alone get one Opus review before merging. Units merged this way are IMPLEMENTED, never VERIFIED. A
  verification wave runs after the app runs: the full review (fidelity, test adequacy, mutation, safety for high-risk
  units) of every unit merged in build mode, with fixes. Gate D and the final parity claim wait for that wave; nothing
  about the target or the contract changes.

## OD-0022 MCP custom headers are dropped at a cross-server redirect

- **Asked 2026-09-23** (blocker-ruling question Q1, `E:/Droppy/work/rulings2/owner_questions.md`): when a configured MCP
  server redirects to a different server (scheme, host or port), should the server's custom headers still be sent?
- **Owner answer, verbatim:** "Drop them (Recommended)".
- **Effect:** Services.MCP.MCPHub, MCPProbe and MCPProxy send a server's custom headers only to that server; a redirect
  that changes scheme, host or port drops them, and a same-server redirect keeps them. This matches ED-152's rule for
  `Authorization`. It is a named safety deviation: a server that deliberately redirects elsewhere and expects the key
  there may fail to connect on Windows where it may work on the Mac.

## OD-0023 The ED-192 relaunch-probe test keeps launching a console program

- **Asked 2026-09-26** (main Claude): the test AppUpdaterInstallerScriptTests.After_a_successful_swap_the_relaunch_step_
  launches_the_new_executable_not_the_destination_folder starts a copy of sort.exe, which Windows 11 shows in a Windows
  Terminal window for a few seconds during every full test run. Options: (a) intercept Start-Process and assert the
  target, (b) a windowless probe exe in a new test project, (c) leave it.
- **Owner answer, verbatim:** "Option C".
- **Effect:** the test is unchanged and keeps its end-to-end launch check; the window during run_suite.sh is expected.

## OD-0024 The pinned baseline moves to live 1.7.0 (B3)

- **Asked 2026-09-26** (main Claude, UNK-0025): upstream main moved 16 commits past B2 (101 files) to 1.7.0.
- **Owner answer, verbatim:** "Move to 1.7.0, check what needs updating and changing and plan accordingly."
- **Effect:** AM-0003. The pin is B3, commit 52bc7926097cd622d08d6294e0d8b08a48c7516e (1.7.0, build 21), reference
  E:\Droppy\reference\droppy-code-v1.7.0. UNK-0025 is resolved by this ruling.

## OD-0025 Claude Code and Codex update themselves on Windows as on the Mac, after one trial run

- **Asked 2026-09-26** (main Claude, B3 decision O1, `E:/Droppy/work/b3/decisions_draft.md`; UNK-0026, PA-0018): Droppy
  Code 1.7.0 runs `claude update` and `codex update` in the background at launch (at most every 12 hours per provider)
  and, when a model says the CLI is too old, updates it and resends the message. Should Marrow do the same? Options:
  test once, then match Mac (Recommended) / match Mac, no test / never auto-update / only when the CLI is idle.
- **Owner answer, verbatim:** "Test once, then match Mac (Recommended)".
- **Effect:** the port reproduces the B3 behaviour exactly (`Services/Providers/ProviderRegistry.swift:467-537`,
  `App/AppModel.swift:275-282`, `App/Runtime/ThreadRuntime.swift:2255-2293`; wording ED-193). Before the lanes that
  implement it merge (A23 ProviderRegistry, A32 AppModel, A33 ThreadRuntime), one supervised trial run of the real
  `claude update` and `codex update` on the owner's PC records exit code, output, any window or prompt, and the version
  before and after, as evidence (PA-0018 verification method). This ruling authorizes that one trial. UNK-0026 is
  resolved by it.

## OD-0026 Chat links on Windows: drive-letter paths open locally, network paths never open, program files are revealed

- **Asked 2026-09-26** (main Claude, B3 decision O4; UNK-0027, PA-0020): what may a link in a chat open on Windows?
  Options: safe parity (Recommended) / full parity / literal source.
- **Owner answer, verbatim:** "Safe parity (Recommended)".
- **Effect:** `MarkdownLinkOpener` (`UI/Markdown/LinkParagraphView.swift:226-245`) treats Windows absolute paths
  (`C:\...`, `C:/...`, and `/C:/...` from a file URL) like the source's "/"-rooted local-file branch, with the same
  `:<line>` suffix rule (contract section 11, preserve intent). A UNC path (`\host\share\...`, `file://host/...`) is
  never opened from a chat link (named safety deviation, like OD-0022: it would send the user's Windows credentials to
  a host that model output named). A local target of a type Windows runs directly is shown selected in File Explorer
  instead of launched (named safety deviation; the rule and mechanism are ED-204). Lanes: A29
  (UI.Markdown.LinkParagraphView) and UI.Markdown.LinkPopover. UNK-0027 is resolved by it.

## OD-0027 The tour's eight screenshots are retaken in Marrow on Windows

- **Asked 2026-09-27** (main Claude, while writing ED-215): the tour (`App/Views/Tour/TourView.swift:256`
  `NSImage(named: page.imageName)`) shows eight asset-catalogue screenshots: tour-welcome, tour-threads, tour-panels,
  tour-slider, tour-themes, tour-hydra, tour-pairs and tour-recipes, each 1320x824 pixels at 2x (OD-0004). They are
  real macOS captures showing the red/yellow/green window buttons and "DroppyCode" text (for example
  "xcodebuild -scheme DroppyCode build" in tour-welcome). Options:
  - A. the Mac screenshots unchanged;
  - B. retake the same eight scenes in Marrow on Windows once it runs, with the pictures empty and tracked until then
    (Recommended);
  - C. leave the pictures out.
- **Owner answer, verbatim:** "Option B".
- **Effect (named deviation from the pinned assets; the tour's code, pages, text and layout stay exact):**
  - Each tour-* image becomes a Marrow-on-Windows capture of the same scene: same content, same framing, same
    1320x824-pixel 2x size, and the same asset name so TourView's lookups are unchanged. The Mac captures are the
    content guide for each scene.
  - Until the captures exist, TourView draws no picture for those pages and keeps a visible STOP naming OD-0027;
    ED-215 excludes the eight tour-* sets from the bundled catalogue.
  - The captures are a post-first-launch task (E:\Droppy\work\followups.md) and need the app running with test data
    only, never the owner's real projects or accounts.
  - OD-0004's use of the Mac captures as an approximate visual oracle is unchanged.


## OD-0028 Notifications on Windows are real Windows toasts, through Microsoft's Windows SDK projection

- **Asked 2026-09-27** (main Claude; audit finding G1-03): the Mac posts user notifications for a finished chat
  (`App/AppModel.swift:1248-1266`) and a found update (`App/UpdateChecker.swift:245-261`), and routes their clicks
  (`App/AppDelegate.swift:63-90`). The port posted nothing (AppModel.Notify and UpdateToastCenter were no-ops). Options:
  - A. use Windows' own toast notifications through Microsoft's official Windows SDK projection for .NET
    (`Microsoft.Windows.SDK.NET.Ref`), a one-time build download from nuget.org (Recommended);
  - B. hand-written interop to the same Windows feature, with no new package;
  - C. no notifications, as a recorded difference.
- **Owner answer, verbatim:** "Yes, ChatGPT already uses it, look how it shows up on the screen on the right lower side."
  (Option A; the owner pointed at a ChatGPT toast on screen as the model.)
- **Effect:**
  - The port may add the Windows SDK projection, restored from nuget.org, to the project that owns the toast code, and
    only that project changes its target framework.
  - Toasts carry the title, the body and the default sound, or none when the source passes nil. A chat's toast carries its
    threadID payload and an update toast its version, and a click routes exactly as AppDelegate.swift:77-90 does.
  - The foreground rule of :63-75 decides banner and sound versus nothing.
  - The app registers its own notification identity, distinct from the owner's separate "Marrow" Electron app.
  - Tests never post a real toast.
  - The mechanism is ED-220.

Provenance (restored historical approval, not a new owner ruling): recovered only this OD-0028
section from f4db0ef539f50bfe6bcc8cbfc59e367f9fc562f1 (2026-09-27),
porting/owner_decisions.md. The owner answer and effects above are preserved verbatim.
The historical final ED-220 citation above refers to the toast decision restored as ED-227
(same clauses 1-7; original 168b20c0582cf235af24bf8f4efc8630b8c52bd0).
It does not refer to the current ED-220 native/process collection exception.


## OD-0031 Terminal Escape descendant-process approximation accepted

- **Owner ruling, 2026-09-30:** "PR-TERM-031 / ED-046: ACCEPTED. Escape closes the terminal drawer only when the shell has no live child program. Otherwise Escape goes to the terminal, and if it can't tell, it also goes to the terminal."
- **Effect:** ED-046's live-descendant approximation is a named accepted deviation, not foreground-process-group parity. Existing key/modifier/focus guards and session lifetime remain unchanged. PR-TERM-031 and PR-TERM-049 no longer await owner acceptance; implementation, interaction tests and mutations remain required. No requirement status or verification credit is granted here.

## OD-0032 MCP argument-editor visible inset accepted deviation

- **Owner ruling, 2026-09-30:** "UI-MCP-053: match the VISIBLE result. Adjust the Windows padding so the space around the text looks the same as the Mac's TextEditor inset, and record it as a named accepted deviation, not parity."
- **Effect:** named accepted deviation "Windows argument-editor inset compensation". Adjust Windows padding for its built-in editor inset; preserve the source's visible text spacing, 66-point height, surface, hint, typography and argument semantics. No invented padding values or visual-verification claim. UI-MCP-053 still needs measured visual/input proof.

## OD-0033 About identity pill uses the Marrow icon

- **Owner ruling, 2026-09-30:** "About icon: the Marrow icon, as OD-0015 already rules. Update the UI-UPDATE-001 record and any wording about the old logo."
- **Effect:** reaffirm OD-0015's existing identity deviation. UI-UPDATE-001 uses the finalized Marrow icon, retaining the source geometry, interpolation and accessibility behavior. Pinned source citations remain historical source facts; the port expectation no longer describes the live Droppy icon. No artwork change or implementation/verification credit.

## OD-0034 ED-065 FIFO main-queue amendment approved as written

- **Owner ruling, 2026-09-30:** "ED-065: APPROVED as written (draft E:\Droppy\work\codex\ed065-amendment-draft.md). Keep the Mac-style FIFO main queue that handles input between passes. Count the dependent records once their tests and mutations are accepted."
- **Effect:** the full approved draft is reproduced under ED-065 in engineering_decisions.md. Snapshot FIFO passes, Default for an idle queue's first pass, Input for later passes, never inline, nested-loop reentrancy/pass supersession, exception ordering, captured ExecutionContext and dispatcher-bound continuation context are binding. IMPLEMENTING.md's old per-job Default wording is superseded narrowly. Draft evidence is attributed historical evidence, not newly verified by this entry. Dependent records receive no count/status advance without accepted tests and mutations.

## OD-0035 Hydra sync spec records the OD-0020 deviation

- **Owner ruling, 2026-09-30:** "PR-HYDRAMERGE-063 spec sentence: covered by OD-0020. Correct the spec text so it states the deviation from the Swift."
- **Effect:** explicitly confirm today's coverage ruling for the existing runtime's OD-0020 Part A/B labels: forced removal of remote-deleted paths after soft reset, and restoration of the captured branch/index/working files on a later sync failure before returning false. The pinned Swift does not undo already-applied changes when its write-back throws; keep that source fact and mark the port rollback as a named accepted deviation. Rollback failure is surfaced rather than called successful restoration. Preserve positional truncated-output behavior and all unchanged source guards/write-back rules. No runtime/test edit or whole-record acceptance is authorized by bookkeeping alone.
- **Historical distinction:** the current OD-0020 entry lists the original wave-A batch without spelling out these Hydra Part A/B labels. Preserve that historical entry; this explicit 2026-09-30 ruling supplies the coverage cross-reference instead of silently rewriting its old wording.

Provenance for OD-0031 through OD-0035: owner chat relayed by J.A.R.V.I.S. and full E:/Droppy/work/codex/turbo/OWNER_RULINGS_20260930b.md, read in full. OD-0029/0030 are reserved by historical docs/b3 decisions and are not imported or renumbered here. Historical OD-0028 and all earlier entries remain unchanged.

## OD-0036 Session end held by a merge: no reason text

- **Owner ruling, 2026-10-01 (chat, answering OD-PENDING-29 (1)):** "No reason text."
- **Effect:** while a Hydra merge holds a Windows session end (ED-004 (2)), the port calls no ShutdownBlockReasonCreate; Windows lists the app as blocking with no reason, as the Swift has no user-facing text (AppDelegate.swift:41-61). Closes the text question of STOP Boot #3 / opencode-App.AppDelegate #3; PR-LAUNCH-050 and PR-WIN-073 are judged on the deferral, poll and cap tests.

## OD-0037 Notification clicks: callback proof now, one manual check later

- **Owner ruling, 2026-10-01 (OD-PENDING-29 (2)):** "Code proof + one manual check."
- **Effect:** PR-LAUNCH-064, PR-LAUNCH-065, PR-UPDATE-024, PR-WIN-076, UI-LAUNCH-003 and UI-LAUNCH-005 are proven by the toast activation callback tests (ED-220 (7) keeps real toasts out of tests). One manual check stays on the Windows verification list: the owner clicks one real thread toast and one real update toast on a dev build.

## OD-0038 Reopen restores a minimised main window

- **Owner ruling, 2026-10-01 (OD-PENDING-29 (3)):** "Restore and bring forward."
- **Effect:** WindowManager's present path (WindowManager.swift:57-60, makeKeyAndOrderFront) restores a minimised main window to its previous state and activates it, for a second launch and for applicationShouldHandleReopen. Whether makeKeyAndOrderFront alone deminiaturizes stays UNVERIFIED (OD-0001); this ruling settles PR-WIN-072.

## OD-0039 The tour is marked seen only by its own close control

- **Owner ruling, 2026-10-01 (OD-PENDING-29 (4)):** "Follow the Mac code."
- **Effect:** hasSeenTour is set by TourView's close action (onClose) only; closing the tour window natively (Alt+F4, caption close) only clears the controller (TourWindowController.swift:103-107). Where UI-LAUNCH-002 says every close marks it seen, the Swift wins.

## OD-0040 The launch counts as an activation for the update catch-up check

- **Owner ruling, 2026-10-01 (OD-PENDING-29 (5)):** "Yes, check at launch too."
- **Effect:** the main window's first activation at launch runs UpdateChecker.handleActivation (UpdateChecker.swift:141-149) like every later activation, so a release build makes a catch-up check at launch (nothing checked yet) besides the 8 s one. Settles PR-LAUNCH-046 and STOP Boot #2.

## OD-0041 The boot sequence covers the Acrylic backdrop's start-up

- **Owner ruling, 2026-10-01 (OD-PENDING-29 (6)), in the owner's words:** "Use a transition or the boot animation can cover the load time."
- **Effect:** the ~150-200 ms between an Acrylic window's tint and its blur (probe P13) must not be seen: the hand-over is arranged so the main window's backdrop is ready before its content appears (for example shown early and fully transparent under the stage, then revealed as ED-103 (5) gives). The mechanism is an ED, proved by a probe; it applies once the main window carries its Acrylic hint (WindowManager STOP #5).

## OD-0042 System controls: the macOS look, except the native context menu

- **Owner ruling, 2026-10-01 (OD-PENDING-28), choice "Mix":** "Mac look, native menu."
- **Effect:** `.bordered` / `.borderedProminent` buttons, `.roundedBorder` fields, the indeterminate `ProgressView()`, `.help()` tooltips and the small Slider imitate the macOS controls as the reference videos show them (measured where the videos show them, UNVERIFIED otherwise); `.contextMenu` stays the native Win32 popup menu (no SF Symbol icons, no red destructive title, Windows menu theme). The Windows 11 counterparts built under the "ED-229" proposal are replaced except the menu.

## OD-0043 Slider track click: settle from the videos

- **Owner ruling, 2026-10-01:** "Check the videos thoroughly."
- **Effect:** whether a press on the bare slider track turns the knob to glass is decided from a full search of both reference videos; until a clip shows it, the port keeps glass on a knob press only.

Provenance for OD-0036 through OD-0043: owner answers in this session's chat (Claude Opus 5.5), 2026-10-01, to the questions OD-PENDING-28 and OD-PENDING-29 (1)-(6) plus the slider track click.

## OD-0044 Caption buttons: Windows order at the top right, a custom animated glass design

- **Owner ruling, 2026-10-01 (chat), settling OD-PENDING caption-button placement:** the three window buttons move from the source's leading-edge traffic-light place to the Windows place and order (minimise, maximise, close at the top right), and are designed for the liquid-glass chrome: not basic icons, but glyphs that animate inside the button when the pointer hovers (lighting up, the glyph itself moving, "like the slider").
- **Effect:** a named DEVIATION from WindowManager.swift/WindowChrome.swift's leading traffic lights. Five candidate designs are on the canvas https://claude.ai/artifact/E8tx6pDRH2roAcjTLrsFxc (A Halo draw, B Sliding lens, C Comet ring, D Liquid fill, E Spark morph); the owner picks one, then it is built test-first (hover, press, the Windows caption semantics and hit-testing of ED-063, reduce motion stills the animation). Until the pick, the current caption buttons stay.
- **Pick, owner ruling 2026-10-01 (chat):** "N Turn Pulse, lock it as my selection." At rest each button shows only its glyph (no disc); on hover two soft rings (24 pt, 1.5 pt stroke, white 65 %; close: rose rgb(255,107,139) 90 %) leave the glyph and fade, scale 0.45 to 1.55 over 1.3 s on cubic-bezier(0.2, 0.6, 0.3, 1), the second 0.45 s after the first, repeating while hovered; the glyph ticks (scale 1 -> 0.86 at 12 % -> 1.06 at 30 % -> 1) on the same 1.3 s beat. Buttons 46 x 40, glyph 12 pt at 1.4 pt stroke, order minimise, maximise (restore while maximised), close, at the top right. The canvas's More.dc.html variant N is the visual reference.

## OD-0045 Mac-look controls without video evidence: Apple's design resources are the reference

- **Owner ruling, 2026-10-01 (chat):** "Use Apple's design kits."
- **Effect:** for the OD-0042 controls the reference videos do not show (`.help()` tooltips, `ProgressView()` spinners, `.bordered`/`.borderedProminent` buttons, `.roundedBorder` fields), sizes, colours and states come from Apple's official public macOS design resources (Apple Design Resources UI kits, Human Interface Guidelines), each value cited to its page or kit and labelled SOURCE FACT (Apple design resource), never from memory.

## OD-0046 Blend: Windows conventions, Droppy Code's design language

- **Owner ruling, 2026-10-01 (chat):** the port keeps the exact look of Droppy Code on macOS (its liquid glass, colours, type, motion) so that it reads as made by the same designer, but where a platform convention differs, the Windows convention is followed, its elements drawn in the app's own design language. Example given: the caption buttons sit at the top right as Windows places them, not at the top left as macOS does, and are designed in the app's glass look (OD-0044).
- **Effect:** a standing rule for platform-boundary elements (window chrome, caption buttons, placement and order of system affordances): their place, order and behaviour follow Windows; their appearance follows the source's design language. Each such element is still recorded as a named deviation where it departs from the source; everything that is not a platform convention keeps exact parity.

## OD-0047 Every provider gets its own colour

- **Owner ruling, 2026-10-01 (chat), in the owner's words:** "Yes, optimally matched to the actual llm's theme, like Claude's theme is dominantly orange so it gets orange ... If two themes coincide or collide, give it another one then. ChatGPT stays locked at white and Claude stays locked at Orange, other LLMs if they have white or orange dominant themes, could maybe use a distinct different shade of that color that stands out."
- **Effect:** a DEVIATION from EffortSlider.swift:853-940 (EffortBrand), where nine providers share `.silver`. Claude keeps the source's orange (fill (0.80, 0.42, 0.28), title (0.93, 0.58, 0.44)) and Codex/ChatGPT keeps the source's silver-white, both locked; DeepSeek, Meta and Antigravity keep their source colours unless they collide; every other provider gets a colour matched to its own brand's dominant colour, a distinct shade where that is white or orange, and another hue where two would collide. The palette is proposed on a canvas and applied only after the owner approves it.

## OD-0048 The effort track blends every selected model, not only a pair

- **Owner ruling, 2026-10-01 (chat):** "Yes, design it now." When 3 or 4 models are selected, the slider holds all their colours, flowing smoothly along the track with the fusion animation, the lead's colour first.
- **Formula, owner ruling 2026-10-02 (chat):** the lead is always on the right, next to the knob, the first lead right against it. There may be several leads (the owner chooses how many leads and how many workers); each lead's colour gets a somewhat larger stretch of the track than a worker's, and the workers fill the track to the leads' left. Canvas: https://claude.ai/artifact/M12dhPeZAsbG2YZtQZ882C (lead share 1.6 x a worker's, proposed).
- **Effect:** a new capability beyond the source (EffortPairBrand blends a lead into one heads provider). The pair look (TrackLook with a pair, ED-213's fusion) is generalised to N colours, lead first, then each further provider in order, strands weaving between all of them; with today's selection (a pair) it draws exactly as before. Designed and shown on a canvas first; built test-first after the owner approves the look.

## OD-0049 OpenRouter as an API provider, with a searchable model list

- **Owner request, 2026-10-02 (chat):** the owner wants to connect an OpenRouter API key and use any OpenRouter model; the list of OpenRouter models that pops up must have a search field so a specific model can be found and selected easily.
- **Effect:** a new capability beyond the source (Droppy Code 1.7.0 has no OpenRouter provider; only PiCLI.swift:38 passes OPENROUTER_API_KEY through to Pi). It is built as an API-key provider in the manner of the source's OpenAI-compatible providers (DeepSeek, Meta, Z.ai: OpenAICompatibleAPI/Session), keyed in the Windows credential store, with its model list read from OpenRouter and shown in the composer's model picker with a search field (the look of the source's Settings model search, ChromeSearchField). Designed on a canvas and approved before it is built; scheduled after the parity work in flight.

## OD-0050 A colour for every new provider, worked out automatically

- **Owner ruling, 2026-10-02 (chat), choice "Read its logo, then pick a free shade":** for a provider or model maker the app has no colour for (for example one reached through OpenRouter), the app finds the colour itself: when it is first added, it downloads that maker's logo or site icon once, takes its dominant colour, and, if that collides with a colour already in use (OD-0047's palette or an earlier automatic one), shifts it to the nearest free shade; the result is saved locally and used like a built-in brand colour (fill, lifted title colour, sparks, blends of OD-0048).
- **Loading, owner ruling 2026-10-02 (chat):** while a newly selected model's colour is being worked out (it should take two to three seconds at most), the effort slider shows a loading animation in the track that says the colour is loading; when the colour arrives it blends into the track with a smooth transition, never an abrupt, static switch. The new colour then glides into the track like liquid (owner, 2026-10-02: it must flow in smoothly, gliding), from the knob end, with a soft moving front. Proposed (to confirm on the design canvas): the work is bounded at 3 s; past that the track settles smoothly on a neutral free shade and blends again when the logo colour arrives.
- **Arrival, owner ruling 2026-10-02 (chat), replacing the glide/flood:** the tube is already full of clear, living water (currents, light, bubbles, seen through the glass); when the colour is decided it is released into that water like watercolour, drops blooming and swirling with the water's current until the tube (in a blend: only that model's stretch) holds the colour, which then stays. Every looping animation in the track must loop seamlessly, with no visible restart (owner, 2026-10-02).
- **Effect:** a new capability beyond the source. The one download per new maker is a network request the app makes on the owner's behalf; it is listed in the port's network inventory, and tests never reach the network (a seam returns a fixed image).

## OD-0051 The effort track's liquid design is approved and locked

- **Owner ruling, 2026-10-02 (chat):** "Lock and ship it! I love it!" for the canvas https://claude.ai/artifact/M12dhPeZAsbG2YZtQZ882C at version 15 (copy: E:\Droppy\work\design\effort-liquid-approved-v15.dc.html). "Ship" is read as: build it into the port; any release, signing or packaging still needs the owner's separate word (standing rule).
- **What is locked:** OD-0047's palette (14 providers as shown: ChatGPT silver-white and Claude terracotta locked; DeepSeek, Meta, Gemini as the source; Cursor ember, Grok liquid obsidian with oil sheen, OpenCode terminal green, OpenCode Go cyan, Z.ai navy to electric blue, Devin teal, Copilot purple to peach, Command Code deep indigo, Pi crimson); the Max track as a glass tube of living liquid (seamless flowing currents, drifting caustic light, randomised wandering bubbles, a thin even rim, refraction toward the rounded rim with a faint colour fringe after rizzytoday/liquid-glass, no sweeping shine); OD-0048's blend formula (leads at the knob end, each lead 1.6 x a worker's stretch, soft OKLab mixing through each colour's heart); OD-0050's arrival of a new colour (clear living water, ink released only at the knob end in pulses, a Stable Fluids simulation with vorticity confinement and slight negative buoyancy, clouds thin with tracer particles at their fronts and dense behind, filling the tube and staying; a lighter rendering on low-power devices).
- **Effect:** these are named deviations from EffortSlider.swift's TrackLook/TrackEffect/EffortBrand, approved by the owner; they are built test-first into the port's effort slider. The source's plain (non-Max) levels and the fast-mode bolt are untouched unless a later ruling says otherwise.

## OD-0052 The liquid design's final form (canvas version 22) is locked

- **Owner ruling, 2026-10-02 (chat):** "Alright lock this one in and move ahead now", for canvas version 22 of https://claude.ai/artifact/M12dhPeZAsbG2YZtQZ882C (copy: E:\Droppy\work\design\effort-liquid-locked-v22.dc.html). It supersedes OD-0051's reference copy (version 15); everything OD-0051 locked stands, with the owner's later rulings in the same session folded in.
- **What version 22 adds to OD-0051 (each asked for by the owner):**
  - The liquid reaches fully into both rounded ends: the glass bends inward at both ends (the displacement map's red channel rises to the right), and the knob's drop shadow is light (0 2 6 px, 14 %) so the liquid is not darkened beside the knob.
  - Bubbles never repeat: every rise of every bubble is new (size, height, path, sway, speed, brightness, start point, then a random pause). Each bubble keeps an even stretch of the tube, and now and then one starts anywhere.
  - The ink arrival takes 8.3 s in all (2.3 s of loading, then the fluid at twice its tuned pace, on real time); the solid colour takes over in a soft half second.
  - It is smooth and light on modest hardware:
    - drawn on the GPU in one pass per tube, the same mathematics as the CSS look;
    - evenly paced frames (every n-th refresh, never below 60 fps);
    - the fluid simulated and painted in a background worker, in lockstep with the display;
    - nothing allocated per frame;
    - only tubes in view are drawn.
- **Effect:** the port's effort slider follows version 22's rendering (OD-0051's deviations, as refined here). Performance targets carry over: one small track at 60 fps, a negligible share of an integrated GPU.

## OD-0053 The Welcome tour's palette row: after Settings, with sparkles

- **Owner ruling, 2026-10-02 (chat), choice "After Settings (Recommended)":** the Help menu's "Welcome tour" (AppCommands.swift:111-113), which OD-0012 G5 moves into the command palette, is the row right after Settings, with the `sparkles` symbol, id `welcome-tour`, no chord.
- **Effect:** closes STOP App.AppCommands #4 (PR-WIN-115, PR-UPDATE-092, UI-UPDATE-055, UI-WIN-027 and the palette clause of PR-WIN-089).

## OD-0054 Apple's macOS design kit may be downloaded

- **Owner ruling, 2026-10-02 (chat), choice "Yes, download it":** for OD-0045, Claude may download Apple's official macOS UI kit from developer.apple.com/design/resources and accept Apple's terms for it on the owner's behalf; values measured from it are cited to the kit.
- **Where the kit is (checked 2026-10-02):** Apple publishes it as a Figma community file and a Sketch web share, not as a file: "Apple macOS 27 UI Kit", https://sketch.com/s/57153a31-3379-4737-8ac6-dbfd6525f052 (viewable without an account: 4,679 symbols with their sizes, 67 text styles, 285 layer styles, 110 colour variables, an inspector, and an "Export Design Tokens" option). Droppy Code 1.7.0 targets macOS 26; the current kit is macOS 27, so each value taken from it is cited as "macOS 27 UI Kit" and checked against the reference videos where they show the control.

## OD-0055 Native evidence runs on this PC

- **Owner ruling, 2026-10-02 (chat), choice "Yes, run it":** Claude may launch the port's own debug build (never the owner's separate Electron app "Marrow") on this PC, with its data folder redirected (MARROW_DEV_DATA_ROOT, ED-212) so the owner's real data is never touched, to record native evidence: the effort slider's frame counts (PR-COMPOSER-132/133) and the launch captures on a clean data folder (PR-LAUNCH-071, UI-LAUNCH-001/002).

## OD-0056 Network errors show Windows' own error text

- **Owner ruling, 2026-10-02 (chat), choice "Windows' own error text":** where Swift shows a Foundation URLError's localizedDescription (for example MCPHub over http: "The request timed out."), the port shows the Windows networking error's own message instead of Apple's English text. DEVIATION from the source wording, recorded; R-ERR-2's "no invented text" stands, since the text is the platform's.

## OD-0057 Test seam for HTTPS requests

- **Owner ruling, 2026-10-02 (chat), choice "Add the seam":** a test-only handler seam on the app's HTTP client (production behaviour unchanged), so PR-MCP-068 and the OAuth retry's success path can be tested.

## OD-0058 Order of work after parity

- **Owner ruling, 2026-10-02 (chat):** "OpenRouter and logo colours first, Release Preparation second, Polish and review pass third and then legacy Marrow features." Release preparation is local only: nothing is signed, published or released without the owner's word.

## OD-0059 Animation timing may be amended for a steady 60

- **Owner ruling, 2026-10-02 (chat), choice "Yes, amend ED-117":** a TimelineView's draws are timed one interval apart from the previous due time, not from when the last frame happened to run, so a 1/60 animation reaches 60 updates a second when frames jitter (native run: about 33 a second on the owner's PC under the old rule). Never faster than the interval. Recorded as ED-232 (amends ED-117).

## OD-0060 OpenRouter: engine now, picker after review

- **Owner ruling, 2026-10-02 (chat), choice "Build the engine, hold the UI":** the OpenRouter provider plumbing (catalogue, keys, requests) is built and tested offline; the model picker UI waits for the owner's approval of its canvas (https://claude.ai/artifact/BNoUoc4vvCLK5pHzYMmVcA).

## OD-0061 Logo colours: a palette canvas first

- **Owner ruling, 2026-10-02 (chat), choice "Prepare a canvas":** the OD-0047/OD-0050 provider palette is proposed on a canvas (every provider against the locked liquid slider and the logos, light and dark); nothing is applied in the app until the owner chooses.

## OD-0062 Installer: Velopack

- **Owner ruling, 2026-10-02 (chat), choice "Velopack":** release preparation builds toward a Velopack per-user install (%LOCALAPPDATA%, delta updates, a feed that can later point at GitHub Releases). Local only: nothing is signed, published or released without the owner's word (OD-0058).

## OD-0063 Colour intensity follows the model and the effort; a router takes the maker's colour

- **Owner ruling, 2026-10-02 (chat, translated from Hindi/Urdu):** within one maker's locked palette (OD-0047, OD-0050/OD-0061), the colour's INTENSITY differs per model and per effort level: a stronger model of the same maker wears a more intense ("vicious") shade of the same palette than a weaker one (e.g. Claude's strongest models more intense than Sonnet or Haiku; each Grok model its own intensity), and each effort level shifts it again, so every model at every effort has its own shade (e.g. Sonnet 5.5 at High differs from Opus at Low), and the liquid's animation adjusts with the effort. This holds for every model and effort the app offers.
- **OpenRouter is not a colour:** it is a router, not a model maker. A model chosen through OpenRouter wears the colour of the maker that created that model (its id's vendor, e.g. anthropic/... takes Claude's palette), at that model's own intensity. The canvas's separate "OpenRouter" colour is withdrawn.
- **Palette canvas feedback (OD-0061, https://claude.ai/artifact/Nny65Zii8axSS64u1AZ7Ws):** the colours are approved in spirit; NVIDIA's green is to be darkened slightly to a more premium, more intense green that sits with the others.
- **Unchanged:** the fast-mode (1.5x) bolt animation is constant and integrated as it is.
- **Effect:** the intensity rule (how model strength and effort map to shade within a palette) is designed on a canvas and approved before it is built (as OD-0050/OD-0061); nothing is applied in the app until the owner chooses.

## OD-0064 New models and makers are coloured by the app itself, automatically

- **Owner ruling, 2026-10-02 (chat, translated from Hindi/Urdu):** the colour system must run inside the app with no help from a development session: whenever a new model appears (new models arrive all the time, through OpenRouter or any provider), the app notices it on its own, works out its maker, and, for a maker it has no palette for, looks the maker up online (its brand colours, logo or site), derives a distinct palette from that (premium, not clashing with any colour in use, as OD-0050), places the model at its intensity within the maker by its strength and applies the effort shades (OD-0063), all within seconds, saves the result locally and uses it from then on. The owner must never have to come back to a session to have new models or makers coloured.
- **Effect:** extends OD-0050 (one logo or site-icon download per new maker, dominant colour, nearest free shade, saved locally) to every new model and maker, with the strength ranking and intensity rule of OD-0063 applied automatically. The online lookup is network use the app makes on the owner's behalf; it is listed in the port's network inventory, works without any API key, degrades to a neutral free shade when offline (OD-0050's 3 s bound), and tests never reach the network (seams). The rule itself is approved on the canvas (OD-0061, OD-0063) before it is applied; the engine can be built before that.

## OD-0065 Every user gets the same colours: the automatic engine is deterministic

- **Owner ruling, 2026-10-02 (chat, translated from Hindi/Urdu):** the app will be given to other people (e.g. 10 users); the automatic colour engine (OD-0064) must perform consistently across all of them: a given new model must lock the SAME colour theme on every user's machine. Verify it by taking one new model through 10 separate instances and checking they all lock the same theme.
- **Effect (engineering consequences, to be designed and verified):** a maker's palette and a model's shade must be a pure, versioned function of public facts (the maker's identity, its logo from one fixed source chain, the model's own published data and the locked palette), never of a user's history: not of which makers that user saw first or in what order (collision avoidance must be order-independent), not of transient network failures (a failed lookup shows the neutral shade and retries; it never locks a fallback colour from a different source), and not of when the user last refreshed a model list (a model's intensity must not drift as its maker adds models). Verified by an automated test of 10 isolated engine instances with different histories, orders and a failing source producing identical palettes, and by a one-off real-network run across 10 separate data folders, recorded as evidence.

## OD-0066 When a maker releases a stronger model, the family's shades are re-spaced (consistently)

- **Owner ruling, 2026-10-02 (chat, translated from Hindi/Urdu):** the colour engine must re-adjust shades as a maker's lineup changes. Example: before GPT-6 Astra existed, the family's intensity range was spread over Sol, Terra and Luna; when the stronger Astra is introduced, Astra takes the most intense shade and the older models are re-adjusted below it; likewise Muse 1.3 above 1.2. The adjustment must be consistent.
- **Effect (amends OD-0065's "a model's intensity must not drift as its maker adds models"):** a model's intensity is a pure, versioned function of its maker's CURRENT LINEUP (the set of that maker's models and their public data), not of the order or time a user saw them: the same lineup gives the same shades on every machine; when a new model appears, every user's app re-spaces that family as soon as its model list shows the new model, so all users converge on the same shades. Strength within a lineup is ranked by public signals in a fixed precedence, so a newer but weaker variant does not take the top shade. The owner did not set the order; it is an engineering decision chosen by measured agreement with an independent strength benchmark (2026-10-02, E:/Droppy/work/design/auto-colour/discovery.md: on 343 same-maker pairs of the real OpenRouter catalogue, price first agreed 75 %, class/tier words first (e.g. Luna < Terra < Sol < Astra) then version, release date and price agreed 88-91 %). The lineup is a global fact (the keyless public catalogue plus a baseline shipped in each build), not the user's own provider lists, which differ per account. A re-spacing is animated (shades glide to their new values; no snap), in line with the owner's no-sudden-transitions standard.

## OD-0067 A Settings control to refresh the colour palettes on demand

- **Owner ruling, 2026-10-02 (chat, translated from Hindi/Urdu):** Settings gets an option to refresh the colour palettes: when used, the update runs, finds out which latest models have appeared across the makers, researches them and refreshes everyone's palettes and shades accordingly.
- **Effect:** the refresh runs the same engine as the automatic checks (OD-0064) right away: every model source is checked, new models are placed in their families (OD-0066 re-spacing), new makers are looked up and get a palette, and the result is the SAME as the automatic path would produce (OD-0065: a manual refresh only brings forward what every user converges to; it never gives one user a different palette). It shows progress and a short summary of what changed; shades that change glide to their new values (no snap). It is new UI: designed in the app's glass language and approved on the canvas before it is built (proposed: Settings > Appearance, a "Colours" row with a Refresh button and a "last checked" line).

## OD-0068 Caption buttons contrast with every theme

- **Owner ruling, 2026-10-02 (chat, translated from Hindi/Urdu):** on the light (white) themes the Windows caption buttons (minimise, maximise, close) blend into the window so much that they cannot be seen; they must always be visible: on light themes dark (black or dark greys), and generally a colour that contrasts with each theme.
- **Effect (amends OD-0044's "N Turn Pulse" colours, which were white 65 % rings and a light glyph):** the glyphs and the hover rings take a colour that contrasts with the theme they are drawn on, for every theme in both appearances (dark on light themes, light on dark themes; the close button's rose hover ring a deeper rose on light themes), with a minimum contrast against the actual window backdrop at rest, on hover and when the window is inactive. The motion, sizes and timing of OD-0044 are unchanged.

## OD-0069 The standard: extremely refined, premium, Apple-grade; no overlaps when scrolling

- **Owner ruling, 2026-10-02 (chat, translated from Hindi/Urdu):** headings at the top (for example "General" or the Settings and chat titles) were seen overlapping content while scrolling; the reference videos must be compared carefully to see how Droppy Code handles this in a premium way, and the port must do the same: no overlaps or anything that makes the app look un-premium. At every cost the app must look extremely refined, well thought through and premium, as if a manufacturer like Apple had designed it: a high-budget application.
- **Effect:** a standing quality bar for all work: any overlap, collision, clipping, rough edge or unrefined detail is a defect, judged against the Mac videos where they show the case (OD-0055/INDEX.md method, full frame rate), and against Apple's own standards (OD-0045) where they do not. Scrolling under the toolbar, page titles and caption buttons follows the Mac's treatment as measured from the videos.

## OD-0070 Legacy Marrow features: 10-15 design variants each, one feature at a time

- **Owner ruling, 2026-10-02 (chat, translated from Hindi/Urdu):** for the features to be carried over from the legacy Marrow app, the UI is designed fresh: for each feature, 10-15 design variants are made on a canvas, the owner chooses one and decides where in the app it goes, and features are added one by one, so each is a premium addition and never looks like an unrefined carry-over from the legacy app (which was not refined).
- **Effect:** extends the earlier rule that legacy features carry capability only and are designed in Droppy Code's glass language, canvas-approved, after parity. No legacy feature UI is built before its variant canvas is approved; one feature at a time.

## OD-0071 Scrolling is soft and weighted everywhere, like the Mac

- **Owner ruling, 2026-10-02 (chat, translated from Hindi/Urdu):** wherever anything scrolls (in the chat and anywhere else), the scrolling must be soft and "premium weighted", as it feels on the Mac: it must feel premium.
- **Effect:** every scroll area (chat timeline, diff and terminal panels, Settings pages and sidebar, the thread sidebar, popovers, menus, panels, the command palette) uses one scrolling behaviour modelled on the Mac's as measured from the reference videos at full frame rate: eased, interpolated wheel steps, momentum with a natural decay for precision touchpads, a soft settle at the ends, animated keyboard and programmatic scrolls (e.g. scroll to bottom), and overlay scroll indicators as the Mac shows them; paced on the display's refresh with zero dropped frames (OD-0069, ED-237).

## OD-0072 Answers to the pending questions (2026-10-02)

Owner answers in chat, 2026-10-02, to the questions put with recommendations:

- **Windows accent near-black/grey:** show every option on the canvas (the Mac's default blue when the accent is grey/black; keep the lifted grey; always the Mac's default blue); the owner chooses by look.
- **Liquid at every effort level vs only at the top:** show both on the canvas; the owner chooses by look.
- **Effort scale:** the position within each model's own effort list (a model's top effort is its full look, its lowest the calmest).
- **Models run by harness providers (Command Code, Copilot, Cursor, OpenCode, Devin, Pi):** wear their MAKER's colour (as OD-0063 for OpenRouter); the provider's own colour remains for its own models and as the fallback.
- **Settings window first open (the ~0.2 s plain tint before the blur):** hide it behind a premium open transition (e.g. a fade-in, or the window's elements arriving into place); variants on the canvas.
- **Window corner wedge (unscrimmed blur in the 8 px rounded corner):** show both (filled with the scrim / left) on the canvas.
- **App updates:** the owner ships new versions himself as installers built in development sessions and shared with others. The installer detects an existing installation: an older version is upgraded in place; the same version reports that the app is already up to date; a newer installed version makes an older installer refuse with a clear message ("A newer version of Marrow (x.y) is already installed."); otherwise it installs normally. The in-app update option becomes the place to manually update the provider tools (Codex, Claude Code and the other CLIs) rather than the app itself (designed on the canvas). Nothing is signed or published without the owner's word (OD-0058).
- **Installed app name:** "Marrow" (Start menu and installer title). The install identity stays distinct from the owner's separate Electron app "Marrow" (ED-235's app id and folder), so neither can replace the other.
- **About page licence line:** "Marrow is licensed under the GNU AGPL v3.0 with attribution terms; the terminal is Marrow's own, built on Windows ConPTY." (OD-0012 H settled.)
- **Provider key link:** choose "a" or "an" by the name's first sound ("Get an OpenRouter key"), for every provider.
- **Fast mode below a model's top effort:** a new, better animation (e.g. electricity coursing through the tube); 10-15 variants on a canvas for the owner to choose.

## OD-0073 Fast mode is "Strike" (variant A2)

- **Owner ruling, 2026-10-02 (chat, "My choice", with a screenshot of the variant):** fast mode's animation is variant A2 "Strike" from the choices canvas https://claude.ai/artifact/8vQa4UTFxApeS1mbCSA8Uf (board FastA.dc.html; locked copy E:/Droppy/work/design/fast-mode-strike-A2-locked.dc.html): a faint leader leaves the knob, then the whole channel strikes in two or three flickers; the liquid flashes from inside and the channel glows out; small sparks play at the knob between strikes; in fast mode the liquid's currents run 1.5x faster. Shown on the canvas below a model's top effort (Claude, High) and at the top (DeepSeek, Gemini at Max), in dark and light.
- **Effect:** replaces OD-0063's "the fast-mode bolt animation stays constant" and OD-0072's open fast-mode choice: the port's fast look draws Strike over the model's own liquid (OD-0063 intensity) at every effort, built test-first to the locked canvas's timing, geometry and colours, cheap per frame (ED-233 GPU passes, ED-237 pacing), looping seamlessly, and stilled under reduce motion.

## OD-0074 The Settings window opens with "Rise" (variant 2)

- **Owner ruling, 2026-10-02 (chat, "My choice", with a screenshot of the variant):** the Settings window's open transition is variant 2 "Rise" from the choices canvas https://claude.ai/artifact/8vQa4UTFxApeS1mbCSA8Uf (board SettingsOpen.dc.html; locked copy E:/Droppy/work/design/settings-open-rise-2-locked.dc.html): the glass fades in, then the sidebar items and the page rows rise into place in a quick wave, 0.26 s from when the glass is ready.
- **Effect:** settles OD-0072's Settings-open choice: the plain tint before the Acrylic blur (ED-236) is never seen; the window stays hidden (cloaked) until its glass is ready, then Rise plays. Built test-first to the locked canvas's timing and offsets, on the frame clock, with reduce motion showing the window without the wave.

## OD-0075 Choices: liquid at every effort, corner filled, Tools page, accent picker, OpenRouter picker

- **Owner answers, 2026-10-02 (chat), on the choices canvas https://claude.ai/artifact/8vQa4UTFxApeS1mbCSA8Uf:**
  - **Liquid scope:** liquid at every effort level (settles OD-0072; each model and effort at its OD-0063 intensity).
  - **Window corner:** the wedge is filled with the window's scrim (settles OD-0072).
  - **Tool updates:** variant 04, a Tools page in the Settings sidebar listing each provider tool's installed and latest versions in columns, with Update, Update all and Retry on failure.
  - **Accent:** the app offers several accent themes to choose from in Settings, so the accent becomes the user's preference; the Windows accent is the shipped default. New UI: designed on the canvas (variants) before it is built.
- **OpenRouter model picker (OD-0060's canvas https://claude.ai/artifact/BNoUoc4vvCLK5pHzYMmVcA):** approved, except that the list overlaps the currently selected model's chip below it; the overlap is fixed on the canvas first, then the picker is built.

## OD-0076 Every change is transitioned, never cut

- **Owner ruling, 2026-10-02 (chat, with two screenshots of the OpenRouter picker canvas: the search "k" with seven models, then "ki" with one):** "The transformation between the two screenshots, that's what I'm talking about, no static changes like the scene cuts from screenshot A and then immediately moves to Screenshot B. It always must be a smooth transition, in this case the card must smoothly collapse down from Screenshot A to Screenshot B." and "I want smooth transition based movements throughout the app, no static A-B movements, always transitioned."
- **Rule:** nothing on screen changes by a cut. Rows that appear or disappear fold open or shut; a card, popover, panel or window that changes size eases to the new size; content that swaps crossfades; controls that appear (a clear button, a check mark) fade and scale in; a label whose width changes eases its container's width. Where the Mac animates, the port matches the Mac's motion (OD-0069). Where the Mac changes state without animation, the port still transitions in the Mac's motion language (hover about 0.1-0.13 s, popovers grow about 0.2 s, size changes about 0.3 s on an ease curve), so this is an owner-ruled deviation wherever the source snaps. Every transition runs on the frame clock (ED-237) with no single-frame jump, and the system's reduce-motion setting stills them.
- **Picker canvas (https://claude.ai/artifact/BNoUoc4vvCLK5pHzYMmVcA, version 4):** narrowing "k" to "ki" folds the six rows that stop matching while the card eases down onto the model chip in about 0.33 s, and clearing the search unfolds them in the same time; measured in headless Chrome at every frame: monotonic, the largest 60 Hz frame step 11-12% of the travel, never closer than 10 px to the chip (E:/Droppy/work/design/openrouter-picker/).
- **Count in the picker (owner ruling, 2026-10-02, chat, with a crop of the "1 of 7" label):** "Instead of 1 of 7, we can just shift the number of models to 1 model, 2 models, 3 models, etc." The count reads the number of models shown: "17 models" with no search, "7 models" for "k", "1 model" for "ki" (singular for one), "0 models" when nothing matches; a change crossfades (the old count fades out in about 0.09 s, the new one rises in after it). With a search the top match is highlighted; the arrow keys move the highlight and Return picks it (canvas version 5).
- **Effect:** an app-wide transition audit lists every state change in the port with the Mac's behaviour and the transition the port gives it; each is then built test-first with a frame-timing check.

## OD-0077 Strike needs varied lightning, not one repeating bolt

- **Owner ruling, 2026-10-02 (chat, Hindi; meaning):** the Strike electricity inside the slider (OD-0073) is very good, but the same kind of lightning keeps repeating; to the human eye it reads as an easy loop. Show lightning with different trajectories; enhance the locked Strike a little.
- **Effect:** Strike stays the chosen look (its colours, glow, flicker character, leader and sparks), but successive strikes must differ visibly in path (where the channel runs in the tube, how it bends, forks and branches), and the rhythm must not read as a fixed cycle. Still premium and restrained, never noisy; deterministic from its seed so it can be tested, and with no period a viewer can notice. Enhanced variants go on the choices canvas beside the locked A2 for the owner to pick by look (OD-0070); the pick is then built over ED-248, test-first.

## OD-0078 Lightning bursts the bubbles it touches

- **Owner ruling, 2026-10-02 (chat, Hindi; meaning):** the owner noticed the bubbles' set pattern was also disturbed when the lightning came. When a lightning bolt touches a bubble, that bubble bursts right there: animate it so whichever bubble the lightning touches pops on the spot.
- **Effect:** in fast mode (Strike, OD-0073, OD-0077), the moment a bolt's channel or one of its branches first reaches a bubble (as the leader passes it, or at the strike), that bubble pops where it is with a short burst animation in the liquid's own light, and it is gone until its own cycle brings it back, so the bubbles otherwise keep their pattern undisturbed. Deterministic (from the bolt's and the bubble's own seeded motion), transitioned (OD-0076), premium and restrained. Designed on the choices canvas with the Strike variants (OD-0077) for the owner to see, then built over ED-248, test-first.

## OD-0079 In fast mode the bubbles simmer

- **Owner ruling, 2026-10-02 (chat, Hindi; meaning):** since this is shown in fast mode, raise the bubbles' intensity too: the speed at which they move, a little like water lightly boiling; show it that way.
- **Effect:** amends ED-248 and the Strike spec, where the bubbles kept their normal speed in fast mode. In fast mode the liquid simmers: bubbles rise faster and somewhat more often, with a gentle boil character (small bubbles nucleating near the bottom, a slight wobble as they rise), premium and restrained, never a chaotic churn. Entering and leaving fast mode ramps the intensity smoothly with no jump in any bubble's position (OD-0076). Lightning still bursts the bubbles it touches (OD-0078). Designed on the choices canvas with the Strike variants (OD-0077), then built test-first.

## OD-0080 Marrow comes with the skills Claude and ChatGPT ship

- **Owner ruling, 2026-10-02 (chat, Hindi; meaning):** ChatGPT and Claude come with pre-installed skills that enable all kinds of different work. Research all of Claude's and ChatGPT's pre-installed skills, and pre-install all of them inside the Marrow app.
- **Effect:** a new capability beyond the pinned source (owner-ruled). The Mac app already surfaces the skills each provider CLI has (Codex skills/list as slash commands, Provider.swift:297-347; Skill tool calls from Claude, Copilot and Command Code), so Marrow ships its own bundle of these skills and makes them available to the providers it runs. Research first: every pre-installed skill of Claude (Anthropic) and ChatGPT/Codex (OpenAI), what each does, its source, its licence and whether it may be redistributed, its dependencies, and how each provider CLI Marrow runs can load skills without overwriting the user's own. Only skills whose licence allows bundling are shipped; the rest are listed for the owner with the reason. Any new UI (for example a Skills page in Settings) is designed as canvas variants first (OD-0070), then everything is built test-first.

## OD-0081 More, and more premium, accent colours

- **Owner ruling, 2026-10-02 (chat, viewing the accent canvas board D):** "we also need to add more accent options, give more premium color accent options." Earlier in the same review the owner said of D3 (the Look card: Theme as System | Light | Dark | named-theme segments over a live mini window, then the Accent row) "I think I like this one", with the Nord label overflow to fix (fixed on the canvas) and the preview to be generic and work for every theme (done: all 26 themes).
- **Effect:** the accent set grows beyond the ten (Windows accent, Blue, Purple, Pink, Red, Orange, Yellow, Green, Teal, Graphite) with a curated premium palette, designed on the canvas (board D first) with a layout that keeps the row calm as the count grows, every colour legible where it is drawn (see the in-situ contrast finding of the accent review), and every change transitioned (OD-0076). The pick between A to D stays open until the owner confirms.

## OD-0082 Every strike style, mixed at random, and the orb strikes first

- **Owner ruling, 2026-10-02 (chat, with a screenshot of the "strikes that never repeat" draft board: A2 locked, V1 Wander, V2 Weather, V3 Living storm):** "I love all of them, I want all of them to be selected and randomized! But I want the orb to get first struck or glow before the lightning passes through it to the slider, as if the orb is generating the lightning or the orb is getting struck first and then lightning passes into the tube. Get this done and first update this for me to review first then I will lock it."
- **Effect:** fast mode's lightning draws each strike from all four styles at random (deterministic from the seed and strike index, no noticeable period): A2's centre run, V1's path families, V2's rhythm, strength and partial strikes, V3's rare forks, twins, reverse strikes and wall runs; bubble bursts (OD-0078) and the simmer (OD-0079) stay. Before every bolt leaves it, the knob's glass orb charges or is struck first (a brief build of light in the bolt's tint on and inside the orb), then the bolt passes from the orb into the tube. Shown on the choices canvas for review; the owner locks it after review, then it is built over ED-248 test-first.

## OD-0083 An ambient light around the composer

- **Owner ruling, 2026-10-02 (chat, with a screenshot and the CSS of the Uiverse.io button by SelfMadeSystem: a slowly rotating orange and blue glow behind a dark rounded button, a tighter inner rim glow, and a 3 px dotted halftone backdrop):** "I want you to animate this light in a premium way around the composer, not in a too distracting way but in a premium ambiance adding way."
- **Effect:** a new ambient effect beyond the pinned source (owner-ruled): a soft light around the composer in the spirit of that button (rotating two-tone glow, rim light, halftone), calm and premium, never competing with the conversation. Designed as canvas variants first (colours, motion, how it responds to focus, typing and a running turn, dark and light), the owner picks, then it is built test-first on the frame clock with a bounded per-frame cost (no per-frame full-size blur), reduce motion stilled, nothing cutting (OD-0076). Uiverse.io content is MIT-licensed; credit SelfMadeSystem in THIRD_PARTY_NOTICES if any of its code or exact values are carried over.
- **Second reference, 2026-10-02 (chat, screenshots of "Border beam", an npm package `border-beam` for React, React Native and SwiftUI: "An animated rainbow glow that rides the border of any card, button or input", with a playground offering Family Rotate or Pulse, Type such as Pulse Inner or Pulse Outside, Color Colorful or Mono with palettes such as Ocean, Sunset and Forest, and Duration):** "add this as well as a comparison to the other lights I gave for the composer. Make sure to add all the options in the playground." The canvas therefore gets one composer-light PLAYGROUND: the real Marrow composer in a chat, its states (rest, typing, running), dark and light, and a control panel to switch between the Uiverse light (with its own options), Border beam (with every option its playground offers) and the designed variants, all transitioned. Border beam's licence is checked before any of its code or values are reused.

## OD-0084 Strike is locked: every style mixed, the orb charges first; bubbles fixed before the board is final

- **Owner ruling, 2026-10-02 (chat, viewing the "strikes that never repeat" board):** "I like charges! Lock it, but I'm seeing the smaller slider has a smaller loop for bubbles? I keep seeing more bubbles at the far left loop, I thought we locked a more random 2 minute loop? Also, bubbles need to burst upon getting hit by lightning strikes?"
- **Locked:** fast mode's lightning is the Mix row (each strike draws its style at random from A2's centre run, V1, V2 and V3, about 1 in 5 centre runs, rare V3 events about 1 in 16 and never two in a row, one coherent rhythm) with the orb in "Charges" mode (the light gathers inside the orb, then the bolt leaves its edge into the tube). This supersedes A2 alone (OD-0073) and settles OD-0077 and OD-0082.
- **Bubbles, to fix on the board before it is built:** the boards still use the older CSS bubbles with fixed short loops (in the small tube they keep circling near the left end). The bubbles must be the locked never-repeating bubbles of OD-0052 (canvas version 22: every rise of every bubble new, each bubble keeping an even stretch of the whole tube, random pauses, now and then one starting anywhere) in both tube sizes, simmering in fast mode (OD-0079), and every bubble a bolt touches must visibly burst (OD-0078).

## OD-0085 Both composer lights ship, chosen in a new Settings "Customization" section; Border beam is the default

- **Owner ruling, 2026-10-02 (chat, Hindi; meaning), after the two composer-light previews (the Uiverse light and Border beam on the real composer):** "I like both: the Border beam and the Uiverse light. Include them with their settings: make a Customization section in Settings and put them there, so a user who prefers the Uiverse light selects it, one who prefers the beam light selects that, and sets up its settings. Integrate it into Settings. Let's ship both of them in the app, and by default ship the beam light."
- **Effect:** the composer light (OD-0083) ships with two styles, Border beam (the default) and the Uiverse light, each with its user-adjustable settings (the options of the composer-light playground: for Border beam its family, type, colours and palettes, speed, strength, brightness, saturation, glow size, hue range and the rest; for the Uiverse light its colours, speed, intensity per state, halo, rim, halftone and breathing), plus a way to turn the light off. A new "Customization" page in the Settings sidebar holds them with a live composer preview; new UI, so designed as canvas variants first (OD-0070) and built test-first after the owner picks. Border beam's default settings are taken from the playground once the owner has tuned or approved them. Border beam is MIT (Jakub Antalik) and the Uiverse button is MIT (SelfMadeSystem): both credited in THIRD_PARTY_NOTICES wherever code or values are carried over.

## OD-0086 Try premium orbs as the slider's knob; the voice orbs are kept for a voice feature

- **Owner ruling, 2026-10-02 (chat, Hindi; meaning), with a link to an orb shader playground (lersent001.github.io/orb, presets Siri Wave, Voice Membrane, Particle Ribbons, Crystal Drop, Violet Ember, Refractive Gel, Chromatic Metal, Aurora Veil, Frost Flow, Liquid Chrome, Iridescent Opal, Prismatic Field, Neural Plasma):** the slider is locked (the randomised lightning, the orb charging first, the bubbles bursting and never repeating). Now test premium orbs as the slider's knob: put these orbs on one page in the slider and show how they look and how the lightning reacts with them. Do not use the top two voice ones (Siri Wave, Voice Membrane): they are kept for voice.
- **Voice (future, owner-ruled new feature):** Marrow gets voice typing using OpenAI's Whisper (the owner said "Whisper 3", read as Whisper large-v3; to be researched: local model or API, licence, Windows audio capture), with the Siri Wave and Voice Membrane orb styles reserved for its UI. Designed on a canvas before it is built.
- **Effect:** a canvas page shows the locked slider with each of the other eleven orbs as its knob, in both tube sizes, dark and light, with the knob reacting to the lightning (charging before each bolt, flaring as it leaves), for the owner to pick. The orb project's licence decides whether its code may be reused; otherwise the looks are re-created with Marrow's own shaders.

## OD-0087 Knob orbs wear the model's own colour

- **Owner ruling, 2026-10-02 (chat, Hindi; meaning), after the knob-orb gallery:** the knobs come in different colours, so their contrast does not match the slider. Make the knob match the model instead: for example Claude's knob is in Claude's orange; whatever animation runs inside is in that model's theme, so it blends with the liquid and reads as one material; still with an obvious glass look, its border with refraction, "you get my point".
- **Effect:** every knob orb (OD-0086) is drawn in the colours of the slider's own liquid for the selected model and effort (the maker's palette and intensity, OD-0063, with dark, mid and light stops, not a single hue), keeping each orb's own motion and pattern and its reaction to the lightning (OD-0084), inside a clear glass shell with refraction, dispersion and a rim highlight at its edge. Shown for several makers (Claude terracotta, OpenAI silver-white, Gemini, DeepSeek, Grok obsidian, NVIDIA) so the blend can be judged across the palette.

## OD-0088 The accent picker is D3 with 22 colours (locked)

- **Owner ruling, 2026-10-02 (chat):** "Lock it D3 with the 22 colours", for board D of the choices canvas https://claude.ai/artifact/8vQa4UTFxApeS1mbCSA8Uf (version 9; locked copies E:/Droppy/work/design/accent-d3-locked.dc.html and E:/Droppy/work/design/accent-palette-locked.json).
- **What is locked (variant D3, the Look card):** at the top of Settings > Appearance, one card holds a live, generic mini window that re-tints as the user picks, the Theme as a segmented control (System | Light | Dark | the named theme or "More themes", opening the full theme menu of all 26 themes), and the Accent row: the ten Classic swatches (Windows accent, the default; Blue, Purple, Pink, Red, Orange, Yellow, which reads as Gold in light; Green, Teal, Graphite) and a "More" capsule that folds open the twelve Signature swatches (Ocean, Denim, Indigo, Lilac, Orchid, Rose, Coral, Bronze, Sage, Alpine, Mint, Slate). Under a named theme the first swatch is that theme's own accent (its default) and any other pick overrides it; the window follows the theme's appearance. Every colour reaches 3.1:1 on the real Settings card and 3:1 on the reference panel, dark and light (the palette's own hexes, lifted against the surface the accent is drawn on). Names only in the UI. Every change transitions (selection ring, fold, crossfades), with full keyboard navigation.
- **Effect:** settles OD-0075's accent question and OD-0081. D3 replaces the source's Theme capsule (SettingsView.swift 1155-1212) with the segmented control and adds the Look card: an owner-ruled deviation. The accent applies wherever the source applies the theme accent (Chrome.accent: prominent buttons, toggles, progress, links, selection). Built test-first to the locked board.

## OD-0089 Composer lights approved to ship: Border beam on by default, tuned in Settings > Customization with a live preview

- **Owner ruling, 2026-10-03 (chat, after the interactive Composer Lights playground with the fitted Border beam, https://claude.ai/artifact/Dmp174VLkzedB9RNJVydho):** "Alright, I'm satisfied with the Composer lights, ship it. By default it will be on beam, its settings should be accessible in settings > Customization with a live preview to be able to make customizations as needed for the user."
- **Locked:** the composer lights as the playground draws them (`work/design/variants/project/ComposerLights.dc.html`, generated by `work/design/composer-lights/playground/src/gen.py` and `logic.js`; Border beam is the border-beam 1.4.1 style generator ported, tables in `work/design/composer-lights/border-beam-tables.json`). Default: Border beam, with the playground's opening values (Geometry "Fitted to the composer", Family Rotate, Type Large, Color Colorful, theme Auto, the package's defaults for the studio and package rows, Duration Auto: 3.69 s a turn). The other choices stay: the Uiverse light with its options, Off, and every Border beam option the playground offers. Settles OD-0085's open item (Border beam's default settings).
- **Effect:** builds OD-0083 and OD-0085 into the app. The light is drawn around the real composer on the frame clock (composition thread, a bounded per-frame cost, no per-frame full-size blur), responds to rest, typing and a running turn as in the playground, stills under reduce motion, and every change of style or setting transitions (OD-0076). Settings > Customization (a new sidebar page) holds the style choice and its settings beside a live composer preview that shows each change as it is made; the user's choices persist and apply to every composer at once. The page is new UI, so its layout is shown to the owner as an interactive design first (OD-0070) while the light itself is built test-first against the playground. Credits: border-beam (MIT, Jakub Antalik) and the Uiverse.io button (MIT, SelfMadeSystem) in THIRD_PARTY_NOTICES.
- **Amendment, 2026-10-03 (chat, viewing the interactive Customization design, https://claude.ai/artifact/AbgSdYZoaZi9zBzN7HWo6Q):** "I like the B stage though, lock it, just implement that free live preview logic too", after "while scrolling down, as long as the options are related to the composer, composer stays frozen when scrolling down, so user can see effect of each option in the live preview", and "the dots color selection should be in customization as well". LOCKED: layout B (Stage): a stage preview of the composer at the end of a conversation with its model and state switches, the style chosen from three cards, then the style's settings; the stage stays pinned at the top while every composer-related section scrolls under it (compacting smoothly as it pins) and releases only at the first section that is not about the composer. New setting "Working dots": the colour of the composer's working dots (ComposerWorkingDots.swift; the source draws them in the accent): Accent (default) | Model colour | Custom (the accent palette's swatches), previewed live in the running state. The preview's model menu opens above its chip, as the source's popover does. Source: `work/design/customization/` (src/gen.py); the voice orbs there are placeholders until the exact ports land.

## OD-0090 The effort slider's knob is the Jewel orb (locked): larger than the track, lifts when pressed, the gem always moving

- **Owner ruling, 2026-10-03 (chat, after three rounds on the interactive Jewel page, https://claude.ai/artifact/CH1VUKJLTkWUG8sRcqFG87, version 3):** "Slider done, ship it! I love it!" Earlier the same day, on the knob variants: Jewel is the one ("Jewel is liked"); "the animation inside the orb is completely static"; "the knob needs to be bigger: the earlier knob sits larger than the slider"; "when the user clicks the knob to drag, it should pop out a little, enlarge slightly, so you know you picked it up"; then "Fasten the animation within the orb ... and also increase the knob size furthermore"; and "in preview give me option to choose different models to view their colors too".
- **Locked (the page and its sources frozen in `work/design/jewel-knob-locked/`):** the knob is the Jewel material (variant V2 of OD-0087, the Violet Ember orb program from Liquid Orb Editor, MIT, LerSent001, re-coloured from the selected maker's own liquid by `jewel()` in knob-variants.js), with the refractive glass shell, the tube refracted at its rim and the OD-0084 strike: the orb charges before each bolt, the bolt leaves its centre, bubbles burst. The gem's folds move at rest at speed 2.6 and up to 4.2 as the charge rises (the source's Idle 0.31 read as a still picture). Size: the knob's diameter is 1.4 times the track's height, so it stands proud of the track above and below and sits flush with the track's end at either extreme (on the app's 34 px track: 48 px, where the source has 42); the fill runs to the knob's far edge and stop centres sit a knob radius in from each end, as EffortSlider.swift 746-760. Press: pressing the knob lifts it to 1.06x where it is, never sliding it sideways (owner, same day: "Just make sure the knob pops out at the point where it is, not on the sides."), and the drag keeps the grip's offset; pressing the track away from the knob springs the knob to the pointer; the lift is (the source's scale, response 0.22 s, damping 0.8) with a deeper shadow fading in; it follows the drag, the effort changing as it passes each stop; on release it springs onto the nearest stop and back to 1x (response 0.32 s, damping 0.78). Colours follow the model's maker (liquid-palettes.json, 30 makers), crossfading over 0.6 s when the model changes.
- **Effect:** supersedes the source's plain Liquid Glass lens on the effort slider (EffortSlider.swift 777-792) and the 42 pt thumb size: owner-ruled deviations. Built test-first to the locked page, on the composition thread (the orb as an SkSL shader, bounded per-frame cost, no per-frame allocation), reduce motion stilling the gem's flow (light only), every change transitioned (OD-0076). Credit Liquid Orb Editor (MIT, LerSent001) in THIRD_PARTY_NOTICES; Neural Plasma's noise caution (RESEARCH.md) does not apply to Violet Ember, which is checked again before shipping.

## OD-0091 Voice typing with Whisper: a voice orb in the composer, Siri Wave by default; the attachment button moves to the left

- **Owner ruling, 2026-10-03 (chat, with the link https://github.com/openai/whisper):** "You know what I'm about to say lol. Bring the attachment icon to the left side, in place of the attachment icon, we will place the voice orb I told you about earlier, with siri orb shipping as default but the option to shift to the other one in customization in settings. The orb must be slowly animated, when hovering it must speed up and glow, when clicked on it should pop out and enlarge a bit while speeding up and slowing down synchronized to the voice input, while processing it should speed up furthermore."
- **Effect:** the voice feature reserved in OD-0086 is now ordered. The composer's attachment button moves to the composer's left side; in its old place sits a voice orb that starts and stops voice typing, transcribed with OpenAI's Whisper (https://github.com/openai/whisper, MIT; the engine (local model or API), model size, Windows microphone capture, privacy and download size to be researched and put to the owner before building). The orb is the Siri Wave preset of Liquid Orb Editor (MIT, LerSent001) by default; Voice Membrane is the alternative, chosen in Settings > Customization (OD-0085, OD-0089) with a live preview. States, every change transitioned (OD-0076): at rest a slow animation; on hover it speeds up and glows; on click (listening) it pops out and enlarges a little, its speed rising and falling in step with the voice's level (the source's audio response, orb-audio.ts); while the speech is transcribed (processing) it runs faster still; then it settles back. New UI beyond the pinned source (owner-ruled): shown to the owner as an interactive design first (OD-0070), then built test-first; credits in THIRD_PARTY_NOTICES (Whisper MIT, Liquid Orb Editor MIT, and whatever engine and capture library are chosen).
- **Amendment, 2026-10-03 (chat, the same day):** "it must return to normal speed during pauses, while speaking as per the voice input it must increase speed, highest speed will be when its processing audio. Once the user is done speaking he will click the orb again and all the spoken text must be processed in the composer but not sent yet. The user will click on send himself. We can maybe add an option in settings to allow the user to choose if he'd like the orb to directly send as prompt or just leave the text in composer." So: while listening, the orb's speed follows the voice (back to its normal resting speed in pauses, faster as the voice rises); the highest speed is while processing. A second click on the orb stops listening; the whole transcript is then inserted into the composer and NOT sent: the user sends it. A setting in Settings > Customization (next to the voice orb choice) lets the user choose "Leave the text in the composer" (the default) or "Send it as the prompt".
- **Second amendment, 2026-10-03 (chat, a screenshot of "Voice" on libraries.dev: the npm package `voice-glow` 0.2.1, MIT, Jakub Antalik, "A colorful glow that rises with your voice from the bottom of a chat input or phone screen, then sweeps side to side while the reply is thought through", and a 30 s screen recording of its playground, Type "Chat input", Source "Demo voice" then "Processing"):** "Composer effects during voice and while processing voice. Review frames well to understand well." Read from the recording frame by frame: while listening, a soft multi-colour glow (orange, pink, violet, cyan, green) rises from the composer's bottom edge inside its rounded shape, a mound of light whose height and brightness follow the voice (low in pauses, up to about half the composer's height on loud syllables), its colours drifting and its peak wandering along the bottom, with a fine bright rainbow line along the bottom edge; on "Processing" the glow sinks away and a smaller, tighter blob of the same colours sweeps side to side along the bottom edge, wrapping up into each bottom corner (about 0.75 s from one corner to the other), with the bright edge line under it. So the composer shows this voice glow while listening and this sweep while the speech is transcribed, together with the voice orb's states, built from the voice-glow package (its code read and ported as border-beam was, its licence carried into THIRD_PARTY_NOTICES), every change transitioned; the composer light (Border beam) gives way to it while voice is active and returns after.
- **Third amendment, 2026-10-03 (chat, Hindi; meaning):** "One thing I forgot to say: when the voice orb is clicked it will obviously grow, but on its left side a cross button should also slide in, so that if the person wants to cancel the voice, the voice is cancelled. And I think live interpretation is better: as the person keeps speaking, it keeps writing in the composer; and while it is writing, the composer is in its writing state, so the animations that play while typing play along too." So: on the click that starts listening, as the orb lifts and grows, a cancel button (a cross) slides out from behind the orb to its left; pressing it (or Esc) cancels: listening stops, nothing is kept (the words written so far are withdrawn, transitioned) and the orb settles back; the cross slides back in. Transcription is LIVE: words are written into the composer as they are recognised, while the user speaks (partial words may be revised as the model hears more, never jumping: transitioned), and while words are being written the composer is in its typing state, with the composer light's typing response (OD-0089) and every typing animation the composer has. The second click on the orb stops listening; a short final pass settles the text (the orb's fastest "processing" speed for that moment); then, by the setting, the text stays in the composer unsent (default) or is sent. Engine note: this needs streaming recognition (whisper.cpp's sliding-window streaming or chunked passes with a final pass), measured on the benchmark before the model recommendation rules are set, since a CPU-only machine may need a smaller model to keep up live.
- **Fourth amendment, 2026-10-03 (chat, Hindi; meaning, with a reference screenshot of the Siri Wave orb inside a dark capsule reading "Thinking..."):** "We can also implement this idea: when you click the voice orb, the orb opens to the side and 'Listening' appears like this, in place of 'Thinking'; Listening, or 'Hearing you', or something; 'Transcribing' once you start speaking, and 'Listening' when you go quiet." So on the click that starts listening the orb opens sideways into a capsule with a status label beside the orb: "Listening..." while the user is silent, "Transcribing..." while speech is being recognised (with hysteresis so short gaps do not flicker), and a label for the final pass; the capsule folds back into the orb on stop or cancel; designed with the cancel cross and the composer's space on the interactive page first. The orbs themselves are the exact source exports the owner pasted from the site's Copy code (Siri Wave, style 9; Voice Membrane, style 19): `work/voice/reference/orb-exports/` (seeds.json and the two export pages), never a re-creation.
- **Fifth amendment, 2026-10-03 (chat, a screenshot of variant B of the interactive Voice Composer design, https://claude.ai/artifact/JFqH3AwciRoxDjeZ9hCFLu: a capsule with a cross at its left end, "Listening...", and the Siri Wave orb at its right):** "Give it a glass ui look though, its excellent". Chosen: variant B, the orb opening leftward into a capsule inside the composer, with the cancel cross inside the capsule at its far end; the capsule is drawn in Marrow's glass material (frosted, translucent over the composer and its light, a hairline rim with the soft highlight and fringe of the app's glass), dark and light.
- **Sixth amendment, 2026-10-03 (chat, a screenshot of the Voice Composer design with Variant B "Pill in the composer", Orb Siri Wave, Size 32 px, Attachment Inside, Dark):** "Ship this as default, however give customization settings in customization section". LOCKED defaults: the capsule in the composer (variant B, in glass per the fifth amendment), the Siri Wave orb, the orb at 32 px, the attachment button inside the composer at its left. Settings > Customization gains, beside the voice orb choice and "After voice input": the listening style (Pill in the composer, the default | Cross beside the orb | Floating pill above the composer), the orb size (32 px, the default | 26 px) and the attachment button's place (Inside the composer, the default | Outside), each previewed live in the stage.

## OD-0092 Voice models: every Whisper model offered in Settings, a hardware check that recommends one, run automatically on first install

- **Owner ruling, 2026-10-03 (chat, answering the voice research's decisions, `work/voice/RESEARCH.md`):** "Install all models of voice, give option in settings to choose and feature an option that detects user's hardware and recommends model based on it. The user must have access to it via a button and would be able to check anytime he wants. By default, ship it with the setting to check user's hardware on first install and select model automatically."
- **Effect (on OD-0091):** voice typing runs Whisper on the user's own computer (whisper.cpp through Whisper.net, MIT, as the research recommends). Settings offers every Whisper model (tiny, base, small, medium, large-v3 and large-v3-turbo, with their English-only and quantized variants where useful), each with its size, speed and accuracy shown plainly and its install state. A "Check my hardware" button, usable at any time, detects the CPU, memory and graphics card (and which GPU backend works) and recommends a model with the reason; the user may take the recommendation or pick another. By default, on first install, the check runs by itself and the recommended model is selected and installed automatically. Read as: every model is available and installable from Settings; the selected model is downloaded with progress, resume and a checksum (installing all of them up front would add about 6 GB; the owner may ask for that instead). Also, the owner gave leave to download whisper.cpp and every Whisper model to benchmark them on the owner's PC (AMD Ryzen 7 9800X3D, NVIDIA RTX 4080, 31 GB) so the recommendation rules rest on measurements. Open, defaulted until the owner says otherwise: the voice language defaults to Auto-detect with an explicit Language setting (English, Hindi, Urdu and the rest); OpenAI cloud transcription is an opt-in second engine; silence: no auto-stop, a 5-minute cap, "no speech" inserts nothing; a one-time privacy note per engine.
- **Amendment, 2026-10-03 (chat, after asking whether better models exist, e.g. for Hindi and Urdu):** "Let's just stick to the models I gave you the go ahead on." The model list is OpenAI Whisper only (tiny, base, small, medium, large-v3, large-v3-turbo and their English-only and quantized variants); other speech models (NVIDIA Parakeet/Canary, Voxtral, Omnilingual ASR, Indic models, cloud services) are not offered. The OpenAI cloud engine stays as researched (whisper-1 or OpenAI's transcription models), opt-in.

## OD-0093 The strike's look is locked as "Flash streaks + Ink": a branching plume in each maker's light, dark ink on pale liquids

- **Owner ruling, 2026-10-03 (chat, after the interactive lightning page https://claude.ai/artifact/VW82uAQUHorutkBD6pqVTN, version 2, which opens on "Flash streaks + Ink"):** "Lightning is now perfect. Ship it." The path there, the same day: on OpenAI's silver liquid the locked bolt (screen-blended white and tint) was invisible ("you need to ensure the lightning strikes contrast with each different model's colour palette and bubbles too"); a deep-tone rim, then an electric-blue rim, was rejected ("not looking premium at all"); "I like CW's Flash's streak of lightning, maybe we can learn from it?"; then "the black one you did earlier looked good: not pure black, not one static colour, a gradient, a little different in different places, but mainly black ... The Flash's colour is a different thing; I was talking about the streaks"; and "the current lightning comes as one branch with small faint branches; the Flash's has multiple branches: look at how it branches out".
- **Locked (page frozen as `work/design/jewel-knob-locked/strike-light-locked.html` with its evidence in `strike-light-shots/`; it draws beside the locked engine `strike-engine.js`, which is unchanged):** every strike keeps OD-0084's rhythm, styles, leader, flickers, afterglow and the orb's charge, and is drawn as a Flash-style plume: the strike's own channel plus 6 filaments on the 40 px tube (4 on the 26 px tube) leaving the orb together, fanning out over the tube's height, arcing and crossing, each kinked at every vertex, splitting forward 2 to 3 times (and again 1 to 2 times on the large tube), thinning over its last 40 % with frayed tips, each flickering on its own with a short two-step trail. On a dark or mid liquid the plume is the maker's own light (the locked tint ratios: outer 0.14, inner 0.36, white core 0.92). On a pale liquid, in proportion to how pale it is (ink = clamp((2.9 - contrast of white against the light stop) / 1.0, 0, 1): 1.0 for OpenAI, Arcee, Devin, Cohere; 0.88 OpenCode; 0.75 OpenCode Go; 0.68 Copilot; 0.31 AWS; 0 for Claude, Grok, DeepSeek and every dark liquid), the plume is dark ink: a near-black core (OKLCH L 0.15 with a breath of the maker's deep hue), a body graded along the tube between L 0.22 and 0.26 with its hue turning 14 degrees and its stops drifting slowly, and a blurred halo from charcoal to the maker's deep tone (neutral liquids take a cool slate hue); the white bolt fades by the same amount. Bubbles on pale liquids keep the neutral dark rim (FastC.dc.html's .bub.dark). Every bubble a drawn line touches (every filament and branch, at its drawn width, any bubble from 3 % opacity) bursts on the spot, audited at zero misses over 60 s on every maker.
- **Effect:** supersedes the light-liquid part of OD-0090's evidence and refines OD-0084's look (not its timing or styles). Ported with the OD-0084 Mix engine into the port's fast-mode strike, test-first against the locked page, on the composition thread with a bounded per-frame cost (the page's slider paces at 16.7 ms p99 17-21 ms; its 11-tube comparison strip is a review aid only).
- **Amendment, 2026-10-03 (chat):** "when a model is shifted or the app starts or whatever, don't start the lightning from the same start, start it randomly from a clean place, don't start it from a point where lightning is initiated, mark clean points and randomize starts too." So the strike sequence (deterministic and endless) is entered at a RANDOM CLEAN POINT each time a slider's lightning starts (app start, a slider appearing, fast mode turned on) and when the model changes: a clean point is a moment in a quiet gap between strikes, outside every strike's window (its charge, leader, return stroke, flickers, afterglow, the knob's spark) with a margin before the next charge begins, so no start ever lands in or just before a strike. The sequence marks its clean windows (from the plan's strike times, leads, charges and the 1.9 s glow), and a start picks one at random (a fresh random draw each time, so two starts never replay the same strikes). A model change while lightning is showing never cuts: the re-entry waits until the current strike has finished and jumps from a quiet moment to a quiet moment; the bubbles run on their own clock and are untouched.

## OD-0094 The effort title's gradient drifts slowly at rest

- **Owner ruling, 2026-10-03 (chat):** after being told that in the reference video the effort title's gradient is static at rest and passes along the letters only while the effort changes (`work/reference-video/study/effort-title-2026-10-03/`), the owner answered the offer "If you'd also like the gradient to drift slowly at rest, which the original doesn't do, tell me" with: "Yes, let the gradient drift slowly at rest too".
- **Effect:** an owner-ruled deviation from the source (EffortSlider.swift titleStyle, a static LinearGradient). Wherever the effort title is drawn as a gradient (a pair's lead-to-heads colours, the provider's brand title at maximum effort, maximum together with fast mode's gold), its colours drift slowly along the letters at rest: a seamless loop (the gradient mirrored so it never jumps), slow and calm (a period of several seconds), continuous with the colour passing along the letters during an effort change (the change's own motion takes over and hands back without a jump), stilled under reduce motion, and cheap (a shader or brush transform on the frame clock, no per-frame allocation, frames only while the title is visible). A single-colour title (a plain effort, or fast mode's gold alone) stays as it is.

## OD-0095 Popovers carry the Mac's arrow tip and sit at the Mac's distance from their button

- **Owner ruling, 2026-10-03 (chat, after a still of the reference video at 34.4 s, `work/reference-video/study/effort-title-2026-10-03/popover_above_chip_34.4.png`, showing the model and effort popover above the "Sonnet High" chip with a small pointed tip at the middle of its bottom edge aimed at the chip; the port's popovers have no tip and touch the chip):** asked whether the port's popovers should get the Mac-style tip, the owner chose: "Yes: every popover (the model and effort menu, and the others built the same way) gets the small tip pointing at its button, and sits at the Mac's distance from it."
- **Effect:** every popover the source presents with SwiftUI `.popover` (an NSPopover) is drawn in the port with the Mac's arrow: a small tip centred on the anchor, on the edge facing it (the arrow edge the source names, flipped with the popover when there is no room), the card at the Mac's distance from the anchor (the video: the card's edge about 11 pt from the chip, the tip about 24 pt wide), the tip part of the card's own shape and material (one outline, one fill, one shadow), and the grow-in and shrink-out from the tip as the port's PopoverAppear already does. Measured from the reference videos (every popover they show) and checked against macOS's own popover geometry; built test-first. This settles the tip and the distance only; the card's corners and material stay with the glass pass (OD-0005).
- **Amendment, 2026-10-03 (measured, not a new owner ruling: round 2 of unit/effort-title-fidelity, from the reference video frame by frame, `work/reference-video/study/effort-title-2026-10-03/arrow/` and `review/`):** the Effect's "about 11 pt" was a reading of the still. Measured at full resolution in all four popovers the videos show (2-chat-use.mov 22.2 s, 24.3 s, 34.4 s with the tip down; 1-settings-tour.mov 84.5 s with the tip up), the card's edge stands 13 pt from its anchor (the tip 10-10.5 pt past the edge with its rounded point 1-2 pt short of the anchor), and the tip's sides run at 45 degrees (its outline narrows by exactly 2 px a row from 3 to 10 px under the card's edge, `review/video_tips_x8_reviewer.png`), not the steeper 1.4 pt across per pt down first fitted; the tip is about 24 pt wide sharp, 27-29 pt with its shoulders, its point about 3 pt round. The port follows the measurement (PopoverCard: 12 pt sharp height, 12 pt half base, 3 pt point radius, 4 pt shoulders, 1 pt gap).

## OD-0096 A "Getting ready" screen on first run, while the app prepares everything

- **Owner ruling, 2026-10-03 (chat, after the jewel-knob warm-up measured two ~115 ms render-thread stalls at startup while it compiles the knob's GPU programs, ED-252):** "You can add "Getting ready for first run" screen/menu while the first run loads and prepares everything?" Taken as a yes: the owner asks for it.
- **Effect:** on the first run after install (and again only when the prepared state is invalid: a new GPU or driver, or a new app build), the window first shows a "Getting ready" screen, in the app's glass language, before the Mac's own first screen (`WelcomeView`, RootView.swift:299, shown while there are no projects). While it is up, the app does its one-time preparation, where a stall cannot be seen:
  - compiling and caching the GPU programs (the jewel knob, composer lights, liquid track and strike; the persistent program cache of ED-252);
  - the hardware check that picks the voice model (OD-0092);
  - any other first-run setup that would otherwise cost frames.

  It shows real progress and steps (no fake timer), never blocks for longer than the work takes, and hands over to the Welcome screen with a transition (OD-0076: nothing cuts). Later launches skip it.
- **Process:** this is a new screen, so it is designed first as an interactive variants page (OD-0070). The owner picks, then it is built test-first.
- **Not yet decided:** the words, the look, and whether model downloads happen here or on first use. These are set on the variants page.

## OD-0097 Voice typing ships with the recommended pill words, the Border beam stepping aside and silent Auto-detect; every alternative is a choice in Settings > Customization

- **Owner ruling, 2026-10-03 (chat, two screenshots of the "Voice Options" decision page, https://claude.ai/artifact/8ZC213hrTRPKzSkudYJtH4: its three questions with L1, B1 and A selected, and its toolbar set to Variant B "Pill in the composer", Orb Siri Wave, Size 32 px, Attachment Inside):** "Voice Options > Ship with these defaults, with options in Customization section to change settings as user preference if the user wishes to."
- **Defaults (as selected on the page):** the pill's words are L1, "Listening…", "Transcribing…", "Finishing…" (OD-0091 fifth amendment's proposal); the Border beam steps aside while you speak, B1 (it fades out as listening starts, 0.5 s, and returns when the text is done, 0.6 s: one light on the composer at a time); the language default is A, Auto-detect, silent (Whisper detects the language each time and nothing is shown; Settings > Voice typing > Language: Auto-detect, as OD-0092 set). The composer layout stays as OD-0091's sixth amendment locked it (Variant B, Siri Wave, 32 px, attachment inside).
- **Effect:** the page's alternatives become user preferences in Settings > Customization (the Voice orb part of the page, beside the existing Orb, Listening style, Orb size, Attachment button and After voice input rows), each defaulting as above: the pill's words (L1 to L6 as the page words them), the composer light while you speak (B1 Steps aside, B2 Dims to 35 %, B3 Stays on), and the language behaviour (A Auto-detect, silent; B Auto-detect, named in the pill once heard; C the Windows display language; D asked on the first click of the orb, in a popover with the Mac's arrow tip, OD-0095). The explicit language list stays on the Voice typing page (OD-0092). Every change of these previews in the Customization stage and is transitioned (OD-0076).
- **Resolves:** the Voice Options page's Q1, Q2 and Q3 and the build questions Q-V-3 (the Border beam while you dictate). The exact rows and words of the new controls follow the Customization page's own row patterns (spec, ENGINEERING DECISION where the page does not draw them).

## OD-0098 Where no reference video shows a state, the Swift source is the proof: no new recordings

- **Owner ruling, 2026-10-03 (chat, after being asked whether records whose only gap is a comparison with a video frame that does not exist may be proven from the Swift source, and then shown the list of scenes a new recording would need):** "This is BS, he can't record all of that. Can you not see Swift Code and understand how its programmed to act in those situations?"
- **Effect:** no new reference recordings are needed. A record whose capture clause asks for a comparison with a frame the reference videos (`work/reference-video/1-settings-tour.mov`, `2-chat-use.mov`) do not contain is verified from the pinned Swift source instead: the values the code states (geometry, padding, fonts, colours, opacities, timings, curves, text, the order and conditions of states), with the frameworks' documented behaviour where the code relies on it (each such reading labelled ENGINEERING DECISION), proven against headless renders and mounted scenarios of the port. The test cites this decision and the Swift lines. Where a reference frame does exist, it still wins (SOURCE FACT) and the comparison stays required. A state that neither the code nor a frame can settle stays UNRESOLVED, named, never guessed.
- **Applies to:** every record blocked as "no frame in the reference video" or "CAPTURE-ONLY" (TimelineRows, ChatView, SidebarView, Settings and the rest), and to ED-181's continuous corner where the videos show no element of that size (the spec's measured curve is used).
- **Amendment, 2026-10-03 (chat, a crop of the voice pill's orb from the Voice Options page showing a speck beside the orb):** "Some sort of artifacts and that separator dot appears during voice pill, make sure to fix that." Effect: while the voice pill is open, nothing but the orb, the label and the cancel control is drawn: no separator dot between the label's parts or beside the orb, and no stray pixels, halo fringes or seams around the orb (its glow and edge must be clean at every size and scale). The build's render tests check the pill's frames for this.
- **Amendment, 2026-10-04 (chat, answering build question Q-V-1 'on a slow PC: live words, or the accurate model?'):** "C, ask user and state it generally that the other models don't support multi-lingual capabilities or whatever when asking the user". Effect: when the speed test finds the PC too slow to run the accurate model live, the app asks the user once, after the test, which they prefer: live words with a smaller, faster model, or text after they stop with the accurate model. The question says plainly that the smaller models handle languages other than English less well (they mis-hear more, Hindi and Urdu most). The answer is kept in Settings > Voice typing and can be changed there; a fast PC (every GPU) is never asked and stays live with the accurate model. The exact words and the question's look are designed first (OD-0070) and shown to the owner.
- **Amendment, 2026-10-04 (chat):** "In Getting Ready screen, I chose D3 but inform the user on Welcome Screen if they'd like to have voice transcribing and upon their selection of yes, initiate the download and end the welcome screen by letting them know that once the voice model downloads, it will be functional or whatever and they can check the progress on it by clicking on the orb on the composer. If user selects no, let them know that they can initiate the voice model download anytime by clicking on the voice orb on the composer." Effect: download choice D3, but with consent: the Getting ready screen does not start the voice model download by itself. The Welcome screen asks whether the user wants voice typing. Yes: the download starts (in the background, then the speed test, OD-0092 and Q-V-1's question when the PC is slow) and Welcome says that voice typing will work once the model has downloaded and that its progress shows when they click the voice orb in the composer. No: Welcome says they can start the download any time by clicking the voice orb in the composer, and nothing is downloaded. The look of the screen (A-L) and its words (W1-W5) are still to be picked; the Welcome question and its two answers are designed first and shown to the owner (OD-0070).
- **Owner pick, 2026-10-04 (chat, from the Getting Ready decision page):** "getting-ready: Look I, Words W1, Download D3". Locked: look I "Sheet" (Welcome waits behind, blurred; a glass sheet hangs from the top with the steps and lifts away), words W1 "Plain" (title "Getting ready", line "Marrow is preparing its graphics and voice for this PC. This happens once.", the step names as drawn, "Ready" when done), download D3 with the consent amendment above (the Welcome screen asks first). Source of the look: work/design/decisions-2026-10-03/getting-ready/ (variant I).
- **Amendment, 2026-10-04 (chat):** "Also, let the user know that if they wish to change the voice model, where can they do it eg: Settings > Voice > Model Select (Whatever is the path in the settings menu)". Effect: the Welcome voice message (after a yes, and after a no) also names where the model is changed: Settings > Voice typing > Speech model (the page and section the voice spec defines; the words follow the real page names if they change).
- **Pick, 2026-10-04 (chat):** "welcome-voice: Place A, Words V2". Effect: the Welcome voice question is built as variant A (a "Voice typing" section above the Activity list, in Welcome's own section-and-row pattern) with word set V2 (Warm), from the decision page E:\Droppy\work\design\decisions-2026-10-04\welcome-voice\ (published as https://claude.ai/artifact/3p4diCLGDqjAXSABHQpffY). The page's stated assumptions stand unless the owner says otherwise: the slow-PC question (OD-0097 Q-V-1) shows after the speed test that follows the download, in the answer's place if Welcome is still up, otherwise on the next orb click; until answered the accurate model is used.


## OD-0099 Answers to the open owner questions, and Marrow's own skills (2026-10-04)

- **Asked:** 2026-10-04, on the decision page "Open Owner Questions" (E:\Droppy\work\design\decisions-2026-10-04\owner-questions\, published https://claude.ai/artifact/EUYSt98wu5Pt4mCeUmAZfe), built from E:\Droppy\work\design\open-questions-2026-10-04.md.
- **Answer, verbatim (picks line):** "owner-questions: Q-V-4 A, Q-V-6 B, Q-V-7 A, Q-V-8 A, Q-V-9 C, Q-V-11 C, Q-V-13 B, Q-V-19 B, Q-CZ-1 A, U-06 C, Q-RM A, Q-PICKER A, Q-SKILLS-1 A, Q-SKILLS-4 A, Q-SKILLS-8 A"
- **Effect of the picks:**
  - Q-V-4 A: the hardware check's reason line is two sentences with this PC's measured times (voice SPEC V-10.4 templates).
  - Q-V-6 B: the voice pill counts down the last 30 seconds before the 5-minute cap (OD-0092), then finishes the text as if the orb were clicked.
  - Q-V-7 A: each error note says what happened plus one link to the place that fixes it; the wording list goes on a variants page for the owner's pick (OD-0070).
  - Q-V-8 A: the first time each engine is used, the composer's note shows that engine's privacy line for about 5 seconds, once per engine.
  - Q-V-9 C: the installer ships Vulkan only; on NVIDIA PCs the hardware check offers CUDA as a separate download.
  - Q-V-11 C: with "Send it as the prompt" on and a turn running, voice text does what Return does: interrupt the turn and send (queue when heads are at work), ComposerView.swift:370-381.
  - Q-V-13 B: an installed model (not the one in use) can be removed from a trash icon shown on hover at the row's end, confirmed by a second click.
  - Q-V-19 B: the Voice typing page gets a Microphone row to choose the input.
  - Q-CZ-1 A: the Voice typing page is locked as drawn, its own sidebar page directly below Customization (voice SPEC V-16.3).
  - U-06 C: the owner sees the strike's bubbles both ways side by side first; the port's drawing (inside the liquid) stays until then.
  - Q-RM A: Reduce Motion stills what OD-0076 added and the places the Mac already gates, not the Mac's un-gated animations (ED-264).
  - Q-PICKER A: the OpenRouter model picker is approved as drawn on the OD-0076 canvas (versions 4 and 5).
  - Q-SKILLS-1 A: all bundled skills install; the document, review, security, GitHub, design and skill-building ones are on at the start; those needing a key, login or internet start off with the need shown.
  - Q-SKILLS-4 A: no brand-guidelines skill.
  - Q-SKILLS-8 A: one removable Marrow-owned plugin in Antigravity's and Cursor's plugin folders, only after a live test on this PC shows it works; otherwise those two get skills only when named.
- **Answer in chat, 2026-10-04 (Hindi, recorded in English; the owner's words):** do not ask Anthropic or OpenAI for anything; study the skills they already ship and build our own, so we have the full functionality with no licence and no dependency; the Word, PDF, Excel and slides skills are built by us the same way; the larger catalogue (about 370 more Anthropic skills and OpenAI's open plugins) is studied and rebuilt as our own and integrated, "even if the app becomes 1 GB", shipped ready to use; the basic tools the skills need (Python, Node, LibreOffice) ship with the app so users save time; a Figma skill is an ordinary skill: when someone uses it, Figma's login comes up on screen, they authenticate, and it works.
- **Effect of the chat answer (closes Q-SKILLS-2, -3, -5, -6, -7):**
  - Marrow ships its own original skills covering the capabilities of the Anthropic and OpenAI catalogues that make sense for the app, documents first. They are written clean-room: the published skills are studied for what they do and which public formats and libraries they use; no text, prompt, code or example is copied. Anything shipped that is third-party is under a permissive licence with notices (THIRD_PARTY_NOTICES). Plan: E:\Droppy\work\skills\ORIGINALS-PLAN.md.
  - No request goes to Anthropic or OpenAI; no skill of theirs ships.
  - The installer carries the skills' runtime: Python, Node and LibreOffice (headless), plus the packages the document skills need, installed under Marrow's own data folder; the app size may grow to about 1 GB.
  - Figma: a Marrow Figma skill that, on first use, opens Figma's own sign-in on screen and then works with the user's account.
  - This supersedes OD-0080's "8 decisions pending" and the OWNER-BRIEF recommendations for Q-SKILLS-2, -3, -5, -6 and -7.
- **Amendment, 2026-10-04 (chat, Q-V-15):** "Keep it, Off by default but accessible and setup able in settings" (answering whether to keep or drop the optional paid cloud voice engine, after the lead explained that the local model runs free on the graphics card and the cloud engine bills the user's own OpenAI key per minute). Effect: the cloud engine stays, off by default; the user turns it on and sets it up (its OpenAI key) on Settings > Voice typing. The local engine stays the default and free. Its model is gpt-transcribe (the page's recommended A, about half a US cent a minute, words after you stop), the lead's reading since the owner named no model; the owner can change it.
- **Amendment, 2026-10-04 (chat, the skills plan's four questions, E:\Droppy\work\skills\ORIGINALS-PLAN.md section 7):** "1. Copy and create better tools for our Marrow app. 2. Accept your recommendation, allow usage and tweaking but by credit only but cant take all 120 and sell or republish them. 3. Recommendation accepted 4. Use Figma via Claude Code, Codex or Cursor I think". Effect: Q2, Marrow's skills ship under Marrow's own terms: users may use and adapt them with credit to Marrow, and may not take the set and sell or republish it. Q3, Marrow's document skills show even where a provider brings its vendor's own. Q4, no Marrow Figma app registration: Figma works through the providers' own Figma sign-in (Claude Code, Codex, Cursor). Q1 (licence exceptions for LibreOffice's LGPL parts, OFL fonts and the permissive Python parts) is UNRESOLVED as worded: the lead reads "copy and create better tools" as studying those tools and building Marrow's own better ones, which may mean not shipping LibreOffice; to be confirmed with the owner before Wave 0's runtime unit (W0-3) starts.
- **Amendment, 2026-10-04 (chat, skills Q1 settled):** after the lead explained what LibreOffice does for the skills and how the legacy Marrow app handles documents (it only reads text from .docx/.odt/.epub/.rtf and shows PDFs with pdf.js), the owner wrote: "Then lets not build our own tools for now, lets ship other's tools to enable functionality?" Effect: option A. Marrow does not build its own replacements for these tools now; it ships the third-party tools the skills need, unmodified and loaded as separate parts: LibreOffice (headless, including its LGPL libraries as separate DLLs), the OFL fonts, Python and Node with the permissively licensed packages (including the ISC, MIT-CMU, Zlib, FTL, IJG, Unicode, BSL-1.0 and Matplotlib variants and the compiler runtime exceptions), each with its notices in THIRD_PARTY_NOTICES and the source offer LGPL asks for. This replaces the UNRESOLVED reading of Q1 above. GPL tools stay out (no Poppler, pandoc, Git, FFmpeg). Marrow's own skills are still written clean-room.
- **Amendment, 2026-10-04 (chat, Hindi, recorded in English; supersedes the bundling parts of the effects above):** the tools would make the app too big, so they are not shipped with it. Settings gets a place (Plugins or Skills) where the user can download the tools and skills they want. When a chat task needs a tool that is not there, Marrow says so in the chat and offers to download it; on yes it downloads and the task goes on; if it was already downloaded from Settings it is simply used. Make the app usable now; Marrow's own skills come later. The app is not commercial: it is for personal use by the owner and two or three relatives, so installing the skills Anthropic and OpenAI ship with their own apps is fine. Effect: (1) no tool runtime in the installer; Python, Node, LibreOffice and the packages are optional downloads, from Settings and on demand from a chat, installed under Marrow's data folder; (2) the vendors' published skills and plugins are offered as installs fetched by the user's own Marrow from the vendors' official public sources at a pinned version, not copied into Marrow's repository or installer, and each stays under its source's terms (Anthropic's document skills are tied to the user's own agreement with Anthropic); (3) Marrow's own clean-room skills (the 120-skill plan) are deferred to later; the clean-room kit already built stays for that; (4) the Settings page and the in-chat download prompt are designed first for the owner's pick (OD-0070).
- **Pick and amendment, 2026-10-04 (chat, from the Skills Downloads page https://claude.ai/artifact/KBarrEq1xKGAjx1KnapYnF):** "skills-downloads: Page D, Offer A, Words S1"; "although I think Plugins must be one section and Skill must be another section, not a combined section. Design it and show me for review first."; "I'll need a download all button with a disclaimer that says it can be a large download to acknowledge the user with the size of download and needs user's confirmation to continue or cancel."; "Skills only get used in chat if they are invoked, don't load all skills in each chat without reason and increase massive usage." Effect: the Settings page uses layout D (library list with a detail pane), the in-chat offer is A (an inline card in the reply), words S1 Plain; Plugins and Skills are separate sections (tools as the page lays them out), redesigned and shown to the owner before building; a Download all control states the total download size and asks the user to confirm or cancel before anything downloads; installed skills are not loaded into every chat: a skill reaches the model only when it is invoked (by the user, or by a task that needs it), so idle skills add no tokens to a chat. The question of the vendors' proprietary skill sets (Anthropic's document skills, OpenAI's proprietary-labelled plugins) is still open.
- **Amendment, 2026-10-04 (chat, Hindi, recorded in English; supersedes "skills reach a chat only when invoked" above):** after the lead explained how Claude handles skills (each session lists only every installed skill's name and short description; the model, or the user by name, invokes a skill and only then is its full SKILL.md read; scripts and references load only when used), the owner ruled: do it the way Claude does: list the installed skills with their descriptions in every session so the model knows which skills exist, and keep that. Effect: Marrow follows Claude's progressive disclosure for every provider: the names and descriptions of the installed (downloaded and enabled) skills are listed in each session; a skill's full instructions load only when the model or the user invokes it; its scripts and reference files only when those instructions need them. Skills that are not installed are not listed.
- **Closed from that page:** Q-CZ-3, Q-RIM, Q-TEXT and U-06 by the Look Picks pick below.
- **Pick, 2026-10-04 (chat, from the Look Picks page https://claude.ai/artifact/FsvzAHxcwSRix63B9X3B21, E:\Droppy\work\design\decisions-2026-10-04\look-picks\):** "look-picks: Q-CZ-3 B, Q-RIM A, Q-TEXT A, U-06 A". Effect: Q-CZ-3 B, the Uiverse light's Custom colour wells open Marrow's own glass picker (saturation-brightness square, hue strip, opacity, hex field, recent colours) in the glass popover with the arrow tip; the owner rejected the classic Windows colour dialog ("I want a modern one"); with opacity the custom colours are stored as #RRGGBBAA and the light draws the alpha. Q-RIM A, the slider's rim teardrops stay as locked (no change to the glass map). Q-TEXT A, text is drawn greyscale everywhere (no subpixel/ClearType), on every surface. U-06 A, the strike's bubbles stay inside the refracted liquid under the lightning, as the port draws them (a recorded difference from the locked OD-0093 page).
- **Amendment, 2026-10-04 (chat, Hindi, recorded in English; closes the open question on the vendors' proprietary skill sets):** the owner, after the lead recommended keeping them out: every plugin and skill Anthropic and OpenAI ship goes into Marrow too; the use is not commercial (the owner and a cousin, on their own computers, both holding Anthropic and OpenAI subscriptions); Marrow is their internal app. Effect: the skills-downloads catalogue offers every skill set and plugin Anthropic and OpenAI publish, including Anthropic's Word, Excel, PowerPoint and PDF skills and OpenAI's proprietary-labelled plugins, each downloaded on the user's request from the vendor's own official source with its licence file kept beside it, never bundled in the installer. On the page's name the owner restated: Plugins and Skills each get their own page (the redesign's Sections C draws that); the owner confirms after reviewing the design.
- **Pick, 2026-10-04 (chat, from the redesigned Skills Downloads page, version 2 of https://claude.ai/artifact/KBarrEq1xKGAjx1KnapYnF, E:\Droppy\work\design\decisions-2026-10-04\skills-downloads\):** "skills-downloads-2: Sections B, DownloadAll A, Invoke A". Effect: one Settings page named "Skills" in layout D, with a three-way Plugins / Skills / Tools switcher above the library list; the list shows one section at a time, with that section's Download all at its foot. Download all A: each section has its own Download all, and the title row has "Download everything"; both open a confirmation over a dimmed window that states the large-download warning, the exact download size, disk space, free space, sources and every item, and nothing downloads before Continue (Pause/Resume, Cancel keeps finished items, Retry fetches only failed ones). Invoke A: typing "/" in the composer opens a picker of the enabled skills, and when a skill's full instructions load, the timeline gets one "Skill" row saying who invoked it (the model or the user); no composer Skills chip and no loaded-skills strip. Every session lists the name and description of each installed, enabled skill; a skill's full text loads only on invocation (amendment above). This pick supersedes the "separate pages" remark in the proprietary-sets amendment: the owner picked B after reviewing the drawn variants.
- **Clarification, 2026-10-04 (chat, Hindi, recorded in English; supersedes "Sections B" and "Download everything" in the pick above):** the owner: Skills must have its own page and Plugins its own page, not mixed; Plugins sits right above or below Skills in the sidebar; each page has its own Download all button; "I think this didn't get conveyed clearly". Effect: Sections C: two Settings pages, Plugins and Skills, adjacent in the sidebar (Plugins directly above Skills, as drawn), each in layout D with its own Download all that opens the confirmation of Download all A (large-download warning, exact size, disk, free space, sources, item list, Continue or Cancel; Pause/Resume, Retry). The tools join the existing Tools page with their own Download all. No switcher, no mixed page, no "Download everything". Invoke A stands.
- **Review of version 3, 2026-10-04 (chat, verbatim):** "32 MB is not a big download, anything above 100 MB, you can say large download for that message. For anything 100MB or under, use different wording that makes sense. While download is initiated, we must allow minimizing the download window to allow user to use the app as necessary until then and the option to pop open the downloading layover/window if he/she needs to. Also, in case a download fails, have a backup plan to suggest user, like visit the website download and place in this folder or something whatever. We also need to provide the ability to let the user install custom tools, skills and plugins? Also, fine the user can invoke skills in chat via composer using / but the session itself should be able to call any skills that it needs as per the requirement just like claude's native app operates." Effect: (1) the "large download" warning appears only when the download is over 100 MB; at 100 MB or under the confirmation states the size in plain words without the warning; (2) a running Download all can be minimized so the app stays usable, and reopened from a visible progress indicator; (3) a failed item offers a manual fallback: get it from the vendor's page and put it in the named folder, after which Marrow checks it; (4) users can add their own tools, skills and plugins; (5) every session can invoke any enabled skill by itself when the task needs it, as Claude's own app does (already the design: listed by name and description each session, loaded on invocation); "/" in the composer stays as the user's way to name one. Items (2) to (4) are drawn as variants for the owner's pick before building.
- **Pick, Minimize A with refinements, 2026-10-04 (chat, verbatim):** "2. I agree with A. However, clicking on the pill must open a small overlay/window from the pill that shows downloading window and progress, sort of like a pop up overlay from the pill and it must have a enlarge caption to reach the exact window in settings. Also, if user stays in settings while the download is continuing, the pill must show elsewhere, probably below all the settings headers in the left column? On the lower left?" Effect: a minimized Download all lives in the sidebar glass progress pill above Settings (the OD-0100 place D pattern); clicking the pill opens a small popover anchored to it with the compact download and progress; the popover's caption has an enlarge control that opens Settings on the exact page with the full Download all dialog; while the user is in the Settings window, the same pill shows at the lower left of the Settings sidebar, below the last entry.
- **Pick, adding your own items, 2026-10-04 (chat):** with a screenshot of Claude's Skills page title row (title "Skills", a Yours/Discover segmented control, a search field "Search skills and plugins", filter and sort icon buttons, and a white "+ Add" button with a chevron), the owner: "Just like Claude has an Add button, similarly". Effect: Custom C: an "Add" button with a chevron in each page's title row (Plugins, Skills, Tools), drawn in Marrow's own decided UI design (the page's glass button style, words S1), the screenshot being a reference for its PLACE only (owner, 2026-10-04: "Just in our decided UI design, claude screenshot is for place reference."), opening a menu of sources (from a folder, from a .zip, from GitHub) that leads to the add sheet (validation, found-items checklist with Select all, parallel install with the green check per row). Only the Add button is picked from the screenshot; the other controls in it (Yours/Discover, search, filter, sort) are not part of this pick.
- **Amendment, adding your own items, 2026-10-04 (chat, reviewing the published skills-downloads page https://claude.ai/artifact/39WZKoxAhcd7YP3dQm5RrX):** "As for when I click add button and select an option, I must get an option to browse the folder/zip, etc depending upon which file/folder type is selected rather than typing the folder or file location". Effect: the add sheet's source field is chosen by browsing, not typed: From a folder… opens the system folder picker, From a .zip… opens the system file picker filtered to .zip files (a tool's folder likewise opens the folder picker); the chosen path is shown in the field with a Browse… button to choose again. From GitHub… stays a link field (there is nothing local to browse). Checking starts from what was picked.

## OD-0100 One "update everything" option in the app (2026-10-04)

- **Asked by the owner, 2026-10-04, verbatim:** "And there should be update option in the app to run all updates including claude code, codex, tools, etc everything"
- **What upstream does (SOURCE FACT):** the app checks for its own new version (UpdateChecker.swift) and installs it from Settings > About; the Claude and Codex CLIs, which the app runs headless and which never update themselves, are updated in the background by their own updaters at most every twelve hours (AppModel.swift:275-282, ProviderRegistry updateCLIIfDue), and forced when a model needs a newer CLI, with a notice in the thread (ThreadRuntime.swift:2255-2290). There is no visible control that updates everything.
- **Effect:** Marrow adds one visible "update everything" control that runs every update in one go: Marrow itself (Velopack), every installed provider CLI that has an updater (Claude Code, Codex and the others the registry knows), the bundled skills runtime (OD-0099: Python, Node, LibreOffice and packages) and Marrow's skills, with per-item progress and results. The upstream background CLI updates stay as they are. Where the control sits, what it is called and how it shows progress is designed first and shown to the owner for a pick (OD-0070); nothing is built before that pick.

- **Pick, 2026-10-04 (chat, from the Update Everything page https://claude.ai/artifact/DrEHJsBWCAGZrBhViUKAfN):** "Q1 - A and D both", "Q2 - U1 Plain", and on where Marrow finds its own new version: "Can we push Marrow updates on my git and the users can fetch the updates from there?" Effect: places A (the Tools/Updates page OD-0075 picked, grown to list every item with Installed and Latest versions and one button) and D (a glass pill above Settings in the main window, shown only when something is out of date, with the run's progress ring and a popover list) both ship; words U1 Plain ("Update everything", "Up to date", "Check now", "Restart Marrow"); items run one at a time, Marrow last. Marrow's own updates come from the owner's GitHub: Velopack reads release packages the owner publishes there (a GitHub Releases feed); each publish still needs the owner's word. The user's own developer tools are included (owner, 2026-10-04, "Yes" to including Git, GitHub CLI, Node, Python and Office): each installed one gets a row and is updated through its own updater (for example winget for Git, GitHub CLI, Node and Python, Office's own Click-to-Run updater); the exact command per tool is read from its documentation and tried once under supervision before it is built. The skills and their tools are separate optional-download rows (OD-0099 amendment), not part of Marrow's package.
- **Feed repository, 2026-10-04 (chat):** asked "which GitHub repository should host Marrow's releases? ... I recommend a new public repo, Oak993/marrow-releases, holding only the installers", the owner: "As long as its free, use my chrome, open github and set it up." The lead created https://github.com/Oak993/marrow-releases (public, free; a README only, no release). Effect: Marrow's update source (Velopack GithubSource) is Oak993/marrow-releases, read without a token; releases are published there only with the owner's word for each publish.

## OD-0101 Everything the Claude and Codex desktop apps can do, Marrow can do (2026-10-04)

- **Owner, 2026-10-04, verbatim (Hindi in Latin script):** "Matlab jo jo kaam claude native app aur codex native app mai hote hain, wo sab Marrow mai ho sakein." ("I mean: every task that can be done in the Claude native app and the Codex native app should be doable in Marrow.") Said right after OD-0099 (skills and bundled tools) and OD-0100 (update everything).
- **Effect:** a direction beyond the pinned Droppy Code source, added by the owner like OD-0080 and OD-0099: Marrow aims to cover every capability of Anthropic's Claude desktop app and OpenAI's Codex desktop app on Windows. First step: an inventory of both apps' capabilities against Marrow (the port, the skills plan, the decisions already taken), with a gap list and an order, at E:\Droppy\work\capabilities\GAP-LIST.md. Each gap that adds something visible is designed and shown to the owner for a pick before it is built (OD-0070); nothing is copied from either app (clean-room, as OD-0099). The exact-parity work on the pinned source continues first.

## OD-0102 In-between corners: Apple's continuous corner at the largest radius that fits (2026-10-04)

- **Asked:** 2026-10-04, on the Corner Band page (https://claude.ai/artifact/WjXLz7q6wH4F3i3zgd75Ro, E:\Droppy\work\design\decisions-2026-10-04\corner-band\): elements drawn `.continuous` whose height sits in ED-275 (2)'s UNRESOLVED band (Apple's corner does not fit at the stated radius, yet the radius is under half the height; for r 18 that is 36 < h < 55), shown as A (Apple's continuous corner at the largest radius that fits, ContinuousCorner.LargestRadius, PaintCode's clamped corner) and B (a circular arc of the stated radius). The lead recommended B.
- **Answer, verbatim:** "corner-band: A"
- **Effect:** in that band the port draws Apple's continuous corner at the largest radius that fits, instead of a circular arc. This settles the band for UI-CHAT-034, UI-CHAT-036, UI-CHAT-037 and UI-CHAT-054 and every other in-band `.continuous` element, EXCEPT where a reference frame measures the element: the composer pill (r 22 on 46 pt) measures as the circular arc in the reference video (ED-275), which is SOURCE FACT and stays as measured. Capsules (r at least half the height) and elements large enough for the full corner are unchanged.

## OD-0103 What ships in this release (2026-10-04)

- **Owner, 2026-10-04, verbatim:** "voice typing, getting ready, customization, update everything, skills downloads all ship in this port man! I was talking about other features from the legacy marrow app, those get reviewed and added one by one later."
- **Effect:** the first installable release contains the exact-parity port plus every owner-approved addition: voice typing (OD-0091, OD-0092, OD-0097 and amendments), the Getting Ready screen and the Welcome voice question (OD-0096 and amendments), Customization (OD-0085, OD-0089 and the Look Picks of OD-0099: Marrow's glass colour picker, greyscale text, the strike's bubbles inside the liquid), update everything (OD-0100) and skills downloads (OD-0099 amendments; its Settings page after the owner reviews the sections redesign), and the earlier owner rulings already in force (OD-0075 liquid at every effort, OD-0093 strike, OD-0102 corners). The release is not built until these are in. Only the legacy Marrow app's other features (and the Claude/Codex gap list of OD-0101) are reviewed later and added one by one.

## OD-0104 Voice typing packaging: VC++ runtime, native size, NVIDIA downloads (2026-10-04)

- **Asked (chat, 2026-10-04), from feature unit VT-0's findings:** 1. Can the installer add Microsoft's VC++ 2015-2022 x64 runtime (Velopack prerequisite vcredist143-x64, about 25 MB from Microsoft), which the voice natives import (MSVCP140, VCRUNTIME140, VCRUNTIME140_1, VCOMP140) and the app does not carry? 2. Is about 62 MB of voice-engine natives (CPU + Vulkan) acceptable against the 50 MB budget? 3. May the optional GPU (CUDA) download fetch NVIDIA's libraries (about 390 MB) straight from NVIDIA's own servers, Marrow never hosting them?
- **Owner, verbatim:** "1. Yes 2. Yes 3. Yes"
- **Effect:** (1) the installer adds Velopack's vcredist143-x64 prerequisite (V-3.2); (2) the voice natives' size budget is raised to fit the measured ~62 MB; (3) the Q-V-9 C CUDA offer downloads the Whisper.net CUDA pack from nuget.org and NVIDIA's redistributable libraries from NVIDIA's servers on the user's request, never bundled or hosted by Marrow, verified by hash, and a half-installed pack is never loaded (VT-0 probe case 15).

## OD-0105 Open questions answered: loading hand-over, strikethrough, settle bounce, D-VN page, light warm-up, installer timing (2026-10-05)

- **Asked (chat, 2026-10-05, plain-language multiple choice):**
  1. "When a chat opens, the loading spinner gets replaced by the messages. On the Mac this happens instantly, in one frame. What should we do?" (UI-CHAT-001 STOP #10)
  2. "When a checklist item is ticked, a line goes through its text. How should the line appear?" (PR-CHAT-114)
  3. "In the sidebar, the tick mark does a small bounce when you mark a chat finished. The Mac's code gives the exact bounce, but no video shows it. Is the code enough proof?" (UI-SIDEBAR-020)
  4. "Voice typing needs one design choice from you (D-VN). Shall I make a page showing the options, labelled A, B, C, for you to pick from?"
  5. "The glowing light around the message box never stops moving. Warming up some graphics would cause a tiny stutter in it. My plan: skip those, so the first time you switch to a different light style it may stutter once. Is that OK?" (fix-knobcache)
  6. "When the port is done, I'll build the installer. My plan: a normal Setup.exe for 64-bit Windows, not signed, saved to a folder on this PC. Nothing gets run, uploaded or published. OK?"
  7. "Earlier small changes, just to check: Look cards 8 px taller (like the Mac), the Getting Ready subtitle letters slightly tighter, About saying "GitHub", and the colour code box taking 8 characters (with transparency). Are all of these fine?"
- **Owner, verbatim (the picks):**
  1. "Smooth fade (Recommended)"
  2. "Fade in (Recommended)"
  3. "Code is enough (Recommended)"
  4. "Yes, make the page (Recommended)"
  5. "No, find another way"
  6. "do so after complete port and first release features are all done"
  7. "All fine (Recommended)"
- **Effect:**
  1. The loading mark fades out while the rows fade and rise in, in about 0.2 s. Rows that leave together fold shut together, in about 0.3 s (OD-0076).
  2. The checklist strikethrough fades in (PR-CHAT-114).
  3. The settle bounce is proven by the Swift source: SidebarView.swift:1909, spring response 0.3 and damping 0.45, then Chrome.panelSlide (UI-SIDEBAR-020).
  4. The D-VN variants page is built for the owner's pick.
  5. The knob warm-up must cause neither a stutter in the composer light nor a hitch on the first switch of light style, so nothing is left to first use (LEAD RULING 2 of fix-knobcache, amended).
  6. The installer (unsigned Setup.exe, x64, local folder) is built only after the whole port and every first-release feature are done. It is never run, signed or published without the owner.
  7. The four FYIs stand.

## OD-0106 Voice notes picks (D-VN): Notes A, Countdown C, CUDA A (2026-10-05)

- **Asked (chat, 2026-10-05):** the D-VN variants page https://claude.ai/artifact/7kBUmes7C8Jz4WGBFrucmB (sources E:/Droppy/work/design/decisions-2026-10-04/voice-notes/, built after OD-0105 item 4). It asked for the Q-V-7 A error-note wording set (A Plain, B Short, C Action, D Explained), the Q-V-6 B countdown look in the voice pill (A time in place of the words, B time after the words, C a draining line with no numbers, D "Stops in" with amber at the end) and the Q-V-9 C CUDA offer look in the hardware card (A its own row, B inside the recommendation box, C on the GPU backend line, D a sentence under the card's note). The recommendations were Notes A, Countdown B and CUDA A.
- **Owner, verbatim:** "voice-notes: Notes A, Countdown C, CUDA A"
- **Effect:** VT-17 builds these picks as drawn on the page.
  - **Notes:** set A, Plain. Each of the eleven notes says what happened, then names the place that fixes it, as one underlined link with the locked "Microphone blocked" note's shape. With an empty draft it shows in the field; with text in the draft it shows in the composer's notice line.
  - **Countdown:** look C. The pill's words stay as they are. A 2 px line inside the pill's foot, from the cross to the orb, shortens over the last 30 s before the 5-minute cap and takes the warning colour for the last 10 s. There are no digits. At the cap the text finishes as if the orb were clicked (V-7.12). Under Reduce Motion the line steps once a second.
  - **CUDA:** look A. On NVIDIA PCs with a working probe, an "NVIDIA CUDA" row sits under the recommendation in Settings > Voice typing > Speech model, with its size (644 MB, from nuget.org and NVIDIA) and a Download capsule. Progress, cancel and the end states use the model list's state column and its 2 px bar.
  - **Still open for VT-17 (lead):** the page's open points. These are the restart after the CUDA download, whether the download note shows beside the download pill, the 5.2 s display time for the new notes, and the screen-reader announcements. VT-17 settles them as engineering decisions without the owner, unless they change what the owner picked.
- **Amendment, owner, 2026-10-05 (chat, verbatim):** "और वो जो लाइन वाला जो मैंने सेलेक्ट किया है ना ड्रेनिंग लाइन ड्रेनिंग लाइन का इस तरह से करो कि जब 30 सेकंड रह गया हो ना तो वैसे तो कलरलेस रहे ठीक है ना 15 सेकंड पे वो येलो हो जाए ठीक है और येलो भी एकदम से स्टैटिक कट हो के नहीं होना चाहिए ठीक है स्मूथली येलो हो जाए और फिर जब 10 सेकंड के पास आ जाए 10 सेकंड हिट होते साथ ही फिर वो स्मूथली रेड हो जाए लाइन ठीक है ना"
  - **In English:** "And the line I selected, the draining line: when 30 seconds are left it stays colourless; at 15 seconds it turns yellow, not by a static cut but smoothly; then as soon as it hits 10 seconds it smoothly turns red."
  - **Effect (replaces "takes the warning colour for the last 10 s" in look C above):** from 0:30 to 0:15 the line is colourless, in the pill's own neutral label tone. At 0:15 it crossfades smoothly to yellow, and at 0:10 it crossfades smoothly to red, each over about 0.6 s on an ease curve (OD-0076, never a cut). Yellow and red are the system warning and error colours of each theme, matching the app's other warning and error marks; VT-17 records the exact tokens as an engineering decision. Under Reduce Motion the colour changes still crossfade, but faster (about 0.2 s), and the line steps once a second.
- **Second amendment, owner, 2026-10-05 (chat, verbatim):** "बल्कि 15 सेकेंड जब रह जाए ना तो उस वक्त येलो ज़रूर कर देना लेकिन रेड करना जब 5 सेकेंड रह जाए ठीक है ना"
  - **In English:** "Rather: when 15 seconds are left, do make it yellow then, but make it red when 5 seconds are left."
  - **Effect (replaces the amendment's red point):** the line is colourless from 0:30 to 0:15, eases to yellow at 0:15, and eases to red at 0:05 (not 0:10). Each change is the same smooth crossfade of about 0.6 s, never a cut.
- **Clarification, owner, 2026-10-05 (chat, verbatim):** "आई डोंट नो अब वो फेड होगा या तो कोई स्मूथ ट्रांजिशन होगा कि जिसमें कलर चेंज हो ठीक है ना लेकिन यू नो इट शुड बी प्रॉपर वे प्रीमियम तरीके से नजर आना चाहिए ठीक है"
  - **In English:** "I don't know whether it will be a fade or some smooth transition in which the colour changes, but it should look properly premium."
  - **Effect:** the lead chooses how the colour changes. It must read as premium, never as a cut or a muddy blend.
  - **Lead's choice (ENGINEERING DECISION, VT-17):** the line's colour glides along a perceptual path in OKLCH, so the hue turns cleanly with even brightness and no grey or brown midpoint. Each glide runs over about 0.8 s on a gentle ease-in-out.
  - **Arrival accent:** as each colour arrives, the line gives one soft, restrained brightening of about 0.4 s, so the change is noticed without flashing.
  - **Reduce Motion:** the glide stays, at about 0.2 s, with no brightening.
  - **Proof:** a frame-by-frame test samples the colour at 60 Hz through each change, and the result is compared on a variant still before it ships.

## OD-0107 The two MIT liquid-glass repos give the app's glass its macOS look (recorded late, 2026-10-05)

- **Owner ruling, 2026-10-02 (chat).** It was found missing from this file on 2026-10-05 and is recorded from the session summary of 2026-10-02, which reads: "Use the two MIT glass repos (rizzytoday/liquid-glass, dashersw/liquid-glass-js) to achieve the macOS glass look." Local clones are in E:/Droppy/work/glass-refs/ (liquid-glass-rizzytoday, liquid-glass-js-dashersw; both MIT).
- **Owner, 2026-10-05 (chat, verbatim; translated):** "In the app that opens in the tests the Acrylic look is coming, but the liquid glass look from the two repos I gave you is not. Is that done in the first-release features, or done but done improperly, which is why I don't see it right? Confirm." The owner then re-sent both links: "https://github.com/rizzytoday/liquid-glass/tree/main https://github.com/dashersw/liquid-glass-js".
- **How it fits OD-0005:** where a window shows the desktop, Windows gives only its system Acrylic. OD-0005 ("acrylic edge", no screen capture) rules out sampling the desktop, so the desktop behind a window cannot be refracted. Everywhere the app draws its own glass over its own content (panels, pills, capsules, popovers, the composer, the effort track), the repos' look applies: the refracting rim band, the displacement map, the specular edge and the colour fringe.
- **Effect:** an audit compares, on a real window, every app-drawn glass surface against the repos' own rendering. Every surface that should carry the material and does not, or carries it weaker than the repos draw, is a defect, fixed test-first. Rim effects that need only the app's own content (specular edge, rim highlight, fringe) are also checked on the window glass over Acrylic.
- **Amendment, owner, 2026-10-05 (chat, verbatim):** "भाई मैंने जो तुम्हें दो रेपो भेजे हैं ना उन्हीं को यूज करते हुए मेरे कजन ने भी अपनी एक और ऐप के अंदर एग्जैक्ट वही मैक ओ एस वाला लिक्विड ग्लास का लुक लेके आया है वो वो दो रेपोस को यूज करते हुए ठीक है ना तो जब मैं सुबह उठूं तो ये लिक्विड ग्लास प्रॉपरली इम्प्लीमेंट होना चाहिए जैसे वीडियोस के अंदर नज़र आ रहा है ठीक है ना और जहाँ जहाँ नज़र आ रहा है वैसी नज़र आना चाहिए ठीक है दिस शुड बी डन बाय मॉर्निंग"
  - **In English:** "Using the two repos I sent, my cousin brought the exact macOS liquid-glass look into another app of his. So when I wake up, this liquid glass must be properly implemented, as it appears in the videos, and look that way wherever it appears. This should be done by morning."
  - **Effect:** top priority overnight. The reference is the Mac videos: every surface that shows liquid glass there shows the same look in the port, built with the two repos' techniques (the refracting rim band and displacement map, the blur, the specular rim, the colour fringe, the tint). It is proven on a real window against frames extracted from the video, side by side. OD-0005 still holds for the desktop behind a window: no screen capture, so the desktop itself is not refracted. Everything the app draws over its own content carries the full look.

## OD-0108 The GPU warm-up runs behind the Marrow boot animation (2026-10-05)

- **Context:** OD-0105 item 5 ("No, find another way"). The fix-knobcache unit then drew every GPU program part in a cloaked helper window before the boot stage, which added 386-541 ms before a later launch's first shown frame.
- **Owner, verbatim:** "भाई वो स्टटर नहीं आना चाहिए जो तुमने ऊपर वाली बात लिखी हुई है कि नॉब कैश रिवर्क फॉलोज योर फाइंड अनदर वे रूल ऑन एवरी लॉन्च आफ्टर द फर्स्ट द ऐप क्वाइटली ड्रॉज ऑल इट्स ग्राफिक्स वंस इन अ हिडन विंडो बिफोर एनीथिंग अपीयर्स भाई जब ऐप लोड हो रही होती है शुरू में तो ऑलरेडी मैरो का एक आती है एनिमेशन लोडिंग की तो उस दौरान क्यों नहीं कर रहा वो उस वक्त तो पूरी विंडो छुपी भी होती है तो जब वो ऐप की एनिमेशन आ रही है मैरो की ना स्पिन स्पिनर जो चल रहा होता है स्पिनिंग एनिमेशन फिर वो पार्टिकल्स बन के जब जाता है तो उसी वक्त सब कुछ चीज़ें हो जानी चाहिए ना लोड तो उस उस दौरान करवाओ ठीक है ना बेशक अगर एक सेकंड और भी लेट हो जाए कोई मसला नहीं है ठीक है ना लेकिन जब शुरू होते साथ ही ऐप की एनिमेशन आ रही है मैरो की आइकन आ रहा है एनिमेट होके और फिर वो पार्टिकल्स बन के विंडो में जा रहा है ठीक है ना तो उसी दौरान सब चीज़ें रेडी हो जानी चाहिए ऐप की पीछे से ताकि जब कोई बंदा यूज़र जब उसको विंडो सामने आए तो एवरी थिंग शुड बी लोडेड एंड रेडी स्मूथ विदाउट एनी स्टटर्स"
- **In English:** "There should be no stutter. When the app starts, the Marrow loading animation already plays: the icon animates and spins, then turns into particles that fly into the window, and the whole window is hidden at that time. Do all the loading during that animation, even if it makes launch up to a second later. By the time the window comes in front of the user, everything should be loaded and ready, smooth, without any stutters."
- **Effect:**
  - The warm-up runs behind the boot animation, while the main window is still hidden: the icon's spin, then the particles into the window. The main window is shown only once everything is ready.
  - The boot animation itself must not stutter.
  - Up to about one extra second of launch is accepted.
  - The cloaked pre-boot pass is replaced or reduced to whatever cannot be hidden in the animation.
  - Nothing is left to first use.
  - This applies to every launch after the first. The first run keeps the Getting ready sheet.

## OD-0109 Built-in colourful wallpapers, animated too, behind the liquid glass (2026-10-05)

- **Owner, verbatim:** "और हाँ ठीक है चलो अगर वो जो तुम बोल रहे हो ना कि डेस्कटॉप डायरेक्टली बिहाइंड अ विंडो स्टिल कैन नॉट बी बेंट तो कोई ना कोई वॉलपेपर्स या तो एनिमेटेड वॉलपेपर्स हम लगा सकें पीछे की तरफ जो कलरफुल हो ठीक है ना मतलब एब्स्ट्रैक्ट कलरफुल मूवमेंट्स या स्मूथ मूवमेंट्स तरह की कुछ वॉलपेपर्स हम लोग एनिमेटेड वॉलपेपर्स का डाल दें इसके अंदर ताकि वो लिक्विड ग्लास का लुक भी मेंटेन रहे और बैकग्राउंड से हमें कोई फ़र्क ना पड़े बिकॉज अवर वॉलपेपर इज़ रनिंग बिहाइंड इस तरह से कुछ भी कर सकते हैं"
- **In English:** "If the desktop directly behind a window still can't be bent, then let us put wallpapers behind it, or animated wallpapers, colourful ones: abstract colourful movements or smooth movements. Then the liquid-glass look is kept, and the background doesn't matter, because our own wallpaper is running behind."
- **Context:**
  - The app already has upstream's wallpaper setting (WallpaperCard). With a wallpaper set, the window draws its own backdrop and the full drawn material refracts it (OD-0014, OD-0005), as in the eight Mac screenshots over the app's own wallpaper (OD-0003).
  - Without one, the glass sits over the desktop as Acrylic.
- **Effect:**
  - The port gains built-in wallpapers: colourful and abstract, some still and some smoothly animated. The liquid glass refracts them everywhere it appears. A user's own wallpaper still works as upstream.
  - This is a new, port-only feature, so it is designed on a variants page for the owner's pick before it is built (OD-0070, OD-0104 style).
  - The page shows the wallpapers behind the app's real glass, each with its per-frame cost: an animated wallpaper re-renders the glass above it every frame, so smooth playback, battery and the composer light are measured.
  - Open for the pick:
    - which wallpapers ship;
    - whether one is the default for new users (upstream's default is no wallpaper, OD-0014);
    - the motion speed;
    - Reduce Motion behaviour.

## OD-0110 A whole wallpaper library: a Static collection and an Animated collection, vibrant to minimal (2026-10-05)

- **Context:** OD-0109's variants page (https://claude.ai/artifact/EiKx64RynPdYEAqsnqrumM, 12 wallpapers).
- **Owner, verbatim:** "I'm not gonna answer Wallpaper Picks artifact, we need a whole sub section of collection of static wallpapers and then another whole sub section of collection of animated wallpapers with the ability to set their speed, all loopable and way more premium, but we need to ensure we cater all users, some like too vibrant while others are minimalistic and don't like too distracting yet colorful. So, we need to create a whole library."
- **Effect (replaces OD-0109's pick questions):**
  - The port ships a wallpaper library, not a short list. The wallpaper settings get two sub-sections: a collection of Static wallpapers, and a collection of Animated wallpapers with a speed control.
  - Every animated wallpaper loops seamlessly, with no visible restart and no period a viewer can notice.
  - The whole library is far more premium than the twelve on the variants page.
  - The library covers every taste. Each collection spans tiers from vibrant through balanced to minimal: calm and undistracting, yet still colourful.
  - Upstream's default stays: no wallpaper until the user picks one (OD-0014).
  - Glass behaviour, cost and Reduce Motion follow OD-0109's notes. The animated wallpapers are measured natively, so the glass over them stays smooth.
  - **LEAD plan:** the library is first drawn as one browsable page, the source of truth for each wallpaper's look and code. That page is published for the owner to see, not to pick from. The library is then built into the app with per-wallpaper fidelity tests against that page, as VT-8 did for the orb.
- **Amendment, owner, 2026-10-05 (chat, verbatim):** "Maybe give options within those animated wallpapers to choose color packs? set speed, etc?"
  - **Effect:** every animated wallpaper offers per-wallpaper options:
    - colour packs, 4-6 named packs chosen by swatches, each with dark and light palettes, spanning quiet to rich;
    - speed;
    - at most two more where they fit, such as motion amount or brightness and softness.
  - For consistency the lead extends colour packs (3-5) to the static wallpapers.
  - Switching packs or options crossfades, never cuts (OD-0076).
  - One shader per wallpaper takes the pack as data.
- **Second amendment, owner, 2026-10-05 (chat, verbatim):** "I love all the wallpapers and animated wallpapers but we need to increase our library 3x and with more premium options." Then: "Like they should feel like they're shipped by Apple, Samsung or Microsoft. Extremely premium."
  - **Context:** the two library pages. Animated: https://claude.ai/artifact/LFRLdPKiHhScEMwqBBs55c (24 wallpapers). Static: 30 wallpapers, not yet published.
  - **Effect:** the library grows to three times its size, so 90 static and 72 animated wallpapers. The current 54 are kept, since the owner loves them.
    - Every wallpaper, new and old, is held to the bar of a wallpaper shipped with an operating system by Apple, Samsung or Microsoft: extremely premium, with nothing generic or computer-rendered.
    - Each wallpaper gets more premium options.
    - The tiers (vibrant, balanced, minimal), the packs with dark and light palettes, the seamless loops, the speed control, crossfades instead of cuts (OD-0076), the per-frame cost budget and the SkSL subset all still apply.
- **Approval, owner, 2026-10-05 (chat, verbatim):** "All wallpapers approved"
  - **What was approved:** both published library pages. Animated: https://claude.ai/artifact/LFRLdPKiHhScEMwqBBs55c, 72 wallpapers (version 2). Static: https://claude.ai/artifact/CrziwfNftCftsFK6mGgUPz, 90 wallpapers.
  - **Effect:** all 162 wallpapers ship as drawn, in the design snapshot at porting/first-release/design-src/wallpaper-library (static at ab4308969, animated at 76a61a4e2). That snapshot is the source of truth for the app's per-wallpaper fidelity tests. This includes the four premium options (Dynamic, Match accent, Perspective, Focus) and the names, all unique across both collections.

## OD-0111 Every update ships with its What's new, and the app shows it with the update (2026-10-05)

- **Context:** the self-update (UE-1, ED-320) already reads a release's markdown notes and the About page shows them as section cards, but the packaging script's `-ReleaseNotesFile` was optional, so a release could go out with no notes.
- **Owner, verbatim:** "अच्छा एक चीज़ मैंडेटरी करवा देना कि आपकी जो अपडेट होती है ना जो मैं फॉर एग्जांपल अपने गिट हब पे अपलोड करूँगा न्यू अपडेट तो जब ऐप के अंदर शो होगी किसी और यूज़र को कि अपडेट आई हुई है ठीक है ना तो यू नो इट शुड शो कि न्यू वर्जन के अंदर क्या चेंजेस हैं क्या अपडेट्स हैं ठीक है ना वो एक चेंज लॉग और अपडेट्स व्हाट्स न्यू ठीक है ना वो होना चाहिए ठीक है ना विथ एवरी पुश ऑफ न्यू अपडेट ये चीज़ साथ साथ पुश होनी चाहिए ताकि जिसका भी ऐप में अपडेट आए उसको पता हो कि क्या चीज़ चेंज हो रही है क्या अपडेट हो रहा है क्या नई चीज़ ऐड हो रही है एवरी थिंग ठीक है ना"
- **In English:** "Make one thing mandatory. When I upload a new update to my GitHub and the app shows another user that an update is available, it should show what changed in the new version: a changelog and What's new. It must be pushed together with every new update, so whoever gets the update knows what is changing, what is updated and what is new."
- **Effect:**
  - Release notes are mandatory. A release cannot be packaged or uploaded without a What's new for that exact version: packaging fails closed when the notes are missing, empty, for another version, or have no recognised section.
  - The notes come from the repository's CHANGELOG, one section per version, in the parity headings (New features, Bug fixes, Refinements), so every pushed update carries its own changelog.
  - When the app offers an update, the user sees that version's What's new (the parity section cards) before installing, and can read it again after updating.
  - Nothing is published or uploaded without the owner's word (unchanged).

## OD-0112 The app icon is D "Aurora" (2026-10-05)

- **Asked:** 2026-10-05. The owner found the OD-0015 icon and its animation less premium than the app ("an app's icon tells you what the app will be like inside") and asked for twenty original directions, each with a launch animation and an idle loop, free of the old three-colour mark. Page: https://claude.ai/artifact/1FCzPs4jpmNAFidJ5FRotZ (copy: `porting/first-release/design-src/app-icon/`).
- **Answer, verbatim:** "app-icon: D lock it and ship it, see who you want to delegate this task to , claude helper or codex whichever"
- **Effect:**
  - Supersedes OD-0015. The port ships D "Aurora": a tile of smoked night glass holding an aurora curtain (emerald foot, teal above, violet fringe) over a dark ridge and a few stars. The locked source is `porting/first-release/design-src/app-icon/src/icons-a-j/D-aurora.js` with `iconkit.js`, including its own small-size art for 16 to 32 px.
  - Every place OD-0015 named uses it: the executable and its `icon.ico`, the window, the taskbar, Alt-Tab, Start, toasts, the in-app logo images, and later the installer.
  - The loading mark and boot animation (OD-0016, OD-0018 "Comet", ED-097/102/103/154) are remade from D's launch animation and idle loop. That motion goes to the owner as a review page before it ships; OD-0108's warm-up behind the boot animation is unchanged.
  - The OD-0015 files stay in history only. The deviation list in the final report names the new icon.

## OD-0113 Boot animation B and three Aurora loaders (2026-10-05)

- **Asked:** 2026-10-05, after OD-0112. Page: https://claude.ai/artifact/UqPvGBv5E3ToEtMLXrihvo (version 2; copy: `porting/first-release/design-src/loading-aurora/`). Round 1 had three boot variants, a per-turn pulse and an icon-based loading mark; the owner picked boot B but asked that it grow into the real window's shape, rejected the icon as a loader, and asked for five new loaders that only lightly resemble the Aurora. The per-turn pulse was dropped: OD-0018's "one pulse per turn" meant a turn of the old spiral, and the chat keeps the composer's working dots.
- **Answer, verbatim:** "loading: boot=B, loader=?, history=2, diff=5, attachment=4"
- **Effect:**
  - Boot: variant B. The Aurora tile appears large over the dimmed desktop, runs D's launch, and when the app is ready its rectangle grows into the main window's exact frame (position, size, Windows 11 corner radius, title-bar band) on the window's own reveal timing, so its last frame is the window; the glass and content fade in inside that rectangle. The OD-0108 warm-up runs behind it with the window hidden. It replaces OD-0018's Comet boot.
  - Loaders (OD-0016 / ED-097 sites, 400 ms rule kept): the older-chat history load uses loader 2 "Curtain Fold"; the diff pane (above its "Reading the diff..." label) uses loader 5 "Aurora Sheen"; the large attachment preview uses loader 4 "Flow Arc". The icon is never a loader. Under Reduce Motion each is a still pose that only breathes in opacity.
  - Nothing in the chat moves while the model works except the composer's working dots (parity, CZ customization).

## OD-0114 Multiple accounts ship in the first release (2026-10-05)

- **Context:** the owner's multiple-accounts feature (several accounts per service, the Handshake, Pills, link up, one-message opinions, local models, per-account enable/disable), designed with the owner on 2026-10-05. Everything agreed is in `E:\Droppy\work\design\ideas\multi-account.md`; the design page is https://claude.ai/artifact/JuyXgrnFdKLar65QWgeL7X. The feasibility study (branch codex/ma-0, `porting/first-release/multi-account-FEASIBILITY.md`) found it technically feasible for file-based CLIs (Copilot CLI and Antigravity keep sign-ins in Windows Credential Manager and stay single-account), and quoted provider terms against bypassing protective measures and circumventing rate limits; the lead raised this risk with the owner before this decision.
- **Owner, verbatim:** "once we finalize the multi account page, that too needs to get pushed in this release"
- **Effect:** multiple accounts join the first-release scope (OD-0103) as a new feature family. Its interface is built only after the owner finalizes the design page, and its picks are then recorded verbatim; design-independent foundations (accounts store, per-account isolation and limits, the local provider) may start at once. The installer still comes last (OD-0105).

## OD-0115 Multiple accounts: the design picks (2026-10-05)

- **Page:** https://claude.ai/artifact/JuyXgrnFdKLar65QWgeL7X (version 8; copy: `porting/first-release/design-src/multi-account/`). Every agreed rule: `porting/first-release/multi-account-NOTES.md`.
- **Owner, verbatim:** "multi-account: handshake=A, composer=A, popover=ok, link=A, opinions=B, usage=B, card-layout=A" and "Just fix that generic toggle and ship it with the current release."
- **Effect:**
  - Handshake animation A (neural filaments), from and to the shared capsule, in the usage light.
  - Composer account control A (the glass capsule), styles from Customization, default Merged capsule.
  - The account popover as shown; link up A (tap the link beside an account); one-message opinions B (an "Ask others" button); usage panel B (the current account, the others one tap away); first-time card layout A.
  - Every switch is the port's own SmallSwitch (SettingsView.cs, ED-280), never the page's generic toggle.
  - It ships in the first release (OD-0114).

## OD-0116 Automatic account switching is off by default and asked the first time (2026-10-05)

- **Context:** the feasibility study (codex/ma-0) and the Handshake engine (cloud/ma-5) note that provider terms forbid bypassing protective measures or circumventing rate limits; the lead asked whether "Keep me going" starts on or off.
- **Owner, verbatim:** "Switched off by default, app asks user the first time."
- **Effect:** automatic switching ("Keep me going", the Handshake into another account) is off by default for every service. The first time an account runs out, the first-time card asks the user (OD-0115 layout A, with the Remember line); nothing switches automatically until the user chooses it. Pills a user creates are that user's explicit choice.

## OD-0117 The Pills section is visible to everyone (2026-10-05)

- **Owner, verbatim:** "Also, pills section in settings will be visible to users who have single accounts connected as well, just the options will be disabled with a message letting them know what this feature is and what would enable its functionality (multiple accounts)"
- **Effect:** amends the notes' "Pills only once 2+ accounts or services are connected". Settings › Pills always shows in the sidebar. With fewer than two accounts or services connected, its controls are present but disabled (dimmed, not focusable as actions), under one short plain message that says what Pills are (a chain of accounts and models that Marrow moves through when one runs out) and what turns them on (connecting a second account or service, with a button to Settings › Models). Everything enables live, with a transition, the moment a second account or service is connected.

## OD-0118 Import skills from other apps on the PC (2026-10-05)

- **Owner, verbatim:** "Also, can we have an option to import skills from other apps and llms on the pc in skills settings?" then "It goes in this release"
- **Effect:** a new first-release unit SK-18 in Settings › Skills: an "Import skills" action scans the places other AI tools on this PC keep skills (Claude Code's skills and plugins folders, Codex's skills, Antigravity's, Cursor's rules, and any folder the user picks), lists what it found grouped by app (name, one-line description, source), and imports the ticked ones either as a Copy (Marrow's own) or a Link (kept in sync with the original). Nothing in the other apps is moved or changed; duplicates offer keep both / replace / skip; imported skills carry a quiet "from <app>" tag and can be removed; they then work wherever Marrow gives skills. Design follows the existing Skills pages (SK-15) and goes to the owner only if a new layout is needed.

## OD-0119 Multi-account checklist conflicts settled (2026-10-05)

- **Owner, verbatim (Hindi, as spoken):** "हैंडशेक स्विच ऑन बाय डिफॉल्ट हाँ तो ऑफ करके डिफॉल्ट में रखो ना और ऐप पहली बार पूछेगी तो फिर ऑन होगा" · "द बिल्डर मस्ट पिक वन स्मार्टली जो सही लग रहा है वो पिक कर लो" · "मैंने और स्टाइल्स मांगे थे तो वो अपडेट हो गया बारह स्टाइल्स हैं अब".
- **Effect:**
  - Pills visibility: OD-0117 stands (the section is always in Settings; disabled below two connected accounts, with a note saying it turns on once two are connected and what the feature does). The page's 2+ gate is outdated.
  - Switching and the Handshake into another account: off by default; on only after the user agrees when the app asks the first time (OD-0116). The page's `hs: true` default is outdated.
  - Per-account Handshake point: the builder picks. Lead pick: the account's ⋯ menu "Handshake at…", plus a small quiet note on that account's row only when its point differs from the default (NOTES:56, matches the page's Settings footer).
  - Pill-in-composer styles: the page's twelve styles are current; the NOTES list of four is outdated.

## OD-0120 Confirm before sending a conversation to another service (2026-10-06)

- **Owner, verbatim:** "Ask for confirmation and have the option to check "Don't ask again" for the user to select it."
- **Effect:** Link-up and "Ask others" ask for confirmation before the conversation goes to another service. The confirmation has a "Don't ask again" checkbox, unchecked by default; only the user can check it. Replaces the planned quiet notice line (ma-3b5-plan Q2).

## OD-0121 Voice pill: design-exact, both rims; Handshake sound included (2026-10-06)

- **Owner, verbatim (answers on the Voice Pill Decision page):** "Rim: C" · "Fix: Fix it anyway you want it, I've let you known what design I want" · "Sound: include it"
- **Effect:**
  - VT-12: the voice pill follows the approved design page (voice-composer.html) exactly: no added blurs on the body, cross or glow; the capsule edge antialiased once; CSS blur radii converted to sigma; no glass refraction inside the pill; the radial clean-cap mask. Rim choice C: the shared glass rim and shade stay, with the page's rim stack on top.
  - The lead chooses the implementation. Tests, tolerances, references and the speck metric stay unchanged (standing rule); any spot that still fails comes back to the owner with its exact location.
  - Multi-account: the Handshake's soft sound ships in this release, using the locked design's recipe (link-up: sine 523.25 Hz 1.1 s and 783.99 Hz 1.3 s at +0.08 s; in-sync tick: 1318.5 Hz 0.18 s). Replaces ma-3b5-plan Q1's default of leaving it out.

## OD-0122 Handshake readiness for Codex and Pi (2026-10-06)

- **Owner, verbatim:** "Same tick as Claude" · "You can run it once but I think I'm out of codex usage, maybe luna might work."
- **Effect:** where the app loads the conversation into a waiting Codex or Pi account without a model turn (history injection, research claude-cli-standby-research.md), the composer capsule shows the same in-sync tick as Claude once the history is loaded. The lead may run ONE local Codex check with the owner's signed-in Codex (a model the owner suggested: Luna) to confirm that injection makes no model call: inject a short test history into a throwaway thread, read the log, delete the thread; no message is sent to the AI.

## OD-0123 Voice pill round 2: outer line removed, Inter kept, design wins over the speck test (2026-10-07)

- **Owner, verbatim (answers to the Round 2 questions on the Voice Pill Decision page):** "Yes, remove it" (fix A, the shared glass's dark line just outside the pill's ends) · "App font (Inter)" (fix B declined) · "Design wins, you rule the test" (to: about 160 V-T133 cases still fail and the approved design page fails the same cases in Chrome).
- **Effect:** the voice pill drops the shared glass's outer contact line on its ends (voice pill only; both rims and their light top and dark bottom stay, OD-0121 C). The label keeps the app font Inter. The approved design wins over V-T133; how V-T133 treats cases the design itself fails is to be settled in the exact rule the lead proposes and the owner confirms (recorded in the next OD). The linear fade kept under the page's radial clean-cap mask (V-T132, ED-387) stands.

## OD-0124 Real-program checks for the seamless handover (2026-10-07)

- **Owner, verbatim (to: may I run a short local check per service with your signed-in apps: open a throwaway chat, load a short test history, confirm nothing ran, delete it; no message is sent to any AI):** "Yes, all installed"
- **Effect:** the lead may run that check once per installed CLI service (Codex and any other installed one) using the owner's existing sign-ins: a throwaway chat or session, a short test history loaded without a model turn, a check that nothing ran, then deletion. No message is sent to any AI, no credentials are typed, no settings are changed. Extends OD-0122.

## OD-0125 V-T133 judges the voice pill against the approved design (2026-10-07)

- **Owner, verbatim (to the lead's proposal: the speck detector stays as it is, but a speck fails only when the approved design page, drawn in Chrome at the same size, state and background, has no speck within 1 pixel of the same spot):** "Do whatever is your recommendation on this issue. I'm tired of dragging this. I need to get this port completed."
- **Effect:** the lead's recommendation is adopted. V-T133's detector (7x7 median, more than 8 levels, small components, ink and ball exclusions) is unchanged. A speck on the port's pill fails the test unless the approved design page voice-composer.html, rendered in Chrome at the same scale, state, theme and backdrop, has a speck within 1 pixel of the same spot. The Chrome renders are generated once by a checked-in script, locked as test references with their hashes, and never regenerated to make a failure pass. Every other test, tolerance and reference stays unchanged (OD-0121, OD-0123).

## OD-0126 GAP-2: the approved design's 0.1 s fade wins over the flat 15% step cap (2026-10-07)

- **Owner, verbatim (to: the approved Customization design fades the old detail out in 0.1 s, a CSS ease as on the Mac; at 60 fps it must drop about 20% in its first frame, but the smoothness test allows at most 15% per frame; which wins?):** "Design wins (Recommended)" — the option read: keep the exact 0.1 s fade; the 15% check judges this motion against the design's own curve, a step is allowed only if the approved design takes the same step; same idea as OD-0125.
- **Effect:** the detail-out fade keeps the design's exact 0.1 s ease. GAP-2's per-frame step check stays at 15% for every motion, except that a frame step is accepted when it does not exceed the approved design curve's own step at the same frame (sampled at 60 Hz from the source oracle, no extra margin). The test cites this OD. No other tolerance changes.

## OD-0127 The palette refresh control: Appearance, a "Model colours" row (2026-10-07)

- **Owner, verbatim (to: where should the 'refresh colour palettes' control go? A: Appearance row; B: Updates table row; C: Models page line; and are the words on the page right?):** "A: Appearance row (Recommended)" and "Yes, use them as shown (Recommended)".
- **Effect:** completes OD-0067's visible control. Settings > Appearance gets a "Colours" section with one row: title "Model colours"; a strip of maker dots; resting line "Checked {when} · {n} models from {m} makers"; a "Refresh" button that reads "Refreshing…" and is disabled while it runs; while running, "Checking OpenRouter and {k} providers… {i} of {n} models" with the Updates page's progress line under the row; when done, a summary below the row ("{x} new models coloured", "{Maker}'s shades re-spaced", "New maker: {Maker}") that stays until the page is left; nothing new: "Checked just now · no new models"; offline: "Couldn't reach the model lists. Check your connection and try again." with colours unchanged. Changed maker dots glide to their new shades (OD-0076; still under Reduce Motion). The refresh runs the automatic engine (OD-0064/0065/0066). Locked design: E:/Droppy/work/design/decisions-2026-10-07/palette-refresh/palette-refresh-locked.html (option A). Decision page: https://claude.ai/artifact/AhTyKzxT4qbYEeGTGW2E8f

## OD-0128 VT-12 voice pill approved by eye as drawn; the strict speck test becomes post-release polish (2026-10-07)

- **Owner, verbatim (to: after the side-by-side comparison of 12 failing cases, design in Chrome vs the port today, approve as is / restore the body beside the orb / not yet?):** "Approve as is".
- **Effect:** the voice pill ships as drawn at the head that carries codex/vt-12-finish plus the label fix from claude/vt-12-opus: rim C (OD-0121), the area beside the orb cleared (V-T132 stays and must pass 224/224), Inter label rasterised as Chrome does. V-T133 (the speck test) is NOT changed, skipped or loosened: its remaining failures are recorded as known, owner-accepted failures (named list, this OD) that do not block merging or release, and stay on record as post-release polish (next steps: remake the references over the current pill-less backdrops, then resume from scratch/handoff/claude-vt12-opus.md). Any NEW V-T133 failure that is not on the recorded list, and any V-T132 failure, still blocks. Comparison page: https://claude.ai/artifact/JHFHUysUvgXEddTi9HJaYn

## OD-0129 Marrow's first release is version 1.0 (2026-10-08)

- **Owner, verbatim (after the first installer was built as 1.7.0, Droppy Code's own number):** "The version has to be v1.0".
- **Effect:** the single version source src/Marrow.App/AssemblyInfo.cs reads CFBundleShortVersionString "1.0.0" and CFBundleVersion "1" (Directory.Release.props derives Version 1.0.0, FileVersion 1.0.0.1, InformationalVersion 1.0.0+build.1, the Velopack package and Marrow-1.0.0-Setup.exe from it), and the CHANGELOG entry is ## [1.0.0]. This is a deliberate deviation from project.yml:13-14's MARKETING_VERSION 1.7.0 / CURRENT_PROJECT_VERSION 21 (PR-UPDATE-112): parity tests that assert the app's OWN version are updated to 1.0.0 / 1 with this OD cited, as strictly as before. Test fixtures that use "1.7.0" as an arbitrary running or offered version (self-update feeds and similar) are unaffected and unchanged.

## OD-0130 The composer text fades beside the open voice pill instead of ending in a hard cut (2026-10-08)

- **Owner, verbatim (hands-on test of the installed build, translated from Hindi):** the composer text "is cut off right there" beside the voice pill; "this should not happen, it should be a smooth fade as it nears the pill".
- **Effect:** while the voice pill holds the text column's right edge in (V-12.6), the column's last 24 points fade from opaque to clear (ComposerView.FadeBesidePill); at rest there is no mask. This amends voice-composer.html's `.field{overflow:hidden}` hard edge for the open pill only; the inset itself (10 before the pill, 0.42 s ez) is unchanged. The owner's separate report of the text first overlapping the pill and then snapping is not reproduced headlessly and stays open (owner finding F5) until his screen recording.

## OD-0132 The voice pill sits entirely inside the composer and opens with a real animation (2026-10-08)

- **Owner, verbatim (hands-on test of the installed 1.0 build, translated from Hindi):** when the voice orb opens beside it "it bulges the composer out from below ... can it not be placed in such a position that it doesn't need to bulge out".
- **Effect:** the pill is placed entirely within the composer's bounds (no swelling of the composer's edge) and opens and closes with a real animation (the recording showed it appearing within two frames). Everything else of the approved pill (OD-0121/0123/0125/0128/0130) stays. The OD-0128 fixed renders that encode the old placement no longer describe the wanted design: their failures caused only by the move are recorded as the OD-0132 placement list (T46), the references are not edited, and they are regenerated only after the owner approves the new placement by eye from T46's 60 fps capture.
- **Owner, verbatim (2026-10-08, translated), same decision:** in the composer "the attach icon, the ask-all-models icon ... the voice orb ... the send button, nothing is in the centre ... one thing is higher and one lower ... if they were in the centre there would be no problem". Measured on his video: the text and the all-models icon sit about 4 px above the composer's centre, the attach icon, orb, model chip, usage ring and send about 5 px below.
- **Effect (also OD-0132):** every control and the single-line text share the composer's vertical centre (within 0.5 px at 100/125/150/200 %); this deliberately deviates from the source's bottom-anchored placement (e.g. the outside attachment circle's centre at height-22) for a single-line composer, while multi-line growth keeps the source's bottom alignment. Tests that encode the old single-line offsets are retired only assertion by assertion, under an ED, with an equal-or-stricter centring assertion in their place.

## OD-0133 The main window does not wait for the providers' sign-in checks (2026-10-10)

- **Context:** the launch analysis (2026-10-10) found Ready waited about 3 s on every launch for the provider refresh (the Antigravity sign-in check alone 2.7 s). The Mac app shows its window first and checks afterwards.
- **Owner, verbatim (answer to the lead's question):** "Show window first (Recommended)" — the option read: "About 3 s faster on every launch, same as the Mac. Provider status fades in softly when its check finishes."
- **Effect:** amends OD-0108 for provider checks only: the boot hand-over no longer waits for the provider refresh or sign-in checks; they start as early as possible and finish after the window shows. Every GPU and content readiness rule of OD-0108 and OD-0113 stays. Provider and account status that arrives after the hand-over changes with a soft transition, never a snap.

## OD-0134 The cost saver ("Smart Saver" working name) for the next version (2026-10-10)

- **Owner, verbatim (answers to the lead's questions):** default mode when turned on: "Cache + auto-compact (Recommended)". Keep-warm for Claude Code and Codex chats: "Ping but hide the ping message itself but rather display something like "Cache re-engaged" or "Auto-compressed to 400k" so user knows when its happening but also show disclaimer to user first time stating that the actual message stays hidden by default unless the user wants to make it appear? and have that setting in the settings too? Maybe under the a new section called "Pro Saver"? (Or what name do you suggest we give this cost saving feature?)"
- **Effect:** the cost saver (E:/Droppy/work/next-version-ideas.md section 7a) defaults to cache keep-warm plus per-model auto-compact; keep-warm pings in CLI chats are hidden and replaced by a short visible event line; the first time, a notice explains the hidden ping, with a setting to show it; its settings live in their own Settings section (name pending the owner's pick). Designed on a canvas, owner pick, then built test-first.
- **Amendment 2026-10-10, owner, verbatim (Hindi, romanised):** "theek hai, Phoenix lock kar do phir" ("OK, lock Phoenix then"). The cost saver is named Phoenix.
- **Amendment 2, 2026-10-10, owner, verbatim:** "Cruise, go with it", then "don't finalize cruise yet", then "Lock Ignis" (final name: **Ignis**). Design: "Kindle ko lock karo" (variant E Kindle), "in settings and in phoenix popover and in updates section, there should be option to update latest context, cache points waghera for each model", "take everything useful from his idea and refine it into our design without repetitions", "just get rid of the W and other keyboard shortcuts in Ignis? They don't make sense to me.", and (Hindi, translated) "Lock in the Ignis design ... I want Ignis launched integrated in the whole app right now, together with these fixes, because I need the savings." **Effect:** Ignis ships in this release (1.0), as designed on https://claude.ai/artifact/UMZnXKtKKtVpjKVKdLA6C5 (Kindle section, Settings > Ignis with savings history, Updates-page row for model points, timeline event lines, first-time hidden-ping notice), no keyboard shortcuts (Escape closes the popover as every popover does), plain words (quick memory, chat size, tidy up). It is a named deviation from the pinned source (a feature upstream does not have); its supporting deviations (API date line date-only, chunked history trim, CacheUsage provider event, /compact instructions, Hydra scout model under Smart helpers, hidden keep-warm ping turns) are recorded as ED entries ED-NEW-ignis-*. Implementation plan: E:/Droppy/work/ignis/PLAN.md.

## OD-0135 The product is named Nth (2026-10-10)

- **Owner, verbatim:** "Lock nth" (chosen from the naming review https://claude.ai/artifact/Em5diFQDk8XNqdARBV1oJy, research in E:/Droppy/work/naming/app-name-research.md).
- **Effect:** the Windows product name changes from "Marrow" to **Nth** in everything a user sees (window titles, About, Start menu, taskbar, installer, notifications, Settings text, update texts) and in the install and data identity, which moves from "Marrow Code" to "Nth" with a one-time copy of an existing "Marrow Code" data folder on first launch so nothing is lost. Feature names stay (Hydra, Ignis). Internal namespaces, project and repository names may stay as they are (not visible). Lead's engineering decision: the product name is written "Nth" in text; the logo wordmark may be lower-case "nth". Formal trademark search (US, EU, India) before a public launch remains the owner's step.

## OD-0136 Nth brand: icon B "Exponent" locked; motion to be upgraded (2026-10-10)

- **Owner (Hindi, translated):** "Lock B. B is great, the Exponent, the n with a small raised bead; lock it down." Then: the loading animations look basic and must be as premium as the icon (take pointers from how Claude's own app animates its dots: premium, smooth); thinking needs a better, upgraded animation; the progress bar's end knob must be shiny and sparkly, like metal work throwing off a few small, light sparks.
- **Effect:** app icon, in-app logo, boot mark, empty-chat mark and tray mark use concept B from https://claude.ai/artifact/ETPH7CQtAokFj7bcj2LZEU (source E:/Droppy/work/design/nth-brand/nth-brand.html), with the thicker cut at 16-24 px. The bead is the moving part of every loader. Ember stays Ignis-only. Loader, thinking and progress designs go back for a premium revision before they are built; the boot and the icon proceed now.

- **OD-0136 amendment 1 (2026-10-10, owner review of motion v2, https://claude.ai/artifact/ETPH7CQtAokFj7bcj2LZEU v2):** Icon APPROVED. Boot APPROVED. Rejected: all three spinners ("way too opposite of premium"), all three thinking options, the sparkly progress knob ("discard it"; no sparks, no bead on bars), the glossy bead in motion ("looks like a necklace bead"), radial glow halos, low-contrast silver ticks. Kept as they are: Hydra's animations, the composer working dots, and the account pill's neural handshake animation. Liked: the light sweep in "Reading the diff", the shine passing over text, model-colours loading (but its glow radius must be fixed). Also: the composer send button should be slightly bigger. Next: a step-by-step review page, one animation per step, answers Yes / No / Needs refinement / Better idea + comment.

- **OD-0136 amendment 2 (2026-10-10, owner answers on the round-3 review https://claude.ai/artifact/MYkaBMpBSz46x4JvmL7rP2, pasted verbatim in chat):** spinner option B, thinking option B, progress bar option B; Yes to progress ring, diff loading, attachment loading, list loading, tool call, download/update, connecting, image preview, model colours, getting ready. Send button: "Yes, but the stop and arrow inside the circle needs to be enlarged, currently its too small." Thread history: "No. This is not premium at all, perhaps a loading animation that's very premium?" (goes back for one more design round). Source of the build specs: E:/Droppy/work/design/nth-brand/nth-motion-review.html.

- **OD-0135 amendment 1 (2026-10-10):** owner, verbatim: "Also, keep in mind the locked name is nth and not Nth". Every user-visible product name is written in lower case, "nth", including at the start of a sentence and in window titles, About, Start menu, installer and notifications. Qualifiers keep their own case ("nth Dev", "nth Setup"). Internal ids may differ.

- **OD-0136 amendment 3 (2026-10-10, owner answers on rounds 4-6 of https://claude.ai/artifact/MYkaBMpBSz46x4JvmL7rP2, pasted verbatim in chat):** round 6: "12 thread-history: Yes", "9 send-button: Yes". Thread history = B1 "Flick" (n line draws, its tip flicks off at the arch top to become the bead, light passes and ends in the bead, the n empties and the bead fades down from the flick point so both finish together). Send button = 32 px circle, arrow 13.3 x 15.1 px stroke 2.4 round caps, stop 11 px radius 2.85. Every motion step is now approved; the spec is E:/Droppy/work/design/nth-brand/nth-motion-review.html (round 6).

## OD-0137 The 6 send-button-shift voice pill speck failures are accepted for the nth 1.0 release (2026-10-10)

- **Owner, verbatim (asked whether to accept 6 new V-T133 failures caused by the approved 32 px send button moving the pill 4 DIP left; then on why an approved design fails):** "Why is the pill moving 4px a failure? If that is an approved design from me, it shouldn't fail anyways right?" then "Do not lengthen it for now, just accept with the 6 failures for this release, mark it for the next time we release more features. Next test would be updated."
- **Effect:** amends OD-0128's "any NEW V-T133 failure still blocks" for this release only: the ComposerVoiceCleanPillTests failures at head 39b9dd99d (183 of 470; 173 as at 00858e24f plus the tip-only cases from OD-0136 step 9's send disc growing 4 DIP to the left, which shifts the trailing-aligned orb and pill) do not block the nth 1.0 installer. The test is not changed, skipped or loosened. Evidence: scratchpad res-base/res-tip trx sets (stable across two runs each).
- **Next feature release (must be done before it ships):** V-T133 is re-anchored to the pill's actual rendered position (same speck rules, tolerances and coverage, equal-or-stricter, "design wins" ED), so approved layout moves no longer read as specks; then the remaining failures are re-classified against the OD-0128/OD-0132 lists.

## OD-0138 Ignis keep-warm cooling: idle limit, learning, and the sidebar ember (variant B) are approved for nth 1.0 (2026-10-10)

- **Owner, translated (asked whether Ignis keeps pinging old chats):** no lock or sleep pause, because he locks the PC while work runs; chats cool after an idle limit, default 12 hours, plus learning; both are settings ("Cool idle chats after", "Learn when to cool") in Ignis Settings and in the Ignis popover.
- **Sidebar mark, owner's pick (review page variant B):** a breathing ember line on a warm chat's leading edge; hovering an invisible, generous zone (like the window caption buttons) fades in the flame slightly brighter and slides out a "Turn off" pill; clicking douses the flame and it stays dim. Owner, verbatim: "B but once turn off has been clicked, the pill must then change to turn on for providing the user an option to turn ignis back on for that chat". On the tooltip: "tooltip looks bad, maybe remove it?" - removed. The flame is shown only in the sidebar, never at the top of the chat.
- **Approval, verbatim, after the built screenshots:** "As long as that Ignis folder is a demo folder, approved." (The "Ignis" project and its chats in the screenshots are capture-only demo data; nothing ships them.)
- **Built at:** claude/ignis-guards b9cea82ed, merged into claude/release-integration at 4bbc9d6c4. Known limits accepted with the approval: the hover zone sits before the row's time and buttons; Turn on for a chat whose cache already expired warms it on the next message; per-chat on/off resets on app restart; a long title is masked under the pill while hovered.

## OD-0142 The nth boot: the tile pops forward, the n draws, the bead appears; no shine (2026-10-11)

- **Owner, translated from Hindi (2026-10-11):** "In the boot-up animation, on the n logo, I want to remove the shine that comes from the top. That shine looks cheap, it is not looking good. Rather, the silver box, the rounded-square box, should pop up and come slightly forward, grow a little toward the viewer, and then the n should form inside it and the dot should appear. It should look professional. The shine has to go."
- **Owner follow-up, translated (same day):** "No shine inside it, but the gradient inside the rectangular box, maybe we can make it move, animate it. I think that will look nice."
- **Effect:** overrides OD-0136's approved boot motion (the gather from blur, the n draw, the bead's drop, the hot light sweep, the breath and the hold's repeating slow sweep); the icon, its stage tile and every other OD-0136 / OD-0113 rule (the scrim, the hand-over at the unchanged time, the grow into the window, OD-0108's warm-up behind it) stay. The boot mark now: the silver tile pops from 0.86 to 1 on a soft spring (peak about 1.012, never past 1.02) while fading in over 0.3 s, its drop shadow lifting with it (tight and close under the tile to the design's soft box-shadow); its own gradient turns from 22 degrees off to the icon's angle and rests exactly on it as the bead lands (no band, streak or moving highlight); at 0.40 s the n draws from its left foot over the arch to its right foot in 0.56 s with a round head that lands flush on the finished glyph; at 0.96 s the bead springs in from 0.4 (one small overshoot, about 1.06) over 0.40 s; the mark is still from 1.36 s and holds until the hand-over (at least 1.8 s). Reduced motion: the finished mark cross-fades in, with the static gradient. The empty chat's tile keeps its one soft sweep (OD-0136). Engineering record: ED-NEW-bootpop-1. Preview: E:/Droppy/work/design/boot-logo/.
- **Numbering note:** OD-0139 is used three times on other branches and OD-0140/OD-0141 by PC control (claude/pc-control); this entry takes OD-0142.

## OD-0140 PC control: any model from any provider can see and use apps the user allows (2026-10-11)

- **Owner, verbatim:** "I want ChatGPT's PC Control functionality/skill to be added into the nth app. Any model from any provider should be able to use it to control pc."
- **Effect:** nth gains a PORT-ONLY feature, working name "PC control" (launch name proposed "Tactus", the owner's pick pending), built to the design in `E:\Droppy\work\design\pc-control\DESIGN.md`: nth hosts its own MCP server `nth-pc` (window-scoped, accessibility-tree-first observation with optional JPEG screenshots, element-number and observation-bound point targeting, settle waits, batching, a per-call audit log) and serves it to every provider: CLI and ACP providers through the same named-pipe relay as the advisor, API providers through native tools. The feature has no Swift counterpart and is recorded as a deviation by owner ruling.
- **Defaults decided by the lead with this ruling:** the feature is off by default (Settings master switch); confirmations for send, delete and pay actions are on by default; the stop pill sits top-centre of the screen that holds the controlled window (its UI is designed and canvas-approved later); inside nth chats, when the feature is on, nth's PC control replaces Codex's own computer-use plugin so two controllers never act at once.
- **Clean-room rule:** OpenAI's plugin, helper and skill text are proprietary; nth learns their design only. nth's code, tool schemas and model instructions are written from scratch; no text or code is copied.
- **Engineering record:** ED-NEW-pcctl-1 onwards in `porting/engineering_decisions.md`.

## OD-0141 PC control is named Tactus (2026-10-11)

- **Owner, verbatim:** "I like the Tactus name, you can finalize it for PC Control"
- **Effect:** every user-facing name of the PC control feature (OD-0140) is "Tactus": the Settings group title, the on-screen pill, the approval sheets, the timeline rows and the tool descriptions and instructions models are shown. Plain descriptive helper text (for example "Let models see and control this PC") may stand beside the name. Internal type and namespace names (PcControl, Pc*) and the wire names (the MCP server nth-pc, the pc_* tools, the error codes) stay as they are.
- **Numbering note:** OD-0139 is used by three different entries on other branches; this entry takes the next number above the highest used on any branch at the time of writing (OD-0140, PC control).

## OD-0144 Tactus works beside the user: its own mouse and keyboard, several apps at once, fast and accurate (2026-10-11)

- **Owner, translated from Hindi (DESIGN.md 2.5, 2.8, 2.10 to 2.12):** "Moving my mouse or clicking or anything should not interrupt Tactus control." "It uses its own mouse ... its own keyboard, not mine." "While it works, if I start typing a message, it clicks somewhere else and starts typing, so we clash. Can it use its own keyboard, not mine?" "If a job needs two or three apps, or however many, it should use them at the same time, in parallel, whether the window is in front, behind, or minimized." "Ultra fast, state of the art." "Do not lower quality for speed ... it must use PC control accurately."
- **Effect:** Tactus (OD-0140, OD-0141) is built to the parallel-use design in `E:\Droppy\work\design\pc-control\DESIGN.md` sections 2.5 to 2.13, 4.4, 8 and 9 (with the critique response of 2026-10-11), which the owner approved through the lead on 2026-10-11: the user's input never stops or pauses Tactus; Tactus acts without the user's pointer, keyboard or focus and never brings a window to the front on its own; several apps run at once in their own ordered streams; every action is checked before it returns; speed never skips a check. Only Ctrl+Alt+Esc (configurable), Esc during a borrow of the real pointer or focus, the pill's Stop, the chat's Stop and turning Tactus off stop it.
- **Engineering record:** ED-NEW-pcctl-6 onwards in `porting/engineering_decisions.md`; ED-NEW-pcctl-7 records the design.
- **Numbering note:** written as OD-0142; renumbered OD-0144 on 2026-10-11 because OD-0142 (the nth boot, already on claude/release-integration) and OD-0143 (Nocta) were taken on other branches first.

## OD-0145 Tactus pill v2: pause, stop paths, a draggable pill and answers from the pill (2026-10-11)

- **Owner, through the lead (2026-10-11):** Tactus pill v2 is approved: `E:\Droppy\work\design\pc-control\DESIGN.md` section 10 with its work units P21 to P30, and canvas v3 (https://claude.ai/artifact/3ejBMyVZiy44VPpycb5XLW), with the canvas defaults.
- **Defaults ruled with it:** Esc pressed three times stays a stop path; Pause is Ctrl+Alt+Space; on resume nth sends "continue" to the chat by default (a setting can turn it off); Esc inside the pill's card only collapses the card.
- **Effect:** Pause and Resume, the stop paths of DESIGN.md 10.2, the draggable pill with its three anchors, and answering Tactus's questions and cards from the pill are built to section 10 after units 1 to 7. This supersedes the earlier note that Pause and Resume were not part of Tactus (ED-NEW-pcctl-6 item 3, ED-NEW-pcctl-7 item 8).
- **Engineering record:** ED-NEW-pcctl-12 onwards in `porting/engineering_decisions.md` (DESIGN.md 10.14 names the P21 entry ED-NEW-pcctl-8; that number was already used by the engine unit, ED-NEW-pcctl-9 and ED-NEW-pcctl-10 by the capture and MCP units built before the pill, and ED-NEW-pcctl-11 by the Cuspis mark (OD-0146), so P21's entry is ED-NEW-pcctl-12).

## OD-0146 Tactus's mark is Cuspis (2026-10-11)

- **Owner, through the lead (2026-10-11):** the Tactus icon is Cuspis, owner-locked: `E:\Droppy\work\design\tactus-icon\pointer-1-final.svg` with its 16 px forms `pointer-1-final-16.svg`, `pointer-1-final-16-working.svg` and `pointer-1-final-16-paused.svg`, and the motion shown on https://claude.ai/artifact/WW257FhBN3ybFmmG9ccRiD. The flame earlier drawn in the pill is wrong.
- **Effect:** Cuspis is drawn by one swappable mark component wherever Tactus shows a mark: the pill, the fullscreen dot, the tray, notifications, the Settings row and the composer chip. Neither the Ignis flame nor the nth mark stands in for it anywhere; changing the mark later is a change to that one component.
