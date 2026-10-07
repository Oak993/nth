# T27 CZ-7b voice preview

Verdict: **NOT READY**. The stage implementation is built and reviewed. Windows behavior, motion and pixels remain unverified, and staging's shared voice-pill V-T133 remains open (ED-389).

The preview composes the real `VoiceOrbSlot`, `VoicePillSlot`/`VoicePill`, both shared crosses, `VoiceGlowLayer`, `DictationLayer`, `ComposerLightView` and `VoiceLanguageQuestion` through `ComposerVoiceController`. CZ-7a's controls update those components. It has the locked scripted speaker/revisions/final pass, Leave/Send, cancellation, attachment transition, individual crossed-line fades, relabel/replay, voice light targets and page-local language answers. No production audio, recognition model, network, chat draft or send is composed.

The independent fixture generator executes the locked JavaScript `SCRIPT`, `SYN`, `STREAM`, `synthAt`, `planFinal`, `vcFollow` and `czBez`. Default auto-stop is 7.160813040910289 s. Final operations occur at stop +0.18/+0.25/+0.37 s, preserving the duplicate design revision. Processing lasts 0.79 s. Landing lasts 0.5 s, then Send waits 0.35 s; its running display lasts 3 s.

Changes are in commits `1700ec59` (new tests/fixture), `1d5de7fe` (implementation), and `b4c1875a` (merge latest staging `622e6168`). The evidence commit also normalizes only the new tests' literal spelling to the required eight-digit Unicode escapes; assertion values are identical. No existing test/tolerance/assertion/reference was changed by this unit. The merged OD-0125 test changes belong to staging and are preserved unchanged against `622e6168`.

Independent static review verified script tuples and final scheduling, and the repairs to pending sends/replays, both crosses, outside attachment 10/20, native popup ownership/material lifecycle, signed below-tip motion and per-line masks. Its final review found no further clear defect; it did not execute Windows tests or certify visual parity.

## Evidence

Linux, .NET SDK 10.0.401, repository `/workspace/marrow-port`. Commands used `/tmp/cz7b-dotnet/dotnet`; test commands set `MARROW_PORT_DATA_DIR=/tmp/cz7b-data`. Logs/TRX are gzip files here; `evidence.json` contains compressed and uncompressed hashes. The full-suite failures have not been characterized against a pristine baseline and are not declared regressions or waived.

| Run | Result |
| --- | --- |
| App tests project build, after staging merge | PASS, 0 warnings / 0 errors |
| Core voice + traceability + sticky | PASS, 696 |
| Services portable voice | PASS, 462 |
| App portable subset before merge | PASS, 26 (raw log remained in `/tmp/cz7b-evidence/app-linux-final.log`) |
| App expanded subset after merge, including reference hashes and CZ-7a rows | 39 pass / 3 fail: Inter glyphTypeface unavailable |
| UI popover subset, including new SiteCard test | 58 pass / 2 fail: `$Default` glyphTypeface unavailable |
| All new App preview cases attempted on Linux | 0 pass / 22 fail at shared Windows `user32.dll` motion preference; marked WINDOWS-ONLY |
| Full Core attempted before final metadata correction | 2378 pass / 330 fail, then process run aborted at `kernel32.dll` |
| Full Services | 3929 pass / 1306 fail / 60 skip |

Full Core failures include Windows `icu.dll`/`kernel32.dll`, Windows path assumptions, case-insensitive environment collisions (`HTTP_PROXY`/`http_proxy`), and one new-test traceability error from using OD rather than ED metadata. The metadata was corrected without changing the guard or assertions; the final 696 run includes all three traceability tests. Full Services failures include Windows native libraries, Windows fixture paths, proxy-key collisions, and watcher/probe timeouts. No Core or Services assertion was edited.

Commands (result logger/output destinations omitted below only for readability; raw artifacts retain results):

```sh
dotnet build tests/Marrow.App.Tests/Marrow.App.Tests.csproj -p:EnableWindowsTargeting=true -m:1 -nr:false
dotnet test tests/Marrow.Core.Tests/Marrow.Core.Tests.csproj --no-build
dotnet test tests/Marrow.Services.Tests/Marrow.Services.Tests.csproj --no-build
dotnet test tests/Marrow.Core.Tests/Marrow.Core.Tests.csproj --no-build --filter 'FullyQualifiedName~Marrow.Core.Tests.Port.Voice|FullyQualifiedName~TraceabilityTests|FullyQualifiedName~StickyStageTests'
dotnet test tests/Marrow.Services.Tests/Marrow.Services.Tests.csproj --no-build --filter 'FullyQualifiedName~Marrow.Services.Tests.Port.Voice&Platform!=WINDOWS-ONLY&FullyQualifiedName!~Native&FullyQualifiedName!~Windows'
dotnet test tests/Marrow.App.Tests/Marrow.App.Tests.csproj --no-build --filter 'FullyQualifiedName~PreviewSessionTests|FullyQualifiedName~ComposerPreviewTests|FullyQualifiedName~CustomizationStageTests|FullyQualifiedName~PreviewMakersTests|FullyQualifiedName~TraceabilityTests|FullyQualifiedName~ComposerVoiceDesignReferenceTests|FullyQualifiedName~CustomizationVoiceRowsTests|FullyQualifiedName~CustomizationPageTests|FullyQualifiedName~CustomizationPersistenceTests'
dotnet build tests/Marrow.UI.Tests/Marrow.UI.Tests.csproj -p:EnableWindowsTargeting=true -m:1 -nr:false
dotnet test tests/Marrow.UI.Tests/Marrow.UI.Tests.csproj --no-build --filter 'FullyQualifiedName~PopoverSiteCardTests|FullyQualifiedName~PopoverCardTests|FullyQualifiedName~PopoverHostThemeTests|FullyQualifiedName~PopoverAppearTests|FullyQualifiedName~PopoverWindowHitTestTests|FullyQualifiedName~TraceabilityTests'
node tests/fixtures/customization/voice.generate.cjs
```

`red-core.log.gz` records test-first compilation failure for the missing design-script APIs. An initial App red attempt hit an MSBuild child-node failure and is not counted as a semantic red result. The early full Core run is not presented as a completed green suite. The `/tmp` logs include intermediate build attempts; the committed build evidence is the post-merge successful build.

## Windows-run list

1. Run **all 22 `ComposerPreviewVoiceTests` cases**, CZ-T73 to T78 and T94 to T96. Verify normal/reduced motion, inline/cross/float styles, both orb sizes and attachment places, exact scripted words/revisions/final timing, early stop and Processing cancel, all cancellations including pending replay/send, relabel at 140/160 ms and replay at 200 ms, language-name first-live boundary, above/below native question, row/orb focus, self-answer at 1.6 s, no persistence, line masks and compaction.
2. Run the existing shared-component regression classes: `ComposerVoicePillGeometryTests`, `ComposerVoicePillLabelTests`, `ComposerVoicePillWordsTests`, `ComposerVoicePillClipTests`, `ComposerVoicePillCoverageTests`, `ComposerVoiceCrossTests`, `ComposerVoiceSlotTests`, `ComposerVoiceLightTests`, `ComposerVoiceQuestionTests`, `ComposerVoiceLanguageNameTests`, `ComposerVoiceFocusTests`, `ComposerVoiceSendTests`, `ComposerVoiceFloatLineTests`, `ComposerVoiceSessionTests` and `ComposerVoiceTransitionAuditTests`.
3. Run `ComposerVoiceCleanPillTests` (V-T132/V-T133, canonical/representative/matrix), `ComposerVoiceCleanLabelTests` and `ComposerVoiceDesignReferenceTests` under OD-0121/OD-0123/OD-0125, **without regenerating references or changing any detector/tolerance**. ED-389 records 177/181 V-T133 matrix failures on staging; this unit makes no claim those are resolved.
4. Run `ComposerPreviewPixelsTests`, `CustomizationAllocationTests`, all Customization/sticky/settings regressions, and the real Windows stage gallery/frame checks (CZ-T69 plus voice stage). Compare dark/light and 1x/1.25x/1.5x/2x while listening/finishing/landing; include actual GPU and software fallback, the real glow/light follower, popup native glass/shadow/outline hit testing and keyboard precedence. Inspect the stage and floating-pill clip, not just component properties.
5. Run full Core/Services/App/UI on Windows, including the Linux font/native/path failures above. Respect the repository's quiet-PC/native-lock gate for visual/performance tests.

Suggested first Windows command:

```powershell
dotnet test tests/Marrow.App.Tests/Marrow.App.Tests.csproj --filter FullyQualifiedName~ComposerPreviewVoiceTests
```

The existing contract/native gate controls final acceptance. This report does not mark that gate or CZ-7b verified. `ledger-prefixes.json` proves both mixed-ending ledgers were appended with their prior byte prefixes intact.
