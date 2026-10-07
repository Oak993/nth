# T18 / VT-16 cloud transcription follow-up

2026-10-07. Branch `codex/vt-16`, from `origin/claude/verify-terminal-20261001` (`1291606c`). Production revision: `95bd5144`.

## Prior implementation and missing integration

ED-311 and the earlier VT-16 merge (`4c5f2970`) already provided `OpenAiTranscriber`, PCM16 WAV encoding, multipart fields, `gpt-transcribe`, SSE parsing and word operations, JSON response handling, timeout/error mappings, and `VoiceKeyStore` over SecretStore service `Marrow Code`, account `voice-openai`. Existing fake-handler service tests cover V-T47 and V-T110 through V-T113. These were retained.

The page still deliberately disabled its Add key capsule. VoiceSession still used local live/window passes for cloud, did not consume the cloud operations during Processing, and App composition did not select the cloud adapter or its model when no local model was selected. These were the missing VT-16 paths.

## Implemented

- A Processing speech-engine seam connects SSE operations to the main-actor session, with generation guards and subscription cleanup. Word pacing preserves operation order across bursts; a done correction cannot precede its word's queued Add, and Landing waits for the queued words.
- Cloud records the complete session in memory and sends one final request after stop, without the local 30-second window, padding, silence timestamp filtering, or Listening requests. Cancellation clears recording state and cancels an in-flight HTTP stream. Key validation precedes microphone capture, including after a previously successful cached model load.
- App composition selects the cloud adapter and exact `gpt-transcribe` model independently of local model selection. The production HTTP handler disables redirects and cookies. Test defaults remain fail closed. Cloud stays off by default; settings schema/defaults are unchanged.
- The key row follows Providers: masked 200px, 12pt monospaced rounded field, eye toggle, exact placeholder/accessibility/refusal text, immediate protected save/replacement, blank removal, Add key/Cancel fold. Closing, switching local, or detaching clears transient field text and remasks it. Keys are never added to settings, logs, or an ordinary file.

## Test-first history

1. `d766b10d`: new fake-only recording/session/key-row regression tests, observed failing before production changes.
2. `552a4298`: explicitly bound the new refusal test to its in-memory backend; its assertions were retained.
3. `903a5cc7`: independent review's SSE ordering regression, red with actual `wurld` instead of expected `world` when the real DictationWords applies the emitted schedule.
4. `9251a3df`: in-flight stream cancellation and cached-load key-removal regressions.
5. `f6e30b31`: added required Rule traceability metadata to the new tests; no behavioral assertions changed.
6. `95bd5144`: production implementation.

The branch only adds test files relative to the requested base. No pre-existing tests, assertions, tolerances, references, or traceability checkers were edited. All new cloud HTTP uses an in-process handler and synthetic in-memory credentials; no real network, key, transcription model, microphone, or local speech model was used. Independent reviewer `/root/vt16_review` identified the ordering bug, reviewed its repair, and found no remaining confirmed VT-16 implementation defect; review is not a substitute for Windows validation.

## Environment and validation

Linux; .NET SDK 10.0.401 installed under `/tmp/vt16-dotnet` per IMPLEMENTING. There is no tracked `global.json` on this base. Avalonia remains the repository's 12.1.2. Builds use `-p:EnableWindowsTargeting=true`; test data is isolated with `MARROW_PORT_DATA_DIR=/tmp/vt16-data`. Raw logs/TRX are under `/tmp/vt16-evidence` for this execution, not durable repository artifacts.

| Check | Passed | Failed | Skipped | Limit |
|---|---:|---:|---:|---|
| Final Core voice + Traceability + Layering | 660 | 0 | 0 | Linux, current production implementation |
| Final Services cloud + Traceability + Layering | 57 | 0 | 1 | Credential Manager Windows test skipped |
| Final App new key/order + composition + Traceability/Layering | 15 | 0 | 0 | Linux headless |
| App page suite | 25 | 1 | 0 | The preserved pre-VT-16 inert-button assertion fails |
| Broad Core final | 2697 | 299 | 0 | Host crash; aborted after 2996 results, not the complete suite |
| Broad Services | 3863 | 1265 | 52 | Completed, 5180 total; started before final scheduling/metadata/lifetime follow-ups |

Final targeted commands (each with `-p:EnableWindowsTargeting=true` and its own TRX logger):

```text
dotnet test tests/Marrow.Core.Tests/Marrow.Core.Tests.csproj --filter "FullyQualifiedName~Port.Voice&FullyQualifiedName!~Native|FullyQualifiedName~TraceabilityTests|FullyQualifiedName~LayeringTests"
dotnet test tests/Marrow.Services.Tests/Marrow.Services.Tests.csproj --filter "FullyQualifiedName~Port.Voice.Cloud|FullyQualifiedName~TraceabilityTests|FullyQualifiedName~LayeringTests"
dotnet test tests/Marrow.App.Tests/Marrow.App.Tests.csproj --filter "FullyQualifiedName~VoiceTypingKeyRowTests|FullyQualifiedName~VoiceCloudWordOrderTests|FullyQualifiedName~VoiceCompositionTests|FullyQualifiedName~TraceabilityTests|FullyQualifiedName~LayeringTests"
```

The broad Services run also exposed missing Rule metadata on the newly added session tests. That omission is fixed by `f6e30b31`; the final Services traceability checks pass. The broad run is recorded as observed, not represented as final whole-suite validation. An earlier broad Core run also aborted (3080 passed, 438 failed); abort timing varies because unrelated Windows process callbacks crash the host.

The 15 new cases (Core 2, Services 8, App 5) were repeated in five sequential rounds using `--no-build`: each round passed 2/2, 8/8 and 5/5 respectively, zero failures or skips. This is focused repeat evidence, not five whole-suite runs. App builds initially retried shared output copies when I launched overlapping builds; the final serial `dotnet build tests/Marrow.App.Tests/Marrow.App.Tests.csproj --no-restore -p:EnableWindowsTargeting=true` succeeded with 0 warnings and 0 errors (build-final.log).

The exact requested broad filter was used for both Core and Services: `--filter "FullyQualifiedName!~Native"`. Excluding names containing Native does not remove every test that calls Windows APIs. The Linux failures include unavailable kernel32/ICU and Windows process/path/file-watch assumptions. No assertions were weakened to accommodate Linux.

## Windows work and verdict

- Run Core and Services with the exact broad filter on Windows, including the five whole-suite repetitions required by IMPLEMENTING R-TEST-5 for concurrency work. Focused repetitions on Linux do not discharge that requirement.
- Run the App suite on Windows. The existing `VoiceTypingSettingsPageTests.The_cloud_engine_opens_the_key_row_which_stays_inert_until_VT_16` conflicts with the requested active key editor and is preserved unchanged: its disabled-button assertion fails. The owner/lead must resolve that outdated requirement before a green complete App suite is possible.
- Run the existing Windows Credential Manager V-T47 test using its disposable synthetic credential; verify the key editor's keyboard/focus, eye, fold and fade behavior in the Windows window with fake credentials/HTTP, without a real key or model call.
- Pre-existing integration limits remain: `VoiceFeature.Enabled` is false, the production VAD/resampler seams remain unbound, and cloud note presentation is not connected to the composer. These are feature activation/session/note integration work outside this VT-16 follow-up. This branch does not make the whole voice feature release-ready.

**NOT READY** for complete acceptance: scoped implementation checks pass, but the preserved conflicting test and required Windows validation remain open. No live cloud interoperability is claimed.

Ledgers are appended as bytes under `ED-390` / `ED-390`; their existing mixed CRLF/LF content is preserved.
