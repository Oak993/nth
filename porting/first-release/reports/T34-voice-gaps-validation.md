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


## Round 2 (2026-10-07)

**READY for T9 Windows verification**, as defined by `45096401f90e1d0ae74a9e156cb9f0cb3fa67e60:R-gaps-round2.md`. This supersedes Round 1's RR-12 implementation blocker. It does not claim full release/native text acceptance.

| Row | Round 2 result |
| --- | --- |
| RR-10 | ALREADY DONE under the lead ruling; native acceptance awaits T9 |
| RR-11 | BLOCKED here; stopped per lead, handed to T33 after VT-12 READY; requires local VT-12/VT-16 fixes |
| RR-12 | DONE implementation; paragraph/editor sharing and wrapped shaped-cluster motion complete; only Windows proof remains |

| Round 2 run | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Core voice + traceability | 656 | 0 | 0 |
| Services voice + traceability | 491 | 0 | 12 |
| Combined affected App + traceability | 121 | 0 | 0 |
| Unchanged ComposerDictation/ComposerTextView tests | 26 | 24 | 0 |

The affected App filter adds `DictationFragment`, `DictationClusterParityTests`, `DictationParagraphLifecycleTests` and `ComposerParagraphLayoutTests` to the Round 1 64-test filter. All 57 new checks pass, with actual CPU text rendering and fake scheduling/preferences. The original composer run's 24 failures are 19 glyphTypeface creation failures (6 default, 13 Inter) and 5 missing user32.dll calls; none is behavior acceptance or a changed assertion. Core/Services full-suite baseline limitations remain as documented in Round 1; full native suites are on the Windows list. See RR-12's **Needs Windows — T9** list for the exact remaining text proof and integration matrix, and RR-10's native CUDA list. RR-11 is the lead's T33 handoff, not further work in this branch.

Test-first sequence: wrapped/paragraph/caret red 1/7 passed; line-based reflow red 37/38; native glyph-cluster green 38/38; font-feature red 5/6; line-height/spacing red 5/7; terminal LF/CRLF red before native leading-line repair; final 121/121. New fixture compilation and oracle arithmetic corrections were retained in evidence and are not counted as behavior witnesses. No existing test/fixture/assertion/reference/tolerance was changed. Independent contract section 15 review cleared final implementation for T9 after verifying all repairs, isolated style invalidation and unchanged Urdu guards.

Gates: B passes 8/8; protected paths unchanged against HEAD. E:/ reference checks are unavailable, regardless of the script's printed clean sentence. Gate A/full solution/Gate D/native release acceptance remain Windows work. All application dependencies in these checks are fake/scripted; there is no real CUDA/model download, network, GPU, microphone or model execution. The throwaway ICU ABI forwarder and explicit Linux font do not prove native Windows rendering.

Integrity: ED-NEW-voice-gaps-4 and STATE append in binary mode; original Round 2 prefixes (STATE 85,736 bytes; ED 1,789,298 bytes) retain their exact hashes and mixed newline bytes. A comparison against all four protected branches finds no owned file overlap (shared ledgers excluded because the user explicitly authorized appends). No original test path from the task base differs. The mirror was read without merging. Only codex/voice-gaps is pushed without force. The new RR-12 implementation and reports form one Round 2 row commit.
