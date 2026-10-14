# nth speed plan (row #79): Windows-first, keep Avalonia

For owner review, 2026-10-14. Plan only: nothing has been changed and no work starts before the owner approves.

**Built from:**
- My measurements on the running nth 1.0.0 (02:47-03:00).
- 8 read-only code researchers, one area each: 111 findings, every one with file:line.
- A critic who re-read the 12 most important claims in the code: 10 true, 2 partly true, 0 false.
- My own spot checks.

All findings in full: `E:\Droppy\work\scratch\lead\speed-plan-findings-2026-10-14.md`. Earlier research: `avalonia-speed-research-2026-10-14.md`.

---

## 1. What the owner decided (about 14:14)

- Keep Avalonia. No rewrite and no restart from scratch. The work should be smaller and targeted.
- Fix what we found inside our Avalonia app so it suits Windows, and stop catering to macOS.
- macOS is not in scope. A Mac version later will be built natively for the Mac, as a separate project.
- No quality drop: glass, animated wallpapers, composer lights and motion all stay.
- The goal: no hiccups, as smooth and light as any good Windows app on a strong PC.

---

## 2. What is actually wrong (plain English, most felt first)

The owner was right that the problem is not small things. It is also not that "Avalonia is slow": the four professional Avalonia apps we studied are smooth. It comes down to a few engine-level habits we copied from the Mac app, and the owner's 240 Hz screen multiplies all of them.

**1. The screen refreshes 240 times a second, and the app's motion was built for the Mac.**
- Your monitor runs at 239 Hz (recorded in our own test log). Many of nth's animations ask for a new frame on every refresh: up to 240 frames a second.
- On Windows, every frame has a fixed price, however small the change:
  - Avalonia copies the whole 2576x1408 window, about 14.5 MB.
  - Windows then re-composes a see-through window behind it.
- So one tiny glow on a button costs almost as much as redrawing the whole window, up to 240 times a second.
- Examples:
  - the composer light ignores its 60 Hz limit for 0.9 s after every key you press (`ComposerLightView.cs:114`, `AmbientMotionController.cs:69-76`);
  - every small animation also asks for a full screen update each frame (`FrameClock.cs:257-268`).

**2. Some animations never stop.**
- The stuck window button (#77). Cause found: Windows stops telling us the mouse left after a click on the button (`WindowManager.cs:495-543`).
- The Dextra ring (#78) never settles (`ArmillaPoses.cs:218`, `ArmillaEngine.cs:473`).
- About 10 more loops can run forever: the loading mark, the Tabula pulse, the working beads, the handshake capsule, the effort slider knob, and the Settings light tiles.
- Plain animation loops keep running even when the window is minimised, covered or idle (`FrameClock.cs:181-186`).

**3. The whole background is redrawn on every wallpaper frame.**
- The animated wallpaper is drawn at full size, 3.6 million pixels a frame (`WallpaperRenderer.cs:225-243`).
- On top of it, a glass layer the size of the window copies, blurs and shades the entire window on every wallpaper frame. It is on by default, because Transparency defaults to 50% (`Chrome.cs:519-524`, `AppSettings.cs:347`).
- The app tracks only one "changed area" per frame: a single box around everything that moved. So two small animations far apart make most of the window's ~44 glass pieces copy and blur again.
- The wallpaper wakes for 20 s after any mouse move or key press, which is exactly while you work.

**4. Windows settings we never tuned.**
- The GPU texture cache is left at its default of about 29 MB. The window glass alone needs about that much, so textures are thrown away and recreated: hitches (`MarrowApplication.cs:25`).
- The window is see-through (Acrylic), so Windows' compositor does extra work for it every frame. An Avalonia bug report (#20643) measured this as very costly on a 4K screen.
- nth never chooses which GPU it runs on.
- Avalonia is held at 12.1.2. Versions 12.1.3 and 12.1.4 fix text-layout churn and memory that is never released, both of which match what we see.

**5. Typing, hovering and streaming do too much work in between.**
- Each key press lays out the whole composer text about 3 times (`ComposerTextView.cs:664-825`).
- Moving the pointer over sidebar chats builds and destroys controls for every row it crosses (`SidebarView.cs:3644-3739`).
- While a reply fades in, whole paragraphs are re-laid out on every fade step.
- Provider messages (Claude, Codex...) are handled on the window's own thread with no time limit, so a burst of messages freezes the window for a moment.
- Opening and closing the sidebar re-lays out the whole chat on every frame of the slide (`RootView.cs:157-185`). About 60 listeners run on every layout pass.

**6. Lists build everything up front.**
- No list in nth shows only what is on screen. That includes the sidebar, long chats, model lists, the skills/plugins/tools libraries, the archive and the diff view.
- Settings is only hidden when you close it, so it keeps working in the background.
- Memory grows from 652 MB to 1,050 MB and is not given back. The cause is not proven yet; it needs a memory dump.

**7. Start-up.**
- On a first launch, the glass effects are prepared on the drawing thread, and that freezes the window for about 4 seconds.
- Several file reads and hardware checks run on the window's thread before the first window appears.

**8. We never measured what you feel.** This is why earlier fixes said "done" while you still saw lag.
- The earlier speed pass cut memory churn, but it measured bytes in tests with no window open.
- Its idle tests ran with the window unfocused, which pauses the animations.
- None of its 10 real-screen frame-time checks were ever run.

**What is NOT the problem (good news, read in the code):**
- The busy screens (sidebar, chat) already update in place. Most of the 132 "clear and rebuild" spots are on cold Settings pages.
- The blocking waits are almost all off the window's thread.
- The ambient effects already stop when the window is minimised or unfocused.

So no rewrite is needed. **The fixes sit in about 20 central places.**

**Was copying macOS the cause?** Partly, yes:
- **Items 1 and 5:** frame-by-frame property animation is the SwiftUI way, and the Mac caps it at 120 Hz.
- **Item 3:** on the Mac, the operating system draws the glass and blur; we redraw them inside the app.

The plan below does those the Windows way.

---

## 3. The Windows way: rules for every change

1. **Idle means zero frames.** When nothing changes, nothing draws.
2. **Frame rate by kind of motion:**
   - Direct touch (scroll, drag, press): the screen's full rate.
   - Decoration (wallpaper, lights, orb, rings): 30-60 Hz, and it stops when you are idle.
   - Progress (spinners): only while work runs, at most 60 Hz.
   - Decoration never runs at 240 Hz.
3. **Move, don't re-layout.** Animations move and fade things with transforms and opacity. They do not change widths and heights frame by frame.
4. **Draw expensive things small, and only once.**
   - Draw the wallpaper at a lower internal resolution and scale it up; it is soft, so it looks the same.
   - Blur at a lower resolution.
   - Reuse the last blur when nothing behind the glass changed.
5. **Lists only build what is on screen.**
6. **The window's thread only does window work.** Provider messages are handled in small slices; files and checks run in the background.
7. **Use Windows' own features where they look the same:**
   - an opaque window when the glass is fully solid;
   - the right GPU;
   - a properly sized GPU cache;
   - the system backdrop, which popovers already use.
8. **Every change is measured on your PC, before and after.** The look is checked by pixel tests, full-frame-rate captures for motion, and your eye on the final installer.

---

## 4. The work, in 7 phases

Sizes: S = hours, M = a day or two, L = several days. Codex does the work in time-boxed units; Claude reviews, merges and runs the gates.

### Phase 0. Speed gate first: measure what you feel (M, about 2 days)

Nothing gets fixed blind. The test tools mostly exist: we extend the existing scripted tour (`InteractionRecorder.cs`) into a scenario runner.

**How it runs:**
- Window maximised and focused, with real input, so the animations behave as they do for you.
- Release build, on your PC, with your settings: corona wallpaper, favilla light, Siri orb.

**Scenarios:**

| | What we measure |
|---|---|
| S0, S1 | Cold start and warm start |
| S2 | 60 s idle on each main screen: chat, Dextra, each Settings page |
| S3 | Typing a short draft and a 2,000-character draft |
| S4 | Streaming a long reply from a fake local provider, so no real model is called |
| S5 | Scrolling a 2,000-entry chat |
| S6 | Sidebar with 500 chats: search, scroll, drag, hover sweep |
| S7 | Opening every Settings page, plus memory |
| S8 | Popovers |
| S9 | Sidebar open/close and window resize |

**What it reports:**
- frames per second;
- p95 and p99 frame time;
- freezes over 16 ms;
- CPU of the drawing and window threads;
- GPU load of nth and of Windows' compositor;
- memory;
- garbage-collection pauses.

**Tools we add:**
- A pass/fail checker with a thresholds file, which compares every run against the last accepted run.
- A trace marker, so the installed app can be measured without a special build.
- Avalonia's FPS and changed-area overlays, in the profile build only.

**First step: a baseline run on today's 1.0.0.**

**Proposed targets** (the owner sets the final numbers):

| Scenario | Target |
|---|---|
| Idle, 25 s with no input, any screen | 0 frames a second; drawing thread under 1% of a core |
| Typing, scrolling, streaming, hovering | 95% of frames under 8 ms; no frame over 16 ms; no freeze over 50 ms |
| Warm start | Usable window within 1.5 s |
| First launch after install | Within 4 s, with no frozen window |
| Settings page or popover | Opens in under 100 ms |
| Memory | Under 600 MB after visiting every Settings page; falls back after Settings closes |

### Phase 1. Stop the waste: idle becomes zero (all S, 1-2 days)

1. **Stuck window button (#77).** In `WindowManager.cs:495-543` and `CaptionButton.cs:132-196`:
   - re-arm Windows' mouse-leave notice on every move over the title bar;
   - clear the hover when the window loses focus or the mouse capture changes;
   - check that the pointer is still over the button;
   - stop the pulse after a few cycles.
2. **Dextra ring (#78).** Put it under the same idle clock as the wallpaper. After 20 s without input it eases into its still pose, and it wakes on input. This needs an owner pick (section 6).
3. **One guard for every endless loop**, in `FrameClock.cs`. A loop that keeps asking for frames for more than 1 s is held while the window is minimised, covered or idle. This covers about 10 loops without touching them one by one.
   - A debug-build watchdog names any loop that runs more than 3 s with no input, so new ones cannot sneak in.
4. **Frame-rate caps on your 240 Hz screen:**
   - lights and orb at most 60 Hz, with no bypass while typing;
   - other UI motion at most 120 Hz;
   - scroll, drag and press stay at 240 Hz.

   Files: `AmbientMotionController.cs:62-76`, `ComposerLightView.cs:114`, `FrameClock.cs`.
5. **One shared motion clock.** The ~35 hand-made animation loops each ask for their own frame and screen update. Instead, one clock asks once per refresh and runs all of them.
6. **GPU cache to 256 MB**: one line in `MarrowApplication.cs:25`, compared against 128 MB.
7. **Opaque window when the glass is fully solid**: same pixels, less work for Windows (`WindowChrome.cs:170`).
8. **Log which GPU is used.** If it is not the strong one, choose it.

Expected: Dextra and the idle chat screen drop to near zero, and hovering and typing get much lighter. Proven by S2, S3 and S6.

### Phase 2. The background: wallpaper, window glass, lights (M, 3-4 days)

1. **Wallpaper at a lower internal resolution.** About 1 million pixels instead of 3.6 million, scaled up, with the grain and dither kept at full resolution (`WallpaperRenderer.cs:225-243`).
   - Wallpapers with hard edges keep full size, through a per-wallpaper setting.
   - Gate: an image-similarity score plus a side-by-side for your eye.
2. **Window glass over the wallpaper.**
   - Today: it copies and blurs the whole window every frame.
   - Change: the wallpaper itself produces its frosted version at low resolution, and the glass reads that (`Chrome.cs:484-489`, `GlassMaterial.cs:1276-1326`).
   - The glass rim and edge are drawn exactly as today.
3. **Glass pieces skip work when nothing behind them changed.** Each piece learns what changed under it, not just "something changed somewhere" (`GlassMaterial.cs:1331-1369`).
   - A hover glow no longer makes a 110 pt area around it redraw.
   - The double copy goes away when the whole piece changed.
4. **Composer glass.** Its flat inner buttons are drawn as plain fills instead of glass shapes, so the composer uses the cheap glass path (`Chrome.cs:306-314`). Same fill colours.
5. **Composer light.** Its readability check reads the text under it only when the text or wallpaper changes, not on every light frame, and only near the text.
6. **Wallpaper motion policy**: an owner pick (section 6).
7. **After step 3:** let the app track several separate changed areas instead of one big box. The glass must learn this first, otherwise it gets slower.
8. **Measure Windows' compositor (dwm.exe).** Only if Acrylic proves very costly: Mica for the window base (an owner pick, because the look changes).

### Phase 3. Typing, hovering, streaming, sidebar (M, 2-3 days)

1. **Composer:** one text layout per key press instead of about 3 (`ComposerTextView.cs:664-825`).
2. **Sidebar hover:** build each row's hover buttons once and fade between the two states (`SidebarView.cs:3644-3739`). Same crossfade.
3. **Streaming fade:** change the colour of the same brush instead of handing over a new one each step, so paragraphs are not re-laid out. Same colours and timing.
4. **Provider messages:**
   - handled in slices of about 4 ms per window turn;
   - neighbouring text pieces merged first;
   - one helper serves all 6-7 session types.

   The same events arrive in the same order, just never as a freeze.
5. **Narration, plan and thinking rows** update in place while streaming, instead of being rebuilt (3 places).
6. **Size animations become move and clip animations:**
   - small ones first: the updates pill, popover bars, palette heights, Ignis folds;
   - the sidebar slide and pane widths after measuring them.

   Same spring curves.
7. **Layout listeners:** one listener per window instead of about 60.
8. **Colours:** one shared brush per colour and theme instead of a new one per control. One file, about 1,281 uses.
9. **Rounded corners:** the corner shape is computed once per size, not about 3 times per change.

### Phase 4. Lists and memory (M-L, 3-4 days)

1. **One "only what's on screen" list block**, built by growing our existing `DeferredRows`.
   - First wave: model lists, the OpenRouter picker, the skills, plugins and tools libraries, the archive, downloads, and the diff view's "Show all".
   - Then the sidebar.

   The rows look and sit exactly as today.
2. **Long chats:** rows far off screen are frozen into placeholders of the same height and rebuilt as you scroll near.
3. **Settings is released when it closes**, and its observers pause while it is hidden (`SettingsOverlay.cs`, `ViewLifetime.cs`).
4. **Memory:** a memory dump to find where the 400 MB goes, then the matching fix:
   - a background clean-up after closing Settings;
   - the "conserve memory" runtime setting;
   - releasing GPU textures when Settings closes.
5. **Sidebar chat events cost the same with 10 chats or 500.** A change counter replaces re-reading every chat.

### Phase 5. Start-up (M, about 2 days)

1. **No 4-second first-launch freeze.**
   - The glass effects are prepared during "Getting ready" and in the installer's own install step, not on the first frames.
   - Later, their size is reduced.
2. **Off the window's thread before the first window:**
   - the library file is read once instead of twice;
   - the hardware checks and "is it warm" checks;
   - the boot drawing preparation;
   - the Hydra sweep.
3. **Pre-open the common popovers' windows** (model picker, effort, menus), so the first open does not stutter.
4. **Measure two .NET start-up options** that the earlier pass left unmeasured.

### Phase 6. Avalonia update (S-M, a one-day trial)

- Try 12.1.4 on a side branch.
- Keep it only if our glass self-checks pass and the numbers are equal or better. Our glass reaches into Avalonia internals, which is why we held the version.

### Later, only if the numbers still ask for it

- Run the provider engine on its own thread (L).
- Cache static rows as images (only after Phase 2; before that, it would add cost).

---

## 5. Size and time (estimates)

| Phase | Size | Time |
|---|---|---|
| 0. Speed gate and baseline | M | about 2 days |
| 1. Stop the waste | S x 8 | 1-2 days |
| 2. Wallpaper, glass, lights | M | 3-4 days |
| 3. Typing, hover, streaming, sidebar | M | 2-3 days |
| 4. Lists and memory | M-L | 3-4 days |
| 5. Start-up | M | about 2 days |
| 6. Avalonia update trial | S-M | about 1 day |

- In a line, this is about 15-16 working days.
- Some phases can run side by side: 2 with 3, and 4 with 5, because they touch different files. With that, the speed work takes about **2.5 weeks**.
- **The first big felt win** (idle at zero, lighter hover and typing) comes at the end of the **first week** (Phases 0 and 1).
- **With the other list rows** (about 1.5-2 weeks, partly side by side), the whole next release is about **3.5-4 weeks**.
- Usage: Claude's weekly limit is at 94% until 18 October.
  - Until then, Codex can start Phases 0 and 1 while Claude reviews lightly.
  - Full pace starts after the reset.
  - Codex's own usage card is checked before each unit.

---

## 6. Decisions the owner needs to make

1. **Approve this plan and its order**, or change it.
2. **The speed targets** in Phase 0: keep them or set your own.
3. **Wallpaper motion:**
   - **(A)** Today: it moves for 20 s after any input and is still at rest, so it moves exactly while you work.
   - **(B)** It holds still while you work and moves at rest.
   - **(C, recommended)** It moves gently whenever you are looking, at the cheap rate. It holds still during typing, scrolling, dragging and resizing bursts, easing down in about 0.15 s and back in about 1 s. It stops fully after 2-3 minutes without input.
4. **Dextra ring:** may it rest in its still pose after 20 s without input, and breathe again when you move?
5. **Frame caps on your 240 Hz screen:** decoration at most 60 Hz and other UI motion at most 120 Hz, with scroll, drag and press at the full 240 Hz. Agreed?
6. **Wallpaper drawn at a lower internal resolution**, but only if the side-by-side shows no difference. Agreed?
7. **Avalonia 12.1.4**, if our checks pass. Agreed?
8. **Mica instead of Acrylic** for the window base, asked only if measuring shows Windows' compositor is paying a lot. Mica does not show what is behind the window, so the look changes.
9. **Quiet time on your PC for the gate runs:** about an hour per full run. The app runs maximised and focused, and nobody types meanwhile.

---

## 7. Do we need Graphify?

**Not for this plan.**

- **What it is:** an open-source tool that turns a codebase into a map of what calls what, which AI coding tools can query instead of searching files. It supports C# and Codex. For code it runs locally, so nothing is sent out.
- **Why not now:**
  - Lag is found by measuring time per frame and reading the hot code.
  - A call map shows who calls whom, not what costs time.
  - The researchers already pinned the ~20 places that matter, with exact file and line.
- **Where it could help later:** Codex finding its way around 300,000 lines during many small fix units, with fewer tokens per lookup.
- **Costs:**
  - It is a third-party download we would install and run, which needs checking and the owner's OK under our rules.
  - It writes its own folders, skill files and optional hooks into the repository.
- **Recommendation:** skip it now. Revisit it if Codex units waste time finding code.

Sources: https://github.com/Graphify-Labs/graphify and https://graphify.com/languages/csharp

---

## 8. Risks and how we handle them

- **The look drifts:**
  - every visual change has a pixel test or an image-similarity score;
  - motion changes get full-frame-rate captures;
  - the owner judges on the final installer.
- **Glass changes are the most delicate (Phase 2):** each change gets an on/off switch for A/B, and they land one at a time.
- **Glass reaches into Avalonia internals:** keep its self-checks; they also decide the 12.1.4 trial.
- **Noisy measurements:** 3-5 repeats on a quiet PC; compare against the stored baseline, not memory.
- **One finding is not settled yet:** the memory growth. It needs the dump in Phase 4 before we pick a fix.

---

## Appendix: changes and where they live (for the Codex units)

| ID | Change | Main files | Size |
|---|---|---|---|
| G1 | Scenario runner, percentiles, per-thread CPU, frame counter, real input, maximised and focused | Marrow.App/Port/InteractionRecorder.cs (244-325, 403-463, 139-169), DesktopPerformanceProbe.cs | M |
| G2 | gate-check.py + gate-thresholds.json, baseline store, run-gate.sh (one heavy slot) | work/lean-pass/native/ | M |
| G3 | Fake local provider, 500-thread and 2,000-entry seeds | harness side (no app change) | M |
| G4 | EventSource "nth-Perf" marks; debug overlays in the profile build | new file + InteractionRecorder probe | S |
| P1 | Caption hover tracking and reset | Marrow.App WindowManager.cs:495-543, Marrow.UI/Chrome/CaptionButton.cs:132-196 | S |
| P2 | Armilla under the ambient clock, still pose at rest | Marrow.UI/Port/Armilla/ArmillaMark.cs:285-430, ArmillaPoses.cs:218, ArmillaEngine.cs:473 | S |
| P3 | Self-renewing loop guard + debug watchdog | Marrow.UI/Port/FrameClock.cs:181-186, 257-268 | S |
| P4 | Frame caps; remove the interaction bypass | AmbientMotionController.cs:62-76, ComposerLightView.cs:35-36, 114, voice orb | S |
| P5 | Shared motion clock (one frame and one commit per refresh); transforms mutated in place | FrameClock.cs, AnimatedScalar, AnimatedColor, SidebarView copies, SwiftAnimation.cs:357-380, 559-567 | M |
| P6 | SkiaOptions.MaxGpuResourceSizeBytes 256 MB (A/B 128) | Marrow.App/Port/MarrowApplication.cs:25-39 | S |
| P7 | TransparencyLevelHint None when solid | Marrow.UI/Chrome/WindowChrome.cs:147-173, RootView UpdateBackdrop | S |
| P8 | Log and choose the GPU adapter | MarrowApplication.cs (Win32PlatformOptions.GraphicsAdapterSelectionCallback), GpuProgramCache.cs:28-32 | S |
| W1 | Wallpaper low-res render (about 1 Mpx, F16) + full-res upscale and dither; per-wallpaper override | Marrow.UI/Theme/Wallpapers/WallpaperRenderer.cs:225-297, WallpaperVisualHandler.cs:80-87, WallpaperCatalogue.cs:96-107 | M |
| W2 | Window glass reads the wallpaper's own frosted texture; no full-window capture or blur | Marrow.UI/Chrome/Chrome.cs:484-541, GlassMaterial.cs:1276-1326, 1698-1729, 1811-1829 | M-L |
| W3 | Per-piece damage in Reach.Grow; skip copy and blur when untouched; skip the double copy on full cover; small dirty area for uniform-only changes | Marrow.UI/Port/GlassMaterial.cs:1287-1369, 1455-1520 | M |
| W4 | Composer flat children as plain fills; lone-piece interior path | Marrow.UI/Chrome/Chrome.cs:306-314, GlassMaterial.cs:321-331, 832-852 | M |
| W5 | Composer light: text analysis on change only, taps near the text | Marrow.UI/Port/ComposerLights/ShaderLightRenderer.cs:61-176, ShaderLightShaders*.cs | M |
| W6 | Wallpaper motion policy (owner pick) + frame-budget governor | AmbientMotionClock.cs:6, AmbientRefreshPacer.cs | S |
| W7 | Multi-rect tracker in the glass, then UseRegionDirtyRectClipping A/B | GlassMaterial.cs:1356-1359, MarrowApplication.cs | M |
| W8 | dwm.exe measurement; Mica only on an owner pick | WindowChrome.cs:170 | S |
| I1 | One composer layout per keystroke; no glyph outlines for multi-line drafts | Marrow.App/Views/Composer/ComposerTextView.cs:664-825 | S |
| I2 | Sidebar hover clusters built once, crossfaded | Marrow.App/Views/Sidebar/SidebarView.cs:3524-3739 | M |
| I3 | Streaming fade mutates one brush per run | StyledText / VeiledText (StreamVeil.cs) | M |
| I4 | Time-budgeted provider message pump (about 4 ms per turn), adjacent delta merge | ClaudeSession and the 6-7 sessions using MainActor.Spawn + await foreach; StdioProcess.cs:206-221 | S-M |
| I5 | Narration, plan and thinking rows stream in place | TimelineRows.cs (3 sites using MarkdownView.Stream) | S |
| I6 | Layout-to-transform slides (UpdatePill, popover bars, palette, Ignis folds; later sidebar and pane width) | UpdatePill.cs:42-136, UpdatePopoverContent.cs:185-220, RootView.cs:157-185, PaneWorkspace | M-L |
| I7 | One LayoutUpdated hub per window; cached glass ContainerOf | GlassMaterial attachments, ComposerView, composer light slot | M |
| I8 | Shared brushes per (colour, theme); interned Opacity() | Marrow.UI/Port/SwiftColor.cs:268-274, 370-470 | M |
| I9 | Continuous-corner clip memo | SwiftShapes.cs:400-448 (ContinuousClip) | S |
| L1 | VirtualRows primitive (grow DeferredRows), first wave of ~8 lists, then the sidebar | Marrow.UI DeferredRows.cs, EffortSlider.cs:467 ModelList, OpenRouterPicker, ProviderModelsSection, Library pages, Archive, DownloadsPopover, DiffInspector.cs:861-925 | L |
| L2 | Timeline far-row freezing | ThreadTimeline.cs:195-300 (ReportVisibility) | M |
| L3 | Release Settings on close; Track paused while not visible | SettingsOverlay.cs:233, 403; ViewLifetime.cs:16-22 | S-M |
| L4 | gcdump, then GC, Skia-purge and Backdrop-release fixes | runtimeconfig, SettingsOverlay close path, GlassMaterial Backdrop.released | M |
| L5 | Sidebar placement revision counter and per-project ThreadsIn cache | AppModel.cs:317-345, SidebarView ListPassNow | M |
| S1 | Glass variants in the awaited Getting-ready pass and the Velopack hook | GlassVariantPreparation.cs:17-24, GettingReadyLaunch, Program.cs:110 | M |
| S2 | Start-up work off the UI thread (library parse once, probes, IsWarm, boot prep, Hydra sweep) | Storage.LoadLibrary, VoiceComposition.Attach, GpuWarmup.IsWarm, AuroraBootPreparation, AuroraStartupPreparation | M |
| S3 | Pre-open the common popover windows | PopoverHost / PlainControls.Popover | S |
| S4 | A/B TieredPGO off and composite ReadyToRun (lean N4) | Marrow.App.csproj:21-29 | S |
| A1 | Avalonia 12.1.4 trial with the reflection self-checks | Directory.Packages.props:4-13, GlassMaterial.cs:1337-1349, ResizeStretch.cs:83-103, PopoverGlass.cs | S-M |
