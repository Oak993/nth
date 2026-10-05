# STATE

Continuation record. Reread `porting/PORT_CONTRACT.md` before acting on this file.
This file is a navigation aid; the contract and the source are the authority.

## Current checkpoint: 2026-10-04 03:17 AST, orchestrator Claude (written by the ledger-text agent from `E:\Droppy\work\NEXT_SESSION_CLAUDE.md`, top block and log up to 2026-10-04 02:59, and from git on this date)

This section supersedes every checkpoint below it. The checkpoints below are history: the baseline is B3, not B2, and
the working branches are the ones named here.

**Baseline.** B3 (owner decision OD-0024, amendment AM-0003): Droppy Code 1.7.0 build 21, commit `52bc7926`, checkout
`E:\Droppy\reference\droppy-code-v1.7.0` (read only); record `porting/baseline.json`. Owner rulings in force:
OD-0001 to OD-0098 (`porting/owner_decisions.md`, last entry OD-0098 with its 2026-10-04 amendments). Engineering
decisions run to ED-261; the next free number is ED-262, and `ED-263` to `-10` (commit `ece699396` on
`claude/ledger-text-20261004`) are renumbered by the lead at merge.

**Branches (read from git, 2026-10-04 03:17).**
- Staging: `claude/verify-terminal-20261001`, worktree `E:\Droppy\wt\cl\terminal-verify`, head `24c4e5cfd`
  ("records IMPLEMENTED: About and update views (19) ..."). Its local copy of the pushed ref is `746158d28`; the head is
  ahead of it. It has NOT been fast-forwarded to `impl/b2`: `origin/impl/b2` is `9efdc27fa`, an ancestor of staging with
  485 commits between them (the local `impl/b2` in `E:\Droppy\wt\integration` is older, `6ae70226e`). Only a gated
  fast-forward moves `impl/b2` (quiet-PC gate with the native window tests alone, flake rule: a failure counts only if it
  fails alone twice); nothing is signed, released or published without the owner's word.
- Port workspace branch `port/windows-exact-parity` (`E:\Droppy\port`), instruction files at `9f1413954` (2026-10-03
  prompt audit: B3 everywhere, every OD in force, this file as the current gate).

**Completion** (`python E:\Droppy\work\claude-verify\completion.py <checkout>`, reproduced on `claude/ledger-text-20261004`
at 24c4e5cfd plus the ledger commits): denominator 5007 records (N/A and RETIRED excluded), IMPLEMENTED
4558 = 91.03 %, IMPLEMENTED and named by at least one `Trait("Req", id)` test 4556 = 90.99 %. The two without a Req test:
PR-COMPOSER-131 (the R8 guard test forbids crediting it until proven) and PR-WIN-025 (`WindowChromeTests.cs:22` says no
Avalonia property can prove it). `node porting/tools/progress.mjs`: source files read 441/441, requirements VERIFIED
0/5007 (no verification run yet), test methods 12277, open quarantine 0. Owner-added features (composer lights, accent
picker, Jewel knob, Strike, Customization page, voice typing, skills) count on top of this and are not in the 5007.

**Units** (worktrees `E:\Droppy\wt\u\<name>`, branches `unit/<name>`; counts are commits ahead of staging, read from git;
a recent commit time is not proof the agent is still running).
- Launched from 24c4e5cfd at 02:59 (log line): blitz-appdelegate (2 ahead, last commit 03:14), blitz-terminalpanel (2
  ahead, 03:12), blitz-windowmanager (1 ahead, 03:14). Each ends with an adversarial review, fixes, a scoped check and a
  merge into staging, one at a time.
- Also committing in the last ten minutes and not yet merged: blitz-composerview (14 ahead, 143 behind), blitz-corners
  (8, 142), blitz-effortslider (15, 143), blitz-settingsview (13, 143), blitz-tail (18, 143), blitz-timelinerows-ab (19,
  142), parity-chatview (37 ahead, 17 behind; the log says its review fix-first list is being closed).
- Merged into staging today (log): MCP settings (ED-257), SidebarView (ED-258), TimelineRows c+d (ED-260),
  integ-defects (ED-259), About and update views (ED-261), network-leaks.
- Not started or waiting: the strike unit waits for `unit/fast-strike` round 3 (still `6d173a080`, not an ancestor of
  staging; jewel-knob is merged at `a9484a49f`); Settings > Customization and voice typing wait for the build questions
  below; the paused units are listed in NEXT_SESSION_CLAUDE.md section 7 item 6.

**Decisions recorded this checkpoint (this branch).** The turn fold animates under OD-0076 (ED-263); Reduce
Motion stills only what OD-0076 added beyond the Mac (-2); PR-CHAT-107 follows the Mac exactly (-3); both About Show
buttons are the small accent glass pill (-4); internal choices for voice, Customization, the strike unit, zoom chords,
the update toast (silent) and the Terminal credit (-5 to -10). Records amended: PR-UPDATE-061 (the sweep checks the
phase once, AboutUpdateViews.swift:801) and PR-COMPOSER-129 (deviation, OD-0090). Open build questions for the owner and
the engineering ones: `E:\Droppy\work\design\open-questions-2026-10-04.md` (43 rows: 24 owner, 19 engineering).

**Next actions.**
1. Let blitz-appdelegate, -terminalpanel and -windowmanager finish; merge each into staging when its scoped check says
   merge; set the closed records IMPLEMENTED with `set_status.py` and `merge_spec.mjs`; rerun `completion.py`.
2. Merge this branch's ledger commits into staging (renumber ED-263 to -10 from ED-262; amend PR-UPDATE-025 for
   the silent update toast, the record text of PR-CHAT-104's `windows_path`, and add PR-CHAT-107's defect to
   `porting/upstream_defects.md`).
3. Build what the decisions order: the turn fold's transition (ED-263) and the About Show buttons (-4), then
   the zoom chords by character (-8) and the silent update toast (-9), each test-first.
4. Put the 24 owner questions to the owner (Q-V-11 differs from the spec's recommendation, see the file).
5. Gate staging on a quiet PC and fast-forward `impl/b2` (gated only); then fast-strike round 3, the strike unit,
   Settings > Customization, voice typing, skills, in the order of NEXT_SESSION_CLAUDE.md section 7.
6. Rules that stay: tests only through `E:\Droppy\work\claude-verify\one.sh`, `one-full.sh`, `one-names.sh`; never a bare
   `dotnet test`; never open the app "Marrow"; never write to the reference checkout; agents never push `unit/*`.

**Incomplete (GR-1, `unit/f-gr-1`, first release OD-0103).** The first-run preparation service is built and tested
(`GpuWarmup` registry; `src\Marrow.App\Port\FirstRun\FirstRunPreparation.cs`), but NOT wired to startup:
`FirstRunStartup.RunAsync` has no caller and `FirstRunStartup.Screen` is null until GR-2's Getting Ready sheet sets it
from its startup hook, so startup behaves as before (the idle fallback of ED-252 still prepares the knob). The Voice
hardware step is absent from the plan until VT-15 registers its work step with `GpuWarmup.Register`. The composer light,
slider liquid and strike programs are verified on the CPU renderer only (the liquid and strike draw into a surface the
window's GPU context makes, through `GpuWarmup.WarmTarget`; a `RenderTargetBitmap` is a CPU raster in Avalonia 12.1.2 and
is not used); a GPU check (the programs made, one cache entry per program, `WarmTarget`'s reflection on Avalonia.Skia's
`DrawingContextImpl` working on a real context) is left to the supervised native session (ED-288 (8));
`EffortSliderNativeFrameTests` now cover nine programs. Review round 2: the marker now also waits for every registered work step (a first run that did not do Voice hardware leaves the machine unprepared even after a later idle pass), and a liquid or strike draw that throws inside its operation faults the warm-up (ED-288 (14) to (16)); both are CPU-renderer verified only, a GPU fault inside `LiquidTrack` is for the native session. Review round 3: the CPU renderer's first run now leaves the machine prepared (a `warm-cpu.txt` marker naming build, adapter and driver, ED-288 (17)), so the next launch skips it; verified headless on the CPU renderer only, and on a real software-rendered PC (a VM, RDP, a broken D3D11 driver) only in the supervised native session. Review round 4: the CPU marker counts only while the app draws on the CPU renderer (a launch on the GPU needs the GPU marker), is kept per adapter, and a registered work step is bounded by `GpuWarmup.StepTimeout` and ends on cancellation (ED-288 (18) to (20)); a GPU platform whose blob cache does not install still writes no marker (unresolved until the native session).

**Incomplete (VT-4, `unit/f-vt-4`, first release OD-0103).** The orb and glow drivers are Core code with tests only (`src\Marrow.Core\Port\Voice\Orb\`, `Glow\`): nothing draws or calls them yet. `OrbMotion` and `VoiceOrbRuntime` wait for VT-8's `VoiceOrbView` (SkSL port) and the orb swap crossfade; `VoiceGlowDriver`, `VoiceGlowResolver` and `VoiceGlowFrame` wait for VT-9's `VoiceGlowView` (the layers, the stylesheet recipe `Ya`, the 60 fps cap and the half-rate governor are the view's, not the driver's). The pointer-hovered Blocked glow follows the spec table, not the page (ED-302 (3)). The seeds are generated C# literals, not an embedded `seeds.json`, because `Marrow.Core.csproj` is protected (ED-302 (2)).
**Incomplete (SA-1, `unit/f-sa-1`, first release OD-0103).** Five Settings pages exist as hidden shells; none is listed in the sidebar or found by its search, and a request for one is ignored, until its unit sets the page class's `IsReady` const to true: Customization (`src\Marrow.App\Port\Settings\CustomizationSettingsPage.cs`, CZ-4), Voice typing (`VoiceTypingSettingsPage.cs`, VT-13), Updates (`UpdatesSettingsPage.cs`, UE-4), Plugins (`PluginsSettingsPage.cs`) and Skills (`SkillsSettingsPage.cs`), the last two SK-15. Each shell is an empty `Decorator`. Their titles, symbols, keywords and factory lines are in `SettingsView.cs` and final (no page unit edits that file); a page that wants the chrome row implements `ISettingsPageChrome`. The settings keys `voiceOrb`, `voiceTyping`, `voiceHardware`, `voiceNotes` and `workingDots` are read and written by `AppSettings` through the CZ-13.5 write coalescer, but nothing reads them yet; the page units that drag a slider must call `AppSettings.FlushPendingWrites()` when the drag ends (the app calls it on exit). ED-303.
**Incomplete (GR-2, `unit/f-gr-2`, first release OD-0103).** The Getting ready sheet (`src\Marrow.App\Port\FirstRun\GettingReadySheet.cs`, `RootView.ShowGettingReady`, the arm and run in `AppDelegate.ApplicationDidFinishLaunching`) is built and tested (47 tests, ED-306) and IS wired to startup: it supersedes the "not wired" of GR-1's note above (the screen goes to `FirstRunStartup.RunAsync` as an argument; `FirstRunStartup.Screen` stays null and unused). Not done or unverified: (a) the pane's saturation (.6 returning to 1) is not drawn, Avalonia 12.1.2 has no saturation effect; (b) the sheet has only been rendered on the headless Skia renderer (its glass, shadow, fonts and blur) and never on the real Acrylic window with a GPU, which is for the supervised native session, as is the blur's frame time over a large detail pane; (c) the line under the title is tracked -0.435 px in the app's font (Inter) to keep the page's one line and height (ED-307 (2)), pending the owner's eye; (d) the Welcome voice question (WV-1) and the voice model download are not part of this sheet (D3 with consent: Welcome asks); (e) the first-run tour now waits for the sheet to lift.
**Incomplete (SK-8 and SK-9, `unit/integ-sk8-sk9`, first release OD-0103).** The update/remove/repair pipeline (`src\Marrow.Services\Port\Skills\Pipelines\SkillsPipeline.cs`) and the per-chat generations (`Generations\SkillGenerations.cs`) are Services code with offline tests; nothing in the app constructs them yet. SK-13 must construct ONE `SkillsPipeline` per data root as its only owner (not a second `SkillsStore` GC on the same root) and bind `SkillGenerations` to it through `IGenerationStore`; first installs, the UE-2 group wiring and the Settings rows are SK-13/SK-15/UE-4. `SkillsStore.RepairAsync` still refuses archive kinds (the pipeline's `RepairAsync` is the archive repair). SK-4's install-time spares are kept with their route but SK-9 builds and claims its own spares. Not measured: PLAN 2.1's 150 ms ready-spare launch budget on this PC (DL-20). ED-326.
**Incomplete (VT-5, `unit/f-vt-5`, first release OD-0103).** Capture is Services code (`src\Marrow.Services\Port\Voice\Capture\`: `WasapiCapture`, `CapturePipeline`, `SampleRing`, `NAudioCaptureHost`, `MicrophoneChoices`) and `src\Marrow.App\Port\Voice\MicrophoneAccess.cs` with scripted-seam tests only; nothing in the app constructs or binds them. The start-up composition (`VoiceSeams.Capture`, `VoiceSeams.Microphone`, `CaptureSeams.Host`) is VT-14's, the Microphone row that lists `MicrophoneChoices.Items` and stores `voiceTyping.mic` is VT-13's. Real capture on a real microphone, a real Windows privacy denial, a real unplug and the real WASAPI buffer cadence are not exercised (a test never opens a device); the native tests only list endpoints and read the WinRT privacy value. ED-331.
**Incomplete (VT-10, `unit/f-vt-10`, first release OD-0103).** The composer's voice views are built and tested (50 tests, ED-NEW-f-vt-10) but OFF: `VoiceFeature.Enabled` (`src\Marrow.App\Port\Voice\VoiceFeature.cs`, V-2.6) is false until VT-15 (First run and switch-on) switches voice typing on, so no composer shows the orb slot, the notes layer or the announcements, and a click cannot start a session (the production seams are not bound: VT-14). LIVE for users: the attachment button's move to the composer's left, inside or outside (V-14.1, owner-ruled), eased. Not done: the voice level never reaches the orb (VoiceSession publishes no level or bands; the orb shows its states only); the text lands at Landing in one edit with no animation (VT-11's DictationLayer writes it live); no pill, cross, floating pill, language name or question (VT-12); no glow or composer-light give-way (VT-9, VT-12); only the cancelled, no-speech and blocked notes and the privacy line exist (the other errors raise none until VT-17); the session's model has no file path until the model store (VT-6, VT-7). Not seen on a real window with a GPU. The audit criterion of at most 15 % per frame cannot hold for the design's ez at 0.34 s and 0.42 s: ruled by the lead, each frame is checked against the curve (ED-NEW-f-vt-10 (3), (15)).

## Current checkpoint: 2026-09-23 04:30, orchestrator Claude Opus 5.5

This section supersedes every checkpoint below it.

**Branches.** `port/windows-exact-parity` (E:\Droppy\port) carries the owner and spec decisions made
outside a wave; `impl/b2` (worktree E:\Droppy\wt\integration) is where implementation waves merge, one
unit at a time. The two are merged into each other between waves.

**Owner rulings in force:** OD-0001..OD-0017. New today: OD-0015 the app icon is the owner's finalized
Marrow icon (assets in `src/Marrow.App/Assets/`); OD-0016 that icon, animated, on loading screens (an
owner-requested deviation, ED-097); OD-0017 live resize adopts `stretch` from probe P4b and accepts the
DWM residual as a named deviation (closes OD-0013; ED-101). Engineering decisions ED-001..ED-101.

**Foundation value types added for the UI layer** (ED-091, ED-092, ED-095, ED-098): SwiftFont,
SwiftColor, SwiftAnimation / SwiftTransition / IAnimatableModifier, StyledText, all as foundation units.
Rulings for the first wave's stops: ED-093 secret store on Credential Manager, ED-094 Calendar.current,
ED-096 ICU formatter API, ED-099 chord format (scan codes, Ctrl/Alt/Shift), ED-100 CocoaError (O-25).
Port types written directly: `src/Marrow.Core/Port/CocoaError.cs`, `src/Marrow.Core/Port/Keyboard.cs`.

**Wave A** (levels -1 to 3: 80 units, 103 parts) runs as workflow run `wf_114c4e01-e0c` from
`E:\Droppy\work\impl\impl-units.js` with `E:\Droppy\work\impl\waveA.args.json`. It was interrupted once
by the account's usage limit and resumed; finished agents replay from cache. Two earlier attempts are
kept in `E:\Droppy\wt\attic-waveA1` and `attic-waveA2` (branches `attic/waveA1/*`, `attic/waveA2/*`).

**Still to add after wave A, on impl/b2:** foundation units F.LoadingMark (ED-097; dependents
ThreadTimeline, DiffInspector, AttachmentPreview, AppDelegate) and F.ResizeStretch (ED-101; dependents
the window chrome units), then wave B (levels 4 to 8) and wave C (9 to 11).

**Lessons.** Workflow subagents see the owner's latest chat message; a question or side request made
some stop. Get an explicit go-ahead before a wave; rerun only the units that stopped. The account's
usage limit can end a wave mid-flight: resume with the same args so finished agents replay.

**Measured counts** (`node porting/tools/progress.mjs` on impl/b2): source files read 436/436;
requirements IMPLEMENTED 0/4,955 and VERIFIED 0/4,955 until wave A units merge; test methods 295.

## Current checkpoint: 2026-09-22 23:45, orchestrator Claude Opus 5.5

This section supersedes every checkpoint below it. Work runs as Claude subagent workflows driven from
the orchestrator session; results land in E:\Droppy\work\... and are committed here after review.

**Baseline B2** (OD-0006 / AM-0002): `1fbf81e61e28a079508a074c01ee1830a9cc23dc` (Droppy Code 1.6.2 build 20),
checkout `E:\Droppy\reference\droppy-code-v1.6.2`, record `porting/baseline.json`.

**Gates.** A 12/12 at B2 (436/436 files read with evidence; 119 re-read through reader, critic and
adjudicator). B 8/8 at B2 (spec refreshed for the delta: 3,938 requirements, 1,051 UI records,
4,973 in scope; every area critiqued and adjudicated). C: quarantine guard passes (no open entries);
stub scan fails on the entry-point stub (expected). D: per-requirement check live
(`porting/tools/req_verify.mjs`), VERIFIED 0 so far. E, F not built.

**Owner rulings in force:** OD-0001..OD-0014 (`porting/owner_decisions.md`), sheets
`porting/decision_batch_20260922.md` and `..._20260922b.md`. Engineering decisions ED-001..ED-063.
All 12 quarantine entries resolved; all 27 pending decisions resolved; OD-0013 makes the live-resize
edge strip a blocker for the window chrome (probe P4b is measuring remedies).

**Probes.** P2 PASS, P3 PASS via app scrim, P4 animation PASS / edge FAIL (OD-0013), P5 PASS, P6 PASS,
P7 PASS with the ED-063 hook, P8 PASS, P9 font: Inter (OD-0009), P10 PASS, P11 PASS.

**In flight:** conventions judge (`E:\Droppy\work\conventions\IMPLEMENTING.md` + foundation-proto),
spec maintenance (OD-0012..0014, ED-059..063, 20 verification spec defects, ledger cleanup; staged in
`E:\Droppy\work\spec_m`), probe P4b.

**Next, in order.** (1) Merge spec maintenance, run merge_spec / gen_tasks / Gate B. (2) Flag the 9
files behind the 13 early packets for rework (`porting/tools/flag_rework.py
E:\Droppy\work\takeover\rework_findings.json`). (3) Commit `porting/IMPLEMENTING.md` and port the
foundation helpers with tests. (4) Create branch `impl/b2` in worktree `E:\Droppy\wt\integration`,
run `porting/tools/plan_units.py`, then `porting/tools/build_impl_args.py --max-level N` and launch
the implementation workflow (`E:\Droppy\work\impl\impl-units.js`) wave by wave; merge `impl/b2` back
into `port/windows-exact-parity` after each wave. (5) Gates C, D evidence, E packaging, F review.

**Incident log.** A verifier once edited the main tree instead of its worktree (reverted, nothing
committed). Subagents hit the account's weekly limit once at about 22:40 and failed; after the owner
said "Resume" agents ran again and the interrupted workflows were resumed. Writing Windows paths
through inline shell heredocs turned "\r" into a carriage return twice; files with backslashes are
now written with the Write tool only.

**Measured counts** (`node porting/tools/progress.mjs`): source files read 436/436; requirements
VERIFIED 0/4,973; IMPLEMENTED 85 (to be reset to rework for the 9 files with findings); packets DONE
12/684; Swift files with real C# 9/194. No application functionality runs yet.

## Current checkpoint: 2026-09-22, direct worker bypass

This section supersedes all historical progress and next-action entries below.
Read [HANDOFF.md](../HANDOFF.md) and [the current evidence report](reports/direct-bypass-20260922.md).

- Workspace: E:\Droppy\port, branch port/windows-exact-parity. Latest implementation commit:
  a06858e; subsequent commits save evidence only. No unreviewed worker output is in the tree.
- Pinned source: dd2b86efef9e77168f304a86e8a8411877578bbe, tree
  f761e5ef3745c4f68958ffa4014ab0b618c9d419. Original checkout remains read-only and clean.
- Latest upstream candidate: 417ab7d3676cdf258fb6b68444f0626aa6fdfbed, separate read-only checkout.
  GitLab main still matched at approximately 04:00 UTC. Whole-source reread/spec refresh is incomplete;
  existing Gate A is not evidence for the candidate revision.
- Workers remain explicitly bypassed. Director performs implementation, tests review and adversarial
  review. T-M2-071 is HIGH and has no independent GLM review in this run; do not claim otherwise.
- Gate A 12/12, Gate B 8/8, protected/reference guard and quarantine guard pass. Warnings-as-errors
  build and all 251 tests pass. Full verify remains exit 1: existing app entry stub plus incomplete
  full acceptance C/D/E/F. No new gate regression.
- 13/662 packets DONE (1.96%); M2 13/78 (16.67%), 65 M2 remain (83.33%). 85/3827 requirements
  IMPLEMENTED (2.22%); 0/3827 VERIFIED. These are literal coverage counts, not overall effort.

Completed packet / requirement IDs:

| Packet | Implemented IDs |
| --- | --- |
| T-M2-001 | PR-HYDRA-055; PR-STORE-129, PR-STORE-130 |
| T-M2-023 | PR-MCP-038 through PR-MCP-047 |
| T-M2-025 | PR-MCP-001 through PR-MCP-010 |
| T-M2-026 | PR-MCP-011 through PR-MCP-020 |
| T-M2-027 | PR-MCP-021 through PR-MCP-030 |
| T-M2-028 | PR-MCP-031 through PR-MCP-037 |
| T-M2-030 | PR-GIT-052 through PR-GIT-055 |
| T-M2-043 | PR-RUNTIME-016 through PR-RUNTIME-020 |
| T-M2-059 | PR-STORE-017, PR-STORE-018, PR-STORE-020 |
| T-M2-066 | PR-STORE-053 through PR-STORE-058 |
| T-M2-071 | PR-PROVACP-149 through PR-PROVACP-153 |
| T-M2-075 | PR-STORE-008, PR-STORE-009; PR-STORE-105 through PR-STORE-112 |
| T-M2-076 | PR-STORE-113, PR-STORE-114 |

Last commands: per-packet protected/A/B/C/D checks via run-packet-gates.mjs; Debug and Release
build/tests; git diff --check; read-only assembly metadata probe. Exact outputs, exits, environment
and test mapping are saved with the current report in reports/direct-bypass-evidence/.

Next: setup/protected checks, then M2 in source-settled dependency order. JSONValue needs exact
Foundation numeric/formatting behavior; latest Hydra and Provider need spec updates. The revised
FileEdit carrier and text helpers now exist for Timeline/Hydra integration. Preserve every approved
test, take a new regression before any correction, and use temporary MARROW_PORT_DATA_DIR.

Unchanged blockers: 12 quarantined behaviors, recorded owner choices, shipping data-folder collision,
matching macOS visual/runtime oracle unavailable, M0 desktop probes, actual provider and persistence
integration, UI and installer acceptance. The earlier two-model bake-off remains incomplete while
the owner's bypass is active. No fresh account-balance or model-cost measurement was made this run.

## Historical session log (superseded by the checkpoint above)

Historical entry last updated: 2026-09-19, session 1.

## Baseline

| Field | Value |
| --- | --- |
| Pinned source | `dd2b86efef9e77168f304a86e8a8411877578bbe` (Droppy Code `main`, 2026-09-18) |
| Pinned tree | `f761e5ef3745c4f68958ffa4014ab0b618c9d419` |
| App version | 1.6.0, build 19 |
| Reference (read-only) | `E:\Droppy\reference\droppy-code` |
| Port workspace | `E:\Droppy\port`, branch `port/windows-exact-parity` |
| Port commit | none yet (no production code written; reading gate in progress) |

## Current gate

**Gate A, complete-code reading.** Not satisfied. Production application code and stack
selection are blocked until Gate A and Gate B are satisfied (contract sections 5, 6, 9).

## Done

- Upstream reachable; default branch resolved and frozen to a full SHA.
- Full clone at the pinned SHA; no submodules, no LFS, no symlinks, no case collisions,
  `git fsck` clean, working tree matches the pinned tree exactly. Recorded in `porting/baseline.md`.
- Build inclusion rules read from `project.yml`: `DroppyCode/` is a syncedFolder (everything in
  the app target), plus `LICENSE`, `THIRD_PARTY_NOTICES.md`, `TRADEMARK.md` as bundle resources.
- Instruction sources inspected and conflicts recorded in `porting/instruction_conflicts.md`
  (C-01 through C-06). Two matter most: the prior "Marrow" port is pinned to an **older** upstream
  commit and is a **deliberately divergent product**, so it is not the specification.
- Mechanical inventory generated: **433 files, 85,392 text lines**, every file classified with
  build-inclusion evidence. `porting/source_inventory.json`.
- Reading partitioned into **63 nonoverlapping bundles**, mechanically proven to cover all 433
  files with no file in two bundles and no file split across bundles.
  `porting/assignments.json`, `porting/tools/partition.mjs`.
- Read-gate tooling written: `gen_inventory.mjs` (refuses a dirty reference),
  `bundle.mjs` (authoritative per-reader assignment), `ledger_apply.mjs` (rejects a report whose
  sha256 mismatches, whose ranges leave a gap, or whose findings are generic).

## In progress

- Reading gate workflow `wf_a6f89100-da4`: 63 reader agents, then 9 subsystem reconciliation
  critics. Reports land in `porting/reports/<BUNDLE>.json`.
- Lead's own spine reading (contract section 5 requires the lead to personally inspect the
  source for each subsystem it implements; agent reports do not close a file for the lead).

## Not started

- `porting/architecture.md`, `porting/parity_matrix.json`, `porting/ui_contract.json`,
  `porting/platform_adaptations.md` (Gate B).
- Windows architecture decision record (Gate B must complete first; contract section 9 forbids
  choosing a stack before discovery).
- Verification entrypoint and CI checks (contract section 14).
- Any production application code.

## Owner decisions taken (see `porting/owner_decisions.md`)

- **OD-0001** No macOS access at all. UNK-0003 closed as permanently blocked. macOS-delegated
  rendering ships UNVERIFIED and is named individually in the final report.
- **OD-0002** The Windows build ships as **Marrow**. UNK-0002 closed. This is an owner-approved
  deviation limited to product identity, so the final statement can be at best
  "VERIFIED WITH USER-APPROVED DEVIATIONS". It does **not** authorize adopting the separate
  existing Marrow product's features. AGPLv3 section 7 attribution stays intact.
- **OD-0003** Liquid Glass: exact look, zero guesswork. Visual fidelity becomes the primary
  weighted criterion in the stack evaluation; a stack that cannot render a custom material with
  backdrop sampling, blur, specular edge and interactive response is disqualified.
- **OD-0004** Reference material found in-tree: eight real 1320x824 macOS screenshots of the
  running app (`tour-*@2x.png`), plus the app's own capture harness
  (`App/Captures/`, 1740 lines). Approximate oracle only; never a pixel-parity claim.

## Blockers

| ID | What | Blocks |
| --- | --- | --- |
| UNK-0005 | What shell, if any, runs model-issued commands on Windows | PA-0009, PA-0011 decisions |
| UNK-0006 | Command Code's executable is literally `cmd`, colliding with the Windows interpreter | Provider launch; needs executable-identity validation |
| UNK-0007 | SF Symbols (70 distinct, 206 sites) and the macOS system font cannot be redistributed | Icon/type substitution decision; text metrics feed layout |
| UNK-0008 | App icon artwork and in-repo Droppy marks under the Marrow name | Packaging identity |

## Next concrete actions

1. Apply reader reports through `ledger_apply.mjs`; re-dispatch every bundle it rejects.
2. Resolve every critic finding by rereading the real source, not by editing a report.
3. Reconcile inventory coverage to 433/433 READ, then close Gate A with the mechanical check.
4. Derive `architecture.md`, then the parity matrix and UI contract from the ledger plus
   direct source reading.
5. Only then evaluate Windows stacks against the derived requirements.

## Commands actually run (latest)

| Command | Result |
| --- | --- |
| `git ls-remote https://gitlab.com/droppyformac1/droppy-code.git` | `HEAD` == `refs/heads/main` == `dd2b86e...` |
| `git clone ... && git checkout --force dd2b86e` | exit 0; 218 MB; tree matches |
| `git fsck --no-progress` | no output |
| `git status --porcelain` (reference) | empty |
| `node porting/tools/gen_inventory.mjs` | 433 files, 85,392 lines |
| `node porting/tools/partition.mjs` | 63 bundles, 433/433 assigned, no overlap |

## Session 1 addendum (2026-09-19, after the owner supplied getdroppycode.app)

- Reading gate: 44/63 bundles reported, **243/433 files** past the evidence gate, 0 rejections
  since the two tool fixes. Workflow `wf_a6f89100-da4` still running.
- Ledger tooling defects found and fixed (documented in the tools, standard not lowered):
  extensionless LICENSE classified as binary; 0-byte file rejected for having no ranges;
  .gitignore rejected for having no symbols. Inventory repaired in place without resetting reads.
- **Update feed resolved and it is platform-neutral (PA-0014).** `App/UpdateChecker.swift:66-71`
  reads the public GitLab Releases API, project id 86415974, per_page=20, no token. Launch delay
  8s, interval 6h, plus on-activation and on-About. Keys updateChecker_release,
  updateChecker_lastChecked, updateChecker_notifiedVersion. The feed is reused unchanged on
  Windows; only the artifact and the install mechanism differ. UNK-0009 records that no Windows
  release asset exists yet, so the live update path stays BLOCKED rather than faked.
- **Reference captures mirrored with permission:** 24 real macOS renderings, 3,084,116 bytes,
  in `porting/reference_captures/` with `PROVENANCE.tsv` (url, bytes, sha256, fetch time).
  hero is 2400x1500; most are 2080x1300. Approximate oracle only; source wins on disagreement.
- Capture geometry is a SOURCE FACT: `WebsiteCaptures.swift:658-659` wide=1320x860,
  narrow=860x560, scale from backingScaleFactor, even-rounded pixel dims, scalesToFit=false.
  The Windows port mirrors this harness so captures are like-for-like.

## Next concrete actions (updated)

1. Finish the reading gate to 433/433; re-dispatch any rejected bundle.
2. Work every reconciliation critic finding back against real source.
3. Read the capture harness crop rects per capture, to mirror states and fixtures on Windows.
4. Derive architecture.md, parity_matrix.json, ui_contract.json from ledger + direct reading.
5. Only then evaluate Windows stacks, with glass fidelity as the primary weighted criterion (OD-0003).

## Session 1, checkpoint 3: reading gate closed, verification entrypoint live

### Reading gate (contract section 5)

- Workflow `wf_a6f89100-da4` completed: **63/63 bundles, 0 failed**, 72 agents, 10.4M subagent
  tokens, 1615 tool uses, 49 minutes.
- **433/433 files past the evidence gate**, 433 ledger records, 1:1 with the inventory.
- `ledger_apply.mjs` made idempotent after re-runs produced 744 duplicate lines for 433 files;
  the ledger is now keyed by path and rewritten whole. Verified by applying all 63 reports twice.

### Reconciliation (contract section 15)

- 9 independent subsystem critics raised **182 gaps and contradictions**; 8 of 9 families returned
  NEEDS_REREAD. Findings written per family to `porting/critiques/*.md`.
- The critics independently re-verified 239 reported sha256 digests byte-for-byte and confirmed
  every `lines_total` and every range covering line 1 to the last line. The reading was genuine.
- A critic caught an error in THIS register: PA-0011 said the Shell reader set had 47 entries.
  Counted from source: **52**. The transcribed list was complete; the stated count was wrong.
  Corrected in place with a note. gitReaders 25 and excludedKeys 15 were confirmed correct.
- Repair workflow `wf_51997e5b-95e` running: 9 family adjudicators plus 7 verbatim extractions
  (ThreadRuntime ProviderEvent dispatch, Chrome design tokens, Hydra prompt corpus, markdown link
  grammar, provider argv/handshakes, git command vectors, MCP protocol and OAuth assets).
  Every critic finding is adjudicated against source; a critic may be wrong and must be shown wrong.

### Verification entrypoint (contract section 14)

- `node porting/tools/verify.mjs [--json] [--gate A..F]`. Exits nonzero on any unmet mandatory
  check. Gates B through F report NOT_IMPLEMENTED and **fail by design**, never pass by omission.
- Current: Gate A **11 PASS, 1 FAIL** (critic findings not yet adjudicated - the repair pass is
  running). Overall RESULT: NOT ACCEPTABLE, exit 1. This is the correct state.
- `node porting/tools/negative_test.mjs`: six controlled regressions injected and all six detected
  by name (unread file, tampered digest, duplicated record, partial coverage, reused generic
  finding, inventory diverging from the reference). Evidence restored byte-for-byte; gate state
  matches baseline. Exit 0.
- PA-0015 recorded: the negative test hit a real Windows file-lock defect (errno -4094 writing a
  file a just-exited child had read), which damaged evidence until the gate caught it. Restore is
  now retried with backoff. Same defect class the product will hit in git, checkpoint and store
  writes.

---

# GATE A CLOSED (2026-09-19)

`node porting/tools/verify.mjs --gate A` -> **12/12 PASS**.
Full run -> PASS=12, NOT_IMPLEMENTED=5, exit 1, RESULT: NOT ACCEPTABLE. Correct: Gates B-F are
not built and fail by design.

Port commits: `9f66288` (Gate A evidence), `ddfff51` (line-ending pin).

## What Gate A rests on

| Evidence | Value |
| --- | --- |
| Files read with evidence | 433/433 |
| Ledger records, 1:1 with inventory | 433 |
| Digests re-verified against the pinned reference | 433 |
| Critic findings raised / adjudicated | 182 / 258 |
| Verdicts | 222 CRITIC_CORRECT, 41 PARTIALLY_CORRECT, 1 CRITIC_WRONG |
| Verbatim extracts | 7 files, 520 KB |
| Upstream defects to reproduce | 99 |
| Dead/unreachable items with proof | 60 |
| Port impacts | 264 |
| Platform adaptations | 15 |
| Unknowns | 11 |
| Negative checks detected / injected | 6 / 6 |

## Corrections made to this port`s own records

1. PA-0011 said the Shell reader set had 47 entries. Source has **52**. Caught by an independent
   critic, verified by counting, corrected in place with a note.
2. The git extract said 19 sites at `timeout: 300`, then self-corrected to 12 mid-sentence.
   Counted from source: **18 total, 12 git, 6 forge**. Recorded in the defects register.
3. Upstream defect 22 implied the PKCE/CSRF token generation was cryptographically weak. It is
   not: Swift`s default RNG is CSPRNG-backed on Apple platforms. Reframed as a **port hazard**
   (the byte counts 32 and 24 and base64url are parity; the RNG choice is the port`s and must be
   a platform CSPRNG on Windows).

## Escalated rather than reproduced

- **UNK-0010**: deleting a custom MCP server runs on one click with no confirmation
  (`MCPSettingsPage.swift:564-566`) and also deletes the server`s Keychain secrets
  (`MCPStore.swift:124`, `:193-199`), ignoring `confirmBeforeDeleting` which defaults true and is
  honored by six other destructive paths. Reproducing it destroys credentials the user cannot
  recover; fixing it silently is a product change. Contract section 8 requires escalation, so this
  control is deferred until the owner decides.

## Next: Gate B

1. Derive `porting/architecture.md` from the ledger, the 7 extracts and direct reading.
2. Build `porting/parity_matrix.json`: every atomic behavior with a stable ID, source evidence,
   preconditions, action, expected result, side effects, persistence, failure and cancellation,
   Windows path, verification method, status.
3. Build `porting/ui_contract.json`: every screen, control, state, visual property, interaction,
   seeded from the Chrome token extract, the reference captures and the UI-layer ledger records.
4. Implement the Gate B checks in verify.mjs so completeness is machine-checked, not asserted.
5. Only then the Windows stack evaluation, with Liquid Glass fidelity primary (OD-0003).

---

# GATE B PASSES (2026-09-20)

node porting/tools/verify.mjs -> Gate A 12/12, Gate B 8/8, 21 checks PASS,
Gates C-F NOT_IMPLEMENTED and failing by design. Exit 1, RESULT: NOT ACCEPTABLE. Correct.

Port commits: 4cf7e30 (Gate B tooling), d0d2086 (defect triage + architecture),
9875483 (BLK-0005), 418651b (Gate B passes).

## Specification produced

| Artifact | Value |
| --- | --- |
| Parity requirements | 3827 (3742 SPECIFIED, 85 DISCOVERED) |
| UI contract records | 1029 |
| Areas | 20 |
| Controls enumerated by exact label | 1433 |
| Persisted fields | 570 |
| Background tasks | 294 |
| Architecture record | 63 KB, 29 modules, 194 source citations |
| Layering violations found vs AGENTS.md | 8 |
| Verbatim extracts | 7 files, 520 KB |

## Quarantine (owner decides individually, neither option approved)

| Entry | Criteria | Behaviour |
| --- | --- | --- |
| BLK-0001 | C1,C2,C5 | custom MCP delete: unconfirmed, destroys Keychain secrets |
| BLK-0002 | C4 | malformed custom-MCP variable discards every server, next save overwrites |
| BLK-0003 | C1,C4 | checkpoint truncation by cookie timeout OR forgery; revert then deletes work |
| BLK-0004 | C1,C4 | previewRestore path mixing; revert deletes wrong files, reports success |
| BLK-0005 | C1,C4 | legacy migration folder literal vs AppInfo.name under the rename |
| BLK-0006 | C2,C3 | API key written to defaults in PLAIN TEXT when protected storage refuses |
| BLK-0007 | C2,C3,C6 | plan limits read other apps credential files; Pi transmits the token |
| BLK-0008 | C3,C4 | read-only classifier is ALSO the auto-approval gate; 3 misclassifications |
| BLK-0009 | C1,C5 | Remove project unconfirmed, ignores confirmBeforeDeleting |
| BLK-0010 | C1,C4 | undecodable library.json starts empty, first save overwrites |
| BLK-0011 | C1 | unreadable hasCustomTitle lets auto-titler overwrite a typed name |
| BLK-0012 | C2 | credential deletion on disconnect discards its result |

27 requirements held at DISCOVERED across these. 19 marked by hand after two rounds of
over-broad automatic matching were caught and reverted (76 then 32, both wrong).

## Decisions

- 57 engineering decisions taken and recorded in porting/engineering_decisions.md
- 27 owner decisions still needed, in porting/owner_decisions_pending.md
- 78 spec questions resolved from source

## Next: Windows architecture selection (contract section 9)

Workflow wf_468d7feb-688 evaluating 8 candidate stacks against the 12 derived constraints, with
Liquid Glass fidelity as the primary weighted criterion per OD-0003, then an adversary pass
against the leaders. No stack chosen yet. No Windows code written yet.

Machine constraint found: .NET 10 RUNTIME only (no SDK), no MSVC, no cmake, no Rust. Node 24,
Python 3.14 and winget are present. Toolchain will be installed only for the chosen stack, after
the decision record, rather than speculatively.
