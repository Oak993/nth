# CZ-8 independent review

**Verdict: FIX FIRST.** Reviewed on 2026-10-05. Production code and existing tests were left unchanged.

Target: `cloud/f-cz-8`, `d1ad5d9e0c05a762352aa8f81f5a3d5bd72e0513`. Review branch: `codex/review-cz-8`.
The merge base with the declared source branch, `origin/codex/verify-cz-7a`, is
`ccc58fc708cb91182ab08771e6147c63d47524cc`. The full diff includes the terminal-staging merge,
VT-13/14/15, UE-4/5, traceability changes and a wallpaper design snapshot: 242 paths.
The CZ-8-only comparison is `0cea7df621fa560256b1c0fa77c04df73485b9ea..d1ad5d9e0` (eight paths).
Review authority: customization-SPEC CZ-11.1, CZ-3.5.3, CZ-T80/81/82/98, CZ-12/14/15;
locked `design-src/customization/`; voice-SPEC V-7.14, V-9.6, V-16.2/16.3;
OD-0076; and ED-NEW-cloud-cz-8. The additional merged changes were inspected with a read-only
review subagent under PORT_CONTRACT section 15.

## Numbered findings

1. **[P2] Speech reports the managed UI culture instead of Windows' display language.**
   `src/Marrow.App/Port/Settings/SpeechSections.cs:41`.
   With `voiceTyping.lang = null`, `voiceOrb.langMode = Windows`, a Windows display language of
   `hi-IN` and a managed UI culture of `fi-FI`, the row says
   `Auto-detect: Whisper works out the language`, instead of `Hindi (from Windows)`.
   A Windows display language of `en-US` fails similarly. This contradicts CZ-11.1's C case and
   voice-SPEC V-7.14. The merged Voice typing page already uses `WindowsDisplayLanguage.Current()`
   (VoiceTypingSettingsPage.cs:86,184), backed by `GetUserDefaultUILanguage`; the two pages can disagree.
   ED item 2's use of the other Customization row as justification does not establish the system language.
   Failing oracle: `REVIEW_Windows_default_uses_the_system_display_language` (two cases, CZ-T98).

2. **[P2] The three links are indistinguishable to assistive technology and expose no summary.**
   `src/Marrow.App/Port/Settings/SpeechSections.cs:48` (also the refresh at :35–37).
   Tab to any Speech link: its accessible name is `Change`, rather than its row title
   (`Speech model`, `Language`, `Engine`), and its help text is empty. CZ-15 requires the row title
   as the control name and its detail as help text. The visible label should remain `Change`.
   `CzRow` names its decorator, which does not name the focusable capsule; `SetDetail` also does not
   update the capsule's help text. The existing new CZ-T81 test asserts the incorrect name, so its pass
   does not prove accessibility parity.
   Failing oracles: `REVIEW_Speech_link_is_named_for_its_row_and_exposes_its_detail` (three cases),
   `REVIEW_Speech_link_help_text_follows_the_engine_summary` (one case).

3. **[P2] Speech's capsule paints cut on theme changes and hover.**
   `src/Marrow.UI/Port/Settings/CzControls/CzButton.cs:41`, reached by
   `src/Marrow.App/Port/Settings/SpeechSections.cs:46`.
   Change an already shown dark Settings tree to light with Reduce Motion off and the inherited
   `AppearanceTurn` flag enabled. The capsule jumps in the change's own frame from `#33000000`
   to `#8cffffff`; CZ-14.1 and ED-253 require every token to take Settings' 0.32 s turnover.
   `Refresh` constructs a new resolved brush immediately. `CzCrossfadeText.cs:49` similarly replaces
   detail foregrounds during measurement. Moving a real headless pointer over the Speech capsule
   also jumps from `#33000000` to `#4d000000` in the event's own frame. The locked design's
   `gen.py:298` `.czbtn` rule requires `background-color .12s ease`; the 60 ms midpoint should be intermediate.
   This is an inherited shared-control defect exposed by
   the new Speech section, rather than a change to those shared files in CZ-8.
   Failing oracles: `REVIEW_Speech_capsule_participates_in_Settings_theme_turnover`,
   `REVIEW_Speech_capsule_hover_fill_takes_the_designs_point_12_second_transition`.
   The theme test asserts that turnover is enabled before testing the change, so an unmarked test
   window is not the explanation for the failure. The hover test verifies `IsPointerOver` before
   inspecting the fill and hosts the actual Speech section within the existing headless window.

4. **[P2] Enabling Reduce Motion does not still an active Speech detail transition.**
   `src/Marrow.UI/Port/Settings/CzControls/CzNumber.cs:28`, reached by
   `src/Marrow.App/Port/Settings/SpeechSections.cs:37`.
   Switch the engine to cloud, advance 120 ms into its detail fade, enable the inherited system
   Reduce Motion property, then advance one 60 Hz frame. Incoming opacity continues to animate
   (`0.47974030646462107`), instead of settling at 1. `CzNumber.To` checks `motion.Still` only when
   the transition begins; `Step` never rechecks it. OD-0076 and CZ-14 require owner-added motion
   to still when the preference changes. This is another inherited shared-control defect affecting
   the new section. It is separate from changes *started* with Reduce Motion already on, which pass.
   Failing oracle: `REVIEW_Enabling_Reduce_Motion_settles_an_active_Speech_detail_fade`.

All finding oracles are in `tests/Marrow.App.Tests/Port/Settings/REVIEW_CustomizationSpeechTests.cs`.
They use the existing manual clock, headless page rig and font priming; no production mutation,
microphone capture, model download, network test or real user data is involved.

## Checks that match, and limits

The visible heading, row order and labels are exact: `Speech`; `Speech model`, `Language`, `Engine`;
three `Change` capsules with trailing chevrons. The section is the first child of `Rest`, outside
`Sticky`, below Working dots. Every capsule requests `SettingsPage.VoiceTyping`. The sidebar places
Voice typing immediately after Customization, and Voice typing has no chrome accessories.
Local model names use V-9.6's catalogue formatter; cloud model/engine sentences, explicit language
names, explicit `auto`, null A/B/D and the pure C mapping/fallback match CZ-11.1. The 22 text cases and
six original Speech UI tests pass. Engine summary changes crossfade on the frame clock; changes
started under Reduce Motion land immediately.

Both amended tests were changed for a real reason. CustomizationPageTests.cs:102 and
CustomizationDotsRowsTests.cs:183 previously required `Rest` to be empty. CZ-11.1 now requires
Speech there, so asserting that its section is the sole child is the correct replacement.
Other order, geometry and Working dots assertions were retained. Both amended tests pass.

The two new original classes carry valid class-level `Decision=ED-251` traits. Class-level traits
are supported by xUnit and the merged Traceability checker; these are not missing-trait defects.
The review tests additionally carry `Feature=CZ-8`, `Review=CZ-8` and per-method `Spec` traits.
All three Traceability tests pass in each of App, Core, UI and Services (12 total).

CZ-T82 is **structurally covered, behaviorally unverified**. The base has no CZ-6 sticky stage;
the new test checks placement, not a scroll through the pin/release boundary. ED item 5 acknowledges
this accurately. It does not prove that the stage releases at Working dots' bottom. This remains
an integration dependency on CZ-6, not a newly introduced CZ-8 regression. No native Windows
scroll/release, screen reader or rendered-window comparison was performed on Linux.

The empty local model detail with no model selected is ED item 3's explicit choice for the null
state allowed by V-16.2. CZ-11.1 supplies no replacement sentence; this review does not invent one.

## Execution evidence

Linux, .NET SDK `10.0.401`, repository Avalonia `12.1.2`. SDK installed outside the repository in
`/tmp/cz8-dotnet`; each test command sets `MARROW_PORT_DATA_DIR` to a separate throwaway directory.

Required strict build:

```sh
/tmp/cz8-dotnet/dotnet build Marrow.sln -t:Rebuild -warnaserror -p:EnableWindowsTargeting=true
```

Both the initial strict compilation and the final forced rebuild fail on the existing
`tests/Marrow.App.Tests/Port/Settings/CustomizationSectionsTests.cs:79`, CS8604 (nullable
`rig.Page.Off` in a collection passed to `Assert.Equal`). That line is unchanged in the pre-Speech
base. No existing test or build configuration was repaired. A subsequent incremental strict build
can reuse the diagnostic build's output and report success; it is not evidence of a clean strict
compilation. The explicit rebuild above prevents that reuse.

To execute App tests despite that compiler blocker, the diagnostic run demotes **only CS8604**
using `-p:WarningsNotAsErrors=CS8604`; all other warning policy remains intact. It is not reported
as a passing warnings-as-errors build. The shared slice filter is:

```text
(FullyQualifiedName~Port.Settings|FullyQualifiedName~Port.Voice|FullyQualifiedName~TraceabilityTests)&FullyQualifiedName!~Native
```

Commands use `dotnet test tests/Marrow.<Layer>.Tests/Marrow.<Layer>.Tests.csproj
-p:EnableWindowsTargeting=true --filter '<filter>' --logger 'trx;LogFileName=<layer>-slice.trx'
--results-directory /tmp/cz8-results`. App additionally uses the CS8604 demotion; Core/UI/Services
use `--no-build` against the previously compiled solution outputs.

| Run | Total | Passed | Failed | Skipped |
|---|---:|---:|---:|---:|
| Core slice | 667 | 667 | 0 | 0 |
| App slice, including final review tests | 428 | 387 | 41 | 0 |
| Services slice | 530 | 523 | 2 | 5 |
| UI slice | 360 | 359 | 1 | 0 |
| Pre-Speech App slice, `0cea7df6` | 391 | 359 | 32 | 0 |

App's nine new review cases fail on the actual assertions described above. Its other 32 failures
are 27 font/glyphTypeface errors, two `user32.dll` load errors and three `icu.dll` load errors.
The original 28 CZ-8 cases, both amended tests and all three App Traceability tests pass.
The pre-Speech comparison ran in the detached `/tmp/cz8-pre-speech` worktree, with the same SDK,
filter and single-warning demotion. Its failure-name set is exactly the head's 32 non-review
failures, and every one of its 391 test outcomes matches the head. The head adds 28 passing original
CZ-8 cases and nine failing review cases. This independently confirms that the two amended tests
were not hiding another failing baseline behavior.

Services fails the existing file-lock test and Windows path-format test; five Windows hardware/
Credential Manager cases are skipped by their existing platform guards even though `Native` is
excluded. UI fails the existing analytic VoiceGlowPainter allocation test at
`tests/Marrow.UI.Tests/Port/Voice/VoiceGlowPainterTests.cs:202`: 5,778 B/frame, bound 5,120.
The allocation test and the two failing Services test files are unchanged by CZ-8; these observations
are not promoted to new Speech defects or dismissed as passing. There are 1,936 passing cases,
44 failures (nine new review cases plus 35 other failures), and five existing platform skips across
the four head slices.

Final slice TRX SHA-256 values (files under `/tmp/cz8-results/`):

```text
app-slice.trx      41060fe2edd491aa78edc5d5c7e795511c40ff4b2c091abe7956b3d2ccd2073c
base-app-slice.trx 4607c5a52c2bf94c0473b27a7a2f8efa2fd41ee25adc54698aebad376759897a
core-slice.trx     8425c0c92720ba201ceed758b58dff889ec648eb67c0421ac9692445ea784107
ui-slice.trx       91670bdc92ff9b54051163dd4f7d026ff792d2477dae835cb67b0a8731b5df0a
services-slice.trx 6dd0d9e175924dac4cea6be389aa9b8c6a244e58d8c7558dae4a3b1e057c763c
```

Logs and TRX are retained in `/tmp/cz8-*.log` and `/tmp/cz8-results/` in this execution workspace;
only this report and the review test source are committed. A passing slice or Linux compilation
does not establish Windows runtime parity. The original ED's 29-failure Linux count is not reused
as this run's count.
