# T44 final follow-up — 2026-10-08

NOT READY. Implementation commit `2067b2d46546ce110b9909fd0a0d9e46eb3ea01e` is pushed on `codex/t44-perf`; the final documentation HEAD is recorded in the external handoff. The source changes pass their affected checks and independent source review, but measured startup, steady frame pacing and near-zero allocation still fail or remain unproved. The latest lead ruling assigns the single full Core/Services/UI/App gate to T41 on the merged branch. No per-thread full sweep was active when that ruling arrived; no new full sweep was started afterward. Earlier aborted logs remain raw evidence with no completion credit. `mark.mjs` is skipped per the lead's explicit instruction because T44 has no queue row.

## Final source and regression checks

The follow-up loads the verified immutable palette pack once on a worker, then publishes it before model/content construction. A restored main shell and the approved Boot B stage can exist while it loads. Early close/session handling and delayed notification routing preserve startup ordering; termination prevents further startup. Background work never constructs UI controls. The existing compiled bootstrap and Skills startup assertions are unchanged. `FinishStartup` is a direct Program state machine; the original zero-argument private `MakeMainWindow` reflection seam delegates to the shared construction path.

Provider executable discovery captures actor-owned settings before filesystem work on the pool, rechecks enabled-account availability after yielding, and reuses the resolved executable when constructing its probe environment. Palette envelopes verify the exact raw UTF-8 data span before typed deserialization, preserving duplicate/escaped property names, whitespace-sensitive checksums, invalid input, unknown fields and future-schema write protection. Bounded serialization avoids retaining whole-document UTF-16 buffers. Diagnostic report serialization also streams outside measurement intervals. Completed Boot B compositor callbacks stop even if UI detachment is delayed.

Only the palette timer/model-fixture ownership portion of T45 `c1ce62b663944fbd2f0a88a6eb02c4cc7a7dc8b2` was ported. Timer posting is serialized against disposal before touching the actor. The harness owns and retires palettes from directly constructed AppModels as well as models made through its helper, before its dispatcher is retired. Engine and facade retain the bound SwiftClock; shipping binds System. No Settings, tooltip or window-ownership changes from T45 were copied. The directly constructed model lifetime regression failed first (0P/1F). Copied unnumbered Decision traits failed the unchanged traceability checker; metadata now uses existing ED-617, with provenance in ED-NEW-T44. No assertion, tolerance or checker was weakened.

| Final affected check | First | Second | Evidence labels |
|---|---:|---:|---|
| App timer/lifetime/startup/shell/bootstrap/traceability | 86P/0F | 86P/0F | `followup-app-final2`, `followup-app-final3` |
| Selected provider and palette envelope/store classes | 40P/0F | 40P/0F | `followup-services-final1`, `followup-services-final2` |
| UI AuroraBoot handler/geometry | 15P/0F | 15P/0F | `followup-boot-final1`, `followup-boot-final2` |

Earlier fail-first and follow-up logs remain under `E:\Droppy\work\scratch\codex-T44`, including raw-envelope checks, account removal/disable during discovery, completed boot request disposal, early-shell closure and startup ordering. The 45-case timer/lifetime group also passed twice before the final broader checks. Independent read-only review found no actionable correctness blocker in the actual changes; its scoped source verdict does not close native acceptance.

## ReadyToRun, startup trace and retained memory

`after-final-release` is a clean self-contained Release publish of implementation `2067b2d46`. Its runtime configuration has concurrent workstation GC and tiered PGO. `after-final-r2r.json` records SHA256 hashes and parses the actual managed native header, RuntimeFunctions and MethodDefEntryPoints, rather than trusting the project property: Marrow.App/Core/Services/UI, Avalonia.Base/Controls/Win32/Skia and SkiaSharp all contain executable per-assembly R2R code. Avalonia.dll and Avalonia.Metal.dll are facades without such code. The scratch composite experiment contains real composite code but was not adopted: it reduced early JIT starts while still stalling startup.

An earlier composite-to-per-assembly publish reused incomplete R2R cache components and crashed before app startup. That failed candidate and its logs are retained without measurement credit. Final publishing cleared only this worktree's checked R2R object directory, explicitly disabled composite mode and verified the output. No installer, packaging or Windows setting was changed.

Startup-suspended dotnet-trace runs with JIT/GC/sampling found 24,298 JIT starts in the earlier per-assembly 65-second diagnostic run, and 14,057 in the composite experiment; these are different diagnostic candidates, not final untraced counts. The final untraced pair below is the acceptance check for launch delay. R2R presence does not establish zero JIT or explain the GPU waits.

Gcdump root analysis of the earlier per-assembly candidate found about 125 MiB of large pooled buffers in a 173.10 MiB live managed heap. After raw UTF-8 envelope loading and streaming diagnostics, the comparable diagnostic candidate had 29 MiB pooled buffers and a 74.74 MiB live heap; the scratch composite sample had 4 MiB pooled buffers/48.92 MiB heap. These dumps warn that .NET 10 graph collection may be lossy; managed roots are not a complete accounting of Skia surfaces or resident native pages. The approximately 96 MiB reduction in pooled-buffer retention is supported by those diagnostic samples, but the composite's remaining reduction is not all attributable to the production JSON change. Evidence: `memory-comparison.json`, `*-heap-roots.json`, `.gcdump`, startup `.nettrace` and memory-map artifacts. The `memory-cold` quiet CPU sample was a background window and receives no foreground acceptance credit.

The controlled v9 before/after pair below remains the baseline comparison: CPU 23.9587% to 0.5729% of one core and allocation 3,627,455 to 65,459 B/s in its warm after interval; working sets 757.86/764.16 MiB did not prove a reduction. The final candidate is measured separately below. The owner's installed 1.42 GB observation is a different state, not a paired baseline.

## Final copied-profile native pair

Cold PID 91828 and relaunch PID 46792 used the same `after-final-release` binary and isolated `profile-final-release-pair`, copied from the owner's profile. No diagnostic-port startup suspension and no PC-control/UIA polling were used. Each measurement acquired unchanged heavy.sh and native.lock and released both immediately afterward, with only its owned app/capture PIDs stopped. Cold duration was 200 seconds for the quiet sample; warm duration was 70 seconds for startup and the 45-second capture. Minimum free RAM was 8,177 MiB cold and 8,145 MiB warm. The final Release's idle sample is foreground-guarded at ten points, nonminimized and uncloaked. No provider message was sent.

| Measurement | Final cold | Final relaunch |
|---|---:|---:|
| First boot compositor draw from process start | 1,102.628 ms | 555.158 ms |
| Initialization-ready marker | 10,692.578 ms | 4,881.550 ms |
| Designed hand-over marker | 10,703.923 ms | 4,888.503 ms |
| First subsequent UI-queue reply after hand-over | 5,434.781 ms later | 1,394.855 ms later |
| Stage Finish marker | 16,139.306 ms | 7,376.614 ms |
| Maximum UI-queue delay | 5,429.992 ms | 1,126.758 ms |
| Maximum boot custom-draw duration | 29.749 ms | 65.969 ms |
| Maximum gap between boot draw starts | 2,750.075 ms | 1,002.769 ms |
| Boot draw-start gaps over 33 ms | 38 | 16 |
| Orb / composer light frame requests in 60 s | 206 / 205 | 500 / 500 |
| Orb / light minimum steady request spacing, 14–20 s | 16.717 / 16.718 ms | 16.876 / 16.873 ms |
| Orb / light requests after 35 s | 0 / 0 | 0 / 0 |
| Quiet CPU, percent of one core | 0.989469% | Not sampled |
| Quiet allocation | 269,301 B/s | Not sampled |
| Quiet working set / private bytes | 718.285 / 718.121 MiB | Not sampled |

The first custom draw is a managed render milestone, not measured first animated screen presentation. It exceeds the 300 ms target in both runs. UI-queue replies are a responsiveness proxy; no physical input-to-response latency is claimed. Cold main-window WM_PAINT scopes include 2,744.538 and 2,396.852 ms waits; warm includes 1,051.277 and 258.755 ms. These are native message scopes around the original handler, not provider awaits. Earlier sampling directly correlated MediaContext.SyncCommit UI waits with SurfaceRenderTarget.Blit/SKCanvas.Flush compositor work. The final cold/warm initialization times differ by 5.811 seconds, so equivalent relaunch timing is not proved.

OD-0113 Boot B and its 1.8-second hand-over floor were retained exactly. Both measured readiness/hand-over markers occur well beyond that floor, so shortening it would not remove the observed initialization wait (inference from the locked sequence and markers; no modified-floor experiment was run). It could save time on a future early-ready launch if the owner elects to change the design. Neither current first-frame nor immediate-interaction target passes.

The foreground final idle CPU narrowly meets <1% in this interval, but allocation is now about 269 KB/s, not the older 65 KB/s result. Neither near-zero allocation nor reliable long-idle CPU/JIT acceptance is claimed. Ambient requests stop around 22.34 seconds cold and 21.54 seconds warm, with none after 35 seconds. Their request spacing supports the 60 Hz decorative cap; it is not presentation FPS or proof of whole-window GPU damage containment. Unseen-stop and ease behavior retain their tests and original native evidence below.

Full artifacts: `final-release-source.json`, `final-release-{cold,warm}-start.json`, `*-performance.json`, `*-phase-summary.json`, cold `*-timing-summary.json`/`*-quiet-performance.json`, `*-process-timeline.json`, `*-memory-map.json`, and the two `*-launch.mp4` captures. The Win32 probe and provider-phase recorder are opt-in copied-profile diagnostics and record no probe arguments, credentials or output.

The captures requested 60 fps DDA with passthrough timestamps and microsecond encoder timebase. Cold has 2,598 frames/45.004 s, mean capture spacing 17.323 ms, maximum 45.879 ms and 210 capture gaps over 33 ms. Warm has 2,585 frames/44.998 s, mean 17.408 ms, maximum 41.328 ms and 172 gaps over 33 ms. No CFR duplication was used. Their actual ~57–58 fps capture rate cannot prove the required native <=17 ms/no >33 ms GPU frame gate. Boot custom-draw gaps independently demonstrate stalls. The prior popover/slider/Settings captures below remain evidence, with their limitations; no new passing model-picker/pill/Settings interaction proof or GPU presentation trace is claimed.

![Final cold Boot B capture crop](E:/Droppy/work/scratch/codex-T44/final-cold-boot-crop.png)

![Final relaunch Boot B capture crop](E:/Droppy/work/scratch/codex-T44/final-warm-boot-crop.png)

## Raw aborted sweeps and final gate disposition

The later uncapped two-heap App attempts were 5,705P/1F/5,706 aborted, then 5,837P/2F/5,839 aborted. Their monitored RAM minima were 7.00835 and 6.23971 GiB; no reserve abort was issued. Windows events identify palette timer posts to a retired dispatcher. The last raw failures were the unchanged Skills startup IL and saved-frame reflection assertions; the first unchanged isolated group failed both (0P/2F), after which source fixes passed the two-case group and the final broader 86-case group twice. The nominal `startup-regression-alone2` was built after the source fix, so it is green-after-fix, not a second unchanged red rerun. The earlier palette glide failure passed both isolated reruns. The retired-dispatcher ownership regression failed before the T45 port and passes afterward. No full sweep was rerun to establish aggregate closure of the abort.

Original mixed-thread Windows event export is retained; `app-startup-final-full-own-windows-events.json` filters the two actual T44 events by path/time. No T46 event is attributed to T44. `VSTEST_DIAG` did not produce a diagnostic file; no such evidence is claimed. An exploratory alternate engine/facade-clock test failed before implementation but was withdrawn when the lead required the minimal T45 port; its exact source is retained in scratch and it is not credited as an implemented or green regression.

The older completed App 6,407P/177F/6,584 OOM/font sweep below retains its exact OD-0128 disposition: 153 fixed identities AND messages match the unchanged list SHA256 `592c4a9aed52101c8c9750b1ed6e16b9168e1423b3be01401c86c0c10ba8a5fa`; 24 outside-list cases passed whole methods twice. Raw totals stay separate from isolated outcomes. The older Core catalogue GAP and Services partial-Range GAP each failed alone twice and remain separate release gaps. None of these historical aggregates becomes green because T41 now owns the full merged gate.

Exact implementation `2067b2d46` `verify.mjs --no-run --json`: 21P/8F (A 12P, B 8P, C 1P/2F, D 3F, E 1F, F 2F), with clean inputs. Failures remain existing stubs and missing requirements/release-evidence inputs, packaged evidence and final acceptance. The documentation HEAD is checked again after committing this report. The original 2,335,993-byte ledger prefix remains byte-identical. The source and final measured failure are recorded under ED-NEW-T44 and STATE's incomplete section.

Remaining: first boot animated presentation <=300 ms, no launch UI stall >50 ms, steady native <=17 ms/no >33 ms frames, immediate hand-over interactivity, equal cold/relaunch timing, region-only GPU redraw, reliable near-zero allocation/long-idle JIT, complete native memory attribution and Settings/popover/slider/pill timing. Glass GPU warm-up registration/order/variant coverage is incomplete; it was not invented as a proven cause or speculative fix. The single merged suite gate remains T41's work.

NOT READY

---

## Historical checkpoint through b80dd09b9 — retained measurements and failure classification

The material below belongs to the earlier checkpoint. Its “final” and “committed” references describe that checkpoint, not the newer implementation above. Earlier controlled pairs, failures and provenance remain preserved.

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