# First-release build plan: the owner-approved additions (OD-0103)

Date: 2026-10-04. Historical planning snapshot at `7876e80da`; implementation has since advanced.
Current tracking: [T39 inventory](reviews/T39-current-inventory.md), corrected under ED-608; unit details below retain their original date.

Authority: `porting/PORT_CONTRACT.md` > amendments > `porting/owner_decisions.md` > the specs' EDs. Every unit follows
`porting/IMPLEMENTING.md`: tests first, adversarial review, then code, in its own worktree under `E:\Droppy\wt\u\`.
Tests run through `E:\Droppy\work\claude-verify\one*.sh` only. No test may use the network, a real microphone, a real
CLI, a model download, the real `%LOCALAPPDATA%`, or the owner's installed apps; every seam fails closed.

Labels: **SF** source fact (file or line named), **ED** engineering decision proposed here (the lead numbers it at
merge), **UN** unresolved (the unit or person that settles it is named), **OWNER** needs the owner.

What OD-0103 puts in the release:
- voice typing (OD-0091, OD-0092, OD-0097 with amendments, and the OD-0099 picks);
- Getting Ready and the Welcome voice question (OD-0096, the OD-0098 amendments);
- Customization (OD-0085, OD-0089, and the Look Picks of OD-0099);
- update everything (OD-0100);
- skills downloads (the OD-0099 amendments, including the latest one, `7876e80da`);
- the earlier rulings in force: OD-0075 liquid at every effort, OD-0093 strike, OD-0102 corners.

---

## 1. Per feature: what staging has, what is left

### 1.1 Voice typing

Governing rulings:
- OD-0086 and OD-0091 with all six amendments.
- OD-0092 with its amendment.
- OD-0097 with its amendment.
- OD-0099's picks: Q-V-4 A, Q-V-6 B, Q-V-7 A, Q-V-8 A, Q-V-9 C, Q-V-11 C, Q-V-13 B, Q-V-19 B, Q-CZ-1 A.
- OD-0099's amendment on Q-V-15: the cloud engine stays, off by default, using gpt-transcribe.
- OD-0098's amendment on Q-V-1 C: the app asks the user on a slow PC.

Spec: `E:\Droppy\work\design\build-specs\voice\SPEC.md`. Its V0-V8 phases and V-T tests are reused below.

**Built on staging: nothing** (SF: no Whisper, NAudio, WASAPI, orb, pill, glow, catalogue, probe or page code in
`src`; `Directory.Packages.props` pins none of the voice packages).

Pieces that already exist and are reused:
- the composer light (ED-251: `ComposerLightView`, `ComposerLightSlot`), which gains the "light while you speak" level;
- `GpuWarmup` and `GpuProgramCache` (ED-252), which compile the orb programs;
- `PopoverCard` (OD-0095 arrow tip), used by the language question;
- `SecretStore` and `CredentialManagerBackend`, which hold the OpenAI key;
- `JewelOrb.cs`, which already ports Liquid Orb Editor's glass shell.

**Left: all of it.** The OD-0099 picks change these spec sections, and the build follows the picks, not the spec's
older recommendations:

| Item | Change from the spec | Unit |
|---|---|---|
| Q-V-1 C | On a slow PC the app asks once after the speed test: live words with a smaller model, or text after you stop with the accurate model. The words are welcome-voice V2's `slow*` strings. The answer is kept on the Voice typing page. | VT-1 (setting), VT-14 (row), WV-1 (question) |
| Q-V-6 B | The pill counts down the last 30 s before the 5-minute cap, then finishes the text. The countdown's look is not drawn yet: D-VN. | VT-3 (timing), VT-17 (look) |
| Q-V-7 A | Error notes say what happened plus one link that fixes it. The wording list goes on a variants page for the owner's pick: D-VN. | VT-17 |
| Q-V-8 A | The first time each engine is used, the composer's note shows the page's privacy line for about 5 s, once per engine. | VT-10 |
| Q-V-9 C | The installer ships the Vulkan and CPU runtimes only. On NVIDIA PCs the hardware check offers the CUDA runtime as a separate download. This changes V-3.2's "no native file is downloaded at run time". The offer's look is not drawn: D-VN. | VT-0 (feasibility, pin), VT-7 (download), VT-17 (offer) |
| Q-V-11 C | With "Send it as the prompt" on and a turn running, the voice text does what Return does: interrupt and send, or queue when heads are at work (ComposerView.swift:370-381). | VT-10 |
| Q-V-13 B | An installed model that is not in use is removed from a trash icon shown on hover, confirmed by a second click. | VT-7 (store), VT-14 (row) |
| Q-V-19 B | The Voice typing page gets a Microphone row to choose the input. This replaces V-4.6's "Windows default only". ED: "Windows default" is the first item and the default. | VT-1, VT-5, VT-13 |
| Q-V-15 | The cloud model is gpt-transcribe, off by default, turned on and set up on the Voice typing page. | VT-16 |
| Q-CZ-1 A | The Voice typing page is locked as drawn, its own sidebar page directly below Customization. | VT-13, VT-14 |
| Q-V-5, Q-V-10, Q-V-12, Q-V-14, Q-V-16, Q-V-17, Q-V-18 | ED-NEW-ledger-5, the lead's to settle: a FLEURS CC BY 4.0 clip; typing ignored while dictating; no AVX2 means not supported locally; the translate notes dropped; the Providers key field reused; "pick one if Hindi and Urdu get mixed up"; "Checked on {date}". | VT-0, VT-1, VT-14, VT-16 |

### 1.2 Getting Ready and the Welcome voice question

Governing rulings:
- OD-0096.
- The OD-0098 amendments: the getting-ready pick "Look I, Words W1, Download D3"; D3 with consent; naming the model's
  place in Settings; the welcome-voice pick "Place A, Words V2".

Designs:
- `E:\Droppy\work\design\decisions-2026-10-03\getting-ready\` (variant I, words W1 in `src\gr_lib.py:28-29`);
- `E:\Droppy\work\design\decisions-2026-10-04\welcome-voice\` (variant A, words V2 in `src\wv_lib.py:63-73`, with the
  slow-PC states).

**Built on staging: the preparation half.** These pieces exist (SF), with 54 tests in
`EffortSliderJewelWarmup*Tests.cs`:
- `src\Marrow.App\Views\Composer\GpuWarmup.cs`: `WarmAsync(TopLevel, IProgress<GpuWarmupStep>, ct)`, the `IsWarm`
  marker (build, adapter, driver), and `WhenIdle`;
- `GpuProgramCache.cs`.

**Left:**
- The warm-up covers only the jewel knob's 4 programs. It still needs the composer light, slider liquid and strike
  programs (W1's steps "Composer light", "Slider liquid", "Fast mode lightning"), and later the voice orbs.
- `GpuWarmup` has no step registry. `AddActivity` registers animations, not programs.
- Nothing in the app calls `WarmAsync` or `IsWarm`.
- No Getting Ready sheet exists.
- `WelcomeView` (`src\Marrow.App\Views\RootView.cs:846`) has no voice section.
- The voice preparation service does not exist (VT-15).

### 1.3 Customization

Governing rulings:
- OD-0083, OD-0085.
- OD-0089 with its amendment: layout B "Stage", and Working dots.
- OD-0091's sixth amendment and OD-0097 (the voice rows).
- OD-0099's Look Picks: Q-CZ-3 B, Q-RIM A, Q-TEXT A, U-06 A.
- Q-CZ-1 A.

Spec: `E:\Droppy\work\design\build-specs\customization\SPEC.md`, phases C1-C5 and tests CZ-T*.

**Built on staging:**
- The composer light engine, settings and persistence: ED-251, 70 tests. Files are under
  `src\Marrow.Core\Models\ComposerLights\` and `src\Marrow.UI\Port\ComposerLights\`, plus `ComposerLightSlot.cs` and
  `AppSettings.ComposerLight`.
- The accent picker D3 with 22 colours (OD-0088): ED-250, ED-253, ED-254. Files are `AccentPalette.cs` and
  `LookCard.cs`.
- Greyscale text everywhere (Q-TEXT A): ED-281, `src\Marrow.UI\Port\GreyscaleText.cs`. The only exception is
  `TaskbarBadge.cs:83`, whose greyscale edging with subpixel positioning is allowed by its test.
- The strike's bubbles inside the liquid (U-06 A). This is what `LiquidTrack.cs` already draws, so it needs no work.
- Q-RIM A: no change.

**Left:**
- The Customization page itself. There is no `SettingsPage.Customization`, and ED-251 states "the page itself is not
  built here".
- The Working dots setting. `ComposerWorkingDots.cs` is hard-wired to the accent.
- Marrow's glass colour picker (Q-CZ-3 B: SV square, hue strip, opacity, hex, recent colours, `#RRGGBBAA`, the light
  drawing alpha). Its design is `look-picks\src\lp-pickers.js` variant B. It replaces the spec's Q-CZ-3 recommendation.
- The voice rows and the voice preview (C4), which need voice units first.
- The Speech section (C5).
- Q-CZ-2, Q-CZ-4, Q-CZ-7 are ED-NEW-ledger-6 (scripted demo; the search words of spec section 20; one 0.32 s dot ease).
- Q-CZ-5 is settled by OD-0095: the preview's model menu is a `PopoverCard` with the arrow tip.

### 1.4 Update everything

Governing rulings:
- OD-0100 with its pick: place A plus place D; words U1 "Update everything / Up to date / Check now / Restart Marrow";
  items one at a time, Marrow last; Marrow's feed is GitHub Releases on the owner's GitHub, each publish on his word;
  the user's own Git, GitHub CLI, Node, Python and Office included through their own updaters.
- OD-0075 (the Tools page, variant 04).
- OD-0062 (Velopack).

Design: `E:\Droppy\work\design\decisions-2026-10-04\update-everything\`.

**Built on staging:**

| Piece | What is there (SF) | Tests |
|---|---|---|
| Velopack 1.2.161 | pinned; `VelopackApp.Build().Run()` at `Program.cs:38-54` | `VelopackStartupHookTests` |
| `scripts\package-windows.ps1` | `vpk pack`, local only (ED-235, ED-247) | |
| `UpdateChecker.cs` | a port with no feed: `DefaultFeed = ""`, OD-0012 I1; it parses the upstream GitLab release shape | |
| `AppUpdater.cs` | a port whose install step is blocked: `WindowsUpdateInstallBoundary.Stage` throws `PortBlockedException` for UNK-0009 | |
| About page update UI | `AboutUpdateViews.cs`, ED-261 | |
| CLI auto-update | `ProviderRegistry.UpdateCLIIfDue`, every 12 hours, forced when a model needs it | `ProviderRegistryCLIUpdateTests` |

**Left:**
- Velopack's `UpdateManager` and `GithubSource`. They are absent, and using them resolves UNK-0009.
- The item model and orchestrator.
- The latest-version lookups. These are new network use and need a network inventory entry.
- The Tools/Updates page with Installed and Latest columns (place A).
- The glass pill above Settings (place D).
- The winget and Office Click-to-Run updaters.
- The "Skills and tools" group, which comes from the skills units.

### 1.5 Skills downloads

Governing rulings, from the OD-0099 amendments of 2026-10-04:
- Nothing heavy ships. Tools and skill sets are optional downloads, from Settings and on demand in a chat.
- The Settings page is layout D, the in-chat offer is A, the words are S1.
- Download all states the total size and asks the user to confirm or cancel.
- Progressive disclosure follows Claude's model:
  - the installed, enabled skills' names and descriptions are listed in each session;
  - a skill's full text loads only when it is invoked.
- **Latest amendment (`7876e80da`): every skill set and plugin Anthropic and OpenAI publish is offered, proprietary
  ones included.** That covers Anthropic's docx, xlsx, pptx and pdf skills and OpenAI's proprietary-labelled plugins.
  Each is downloaded on request from the vendor's official source, with its licence file kept beside it, and never
  bundled.
- **Plugins and Skills each get their own Settings page** (the redesign's Sections C). The owner confirms after
  reviewing it.
- Earlier picks that still apply: Q-SKILLS-1 A, Q-SKILLS-4 A, Q-SKILLS-8 A.

Plans:
- `E:\Droppy\work\skills\ONDEMAND-PLAN.md`: units DL-0 to DL-20, regrouped below.
- `PLAN.md` revision 3: provider wiring WU-4 to WU-21.

**Built on staging: only the upstream skills port** (SF):
- Codex `skills/list` (`CodexSession.cs:640`);
- `SlashCommand.CodexSkills` (`Provider.cs:425-500`);
- the Skill tool-call rows in the Claude, Copilot and Command Code sessions;
- Pi run with `--no-skills`.

`unit/skills-w0` (not merged) holds the clean-room kit and the sample corpus. They are kept for later; the corpus
feeds DL-20's live document checks. **Everything in ONDEMAND-PLAN is left.**

What the latest amendment changes in ONDEMAND-PLAN:

| Point | Change |
|---|---|
| Q-OD-1 | Closed. A2 (Anthropic document skills) and O5 (OpenAI proprietary plugins) are in the catalogue. |
| Catalogue scope (ED) | It grows from "Anthropic's and OpenAI's own, open" to "every skill set and plugin they publish". SK-2's builder re-reads `anthropics/claude-plugins-official` (its Anthropic-authored plugins) and the 8 Figma skill folders in `openai/skills` and adds them. Plugins written by third parties that the vendors only host, and the macOS-only plugins, stay out: they are not the vendors' own, and the macOS ones cannot run on Windows. The lead confirms this reading. |
| Licence file | Kept beside each installed item (`<item>\LICENSE*` as published) and linked from its detail. |
| Terms step (ED) | ONDEMAND 2.5's "I have read these terms" step for non-open items stays as ED-proposed; the lead may drop it if he reads the ruling as covering consent. |
| Pages | "Plugins" lists the plugin sets: A3, A4, O4, O5 and claude-plugins-official. "Skills" lists the skill sets: A1, A2, A5, O1, O2, O3, Figma. Where the tools are listed follows the Sections C pick. |
| Plugin contents (ED) | A Claude plugin can carry commands, agents, hooks and MCP servers as well as skills. Only providers that load plugins (Claude `--plugin-dir`, Grok) get the whole folder; the others get the plugin's skills. Whether a downloaded plugin's hooks and MCP servers start enabled is an ED for the lead in SK-9; the proposal is off until the user switches the plugin on, as with skills. |

### 1.6 Earlier rulings in force

| Ruling | Staging | Left |
|---|---|---|
| OD-0075 liquid at every effort | Absent: cff1ba18d withdrew PR-COMPOSER-127 and -128; `TrackLook.Kind.Plain` is at `EffortSlider.cs:1091`. | **In flight** on `unit/blitz-effortslider2` (fac98e549, 2 ahead, 4 uncommitted test files): review, finish, merge (ES-0). It also settles strike U-05. |
| OD-0102 corners | `ContinuousCorner.LargestRadius` exists; the band still draws circular. | **In flight** on `unit/blitz-cornerband` (7744ed0ef, active): review, merge (CB-0). It edits `ComposerView.cs`, `GlassMaterial.cs` and `SwiftShapes.cs`. |
| OD-0093 strike (with OD-0084 Mix, OD-0079 simmer, OD-0078 bursts, the clean-start amendment) | Only the A2 strike (`LiquidStrike.cs`) and the jewel-knob ink on bubbles. | Everything in `E:\Droppy\work\design\strike-port\SPEC.md` sections 4-12. It is blocked on `unit/fast-strike` round 3 (6d173a080 was refuted under load and needs a quiet PC) and on its conflict-resolving merge (U-01). U-02 to U-05 and U-07 are ED (`engineering_decisions.md:1834`). |
| OD-0088 accent, OD-0090 jewel knob, OD-0094 title drift | Built and merged. | Nothing. |

Outside this plan, these rulings are also in force but not named in OD-0103. The lead decides whether they gate the
release:
- the OD-0060/OD-0075/OD-0076 OpenRouter model picker (Q-PICKER A approved; not built: no picker class in `src`; canvas
  `E:\Droppy\work\design\openrouter-picker\`);
- OD-0074 Rise (`settings-rise` was refuted and paused);
- the OD-0076 transition audit (`transitions` was paused);
- OD-0071 scrolling (`premium-scroll` was refuted).

---

## 2. Build units

Each unit takes about 1-3 agent-days in one worktree and is test-first. Where the spec numbers a test, the unit
cites it. "Cloud" means it can run on a Linux cloud worker (`CLOUD_WORKER_B3.md`: build with
`EnableWindowsTargeting`, run only its own classes, list WINDOWS-ONLY tests for the Windows gate). "Local" means it
must run on this PC. Sizes: **S** is about 1 day, **M** 1-2 days, **L** 2-3 days.

Shared rules for every unit:
- **Fixture generators.** Node-only generators (orb runtime, glow driver, czTone, the strike vectors) run anywhere.
  Generators that need headless Chrome with WebGL2 (ANGLE D3D11) run locally and are committed before a cloud unit
  needs them.
- **One owner per hot file at a time.** The locks are in section 3.

### 2.0 Shared settings

| Id | Contents | Files | Depends on | Acceptance tests | Where | Size | Owner Q |
|---|---|---|---|---|---|---|---|
| **SA-1** Settings pages and keys | (1) Adds `SettingsPage.Customization`, `VoiceTyping` and `Updates` to `SettingsView.cs` once, in sidebar order: General, Hydra, Appearance, Customization, Voice typing, Providers, Tools/Updates, MCP, … (spec CZ-2.1 and the update-everything page's order). Titles, symbols (`sparkles`, `waveform` with a Lucide mapping), keywords (Q-CZ-4 ED) and factory lines. Each page is a shell class whose `IsReady` const (in its own file) hides it from the sidebar until its unit sets it: tracked as incomplete in `STATE.md` (ED, the same pattern as V-2.6's flag). After this unit, page units never edit `SettingsView.cs`. (2) Adds `AppSettings` keys `voiceOrb`, `voiceTyping` (with `mic`, `liveWordsChoice`), `voiceHardware`, `voiceNotes`, `workingDots` and CZ-13.5 write coalescing. | `SettingsView.cs`, `AppSettings.cs`, `src\Marrow.App\Port\Settings\{Customization,VoiceTyping,Updates}SettingsPage.cs` (shells), `src\Marrow.Core\Port\Settings\WorkingDotsSettings.cs` | VT-1 | CZ-T1, CZ-T2, CZ-T3, CZ-T4 (case part); CZ-T27; CZ-T50 (JSON); V-T10 wiring; hidden pages absent from the sidebar and from search | Local | M | — |

### 2.1 Voice typing (18 code units and 1 design unit)

| Id | Contents | Files | Depends on | Acceptance tests (no microphone, network or download) | Where | Size | Owner Q |
|---|---|---|---|---|---|---|---|
| **VT-0** Pins, natives, assets, notices (V0) | The lead pins Whisper.net, Whisper.net.Runtime and Whisper.net.Runtime.Vulkan 1.9.1, and NAudio.Wasapi 3.1.0. Package references go into the Services csproj (this unit only). Reflection-records the Whisper.net builder, VAD and runtime names. Records the published native file list and sizes (budget under 50 MB). Checks the VC++ 2015-2022 runtime situation. Pins the Silero ggml VAD asset and the self-test clip (Q-V-5 ED: FLEURS CC BY 4.0, licence read from the dataset). Writes the THIRD_PARTY_NOTICES "Voice typing" section (V-3.6). **Q-V-9 C feasibility:** can Whisper.net load the CUDA runtime from a downloaded folder (`RuntimeOptions` library path)? Pins the nuget.org CUDA package's URL, size and SHA-512. | `Directory.Packages.props` (lead), `Marrow.Services.csproj`, `src\Marrow.Services\Port\Voice\Assets\*`, `THIRD_PARTY_NOTICES.md`, tests `Services.Tests\Port\Voice\VoicePinsTests.cs` | — | V-T2, V-T3, V-T4, V-T5; clip hash pin; CUDA pin record | Local (natives, VC++) | M | **Conditional:** if the VC++ runtime is not already carried, adding Velopack's `vcredist143-x64` prerequisite needs the owner's word (V-3.2). |
| **VT-1** Core settings, catalogue, recommender | The `VoiceOrbSettings`, `VoiceTypingSettings` (+ `mic` for Q-V-19 B, + the Q-V-1 C answer), `voiceHardware` and `voiceNotes` records and JSON. `ModelCatalog` with the embedded manifest (29 files, V-9.1), names and sizes (V-9.2, V-9.6), the Q-V-14 notes dropped. `Recommender`, `SelfTestPlan` (with the ladders) and the hardware key. Q-V-4 A reason templates. `VoiceLanguage.Resolve` and the reset rule. The word sets L1-L6. The Q-V-17 text. | `src\Marrow.Core\Port\Voice\*` | — | V-T1, V-T10 to V-T16, V-T120 to V-T123 | Cloud | M | — |
| **VT-2** Core signal path | `PolyphaseResampler`, `LevelAnalyser` (Web Audio AnalyserNode equivalent), noise floor, followers. | `src\Marrow.Core\Port\Voice\Dsp\*`; fixtures `tests\fixtures\voice\analyser\` (headless Chrome, made locally) | — | V-T17 to V-T20 | Cloud (fixture made locally) | M | — |
| **VT-3** Core recognition and session | Seam interfaces with fail-closed defaults. Scripted doubles: `ScriptedCapture`, `ScriptedVad`, `ScriptedEngine`, and the V-14.8 script data. `SpeechLabel`. `StreamingTranscriber` (LocalAgreement-2, window rules). `TranscriptOps` with the final-pass schedule. `VoiceSession` (the section 15 table, Asking, Blocked, the 300 s cap with **the Q-V-6 B 30 s countdown state**, one session at a time, the composer leaving, **the Q-V-11 C send intent**). The insertion rules. | `src\Marrow.Core\Port\Voice\{Seams,Session,Transcript}\*` | VT-1 | V-T21 to V-T26, V-T30, V-T31, V-T46, V-T124, V-T89 (pure part); countdown from 270 s to 300 s on the manual clock | Cloud | L | — |
| **VT-4** Core orb and glow drivers | `OrbMotion` (the K table, springs). The orb export runtime (`sampleTransition`, `applyAudioUniforms`, motion phase, `seeds.json` byte-identical). `VoiceGlowDriver`. | `src\Marrow.Core\Port\Voice\Orb\*`, `Glow\*`; node fixtures from `voice-orbs.js` and `voice-glow.js` | — | V-T27, V-T28, V-T29 | Cloud | M | — |
| **VT-5** Capture (Windows) | `WasapiCapture` in the device mix format, `CapturePipeline` (lock-free ring, threads, no allocation), device events, **endpoint enumeration and selection for the Microphone row (Q-V-19 B)**, all-zero blocked check, `MicrophoneAccess` (WinRT). | `src\Marrow.Services\Port\Voice\Capture\*`, `src\Marrow.App\Port\Voice\MicrophoneAccess.cs` | VT-0, VT-2, VT-3 | V-T40, V-T41, V-T42; device list and choice from a scripted enumerator; the chosen device missing falls back to the default | Local | M | — |
| **VT-6** Engine, VAD, hardware probe | `SileroVad` (P/Invoke fallback per V-6.1). `WhisperNetEngine` (Vulkan then CPU, and the downloaded CUDA folder when present). `HardwareProbe` (DXGI, Win32, registry; GPU backend confirmed by a VAD load). The `voice-evidence` gate tool. | `src\Marrow.Services\Port\Voice\{Engine,Probe}\*`, `tools\voice-evidence\` | VT-0, VT-3 | V-T45; engine adapter against `ScriptedEngine`-shaped contract tests; runtime-order selection from a scripted runtime list | Local | L | — |
| **VT-7** Model store and downloads | `ModelStore` (`installed.json` trust rules). `ModelDownloader` **on SK-3's shared downloader** (ED: one downloader in the app). **Remove (Q-V-13 B, store side)**. **CUDA runtime pack as a downloadable item (Q-V-9 C)**, installed under the data root. | `src\Marrow.Services\Port\Voice\Models\*` | VT-1, VT-3, SK-3 | V-T43, V-T44; remove refuses the model in use; CUDA pack verify and unpack on the fake server | Cloud | M | — |
| **VT-8** Orb renderer | `VoiceOrbView` with the SkSL port, 2x below 128 px, one reused surface, frames only while visible, orb swap crossfade. Registers its programs through GR-1's registry from its own file. | `src\Marrow.UI\Port\Voice\Orb*`, `src\Marrow.App\Port\Voice\VoiceWarmup.cs`; reference PNGs `tests\fixtures\voice\ref-orbs.js` (Chrome ANGLE D3D11) | VT-4, GR-1 | V-T50 to V-T53 (V-T54 native at the gate) | Local | L | — |
| **VT-9** Glow renderer and the light while you speak | `VoiceGlowView` and its shaders; `ComposerLightView.VoiceLight` (away / dim / stay follower). | `src\Marrow.UI\Port\Voice\Glow*`, `ComposerLightView.cs` | VT-4 | V-T60, V-T61, V-T62 | Local (reference frames) | M | — |
| **VT-10** Composer A: slot, attachment, notes | Attachment move (inside, outside). The orb slot. `VoiceFeature.Enabled` (false). Esc. The notes layer: cancelled, no speech, blocked, **the privacy note once per engine (Q-V-8 A)**. Announcements. One session at a time. The composer leaving. After voice input leave or send, **send while a turn runs = Return (Q-V-11 C)**. | `ComposerView.cs`, `src\Marrow.App\Port\Voice\{ComposerVoiceController,VoiceNotes,VoiceFeature}.cs` | VT-3, VT-8, SA-1, CB-0 merged | V-T70, V-T71, V-T80, V-T82 to V-T84, V-T87; privacy note shown once per engine (store fake); interrupt-and-send and queue-when-heads paths against a fake runtime | Local | L | — |
| **VT-11** Composer B: live words | `DictationLayer`: live words, the tentative colour, revise with FLIP, caret, undo as one step, withdraw on cancel, RTL, the pixel-identical hand-over. | `src\Marrow.App\Port\Voice\DictationLayer.cs`, `ComposerView.cs` (editor hook) | VT-10 | V-T76 to V-T79, V-T81 | Local | L | — |
| **VT-12** Composer C: pill, cross, float, question | The glass pill, cross and floating pill; label crossfade and width; pill words relabel; the language name (B); the language question (D) in `PopoverCard`; the clean pill. The glow and light wired in a real composer. Transition audit. | `src\Marrow.App\Port\Voice\{VoicePill,VoiceCross,VoiceLanguageQuestion}.cs`, `ComposerView.cs` | VT-11, VT-9, VT-2 | V-T72 to V-T75, V-T85, V-T86, V-T88, V-T125 to V-T129, V-T131 to V-T133 | Local | L | — |
| **VT-13** Voice typing page A | Page shell content: the Engine segmented control (the cloud key row inert until VT-16). The Language menu with the language-default values. **The Microphone row (Q-V-19 B)**. "Orb and after voice input" link. The privacy line. Uses CZ-1's menu button and segmented control. Sets `IsReady`. | `VoiceTypingSettingsPage.cs` | CZ-1, SA-1, VT-5 | V-T90 (section 1 strings), V-T91, V-T92, V-T130; microphone row lists the scripted endpoints and stores the pick | Local | M | — |
| **VT-14** Voice typing page B: Speech model | The hardware card: probe rows, recommendation box, Use recommended / Keep, **"Checked on {date}"**. The model list: versions, English only, meters, state column, progress, Recommended badge, **hover trash and confirm (Q-V-13 B)**. The footer total. **The Q-V-1 C preference row** (ED place: under the recommendation box, V2's words). | `VoiceTypingSettingsPage.cs`, `src\Marrow.App\Port\Settings\VoiceModelList.cs` | VT-13, VT-6, VT-7 | V-T93 to V-T96; remove; preference row round-trip | Local | L | — |
| **VT-15** First run and switch-on | `VoicePreparation`: Probe, Recommend, InstallAndTime, IsPrepared. The self-test ladder; `LiveWords` from the record. **D3 with consent:** nothing downloads until Welcome's yes or an orb click; an early orb click shows the download progress. Registers the "Voice hardware" step in GR-1's registry. Sets `VoiceFeature.Enabled = true`. | `src\Marrow.App\Port\Voice\VoicePreparation.cs`, `VoiceFeature.cs` | VT-6, VT-7, VT-12, VT-5, GR-1 | V-T100 to V-T104; no transfer before consent (the downloader fake throws) | Local | M | — |
| **VT-16** OpenAI cloud engine | `OpenAiTranscriber` (multipart 16 kHz WAV, gpt-transcribe, SSE deltas in Processing). `VoiceKeyStore` (SecretStore `Marrow Code` / `voice-openai`). The key row (the Providers field pattern). Cloud stays off by default. | `src\Marrow.Services\Port\Voice\Cloud\*`, `VoiceTypingSettingsPage.cs` | VT-13, VT-14 (page file), VT-3 | V-T47, V-T110 to V-T113 | Local | M | — |
| **D-VN** "Voice notes" variants page (design, not code) | One decision page (OD-0070) drawing: the Q-V-7 A error-note wording list (no microphone, unplugged or busy, model still downloading, failed to load, no AVX2, engine failure, key missing / refused / out of quota, no network), each with its one link; **the Q-V-6 B countdown in the pill**; **the Q-V-9 C CUDA offer** in the hardware card. Dark and light, in the composer's note and pill patterns. | `E:\Droppy\work\design\decisions-2026-10-04\voice-notes\` | — | page check script (as the other decision pages) | Local (Chrome) | S | **OWNER pick** |
| **VT-17** Voice notes, countdown, CUDA offer | Builds the pick: every error note with its link; the countdown look; the CUDA offer and its download through VT-7. | `src\Marrow.App\Port\Voice\{VoiceNotes,VoicePill}.cs`, `VoiceTypingSettingsPage.cs` | VT-12, VT-16, VT-7, D-VN pick | the picked strings byte-equal; each error from a scripted seam failure; countdown frames; CUDA offer shown only for NVIDIA plus a working probe | Local | M | waits on the D-VN pick |

### 2.2 Getting Ready and Welcome (3 units)

| Id | Contents | Files | Depends on | Acceptance tests | Where | Size | Owner Q |
|---|---|---|---|---|---|---|---|
| **GR-1** First-run preparation service | A program/step registry in `GpuWarmup`: the jewel's 4 programs move into it, and the composer light and slider liquid programs plus today's strike programs are added. `FirstRunPreparation`: W1's steps in order (Graphics card, Effort slider knob, Composer light, Slider liquid, Fast mode lightning, Voice hardware), real progress events only, the prepared-state check (build, adapter, driver; ED-252's marker), skipped when prepared. A step registered later (voice, new strike) joins the list without editing `GpuWarmup.cs`. Wired at startup to run behind the screen of GR-2. | `GpuWarmup.cs`, `src\Marrow.App\Port\FirstRun\FirstRunPreparation.cs` | — | step order and names (W1); progress only from completed work (no timers: a manual clock never advances it); invalidation by build, adapter and driver; skip when warm; the existing 54 warm-up tests stay green | Local | M | — |
| **GR-2** Getting Ready sheet | Look I "Sheet": Welcome blurred behind, a glass sheet hanging from the top with the steps (spinner, check), "Ready", then it lifts away. Words W1. The transition into Welcome (OD-0076). Reduce motion. Shown only on first run or an invalid prepared state; never longer than the work. | `src\Marrow.App\Port\FirstRun\GettingReadySheet.cs`, `RootView.cs`, the startup hook in `DroppyCodeApp.cs`/`AppDelegate.cs` | GR-1 | geometry and timing against a fixture measured from `getting-ready` variant I (headless Chrome); states for a scripted preparation; frame-clock transition audit; not shown when prepared | Local | L | — |
| **WV-1** Welcome voice question | Variant A: a "Voice typing" section above Activity in `WelcomeView`, words V2, states ask / yes / ready / no / slow / slowlive / slowacc / gone. It names "Settings > Voice typing > Speech model". Yes starts VT-15's download and speed test; slow asks the Q-V-1 C question. When Welcome is gone, the question shows at the next orb click (ED: in the orb's `PopoverCard`, V2 words). | `RootView.cs`, `src\Marrow.App\Port\FirstRun\WelcomeVoiceSection.cs`, `src\Marrow.App\Port\Voice\VoiceSpeedQuestion.cs` | GR-2, VT-15, VT-14 | V2 strings byte-equal (fixture from `wv_lib.py`); the state machine with scripted preparation (pc, slow profiles); No downloads nothing (seam throws); the answer stored and shown on the Voice typing page | Local | M | — |

### 2.3 Customization (9 units)

| Id | Contents | Files | Depends on | Acceptance tests | Where | Size | Owner Q |
|---|---|---|---|---|---|---|---|
| **CZ-1** Controls and text | `CzControls`: segmented control, slider with Auto (inverted too), swatch row, capsule button, fold row, crossfade text, style card, orb tile, shared menu button (CZ-12.12). Also `CzText`, `fmt`, slider snap, `MakerLiquids`, `czTone`. | `src\Marrow.UI\Port\Settings\CzControls.cs`, `src\Marrow.Core\Port\Settings\{CzText,MakerLiquids}.cs` | — | CZ-T6 to T9, T13, T25, T29, T30, T42, T43 (control part), T92 | Cloud | L | — |
| **CZ-2** Glass colour picker (Q-CZ-3 B) | `GlassColorPicker` in a `PopoverCard`: SV square, hue strip, opacity strip, hex (`#RRGGBB` and `#RRGGBBAA`), 8 recent colours, keyboard. Uiverse custom colours stored as `#RRGGBBAA`, and the light draws alpha. | `src\Marrow.UI\Port\Settings\GlassColorPicker.cs`, `ComposerLightSettings.cs`, `ComposerLightRenderer.cs`, `ComposerLightShaders.cs` | — | maths and hex parsing against a node fixture of `lp-pickers.js`; recent-list order; alpha round trip and rendered alpha (CPU raster); arrow-tip geometry (OD-0095) | Cloud | M | — |
| **CZ-3** Stage and preview composer | `CustomizationStage` (pinned block, not sticky yet), `ComposerPreview` (472 px), `PreviewModelSwitch` (steppers, chip, menu above the chip in `PopoverCard`), preview phase, preview maker. | `src\Marrow.App\Port\Settings\{CustomizationStage,ComposerPreview,PreviewModelSwitch}.cs` | CZ-1 | CZ-T28, T31 to T38, T45, T46 | Local | L | — |
| **CZ-4** Page and light sections (C1) | Customization page: chrome accessory, Style cards, Border beam (Look, palettes, pace, Advanced, Reset), Uiverse (Look with Custom wells opening CZ-2, Motion, Brightness, Advanced, Reset), Off. Persistence to every composer. Sets `IsReady`. | `CustomizationSettingsPage.cs` | CZ-1, CZ-2, CZ-3, SA-1 | CZ-T5, T10 to T24, T26, T39 to T41, T44 | Local | L | — |
| **CZ-6** Sticky stage (C3) | Pin, compaction, release, clip, hairline, focus scrolling, no measure pass during a scroll. | `CustomizationStage.cs`, `CustomizationSettingsPage.cs` | CZ-4 | CZ-T60 to T68 (T69 native at the gate) | Local | M | — |
| **CZ-5** Working dots (C2) | The section, the `workingDots` setting applied in every composer (Accent, Model colour, Custom), the 0.32 s ease (Q-CZ-7 ED). | `CustomizationSettingsPage.cs`, `ComposerView.cs` (`ComposerWorkingDotsSlot`), `ComposerWorkingDots.cs` | CZ-6, SA-1; `ComposerView.cs` free (after VT-12) | CZ-T50 to T55 | Local | M | — |
| **CZ-7a** Voice orb rows (C4, page part) | Orb tiles, Listening style, Pill words, Orb size, Attachment, Composer light while you speak, Spoken language, After voice input, the note. Writes to `voiceOrb`, with the `voiceTyping.lang` reset. | `CustomizationSettingsPage.cs` | CZ-5, VT-12, VT-1 | CZ-T70 to T72, T90, T91, T93, T97 | Local | M | — |
| **CZ-7b** Voice preview (C4, stage part) | The preview's orb, pill, cross, glow, attachment place, scripted demo, the language question in the stage, the light's level while it listens. | `ComposerPreview.cs`, `CustomizationStage.cs` | CZ-6, VT-12, VT-9 | CZ-T73 to T78, T94 to T96 | Local | L | — |
| **CZ-8** Speech section (C5) | The Speech section, its Language detail under the language defaults, `Change` opening Voice typing, the pin released at Speech. | `CustomizationSettingsPage.cs` | CZ-7a, VT-14 | CZ-T80 to T82, T98 | Local | S | — |

### 2.4 Update everything (5 units)

| Id | Contents | Files | Depends on | Acceptance tests | Where | Size | Owner Q |
|---|---|---|---|---|---|---|---|
| **UE-1** Velopack self-update | `UpdateManager` with `GithubSource`. The repository is one named constant, empty until the owner answers, which means "no feed": the OD-0012 I1 behaviour stays. Check, download with % progress, apply and restart ("Restart Marrow"). `WindowsUpdateInstallBoundary.Stage` through Velopack (closes UNK-0009). About page and `UpdateChecker` on the new source. `package-windows.ps1` writes the release folder `vpk upload github` needs, but never uploads. | `UpdateChecker.cs`, `AppUpdater.cs`, `src\Marrow.App\Port\UpdateInstallBoundary.cs`, `scripts\package-windows.ps1` | — | Velopack source and downloader seams on a loopback fake (fail closed); check, download % and apply paths; empty constant equals today's behaviour; the existing `AppUpdater*` and `UpdateChecker*` tests kept or amended with the decision cited | Local | L | ANSWERED (OD-0100, 2026-10-04): the feed is the public repository Oak993/marrow-releases (created by the lead). Was: which GitHub repository hosts the releases. It must be public for the relatives to update without a token; a private repo would need a token inside the app. Each publish still needs his word. |
| **UE-2** Update orchestrator | Item model: Marrow (through an interface UE-1 implements), every provider CLI that has an updater (through `ProviderRegistry`'s existing update run, exposed with a result), a "Skills and tools" group (SK-8 fills it), and user dev-tool rows (UE-3 fills them). Check now: latest versions from public listings (a network inventory entry, ED from the page), "Checks when updated" when there is no listing. Runs one at a time, Marrow last; progress (indeterminate for CLIs); results; Retry. The 12-hour background updates are unchanged. | `src\Marrow.Core\Port\Updates\*`, `src\Marrow.Services\Port\Updates\*`, `ProviderRegistry.cs` | — | order and Marrow-last; per-item states and Retry with fake runners; listings parsed from fixtures on SK-0's fake server; the background `UpdateCLIIfDue` tests unchanged | Cloud | L | — |
| **UE-3** Your dev tools | Detects installed Git, GitHub CLI, Node, Python and Office. Updates through winget, or Office's Click-to-Run updater, with the exact commands taken from the supervised trial (section 4, H). Rows only for installed tools. | `src\Marrow.Services\Port\Updates\{Winget,OfficeC2R}Updater.cs` | UE-2, the supervised trial | argv, exit codes and output parsing on recorded fixtures; no real process (the seam throws) | Cloud | M | — |
| **UE-4** Updates page (place A) | The Tools page of OD-0075 grown into Updates: rows with Installed and Latest columns, "Update everything", "Check now", per-row Update and Retry, "Up to date", "Restart Marrow" (U1). Transitions. Sets `IsReady`. | `UpdatesSettingsPage.cs` | UE-1, UE-2, SA-1, CZ-1 | strings (U1); states from a scripted orchestrator; transition audit; Reduce Motion | Local | L | — |
| **UE-5** Update pill (place D) | A glass pill above Settings in the sidebar, shown only when something is out of date, with the run's progress ring and a `PopoverCard` list; Restart. | `SidebarView.cs`, `src\Marrow.App\Port\Updates\UpdatePill.cs` | UE-1, UE-2 | hidden when current; ring progress; popover rows; the existing `UpdateAvailableDot` tests kept | Local | M | — |

### 2.5 Skills downloads (23 units, counting SK-11's six provider units)

These units regroup ONDEMAND-PLAN's DL units. DL-11 (wiring) and DL-12 (event taps) are merged per provider so each
session file has one owner. DL-14 (`request_tool`) joins the API-provider unit.

| Id | Contents (ONDEMAND §) | Depends on | Acceptance tests | Where | Size | Owner Q |
|---|---|---|---|---|---|---|
| **SK-0** Fake download server and network guard (DL-0) | — | — | DL-0's list; shared with VT-7, UE-2 | Cloud | M | — |
| **SK-1** Catalogue schema, embedded catalogue, gate (DL-1) | **Now with A2, O5, claude-plugins-official (Anthropic-authored) and the Figma folders.** Each item carries `kind` (skillset or plugin), `termsClass` and its licence file path. | — | DL-1's list with the new counts (set by SK-2's re-read); every item names its licence file | Cloud | M | — |
| **SK-2** Catalogue builder and change report (DL-2) | Developer script; network allowed by hand, not a test. | SK-1 | DL-2's offline test | Cloud (the real pin run is the lead's) | M | — |
| **SK-3** Shared downloader (DL-3) | — | SK-0 | DL-3's list | Cloud | L | — |
| **SK-4** Installers by kind (DL-4) | Keeps the licence file beside each item. | SK-3, SK-7 | DL-4's list | Local (reparse points, exe fixtures) | L | — |
| **SK-5** Store, lock, verify, GC (DL-5) | — | SK-1, SK-7 | DL-5's list | Local (ACL) | L | — |
| **SK-6** Shims and the PATH rule (DL-6) | A new NativeAOT launcher project. | SK-1 | DL-6's list | Local | M | — |
| **SK-7** State model (DL-7) | — | SK-1 | DL-7's list | Cloud | M | — |
| **SK-8** Update and remove pipelines (DL-8) | Feeds UE-2's "Skills and tools" group. | SK-4, SK-5, SK-7, UE-2 | DL-8's list | Cloud or local | M | — |
| **SK-9** Sets into per-chat generations (DL-9) | Covers progressive disclosure (only installed and enabled items listed by name and description; full text on invocation), Q-SKILLS-1 A defaults, and **plugin folders** (whole folder for plugin-loading providers, skills only for the rest; the hooks/MCP default as an ED). | SK-5, SK-7 | DL-9's list; a disabled or not-installed skill never appears in a generation; plugin split per provider | Cloud | M | — |
| **SK-10** Missing-tool detector (DL-10) | — | SK-1 | DL-10's list | Cloud | M | — |
| **SK-11a** Claude: `--plugin-dir` and taps | WU-6 and DL-12 Claude: stream-json `tool_use`, `tool_result`, `can_use_tool`. | SK-9, SK-6, SK-10 | WU-6 tests; normalized events from fixture transcripts; argv identical with nothing installed | Cloud | M | — |
| **SK-11b** Codex | WU-7: `extraRoots/set`, item and approval taps. | same | WU-7 tests; taps | Cloud | M | — |
| **SK-11c** Copilot and ACP | WU-8: `skillDirectories` with the -32602 fallback, permission taps. | same | WU-8 tests; taps | Cloud | M | — |
| **SK-11d** OpenCode | WU-9: `OPENCODE_CONFIG_CONTENT`. | same | WU-9 tests | Cloud | M | — |
| **SK-11e** Pi, Command Code, Grok | WU-10 and taps. | same | WU-10 tests; taps | Cloud | M | — |
| **SK-11f** API providers | WU-12: the catalogue block, `use_skill`, progressive disclosure, and DL-14 `request_tool`. | same | WU-12 and DL-14 tests | Cloud | M | — |
| **SK-12** Offer flow and resume (DL-13) | R1 to R4, the S1 continuation message. | SK-10, SK-11a-f, SK-3, SK-4 | DL-13's list | Local (ThreadRuntime) | L | — |
| **SK-13** Terms, records, uninstall hook, Reset (DL-15, DL-16) | Licence beside each item; the terms step kept as an ED. | SK-5, SK-7 | DL-15 and DL-16 lists | Local (`Program.cs`, ACL) | M | — |
| **SK-14** In-chat offer card (DL-18) | Offer A inline in the reply, words S1. Both are picked, so this can be built. | SK-12 | DL-18's list | Local | M | — |
| **SK-15** Plugins page and Skills page (DL-17) | Layout D. **Separate Plugins and Skills pages (Sections C)**, Download all with the total size and confirm or cancel, found on this PC, "Already available through your providers", per-skill switches, footer. Adds two `SettingsPage` cases (edits `SettingsView.cs` itself, which is free by then). | SK-8, SK-9, SK-13 | DL-17's list for the picked design; the Download all confirmation sums sizes and starts nothing on cancel | Local | L | Picked (OD-0099 clarification, staging 4e6d004e0): Sections C, separate Plugins and Skills pages (Plugins directly above Skills), each with its own Download all using Download all A's confirmation; tools on the Tools page with their own Download all; no Download everything; Invoke A. Build from the final skills-downloads page. |
| **SK-16** Wiring (DL-19) | One download per item across Settings and offers; restart and resume; the Skills and tools rows on the Updates page. | SK-14, SK-15, UE-4 | DL-19's list | Local | M | — |
| **SK-17** Antigravity and Cursor plugin folder (Q-SKILLS-8 A) | Only if the live test (section 4, I) passes; otherwise these two providers get skills only when named, and this unit records that. | the DL-20 result | removable Marrow-owned plugin; never touches the user's own | Local | S | — |

### 2.6 Strike and the rulings in flight (4 new units, plus 2 in flight)

| Id | Contents | Files | Depends on | Acceptance tests | Where | Size | Owner Q |
|---|---|---|---|---|---|---|---|
| **ES-0** (in flight) `unit/blitz-effortslider2` | OD-0075 liquid at every effort; review, finish the 4 uncommitted tests, merge. | `EffortSlider.cs`, `SwiftColor.cs` | — | its own | Local | — | — |
| **CB-0** (in flight) `unit/blitz-cornerband` | OD-0102 band rule; review, merge. | `SwiftShapes.cs`, `ContinuousCorner.cs`, `GlassMaterial.cs`, `ComposerView.cs` | — | its own | Local | — | — |
| **ST-0** Fast-strike round 3 and merge (U-01) | Fixes from ED-248 review 2 (refresh over-count, server GC in the test host, UI-thread bytes). A quiet-PC measurement (section 4, A). Merge into staging, keeping fast-strike's frame path over jewel-knob's in `LiquidStrike`, `LiquidTrack`, `EffortSlider`, `FrameClock` (`RenderFrameSource`, `SkiaShaders`, `GlassMaterial`). | those files, `Marrow.App.csproj` (server GC) | ES-0, CB-0 | `EffortSliderFramePathTests`, `RenderFramesTests`, `GlassFramePathTests`; all jewel tests green after the merge | Local (quiet PC for the measurement) | L | — |
| **ST-1** Strike pure maths (TESTS Phase A) | `StrikePlan` (Mix timing, styles, paths, ring of 8), `StrikeStarts` (clean windows, random clean starts, the model-change wait), `StrikeInk`, `Strike.Ss` and the Ease table. | new `StrikePlan.cs`, `StrikeStarts.cs`, `StrikeInk.cs`; `LiquidStrike.cs` | ST-0 | Phase A against `strike-port\vectors\*.json` | Cloud | L | — |
| **ST-2** One frame resolved (Phase B) | `StrikePlume` geometry and per-frame resolution; the OD-0093 contact rule in `BubbleBursts`; the Mix charge in `KnobMotion`. | `StrikePlume.cs`, `BubbleBursts.cs`, `KnobMotion.cs` | ST-1 | Phase B (vectors: plume, contacts, bursts, knob-signal) | Cloud | L | — |
| **ST-3** Rasterised and the frame path (Phases C, D) | `LiquidTrack` layers (masks, two composite groups, the glass pass `over` child), frame order, simmer and nucleating lanes, ink times the fast ramp (U-02 ED), `EffortSlider` wiring (ex, start triggers, ramp, Reduce Motion still), zero allocation. Registers the strike programs with GR-1. | `LiquidTrack.cs`, `LiquidStrike.cs`, `EffortSlider.cs`, `src\Marrow.App\Port\FirstRun\StrikeWarmup.cs` | ST-2, GR-1 | Phases C, D (Phase E native, section 4) | Local | L | — |

**Unit count per feature (new units):**

| Feature | Units |
|---|---|
| Voice typing | 18 code units (VT-0 to VT-17) and 1 design unit (D-VN) |
| Getting Ready and Welcome | 3 (GR-1, GR-2, WV-1) |
| Customization | 9 (CZ-1 to CZ-8, with CZ-7 split into a and b) |
| Update everything | 5 (UE-1 to UE-5) |
| Skills downloads | 23 (SK-0 to SK-17, SK-11 being 6 units) |
| Earlier rulings | 4 (ST-0 to ST-3), plus 2 in flight (ES-0, CB-0) |
| Shared | 1 (SA-1) |
| **Total** | **64 new units** |

---

## 3. Order: at most 4 local and 2 cloud at once, one owner per file

A wave is about 2 agent-days. A slot starts its next unit as soon as its current one merges, so waves overlap in
practice. Every unit's dependencies merge in an earlier wave. Units marked * can run in the cloud but run locally
there to balance the load.

| Wave | Local (4) | Cloud (2) |
|---|---|---|
| **W1, now** | **GR-1**, **VT-0** (2 slots free now; ES-0 and CB-0 hold the other two); as those merge: **UE-1**, **D-VN** | **VT-1**, **SK-0** |
| W2 | ST-0 (measurement in a quiet window), SA-1, VT-4*, CZ-1* | VT-3, SK-1 |
| W3 | GR-2, VT-8, CZ-3, VT-2* | SK-3, CZ-2 |
| W4 | VT-5, VT-6, VT-9, CZ-4 | SK-7, ST-1 |
| W5 | VT-10, CZ-6, SK-5, SK-6 | VT-7, ST-2 |
| W6 | VT-11, VT-13, ST-3, UE-2* | SK-9, SK-10 |
| W7 | VT-12, VT-14, UE-4, SK-4 | SK-11a, SK-11b |
| W8 | VT-15, VT-16, CZ-5, UE-5 | SK-11c, SK-11d |
| W9 | WV-1, CZ-7a, CZ-7b, SK-13 | SK-11e, SK-11f |
| W10 | SK-12, CZ-8, VT-17 (after the D-VN pick), SK-8* | SK-2, UE-3 (after trial H) |
| W11 | SK-14, SK-15 (after the Sections C review), SK-17 (after the live test) | — |
| W12 | SK-16; then the release gate (section 4 and a full `verify.sh`) | — |

**File locks.** Each hot file has one owner at a time, in this order:

| File | Owners, in order |
|---|---|
| `ComposerView.cs` | CB-0 → VT-10 → VT-11 → VT-12 → CZ-5 |
| `SettingsView.cs` | SA-1 only (SA-1 adds all five pages, Plugins and Skills included, 2026-10-04; page units and SK-15 edit only their own page file and flip its IsReady) |
| `AppSettings.cs` | SA-1 only |
| `GpuWarmup.cs` | GR-1 only (later units register from their own files) |
| `RootView.cs` | GR-2 → WV-1 |
| `ComposerLightView.cs` | VT-9 |
| `ComposerLightRenderer.cs` and `ComposerLightShaders.cs` | CZ-2 |
| `CustomizationSettingsPage.cs` | CZ-4 → CZ-6 → CZ-5 → CZ-7a → CZ-8 |
| `CustomizationStage.cs` and `ComposerPreview.cs` | CZ-3 → CZ-6 → CZ-7b |
| `VoiceTypingSettingsPage.cs` | VT-13 → VT-14 → VT-16 → VT-17 |
| `EffortSlider.cs`, `LiquidTrack.cs`, `LiquidStrike.cs` | ES-0 → ST-0 → ST-1 (`LiquidStrike.cs` only) → ST-3 |
| `KnobMotion.cs` and `BubbleBursts.cs` | ST-2 |
| `ProviderRegistry.cs` | UE-2 |
| `ThreadRuntime.cs` | SK-12 |
| `Program.cs` | SK-13 |
| `SidebarView.cs` | UE-5 |
| each session class | its SK-11 unit |
| `Directory.Packages.props`, `THIRD_PARTY_NOTICES.md`, `Marrow.Services.csproj` | VT-0 |
| `Marrow.App.csproj` | ST-0 |
| `Marrow.sln` | SK-6 (the new launcher project) |

**Quiet windows.** ST-0's frame measurement and every native frame gate need the PC idle with no agents running.
Schedule one quiet window at the end of W2, for ST-0, and the supervised session of section 4 before the release.

---

## 4. Native and quiet-PC checks for one supervised session

Preconditions:
- The PC is idle (no agents, no gate running).
- The port is run from its own build in a worktree, with a throwaway data root (`MARROW_PORT_DATA_DIR`), never the
  owner's data.
- The installed Electron app "Marrow" is never opened.
- Every action that changes Windows settings, or types a key or password, is the owner's own.

**Do item H first, and early.** It is a 15-minute trial, and UE-3 cannot start without it.

**A. Frame gates on the quiet PC**
- Fast-strike round 3 (ST-0) and the strike unit's Phase E (ST-3):
  - at least 99 % of present intervals on cadence at 240 Hz;
  - the longest content gap at most 21 ms;
  - no GC in 60 s;
  - 0 main-thread frames.
- `EffortSliderNativeFrameTests`.
- Customization sticky-stage scrolling (CZ-T69).
- Voice orb at most 0.5 ms and voice glow at most 2 ms of GPU time per frame, with 0 B allocated per frame (V-19).
- Composer light and voice glow together.
- Every registered GPU program compiles on ANGLE/D3D11: jewel, composer light, liquid, strike, both voice orbs
  (V-T54).

**B. Getting Ready** on a fresh data root:
- the sheet shows real steps and hands over to Welcome;
- no ~115 ms stalls afterwards (ED-252's measurement);
- the second launch skips it;
- a changed build marker shows it again.

**C. Welcome voice question:**
- Yes: the real model download from Hugging Face at the pinned commit (the recommendation for the RTX 4080:
  large-v3-turbo-q8_0, 874 MB), with progress at an orb click, the checksum, and resume after the process is killed
  mid-download.
- No: nothing is downloaded.

**D. Voice on real hardware.**
- GPU and timing:
  - the Vulkan runtime loads on the RTX 4080;
  - large-v3-turbo-q8_0 RTF on Vulkan, compared with BENCH's CUDA figure (this decides how strongly the Q-V-9 C CUDA
    offer is worded);
  - the CUDA pack downloads and loads;
  - a forced CPU-only run reaches the slow-PC question;
  - first live word 0.3-0.7 s after speech starts;
  - final pass under 0.5 s;
  - frame pacing holds during GPU inference.
- Real microphone:
  - the default input;
  - switching inputs on the Microphone row;
  - unplugging mid-session (the final pass runs on what it has);
  - another app holding the device;
  - the owner turns microphone privacy off: the Blocked note and its `ms-settings` link;
  - the all-zero first second.
- Session behaviour:
  - Hindi and Urdu samples;
  - Esc cancels;
  - the 5-minute cap with its countdown;
  - "Send it as the prompt" while a turn runs (interrupt and send).

**E. Cloud voice**, with the owner's own OpenAI key typed by him: one gpt-transcribe dictation, then a bad key
(401) and its note.

**F. VC++ runtime.** Install the packaged build in Windows Sandbox and confirm the Whisper natives load. This settles
VT-0's conditional question.

**G. Velopack from a GitHub feed.**
- Package versions N and N+1 with `package-windows.ps1`.
- **With the owner's word**, publish N+1 (a pre-release is enough) on the chosen repository.
- Install N in Windows Sandbox and run "Update everything": provider CLIs, skills and tools, dev-tool rows, then
  Marrow last with % progress.
- Restart Marrow; the version after the restart is N+1; the pill (place D) appears and clears.
- Uninstall removes `addons\`.

**H. Your dev tools (precondition for UE-3).** Read each updater's documentation, then try each once, supervised:
- winget for Git, GitHub CLI, Node and Python;
- Office's Click-to-Run updater.

Record the exact commands, exit codes and output as UE-3's fixtures.

**I. Skills (DL-20).**
- Downloads:
  - each pinned artifact downloaded once from its real source and hash-checked: the LibreOffice 26.2.6 MSI (356 MB),
    Python 3.14.8 embeddable, Node 24.21.0, Poppler, qpdf, pandoc, Git portable, gh, uv, Playwright Chromium
    (325 MB);
  - the newly added A2, O5, claude-plugins-official and Figma sets, each with its licence file beside it.
- Install mechanics:
  - `msiexec /a` without elevation;
  - Python `._pth`, the site `pip.ini` target, `PYTHONUTF8` through the shim;
  - `NODE_PATH` for the Node scripts;
  - the Chromium layout accepted by playwright 1.63.0.
- Real chats:
  - one per signal (S1 to S4) under Claude and Codex, for example a .docx task with LibreOffice missing: offer,
    download, the task continues;
  - Codex's elevated sandbox reading `addons\`.
- Antigravity and Cursor plugin-folder test (decides SK-17).
- Download all, with its size confirmation.
- Removing a tool that is in use.

**J. Settings on the real PC:**
- Check my hardware rows: Ryzen 7 9800X3D, 31 GB, RTX 4080, Vulkan working;
- the Customization live preview with each light and the glass colour picker;
- the Updates page during a real run.

**K. Reduce Motion.** The owner turns Windows' animation effects off; every new feature stills.

Release gate after the session:
- a full `verify.sh` green;
- every feature flag on (`VoiceFeature.Enabled`, every page `IsReady`);
- LICENSE, THIRD_PARTY_NOTICES and TRADEMARK in the package (the known blocker UI-SETTINGS-060 / UI-UPDATE-032);
- the voice and skills notices present;
- the release published only on the owner's word.

---

## 5. Owner questions still open

1. **Release feed repository (UE-1).** Which GitHub repository hosts Marrow's releases? It needs to be public so the
   relatives update without a token. Each publish still needs his word.
2. **Voice notes page (D-VN), a pick.** The error-note wording (Q-V-7 A), the 5-minute countdown look (Q-V-6 B) and
   the CUDA offer (Q-V-9 C). Only VT-17 waits on it.
3. **Plugins and Skills pages (SK-15), the review already pending.** The Sections C redesign, with Download all and
   its size confirmation. Only SK-15, and after it SK-16, wait.
4. **Conditional (VT-0, item F).** If the installed app does not carry the VC++ 2015-2022 runtime, adding Velopack's
   `vcredist143-x64` prerequisite needs his word.

Closed since the inputs were written: Q-OD-1 (vendor proprietary sets: all are offered, `7876e80da`); Q-V-1, Q-V-2,
Q-V-3 and the Q-V picks (OD-0097, OD-0098, OD-0099); Q-CZ-1, Q-CZ-3, Q-CZ-5 and Q-CZ-6.

For the lead, not the owner:
- the ED-NEW-ledger-5, -6 and -7 items;
- the catalogue-scope reading of the latest amendment;
- the terms step;
- the default for plugin hooks and MCP servers;
- SA-1's hidden-page pattern;
- whether the OpenRouter picker, Rise, the transition audit and scrolling gate this release (section 1.6).
