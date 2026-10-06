# T7 SK-15 Windows review verification

Verdict: READY for these review corrections under the lead's alone-twice rule. This is not a whole-port parity claim or an all-green raw whole-suite claim. The original SK-15 incomplete features and the expressly unresolved size/provider-origin facts remain open.

Worktree: `E:/Droppy/wt/u/f-sk-15`, branch `codex/f-sk-15`, push target `origin/codex/f-sk-15`. Retained unit head `992f931de`; merged staging `e5b455fe9bf2c7f80186d697599f315ea4c8b08d` as `c855a2d33`. Both ledger sides were retained byte-exact; staging's complete ED content precedes the retained SK-15 section. Final SHA is recorded in `E:/Droppy/work/scratch/handoff/codex-T7-sk15-fix.md` after committing this report.

## Fixes and proof

- Explicit backend CanFetch capability; production fail-closed defaults false. Disabled Download, Retry, Download all, Continue and Retry N, reusing the existing one-line reason. Direct controller calls cannot queue an unbound install. Services acceptance test reproduced Available becoming Failed before the fix; App acceptance reproduced enabled controls. Existing fail-closed error-path assertions remain, exercised by a deliberately misreported bound fixture in addition to the new no-start proof.
- Optional still=false sheet parameter; SK-15 opts into opacity-only fades for Reduce Motion. Default scale behavior is unchanged. Both in-window and popup paths are frame-tested; closing content stays mounted/visible mid-fade and is removed afterward. A later suspicion that logical popup cleanup caused a cut was disproved by these fresh mounted-content assertions, so no speculative production change was made.
- Required space rounds upward independently of measured free-space formatting. The 12.46 GB regression was red before the fix; exact-fit and one-byte-short boundaries are pinned.
- All changing dialog facts/progress crossfade; changed Included ids and Needs lists retain outgoing content while fading. New regression tests were red before implementation; the Needs proof uses a mounted actual dependency list.
- Copied exact Sections JS and self-contained page. Complete running subtitle and partial footer now match the owner source. Ready in new chats is drawn for installed skill sets. See `codex-sk15-strings.md` for the complete literal audit, source hashes and explicit plan/ED extensions.
- Added undownloaded-plugin-saved-on and plugin-folder-default-off coverage. Windows mutations caught the three earlier survivor gaps plus Escape-starts, Hide-stops and Reset-no-op; each was restored byte-exact. An initial redundant Hide-flag mutant survived because OnSheetClosed independently sets the same flag; the semantic Hide mutation was caught.

## Counted Windows gates

Every counted build/run held an unchanged heavy.sh slot. Tests used lead one-names-long.sh or one-full.sh with Native excluded and fresh script-provided data roots. App, Core and Services builds completed with zero warnings/errors. No owner app, installer, packaging, native test, real transfer or provider CLI was run.

| Gate | Passed | Failed | Total | Notes |
|---|---:|---:|---:|---|
| Settings / LibraryPage / Skills / Traceability | 1680 | 2 | 1682 | Allocation Aura and wallpaper indeterminate; both pass alone twice |
| Strengthened review tests before full | 7 | 0 | 7 | Mounted Needs, disabled Retry N, both sheet paths, ready phrase |
| Core Skills / Traceability / Layering | 451 | 0 | 451 | Includes four new Core review tests |
| Services Pages / Host / plugin switch / NoVendorImage | 62 | 0 | 62 | Includes explicit production-backend capability checks |
| Full App, FullyQualifiedName!~Native | 8394 | 8 | 8402 | Zero skipped; 17m35s; all failures pass alone twice |
| Final LibraryPage / Traceability after closing-visual assertions | 49 | 0 | 49 | Fresh build; all review, motion, layout and trace tests |

The full run tested production code at `2ee79a0cb99172b4034ce35153b2c86c842ee68a`. Afterward only closing-visual assertions and documentation were strengthened; no production code changed. Fresh closing-visual tests passed 2/2. One preliminary follow-up check overlapped a finishing build and could not guarantee binary freshness: discarded, not counted. The build was awaited and all follow-up evidence was collected afresh.

## Failure isolation

All names below are unchanged tests. None failed alone twice, so none meets the lead's qualifying-failure rule. No assertion was relaxed to suppress these combined-run failures, and no base-checkout mutation was needed for a qualifying failure because none remained.

| Combined failure | Alone 1 | Alone 2 |
|---|---|---|
| CustomizationAllocationTests.Drawing_a_style_glyph_allocates_nothing (Aura, slice only) | 3/3 pass, including Aura | 3/3 pass, including Aura |
| AppModelTurnFinishedTests.The_chime_plays_unless_the_turn_was_stopped_and_a_chime_that_played_leaves_the_banner_silent (Completed + Failed cases) | 5/5 pass, including both cases | 5/5 pass, including both cases |
| AppModelTurnFinishedTests.With_notify_when_finished_off_there_is_no_banner_but_the_chime_still_plays | 1/1 pass | 1/1 pass |
| AppModelTurnFinishedTests.A_visible_thread_in_the_active_app_chimes_but_shows_no_banner_and_is_left_read | 1/1 pass | 1/1 pass |
| ComposerWorkingDotsEaseTests.A_later_pick_eases_the_dots_on_the_render_server_over_point_32_seconds_with_no_ui_frame | 1/1 pass | 1/1 pass |
| ComposerWorkingDotsEaseTests.The_render_server_is_asked_for_frames_only_while_the_blend_runs | 1/1 pass | 1/1 pass |
| WallpaperCardTests.Both_row_buttons_are_disabled_while_an_install_is_running | 1/1 pass | 1/1 pass |
| WallpaperCardTests.An_indeterminate_progress_indicator_shows_only_while_installing_and_disables_the_buttons (slice and full) | 1/1 pass after each combined run | 1/1 pass after each combined run |

## Open items

UNRESOLVED: SK-2 must supply measured download/unpacked sizes. approxSizeMb is an estimate, not exact installed disk usage; the dialog explicitly says Sizes are estimates. Unpacked size is not known yet. The provisional room calculation is not certified against a real installed tree.

UNRESOLVED: Your Claude account provides this requires a synced-account origin/equivalence fact absent from ProviderSkillGroup; a Claude provider name also covers own/system skills. Record this for SK-11/SK-16 rather than making a false account claim. This deferral is expressly permitted by T7 item 5.

Existing open work: real first-install/manual-file wiring, catalogue pins/descriptions/default metadata, provider reports, Minimize/pill, Add, Tools and live/native appearance proof. No global DONE/port-parity claim is made. Review decisions are appended under ED-355; original STATE and ED prefixes remain byte-exact.

Evidence: `E:/Droppy/work/scratch/codex-sk-15/` contains the red tests, clean builds, required combined/full logs, exact isolated reruns, mutation logs and original-byte backups. `app-full.log` is the raw full result; `full1-alone-*-*.log` records both reruns for every distinct failed method.
