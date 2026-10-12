# SPEC 2: Voice typing (build specification)

Status: ready for test-first build, phase by phase (section 20). Written 2026-10-03 for the Marrow Windows port
(C# / .NET 10 / Avalonia 12.1.2). Owner-ruled addition beyond the pinned Droppy Code 1.7.0 source (which has no voice
input): every element is a DEVIATION under OD-0086, OD-0091 (and its six amendments), OD-0092 (and its amendment),
OD-0096, OD-0097 (and its amendment) and OD-0076.

Amended 2026-10-03 for OD-0097 (voice options: pill words, the composer light while you speak, the language default;
V-1.5, V-6.3, V-7.13, V-7.14, V-12.3, V-12.9 to V-12.12, V-13.4, V-15, V-16.1 to V-16.3, V-20, V-21 tests V-T120 to
V-T133, V-22, Q-V-3) and its amendment (nothing but the orb, the label and the cancel control in the pill: V-12.12).
In the amended clauses **OWNER** marks the owner's ruling, **SOURCE** the Voice Options decision page as the owner
approved it, **ED** an engineering decision.

Clause ids (`V-n.m`) are cited by the tests (section 21) and by code comments.

Evidence labels: **DESIGN** (the locked voice design, file and line), **OWNER** (OD and amendment), **BENCH** (measured
on the owner's PC, `E:\Droppy\work\voice\BENCH.md` and `bench\results\`), **RESEARCH** (`E:\Droppy\work\voice\RESEARCH.md`,
with its own VERIFIED / REPORTED / ESTIMATE labels), **PORT** (port code at staging `ad25b0b14`), **ED** (an engineering
decision proposed here; recorded as the next free `ED-nnn` at merge), **UNVERIFIED** (must be checked in phase V0 before
code depends on it), **UNRESOLVED** (needs the owner: section 23).

---

## 0. Sources and authority

| What | Where |
|---|---|
| Voice composer design (locked defaults: OD-0091 sixth amendment) | `E:\Droppy\work\voice\design\voice-composer.html` (CSS, tokens), `voice-composer.js` (states, numbers `K`, live words, final pass, pill geometry), `voice-orbs.js` (the exact Liquid Orb exports in WebGL2), `voice-glow.js` (voice-glow 0.2.1, verbatim), `voice-beam.js`, `NOTICES.md`; page https://claude.ai/artifact/JFqH3AwciRoxDjeZ9hCFLu (v2), copy `E:\Droppy\work\design\published\vcpub\` |
| Orb exports pasted by the owner | `E:\Droppy\work\voice\reference\orb-exports\` (`seeds.json`, `siri-wave.export.html`, `voice-membrane.export.html`, idle/thinking PNGs) |
| Customization rows for voice | SPEC 1 (`..\customization\SPEC.md`) section 9; design `E:\Droppy\work\design\customization\src\voicecmp.js` |
| Voice typing page design | `E:\Droppy\work\design\customization\src\voice.js`, `gen.py` (PAGE_V, lines 570-606) |
| Research, benchmark | `RESEARCH.md`, `BENCH.md`, `bench\results\tables.md`, `bench\downloads.json`, `bench\scripts\hf_tree.json` |
| Getting ready (OD-0096) | variants canvas `E:\Droppy\work\design\decisions-2026-10-03\getting-ready\` (pending the owner's pick; its Downloads board offers D1 / D2 / D3, D3 recommended) |
| Composer light (give way) | port `src\Marrow.UI\Port\ComposerLights\ComposerLightView.cs`, `src\Marrow.App\Views\Composer\ComposerLightSlot.cs` |
| Voice options (OD-0097: pill words L1-L6, light while you speak B1-B3, language default A-D) | decision page `E:\Droppy\work\design\decisions-2026-10-03\voice-options\` (`voice-options.html` Q1-Q3 option texts, `voice-options.js` `LSETS`, the beam follower, `askLang` / `answerLang`, `voice-options-q.js`; built by `src\build_voice.py` from the locked voice design); published https://claude.ai/artifact/8ZC213hrTRPKzSkudYJtH4 |

Authority: PORT_CONTRACT > amendments > owner_decisions > this spec's EDs. Conventions: `porting\IMPLEMENTING.md`
(port-only code under `Port\`, R-TEST-1..10). Tests run ONLY through `E:\Droppy\work\claude-verify\one.sh`,
`one-full.sh`, `one-names.sh`. **No test uses a real microphone, the network, or a model download** (V-2.5).

---

## 1. What the user gets (owner rulings in one place)

V-1.1 (OD-0091) The composer's attachment button moves to the composer's left; a **voice orb** sits where it was. The orb
is the exact Liquid Orb Editor export: **Siri Wave** by default, **Voice Membrane** as the alternative (Customization).

V-1.2 (OD-0091 + amendments) At rest the orb moves slowly; on hover it speeds up and glows; a click starts listening: it
pops and grows, its speed rises and falls with the voice (resting speed in pauses); a second click stops; the final
pass runs at the orb's fastest speed; the text lands in the composer **unsent** (setting: leave | send). A cancel cross
(or Esc) discards. Transcription is **live**: words are written into the composer as they are recognised, partial words
revised without jumps, the composer in its typing state. The orb opens sideways into a **glass pill** reading
`Listening…` / `Transcribing…` (hysteresis) / `Finishing…`, the cross inside its far end (default; alternatives Cross
beside the orb, Floating pill). The composer shows the **voice glow** while listening and its **sweep** while
processing; the composer light **steps aside** and returns after (the default, OD-0097 B1; V-13.4).

V-1.3 (OD-0092 + amendment) Recognition runs **Whisper on the user's PC** (whisper.cpp through Whisper.net). Settings
offers **every Whisper model** (tiny, base, small, medium, large-v3, large-v3-turbo with their English-only and quantized
variants), each with size, speed, accuracy and install state; a **Check my hardware** button recommends one; on first
install the check runs by itself and the recommended model is selected and installed. Defaults until the owner says
otherwise: language **Auto** with an explicit Language setting; **OpenAI cloud** as an opt-in second engine; **no
auto-stop**; a **5-minute cap**; "no speech" inserts nothing; a one-time privacy note per engine.

V-1.4 (OD-0096) A first-run **Getting ready** screen hosts the hardware check (and, by the owner's pick, the download and
self-test). Its design is pending; this spec defines the service it calls (V-10.7).

V-1.5 (OWNER OD-0097) Voice typing ships with the pill words **L1** (`Listening…`, `Transcribing…`, `Finishing…`), the
composer light **stepping aside** while you speak (**B1**: it fades out as listening starts, about 0.5 s, and returns
when the text is done, about 0.6 s: one light on the composer at a time) and the language default **A** (Auto-detect,
silent: Whisper detects the language each time and nothing is shown; this confirms OD-0092's provisional default).
The composer layout stays as OD-0091's sixth amendment locked it. Every alternative of the Voice Options page is a user
choice in Settings > Customization (SPEC 1 CZ-9.8 to CZ-9.11): the pill's words **L1 to L6** (V-12.9), the composer
light while you speak **B1 Steps aside, B2 Dims to 35 %, B3 Stays on** (V-13.4), and the language default **A
Auto-detect, silent; B Auto-detect, named in the pill; C the Windows display language; D asked on the first click of
the orb** in a popover with the Mac's arrow tip (V-7.14, V-12.10, V-12.11). The voice runtime reads them from
`voiceOrb` (V-16.1). The explicit language list stays on the Voice typing page (V-16.3.1).

V-1.6 (OWNER OD-0097 amendment, 2026-10-03: "Some sort of artifacts and that separator dot appears during voice pill,
make sure to fix that") While the pill is open nothing but the orb, the label and the cancel control is drawn: no
separator dot between the label's parts or beside the orb, and no stray pixels, halo fringes or seams around the orb
at any size or scale (V-12.12, tests V-T131 to V-T133).

---

## 2. Architecture

### V-2.1 Projects and files (ED; IMPLEMENTING 2.1, 2.3)

| Layer | Folder | Holds |
|---|---|---|
| `Marrow.Core` (no packages) | `src\Marrow.Core\Port\Voice\` | settings records and JSON (V-16), `ModelCatalog` (V-9.1), `Recommender` and `SelfTestPlan` (V-10), `PolyphaseResampler`, `LevelAnalyser` (V-5), `SpeechLabel` (V-6.3), `StreamingTranscriber` and `TranscriptOps` (V-7), `OrbMotion` (V-11.4), `VoiceGlowDriver` (V-13.2), `VoiceSession` (the state machine, V-15), the seam interfaces (V-2.4) |
| `Marrow.Services` (net10.0-windows; packages NAudio.Wasapi, Whisper.net + runtimes) | `src\Marrow.Services\Port\Voice\` | `WasapiCapture`, `CapturePipeline` (threads, ring buffer), `SileroVad`, `WhisperNetEngine`, `ModelStore` and `ModelDownloader`, `HardwareProbe`, `OpenAiTranscriber`, `VoiceKeyStore` |
| `Marrow.UI` | `src\Marrow.UI\Port\Voice\` | `VoiceOrbView` and `VoiceOrbShader` (V-11), `VoiceGlowView` and its shaders (V-13), `VoicePill`, `VoiceCross` (V-12), `DictationLayer` (V-8) |
| `Marrow.App` | `src\Marrow.App\Port\Voice\`, `src\Marrow.App\Port\Settings\VoiceTypingSettingsPage.cs` | composer wiring (V-14), `MicrophoneAccess` (WinRT `DeviceAccessInformation`, needs the app's 10.0.19041 TFM), the Voice typing page (V-16.3), first-run hook (V-10.7) |
| Edits | `ComposerView.cs`, `ComposerLightSlot.cs`, `ComposerLightView.cs`, `AppSettings.cs`, `SettingsView.cs` (SPEC 1 CZ-2) | V-14, V-13.4, V-16 |

### V-2.2 Threads (ED)
1. **Capture thread** (NAudio's event-driven WASAPI thread, MMCSS "Audio"): copies each buffer into a single-producer
   single-consumer ring (float, 2 s at the device rate). No allocation, no lock, nothing else.
2. **Voice processing thread** (one dedicated thread, normal priority, started per session): every 10 ms drains the ring;
   downmix, level and bands (V-5.3), resample to 16 kHz (V-5.2), VAD (V-6), append to the session's 16 kHz buffer,
   publish `Level`, `Bands` and `SpeechState` as volatile values.
3. **Inference thread** (one dedicated long-running thread, `BelowNormal` priority): model load, live passes and the
   final pass (V-7); whisper.cpp threads = `max(1, physicalCores - 2)` (RESEARCH 5).
4. **UI thread** (MainActor): reads the volatile values once per frame, receives transcript operations (V-7.5) through
   the main executor, coalesced to one batch per frame; drives the composer.
5. **Render thread**: the orb and glow draw on composition custom visuals (as `ComposerLightView`), fed immutable
   snapshots; only a few floats cross per frame.

### V-2.3 Data flow
`IAudioCapture` -> ring -> downmix -> `LevelAnalyser` (level, bands at device rate) and `PolyphaseResampler` (16 kHz) ->
`IVoiceActivityDetector` (512-sample frames) -> session buffer -> `StreamingTranscriber` (passes on `ISpeechEngine`) ->
`TranscriptOps` -> `DictationLayer` + draft. Level and bands -> `OrbMotion` and `VoiceGlowDriver` -> render snapshots.
`SpeechState` + word arrivals -> `SpeechLabel` -> pill label.

### V-2.4 Seams (interfaces in Core; every test binds fakes)
| Seam | Production | Test double |
|---|---|---|
| `IAudioCapture` (start in the default endpoint's mix format, stop, device events) | `WasapiCapture` | `ScriptedCapture`: PCM from WAV fixtures or synthesised signals, pushed on the manual clock; device-removed, format-change and all-zero scripts |
| `IVoiceActivityDetector` (`FrameSamples` = 512 at 16 kHz, `Probability(frame)`, `Reset()`) | `SileroVad` | `ScriptedVad` (probabilities by time) |
| `ISpeechEngine` (`LoadAsync(model, options)`, `TranscribeAsync(audio16k, DecodeRequest) -> Hypothesis`) | `WhisperNetEngine`, `OpenAiTranscriber` | `ScriptedEngine`: hypotheses as a function of the audio length decoded, with a scripted latency on the manual clock; throws on demand |
| `IModelStore` / `IModelTransport` (`GetRange(uri, offset)`) | `ModelStore` + `HttpClient` | a loopback HTTP server (R-TEST-10) serving fixture bytes with Range, 206/200/416/5xx, cut streams, wrong bytes |
| `IHardwareProbe` | `HardwareProbe` (DXGI, Win32, registry) | `ScriptedProbe` (the owner's desktop, a CPU-only laptop, a 4-core machine, a 2 GB VRAM card) |
| `IMicrophoneAccess` | `MicrophoneAccess` (WinRT) | fake Allowed / Denied / NoDevice |
| `ICredentialBackend` | `CredentialManagerBackend` (PORT SecretStore.cs) | the existing in-memory fake |
| `ISettingsStore` | the app's store | the existing fake |
| time | `SwiftClock.Provider` (`TimeProvider`) | the manual clock (R-TEST-5) |
| OpenAI HTTP | `HttpClient` with the real handler | a loopback server mimicking `/v1/audio/transcriptions` (JSON and SSE) |

V-2.5 **Fail closed** (owner memory "CLI seams fail closed"): production implementations are bound only in the app's
start-up composition. Every seam's ambient default in a test runtime is a stub that **throws** when touched, so a test
can never reach a real microphone, the network, a model file or the credential store by omission. One test per seam
asserts this.

V-2.6 **Feature flag (ED).** `VoiceFeature.Enabled` (internal constant, `false` until phase V7 merges) hides the orb in
the app's composers; tests run with it on. Listed in `porting\STATE.md` as incomplete while false. The Customization
preview (SPEC 1) does not read it.

---

## 3. Packages, native binaries, licences, notices

### V-3.1 NuGet packages (pinned in `Directory.Packages.props` by the orchestrator only; its header forbids worker edits)

| Package | Version | Licence | Used by | Note |
|---|---|---|---|---|
| `Whisper.net` | 1.9.1 | MIT (Copyright (c) 2024 sandrohanea) | Services | managed wrapper; targets net8/9/10 (RESEARCH 1.1, VERIFIED) |
| `Whisper.net.Runtime` | 1.9.1 | MIT | Services | CPU natives (needs AVX, AVX2, FMA, F16C) |
| `Whisper.net.Runtime.Vulkan` | 1.9.1 | MIT | Services | Vulkan natives, Windows x64 (+35.16 MB nupkg) |
| `NAudio.Wasapi` | 3.1.0 | MIT (Copyright 2020 Mark Heath) | Services | `WasapiRecorder` builder API; net9.0+ |
| transitive: `Microsoft.Extensions.AI.Abstractions` (>= 10.2.0, via Whisper.net), `NAudio.Core` (if NAudio.Wasapi 3.1.0 depends on it) | as resolved | MIT (UNVERIFIED) | | V0 lists the full transitive closure with licences |

Not taken: CUDA runtimes (`Whisper.net.Runtime.Cuda.Windows` 136 MB, `.Cuda12.Windows` 238 MB) unless the owner says so
(Q-V-9); `Whisper.net.Runtime.NoAvx` (Q-V-12); any OpenAI SDK (the cloud engine uses `HttpClient`, ED); ONNX Runtime
(Silero runs inside whisper.cpp, V-6.1).

### V-3.2 Native binaries and how they ship
- The Whisper.net runtime packages place their natives under `runtimes\win-x64\native\` (whisper.cpp and ggml DLLs:
  exact file names and sizes UNVERIFIED, recorded in V0 from the publish output). `dotnet publish -r win-x64` copies
  them; Velopack packs the publish folder as it does today (ED-235). No native file is downloaded at run time.
- Runtime order: Vulkan, then CPU (Whisper.net's default order, RESEARCH 1.2, restricted by `RuntimeOptions` to the
  shipped ones). The loaded runtime is reported to the hardware check (V-10.2).
- The Microsoft Visual C++ 2015-2022 x64 runtime is required (RESEARCH 1.2). V0 checks whether the installed app already
  carries it; if not, the Velopack pack command gains its `vcredist143-x64` prerequisite (an installer change the owner
  approves with the next packaging, never done without his word).
- Size budget: CPU + Vulkan natives under 50 MB unpacked (RESEARCH ESTIMATE; V0 measures and records).

### V-3.3 Model files (never shipped in the installer: OD-0092 "the selected model is downloaded")
Downloaded from Hugging Face `ggerganov/whisper.cpp` pinned to commit `5359861c739e955e79d9a303bcbc70fb988958b1`
(the commit the benchmark used and verified), URL `https://huggingface.co/ggerganov/whisper.cpp/resolve/<commit>/<file>`.
The manifest (V-9.1) embeds name, bytes and SHA-256 for every file; nothing is trusted from the server.

### V-3.4 The VAD model (bundled)
Silero VAD in whisper.cpp's ggml form (`ggml-silero-v6.2.0.bin` from `ggml-org/whisper-vad`, RESEARCH 6), about 1-2 MB,
MIT. Bundled with the app (`src\Marrow.Services\Port\Voice\Assets\`), copied to the output, SHA-256 pinned in a test.
File name, URL with commit, bytes and hash: UNVERIFIED, pinned in V0.

### V-3.5 The self-test clip (bundled)
One 10 s English speech clip, 16 kHz mono 16-bit WAV (about 320 KB), for the first-run timing (BENCH "First-run
self-test"). Its source and licence: UNRESOLVED (Q-V-5). Bundled and hash-pinned like V-3.4.

### V-3.6 THIRD_PARTY_NOTICES.md: the section to add (wording for the file)

```
## Voice typing

Voice typing (owner decisions OD-0091, OD-0092; ED-nnn) runs OpenAI's Whisper on the user's computer through whisper.cpp
and Whisper.net, captures the microphone with NAudio, detects speech with Silero VAD, and draws the voice orb and the
composer's voice glow from the two works credited last. Model files are downloaded on request from Hugging Face
(ggerganov/whisper.cpp, commit 5359861c739e955e79d9a303bcbc70fb988958b1) and are not part of the installer.

### OpenAI Whisper (model weights and reference code)
- Source: https://github.com/openai/whisper. Licence: MIT, "Copyright (c) 2022 OpenAI".
- The Hugging Face card of openai/whisper-large-v3 lists Apache-2.0 in its metadata while the repository states MIT;
  the Apache License 2.0 text is included below for the large-v3 files.
### whisper.cpp and ggml (native engine inside Whisper.net's runtimes; the ggml model files)
- Source: https://github.com/ggml-org/whisper.cpp. Licence: MIT, "Copyright (c) 2023-2026 The ggml authors".
### Whisper.net
- Source: https://github.com/sandrohanea/whisper.net (NuGet Whisper.net, Whisper.net.Runtime, Whisper.net.Runtime.Vulkan
  1.9.1). Licence: MIT, "Copyright (c) 2024 sandrohanea".
### NAudio
- Source: https://github.com/naudio/NAudio (NuGet NAudio.Wasapi 3.1.0). Licence: MIT, "Copyright 2020 Mark Heath".
### Silero VAD
- Source: https://github.com/snakers4/silero-vad (the ggml conversion distributed by ggml-org). Licence: MIT,
  "Copyright (c) 2020-present Silero Team".
### Liquid Orb Editor (LerSent001): the voice orb
- Source: https://github.com/LerSent001/orb. Licence: MIT, "Copyright (c) 2026 LerSent001". The port carries the site's
  Web export of the Siri Wave and Voice Membrane presets: their state seeds (136 values each), the export's runtime
  (applyAudioUniforms with audioRules and audioFlowStrengths, the sRGB transitions, the integrated motion phase) and the
  shader (effect.wgsl and shader-source.ts) translated to SkSL.
### voice-glow 0.2.1 (Jakub Antalik): the composer's voice glow
- Source: https://www.npmjs.com/package/voice-glow (github.com/Jakubantalik/Libraries.dev, packages/voice-glow).
  Licence: MIT, "Copyright (c) 2026 Jakub Antalik". The port re-implements its driver (envelope, flow, processing sweep,
  lobes, band polyline, hue drift), its theme presets, palettes and type presets, and its layer recipe.
### Microsoft.Extensions.AI.Abstractions (dependency of Whisper.net)
- Licence: MIT (verify in V0).
[full MIT texts for each; the Apache-2.0 text; and, if Q-V-5 picks a FLEURS clip, the CC BY 4.0 attribution]
```
Border-beam and the Uiverse button are already credited (ED-251). The design-folder `NOTICES.md` travels with this text.

---

## 4. Capture (NAudio WASAPI)

V-4.1 Open the **default capture endpoint** (`MMDeviceEnumerator.GetDefaultAudioEndpoint(Capture, Console)`) in shared,
event-driven mode with NAudio 3's `WasapiRecorder` builder, **in the device's own mix format** (never ask WASAPI to
convert: RESEARCH 3.2 item 1, three REPORTED silent-buffer cases on Windows 11 26200, the owner's build).

V-4.2 Accept float32 or PCM 16/24/32 at any rate and 1-8 channels; convert to float on the processing thread.

V-4.3 Start latency: capture starts on the click itself (before the model is loaded: V-7.9), so no speech is lost.

V-4.4 Device events (`IMMNotificationClient`): the default device changes or the device is removed mid-session -> the
session stops capture and runs the final pass on what it has (ED), then shows the device note (Q-V-7). Exclusive use by
another app (`AUDCLNT_E_DEVICE_IN_USE`) -> error note.

V-4.5 **Permission** (RESEARCH 3.3): before starting, `IMicrophoneAccess` (WinRT
`DeviceAccessInformation.CreateFromDeviceClass(AudioCapture).CurrentStatus`); denied -> the Blocked state (V-15) without
opening the device. Because Windows may hand a blocked desktop app pure digital silence instead of an error, the first
**1.0 s** of a session is also checked: if every sample is exactly 0.0, capture stops and the Blocked state shows.

V-4.6 Microphone choice: the Windows default input only (no device picker: not designed; Q-V-8).

V-4.7 Nothing is written to disk; the session buffer lives in memory and is cleared (zeroed and returned to the pool)
when the session ends.

---

## 5. Signal processing

V-5.1 **Downmix**: average of the channels.

V-5.2 **Resampler** (`PolyphaseResampler`, ED; RESEARCH 3.2 item 2): rational L/M polyphase FIR to 16 kHz (48 kHz: 1/3;
44.1 kHz: 160/441; others by their reduced ratio), passband to 7.2 kHz within 0.1 dB, stopband at 8 kHz and above at
least 60 dB down, linear phase; streaming output identical to one-shot output.

V-5.3 **Level and bands: the design's audio response** (DESIGN voice-composer.js:114-127 `fileRead`, which is
orb-audio.ts `read()`), computed on the device-rate mono signal at a fixed 60 Hz cadence (ED: the design reads it per
animation frame):
- an analyser equal to Web Audio's `AnalyserNode` with `fftSize` 2048 and `smoothingTimeConstant` 0.65: the last 2048
  samples; Blackman window (a0 0.42, a1 0.5, a2 0.08); magnitude / 2048; smoothing `X = 0.65 X_prev + 0.35 |X|`;
  `dB = 20 log10(X)`; byte = `floor(255 * (dB + 100) / 70)` clamped 0..255 (minDecibels -100, maxDecibels -30);
- `all = min(1, max(0, rms - 0.004) * 5.5)` with `rms` the root mean square of the 2048 samples;
- `low`, `mid`, `high` = the mean byte over the bins for 30-200 Hz, 200-2000 Hz, 2000-16000 Hz (bins clamped to Nyquist),
  `/ 255 * 2`, clamped to 1; all zero when `all` is 0.

V-5.4 **Pauses return to rest (ED, OWNER OD-0091 first amendment).** The `rms` above first subtracts the noise floor:
the rolling 2 s minimum of 50 ms RMS windows (RESEARCH 3.2 item 3), so a noisy room does not hold the orb above its
resting speed.

V-5.5 **Followers** (DESIGN voice-composer.js:718-722): per band `follow(x, raw, dt, 0.07, 0.24)` (attack, release,
seconds; `follow` = `x + (t - x)(1 - exp(-dt / tau))`, `tau` = up when rising, down when falling), values under 1e-4 set
to 0; the orb's voice value `v = follow(v, bands.all, dt, 0.12, 0.45)`. These run on the UI thread per frame from the
published raw values.

V-5.6 **Zero-signal check**: V-4.5. **Low-level hint**: none (not designed).

---

## 6. Voice activity (Silero VAD) and the Listening / Transcribing label

V-6.1 `SileroVad` runs Silero v6 through whisper.cpp's VAD (Whisper.net 1.9.1 "Silero VAD factory and processor",
RESEARCH 6, VERIFIED as present; the per-frame probability API is UNVERIFIED). If Whisper.net does not expose per-frame
probabilities, P/Invoke whisper.cpp's C API in the same native library (`whisper_vad_init_from_file_with_params`,
`whisper_vad_detect_speech`, `whisper_vad_n_probs`, `whisper_vad_probs`, `whisper_vad_free`) (ED). Frames of 512
samples at 16 kHz (32 ms); under 1 ms each (RESEARCH VERIFIED).

V-6.2 **Speech state** (ED, Silero's own hysteresis): enters speech when p >= 0.5; leaves when p < 0.35 for 100 ms.

V-6.3 **The pill's label** (DESIGN voice-composer.js:385-393, 753-756; OWNER fourth amendment):
- `speechAt = now` whenever the speech state is on (V-6.2) or a word arrived less than 0.35 s ago;
- label = `Transcribing…` while `now - speechAt < 0.6 s`, else `Listening…`; the session starts on `Listening…`;
- `Finishing…` from the second click until the final pass completes.
(The three states are `listening`, `transcribing`, `finishing`; the words drawn for each come from the word set the
user picked, `voiceOrb.labels`, default L1 as written above: V-12.9. The rule is the same for every set.)
(The design used the level `v > 0.16` in place of the VAD; the owner's brief names Silero for this: ED.)

V-6.4 The VAD also gates recognition: a live pass never runs on audio without speech (V-7.4), and a session in which the
VAD never saw speech ends with "No speech was heard" (V-15).

---

## 7. Recognition: live streaming and the final pass

V-7.1 **Engine** (`WhisperNetEngine`): one model loaded at a time (process-wide), from the model store's verified path;
`WhisperFactory` from path with GPU on when a GPU runtime loaded; per pass a processor built with: language (forced code
or auto-detect), threads (V-2.2), sampling (live: greedy, best-of 1; final: beam search, beam 5: ED; BENCH measured
beam), initial prompt (V-7.3), token timestamps on, temperature 0 with the default fallback. Exact Whisper.net builder
member names: UNVERIFIED, recorded by V0.

V-7.2 **Words from segments**: a token beginning with a space starts a word; a word's start and end are its first and
last token's timestamps; punctuation stays attached. Scripts written without spaces yield one word per token run (ED).

V-7.3 **Live passes: LocalAgreement-2 over a sliding window** (ED; the policy of whisper_streaming, the sliding-window
re-decode of whisper.cpp's stream example, RESEARCH 1.6):
1. The **uncommitted buffer** is the session audio from the end of the last committed word (minus 0.1 s) to now.
2. A pass starts when the previous pass is done, at least **0.5 s** of new audio has arrived since the last pass began,
   and the VAD saw speech in the uncommitted buffer. It decodes the whole uncommitted buffer (never more than 30 s:
   V-7.4) with the prompt = the last 200 characters of committed text.
3. The new hypothesis `H_new` is compared with the previous `H_prev` word by word; the **longest common prefix** of the
   two is **committed** (solid); the rest of `H_new` is the **tentative tail** (drawn lighter).
4. Committed words never change again (no jumps); only the tentative tail is revised.

V-7.4 **Window management** (ED):
- long pause: when the VAD has been out of speech for **1.5 s** after speech, a pass runs on the uncommitted buffer and
  **all** its words are committed; the silent audio is dropped from the buffer (silence never reaches Whisper: RESEARCH
  6);
- buffer cap: if the uncommitted buffer exceeds 25 s without a commit, the tentative words ending more than 10 s back
  are committed and the buffer is cut at the end of the last one;
- whisper.cpp decodes a fixed 30 s window (RESEARCH 1.4), so the buffer never exceeds 30 s.

V-7.5 **UI operations** (`TranscriptOps`, from the design's live words, voice-composer.js:221-303): the difference between
the shown tail and the new tail as a list of operations, applied on the UI thread:
- `Add(word, tentative)`: the word rises in (opacity 0.3 s, translate Y 6 -> 0 over 0.4 s ez); successive adds at least
  60 ms apart; adding a word confirms the previous one;
- `Revise(index, text)`: the old text lifts away (opacity 0.22 s, translate Y 0 -> -6 over 0.3 s ez) while the new rises
  in from +6; the word's box width eases over 0.3 s ez; later words follow their new positions with the same ease
  (FLIP, ED, so a wrap never jumps);
- `Remove(index)` (a tail word the new hypothesis dropped; not in the design, ED): the withdraw motion of V-8.6;
- `Confirm(index)`: tentative colour to solid over 0.45 s.
(ez = cubic-bezier(.32,.72,0,1).)

V-7.6 **Final pass** (second click, the cap, or a stop event): capture stops (the last 30 ms flushed); the uncommitted
buffer plus 0.3 s of silence (ED) is decoded with the final parameters; its words replace the tentative tail. The design's
schedule (voice-composer.js:304-324): starting 0.18 s after the result, adds 0.07 s apart and revisions 0.12 s apart; the
last word's final form (its punctuation) arrives with the final pass. The Processing state lasts
`max(0.75 s, scheduleEnd + 0.3 s)` from the click (`finalMin` 0.75: DESIGN K) so it always reads.

V-7.7 **Live words when the PC is too slow.** BENCH proposes: self-test RTF <= 0.15 live words on; 0.15 < RTF <= 0.5
live words off by default (text after the stop); RTF > 0.5 step down. This conflicts with OD-0091's "live" ruling for
slow machines: UNRESOLVED (Q-V-1). The code carries one internal switch `LiveWords` from the hardware record (V-10.5) and
both paths are built and tested; with live words off the pill still shows Listening/Transcribing from the VAD and the
words arrive in the final pass by its schedule.
RESOLVED (lead ruling 2026-10-04): by OD-0099 Q-V-1 C (the amendment of 2026-10-04) and engineering_decisions.md
ED-289 item 13 (rulings R1 to R7). A timing on a working GPU backend stays live with the model, never stepped down
or asked. A processor timing too slow for live words with a smaller rung on the model's ladder keeps the model and asks
once (also after the one step down); with no smaller rung the model is kept with live words off and nothing is asked.
The answer is `voiceTyping.liveWordsChoice` with the hardware part of the key it was given under
(`liveWordsChoiceKey`: adapter, driver, processor, memory; not the app build or the runtime), so the question comes back
on new hardware only; `live` takes the next smaller rung with live words on (not timed again), `accurate` keeps the
model with text after the stop; a dismissed question stores nothing and is asked again at the next self-test, and
until it is answered the accurate model is kept with live words off (R8, OD-0099's welcome-voice pick).

V-7.8 **No hallucinated text** (RESEARCH 6): no pass on VAD-silent audio; a session whose VAD never saw speech never calls
the engine; the final pass drops segments whose words all lie in VAD-silent spans (ED).

V-7.9 **Model residency** (ED; RESEARCH 9 item 12): loading starts on the orb's pointer-enter or the click, whichever
comes first, on the inference thread; capture and VAD run meanwhile; words begin when the model is ready. The model is
unloaded after 5 minutes without a session. A load failure ends the session with the engine note (Q-V-7).

V-7.10 **Cancellation**: Esc, the cross, or a cancel event cancels the pass in flight (CancellationToken into the engine's
abort callback) and discards the session's audio and words.

V-7.11 **One session at a time**: a click on another composer's orb first finishes the running session (as a second
click) and then starts the new one (ED).

V-7.12 **The 5-minute cap** (OWNER OD-0092): at 300 s of listening the session stops as if the orb were clicked (final
pass, text kept). Warning before the cap: not designed (Q-V-6).

V-7.13 **Language**: the session's language comes from `VoiceLanguage.Resolve` (V-7.14): forced -> the Whisper code;
auto -> auto-detect. The detected language is shown only under the language default B (V-12.10); otherwise nothing
names it (OWNER OD-0097 A, the default).

V-7.14 **The language default** (OWNER OD-0097; SOURCE voice-options.html Q3, voice-options.js `askLang`, `answerLang`,
`setLang`; stored as `voiceOrb.langMode`, V-16.1). `VoiceLanguage.Resolve(voiceTyping.lang, langMode, windowsUiLanguage)`
(Core, pure) returns `(language: code | auto, named: bool, ask: bool)`:
1. `voiceTyping.lang` is a Whisper code (picked on the Voice typing page, or the answer to D): forced to it; `named`
   false.
2. `voiceTyping.lang` is `auto` (Auto-detect picked on the Voice typing page): auto-detect; `named` = (`langMode` is
   `show`) (ED: naming belongs to B whenever it auto-detects).
3. `voiceTyping.lang` is `null` (nothing picked: the default, V-16.2), by `langMode`:
   - `auto` (A, the default): auto-detect, nothing shown;
   - `show` (B): auto-detect, `named` true (V-12.10);
   - `windows` (C): forced to Windows' display language, read at each session start from `GetUserDefaultUILanguage`
     mapped through `CultureInfo(lcid).TwoLetterISOLanguageName` (ED), when that code is in V-16.2's list; otherwise
     auto-detect, nothing shown (ED: a language Whisper is not offered for here is never forced);
   - `ask` (D): `ask` true: the click that would start listening opens the language question instead (V-12.11).
Picking any option of the Customization row (also the one already picked) sets `voiceTyping.lang` back to `null`, so
the default just chosen applies at once and D asks again at the next first click (ED, after the page's `setLang`, which
re-arms the question; the latest choice wins over a language picked earlier on the Voice typing page). An explicit pick
on the Voice typing page afterwards overrides the default again (rules 1 and 2). The cloud engine sends `language` only
for a forced language (V-18.1).

---

## 8. The words in the composer

V-8.1 **Insertion point** (ED, RESEARCH 4): the caret; a selection is replaced when the first word arrives (one edit);
with no words the draft is untouched. A space is inserted before the first word when the preceding character is not
whitespace and the word does not begin with punctuation, and after the last word when the following character is not
whitespace or punctuation. The caret ends after the inserted text.

V-8.2 **The draft follows live**: committed and tentative words are written into the draft as they change, so the
composer is in its typing state (`DraftIsEmpty` false -> the composer light's Typing phase, the send button lit, height
growth): OWNER third amendment.

V-8.3 **Drawing** (`DictationLayer`, ED): while a session runs, the editor (`ComposerNSTextView`, a `TextBox`) keeps the
text but draws it transparent; the layer, laid exactly over the editor's text area, draws the editor's own
`TextLayout` (same font, width, wrapping, flow direction, alignment, scroll offset) with the dictated runs animated per
V-7.5 and V-8.6 and a caret after the last word (1.5 px wide, 17 px high, the text colour, blinking 1.06 s: DESIGN
voice-composer.html:130-131). The text before and after the insertion is drawn still. When the session lands, the editor
draws again and the layer is removed in the same frame: pixel-identical hand-over (test).

V-8.4 **Tentative colour**: `wink3` (the text colour at 0.6; DESIGN voice-composer.html:124); solid: the text colour.

V-8.5 **The editor during a session** (ED; Q-V-10): read-only (typing ignored), selection frozen, Esc cancels; focus is
not stolen if the composer was not focused.

V-8.6 **Withdraw on cancel** (DESIGN voice-composer.js:325-343): this session's words leave from the end, one every 30 ms,
each fading 0.26 s and moving down 5 px over 0.3 s ez; text that was there before stays; the draft returns to its
pre-session text and selection.

V-8.7 **Landing** (DESIGN voice-composer.js:427-437): tentative words ink to solid 25 ms apart; one undo step covers the
whole insertion (live edits do not record undo states; ED); the caret stays after the text.

V-8.8 **After voice input** (OWNER first amendment; `voiceOrb.afterVoice`): `leave` keeps the text unsent (default);
`send` sends **0.35 s after landing** (DESIGN voice-composer.js:704-708) through the composer's own send action. If a turn
is running at that moment: Q-V-11.

V-8.9 **Right-to-left** (RESEARCH 1.5: Urdu, Arabic, Persian): the layer uses the editor's bidi `TextLayout`; words in RTL
runs animate in place. Test with an Urdu fixture.

V-8.10 Multi-line: the composer grows by its existing height animation; when the text exceeds the editor's height the end
stays in view, scrolled with a 0.36 s ez ease (DESIGN `.tin` transform), never a jump.

---

## 9. The models

### V-9.1 Catalogue (OWNER OD-0092 amendment: Whisper only; DESIGN voice.js:8-17; files and hashes from
`bench\scripts\hf_tree.json` at commit 5359861c…)

Display names: `Tiny`, `Base`, `Small`, `Medium`, `Large v3 Turbo`, `Large v3`. Version names: q5 -> `Compact`, q8 ->
`Balanced`, f16 -> `Full`. Speed / accuracy dots (of 5): Tiny 5/1, Base 5/2, Small 4/3, Medium 2/4, Large v3 Turbo 4/5,
Large v3 1/5. `English only` exists for Tiny, Base, Small, Medium.

| Id (file stem) | Model | Version | En | Bytes | SHA-256 |
|---|---|---|---|---|---|
| tiny-q5_1 | Tiny | Compact | | 32152673 | 818710568da3ca15689e31a743197b520007872ff9576237bda97bd1b469c3d7 |
| tiny.en-q5_1 | Tiny | Compact | yes | 32166155 | c77c5766f1cef09b6b7d47f21b546cbddd4157886b3b5d6d4f709e91e66c7c2b |
| tiny-q8_0 | Tiny | Balanced | | 43537433 | c2085835d3f50733e2ff6e4b41ae8a2b8d8110461e18821b09a15c40c42d1cca |
| tiny.en-q8_0 | Tiny | Balanced | yes | 43550795 | 5bc2b3860aa151a4c6e7bb095e1fcce7cf12c7b020ca08dcec0c6d018bb7dd94 |
| tiny | Tiny | Full | | 77691713 | be07e048e1e599ad46341c8d2a135645097a538221678b7acdd1b1919c6e1b21 |
| tiny.en | Tiny | Full | yes | 77704715 | 921e4cf8686fdd993dcd081a5da5b6c365bfde1162e72b08d75ac75289920b1f |
| base-q5_1 | Base | Compact | | 59707625 | 422f1ae452ade6f30a004d7e5c6a43195e4433bc370bf23fac9cc591f01a8898 |
| base.en-q5_1 | Base | Compact | yes | 59721011 | 4baf70dd0d7c4247ba2b81fafd9c01005ac77c2f9ef064e00dcf195d0e2fdd2f |
| base-q8_0 | Base | Balanced | | 81768585 | c577b9a86e7e048a0b7eada054f4dd79a56bbfa911fbdacf900ac5b567cbb7d9 |
| base.en-q8_0 | Base | Balanced | yes | 81781811 | a4d4a0768075e13cfd7e19df3ae2dbc4a68d37d36a7dad45e8410c9a34f8c87e |
| base | Base | Full | | 147951465 | 60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe |
| base.en | Base | Full | yes | 147964211 | a03779c86df3323075f5e796cb2ce5029f00ec8869eee3fdfb897afe36c6d002 |
| small-q5_1 | Small | Compact | | 190085487 | ae85e4a935d7a567bd102fe55afc16bb595bdb618e11b2fc7591bc08120411bb |
| small.en-q5_1 | Small | Compact | yes | 190098681 | bfdff4894dcb76bbf647d56263ea2a96645423f1669176f4844a1bf8e478ad30 |
| small-q8_0 | Small | Balanced | | 264464607 | 49c8fb02b65e6049d5fa6c04f81f53b867b5ec9540406812c643f177317f779f |
| small.en-q8_0 | Small | Balanced | yes | 264477561 | 67a179f608ea6114bd3fdb9060e762b588a3fb3bd00c4387971be4d177958067 |
| small | Small | Full | | 487601967 | 1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b |
| small.en | Small | Full | yes | 487614201 | c6138d6d58ecc8322097e0f987c32f1be8bb0a18532a3f88f734d1bbf9c41e5d |
| medium-q5_0 | Medium | Compact | | 539212467 | 19fea4b380c3a618ec4723c3eef2eb785ffba0d0538cf43f8f235e7b3b34220f |
| medium.en-q5_0 | Medium | Compact | yes | 539225533 | 76733e26ad8fe1c7a5bf7531a9d41917b2adc0f20f2e4f5531688a8c6cd88eb0 |
| medium-q8_0 | Medium | Balanced | | 823369779 | 42a1ffcbe4167d224232443396968db4d02d4e8e87e213d3ee2e03095dea6502 |
| medium.en-q8_0 | Medium | Balanced | yes | 823382461 | 43fa2cd084de5a04399a896a9a7a786064e221365c01700cea4666005218f11c |
| medium | Medium | Full | | 1533763059 | 6c14d5adee5f86394037b4e4e8b59f1673b6cee10e3cf0b11bbdbee79c156208 |
| medium.en | Medium | Full | yes | 1533774781 | cc37e93478338ec7700281a7ac30a10128929eb8f427dda2e865faa8f6da4356 |
| large-v3-turbo-q5_0 | Large v3 Turbo | Compact | | 574041195 | 394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2 |
| large-v3-turbo-q8_0 | Large v3 Turbo | Balanced | | 874188075 | 317eb69c11673c9de1e1f0d459b253999804ec71ac4c23c17ecf5fbe24e259a1 |
| large-v3-turbo | Large v3 Turbo | Full | | 1624555275 | 1fc70f774d38eb169993ac391eea357ef47c88757ef72ee5943879b7e8e2bc69 |
| large-v3-q5_0 | Large v3 | Compact | | 1081140203 | d75795ecff3f83b5faa89d1900604ad8c780abd5739fae406de19f23ecd98ad1 |
| large-v3 | Large v3 | Full | | 3095033483 | 64d182b440b98d5203c4f9bd541544d84c605196c4f7b845dfa11fb23594d1e2 |

File name = `ggml-<id>.bin`. 29 files; large-v1, large-v2 and their quantizations are not offered (OD-0092 lists
large-v3 and large-v3-turbo). Notes per model (DESIGN voice.js:147, first match wins): Medium and Large v3 `Can also
translate speech into English.` (Q-V-14: no translate option exists); Large v3 Turbo `About as accurate as Large v3, far
quicker; it does not translate.`; Tiny, Base, Small `English only is a little more accurate for English and knows no other
language.`

### V-9.2 Sizes as shown (DESIGN voice.js:32): `MB = bytes / 1,000,000`; shown as `fmt(MB / 1000, 2) + ' GB'` when MB >=
1000, else `round(MB) + ' MB'` (`fmt` and `round` as SPEC 1 CZ-12.10). Examples: 32 MB, 44 MB, 574 MB, 874 MB, 1.53 GB,
1.62 GB, 3.1 GB.

### V-9.3 Storage (RESEARCH 2)
`<data root>\models\whisper\` with the data root from `StoragePaths` (PORT; `%LOCALAPPDATA%\Marrow Code` in production,
the dev root otherwise, `MARROW_PORT_DATA_DIR` in tests). An `installed.json` beside the files records id, bytes, SHA-256,
verified time and file time.

### V-9.4 Install state per id: `NotInstalled` | `Downloading(fraction)` | `Installed` | `InUse` (installed and selected).
At start the store trusts `installed.json` only when the file's size and time match; otherwise the file is re-hashed
on the inference thread before it is loaded (never at start-up on the UI thread).

### V-9.5 Download (`ModelDownloader`, ED from RESEARCH 2)
1. GET the pinned URL to `<file>.partial`; with an existing `.partial`, resume with `Range: bytes=<length>-`; a `200`
   reply to a range request restarts from 0; `416` with a full-length partial goes to verification.
2. Progress events at most 10 per second: received bytes, total, fraction.
3. On completion: size must equal the manifest; SHA-256 (streamed while writing, re-hashed if resumed) must equal the
   manifest; then an atomic rename to `ggml-<id>.bin` and an `installed.json` update. A mismatch deletes the partial and
   reports `Corrupt`; the file is never loaded.
4. Cancel (the row's x) stops the transfer and **keeps** the `.partial` so a later Download resumes (ED).
5. Several downloads may run at once (DESIGN voice.js:66-98: one progress per row).
6. Network errors: retried 3 times with 2 s, 4 s, 8 s back-off on the manual clock, then `Failed` with the row back to
   `Download` (error wording: Q-V-7).
7. HTTP: `HttpClient` through the `IModelTransport` seam; redirects followed (Hugging Face serves from a CDN); no
   credentials sent.

### V-9.6 Model name (DESIGN voice.js:33): `ModelName(id) = <Model> + (en ? ' (English only)' : '') + ', ' +
<version lower case> + ' ' + <size>`, e.g. `Large v3 Turbo, balanced 874 MB`, `Small (English only), compact 190 MB`.

### V-9.7 Removal: not designed (Q-V-13). Total installed: shown in the list footer (V-16.3.6).

---

## 10. Hardware check, recommendation, first-run self-test (OWNER OD-0092, OD-0096; BENCH)

V-10.1 **Probe** (`HardwareProbe`, each step reported as it completes; no fake delays: OD-0096 "real progress"):
| Row | Source | Shown as (DESIGN voice.js:21-24) |
|---|---|---|
| `Processor` | `ProcessorNameString` (HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0), physical cores from `GetLogicalProcessorInformationEx(RelationProcessorCore)` | `{name}, {cores} cores` |
| `Memory` | `GlobalMemoryStatusEx.ullTotalPhys` | `{floor(bytes / 2^30)} GB` (ED: the usable figure; the owner's 32 GB PC is recorded as 31 GB in OD-0092 and the design) |
| `Graphics` | DXGI `EnumAdapters1`, skipping software adapters; the adapter with the most dedicated memory; free memory from `IDXGIAdapter3.QueryVideoMemoryInfo(LOCAL)` (budget - usage); driver version from the adapter's registry key | `{description}, {round(dedicated / 2^30)} GB`, or `{description}, shared memory` when it has under 512 MB dedicated |
| `GPU backend` | which Whisper.net runtime loaded (V-3.2), confirmed by a tiny load test of the VAD model on it (ED) | `{Vulkan or CUDA}, working`, or `None working, runs on the processor` |

V-10.2 **Candidate** (BENCH "Draft recommendation table"; the language from V-16.2: English when forced to English,
otherwise multilingual):
| Found | Candidate |
|---|---|
| a working GPU backend on an NVIDIA GPU with >= 3 GB free | `large-v3-turbo-q8_0` |
| a working GPU backend otherwise (less VRAM, AMD, Intel) | `large-v3-turbo-q5_0` |
| CPU only, >= 8 physical cores, English | `small.en-q8_0` |
| CPU only, >= 8 physical cores, multilingual | `large-v3-turbo-q5_0` (self-test decides; fallback `medium-q8_0`) |
| CPU only, < 8 physical cores, English | `base.en` |
| CPU only, < 8 physical cores, multilingual | `small-q5_1` |
Never recommended (BENCH "Dominated"): `large-v3-q5_0`, the `medium.en` family, tiny/base for non-English, the f16
versions of small, medium and turbo. `large-v3` (f16) only as a user's own choice.

V-10.3 **Self-test** (BENCH): with the candidate installed, a warm-up load, then one timed transcription of the bundled
10 s clip (V-3.5) with the live parameters; `RTF = seconds / 10`.
- `RTF <= 0.15`: keep, live words on;
- `0.15 < RTF <= 0.5`: keep; live words per Q-V-1;
- `RTF > 0.5`: step down one rung, install it if needed, time again **once only**: multilingual ladder
  `large-v3-turbo-q8_0 -> large-v3-turbo-q5_0 -> medium-q8_0 -> small-q8_0 -> small-q5_1`; English ladder
  `small.en-q8_0 -> base.en -> tiny.en`.
The thresholds are BENCH proposals, not measurements (BENCH says so); kept as named constants.
Lead ruling 2026-10-04 (R1, from the OD-0099 Q-V-1 C amendment "a fast PC (every GPU) is never asked and stays live
with the accurate model"): the step down and the live-words bands above apply to the processor basis only; a timing on
a working GPU backend keeps the model with live words on at any RTF. The question and its answer: V-7.7.

V-10.4 **Reason text**: UNRESOLVED wording (Q-V-4). Proposed templates from the design's two examples (voice.js:22, 24),
filled with measured numbers: GPU `Your {GPU name} runs it on the graphics card: text about {t} seconds after you stop
speaking.`; CPU `On the processor alone {Model} answers in about {t} seconds; {Bigger model} would take {t2} seconds or
more.` where `t` = RTF x 10 rounded to 0.5 s (and `t2` from the rung above, measured or omitted).

V-10.5 **Stored result** (`voiceHardware`, V-16.4) keyed by a hash of: adapter description and driver version, CPU name,
RAM GB, app version and build, Whisper.net runtime version. A mismatch at start marks the result stale: the check runs
again at the next first-run moment (V-10.7), never mid-session.

V-10.6 **Check my hardware** (Voice typing page, V-16.3.4): probe rows resolve in order, then the recommendation folds
open. It does not download anything by itself. `Use recommended` installs the candidate (if needed), runs the self-test,
and selects the result (stepping down per V-10.3 and updating the card).

V-10.7 **First run** (OWNER OD-0092 default "check on first install, select and install automatically"; OD-0096): the
`VoicePreparation` service the Getting ready screen calls:
- `ProbeAsync(progress, ct)` (step id `voice.probe`): the four rows; always on the Getting ready screen;
- `RecommendAsync()`: V-10.2;
- `InstallAndTimeAsync(id, progress, ct)` (steps `voice.download`, `voice.verify`, `voice.selftest`): where it runs is the
  owner's pick on the Getting ready canvas's Downloads board (D1 on the screen, D2 on first use, D3 behind Welcome;
  D3 recommended there): Q-V-2;
- `IsPrepared(out record)`; progress items `(Id, Label, Fraction?, State)`; the labels are the Getting ready Words board's
  pick (pending).
Until OD-0096's screen is built, V7 runs `ProbeAsync` + `RecommendAsync` at the first composer appearance and
`InstallAndTimeAsync` in the background (the D3 behaviour), so voice is usable as soon as it finishes.

---

## 11. The orb (exact export port)

V-11.1 **What is carried, verbatim** (OWNER fourth amendment: "the EXACT site exports, never a re-creation"; DESIGN
voice-orbs.js:1-35, NOTICES.md): the two presets' `stateSeeds` (136 floats each, idle and thinking: Siri Wave = style 9,
Voice Membrane = style 19) byte-identical to `reference\orb-exports\seeds.json`; the export runtime: `audioRules`,
`audioFlowStrengths`, `applyAudioUniforms`, `srgbToLinear`, `linearToSrgb`, `mixSrgb`, `sampleTransition` (index >= 40
and `(index - 40) % 4 < 3` mixed in linear light, the rest linearly), the integrated motion phase
(`phase += dt * max(values[3], 0)`; `values[2] = phase / max(values[3], 0.001)`); and the fragment shader (voice-orbs.js
`FRAG`, itself a function-by-function WGSL translation) translated to **SkSL** function by function with the same names.
Only the two presets' branches are carried.

V-11.2 **Marrow's state layer** (voice-orbs.js:16-23, 495-520), exactly as the design:
- framing: `values[4] = (ball / size) * scale`; `values[16] = clamp((1.5 / max(radPx, 1) - 0.01) / 2, 0.005, 0.15)`
  with `radPx = values[4] * rdev / 2`;
- speed: `values[3] = speed * idleSeed[3]` (rest speed = the preset's Idle speed: Siri Wave 0.246, Voice Membrane 0.266);
- glow: `values[17] = glow` (the source's Outer Glow);
- `mrw = (lift, desat, dark ? 1 : 0, 0)`: the soft lift shadow and the error desaturation terms (zero when 0);
- output is premultiplied (the shader multiplies colour by its fit alpha), as Skia expects.

V-11.3 **Rendering** (ED, after `ComposerLightView`): `VoiceOrbView`, a composition custom visual, 72 x 72 view px
centred on the orb slot (room for the glow and the pop), drawing the ball at `D` px; the shader runs
into a reused offscreen surface at 2x and is filtered down (voice-orbs.js:493-497, below 128 device px; the port at every scale,
ED-NEW-voicefix-ui-7); no per-frame allocation; one 136-float
uniform block per frame (SkSL `uniform float4 u[34]` or named members: the builder's choice, with a layout test); frames
only while the view is visible and something moves (always true at rest, since the orb moves slowly; none when the
window is minimised or occluded); no GPU (software rasterizer): one still frame as under reduce motion (ED-251 rule).
Y origin: Skia's top-left against WebGL's bottom-left (the shader's `1.0 - inUv.y`): settled by the fidelity test.
The two programs are added to `GpuWarmup` (ED-252) so the Getting ready screen compiles them; if a program fails on
ANGLE/D3D11 (ED-233), split it into passes as the jewel orb did.

V-11.4 **Motion** (`OrbMotion`, DESIGN voice-composer.js:15-25, 725-773). Constants `K`:
`restBase 1.0, restAct 0, hoverSpeed 2.0, voiceSpeed 2.2, procSpeed 4.6, errSpeed 0.6, hoverAct 0.3, hoverScale 1.04,
listenScale 1.16, voiceScale 0.04, procScale 1.1, pressScale 0.94, hoverGlow 0.55, listenGlow 0.45, voiceGlow 0.4,
procGlow 0.75, actUp 0.22, actDown 0.65, vAttack 0.12, vRelease 0.45, popResp 0.3, popDamp 0.62, settleResp 0.32,
settleDamp 0.78, hoverResp 0.22, hoverDamp 0.8, stagger 0.026, sendHold 3.0, finalMin 0.75`.

Targets per state (hover only counts at Rest or Landing):

| State | speed | act | scale (spring response, damping) | glow | lift | desat |
|---|---|---|---|---|---|---|
| Rest | 1 | 0 | 1 (0.32, 0.78) | 0 | 0 | 0 |
| Hover | 2.0 | 0.3 | 1.04 (0.22, 0.8) | 0.55 | 0.15 | 0 |
| Listening | 1 + 2.2 v | 1 | 1.16 + 0.04 v (0.3, 0.62) | 0.45 + 0.4 v | 1 | 0 |
| Processing | 4.6 | 1 | 1.1 (0.3, 0.75) | 0.75 | 0.8 | 0 |
| Blocked | 0.6 | 0 | 1 (0.32, 0.78) | 0.15 if hovered, else 0 | 0 | 1 |
| Pressed (any but Processing) | | | 0.94 x (1.16 if Listening else 1) (0.18, 0.8) | | | |

Integrators: `act` uses the export's ramps (export `transitionProgress`): toward a higher value ease-out cubic over 0.22 s,
toward a lower value smoothstep over 0.65 s, each from the value shown when the target changed. `speed`: follow with
tau 0.05 up and down while Listening, else 0.2 up, 0.5 down. `glow`: 0.12 / 0.25. `lift`: 0.12 / 0.3. `desat`: 0.117 /
0.2. `scale`: SwiftUI `spring(response, dampingFraction)` integrated in 4 ms substeps (voice-composer.js:130-134). The
slot size `D` springs to the setting (response 0.34, damping 0.86) and the slot follows it. While Listening the export's
`applyAudioUniforms` takes the smoothed bands (V-5.5); otherwise none. Reduce motion: the phase stops, every integrator
lands at once.

V-11.5 **Orb choice** (`voiceOrb.orb`): both orbs stay ready; a change crossfades the two views (follow tau 0.12 s,
snapped within 0.004) and the new orb takes the old one's phase (voice-composer.js:369-372, 763-768).

V-11.6 **Colours**: the presets' own colours in both themes ("the orbs keep the source's colours exactly", voice-orbs.js
`recolor()` is a no-op); `setDark` only changes the lift shadow's strength.

V-11.7 Fidelity: reference frames rendered from `voice-orbs.js` (WebGL2, ANGLE D3D11) in headless Chrome by a fixture
script (`tests\fixtures\voice\ref-orbs.js`, as `composer-lights\ref.js`) at fixed times and states; the port renders the
same jobs; per-channel difference <= 2/255 at >= 99.5 % of pixels (ED; the WebGL port itself was within 1/255 of the
WebGPU export).

---

## 12. The pill, the cross, the floating pill (OWNER fourth, fifth, sixth amendments)

V-12.1 **Styles** (`voiceOrb.lstyle`): `pill` = Pill in the composer (default), `cross` = Cross beside the orb, `float` =
Floating pill (DESIGN voicecmp.js:13; voice-composer.js:32-39).

V-12.2 **Pill geometry** (DESIGN voice-composer.js:39, 811-834): inline `H 40`, `font 13.5`; float `H 44`, `font 15`.
Open width `W = H + 8 + labelWidth + 8 + 24 + 8` (labelWidth = the label's measured width at that font, weight 600,
rounded up); closed width `H` (a disc around the orb). Inline: vertically centred on the orb's centre, right end fixed at
`orbCentreX + H/2`, opening to the left. Float: top `-52` from the composer's top, left end at `orbCentreX - H/2`, opening
to the right; the orb lifts by `flt * (-52 + H/2 - 26) = -56 flt` px, `flt` a spring to 1 while open (response 0.36,
damping 0.8) and back to 0 (0.34, 0.86).

V-12.3 **Pill contents** (voice-composer.html:154-197): the cross in its own 24 px glass disc at the near end (inline:
left 8; float: right 8), icon 11 px, stroke 1.5; the label at left 40 (inline) / 52 (float), one of `Listening…`,
`Transcribing…`, `Finishing…` (U+2026) with the default word set, else the picked set's words (V-12.9). Label text sweep: a 105 deg gradient `pgLab` 0-38 %, `pgLabHi` 49 %, `pgLab`
60-100 %, 260 % wide, moving from 135 % to -55 % over 2.35 s ease-in-out (held at 0-12 % and 70-100 % of the cycle),
repeating; paused while the pill is closed and under reduce motion.

V-12.4 **Glass** (OWNER fifth amendment; design tokens voice-composer.html:22, 35): drawn through the port's
`GlassMaterial` in a capsule (ED: extend it with a tint override if it has none), matching the design's recipe: backdrop
blur 6 px and saturate 1.7, tint `pgBg`, a 0.5 px `pgRim` and a 1 px `pgEdge` inset rim, the dispersion fringe (pink
rgba(255,96,150,.38) left, blue rgba(96,176,255,.4) right, 0.7 px), an inner shade `pgShade` at the bottom, a soft top
highlight `pgHi` (radial, 0.55 opacity), and two refracting end bands (white 0.10, 14 px), drop `pgDrop` (float: 0 14 30
-12 rgba(0,0,0,.55)).

| Token | Light | Dark |
|---|---|---|
| pgBg | rgba(250,249,251,.56) | rgba(18,18,22,.50) |
| pgRim | rgba(255,255,255,.75) | rgba(255,255,255,.16) |
| pgEdge | rgba(40,30,60,.14) | rgba(0,0,0,.35) |
| pgHi | rgba(255,255,255,.85) | rgba(255,255,255,.22) |
| pgShade | rgba(60,40,30,.10) | rgba(0,0,0,.22) |
| pgDrop | 0 8 22 -10 rgba(70,50,35,.32) | 0 10 24 -10 rgba(0,0,0,.6) |
| pgLab / pgLabHi | #55565B / #16161A | #8D8E94 / #F7F7F8 |
| pgX / pgXHover | #4A4B50 / #16161A | #B4B5BA / #F7F7F8 |
| pgXBg / pgXRim | rgba(255,255,255,.55) / rgba(40,30,60,.12) | rgba(255,255,255,.09) / rgba(255,255,255,.16) |

V-12.5 **Cross beside the orb** (`cross`; voice-composer.html:197-205): 24 x 24, radius 12, fill `hoverfill`, a 1 px
`winedge` inset ring, icon 12 px in `wink3`; tucked behind the orb at rest (opacity 0, scale 0.55, right edge at the
slot's centre); while live it slides to `translateX(-(D * 1.16 / 2 + 7))`; hover fill `pressfill`, icon `wink`.

V-12.6 **The text field's right edge** (voice-composer.js:814-831): rest: 12 px before the tools; live inline pill: 10 px
before the pill's left end; live cross: 10 px before the cross. Eased over 0.42 s ez (left inset 0.34 s ez). In the app
the composer's text column gets an animated right inset (ED) so text never runs under the pill.

V-12.7 Floating pill and content above the composer: any line it crosses (the files-changed strip, SPEC 1 CZ-9.7) fades
out 0.16 s and back 0.25 s after 0.05 s.

V-12.8 Accessibility: the cross is named `Cancel voice input`, tooltip `Cancel (Esc)`; it is a tab stop only while live.

V-12.9 **The pill's words** (OWNER OD-0097: "the pill's words (L1 to L6 as the page words them)", default L1; SOURCE
voice-options.js `LSETS`, voice-options.html Q1; stored `voiceOrb.labels`). Exact strings (`…` is U+2026; L4's
`Speak now` and all of L6 have none):

| Set | listening | transcribing | finishing |
|---|---|---|---|
| `L1` (default) | `Listening…` | `Transcribing…` | `Finishing…` |
| `L2` | `Listening…` | `Writing…` | `Finishing…` |
| `L3` | `Hearing you…` | `Writing…` | `Tidying up…` |
| `L4` | `Speak now` | `Transcribing…` | `Almost done…` |
| `L5` | `Listening…` | `Typing…` | `Wrapping up…` |
| `L6` | `Listening` | `Transcribing` | `Finishing` |

- Which state shows is V-6.3, unchanged; the pill's open width uses the shown words' measured width (V-12.2), so a
  shorter set gives a narrower pill (L6, as the page says).
- A change while the pill is open (SOURCE voice-options.js `setLabels`, html `.vpill.relabel`): the shown label fades
  out over 0.14 s; 0.16 s after the change the words are replaced and the label fades in by its own crossfade (V-12.3:
  opacity 0.26 s after 0.14 s, rising from -4 px over 0.4 s ez after 0.1 s) while the pill's width eases to the new
  words over 0.42 s ez (OD-0076). A change while the pill is closed takes effect at the next opening.
- The announcements (V-14.5) and the orb's accessible names (V-14.2) keep their wording for every set (ED: they say
  what happens, the pill's words are a look). The label is the pill's polite live region as before.
- Cross beside the orb draws no pill, so the words are not drawn in that style (ED; SPEC 1 dims the row then).

V-12.10 **The language named in the pill** (OWNER OD-0097 B "Auto-detect, named in the pill once heard", and its
amendment "no separator dot"; SOURCE voice-options.js `setLabel`, `labelWidth`). When `Resolve` gives `named` (V-7.14):
- Once the language has been heard the label carries the language after its words: an en space (U+2002), then the
  language's name in weight 400, in the label's own colours and text sweep (one run of the label), e.g.
  `Transcribing… English`. **No separator dot** (the page's `·` is removed by the amendment; ED: the en space and the
  lighter weight separate the name).
- "Heard" (ED): the first live pass that reports Whisper's detected language (whisper.cpp `whisper_full_lang_id`; the
  cloud engine's response `language`), and never before the label first shows the transcribing state (the page shows
  it then). The name fades in over 0.3 s after 0.1 s when it arrives during a label, or arrives with the label's own
  crossfade when the label changes at that moment. It stays for the rest of the session (also with the listening and
  finishing words) and leaves when the pill folds. If a later pass (or the final pass) reports another language, the
  name crossfades (out 0.1 s, in 0.2 s after 0.08 s, as SPEC 1 CZ-12.9) and the width eases.
- Width: `W = H + 8 + labelWidth + nameWidth + 8 + 24 + 8`, `nameWidth` = the en space plus the name measured at
  weight 400 and the pill's font size, rounded up; eased 0.42 s ez like any label change.
- Name: V-16.2's list name for the code; a detected code outside the list shows Whisper's own English name for it with
  an initial capital (ED, e.g. `Marathi`).
- Cross beside the orb has no pill: nothing is shown (ED). The name is not announced (the page announces nothing; ED).
- Never drawn while hidden: the name is not part of the label's text until it is shown (the page's hidden chip was
  painted through the label's text sweep and showed its dot: V-12.12).

V-12.11 **The language question** (OWNER OD-0097 D "asked on the first click of the orb, in a popover with the Mac's
arrow tip, OD-0095"; SOURCE voice-options.html `.vask`, voice-options.js `askLang`, `answerLang`, `say()`).
- When: a click, Enter or Space on the orb that would start listening while `Resolve` gives `ask` (`langMode` = `ask`
  and `voiceTyping.lang` = `null`). Listening does not start; the orb stays at Rest or Hover (no pop); the question
  opens.
- Card: the port's `PopoverCard` (OD-0095 and its measured amendment: the tip part of the card's one outline, fill and
  shadow; 12 pt sharp height, 12 pt half base, 3 pt point radius, 4 pt shoulders; the card's edge 13 pt from its
  anchor, the tip 1 pt short of it). Anchor: the orb's slot (OWNER OD-0095 "at the Mac's distance from their button";
  the page measured its 13 px from the composer's top edge instead: superseded, ED). The card sits above the orb with
  the tip down, centred on the orb; with no room above inside the window it flips below with the tip up (OD-0095).
  Horizontal (SOURCE `askLang`): the card's right edge at the orb's centre + 34, clamped so the card stays within the
  composer's left and right edges; the tip stays on the orb's centre.
- Size and material (SOURCE): 268 wide, padding 12 (top) / 12 (sides) / 10 (bottom), radius 14; the pill's glass
  (V-12.4 tokens) with backdrop blur 14 and saturate 1.7, a 0.5 px `pgRim` and a 1 px `pgEdge` inset rim, shadow
  0 14 30 -10 rgba(0,0,0,.45).
- Content, exact strings (SOURCE): question `Which language will you speak?` (13, semibold, `pgLabHi`, 9 px above the
  answers); answers as capsules `English`, `Hindi`, `Urdu`, `Auto-detect` (26 high, padding 0 11, radius 13, 12.5,
  `pgLabHi` on `pgXBg` with a 1 px `pgXRim` ring, wrapping with a 6 px gap; hover brightness 1.1 over 0.15 s; the
  picked one in the user's accent with white text and no ring); footer `Kept in Settings > Voice typing` (11, `pgLab`,
  8 px below the answers).
- Motion (SOURCE, OD-0076): opens with opacity over 0.18 s and from translate Y 6 and scale 0.96 over 0.24 s ez, its
  origin at the tip; an answer lights first and the card closes 0.22 s later (the close: opacity 0.18 s, back to scale
  0.96 over 0.24 s); reduce motion: opens and closes at once.
- Answer: stores `voiceTyping.lang` = `en` | `hi` | `ur` | `auto` (written at once) and starts listening at once, as
  the click that starts it (V-15), with the resolved language.
- Not designed on the page (ED): Esc, a click outside the card, or a second click on the orb closes it with nothing
  started and nothing stored, so the next click asks again. Keyboard: on open, focus moves to `English`; Left/Right and
  Tab/Shift+Tab move among the four answers (wrapping); Enter/Space pick; on close focus returns to the orb. The card is
  a dialog named `Voice language`. Announcement on open (SOURCE `say()`): `Which language will you speak? English, Hindi,
  Urdu or Auto-detect.` One question at a time, in the composer whose orb was clicked; it follows its anchor if the
  layout moves while open.

V-12.12 **Nothing but the orb, the label and the cancel control** (OWNER OD-0097 amendment; found on the Voice Options
page 2026-10-03 and fixed there, see the record in V-22). While the pill is open:
- The label draws exactly the current words (V-12.9), plus the language name only when V-12.10 shows it. No separator
  dot or other glyph between the label's parts or beside the orb. Text that is not shown is not in the label's text at
  all: a sweep drawn through the text's glyphs (V-12.3) must never paint a hidden run (the page's cause of the dot).
- The label's sweep is clipped to the glyphs' full ink box, descenders included (the page's 1.1 line box cut the foot
  of `g`, `p`, `y`; ED: the box is the font's ascent to descent, centred as before).
- No stray pixels, halo fringes or seams around the orb at any orb size (32, 26 and the slot's spring between them),
  scale (the pop, the voice breathing, Processing) or display scale (100 to 200 %): the pill's material (its tint, the
  `pgRim` and `pgEdge` rims, the dispersion fringe and the refracting end band, V-12.4) is faded out around the orb by
  a radial mask centred on the orb, transparent inside `D/2 - 1` and opaque from `H/2 + 1.5` (ED; the page's fix:
  the pill's end cap is concentric with the orb and 2 to 4 px wider than the popped ball, so its rim showed as a
  crescent and a tick beside the orb). The orb's own glow covers the band. Inline: the mask's centre is the pill's
  right cap; float: its left cap (where the orb settles once lifted).
- The orb's own edge is clean: its glow and shadow fade to zero inside the 72 px view (no square edge of the
  offscreen surface, no ring from the 2x downsample: V-11.3), and the slot draws nothing behind it.

---

## 13. The voice glow, the sweep, and the composer light while you speak (OWNER second amendment; OD-0097)

V-13.1 **What is carried** (DESIGN voice-glow.js, NOTICES.md: voice-glow 0.2.1 `dist/index.es.js` lines 3-997 and
1014-1016, verbatim in JavaScript): the theme presets `Ga`, lobes `we` (7 lobes: x 0, ±36, ±72, ±108; w 74/54/48/42;
h 46/40/32/26), palettes, the stylesheet recipe `Ya` (stroke, inner, bloom layers, masks, filters, hue rotate,
brightness, saturation), the band canvas (`xn` polyline of 57 points, `Sn` strokes with blur and chromatic offsets), the
warp (`distortion`), the driver `ka` (envelope `pa`/`Ge`, flow phase, processing scan `scanA`/`scanT`, corner follow
`gt`, lobe positions and levels, hue drift `Tn`), defaults `Rn` and type presets `Pn`, `Ln`, `In`, `En`, `Wn`, colours
`Ue`, and `mountVoiceGlow`'s prop resolution (voice-glow.js:1018-1150).

V-13.2 **Port** (ED): the driver math in Core (`VoiceGlowDriver`, deterministic per dt, unit-tested against values
captured from voice-glow.js in node); the layers drawn by `VoiceGlowView` (composition visual, SkSL): each CSS layer of
`Ya` becomes a shader pass at composer size (radial-gradient lobes, masks, blur via the layer's own blur radius applied
once per layer in a reused surface, never a per-frame full-size blur of the window), the band polyline stroked with
Skia paths, the SVG displacement replaced by an equivalent noise-offset in the same shader. Fidelity against headless
Chrome frames of the design (`tests\fixtures\voice\ref-glow.js`) per state and level, with the tolerance set from the
first comparison and recorded (ED).

V-13.3 **Configuration used** (DESIGN voice-composer.js:192-195): `type 'default'`, `scale 0.7`, `glowWidth 1.6`,
`lobeSpacing 2.1`, `rangeWidth 1.9`, `processingTravel 2.05`, `borderRadius 22` (the composer's corner), `theme` = the
app's appearance, `active` = Listening or Processing, `processing` = Processing, `reducedMotion` = the system setting,
`level` = the raw level `all` (V-5.3, unsmoothed; the driver smooths it). Everything else from the package defaults for
`default` and the theme. Drawn inside the composer's rounded shape, above its glass and the composer light, under the
text and controls (voice-composer.html:110-112).

V-13.4 **The composer light while you speak** (DESIGN voice-composer.js:775-782; OWNER OD-0097 B1 default, B2 and B3
as choices; SOURCE voice-options.js `S.beamVis = follow(S.beamVis, voiceOn ? (away 0 | dim 0.35 | stay 1) : 1, dt, 0.2,
0.17)`; stored `voiceOrb.lightVoice`). `ComposerLightView` gains a `VoiceLight` property, a target level in [0, 1]
(default 1; it replaces the earlier `GiveWay` bool). During Listening, Processing and Landing the slot sets it to the
setting's level, otherwise 1:

| `lightVoice` | Option (Customization) | Level while voice is on | What the user sees (SOURCE) |
|---|---|---|---|
| `away` (default) | `Steps aside` (B1) | 0 | the light fades out as listening starts and returns when the text is done: one light on the composer at a time |
| `dim` | `Dims to 35 %` (B2) | 0.35 | the light keeps running at 35 % under the voice glow |
| `stay` | `Stays on` (B3) | 1 | the light keeps running at full strength, in its typing response, under the voice glow |

The light's whole output (any style: Border beam or the Uiverse light; ED, the setting names the composer light, not
one style) is multiplied by `vis = follow(vis, target, dt, 0.2, 0.17)` on the render thread: time constant 0.17 s going
down (95 % in 0.51 s, the page's "0.5 s") and 0.2 s going up (95 % in 0.6 s, the page's "0.6 s"); snapped to 0 below
0.003 and to 1 above 0.997. At `vis` 0 the light requests no frames and its own motion clock stops, resuming where it
stopped when it returns (SOURCE: the page advances the beam only while it is visible). Under B2 and B3 the light keeps
its own phase response: the composer is in its Typing phase while words are written (V-8.2). A change of the setting
during a session retargets at once through the same follower; reduce motion: `vis` jumps to the target. With the
light Off the setting has no effect (SPEC 1 dims its row).

V-13.5 Pre-warm (DESIGN voice-composer.js:196-203 raised the layers once at load: a 50 ms first frame otherwise): the
glow's programs join `GpuWarmup`; its surfaces are created at the first composer layout, not at the first click.

---

## 14. Composer integration (OWNER OD-0091)

V-14.1 **Attachment button** (`voiceOrb.attach`): the source's button (paperclip symbol, `Chrome.IconFont`, the chip
style, tooltip and name `Attach files`, the recent-downloads popover; PORT ComposerView.cs:1225-1248) moves out of
`ComposerControls`:
- `in` (default): inside the composer at its left, 34 x 28, radius 14, 8 px from the left edge, aligned with the bottom
  controls row (12 px from the top on a one-line composer: DESIGN voice-composer.html:139-148); the text inset becomes 48;
- `out`: a 36 x 36 circle in the margin left of the composer (fill `turn`, a 1 px `winedge` ring), centred on the
  composer's last line; the composer gives it 44 px of width; text inset 14.
- Switching eases (SPEC 1 CZ-14.1 "Attachment place").

V-14.2 **The orb slot**: `ComposerControls`' first item (where the attachment was), `D` x `D` (32 or 26), the 72 px orb view
centred on it overflowing, a hit target 4 px larger all round (DESIGN `.orbbtn{inset:-4px}`), focusable, Enter/Space
toggle, tooltip `Voice input`, accessible name `Start voice input ({orb name})` / `Stop and finish the text` (Listening) /
`Finishing the text` (Processing), pressed state while Listening. The gap from the slot to the model chip equals the
design's 14 px (the row's own spacing stays the source's; ED).

V-14.3 Every `ComposerView` gets the orb (main, compact, Hydra heads) (ED).

V-14.4 **Esc** while Listening or Processing cancels and is consumed; otherwise Esc keeps its existing meaning.

V-14.5 **Announcements** (DESIGN voice-composer.js `say()`), as polite live-region text: start `Listening. Your words are
written into the composer as you speak. Click the orb again to finish, or press Escape to cancel.`; stop `Finishing the
text.`; done `Done: {text}` + (` Sending.` | ` The text is in the composer; send it when you are ready.`); cancel
`Voice input cancelled; the words were removed.`; blocked `The microphone is blocked. Allow Marrow in Windows Settings,
Privacy and security, Microphone.`

V-14.6 **Notes in the field** (DESIGN voice-composer.js:211-219, 438-458): a note layer that crossfades with the
placeholder (in 0.26 s after 0.05 s, rising 3 px over 0.38 s ez; out 0.2 s / 0.3 s): `Voice input cancelled` (1.6 s, only
when nothing was written), `No speech was heard` (1.6 s), and the blocked note (5.2 s) with an icon (15 px circle and
mark in the warning colour) and the text `Microphone blocked. Allow Marrow in ` + link `Privacy & security > Microphone`
(underlined; opens `ms-settings:privacy-microphone`). When the draft holds text, the note shows in the composer's existing
notice line instead (ED: never hide the user's text). Other errors: Q-V-7.

V-14.7 The composer leaving the screen (thread switch, window close) during a session stops it as a second click would;
the text lands in that thread's draft and is never auto-sent (ED).

V-14.8 **Scripted session source** (ED; used by SPEC 1's Customization preview and by the tests): a `VoiceSession`
bound to `ScriptedCapture` + `ScriptedVad` + `ScriptedEngine` that replay the voice design's simulated speaker and live
words exactly (voice-composer.js:42-83 `SCRIPT`, `SYN` with its seeded `rng(91)`, `synthAt`; 221-240 `STREAM` with
`rng(7)`, first guesses `theme -> team`, `switch, -> switch`, `tooltip -> tool`, `too. -> too`, arrivals 0.3-0.7 s after
each word, revisions 0.35-0.6 s later; 304-324 the final pass), on the frame clock. It opens no device, loads no model and
touches no network; it writes only into the control it is given (the preview composer), never a thread's draft.

---

## 15. States and transitions (OWNER OD-0076: nothing cuts; DESIGN voice-composer.js:373-480, 544-552)

| From | Event | To | What changes |
|---|---|---|---|
| Rest | pointer enters the orb | Hover | V-11.4 Hover targets; model load starts (V-7.9) |
| Hover | pointer leaves | Rest | targets back (act smoothstep 0.65 s, scale spring 0.32/0.78) |
| Rest, Hover, Landing | click / Enter / Space, microphone allowed | Listening | capture starts; orb pops (spring 0.3/0.62), act ramps 0.22 s; pill opens (left and width 0.42 s ez, opacity 0.14 s; label in 0.26 s after 0.14 s, sliding 4 px over 0.4 s ez after 0.1 s; cross disc in 0.22 s after 0.16 s, scale 0.6 -> 1 over 0.38 s ez after 0.12 s) or the cross slides out (0.42 s ez, opacity 0.24 s after 0.04 s) or the float pill lifts; the glow becomes active; the light goes to its voice level (V-13.4); announcement |
| Rest, Hover | click / Enter / Space while `Resolve` gives `ask` (V-7.14) | Asking | no capture, no pop; the language question opens (V-12.11); its announcement |
| Asking | an answer | Listening | `voiceTyping.lang` stored; then as the Rest -> Listening row, with the resolved language; the card closes 0.22 s after the answer |
| Asking | Esc, a click outside, or the orb clicked again | Rest | the card closes; nothing stored or started; focus back to the orb (ED) |
| Rest | click, microphone blocked (V-4.5) | Blocked | no pop; colour drains (desat tau 0.117 s), speed 0.6; the blocked note 5.2 s; then Rest (return tau 0.2 s) |
| Listening | voice | Listening | speed, scale, glow follow `v`; label hysteresis (V-6.3, the pill's width eases to the new label over 0.42 s ez, labels crossfade 0.2 s); live words (V-7.3, V-7.5) |
| Listening | click / Enter / Space, or the 5-minute cap, or a stop event | Processing | capture stops; label `Finishing…`; speed 4.6, scale 1.1 (damping 0.75), glow 0.75, lift 0.8; the glow gathers into the sweep (1.1 s a pass, corner to corner); final pass (V-7.6) |
| Listening | stop with nothing written and no speech seen | Rest | `No speech was heard` note; pill folds |
| Listening, Processing | Esc or the cross | Rest | capture and pass cancelled; words withdraw (V-8.6); `Voice input cancelled` note if none were written; pill folds (width to H over 0.42 s ez, then opacity 0.18 s after 0.24 s; cross out 0.16 s / 0.3 s); glow fades (package fade); light returns (tau 0.2 s) |
| Processing | click | Processing | ignored; the orb is not pressable |
| Processing | final pass complete | Landing (0.5 s) | words ink (25 ms apart); pill folds; orb settles (act 0.65 s smoothstep, spring 0.32/0.78, speed tau 0.5 s); glow ends; the light returns to full when Landing ends (V-13.4: it is held at its voice level through Landing) |
| Landing | 0.5 s elapsed | Rest | with `send`: send 0.35 s later (V-8.8) |
| any | the composer leaves | Processing -> Landing | V-14.7 |
| any | another orb clicked | Processing (this one) | V-7.11 |
| Rest | orb, size, style or attachment setting changes | Rest | V-11.5 crossfade; size spring; pill style takes effect next session; attachment eases |
| any | pill words, light-while-you-speak or language-default setting changes (OD-0097) | same | words: V-12.9 relabel while open, else next opening; light: retarget through V-13.4's follower; language default: `voiceTyping.lang` reset (V-7.14), applies from the next session (a running session keeps its language; ED) and an open question closes |

Reduce motion: every transition lands at once; the orb is still; the glow follows the package's reduced-motion path; the
label sweep stops.

---

## 16. Settings

### V-16.1 `voiceOrb` (Customization; SPEC 1 CZ-9, CZ-13.4)
`VoiceOrbSettings` record in Core: `Orb` (Siri | Membrane), `ListeningStyle` (Pill | Cross | Float), `OrbSize` (32 | 26),
`Attachment` (Inside | Outside), `AfterVoice` (Leave | Send), and (OWNER OD-0097) `Labels` (L1 to L6, V-12.9),
`LightVoice` (Away | Dim | Stay, V-13.4), `LangMode` (Auto | Show | Windows | Ask, V-7.14); JSON key `voiceOrb`,
`{"orb":"siri","lstyle":"pill","orbSize":32,"attach":"in","afterVoice":"leave","labels":"L1","lightVoice":"away","langMode":"auto"}`;
lenient read (a missing or unknown value keeps its default). Key names and values (ED): the decision page's own
(`labels` L1..L6; `lightVoice` away | dim | stay; its `lang` auto | show | windows | ask stored as `langMode`, so it is
never confused with `voiceTyping.lang`).

### V-16.2 `voiceTyping`
`{"engine":"local","lang":null,"model":null}` (ED key names): `engine` local | cloud; `lang` `null` (nothing picked: the
language default of V-7.14 decides; the default, ED for OD-0097), `auto` (Auto-detect picked) or a Whisper code;
`model` a catalogue id or null (none selected yet). Lenient read: a missing `lang` is `null`; an unknown string is
`null`. Written only when set, after `model` (lead ruling 2026-10-04; ED-289 items 3 and 13): `mic` (the
Microphone row's device id, OD-0099 Q-V-19 B; absent is "Windows default"), `liveWordsChoice` (`"live"` | `"accurate"`,
the answer to the slow-PC question, OD-0099 Q-V-1 C) and `liveWordsChoiceKey` (the hardware part of the V-10.5 key the
answer was given under: adapter, driver, processor, memory; R5). An unknown `liveWordsChoice` and an empty or non-string
`mic` or `liveWordsChoiceKey` read as absent. Languages in the design's order (voice.js:26) with codes:
`Auto-detect` auto, `English` en, `Hindi` hi, `Urdu` ur, `Arabic` ar, `Bengali` bn, `Chinese` zh, `French` fr, `German` de,
`Indonesian` id, `Italian` it, `Japanese` ja, `Korean` ko, `Persian` fa, `Portuguese` pt, `Punjabi` pa, `Russian` ru,
`Spanish` es, `Tamil` ta, `Turkish` tr, `Vietnamese` vi.

### V-16.3 The Voice typing page (DESIGN gen.py:570-606, voice.js:102-182; lock pending Q-CZ-1 of SPEC 1)
A `SettingsPage.VoiceTyping` page (no chrome accessory), built from the port's `ChromeSection`, `ChromeCard`,
`ChromeRow` and SPEC 1's controls (CZ-12):

V-16.3.1 Section `Voice typing`, one card:
- `Engine`: segmented sm `On this PC` | `OpenAI cloud`; detail local `Your voice never leaves this computer`; cloud
  `Sent to OpenAI with your own key, about half a US cent a minute` (the price depends on Q-V-15).
- (folds open only for cloud) `OpenAI key`, detail `Cloud voice typing uses your own key, kept in Windows’ credential
  store`, capsule `Add key` / `Cancel`; under it, folding, a password field with placeholder `Paste your OpenAI key`
  (accessible name `OpenAI key`). Saving, replacing and removing a stored key: Q-V-16 (recommended: the app's existing API
  key field pattern from the Providers page).
- `Language`: a menu button (chip style, name crossfade, width eased 0.3 s ez, chevron) opening a 220 px menu below it,
  right-aligned, with a check on the current item; keys as SPEC 1 CZ-4.5.2 (the shared control is SPEC 1 CZ-12.12). Detail: Auto `Whisper works out the
  language; pick one if Hindi comes out as Urdu` (wording: Q-V-17); Hindi `Written in Devanagari`; Urdu `Written in Urdu
  script, right to left`; others `Every dictation is read as {Language}`.
  With `lang` = `null` (OWNER OD-0097; SOURCE the page's `Settings > Voice typing: Language` row, `#langv`) the menu
  button shows the language default's value: A `Auto-detect`; B `Auto-detect · shown while you speak`; C `{Language}
  (from Windows)` (`Auto-detect` when Windows' language is not in the list, V-7.14); D `Asked on first use`. (The
  middle dot here is a settings value's text, not the pill; the amendment's rule is the pill's.) The check (ED) sits on
  `Auto-detect` for A and B, on the Windows language's item for C (on `Auto-detect` in its fallback), on no item for D.
  The detail (ED) is the checked item's detail; for D `You will be asked the first time you click the orb`. Picking any
  item stores it explicitly (`auto` or the code), which overrides the default until a language default is picked again
  in Customization (V-7.14).
- `Orb and after voice input`, detail `How the orb looks, and whether the words are sent`, capsule `Customization` with
  a chevron: opens the Customization page.

V-16.3.2 Section `Speech model` (folds away for cloud), first card ("hwcard"):
- row `Recommended for this PC`; detail: never checked `Not checked on this computer yet`; running `Checking this
  computer`; done `Checked by itself when voice typing was set up` (first-run result) / `Checked just now` (a check run in
  this session) (wording for later sessions: Q-V-18); capsule button with a spinner that grows in while running:
  `Check my hardware` / `Checking` (inert) / `Check again`.
- the four probe rows (folding open on the first check): a 12 px spinner while that row's probe runs, a check mark when
  done (0.2 s / 0.3 s ez), key column 104 px (`Processor`, `Memory`, `Graphics`, `GPU backend`), value crossfading in.
- the recommendation box (folds open when done): the model name (V-9.6, 13 semibold), the reason (V-10.4); then either
  buttons `Use recommended` (primary, accent fill) and `Keep {current model}` (shown while the user has not decided and
  the recommendation is not in use), or a state line with a check: `In use` / `Downloading, then in use` / `You kept
  {current model}`.
- note: `The first time voice typing is set up, this check runs by itself and installs the model it recommends. Run it
  again any time.`

V-16.3.3 Second card, the model list: caption `Whisper` with the subtitle `OpenAI, 99 languages. Speed as on a graphics card;
the processor alone is slower`; one row per model (6):
- name, and a `Recommended` badge (10.5, accent text on 14 % accent, radius 5; scales in from 0.85 over 0.3 s ez) on the
  recommended model;
- a version button (12, secondary, chevron turning 180 deg) reading `{Version}, {size}` + `, English only`; it folds open
  a segmented sm control `{Version} · {size}` per version and, for Tiny to Medium, an `English only` switch, then the
  model's note (V-9.1);
- meters, 112 px: `Speed` and `Accuracy`, five 5 px dots each (lit to the model's score), accessible as
  `{name}: speed {n} of 5, accuracy {m} of 5`;
- a 96 px state column crossfading among: `In use` (check), progress (`{n}%` tabular + a 20 px `x` named `Cancel the
  download`), `Use` capsule (installed), `Download` capsule; a 2 px progress bar under the row (accent fill, fading in
  0.3 s, width 0.18 s linear);
- the version shown in each row: the in-use variant, else the recommended variant, else Compact (ED).
`Download` downloads without selecting; `Use` selects an installed variant; `Use recommended` downloads then selects.

V-16.3.4 The footer of the list: `Installed: {size}, in Marrow Code’s models folder` (sum of installed files, V-9.2).

V-16.3.5 The privacy line under the cards (12 / 17, secondary): local `Voice typing runs on this computer. Your voice is not
sent anywhere, and recordings are never kept.`; cloud `Your recording is sent to OpenAI to be turned into text, using
your key. OpenAI states it does not train on API data. Nothing is kept on this PC.`

V-16.3.6 Transitions: as SPEC 1 CZ-14 (folds, crossfades, segmented pills, switches), plus the state column crossfade
(out 0.15 s / scale 0.94 over 0.25 s ez; in 0.25 s after 0.08 s / 0.35 s ez) and the spinner growing in (width 0.25 s ez).

### V-16.4 `voiceHardware`
`{"key":"<sha256>","origin":"auto"|"manual","cpu":"…","cores":8,"ramGB":31,"gpu":"…","vramGB":16,"freeVramGB":14,
"backend":"vulkan"|"cuda"|"cpu","candidate":"large-v3-turbo-q8_0","model":"…","rtf":0.06,"liveWords":true,"at":"…",
"app":"<version+build>"}` (ED).

### V-16.5 Privacy notes shown (`voiceNotes`, ED): `{"local":false,"cloud":false}`, for the one-time note per engine
(Q-V-8b).

### V-16.6 Write coalescing as SPEC 1 CZ-13.5; all keys read leniently (unknown values keep defaults).

---

## 17. Privacy, the cap, errors

V-17.1 Local: audio stays in memory, is never written, never logged, cleared at session end; transcripts are never logged
(not even in diagnostics); the model download sends no user data.

V-17.2 Cloud: the recording (16 kHz mono WAV) goes to OpenAI over HTTPS with the user's key, after the user chose the
cloud engine and stored a key (explicit opt-in); the key lives in Windows Credential Manager through `SecretStore`
(service `Marrow Code`, account `voice-openai`, ED).

V-17.3 The orb is the microphone-in-use indicator: capture stops the moment the session leaves Listening.

V-17.4 One-time privacy note per engine (OWNER OD-0092 default): wording and place not designed (Q-V-8).

V-17.5 Cap: V-7.12. No auto-stop on silence (OWNER default).

V-17.6 Errors with a designed answer: Blocked (V-4.5, V-14.6), cancelled, no speech. Errors without a design (Q-V-7):
no microphone, device removed or busy, model not installed (and downloading), model load failed or the CPU lacks AVX2,
engine failure, cloud without a key, key refused (401), quota or rate (429), network failure.

---

## 18. OpenAI cloud engine (OWNER OD-0092: opt-in)

V-18.1 `OpenAiTranscriber : ISpeechEngine` (ED): `POST https://api.openai.com/v1/audio/transcriptions`, multipart:
`file` = the session audio as 16 kHz mono PCM16 WAV, `model` (Q-V-15; recommended `gpt-transcribe`), `language` when forced,
`response_format` `json`, and `stream` true where the model supports it (`transcript.text.delta` events written into the
composer as they arrive during Processing; `transcript.text.done` ends it).

V-18.2 Live words with cloud: not in v1 unless the owner picks the Realtime route (Q-V-15): during Listening the pill shows
the VAD label; words arrive in Processing.

V-18.3 Limits: 25 MB per file (RESEARCH 1.7); the 5-minute cap keeps a session under 10 MB.

V-18.4 Errors map to the cloud notes (Q-V-7); no automatic fallback to local (ED; RESEARCH suggested offering it).

V-18.5 No OpenAI SDK; `HttpClient` with a 60 s timeout; the handler is a seam (loopback in tests).

---

## 19. Performance budgets (ED; measured at the gate on a quiet PC)

| Item | Budget |
|---|---|
| Capture + processing threads | under 2 % of one core while listening |
| UI thread per frame during a session | under 1.5 ms (orb motion, label, ops batch) |
| Orb draw (72 px at 2x) | under 0.5 ms GPU per frame; 0 B allocated per frame |
| Glow draw (composer size) | under 2 ms GPU per frame; 0 B allocated per frame |
| Frame pacing during inference on the GPU (Vulkan) | the app's frame-gap gate holds; if it does not, record it and fall back to CPU inference (ED, RESEARCH 5) |
| First word after speech starts (live, owner's PC, turbo-q8_0) | 0.3 to 0.7 s (the design's arrival window) |
| Final pass after the second click (owner's PC) | under 0.5 s plus the schedule |

---

## 20. Phased build plan (each phase mergeable on its own)

| Phase | Content | Mergeable because |
|---|---|---|
| **V0** Pins and probes | orchestrator pins V-3.1 packages; one agent: records Whisper.net's builder, VAD and runtime-report API names (reflection test), the published native file list and sizes, the VC++ runtime situation, the Silero ggml pin (V-3.4), the model manifest as an embedded resource with its hash test, the THIRD_PARTY_NOTICES section (V-3.6) | no behaviour; only pins, assets, notices |
| **V1** Core | settings records and JSON (V-16), `ModelCatalog`, `Recommender` + `SelfTestPlan`, `PolyphaseResampler`, `LevelAnalyser`, `SpeechLabel`, `StreamingTranscriber` + `TranscriptOps` (incl. the final-pass schedule), `OrbMotion`, `VoiceGlowDriver`, `VoiceSession` on seams; OD-0097: the `voiceOrb` keys `labels`, `lightVoice`, `langMode` and `voiceTyping.lang` = null (V-16.1, V-16.2), the word sets (V-12.9), `VoiceLanguage.Resolve` and the reset rule (V-7.14), the Asking state (V-15) | pure code, unused by the app |
| **V2** Services | `WasapiCapture` + `CapturePipeline`, `SileroVad`, `WhisperNetEngine`, `ModelStore` + `ModelDownloader`, `HardwareProbe`, `MicrophoneAccess` (App), `VoiceKeyStore` | unused by the app; adapters behind seams |
| **V3** Orb renderer | `VoiceOrbView` + SkSL port, fidelity fixtures, `GpuWarmup` registration | a control nobody shows yet (SPEC 1 C4 may use it) |
| **V4** Glow renderer | `VoiceGlowView` + fidelity fixtures; `ComposerLightView.VoiceLight` (default 1: no change; V-13.4) | no visible change |
| **V5** Composer integration | attachment move (V-14.1) with `voiceOrb.attach`; orb slot; pill / cross / float; `DictationLayer`; insertion, undo, send; Esc; notes; announcements; one-session rule; OD-0097: the pill's words and relabel (V-12.9), the language name (V-12.10), the language question (V-12.11), the light's voice level wired from `voiceOrb.lightVoice` (V-13.4), the clean pill (V-12.12); the scripted session source for SPEC 1's preview and tests; all behind `VoiceFeature.Enabled` = false except the attachment move | the composer is unchanged for users except the attachment's place (owner-ruled) |
| **V6** Voice typing page | the page (V-16.3), language (with the language default's value while nothing is picked, V-16.3.1), engine (cloud row inert until V8), model list with downloads, manual hardware check, privacy line | a usable settings page; voice still hidden |
| **V7** First run + switch on | `VoicePreparation` (V-10.7), self-test, the first-run hook (D3 behaviour until OD-0096's screen lands), `VoiceFeature.Enabled = true` | the feature ships complete for the local engine |
| **V8** OpenAI cloud | `OpenAiTranscriber`, key row, cloud notes | opt-in; local unchanged |

Order of dependence: V0 -> V1 -> {V2, V3, V4} -> V5 -> V6 -> V7 -> V8. SPEC 1's C4 needs V3 + V5; C5 needs V6.
Each phase: tests first, adversarial review, fixes, merge, gate (native frame tests alone on a quiet PC; flake rule).

---

## 21. Tests per phase (seams named; never a real microphone, network or model download)

Traits per R-TEST-1 (`[Trait("Decision", "ED-nnn")]`, the OD in the comment). Expected values come from the design files,
BENCH and the manifest, never from running the port (R-TEST-4). Reference fixtures for JavaScript behaviour (orb
runtime, glow driver, live-words schedule, analyser) are produced offline by node / headless-Chrome scripts under
`tests\fixtures\voice\` and committed with their generator.

### V0
| # | Test | Clause |
|---|---|---|
| V-T1 | Manifest: 29 entries, ids, bytes, SHA-256 equal V-9.1; the HF commit pinned | V-3.3, V-9.1 |
| V-T2 | Whisper.net API members the adapter uses exist (reflection; names recorded) | V-7.1, V-6.1 |
| V-T3 | Bundled VAD model hash pin | V-3.4 |
| V-T4 | THIRD_PARTY_NOTICES has the Voice typing section with each component, licence and copyright line | V-3.6 |
| V-T5 | Package pins equal V-3.1 (reads Directory.Packages.props) | V-3.1 |

### V1 (pure, `TestRuntime`, manual clock)
| # | Test | Clause |
|---|---|---|
| V-T10 | `voiceOrb`, `voiceTyping`, `voiceHardware`, `voiceNotes` JSON bytes for defaults; lenient read table | V-16 |
| V-T11 | Language list order, names, codes | V-16.2 |
| V-T12 | Catalogue names, versions, dots, notes, English-only availability; sizes as shown (V-9.2 examples) | V-9.1, V-9.2 |
| V-T13 | `ModelName` strings | V-9.6 |
| V-T14 | Recommender: every row of V-10.2 for scripted probes (desktop RTX 4080, 2 GB VRAM NVIDIA, AMD, Intel iGPU, CPU 16 cores en / multi, CPU 4 cores en / multi); never a dominated model | V-10.2 |
| V-T15 | Self-test thresholds and the one-rung ladders (incl. "once only") | V-10.3 |
| V-T16 | Hardware key changes with each input; a stale record re-runs | V-10.5 |
| V-T17 | Resampler: 1 kHz tone level within 0.1 dB at 48k, 44.1k, 16k inputs; 9 kHz attenuated >= 60 dB; streaming equals one-shot | V-5.2 |
| V-T18 | Analyser equals Web Audio's for fixture signals (bytes from a headless-Chrome AnalyserNode run): `all`, `low`, `mid`, `high` | V-5.3 |
| V-T19 | Noise-floor subtraction returns `all` to 0 in a scripted noisy pause | V-5.4 |
| V-T20 | Followers: band attack 0.07 / release 0.24, voice 0.12 / 0.45; values under 1e-4 to 0 | V-5.5 |
| V-T21 | `SpeechLabel`: Listening -> Transcribing on speech or a word within 0.35 s; back after 0.6 s; no flicker over gaps under 0.6 s; Finishing on stop | V-6.3 |
| V-T22 | LocalAgreement: commits the common prefix only; committed words never change; tail revisions produce `Revise` ops | V-7.3, V-7.5 |
| V-T23 | Pass gating: 0.5 s new audio, previous pass done, VAD speech present | V-7.3 |
| V-T24 | Long pause (1.5 s) commits all and drops silence; 25 s cap commits older than 10 s | V-7.4 |
| V-T25 | Ops timing: adds >= 60 ms apart; final schedule 0.18 s start, 0.07 s adds, 0.12 s revisions; Processing lasts max(0.75, end + 0.3) (fixture: the design's planFinal) | V-7.5, V-7.6 |
| V-T26 | No engine call on VAD-silent audio; silent session ends "No speech" | V-7.8, V-6.4 |
| V-T27 | `OrbMotion`: targets per state table; act ramps (ease-out cubic 0.22 s up, smoothstep 0.65 s down, from the shown value); spring integration equals the design's `spring` (fixture); D spring 0.34/0.86; reduce motion lands at once | V-11.4 |
| V-T28 | Orb runtime: `sampleTransition`, `applyAudioUniforms` and the motion phase equal the export's for fixture inputs (values arrays compared float for float) | V-11.1, V-11.2 |
| V-T29 | `VoiceGlowDriver` equals voice-glow.js's driver for scripted level sequences (lobe positions and levels, scan, hue, warp) | V-13.1, V-13.2 |
| V-T30 | `VoiceSession` state table: every row of section 15 with scripted seams (incl. cap at 300 s, cancel during Processing, one-session rule, composer leaving) | V-15, V-7.11, V-7.12, V-14.7 |
| V-T31 | Insertion rules: caret, selection replaced at the first word, spacing rules, caret after; cancel restores text and selection | V-8.1, V-8.6 |
| V-T120 | OD-0097 settings: `voiceOrb` default bytes with `"labels":"L1","lightVoice":"away","langMode":"auto"`; each value round-trips; unknown or missing values keep the defaults; `voiceTyping` default bytes with `"lang":null`; `"auto"`, a code, a missing key and an unknown string read as V-16.2 | V-16.1, V-16.2 |
| V-T121 | Word sets: the 18 strings byte-equal to the decision page's `LSETS` (fixture extracted from voice-options.js by a node script, committed with it); `SpeechLabel` gives each set's word for listening, transcribing and finishing with the unchanged V-6.3 timing | V-12.9, V-6.3 |
| V-T122 | `VoiceLanguage.Resolve` table: `lang` {null, auto, en, hi} x `langMode` {auto, show, windows, ask} x Windows UI language {en-US, hi-IN, ur-PK, fr-FR, sw-KE (not in the list)} -> (language, named, ask) as V-7.14 | V-7.14 |
| V-T123 | Reset rule: picking any language default (also the current one) sets `voiceTyping.lang` to null; an answer to the question stores en / hi / ur / auto; a Voice typing page pick stores it explicitly | V-7.14, V-12.11 |
| V-T124 | `VoiceSession` Asking: a click with `ask` opens no capture and loads nothing (seams throw if touched); an answer goes to Listening with the forced language passed to `ISpeechEngine`; Esc, outside click and a second orb click go back to Rest with nothing stored, and the next click asks again | V-15, V-12.11 |

### V2 (adapters; loopback server and scripted devices; no microphone, network or download)
| # | Test | Clause |
|---|---|---|
| V-T40 | Capture pipeline with `ScriptedCapture`: float and PCM16/24 at 48k and 44.1k, 1-8 channels, to 16 kHz mono; no allocation in the capture callback (allocation probe) | V-4.1, V-4.2, V-2.2 |
| V-T41 | All-zero first second -> Blocked; device removed mid-session -> final pass on what was captured | V-4.5, V-4.4 |
| V-T42 | `MicrophoneAccess` denied -> no device opened | V-4.5 |
| V-T43 | Downloader on loopback: full download, resume with Range, 200-to-range restart, 416 complete, cut stream retried with back-off on the manual clock, wrong bytes -> Corrupt and never loaded, cancel keeps `.partial`, parallel downloads, progress at most 10 per second | V-9.5 |
| V-T44 | Store: `installed.json` trust rules; size/time mismatch re-hash before load; data root from `MARROW_PORT_DATA_DIR` | V-9.3, V-9.4 |
| V-T45 | `HardwareProbe` formats rows from scripted DXGI/Win32 values (seam under the probe) | V-10.1 |
| V-T46 | Every seam's test default throws when touched | V-2.5 |
| V-T47 | `VoiceKeyStore` uses `SecretStore` service and account; never logs the key | V-17.2 |
| (gate tool, not a unit test) | `voice-evidence` console run on the owner's PC with a model already on disk and a fixture WAV: WhisperNetEngine words, VAD probabilities, RTF; output saved to `work\voice\evidence\` | V-7.1, V-6.1 |

### V3
| # | Test | Clause |
|---|---|---|
| V-T50 | Orb fidelity: Siri Wave and Voice Membrane x {rest at t 0, 1.3, 4.0; hover; listening with three band sets; processing; blocked} x {32, 26} x {dark, light}, against the reference PNGs, <= 2/255 at >= 99.5 % of pixels | V-11.7, V-11.1, V-11.2 |
| V-T51 | 2x supersampling at every scale (ED-NEW-voicefix-ui-7; the design: below 128 device px); one reused surface; 0 B per frame | V-11.3 |
| V-T52 | Frames only while visible; none when minimised; still frame without a GPU and under reduce motion | V-11.3 |
| V-T53 | Orb swap crossfade (tau 0.12) and phase hand-over | V-11.5 |
| V-T54 | Programs registered with `GpuWarmup`; compile on ANGLE/D3D11 (native, quiet PC) | V-11.3 |

### V4
| # | Test | Clause |
|---|---|---|
| V-T60 | Glow fidelity per state (idle-active, levels 0.2 / 0.6 / 1.0, processing sweep at 3 phases) x theme | V-13.2, V-13.3 |
| V-T61 | `VoiceLight`: for `away`, `dim`, `stay` the target is 0 / 0.35 / 1 during Listening, Processing and Landing and 1 otherwise; follow tau 0.17 down (95 % at 0.51 s) and 0.2 up (95 % at 0.60 s) on the frame clock; snaps below 0.003 and above 0.997; no frames requested at 0 and the light's motion resumes where it stopped; both light styles; a mid-session change retargets; reduce motion jumps; default 1 changes nothing | V-13.4 |
| V-T62 | Glow draws inside the composer shape, under content; 0 B per frame | V-13.3, V-19 |

### V5 (headless app tests with the scripted session source)
| # | Test | Clause |
|---|---|---|
| V-T70 | Attachment inside: 34 x 28 at left 8, text inset 48; outside: 36 x 36 in the margin, composer 44 px narrower, inset 14; the popover and chip state move with it | V-14.1 |
| V-T71 | Orb slot first in the controls, D 32 / 26, hit target +4, focusable, names per state, tooltip | V-14.2 |
| V-T72 | Pill geometry: closed H, open W from the label width, inline right end fixed, float opens right and lifts the orb -56 | V-12.2 |
| V-T73 | Label crossfade and width ease; text sweep running only while open | V-12.3 |
| V-T74 | Cross styles: in-pill disc and beside-the-orb slide (xo = -(D*1.16/2+7)); tab stop only while live | V-12.5, V-12.8 |
| V-T75 | Text field right edge per state; eased | V-12.6 |
| V-T76 | Live words: draft updated, composer Typing phase, layer animations (rise, tentative colour, revision with width ease and FLIP), caret | V-8.2, V-8.3, V-7.5 |
| V-T77 | Pixel-identical hand-over from the layer to the editor at landing | V-8.3 |
| V-T78 | Undo: one step removes the whole dictation | V-8.7 |
| V-T79 | Withdraw on cancel (30 ms stagger, 0.26 s / 0.3 s) and restored draft | V-8.6 |
| V-T80 | Send after landing + 0.35 s with `send`; nothing sent with `leave` | V-8.8 |
| V-T81 | RTL Urdu fixture text animates and hands over correctly | V-8.9 |
| V-T82 | Editor read-only during a session; Esc cancels and is consumed | V-8.5, V-14.4 |
| V-T83 | Notes: cancelled / no speech / blocked (texts, durations, link target `ms-settings:privacy-microphone`); notice line when the draft has text | V-14.6 |
| V-T84 | Announcements' exact strings | V-14.5 |
| V-T85 | The composer light goes to `voiceOrb.lightVoice`'s level during Listening, Processing, Landing in a real composer, for each of away / dim / stay, and returns after Landing | V-13.4 |
| V-T86 | Floating pill fades a crossed line above the composer | V-12.7 |
| V-T87 | `VoiceFeature.Enabled` false hides the orb; true shows it | V-2.6 |
| V-T88 | Transition audit for section 15 on the frame clock (monotonic, no single-frame jump over 15 % of travel) | V-15 |
| V-T125 | Pill words: each set's words in the pill per state (inline and float); the open width from the shown words (L6 narrower than L1 by their measured difference); a change while open fades the label out 0.14 s, swaps at 0.16 s, fades in and eases the width 0.42 s (frame clock, no jump over 15 % of travel); a change while closed applies at the next opening; announcements and orb names unchanged; no words drawn in Cross style | V-12.9 |
| V-T126 | Language name (`named`): absent until the scripted engine reports a language and never before the first transcribing label; then `Transcribing…` + U+2002 + `English` at weight 400, with no U+00B7 or other separator anywhere in the label's runs; the width includes it; it stays to the end and leaves with the fold; a different later language crossfades; never with a forced language; nothing in Cross style | V-12.10 |
| V-T127 | Language question geometry: `PopoverCard` above the orb slot with the tip on the orb's centre, card edge 13 pt from the slot, right edge at orb centre + 34 clamped to the composer; flips below with the tip up when the window has no room above; 268 wide, padding 12/12/10, radius 14, glass tokens | V-12.11 |
| V-T128 | Language question content and motion: the exact strings, capsule metrics, picked capsule in the accent, open 0.18 s / 0.24 s ez from the tip, close 0.22 s after an answer; dialog name; the announcement; reduce motion at once | V-12.11 |
| V-T129 | Language question keys: focus on `English` at open; Left/Right and Tab wrap; Enter/Space pick; Esc, outside click, orb click close with nothing stored and focus back on the orb | V-12.11 |
| V-T131 | Clean pill, the label (OD-0097 amendment): the label's drawn text runs equal exactly the current words (plus the en space and name only when V-12.10 shows it) for every set and state; no U+00B7 or other separator glyph is laid out; a run that is not shown is not in the text layout (the sweep never paints it); the glyphs' ink, descenders of `g`, `p`, `y` included, lies inside the sweep's clip (compare the label rendered with the sweep against the same text in solid colour: the ink masks match within 1 px) | V-12.12, V-12.3 |
| V-T132 | Clean pill, beside the orb (OD-0097 amendment): frames of the open pill (inline and float) at D 32 and 26, during the pop, at voice levels 0 / 0.6 / 1 and in Processing, at 100 / 125 / 150 / 200 % display scale, dark and light, rendered once with the pill and once without it (same orb, glow and composer): in the annulus from the ball's edge + 1 px to the pill cap's radius + 3 px, outside the sector where the pill's body continues, the two frames differ by at most 3/255 per channel (no rim crescent, fringe, end band or tick beside the orb) | V-12.12 |
| V-T133 | Clean pill, no specks: in the same frames, a speck detector over the pill and a 12 px margin around it (connected components of at most 9 px whose colour differs by more than 8/255 from the median of their 7 x 7 neighbourhood) finds none other than the label's and the cross's own glyph edges (masked by their rendered ink); the orb's 72 px view fades its glow to zero before its own edge (no square edge, no downsample ring) | V-12.12, V-11.3 |
| V-T89 | Scripted session source replays the design's speaker, word arrivals, revisions and final pass (fixture from voice-composer.js) and touches no device, model, network or draft (seams throw) | V-14.8 |

### V6
| # | Test | Clause |
|---|---|---|
| V-T90 | Page strings exact (sections, rows, details, buttons, notes, privacy lines) | V-16.3 |
| V-T91 | Engine switch folds the key row and the Speech model section | V-16.3.1, V-16.3.2 |
| V-T92 | Language menu: order, check, keys, detail per language | V-16.3.1, V-16.2 |
| V-T93 | Hardware card: idle / running / done details; rows resolve as the scripted probe completes; recommendation box states and buttons | V-16.3.2, V-10.6 |
| V-T94 | Model rows: version button text, versions fold, English-only switch, meters' names, state column per install state, progress bar, Recommended badge | V-16.3.3 |
| V-T95 | Download / Use / Use recommended semantics on the loopback store | V-16.3.3, V-9.5 |
| V-T96 | Footer total; sizes formatting | V-16.3.4, V-9.2 |
| V-T130 | Language row with `lang` null under each language default: button text (`Auto-detect`, `Auto-detect · shown while you speak`, `{Language} (from Windows)` and its fallback, `Asked on first use`), the check, the detail; an item picked stores it explicitly | V-16.3.1, V-7.14 |

### V7
| # | Test | Clause |
|---|---|---|
| V-T100 | `VoicePreparation` steps and progress items; real progress only (no timers) | V-10.7 |
| V-T101 | First run selects and installs the recommendation (scripted probe, loopback model, `ScriptedEngine` timing) and stores `voiceHardware` with `origin: auto` | V-10.7, V-16.4 |
| V-T102 | Step-down path when the scripted RTF > 0.5 | V-10.3 |
| V-T103 | `LiveWords` from the record drives both paths | V-7.7 |
| V-T104 | Feature enabled; the orb appears in every composer | V-2.6, V-14.3 |

### V8
| # | Test | Clause |
|---|---|---|
| V-T110 | Multipart request shape (fields, WAV header 16 kHz mono PCM16, language only when forced) on loopback | V-18.1 |
| V-T111 | SSE deltas become ops during Processing; JSON reply path | V-18.1 |
| V-T112 | 401 / 429 / 5xx / timeout map to the cloud notes; no fallback | V-18.4 |
| V-T113 | No key -> the cloud note; key from `SecretStore` only | V-17.2 |

---

## 22. Records to write at merge

- One ED per phase (engine choices V-7, seams and fail-closed V-2.5, the dictation layer V-8.3, downloads V-9.5, probe
  V-10.1, orb rendering V-11.3, glow port V-13.2, give-way V-13.4, the attachment and orb placement V-14.1-14.3).
- THIRD_PARTY_NOTICES "Voice typing" (V-3.6) in V0; the design `NOTICES.md` content carried.
- `porting\parity_matrix.json` / `ui_contract.json`: the voice feature as an owner-ruled deviation; `STATE.md`: the
  feature flag while false.
- `NEXT_SESSION_CLAUDE.md` log lines per merged phase.
- OD-0097 EDs (record each as the next free `ED-nnn` at the merge of its phase): the `voiceOrb` key names and
  `voiceTyping.lang` = null default (V-16.1, V-16.2); `Resolve`'s rules, naming under an explicit Auto, the Windows
  language mapping and its fallback, and the reset rule (V-7.14); the light level applying to every light style, the
  paused motion at 0 and the change taking effect mid-session (V-13.4); announcements unchanged by the words and no
  words in Cross style (V-12.9); when the language counts as heard, the name's weight-400 run after an en space in
  place of the page's dot, unlisted names, nothing in Cross style (V-12.10); the question anchored on the orb slot
  (superseding the page's 13 px from the composer's top), its dismissal, keys and focus (V-12.11); the mask around the
  orb and the label's full ink box (V-12.12); the Voice typing Language row's check and D detail (V-16.3.1).
- The decision page fix (OD-0097 amendment), done 2026-10-03 in `decisions-2026-10-03\voice-options\src\build_voice.py`
  and rebuilt (page and `share\`): the dot came from the page's hidden language chip (`&nbsp;&#183; English` at opacity
  0 inside the label, painted anyway by the label's `background-clip: text` sweep, so `·` showed between the words and
  the orb even with Auto-detect); the tick and crescent beside the orb were the pill's rim, dispersion fringe and end
  band at its right cap, which is 2 to 4 px wider than the popped ball; the label's 1.1 line box also cut the foot of
  `g`. Fixed: the chip is out of the layout until shown and has no dot, the pill is masked around the orb, the label's
  line box is 1.5. Before and after crops: `E:\Droppy\work\design\customization\shots\od0097\` (`pill-dot-*`).

---

## 23. Open questions for the owner (each with the recommended answer)

| # | Question | Recommended answer |
|---|---|---|
| Q-V-1 | ~~On a PC too slow for live words (self-test RTF 0.15 to 0.5, e.g. a CPU-only laptop with Large v3 Turbo for Urdu), keep the more accurate model and show the words after you stop, or keep live words with a smaller, less accurate model?~~ | **RESOLVED by OD-0099 Q-V-1 C (the amendment of 2026-10-04) and engineering_decisions.md ED-289 item 13 (lead rulings R1 to R8, 2026-10-04):** every GPU stays live with the accurate model and is never asked; a processor too slow for live words with a smaller rung is asked once per hardware part (`liveWordsChoice` with `liveWordsChoiceKey`, V-16.2): live takes the next smaller rung with live words on, accurate keeps the model with the text after the stop; dismissed or not yet answered keeps the accurate model with the text after the stop. See V-7.7. |
| Q-V-2 | When does the first-run model download happen: on the Getting ready screen (D1), at the first click on the orb (D2), or behind Welcome in the background (D3)? | D3: Getting ready ends after the graphics and the hardware check; the model downloads and is timed behind Welcome; an early orb click shows the progress in the pill. |
| Q-V-3 | ~~Border beam steps aside while you dictate (designed that way, provisional). Confirm?~~ | **RESOLVED by OD-0097 (2026-10-03):** it steps aside by default (B1); Dims to 35 % (B2) and Stays on (B3) are user choices in Settings > Customization (V-13.4). |
| Q-V-4 | Wording of the hardware check's reason line. | The two templates of V-10.4, filled with this PC's measured times. |
| Q-V-5 | The 10 s clip used to time the PC: which recording? | A short CC BY 4.0 English clip from FLEURS (credited in the notices), or a clip you record yourself. |
| Q-V-6 | A warning before the 5-minute cap? | No warning in v1; at 5:00 it finishes the text as if you had clicked. |
| Q-V-7 | Messages for errors the design does not show: no microphone; microphone unplugged or busy; model still downloading; model failed to load / processor too old; cloud key missing, refused, out of quota; no network. | Show them in the same calm note as "Microphone blocked", with one action where there is one (open Sound settings, open Voice typing). A short wording list to be put on a variants page for your pick. |
| Q-V-8 | The one-time privacy note per engine: where and what words? (8b: shown once per engine, then never) | The first time each engine is used, the composer's note shows the page's privacy line for about 5 s; once per engine. |
| Q-V-9 | Ship NVIDIA CUDA support (+136 MB) or Vulkan only? BENCH measured CUDA; Vulkan was not measured. | Vulkan only in the installer; measure Vulkan on your RTX 4080 at the gate; CUDA as an optional download later only if Vulkan is clearly slower. |
| Q-V-10 | Typing on the keyboard while dictating: ignore it, or stop the dictation? | Ignore it (the composer is read-only while listening); Esc cancels. |
| Q-V-11 | "Send it as the prompt" while a turn is still running: interrupt and send, queue, or leave the text unsent? | Leave the text unsent (never interrupt a running turn by voice). |
| Q-V-12 | Processors without AVX2 (rare on Windows 11): ship the slower no-AVX engine or say "not supported"? | Not supported locally; the cloud engine still works. |
| Q-V-13 | A Remove button for downloaded models (the design has none)? | Yes, as a small action in each installed row, on a variants page first. |
| Q-V-14 | The model notes say Medium and Large v3 "can also translate speech into English", but there is no translate option. Keep, drop, or add the option? | Drop the translate notes for now. |
| Q-V-15 | The cloud model: gpt-transcribe (about 0.45 US cents a minute, words after you stop) or the Realtime live model (about 1.7 cents a minute, live words)? | gpt-transcribe with its streamed text during Finishing; the page's "about half a US cent a minute" stays true. |
| Q-V-16 | Storing, replacing and removing the OpenAI key on the Voice typing page. | Reuse the Providers page's API key field and its refusal line, in this row. |
| Q-V-17 | The Auto language line says "pick one if Hindi comes out as Urdu"; the benchmark mostly saw Urdu come out as Hindi on small models. | `Whisper works out the language; pick one if Hindi and Urdu get mixed up`. |
| Q-V-18 | "Checked just now" on later visits reads wrong. | Later visits read `Checked on {date}`. |
| Q-V-19 | A microphone picker (the design follows the Windows default input only). | No picker in v1; follow the Windows default. |
