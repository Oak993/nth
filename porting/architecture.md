# Architecture of the pinned Droppy Code (derived, not idealized)

Reference: `E:/Droppy/reference/droppy-code`, commit `dd2b86efef9e77168f304a86e8a8411877578bbe`,
tree `f761e5ef3745c4f68958ffa4014ab0b618c9d419`. Read-only. Nothing under it was modified to
produce this document.

This is contract section 6 output: the **real** architecture of the app as committed, with
file:line evidence. Where the source contradicts the project's own stated design rules, the
source wins and the contradiction is recorded rather than smoothed over.

Label key: **SF** = SOURCE FACT (verified in the pinned source), **ED** = ENGINEERING DECISION
(a choice this port makes, not a fact about the source), **UNRESOLVED** = insufficient or
conflicting evidence.

Inputs used, in authority order: the source itself; `porting/read_ledger.jsonl` (433 evidence
records); the seven verbatim extracts in `porting/extracts/`; `AGENTS.md` (claim under test,
not authority).

---

## 0. Shape of the thing, measured

**SF.** Counted from the pinned tree with `find` and `wc -l`:

| Unit | Count |
| --- | --- |
| Tracked files in scope (contract section 5) | 433 |
| Text lines in scope | 85,392 |
| Swift files under `DroppyCode/` | 191 |
| Swift lines under `DroppyCode/` | 72,303 |
| Objective-C files | 2 `.m`, 2 `.h`, plus 1 bridging header |
| Metal shader files | 1 (`UI/Common/Genie.metal`, 2,560 bytes) |

Swift lines by layer directory:

| Layer | Files | Lines |
| --- | --- | --- |
| `DroppyCode/App` | 78 | 40,860 |
| `DroppyCode/Services` | 49 | 16,759 |
| `DroppyCode/UI` | 34 | 8,854 |
| `DroppyCode/Core` | 30 | 5,830 |

**SF, build inclusion** (`project.yml:19-24`): the target's sources are `DroppyCode` as a
`syncedFolder`, so every file under it is in the app target with no per-file opt-in, plus
`LICENSE`, `THIRD_PARTY_NOTICES.md` and `TRADEMARK.md` as bundle resources.

**SF, build settings that bound the port** (`project.yml:5-6, 11-21, 26-45`):
`deploymentTarget.macOS: "26.0"`, `SWIFT_VERSION: "6.0"`, `SWIFT_APPROACHABLE_CONCURRENCY: YES`,
`ARCHS: arm64`, `EXCLUDED_ARCHS: x86_64`, `PRODUCT_BUNDLE_IDENTIFIER: iordv.droppycode`
(Debug: `iordv.droppycode.dev`), `MARKETING_VERSION: "1.6.0"`, `CURRENT_PROJECT_VERSION: "19"`,
`SWIFT_OBJC_BRIDGING_HEADER: DroppyCode/App/Window/DroppyCode-Bridging-Header.h`,
`ENABLE_HARDENED_RUNTIME: YES`, `DEAD_CODE_STRIPPING: YES`.

**SF, the only third-party dependency** (`project.yml:7-10, 25`): SwiftTerm, pinned
`exactVersion: 1.20.0`. There is no other package. There is no test target and no test
script in the project file.

---

## 1. Modules and subsystems: responsibility and state ownership

29 modules are documented below. "Owns" means the module holds the authoritative in-memory
copy and is the only writer.

### Core layer (30 files, 5,830 lines): records and process plumbing

**1. `Core/Models` (17 files).** Plain `Codable`/`Sendable` value types plus the Hydra prompt
corpus. Owns no runtime state; every type here is copied into the App layer's lists.
Notable: `Library.swift` (the persisted root: `projects`, `threads`),
`Timeline.swift` (332 lines: `TimelineItem`, `ToolCall`, `TurnRecord`, `ThreadDocument`),
`Hydra.swift` (1,345 lines, of which the overwhelming majority is the literal prompt corpus:
`HydraPrompts`, `HydraBudget.maxSeconds = 35 * 60` at `Hydra.swift:194`,
`HydraBudget.reportSeconds = 3 * 60` at `:196`, `HydraPrompts.maxDelegationRounds = 3` at `:847`),
`Provider.swift` (`enum ProviderKind`, 13 cases, `Provider.swift:3-17`),
`MCP.swift`, `MCPCatalog.swift` (483 lines of catalog literals), `MCPCustom.swift`,
`Shortcuts.swift`, `ThreadContinuation.swift`, `TokenSpend.swift`, `HydraCookbook.swift`,
`HydraDelegationStream.swift`, `Requests.swift`, `MergeRequestLink.swift`,
`FileChangeSummary.swift`, `AntiSlopPolicy.swift`, `ProviderSettings.swift`.

**SF, the 13 provider kinds and their executables** (`Provider.swift:3-54`):
`codex`→`codex`, `claude`→`claude`, `cursor`→`cursor-agent`, `opencode`→`opencode`,
`grok`→`grok`, `deepseek`→`""`, `meta`→`""`, `zai`→`""`, `devin`→`devin`,
`antigravity`→`agy`, `copilot`→`copilot`, `commandcode`→`cmd`, `pi`→`pi`.
The three with an empty executable name are the API-key providers.

**2. `Core/Support` (13 files).** Process launching, JSON, caches, text cleanup, watchdogs.
- `Shell.swift` (458 lines) owns `LoginEnvironment`'s captured environment
  (`Mutex<[String: String]?>` at `Shell.swift:326`), the single-flight load task
  (`Shell.swift:329`) and the resolved git binary (`Shell.swift:398`).
- `StdioProcess.swift` (180 lines) owns one child process, its three pipes, its framer, its
  stderr tail and its exit waiters (`StdioProcess.swift:16-25`).
- `StdioFramer.swift` owns line and `Content-Length` framing.
- `JSONValue.swift`, `JSONRPC.swift`, `Coding.swift` are pure.
- `RecentCache.swift` is a cost-bounded LRU used by the document prefetcher and the diff caches.
- `ThumbnailCache.swift` is the app's only `actor` outside MCP (`ThumbnailCache.swift:9`).
- `HangWatchdog.swift` owns a `OSAllocatedUnfairLock(initialState: State())`
  (`HangWatchdog.swift:34`) and a dedicated `Thread`.
- `SwitchLatency.swift` owns a `CFRunLoopObserver`, development builds only
  (`SwitchLatency.swift:21`).
- `CaptureRun.swift` owns the capture-run launch-argument decision, read once
  (`CaptureRun.swift:10-19`).
- `Text.swift` holds `AppInfo` (`Text.swift:3-9`) and `TextCleanup`.
- `ObservedValue.swift` (14 lines) is the per-item observation cell the whole UI's
  render-isolation strategy rests on.

### Services layer (49 files, 16,759 lines)

**3. `Services/Store/Storage.swift` (389 lines).** Owns all on-disk locations
(`Storage.root` at `:19-25`, `libraryURL` `:27`, `threadsDirectory` `:31`,
`attachmentsDirectory` `:33`, `wallpaperDirectory` `:36`, `worktreesDirectory` `:38`,
`patchesDirectory` `:41`). Also holds two process-wide singletons:
- `DocumentPrefetch.shared` (`Storage.swift:245-335`) owns a `Mutex<State>` with a
  `RecentCache<UUID, ThreadDocument>` (`limit: 12`, `costLimit: 16 * 1_024 * 1_024`,
  `pendingLimit: 24`, `Storage.swift:264-273`) and one detached utility worker.
- `DiskWriter.shared` (`Storage.swift:337-389`) owns the serial write queue
  `"droppycode.storage.write"` at `.utility` (`:339`) and a `Mutex<[URL: any Encodable & Sendable]>`
  of coalesced pending writes (`:343`). **This is the only writer of library and thread files.**

**4. `Services/Store/TokenLedger.swift` (716 lines).** `@MainActor @Observable` singleton
(`TokenLedger.shared`, `:63-64`). Owns `dailyTotals`, `legacyDaily`, live spend state and
the Codex rollout provenance. Its own comment states the confinement assumption: "Every
provider session runs on the main actor, so this needs no lock" (`TokenLedger.swift:105-106`).

**5. `Services/Git` (7 files, `Git.swift` 723 lines).** `Git` is a `Sendable` struct with
exactly one stored property, `directory` (`Git.swift:20-26`). It owns no state.
Two pieces of shared state do live here: `Git.worktreeIndexPaths`
(`Mutex<[String: (stamp: GitFileStamp, path: String?)]>`, `Git.swift:509`) and
`WorkingTreeWatch.watches` (`Mutex<[(directory: String, watch: WorkingTreeWatch)]>`,
`WorkingTreeWatch.swift:86`, `limit = 32` at `:87`). `WorkingTreeWatch` owns an FSEvents
stream on a private `.utility` queue `"droppy.working-tree-watch"` (`:121, :147`) and a
scratch git index in the temporary directory (`:120`).

**6. `Services/MCP` (10 files).**
- `MCPStore.swift` is `@MainActor @Observable` and owns `connections`, `states`, `drafts`,
  `customs` and the per-server probe tasks (`MCPStore.swift:10-20`).
- `MCPHub.swift` is an `actor` (`:17`), `MCPHub.shared` (`:18`), owning `specs`, `clients`,
  `toolCache`, `failed` (`:45-48`), plus a `nonisolated` lock-guarded process registry
  `MCPLiveProcesses` (`:23`, `:185-206`) so the quit path can reach it without an actor hop.
- `MCPProxy.swift` is a `@unchecked Sendable` singleton owning the loopback `NWListener`,
  the route table and live connections behind one `NSLock` plus a `DispatchQueue("MCPProxy")`
  (`MCPProxy.swift:53-58`), and the persisted port in `Mutex<UInt16>` (`:14`).
- `MCPOAuth.swift` (929 lines) owns the OAuth token store and a loopback callback listener.
- `MCPProbe.swift`, `MCPProviderConfig.swift`, `MCPBridgeSource.swift` (an embedded ESM
  JavaScript bridge), `MCPKeychain.swift`, `MCPExternalSync.swift`, `MCPSessionOverrides.swift`.

**7. `Services/Providers` (30 files, the largest Services group).**
- `ProviderSession.swift` (203 lines) defines the whole boundary: `TurnInput`,
  `SessionConfiguration` (16 fields, `:22-47`), `TranscriptMessage`, `AgentSpawn`,
  and `enum ProviderEvent` with **30 cases** (`:82-118`).
- `ProviderRegistry.swift` (817 lines) is `@MainActor @Observable` and owns `statuses`,
  `catalogs`, `commands`, `commandErrors`, `recentCommands`, `loadingCatalogs`, `planLimits`,
  `loadingLimits`, `redeemingReset`, `credits` (`:33-68`), with freshness windows
  `commandFreshness = 600` s (`:44`) and `catalogFreshness = 6 * 3600` s (`:56`).
- Ten session adapters: `ClaudeSession`, `CodexSession`, `CopilotSession`, `AntigravitySession`,
  `CommandCodeSession`, `PiSession`, `ACPSession` (serving `cursor`, `opencode`, `grok`, `devin`),
  `DeepSeekSession`, `MetaSession`, `ZaiSession`, with `OpenAICompatibleSession` (1,336 lines)
  underneath the API-key three.
- Four keychain shims (`APIKeychain`, `CommandCodeKeychain`, `DeepSeekKeychain`, `MetaKeychain`,
  `ZaiKeychain`), `PlanLimits`, `PiPlanLimits`, `ProviderCredits`, `UsageLimitSignal`,
  `NativeFileTools`, `ProjectGuidance`, `TextGeneration`, `CommandCodeAPI`, `DeepSeekAPI`,
  `MetaAPI`, `ZaiAPI`, `PiCLI`.

### UI layer (34 files, 8,854 lines)

**8. `UI/Chrome` (10 files).** `Chrome.swift` (1,107 lines) holds the design tokens and the
glass surfaces. `WindowChrome.swift` (571 lines) configures the AppKit window and **also
declares `SidebarLayout` (`WindowChrome.swift:239`)**, which the App layer owns an instance of.
`Popovers.swift`, `ChromeSegmentedPicker`, `ChromeVisualPicker`, `RowDrag`, `InfoHoverButton`,
`HydraPairMark`, `ProjectIconPicker`, `ProjectSymbolCatalogue`.

**9. `UI/Common` (10 files).** `Genie.swift` + `Genie.metal` (the only GPU shader,
applied through `.distortionEffect(ShaderLibrary.genie(...))` at `Genie.swift:157-158`),
`AttachmentPreview`, `AttachmentPreviewPanel`, `Components`, `ChatZoom`, `HydraGlyph`,
`MCPGlyph`, `WorkingIndicators`, and `RenderBudget.swift` which owns `ScrollActivity`
(`RenderBudget.swift:13`), read by the streaming pacer.

**10. `UI/Markdown` (9 files, 3,000+ lines).** `MarkdownParser`, `MarkdownView` (1,499 lines),
`LinkParagraphView` (550), `StreamVeil` (431), `ProseRunView`, `LinkTitles` (which owns its own
`NSLock`-guarded cache at `LinkTitles.swift:84`), `LinkPopover`, `SelectableMessageText`,
`HydraHeadHoverPopover`.

**11. `UI/Theme` (3 files).** `AppTheme.swift` (theme specs and accents), `ShortcutTint.swift`,
`Wallpaper.swift` which owns `WallpaperStore.shared` (`Wallpaper.swift:16`), the decoded and
blurred backdrop image.

**12. `UI/Sound` (3 files).** `ToneSynth`, `FinishChime`, `SettleChime`.

### App layer (78 files, 40,860 lines)

**13. Entry and lifecycle.** `DroppyCodeApp.swift` (26 lines), `AppDelegate.swift` (87),
`LegacyMigration.swift` (27), `AppCommands.swift` (105, the menu bar only).

**14. `AppModel` (4 files, 3,296 lines).** `AppModel.swift` (1,356), `AppModel+Hydra.swift`
(1,082), `AppModel+HydraMerge.swift` (721), `AppModel+HydraPairs.swift` (137), plus
`HydraCookbook+Resolve.swift` (69), which is the sanctioned upward-extension pattern
`AGENTS.md` names. `AppModel` owns the library: `projects`, `threads` (`AppModel.swift:30-45`),
the derived `ThreadIndex` (`:46-61`), the per-item observation cells `projectCells`,
`threadCells`, `childCells`, `selectionCells` (`:62-71`), `selectedThreadID` (`:72`),
`runtimes: [UUID: ThreadRuntime]` (`:108`), `idleSessionStops` (`:109`), `saveTask` (`:110`),
`hydraMergeSweepTask` (`:116`), `persistenceEnabled` (`:117`), and it constructs and holds
`settings`, `providers`, `mcp`, `terminals`, `sidebar`, `autoContinue` (`:15-22`, `:120-146`).

**15. `AppSettings.swift` (938 lines).** `@MainActor @Observable`. Owns every user default.
The key table is `AppSettings.Key`, `AppSettings.swift:117-179` (**61 key constants**, counted
from the enum body). Every stored property writes through `didSet` into `UserDefaults`
immediately, for example `AppSettings.swift:188-190`.

**16. `App/Runtime` (3 files, 3,874 lines).** `ThreadRuntime.swift` (3,637) is the single
largest and most important file in the app. `AutoContinue.swift` (159) owns
`resumesAt: [UUID: Date]`. `LimitHandoff.swift` (78).

**17. `App/Window` (7 files).** `WindowManager.swift` (219) owns `AppModel` itself
(`WindowManager.swift:13`), `mainWindow`, `settingsWindow` (`:15-16`), and the window sizes
(`:18-22`). `ThreadWindow.swift`, plus the two Objective-C guards.

**18. `App/Captures` (3 files, 1,740 lines).** `WebsiteCaptures.swift` (1,122),
`TourCaptures.swift`, `TourCaptures+Hydra.swift`. Drives the app's own screenshot and film
harness with ScreenCaptureKit (`WebsiteCaptures.swift:3, 1041-1048`) on a private
`.userInteractive` queue `"droppycode.website-captures.film"` (`:1064`).

**19. Updater (2 files).** `UpdateChecker.swift` (feed polling) and `AppUpdater.swift` (521,
download, verify, stage, hand off, relaunch).

**20-28. `App/Views` (9 groups, 57 files).** `Chat` (18 files, 12,000+ lines, of which
`TimelineRows.swift` 2,878 and `ThreadTimeline.swift` 2,298 and `ChatView.swift` 1,734),
`Composer` (14), `Settings` (12), `Sidebar` (3), `Usage` (3), `Tour` (3), `Terminal` (2),
`Diff` (1), `Palette` (1), plus `RootView.swift` (479).
`App/Views/Terminal/TerminalStore.swift` owns `TerminalSession` objects keyed by thread
(`TerminalStore.swift:105-107`), each wrapping a SwiftTerm `LocalProcessTerminalView` and a pty.

**29. `Resources`.** `Assets.xcassets` (provider marks, MCP marks, 25 Hydra head images, 8 tour
screenshots), `AppIcon.icon`.

---

## 2. The real dependency graph, and where it breaks the stated rule

### 2.1 What `AGENTS.md` claims

`AGENTS.md:15-24`: "`DroppyCode/Core` (models, pure support) reaches into nothing else in the
app. `DroppyCode/Services` (Git, Store, Providers) and `DroppyCode/UI` (Chrome, Common,
Markdown, Theme, Sound) reach only into Core. `DroppyCode/App` (entry, model, runtime,
windows, captures and every feature view) reaches into everything."

**SF: this rule is not enforced by anything.** All four directories compile into a single Swift
module (`project.yml:19-21`, `PRODUCT_MODULE_NAME: DroppyCode`, sources as one `syncedFolder`).
There is no module boundary, no access-control barrier and no build check. The rule is a
convention that the compiler cannot hold anyone to. That is the first architectural fact the
Windows port has to know: **layering here is documentation, not structure.**

### 2.2 Method

A mechanical pass extracted every top-level `class`/`struct`/`enum`/`actor`/`protocol`
declaration in all 191 Swift files (939 distinct type names), mapped each to its layer
directory, then scanned every non-comment line of each `Core`, `Services` and `UI` file for
references to type names declared only in a higher layer. That produced 29 candidate
file/symbol pairs, each of which was then read at its cited line. 24 were false positives
(string literals such as `"Chrome DevTools"` and `"Git"` in `MCPCatalog.swift:51, :130`,
generic parameter names such as `Key` and `ID`, and nested type references such as
`KeyPress.Result` and `AsyncStream<JSONValue>.Continuation`). Five are real.

### 2.3 The confirmed violations: 5 file/symbol pairs, 9 call sites, 3 files

| # | Site | Reaches | Direction | Evidence |
| --- | --- | --- | --- | --- |
| V1 | `Core/Models/MCP.swift:165` | `MCPProxy.url(for:)` | Core → Services | `server.url = MCPProxy.url(for: id)`; `MCPProxy` declared `Services/MCP/MCPProxy.swift:5` |
| V2 | `Core/Models/MCP.swift:238`, `:244`, `:246` | `Storage.root` | Core → Services | `let url = Storage.root.appendingPathComponent("mcp", isDirectory: true)`, then `connectionsURL` and `customsURL`; `Storage` declared `Services/Store/Storage.swift:18` |
| V3 | `UI/Theme/Wallpaper.swift:75` | `WebsiteCaptures.defaults` | UI → App | `private let defaults = WebsiteCaptures.defaults ?? .standard`; `WebsiteCaptures` declared `App/Captures/WebsiteCaptures.swift:21` |
| V4 | `UI/Theme/Wallpaper.swift:89`, `:128`, `:130` | `Storage.wallpaperDirectory` | UI → Services | three call sites |
| V5 | `UI/Markdown/MarkdownView.swift:331` | `HydraDelegationBlock` | UI → App | `HydraDelegationBlock(json: code, sent: info != "hydra")`; declared `App/Views/Chat/TimelineRows.swift:652` |

**Verdict (SF): the four-layer rule in `AGENTS.md` is violated in three files.** `Core` is not
dependency-free: `MCPPaths` (a Core enum) computes its file locations from `Storage` and its
proxy URLs from `MCPProxy`, both Services types. `UI` is not Core-only: the wallpaper store
reaches into both Services (`Storage`) and App (`WebsiteCaptures`), and the markdown renderer
instantiates an App-layer view.

**Consequence for the port (ED):** V1 and V2 make `Core/Models/MCP.swift` unloadable without the
storage and proxy subsystems, so a Windows port cannot lift `Core` as a standalone leaf
assembly or crate. V5 makes `UI/Markdown` depend on the chat feature views, so the markdown
renderer is not reusable in isolation either. The port should either reproduce the coupling
exactly (parity) or invert it through a seam and record the deviation; it must not silently
"fix" the layering, because `MCPPaths.directory` is a `static let` whose initializer creates
the directory as a side effect (`MCP.swift:238-241`), and moving it changes when that
directory appears on disk.

### 2.4 Framework dependency per layer (SF)

Counted from `import` lines:

| Layer | Imports |
| --- | --- |
| Core | Foundation 28, SwiftUI 3, os 2, Synchronization 1, Observation 1, ImageIO 1, CoreGraphics 1, AppKit 1 |
| Services | Foundation 48, Synchronization 4, Security 2, Observation 2, Network 2, AppKit 2, UniformTypeIdentifiers 1, SwiftUI 1, OSLog 1, ImageIO 1, CryptoKit 1, CoreServices 1 |
| UI | SwiftUI 26, AppKit 22, Foundation 6, UniformTypeIdentifiers 2, QuartzCore 2, ImageIO 2, CoreImage 1, AVFoundation 1 |
| App | SwiftUI 66, AppKit 50, Foundation 12, UserNotifications 3, UniformTypeIdentifiers 2, SwiftTerm 1, Security 1, ScreenCaptureKit 1, QuartzCore 1, AVFoundation 1 |

**SF:** `Core` imports SwiftUI (three files) and AppKit (one), so even the "pure support" layer
is not UI-framework-free. `Core/Models/MCP.swift:255` declares `extension Color`.

### 2.5 The real direction of control

Reading past the directory names, the actual call graph has one hub and two rings:

```
                      AppDelegate ──▶ WindowManager (owns AppModel)
                                            │
                                            ▼
   AppSettings ◀── AppModel ──▶ ProviderRegistry ──▶ 10 session adapters
   MCPStore    ◀──   │   ──▶ TerminalStore          │       │
   SidebarLayout ◀───┤                              │       ▼
                     ▼                              │   StdioProcess / URLSession
        runtimes: [UUID: ThreadRuntime] ◀───────────┘  (ProviderEvent callback)
                     │
                     ├──▶ Git (struct, per directory)
                     ├──▶ Storage / DiskWriter / DocumentPrefetch
                     ├──▶ TokenLedger.shared
                     └──▶ MCPHub.shared (actor) ──▶ child MCP servers
                                                 └▶ MCPProxy (loopback HTTP)
```

`ThreadRuntime` holds `private weak var app: AppModel?` (`ThreadRuntime.swift:413`) and calls
back into it constantly (`app.updateThread`, `app.turnFinished`, `app.spawnDroppyHead`), so
the App layer contains a genuine mutual dependency between `AppModel` and `ThreadRuntime`.
That cycle is not a layering violation under the stated rule (both are App), but it is the
single most load-bearing structural fact in the app and the port must preserve the weak
direction: `AppModel` owns the runtimes strongly; each runtime points back weakly.

---

## 3. Application lifecycle, in order

### 3.1 Process start, before any app object exists

**SF, `DroppyCodeApp.swift:3-19`.** `@main struct DroppyCodeApp: App` with
`@NSApplicationDelegateAdaptor(AppDelegate.self)`. Its `init()` runs five things in this
exact order, each with a comment stating why the order matters:

1. `DCInstallNotificationGuard()` (`:10`). Swizzles `NSNotificationCenter`'s two posting
   methods to catch and log an exception raised by an observer instead of letting it end the
   process (`DCNotificationGuard.m:19-40`). Must run **before anything posts a notification**.
2. `signal(SIGPIPE, SIG_IGN)` (`:12`). Provider children exit while the app writes to them;
   without this the write kills the process instead of returning EPIPE.
3. `WebsiteCaptures.prepare()` (`:14`). Must run **before anything reads settings or the
   library**. In a normal launch it returns immediately (`guard let output = outputDirectory,
   let root = storageRoot, let defaults else { return }`). In a capture run it wipes the
   capture storage root, recreates `threads/` and `films/`, removes the persistent domain
   `iordv.droppycode.website-captures` and writes a fixed fixture library.
4. `LegacyMigration.run()` (`:15`). One-time carry-over from bundle id `iordv.cody` and the
   Application Support folder `Cody`; guarded by `AppInfo.isDevelopment` and the
   `migratedFromEarlierName` default (`LegacyMigration.swift:5-25`).
5. `HangWatchdog.start()` (`:17`) then `SwitchLatency.start()` (`:18`).

**SF, `DroppyCodeApp.swift:21-25`:** the `Scene` is `Settings { EmptyView() }` carrying only
`.commands { AppCommands(model: WindowManager.shared.model) }`. **The app has no SwiftUI
windows.** Reading `WindowManager.shared` here is what constructs `WindowManager`, and with it
`AppModel`, since `AppModel` is a stored `let` on the manager (`WindowManager.swift:13`).

### 3.2 `AppModel.init()` side effects

**SF, `AppModel.swift:120-146`.** Ordered:
1. `settings = AppSettings()` (reads every default, `AppSettings.swift:690+`).
2. `providers = ProviderRegistry(settings: settings)`.
3. `mcp = MCPStore()`. **This has heavy side effects**: `MCPStore.init` calls `load()`, then
   `MCPProxy.shared.start()` (binding a loopback TCP listener), then `exportAll()` (which
   rewrites every provider MCP config file on disk) (`MCPStore.swift:22-28`).
4. `terminals = TerminalStore()`, `sidebar = SidebarLayout()`.
5. `let library = Storage.loadLibrary()` (synchronous read and decode of `library.json`,
   `Storage.swift:60-63`), then `projects` and `threads` assigned.
6. Crash recovery, in two loops: every thread with `lastStatus == .running` becomes
   `.interrupted` (`:129-131`); every thread with `hydra?.status == .running` becomes
   `.stopped` with `finishedAt = .now` (`:133-136`).
7. Observation cells built by hand, because "Property observers stay quiet inside an
   initializer" (`:137-141`).
8. `autoContinue = AutoContinue(app: self)` (`:142`).
9. A dangling `settings.lastProjectID` that names no project is cleared (`:143-145`).

**Ordering dependency (SF):** step 5 reads a file whose location (`Storage.root`) depends on
`CaptureRun.storageRoot`, which depends on `CommandLine.arguments`, which is why step 3 of
§3.1 has to precede everything.

### 3.3 `applicationDidFinishLaunching`

**SF, `AppDelegate.swift:8-28`**, in order:

1. `UNUserNotificationCenter.current().delegate = self`.
2. `let windows = WindowManager.shared; model = windows.model`.
3. **Capture-run fork**: `if WebsiteCaptures.isEnabled { Task { await WebsiteCaptures.run(model:) }; return }`.
   A capture run never shows the main window, never bootstraps and never checks for updates.
4. `windows.showMain()` (builds the window, §3.4).
5. `windows.model.openThreadOnLaunch()` (`AppModel.swift:574-601`): reuses the newest unused,
   untitled, unsent, non-helper thread in the current project, re-stamping its provider, model,
   effort, fast mode and Hydra pair from a fresh thread, and clearing
   `providerSessionID`/`providerResumeAt` if the provider changed; otherwise `newThread(in:)`.
6. Tour, if `!settings.hasSeenTour`: `Task { @MainActor in try? await Task.sleep(for: .milliseconds(700)); Tour.present(model:) }` (`:18-20`). The 700 ms is a literal.
7. `Task { await windows.model.bootstrap() }` (`:21`).
8. `UpdateChecker.shared.startBackgroundChecks()` (`:22`). Launch delay 8 s, interval 6 h
   (`UpdateChecker.swift:74-75`).
9. `if AppUpdater.shared.consumeRelaunchMarker() { UpdateInstallProgress.shared.armCelebration(); windows.showSettings(page: .about) }` (`:24-27`).

### 3.4 Window construction

**SF, `WindowManager.swift:62-110`.** Windows are plain AppKit windows, not SwiftUI scenes:

- Class: `DCDisplayCycleGuardedWindow` by default; the main window is `ThreadWindow`, the
  settings window is `SecondaryWindow` (`:63`, `:81`, `:90`).
- Style mask: `[.titled, .closable, .miniaturizable, .fullSizeContentView]`, plus `.resizable`
  when resizable (`:93-94`).
- `isReleasedWhenClosed = false`, `animationBehavior = .documentWindow`,
  `tabbingMode = .disallowed`, then `WindowChrome.configure(window)` (`:98-101`).
- Content: a `WindowLiveResize` observer object, then
  `FrameHostingView(rootView: AnyView(HostedRoot(model:liveResize:content:)))` inside a plain
  `NSView` container (`:103-108`).
- Main window size `1320 x 860`, minimum `860 x 560`, settings `780 x 580` (`:18-22`).
- Frame autosave name `"DroppyCodeMainWindow"` (`:24`, `:67-70`).

`FrameHostingView` overrides `acceptsFirstResponder` to always return true (`:211`) and
`performKeyEquivalent` to return false while hidden (`:216-218`). `HostedRoot` applies
`preferredColorScheme` and drives `NSApp.appearance` from the theme (`:148-159`).

### 3.5 Bootstrap (asynchronous, after the window is up)

**SF, `AppModel.swift:239-278`**, in order, with the comments stating the reasons:

1. `warmDocuments(recent)` where `recent` is the 12 most recently updated non-archived,
   non-panel threads (`:242-243`).
2. `await settings.loadKeychainInBackground()` (`:244`).
3. `await LoginEnvironment.load()` (`:245`). Everything after this runs with the user's PATH.
4. `sweepHydraCopies()` (`:247`).
5. `await resumeHydraMerges()` (`:249`), the launch merge sweep.
6. `hydraMergeSweepTask` started: a loop sleeping `.seconds(300)` and re-running
   `resumeHydraMerges()` for the life of the app (`:254-261`).
7. `await providers.refreshAll()` (`:262`).
8. A `withTaskGroup` loading catalogs for `[.claude, .codex, .deepseek, .meta]` that report
   installed (`:266-270`), then `.zai` serially if installed (`:271-273`).
9. `guard !WebsiteCaptures.isEnabled else { return }`, then
   `await Storage.sweepOrphanedAttachments()` (`:276-277`).

### 3.6 Steady state

Steady state is three concurrent regimes:
- The main actor runs the whole UI, `AppModel`, every `ThreadRuntime`, `AppSettings`,
  `ProviderRegistry`, `MCPStore`, `TerminalStore` and `TokenLedger`.
- Private serial queues run disk writes (`droppycode.storage.write`), stdio writes
  (`droppycode.stdio.write`), the proxy (`MCPProxy`), FSEvents (`droppy.working-tree-watch`),
  and, in a capture run, the film encoder.
- Detached tasks run document decoding, shell processes (`@concurrent` on `Shell.run`,
  `Shell.swift:35`) and the attachment sweep.

Two recurring timers run for the app's whole life: the Hydra merge sweep every 300 s
(`AppModel.swift:257`) and the update check every 6 h (`UpdateChecker.swift:74`). Per-thread,
`scheduleIdleSessionStop` runs a 600 s loop per backgrounded thread (`AppModel.swift:476-487`).

### 3.7 Quit path, including the Hydra merge wait

**SF, `AppDelegate.swift:38-48`.** `applicationShouldTerminate`:

```
guard let model, model.isAnyHydraMergeRunning else { return .terminateNow }
Task { @MainActor in
    let deadline = Date.now.addingTimeInterval(4 * 60)
    while model.isAnyHydraMergeRunning, Date.now < deadline {
        try? await Task.sleep(for: .milliseconds(250))
    }
    sender.reply(toApplicationShouldTerminate: true)
}
return .terminateLater
```

**SF, the exact numbers:** cap `4 * 60` seconds (240 s), poll `250` ms.
`isAnyHydraMergeRunning` is `liveRuntimes.contains { $0.isHydraMerging }`
(`AppModel+HydraMerge.swift:31`), and `liveRuntimes` is `Array(runtimes.values)`
(`AppModel.swift:437`). **A thread whose runtime was released for idleness is not in
`runtimes`, so a merge it owned cannot be seen here.** In practice `isHydraMerging` is set by
the merge itself, which holds its runtime, so the window is narrow; it is recorded because it
is a real property of the design.

### 3.8 Terminate cleanup

**SF, `AppDelegate.swift:50-55`.** `applicationWillTerminate` does exactly three things:
`model?.saveBeforeQuit()`, `TokenLedger.shared.flushLiveSpend()`, `MCPHub.terminateAll()`.

`saveBeforeQuit` (`AppModel.swift:1309-1327`), in order:
1. `guard persistenceEnabled`, then `persistenceEnabled = false` (a latch: nothing may queue
   another save after this point).
2. Cancel and drop `saveTask`.
3. Build a `Library` from `projects` and `threads`, `DiskWriter.shared.writeSynchronously(...)`
   to `Storage.libraryURL`.
4. For each live runtime: `cancelPersistence()`, `stopSession()`, then
   `writeSynchronously(snapshot, to: Storage.threadURL(runtime.threadID))` **only if
   `unsavedSnapshot()` is non-nil** (a thread merely read since its last save is skipped).
5. `DiskWriter.shared.waitForQueuedWrites()` (a `queue.sync {}` barrier, `Storage.swift:377-379`).
6. `terminals.terminateAll()` (`TerminalStore.swift:171-175`), which calls
   `LocalProcessTerminalView.terminate()` on every pty.

`MCPHub.terminateAll()` (`MCPHub.swift:29`, implementation `:196-205`) sends `SIGTERM` to every
tracked server process, then `SIGKILL` to the process group (`kill(getpgid(pid) == pid ? -pid : pid, SIGKILL)`)
for any still running, with no delay between the two loops.

**SF, gaps in the teardown (reproduce, do not fix):**
- `MCPHub.track(_:)` is called from exactly **one** site, `MCPHub.swift:370`. `MCPProbe`'s own
  child processes (`MCPProbe.swift:121`) and every provider `StdioProcess` child
  (`StdioProcess.swift:16`) are **not** in the tracked set. Provider children are stopped
  indirectly, via `runtime.stopSession()` in step 4 above, but only for runtimes still alive.
- `MCPProxy` is never stopped. Its `NWListener` and connections go with the process.
- `WorkingTreeWatch`'s FSEvents streams and their scratch index files in the temporary
  directory are not torn down at quit.

---

## 4. Event flows, both directions

### 4.1 User action to persistence and back

Take "the user sends a message". **SF**, traced through the source:

1. The composer's send calls into the thread's `ThreadRuntime` (`App/Views/Composer/ComposerView.swift`),
   reaching `startTurn` (`ThreadRuntime.swift:1158-1332`).
2. `startTurn` appends the user `TimelineItem`, sets `phase`, creates the `TurnRecord`, takes a
   start checkpoint through `Git.checkpointRef(thread:turn:phase:)` (ref namespace
   `refs/droppy-code/checkpoints/<thread uuid lowercased>/<turn>-<phase>`, phases `"start"` and
   `"end"`, `Git.swift:409-411`), then `ensureSession(directory:)` and `session.send(...)`.
3. Every timeline mutation bumps `saveRevision` and calls `scheduleSave()`.
4. `ThreadRuntime.scheduleSave()` (`:3461-3482`) has **two regimes**: idle threads re-arm an
   **800 ms** debounce; a running turn coalesces into **one 5 s** task
   (`guard saveTask == nil else { return }`). The comment states why: a running turn fires this
   after nearly every streamed event, and every save rewrites the whole thread file.
5. `saveNow()` (`:3484-3500`) cancels the timer first, then `guard saveRevision != lastSavedRevision`,
   then `DiskWriter.shared.encodeAndWrite(snapshotForPersistence(), to: Storage.threadURL(threadID))`.
6. `DiskWriter.encodeAndWrite` coalesces by URL: if a write for that URL is already pending it
   replaces the value and returns without queueing a second job (`Storage.swift:345-356`). The
   queued job encodes with `JSONEncoder.storage` and writes `options: .atomic`
   (`Storage.swift:381-388`). A failure is `NSLog`ged, not surfaced.
7. Thread **metadata** (title, status, provider session id, unread) lives on `ChatThread` in
   `AppModel.threads`, not in the document. `AppModel.updateThread` (`:705-712`) looks the
   index up in `threadIndex.positions`, applies the mutation, **returns early if the value is
   unchanged**, writes it back, and calls `AppModel.scheduleSave()` (`:1289-1297`), a flat
   **400 ms** debounce writing `library.json` through the same `DiskWriter`.
8. Back out: the `threads` `didSet` (`AppModel.swift:33-45`) diffs into `threadCells`,
   `childCells` and rebuilds `ThreadIndex` only when membership changed. Each `ObservedValue`
   cell is what a view actually observes, so one thread's title change re-renders that row and
   nothing else.

**SF, two independent debounces with different periods:** library 400 ms
(`AppModel.swift:1293`), thread document 800 ms idle / 5 s running
(`ThreadRuntime.swift:3466, :3478`). A crash therefore costs at most 5 s of a running turn.

### 4.2 Provider and background event to rendered result

**SF**, from the extract `porting/extracts/threadruntime-providerevent-dispatch.md`,
re-verified against `ThreadRuntime.swift` and `ProviderSession.swift`:

1. A session adapter parses its transport (stdio JSON lines, `Content-Length` frames, SSE, or
   HTTP) and calls `onEvent?(ProviderEvent)`.
2. `ThreadRuntime.observe(_:)` installed that closure (`:2030-2037`) and captured a
   `sessionEpoch`. The closure drops the event unless `self.sessionEpoch == epoch`. This epoch
   check, not `onEvent = nil`, is what actually silences a released session; `onEvent` is
   cleared one runloop turn later on purpose (`releaseSession`, `:2042-2056`), because a
   closing session usually speaks from inside its own handler.
3. `handle(_:)` (`:2079-2196`) dispatches all **30** cases.
4. Streaming text does not go straight to a row. `queueDelta` (`:2944-2991`) accumulates into
   `pendingDeltas[id]`, where **the first delta's kind wins for the whole pending batch**.
   Flush is immediate if `ContinuousClock.now - lastFlushAt >= flushWindow` (`.milliseconds(45)`,
   `:3032`); otherwise it schedules at `flushDelay(for:scrolling:)`:
   **45 ms** under 4,096 pending characters, **90 ms** under 32,768, **150 ms** above, and
   `max(paced, 160)` while the timeline is scrolling (`:3020-3030`).
   For Hydra heads the delay is additionally floored at
   `StreamLoad.headFlushDelayMilliseconds = max(45, streamingCount * 70)` (`:3065`).
5. `flushDeltas(scheduleSave:)` (`:3073-3094`) appends into each row's content in place via
   `appendStreamed` (`:3549-3571`), which matches exactly four `(kind, content)` pairs and
   **silently drops** any other combination, bumps `saveRevision`, and scans streamed rows for
   Hydra delegation blocks.
6. The row is a `TimelineEntry` class, compared by identity (`:46-50`), so SwiftUI rebuilds
   only that row.
7. `finishTurn(status:turnID:)` (`:2201-2387`) is a 26-step sequence. Load-bearing points:
   `finishingTurnID` is claimed **before the first `await`** so `.turnCompleted` and `.exited`
   for one turn finalize once; a non-completed turn gives command settles a **3 s** grace
   (`:2236`); the end checkpoint is captured only when a start checkpoint exists; `touchedPaths`
   and the end checkpoint are written in **one** `updateTurn`; the continuation decision is a
   four-branch ordered chain (pending send, delegated heads, Hydra reports, follow-ups).
8. `app.turnFinished(threadID:status:continues:)` (`AppModel.swift:1173-1216`) is the **only**
   trigger for the chime, the notification and the dock badge.
9. Auto-merge fires only when **nine** conjuncts all hold (`ThreadRuntime.swift:2381-2386`).

**Background event flows that are not provider events:**
- FSEvents → `WorkingTreeWatch` callback on its own queue → invalidation of the working-tree
  snapshot used for tool-row diffs (`WorkingTreeWatch.swift:139-155`).
- `MCPProxy` loopback connection → `NWConnection` handler on the `MCPProxy` queue → upstream
  `URLSession` request with a **3,600 s** timeout (`MCPProxy.swift:325`) → response streamed back.
- `UpdateChecker` timer → GitLab Releases API (project id `86415974`, `per_page=20`, no token,
  `UpdateChecker.swift:67-70`) → `updateChecker_release` default → About page.

---

## 5. Concurrency and isolation

### 5.1 Main-actor confinement is the primary model

**SF, counted:** 256 `@MainActor` annotations across 89 files; 99 `nonisolated` annotations;
40 `@Observable` declarations; 158 `Sendable` conformances.

Every stateful application object is main-actor confined: `AppModel` (`AppModel.swift:12`),
`ThreadRuntime` (`ThreadRuntime.swift:166`), `TimelineEntry` (`:6`), `AppSettings`
(`AppSettings.swift:110`), `ProviderRegistry` (`ProviderRegistry.swift:30`), `MCPStore`
(`MCPStore.swift:8`), `TerminalStore` and `TerminalSession` (`TerminalStore.swift:5, :103`),
`TokenLedger` (`TokenLedger.swift:61`), `WindowManager` (`WindowManager.swift:9`),
`AppDelegate` (`AppDelegate.swift:4`), `WindowLiveResize` (`WindowManager.swift:118`).

**SF:** `TokenLedger` documents the confinement as a correctness argument, not a convenience:
"Every provider session runs on the main actor, so this needs no lock" (`TokenLedger.swift:105-106`).
**The port must preserve that invariant or add the lock.** Every provider adapter's event
callback therefore lands on the main actor.

### 5.2 Actors: two, and only two

**SF:** `grep -n "^actor "` over the whole tree returns exactly two hits:
- `Core/Support/ThumbnailCache.swift:9` declares `actor ThumbnailCache`.
- `Services/MCP/MCPHub.swift:17` declares `actor MCPHub`, with `MCPHub.shared` at `:18`.

`MCPHub` deliberately punches two `nonisolated static` holes through its own isolation
(`track(_:)` at `:25`, `terminateAll()` at `:29`) precisely because the quit path
(`applicationWillTerminate`) cannot await an actor hop.

### 5.3 `@concurrent`: 16 sites

**SF, the complete list:**

| File | Lines |
| --- | --- |
| `App/Runtime/ThreadRuntime.swift` | 2018, 2023, 3381 |
| `App/Views/Composer/ComposerView.swift` | 1161, 1185, 1256 |
| `Core/Models/Timeline.swift` | 99 |
| `Core/Support/Shell.swift` | 35, 363 |
| `Services/MCP/MCPProbe.swift` | 52 |
| `Services/Providers/NativeFileTools.swift` | 40, 79, 92, 122 |
| `Services/Providers/OpenAICompatibleSession.swift` | 595 |
| `UI/Markdown/MarkdownView.swift` | 237 |

The two that matter most: `Shell.run` (`Shell.swift:35`) is `@concurrent`, so **every process
the app launches is launched off the main actor**; and `LoginEnvironment.read()`
(`Shell.swift:363`) likewise. `NativeFileTools`' four file operations are `@concurrent` so
model-issued file reads and writes do not block the UI.

### 5.4 Detached work

**SF:** 24 `Task.detached` sites. The structurally important ones are
`DocumentPrefetch`'s single utility worker (`Storage.swift:293`) and
`Storage.sweepOrphanedAttachments` (`Storage.swift:120`).
11 `MainActor.assumeIsolated` sites and 9 `MainActor.run` sites bridge callbacks that AppKit
or Network.framework already delivers on the main thread.

### 5.5 Locks and queues, complete inventory

**SF:**

| Primitive | Site | Guards |
| --- | --- | --- |
| `Mutex<[String: String]?>` | `Shell.swift:326` | captured login environment |
| `Mutex<Task<Void, Never>?>` | `Shell.swift:329` | single-flight environment load |
| `Mutex<URL?>` | `Shell.swift:398` | resolved git binary |
| `NSLock` | `Shell.swift:220` | `OnceContinuation` |
| `NSLock` | `Shell.swift:250` | `OutputCollector` buffers |
| `OSAllocatedUnfairLock` | `HangWatchdog.swift:34` | watchdog state |
| `NSLock` + `DispatchQueue("droppycode.stdio.write")` | `StdioProcess.swift:20-21` | stdin writes, stderr tail, exit waiters |
| `Mutex<[String: (GitFileStamp, String?)]>` | `Git.swift:509` | worktree index paths |
| `Mutex<[(String, WorkingTreeWatch)]>` + `DispatchQueue("droppy.working-tree-watch")` | `WorkingTreeWatch.swift:86, :121` | watch registry, FSEvents callbacks |
| `NSLock` x2 | `MCPHub.swift:499, :534` | `HubConversation`, `MCPLiveProcesses` |
| `NSLock` x2 | `MCPOAuth.swift:82, :101` | token store, callback state |
| `DispatchQueue("MCPOAuth.callback")` | `MCPOAuth.swift:420` | loopback callback listener |
| `NSLock` | `MCPProbe.swift:384` | probe conversation buffer |
| `Mutex<UInt16>` + `NSLock` + `DispatchQueue("MCPProxy")` | `MCPProxy.swift:14, :53-54` | port, routes, connections |
| `Mutex<State>` | `Storage.swift:260` | `DocumentPrefetch` queue and ready cache |
| `DispatchQueue("droppycode.storage.write", .utility)` + `Mutex<[URL: ...]>` | `Storage.swift:339, :343` | all library and thread writes |
| `NSLock` | `AppUpdater.swift:459` | updater progress |
| `NSLock` | `LinkTitles.swift:84` | link title cache |
| `DispatchQueue("droppycode.website-captures.film", .userInteractive)` | `WebsiteCaptures.swift:1064` | film encoding |

### 5.6 Where ordering is load-bearing (SF)

1. **Launch order** in `DroppyCodeApp.init` (§3.1): the notification guard before any post,
   `WebsiteCaptures.prepare()` before any settings or library read.
2. **`releaseSession` resets before its guard** (`ThreadRuntime.swift:2042-2052`):
   `sessionEpoch`, `sessionSignature`, `unsentSessionID`, `antiSlopFallbackSent`,
   `continuationSent` are cleared **before** `guard let old = session`, so they reset even when
   no session exists.
3. **`flushDeltas()` runs before every completion handler** (`messageCompleted`,
   `reasoningCompleted`, `toolStarted`, `toolUpdated`, `planCompleted`, `turnCompleted`,
   `exited`). Reordering loses text.
4. **`finishingTurnID` is claimed before the first suspension** in `finishTurn`, which is what
   makes `.turnCompleted` + `.exited` idempotent for one turn.
5. **`unsentSessionID` is consumed only after a successful `send`** (`:1315-1318`); a release
   discards it. The API-key branch bypasses this entirely and writes `providerSessionID`
   directly at `:1490`.
6. **`AppModel.sync`/`write` split** (`AppModel.swift:157-182`): cell writes are deliberately
   deferred out of the dictionary's exclusive-access scope, because a cell write inside
   `withAnimation` makes SwiftUI rebuild the menu commands synchronously, which reads back
   through `thread(_:)` and would trap on exclusivity.
7. **`bootstrap` ordering** (§3.5): `LoginEnvironment.load()` before `sweepHydraCopies()`, so
   git runs with the user's PATH.
8. **`saveNow` cancels its timer first** (`:3489-3490`); the comment states that leaving the
   handle standing across a skipped save would suppress every later save until the turn ended.
9. **MCP HTTP headers**: per-server headers are applied first, then the fixed `Content-Type`,
   `Accept` and `MCP-Protocol-Version` overwrite them (`MCPHub.swift:454-462`).
10. **`MCPHub.terminateAll` sends SIGTERM to all, then SIGKILL to all still running, with no
    sleep between the loops** (`MCPHub.swift:196-205`).

---

## 6. Persistence: every store

**SF, locations.** `Storage.root` is `CaptureRun.storageRoot` if a capture run, else
`<~/Library/Application Support>/<AppInfo.name>/`, created on first touch
(`Storage.swift:19-25`). `AppInfo.name` is `CFBundleDisplayName` or the literal `"Droppy Code"`
(`Text.swift:4`). A second root exists: `AppInfo.stateDirectoryName` is `.droppy-code`, or
`.droppy-code-dev` for the dev bundle id (`Text.swift:6`), under the home directory
(`Storage.swift:43-48`).

| Store | Location | Format | Write strategy | Migration |
| --- | --- | --- | --- | --- |
| Library | `<root>/library.json` | `Library` JSON (`projects`, `threads`) | `DiskWriter`, 400 ms debounce, atomic, coalesced per URL; synchronous at quit | `LegacyMigration` moves `Cody/` to `Droppy Code/` once (`LegacyMigration.swift:18-24`) |
| Thread documents | `<root>/threads/<uuid>.json` | `ThreadDocument` (7 stored properties: `items`, `turns`, `usage`, `continuation`, `followUps`, `hydraMerges`, `threadID`) | `DiskWriter`, 800 ms idle / 5 s running, atomic | on load: running tools failed, streaming flags cleared, empty reasoning dropped, em dashes cleaned, running turns set `.interrupted`, synthetic `.turnEnd` inserted for any turn missing one (`ThreadRuntime.install`, `:551-612`) |
| Attachments | `<root>/attachments/<uuid>.<ext>` | raw bytes | `copyItem` or `data.write(.atomic)` (`Storage.swift:97-110`) | orphan sweep at bootstrap, files older than one day only |
| Wallpaper | `<root>/wallpaper/wallpaper-<uuid>.<ext>` | image file | direct write (`Wallpaper.swift:128-130`) | none |
| Settings | `UserDefaults` (`.standard`, or the capture suite `iordv.droppycode.website-captures`) | plist | immediate `didSet` per property (`AppSettings.swift:188-190`) | `LegacyMigration` copies the `iordv.cody` persistent domain key by key, only where the new domain has no value (`LegacyMigration.swift:13-17`) |
| Provider secrets | macOS Keychain, service `AppInfo.name` | generic password | `SecItemUpdate` then `SecItemAdd`, `kSecAttrAccessibleWhenUnlocked` | UserDefaults fallback retained for migration (`AppSettings.swift:356-395`) |
| MCP secrets | macOS Keychain, service `AppInfo.name`, account `"MCP <server> <field>"` | generic password | same (`MCPKeychain.swift:30-50`) | none |
| MCP connections | `<root>/mcp.json` | `[MCPConnection]` | rewritten on every change (`MCPStore.save`) | none |
| MCP customs | `<root>/custom-mcp.json` | `[MCPCustomServer]` | rewritten on every change | none |
| MCP provider configs | `<root>/mcp/claude.json`, `codex.json`, `copilot.json`, `gemini-settings.json` | per-provider JSON (`mcpServers` / `mcp_servers`) | rewritten whole by `writeAll` on every store change and at `MCPStore.init` | `MCPExternalSync` one-time cleanup, tracked in `<root>/mcp/managed.json` |
| MCP proxy port | `<root>/mcp/proxy-port` | decimal `UInt16` as UTF-8 | written at first pick and on re-pick (`MCPProxy.swift:23, :31`) | re-picked when the saved port refuses to bind |
| MCP bridge | `<Application Support>/<AppInfo.name>/MCP/droppy-mcp-bridge.mjs` | ESM JavaScript | written from the embedded source (`MCPBridgeSource.swift:245`) | none |
| Token ledger, live | `<root>/token-activity-live-v2.json` | JSON | debounced task, flushed synchronously at quit (`TokenLedger.flushLiveSpend`) | v1 lived in `UserDefaults` key `droppycode.tokenActivity.liveDaily`, read once into `legacyDaily` and **left on disk** (`TokenLedger.swift:83-85`) |
| Token ledger, Codex history | `<root>/token-activity-codex-v3.json` | JSON | as above | v1 at `token-activity-codex.json` (`:281`), v2 at `token-activity-codex-v2.json` (`:292`) |
| Hydra worktrees | `~/.droppy-code/worktrees/<project>-<persona>-<8 hex>` | git detached worktrees | `git worktree add` per head (`AppModel+Hydra.swift:495-502`) | swept at bootstrap (`sweepHydraCopies`) |
| Hydra patches | `~/.droppy-code/patches/` | git patch text | written when a head's work will not land | none |
| Window frame | `UserDefaults`, autosave name `DroppyCodeMainWindow` | AppKit frame string | AppKit | none |
| Hang reports | `~/Library/Logs/Droppy Code/hang-<date>.txt` | `sample` output | on stall, 10 kept (`HangWatchdog.swift:30-32`) | none |
| Update state | `UserDefaults` keys `updateChecker_release`, `updateChecker_lastChecked`, `updateChecker_notifiedVersion`, `appUpdater_pendingRelaunchVersion` | plist | immediate | none |

**SF, the single-writer invariant:** every library and thread-document write in the app goes
through `DiskWriter.shared`. `Storage.readDocument` calls `DiskWriter.shared.waitForQueuedWrites()`
**before** reading (`Storage.swift:74`), which is what makes a released-and-reopened thread
impossible to read stale. **The port must keep that barrier.**

**SF, no schema versioning on the library or thread documents.** There is no version field and
no migration ladder; compatibility rests entirely on `Codable` optionality and on
`KeyedDecodingContainer.value(_:default:)` (`Coding.swift:3-17`).

---

## 7. Process and subprocess topology

### 7.1 What the app spawns

**SF, every `Process()` construction site in the tree:**

| Site | What it launches | Supervision |
| --- | --- | --- |
| `Core/Support/Shell.swift:45` | every one-shot command: git, forge CLIs, provider probes, `hdiutil`, `ditto`, `xcode-select`, the login shell | timeout task then `terminate(process)`; `SIGTERM`, and one second later `SIGKILL` to the process group when `getpgid(pid) == pid`, else to the pid (`Shell.swift:110-118`) |
| `Core/Support/StdioProcess.swift:16` | every long-lived provider CLI session | `terminate()` closes stdin, `SIGTERM`, then a 3 s deadline (`StdioProcess.swift:114-120`) |
| `Services/MCP/MCPHub.swift:331` | stdio MCP servers | registered in `MCPLiveProcesses` (`:370`); `terminate()` with a 1 s grace before `SIGKILL` (`:289`); all killed at quit |
| `Services/MCP/MCPProbe.swift:121` | MCP servers during a probe | 90 s deadline, or 240 s when `args` contains `mcp-remote`; 1 s grace before `SIGKILL`. **Not registered in `MCPLiveProcesses`.** |
| `Core/Support/HangWatchdog.swift:95` | `sample` against the app's own pid | bounded by `sampleSeconds = 2` |
| `App/AppUpdater.swift:383` | the detached install/relaunch shell script | intentionally outlives the app |
| `App/Captures/WebsiteCaptures.swift:167` | `git`, to build fixture repositories | capture runs only |

Plus, outside `Process`: `TerminalSession` starts a **pty** through SwiftTerm's
`LocalProcessTerminalView.startProcess(...)` (`TerminalStore.swift:40`).

### 7.2 Environment and identity

**SF.** Every child inherits `LoginEnvironment.current` unless told otherwise
(`Shell.swift:49`). That environment is captured once, by running
`<user shell> -l -i -c "printf '__DROPPY_CODE_ENVIRONMENT__'; /usr/bin/env -0; printf '__DROPPY_CODE_ENVIRONMENT__'"`
with an 8 s timeout (`Shell.swift:147-151`), parsing NUL-separated pairs between the two
markers, dropping **15** excluded keys (`Shell.swift:113-117`), then overwriting `PATH` with
`augmentedPath` (`Shell.swift:219-231`), which appends, in this order, any of
`/opt/homebrew/bin`, `/opt/homebrew/sbin`, `/usr/local/bin`, `~/.local/bin`, `~/.bun/bin`,
`~/.cargo/bin`, `~/.npm-global/bin`, `~/.volta/bin`, `/usr/bin`, `/bin`, `/usr/sbin`, `/sbin`
that are not already present.

**SF.** The user's shell is `$SHELL` if executable, else `getpwuid(getuid())->pw_shell`, else
the literal `/bin/zsh` (`Shell.swift:207-217`).

**SF, git identity.** Every git argv is exactly
`[<LoginEnvironment.git>, "-c", "core.quotepath=false", "-c", "color.ui=false"] + <caller args>`,
with `GIT_TERMINAL_PROMPT="0"` and `GIT_OPTIONAL_LOCKS="0"` forced into the environment
(`Git.swift:29-47`). The git binary is resolved once at launch: if PATH gives `/usr/bin/git`
(Apple's shim), `xcode-select -p` is run and its `usr/bin/git` is used instead
(`Shell.swift:182-191`). `Git.swift` contains **18** sites that pass an explicit timeout and
every one of them passes `300`: **12** are git `run(...)` calls (`Git.swift:169, :253, :286,
:290, :404, :448, :459, :484, :558, :566, :679, :707`) and **6** are forge-CLI
`Shell.run(tool:)` calls (`:181, :186, :191, :216, :217, :218`, covering `glab`, `gh`, `tea`).
Every other git site runs at the `120` s default (`Git.swift:35`). The extract
`porting/extracts/git-command-vectors.md` says "thirteen" while listing twelve line numbers;
the count above was re-taken from the source and is 12 + 6 = 18.

**SF, terminal environment.** A pty session gets `LoginEnvironment.current` plus
`TERM=xterm-256color`, `COLORTERM=truecolor`, `TERM_PROGRAM=Droppy Code`,
`DROPPY_CODE_PROJECT_ROOT=<directory>`, is launched as a login shell via `execName` of
`"-" + basename(shell)`, and with a project script becomes
`["-l", "-i", "-c", "<command>; exec <shell> -l"]` (`TerminalStore.swift:28-40`).

### 7.3 Network listeners the app itself runs

**SF.** Two loopback listeners:
- `MCPProxy`: `NWListener` with `requiredInterfaceType = .loopback`,
  `allowLocalEndpointReuse = true`, `requiredLocalEndpoint = 127.0.0.1:<port>`
  (`MCPProxy.swift:98-103`). Port persisted in `<root>/mcp/proxy-port`; hard fallback `48123`
  (`MCPProxy.swift:161, :172, :174`). Server URLs are `http://127.0.0.1:<port>/mcp/<serverID>`
  (`MCPProxy.swift:35-37`). Head limit 65,536 bytes, body limit 33,554,432 bytes
  (`:49-50`), upstream timeout 3,600 s (`:325`).
- `MCPOAuth`: a callback listener with a 300 s lifetime (`MCPOAuth.swift:129`), 1 MiB head cap
  (`:483`), 65,536-byte receive chunks (`:466`).

### 7.4 Process-group semantics

**SF.** Both kill paths use the same idiom:
`kill(getpgid(pid) == pid ? -pid : pid, SIGKILL)` (`Shell.swift:116`, `MCPHub.swift:203`).
That is, the whole process group is killed when the child is its own group leader, otherwise
only the child. Neither site calls `setsid` or otherwise makes the child a group leader, so
whether the group is killed depends on how the child behaved. This is a real, reproducible
behavior, not an accident to normalize away.

---

## 8. The facts that most constrain the Windows stack choice

Ordered by how hard they bite.

**C1. Liquid Glass is the app's surface, and it is macOS-delegated (SF + OD-0003).**
22 `glassEffect` / `GlassEffectContainer` sites across 18 files
(`UI/Chrome/Chrome.swift:178, :180, :243, :252` and 17 feature-view files). `Chrome.swift:219`
records the measured cost reason: a window-sized glass surface is a full-window backdrop sample
every frame. There is also one Metal shader used through SwiftUI's
`.distortionEffect(ShaderLibrary.genie(...))` (`Genie.swift:157-158`, `Genie.metal`). Owner
decision OD-0003 makes visual fidelity the primary weighted criterion, so any stack that cannot
render a custom material with backdrop sampling, blur, specular edge and interactive response
is disqualified. **This is the single largest constraint.**

**C2. The windows are AppKit, not scenes (SF, `WindowManager.swift:62-110`, PA-0002).**
The app's own `Scene` is `Settings { EmptyView() }`. Frame autosave, style mask,
`fullSizeContentView`, traffic-light geometry (`Chrome.trafficLightTop`,
`Chrome.trafficLightDiameter`, read in `RootView.swift:30, :62`), a hosting view that always
accepts first responder and suppresses key equivalents while hidden, and two Objective-C window
subclasses exist because SwiftUI under a transparent title bar misbehaves. A Windows stack must
give the port the same level of window-server control, and the traffic-light-derived layout
constants have no Windows analogue and must be re-derived as a recorded deviation.

**C3. Main-actor confinement is a correctness invariant, not a style (SF).**
256 `@MainActor` annotations; exactly two actors; `TokenLedger` explicitly skips locking on the
strength of it. Any stack whose UI thread model differs (a renderer process separate from a
logic process, say) must reintroduce the ordering guarantees listed in §5.6 by hand, and the
`sessionEpoch` / `finishingTurnID` / `unsentSessionID` rules are the ones most likely to break.

**C4. Subprocess control is deep and POSIX-shaped (SF, §7).**
`SIGPIPE` ignored process-wide, `SIGTERM`-then-`SIGKILL` with a one-second gap, process-group
kill conditioned on `getpgid(pid) == pid`, a pty for the terminal panel, a login shell run with
`-l -i -c` to harvest the environment, `/usr/bin/env -0` NUL parsing, and a colon-separated
`PATH`. None of these map one-to-one on Windows. The open questions UNK-0005 (what shell, if
any, runs model-issued commands) and UNK-0006 (Command Code's executable is literally `cmd`,
which collides with the Windows interpreter) both live here and both block PA-0009 and PA-0011.

**C5. Shell command write-classification is product behavior (SF, `Shell.swift:136-216`, PA-0011).**
Whether a model-issued command counts as a reader decides whether a working-tree snapshot is
taken around it, which decides what the timeline's changes row shows. The reader set is 52
entries plus 25 git subcommands, with special cases for `sort`, `xcodebuild`, `env` and the
version-flag tools. This is a table of Unix tool names. Porting it is a parity decision, not
plumbing.

**C6. FSEvents has no drop-in Windows equivalent with the same semantics (SF, `WorkingTreeWatch.swift:139-155`).**
The watch uses `kFSEventStreamCreateFlagFileEvents | kFSEventStreamCreateFlagNoDefer`, resolves
paths with `realpath(3)` because events name `/private/var`, writes cookie files under the git
directory to order events, keeps a scratch git index per watch, and caps the registry at 32
watches. `ReadDirectoryChangesW` differs in coalescing, in path form and in overflow behavior.

**C7. Secrets are Keychain items with `kSecAttrAccessibleWhenUnlocked` (SF, `MCPKeychain.swift:47`).**
Two separate keychain surfaces exist (provider API keys in `AppSettings`, MCP field values in
`MCPKeychain`), both keyed by service `AppInfo.name`. Both have a UserDefaults fallback path
retained for migration (`AppSettings.swift:356-395`). A Windows replacement must be equally
per-user and equally unavailable while locked, and the fallback must not silently become the
normal path.

**C8. Iconography and type are macOS-supplied and non-redistributable (UNK-0007, PA-0012, PA-0013).**
181 `Image(systemName:)` sites. SF Symbols and the macOS system font cannot ship with a Windows
build, and text metrics feed layout, so substitution is a measured change, not a swap.

**C9. Single-module compilation hides the layering (SF, §2).**
Because the four directories are one Swift module with five confirmed upward references, a
Windows solution that splits Core / Services / UI / App into separate assemblies, crates or
packages will not compile a faithful transcription without seams at `Core/Models/MCP.swift`,
`UI/Theme/Wallpaper.swift` and `UI/Markdown/MarkdownView.swift`. Decide this before the first
project file is written.

**C10. Storage is two roots, not one (SF, `Storage.swift:19-48`).**
`~/Library/Application Support/<name>/` for the library, threads, attachments, wallpaper and
MCP, and `~/.droppy-code/` for worktrees and patches. The second is a dotted home directory
that git worktrees point into. A Windows port must choose two roots and keep the split, because
`git worktree` records absolute paths.

**C11. The update feed is platform-neutral, the install path is not (SF, PA-0014, UNK-0009).**
`UpdateChecker` reads the public GitLab Releases API with no token
(`UpdateChecker.swift:66-70`), which ports unchanged. `AppUpdater` mounts a `.dmg` with
`hdiutil`, copies with `ditto`, verifies the signature and writes a detached shell script that
waits for the app to quit, swaps the bundle and relaunches (`AppUpdater.swift:286-415`). None of
that ports. UNK-0009 records that no Windows release asset exists, so the live update path stays
BLOCKED rather than faked.

**C12. Timings are specified, not emergent (SF).**
The port has to hit literal numbers, not "feel the same": flush window 45 ms; flush tiers
45/90/150 ms at 4,096 / 32,768 characters; scrolling floor 160 ms; head flush floor
`max(45, streamingCount * 70)` ms; idle save 800 ms; running save 5 s; library save 400 ms;
interrupt watchdog 8 s; finish-turn grace 3 s; idle session stop 600 s; Hydra merge sweep 300 s;
quit merge wait 240 s at 250 ms polls; update launch delay 8 s and interval 6 h; git 120 s
default and 300 s at 18 sites (12 git, 6 forge); MCP 60 s initialize, 60 s `tools/list`, 120 s `tools/call`,
90 s probe (240 s with `mcp-remote`), 3,600 s proxy upstream; shell output collection grace 3 s;
process kill gap 1 s.

**C13. The app ships shipped-but-dead state that must be reproduced as dead (SF).**
`TurnRecord.tokenSpends` is declared (`Timeline.swift:269`) and decoded (`:292`) and read once
(`HydraRunMetricsView.swift:135`), but **never assigned anywhere in the repository**. The Hydra
run-metrics view therefore always renders empty for spends. The port must reproduce the empty
render, not "fix" it. 60 such dead or unreachable items are recorded with proof in
`porting/dead_code.md`.

**C14. The pinned checkout does not compile (SF, UNRESOLVED for the port).**
Four session adapters emit `onEvent?(.tokenSpend(...))` (`AntigravitySession.swift:308`,
`CommandCodeSession.swift:350`, `CopilotSession.swift:554`, `OpenAICompatibleSession.swift:494`)
while `enum ProviderEvent` at this commit has 30 cases and no `tokenSpend`
(`ProviderSession.swift:82-118`). The commit is mid-refactor. **UNRESOLVED:** port the 30-case
enum as committed, or the 31-case enum the adapters expect. This must be decided before the
provider layer is written; it is recorded here rather than guessed.

---

## 9. Open items this document does not close

- **UNK-0010 (explicitly unresolved and isolated by owner instruction).** Deleting a custom MCP
  server runs on one click with no confirmation (`MCPSettingsPage.swift:564-566`) and also
  deletes the server's Keychain secrets (`MCPStore.swift:193-199` → `disconnect` →
  `MCPKeychain.deleteAll` at `MCPStore.swift:124`), ignoring `confirmBeforeDeleting`, which
  defaults `true` (`AppSettings.swift:702`) and is honored by six other destructive paths
  (`SettingsView.swift:851, :892`; `SidebarView.swift:192, :360, :891`). **No behavior is
  chosen here. Neither alternative is treated as approved. This document specifies the
  surrounding architecture without depending on the outcome, so it does not block other
  specification or implementation work.**
- **UNK-0005 / UNK-0006** (shell for model-issued commands; the `cmd` executable collision)
  remain open and are named in C4 above.
- **UNK-0003** (no macOS runtime) means every macOS-delegated rendering property in C1, C2 and
  C8 ships UNVERIFIED and is named individually in the final report, per OD-0001.
- **C14** (the 30-case versus 31-case `ProviderEvent`) is UNRESOLVED and needs a decision
  before provider work starts.
- Ordinary source-defined quirks not listed above are preserved for parity under the original
  contract; only consequential exceptions are escalated individually, and the "99 upstream
  defects to reproduce" record is not treated as blanket approval for destructive or
  security-sensitive behavior.

---

## 10. Evidence index

Every claim above is either cited inline with `file:line` against
`E:/Droppy/reference/droppy-code`, or derived mechanically from the tree by the counts stated
in §0, §2.2, §2.4 and §5. The seven verbatim extracts under `porting/extracts/` were used for
the `ProviderEvent` dispatch table (§4.2), the git invocation invariants (§7.2), and the MCP
paths, timeouts and header ordering (§5.6, §6, §7.3); each of those was re-checked against the
source file named in the extract before being repeated here.
