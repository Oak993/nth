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
