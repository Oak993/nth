# Platform adaptation register

One entry per macOS-dependent source boundary: the original API and behavior, the Windows
replacement, the evidence for it, the requirements it affects, and how it is verified.

Status values: IDENTIFIED (boundary found, replacement not yet decided), DECIDED (replacement
chosen with evidence), IMPLEMENTED, VERIFIED, BLOCKED.

This register is seeded by the lead's own reading and extended from the reader ledger. It is
incomplete until the reading gate closes.

---

## PA-0001 Liquid Glass is the app's entire surface treatment

- **Status:** IDENTIFIED. Highest-risk visual boundary in the port.
- **Original:** SwiftUI's macOS 26 Liquid Glass. The app calls `.glassEffect(...)` at 19 sites and
  wraps groups in `GlassEffectContainer` at 3 sites. Verified by exhaustive grep over
  `DroppyCode/**/*.swift` at the pinned SHA:
  - `UI/Chrome/Chrome.swift:178` `.glassEffect(.regular.tint(Chrome.glassTint.opacity(0.3)).interactive(), in: shape)`
  - `UI/Chrome/Chrome.swift:180` same without `.interactive()`
  - `UI/Chrome/Chrome.swift:243`, `:252` `.glassEffect(in: shape)`
  - `App/Views/Chat/ChatView.swift:1127` `GlassEffectContainer(spacing: 2)`
  - `App/Views/Composer/ComposerView.swift:23` `GlassEffectContainer(spacing: 8)`, `:1304` `.glassEffect(.regular, in: shape)`
  - `App/Views/Diff/DiffInspector.swift:25` `GlassEffectContainer(spacing: 2)`
  - `App/Views/Composer/EffortSlider.swift:788`
    `.glassEffect(.regular.tint(Color.white.opacity(look.isLightFill ? 0.55 : 0.32)).interactive(), in: Circle())`
  - `App/Views/Composer/RequestCards.swift:64` `.glassEffect(.regular.tint(Chrome.warning.opacity(0.1)), in: .rect(cornerRadius: 20, style: .continuous))`
  - `App/Views/Composer/ThreadChangesTab.swift:38`, `App/Views/Settings/ShortcutsSettingsPage.swift:477` `.regular.interactive()`
  - `App/Views/Palette/CommandPalette.swift:97` `in: .rect(cornerRadius: 24, style: .continuous)`
  - `App/Views/Chat/TimelineMinimap.swift:338` `in: .rect(cornerRadius: Chrome.cardCornerRadius, style: .continuous)`
  - plus `HydraPanel.swift:105`, `HydraProgressBar.swift:576`, `SubagentPanel.swift:339`,
    `FollowUpQueueTab.swift:154`, `FloatingSidebarPanel.swift:90`, `TourView.swift:82`,
    `UsageFloatingPanel.swift:59`, `WallpaperCard.swift:42`
- **Corroborating source comment:** `UI/Theme/AppTheme.swift:11-13` states "The app stays Liquid
  Glass throughout: a theme tints the glass and recolors the accent and status hues, never paints
  opaque surfaces."
- **Notable negative finding:** there is **no** `NSVisualEffectView`, `.ultraThinMaterial`,
  `.thinMaterial`, `.regularMaterial` or `.thickMaterial` anywhere in the app. The source has no
  pre-Liquid-Glass fallback to copy, so the Windows implementation cannot be derived from an
  existing alternative path in the source.
- **Why this is hard on Windows:** Liquid Glass is a rendering behavior of macOS 26, not a set of
  values in this source. It combines a backdrop blur with specular edge highlighting, refraction
  ("lensing") of content behind the shape, adaptive tinting against the backdrop, and, under
  `.interactive()`, deformation that tracks the pointer. Windows 11 offers Mica and Acrylic, which
  are blur-and-tint materials with none of the lensing, specular edge, or interactive deformation.
  Substituting Acrylic would be exactly the "flatten translucent material into a solid fill because
  the framework defaults to one" that contract section 10 forbids, only one step less flat.
- **What IS extractable from source:** the shape each glass surface is drawn in (`Circle`,
  `Capsule(style: .continuous)`, `.rect(cornerRadius: N, style: .continuous)`), the exact corner
  radii, the tint color and opacity per site, which sites are `.interactive()`, and the
  `GlassEffectContainer` spacing values (2, 2, 8). Those are SOURCE FACTS and must be reproduced
  exactly whatever the material implementation is.
- **What is NOT extractable from source:** the rendered appearance of the material itself. That
  requires the macOS reference runtime, which this session cannot reach (UNK-0003).
- **Consequence:** the material's appearance is a **visible compromise** in the sense of contract
  section 10 and therefore needs explicit owner acceptance and must remain identified as a
  deviation. It is recorded as UNK-0004 and it is a primary input to the Windows stack evaluation
  (contract section 9): any candidate stack must be judged on how closely it can render this, not
  on general suitability.
- **Affected requirements:** every screen. Assigned once the UI contract exists.
- **Verification method:** side-by-side capture against the pinned macOS build, per contract
  section 10. Currently BLOCKED by UNK-0003; until then the material stays UNVERIFIED while the
  source-defined geometry around it can still be verified.

---

## PA-0002 Windows are AppKit, not SwiftUI scenes

- **Status:** IDENTIFIED.
- **Original:** `App/DroppyCodeApp.swift:21-25` declares exactly one SwiftUI `Scene`:
  `Settings { EmptyView() }.commands { AppCommands(model: WindowManager.shared.model) }`, with the
  comment "The windows themselves are AppKit windows owned by WindowManager. This scene only
  carries the menus." Real windows are built in `App/Window/WindowManager.swift` as
  `DCDisplayCycleGuardedWindow` subclasses hosting SwiftUI through `FrameHostingView`.
- **Source facts captured:** main window 1320x860, content minimum 860x560, frame autosave name
  `DroppyCodeMainWindow`; settings window 780x580 and not resizable, title "Settings"; style mask
  `[.titled, .closable, .miniaturizable, .fullSizeContentView]` plus `.resizable` for the main
  window; `isReleasedWhenClosed = false`; `animationBehavior = .documentWindow`;
  `tabbingMode = .disallowed`; settings window centered over the main window and clamped to that
  window's screen `visibleFrame`.
- **Windows replacement:** not yet decided. Decision deferred to the stack evaluation, per
  contract section 9.
- **Note for the evaluation:** a stack that can only present one web view per window, or that
  cannot host native child windows and panels, would fail this boundary. Contract section 10:
  "Do not reduce multiple panels to one page."

---

## PA-0003 Objective-C window guards exist for macOS-specific crashes

- **Status:** IDENTIFIED, likely NOT_APPLICABLE on Windows but must be classified with evidence,
  not assumed away.
- **Original:** `App/Window/DCDisplayCycleGuardedWindow.h/.m` and `App/Window/DCNotificationGuard.h/.m`,
  reached through `App/Window/DroppyCode-Bridging-Header.h` (`SWIFT_OBJC_BRIDGING_HEADER` in
  `project.yml`). `DroppyCodeApp.swift:10` calls `DCInstallNotificationGuard()` before anything
  posts a notification; the comment cites "the popover window ordering on macOS 27". The window
  guard's comment cites AppKit evaluating the SwiftUI view graph inside its own display cycle
  under a transparent title bar on macOS 26 and later.
- **Assessment:** these are workarounds for specific AppKit defects. They are not user-observable
  product behavior. Pending full reading of the four files, the expected classification is
  NOT_APPLICABLE_WITH_EVIDENCE for Windows, with the evidence being that the defect they guard is
  an AppKit display-cycle and notification-ordering behavior that does not exist off AppKit.
  This must be confirmed by reading them, not by their names.

---

## PA-0004 SIGPIPE suppression for provider process writes

- **Status:** IDENTIFIED.
- **Original:** `App/DroppyCodeApp.swift:12` `signal(SIGPIPE, SIG_IGN)`, with the comment
  "Provider processes can exit while we write to them; report EPIPE instead of crashing."
- **Behavior to preserve:** writing to a provider process that has already exited must surface as a
  recoverable error on the write, not terminate the app.
- **Windows note:** Windows has no SIGPIPE; a write to a broken pipe fails with
  `ERROR_BROKEN_PIPE`/`ERROR_NO_DATA` on the write call itself. The observable behavior the source
  wants is therefore the default there. The adaptation is to ensure the chosen runtime does not
  convert that failure into an unhandled fault, and to route it to the same error path the macOS
  build reaches. To be confirmed against the actual write sites in `Core/Support/StdioProcess.swift`
  and the provider sessions once read.

---

## PA-0005 Hang watchdog writes crash stacks to a macOS log directory

- **Status:** IDENTIFIED.
- **Original:** `App/DroppyCodeApp.swift:16-17`, comment: "A frozen main thread gets its stacks
  written to `~/Library/Logs/Droppy Code`." Implementation in `Core/Support/HangWatchdog.swift`
  (131 lines, not yet read in full by the lead).
- **Windows replacement:** a per-user application log location. The exact choice is deferred until
  the file is read, because the requirement is whatever the source actually writes (format,
  trigger threshold, rotation), not a generic logging feature.

---

## PA-0006 Quit waits for an in-flight Hydra merge

- **Status:** IDENTIFIED.
- **Original:** `App/AppDelegate.swift:38-48`. `applicationShouldTerminate` returns `.terminateNow`
  unless `model.isAnyHydraMergeRunning`; if a merge is running it returns `.terminateLater`, polls
  every 250 ms, and replies true once the merge finishes or a **4 minute** deadline passes.
  `applicationWillTerminate` then calls `model.saveBeforeQuit()`,
  `TokenLedger.shared.flushLiveSpend()` and `MCPHub.terminateAll()`.
- **Windows replacement:** the platform's close/shutdown path must offer the same deferral with the
  same 250 ms poll and 4 minute cap, and must run the same three shutdown steps in the same order.
  This is product behavior, not a platform detail, so it is a parity requirement with a platform
  binding, and child-process cleanup at exit is a Gate E check.

---

## PA-0007 Process lifecycle, timeout and process-group kill

- **Status:** IDENTIFIED.
- **Original:** `Core/Support/Shell.swift:36-118`. Foundation `Process` with `Pipe` for stdout,
  stderr and optional stdin; `standardInput` defaults to `FileHandle.nullDevice` when there is no
  input. Default `timeout` is **120 seconds**. Cancellation and the deadline both route to
  `terminate(_:)` at `:110-118`, which calls `process.terminate()` (SIGTERM) and then, **1 second**
  later and only if still running, `kill(getpgid(pid) == pid ? -pid : pid, SIGKILL)`.
- **Exact semantics to preserve:** if the child is a process-group leader the SIGKILL goes to the
  **whole group** (negative pid); otherwise only to the child. This is what stops an agent CLI from
  leaving a tree of orphans behind.
- **Windows replacement (not yet decided, candidate only):** a Job Object per spawned child with
  `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`, giving group termination that matches the negative-pid case.
  Graceful-then-forceful needs a real equivalent of SIGTERM, which Windows lacks for console-less
  children; the candidate is a console control event or cooperative shutdown first, then job
  termination after the same 1 second. **This must be probed, not assumed** (contract section 11:
  "Do not blindly replace a Unix command name with a Windows one").
- **Affected:** every provider session, every project script, git calls, the terminal.
- **Gate E relevance:** child-process cleanup on app close is an explicit Gate E check.

## PA-0008 Output collection keeps a bounded tail

- **Status:** IDENTIFIED.
- **Original:** `Core/Support/Shell.swift:244-321`. With an `outputLimit`, each stream keeps only the
  **tail**; the buffer is trimmed to `limit` bytes once it exceeds `limit * 2`, so an endless stream
  costs O(limit) memory. Without a limit the whole output is kept. The source comment states the
  distinction: a command a model runs can print without end, the app's own commands keep everything.
  After the process exits, `finished(within: 3)` waits up to **3 seconds** for the readers to drain.
- **To preserve:** the limit semantics, the 2x trim threshold, and the 3 second drain window, all of
  which are observable when a tool prints a lot.

## PA-0009 Login-shell environment capture

- **Status:** IDENTIFIED. No direct Windows analogue; needs a decision backed by the source intent.
- **Original:** `Core/Support/Shell.swift:325-457`. Because a Finder-launched macOS app inherits a
  minimal PATH that cannot find provider CLIs, the app runs the user login shell once at launch with
  arguments `-l -i -c`, printing a marker, then `/usr/bin/env -0`, then the marker again, with an
  **8 second** timeout, and parses the NUL-separated environment between the two markers. The marker
  string is `__DROPPY_CODE_ENVIRONMENT__`.
- **Exact excluded keys (do not paraphrase):** `_`, `SHLVL`, `PWD`, `OLDPWD`, `TERM`, `TERM_PROGRAM`,
  `TERM_PROGRAM_VERSION`, `TERM_SESSION_ID`, `COLORTERM`, `ITERM_SESSION_ID`, `XPC_SERVICE_NAME`,
  `__CFBundleIdentifier`, `CLAUDECODE`, `CLAUDE_CODE_ENTRYPOINT`, `CLAUDE_CODE_SSE_PORT`.
- **Exact PATH augmentation, appended in this order when absent:** `/opt/homebrew/bin`,
  `/opt/homebrew/sbin`, `/usr/local/bin`, `$HOME/.local/bin`, `$HOME/.bun/bin`, `$HOME/.cargo/bin`,
  `$HOME/.npm-global/bin`, `$HOME/.volta/bin`, `/usr/bin`, `/bin`, `/usr/sbin`, `/sbin`.
  Separator is a colon.
- **Shell resolution:** `$SHELL` if executable, else the `pw_shell` from `getpwuid(getuid())` if
  executable, else `/bin/zsh`.
- **Fallback environment:** the process environment minus the excluded keys, with the augmented
  PATH, and `LANG` defaulted to `en_US.UTF-8` when unset.
- **Git resolution (`:394-409`):** PATH normally yields `/usr/bin/git`, which is Apple's shim that
  resolves the active developer directory on every call. The source measures 11.5 ms per shim call
  against 4.5 ms for the real binary and therefore resolves `xcode-select -p` once and uses
  `<developer>/usr/bin/git` when it is executable; without developer tools the shim stays so its
  install prompt still appears.
- **Windows question this raises (UNRESOLVED, not to be guessed):** Windows processes inherit the
  user environment directly, so the login-shell read has no purpose there; but the PATH augmentation
  list is entirely macOS and Unix package-manager paths that do not exist on Windows. Reproducing the
  literal list would be meaningless, and inventing a Windows list would be an invented default, which
  contract section 2 forbids. The intent (find provider CLIs installed by user-level package managers)
  is a SOURCE FACT; the Windows locations are not in the source. This needs an explicitly justified
  ENGINEERING DECISION derived from where the supported provider CLIs actually install on Windows,
  verified by probing. Tracked as UNK-0005.

## PA-0010 Executable lookup

- **Status:** IDENTIFIED.
- **Original:** `Core/Support/Shell.swift:411-423`. A name containing a slash is treated as a path
  with a leading tilde expanded and tested with `isExecutableFile`; otherwise PATH is split on colons
  and each directory is joined with the bare name and tested with `isExecutableFile`.
- **Windows difference that must be handled deliberately:** Windows resolves executables through
  `PATHEXT` and separates PATH entries with semicolons. A bare name such as `codex` exists on disk as
  `codex.cmd` or `codex.exe`, and npm-installed CLIs are typically `.cmd` shims. Contract section 11
  calls this out explicitly for the provider whose command name is `cmd`: the port must establish how
  to distinguish that provider from the Windows command interpreter and validate the actual
  executable identity. Tracked as UNK-0006.

## PA-0011 Shell command write-classification is product behavior, not plumbing

- **Status:** IDENTIFIED. Highest risk of being wrongly "adapted".
- **Original:** `Core/Support/Shell.swift:141-216`, `ShellCommandKind.mayWriteFiles`. The app decides
  whether to snapshot the working tree before and after a command by classifying the command text.
  Anything not provably read-only counts as writing, so an edit is never missed.
- **Exact rules:** a command containing a redirection character, a dollar-parenthesis substitution, or
  a backtick is a writer immediately. Otherwise the text is split on the six separators
  (and-and, or-or, semicolon, pipe, newline, single ampersand) and every part must be read-only.
  Per-tool rules: `sed` reads only with a short single-dash flag containing `n` and without `-i` or
  `--in-place`; `awk`, `gawk`, `mawk` read only when the text has no `system(` call; `find` and `fd`
  read only without `-delete`, `-exec`, `-execdir`, `-ok`, `-okdir`, `-x`, `-X`; `sort` reads only
  without a `-o` argument; `git` reads only for the listed subcommands; `xcodebuild` reads only with
  `-showBuildSettings`, `-list`, `-version` or `-showsdks`; `swift`, `swiftc`, `node`, `npm`,
  `python`, `python3` read only for an argument list that is exactly `--version`, `-version` or `-v`;
  `env` reads only when every argument starts with a dash; everything else must be in the reader set.
  The tool name is taken as the last path component of the first token.
- **Reader set (exact, 52 entries; count corrected 2026-09-19 after an independent critic caught a miscount in this register - the transcribed list below was complete, the stated number was not):** ls, cat, bat, head, tail, wc, grep, egrep, fgrep, rg, tree,
  pwd, echo, printf, which, command, type, stat, file, du, df, ps, printenv, date, whoami, hostname,
  uname, basename, dirname, realpath, readlink, column, uniq, cut, tr, nl, diff, cmp, jq, yq, xxd,
  od, md5, shasum, cksum, man, true, false, sleep, cd, pushd, popd.
- **Git reader subcommands (exact, 25 entries):** log, status, diff, show, branch, rev-parse, rev-list, ls-files,
  ls-tree, blame, remote, describe, shortlog, cat-file, tag, config, grep, whatchanged, reflog,
  for-each-ref, symbolic-ref, count-objects, var, version, help.
- **Why this must be ported verbatim:** these lists determine whether a turn records file changes.
  Dropping or modernizing an entry changes observable product behavior (a missed or spurious diff).
  Contract section 2 forbids changing defaults. The lists are Unix tool names, but the classifier runs
  on the **command text the model produced**, so the list is a product constant, not a platform
  constant. Whether the Windows build additionally needs Windows tool names depends on what shell the
  app runs commands in on Windows, which is an open boundary tracked with UNK-0005.
- **Known imprecision, recorded not repaired:** the splitter does not respect quoting, so a separator
  inside a quoted argument splits the line into parts that read as unfamiliar tools. The source
  comments that this is deliberate because the resulting answer (writer) is the safe one. Contract
  section 8 requires recording this rather than fixing it.

## PA-0012 Iconography is SF Symbols, which cannot be redistributed

- **Status:** IDENTIFIED. Second-highest visual risk after PA-0001, and a licensing obstacle rather
  than only an engineering one.
- **Original:** the app draws system iconography with SF Symbols. Measured at the pinned SHA over
  `DroppyCode/**/*.swift`: 184 `systemName:` sites, 19 `systemImage:` sites, 3 `systemSymbolName:`
  sites, using **70 distinct symbol names**. Full list with per-symbol counts:
  `porting/evidence/sf_symbols_used.txt`.
- **Most-used symbols:** `chevron.right` (15), `checkmark` (9), `chevron.down` (8), `xmark` (6),
  `plus` (6), `xmark.circle.fill` (5), `arrow.up.right` (5), `magnifyingglass` (4), `info.circle` (4),
  `checkmark.circle.fill` (4), `arrow.right` (4).
- **Semantically loaded symbols that carry product meaning, not decoration:** `hand.raised`,
  `hand.raised.fill`, `hand.raised.slash` (permission and supervision states), `lock.open`,
  `wand.and.sparkles`, `pencil.line` (the four `RuntimeMode` symbols in
  `Core/Models/Provider.swift:171-178`), `arrow.triangle.merge` (Hydra landing),
  `point.3.connected.trianglepath.dotted`, `server.rack` (MCP), `drop`/`drop.fill` (the Droppy mark
  context), `checkmark.seal.fill`, `checkmark.shield.fill`.
- **The obstacle:** SF Symbols are Apple artwork licensed for use on Apple platforms. They are not
  redistributable inside a Windows application. Contract section 10 states plainly: "Do not extract
  or redistribute restricted platform assets or fonts. Record any licensing or availability obstacle
  that affects parity instead of silently substituting assets."
- **Therefore:** the Windows build cannot ship these glyphs, and quietly swapping in a different icon
  set would be an unrecorded product change. This needs an owner decision. Tracked as UNK-0007.
- **What is NOT affected:** the app's own artwork is first-party and ships in the repository:
  74 SVGs under `DroppyCode/Resources/Assets.xcassets` including 25 `hydra-head-NN` marks,
  `hydra-mark`, 13 `provider-*` icons and 27 `mcp-*` vendor icons, plus the `AppIcon.icon` assets.
  Those are inventoried and reusable subject to the trademark question in UNK-0002.
- **Verification method:** once a decision exists, each of the 70 symbols maps to a named replacement
  in the UI contract, per site, so no icon is substituted silently.

## PA-0013 Typography is the macOS system font

- **Status:** IDENTIFIED.
- **Original:** 430 `.font(.system...)` sites plus `.font(.caption)` (17), `.font(.callout)` (11),
  `.font(.title...)` (2), `.font(.headline)` (2), `.font(.body)` (2), and AppKit
  `NSFont.systemFont` (8), `NSFont.monospacedSystemFont` (2), `NSFont.boldSystemFont` (1).
  There are 73 `.font(.chat...)` sites, which are the app's own semantic font scale, and 50
  `.monospaced` sites.
- **What this means:** on macOS the system font resolves to SF Pro (and SF Mono for monospaced),
  with Apple's metrics, optical sizing and dynamic weight axes. SF Pro is licensed for use on Apple
  platforms and is not redistributable with a Windows application.
- **Consequence for parity:** text metrics drive layout. A different face with different advance
  widths changes line breaks, truncation points, and the width of every label-sized control, which
  contract section 10 requires be checked explicitly ("text/line-wrap checks"). So this is not a
  cosmetic substitution; it propagates into geometry.
- **What IS source-defined and must be preserved exactly:** the app's own `.font(.chat...)` scale
  and every explicit size, weight and design argument passed to `.font(.system(...))`. Those are
  SOURCE FACTS. The 73 `.font(.chat...)` sites mean there is a first-party type scale to extract;
  it is read as part of the UI layer assignment.
- **Open decision:** which face the Windows build uses. Windows ships Segoe UI Variable and Cascadia
  Mono, both redistributable on Windows. Choosing one is an ENGINEERING DECISION that must be
  recorded with its layout consequences measured, not assumed. Folded into UNK-0007.

## PA-0014 The update feed is the public GitLab Releases API and is platform-neutral

- **Status:** IDENTIFIED. The feed needs no adaptation; the artifact and the installer do.
- **Original feed (SOURCE FACT, `App/UpdateChecker.swift:66-71`):**
  - project id `86415974`, described in source as "Public, so the API needs no token"
  - project URL `https://gitlab.com/droppyformac1/droppy-code`
  - releases page `https://gitlab.com/droppyformac1/droppy-code/-/releases`
  - API `https://gitlab.com/api/v4/projects/86415974/releases?per_page=20`
- **Check policy (SOURCE FACT, `:59-64`, `:76-79`):** a check keeps "the newest stable version that
  ships a disk image" and compares it with the running build. Checks run on launch after a
  `launchDelay` of **8 seconds**, every `checkInterval` of **6 hours** while running, when the app
  returns to the front after a stale interval, and whenever the About page opens or asks.
- **Persisted keys (SOURCE FACT, `:81-85`):** `updateChecker_release`, `updateChecker_lastChecked`,
  `updateChecker_notifiedVersion`. The result is persisted so About can show a known update
  immediately, and `updateAvailable` is recomputed from the running version so an installed update
  stops reading as available.
- **Transport (SOURCE FACT, `:92-101`):** ephemeral `URLSession`, `reloadIgnoringLocalCacheData`,
  `urlCache = nil`, request timeout **15 s**, resource timeout **30 s**, `waitsForConnectivity = false`,
  headers `User-Agent: Droppy Code/<version>` and `Accept: application/json`.
- **Why this matters for the port:** the feed is plain HTTPS against a public GitLab API. It works
  unchanged on Windows. Contract section 17 forbids inventing a release endpoint, and none needs to
  be invented: this is the real one, and it is reused as-is.
- **What does NOT carry over (`App/AppUpdater.swift`):** the macOS install mechanism. The source
  downloads a `.dmg` to `DroppyCodeUpdate-<uuid>.dmg`, attaches it with `/usr/bin/hdiutil attach`,
  verifies the staged bundle is Droppy's and that **Gatekeeper accepts it**, detaches with
  `hdiutil detach -force` (60 s timeout), writes a shell installer script, hands off, quits, and the
  script waits for the process to end, swaps the bundle into place, clears the quarantine attribute
  with `xattr -dr com.apple.quarantine`, and relaunches. There is a handoff deadline after which the
  installer is called off, and a refused quit makes the installer give up and the UI pill revert.
- **Windows replacement (to be decided, with the source behavior preserved):** the same feed and the
  same check policy; a Windows release asset instead of a disk image; an out-of-process updater that
  waits for the app to exit, replaces the installed files, and relaunches, mirroring the source's
  handoff, deadline, give-up and pill-revert behavior. Authenticode signature verification is the
  structural analogue of the Gatekeeper acceptance check, so the update is not installed if the
  downloaded artifact is not trusted.
- **REAL BLOCKER, recorded honestly and not worked around (UNK-0009):** the source selects "the
  newest stable version that ships a disk image". Published releases currently carry macOS disk
  images only. A Windows build pointed at this same feed will therefore find **no installable
  Windows asset**, so the end-to-end update path cannot be verified live until a Windows asset is
  published to that GitLab project. Contract section 17 is explicit that missing endpoints,
  certificates or publishing access "must remain explicit blockers for those capabilities". The
  updater is still implemented and tested against a controlled local fixture of the same API shape;
  the live path stays BLOCKED, and no unsigned local build is described as production-ready.

## PA-0015 Windows file handles linger after the owning process exits

- **Status:** IDENTIFIED. Found empirically on the target platform, not inferred.
- **How it was found:** running `porting/tools/negative_test.mjs` on Windows 11 on 2026-09-19.
  A `writeFileSync` to `porting/read_ledger.jsonl` threw `UNKNOWN`, `errno -4094`, `syscall open`,
  immediately after a child `node verify.mjs` process that had read the same file had exited. The
  failed write left an injected regression in place; the gate then correctly reported the damaged
  evidence, which is how it was noticed. A single write is therefore not a reliable file update on
  Windows even when the previous reader has exited.
- **Why this is a product concern and not only a tooling concern:** the app performs exactly this
  pattern constantly. `Core/Support/Shell.swift` spawns child processes that read and write project
  files; `Services/Git/Git.swift` runs git processes over the checkout; checkpoint restore and
  revert rewrite the working tree; `Services/Git/WorkingTreeWatch.swift` watches it; the store
  rewrites `library.json` and `threads/<uuid>.json` atomically per save. On macOS a POSIX unlink or
  rename succeeds even while another process holds the file open. On Windows it can fail with a
  sharing violation, and the failure is transient and timing-dependent.
- **Corroborating prior evidence, independently recorded:** the separate Marrow project's own
  Windows test run found that a recursive delete failed on read-only files across every directory
  holding Git loose objects. That is the same family of defect: Windows filesystem semantics
  differing from POSIX in ways that only appear at runtime under real timing.
- **What the port must do:**
  - Treat file replace, delete and rename as operations that can fail transiently. Retry with
    backoff, and surface a real error after a bounded number of attempts rather than silently
    dropping the write.
  - Clear the read-only attribute before deleting, since Windows refuses to delete a read-only
    file that POSIX would happily unlink.
  - Prefer atomic replace primitives where the platform offers them, and verify the bytes landed.
  - Never assume a child process's handles are gone the instant it exits.
- **Where this lands in the parity matrix:** it does not change any observable product behavior,
  so it is an internal ENGINEERING DECISION serving existing requirements (save, checkpoint,
  revert, worktree teardown, project script runs). It is recorded here because getting it wrong
  produces data loss and intermittent failures that a happy-path test will not catch.
- **Verification method:** targeted tests that hold a handle open while the app attempts the
  operation, plus the Gate E child-process-cleanup check. To be written with the persistence tests.
