# T36 skills UI release-gap report

2026-10-07. Branch `codex/skills-ui`, based on `origin/claude/verify-terminal-20261001` at `285b6b7a`. Audit input: `origin/codex/release-audit` at `6b2e6994`, `release-readiness.md` rows RR-17, RR-18 and RR-44. Law: CLAUDE.md, IMPLEMENTING.md, owner_decisions.md OD-0099 amendments / OD-0076, skills-ONDEMAND-PLAN.md, and the locked skills-downloads design sources. This report records implementation and remaining acceptance; it grants no design deviation.

| Row | Verdict | Implemented | Remaining release gap |
| --- | --- | --- | --- |
| RR-17 | BLOCKED | Same minimized library runs mounted in both sidebar locations; compact popup controls, completion/failure persistence and enlarge to the original page/run. | Exact locked motion/presentation remains unfinished. See ED-547; native acceptance is also outstanding. |
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


## Round 2 — lead rulings, 2026-10-07

Ruling read from `origin/claude/lead-rulings:R-gaps-round2.md`, general section and T36. READY in this section means ready for T9's Windows verification; it is not full release acceptance. The Round 1 findings above remain historical evidence.

| Row | Round 2 status | Result / remaining work |
| --- | --- | --- |
| RR-17 | DONE | Independent locked arrival opacity/movement with 120 ms delay, 450 ms delayed ring/check, compact popup opacity .22 s and movement .32 s, retained state folds with .34/.26 s curves. Outside-click/deactivation dismissal uses the same motion; interrupted close resumes from the drawn state. Compact completion/failure faces, inline progress and 26 px enlarge face completed. Both sidebar mounts and enlarge-to-original-run remain covered. Native acceptance belongs to T9. |
| RR-18 | BLOCKED | Real Check/Add is bound to T35's API, including Folder/ZIP/public GitHub repository, exact checked tokens, cancellation/discard, retry, persisted Your own rows and actual provenance, own enable/remove, plugin generations and command registration. Tools Download all remains bound to original catalogue kinds through the existing host. The locked optional GitHub subtree path is still unsupported by the service; this is a functional gap, beyond Windows proof. |
| RR-44 | DONE | Whole offer surface collapse, truthful known bytes/unknown-total spinner and omitted unknown percentage remain covered. T35's ordered start reservation is merged and exercised through the same host download service used by the UI. |

No row was ALREADY DONE at the task's branch point. No new service/core/runtime implementation was authored by T36; those files are byte-identical to the merged T35 branch `a0437bbf`. T35 API `ca504ef5`, ordered starts `ef5ac4d2` and own command/plugin composition `a0437bbf` were merged, with engineering-ledger conflicts resolved by retaining all existing UI bytes and appending only the new backend records.

RR-17 code: `SkillsProgressPill.cs`, `SkillsMotion.cs`, `SkillsCompactBody.cs`, `SkillsCompactFold.cs`; optional motion hook in `ViewLifetime.cs`, outside-dismiss routing in `PlainControls.cs`/`PopoverHost.cs`. Clock tests solve the CSS curve independently at fixed times; they do not relax the timing or compare only end states. The user's explicit approval corrects only the new Round 1 `SkillsProgressPillTests.cs:85` assertion, quoted in ED-550: zero movement at 100/120 ms, then 0.7791312484017134 at 220 ms. ED-8 corrects ED-6's expanded transcription to the literal CSS `transition-delay:.12s`. Baseline tests, references and tolerances are unchanged.

RR-18 code: `CustomAddUiBackend.cs`, `CustomOwnModel.cs`, `CustomOwnRow.cs`, `CustomOwnDetail.cs`, `CustomAddSheet.cs` and `LibraryPage.cs`. IO and registry mutations run off the UI thread; successful Add refreshes the app-lifetime own model before claiming the item is under Your own. Service snapshots and copied payloads remain separate from managed catalogue state and provider imports. Full commit/location provenance is displayed without invented versions/licences.

RR-18 blocker, with code proof: the locked sheet promises `GitHub link (owner/repo, optional path)` (`sd-page.js`, mirrored by `CustomAddSheet.cs:184`); the UI forwards the complete path. T35's `CustomSourcePaths.cs:79-95` accepts only `^[A-Za-z0-9_-]+/[A-Za-z0-9_.-]+$` and rejects `owner/repository/skills`. Its API note explicitly says branch/path URLs are unsupported. Stripping the path would add the wrong items and lose requested provenance. T35 needs subtree selection within the pinned archive, retaining the full input path/commit and exact validated tokens; T36 can then use the existing adapter unchanged. No backend-owned file was edited to bypass this boundary.

RR-44 binding: `HostLibraryBackend.cs:34` uses `SkillsFirstInstall(host.Downloads)`; `SkillsDownloads.Join` now reserves its scheduler start inside the same gate as flight creation. The imported regression delays the first observer, requests a second item, and requires actual fake start order S1 then S3.

Round 2 Linux verification (SDK 10.0.100, Debug, EnableWindowsTargeting=true; all functional tests use fakes, no live downloads/network):

- Merged solution build: zero warnings, zero errors.
- Final targeted App presentation/motion/Add/offer/formatter cases: 32/32 passed, zero skips.
- Final selected UI popup cases: 54/54 passed, zero skips, including two additive custom-dismiss/reopen regressions and unchanged default-popup/card/content-swap cases.
- Final custom-source, command/plugin composition and ordered-start Services cases: 25/25 passed, zero skips.
- Five serial final App Linux selections: 236/236 passed each, zero skips. This selection includes Port.Skills, library/import/settings/update-pill and traceability cases; it excludes only the existing SkillsOfferFlowTests, SkillsOfferTimelineNativeTests and SkillsUninstallHookTests Windows paths. It does not substitute for the complete five-run Windows suite.
- Gate B: eight passes. The complete Linux verify attempt cannot establish Gate A (missing pinned E:/Droppy reference), Gate D's default Windows build/requirement acceptance or the unimplemented remaining gates; it reports 8 FAIL / 16 PASS / 4 NOT_IMPLEMENTED. Its raw output is retained, alongside the successful explicit Linux-targeting solution build.
- Source audit: no baseline test modified; the sole modified Round 1 test is the approved arrival correction. Engineering and STATE ledger Round 1 prefixes remain byte-identical (1,795,078 and 84,714 bytes). Scope checks show Core/Services/runtime match the merged T35 branch. No force push or other branch push.

Raw red/green logs and TRX: `first-release/evidence/skills-ui/round2/`. The compact completion test first failed for the missing check; the fold test first failed for its absent type; outside dismissal first failed because native light-dismiss could close instantly; reopening first failed because the custom Open motion was not resumed. Original failures are retained. ED-549 through -11 record the Round 2 work and the approved test correction.

Windows-run needs for T9:

- Run the complete intended Windows build/test configurations and the full suite five times, including the three excluded App classes, import regressions, all custom-source/command/plugin composition and ordered-start cases, using fakes.
- Capture both approved pill locations and compact popup in dark/light and required DPI modes. Verify locked dimensions, glass/tip/anchoring, typography/strings and source curves; test outside click, owner deactivation, Escape, rapid close/reopen, enlarge to the same run/page, completion/failure persistence, Pause/Resume/Cancel/Retry/Close and Reduce Motion.
- Verify real Windows native Folder and ZIP selection/cancellation and ZIP filter; typed GitHub validation; tool Name/Command and bundle validation; candidate selection, parallel progress, retry, Done, restart, provenance, own switches/remove/original preservation and provider command/plugin use with fake providers.
- Verify optional Tools library/detail and its Download all, terms/progress/cancel/failure/completion; offer-card known/unknown progress and all terminal collapses. Check Narrator/focus/keyboard and 100/125/150/200% DPI without changing references or tolerances.
- Restore the pinned reference and run the mandatory acceptance/protected checks. Resolve unrelated release gate gaps through their owning work. Optional GitHub paths need a backend implementation before RR-18 can pass its corresponding Windows scenario.

Round 2 verdict: **NOT READY** — RR-18's optional GitHub path remains a concrete API integration gap. RR-17 and RR-44 are ready for T9's Windows verification.
