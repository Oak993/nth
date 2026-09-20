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
