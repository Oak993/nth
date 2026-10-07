# STATE

Continuation record. Reread `porting/PORT_CONTRACT.md` before acting on this file.
This file is a navigation aid; the contract and the source are the authority.

## Current checkpoint: 2026-10-04 03:17 AST, orchestrator Claude (written by the ledger-text agent from `E:\Droppy\work\NEXT_SESSION_CLAUDE.md`, top block and log up to 2026-10-04 02:59, and from git on this date)

This section supersedes every checkpoint below it. The checkpoints below are history: the baseline is B3, not B2, and
the working branches are the ones named here.

**Baseline.** B3 (owner decision OD-0024, amendment AM-0003): Droppy Code 1.7.0 build 21, commit `52bc7926`, checkout
`E:\Droppy\reference\droppy-code-v1.7.0` (read only); record `porting/baseline.json`. Owner rulings in force:
OD-0001 to OD-0098 (`porting/owner_decisions.md`, last entry OD-0098 with its 2026-10-04 amendments). Engineering
decisions run to ED-261; the next free number is ED-262, and `ED-263` to `-10` (commit `ece699396` on
`claude/ledger-text-20261004`) are renumbered by the lead at merge.

**Branches (read from git, 2026-10-04 03:17).**
- Staging: `claude/verify-terminal-20261001`, worktree `E:\Droppy\wt\cl\terminal-verify`, head `24c4e5cfd`
  ("records IMPLEMENTED: About and update views (19) ..."). Its local copy of the pushed ref is `746158d28`; the head is
  ahead of it. It has NOT been fast-forwarded to `impl/b2`: `origin/impl/b2` is `9efdc27fa`, an ancestor of staging with
  485 commits between them (the local `impl/b2` in `E:\Droppy\wt\integration` is older, `6ae70226e`). Only a gated
  fast-forward moves `impl/b2` (quiet-PC gate with the native window tests alone, flake rule: a failure counts only if it
  fails alone twice); nothing is signed, released or published without the owner's word.
- Port workspace branch `port/windows-exact-parity` (`E:\Droppy\port`), instruction files at `9f1413954` (2026-10-03
  prompt audit: B3 everywhere, every OD in force, this file as the current gate).

**Completion** (`python E:\Droppy\work\claude-verify\completion.py <checkout>`, reproduced on `claude/ledger-text-20261004`
at 24c4e5cfd plus the ledger commits): denominator 5007 records (N/A and RETIRED excluded), IMPLEMENTED
4558 = 91.03 %, IMPLEMENTED and named by at least one `Trait("Req", id)` test 4556 = 90.99 %. The two without a Req test:
PR-COMPOSER-131 (the R8 guard test forbids crediting it until proven) and PR-WIN-025 (`WindowChromeTests.cs:22` says no
Avalonia property can prove it). `node porting/tools/progress.mjs`: source files read 441/441, requirements VERIFIED
0/5007 (no verification run yet), test methods 12277, open quarantine 0. Owner-added features (composer lights, accent
picker, Jewel knob, Strike, Customization page, voice typing, skills) count on top of this and are not in the 5007.

**Units** (worktrees `E:\Droppy\wt\u\<name>`, branches `unit/<name>`; counts are commits ahead of staging, read from git;
a recent commit time is not proof the agent is still running).
- Launched from 24c4e5cfd at 02:59 (log line): blitz-appdelegate (2 ahead, last commit 03:14), blitz-terminalpanel (2
  ahead, 03:12), blitz-windowmanager (1 ahead, 03:14). Each ends with an adversarial review, fixes, a scoped check and a
  merge into staging, one at a time.
- Also committing in the last ten minutes and not yet merged: blitz-composerview (14 ahead, 143 behind), blitz-corners
  (8, 142), blitz-effortslider (15, 143), blitz-settingsview (13, 143), blitz-tail (18, 143), blitz-timelinerows-ab (19,
  142), parity-chatview (37 ahead, 17 behind; the log says its review fix-first list is being closed).
- Merged into staging today (log): MCP settings (ED-257), SidebarView (ED-258), TimelineRows c+d (ED-260),
  integ-defects (ED-259), About and update views (ED-261), network-leaks.
- Not started or waiting: the strike unit waits for `unit/fast-strike` round 3 (still `6d173a080`, not an ancestor of
  staging; jewel-knob is merged at `a9484a49f`); Settings > Customization and voice typing wait for the build questions
  below; the paused units are listed in NEXT_SESSION_CLAUDE.md section 7 item 6.

**Decisions recorded this checkpoint (this branch).** The turn fold animates under OD-0076 (ED-263); Reduce
Motion stills only what OD-0076 added beyond the Mac (-2); PR-CHAT-107 follows the Mac exactly (-3); both About Show
buttons are the small accent glass pill (-4); internal choices for voice, Customization, the strike unit, zoom chords,
the update toast (silent) and the Terminal credit (-5 to -10). Records amended: PR-UPDATE-061 (the sweep checks the
phase once, AboutUpdateViews.swift:801) and PR-COMPOSER-129 (deviation, OD-0090). Open build questions for the owner and
the engineering ones: `E:\Droppy\work\design\open-questions-2026-10-04.md` (43 rows: 24 owner, 19 engineering).

**Next actions.**
1. Let blitz-appdelegate, -terminalpanel and -windowmanager finish; merge each into staging when its scoped check says
   merge; set the closed records IMPLEMENTED with `set_status.py` and `merge_spec.mjs`; rerun `completion.py`.
2. Merge this branch's ledger commits into staging (renumber ED-263 to -10 from ED-262; amend PR-UPDATE-025 for
   the silent update toast, the record text of PR-CHAT-104's `windows_path`, and add PR-CHAT-107's defect to
   `porting/upstream_defects.md`).
3. Build what the decisions order: the turn fold's transition (ED-263) and the About Show buttons (-4), then
   the zoom chords by character (-8) and the silent update toast (-9), each test-first.
4. Put the 24 owner questions to the owner (Q-V-11 differs from the spec's recommendation, see the file).
5. Gate staging on a quiet PC and fast-forward `impl/b2` (gated only); then fast-strike round 3, the strike unit,
   Settings > Customization, voice typing, skills, in the order of NEXT_SESSION_CLAUDE.md section 7.
6. Rules that stay: tests only through `E:\Droppy\work\claude-verify\one.sh`, `one-full.sh`, `one-names.sh`; never a bare
   `dotnet test`; never open the app "Marrow"; never write to the reference checkout; agents never push `unit/*`.

**Incomplete (GR-1, `unit/f-gr-1`, first release OD-0103).** The first-run preparation service is built and tested
(`GpuWarmup` registry; `src\Marrow.App\Port\FirstRun\FirstRunPreparation.cs`), but NOT wired to startup:
`FirstRunStartup.RunAsync` has no caller and `FirstRunStartup.Screen` is null until GR-2's Getting Ready sheet sets it
from its startup hook, so startup behaves as before (the idle fallback of ED-252 still prepares the knob). The Voice
hardware step is absent from the plan until VT-15 registers its work step with `GpuWarmup.Register`. The composer light,
slider liquid and strike programs are verified on the CPU renderer only (the liquid and strike draw into a surface the
window's GPU context makes, through `GpuWarmup.WarmTarget`; a `RenderTargetBitmap` is a CPU raster in Avalonia 12.1.2 and
is not used); a GPU check (the programs made, one cache entry per program, `WarmTarget`'s reflection on Avalonia.Skia's
`DrawingContextImpl` working on a real context) is left to the supervised native session (ED-288 (8));
`EffortSliderNativeFrameTests` now cover nine programs. Review round 2: the marker now also waits for every registered work step (a first run that did not do Voice hardware leaves the machine unprepared even after a later idle pass), and a liquid or strike draw that throws inside its operation faults the warm-up (ED-288 (14) to (16)); both are CPU-renderer verified only, a GPU fault inside `LiquidTrack` is for the native session. Review round 3: the CPU renderer's first run now leaves the machine prepared (a `warm-cpu.txt` marker naming build, adapter and driver, ED-288 (17)), so the next launch skips it; verified headless on the CPU renderer only, and on a real software-rendered PC (a VM, RDP, a broken D3D11 driver) only in the supervised native session. Review round 4: the CPU marker counts only while the app draws on the CPU renderer (a launch on the GPU needs the GPU marker), is kept per adapter, and a registered work step is bounded by `GpuWarmup.StepTimeout` and ends on cancellation (ED-288 (18) to (20)); a GPU platform whose blob cache does not install still writes no marker (unresolved until the native session).

**Incomplete (VT-4, `unit/f-vt-4`, first release OD-0103).** The orb and glow drivers are Core code with tests only (`src\Marrow.Core\Port\Voice\Orb\`, `Glow\`): nothing draws or calls them yet. `OrbMotion` and `VoiceOrbRuntime` wait for VT-8's `VoiceOrbView` (SkSL port) and the orb swap crossfade; `VoiceGlowDriver`, `VoiceGlowResolver` and `VoiceGlowFrame` wait for VT-9's `VoiceGlowView` (the layers, the stylesheet recipe `Ya`, the 60 fps cap and the half-rate governor are the view's, not the driver's). The pointer-hovered Blocked glow follows the spec table, not the page (ED-302 (3)). The seeds are generated C# literals, not an embedded `seeds.json`, because `Marrow.Core.csproj` is protected (ED-302 (2)).
**Incomplete (SA-1, `unit/f-sa-1`, first release OD-0103).** Five Settings pages exist as shells; none but Customization (CZ-4 set its `IsReady`; it is listed, searchable and opened by a request) is listed in the sidebar or found by its search, and a request for the others is ignored, until its unit sets the page class's `IsReady` const to true: Voice typing (`VoiceTypingSettingsPage.cs`, VT-13), Updates (`UpdatesSettingsPage.cs`, UE-4), Plugins (`PluginsSettingsPage.cs`) and Skills (`SkillsSettingsPage.cs`), the last two SK-15. Each shell is an empty `Decorator`. Their titles, symbols, keywords and factory lines are in `SettingsView.cs` and final (no page unit edits that file); a page that wants the chrome row implements `ISettingsPageChrome`. The settings keys `voiceOrb`, `voiceTyping`, `voiceHardware`, `voiceNotes` and `workingDots` are read and written by `AppSettings` through the CZ-13.5 write coalescer, but nothing reads them yet; the page units that drag a slider must call `AppSettings.FlushPendingWrites()` when the drag ends (the app calls it on exit). ED-303.
**Incomplete (GR-2, `unit/f-gr-2`, first release OD-0103).** The Getting ready sheet (`src\Marrow.App\Port\FirstRun\GettingReadySheet.cs`, `RootView.ShowGettingReady`, the arm and run in `AppDelegate.ApplicationDidFinishLaunching`) is built and tested (47 tests, ED-306) and IS wired to startup: it supersedes the "not wired" of GR-1's note above (the screen goes to `FirstRunStartup.RunAsync` as an argument; `FirstRunStartup.Screen` stays null and unused). Not done or unverified: (a) the pane's saturation (.6 returning to 1) is not drawn, Avalonia 12.1.2 has no saturation effect; (b) the sheet has only been rendered on the headless Skia renderer (its glass, shadow, fonts and blur) and never on the real Acrylic window with a GPU, which is for the supervised native session, as is the blur's frame time over a large detail pane; (c) the line under the title is tracked -0.435 px in the app's font (Inter) to keep the page's one line and height (ED-307 (2)), pending the owner's eye; (d) the Welcome voice question (WV-1) and the voice model download are not part of this sheet (D3 with consent: Welcome asks); (e) the first-run tour now waits for the sheet to lift.
**Incomplete (SK-8 and SK-9, `unit/integ-sk8-sk9`, first release OD-0103).** The update/remove/repair pipeline (`src\Marrow.Services\Port\Skills\Pipelines\SkillsPipeline.cs`) and the per-chat generations (`Generations\SkillGenerations.cs`) are Services code with offline tests; nothing in the app constructs them yet. SK-13 must construct ONE `SkillsPipeline` per data root as its only owner (not a second `SkillsStore` GC on the same root) and bind `SkillGenerations` to it through `IGenerationStore`; first installs, the UE-2 group wiring and the Settings rows are SK-13/SK-15/UE-4. `SkillsStore.RepairAsync` still refuses archive kinds (the pipeline's `RepairAsync` is the archive repair). SK-4's install-time spares are kept with their route but SK-9 builds and claims its own spares. Not measured: PLAN 2.1's 150 ms ready-spare launch budget on this PC (DL-20). ED-326.
**Incomplete (VT-5, `unit/f-vt-5`, first release OD-0103).** Capture is Services code (`src\Marrow.Services\Port\Voice\Capture\`: `WasapiCapture`, `CapturePipeline`, `SampleRing`, `NAudioCaptureHost`, `MicrophoneChoices`) and `src\Marrow.App\Port\Voice\MicrophoneAccess.cs` with scripted-seam tests only; nothing in the app constructs or binds them. The start-up composition (`VoiceSeams.Capture`, `VoiceSeams.Microphone`, `CaptureSeams.Host`) is VT-14's, the Microphone row that lists `MicrophoneChoices.Items` and stores `voiceTyping.mic` is VT-13's. Real capture on a real microphone, a real Windows privacy denial, a real unplug and the real WASAPI buffer cadence are not exercised (a test never opens a device); the native tests only list endpoints and read the WinRT privacy value. ED-331.
**Incomplete (VT-13, `unit/f-vt-13`, first release OD-0103).** The Voice typing page now exists and is listed (`src\Marrow.App\Port\Settings\VoiceTypingSettingsPage.cs`, `IsReady` true): the Voice typing section (Engine, Language, Microphone, the Customization link) and the privacy line, with `VoiceSettingsRow`, `VoiceTypingText` and Core's `VoiceLanguageRow`. Not done: the Speech model section and its fold for the cloud engine (VT-14: put it into `Sections`, between the section and the privacy line); the OpenAI key row is drawn but inert, with no field and no storing (VT-16); `CaptureSeams.Host` is still the fail-closed default (VT-14 binds it), so the Microphone row lists `Windows default` only in the running app until then; the page has only run in the headless session with a scripted microphone list, never on a real window with real devices or in the supervised native session. `CzMenuButton` gained `SetOptions` and a nullable checked item (ED-341).
**Incomplete (VT-14, `unit/f-vt-14`, first release OD-0103).** The Speech model section is built and tested (`src\Marrow.App\Port\Settings\{SpeechModelSection,VoiceHardwareCard,VoiceModelList,VoiceModelRow,VoicePreferenceRow}.cs`, the controls in `src\Marrow.UI\Port\Settings\Voice\`, the logic in `src\Marrow.App\Port\Voice\SpeechModelController.cs` and `src\Marrow.Core\Port\Voice\{SpeechModelList,HardwareCardText,ManualHardwareCheck,SlowPcPreference}.cs`; ED-342) and `VoiceComposition.Install()` binds the endpoint host, the privacy gate, WASAPI capture, the hardware probe and the model store at start-up. Not done: the self-test behind `Use recommended` (the seam `ISpeechSelfTest` defaults to timing nothing: VT-15), so the reason line stays empty until a time is measured; the engine, VAD and resampler bindings (VT-15); the CUDA offer (VT-17); a run on a real window with a real probe, a real microphone list and a real download (supervised native session).

**Incomplete (CZ-3, `unit/f-cz-3`, first release OD-0103).** The Customization stage is built and tested but mounted nowhere: `CustomizationStage` (the pinned block, 372 high at rest, not sticky), `ComposerPreview`, `PreviewModelSwitch` and `PreviewSession` (`src\Marrow.App\Port\Settings\`) wait for CZ-4 to put them on the hidden Customization page (`IsReady` is still false) and to bind the chrome row's Preview state control to `PreviewSession.Pick(PreviewState)`. The preview has no voice parts yet (no orb, pill, cross, glow, attachment-outside place or voice demo: CZ-7b), no compaction, pin, clip or hairline (CZ-6) and no Working dots colour (CZ-5). ED-335.
**Incomplete (SK-13, `unit/f-sk-13`, first release OD-0103).** ONE `SkillsPipeline` per data root with SK-9's generations bound to it is now composed (`src\Marrow.Services\Port\Skills\Host\SkillsHost.cs`, opened by `src\Marrow.App\Port\Skills\SkillsStartup.cs` from `Program.Main`, published on `SkillsSeams.Host`), and the terms step (`Skills\Terms\TermsStep.cs`, `TermsGate`) and the uninstall hook body (`Skills\Uninstall\SkillsUninstall.cs`, registered in `Program.RunVelopackHook`) are built and tested. Not done: (a) the built app's `--veloapp-uninstall` dispatch from a fake Velopack layout (WU-18 r3 (b)) is NOT run (no Velopack step may run): NOT CLOSED, needs the owner's word; (b) the production host binds no download, binding or licence-text seam, so nothing downloads or installs, and first installs are not in the pipeline (SK-16); (c) trusted descriptors are rebuilt after a restart only for skill sets at the catalogue's current pin: an installed tool or an older pin is dropped from state.json and reported in `SkillsHost.Unrestored`, its tree retained on every launch as an orphan (`addons\orphans.json`) until its removal or Reset, and not shown as installed or as an update (persisting descriptors is SK-16's); (d) the terms control, the row label "Under <vendor>'s terms" and the footer line (`SkillsNotices`) are not drawn: the Skills and Plugins pages are hidden shells (SK-15); every catalogue item is still Unpinned, so owed terms cannot be accepted until SK-2's pin run; (e) ED-326 (6)(d) stands. ED-339.

**Incomplete (VT-9, `unit/f-vt-9`, first release OD-0103).** The voice glow renderer (`src\Marrow.UI\Port\Voice\Glow\`: `VoiceGlowView`, `VoiceGlowPainter` and the rest; it supersedes the glow half of VT-4's note above) and `ComposerLightView.VoiceLight` (with `VoiceLightTarget` and `VoiceLightFollower` in Core) are built and tested (V-T60, V-T61, V-T62, the native GPU test) but nothing hosts them: no composer places a `VoiceGlowView` under its content, writes its `Level` or sets `Active` / `Processing`, and nothing sets `VoiceLight` from `VoiceLightTarget.For(setting, state)` (VT-10, VT-12; the Customization preview is CZ-7b), and nothing calls `VoiceGlowWarmup.Register()` (nor VT-8's `VoiceWarmup.Register()`) at start-up (VT-15 or the lead). Without a GPU the glow is one still. Measured natively on the RTX 4080: 0.56 ms a frame; not measured on a weaker GPU or through a live composer on screen; 0 B a frame is not met (6.6 KB a frame on the GPU, 3.7 to 7.4 KB on the CPU raster, all SkiaSharp's shader, image and filter objects; the port's own arithmetic and caches allocate nothing); the owner's eye on the picture is outstanding. ED-336.

**Incomplete (CZ-3, `unit/f-cz-3`, first release OD-0103).** The Customization stage is built, tested and mounted on the Customization page by CZ-4 (the chrome row's Preview state control is bound to `PreviewSession.Pick(PreviewState)`): `CustomizationStage` (the pinned block, 372 high at rest, not sticky), `ComposerPreview`, `PreviewModelSwitch` and `PreviewSession` (`src\Marrow.App\Port\Settings\`). The preview has no voice parts yet (no orb, pill, cross, glow, attachment-outside place or voice demo: CZ-7b), no compaction, pin, clip or hairline (CZ-6) and no Working dots colour (CZ-5). ED-335.
**Incomplete (CZ-4, `unit/f-cz-4`, first release OD-0103).** The Customization page (`src\Marrow.App\Port\Settings\CustomizationSettingsPage.cs`, `IsReady` true) holds the pinned block, Style, the Border beam, Uiverse light and Off sections with Reset and Advanced, and persists to every composer. Not yet on it, each a hook marked in the file: the pinned block is not sticky (no pin, compaction, release, clip, hairline or focus scroll: CZ-6; `Sticky` is the container to turn sticky), the Voice orb section (CZ-7) and the Working dots section (CZ-5) belong at the end of `Sticky`, the Speech section (CZ-11) in `Rest`; the voice demo, orb, pill, cross, glow and attachment-outside place of the preview are CZ-7b, and a Working dots touch calls `PreviewSession.WorkingDotsTouched()`. No native frame check of the page on a real window was made (CZ-T69 is CZ-6's gate). ED-337.
**Incomplete (UE-4, `unit/f-ue-4`, first release OD-0103).** The Updates page is built and ready (`UpdatesSettingsPage.IsReady` is true; `src\Marrow.App\Port\Settings\Updates*.cs`) and reads UE-5's `UpdateEverythingService`, the same orchestrator as the pill. Its "Skills and tools" and "Developer tools" sections stay empty until the units that own those items set `UpdateEverythingProduction.ExtraItems` (SK-13/SK-16 with the pipeline's `SkillUpdateItem`s, UE-3 with its dev-tool items); the look was not compared pixel by pixel with the design or seen on the real glass window; no real check, update or restart was run. ED-343.
**Incomplete (UE-5, `unit/f-ue-5`, first release OD-0103).** The update pill (place D) is built, wired into `SidebarView` (and the floating sidebar) and tested (11 Core and 39 App tests, ED-344), but with nothing to start a check except the background one: `UpdateEverythingProduction.Configure` (called from `AppDelegate`) fills `UpdateEverythingService.Shared` with the provider CLIs and Marrow, and its first check runs 15 s after launch, then every 6 h, never in a development run. Not done: the Updates page and its Check now / Update everything controls (UE-4, which should read the same service); the "Skills and tools" group (SK-8) and the developer-tool rows (UE-3); brand icons on the popover rows (initials chips for now); the design page's quick-fade for reduce motion (the pill is taken at once); the command text in the failure banner; any run on a real window (the popover is presented through `RecordingPresenter` in the headless tests, so its anchoring and the popup following the card's eased size are unverified). A finished run that needs no restart leaves the done phase ("N updated") until its popover has been closed. ED-344.

**Incomplete (VT-15, `unit/f-vt-15`, first release OD-0103).** The voice preparation service is built and tested (`src\Marrow.App\Port\Voice\VoicePreparation.cs`, `StoreModelSource.cs`, `SelfTestClip.cs`, `VoiceFeature.cs`; ED-340; V-T100 to V-T103 and the first half of V-T104) with scripted seams only; nothing in the app constructs or calls it yet. `VoicePreparation.RegisterStep()` is not called at start-up, so the Voice hardware step is still absent from the Getting ready plan; Welcome's yes and the orb click do not call `GrantConsent()` or `PrepareAsync` (WV-1, VT-12), so nothing downloads and no `voiceHardware` is stored; the `app` and Whisper.net runtime version strings and the production `StoreModelSource` composition (the store and downloader on the data root) are not supplied. `VoiceFeature.Enabled` stays false (an internal static): the start-up composition calls `VoiceFeature.SwitchOn()` once the orb is in every composer (VT-12), and V-T104's second half (the orb in every composer, V-14.3) waits for that. The self-test timing and the download are verified against fakes only: no real model, engine or network was used.
**Incomplete (VT-10, `unit/f-vt-10`, first release OD-0103).** The composer's voice views are built and tested (50 tests, ED-348) but OFF: `VoiceFeature.Enabled` (`src\Marrow.App\Port\Voice\VoiceFeature.cs`, V-2.6) is false until VT-15 (First run and switch-on) switches voice typing on, so no composer shows the orb slot, the notes layer or the announcements, and a click cannot start a session (the production seams are not bound: VT-14). LIVE for users: the attachment button's move to the composer's left, inside or outside (V-14.1, owner-ruled), eased. Not done: the voice level never reaches the orb (VoiceSession publishes no level or bands; the orb shows its states only); the text lands at Landing in one edit with no animation (VT-11's DictationLayer writes it live); no pill, cross, floating pill, language name or question (VT-12); no glow or composer-light give-way (VT-9, VT-12); only the cancelled, no-speech and blocked notes and the privacy line exist (the other errors raise none until VT-17); the session's model has no file path until the model store (VT-6, VT-7). Not seen on a real window with a GPU. The audit criterion of at most 15 % per frame cannot hold for the design's ez at 0.34 s and 0.42 s: ruled by the lead, each frame is checked against the curve (ED-348 (3), (15)).
**Incomplete (VT-11, `unit/f-vt-11`, first release OD-0103).** The live words and the audio level are built and tested (82 new tests, ED-349) but OFF with the rest of voice typing: `VoiceFeature.Enabled` stays false (see VT-10), so no composer shows the orb or the layer. Done: `VoiceSession.AudioLevel` (the raw level and bands at 60 Hz, computed on the capture's processing thread from the new `IAudioCaptureSink.OnAudioBlock` tap, allocation-free) and the orb follows it (smoothed per frame; it falls on the release); the `DictationLayer` (`src\Marrow.App\Port\Voice\DictationLayer.cs`): the words reach the draft live and are drawn over the editor (rise, tentative colour, revision with FLIP, caret, ink at landing, withdraw on cancel, right-to-left, scroll), landing is one edit of the editor (one undo step), the hand-over is pixel-identical (headless Skia). Not done: nothing was seen on a real window with a GPU; the production capture is not bound (VT-14), so no real microphone feeds the orb or the words yet; the editor has no right-to-left paragraph direction (Urdu in a left-to-right editor is one right-to-left run); a word the layout splits across lines does not rise on its own; the pill, cross, floating pill, language name and question (VT-12), the glow and the light's give-way (VT-9, VT-12), the other error notes and the countdown (VT-17) stay as VT-10 lists them. Also: an Add operation does not confirm the word before it (the design confirms it, and V-7.5 says so; ED-349 item 5 diverges on purpose: Confirm operations decide, as the transcriber produces them).

**Incomplete (CZ-4, `unit/f-cz-4`, first release OD-0103).** The Customization page (`src\Marrow.App\Port\Settings\CustomizationSettingsPage.cs`, `IsReady` true) holds the pinned block, Style, the Border beam, Uiverse light and Off sections with Reset and Advanced, and persists to every composer. Not yet on it, each a hook marked in the file: the pinned block is not sticky (no pin, compaction, release, clip, hairline or focus scroll: CZ-6; `Sticky` is the container to turn sticky), the Voice orb section (CZ-7) and the Working dots section (CZ-5) belong at the end of `Sticky`, the Speech section (CZ-11) in `Rest`; the voice demo, orb, pill, cross, glow and attachment-outside place of the preview are CZ-7b, and a Working dots touch calls `PreviewSession.WorkingDotsTouched()`. No native frame check of the page on a real window was made (CZ-T69 is CZ-6's gate). ED-337.
**Incomplete (CZ-6, `unit/f-cz-6`, first release OD-0103).** The Customization pinned block is sticky (`StickyStageController`, `CustomizationStage.PinTo/Compact/ShowHairline`, Core `StickyStage`): pin, compaction, release, clip under the block, hairline and animated focus scroll, CZ-T60 to T68 green; this supersedes the "not sticky" and "no compaction, pin, clip or hairline" words of the CZ-3 and CZ-4 lines above. Not done: CZ-T69, the native frame test of scrolling the page at 60 Hz on a real window (needs a quiet PC and the native lock; the gate's), and no native look at the compaction. The Working dots (CZ-5), Voice orb (CZ-7) and Speech (CZ-11) sections are still to come at the end of `Sticky` and in `Rest`; the sticky tests stand in for them with probes and the controller needs nothing from them. ED-346.

**Incomplete (VT-17, `unit/f-vt-17`, first release OD-0103).** Voice notes, the cap countdown and the CUDA offer are model and service code with scripted-seam tests only (`src\Marrow.App\Port\Voice\Notes\`, `Countdown\`, `Cuda\`; ED-351; notes set A byte-equal to the D-VN page): nothing draws or constructs them. The draining line is not drawn in the pill (VT-12; `CapCountdownLine.Frame()` and `Announced` are what it reads), the notes' field layer, notice-line link and link targets are not built (VT-12, VT-13, VT-14; no link is opened), and the CUDA row is not in the Speech model card (VT-14; it reads `CudaOffer.Row`). The CUDA offer never shows in a build: `FailClosedCudaCompanions` is the only binding, because the installer for NVIDIA's cuBLAS and CUDA runtime archives is not written (the pins leave open whether Marrow may unpack them: owner), so the pack's download and every end state are proven on the loopback fake only. `IProcessorFeatures` (the AVX2 check) has no production binding. The two screen-reader announcements ("30 seconds left." and "10 seconds left.") are new wording of the lead's, not the page's. The countdown's last step, comparing it on a variant still before it ships, needs the pill drawn.

**Incomplete (WV-1, `unit/f-wv-1`, first release OD-0103).** The Welcome voice question is built and tested (`src\Marrow.App\Port\FirstRun\WelcomeVoiceSection.cs`, `WelcomeVoiceWords.cs`, `src\Marrow.App\Port\Voice\VoiceSpeedQuestion.cs`; ED-352; V2 words byte-equal to wv_lib.py through `tests\fixtures\welcome-voice\v2.json`) but nothing shows it: `DetailView.VoiceQuestion` and `RootView.VoiceQuestion` are internal and null, because the start-up composition that builds `VoiceSpeedQuestion` over a `VoicePreparation` (production `StoreModelSource`, the probe, the `app` and runtime version strings) is not written, and `VoicePreparation.RegisterStep()` is still not called. Not drawn of the design's Place A: the lead marks (orb, progress ring, check), the "N of M MB" counter, the CSS look of the option rows and buttons and the path in bold. The slow answer is stored but not shown on the Voice typing page (VT-14), and an open slow question at the next orb click needs the orb's `PopoverCard` (VT-12). A failed preparation returns the question to Ask; its sentences stay diagnostics in `VoiceSpeedQuestion.Error` and are never drawn. This supersedes the VT-15 note's "Welcome's yes and the orb click do not call `GrantConsent()`": `VoiceSpeedQuestion.YesAsync` does, but nothing constructs it at start-up.

**Incomplete (SK-13, `unit/f-sk-13`, first release OD-0103).** ONE `SkillsPipeline` per data root with SK-9's generations bound to it is now composed (`src\Marrow.Services\Port\Skills\Host\SkillsHost.cs`, opened by `src\Marrow.App\Port\Skills\SkillsStartup.cs` from `Program.Main`, published on `SkillsSeams.Host`), and the terms step (`Skills\Terms\TermsStep.cs`, `TermsGate`) and the uninstall hook body (`Skills\Uninstall\SkillsUninstall.cs`, registered in `Program.RunVelopackHook`) are built and tested. Not done: (a) the built app's `--veloapp-uninstall` dispatch from a fake Velopack layout (WU-18 r3 (b)) is NOT run (no Velopack step may run): NOT CLOSED, needs the owner's word; (b) the production host binds no download, binding or licence-text seam, so nothing downloads or installs, and first installs are not in the pipeline (SK-16); (c) trusted descriptors are rebuilt after a restart only for skill sets at the catalogue's current pin: an installed tool or an older pin is dropped from state.json and reported in `SkillsHost.Unrestored`, its tree retained on every launch as an orphan (`addons\orphans.json`) until its removal or Reset, and not shown as installed or as an update (persisting descriptors is SK-16's); (d) the terms control, the row label "Under <vendor>'s terms" and the footer line (`SkillsNotices`) are not drawn: the Skills and Plugins pages are hidden shells (SK-15); every catalogue item is still Unpinned, so owed terms cannot be accepted until SK-2's pin run; (e) ED-326 (6)(d) stands. ED-NEW-f-sk-13.
**Incomplete (SK-15, `unit/f-sk-15`, first release OD-0103).** The Plugins and Skills pages are built and listed in the Settings sidebar (`PluginsSettingsPage.IsReady` and `SkillsSettingsPage.IsReady` are true; `src\Marrow.App\Port\Skills\Pages\LibraryPage.cs`, `src\Marrow.Services\Port\Skills\Pages\LibraryController.cs`, `src\Marrow.Core\Port\Skills\Pages\*`): layout D, Download all with its confirmation, the terms control, row label and footer, per-skill and per-plugin switches, "Already available through your providers", Reset. Not done: (a) nothing downloads in the app yet: the first-install seam (`IFirstInstall`) is fail closed until SK-16 binds it, no catalogue item is pinned (SK-2), no provider reports skills (SK-11/SK-16), no skill description text exists, so the pages show every state and fetch nothing; (b) the Minimize pill and its popover (OD-0099 Minimize A) are not built (Hide hides the dialog, rows show progress); (c) the "Download it yourself" manual fallback is not built (needs SK-16's manual-file install); (d) the Add button with its menu and sheet (Custom C) and the Tools page with its own Download all are other work; (e) no live look at the page was taken. ED-354.

**Incomplete (SK-15, `unit/f-sk-15`, first release OD-0103).** The Plugins and Skills pages are built and listed in the Settings sidebar (`PluginsSettingsPage.IsReady` and `SkillsSettingsPage.IsReady` are true; `src\Marrow.App\Port\Skills\Pages\LibraryPage.cs`, `src\Marrow.Services\Port\Skills\Pages\LibraryController.cs`, `src\Marrow.Core\Port\Skills\Pages\*`): layout D, Download all with its confirmation, the terms control, row label and footer, per-skill and per-plugin switches, "Already available through your providers", Reset. Not done: (a) nothing downloads in the app yet: the first-install seam (`IFirstInstall`) is fail closed until SK-16 binds it, no catalogue item is pinned (SK-2), no provider reports skills (SK-11/SK-16), no skill description text exists, so the pages show every state and fetch nothing; (b) the Minimize pill and its popover (OD-0099 Minimize A) are not built (Hide hides the dialog, rows show progress); (c) the "Download it yourself" manual fallback is not built (needs SK-16's manual-file install); (d) the Add button with its menu and sheet (Custom C) and the Tools page with its own Download all are other work; (e) no live look at the page was taken. ED-354.
**Incomplete (SK-16, `cloud/f-sk-16`, first release OD-0103).** Settings and the in-chat offers share ONE download per item: `SkillsPipeline.InstallAsync` is the first-install path (ED-326 (6)(a)), `SkillsDownloads` (`SkillsHost.Downloads`, `src\Marrow.Services\Port\Skills\Fetch\`) joins a second ask for an item to the running download, `SkillsFirstInstall` is SK-15's `IFirstInstall` bound to it and `SkillsHost.OfferDownloads` is SK-12's `ISkillsOfferDownload` bound to it; the Settings controller shows and cancels a download an offer started; `IFirstInstall.IsAvailable`/`FetchingAvailable` say whether fetching is available. `SkillsStartup.Bind` binds the production download, binding and licence-text seams (`SkillsProductionSeams.WithDownloads`); restart and resume are proved against SK-0's loopback fake (the partial stays, nothing starts after a restart, a press resumes with Range); `ManualFiles` is the Services path of "Download it yourself"; the Updates page's "Skills and tools" rows come from the pipeline's `UpdateItems()` through `SkillsUpdateRows` and refresh on a change (ED-368). Not done: SK-14's card is BLOCKED on the missing locked design, so nothing constructs a `SkillsOfferFlow` (it would block `request_tool` on an Accept no control can give) and the declined-offers writer on the pipeline is not built; the page words of "Download it yourself" (vendor-page link, folder text, "Check again") are not in the repository and no string was invented; a tool, a Python or Node set and a per-skill-pin set have no install parameters in the catalogue and fail closed with the one unavailable message; every catalogue item is still Unpinned, so the running app still refuses every item before any request; descriptors for tools after a restart are not persisted; no real download, installer, provider chat or live look was run (DL-20).
**Review (SK-16, `codex/review-sk-16` 2698c9eed, FIX FIRST).** Fixed on `cloud/f-sk-16` with `codex/f-sk-15` c7526e100 merged and the review's four REVIEW_ files and report merged unchanged (all 17 cases pass): unbound downloads stay disabled and admit nothing; no listener runs under a lock; an offer card ends at the first failing item and counts a shared need once and an installed item as done; an installed skill set survives a catalogue update (`addons\manifests`, `PipelineManifests`) and is offered as an update; Update everything reconciles its rows when idle. A tool or package set still has no persisted descriptor, and the SK-14 card, the "Download it yourself" words and every real run are still not done (ED-368 items 11 to 17).

## Current checkpoint: 2026-09-23 04:30, orchestrator Claude Opus 5.5

This section supersedes every checkpoint below it.

**Branches.** `port/windows-exact-parity` (E:\Droppy\port) carries the owner and spec decisions made
outside a wave; `impl/b2` (worktree E:\Droppy\wt\integration) is where implementation waves merge, one
unit at a time. The two are merged into each other between waves.

**Owner rulings in force:** OD-0001..OD-0017. New today: OD-0015 the app icon is the owner's finalized
Marrow icon (assets in `src/Marrow.App/Assets/`); OD-0016 that icon, animated, on loading screens (an
owner-requested deviation, ED-097); OD-0017 live resize adopts `stretch` from probe P4b and accepts the
DWM residual as a named deviation (closes OD-0013; ED-101). Engineering decisions ED-001..ED-101.

**Foundation value types added for the UI layer** (ED-091, ED-092, ED-095, ED-098): SwiftFont,
SwiftColor, SwiftAnimation / SwiftTransition / IAnimatableModifier, StyledText, all as foundation units.
Rulings for the first wave's stops: ED-093 secret store on Credential Manager, ED-094 Calendar.current,
ED-096 ICU formatter API, ED-099 chord format (scan codes, Ctrl/Alt/Shift), ED-100 CocoaError (O-25).
Port types written directly: `src/Marrow.Core/Port/CocoaError.cs`, `src/Marrow.Core/Port/Keyboard.cs`.

**Wave A** (levels -1 to 3: 80 units, 103 parts) runs as workflow run `wf_114c4e01-e0c` from
`E:\Droppy\work\impl\impl-units.js` with `E:\Droppy\work\impl\waveA.args.json`. It was interrupted once
by the account's usage limit and resumed; finished agents replay from cache. Two earlier attempts are
kept in `E:\Droppy\wt\attic-waveA1` and `attic-waveA2` (branches `attic/waveA1/*`, `attic/waveA2/*`).

**Still to add after wave A, on impl/b2:** foundation units F.LoadingMark (ED-097; dependents
ThreadTimeline, DiffInspector, AttachmentPreview, AppDelegate) and F.ResizeStretch (ED-101; dependents
the window chrome units), then wave B (levels 4 to 8) and wave C (9 to 11).

**Lessons.** Workflow subagents see the owner's latest chat message; a question or side request made
some stop. Get an explicit go-ahead before a wave; rerun only the units that stopped. The account's
usage limit can end a wave mid-flight: resume with the same args so finished agents replay.

**Measured counts** (`node porting/tools/progress.mjs` on impl/b2): source files read 436/436;
requirements IMPLEMENTED 0/4,955 and VERIFIED 0/4,955 until wave A units merge; test methods 295.

## Current checkpoint: 2026-09-22 23:45, orchestrator Claude Opus 5.5

This section supersedes every checkpoint below it. Work runs as Claude subagent workflows driven from
the orchestrator session; results land in E:\Droppy\work\... and are committed here after review.

**Baseline B2** (OD-0006 / AM-0002): `1fbf81e61e28a079508a074c01ee1830a9cc23dc` (Droppy Code 1.6.2 build 20),
checkout `E:\Droppy\reference\droppy-code-v1.6.2`, record `porting/baseline.json`.

**Gates.** A 12/12 at B2 (436/436 files read with evidence; 119 re-read through reader, critic and
adjudicator). B 8/8 at B2 (spec refreshed for the delta: 3,938 requirements, 1,051 UI records,
4,973 in scope; every area critiqued and adjudicated). C: quarantine guard passes (no open entries);
stub scan fails on the entry-point stub (expected). D: per-requirement check live
(`porting/tools/req_verify.mjs`), VERIFIED 0 so far. E, F not built.

**Owner rulings in force:** OD-0001..OD-0014 (`porting/owner_decisions.md`), sheets
`porting/decision_batch_20260922.md` and `..._20260922b.md`. Engineering decisions ED-001..ED-063.
All 12 quarantine entries resolved; all 27 pending decisions resolved; OD-0013 makes the live-resize
edge strip a blocker for the window chrome (probe P4b is measuring remedies).

**Probes.** P2 PASS, P3 PASS via app scrim, P4 animation PASS / edge FAIL (OD-0013), P5 PASS, P6 PASS,
P7 PASS with the ED-063 hook, P8 PASS, P9 font: Inter (OD-0009), P10 PASS, P11 PASS.

**In flight:** conventions judge (`E:\Droppy\work\conventions\IMPLEMENTING.md` + foundation-proto),
spec maintenance (OD-0012..0014, ED-059..063, 20 verification spec defects, ledger cleanup; staged in
`E:\Droppy\work\spec_m`), probe P4b.

**Next, in order.** (1) Merge spec maintenance, run merge_spec / gen_tasks / Gate B. (2) Flag the 9
files behind the 13 early packets for rework (`porting/tools/flag_rework.py
E:\Droppy\work\takeover\rework_findings.json`). (3) Commit `porting/IMPLEMENTING.md` and port the
foundation helpers with tests. (4) Create branch `impl/b2` in worktree `E:\Droppy\wt\integration`,
run `porting/tools/plan_units.py`, then `porting/tools/build_impl_args.py --max-level N` and launch
the implementation workflow (`E:\Droppy\work\impl\impl-units.js`) wave by wave; merge `impl/b2` back
into `port/windows-exact-parity` after each wave. (5) Gates C, D evidence, E packaging, F review.

**Incident log.** A verifier once edited the main tree instead of its worktree (reverted, nothing
committed). Subagents hit the account's weekly limit once at about 22:40 and failed; after the owner
said "Resume" agents ran again and the interrupted workflows were resumed. Writing Windows paths
through inline shell heredocs turned "\r" into a carriage return twice; files with backslashes are
now written with the Write tool only.

**Measured counts** (`node porting/tools/progress.mjs`): source files read 436/436; requirements
VERIFIED 0/4,973; IMPLEMENTED 85 (to be reset to rework for the 9 files with findings); packets DONE
12/684; Swift files with real C# 9/194. No application functionality runs yet.

## Current checkpoint: 2026-09-22, direct worker bypass

This section supersedes all historical progress and next-action entries below.
Read [HANDOFF.md](../HANDOFF.md) and [the current evidence report](reports/direct-bypass-20260922.md).

- Workspace: E:\Droppy\port, branch port/windows-exact-parity. Latest implementation commit:
  a06858e; subsequent commits save evidence only. No unreviewed worker output is in the tree.
- Pinned source: dd2b86efef9e77168f304a86e8a8411877578bbe, tree
  f761e5ef3745c4f68958ffa4014ab0b618c9d419. Original checkout remains read-only and clean.
- Latest upstream candidate: 417ab7d3676cdf258fb6b68444f0626aa6fdfbed, separate read-only checkout.
  GitLab main still matched at approximately 04:00 UTC. Whole-source reread/spec refresh is incomplete;
  existing Gate A is not evidence for the candidate revision.
- Workers remain explicitly bypassed. Director performs implementation, tests review and adversarial
  review. T-M2-071 is HIGH and has no independent GLM review in this run; do not claim otherwise.
- Gate A 12/12, Gate B 8/8, protected/reference guard and quarantine guard pass. Warnings-as-errors
  build and all 251 tests pass. Full verify remains exit 1: existing app entry stub plus incomplete
  full acceptance C/D/E/F. No new gate regression.
- 13/662 packets DONE (1.96%); M2 13/78 (16.67%), 65 M2 remain (83.33%). 85/3827 requirements
  IMPLEMENTED (2.22%); 0/3827 VERIFIED. These are literal coverage counts, not overall effort.

Completed packet / requirement IDs:

| Packet | Implemented IDs |
| --- | --- |
| T-M2-001 | PR-HYDRA-055; PR-STORE-129, PR-STORE-130 |
| T-M2-023 | PR-MCP-038 through PR-MCP-047 |
| T-M2-025 | PR-MCP-001 through PR-MCP-010 |
| T-M2-026 | PR-MCP-011 through PR-MCP-020 |
| T-M2-027 | PR-MCP-021 through PR-MCP-030 |
| T-M2-028 | PR-MCP-031 through PR-MCP-037 |
| T-M2-030 | PR-GIT-052 through PR-GIT-055 |
| T-M2-043 | PR-RUNTIME-016 through PR-RUNTIME-020 |
| T-M2-059 | PR-STORE-017, PR-STORE-018, PR-STORE-020 |
| T-M2-066 | PR-STORE-053 through PR-STORE-058 |
| T-M2-071 | PR-PROVACP-149 through PR-PROVACP-153 |
| T-M2-075 | PR-STORE-008, PR-STORE-009; PR-STORE-105 through PR-STORE-112 |
| T-M2-076 | PR-STORE-113, PR-STORE-114 |

Last commands: per-packet protected/A/B/C/D checks via run-packet-gates.mjs; Debug and Release
build/tests; git diff --check; read-only assembly metadata probe. Exact outputs, exits, environment
and test mapping are saved with the current report in reports/direct-bypass-evidence/.

Next: setup/protected checks, then M2 in source-settled dependency order. JSONValue needs exact
Foundation numeric/formatting behavior; latest Hydra and Provider need spec updates. The revised
FileEdit carrier and text helpers now exist for Timeline/Hydra integration. Preserve every approved
test, take a new regression before any correction, and use temporary MARROW_PORT_DATA_DIR.

Unchanged blockers: 12 quarantined behaviors, recorded owner choices, shipping data-folder collision,
matching macOS visual/runtime oracle unavailable, M0 desktop probes, actual provider and persistence
integration, UI and installer acceptance. The earlier two-model bake-off remains incomplete while
the owner's bypass is active. No fresh account-balance or model-cost measurement was made this run.

## Historical session log (superseded by the checkpoint above)

Historical entry last updated: 2026-09-19, session 1.

## Baseline

| Field | Value |
| --- | --- |
| Pinned source | `dd2b86efef9e77168f304a86e8a8411877578bbe` (Droppy Code `main`, 2026-09-18) |
| Pinned tree | `f761e5ef3745c4f68958ffa4014ab0b618c9d419` |
| App version | 1.6.0, build 19 |
| Reference (read-only) | `E:\Droppy\reference\droppy-code` |
| Port workspace | `E:\Droppy\port`, branch `port/windows-exact-parity` |
| Port commit | none yet (no production code written; reading gate in progress) |

## Current gate

**Gate A, complete-code reading.** Not satisfied. Production application code and stack
selection are blocked until Gate A and Gate B are satisfied (contract sections 5, 6, 9).

## Done

- Upstream reachable; default branch resolved and frozen to a full SHA.
- Full clone at the pinned SHA; no submodules, no LFS, no symlinks, no case collisions,
  `git fsck` clean, working tree matches the pinned tree exactly. Recorded in `porting/baseline.md`.
- Build inclusion rules read from `project.yml`: `DroppyCode/` is a syncedFolder (everything in
  the app target), plus `LICENSE`, `THIRD_PARTY_NOTICES.md`, `TRADEMARK.md` as bundle resources.
- Instruction sources inspected and conflicts recorded in `porting/instruction_conflicts.md`
  (C-01 through C-06). Two matter most: the prior "Marrow" port is pinned to an **older** upstream
  commit and is a **deliberately divergent product**, so it is not the specification.
- Mechanical inventory generated: **433 files, 85,392 text lines**, every file classified with
  build-inclusion evidence. `porting/source_inventory.json`.
- Reading partitioned into **63 nonoverlapping bundles**, mechanically proven to cover all 433
  files with no file in two bundles and no file split across bundles.
  `porting/assignments.json`, `porting/tools/partition.mjs`.
- Read-gate tooling written: `gen_inventory.mjs` (refuses a dirty reference),
  `bundle.mjs` (authoritative per-reader assignment), `ledger_apply.mjs` (rejects a report whose
  sha256 mismatches, whose ranges leave a gap, or whose findings are generic).

## In progress

- Reading gate workflow `wf_a6f89100-da4`: 63 reader agents, then 9 subsystem reconciliation
  critics. Reports land in `porting/reports/<BUNDLE>.json`.
- Lead's own spine reading (contract section 5 requires the lead to personally inspect the
  source for each subsystem it implements; agent reports do not close a file for the lead).

## Not started

- `porting/architecture.md`, `porting/parity_matrix.json`, `porting/ui_contract.json`,
  `porting/platform_adaptations.md` (Gate B).
- Windows architecture decision record (Gate B must complete first; contract section 9 forbids
  choosing a stack before discovery).
- Verification entrypoint and CI checks (contract section 14).
- Any production application code.

## Owner decisions taken (see `porting/owner_decisions.md`)

- **OD-0001** No macOS access at all. UNK-0003 closed as permanently blocked. macOS-delegated
  rendering ships UNVERIFIED and is named individually in the final report.
- **OD-0002** The Windows build ships as **Marrow**. UNK-0002 closed. This is an owner-approved
  deviation limited to product identity, so the final statement can be at best
  "VERIFIED WITH USER-APPROVED DEVIATIONS". It does **not** authorize adopting the separate
  existing Marrow product's features. AGPLv3 section 7 attribution stays intact.
- **OD-0003** Liquid Glass: exact look, zero guesswork. Visual fidelity becomes the primary
  weighted criterion in the stack evaluation; a stack that cannot render a custom material with
  backdrop sampling, blur, specular edge and interactive response is disqualified.
- **OD-0004** Reference material found in-tree: eight real 1320x824 macOS screenshots of the
  running app (`tour-*@2x.png`), plus the app's own capture harness
  (`App/Captures/`, 1740 lines). Approximate oracle only; never a pixel-parity claim.

## Blockers

| ID | What | Blocks |
| --- | --- | --- |
| UNK-0005 | What shell, if any, runs model-issued commands on Windows | PA-0009, PA-0011 decisions |
| UNK-0006 | Command Code's executable is literally `cmd`, colliding with the Windows interpreter | Provider launch; needs executable-identity validation |
| UNK-0007 | SF Symbols (70 distinct, 206 sites) and the macOS system font cannot be redistributed | Icon/type substitution decision; text metrics feed layout |
| UNK-0008 | App icon artwork and in-repo Droppy marks under the Marrow name | Packaging identity |

## Next concrete actions

1. Apply reader reports through `ledger_apply.mjs`; re-dispatch every bundle it rejects.
2. Resolve every critic finding by rereading the real source, not by editing a report.
3. Reconcile inventory coverage to 433/433 READ, then close Gate A with the mechanical check.
4. Derive `architecture.md`, then the parity matrix and UI contract from the ledger plus
   direct source reading.
5. Only then evaluate Windows stacks against the derived requirements.

## Commands actually run (latest)

| Command | Result |
| --- | --- |
| `git ls-remote https://gitlab.com/droppyformac1/droppy-code.git` | `HEAD` == `refs/heads/main` == `dd2b86e...` |
| `git clone ... && git checkout --force dd2b86e` | exit 0; 218 MB; tree matches |
| `git fsck --no-progress` | no output |
| `git status --porcelain` (reference) | empty |
| `node porting/tools/gen_inventory.mjs` | 433 files, 85,392 lines |
| `node porting/tools/partition.mjs` | 63 bundles, 433/433 assigned, no overlap |

## Session 1 addendum (2026-09-19, after the owner supplied getdroppycode.app)

- Reading gate: 44/63 bundles reported, **243/433 files** past the evidence gate, 0 rejections
  since the two tool fixes. Workflow `wf_a6f89100-da4` still running.
- Ledger tooling defects found and fixed (documented in the tools, standard not lowered):
  extensionless LICENSE classified as binary; 0-byte file rejected for having no ranges;
  .gitignore rejected for having no symbols. Inventory repaired in place without resetting reads.
- **Update feed resolved and it is platform-neutral (PA-0014).** `App/UpdateChecker.swift:66-71`
  reads the public GitLab Releases API, project id 86415974, per_page=20, no token. Launch delay
  8s, interval 6h, plus on-activation and on-About. Keys updateChecker_release,
  updateChecker_lastChecked, updateChecker_notifiedVersion. The feed is reused unchanged on
  Windows; only the artifact and the install mechanism differ. UNK-0009 records that no Windows
  release asset exists yet, so the live update path stays BLOCKED rather than faked.
- **Reference captures mirrored with permission:** 24 real macOS renderings, 3,084,116 bytes,
  in `porting/reference_captures/` with `PROVENANCE.tsv` (url, bytes, sha256, fetch time).
  hero is 2400x1500; most are 2080x1300. Approximate oracle only; source wins on disagreement.
- Capture geometry is a SOURCE FACT: `WebsiteCaptures.swift:658-659` wide=1320x860,
  narrow=860x560, scale from backingScaleFactor, even-rounded pixel dims, scalesToFit=false.
  The Windows port mirrors this harness so captures are like-for-like.

## Next concrete actions (updated)

1. Finish the reading gate to 433/433; re-dispatch any rejected bundle.
2. Work every reconciliation critic finding back against real source.
3. Read the capture harness crop rects per capture, to mirror states and fixtures on Windows.
4. Derive architecture.md, parity_matrix.json, ui_contract.json from ledger + direct reading.
5. Only then evaluate Windows stacks, with glass fidelity as the primary weighted criterion (OD-0003).

## Session 1, checkpoint 3: reading gate closed, verification entrypoint live

### Reading gate (contract section 5)

- Workflow `wf_a6f89100-da4` completed: **63/63 bundles, 0 failed**, 72 agents, 10.4M subagent
  tokens, 1615 tool uses, 49 minutes.
- **433/433 files past the evidence gate**, 433 ledger records, 1:1 with the inventory.
- `ledger_apply.mjs` made idempotent after re-runs produced 744 duplicate lines for 433 files;
  the ledger is now keyed by path and rewritten whole. Verified by applying all 63 reports twice.

### Reconciliation (contract section 15)

- 9 independent subsystem critics raised **182 gaps and contradictions**; 8 of 9 families returned
  NEEDS_REREAD. Findings written per family to `porting/critiques/*.md`.
- The critics independently re-verified 239 reported sha256 digests byte-for-byte and confirmed
  every `lines_total` and every range covering line 1 to the last line. The reading was genuine.
- A critic caught an error in THIS register: PA-0011 said the Shell reader set had 47 entries.
  Counted from source: **52**. The transcribed list was complete; the stated count was wrong.
  Corrected in place with a note. gitReaders 25 and excludedKeys 15 were confirmed correct.
- Repair workflow `wf_51997e5b-95e` running: 9 family adjudicators plus 7 verbatim extractions
  (ThreadRuntime ProviderEvent dispatch, Chrome design tokens, Hydra prompt corpus, markdown link
  grammar, provider argv/handshakes, git command vectors, MCP protocol and OAuth assets).
  Every critic finding is adjudicated against source; a critic may be wrong and must be shown wrong.

### Verification entrypoint (contract section 14)

- `node porting/tools/verify.mjs [--json] [--gate A..F]`. Exits nonzero on any unmet mandatory
  check. Gates B through F report NOT_IMPLEMENTED and **fail by design**, never pass by omission.
- Current: Gate A **11 PASS, 1 FAIL** (critic findings not yet adjudicated - the repair pass is
  running). Overall RESULT: NOT ACCEPTABLE, exit 1. This is the correct state.
- `node porting/tools/negative_test.mjs`: six controlled regressions injected and all six detected
  by name (unread file, tampered digest, duplicated record, partial coverage, reused generic
  finding, inventory diverging from the reference). Evidence restored byte-for-byte; gate state
  matches baseline. Exit 0.
- PA-0015 recorded: the negative test hit a real Windows file-lock defect (errno -4094 writing a
  file a just-exited child had read), which damaged evidence until the gate caught it. Restore is
  now retried with backoff. Same defect class the product will hit in git, checkpoint and store
  writes.

---

# GATE A CLOSED (2026-09-19)

`node porting/tools/verify.mjs --gate A` -> **12/12 PASS**.
Full run -> PASS=12, NOT_IMPLEMENTED=5, exit 1, RESULT: NOT ACCEPTABLE. Correct: Gates B-F are
not built and fail by design.

Port commits: `9f66288` (Gate A evidence), `ddfff51` (line-ending pin).

## What Gate A rests on

| Evidence | Value |
| --- | --- |
| Files read with evidence | 433/433 |
| Ledger records, 1:1 with inventory | 433 |
| Digests re-verified against the pinned reference | 433 |
| Critic findings raised / adjudicated | 182 / 258 |
| Verdicts | 222 CRITIC_CORRECT, 41 PARTIALLY_CORRECT, 1 CRITIC_WRONG |
| Verbatim extracts | 7 files, 520 KB |
| Upstream defects to reproduce | 99 |
| Dead/unreachable items with proof | 60 |
| Port impacts | 264 |
| Platform adaptations | 15 |
| Unknowns | 11 |
| Negative checks detected / injected | 6 / 6 |

## Corrections made to this port`s own records

1. PA-0011 said the Shell reader set had 47 entries. Source has **52**. Caught by an independent
   critic, verified by counting, corrected in place with a note.
2. The git extract said 19 sites at `timeout: 300`, then self-corrected to 12 mid-sentence.
   Counted from source: **18 total, 12 git, 6 forge**. Recorded in the defects register.
3. Upstream defect 22 implied the PKCE/CSRF token generation was cryptographically weak. It is
   not: Swift`s default RNG is CSPRNG-backed on Apple platforms. Reframed as a **port hazard**
   (the byte counts 32 and 24 and base64url are parity; the RNG choice is the port`s and must be
   a platform CSPRNG on Windows).

## Escalated rather than reproduced

- **UNK-0010**: deleting a custom MCP server runs on one click with no confirmation
  (`MCPSettingsPage.swift:564-566`) and also deletes the server`s Keychain secrets
  (`MCPStore.swift:124`, `:193-199`), ignoring `confirmBeforeDeleting` which defaults true and is
  honored by six other destructive paths. Reproducing it destroys credentials the user cannot
  recover; fixing it silently is a product change. Contract section 8 requires escalation, so this
  control is deferred until the owner decides.

## Next: Gate B

1. Derive `porting/architecture.md` from the ledger, the 7 extracts and direct reading.
2. Build `porting/parity_matrix.json`: every atomic behavior with a stable ID, source evidence,
   preconditions, action, expected result, side effects, persistence, failure and cancellation,
   Windows path, verification method, status.
3. Build `porting/ui_contract.json`: every screen, control, state, visual property, interaction,
   seeded from the Chrome token extract, the reference captures and the UI-layer ledger records.
4. Implement the Gate B checks in verify.mjs so completeness is machine-checked, not asserted.
5. Only then the Windows stack evaluation, with Liquid Glass fidelity primary (OD-0003).

---

# GATE B PASSES (2026-09-20)

node porting/tools/verify.mjs -> Gate A 12/12, Gate B 8/8, 21 checks PASS,
Gates C-F NOT_IMPLEMENTED and failing by design. Exit 1, RESULT: NOT ACCEPTABLE. Correct.

Port commits: 4cf7e30 (Gate B tooling), d0d2086 (defect triage + architecture),
9875483 (BLK-0005), 418651b (Gate B passes).

## Specification produced

| Artifact | Value |
| --- | --- |
| Parity requirements | 3827 (3742 SPECIFIED, 85 DISCOVERED) |
| UI contract records | 1029 |
| Areas | 20 |
| Controls enumerated by exact label | 1433 |
| Persisted fields | 570 |
| Background tasks | 294 |
| Architecture record | 63 KB, 29 modules, 194 source citations |
| Layering violations found vs AGENTS.md | 8 |
| Verbatim extracts | 7 files, 520 KB |

## Quarantine (owner decides individually, neither option approved)

| Entry | Criteria | Behaviour |
| --- | --- | --- |
| BLK-0001 | C1,C2,C5 | custom MCP delete: unconfirmed, destroys Keychain secrets |
| BLK-0002 | C4 | malformed custom-MCP variable discards every server, next save overwrites |
| BLK-0003 | C1,C4 | checkpoint truncation by cookie timeout OR forgery; revert then deletes work |
| BLK-0004 | C1,C4 | previewRestore path mixing; revert deletes wrong files, reports success |
| BLK-0005 | C1,C4 | legacy migration folder literal vs AppInfo.name under the rename |
| BLK-0006 | C2,C3 | API key written to defaults in PLAIN TEXT when protected storage refuses |
| BLK-0007 | C2,C3,C6 | plan limits read other apps credential files; Pi transmits the token |
| BLK-0008 | C3,C4 | read-only classifier is ALSO the auto-approval gate; 3 misclassifications |
| BLK-0009 | C1,C5 | Remove project unconfirmed, ignores confirmBeforeDeleting |
| BLK-0010 | C1,C4 | undecodable library.json starts empty, first save overwrites |
| BLK-0011 | C1 | unreadable hasCustomTitle lets auto-titler overwrite a typed name |
| BLK-0012 | C2 | credential deletion on disconnect discards its result |

27 requirements held at DISCOVERED across these. 19 marked by hand after two rounds of
over-broad automatic matching were caught and reverted (76 then 32, both wrong).

## Decisions

- 57 engineering decisions taken and recorded in porting/engineering_decisions.md
- 27 owner decisions still needed, in porting/owner_decisions_pending.md
- 78 spec questions resolved from source

## Next: Windows architecture selection (contract section 9)

Workflow wf_468d7feb-688 evaluating 8 candidate stacks against the 12 derived constraints, with
Liquid Glass fidelity as the primary weighted criterion per OD-0003, then an adversary pass
against the leaders. No stack chosen yet. No Windows code written yet.

Machine constraint found: .NET 10 RUNTIME only (no SDK), no MSVC, no cmake, no Rust. Node 24,
Python 3.14 and winget are present. Toolchain will be installed only for the chosen stack, after
the decision record, rather than speculatively.


## 2026-10-05 SK-12 checkpoint (codex/f-sk-12)

Merged SK-11a, SK-11c, SK-11b, SK-11e and SK-11f from their requested remote heads, preserving both ledger contributions and their original line endings. Implemented DL-13 offer flow/R1-R4 and S1 continuation with offline test-first evidence. See ED-367 and porting/evidence/first-release/sk-12/REPORT.md for named tests, suite counts and Windows-only gaps. Validation remains partial on Linux; no release/parity claim, package, sign, publish or installer.

## CZ-7a local Windows verification addendum, 2026-10-05

`codex/verify-cz-7a`, `E:\Droppy\wt\cx\v-cz7a`: staging 50a6197b9 merged,
CZ-5 476fd501b already contained. App/Core builds pass with 0 warnings/errors.
Final CZ slice 117/117; dots/control checks 57/57; Core non-native 4,171/4,171;
Traceability alone twice 3/3 each after correcting three inherited OD-valued
Decision traits to ED-338. Off-section test now requires the ordered Off, Voice orb
and Working dots sections specified by CZ-3.3. Full App non-native: 8,138 pass,
4 fail of 8,142; the traceability failure is repaired, and the two dots easing
failures and Terminal enumeration-timeout failure each passed alone twice.
READY under the lead's alone-twice rule; raw full-suite result is not called green.
Evidence: `porting/reports/codex-cz7a-windows.md`, ED-347 item 9.
Remaining unit boundary: CZ-7b live preview/demo, CZ-6 native/sticky checks and
CZ-8 Speech are not completed by this row unit. No native, app or installer run.

## SK-15 T7 Windows review addendum (2026-10-05)

The ready Plugins/Skills page bodies and all staging-ready Settings pages are retained. Unbound first installs now advertise CanFetch=false: Download, Retry, Download all, Continue and Retry N are disabled with the existing one-line reason; direct controller requests start nothing. Reduce Motion opts these sheets into opacity-only open/close fades; default sheets retain scale. Dialog facts/progress, changed Included ids and changed Needs transition. Required free space rounds upward. Ready in new chats is drawn for installed skill sets. The exact Sections source/page and words audit are in the tree; the two shortened prototype sentences were corrected. See ED-355.

**Incomplete / UNRESOLVED:** SK-2 has no unpacked-size field: displayed sizes and the disk-space admission estimate are provisional, explicitly labelled, not exact or certified. Your Claude account provides this cannot yet be drawn truthfully: provider reports have no synced-account origin / equivalence fact (SK-11/SK-16). First-install wiring, pins, descriptions/default-on metadata, provider reports, Minimize/pill, manual-file fallback, Add and Tools remain their original outstanding work. No app, native gate, packaging, installer or real provider/transfer was run. T7's Windows counts and isolated failure outcomes are recorded in porting/reports/codex-sk15-windows.md when the full run completes.

Merged SK-11a, SK-11c, SK-11b, SK-11e and SK-11f from their requested remote heads, preserving both ledger contributions and their original line endings. Implemented DL-13 offer flow/R1-R4 and S1 continuation with offline test-first evidence. See ED-367 and porting/evidence/first-release/sk-12/REPORT.md for named tests, suite counts and Windows-only gaps. Validation remains partial on Linux; no release/parity claim, package, sign, publish or installer.

## A11Y-FIX-1 — 2026-10-05

Isolated delivery branch `codex/a11y-fix-1`, based on staging `889c9ebd523cbfedb39d9bf06e22ba7b9988ea46`; pinned source B3 `52bc7926097cd622d08d6294e0d8b08a48c7516e`. Production fix commit `12687d0ed122d17dde18372bb949e207f3d3d9ec`. A11Y-1 review source: `8c6d331e969bf06b53bd52f61e356e88cfdd724f`.

Fixed staging findings 1, 2, 5, 6, 7, 13, 14 and 15. Pending-only findings 3, 4, 8-12 and 16-17 are excluded, with every test listed in `porting/reports/a11y-fix-1.md`. The resting look and upstream workflows remain intact; focus uses the existing app styles.

Latest validation: Linux strict analyzer-enabled solution build with `-p:EnableWindowsTargeting=true -warnaserror`, zero warnings/errors; focused Accessibility/App Traceability 24/24; UI controls/Traceability 39/39; Core and Services Traceability 3/3 each, zero skips. Broad affected App slice 328 pass / 141 fail / 469; all 141 failures reproduce in the untouched-production staging comparison (306 pass / 142 fail / 448), with no newly failing existing test. Commands, source evidence, independent semantic review, corrected capture precondition and local log/TRX paths are in the report.

Status: NOT READY for complete touched-slice acceptance. Next: resolve or execute on Windows the reproduced DLL/image/font/globalization/time-out limits, verify Narrator/UIA and native picture-picker behavior, and apply the excluded regressions when their pending surfaces land. This is not full product-parity acceptance; no unrelated numbered queue packet was marked DONE.

**Incomplete (SK-16, `cloud/f-sk-16`, first release OD-0103).** Settings and the in-chat offers share ONE download per item: `SkillsPipeline.InstallAsync` is the first-install path (ED-326 (6)(a)), `SkillsDownloads` (`SkillsHost.Downloads`, `src\Marrow.Services\Port\Skills\Fetch\`) joins a second ask for an item to the running download, `SkillsFirstInstall` is SK-15's `IFirstInstall` bound to it and `SkillsHost.OfferDownloads` is SK-12's `ISkillsOfferDownload` bound to it; the Settings controller shows and cancels a download an offer started; `IFirstInstall.IsAvailable`/`FetchingAvailable` say whether fetching is available. `SkillsStartup.Bind` binds the production download, binding and licence-text seams (`SkillsProductionSeams.WithDownloads`); restart and resume are proved against SK-0's loopback fake (the partial stays, nothing starts after a restart, a press resumes with Range); `ManualFiles` is the Services path of "Download it yourself"; the Updates page's "Skills and tools" rows come from the pipeline's `UpdateItems()` through `SkillsUpdateRows` and refresh on a change (ED-NEW-cloud-sk-16). Not done: SK-14's card is BLOCKED on the missing locked design, so nothing constructs a `SkillsOfferFlow` (it would block `request_tool` on an Accept no control can give) and the declined-offers writer on the pipeline is not built; the page words of "Download it yourself" (vendor-page link, folder text, "Check again") are not in the repository and no string was invented; a tool, a Python or Node set and a per-skill-pin set have no install parameters in the catalogue and fail closed with the one unavailable message; every catalogue item is still Unpinned, so the running app still refuses every item before any request; descriptors for tools after a restart are not persisted; no real download, installer, provider chat or live look was run (DL-20).
**Review (SK-16, `codex/review-sk-16` 2698c9eed, FIX FIRST).** Fixed on `cloud/f-sk-16` with `codex/f-sk-15` c7526e100 merged and the review's four REVIEW_ files and report merged unchanged (all 17 cases pass): unbound downloads stay disabled and admit nothing; no listener runs under a lock; an offer card ends at the first failing item and counts a shared need once and an installed item as done; an installed skill set survives a catalogue update (`addons\manifests`, `PipelineManifests`) and is offered as an update; Update everything reconciles its rows when idle. A tool or package set still has no persisted descriptor, and the SK-14 card, the "Download it yourself" words and every real run are still not done (ED-NEW-cloud-sk-16 items 11 to 17).
**Incomplete (SK-14, `cloud/f-sk-14`, first release OD-0103).** The offer card is mounted inline in the reply (end of the timeline's rows, with its caption line) and constructed per chat, with LEAD-PROPOSED WORDING for the asked sentence, why line, decline label and captions (ED-384 item 16); earlier text: mounted in the composer area and constructed per chat (see ED-384 items 10-13; earlier text: built and tested but mounted nowhere): `OfferCardProjector` (the states as a value), `OfferCardControl` over `IOfferCardSource` (the states, Reduce Motion, Return only with the card focused, never over the composer's text) and `SkillsOfferFlows.Create(runtime, host)` over the host (`SkillsHost.OfferDownloads`, `SkillsPipeline.SetDeclined`); the "Download it yourself" block (`ManualFallbackWords` from the design's `sd-page.js` fbBlock, `ManualFallbacks`, `ManualBlock`, `ManualFiles.PlaceFolder`, "Check again") is on the Settings detail of a failed item. NOT in the repository's design sources, so not written: the offer card's asked sentence, its why-needed line, the decline control and its label, the done, continuing and declined captions, the terms step inside the card and the card's geometry and place in a reply (the Offer A page is the first variants page, artifact KBarrEq1xKGAjx1KnapYnF); nothing constructs an offer for a thread, because an offer without a card to answer it would hang a model's `request_tool` (ED-384).

## Incomplete: T10 boot-loaders (2026-10-05)

NOT READY on codex/boot-loaders. Aurora loaders and standalone Boot B geometry are under validation. Locked Chrome pixel comparisons and complete-frame zero allocations remain open. The Aurora painter dependency is NOT READY at 20e15c8ffdd838665ed120f66ea2bc49928a8361; Comet startup remains active. Boot B startup integration, hidden-window warm-up, ED-236 hand-over, native boot proof and boot frame-time capture are not delivered. See ED-375 and E:\Droppy\work\scratch\handoff\codex-T10-boot-loaders.md. No task or requirement is marked DONE by this unit.

Final dependency recheck (2026-10-05): origin/codex/aurora-render advanced to d42ac5433d81c94db28adfdac76b492982dd3049, whose commit still explicitly says NOT READY. This supersedes the earlier 20e15c8ff dependency snapshot above; no renderer merge was performed. Native loader CSVs use the actual compositor cadence, not a forced 60 Hz schedule; fixed-60-fps capture and all boot capture remain open. Full App suite attempts aborted; their partial counts must not be treated as a green gate.


## Incomplete: T10 continuation (2026-10-05)

Supersedes the earlier T10 snapshot above: READY Aurora production renderer 182634dcc38c0574c560a3b682b51e1b1235abbb is merged; Boot B replaces Comet through the existing entry point. Hidden bootstrap/preparation, ED-236 cloaked hand-over, native whole-window clipping/fade and exact final DWM geometry are implemented and exercised. All 72 loader Chrome frames and all 210 READY renderer frames pass the current premultiplied per-channel bounds. Four-scale zero-allocation drawing checks and complete native loader handler zero-allocation checks pass. Real target-60-fps captures exist for B, Reduce Motion and all six loader/theme combinations.

NOT READY: normal cold Boot B frame timing fails in both isolated native runs (mean interval 23.807707 / 23.958256 ms versus unchanged 14-20 ms; maximum 421.3959 / 400.4862 ms). Reduced Motion and native geometry/readiness/hand-over checks pass. Shared compositor warm-up remains a visible stall risk; working dots are untouched. Final full-suite results and head are in E:\Droppy\work\scratch\handoff\codex-T10-boot-loaders.md. No DONE status is claimed.

T10 final verification (2026-10-05): Boot B replaces Comet on READY renderer 182634dcc; loader 72/72 and renderer 210/210 premultiplied per-channel frames pass. Full UI 3421/0, Core 4246/0, Services 4612/1, App 8360/3 (all completed, no aborts); all four failed methods pass twice alone. Loader native passes with whole-handler and painter 0 B after warm-up. Boot's native fact still fails alone twice on the final build: normal cold mean intervals 25.267788 and 23.531791 ms exceed the unchanged 20 ms bound. Functional native geometry/cloaking/readiness/hand-over pass, raw captures retained/exported. NOT READY: cold GPU warm-up scheduling/frame pacing remains incomplete; raw full App/Services gates are not green. No task marked DONE.


## Incomplete: T10 quiet-PC release cadence gate (2026-10-05)

Supersedes the prior400 ms shared-compositor blocker. Implementation aaec03d7b96aeda2be09059eaa9dc707e7c799b3 moves cold shader preparation onto an independent D3D11 worker, proves real compositor binary reuse, makes cache I/O asynchronous/durable, warms drawing/UI/bootstrap JIT before showing B, drives animation on the compositor, and skips redundant HWND work. READY Aurora182634dcc remains integrated unchanged. Native boot passes twice with all-frame loaded-PC means18.153484/18.512592 ms and maxima88.3542/88.4660 ms;38 actual compositor cache hits per cold child. Final14-20 ms cadence remains unproven and explicitly deferred to the quiet-PC release gate; residual70-73 ms compositor light passes are retained, not labeled unavoidable or entirely PC-load caused.

NOT READY: raw full App8365/2 and UI3419/2 are non-green; their failing methods pass both isolated reruns, but interaction causes are not established. Core4246/0 and Services4613/0; all full suites completed without aborts. Focused App160/0, Aurora UI48/0; loader72/72 and renderer210/210 unchanged premultiplied bounds. Loader native passes all six themes with whole-handler/painter0 B after warm-up. Both authenticated boot children pass and join. Actual real-provider/bootstrap I/O was not invoked by the native captures; scheduling is preserved and named diagnostic spans are available. The handoff at E:/Droppy/work/scratch/handoff/codex-T10-boot-loaders.md holds the final head, counts, captures and pending gate. No task marked DONE.


**Incomplete (UE-3, `codex/trial-h`, supervised trial H, first release OD-0103).** Installed-tool discovery, fixture-driven winget/Office IUpdateItems and explicit Windows bindings are connected to UE-2's shared service; this supersedes the older UE-4/UE-5 notes that developer-tool rows are wholly absent. The skills ExtraItems hook is unchanged. NOT READY: the once-only Git live attempt failed due to other agents holding Git files; Office's request returned zero but no version change/completion/current status was proven; GitHub CLI is absent on this PC and its upgrade remains fail-closed, as do other untrialled Node/Python IDs; Node's winget ID mismatch is preserved; Office direct automation is an unsupported internal API. Store/portable Python variants without supported registered/direct probes are incomplete, and inventory errors are logged without a dedicated page banner. Original installer launch history is unresolved. Trial scheduling violated the stricter one-tool-at-a-time rule because read-only Office inspection overlapped Python waits. No native app/UI upgrade run, packaged/full Gate D acceptance, mutation review, or mark.mjs DONE acceptance is claimed. ED-377; porting/first-release/reports/TRIAL-H.md.

**UE-3 lead acceptance follow-up (`codex/trial-h`, 2026-10-05).** The lead accepts trial H and its recorded inspection overlap, and owns mark.mjs; this supersedes the earlier UE-3 NOT READY verdict for Git/Office/mark acceptance. Git's recorded in-use cancellation is presented verbatim as "Git is in use by another app. Close it and try again." with Retry; no process is ever terminated. Accepted asynchronous Office requests show "Updating in the background" on the shared page/pill; ten-second later version checks preserve pending on unchanged/unavailable reads and confirm Updated only when the version moves, without relaunching. Absent GitHub CLI still has no row. Packet READY after the scoped test-first follow-up and review is recorded in the task handoff/TRIAL-H report; prior broader platform/untrialled/native/full-gate limitations remain coverage notes, not failed scoped tests or a claim that the whole port is accepted. ED-377 items 8-12.

**UE-3 final scoped follow-up evidence.** Services 146/146, App 117/117, Core 56/56 each passed twice; zero failures/skips and clean builds. Accepted Office state also survives verification cancellation and cannot be relaunched, and Git in-use wording survives unavailable post-inventory. Independent read-only static review has no remaining blocker in the lead-accepted scope. READY for the merge train; lead owns mark.mjs. ED-377 item 13; updated TRIAL-H report and codex-T13-trial-h handoff.

**W2(a) trial-H recovery-filter fix (2026-10-06, codex/fledge-trial-h-fix).** Review finding 1 is covered: malformed developer inventories follow the existing IOException recovery path while provider/self-update rows remain, strict no-handler behavior and cancellation propagate, and failed Configure preserves previous rows. The three inherited dirty files were reviewed and retained with minimal compilation/fixture repairs; full disposition and red/green evidence are in E:\Droppy\work\scratch\handoff\codex-W2-trialh-fix.md. Scoped non-native Services 157/157 and App 122/122, both twice, include Traceability/Layering and clean builds. READY for the orchestrator's independent section-3 check; local commits only, push/integration held. No full-port/native/package acceptance or mark.mjs closure is claimed. ED-378.


## SK-17 T12 local live-check addendum (2026-10-05)

Antigravity 1.2.17 discovers the Marrow-owned .agents/skills tree under a second --add-dir root. The generation layout and verified provider launch endpoint are implemented. Cursor 2026.10.01-e373342 passed print-mode discovery, but its production ACP routes did not; it remains explicit-only. Evidence: porting/first-release/reports/SK-17-live-test.md; ED-379.

**Incomplete / UNRESOLVED:** the required staging ref lacks shared SK-11/SK-16 generation preparation and lease ownership at ThreadRuntime.EnsureSession; no caller currently supplies SessionConfiguration.SkillGeneration. SK-17 has no queue entry, so mark.mjs SK-17 DONE is rejected. The endpoint and live decision do not close the full integration gate. No Marrow app, native test, installer or packaging was run.


## SK-17 T12 shared launch completion (2026-10-05)

The lead accepted Antigravity automatic discovery and Cursor explicit-only, then authorized the missing shared launch integration. ThreadRuntime.EnsureSession now supplies a verified owned generation for every Antigravity launch, including resumes/restarts/runtime-created heads. Trusted installed pins, current switches, reviewed defaults and read-only user/project clashes feed SK-9 admission. Per-turn verification and actual-child-exit lease ownership are wired. Cursor's ACP path remains explicit-only.

The lead-approved conservative admission cap is 8,192 characters; WU-11 retains actual CLI-limit measurement. The earlier missing shared caller/lease integration is CLOSED by this follow-up. Queue entries and mark.mjs remain the lead's job and do not block the worker verdict. No T13 trial-h work is present; the accidental commit remains on its preserved branch.

Validation: final focused Services 2/2 and App 14/14; Skills/Providers/Traceability Core 691, Services 2912, UI 9, App including ThreadRuntime 934; zero failures/skips and clean builds through lead wrappers in unchanged heavy slots. No owner app, real CLI in tests, native, installer or packaging run. READY for lead integration. Full evidence, test-first failures and limits: porting/first-release/reports/SK-17-launch-integration.md; ED-379. This supersedes the historical SK-17 Incomplete/UNRESOLVED paragraph above.


## SK-17 T12 Cursor automatic-skills completion (2026-10-05)

The owner requested Cursor automatic skills too. Its per-session ACP mcpServers route (A) passed one supervised unnamed live prompt: owned list_skills, actual read_skill SKILL.md read, then the unique body-only answer marker. B/C are NOT RUN under the first-passing-route instruction. This supersedes the earlier Cursor explicit-only ruling; historical plugin-directory failures remain recorded. Config bytes were restored and all disposable probe processes/folders removed without user project writes.

ThreadRuntime supplies verified Cursor generations and ACPSession appends the owned read-only MCP server for new/load sessions. The private app stdio relay avoids desktop/Velopack/data startup; its parent reads from the trusted retained generation through a current-user-only pipe. Every list/read verifies the pinned generation, explicit-only entries stay out of automatic discovery, and stop/damage after the launch gate prevents a late child. Existing user servers/argv and Antigravity's second --add-dir are retained. The approved 8,192-character cap remains interim; actual CLI measurement stays WU-11.

Validation: focused Services 16/16, App 20/20; Core Skills/Providers/Traceability 691, Services 2926, App including ThreadRuntime 940, all zero failed/skipped. Initial combined Pi fixture failures passed alone twice and the final Services rerun passed; raw results retained. Startup guard results are in the report. No owner app, real CLI in tests, native, installer or packaging. READY for lead integration. Evidence: porting/first-release/reports/SK-17-cursor-automatic.md; ED-379. Preserved accidental changes remain on their own branch and no T13 work is imported. Lead owns marking/queue work.


## SK-17 W2(b) catch-filter repair (2026-10-06)

Review finding 7 is repaired: real composer InvalidDataException now becomes ProviderError.Case.Failed in both AntigravitySkills and CursorSkills, with original localized text. SwiftTry.IsThrownError and all existing exception cases are retained. Offline production-boundary tests use a real rooted SkillsHost and corrupt pinned disclosure; both providers failed twice alone before the two-line fix. No fake replaces Prepare.

Scoped green validation through unchanged heavy.sh/lead wrappers, Native excluded: App preparation/affected launches/Traceability 25/25; Services composer/Traceability 5/5. Zero failed/skipped; clean red/green builds. READY for orchestrator independent check; local commits only, push held. Evidence: porting/first-release/reports/W2-sk17-catch-filter.md; ED-380; final head in E:\Droppy\work\scratch\handoff\codex-W2-sk17-fix.md. No new live/CLI/network/account run, owner app, native, installer or packaging. This entry only covers W2(b); orchestration owns integration.


## T22 parity tail checkpoint (2026-10-07)

Branch codex/parity-tail from origin/claude/verify-terminal-20261001 (1291606c). PR-LAUNCH-071, PR-SETTINGS-249, UI-SIDEBAR-031/032 already have production implementations; added nine source-derived proof cases and closed IMPLEMENTED with evidence. No existing tests, composer records, EffortSlider, LiquidTrack or LiquidStrike changed. ED-387/2/3; report porting/verification/parity-tail-20261007/REPORT.md. Supplemental focused Linux ICU76 9/9, five repeats 9/9; affected 24/25 (unchanged user32.dll failure); final without alias 7/9. Core non-Native aborted on kernel32.dll (2844 pass/344 fail); Services 3856 pass/1266 fail/52 skip; UI stopped stalled run (2047 pass/298 fail executed, aborted). Final App test rebuild 0 warnings/errors. Native Windows boot/bootstrap/popup visuals, whole-suite repeats and installed legal files pending. Verdict NOT READY for Windows acceptance; implementation status is not VERIFIED.


## ED-390 T18 / VT-16 follow-up (2026-10-07)

On codex/vt-16 from claude/verify-terminal-20261001 1291606c. The Services cloud adapter/key store already existed (ED-311); built its missing final-only complete-recording session path, ordered Processing SSE updates, app cloud engine/model composition and Providers-pattern protected key editor. Cloud remains off by default. Fake-only test-first commits and independent review; no real network/key/model/microphone, no existing tests changed, no plaintext key persistence/logging.

Final focused Core 660 passed; Services 57 passed/1 Windows skip; App 15 passed. New cases passed all five repeat rounds (2 Core, 8 Services, 5 App per round). Broad Linux Core exact Native-excluded filter aborted after 2697 passed/299 failed; Services completed 3863 passed/1265 failed/52 skipped, before final scheduling/metadata/lifetime follow-ups. Missing new-test metadata is repaired and final traceability checks pass. The unchanged App inert-until-VT-16 test fails (page suite 25 passed/1 failed), preserved under the owner's no-existing-test-change instruction. Windows full suites, five whole-suite repeats, Credential Manager and visual/keyboard verification remain due. Feature activation, VAD/resampler production binding and note presentation still need their existing integration work. NOT READY for complete acceptance. ED-390; porting/first-release/reports/T18-VT-16-cloud-transcription.md.


## T23 VT-15 / WV-1 integration (2026-10-07)

The existing staging preparation, speed-question state machine and V2 Welcome section are connected to production startup and every composer. Voice hardware registers before GR-1 takes its plan and VoiceFeature switches on. Downloads stay behind Welcome Yes / orb consent; an early orb click displays actual transfer progress and a deferred slow question uses the orb PopoverCard. Verified local engine, deferred VAD, resampler and settings self-test are bound. Current records and unanswered choices survive restart, probe-only records cannot bypass setup, and LiveWords controls live versus complete after-stop transcription. Cloud engine source and the key row are unchanged.

Affected fake suites: Core voice 656 pass, Services voice 473 pass / 5 Windows skips, App preparation/question/V2/integration logic 86 pass. Existing Welcome section UI tests 9 pass on Linux; composer UI and broad required suites do not establish Windows readiness. Required non-Native Core run aborts on Windows dependencies; Services run times out after recorded unrelated platform/fixture failures. Gate B 8 pass; Gate A lacks the pinned Windows reference checkout. Verdict NOT READY pending the report's Windows-run list. No real voice hardware, network download or model in added tests; no base test/assertion/tolerance changes. ED-391 through ED-394; porting/first-release/reports/T23-vt15-wv1.md. Branch codex/vt-15-wv-1 only. Existing mixed line endings retained byte-for-byte before these appended entries.


T23 final evidence supplement: f11ca528 repairs duplicate parenting caught by the new orb regression. Final App logic + Welcome/DetailView + traceability 100/100 pass at 0344a55d; Core traceability/new session regressions 6/6, Services traceability 3/3. Mounted orb reaches the Windows user32 popup boundary and needs Windows. Required broad Windows suites, pinned reference and packaged composition checks remain: NOT READY. This supplement appends bytes; prior entries remain exact.


## WL-1 engine handoff (2026-10-05)

Branch codex/f-wl-1 from requested base 0eb930d1d6d4611ac6654564ea52b3409b465666. Engine, resources, catalogue, compositor backdrop and saved selection are implemented; upstream None and custom-image behavior are retained. Scoped CPU tests: 307 new + 71 existing = 378 passing, no skipped; App builds with no warnings/errors. App Linux tests: 80 pass/95 fail, exactly the same outcomes as the requested base. Native Windows/GPU/WIC and visual/cost checks remain unverified. Final Settings library UI is the next separate unit. No queue status or whole-port parity claim. See porting/reports/codex-wl-1.md, its evidence JSON/ZIP, and the appended ED-396 section (including one later shared-options test-first-ordering exception). Only codex/f-wl-1 is authorized for push; no packaging/signing/publication/installer.


[WALLPAPER][WL-RECONCILE] 2026-10-05. Branch codex/wl-reconcile starts at codex/f-wl-1 51447ee99, reconciles cloud/f-wl-3 f8fbb675d, codex/f-wl-4 b95251ee8 and codex/f-wl-5 d0d72fb1c, and preserves every wallpaper REVIEW artifact from cloud/review-wl-4-5 4930bebd5 byte-exact. One environment/selection/renderer/view/wake/store serves the approved 90 static and 72 animated wallpapers. The locked snapshot is unchanged; an explicit hash-bound derived-page patch resolves no-brown consistently. The full oracle has 5,148 cases with no size pruning. Decision: ED-397. Evidence and readiness/counts: porting/reports/wl-reconcile.md and porting/reports/wl-reconcile/. Only codex/wl-reconcile is authorized for push; no full-port/native Windows acceptance is claimed.

[WALLPAPER][T20-WL-FIX] 2026-10-07. codex/wl-fix starts at held fa74d2afce279017e89d9670b61dce17c3765c2c. ED-398 checks in the independent CPU/live branch proof before production edits. 39 over-limit pixel occurrences in 20 frames; approximate pow/log and atan2 are causal. Perspective wiring matches the reference. Proof and next repair: porting/reports/wl-fix.md. Only codex/wl-fix is authorized for push.

[WALLPAPER][T20-WL-FIX] 2026-10-07. ED-399 applies the proven reference arithmetic after f5ae4578. Six branch regressions and 44 affected frame comparisons pass; all corrected live grid branches match the reference. Full post-fix matrix remains the next gate; Windows GPU and inherited WIC acceptance are not claimed. Only codex/wl-fix may be pushed.

[WALLPAPER][T20-WL-FIX] 2026-10-07. ED-400: READY for requested T20 raster fidelity. At tested production 24706fa7, full 5148-frame matrix, App 339 and UI 1239 cases pass with zero failures/skips; all 5104 unrelated measurements are identical. Source/fixture/assertion bytes and ledger prefixes remain intact. Native Windows GPU and inherited WIC acceptance are unverified. Final evidence: porting/reports/wl-fix.md and wl-fix/results-final.json. Proof f5ae4578 precedes fix 24706fa7. Push only codex/wl-fix without force.


### T9 Batch 10: VT16 HELD after unchanged Windows failure

Input 502b082e66730796da75f8297bf9fb8df43e3e0c; verification merge 342f24659e791616219d91e6af4df1f35328fb23. The existing cloud-row disabled-button assertion (VoiceTypingSettingsPageTests.cs:80) failed alone twice on the full group and passed alone twice after reverting only VT16 on an owned scratch branch. The VT16 merge is reverted; historical ED-390 / STATE append bytes remain intact. VT15, WL-fix and icon D remain. No assertion, tolerance, reference or production repair was made. Evidence: E:/Droppy/work/scratch/codex-merge-train/batch10/group/bisect-vt16/.
