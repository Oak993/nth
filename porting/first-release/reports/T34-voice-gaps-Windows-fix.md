# T34 voice-gaps Windows repair

Verdict: READY for T9 code integration under the lead's twice-alone rule. Raw combined failures remain recorded below; this is not native or full release acceptance.

Production repair: `67a040d19bf2946e73a85200aeae5c6f5b06ef52`. Separate owner-approved fixture repair: `9e83ac580e8e5e9bc47e40b765aac6744169d429`. The final delivery commit adds only this report and the append-only STATE checkpoint.

Branch `codex/voice-gaps-fix`, worktree `E:/Droppy/wt/cx/voice-gaps-fix`. Input `5a2f4ea4a983a4433017874a2104b5a0f870a9bb`; staging `666097b04b728cff79b55af9b9444fed213abf78`, merged first in `b2535826f342ec90552b48828761f59795f12ece`. Because staging already contained the input and its subsequent voice-only withdrawal `95b27d3ef`, the withdrawal was restored before repair. Other withdrawn inputs were not restored.

The unchanged restored Windows baseline reproduces all four held methods alone twice. Both Urdu identities differ at one joining-edge pixel; the feature identity demands a caret displacement although the bundled Inter has no `liga` substitution; the release-note fallback resurrects a field note in the notice line; the final-pass curve oracle omits implicit confirmation on the next Add.

Production now keeps captured cluster paint order while glyphs glide after rewrapping. It retains target native order after settlement and freezes the actual displayed order for an old revision. Captured coordinates, glyphs, curves and the exact original first-frame assertion remain unchanged. Independent analysis of 42 clusters per capture locates the Urdu witness at (35,16), or (35,33) with the English prefix, RGB33 before versus RGB32 after, alpha255. Two joined glyphs reverse order without moving. A new repeated-reflow regression failed both identities before the lifecycle repair and passes afterward, comparing exact first frames and settled native pictures across five width changes.

Controller note placement now has one owner. Null release-error fallback calls that owner's refresh, preserving field-note dismissal after typing. Actual release errors retain dynamic field/notice movement and cached lifetime/announcements. The existing held note and newer mounted-error tests remain unchanged.

Only two old oracle assumptions are retired under the user's explicit locked-design exception and ED-581. SPEC V-8.3:408-413 requires exact native picture/caret; it does not demand feature-induced displacement. All property cases now prove native layout invalidation while retaining exact native picture/caret comparisons; other properties still demand displacement. A new Inter cv01 numeric case proves visibly changed ink outside both live carets and exact native glyph/caret parity. Locked voice-composer.js:283-294 confirms the prior visual word on every Add; :701-702 applies the same rule during final processing, with HTML:123's .45s ease. The curve oracle now includes that event while retaining all 60Hz assertions and logical-tail/landing guards. Original oracle files and locked mirror SHA provenance are preserved in scratch. The only additional existing test-file edit is the separately approved atomic fixture counter described below; no other old assertion, reference or tolerance changed.

Evidence root: `E:/Droppy/work/scratch/codex-voice-gaps-fix`. Independent source review: `review/independent-final-review.md`. Original mixed-ending ED prefix (2,122,860 bytes) is preserved. Rejected native-coordinate/custom-drawing work is archived and removed from delivered code. Repair-2 used stale binaries after its diagnostic build failed and is explicitly excluded from validation.

Final Windows evidence, all through the required wrappers and shared heavy slot:

| Selection | Passed / failed / skipped |
| --- | --- |
| Focused App repair | 148 / 0 / 0 |
| Four held methods, each alone twice | 3/3, 4/4, 1/1, 1/1 in each run |
| New repeated-reflow and visible-feature guards, alone twice | 2/2 and 1/1 in each run |
| Full Core | 4899 / 0 / 0 |
| Raw full Services, before the fixture repair | 5691 / 1 / 0 |
| Repaired NVIDIA installer, alone twice | 1 / 0 / 0 each |
| NVIDIA companion fixture and counting proofs, twice | 10 / 0 / 0 each |
| Final Services voice plus traceability | 499 / 0 / 0 |
| Raw full App | 10139 / 76 / 0 |
| All 73 raw-failing App methods, each alone twice | 152 / 0 / 0 total |
| UI voice/glass/clock slice, twice | 756 / 0 / 0 each |
| Gate A / Gate B | 12/12 / 8/8 |

The Services failure was confirmed twice alone: `NvidiaCompanionInstallerTests.Verified_archives_publish_all_three_libraries_together_and_keep_the_pack`, Expected 2 / Actual 1. Two concurrent downloader workers completed both fake archives and the successful, hash-verified install, but unsynchronized `Requests++` lost an increment. The owner's explicit fixture-only exception permits `Interlocked.Increment` at the same receive point and `Volatile.Read` in the getter. Every original installer assertion, expected count of 2, request concurrency and production behavior remain unchanged. ED-582 records the scoped exception; the repair is committed separately. The new eight-worker proof counted 3948 rather than 4096 before the repair and now counts exactly 4096, checking every response. The one-request proof still rejects the existing two-request expectation. Independent supplement: `review/nvidia-request-counter-supplement.md`; approval: `owner-fixture-exception.md`; green logs: `nvidia-fixed-alone-*.log`, `fixture-counter-green-*.log`, `fixture-final-services-voice.log`. The original raw Services sweep remains red; no clean full post-repair sweep is claimed.

All 76 raw App failing identities across 73 methods passed twice alone unchanged, so none meets the lead's repeated-failure criterion. They remain separately classified as unconfirmed combined-run failures; the raw App sweep stays red. Exact method/identity dispositions and paths: `raw-failure-classification.json` and `raw-app-alone-*.log`. No App/Services failure was erased or silently relabeled. All acceptance builds have zero warnings/errors; the retained invalid diagnostic build is excluded.

RR-11 is a subsequent separate task on `codex/rr-11` from staging with the owner-approved merged pill. No RR-11 implementation or native/manual release acceptance is claimed here. Tests use official wrappers, fresh throwaway data, fake application seams and the shared heavy slot; Marrow was never opened. These first-release rows have no corresponding legacy packet ID in porting/tasks/queue.json, so no unrelated task was marked DONE.
