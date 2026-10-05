# A11Y-FIX-1 — staging accessibility fixes

2026-10-05. Branch: `codex/a11y-fix-1`. **NOT READY for complete touched-slice acceptance:** all eight staging accessibility regressions and supplemental focused checks pass, and the solution builds with warnings as errors; the broader App slice still has Linux execution failures. No Windows/Narrator acceptance is claimed.

## Frozen inputs

- Staging/start branch: `claude/verify-terminal-20261001`, `889c9ebd523cbfedb39d9bf06e22ba7b9988ea46`.
- Review and test source: `codex/a11y-1`, `8c6d331e969bf06b53bd52f61e356e88cfdd724f`, including `porting/reports/review-a11y-1.md` read with `git show`.
- Read-only Droppy reference: B3 `52bc7926097cd622d08d6294e0d8b08a48c7516e`, tree `115745914ccc103dfe4e74d96c6c539faab2eedb`, acquired in `/workspace/droppy-reference` and left clean.
- Validated production fix commit: `12687d0ed122d17dde18372bb949e207f3d3d9ec`. Later report/decision edits change no production or test source. The delivered branch head is obtained with `git rev-parse HEAD` after that documentation commit.

## Fixed findings already on staging

| A11Y-1 finding | Fix |
| --- | --- |
| 1 — Voice-model percentage | `VoiceStateColumn` makes its visible percent text polite only during Downloading, turning announcements off before hidden resets. Repeated unchanged whole percents produce no name change. |
| 2 — Customization switch focus | `SmallSwitch` draws the existing CZ 2 px accent ring, offset 2 px, for keyboard focus. Track/knob geometry and resting pixels remain unchanged. Pointer focus explicitly uses `NavigationMethod.Pointer`. |
| 5 — Updates status/progress | `UpdatesStatusCell` has one stable polite peer named from its current incoming text. State and same-state percentage changes raise Name changes; identical updates do not. Animated outgoing faces are not the announcement source. |
| 6 — Wallpaper names | Empty preview copies `Drop a picture here, or choose one`; chooser/Remove copy their visible labels, and the chooser name follows `Choose…` → `Change…`. The installed-photo preview retains upstream's lack of a dedicated photo name. |
| 7 — Wallpaper focus | Preview and small glass actions opt into the app's existing `SystemButtons` focus band. |
| 13 — Getting ready steps | Visible step labels have polite peers whose accessible names include Waiting, Running percent, or Done. Progress is updated even when the state stays Running; visible labels remain verbatim. No new actions or cancellation are added. |
| 14 — Update-pill focus | The actual pill opts into the same existing button focus band. |
| 15 — What's new cards | Category buttons opt into that band following their existing 14 pt continuous shape; names, native Button activation and disabled behavior remain intact. |

`PlainControls.KeyboardFocus` is opt-in at these affected surfaces; the default plain template remains unchanged. It adds no padding or size and draws only for `:focus-visible`. Its band is the existing 3.5 pt accent at 0.25 opacity from `SystemButtons`. Resting pixels, bounds, focus removal, pointer focus and disabled focus are checked in both themes for capsule/card shapes. SmallSwitch's geometry and resting pixels are also checked in both themes, including real headless pointer input.

Parity was checked against `WallpaperCard.swift:26–75,118–123` and `AboutUpdateViews.swift:207–240`. Voice, Customization, Getting ready and the update pill use their approved port specifications; no upstream workflow was redesigned.

## Tests first and corrected capture precondition

The eight `A11yFirstReleaseTests` and three keyboard/calibration checks were imported before production changes. Initial exact-namespace run: eight failures, three passes, no skips. Three additional automation-name/event tests were also red before their production fixes. Test-only commits precede the production fix commit.

The original Customization capture was invalid: the first enabled switch occupied window coordinates `(466,944)` through `(510,964)` while the unscrolled test window clipped at height 900. `IsEffectivelyVisible` does not establish that a control is on screen. The setup now mounts the real page in the port's real ScrollViewer, brings that switch into view, and asserts viewport containment before running the original unchanged focus/pixel assertion. An existing scripted motion-platform seam prevents a Linux native Windows-setting read. A resize-only attempt failed its viewport precondition and is not defect evidence.

With the corrected final setup and the original staging `SettingsView.cs` temporarily restored, the mounted-page focus test and standalone dark/light switch tests all failed at the missing-pixel assertion; the real pointer-input check passed. Restoring the fix makes them pass. This proves the focus finding independently of the original offscreen capture. An independent read-only subagent reviewed the scope, production diff, source parity, capture correction and red evidence, finding no remaining scoped semantic omissions. It did not independently execute final tests or Windows Narrator.

## Pending tests deliberately left out

The staging tree has no `LibraryRowView`, skills download-dialog surface, composer voice controller/orb, or Welcome voice section. The entire `A11yPendingSurfaceTests.cs` stays on the review branch; no pending production branches were merged, and its unused late-bound fixture helpers were removed from the copied helper.

| Findings | Exact excluded regressions | Pending surface |
| --- | --- | --- |
| 3 | `A11Y_Skills_rows_expose_an_automation_action` | SK-15 / `codex/f-sk-15` |
| 4 | `A11Y_Skills_rows_have_visible_keyboard_focus` | SK-15 / `codex/f-sk-15` |
| 8 | `A11Y_Composer_voice_orb_has_visible_keyboard_focus` | Composer VT-10/VT-17 / `codex/f-vt-17` |
| 9 | `A11Y_Composer_announces_the_thirty_second_countdown` | Incomplete countdown production wiring / `codex/f-vt-17` |
| 10 | `A11Y_Welcome_voice_answers_have_names_matching_their_text` | WV-1 / `codex/f-wv-1` |
| 11 | `A11Y_Welcome_voice_answer_has_visible_keyboard_focus` | WV-1 / `codex/f-wv-1` |
| 12 | `A11Y_Welcome_voice_state_change_is_announced` | WV-1 / `codex/f-wv-1` |
| 16 | `A11Y_Skills_download_dialog_has_visible_keyboard_focus` | SK-15 / `codex/f-sk-15` |
| 17 | `A11Y_Skills_download_dialog_progress_is_a_live_region` | SK-15 / `codex/f-sk-15` |

The pending supporting test `Keyboard_Skills_dialog_tab_cycles_and_escape_cancels_without_downloading` is also excluded. Missing Wallpapers-library selection, listening/countdown UI and other incomplete preview surfaces retain the A11Y-1 coverage limits. Native Narrator/UIA announcement delivery and file-picker traversal remain Windows follow-ups.

## Linux validation

SDK 10.0.401, Avalonia 12.1.2, `DOTNET_PROCESSOR_COUNT=4`, temporary `MARROW_PORT_DATA_DIR`. Final build leaves analyzers enabled and suppresses no warning. The copied test-only Linux font mappings use the bundled release fonts; Windows and production font configuration are unchanged. Diagnostic red iterations sometimes disabled analyzers; final acceptance builds do not.

| Check | Result |
| --- | --- |
| `dotnet build Marrow.sln -m:1 -p:EnableWindowsTargeting=true -warnaserror` | PASS: 0 warnings, 0 errors, 1:27.90 |
| Focused Accessibility namespace + App Traceability | PASS: 24/24, 0 skipped (8 original regressions, 3 keyboard/calibration, 3 announcement contracts, 7 focus/rest/pointer cases, 3 Traceability) |
| UI Settings/SystemButtons/PlainControls + Traceability | PASS: 39/39, 0 skipped |
| Core Traceability | PASS: 3/3, 0 skipped |
| Services Traceability | PASS: 3/3, 0 skipped |
| Broad affected App slice | 469 total: 328 pass, 141 fail, 0 skipped |
| Same existing App slice on staging, untouched production | 448 total: 306 pass, 142 fail, 0 skipped; every one of the fixed branch's 141 failures is also present here |

Every final test filter includes `&FullyQualifiedName!~Native`. The broad App failures comprise 52 `icu.dll`, 39 `user32.dll`, 25 glyph-typeface errors, 17 picture-installation errors and 8 related assertion/time-out failures. These are not counted as failed A11Y findings or as passing validation.

The comparison used a disposable detached worktree at `889c9ebd` with **unchanged staging production** and only the same test-only Linux font helper/builder copied in. Its strict analyzer-enabled App test-project build also passed with zero warnings/errors (1:26.32). The identical existing-test filter had 142 failures; all 141 failures on the fixed branch recur there by exact test name. The baseline-only extra failure was `WallpaperCardTests.An_indeterminate_progress_indicator_shows_only_while_installing_and_disables_the_buttons` (also recorded as a load-sensitive failure in prior work). The fixed branch adds 21 passing cases and introduces no newly failing existing test in this comparison. Full broad-slice acceptance remains blocked by those reproduced staging/Linux limits, not by an unfixed imported A11Y regression. The baseline TRX is `/tmp/a11y-fix-1-results/baseline-app-slices.trx`.

Commands (after sourcing `/workspace/.tools/marrow-env.sh`):

```sh
export DOTNET_PROCESSOR_COUNT=4
export MARROW_PORT_DATA_DIR=/tmp/a11y-fix-1-data
dotnet build Marrow.sln -m:1 -p:EnableWindowsTargeting=true -warnaserror
dotnet test tests/Marrow.App.Tests/Marrow.App.Tests.csproj --no-build -p:EnableWindowsTargeting=true --filter '(FullyQualifiedName~Marrow.App.Tests.Accessibility.|FullyQualifiedName~Traceability)&FullyQualifiedName!~Native'
dotnet test tests/Marrow.UI.Tests/Marrow.UI.Tests.csproj --no-build -p:EnableWindowsTargeting=true --filter '(FullyQualifiedName~Settings|FullyQualifiedName~SystemButtons|FullyQualifiedName~PlainControls|FullyQualifiedName~Traceability)&FullyQualifiedName!~Native'
dotnet test tests/Marrow.Core.Tests/Marrow.Core.Tests.csproj --no-build -p:EnableWindowsTargeting=true --filter 'FullyQualifiedName~Traceability&FullyQualifiedName!~Native'
dotnet test tests/Marrow.Services.Tests/Marrow.Services.Tests.csproj --no-build -p:EnableWindowsTargeting=true --filter 'FullyQualifiedName~Traceability&FullyQualifiedName!~Native'
dotnet test tests/Marrow.App.Tests/Marrow.App.Tests.csproj --no-build -p:EnableWindowsTargeting=true --filter '(FullyQualifiedName~Marrow.App.Tests.Accessibility.|FullyQualifiedName~SmallSwitchMotion|FullyQualifiedName~SettingsViewFinishGeneral|FullyQualifiedName~SettingsViewFinishAppearance|FullyQualifiedName~SettingsViewReduceMotion|FullyQualifiedName~Customization|FullyQualifiedName~CzControls|FullyQualifiedName~VoiceModelListView|FullyQualifiedName~ReviewVt14|FullyQualifiedName~VoiceTypingSettingsPage|FullyQualifiedName~UpdatesSettingsPage|FullyQualifiedName~UpdatesTransitionAudit|FullyQualifiedName~UpdatesSharedService|FullyQualifiedName~UpdatePill|FullyQualifiedName~GettingReady|FullyQualifiedName~WallpaperCard|FullyQualifiedName~AboutUpdateViews|FullyQualifiedName~Traceability|FullyQualifiedName~ContinuousCornerSites)&FullyQualifiedName!~Native'
```

Logs are under `/tmp/a11y-fix-1-*.log`; TRX evidence is under `/tmp/a11y-fix-1-results/`. No full solution-wide test or Windows accessibility pass is claimed. `node porting/tools/verify.mjs --gate C` still reports the baseline's 26 stub markers and its unimplemented semantic gate; the quarantine guard passes. Protected build/configuration/contract files are unchanged. The protected-path checker flags the intentional lead-authored decision/report/state documentation edits; these record the user's requested fix and the contract's resumable state, and no guard configuration is changed. This task is not a numbered task-queue packet, so no unrelated packet is marked DONE.

The existing 1,441,968 bytes of `porting/engineering_decisions.md` are preserved as an exact prefix (SHA-256 `7987e78c1a6f53a701ea729f89cf3e9268872e6be56bb12781c933050376fee5`), with only the requested `## ED-NEW-a11y-fix-1` section appended.
