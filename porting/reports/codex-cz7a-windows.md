# CZ-7a Windows verification, 2026-10-05

Worktree: `E:\Droppy\wt\cx\v-cz7a`; delivery branch: `codex/verify-cz-7a`.
Source pin: B3 `52bc7926097cd622d08d6294e0d8b08a48c7516e`.
Staging merged at `50a6197b960df6b287ea863b956fc815c01d392b`; CZ-5
`476fd501bfb038ac2dd4a105a1747610e9fb270e` was already an ancestor.

## Fixes

- `CustomizationSectionsTests.Off_shows_the_note_alone_and_both_styles_keep_their_settings`
  now requires the ordered open sections Off, Voice orb and Working dots. The light's
  Off card still contains exactly its original note, and both light styles retain
  their stored values. Specification CZ-3.3 places Voice orb and Working dots after
  the selected style's sections; CZ-8's note-only rule describes the Off card.
  The previous whole-container single-card assertion failed in the Windows slice.
  Its missing null-forgiving annotation was repaired after a CS8604 build failure.
- Three staging tests carried `Decision="OD-0075"`, which violates R-TEST-1's ED
  format. Traceability failed alone twice, listing the scripts-editor hairline
  review, Settings corner fade and native main-window corner tests. They now cite
  `ED-338`, which implements that owner decision. No behavioral assertion changed.
  Their source files were otherwise identical to staging at the merged SHA.
- The engineering ledger was reconstructed from original Git blob bytes: all
  1,390,766 staging bytes, the 8,535-byte CZ-5 section and the 6,162-byte CZ-7a
  section remain intact, with CZ-7a at the end. STATE matched staging byte-for-byte.

## Windows results

| Check | Passed | Failed | Skipped | Total |
|---|---:|---:|---:|---:|
| CZ slice after Off correction, before trait correction | 116 | 1 | 0 | 117 |
| Traceability before correction, alone run 1 | 2 | 1 | 0 | 3 |
| Traceability before correction, alone run 2 | 2 | 1 | 0 | 3 |
| Full App non-native suite, before trait correction | 8,138 | 4 | 0 | 8,142 |
| Traceability after correction, alone run 1 | 3 | 0 | 0 | 3 |
| Traceability after correction, alone run 2 | 3 | 0 | 0 | 3 |
| Final CZ slice | 117 | 0 | 0 | 117 |
| Final dots/control checks | 57 | 0 | 0 | 57 |
| Refreshed full Core non-native suite | 4,171 | 0 | 0 | 4,171 |

App build after corrections: 0 warnings, 0 errors. The full App run took 17 m 31 s.
The full suite used the compiled binary preceding the trait-only correction;
its Traceability failure is covered by the two fresh-build reruns above.

The other three full-suite failures all passed individually twice:

| Full-suite failed method | Alone 1 | Alone 2 |
|---|---|---|
| `ComposerWorkingDotsEaseTests.A_later_pick_eases_the_dots_on_the_render_server_over_point_32_seconds_with_no_ui_frame` | 1/1 pass | 1/1 pass |
| `ComposerWorkingDotsEaseTests.The_render_server_is_asked_for_frames_only_while_the_blend_runs` | 1/1 pass | 1/1 pass |
| `TerminalStoreTests.Foreground_job_detection_answers_false_when_the_enumeration_times_out` | 1/1 pass | 1/1 pass |

These do not count as real failures under the lead's alone-twice rule. No production
change was made to the dots easing or Terminal process enumeration.

## Commands and evidence

The continuation uses Git Bash with system Windows PowerShell and its system module
path so heavy.sh's real memory check runs. `heavy.sh` is sourced unchanged;
`HEAVY_NO_TRAP=1`, `heavy_acquire`, the build or lead test script, and `heavy_release`
are used. An EXIT trap calls `heavy_release` even on an error. One build/test at a time.
No replacement of `heavy_free_gb`, manual lock removal, app launch, native test,
installer, package, microphone, model download, real provider CLI or network test.

Build: `dotnet build tests/Marrow.App.Tests -v q --nologo`.
CZ filter: `(FullyQualifiedName~Customization|FullyQualifiedName~CzControls|FullyQualifiedName~ComposerPreview|FullyQualifiedName~Traceability)&FullyQualifiedName!~Native`.
Full filter: `FullyQualifiedName!~Native`.
Traceability filter: `(FullyQualifiedName~TraceabilityTests)&FullyQualifiedName!~Native`.
Each failing method is rerun with its own fully qualified name and the Native exclusion.
Tests use `one-names-long.sh`; `one-full.sh` supplies isolated error detail.

Evidence retained in `E:\Droppy\work\scratch\codex-cz-7a\`: `app-full.log`,
`alone-twice.log`, `cz-final.log`, `dots-final.log`, `core-final.log`, `run-heavy.sh`,
`repeat-failures.sh`, and the build logs. Core's refreshed build also passes with
0 warnings and 0 errors.

**READY for the requested CZ-7a non-native Windows verification**, under the lead's
alone-twice rule. No remaining confirmed failure. The full App raw run is not claimed
green: its four failures and the exact isolated results are above. CZ-7b live preview
and demo remain outside this page-row unit. This report cannot establish port-wide
exact parity.
