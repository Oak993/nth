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

- **2026-09-22:** RESOLVED by OD-0002 (the build ships as Marrow) and OD-0008 C7 (install and data identity "Marrow Code").

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

- **2026-09-22:** RESOLVED by OD-0005 (Acrylic over the desktop, SKSL shader over app content), a named deviation; OD-0014 keeps the clear default.

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

- **2026-09-22:** RESOLVED by OD-0008 C5: PowerShell 7 when installed, otherwise Windows PowerShell.

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

- **Status:** RESOLVED 2026-09-20 by PA-0016, with version-resource evidence measured on this
  machine. Kept here for the trail; the decision and its justification live in
  `porting/platform_adaptations.md` PA-0016.
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

- **2026-09-22:** RESOLVED by OD-0009 (text face: Inter if it measures closer than Segoe UI Variable; mono Cascadia Mono) and OD-0010 (icons: Lucide).

- **2026-09-23 correction:** Cascadia Mono is not an inbox font: it resolves by name only while Windows Terminal is installed (probe P9 RESULT.md section 2). The port ships the unmodified static Cascadia Mono files of Microsoft's official release inside the app (ED-107), once the owner confirms the download.

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

- **2026-09-22:** RESOLVED by OD-0008 C6: "Check failed" until the owner has a Windows feed; named deviation.

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
- **2026-10-04 (UE-1; OD-0062, OD-0100; Windows review, unit/integ-ue1):** the asset and the feed are decided: the Windows
  release asset is Velopack's full package (`MarrowCode-<version>-full.nupkg`) on the owner's public GitHub repository
  Oak993/marrow-releases, read by Velopack's GithubSource. Check, download with percent, the package check and the hand-off
  to Update.exe are implemented and verified on a loopback fake GitHub (`VelopackLoopbackFeedTests`). STILL OPEN: the
  repository holds no release yet, so the live path (a real check, download, Update.exe swap and restart of an installed
  Marrow) is unverified until the owner publishes one; it stays reported as BLOCKED, never as passing.

## UNK-0008 App icon artwork and the in-repo Droppy marks under the Marrow name

- **2026-09-22:** RESOLVED by OD-0012 F1: every remaining product-name occurrence becomes Marrow, except droppy-code.json and DROPPY_CODE_PROJECT_ROOT.

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

- **2026-09-22:** RESOLVED by OD-0008 (see the matching BLK entry in porting/blocked_requirements.json).

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

### Owner ruling on this entry, 2026-09-20 (verbatim)

> Keep UNK-0010 explicitly unresolved and isolated pending my decision. Do not silently change the
> MCP deletion behavior, and do not treat either alternative as approved. This issue must not block
> unrelated specification or implementation work.

**Effect.** The entry stays OPEN and is now isolated mechanically rather than by note:

- Quarantined as `BLK-0001` in `porting/blocked_requirements.json`, with the two quarantined code
  paths named (the delete affordance, and the Keychain teardown that delete triggers).
- A Gate C check in `porting/tools/verify.mjs` **fails** while `BLK-0001` is OPEN and a matching
  implementation path exists. Neither alternative can satisfy the gate, so neither can be adopted
  by accident or by drift.
- Scope is exactly those two paths. Every other MCP requirement proceeds: add, edit, connect,
  disconnect, OAuth, probe, proxy, catalog, and the `confirmBeforeDeleting` setting itself along
  with the six paths that already honor it.
- Recorded as amendment AM-0001 Effect A in `porting/contract_amendments.md`.

**Not approved:** neither reproducing upstream exactly nor honoring `confirmBeforeDeleting` here.
Both remain open options for the owner alone.

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
     2026-09-23: cross-root ordering is settled by ED-113 (a watch whose git directory lies outside the root holds no
     stream); the loss-flag mapping is settled by probe P8 and ED-114 (Error with InternalBufferOverflowException,
     and every other Error, means everything). See PA-0017.
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

## UNK-0012 ESCALATION: Malformed custom-MCP variable silently discards every custom server, then the next save overwrites the file

- **2026-09-22:** RESOLVED by OD-0008 (see the matching BLK entry in porting/blocked_requirements.json).

- **Status:** BLOCKED_ON_OWNER. Raised 2026-09-20 by the defect re-triage required by
  amendment AM-0001. Quarantined as BLK-0002 in `porting/blocked_requirements.json`.
- **Consequence criteria met:** C4.
- **Source evidence:** DroppyCode/Core/Models/MCPCustom.swift:15-19, :36-48; DroppyCode/Services/MCP/MCPStore.swift:245-248
- **Upstream defect entry:** 17 in `porting/upstream_defects.md`.
- **Concrete harm:** A user with a hand-edited or partially corrupted mcp-customs.json - the exact case the file's own doc comment says is tolerated - opens the app and finds Settings > MCP > Custom empty, with no error anywhere. If they then add a replacement server, saveCustoms atomically overwrites the file with the single new entry and every previous custom server definition (name, command, args, url, variable keys) is permanently gone. Keychain secrets are NOT destroyed on this path, since deleteCustom is never called, so they survive as orphans - this is lost configuration, retypeable, not lost credentials and not lost project work. Probability is low: isSecret has existed since the file's first commit (75e30f217, 2026-09-18), so no shipped build ever wrote a Variable without it, and the trigger is an out-of-band edit or file corruption.
  - C4 - silent data loss the user cannot detect or undo: one malformed nested object discards the entire custom-server list with no message (the try? at MCPStore.swift:247), and the first subsequent edit atomically overwrites mcp-customs.json with the empty list (MCPStore.swift:275). No backup, no undo, and nothing tells the user their file failed to parse.
- **Neither option is approved:**
  - Reproduce upstream exactly, including the data-loss path. Exact parity, including the defect.
  - Harden the path so the loss cannot occur. This is a recorded owner-approved deviation, NOT parity.
- **Quarantined paths:** any Windows implementation of custom MCP server decoding that swallows a decode failure; any save path that overwrites the custom-server store after a failed load
- **Isolation:** a Gate C check fails while this entry is OPEN and a matching implementation
  path exists, so neither alternative can be adopted by drift. Nothing else in the subsystem
  is blocked.

## UNK-0013 ESCALATION: Cookie-timeout tearDown can truncate a checkpoint silently; a later revert then deletes unrecoverable work

- **2026-09-22:** RESOLVED by OD-0008 (see the matching BLK entry in porting/blocked_requirements.json).

- **Status:** BLOCKED_ON_OWNER. Raised 2026-09-20 by the defect re-triage required by
  amendment AM-0001. Quarantined as BLK-0003 in `porting/blocked_requirements.json`.
- **Consequence criteria met:** C1, C4.
- **Source evidence:** DroppyCode/Services/Git/WorkingTreeWatch.swift:271 and :159-170, against Git.swift:445-462
- **Upstream defect entry:** 81 in `porting/upstream_defects.md`.
- **Concrete harm:** Usually benign: the turn simply has no checkpoint and the user later sees "No file checkpoint is available for this message." The bad outcome, which the source itself names, is a checkpoint that silently records only a few directories as though it were the whole tree. A later revert against that base then treats every other touched file as new (Git.swift:613) and DELETES it with FileManager.removeItem (Git.swift:619) instead of restoring its pre-turn content; for an uncommitted file that content is gone, and the app reports the revert as successful.
  - C1: a revert against a silently truncated checkpoint deletes files at Git.swift:619 whose pre-turn, uncommitted content exists nowhere else, so the user's work is permanently lost.
  - C4: the truncated capture exits 0 and is cached and committed as an ordinary checkpoint, so neither app nor user can tell the snapshot is wrong until a revert acts on it.
- **Neither option is approved:**
  - Reproduce upstream exactly, including the data-loss path. Exact parity, including the defect.
  - Harden the path so the loss cannot occur. This is a recorded owner-approved deviation, NOT parity.
- **Quarantined paths:** any Windows implementation of the working-tree watch scratch-index teardown; any checkpoint capture that can exit successfully on a partial tree
- **Isolation:** a Gate C check fails while this entry is OPEN and a matching implementation
  path exists, so neither alternative can be adopted by drift. Nothing else in the subsystem
  is blocked.

## UNK-0014 ESCALATION: previewRestore mixes CWD-relative and repo-root-relative paths; a revert can delete the wrong files and report success

- **2026-09-22:** RESOLVED by OD-0008 (see the matching BLK entry in porting/blocked_requirements.json).

- **Status:** BLOCKED_ON_OWNER. Raised 2026-09-20 by the defect re-triage required by
  amendment AM-0001. Quarantined as BLK-0004 in `porting/blocked_requirements.json`.
- **Consequence criteria met:** C1, C4.
- **Source evidence:** DroppyCode/Services/Git/Git.swift:586-590 (with :603-619 and :43)
- **Upstream defect entry:** 84 in `porting/upstream_defects.md`.
- **Concrete harm:** A user with diff.relative = true who has added a project that is a subfolder of a larger repository asks to undo a turn's file changes. The files the agent actually changed are not restored at all; the app acts instead on same-named paths at the repository root, DELETING any the base checkpoint does not hold (Git.swift:619) and overwriting any it does (Git.swift:607-611). The revert sheet shows the mis-derived names, which read like plausible file names, and the revert reports success.
  - C1: files at the repository root that the turn never touched are deleted outright at Git.swift:619, and uncommitted content in them is not recoverable from the repository.
  - C4: preview, restore and the app's own report all look normal, so the user gets no signal that the wrong files were acted on and the intended ones left alone.
- **Neither option is approved:**
  - Reproduce upstream exactly, including the data-loss path. Exact parity, including the defect.
  - Harden the path so the loss cannot occur. This is a recorded owner-approved deviation, NOT parity.
- **Quarantined paths:** any Windows implementation of previewRestore path derivation; any revert path that deletes files not present in the base checkpoint
- **Isolation:** a Gate C check fails while this entry is OPEN and a matching implementation
  path exists, so neither alternative can be adopted by drift. Nothing else in the subsystem
  is blocked.


## UNK-0015 ESCALATION: Legacy migration hardcodes "Droppy Code" while the store reads AppInfo.name; the approved rename makes them diverge

- **2026-09-22:** RESOLVED by OD-0008 (see the matching BLK entry in porting/blocked_requirements.json).

- **Status:** BLOCKED_ON_OWNER. Raised 2026-09-20 during Gate B specification. Quarantined as
  BLK-0005.
- **This is not a parity question.** Upstream the literal and AppInfo.name agree, because
  CFBundleDisplayName is "Droppy Code", so the inconsistency is latent and harmless there. The
  hazard is created by OD-0002, the approved rename to Marrow.
- **Consequence criteria met:** C1, C4.
- **Source evidence:**
  - DroppyCode/App/LegacyMigration.swift:21 - destination is the string literal "Droppy Code"
  - DroppyCode/App/LegacyMigration.swift:7 - source folder is the literal "Cody"
  - DroppyCode/App/LegacyMigration.swift:22-24 - moves only when Cody exists and the destination does not
  - DroppyCode/Services/Store/Storage.swift:20 - root is base.appendingPathComponent(AppInfo.name)
  - DroppyCode/Core/Support/Text.swift:4 - AppInfo.name reads CFBundleDisplayName, defaulting to "Droppy Code"
  - DroppyCode/App/LegacyMigration.swift:10 - skipped entirely when AppInfo.isDevelopment
- **Concrete harm:** Upstream the literal and AppInfo.name agree, because CFBundleDisplayName is "Droppy Code", so the inconsistency is latent and harmless. Under OD-0002 the product ships as Marrow, AppInfo.name becomes "Marrow", and the literal does not follow. A user migrating from the earlier name would have their entire library, thread histories and attachments moved into a folder the store never reads, and the app would start empty. This is a data-loss path CREATED BY the rename decision, not present upstream, so it is not a parity question.
- **Likely resolution, which must still be evidenced not assumed:** Likely resolves to NOT_APPLICABLE_WITH_EVIDENCE: Droppy Code has never shipped on Windows, so no "Cody" Application Support folder can exist there and the migration has nothing to migrate. That classification must be an evidenced decision, not a convenience, and it interacts with whether the owner wants the Windows build to migrate from the separate existing Marrow installation, which is a different product.
- **Three options, none approved:**
  - Port the migration literally, keeping the "Droppy Code" destination literal. Exact parity, and under the rename it targets a folder the store never reads.
  - Port it with the destination following AppInfo.name so the rename is applied consistently. A recorded owner-approved deviation, NOT parity.
  - Classify NOT_APPLICABLE_WITH_EVIDENCE because no prior Windows install exists. Requires the owner to confirm no migration from the separate Marrow product is wanted.

## UNK-0023 Whether the app should always draw its own backdrop

- **2026-09-22:** RESOLVED by OD-0014: keep upstream's clear window (parity).

- **Status:** OPEN. Follows from OD-0005. Does not block the stack choice or implementation of
  anything else; it decides how often the richer material is visible.
- **The facts:** `UI/Chrome/WindowChrome.swift:116-117` sets `isOpaque = false` and
  `backgroundColor = .clear`; `UI/Theme/Wallpaper.swift` makes the app wallpaper optional and it
  is unset by default. So upstream, out of the box, the glass sits over the desktop.
- **Why it matters now:** under OD-0005 the desktop-backed case gets an Acrylic-class material
  with no lensing. If the app always drew a backdrop, the full source-defined material would
  apply everywhere, at the cost of changing an upstream default, which contract section 2 forbids
  doing silently.
- **Options, neither assumed:** keep the upstream default (clear window, Acrylic over the desktop,
  richer material only once the user sets a wallpaper), or ship a default backdrop so the richer
  material is the normal case and record that as a second named deviation.

## UNK-0024 The port and the owner's existing Marrow app would share one data folder

- **2026-09-22:** RESOLVED by OD-0008 C7: the port installs and stores data as "Marrow Code" (%LOCALAPPDATA%\Marrow Code).

- **Status:** OPEN for the shipping default. **Mitigated immediately for development** by the isolation
  rule below, so it blocks nothing today.
- **The collision, from two independent records:**
  - Engineering decision ED (STORE Q171): the port's store root is `%LOCALAPPDATA%\<display name>`.
    Under OD-0002 the display name is `Marrow`, so the root is `%LOCALAPPDATA%\Marrow`.
  - The owner's EXISTING Marrow app (the separate project at
    `C:\Users\omera\Downloads\Claude outputs\agent-workbench`) already lives in
    `%LOCALAPPDATA%\Marrow`; its own CLAUDE.md records that it took over `Agent Workbench` and
    became `Marrow`.
- **Why it matters now, not at release:** the owner intends to RUN the existing Marrow app to build
  this port. The first time a worker launches the port to test it, the port would read and write
  `library.json`, the thread histories and the settings of the very app driving the work. Contract
  section 3 forbids touching the owner's working application or its data; section 2 forbids changing
  user data. This is data loss in the owner's own tool.
- **Development rule, binding on every worker and every test (ENGINEERING DECISION):** the port reads
  its store root from the environment variable `MARROW_PORT_DATA_DIR` and, when it is set, uses that
  folder instead of `%LOCALAPPDATA%\Marrow`. Every test, probe and manual run sets it to a disposable
  folder. The name is deliberately DIFFERENT from the existing app's `MARROW_DATA_DIR`, so neither app
  can pick up the other's override by accident. A Gate C check fails if the port can resolve its
  store root to `%LOCALAPPDATA%\Marrow` while running under test.
- **Still the owner's to decide:** the shipping data folder once both apps exist on one machine.
  Options: the port takes `%LOCALAPPDATA%\Marrow` and the old app is retired first; or the port ships
  under a distinct folder until then, which is a visible deviation from the source's
  `AppInfo.name`-derived root. The same collision applies to the installer identity, the Start menu
  entry and any single-instance lock, which are packaging decisions for M8.

## UNK-0016 Quarantine question behind BLK-0006

- **Status:** RESOLVED 2026-09-22 by OD-0008. Raised 2026-09-20 with the quarantine entry BLK-0006 in porting/blocked_requirements.json, which holds the evidence, the harm and the ruling; recorded here so every unknown a record names has an entry.

## UNK-0017 Quarantine question behind BLK-0007

- **Status:** RESOLVED 2026-09-22 by OD-0008. Raised 2026-09-20 with the quarantine entry BLK-0007 in porting/blocked_requirements.json, which holds the evidence, the harm and the ruling; recorded here so every unknown a record names has an entry.

## UNK-0018 Quarantine question behind BLK-0008

- **Status:** RESOLVED 2026-09-22 by OD-0008. Raised 2026-09-20 with the quarantine entry BLK-0008 in porting/blocked_requirements.json, which holds the evidence, the harm and the ruling; recorded here so every unknown a record names has an entry.

## UNK-0019 Quarantine question behind BLK-0009

- **Status:** RESOLVED 2026-09-22 by OD-0008. Raised 2026-09-20 with the quarantine entry BLK-0009 in porting/blocked_requirements.json, which holds the evidence, the harm and the ruling; recorded here so every unknown a record names has an entry.

## UNK-0020 Quarantine question behind BLK-0010

- **Status:** RESOLVED 2026-09-22 by OD-0008. Raised 2026-09-20 with the quarantine entry BLK-0010 in porting/blocked_requirements.json, which holds the evidence, the harm and the ruling; recorded here so every unknown a record names has an entry.

## UNK-0021 Quarantine question behind BLK-0011

- **Status:** RESOLVED 2026-09-22 by OD-0008. Raised 2026-09-20 with the quarantine entry BLK-0011 in porting/blocked_requirements.json, which holds the evidence, the harm and the ruling; recorded here so every unknown a record names has an entry.

## UNK-0022 Quarantine question behind BLK-0012

- **Status:** RESOLVED 2026-09-22 by OD-0008. Raised 2026-09-20 with the quarantine entry BLK-0012 in porting/blocked_requirements.json, which holds the evidence, the harm and the ruling; recorded here so every unknown a record names has an entry.

## UNK-0025 Upstream has moved past the pinned baseline (B2, 1.6.2) to 1.7.0

- **Status:** RESOLVED 2026-09-26 by OD-0024 (move to 1.7.0, AM-0003). Raised 2026-09-26 by Claude.
- **Evidence (SOURCE FACT, checked against `https://gitlab.com/droppyformac1/droppy-code.git`, branch `main`, at
  `52bc79260...`):** 16 commits, 101 files, +2188/-2577 lines past our pin `1fbf81e61e28a079508a074c01ee1830a9cc23dc`.
  `project.yml` MARKETING_VERSION is now `1.7.0` (ours: 1.6.2). Notable commits: `249fc5a9e` "1.7.0: Opus 5.5 and
  GPT-5.6/6 models, self-updating CLIs, Hydra merges that never wait" (a provider/model roster and Hydra-merge behavior
  change - would touch our already-ported ProviderRegistry/Hydra units); `d70cff8fe` "refactor: audit cleanups - five bug
  fixes, shared MCP and provider code, layer rules restored" (behavior fixes plus a restructuring of code we already
  ported); `280e05d8b` a new app icon; `02da39027`, `ff5032673`, `b89a1da92`, `daef6725a`, `0666ecd47` - five more fixes
  (layout freeze, markdown local-file links, Codex phantom root head, Codex spend labeling).
- **Why this matters:** re-pinning mid-port means re-reading the delta's files (Gate A/B rework, like the 1.6.0->1.6.2
  refresh CLAUDE.md records), and some fixes may be worth porting as corrections even without a full re-pin. Neither is
  a call this session can make alone; OD-0006/AM-0002 froze the pin deliberately.
- **Options for the owner:** (a) stay on B2/1.6.2 for this port and treat 1.7.0 as a future delta, same as 1.6.0->1.6.2;
  (b) re-pin now to 52bc79260/1.7.0, which reopens Gate A/B for the 101-file delta before more units are built on the old
  pin; (c) stay pinned but cherry-pick specific named fixes from the delta as engineering decisions, each with its own
  Swift-diff evidence. No action taken until the owner rules; work below continues against the B2 pin.

## UNK-0026 CLI self-update on Windows: whether `claude update` and `codex update` can run headless

- **Status:** RESOLVED 2026-09-26 by OD-0025 (owner: "Test once, then match Mac (Recommended)").
  Owner question asked 2026-09-26 (B3 decision O1, `E:/Droppy/work/b3/decisions_draft.md`;
  full source citations and options in `porting/platform_adaptations.md` PA-0018, the wording decision in
  `porting/engineering_decisions.md` ED-193).
- **What is new at B3, in one line:** `Services/Providers/ProviderRegistry.swift:467-537` adds a background
  updater that runs `claude update`/`codex update` every 12 hours and after a "too old" CLI error, none of which
  exists at B2.
- **Why it is the owner's call, not engineering's (CLAUDE.md: do not modify the owner's installed apps without
  authorization; contract sections 2, 8, 11, 12):** finding out whether either updater completes headless, and
  whether it succeeds while a live session holds that CLI open, requires actually running the real updaters on
  the owner's workstation, which changes the owner's installed Claude Code and Codex versions.
- **Options on record (PA-0018):** (1) probe once, authorized, then ship parity exactly as B3 does
  (recommended); (2) ship parity without a probe; (3) never run the updaters on Windows (named deviation); (4)
  update only when no session of that provider is live (named deviation).
- **Blocks:** unit A23 (Services.Providers.ProviderRegistry), A32 (App.AppModel), A33 (App.Runtime.ThreadRuntime).
- **Trial outcome (SOURCE FACT, 2026-09-26 22:29:36-22:29:53 +03:00; evidence `E:/Droppy/work/b3/evidence_od0025/`):** codex-cli updated 0.154.0 to 0.157.1 headless while a codex process was running (its updater spawns PowerShell); Claude Code reported already up to date (2.1.283, unchanged). Owner watching, verbatim: "On the question, nothing appeared" (no window, prompt or UAC). Exit codes were not captured (a PowerShell Start-Process -PassThru redirection quirk); stdout and the version change stand for success. Resolved per OD-0025: lanes A23 (b3/L3g), A33 (b3/L4a) and A32 (b3/L4b) merged on this evidence (B3 merge batch 6).

## UNK-0027 Chat links on Windows: drive-letter paths, UNC paths, and what a link may launch

- **Status:** RESOLVED 2026-09-26 by OD-0026 (owner: "Safe parity (Recommended)"; rule and mechanism ED-204).
  Owner question asked 2026-09-26 (B3 decision O4, `E:/Droppy/work/b3/decisions_draft.md`;
  full source citations and options in `porting/platform_adaptations.md` PA-0020; the parity-independent opener
  hardening is already recorded as ENGINEERING in `porting/engineering_decisions.md` ED-203).
- **What is new at B3, in one line:** `UI/Markdown/LinkParagraphView.swift:226-245` adds `MarkdownLinkOpener`,
  which opens a scheme-less, host-less, `"/"`-rooted URL as a local file (with a `:<line>` suffix rule) and hands
  everything else to `NSWorkspace.shared.open`; ported literally, a Windows drive-letter link parses with the
  drive letter read as a URL scheme and mostly fails.
- **The three sub-questions (PA-0020):** (1) should a Windows absolute path (`C:\...`, `C:/...`, `/C:/...`) take
  the source's local-file branch with the same `:line` rule; (2) should a UNC path
  (`\\host\share\...`, `file://host/...`) ever be opened, given it sends the user's Windows credentials to a
  named host; (3) should a directly-runnable local file type (`.exe`, `.bat`, `.cmd`, `.vbs`, `.js`, `.hta`,
  `.msi`, `.scr`, `.lnk`, `.cpl`, among others) be launched, as NSWorkspace launches an app, or shown selected in
  File Explorer instead.
- **Why it is the owner's call (contract section 8: a serious security problem needs a decision; section 11:
  drive and UNC paths, preserve intent):** options (2) and (3) trade fidelity to the source against sending
  credentials to, or executing, something a chat model named.
- **Options on record (PA-0020):** A, parity of intent (yes / open / launch); B, recommended (yes / never open /
  reveal in File Explorer); C, literal (no Windows-path handling at all).
- **Blocks:** unit A29 (UI.Markdown.LinkParagraphView) and UI.Markdown.LinkPopover.
