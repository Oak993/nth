# T44 owner performance — 2026-10-08

NOT READY. The ambient/idle repair is measurable, but the mandatory launch and native frame-time gates remain open. T44 owns `E:\Droppy\wt\cx\t44-perf`, branch `codex/t44-perf`, based on `9e2cd6d535e1293b17f892da50f82e88fca5c083`. Final HEAD and push verification are in the external handoff `E:\Droppy\work\scratch\handoff\codex-T44.md`. This report supersedes the earlier partial handoff, including its incorrect attribution of the physical Escape to the owner: the lead pressed Escape, then stopped screen control; the owner later stopped this chat's PC-control connection for quiet measurement.

## Implemented

- Concurrent workstation GC for shipping Marrow; ReadyToRun and PGO retained. The test host's project still declares its separate server-GC policy.
- Immutable palette catalogue facts, market rows and maker lineups are cached. Unchanged polls return the previous snapshot; unrelated changes retain shade tables; decoded equivalent shades are interned. PaletteMath avoids per-mix temporary arrays/LINQ while preserving arithmetic/reference results. The 486-model poll regression fell from 27,633,456 allocated bytes to below its unchanged 8 MB budget; exact final bytes were not exported. The 1,000-mix regression fell from 1,488,000 bytes to below 64 KB.
- Early cloaked Show and boot-stage Close are posted at Background priority after the render callback returns; preparation is latched once and a closed stage cannot reopen. This removes that synchronous callback destruction path, without proving the launch stall is gone.
- Optional missing thread/MCP files no longer repeatedly throw on reads. Existing-file and read-race errors remain handled. Remaining startup skills/custom-MCP probes still throw; transport IO failures are separate.
- HttpClient's own deadline cancellation becomes TimeoutException when the caller is not cancelled. Caller cancellation remains cancellation. This fixes the authentic copied-profile v3 crash; the two subsequent unexplained exits were later confirmed as accidental owner cleanup.
- Ctrl+Break helper creation and its bounded wait run on one context-free worker; PID reservation stays live through both phases. Existing helper job/signal script and one/three-second force-kill deadlines remain. The shell fixture now observes the child's signal receipt before advancing fake time, retaining all prior output, one-tick deadline and 500 ms assertions. Real-child blocked-launcher tests verify both deadlines independently of helper completion. The first marker fixture exposed PowerShell's null-to-empty-string conversion; its guard was corrected and that failed run is retained.
- Explicit marked `--performance-profile` Release runs isolate copied data/kernel/credential identity and suppress production OS registration, Hydra maintenance and CLI auto-update. Normal startup retains those behaviors. A 60-second optional managed scope/UI-queue recorder and a separate precise 30-second quiet allocation/CPU probe provide reproducible diagnostics.

## Owner ambient ruling and implementation

The approved rest motion remains. Decorative frame requests are capped at 60 Hz on every display. Hidden, minimized, DWM-cloaked, fully covered or nonforeground windows stop ambient requests. After 20 seconds without actual pointer/key input, motion eases to zero over 0.6 seconds; the next input eases in over 0.6 seconds from the preserved phase. Foreground restoration alone does not count as input. Real interaction transitions retain display rate. Popup input and availability resolve through the owning window. Orb/light/title/wallpaper/effort-track ambient sources use this policy; small local invalidation areas remain, with no explicit whole-window-glass invalidation added. GPU damage-region containment is not yet measured.

The final regression restricts the interaction-rate exemption to an actual ambient compositor source. Manual/legacy clocks keep their previous 60 Hz grid; all seven existing cadence cases passed after the correction, without changing an assertion. ED-NEW-T44 records the numeric ruling, implementation and limits. The original 2,335,993-byte engineering ledger prefix remains identical.

The faster palette callbacks exposed a composer-fixture context race: it bound frames after runtime construction and window Show. The unchanged baseline passed MAUI_002/053 twice while T44 failed both twice. A temporary FirstChance capture established PaletteSurfaceBinding -> MainActor.Run -> ModelEffortButton -> EffortView.Animate -> UnboundFrameSource. The fixture now binds its frame source before runtime construction; its normal lifetime and assertions remain. Temporary capture code was moved out of the source tree.

## Controlled before/after measurement

`before-release` is a scratch archive of the base with only the 11 listed diagnostic/isolation files copied in (`baseline-instrumentation-files.txt`); server GC, old palette/math, old boot and always-running decorations remain. `after-release` is the owned self-contained Release build. Cold runs use fresh copies of the owner's profile. The warm run uses the same final binary/profile again. API-key variables were cleared; no provider message was sent. Native runs held `native.lock` and a heavy slot. PC control/UIA polling was absent during the final quiet samples.

At 160–190 seconds after launch, the recorder has stopped and no Runtime meter is attached. Direct in-process `GC.GetTotalAllocatedBytes` and process CPU are sampled for 30 seconds, with report writing after the interval. Read-only HWND guards at both sample endpoints verify the owned process, foreground, nonminimized and uncloaked state. They are endpoint guards, not continuous observation.

| Measurement | Before v9 cold | After v9 cold | After v9 warm |
|---|---:|---:|---:|
| CPU, percent of one core | 23.9587% | 0.9375% | 0.5729% |
| Allocation, bytes/s | 3,627,455 | 65,330 | 65,459 |
| Working set, MiB | 757.86 | 911.12 | 764.16 |
| GC suspend-to-resume pairs in launch trace | 362 | 23 | 25 |
| Total paired launch GC interval, ms | 385.850 | 228.587 | 197.307 |
| Max paired launch GC interval, ms | 50.251 | 46.630 | 27.097 |
| Quiet trace paired GC intervals | 3 | 0 | 0 |
| Quiet trace exceptions | 3 transport IOExceptions | 0 | 0 |
| Maximum launch UI-queue delay, ms | 5,395.074 | 6,452.569 | 7,128.228 |
| Native cloaked Show managed scope, ms | 349.481 | 416.896 | 260.150 |

Evidence root: `E:\Droppy\work\scratch\codex-T44`. Direct files: `before-v9-cold-direct-quiet.json`, `after-v9-cold-direct-quiet.json`, `after-v9-warm-direct-quiet.json`; matching `*-steady-start-guard.json`/`*-steady-end-guard.json`; `performance-comparison.json`; `*-correlated-gc.json` and original `.nettrace` files. GC intervals are matched by process and initiating thread, excluding profiler SuspendOther; raw suspend/stop/restart timestamps and unmatched counts are retained. They are not GPU frame durations. The v9 CLR keyword mask 0x8001 covers GC/exceptions, not JIT; zero observed JIT events in those traces does not prove zero JIT. The separate JIT-at-idle requirement remains unclosed.

Two subsequent Release traces explicitly enable JIT (CLR mask 0x8011) under native.lock, using the existing copied after profile without desktop input. Endpoint foreground/nonminimized/uncloaked guards pass. The 190-second trace records 97 JIT starts: 94 in its first second around EventSource enablement and the diagnostic quiet-report serialization, then three framework task/HTTP-pool/cache starts; no GC interval. The follow-up starts at 230 seconds, after report writing, and records 101 starts plus one 5.553 ms paired GC interval, one TaskCanceledException and two CancellationError events. It includes ProviderKindExtensions, ProcessLauncher.QuoteArgument, SerialExecutor, SwiftJson, PaletteSurfaceBinding and AppModel paths. Sample stacks include MultiAccountRouting.RefreshAsync and PaletteSurfaceBinding.Update; no sampled UI Automation frame was found in that follow-up. This is not zero JIT and is not discarded as diagnostic serialization. Unchanged PaletteEngine.Publish already suppresses equal digests; Bootstrap awaits provider refresh before starting account reports/catalogues/palette checks. The trace has not established whether the 240-second burst is periodic work or late bootstrap completion, so no speculative source/refresh-semantic change was made. Evidence: `after-jit*-quiet.nettrace`, summaries and endpoint guards, plus `after-jit2-quiet.speedscope.json`/`after-jit2-sample-stacks.json`.

The v9 CPU/allocation table is valid for its guarded 160–190 second intervals; it does not prove bootstrap was complete or that every later idle interval meets those numbers. The new later burst leaves long-idle/late-bootstrap CPU and allocation acceptance unclosed as well as JIT. No claim of globally idle CPU below 1% or zero ongoing JIT is made.


The controlled warm comparison shows about 42 times less CPU and 55 times less allocation. Allocation is still measurable (~65 KB/s), so ~zero allocation is not asserted. The controlled working sets are approximately equal; a large memory reduction is not proved. The owner's installed 1.42 GB working set is a different state and is not substituted for the paired baseline.

The baseline recorder logged 9,906 orb draws and 9,906 light draws in 60 seconds (about 165 Hz). It did not have new frame-request instrumentation. Final cold: 371 requests per source, last at about 22.09 seconds; warm: 552 orb/549 light requests, last about 21.84 seconds; no ambient request after 35 seconds. Warm steady 14–20 second minimum request spacing is 16.6694 ms orb / 16.6671 ms light. Draw counts and requests are different measures; they must not be equated with presents.

Earlier 70–100 second baseline v9 sampling lost foreground ownership at its end (ChatGPT foreground). Its raw 44.57% CPU is retained but excluded from the paired comparison. Earlier installed and copied-profile samples overlapping accessibility inspection were contaminated and are not clean idle evidence. Resetting the automation connection alone did not end the helper; the owner stopped PC control before the final samples.

## Unclosed native gates and evidence

Warm v8 sampling correlates UI-thread `WindowImpl.AppWndProc -> MediaContext.SyncCommit -> Task.Wait` intervals of 3,449 and 3,085 ms with compositor `SurfaceRenderTarget.Blit -> SKCanvas.Flush -> native` intervals of 3,419 and 3,099 ms. Both waits precede stage Finish/Close; the async provider-refresh aggregate is not their UI-thread cause. HWND alpha/style/region changes are temporally nearby but are only a hypothesis, not an established cause. No unproved renderer/backend switch was shipped.

The settled trace has roughly 500–543 ms compositor wake samples and no ambient callbacks after the idle deadline. The composer autofocuses its text box; Avalonia 12.1.2 starts a 500 ms caret timer on focus and invalidates on blink. Caret blinking plausibly explains residual work, but no caret callback stack or same-window focus-away paired sample proves causality. The caret and approved autofocus were not suppressed to manufacture a pass.

Authentic captures: `after-v9-cold-launch.mp4`, `after-v9-warm-launch.mp4`, `before-v9-cold-launch.mp4`, `after-v7-interactions.mp4`, `after-v7-settings-only.mp4`. Extracted evidence: `popover-25.png` (effort popover Max), `popover-35.png` (drag/transition), `popover-45.png` (Low), `settings-evidence.png` (Appearance). The interaction capture contains 3,561 actual frames in 60 seconds (~59.35 capture fps), 39 capture timestamp gaps above 33 ms and maximum 33.334 ms (`video-content-analysis.json`/`video-cadence.json`). FFmpeg's duplicate-frame resampling was corrected with passthrough. Capture cadence and ROI changes do not prove GPU presents or the required <=17 ms / no >33 ms app-frame gate.

The model-chip subtitle click was inert; this capture does not prove model-picker opening. Settings Appearance/General were exercised; no captured Settings close or precise <=50 ms interaction timing is claimed. Real microphone/model download was not exercised, so voice-pill timing acceptance is unclosed. WPR recording failed 0xc5585011 and the UIA logman attempt was denied; no PC setting or elevation workaround was applied.

![Effort popover at Max](E:/Droppy/work/scratch/codex-T44/popover-25.png)

![Effort drag transition](E:/Droppy/work/scratch/codex-T44/popover-35.png)

![Settings Appearance capture](E:/Droppy/work/scratch/codex-T44/settings-evidence.png)

## Verification

All builds/tests used unchanged heavy.sh and official Windows Git Bash wrappers, fresh MARROW_PORT_DATA_DIR roots and cleared API-key variables. Native tests were excluded from full sweeps. Test-only heap settings and reserve aborts below never change shipping GC or assertions.

| Suite/group | Raw result | Twice-alone or final affected disposition |
|---|---:|---|
| Core full, final5 | 4,962P / 1F / 4,963 | Strict embedded skills catalogue GAP fails alone twice; signal/ProcessLauncher group 60P twice after fixture repair |
| Services full | 5,832P / 3F / 5,835 | Voice partial-download Range GAP fails alone twice; Antigravity interrupt and Cursor overlay each pass twice alone; affected 93P twice |
| UI full | 4,724P / 0F | Final affected 1,461P twice after manual-source/availability correction; cost-allocation failure from an earlier affected sweep passes twice alone |
| App full, exclusive final | 6,407P / 177F / 6,584 | Raw completed, but 4 GiB heap exhaustion/font-loading fallout; cannot count as clean full-suite acceptance |
| App affected, two heaps | 610P / 0F; 609P / 1F | Strike-capacity method passes twice alone; subsequent composer rig/palette/ambient group 105P twice |
| App boot | 14P twice | Deferred boot lifecycle assertions retained |
| App manual cadence | 7P twice | Existing cadence tests unchanged |

Remaining Core failure: `GapCatalogueAcceptanceTests.GAP_The_embedded_catalogue_has_every_required_release_pin_and_passes_the_strict_gate` — missing catalogue licence/files/name pins. Remaining Services failure: `GapModelDownloadAcceptanceTests.GAP_A_voice_model_partial_is_resumed_with_Range_and_refetches_no_byte` — Assert.Equal mismatch. Both are existing separately tracked release gaps, reproduced alone twice and outside T44 scope. The aggregate Core/Services gates remain red.

The exclusive App sweep temporarily held all three original heavy leases and completed in 24m56s, with minimum sampled free RAM 7.4086 GiB. Its 153 V-T133 speck identities AND messages match the unchanged OD-0128 fixed matrix list exactly (list SHA256 `592c4a9aed52101c8c9750b1ed6e16b9168e1423b3be01401c86c0c10ba8a5fa`; 153 matrix cases plus six fixed aliases). It also had 21 V-T132 OOM/font failures and three other failures: StrikePlanGeometryVector allocation, HandshakePainter allocation/pixels, and EffortSliderJewelFrames capacity. Fresh method reruns are recorded below; membership in the fixed identity list alone is insufficient if the message changes. `final-known-speck-comparison.json` preserves individual messages.

Fresh-process reruns on the committed implementation (same source as the exclusive raw sweep) all pass twice, with original assertions unchanged:

| Whole method | First / second |
|---|---:|
| `Nothing_of_the_pills_material_shows_beside_the_orb` | 224P / 224P |
| `Port_frames_render_for_the_side_by_side` | 2P / 2P |
| `The_eight_plan_slots_reuse_all_geometry_buffers_without_allocating_on_new_strikes` | 1P / 1P |
| `Live_snapshot_draw_is_pixel_equal_to_the_existing_geometry_oracle_and_allocates_zero` | 4P / 4P |

The 21 extra V-T132 OOM/font cases therefore do not reproduce in either 224-case fresh method run. The three other failing methods also pass both fresh runs. This classifies the observed failures; it does not establish complete full-App discovery/coverage or turn the raw OOM sweep green. Exact cases, labels and counts are in `additional-failure-rerun-map.json` and `fresh-rerun-summary.json`.


Earlier App raw evidence remains explicit: initial 6,342P/64F/6,406 aborted; overlapping owned runs stopped at 1,899 and 531 passes; one uncapped affected run stopped at 465P/1F; a 1 GiB build failed Roslyn OOM before tests; a later full run stopped at 1,941 passes; the 1 GiB affected run aborted at 166 passes. Editing a running script caused a later wrapper command-not-found harness error, acknowledged and retained; no test result was credited. The 2 GiB full attempt finished 6,356P/117F/6,473 with OOM/font fallout. The ordinary uncapped full run was reserve-aborted at 5,754P/2F/5,756, sampled free RAM 5.2276 GiB. The two-heap uncapped full run was reserve-aborted at 6,009P/3F/6,012, sampled free RAM 4.2766 GiB. A two-second reserve monitor reacts to shared-PC drops; it does not guarantee the instantaneous free-memory value never falls below its threshold. Later exclusive capped execution preserved the reserve but still exhausted the test heap. Historical 10,803-case baseline records belong to another release checkpoint and are not credited as this branch's completed discovery/coverage.

Earlier affected App 603P/7F and 602P/8F included seven cadence failures, fixed with unchanged assertions, and a Card thread case that passed its 12-case method twice alone. MAUI_002/053 initially failed twice alone while the unchanged base archive passed twice; early fixture frame binding made each pass twice, followed by 105P twice. Original HandshakePainter and Strike-capacity full-sweep failures also passed fresh methods twice (4 and 2 cases). `final-result-classification.json` and rerun-map JSONs retain exact cases, source logs and raw counts. A passed isolated rerun never converts an earlier failed/aborted aggregate to green.


`node porting/tools/verify.mjs --no-run --json`: A 12P, B 8P, C 1P/2F, D 3F, E 1F, F 2F (21 pass / 8 fail). Existing stub/requirements/release-evidence gaps prevent final acceptance. `mark.mjs T44 DONE` rejects "no task T44 in the queue"; no unrelated queue row was changed. Release publish succeeds; this is not installer/packaging acceptance.

## Isolation incident and scope

Earlier diagnostics incorrectly registered production protocol/AUMID scratch values. After exact ownership checks, both were restored to the installed executable/icon (`diagnostic-registration-incident.json`), then guarded by fail-first registration and maintenance tests. Earlier copied real project paths/CLI maintenance were an isolation hazard; no actual mutation was established, and no claim that those earlier runs could not have affected them is made. Later marked runs skip those callbacks and preserve installed registration.

No provider message, installer, packaging, signing, Windows setting change, reference-tree write or other worktree edit was performed. Only exact owned app/helper/test PIDs were cleaned up. T45/T46 surfaces remain their threads' scope. Independent read-only review found the final cadence/fixture issues described above, which were corrected; numerical/native acceptance is still incomplete.

NOT READY