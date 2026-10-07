# T36 skills UI release-gap report

2026-10-07. Branch `codex/skills-ui`, based on `origin/claude/verify-terminal-20261001` at `285b6b7a`. Audit input: `origin/codex/release-audit` at `6b2e6994`, `release-readiness.md` rows RR-17, RR-18 and RR-44. Law: CLAUDE.md, IMPLEMENTING.md, owner_decisions.md OD-0099 amendments / OD-0076, skills-ONDEMAND-PLAN.md, and the locked skills-downloads design sources. This report records implementation and remaining acceptance; it grants no design deviation.

| Row | Verdict | Implemented | Remaining release gap |
| --- | --- | --- | --- |
| RR-17 | BLOCKED | Same minimized library runs mounted in both sidebar locations; compact popup controls, completion/failure persistence and enlarge to the original page/run. | Exact locked motion/presentation remains unfinished. See ED-NEW-skills-ui-3; native acceptance is also outstanding. |
| RR-18 | BLOCKED | Add source menu, native picker wiring, validation/checklist/parallel-add UI through an explicit fakeable seam; optional Tools library and its own Download all. | Production custom validation/install/Your own registry APIs are absent; Check/Add is disabled until a real backend is bound. Own-items presentation/integration and exact native sheet/menu acceptance remain open. |
| RR-44 | BLOCKED | Whole offer card now fades/collapses, including title/host/padding; transferred bytes shown; unknown total uses a spinner and omits percent. | Ordered-start race remains in Services, outside the authorized UI scope. |

None of the rows was ALREADY DONE at the branch point.

## Missing-state evidence and changed code

RR-17 at base: DownloadAllDialog.cs:245 only hid/dismissed the dialog; LibraryPage.cs:187 hid the sheet without a progress composition; SidebarView.cs:312 mounted only the unrelated update pill; SettingsView.cs:213 had no progress footer. New implementation: SidebarView.cs:315 and SettingsView.cs:211 mount SkillsProgressPills; SkillsProgressPill.cs:61,143 implement the pill and compact controls. LibraryModel makes hidden state observable; LibraryPage preserves it through completion and enlarges the same run. Wider unchanged review tests caught a redundant false assignment on a closed/detached page; the guarded write fixes it without changing those tests.

RR-17 exact-design gap: SkillsProgressPill.cs:99 currently uses a single .4 s crossfade/movement; sd-page.css:456-459 requires .3 s opacity, .4 s movement and .12 s arrival delay. The .45 s delayed ring/check treatment at CSS:469-470 is absent. SkillsProgressPill.cs:73 delegates popup appearance to PlainControls / Presentation / PopoverAppear, whose default spring/scale differs from CSS:473-476 (.22 s opacity, .32 s movement, scale .92, translateY 8px). These require further UI work and native verification; successful functional tests do not close them. No alternate timings have been approved.

RR-18 at base: LibraryPage.cs:35-81 had no Add action; UpdatesSettingsPage had no optional-download library; LibrarySection.cs:8-12 contained only Plugins/Skills. Import skills and manual fallback are separate flows. New CustomAddAction/CustomAddSheet implement Custom C/Browse: exact source labels; 196-point menu, 500-point sheet, local read-only paths with Browse and ZIP filter, GitHub editable, valid-only selection, side-by-side Add, per-item progress/check/retry, and source-reset isolation. CustomAddSheet.cs:23 defines ICustomAddBackend; :34 intentionally defaults to null because SkillsHost/ISkillsLibraryBackend have no matching production API. It does not fabricate an installation. The schema/service integration for persisted Your own items, provenance and command registration is still required. Custom sheet/menu currently use shared presenters; their locked clip/fade native acceptance is outstanding.

ToolsLibraryBackend.cs:15 filters Tool/Pyset/Nodeset into the existing LibraryController's presentation path. Its original-install wrapper (:25) restores the real catalogue kind and entry before installation; UI projection cannot alter trust verification. It forwards terms, state, drive space and installation/removal controls. OptionalToolsPage joins UpdatesSettingsPage (the existing Tools page, OD-0100 name); Tools have their own download run, no skill switches/provider imports, and enlarge to Updates. No new Core enum or service/runtime changes.

RR-44 at base: OfferCardControl.cs:52-62 kept title/host/padding outside the crossfade; :131 substituted 0% for null percent and displayed no transferred-byte count. New OfferCardControl.cs:33,109,136 puts the entire surface inside the animated slot. Terminal Done/Continuing/Declined now reach less than two points of visible height before leaving layout rather than cutting the final 66-point header. :156 displays bytes; :188 swaps known progress for an indeterminate spinner. OfferTransferWords follows the existing English file-byte rules documented in SwiftFormatters.cs:479-560 without loading Windows ICU in this English UI surface.

The ordered-start race is still present: SkillsDownloads.cs:147-155 creates/reserves a flight under `gate`, then :176-180 calls Notify and enqueues after releasing it. Another caller can enqueue first. Serializing UI buttons would not close this race for concurrent non-UI callers. Its fix and fake-controlled service regression test belong in the backend-owned code; no service file was changed here.

## Validation and evidence

All new checks use fake catalogue/backend/install/picker seams and manual UI/frame clocks; no real download or remote network operation was performed by tests. Existing baseline tests, tolerances, fixtures/references and assertions remain byte-identical. Engineering/STATE ledgers are appended in binary mode; original bytes and mixed line endings are preserved.

Test-first evidence: rr17-red.log failed on absent progress types; rr18-red.log failed on absent custom/types; rr44-red.log failed on missing byte text and the 66-point terminal cut in all three terminal phases. rr44-format-red.log failed before the new locale-independent formatter existed. rr44-final-green.log records 48/48 targeted offer/format tests. rr17-review-green.log records 9/9 unchanged review plus new progress tests. rr17-regression.log records 81/81 and rr18-regression.log 88/88. TRX files and raw logs are under `first-release/evidence/skills-ui/`.

The broad initial 240-test selection recorded 231 passes / nine failures. Two were the new redundant write and are fixed by the unchanged review checks. The other failures entered Windows ICU/native offer/uninstall paths. The Linux selection explicitly excludes SkillsOfferFlowTests, SkillsOfferTimelineNativeTests and SkillsUninstallHookTests, without modifying or skipping any test source. It also includes library/import/settings/update-pill and traceability regressions.

An initial repeat under concurrent solution compilation recorded three 211/211 passes then 209/211 (two ImportSkills UI assertions failed). Its logs are retained as `linux-preliminary-run-*`; this result is not erased or labelled a proven baseline defect. All five final serial runs passed 211/211 with zero skips; the results are recorded in `final-run-*` logs/TRX. The complete Windows suite, including those import assertions, must still be repeated five times per R-TEST-5; a filtered Linux run is not the complete suite.

Linux SDK 10.0.100 solution build, Debug with EnableWindowsTargeting=true: zero warnings, zero errors. Gate B: 8 PASS. Gate A cannot verify the missing E:/Droppy reference checkout (5 FAIL / 7 PASS). Gate C: existing 26 MARROW_STUB markers and an unimplemented semantic gate (1 PASS / 1 FAIL / 1 NOT_IMPLEMENTED). check_protected reports no working-tree protected changes but emits missing-Windows-reference errors, so its reference-clean text is not sufficient evidence of Gate A.

## Windows-run needs

- Restore the pinned E:/Droppy reference checkouts and run Gate A/protected-reference validation; resolve existing Gate C release blockers through their owning work.
- Build the intended Windows configurations and run the complete suite five times. Rerun SkillsOfferFlowTests, SkillsOfferTimelineNativeTests, SkillsUninstallHookTests, all import checks, and service ordered-start tests after the backend fix, using fakes rather than live downloads.
- Resolve RR-17's known motion differences in source, then capture both real sidebar locations, popup anchoring/tip/glass and exact locked dimensions/strings/timings, keyboard/light dismissal/enlarge, Pause/Resume/Cancel/Retry/Close, completion/failure persistence and Reduce Motion.
- Capture Folder/ZIP native selection/cancel/filter, GitHub typing, tool Name/Command validation, source changes, checklist and parallel/retry/done states; bind the production custom backend and verify persisted Your own items and trust/command registration.
- Verify Tools page list/detail and its own Download all with terms, progress, cancellation, failure and completion; no skill switches or provider-only listing. Inspect offer-card known/unknown progress and all terminal collapses during rapid state changes.
- Native dark/light, Narrator/focus, required 100/125/150/200% DPI and Reduce Motion for all changed surfaces. Compare to the locked design; no substitute tolerances or reference updates.

Release verdict: **NOT READY**. The commits are partial implementation with explicit remaining gaps, not a release-ready declaration.
