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
