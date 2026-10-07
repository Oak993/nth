# T34 voice gaps — validation and release verdict

**NOT READY.** RR-10 implementation is DONE with fake acceptance. RR-11 is BLOCKED on exact pill countdown and preparation/cloud hooks. RR-12 is BLOCKED on RTL paragraph and wrapped-fragment parity. These are implementation blockers in addition to the Windows acceptance work.

Base: `285b6b7a99b39e0e517f34c8d34a1860c945f83f`; branch: `codex/voice-gaps`. Row commits start with `e553dc79` (RR-10) and `a242582c` (RR-11); RR-12 and this validation are committed together. See the three `T34-voice-gaps-RR*.md` reports for missing-code citations, implementation scope and exact handoff work.

## Linux results

.NET SDK 10.0.401, runtime 10.0.12, throwaway `MARROW_PORT_DATA_DIR=/tmp/t34-data`. Build options: `-m:1 -p:UseSharedCompilation=false -p:EnableWindowsTargeting=true`; GC heap limit 4 GiB. Existing tests, assertions, references, tolerances and test time bounds were not edited. Added test helpers live in separate fixture files and tests carry existing ED-351/ED-292 traceability traits; the requested new engineering entries use `ED-NEW-voice-gaps-1/2/3`.

| Run | Passed | Failed | Skipped | Interpretation |
| --- | ---: | ---: | ---: | --- |
| Core voice (`FullyQualifiedName~Port.Voice`) | 653 | 0 | 0 | Green scripted voice suite |
| Core traceability | 3 | 0 | 0 | Unchanged metadata checks |
| Services voice + traceability | 491 | 0 | 12 | Includes 8 new companion cases; native Windows tests skip |
| App affected controls/models + traceability | 64 | 0 | 0 | Includes actual mounted CUDA buttons/error surfaces and both new CUDA boundary regressions |
| Broader App voice/editor/notes + traceability | 338 | 35 | 1 | All failures stop at `user32.dll` (6) or headless Inter/default glyph typeface creation (29); native microphone test skips |
| Full Core, stock Linux environment | 1949 | 235 | 0 | Aborted at Windows ProcessLauncher execution; includes missing `icu.dll` and platform failures |
| Full Core, ICU ABI forwarded | 2314 | 177 | 0 | Still aborted at Windows ProcessLauncher execution; not full-suite acceptance |
| Full Services, stock Linux environment | 3450 | 1186 | 42 | Windows/resource/path/library failures and repeated fake-probe/watcher timeouts; test host interrupted after 8m35s; not full-suite acceptance |

Affected App filter:

```text
FullyQualifiedName~Cuda|FullyQualifiedName~VoiceReleaseNoteTests|FullyQualifiedName~VoiceErrorMountTests|FullyQualifiedName~DictationAddConfirmationTests|FullyQualifiedName~CapCountdownLineTests|FullyQualifiedName~TraceabilityTests
```

Broader App filter:

```text
FullyQualifiedName~Port.Voice|FullyQualifiedName~ComposerDictation|FullyQualifiedName~ComposerVoiceNotes|FullyQualifiedName~TraceabilityTests
```

App rendering and the second Core attempt used `LD_LIBRARY_PATH=/tmp/t34-icu`: a disposable ABI forwarding library exposing the repository's unversioned ICU calls through installed Linux ICU 76. It contains no replacement Unicode behavior and is outside the repository. It is diagnostic Linux support, not Windows acceptance. The initial unchanged App baseline (CUDA + editor) was 30 passed/25 failed at missing Windows ICU; a scoped forwarded Urdu baseline also hit the existing headless Inter failure. No production DLL or test/reference was changed to make those results pass.

## Static gates

`check_protected.mjs` returned 0 for workspace protected paths against HEAD, but printed missing Windows reference directories; that reference-clean wording is not accepted as reference proof. Gate B passed all 8 implemented checks. Gate A failed 5 checks because `E:/Droppy/reference/droppy-code-v1.7.0` is unavailable here (7 ledger/inventory checks passed). `req_verify.mjs` could not launch its bare `dotnet` command from this tool environment; its runner also requires Windows `cmd /c ver`. Full solution/Gate A/Gate D acceptance remains a Windows run, with the pinned reference and standard build configuration. No gate script, protected build input or requirement status was modified.

## Red/green evidence

New installer tests initially failed compilation on missing companion installer/archive types. Refresh regression failed `Expected: 2; Actual: 1` before cache/reapply repair. Both final CUDA boundary tests failed before correction: post-probe `TaskCanceledException`, and the fake HTTP request entering while initial Downloading delivery was still in progress. They pass in the 64-test run after cancellation handling and the start gate. Traceability first rejected new missing traits/embedded helper types; only the new files' labels/helper placement were corrected, and all 9 project metadata tests pass. The unchanged Add assertion was replayed against the base layer, but that isolated run stopped at headless Inter creation before the assertion. The base code and existing DictationWords tests establish the missing prior-word visual confirmation; the new assertion passes in the combined affected-control run after the two-line repair. No expectation or fixture was weakened, and the isolated font failure is not counted as a behavior red witness.

No real CUDA/model download, microphone, GPU or speech engine was exercised. In-memory handlers and the existing guarded loopback fake provide download bodies; scripted capture/engine/probe and fake clocks/frames provide voice facts. SDK/NuGet build tooling and requested Git operations are separate from the application's download paths.

## Windows run list

1. Repeat affected Core/Services/App fake suites and full suites on the supported Windows build environment, with the normal ICU/fonts/resources. Run existing `ComposerDictation*`, `DictationWordsTests`, `VoiceNote*`, `ComposerVoiceNotesTests`, CUDA offer/installer and countdown suites unchanged.
2. Fake CUDA files: refusal with an open dummy handle, rollback after publication failure, cancellation/repair/restart and settings detach/reattach. Render CUDA A at supported DPI, themes and Reduce Motion; verify keyboard/UIA focus, cancel name and polite progress. Native resource acceptance must additionally validate pinned archive entry paths and complete loader restart/backend selection; this task supplies no such native proof.
3. Notes: every Windows/internal destination, actual target-row focus/key editor, exact words/warning appearance, once-only announcements, field/notice movement, 5.2-second expiry and retry/reattach behavior. Reconcile first-download suppression with the preparation/pill owner.
4. After the pill owner integration, mount exact Countdown C at its ruled footer geometry; verify 270-second start, 15/5-second color thresholds, cap completion, 30/10-second once-only announcements, OKLCH transitions and Reduce Motion. Model-only green tests do not close mounting.
5. Implement and prove automatic/mixed RTL paragraphs and independent wrapped-word fragments with full-context shaping. Add/revise/remove/empty-word/wrap-count changes, final-line caret/scroll, landing/undo and withdraw need pixel comparison against the same editor layout and pinned design at supported DPI and Reduce Motion.

Independent source review was performed under PORT_CONTRACT §15 and found the corrected transaction, mounting/lifecycle, geometry and timing issues. It retained the same NOT READY verdict and found no further concrete issue after the final CUDA repairs. The unproven fragment renderer was excluded.

## Integrity

Both ledgers are appended in binary mode. Their original base prefixes remain byte-identical, including mixed CRLF/LF. Each row appends its own entry and checks `git diff --stat`. Ownership comparison against all four named branches finds no protected source/test/reference edit; shared ledgers are touched only by the explicitly requested appends. No existing test file differs from the base, and no reference image or tolerance was altered. Only `codex/voice-gaps` is pushed, without force.
