# T26 — Multi-account design-fidelity review

**Verdict: FINDINGS.** 65 findings: **P0 0 · P1 14 · P2 40 · P3 11**. Each finding has a new intentionally failing test. No production code, existing tests, design sources or ledgers were changed. This is a review, not an acceptance claim.

P1 means a missing agreed interaction, inaccessible operation or broken accessibility preference; P2 means a substantial layout, state, copy or motion mismatch; P3 means a smaller visible precision mismatch. All three severities fail the owner's exact-fidelity bar.

## Reference and scope

- Review branch: `codex/ma-ui-review`, based on wave 2 `d61419abf81d365fbfd08ed51c9db62642178461`. Wave 1 `5ea92913550051edb38ce6ddfb1ce44756baf49b` is already an ancestor; the requested `merge --no-ff` returned “Already up to date.” No merge conflict or separate-branch fallback was necessary.
- Locked HTML: `porting/first-release/design-src/multi-account/multi-account.html`, SHA-256 `909bee162d1d794cb704c50bf2712735768b70eedd3c33d570095e8ff59eddb8`.
- Checklist: `porting/first-release/multi-account-CHECKLIST.md`, SHA-256 `2eddbd5085042555d4b4415d34ba0d7c34c963c2cbf46517d76ea4c22487afee`.
- OD-0114..OD-0126 were read from `git show origin/claude/verify-terminal-20261001:porting/owner_decisions.md`, reference commit `285b6b7a99b39e0e517f34c8d34a1860c945f83f`. Owner decisions take precedence.
- Reviewed the built .NET 10 / Avalonia 12.1.2 Core models and App control trees, input events, themes, animation sampling and Skia painter output. Tests ran on Linux using the Windows target with `EnableWindowsTargeting=true`. Native Windows font rasterization, real UI Automation clients and live GPU presentation were not verified here. The findings below do not depend on those unverified checks.
- HTML references below follow the **final CSS cascade and DOM construction code**, including overrides at 1347, 1361–1362, 1368, 1427–1447, 1459 and 1468. In particular, the earlier fixed 70px popover row and monospace percentage rules are superseded. Shared app typography uses the port's app font; MAUI-011 pins percentage text to that same font, rather than treating the HTML's fallback font list as a new app font decision.

## Top ten

1. **MAUI-001 / P1:** no link action in the account popover; the owner picked tap-to-link A.
2. **MAUI-002 / P1:** no accessible “Ask others for this message” composer action; B was picked.
3. **MAUI-053 / P1:** a history-loaded Codex readiness event never presents the agreed “In sync” composer signal.
4. **MAUI-049 / P1:** the Handshake renderer drops the incoming shared capsule entirely.
5. **MAUI-018 / P1:** Settings account dragging translates under Reduce Motion.
6. **MAUI-025 / P1:** the Handshake slider ignores its specified keyboard controls.
7. **MAUI-042 / P1:** the account use switch is announced as “Rename”.
8. **MAUI-019 / P1:** unmodified Up/Down do not navigate popover rows.
9. **MAUI-065 / P1:** card action background/radius values never reach a painted button face.
10. **MAUI-056 / P1:** the exact Core sound recipe has no consumer in the built App.

## Findings and failing tests

Every `MAUI-nnn` identifies one test case; the test files cite the authority beside the case. Paths and line numbers below identify the reviewed **production** code at the base commit. They are not proposed fixes.

Test key:

- **A:** `tests/Marrow.App.Tests/Views/MultiAccount/MaUiReviewTests.cs` — the `finding` theory argument or the corresponding `MAUI_nnn_…` fact.
- **M:** `tests/Marrow.Core.Tests/Port/MultiAccount/Handshake/MaUiReviewMotionTests.cs` — `Queued_events_preserve_the_locked_phase_intervals(finding)`.
- **W:** `tests/Marrow.Core.Tests/Port/MultiAccount/Settings/MaUiReviewWordsTests.cs` — `MAUI_054_Local_discovery_success_keeps_the_locked_server_name`.
- **S:** `tests/Marrow.App.Tests/Views/MultiAccount/MaUiReviewSoundTests.cs` — `MAUI_056_Shipped_App_consumes_the_locked_enabled_sound_recipe`.


| ID | Severity | Port file:line | Design value / authority | Port value / observed difference | Test |
| --- | --- | --- | --- | --- | --- |
| MAUI-001 | P1 | `src/Marrow.App/Port/MultiAccount/Popover/AccountPopoverRowControl.cs:66` | Per-account Link/Unlink button; “Link Claude Max 2 to check this thread’s work”; HTML 2687, 2715, checklist §4 Link, OD-0115/0120 | No link button, monogram target, header link mode or link animation; row contains only pin/config/options. Cross-service confirmation cannot be reached from this screen. | A |
| MAUI-002 | P1 | `src/Marrow.App/Views/Composer/ComposerView.cs:1684` | B action “Ask others for this message”; HTML 4804–4811, 4855–4871, checklist §5, OD-0115/0120 | Composer control tree has no Ask others action. Core `AskOthersModel` has the copy/consent logic, but there is no App view consuming it, chooser, extra-answer display or Combine flow. | A |
| MAUI-003 | P2 | `src/Marrow.App/Port/MultiAccount/Popover/AccountPopoverRowControl.cs:106` | Row menu “Keep this thread on it” / “Back to order”; HTML 2734, checklist §4 Row tap | Menu contains only move commands and “Handshake at…”. | A |
| MAUI-004 | P2 | `src/Marrow.App/Port/MultiAccount/Popover/AccountPopoverRowControl.cs:64` | Per-account point in ⋯ menu, quiet note **only when different**; OD-0119 | An unconditional separate “Automatically” text remains in the settings line even with no differing override. The additional conditional `HandshakeNote` does not remove it. | A |
| MAUI-005 | P3 | `src/Marrow.App/Port/MultiAccount/Popover/AccountPopover.cs:28` | Popover content padding 6px; HTML 215 | 8px margin on all sides. | A |
| MAUI-006 | P2 | `src/Marrow.App/Port/MultiAccount/Popover/AccountPopoverRowControl.cs:48` | Auto row height, minimum 68px, 7px vertical padding; final HTML 1347 | Fixed 70px row and fixed 70px pin button at `src/Marrow.App/Port/MultiAccount/Popover/AccountPopoverRowControl.cs:76`, without the final minimum-height layout. | A |
| MAUI-007 | P2 | `src/Marrow.App/Port/MultiAccount/Popover/AccountPopoverRowControl.cs:66` | Account monogram, e.g. M1; HTML 2686, 2716, checklist §4 Rows | No monogram control; the grid goes straight from number to account content. | A |
| MAUI-008 | P3 | `src/Marrow.App/Port/MultiAccount/Popover/AccountPopoverRowControl.cs:67` | Grip 16×24px; HTML 147 | 22×22px grip. | A |
| MAUI-009 | P3 | `src/Marrow.App/Port/MultiAccount/Popover/AccountPopoverRowControl.cs:74` | Number in an 18px-high rounded capsule, radius 9; HTML 1447 | Plain 12px text directly in the full-height row grid, without a number capsule. | A |
| MAUI-010 | P2 | `src/Marrow.App/Port/MultiAccount/Popover/AccountPopoverRowControl.cs:51` | Active dot: 7px green `#0F7B3F` in light theme, 3px soft-green halo; HTML 241 and token 3 | Primary-text “●” glyph at 6px, no halo. | A |
| MAUI-011 | P3 | `src/Marrow.App/Port/MultiAccount/Popover/AccountPopoverRowControl.cs:135` | Percentage uses `var(--app)`; final HTML 1361 | Explicit Cascadia Mono/Consolas instead of the account text's app font. | A |
| MAUI-012 | P2 | `src/Marrow.App/Port/MultiAccount/Popover/AccountPopoverRowControl.cs:116` | Off content/number/grip opacity .42; final HTML 1362 | Content and number .45; grip is left bright. | A |
| MAUI-013 | P2 | `src/Marrow.App/Port/MultiAccount/Popover/AccountPopoverRowControl.cs:76` | Pinned row background `--pacSoft`, light `rgba(0,103,192,.10)`; HTML 230, token 3 | Pin button background remains null, including after pinning. | A |
| MAUI-014 | P2 | `src/Marrow.App/Port/MultiAccount/Popover/AccountPopoverRowControl.cs:63` | Config button has a down chevron; HTML 2692, 1303 | Button hosts `AccountConfigLine` without a chevron. | A |
| MAUI-015 | P3 | `src/Marrow.App/Port/MultiAccount/Popover/AccountPopover.cs:60` | Footer has top divider and padding 8px/8px/6px; HTML 247 | Plain text directly in `CrossfadeSlot`; no divider/padded footer surface. | A |
| MAUI-016 | P2 | `src/Marrow.App/Port/MultiAccount/Popover/AccountPopoverRowControl.cs:70` | Displaced neighbours glide into the proposed order over .34s `--ez`; HTML 2020–2025 | Only the grabbed row translates; neighbouring row transform stays null until the store is reordered on release. | A |
| MAUI-017 | P2 | `src/Marrow.App/Port/MultiAccount/Popover/AccountPopoverRowControl.cs:69` | Begin dragging after 5px movement; HTML 2008 | Capture starts on press and a 4px movement already translates the row. | A |
| MAUI-018 | P1 | `src/Marrow.App/Port/MultiAccount/Settings/AccountRowControl.cs:58` | Reduce Motion replaces movement with a crossfade; HTML 467, checklist §8 Reduce Motion | Settings row still translates by 40px with the window's Reduce Motion enabled. | A |
| MAUI-019 | P1 | `src/Marrow.App/Port/MultiAccount/Popover/AccountPopoverRowControl.cs:85` | Up/Down without modifiers focus previous/next row; HTML 2049–2050 | Handler exits unless Alt is held; Down leaves focus on the original row. | A |
| MAUI-020 | P1 | `src/Marrow.App/Port/MultiAccount/Popover/AccountPopoverRowControl.cs:85` | Shift+F10 / ContextMenu opens the focused row menu; HTML 2052 | No handler; Shift+F10 leaves the menu closed. | A |
| MAUI-021 | P2 | `src/Marrow.App/Port/MultiAccount/Popover/AccountDefaultsPanel.cs:29` | Defaults popover width 330px; HTML 834 | 346px. | A |
| MAUI-022 | P2 | `src/Marrow.App/Port/MultiAccount/Popover/AccountHandshakePanel.cs:23` | Handshake point popover width 248px; final HTML 1368 | 330px. | A |
| MAUI-023 | P2 | `src/Marrow.App/Port/MultiAccount/Popover/AccountHandshakePanel.cs:27` | “Use the default” hidden unless there is an own override; HTML 2642 | Reset remains visible with no override. | A |
| MAUI-024 | P2 | `src/Marrow.App/Port/MultiAccount/Popover/AccountHandshakePanel.cs:25` | Slider aria “Handshake point for this account”; HTML 2635 | “Start the Handshake at this share of the limit” (the general control's label). | A |
| MAUI-025 | P1 | `src/Marrow.App/Port/MultiAccount/Settings/HandshakeSliderControl.cs:27` | Right/Up, Left/Down, Home/End traverse Smart→80…99; HTML 2613–2617 | No Handshake keyboard handler; End leaves the account override null instead of setting 99%. | A |
| MAUI-026 | P2 | `src/Marrow.App/Port/MultiAccount/Settings/HandshakeSliderControl.cs:27` | Compact Handshake control height 18px, track 12px, knob 17px; HTML 1071–1082 | Full EffortSlider: height/knob 48px, track 34px (`src/Marrow.App/Views/Composer/EffortSlider.cs:858`, 863, 941); forced 220px width at `src/Marrow.App/Port/MultiAccount/Settings/HandshakeSliderControl.cs:28`. | A |
| MAUI-027 | P3 | `src/Marrow.App/Port/MultiAccount/Usage/MultiAccountUsageView.cs:74` | Usage header 12px; HTML 436 | 13px. | A |
| MAUI-028 | P3 | `src/Marrow.App/Port/MultiAccount/Usage/MultiAccountUsageView.cs:78` | Current account name 15px; HTML 447 | 13px. | A |
| MAUI-029 | P2 | `src/Marrow.App/Port/MultiAccount/Usage/MultiAccountUsageView.cs:82` | No-5-hour state retains “5-hour window” then the dim “No 5-hour limit”; HTML 4343, checklist §6 | Only “No 5-hour limit” is added; the window label disappears. | A |
| MAUI-030 | P2 | `src/Marrow.App/Port/MultiAccount/Usage/MultiAccountUsageView.cs:40` | Incoming All accounts page starts at 100% of its width; HTML 461, 463 | Fixed 32px offset regardless of page width (32 vs 572px in the fixture). | A |
| MAUI-031 | P2 | `src/Marrow.App/Port/MultiAccount/Usage/MultiAccountUsageView.cs:40` | Usage navigation .45s `cubic-bezier(.32,.72,0,1)`; HTML 460 and `--ez` token 3 | .35s `cubic-bezier(.22,1,.36,1)`; at 175ms incoming X ≈1.236px vs design ≈49.868px for a 572px page. | A |
| MAUI-032 | P2 | `src/Marrow.App/Port/MultiAccount/Usage/MultiAccountUsageView.cs:90` | Reduce Motion Usage crossfade .3s CSS `ease` (`.25,.1,.25,1`); HTML 469 | .25s EaseOut; at 150ms opacity ≈.785139 vs .802403. | A |
| MAUI-033 | P2 | `src/Marrow.Core/Port/MultiAccount/Cards/LimitCardModel.cs:66` | First-time “Keep me going” is primary; HTML 4000, checklist §7 First-time | All three buttons have `Primary=false`. OD-0116 removes automatic switching before consent; it does not remove this explicit-choice button's primary styling. | A |
| MAUI-034 | P2 | `src/Marrow.App/Port/MultiAccount/Cards/LimitCardView.cs:37` | Mid-reply explanation emphasizes “Finish now” and “Stop”; HTML 4225, checklist §7 Mid-reply | Plain TextBlock, no emphasized runs. | A |
| MAUI-035 | P3 | `src/Marrow.App/Port/MultiAccount/Cards/LimitCardView.cs:63` | Remember separator 1px; HTML 380 | .5px. | A |
| MAUI-036 | P2 | `src/Marrow.App/Port/MultiAccount/Cards/LimitCardView.cs:46` | Primary button ink uses `--pacInk`, dark `#06121C`; HTML 378, token 4 | Hard-coded white in both themes. | A |
| MAUI-037 | P3 | `src/Marrow.App/Port/MultiAccount/Cards/LimitCardView.cs:32` | Limit gauge uses `--hot`, light `#B25E00`; HTML 371, token 3 | Secondary-text black at alpha .596 (`#98000000` in the fixture). | A |
| MAUI-038 | P2 | `src/Marrow.App/Port/MultiAccount/Cards/LimitCardNote.cs:14` | After-choice note is a rounded tile/capsule, radius 999px, 4px/11px padding; HTML 346, checklist §7 Limit note | Bare StackPanel, no background or rounded surface. | A |
| MAUI-039 | P3 | `src/Marrow.App/Port/MultiAccount/Settings/AccountRowControl.cs:22` | Settings account row height 46px; HTML 142 | Inner row grid height 56px. | A |
| MAUI-040 | P2 | `src/Marrow.App/Port/MultiAccount/Settings/AccountRowControl.cs:128` | Off Settings row dims words/usage/number to .45; switch remains bright; final HTML 1459 | Entire row opacity .55, including its SmallSwitch and menu. | A |
| MAUI-041 | P2 | `src/Marrow.Core/Port/MultiAccount/Settings/AccountsSettingsModel.cs:91` | Quiet Handshake note only if the point differs from the general default; OD-0119 | Any non-null own value adds “Handshake automatically”, even when equal to the default. | A |
| MAUI-042 | P1 | `src/Marrow.App/Port/MultiAccount/Settings/AccountRowControl.cs:75` | Use switch aria “Use Claude Max 1”; HTML 3846, checklist §1 multi-account rows | Automation name is “Rename”. | A |
| MAUI-043 | P3 | `src/Marrow.App/Port/MultiAccount/Settings/AccountRowControl.cs:176` | At 24/24 the name counter uses `--hot`, light `#B25E00`; HTML 3867, checklist §1 Rename | `ChromeTokens.Danger`, `#FF3B30`. | A |
| MAUI-044 | P2 | `src/Marrow.App/Port/MultiAccount/Settings/PillStepRow.cs:43` | Step row shows account monogram M1/M2; HTML 4457, 4472, checklist §2 Step rows | Provider icon, which does not identify which account of that provider. | A |
| MAUI-045 | P2 | `src/Marrow.App/Port/MultiAccount/Settings/PillStepRow.cs:86` | Yellow filled Fast bolt **before** model, aria “Fast mode”; HTML 1791, 4457, 4473, checklist §9; `src/Marrow.App/Views/Composer/EffortSlider.cs:180`–183 | Yellow filled bolt is after the config text and has no Fast mode automation name. | A |
| MAUI-046 | P2 | `src/Marrow.App/Port/MultiAccount/Settings/PillStepRow.cs:178` | Step config opens floating defaults dialog with “Same as the account” SmallSwitch; HTML 4462–4465, 2382, checklist §2/§9, OD-0115 | Expands `StepSettingsPanel` inline (`settingsFold.Open=true`); the Same-as-account switch is absent. | A |
| MAUI-047 | P2 | `src/Marrow.App/Port/MultiAccount/Settings/PillStepRow.cs:67` | Removing below two steps gives 300ms feedback; HTML 4466, checklist §2 Step rows | `RemoveStep` returns false but the click discards the result; no wiggle or crossfade feedback. | A |
| MAUI-048 | P2 | `src/Marrow.App/Port/MultiAccount/Settings/PillStepRow.cs:148` | Off step name/mono/number opacity .45; final HTML 1468 | Entire row .6, including settings controls/tags. | A |
| MAUI-049 | P1 | `src/Marrow.App/Port/MultiAccount/Handshake/HandshakePainter.cs:28` | Draw the incoming **same shared capsule**, including when filaments/glows are absent; HTML 3030–3033, 3078–3090, 3157, OD-0115 | `frame.Ghost` is ignored; isolated visible ghost produces zero non-transparent pixels. `HandshakeCanvas` also has no shared-capsule child/composition layer to supply it. | A |
| MAUI-050 | P2 | `src/Marrow.App/Port/MultiAccount/Popover/AccountUsageBar.cs:24` | Bar fill .6s `cubic-bezier(.32,.72,0,1)`; HTML 125, token 3 | .6s `cubic-bezier(.22,1,.36,1)`; at 300ms a 50% target is .480691 vs design .477405. | A |
| MAUI-051 | P2 | `src/Marrow.Core/Port/MultiAccount/Handshake/HandshakeMotionModel.cs:30` | Standby lasts at least 1000ms before Linking (`T.sb=1000`, `T.lk=2000`); HTML 3039, checklist §8 Timeline | Minimum 600ms; queued Linking is already shown at 999ms. | M |
| MAUI-052 | P2 | `src/Marrow.Core/Port/MultiAccount/Handshake/HandshakeMotionModel.cs:30` | In sync through Safe pause lasts 1400ms before Switch (`T.sy=3000`, `T.sw=4400`); HTML 3039, checklist §8 Timeline | Minimum 800ms; queued Switch is already shown at 1399ms. | M |
| MAUI-053 | P1 | `src/Marrow.App/Port/MultiAccount/Capsule/ComposerCapsuleSlot.cs:47`; `src/Marrow.App/Views/Composer/ComposerView.cs:1687` | Ready history presents the same In sync capsule signal/chip for Codex/Pi as Claude; HTML 2481, 324, checklist §3 During Handshake, OD-0122 | Only a running bool/standby ID can reach the capsule. No App consumer of `HandshakeMotionModel`/`ShowTick`; an `InSyncReady` event on the offered card port leaves the displayed capsule without “In sync”. | A |
| MAUI-054 | P2 | `src/Marrow.Core/Port/MultiAccount/Settings/MultiAccountWords.cs:46`; `src/Marrow.Core/Port/MultiAccount/Settings/AccountsSettingsModel.cs:231` | Local success “Found Ollama on this PC · 1 model added”; HTML 3774, checklist §1 Local / §10 Add | “Found a server on this PC · 1 model added”; server kind is erased from the agreed success copy. | W |
| MAUI-055 | P2 | `src/Marrow.App/Port/MultiAccount/Settings/PillStepRow.cs:167`; `src/Marrow.App/Port/MultiAccount/Settings/HandshakeSliderControl.cs:56` | Locked step tooltip “Same for every step: set it with the slider at the top”; HTML 2622 | Only “Same for every step”; the instruction locating the active slider is absent. | A |
| MAUI-056 | P1 | `src/Marrow.Core/Port/MultiAccount/Handshake/HandshakeMotionModel.cs:65`; `src/Marrow.App/Port/MultiAccount/Capsule/ComposerCapsuleSlot.cs:47` | Handshake soft sound ships, exact link/tick sine recipe; HTML 3053–3066, OD-0121 | Core queues the correct tones, but zero built App methods reference `TakeSounds`; no audio consumer, synth/playback adapter or enabled sound presentation path exists. The new static wiring test first verifies the exact recipe, then fails on the absent App consumer. | S |
| MAUI-057 | P1 | `src/Marrow.App/Port/MultiAccount/Popover/AccountPopoverRowControl.cs:76` | Keyboard focus shows inset 2px accent ring `#0067C0`, radius 9; HTML 229 | Focusable plain pin button uses the content-only template, without a focus ring; focusing by Tab displays no accent border. | A |
| MAUI-058 | P2 | `src/Marrow.App/Port/MultiAccount/Popover/AccountPopoverRowControl.cs:76` | Hover background `--hov`, light `rgba(0,0,0,.05)`; HTML 228, token 3 | Background remains null while the row is hovered; no hover treatment. | A |
| MAUI-059 | P2 | `src/Marrow.App/Port/MultiAccount/Popover/AccountPopoverRowControl.cs:52`; `src/Marrow.App/Port/MultiAccount/Popover/AccountPopover.cs:31` | Text tokens: light primary `#1C1C1E`, secondary `rgba(28,28,30,.66)`, faint `.42`; dark primary `#ECECEC`, secondary/faint matching RGB at .66/.42; HTML 3–4, 218, 234, 239 | Generic Chrome primary/secondary brushes use translucent black/white rather than these RGB/alpha values, and the faint order hint uses the secondary brush. Light meter label is `#98000000` instead of `#A81C1C1E`. | A |
| MAUI-060 | P1 | `src/Marrow.App/Port/MultiAccount/Settings/PillStepRow.cs:61` | Pill drag movement replaced by crossfade with Reduce Motion; HTML 467, 2024, checklist §8 | Step row translates by 40px despite Reduce Motion. This is a second independent handler from the Settings account-list bug in 018. | A |
| MAUI-061 | P1 | `src/Marrow.App/Port/MultiAccount/Settings/AccountRowControl.cs:57` | Reorder accepts primary mouse button only; HTML 1993 | Secondary-button press captures the grip and translates/reorders the row; no button check. PillStepRow:60 repeats the same pattern. | A |
| MAUI-062 | P2 | `src/Marrow.App/Port/MultiAccount/Popover/AccountPopoverRowControl.cs:70` | Carried row scales to 1.02; HTML 2018 | TranslateTransform only, scale stays 1.00 during a real drag. | A |
| MAUI-063 | P2 | `src/Marrow.App/Port/MultiAccount/Popover/AccountPopoverRowControl.cs:70` | Edge rubber band clamps excess translation at .35 of row pitch; HTML 2016–2017 | No clamp/damping: dragging the first row upward 200px renders a -200px offset, beyond the measured pitch's 35% cap. | A |
| MAUI-064 | P2 | `src/Marrow.App/Port/MultiAccount/Popover/AccountPopoverRowControl.cs:71` | Drop settles with `SP.settle` from previous rect to the new order; HTML 1983, 2029–2045 | Immediately reorders and clears the transform; at the first 16ms frame the original row already has no settling transform. | A |
| MAUI-065 | P1 | `src/Marrow.App/Port/MultiAccount/Cards/LimitCardView.cs:42`; `src/Marrow.UI/Port/PlainControls.cs:27` | Card actions paint their capsule face: primary `--pac` (light `#0067C0`), radius 14, neutral fill/rim; HTML 375–378 | Background and CornerRadius are assigned to a plain button whose template is just an unpainted ContentPresenter. No colored face is drawn; the primary white label loses its blue backdrop. | A |

## Owner-decision reconciliation

| Decision | Applied in this review |
| --- | --- |
| OD-0114 | First-release scope; incomplete App interactions are findings rather than deferred scope. |
| OD-0115 | Only Handshake A, composer A, link A, opinions B, usage B and card layout A are required. All **existing** multi-account switches route through the port SmallSwitch (`SettingsSwitch` / `CzSwitch`); no alternate generic switch was found. MAUI-046 identifies an agreed switch that is missing. |
| OD-0116 | Off/unset defaults and explicit first-time consent are correct in Core. The HTML's `hs:true` and “On by default” are superseded. Primary button styling remains an explicit user action, MAUI-033. |
| OD-0117 | Pills remains in the sidebar, disabled/dimmed below two with Models navigation; existing tests cover live enabling and disabled actions. The builder-authored disabled copy is allowed, not compared to a nonexistent page string. |
| OD-0118 | Skills import is outside these multi-account screens; no inferred multi-account requirement. |
| OD-0119 | Menu plus note only when different, off defaults and all twelve styles are authoritative. MAUI-004/041 fail the note rule. |
| OD-0120 | Core link/opinion consent stores require confirmation, Don't ask again starts unchecked. App entry points are absent (MAUI-001/002), so the visible confirmation flow has no acceptance evidence. |
| OD-0121 | Core tones match 523.25Hz/1.1s, 783.99Hz/1.3s/+0.08s and 1318.5Hz/.18s, with the locked gains/envelope; sound defaults off. The App has no `TakeSounds` consumer: MAUI-056. The test audits built IL for the queue's consumer and pins all three recipes; it does not claim acoustic verification. The voice-rim changes are outside scope. |
| OD-0122 | Primed and injected readiness both set `ShowTick` in Core. The missing visible composer readiness signal is MAUI-053. |
| OD-0123 | Voice-only rim and speck requirements are outside scope. The app font Inter is retained. |
| OD-0124 | Permission for one installed-CLI history check does not establish design fidelity; no real CLI/account/model turn was used in this review. |
| OD-0125 | Voice-only speck oracle, outside scope; no screenshot references or tolerances were regenerated. |
| OD-0126 | The approved curve wins over generic smoothness limits. New Usage/bar motion tests use the exact HTML curve, without a flat per-frame cap or extra tolerance. No Customization detail-out fade was changed. |

## Checklist traceability — every item

“Reviewed” below means source/control/test comparison of that item, not pixel-level Windows acceptance. Existing test classes are named to make the passing behavioral evidence findable. A failing surface can have correct underlying copy or decisions. Demonstration controls, alternate unpicked variants and sample account/answer data are not required product screens.

| Checklist item | Result / evidence |
| --- | --- |
| §1 Accounts heading and foot | Reviewed; `MultiAccountWords` exact heading/foot, `ModelsAccountsViewTests`. |
| §1 Accounts: one-account block/sub/mini week bar | Reviewed; `ServiceBlocksModelTests`, `ModelsAccountsViewTests`, `ServiceBlockPiecesTests`. |
| §1 Accounts: 2+ list, name/plan/usage/switch/options/off state | Findings 018, 039–042, 061; list/rename/actions otherwise covered by `AccountsListModelTests` and `ModelsAccountsViewTests`. |
| §1 Accounts: rename limit, counter, aria, Enter/Escape | Finding 043; maxlength/aria/commit/cancel reviewed in `AccountRowControl` and existing rename tests. |
| §1 Accounts: ⋯ menu and disabled end moves | Reviewed; `AccountsSettingsModel.Menu`, `AccountsListModelTests`; Handshake note finding 041. |
| §1 Accounts: Add another / Add a local server | Reviewed; `FakeAccountActions` recording tests and `ServiceBlockPiecesTests`. |
| §1 When an account runs out: gating, unset choice, options, foot | Reviewed; `RunOutSettingsTests`, `AccountsSettingsModel`, `MultiAccountWords`; OD-0116 overrides on-default language. |
| §1 Dependent mid-reply / earlier / all-services-out rows | Reviewed; exact `MultiAccountWords`, indented fold rows, `RunOutSettingsTests` and `ModelsAccountsViewTests`. |
| §1 What the next account gets / Show an example | Reviewed; option copy and `HandedOverExampleTests`; `HandedSheetView` supplies the example. |
| §1 Handshake group, switch, general point, per-account note and foot | Findings 004, 025–026, 041; default/points/plan thresholds otherwise `HandshakeSettingsTests`. OD-0119 removes the always-visible per-row button. |
| §1 OpenRouter keys, credits, Keys and order | Reviewed; `ServiceBlocksModelTests` / `ServiceBlockPiecesTests`. Credit totals are live data, not locked sample amounts. |
| §1 Local sub, per-account copy, add address, note, progress/success | Finding 054; placeholder/looking/local-no-limit reviewed in model/view tests. The checklist quotes the unused `note` variable at HTML 3763; the paragraph actually appended at 3764 matches `MultiAccountWords.LocalNote`. That unused string was not asserted as visible copy. |
| §1 One account / Several accounts demo toggle | Demonstration-only. Actual account connection reflows live; `ProvidersRevealTests`, `PillsPageViewTests`. |
| §2 Pills sidebar, beside Models | Reviewed, OD-0117 wins; `PillsSidebarTests`. |
| §2 Editor title/sub/name length/aria/step-service count | Reviewed; `PillEditorModelTests`, `PillEditorViewTests`. |
| §2 Step rows, monogram/config/slider/tags/remove/add/minimum | Findings 044–048, 055, 060, shared slider findings 025–026; add/new-service/skipped/local tags and minimum-two model rule otherwise reviewed. |
| §2 Handshake-point card: common/per-step/description/local | Reviewed; `PillEditorModelTests`, `PillEditorViewTests`; the control dimensions/keyboard/lock tooltip fail 025–026/055. |
| §2 Earlier-account policy including Like in Settings | Reviewed; `PillEditorModelTests`, exact policy strings and persistence. |
| §2 Loop note | Reviewed; exact `MultiAccountWords.LoopLine`. |
| §2 Disabled/empty state | Reviewed against OD-0117; `PillsSettingsModelTests`, `PillsPageViewTests`; builder copy is explicitly allowed. |
| §2 Pills page foot | Reviewed; exact `MultiAccountWords.PillsFoot`. |
| §3 Capsule caption / 2+ display gate | Reviewed; `ComposerCapsuleModelTests`, `ComposerCapsuleSlotTests`; one-account gate follows caption. |
| §3 Four composer variants and D hover/switch | A is the owner pick; B/C/D are alternatives on the design page, not additional shipping controls. |
| §3 Capsule 22px/34px geometry, usage-light anchors/ease, composer body | Reviewed in `CapsuleLayoutTests`, `UsageLightTests`, `AccountCapsuleTests`; four color anchors and shared family are implemented. No native raster approval inferred. |
| §3 Twelve Customization styles, default, labels/descriptions, hover/name | Reviewed; `CapsuleStylesTests`, `PillCapsuleSectionTests`, `AccountCapsuleTests`. HTML 2293 actually renders `view.name` for Name below; the checklist's “step n of m” phrase describes the tooltip used by other styles, not that visible label. Twelve-style OD wins over NOTES. |
| §3 During Handshake tick / In sync chip | Finding 053; Core readiness alone does not draw the composer chip. |
| §4 Header/subtitle/footer and local variants | Core strings reviewed in `AccountPopoverModelTests`; footer treatment finding 015. |
| §4 Rows: grip/number/mono/dot/name/chips/reset/bars/no-5h/local/out/hot | Findings 006–012, 016–017, 050, 057–059, 062–064; usage/reset/state copy reviewed in `AccountPopoverModelTests`, `NoFiveHourCellTests`. Percentage font follows final CSS, superseding checklist “mono percents”. |
| §4 Handshake column/dialog/aria/local | OD-0119 supersedes unconditional column; findings 004, 022–026. Local Never runs out model text reviewed. |
| §4 Config line/dialog/composer-override note | Findings 014, 021; exact config copy/shared yellow bolt via `AccountConfigLineTests`; dialog state persistence via `AccountPopoverDialogTests`. |
| §4 Link action/aria/header mode/fly motion | Finding 001; no visible link feature to verify 560ms fly or its reduced-motion alternative. |
| §4 Tap pin, Back to order menu, refused out-row feedback | Findings 003, 013; Core pin/back behavior and refused 300ms feedback otherwise `AccountPopoverTests`. |
| §4 Thread remembers / Use Settings / follows Settings | Reviewed; `AccountPopoverModelTests`, `AccountPopoverTests`. |
| §4 Create a pill footer action | Reviewed; enabled/gated action and `CreatePillRequested` in popover tests; no additional demo pill account data required. |
| §5 B button, chooser/@all/chips/message note | Finding 002; Core `AskOthersModelTests` pass copy/selection/scope/consent, but no App flow exists. A's typed-@ trigger is unpicked. |
| §5 Side-by-side replies / Combine / combined byline | Covered by missing App flow 002; Core opinions/link ports have round/combine records. Sample combined-answer prose is demonstration content, not a required hard-coded assistant answer. |
| §5 One-message-only after state | Core `AskOthersModelTests` and `OpinionAndLinkPortTests` cover scope; visible after-state cannot be reached because of 002. |
| §6 B current/labels/reset/no-5h/local/next/all/back | Findings 027–032; model strings/counts/next/local/back otherwise `UsagePanelModelTests`, `MultiAccountUsageTests`. |
| §6 A service-first/Show all alternative | Unpicked; OD-0115 usage=B. No requirement to ship both layouts. |
| §6 Out/hot/OpenRouter credits | Reviewed in `UsagePanelModelTests`, `NoFiveHourTests`, `AccountUsageBar`; shared fill-motion finding 050. |
| §7 First-time title/three choices/layout A | Findings 033, 065; title and all three words match, explicit consent remains necessary. |
| §7 Limit title/switch/wait/continued/waiting/handed-over | Finding 038; exact Core title/actions and sheet/wait/switch behavior reviewed in `LimitCardModelTests`, `LimitCardNotesTests`. |
| §7 Other-service title/explanation/Wait primary/Use | Reviewed in `ReturnAndServiceCardTests`, `LimitCardModelTests`; shared primary-ink/gauge/button-face findings 036–037, 065. |
| §7 Mid-reply title/explanation/actions | Finding 034; other copy/action state covered by `MidReplyCardTests`. |
| §7 Remember: three segments/default/thread/all writes | Finding 035; default/scope/keyboard/one answer covered by `FirstTimeCardTests`, `CardPortTests`. |
| §7 Unset first-time mirrors and description | Reviewed in `RunOutSettingsTests`, exact `RunNone` / `RunNoneDescription`; no automatic choice until consent. |
| §8 Hero picks A / B and C alternatives | A geometry implemented; incoming shared-capsule finding 049. B/C are unpicked. |
| §8 Phases/status/timeline/optional sound | Findings 051–053, 056; geometry/status/recipe/readiness Core tests pass their existing checks. Sound recipe matches OD-0121, but App has no sound consumer, 056. |
| §8 Streaming same bubble / safe pause between steps | Core cards/motion are event-driven; no modal typing block in views. Missing live presentation 053 prevents end-to-end visual sign-off. |
| §8 Reduce Motion crossfade-and-tick | Findings 018, 032, 053, 060; reduced Handshake geometry is stationary and capsule loops are stopped in existing tests. |
| §8 GPU warm-up | `HandshakeGpuProgram.EnsureRegistered` is called in `Program.cs:119`; `HandshakePainterTests` cover retained resources/registration. Real GPU performance is unverified in this environment. |
| §8 Continued note / CLI hard-stop fallback | Shared note finding 038; continued/waiting Core copy reviewed. Live CLI fallback is outside this fake-port UI run. |
| §8 Start Handshake slider / Smart and 95/98% defaults | Core Smart/80…99 stops and plan defaults reviewed in `HandshakeSettingsTests`; point-control findings 025–026. |
| §9 Fast mode: yellow filled bolt/position/aria/account + step overrides | Shared account config uses the port bolt and Fast mode aria. Pill-step order/aria finding 045; floating step defaults finding 046. No replacement Fast text is required or introduced. |
| §10 Local provider/models/server names | `ServiceNames` and ThreadAccount server metadata reviewed; sample model names are user/model data. Local discovery loses server-kind copy, 054. |
| §10 Never runs out / all no-limit strings | Reviewed in `NoFiveHourTests`, `AccountPopoverModelTests`, `UsagePanelModelTests`, `PillEditorModelTests`; no artificial 5-hour limit added. |
| §10 Add arbitrary local server / address/note/no download | Model/view recording paths reviewed; success-copy finding 054; no model downloads were invoked. |
| §10 Local last pill step and local in link/opinions | Core Local steps/mono/no-limit reviewed; visible link/opinions missing, 001/002. |
| §11 One-account block/no list/name hidden | Reviewed; `ServiceBlocksModelTests`, `ModelsAccountsViewTests`. |
| §11 When-out / Handshake sections gated by multi or pill | Reviewed; `AccountsSettingsModel.ShowAdvanced`, `ModelsAccountsViewTests`. |
| §11 Single-account composer capsule hidden | Reviewed; `ComposerCapsuleModelTests`; follows explicit section caption (1550). |
| §11 Pills sidebar gate | Superseded by OD-0117; always visible/disabled below two, tests above. |
| §11 Live one/several reflow | Actual connection/disconnection live reflow reviewed; demo toggle is not a product setting. |
| Conflicts 1–4 | Applied ODs: Pills always visible; Handshake off; menu + differing-note only; twelve styles. No test pins a superseded page default. |
| Unsure: disabled Pills wording | Owner delegated exact copy; intent checked, no invented reference text. |
| Unsure: sound control | OD-0121 requires shipped recipe; demo On/Off strip is not itself a required settings row. Core off default retained; missing App sound hookup is 056. |
| Unsure: What was handed over | Reviewed implemented sheet/live record; no asserted requirement to reuse the sample sheet's exact example contents for every conversation. |
| Unsure: one-account capsule | Explicit caption/gate at 1550 used; general “same family” paragraph at 2112 does not force visibility with only one account. |

## Validation and reproduction

SDK: .NET `10.0.401`. All new tests compile with the repository's warning/analyzer settings. The initial unmodified App multi-account view run was **43 passed, 0 failed, 0 skipped**. Final relevant runs:

- App: **160 existing passed, 62 new failed, 0 skipped** (222 total).
- Core: **218 existing passed, 3 new failed, 0 skipped** (221 total).
- Combined: **378 existing passed; all 65 added cases fail intentionally**. Failure messages were checked for design assertions rather than fixture/setup errors. No passing existing test was rewritten to change its expectation.

Run from the repository root with a throwaway data directory (required by R-TEST-9):

```bash
MARROW_PORT_DATA_DIR=/tmp/ma-ui-review-data dotnet test tests/Marrow.App.Tests/Marrow.App.Tests.csproj \
  -p:EnableWindowsTargeting=true \
  --filter 'FullyQualifiedName~MaUiReview|FullyQualifiedName~Views.MultiAccount|FullyQualifiedName~Views.Composer.MultiAccount|FullyQualifiedName~Views.Settings.MultiAccount' \
  --logger 'trx;LogFileName=ma-ui-app.trx' --results-directory /tmp/ma-ui-review-results
dotnet test tests/Marrow.Core.Tests/Marrow.Core.Tests.csproj \
  -p:EnableWindowsTargeting=true --filter 'FullyQualifiedName~Port.MultiAccount' \
  --logger 'trx;LogFileName=ma-ui-core.trx' --results-directory /tmp/ma-ui-review-results
```

Both commands should exit nonzero because this branch deliberately pins unfixed findings. Filter `Feature=T26` to run only the new review cases. The hover-state case dispatches PointerEntered and selects the control's hover pseudo-state directly; drag and keyboard cases use the production input paths. Animation expectations solve the cited CSS Bézier directly and compare at fixed frame times; no screenshot tolerance or existing oracle was relaxed.

The repository entrypoint `node porting/tools/verify.mjs --gate D` was also attempted. It is **not acceptable** here: its unmodified Windows solution build omits `EnableWindowsTargeting` and reports NETSDK1100 on Linux; the bound requirement report is absent; its remaining Gate D evidence is NOT_IMPLEMENTED. The explicit target-aware commands above provide the scoped build/test evidence. This review does not mark any ledger item DONE or claim Gate D acceptance.
