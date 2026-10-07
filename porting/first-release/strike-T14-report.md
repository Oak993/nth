# T14 Strike closeout

READY for code/raster/production integration on codex/strike. Worktree E:\Droppy\wt\cx\strike, baseline0ec0b95c2f8b8fd90387cf2967dd8cc569931b81. Validated implementation checkpoint5cf40cc4c253def4952551f0036307dff2773706; final metadata/report commit and pushed branch SHA are recorded in E:\Droppy\work\scratch\handoff\codex-T14-strike.md. T9 merges from that handoff/branch. The lead explicitly waives mark.mjs for T14; no queue packet or T-M7 unit was edited.

ST-0 fixes retained frame clocks, whole-refresh over-counting, native child server GC and main/render-thread allocation measurement/gating. ST-1 implements locked Mix timing, geometry/plume/events, ink/palette/tint, windows/clean random entry, lanes/ramps and knob signal. ST-2 integrates frame masks/lights/lit contacts, independent zero-miss burst audit, burst drawings and clean reentry. ST-3 renders six retained A8 masks into over/screen groups, stages RGBA8 soft before CSS blur, combines native body/core shader nodes, integrates refraction/bubble/ring/glass order, full-tube gradients, shared knob clock, model tint fade, solo toggle ownership and the real FirstRun Strike warm family.

Source-backed budgets: actual full-tube raster composite peaks Claude4/OpenAI3/AWS7, plus one glass draw<=8. Combined resolve/contact/knob clean reentry builds at most one plan per frame. Full warmed liquid/Mix/bubble/ring/glass pipeline allocates exactly0B over2880frames; retained surface/filter/gradient counts are stable. Capacity walks cover100000primary/twin strikes on each of four geometries. Independent contact audits report zero misses on all five makers; locked burst totals154/154/153/185/158/73 and every ordered field remain unchanged. All80 model-wait/jump cases retain original counts/timestamps/tolerances. Two-hour lane history/reset equals cold processing without restarting cached lanes.

Validation uses unchanged heavy.sh and official wrappers, one heavy run at a time, fresh data directories/provider keys unset, Native excluded. Final receipts under E:\Droppy\work\scratch\codex-strike:

| Receipt | Result |
| --- | --- |
| st3-close-scope.log | Strike/knob/Traceability154/154,0skips |
| st3-close-all.log | Composer/clocks/GPU-warm/Traceability1380/1380,0skips |
| st3-final-ui.log (earlier full sweep) | UI3465/3465,0skips |
| st3-close-ui.log (latest full sweep) | Raw UI3464pass/1fail/0skip/3465 |
| st3-close-ui-alone-1/2.log | Aurora allocation method12/12 each,0skips; raw failure UNCONFIRMED under the explicit alone-twice rule |
| st3-close-release-1/2.log | Release D04 1/1 each,0skips; reports confirmRelease |
| close-gate-A/B.json | verify.mjs GateA/GateB PASS |

All final builds have zero warnings/errors. Latest UI raw failure: AuroraLoaderTests.Drawing_allocates_no_managed_bytes_after_warm_up(FlowArc40x40scale1.25),7024B atframe5. Aurora source is unchanged; all12 method cases passed alone twice. Do not describe the latest raw sweep as clean. Earlier raw Composer failures are retained in the handoff; the old overshoot-start check passed alone twice and the final1380-test sweep is clean.

Release timing over3600 measured frames after3600warm frames:

| Run | Resolve p99 / max ms | Contacts p99 / max ms | Plan p99 / max ms |
| --- | --- | --- | --- |
| 1 | .0444 / .1041 | .1340 / .2194 | .0094 / .0161 |
| 2 | .0453 / .1008 | .1323 / .2236 | .0090 / .0463 |

Headless gates p99resolve<=.25ms,contacts<=.15ms,planmax<=.5ms pass unchanged. These are not native recording/submit or presentation measurements.

Assigned PR-COMPOSER-112,117,123,125,126,127,128,132,133 and UI-COMPOSER-012,030,031,033,035,036 are IMPLEMENTED with source-bound test classes and final1380-test receipt, synchronized into spec/COMPOSER.json. Deep JSON audit preserves all contract fields/unassigned records and permits only assigned status/evidence plus PR counts(IMPLEMENTED+9,SPECIFIED-9). No VERIFIED status is claimed. UI035's existing no-Req-credit guard remains; locked-page glass/fringe/inner-shade capture and owner eye check remain unverified.

ED-521..12 record the steps. ED10 and commit553c8227d list EVERY lead-authorized superseded A2 production assertion, replacement oracle, SPEC/OD and unchanged precision; all legacy maths/other assertions remain. ED5 and strike-TESTS-evidence.md list all19 A17 serialization ties, using the locked generator's uniform full-precision input construction, without changing vectors/counts/timestamp checks/tolerances. ED11 and commit5cf40cc4c record plan/composite budgets and the faithful raw-premultiplied readback for NEW branch raster guards under SPEC6.4 and the lead's protection ruling. C02 precision remains1/255(no spark)/3/255(spark), B12/per-layer1/255; alpha/gain/union/independent reference pixels and all legacy assertions unchanged. Diagnostic failures alone twice proved raw RGB/alpha maxima1 where decoded/re-multiplied RGB falsely exceeded1. C01's explicit SPEC6.4 ruling remains at1/255.

Native quiet-PC gate: **needs the lead's quiet window**. No native run/quiet wait occurred in this phase, as instructed. Exact prepared current-production ST0 probe:

```powershell
& 'C:\Program Files\Git\bin\bash.exe' 'E:/Droppy/work/scratch/codex-strike/strike-quiet-window.sh'
```

Duration5secondswarm+60secondsmeasured, plus build/start. It uses heavy/native locks, official wrapper and server-GC child, with informative allocation/update/GC/main-thread report. [strike-native-release.md](strike-native-release.md) lists the exact unchanged full PhaseE pass gates and verified DXGI capture command. The ST0 probe is executable, but does not replace the lead's scenario/presentation/baseline-equal allocation/independent native-audit campaign. Historical under-load ST0 measurements are informative only:59.97967updates/s,main0/recordings0,UI414.775B/s,quiet80B,drawn5792B,gap21.5718ms,GC[1,0,0],CPUmean30.22%/max94.41%. They are not a quiet or native-presentation pass.

Reviewer owns D07 mutation tests (none run by implementer); owner page visual check remains pending. No owner Marrow app, installer/package/sign/release, real CLI/model/network test, reference write, other worktree write or PC-wide setting change occurred. These explicit release gates do not block this owner-authorized code integration closeout.

Verdict: READY
