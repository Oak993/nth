# Unknowns, blockers and owner decisions

Status values: OPEN, INVESTIGATING, RESOLVED, BLOCKED_ON_OWNER, BLOCKED_ON_ACCESS.
Nothing here may be silently implemented. A requirement that depends on an OPEN entry
carries that dependency in `porting/parity_matrix.json`.

---

## UNK-0001 Upstream bundle metadata contradicts the repository license

- **Status:** RESOLVED as a recorded upstream inconsistency. Carried as-is.
- **Evidence:** `project.yml` sets
  `INFOPLIST_KEY_NSHumanReadableCopyright: "Droppy Code, the coding app by Droppy. MIT License."`
  while `LICENSE` at the same commit is AGPLv3 with section 7 additional terms, and `TRADEMARK.md`
  sets separate mark rules.
- **Handling:** contract section 8 forbids silently repairing unrelated upstream behavior during a
  parity port. The string is a shipped bundle value, so changing it would be a product change.
  Recorded, reproduced, and surfaced. Not a security or data-loss issue, so no decision is required
  to proceed.

## UNK-0002 Product branding for the Windows artifact

- **Status:** BLOCKED_ON_OWNER. Does not block reading, specification, or implementation.
  Blocks the final packaging identity and Gate E artifact naming only.
- **Why it cannot be resolved from source:** the pinned source names the product "Droppy Code",
  bundle id `iordv.droppycode`, and ships `TRADEMARK.md` with separate rules for names and marks;
  `LICENSE` section 7 adds attribution and origin terms. Whether this Windows build may ship under
  the Droppy Code name and marks is a rights and authorization question, not a source question.
- **Conflicting instruction on record:** the separate Marrow project states "Never present the app
  as Droppy Code and never use Droppy names or icons." That instruction governs Marrow, a different
  product; it is not this port's instruction, but it shows the owner has previously required a
  different answer. See `porting/instruction_conflicts.md` C-06.
- **What the owner needs to decide:** ship as Droppy Code with the original marks, or ship under a
  different product name and icon while keeping the AGPLv3 section 7 attribution intact.
- **Interim handling:** the port keeps the pinned source's own product strings and marks internally
  so that parity is measurable. No shipping artifact is produced under any name without an explicit
  instruction.

## UNK-0003 macOS reference runtime for visual parity

- **Status:** BLOCKED_ON_ACCESS.
- **Why:** contract section 10 requires reference captures from the pinned macOS build with
  deterministic fixture data, and contract section 6 makes a matching-build runtime an authority
  alongside source. The pinned commit targets macOS 26, Swift 6, arm64, and requires an Apple
  toolchain and a `DEVELOPMENT_TEAM`. This session runs on Windows 11 x64 with no macOS machine,
  no Xcode, and no Apple hardware reachable.
- **Consequence, stated plainly:** where appearance is delegated to macOS rather than specified in
  source, the correct result cannot be observed from here. Those properties stay **UNVERIFIED**.
  Contract section 10: "Without matching macOS reference captures, source-based UI implementation
  may proceed, but visual parity remains UNVERIFIED. Never manufacture reference screenshots or
  call it pixel-perfect from source inspection alone."
- **What is still possible without it:** every source-defined dimension, spacing, radius, color
  token, font size, weight, animation curve and duration is extractable from the Swift source and
  is therefore a SOURCE FACT. Only macOS-delegated rendering (system materials, vibrancy, system
  control metrics, glyph rasterization, default fonts) is unobservable.
- **Bounded resolution paths, in order of preference:** (1) the owner runs the pinned commit on
  their macOS machine and supplies captures with the environment record contract section 10 lists;
  (2) the owner supplies an existing build plus its exact commit, in which case it is an
  approximate oracle only and the mismatch is recorded; (3) no reference, and the affected
  requirements ship as UNVERIFIED with the exact list named in the final report.

## UNK-0004 Liquid Glass has no Windows equivalent and needs an accepted deviation

- **Status:** BLOCKED_ON_OWNER for acceptance of the deviation. Does not block reading, spec, or
  the stack evaluation; it is a primary **input** to the stack evaluation.
- **Evidence:** see `porting/platform_adaptations.md` PA-0001. The app renders every surface with
  macOS 26 Liquid Glass (19 `.glassEffect` sites, 3 `GlassEffectContainer` sites) and contains no
  `NSVisualEffectView` or SwiftUI `Material` fallback anywhere, so there is no alternative path in
  the source to follow.
- **Why source cannot resolve it:** the source specifies the shape, radius, tint and interactivity
  of each glass surface, but the material itself is rendered by macOS. Its appearance is not a
  value in this repository.
- **What the owner needs to accept:** that the Windows build cannot reproduce Liquid Glass exactly,
  and which approximation is acceptable. This is a visible compromise, so contract section 10
  requires explicit acceptance and requires it to stay labeled a deviation rather than parity.
- **Interim handling:** the port reproduces every source-defined property around the material
  exactly (shape, corner radius, tint color and opacity, interactive flag, container spacing) and
  treats the material rendering as a named, tracked deviation. No screenshot comparison will be
  presented as parity for these surfaces.

## UNK-0005 What shell, if any, the Windows build runs model-issued commands in

- **Status:** OPEN, INVESTIGATING. Blocks PA-0009 and PA-0011 decisions and any requirement that
  depends on running a command the model produced.
- **What source establishes (SOURCE FACT):** the macOS build captures the user login shell
  environment once at launch and augments PATH with Unix package-manager directories
  (`Core/Support/Shell.swift:325-457`); it classifies model-issued command text with a Unix-tool
  reader list to decide whether to snapshot the working tree (`:141-216`); the terminal panel and
  project scripts run through the same `Shell` layer.
- **What source does not establish:** anything about Windows. There is no Windows branch anywhere in
  the pinned tree.
- **Why it cannot be answered by analogy:** the command text comes from the **model**, not from the
  app. Whether a model writing `ls -la && grep foo` is executed by a POSIX shell, by PowerShell, or
  by the command interpreter changes what actually runs, what the classifier should recognize, and
  whether the app quotes arguments correctly. Picking one by preference would be an invented default.
- **Bounded resolution path:** determine, per supported provider, what the provider CLI itself does
  on Windows when it runs a shell command, because the app's job is to host that CLI rather than to
  invent a shell policy. Then decide the app's own script and terminal behavior to match the source
  intent. Record the result as an ENGINEERING DECISION with the probe evidence behind it.

## UNK-0006 Distinguishing the `cmd` provider from the Windows command interpreter

- **Status:** OPEN. Named explicitly by contract section 11.
- **What source establishes:** the pinned tree contains a Command Code provider
  (`Services/Providers/CommandCodeSession.swift`, `CommandCodeAPI.swift`, `CommandCodeKeychain.swift`,
  a `provider-commandcode` icon, and a tracked `.commandcode/` directory at the repository root).
  The exact executable name it resolves is to be confirmed when those files are read in full.
- **The hazard:** `Core/Support/Shell.swift:411-423` resolves a bare name by walking PATH and taking
  the first executable match. On Windows, resolving a bare name of `cmd` finds
  `C:\Windows\System32\cmd.exe`, the command interpreter, long before any provider CLI. Launching it
  as if it were the provider would hang on a console with no protocol on stdout.
- **Required handling per contract section 11:** establish how to distinguish the provider from the
  interpreter and **validate the actual executable identity**, not merely the name. Candidate
  evidence for identity: the resolved path, the file version information, and a protocol handshake
  that the interpreter cannot answer. To be decided after the provider files are read, and verified
  by a probe on Windows.

## UNK-0007 Apple-licensed iconography and typography cannot ship on Windows

- **Status:** BLOCKED_ON_OWNER for the substitution decision. Does not block reading or
  specification. Blocks any claim of visual parity for icons and text metrics.
- **Evidence:** `porting/platform_adaptations.md` PA-0012 and PA-0013;
  `porting/evidence/sf_symbols_used.txt`.
- **The facts:** the app uses 70 distinct SF Symbols across 206 sites, and resolves body and control
  typography to the macOS system font at 430-plus sites. Both are Apple assets licensed for Apple
  platforms and are not redistributable inside a Windows application.
- **Why source cannot resolve it:** the source names the symbols and the semantic font roles. It does
  not contain the artwork or the font, and it contains no alternative set, because it never needed
  one.
- **Why this is not a small matter:** contract section 10 forbids silently substituting assets, and
  font metrics propagate into layout. A replacement face with different advance widths moves line
  breaks and truncation points across every screen, so the substitution decision has to be made
  before the UI contract can record expected text wrapping.
- **What the owner needs to decide:** (a) which icon set replaces SF Symbols, mapped symbol by symbol
  so nothing is swapped silently, and (b) which type family the Windows build uses. Windows ships
  Segoe UI Variable and Cascadia Mono, which are redistributable on Windows, but that is a candidate,
  not a source fact, and picking one without instruction would be an invented default.
- **Interim handling:** the port records every symbol site and every font role in the UI contract with
  its exact source value, so that whichever replacement is chosen is applied uniformly and remains
  visible as a named deviation rather than being absorbed into a parity claim.
- **Not affected:** the app's own first-party artwork (25 Hydra head marks, the Hydra mark, 13
  provider icons, 27 MCP vendor icons, the app icon assets) ships in the repository and is reusable,
  subject only to the trademark question in UNK-0002.

## UNK-0009 No Windows release asset exists on the update feed

- **Status:** BLOCKED_ON_OWNER / BLOCKED_ON_ACCESS. Blocks live end-to-end update verification only.
- **Evidence:** `porting/platform_adaptations.md` PA-0014. `App/UpdateChecker.swift` selects the newest
  stable release that ships a disk image, from `https://gitlab.com/api/v4/projects/86415974/releases`.
  Published releases carry macOS disk images.
- **Consequence:** a Windows build reading the same feed finds no installable Windows artifact, so the
  download-verify-swap-relaunch path cannot be exercised against the real feed.
- **Handling:** the updater is implemented in full and verified against a controlled local fixture that
  reproduces the same API response shape. The live path is reported as BLOCKED, never as passing.
  Contract section 17: missing endpoints, certificates or publishing access remain explicit blockers.
- **Resolution path (owner):** publish a Windows asset to that GitLab project, or supply a separate
  feed location for Windows releases. Neither is invented here.

## UNK-0008 App icon artwork and the in-repo Droppy marks under the Marrow name

- **Status:** BLOCKED_ON_OWNER. Raised by OD-0002 (the build ships as Marrow). Blocks packaging
  identity and the icon asset set; blocks nothing upstream of that.
- **What the source ships:** `DroppyCode/Resources/AppIcon.icon/` (an Icon Composer document with
  `icon.json` plus layered assets: an SVG, `mascot (3).png`, `mascot dark.png`, `shadow dark.png`,
  `shadow def.png`), the `droppy-logo` imageset at 1x/2x, `docs/icon.png`, `docs/droppy-logo.png`,
  and in-app usage of the `drop` and `drop.fill` symbols.
- **The question:** under the Marrow name, is the Droppy mascot/drop artwork replaced, removed, or
  retained. `TRADEMARK.md` sets separate rules for names and marks, and LICENSE section 7(c) covers
  names and marks, so retaining Droppy marks under a different product name is a rights question the
  source cannot answer.
- **Interim handling:** the port inventories every icon site and keeps the source assets in place so
  parity stays measurable, and produces no shipping artifact identity until the owner decides.
- **Note:** AGPLv3 section 7 attribution to Droppy Code stays intact regardless of the icon decision;
  that is an attribution obligation, not a mark usage.

## UNK-0010 ESCALATION: deleting a custom MCP server destroys Keychain secrets with no confirmation

- **Status:** BLOCKED_ON_OWNER. Raised under contract section 8, which requires escalation rather
  than silent reproduction when faithfully copying a behavior would destroy data.
- **The behavior, verified directly in source:**
  - `App/Views/Settings/MCPSettingsPage.swift:564-566`: the custom-server ellipsis menu's
    `PopoverItem("Delete", symbol: "trash", isDestructive: true)` calls
    `model.mcp.deleteCustom(server)` immediately on click. There is no confirmation step.
  - `deleteCustom` also runs `disconnect()`, which removes the server's stored secrets from the
    Keychain (`Services/MCP/MCPStore.swift:124`, `:193-199`).
  - The user setting `confirmBeforeDeleting` exists, defaults to **true**
    (`App/AppSettings.swift:123`, `:209-210`), is exposed in Settings
    (`SettingsView.swift:361`), and is honored by **six** other destructive paths:
    `SettingsView.swift:851`, `:892`; `SidebarView.swift:192`, `:360`, `:891`.
  - This one path ignores it.
- **Why this is escalated rather than reproduced or fixed:**
  - Reproducing it means a Windows user who has explicitly asked to confirm before deleting can
    destroy a server configuration **and its credentials** with one unconfirmed click. Credentials
    are not recoverable from the app; they must be re-obtained from the provider.
  - Fixing it unilaterally would be a silent product change, which contract section 2 forbids and
    contract section 8 specifically warns against for a parity port.
  - So it is neither reproduced nor fixed until the owner decides.
- **What the owner needs to decide:** reproduce the upstream behavior exactly (one-click,
  unconfirmed, secrets deleted), or treat it as a defect and have the Windows build honor
  `confirmBeforeDeleting` here as the other six paths do. The second option is a recorded,
  owner-approved deviation, not parity.
- **Interim handling:** implementation of this control is deferred. No other MCP work is blocked.
- **Note:** this is recorded as upstream defect 68 in `porting/upstream_defects.md`. That register
  says "Reproduce, do not fix"; this entry supersedes it pending the owner's decision.

## UNK-0011 Thirteen findings the repair pass could not settle from source alone

- **Status:** OPEN. None blocks the specification; each names the exact missing evidence.
- **Source:** the repair pass returned 13 `still_unresolved` items across the nine families, held
  in `porting/critiques/repair_summary.json`. They fall into four kinds:
  1. **Swift or AppKit runtime semantics not observable from the checkout** - whether a synthesized
     `Decodable` throws on a missing key with a defaulted property (`MCPCustom.swift:15-19`);
     whether `NSPopover.behavior = .semitransient` can dismiss without the coordinator's local mouse
     monitor running first; whether an empty `VStack` contributes one or two `Chrome.sectionSpacing`
     units on the About page. Each needs a run, not a read.
  2. **Network.framework and FSEvents precedence questions** - whether an unset
     `requiredInterfaceType` with a set `requiredLocalEndpoint` can bind a non-loopback interface
     (`MCPOAuth.swift:408-409`); the exact FSEvents-to-Windows mapping for the three event-loss
     flags in `WorkingTreeWatch.swift:180`.
  3. **Build and deployment facts not in the repository** - whether Xcode's Icon Composer
     compilation emits the `AppIcon.icns` that `scripts/package_dmg.sh:46-47` requires; whether
     `scripts/website_captures.py` completes inside its 330 s budget against a 600 s expectation;
     which version the deployed website actually serves (`index.html` says 1.5.3 while
     `changelog.json` says 1.6.0, generated 2026-09-18T17:57:43Z); whether all 26 per-theme stills
     the picker requests exist on the CDN.
  4. **Unstated policy** - the minimum git version the app supports, which is needed to settle
     whether `git diff --numstat -z` always emits repository-root-relative paths. Stated nowhere in
     the checkout.
- **Handling:** items in 1 and 2 become Windows verification items with an explicit expected
  behavior recorded as the specification; items in 3 do not affect the port's product behavior and
  are recorded only; item 4 is treated as repository-root-relative by specification and verified on
  Windows against the real git.
